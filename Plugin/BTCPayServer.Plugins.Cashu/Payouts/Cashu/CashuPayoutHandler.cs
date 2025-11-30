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
    private readonly EventAggregator _eventAggregator;
    private readonly NotificationSender _notificationSender;
    private readonly ILogger<CashuPayoutHandler> _logger;

    public AsyncKeyedLocker<string> PayoutLocker = new AsyncKeyedLocker<string>();

    public CashuPayoutHandler(
        PaymentMethodHandlerDictionary paymentHandlers,
        BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
        ApplicationDbContextFactory dbContextFactory,
        EventAggregator eventAggregator,
        NotificationSender notificationSender,
        ILogger<CashuPayoutHandler> logger)
    {
        _paymentHandlers = paymentHandlers;
        _jsonSerializerSettings = jsonSerializerSettings;
        _dbContextFactory = dbContextFactory;
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

    public Task TrackClaim(ClaimRequest claimRequest, PayoutData payoutData)
    {
        // No tracking needed for Cashu tokens
        return Task.CompletedTask;
    }

    public Task<(IClaimDestination destination, string error)> ParseClaimDestination(string destination, CancellationToken cancellationToken)
    {
        destination = destination?.Trim();
        if (string.IsNullOrEmpty(destination))
        {
            return Task.FromResult<(IClaimDestination, string)>((null!, "Destination cannot be empty"));
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
        // For now, accept all Cashu destinations
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
        // Subscribe to Cashu token state change events
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

