using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.enums;
using BTCPayServer.Plugins.Cashu.Errors;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.HostedServices;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.Payouts.Cashu;
using DotNut;
using DotNut.ApiModels;
using Microsoft.AspNetCore.Cors;
using NBitcoin;
using Newtonsoft.Json.Linq;
using InvoiceStatus = BTCPayServer.Client.Models.InvoiceStatus;
using PubKey = DotNut.PubKey;
using StoreData = BTCPayServer.Data.StoreData;
using PayoutData = BTCPayServer.Data.PayoutData;

namespace BTCPayServer.Plugins.Cashu.Controllers;

[Route("stores/{storeId}/cashu")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class CashuController: Controller
{
    public CashuController(InvoiceRepository invoiceRepository,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        CashuStatusProvider cashuStatusProvider,
        CashuPaymentService cashuPaymentService,
        CashuDbContextFactory cashuDbContextFactory,
        Payouts.Cashu.CashuAutomatedPayoutSenderFactory payoutSenderFactory,
        PayoutProcessorService payoutProcessorService,
        EventAggregator eventAggregator,
        ApplicationDbContextFactory applicationDbContextFactory,
        PayoutMethodHandlerDictionary payoutHandlers,
        BTCPayNetworkJsonSerializerSettings jsonSerializerSettings)
    {
        _invoiceRepository = invoiceRepository;
        _storeRepository = storeRepository;
        _cashuPaymentService = cashuPaymentService;
        _cashuDbContextFactory = cashuDbContextFactory;
        _cashuStatusProvider = cashuStatusProvider;
        _handlers = handlers;
        _payoutSenderFactory = payoutSenderFactory;
        _payoutProcessorService = payoutProcessorService;
        _eventAggregator = eventAggregator;
        _applicationDbContextFactory = applicationDbContextFactory;
        _payoutHandlers = payoutHandlers;
        _jsonSerializerSettings = jsonSerializerSettings;
    }
    private StoreData StoreData => HttpContext.GetStoreData();
    
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly CashuStatusProvider _cashuStatusProvider;
    private readonly CashuPaymentService _cashuPaymentService;
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly Payouts.Cashu.CashuAutomatedPayoutSenderFactory _payoutSenderFactory;
    private readonly PayoutProcessorService _payoutProcessorService;
    private readonly EventAggregator _eventAggregator;
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;

    
    /// <summary>
    /// Api route for fetching current plugin configuration for this store
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> StoreConfig()
    {

        var cashuPaymentMethodConfig =
            StoreData.GetPaymentMethodConfig<CashuPaymentMethodConfig>(CashuPlugin.CashuPmid, _handlers);

        CashuStoreViewModel model = new CashuStoreViewModel();

        if (cashuPaymentMethodConfig == null)
        {
            model.Enabled = await _cashuStatusProvider.CashuEnabled(StoreData.Id);
            model.PaymentAcceptanceModel = CashuPaymentModel.SwapAndHodl;
            model.TrustedMintsUrls = "";
            model.CustomerFeeAdvance = 0;
            // 2% of fee can be a lot, but it's usually returned. 
            model.MaxLightningFee = 2;
            //I wouldn't advise to set more than 1% of keyset fee... since they're denominated in ppk (1/1000 * unit * input_amount)in tokens unit
            model.MaxKeysetFee = 1;
        }
        else
        {
            model.Enabled = await _cashuStatusProvider.CashuEnabled(StoreData.Id);
            model.PaymentAcceptanceModel = cashuPaymentMethodConfig.PaymentModel;
            model.TrustedMintsUrls = String.Join("\n", cashuPaymentMethodConfig.TrustedMintsUrls ?? [""]);
            model.CustomerFeeAdvance = cashuPaymentMethodConfig.FeeConfing.CustomerFeeAdvance;
            model.MaxLightningFee = cashuPaymentMethodConfig.FeeConfing.MaxLightningFee;
            model.MaxKeysetFee = cashuPaymentMethodConfig.FeeConfing.MaxKeysetFee;
        }

        return View(model);
    }

    
    /// <summary>
    /// Api route for setting plugin configuration for this store
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> StoreConfig(CashuStoreViewModel viewModel)
    {
        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = CashuPlugin.CashuPmid;

        viewModel.TrustedMintsUrls ??= "";

        //trimming trailing slash 
        var parsedTrustedMintsUrls = viewModel.TrustedMintsUrls
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd('/'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var lightningEnabled = StoreData.IsLightningEnabled("BTC");

        //If lighting isn't configured - don't allow user to set meltImmediately.
        var paymentMethodConfig = new CashuPaymentMethodConfig()
        {
            PaymentModel = lightningEnabled ? viewModel.PaymentAcceptanceModel : CashuPaymentModel.SwapAndHodl,
            TrustedMintsUrls = parsedTrustedMintsUrls,
            FeeConfing = new CashuFeeConfig
            {
                CustomerFeeAdvance = viewModel.CustomerFeeAdvance,
                MaxLightningFee = viewModel.MaxLightningFee,
                MaxKeysetFee = viewModel.MaxKeysetFee,
            }
        };

        blob.SetExcluded(paymentMethodId, !viewModel.Enabled);

        StoreData.SetPaymentMethodConfig(_handlers[paymentMethodId], paymentMethodConfig);
        store.SetStoreBlob(blob);
        await _storeRepository.UpdateStore(store);
        if (viewModel.PaymentAcceptanceModel == CashuPaymentModel.MeltImmediately && !lightningEnabled)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Can't use this payment model. Lightning wallet is disabled.";
        }
        else
        {
            TempData[WellKnownTempData.SuccessMessage] = "Config Saved Successfully";
        }

        return RedirectToAction("StoreConfig", new { storeId = store.Id, paymentMethodId });
    }

    /// <summary>
    /// Configuration page for Cashu automated payout processor
    /// </summary>
    [HttpGet("payout-processors/cashu-automated")]
    public async Task<IActionResult> ConfigurePayoutProcessor(string storeId)
    {
        var activeProcessor =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new[] { _payoutSenderFactory.Processor },
                    PayoutMethods = new[]
                    {
                        PayoutMethodId.Parse(CashuPlugin.CashuPmid.ToString())
                    }
                }))
            .FirstOrDefault();

        var blob = activeProcessor?.HasTypedBlob<Payouts.Cashu.CashuAutomatedPayoutBlob>();
        return View(new ViewModels.ConfigureCashuPayoutProcessorViewModel(
            blob?.GetBlob() ?? new Payouts.Cashu.CashuAutomatedPayoutBlob()));
    }

    /// <summary>
    /// Save configuration for Cashu automated payout processor
    /// </summary>
    [HttpPost("payout-processors/cashu-automated")]
    public async Task<IActionResult> ConfigurePayoutProcessor(string storeId, ViewModels.ConfigureCashuPayoutProcessorViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var activeProcessor =
            (await _payoutProcessorService.GetProcessors(
                new PayoutProcessorService.PayoutProcessorQuery()
                {
                    Stores = new[] { storeId },
                    Processors = new[] { _payoutSenderFactory.Processor },
                    PayoutMethods = new[]
                    {
                        PayoutMethodId.Parse(CashuPlugin.CashuPmid.ToString())
                    }
                }))
            .FirstOrDefault();

        activeProcessor ??= new PayoutProcessorData();
        activeProcessor.HasTypedBlob<Payouts.Cashu.CashuAutomatedPayoutBlob>().SetBlob(model.ToBlob());
        activeProcessor.StoreId = storeId;
        activeProcessor.PayoutMethodId = CashuPlugin.CashuPmid.ToString();
        activeProcessor.Processor = _payoutSenderFactory.Processor;

        var tcs = new TaskCompletionSource();
        _eventAggregator.Publish(new PayoutProcessorUpdated()
        {
            Data = activeProcessor,
            Id = activeProcessor.Id,
            Processed = tcs
        });

        TempData[WellKnownTempData.SuccessMessage] = "Processor updated.";

        await tcs.Task;
        return RedirectToAction(nameof(ConfigurePayoutProcessor), "Cashu", new { storeId });
    }

    /// <summary>
    /// Api route for fetching current store Cashu Wallet view - All stored proofs grouped by mint and unit which can be exported.
    /// </summary>
    /// <returns></returns>
    [HttpGet("wallet")]
    public async Task<IActionResult> CashuWallet()
    {
        await using var db = _cashuDbContextFactory.CreateContext();

        var mints = await db.Mints.Select(m => m.Url).ToListAsync();
        var proofsWithUnits = new List<(string Mint, string Unit, ulong Amount)>();
        
        var unavailableMints = new List<string>();
        
        foreach (var mint in mints)
        {
            try
            { 
                var cashuHttpClient = CashuUtils.GetCashuHttpClient(mint); 
                var keysets = await cashuHttpClient.GetKeysets();

               var localProofs = await db.Proofs
                   .Where(p => keysets.Keysets.Select(k => k.Id).Contains(p.Id) &&
                               p.StoreId == StoreData.Id &&
                                 !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)
                                     )).ToListAsync();
               
                foreach (var proof in localProofs)
                {
                    var matchingKeyset = keysets.Keysets.FirstOrDefault(k => k.Id == proof.Id);
                    if (matchingKeyset != null)
                    {
                        proofsWithUnits.Add((Mint: mint, matchingKeyset.Unit, proof.Amount));
                    }
                }
            }
            catch (Exception)
            {
                unavailableMints.Add(mint);
            }
        }

        var groupedProofs = proofsWithUnits
            .GroupBy(p => new { p.Mint, p.Unit })
            .Select(group => new
                {
                 group.Key.Mint,
                 group.Key.Unit,
                 Amount = group.Select(x => x.Amount).Sum()
                }
            )
            .OrderByDescending(x => x.Amount)
            .Select(x => (x.Mint, x.Unit, x.Amount))
            .ToList();
        
        var exportedTokens = db.ExportedTokens.Where(et=>et.StoreId == StoreData.Id).ToList();
        if (unavailableMints.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Couldn't load {unavailableMints.Count} mints: {String.Join(", ", unavailableMints)}";
        }
        var viewModel = new CashuWalletViewModel {AvaibleBalances = groupedProofs, ExportedTokens = exportedTokens};
        
        return View(viewModel);
    }
    
    /// <summary>
    /// Api route for exporting stored balance for chosen mint and unit
    /// </summary>
    /// <param name="mintUrl">Chosen mint url, form which proofs we want to export</param>
    /// <param name="unit">Chosen unit of token</param>
    [HttpPost("ExportMintBalance")]
    public async Task<IActionResult> ExportMintBalance(string mintUrl, string unit)
    {
        if (string.IsNullOrWhiteSpace(mintUrl)|| string.IsNullOrWhiteSpace(unit))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid mint or unit provided!";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id});
        }
        
        await using var db = _cashuDbContextFactory.CreateContext();
        List<GetKeysetsResponse.KeysetItemResponse> keysets;
        try
        {
            var cashuWallet = new CashuWallet(mintUrl, unit);
            keysets = await cashuWallet.GetKeysets();
            if (keysets == null || keysets.Count == 0)
            {
                throw new Exception("No keysets were found.");
            }
        }
        catch (Exception)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Couldn't get keysets!";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id});
        }
 
        var selectedProofs = db.Proofs.Where(p=>
            p.StoreId == StoreData.Id 
            && keysets.Select(k => k.Id).Contains(p.Id) 
            //ensure that proof is free and spendable (yeah!)
            && !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p))
            ).ToList();
    
        var createdToken = new CashuToken()
        {
            Tokens =
            [
                new CashuToken.Token
                {
                    Mint = mintUrl,
                    Proofs = selectedProofs.Select(p => p.ToDotNutProof()).ToList(),
                }
            ],
            Memo = "Cashu Token withdrawn from BTCNutServer",
            Unit = unit
        };
        
        var tokenAmount = selectedProofs.Select(p => p.Amount).Sum();
        var serializedToken = createdToken.Encode();
    
        var proofsToRemove = await db.Proofs
            .Where(p => p.StoreId == StoreData.Id && 
                        keysets.Select(k => k.Id).Contains(p.Id))
            .ToListAsync();
        
        var exportedTokenEntity = new ExportedToken
        {
            SerializedToken = serializedToken,
            Amount = tokenAmount,
            Unit = unit,
            Mint = mintUrl,
            StoreId = StoreData.Id,
            IsUsed = false,
        };
        var strategy = db.Database.CreateExecutionStrategy();
        
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.Proofs.RemoveRange(proofsToRemove);
                db.ExportedTokens.Add(exportedTokenEntity);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                ViewData[WellKnownTempData.ErrorMessage] = $"Couldn't export";
                RedirectToAction(nameof(CashuWallet), new { storeId = StoreData.Id });
            }
        });
        return RedirectToAction(nameof(ExportedToken), new { tokenId = exportedTokenEntity.Id });
    }

    /// <summary>
    /// Api route for fetching exported token data
    /// </summary>
    /// <param name="tokenId">Stored Token GUID</param>
    [HttpGet("/Token")]
    public async Task<IActionResult> ExportedToken(Guid tokenId)
    {
        
        var db = _cashuDbContextFactory.CreateContext();
       
        var exportedToken = db.ExportedTokens.SingleOrDefault(e => e.Id == tokenId);
        if (exportedToken == null)
        {
            return BadRequest("Can't find token with provided GUID");
        }

        if (!exportedToken.IsUsed)
        { 
            try
            {
                var wallet = new CashuWallet(exportedToken.Mint, exportedToken.Unit);
                var proofs = CashuTokenHelper.Decode(exportedToken.SerializedToken, out _)
                    .Tokens.SelectMany(t => t.Proofs)
                    .Distinct()
                    .ToList();
                var state = await wallet.CheckTokenState(proofs);
                if (state == StateResponseItem.TokenState.SPENT)
                {
                    exportedToken.IsUsed = true;
                    db.ExportedTokens.Update(exportedToken);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                //honestly, there's nothing to do. maybe it'll work next time
            }
        }
        
        var model = new ExportedTokenViewModel()
        {
            Amount = exportedToken.Amount,
            Unit = exportedToken.Unit,
            MintAddress = exportedToken.Mint,
            Token = exportedToken.SerializedToken,
        };
        
        return View(model);
    }

    /// <summary>
    /// Display pull payment claim QR code for a Cashu payout
    /// </summary>
    /// <param name="payoutId">Payout ID</param>
    [HttpGet("pull-payment-claim/{payoutId}")]
    public async Task<IActionResult> PullPaymentClaim(string payoutId)
    {
        if (string.IsNullOrEmpty(payoutId))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Payout ID is required";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id });
        }

        await using var ctx = _applicationDbContextFactory.CreateContext();
        
        // Get the payout
        var payout = await ctx.Payouts.GetPayout(payoutId, StoreData.Id, includePullPayment: true, includeStore: true);
        
        if (payout == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Payout not found";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id });
        }

        // Verify it's a Cashu payout
        if (!PayoutMethodId.TryParse(payout.PayoutMethodId, out var payoutMethodId) ||
            payoutMethodId.ToString() != CashuPlugin.CashuPmid.ToString())
        {
            TempData[WellKnownTempData.ErrorMessage] = "This payout is not a Cashu payout";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id });
        }

        // Get the payout handler to parse the proof
        var payoutHandler = _payoutHandlers.TryGet(payoutMethodId);
        if (payoutHandler is not CashuPayoutHandler cashuPayoutHandler)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Cashu payout handler not found";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id });
        }

        // Parse the proof to get the token
        var proof = cashuPayoutHandler.ParseProof(payout) as CashuPayoutBlob;
        
        if (proof == null || string.IsNullOrEmpty(proof.Token))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Token not yet generated for this payout. Please wait a moment and try again.";
            return RedirectToAction("CashuWallet", new { storeId = StoreData.Id });
        }

        var model = new ViewModels.PullPaymentClaimViewModel()
        {
            Token = proof.Token,
            Amount = proof.Amount,
            Unit = "sat",
            MintAddress = proof.Mint,
            PayoutId = payout.Id,
            StoreId = StoreData.Id
        };
        
        return View(model);
    }

    /// <summary>
    /// Api route for fetching failed transactions list
    /// </summary>
    /// <returns></returns>
    [HttpGet("FailedTransactions")]
    public async Task<IActionResult> FailedTransactions()
    {
        await using var db = _cashuDbContextFactory.CreateContext();
        //fetch recently failed transactions 
        var failedTransactions = db.FailedTransactions
            .Where(ft => ft.StoreId == StoreData.Id)
            .Include(ft=>ft.UsedProofs)
            .ToList();
            
        return View(failedTransactions);
    }
    
    /// <summary>
    /// Api route for checking failed transaction state.
    /// </summary>
    /// <param name="failedTransactionId"></param>
    /// <returns></returns>
     [HttpPost("FailedTransactions")]
     public async Task<IActionResult> PostFailedTransaction(Guid failedTransactionId)
     {
         await using var db = _cashuDbContextFactory.CreateContext();
         var failedTransaction = db.FailedTransactions
             .Include(failedTransaction => failedTransaction.UsedProofs)
             .SingleOrDefault(t => t.Id == failedTransactionId);
         
         if (failedTransaction == null)
         {
             TempData[WellKnownTempData.ErrorMessage] = "Can't get failed transaction with provided GUID!";
             return RedirectToAction(nameof(FailedTransactions),  new { storeId = StoreData.Id});
         }
         
         if (failedTransaction.StoreId != StoreData.Id)
         {
             TempData[WellKnownTempData.ErrorMessage] ="Chosen failed transaction doesn't belong to this store!";
             return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
         }

         var handler = _handlers[CashuPlugin.CashuPmid];
         
         if (handler is not CashuPaymentMethodHandler cashuHandler)
         {
             TempData[WellKnownTempData.ErrorMessage] = "Couldn't obtain CashuPaymentHandler";
             return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
         }
         
         var invoice = await _invoiceRepository.GetInvoice(failedTransaction.InvoiceId);
             
         if (invoice is null)
         {
             TempData[WellKnownTempData.ErrorMessage] = "Couldn't find invoice with provided GUID in this store.";
             return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
         }

         CashuPaymentService.PollResult pollResult;

         try
         {
             if (failedTransaction.OperationType == OperationType.Melt)
             {
                 pollResult = await _cashuPaymentService.PollFailedMelt(failedTransaction, StoreData, cashuHandler);
             }
             else
             {
                 pollResult = await _cashuPaymentService.PollFailedSwap(failedTransaction, StoreData);
             }
         }
         catch (Exception ex)
         {
             TempData[WellKnownTempData.ErrorMessage] = "Couldn't poll failed transaction: " + ex.Message;
             return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});

         }
         
         if (pollResult == null)
         {
             TempData[WellKnownTempData.ErrorMessage] = "Polling failed. Received no response.";
             return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
         }

         if (!pollResult.Success)
         {
             TempData[WellKnownTempData.ErrorMessage] = $"Transaction state: {pollResult.State}. {(pollResult.Error == null ? "" : pollResult.Error.Message)}";
             return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
         }
         
         await _cashuPaymentService.AddProofsToDb(pollResult.ResultProofs, StoreData.Id, failedTransaction.MintUrl);
         decimal singleUnitPrice;
         try
         {
             singleUnitPrice = await CashuUtils.GetTokenSatRate(failedTransaction.MintUrl, failedTransaction.Unit,
                 cashuHandler.Network.NBitcoinNetwork);

         }
         catch (Exception)
         {
             TempData[WellKnownTempData.ErrorMessage] = $"Couldn't fetch token/satoshi rate";
             return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
         }
         
         var summedProofs = failedTransaction.UsedProofs.Select(p=>p.Amount).Sum();
         await _cashuPaymentService.RegisterCashuPayment(invoice, cashuHandler, Money.Satoshis(summedProofs*singleUnitPrice));
         db.FailedTransactions.Remove(failedTransaction);
         await db.SaveChangesAsync();
         TempData[WellKnownTempData.SuccessMessage] = $"Transaction retrieved successfully. Marked as paid.";
         return RedirectToAction("FailedTransactions", new { storeId = StoreData.Id});
     }
    
    
    /// <summary>
    /// Api endpoint for Paying Invoice via Post Request, by sending all the data exclusively.
    /// </summary>
    /// <param name="token">V4 encoded Cashu Token</param>
    /// <param name="invoiceId"></param>
    /// <returns></returns>
    /// <exception cref="CashuPaymentException"></exception>
    [EnableCors(CorsPolicies.All)]
    [AllowAnonymous]
    [HttpPost("~/cashu/PayInvoice")]
    public async Task<IActionResult> PayByToken(string token, string invoiceId)
    {
        try
        {
            if (!CashuUtils.TryDecodeToken(token, out var decodedToken))
            {
                throw new CashuPaymentException("Invalid token");
            }
            await _cashuPaymentService.ProcessPaymentAsync(decodedToken, invoiceId);
        }
        catch (CashuPaymentException cex)
        {
            return BadRequest($"Payment Error: {cex.Message} ");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        return Redirect(Url.ActionAbsolute(this.Request, nameof(UIInvoiceController.Checkout), "UIInvoice", new { invoiceId = invoiceId }).AbsoluteUri);
    }
    
    /// <summary>
    /// Api endpoint for Paying Invoice via Post Request, by sending nut19 payment payload.
    /// </summary>
    /// <param name="paymentPayload"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    [EnableCors(CorsPolicies.All)]
    [AllowAnonymous]
    [HttpPost("~/cashu/PayInvoicePr")]
    public async Task<ActionResult> PayByPaymentRequest([FromBody] JObject payload)
    {
        try
        {
            if (payload["mint"] == null || payload["id"] == null || payload["unit"] == null || payload["proofs"] == null)
            {
                throw new ArgumentException("Required fields are missing in the payload.");
            }

            //it's a workaround - it should be deserialized by JSONSerializer 
            var parsedPayload = new PaymentRequestPayload
            {
                Mint = payload["mint"].Value<string>(),
                PaymentId = payload["id"].Value<string>(),
                Memo = payload["memo"]?.Value<string>(),
                Unit = payload["unit"].Value<string>(),
                Proofs = payload["proofs"].Value<JArray>().Select(p => new Proof
                {
                    Amount = p["amount"]!.Value<ulong>(),
                    Id = new KeysetId(p["id"]!.Value<string>()),
                    Secret = new StringSecret(p["secret"]!.Value<string>()), 
                    C = new PubKey(p["C"]!.Value<string>()), 
                }).ToArray()
            };
            // var parsedPayload = JsonSerializer.Deserialize<PaymentRequestPayload>(paymentPayload);
            //   "id": str <optional>, will correspond to invoiceId
            //   "memo": str <optional>, idc about this
            //   "mint": str, //if trusted mint - save to db, if not - melt 🔥
            //   "unit": <str_enum>, should always be in sat, since there aren't any standardisation for unit denomination
            //   "proofs": Array<Proof>  yeah proofs

            var token = new CashuToken
            {
                Tokens =
                [
                    new CashuToken.Token
                    {
                        Mint = parsedPayload.Mint,
                        Proofs = parsedPayload.Proofs.ToList()
                    }
                ],
                Memo = parsedPayload.Memo,
                Unit = parsedPayload.Unit
            };

            await _cashuPaymentService.ProcessPaymentAsync(token, parsedPayload.PaymentId);
            return Ok("Payment sent!");
        }
        catch (CashuPaymentException cex)
        {
            return BadRequest($"Payment Error: {cex.Message}");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}

  



