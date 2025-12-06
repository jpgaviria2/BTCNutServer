using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Cashu;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Plugins.Cashu.ViewModels;
using BTCPayServer.Payouts;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using DotNut;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Cashu.Controllers;

[Route("stores/{storeId}/cashu/claims")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class CashuClaimsController : Controller
{
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly StoreRepository _storeRepository;

    public CashuClaimsController(
        CashuDbContextFactory cashuDbContextFactory,
        ApplicationDbContextFactory applicationDbContextFactory,
        PaymentMethodHandlerDictionary paymentHandlers,
        StoreRepository storeRepository)
    {
        _cashuDbContextFactory = cashuDbContextFactory;
        _applicationDbContextFactory = applicationDbContextFactory;
        _paymentHandlers = paymentHandlers;
        _storeRepository = storeRepository;
    }

    private StoreData StoreData => HttpContext.GetStoreData();

    [HttpGet("create")]
    public IActionResult Create()
    {
        return View("CreateClaim", new CashuClaimCreateViewModel());
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CashuClaimCreateViewModel vm)
    {
        if (!ModelState.IsValid)
            return View("CreateClaim", vm);

        var expiresAt = vm.ExpiryMinutes is > 0
            ? DateTimeOffset.UtcNow.AddMinutes(vm.ExpiryMinutes.Value)
            : (DateTimeOffset?)null;

        await using var cashuCtx = _cashuDbContextFactory.CreateContext();

        var claim = new CashuClaim
        {
            StoreId = StoreData.Id,
            AmountSats = vm.AmountSats,
            ExpiresAt = expiresAt,
            Status = CashuClaimStatus.Pending
        };

        cashuCtx.CashuClaims.Add(claim);
        await cashuCtx.SaveChangesAsync();

        var claimUrl = Url.ActionLink(nameof(Claim),
            "CashuClaims",
            new { claimId = claim.Id },
            Request.Scheme,
            Request.Host.ToUriComponent());

        var linkVm = new CashuClaimLinkViewModel
        {
            ClaimId = claim.Id,
            StoreId = claim.StoreId,
            AmountSats = claim.AmountSats,
            CreatedAt = claim.CreatedAt,
            ExpiresAt = claim.ExpiresAt,
            ClaimUrl = claimUrl ?? string.Empty
        };

        return View("ClaimLinkCreated", linkVm);
    }

    [AllowAnonymous]
    [Route("/cashu/claim/{claimId:guid}")]
    public async Task<IActionResult> Claim(Guid claimId)
    {
        await using var cashuCtx = _cashuDbContextFactory.CreateContext();
        var claim = await cashuCtx.CashuClaims.FirstOrDefaultAsync(c => c.Id == claimId);
        if (claim == null)
        {
            return NotFound("Claim not found");
        }

        var now = DateTimeOffset.UtcNow;
        if (claim.IsExpired(now))
        {
            claim.Status = CashuClaimStatus.Expired;
            await cashuCtx.SaveChangesAsync();
            return View("ClaimPublic",
                new CashuClaimPublicViewModel
                {
                    ClaimId = claim.Id,
                    AmountSats = claim.AmountSats,
                    IsExpired = true,
                    Token = null,
                    Mint = claim.Mint,
                    Error = "Claim expired"
                });
        }

        if (claim.Status == CashuClaimStatus.Claimed && !string.IsNullOrEmpty(claim.Token))
        {
            return View("ClaimPublic",
                new CashuClaimPublicViewModel
                {
                    ClaimId = claim.Id,
                    AmountSats = claim.AmountSats,
                    Token = claim.Token,
                    Mint = claim.Mint,
                    IsClaimed = true,
                    ClaimedAt = claim.ClaimedAt
                });
        }

        // Try to generate the token on-demand
        var (success, token, mint, error) = await GenerateTokenForClaim(claim);
        if (!success)
        {
            claim.Status = CashuClaimStatus.Failed;
            claim.Error = error;
            await cashuCtx.SaveChangesAsync();
            return View("ClaimPublic",
                new CashuClaimPublicViewModel
                {
                    ClaimId = claim.Id,
                    AmountSats = claim.AmountSats,
                    Token = null,
                    Mint = mint,
                    Error = error,
                    IsClaimed = false
                });
        }

        claim.Token = token;
        claim.Mint = mint;
        claim.ClaimedAt = DateTimeOffset.UtcNow;
        claim.Status = CashuClaimStatus.Claimed;
        await cashuCtx.SaveChangesAsync();

        return View("ClaimPublic",
            new CashuClaimPublicViewModel
            {
                ClaimId = claim.Id,
                AmountSats = claim.AmountSats,
                Token = token,
                Mint = mint,
                IsClaimed = true,
                ClaimedAt = claim.ClaimedAt
            });
    }

    private async Task<(bool success, string? token, string? mint, string? error)> GenerateTokenForClaim(CashuClaim claim)
    {
        await using var ctx = _applicationDbContextFactory.CreateContext();
        var store = await ctx.Stores.FindAsync(claim.StoreId);
        if (store == null)
        {
            return (false, null, null, $"Store {claim.StoreId} not found");
        }

        var config = store.GetPaymentMethodConfig<CashuPaymentMethodConfig>(
            CashuPlugin.CashuPmid, _paymentHandlers);

        if (config == null || config.TrustedMintsUrls == null || config.TrustedMintsUrls.Count == 0)
        {
            return (false, null, null, $"Cashu not configured for store {claim.StoreId}");
        }

        var mintUrl = config.TrustedMintsUrls.First();
        var wallet = new CashuWallet(mintUrl, "sat", _cashuDbContextFactory);

        await using var cashuCtx = _cashuDbContextFactory.CreateContext();

        var storedProofs = await cashuCtx.Proofs
            .Where(p => p.StoreId == claim.StoreId)
            .Where(p => !cashuCtx.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
            .OrderByDescending(p => p.Amount)
            .ToListAsync();

        // Balance check
        ulong availableAmount = 0;
        foreach (var proof in storedProofs)
        {
            availableAmount += (ulong)proof.Amount;
        }

        if (availableAmount < claim.AmountSats)
        {
            return (false, null, mintUrl,
                $"Insufficient Cashu balance. Available: {availableAmount} sats, Required: {claim.AmountSats} sats");
        }

        // Select proofs
        var proofsToUse = new System.Collections.Generic.List<DotNut.Proof>();
        var storedProofsToUse = new System.Collections.Generic.List<StoredProof>();
        ulong selectedAmount = 0;

        foreach (var storedProof in storedProofs)
        {
            if (selectedAmount >= claim.AmountSats)
                break;

            proofsToUse.Add(storedProof.ToDotNutProof());
            storedProofsToUse.Add(storedProof);
            selectedAmount += (ulong)storedProof.Amount;
        }

        // Keyset and split
        var keysets = await wallet.GetKeysets();
        var activeKeyset = await wallet.GetActiveKeyset();
        var keys = await wallet.GetKeys(activeKeyset.Id);

        if (keys == null)
        {
            return (false, null, mintUrl, $"Could not get keys for keyset {activeKeyset.Id}");
        }

        var outputAmounts = CashuUtils.SplitToProofsAmounts(claim.AmountSats, keys);

        // Swap
        var swapResult = await wallet.Swap(proofsToUse, outputAmounts, activeKeyset.Id, keys);

        if (!swapResult.Success || swapResult.ResultProofs == null)
        {
            return (false, null, mintUrl,
                $"Failed to swap Cashu proofs: {swapResult.Error?.Message ?? "Unknown error"}");
        }

        var payoutProofs = swapResult.ResultProofs.Take(outputAmounts.Count).ToList();
        var createdToken = new CashuToken()
        {
            Tokens =
            [
                new CashuToken.Token
                {
                    Mint = mintUrl,
                    Proofs = payoutProofs
                }
            ],
            Unit = "sat"
        };
        var serializedToken = createdToken.Encode();

        // Remove used proofs
        var proofsToRemove = cashuCtx.Proofs
            .Where(p => storedProofsToUse.Select(sp => sp.ProofId).Contains(p.ProofId));
        cashuCtx.Proofs.RemoveRange(proofsToRemove);

        // Store change if any
        if (swapResult.ResultProofs.Length > outputAmounts.Count)
        {
            var changeProofs = swapResult.ResultProofs.Skip(outputAmounts.Count).ToList();
            var storedChangeProofs = changeProofs.Select(p =>
                new StoredProof(p, claim.StoreId));
            await cashuCtx.Proofs.AddRangeAsync(storedChangeProofs);
        }

        await cashuCtx.SaveChangesAsync();

        return (true, serializedToken, mintUrl, null);
    }
}

