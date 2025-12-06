using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.Errors;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using DotNut;
using DotNut.Api;
using DotNut.ApiModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Services;

public class CashuWalletService
{
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly LightningClientFactoryService _lightningClientFactoryService;
    private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;
    private readonly Logs _logs;
    private readonly StoreRepository _storeRepository;

    public CashuWalletService(
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        LightningClientFactoryService lightningClientFactoryService,
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        CashuDbContextFactory cashuDbContextFactory,
        Logs logs)
    {
        _storeRepository = storeRepository;
        _handlers = handlers;
        _lightningClientFactoryService = lightningClientFactoryService;
        _lightningNetworkOptions = lightningNetworkOptions;
        _cashuDbContextFactory = cashuDbContextFactory;
        _logs = logs;
    }

    /// <summary>
    /// Receive ecash tokens via swap operation
    /// </summary>
    public async Task<ReceiveEcashResult> ReceiveEcashAsync(
        string storeId,
        string mintUrl,
        string unit,
        List<Proof> proofsToReceive,
        ulong inputFee = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallet = new CashuWallet(mintUrl, unit, _cashuDbContextFactory);
            var swapResult = await wallet.Receive(proofsToReceive, inputFee);

            if (!swapResult.Success)
            {
                return new ReceiveEcashResult
                {
                    Success = false,
                    Error = swapResult.Error
                };
            }

            await AddProofsToDb(swapResult.ResultProofs!, storeId, mintUrl);

            return new ReceiveEcashResult
            {
                Success = true,
                ReceivedProofs = swapResult.ResultProofs
            };
        }
        catch (Exception ex)
        {
            _logs.PayServer.LogError(ex, "(Cashu Wallet) Error receiving ecash for store {StoreId}", storeId);
            return new ReceiveEcashResult
            {
                Success = false,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Receive lightning payment by creating mint quote, paying invoice, and receiving tokens
    /// NOTE: This implementation is incomplete - the DotNut API structure for mint quotes with BOLT11 invoices
    /// needs to be verified. The typical flow is: create quote -> pay invoice -> mint with outputs -> get signatures.
    /// </summary>
    public async Task<ReceiveLightningResult> ReceiveLightningAsync(
        string storeId,
        string mintUrl,
        string unit,
        string bolt11Invoice,
        BTCPayNetwork network,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement based on actual DotNut API structure for mint quotes with BOLT11 invoices
        // The current implementation is a placeholder and needs to be completed based on the actual API
        return new ReceiveLightningResult
        {
            Success = false,
            Error = new NotImplementedException("ReceiveLightningAsync is not yet fully implemented. The DotNut API structure for mint quotes with BOLT11 invoices needs to be verified and implemented.")
        };
    }
    
    // Original implementation commented out - needs DotNut API verification
    /*
    private async Task<ReceiveLightningResult> ReceiveLightningAsync_Original(
        string storeId,
        string mintUrl,
        string unit,
        string bolt11Invoice,
        BTCPayNetwork network,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var wallet = new CashuWallet(mintUrl, unit, _cashuDbContextFactory);
            var cashuHttpClient = CashuUtils.GetCashuHttpClient(mintUrl);

            // Step 1: Parse the BOLT11 invoice to get amount
            if (!BOLT11PaymentRequest.TryParse(bolt11Invoice, out var parsedInvoice, network.NBitcoinNetwork))
            {
                throw new CashuPluginException("Invalid BOLT11 invoice");
            }

            var invoiceAmount = parsedInvoice.MinimumAmount.ToUnit(LightMoneyUnit.Satoshi);

            // Step 2: Get token sat rate to determine how many tokens we'll receive
            var singleUnitPrice = await CashuUtils.GetTokenSatRate(mintUrl, unit, network.NBitcoinNetwork);
            var tokenAmount = (ulong)Math.Floor((decimal)invoiceAmount / singleUnitPrice);

            if (tokenAmount == 0)
            {
                throw new CashuPluginException("Invoice amount too small to receive tokens");
            }

            // Step 3: Get active keyset and create outputs for the tokens we'll receive
            var activeKeyset = await wallet.GetActiveKeyset();
            var keys = await wallet.GetKeys(activeKeyset.Id);
            if (keys == null)
            {
                throw new CashuPluginException("Could not get keys for keyset");
            }

            // Create outputs for the token amount
            var outputAmounts = CashuUtils.SplitToProofsAmounts(tokenAmount, keys);
            var outputs = CashuUtils.CreateOutputs(outputAmounts, activeKeyset.Id, keys);

            // Step 4: Create mint quote with BOLT11 invoice
            // NOTE: The PostMintQuoteBolt11Request structure needs to be verified
            // Based on CashuUtils, it uses Amount, but for receiving we need to provide the invoice
            // This is a placeholder - needs to be updated based on actual DotNut API
            // For now, using Amount as a workaround - the actual API may have a different structure
            var mintQuoteRequest = new PostMintQuoteBolt11Request
            {
                Amount = tokenAmount, // Using token amount as placeholder
                Unit = unit
            };

            var mintQuote = await cashuHttpClient.CreateMintQuote<PostMintQuoteBolt11Response, PostMintQuoteBolt11Request>(
                "bolt11",
                mintQuoteRequest);

            if (string.IsNullOrEmpty(mintQuote.Quote))
            {
                throw new CashuPluginException("Mint quote creation failed");
            }

            // Step 5: Get store's lightning client and pay the invoice
            var storeData = await _storeRepository.FindStore(storeId);
            if (storeData == null)
            {
                throw new CashuPluginException("Store not found");
            }

            var lightningClient = GetStoreLightningClient(storeData, network);
            if (lightningClient == null)
            {
                throw new CashuPluginException("Lightning client not configured");
            }

            // Pay the invoice
            var payResult = await lightningClient.Pay(bolt11Invoice, null, cancellationToken);
            if (payResult?.Result != PayResult.Ok)
            {
                throw new CashuPluginException($"Failed to pay invoice: {payResult?.ErrorDetail ?? "Unknown error"}");
            }

            // Step 6: Poll mint quote until paid
            var maxAttempts = 30;
            var delayMs = 2000;
            PostMintQuoteBolt11Response? mintQuoteState = null;

            for (int i = 0; i < maxAttempts; i++)
            {
                await Task.Delay(delayMs, cancellationToken);
                
                mintQuoteState = await cashuHttpClient.CheckMintQuote<PostMintQuoteBolt11Response>(
                    "bolt11",
                    mintQuote.Quote,
                    cancellationToken);

                if (mintQuoteState.State == "PAID")
                {
                    break;
                }

                if (mintQuoteState.State == "UNPAID" && mintQuoteState.Expiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                {
                    throw new CashuPluginException("Mint quote expired");
                }
            }

            if (mintQuoteState?.State != "PAID")
            {
                throw new CashuPluginException("Mint quote not paid within timeout");
            }

            // Step 7: After quote is paid, we need to call the mint endpoint with outputs to get signatures
            // The mint quote state when paid may not contain signatures - we need to mint with outputs
            // This is a simplified implementation - the actual DotNut API may require different approach
            // For now, we'll throw an error indicating this needs to be implemented based on actual API
            throw new NotImplementedException("Mint endpoint call after quote payment needs to be implemented based on DotNut API. The mint should accept quote ID and outputs, then return signatures.");
        }
        catch (Exception ex)
        {
            _logs.PayServer.LogError(ex, "(Cashu Wallet) Error receiving lightning for store {StoreId}", storeId);
            return new ReceiveLightningResult
            {
                Success = false,
                Error = ex
            };
        }
    }
    */

    /// <summary>
    /// Send ecash tokens by selecting proofs and creating a token
    /// </summary>
    public async Task<SendEcashResult> SendEcashAsync(
        string storeId,
        string mintUrl,
        string unit,
        ulong amount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = _cashuDbContextFactory.CreateContext();
            
            // Get available proofs for this mint and unit
            var wallet = new CashuWallet(mintUrl, unit, _cashuDbContextFactory);
            var keysets = await wallet.GetKeysets();
            
            var availableProofs = await db.Proofs
                .Where(p => p.StoreId == storeId &&
                           keysets.Select(k => k.Id).Contains(p.Id) &&
                           !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
                .ToListAsync(cancellationToken);

            if (!availableProofs.Any())
            {
                return new SendEcashResult
                {
                    Success = false,
                    Error = new CashuPluginException("No available proofs to send")
                };
            }

            var dotNutProofs = availableProofs.Select(p => p.ToDotNutProof()).ToList();
            
            // Select proofs to send
            var sendResponse = CashuUtils.SelectProofsToSend(dotNutProofs, amount);
            
            if (!sendResponse.Send.Any() || sendResponse.Send.Select(p => p.Amount).Sum() < amount)
            {
                return new SendEcashResult
                {
                    Success = false,
                    Error = new CashuPluginException("Insufficient balance")
                };
            }

            // Create token from selected proofs
            var token = new CashuToken
            {
                Tokens = new List<CashuToken.Token>
                {
                    new CashuToken.Token
                    {
                        Mint = mintUrl,
                        Proofs = sendResponse.Send
                    }
                },
                Memo = $"Cashu token sent from BTCPay Server wallet",
                Unit = unit
            };

            var serializedToken = token.Encode();

            // Remove sent proofs from database
            var proofsToRemove = availableProofs
                .Where(p => sendResponse.Send.Any(sp => 
                    sp.Secret.GetBytes().SequenceEqual(p.Secret.GetBytes())))
                .ToList();

            db.Proofs.RemoveRange(proofsToRemove);
            await db.SaveChangesAsync(cancellationToken);

            return new SendEcashResult
            {
                Success = true,
                Token = serializedToken,
                Amount = sendResponse.Send.Select(p => p.Amount).Sum()
            };
        }
        catch (Exception ex)
        {
            _logs.PayServer.LogError(ex, "(Cashu Wallet) Error sending ecash for store {StoreId}", storeId);
            return new SendEcashResult
            {
                Success = false,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Send lightning payment via melt operation
    /// </summary>
    public async Task<SendLightningResult> SendLightningAsync(
        string storeId,
        string mintUrl,
        string unit,
        string bolt11Invoice,
        BTCPayNetwork network,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get store data and lightning client
            var storeData = await _storeRepository.FindStore(storeId);
            if (storeData == null)
            {
                throw new CashuPluginException("Store not found");
            }

            var lightningClient = GetStoreLightningClient(storeData, network);
            if (lightningClient == null)
            {
                throw new CashuPluginException("Lightning client not configured");
            }

            // Parse invoice to get amount
            if (!BOLT11PaymentRequest.TryParse(bolt11Invoice, out var parsedInvoice, network.NBitcoinNetwork))
            {
                throw new CashuPluginException("Invalid BOLT11 invoice");
            }

            var invoiceAmount = parsedInvoice.MinimumAmount.ToUnit(LightMoneyUnit.Satoshi);
            
            // Get token sat rate
            var singleUnitPrice = await CashuUtils.GetTokenSatRate(mintUrl, unit, network.NBitcoinNetwork);
            var tokenAmountNeeded = (ulong)Math.Ceiling((decimal)invoiceAmount / singleUnitPrice);

            // Get available proofs
            await using var db = _cashuDbContextFactory.CreateContext();
            var wallet = new CashuWallet(lightningClient, mintUrl, unit, _cashuDbContextFactory);
            var keysets = await wallet.GetKeysets();

            var availableProofs = await db.Proofs
                .Where(p => p.StoreId == storeId &&
                           keysets.Select(k => k.Id).Contains(p.Id) &&
                           !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
                .ToListAsync(cancellationToken);

            if (!availableProofs.Any())
            {
                return new SendLightningResult
                {
                    Success = false,
                    Error = new CashuPluginException("No available proofs")
                };
            }

            var dotNutProofs = availableProofs.Select(p => p.ToDotNutProof()).ToList();

            // Create a simplified token for melt quote
            var token = new CashuUtils.SimplifiedCashuToken
            {
                Mint = mintUrl,
                Proofs = dotNutProofs,
                Unit = unit
            };

            // Create melt quote
            var meltQuoteResult = await wallet.CreateMeltQuote(token, singleUnitPrice, keysets);
            if (!meltQuoteResult.Success)
            {
                return new SendLightningResult
                {
                    Success = false,
                    Error = meltQuoteResult.Error ?? new CashuPluginException("Failed to create melt quote")
                };
            }

            // Select proofs to cover the melt amount + fees
            var feeReserve = (ulong)meltQuoteResult.MeltQuote!.FeeReserve;
            var keysetFee = meltQuoteResult.KeysetFee ?? 0UL;
            var totalNeeded = tokenAmountNeeded + (ulong)feeReserve + keysetFee;
            var sendResponse = CashuUtils.SelectProofsToSend(dotNutProofs, totalNeeded);

            if (!sendResponse.Send.Any() || sendResponse.Send.Select(p => p.Amount).Sum() < totalNeeded)
            {
                return new SendLightningResult
                {
                    Success = false,
                    Error = new CashuPluginException("Insufficient balance for melt operation")
                };
            }

            // Execute melt
            var meltResult = await wallet.Melt(meltQuoteResult.MeltQuote, sendResponse.Send, cancellationToken);

            if (!meltResult.Success)
            {
                return new SendLightningResult
                {
                    Success = false,
                    Error = meltResult.Error ?? new CashuPluginException("Melt operation failed")
                };
            }

            // Verify lightning invoice is paid
            var invoicePaid = await wallet.ValidateLightningInvoicePaid(meltQuoteResult.Invoice?.Id);
            if (!invoicePaid)
            {
                return new SendLightningResult
                {
                    Success = false,
                    Error = new CashuPluginException("Lightning invoice not paid")
                };
            }

            // Store change proofs and remove used proofs
            if (meltResult.ChangeProofs != null && meltResult.ChangeProofs.Length > 0)
            {
                await AddProofsToDb(meltResult.ChangeProofs, storeId, mintUrl);
            }

            var proofsToRemove = availableProofs
                .Where(p => sendResponse.Send.Any(sp => 
                    sp.Secret.GetBytes().SequenceEqual(p.Secret.GetBytes())))
                .ToList();

            db.Proofs.RemoveRange(proofsToRemove);
            await db.SaveChangesAsync(cancellationToken);

            return new SendLightningResult
            {
                Success = true,
                AmountSent = invoiceAmount,
                ChangeReceived = meltResult.ChangeProofs?.Select(p => p.Amount).Sum() ?? 0
            };
        }
        catch (Exception ex)
        {
            _logs.PayServer.LogError(ex, "(Cashu Wallet) Error sending lightning for store {StoreId}", storeId);
            return new SendLightningResult
            {
                Success = false,
                Error = ex
            };
        }
    }

    /// <summary>
    /// Get wallet balance grouped by mint and unit
    /// </summary>
    public async Task<List<(string Mint, string Unit, ulong Amount)>> GetBalanceAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        
        var mints = await db.Mints.Select(m => m.Url).ToListAsync(cancellationToken);
        var balances = new List<(string Mint, string Unit, ulong Amount)>();

        foreach (var mint in mints)
        {
            try
            {
                var cashuHttpClient = CashuUtils.GetCashuHttpClient(mint);
                var keysets = await cashuHttpClient.GetKeysets();

                var localProofs = await db.Proofs
                    .Where(p => keysets.Keysets.Select(k => k.Id).Contains(p.Id) &&
                               p.StoreId == storeId &&
                               !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
                    .ToListAsync(cancellationToken);

                foreach (var proof in localProofs)
                {
                    var matchingKeyset = keysets.Keysets.FirstOrDefault(k => k.Id == proof.Id);
                    if (matchingKeyset != null)
                    {
                        balances.Add((Mint: mint, matchingKeyset.Unit, proof.Amount));
                    }
                }
            }
            catch (Exception ex)
            {
                _logs.PayServer.LogWarning(ex, "(Cashu Wallet) Could not load balance for mint {Mint}", mint);
            }
        }

        return balances
            .GroupBy(b => new { b.Mint, b.Unit })
            .Select(g => (g.Key.Mint, g.Key.Unit, Amount: g.Select(x => (ulong)x.Amount).Aggregate(0UL, (a, b) => a + b)))
            .OrderByDescending(x => x.Amount)
            .ToList();
    }

    private ILightningClient? GetStoreLightningClient(StoreData store, BTCPayNetwork network)
    {
        var lightningPmi = PaymentTypes.LN.GetPaymentMethodId(network.CryptoCode);
        var lightningConfig = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(
            lightningPmi,
            _handlers);

        if (lightningConfig == null)
            return null;

        return lightningConfig.CreateLightningClient(
            network,
            _lightningNetworkOptions.Value,
            _lightningClientFactoryService);
    }

    private async Task AddProofsToDb(IEnumerable<Proof>? proofs, string storeId, string mintUrl)
    {
        if (proofs == null)
        {
            return;
        }

        var enumerable = proofs as Proof[] ?? proofs.ToArray();

        if (enumerable.Length == 0)
        {
            return;
        }

        await using var dbContext = _cashuDbContextFactory.CreateContext();

        if (!dbContext.Mints.Any(m => m.Url == mintUrl))
        {
            dbContext.Mints.Add(new Mint(mintUrl));
        }

        var dbProofs = StoredProof.FromBatch(enumerable, storeId);
        dbContext.Proofs.AddRange(dbProofs);

        await dbContext.SaveChangesAsync();
    }
}

// Result classes
public class ReceiveEcashResult
{
    public bool Success { get; set; }
    public Proof[]? ReceivedProofs { get; set; }
    public Exception? Error { get; set; }
}

public class ReceiveLightningResult
{
    public bool Success { get; set; }
    public Proof[]? ReceivedProofs { get; set; }
    public Exception? Error { get; set; }
}

public class SendEcashResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public ulong Amount { get; set; }
    public Exception? Error { get; set; }
}

public class SendLightningResult
{
    public bool Success { get; set; }
    public decimal AmountSent { get; set; }
    public ulong ChangeReceived { get; set; }
    public Exception? Error { get; set; }
}

