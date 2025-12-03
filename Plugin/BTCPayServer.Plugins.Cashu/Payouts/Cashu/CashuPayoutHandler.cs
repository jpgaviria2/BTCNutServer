#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.Payouts.Cashu.Events;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DotNut;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Payouts.Cashu;

/// <summary>
/// Payout handler for Cashu payouts.
/// Handles parsing of Cashu token destinations and manages payout proofs.
/// Enhanced with background monitoring and payout-specific actions.
/// </summary>
public class CashuPayoutHandler : IPayoutHandler
{
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly EventAggregator _eventAggregator;
    private readonly NotificationSender _notificationSender;
    private readonly ILogger<CashuPayoutHandler> _logger;

    public AsyncKeyedLocker<string> PayoutLocker = new AsyncKeyedLocker<string>();

    public CashuPayoutHandler(
        PaymentMethodHandlerDictionary paymentHandlers,
        BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
        ApplicationDbContextFactory dbContextFactory,
        CashuDbContextFactory cashuDbContextFactory,
        EventAggregator eventAggregator,
        NotificationSender notificationSender,
        ILogger<CashuPayoutHandler> logger)
    {
        _paymentHandlers = paymentHandlers;
        _jsonSerializerSettings = jsonSerializerSettings;
        _dbContextFactory = dbContextFactory;
        _cashuDbContextFactory = cashuDbContextFactory;
        _eventAggregator = eventAggregator;
        _notificationSender = notificationSender;
        _logger = logger;
        PayoutMethodId = PayoutMethodId.Parse(CashuPlugin.CashuPmid.ToString());
        PaymentMethodId = CashuPlugin.CashuPmid;
        Currency = "BTC"; // Cashu is BTC-denominated
    }

    public PayoutMethodId PayoutMethodId { get; }
    public PaymentMethodId PaymentMethodId { get; }
    public string Currency { get; }

    public bool IsSupported(StoreData storeData)
    {
        // Check if Cashu payment method is configured for the store
        var config = storeData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid, _paymentHandlers);
        return config != null && config.TrustedMintsUrls != null && config.TrustedMintsUrls.Count > 0;
    }

    public async Task TrackClaim(ClaimRequest claimRequest, PayoutData payoutData)
    {
        // Only generate token for direct payouts (not pull payment claims)
        // Pull payment claims are for receiving - user provides their token
        // Direct payouts are for sending - we generate token with QR code
        if (payoutData.PullPaymentDataId != null)
        {
            // This is a pull payment claim (receiving) - no token generation needed
            return;
        }

        // This is a direct payout (sending) - generate token immediately
        await GenerateCashuTokenForPayoutAtCreation(payoutData, claimRequest);
    }

    public Task<(IClaimDestination destination, string error)> ParseClaimDestination(string destination, CancellationToken cancellationToken)
    {
        destination = destination?.Trim();
        if (string.IsNullOrEmpty(destination))
        {
            // Allow empty for payouts - validation will check context in ValidateClaimDestination
            return Task.FromResult<(IClaimDestination, string)>(
                (new CashuQRCodeClaimDestination(), null!));
        }

        // For Cashu, destination can be:
        // 1. A Cashu token string (for receiving tokens)
        // 2. An email address (to send token to)
        // 3. A phone number (to send token via SMS)
        
        // Try parsing as Cashu token first
        if (CashuUtils.TryDecodeToken(destination, out var token))
        {
            // Valid Cashu token - create a claim destination
            return Task.FromResult<(IClaimDestination, string)>(
                (new CashuTokenClaimDestination(destination), null!));
        }

        // Try parsing as email
        if (destination.Contains("@") && destination.Contains("."))
        {
            return Task.FromResult<(IClaimDestination, string)>(
                (new CashuEmailClaimDestination(destination), null!));
        }

        // Default: treat as a generic Cashu destination (could be phone number or other identifier)
        return Task.FromResult<(IClaimDestination, string)>(
            (new CashuGenericClaimDestination(destination), null!));
    }

    public (bool valid, string? error) ValidateClaimDestination(IClaimDestination claimDestination, PullPaymentBlob? pullPaymentBlob)
    {
        // Allow QR code claim destination for direct payouts only (when pullPaymentBlob is null)
        // For pull payment claims (receiving), destination is required
        if (claimDestination is CashuQRCodeClaimDestination)
        {
            if (pullPaymentBlob == null)
            {
                // This is a direct payout (sending) - QR code claim is allowed
                return (true, null);
            }
            // This is a pull payment claim (receiving) - destination is required
            return (false, "Destination is required for pull payment claims");
        }
        
        // For other destination types, accept them
        return (true, null);
    }

    public IPayoutProof ParseProof(PayoutData payout)
    {
        if (payout?.Proof == null)
            return null!;

        try
        {
            var proof = JObject.Parse(payout.Proof);
            var proofType = proof["ProofType"]?.ToString();

            if (proofType == CashuPayoutBlob.CashuPayoutBlobProofType)
            {
                return proof.ToObject<CashuPayoutBlob>(
                    JsonSerializer.Create(_jsonSerializerSettings.GetSerializer(PayoutMethodId)))!;
            }
        }
        catch
        {
            // Invalid proof format
        }

        return null!;
    }

    public void StartBackgroundCheck(Action<Type[]> subscribe)
    {
        // Subscribe to Cashu token state change events (for pull payment claims tracking)
        // No longer subscribe to approval events - token generation happens at creation time
        subscribe([typeof(CashuTokenStateUpdated)]);
    }

    public async Task BackgroundCheck(object o)
    {
        if (o is CashuTokenStateUpdated tokenEvent)
        {
            // Check if this token matches any awaiting payouts
            await using var ctx = _dbContextFactory.CreateContext();
            var payouts = await ctx.Payouts
                .Include(p => p.StoreData)
                .Include(p => p.PullPaymentData)
                .Where(p => p.State == PayoutState.AwaitingPayment)
                .Where(p => p.PayoutMethodId == PayoutMethodId.ToString())
                .ToListAsync();

            foreach (var payout in payouts)
            {
                if (PayoutLocker.LockOrNullAsync(payout.Id, 0) is var locker && await locker is { } disposable)
                {
                    using (disposable)
                    {
                        // Check if the payout destination matches the token
                        var blob = payout.GetBlob(_jsonSerializerSettings);
                        var claim = await ParseClaimDestination(blob.Destination, CancellationToken.None);
                        
                        // For Cashu token destinations, check if the token matches
                        if (claim.destination is CashuTokenClaimDestination tokenDest && 
                            tokenDest.Id == tokenEvent.Token)
                        {
                            // Verify amount matches
                            if (payout.Amount != null && 
                                Money.Satoshis((long)tokenEvent.Amount).ToDecimal(MoneyUnit.BTC) == payout.Amount.Value)
                            {
                                // Token state updated - create proof
                                var proof = new CashuPayoutBlob
                                {
                                    Token = tokenEvent.Token,
                                    Mint = tokenEvent.Mint,
                                    Amount = tokenEvent.Amount,
                                    DetectedInBackground = true,
                                    ConfirmedAt = tokenEvent.Timestamp,
                                    TransactionId = tokenEvent.TransactionId
                                };
                                
                                SetProofBlob(payout, proof);
                                
                                await _notificationSender.SendNotification(
                                    new StoreScope(payout.StoreDataId),
                                    new ExternalPayoutTransactionNotification()
                                    {
                                        PaymentMethod = payout.PayoutMethodId,
                                        PayoutId = payout.Id,
                                        StoreId = payout.StoreDataId
                                    });
                                
                                await ctx.SaveChangesAsync();
                                _eventAggregator.Publish(new PayoutEvent(PayoutEvent.PayoutEventType.Updated, payout));
                                
                                _logger.LogInformation(
                                    "Cashu payout {PayoutId} confirmed via background check. Token: {Token}",
                                    payout.Id,
                                    tokenEvent.Token);
                            }
                        }
                    }
                }
            }
        }
        // Token generation now happens at creation time (TrackClaim), not on approval
        // Removed approval event handler
    }

    public Task<decimal> GetMinimumPayoutAmount(IClaimDestination claimDestination)
    {
        // Minimum payout amount in BTC (1 satoshi)
        return Task.FromResult(0.00000001m);
    }

    public Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions()
    {
        return new Dictionary<PayoutState, List<(string Action, string Text)>>()
        {
            {
                PayoutState.AwaitingPayment, new List<(string Action, string Text)>()
                {
                    ("reject-payment", "Reject payout transaction"),
                    ("mark-paid", "Mark payout as paid")
                }
            }
        };
    }

    public async Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId)
    {
        switch (action)
        {
            case "mark-paid":
                await using (var context = _dbContextFactory.CreateContext())
                {
                    var payouts = (await PullPaymentHostedService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
                    {
                        States = [PayoutState.AwaitingPayment],
                        Stores = [storeId],
                        PayoutIds = payoutIds
                    }, context)).Where(data =>
                        PayoutMethodId.TryParse(data.PayoutMethodId, out var payoutMethodId) &&
                        payoutMethodId == PayoutMethodId)
                    .Select(data => (data, ParseProof(data) as CashuPayoutBlob))
                    .Where(tuple => tuple.Item2 is { DetectedInBackground: false });
                    
                    foreach (var (payout, _) in payouts)
                    {
                        payout.State = PayoutState.Completed;
                    }

                    await context.SaveChangesAsync();
                }

                return new StatusMessageModel
                {
                    Message = "Payout payments have been marked as confirmed",
                    Severity = StatusMessageModel.StatusSeverity.Success
                };
                
            case "reject-payment":
                await using (var context = _dbContextFactory.CreateContext())
                {
                    var payouts = (await PullPaymentHostedService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
                    {
                        States = [PayoutState.AwaitingPayment],
                        Stores = [storeId],
                        PayoutIds = payoutIds
                    }, context)).Where(data =>
                        PayoutMethodId.TryParse(data.PayoutMethodId, out var payoutMethodId) &&
                        payoutMethodId == PayoutMethodId)
                    .Select(data => (data, ParseProof(data) as CashuPayoutBlob))
                    .Where(tuple => tuple.Item2 is { DetectedInBackground: true });
                    
                    foreach (var (payout, _) in payouts)
                    {
                        SetProofBlob(payout, null);
                    }

                    await context.SaveChangesAsync();
                }

                return new StatusMessageModel()
                {
                    Message = "Payout payments have been unmarked",
                    Severity = StatusMessageModel.StatusSeverity.Success
                };
        }

        return new StatusMessageModel
        {
            Message = "Action not supported",
            Severity = StatusMessageModel.StatusSeverity.Error
        };
    }

    public Task<IActionResult> InitiatePayment(string[] payoutIds)
    {
        // For Cashu, manual payment initiation would trigger the automated payout processor
        // Return null to use default behavior (automated processor handles it)
        return Task.FromResult<IActionResult>(null!);
    }
    
    /// <summary>
    /// Generates a Cashu token for a payout at creation time (called from TrackClaim)
    /// Throws exception if insufficient balance to fail payout creation
    /// </summary>
    private async Task GenerateCashuTokenForPayoutAtCreation(PayoutData payout, ClaimRequest claimRequest)
    {
        try
        {
            // Load store data if not already loaded
            await using var ctx = _dbContextFactory.CreateContext();
            var storeData = payout.StoreData;
            if (storeData == null)
            {
                storeData = await ctx.Stores.FindAsync(payout.StoreDataId);
                if (storeData == null)
                {
                    throw new InvalidOperationException($"Store {payout.StoreDataId} not found for payout {payout.Id}");
                }
            }

            var config = storeData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
                CashuPlugin.CashuPmid, _paymentHandlers);
            
            if (config == null || config.TrustedMintsUrls == null || config.TrustedMintsUrls.Count == 0)
            {
                throw new InvalidOperationException($"Cashu not configured for store {payout.StoreDataId}");
            }

            // Use the first trusted mint
            var mintUrl = config.TrustedMintsUrls.First();
            var wallet = new CashuWallet(mintUrl, "sat", _cashuDbContextFactory);

            await using var cashuCtx = _cashuDbContextFactory.CreateContext();
            
            // At creation time, use OriginalAmount (Amount is set during approval)
            var amountSatoshis = (ulong)Money.Coins(payout.OriginalAmount).Satoshi;

            // Get stored proofs from the store's Cashu wallet
            var storedProofs = await cashuCtx.Proofs
                .Where(p => p.StoreId == payout.StoreDataId)
                .Where(p => !cashuCtx.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
                .OrderByDescending(p => p.Amount)
                .ToListAsync();

            // Check balance - THROW EXCEPTION if insufficient (fail payout creation)
            ulong availableAmount = 0;
            foreach (var proof in storedProofs)
            {
                availableAmount += (ulong)proof.Amount;
            }
            
            if (availableAmount < amountSatoshis)
            {
                throw new InvalidOperationException(
                    $"Insufficient Cashu balance for payout. Available: {availableAmount} sats, Required: {amountSatoshis} sats");
            }

            // Select proofs to use for the swap
            var proofsToUse = new List<Proof>();
            var storedProofsToUse = new List<StoredProof>();
            ulong selectedAmount = 0;
            
            foreach (var storedProof in storedProofs)
            {
                if (selectedAmount >= amountSatoshis)
                    break;

                proofsToUse.Add(storedProof.ToDotNutProof());
                storedProofsToUse.Add(storedProof);
                selectedAmount += (ulong)storedProof.Amount;
            }

            // Get keyset and split amount
            var keysets = await wallet.GetKeysets();
            var activeKeyset = await wallet.GetActiveKeyset();
            var keys = await wallet.GetKeys(activeKeyset.Id);

            if (keys == null)
            {
                throw new InvalidOperationException($"Could not get keys for keyset {activeKeyset.Id}");
            }

            var outputAmounts = CashuUtils.SplitToProofsAmounts(amountSatoshis, keys);

            // Perform swap to create new proofs
            var swapResult = await wallet.Swap(proofsToUse, outputAmounts, activeKeyset.Id, keys);

            if (!swapResult.Success || swapResult.ResultProofs == null)
            {
                throw new InvalidOperationException(
                    $"Failed to swap Cashu proofs: {swapResult.Error?.Message ?? "Unknown error"}");
            }

            // Create ecash token from the new proofs (only the proofs for the payout amount, not change)
            var payoutProofs = swapResult.ResultProofs.Take(outputAmounts.Count).ToList();
            var createdToken = new CashuToken()
            {
                Tokens =
                [
                    new CashuToken.Token
                    {
                        Mint = mintUrl,
                        Proofs = payoutProofs,
                    }
                ],
                Memo = $"Cashu payout {payout.Id}",
                Unit = "sat"
            };
            var serializedToken = createdToken.Encode();

            // Remove used proofs from database
            var proofIdsToRemove = storedProofsToUse.Select(p => p.ProofId).ToList();
            var proofsToRemove = await cashuCtx.Proofs
                .Where(p => proofIdsToRemove.Contains(p.ProofId))
                .ToListAsync();
            
            cashuCtx.Proofs.RemoveRange(proofsToRemove);

            // Store new proofs (change if any) in database
            if (swapResult.ResultProofs.Length > outputAmounts.Count)
            {
                // There's change - store it
                var changeProofs = swapResult.ResultProofs.Skip(outputAmounts.Count).ToList();
                var storedChangeProofs = changeProofs.Select(p => 
                    new StoredProof(p, payout.StoreDataId));
                await cashuCtx.Proofs.AddRangeAsync(storedChangeProofs);
            }

            await cashuCtx.SaveChangesAsync();

            // Store the token as proof in payout immediately
            var proofBlob = new CashuPayoutBlob
            {
                Token = serializedToken,
                Mint = mintUrl,
                Amount = amountSatoshis,
                StoreId = payout.StoreDataId,
                PayoutId = payout.Id,
                DetectedInBackground = false
            };
            
            SetProofBlob(payout, proofBlob);
            
            _logger.LogInformation(
                "Successfully generated Cashu token for payout {PayoutId} with amount {Amount} sats at creation time",
                payout.Id,
                amountSatoshis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating Cashu token for payout {PayoutId} at creation time", payout.Id);
            // Re-throw to fail payout creation
            throw;
        }
    }

    /// <summary>
    /// Generates a Cashu token for a pull payment payout by swapping existing proofs
    /// This method is kept for backward compatibility but should no longer be used
    /// </summary>
    private async Task GenerateCashuTokenForPayout(PayoutData payout)
    {
        if (PayoutLocker.LockOrNullAsync(payout.Id, 0) is var locker && await locker is { } disposable)
        {
            using (disposable)
            {
                try
                {
                    await using var ctx = _dbContextFactory.CreateContext();
                    // Reload payout to ensure we have the latest state
                    var reloadedPayout = await ctx.Payouts
                        .Include(p => p.StoreData)
                        .FirstOrDefaultAsync(p => p.Id == payout.Id);
                    
                    if (reloadedPayout == null || reloadedPayout.State != PayoutState.AwaitingPayment)
                    {
                        _logger.LogWarning("Payout {PayoutId} is not in AwaitingPayment state, skipping token generation", payout.Id);
                        return;
                    }

                    // Check if token already generated
                    var existingProof = ParseProof(reloadedPayout);
                    if (existingProof is CashuPayoutBlob existingBlob && !string.IsNullOrEmpty(existingBlob.Token))
                    {
                        _logger.LogInformation("Payout {PayoutId} already has a token generated", payout.Id);
                        return;
                    }

                    // Get store configuration
                    var storeData = reloadedPayout.StoreData;
                    var config = storeData?.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
                        CashuPlugin.CashuPmid, _paymentHandlers);
                    
                    if (config == null || config.TrustedMintsUrls == null || config.TrustedMintsUrls.Count == 0)
                    {
                        _logger.LogWarning("Cashu not configured for store {StoreId}, cannot generate token for payout {PayoutId}", 
                            reloadedPayout.StoreDataId, payout.Id);
                        return;
                    }

                    // Use the first trusted mint
                    var mintUrl = config.TrustedMintsUrls.First();
                    var wallet = new CashuWallet(mintUrl, "sat", _cashuDbContextFactory);

                    await using var cashuCtx = _cashuDbContextFactory.CreateContext();
                    var amountSatoshis = (ulong)Money.Coins(reloadedPayout.Amount.Value).Satoshi;

                    // Get stored proofs from the store's Cashu wallet
                    var storedProofs = await cashuCtx.Proofs
                        .Where(p => p.StoreId == reloadedPayout.StoreDataId)
                        .Where(p => !cashuCtx.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
                        .OrderByDescending(p => p.Amount)
                        .ToListAsync();

                    // Check balance
                    ulong availableAmount = 0;
                    foreach (var proof in storedProofs)
                    {
                        availableAmount += (ulong)proof.Amount;
                    }
                    
                    if (availableAmount < amountSatoshis)
                    {
                        _logger.LogWarning(
                            "Insufficient Cashu proofs for payout {PayoutId}. Available: {Available}, Required: {Required}",
                            reloadedPayout.Id,
                            availableAmount,
                            amountSatoshis);
                        return;
                    }

                    // Select proofs to use for the swap
                    var proofsToUse = new List<Proof>();
                    var storedProofsToUse = new List<StoredProof>();
                    ulong selectedAmount = 0;
                    
                    foreach (var storedProof in storedProofs)
                    {
                        if (selectedAmount >= amountSatoshis)
                            break;

                        proofsToUse.Add(storedProof.ToDotNutProof());
                        storedProofsToUse.Add(storedProof);
                        selectedAmount += (ulong)storedProof.Amount;
                    }

                    // Get keyset and split amount
                    var keysets = await wallet.GetKeysets();
                    var activeKeyset = await wallet.GetActiveKeyset();
                    var keys = await wallet.GetKeys(activeKeyset.Id);

                    if (keys == null)
                    {
                        _logger.LogError("Could not get keys for keyset {KeysetId} for payout {PayoutId}", 
                            activeKeyset.Id, reloadedPayout.Id);
                        return;
                    }

                    var outputAmounts = CashuUtils.SplitToProofsAmounts(amountSatoshis, keys);

                    // Perform swap to create new proofs
                    var swapResult = await wallet.Swap(proofsToUse, outputAmounts, activeKeyset.Id, keys);

                    if (!swapResult.Success || swapResult.ResultProofs == null)
                    {
                        _logger.LogError(
                            "Failed to swap Cashu proofs for payout {PayoutId}. Error: {Error}",
                            reloadedPayout.Id,
                            swapResult.Error?.Message ?? "Unknown error");
                        return;
                    }

                    // Create ecash token from the new proofs (only the proofs for the payout amount, not change)
                    var payoutProofs = swapResult.ResultProofs.Take(outputAmounts.Count).ToList();
                    var createdToken = new CashuToken()
                    {
                        Tokens =
                        [
                            new CashuToken.Token
                            {
                                Mint = mintUrl,
                                Proofs = payoutProofs,
                            }
                        ],
                        Memo = $"Cashu pull payment payout {reloadedPayout.Id}",
                        Unit = "sat"
                    };
                    var serializedToken = createdToken.Encode();

                    // Remove used proofs from database
                    var proofIdsToRemove = storedProofsToUse.Select(p => p.ProofId).ToList();
                    var proofsToRemove = await cashuCtx.Proofs
                        .Where(p => proofIdsToRemove.Contains(p.ProofId))
                        .ToListAsync();
                    
                    cashuCtx.Proofs.RemoveRange(proofsToRemove);

                    // Store new proofs (change if any) in database
                    if (swapResult.ResultProofs.Length > outputAmounts.Count)
                    {
                        // There's change - store it
                        var changeProofs = swapResult.ResultProofs.Skip(outputAmounts.Count).ToList();
                        var storedChangeProofs = changeProofs.Select(p => 
                            new StoredProof(p, reloadedPayout.StoreDataId));
                        await cashuCtx.Proofs.AddRangeAsync(storedChangeProofs);
                    }

                    await cashuCtx.SaveChangesAsync();

                    // Store the token as proof in payout
                    var proofBlob = new CashuPayoutBlob
                    {
                        Token = serializedToken,
                        Mint = mintUrl,
                        Amount = amountSatoshis,
                        StoreId = reloadedPayout.StoreDataId,
                        PayoutId = reloadedPayout.Id,
                        DetectedInBackground = false
                    };
                    
                    SetProofBlob(reloadedPayout, proofBlob);
                    await ctx.SaveChangesAsync();
                    
                    _logger.LogInformation(
                        "Successfully generated Cashu token for payout {PayoutId} with amount {Amount} sats",
                        reloadedPayout.Id,
                        amountSatoshis);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating Cashu token for payout {PayoutId}", payout.Id);
                }
            }
        }
    }
    
    /// <summary>
    /// Sets the proof blob on a payout data object
    /// </summary>
    public void SetProofBlob(PayoutData data, CashuPayoutBlob? blob)
    {
        if (blob == null)
        {
            data.Proof = null;
        }
        else
        {
            var serializer = JsonSerializer.Create(_jsonSerializerSettings.GetSerializer(PayoutMethodId));
            data.Proof = JObject.FromObject(blob, serializer).ToString();
        }
    }
}

// Claim destination classes for Cashu
public class CashuTokenClaimDestination : IClaimDestination
{
    public CashuTokenClaimDestination(string token)
    {
        Id = token;
    }

    public string? Id { get; }
    public decimal? Amount { get; set; }
}

public class CashuEmailClaimDestination : IClaimDestination
{
    public CashuEmailClaimDestination(string email)
    {
        Id = email;
    }

    public string? Id { get; }
    public decimal? Amount { get; set; }
}

public class CashuGenericClaimDestination : IClaimDestination
{
    public CashuGenericClaimDestination(string identifier)
    {
        Id = identifier;
    }

    public string? Id { get; }
    public decimal? Amount { get; set; }
}

public class CashuQRCodeClaimDestination : IClaimDestination
{
    public CashuQRCodeClaimDestination()
    {
        Id = "QR Code Claim";
    }

    public string? Id { get; }
    public decimal? Amount { get; set; }
}

