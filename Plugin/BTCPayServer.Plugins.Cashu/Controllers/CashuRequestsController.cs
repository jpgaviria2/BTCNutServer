using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.Data.Models;
using BTCPayServer.Plugins.Cashu.ViewModels;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Cashu.Controllers;

[Route("stores/{storeId}/cashu/requests")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class CashuRequestsController : Controller
{
    // Temporary hard-disable for request flow
    private const bool RequestsDisabled = true;
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;

    public CashuRequestsController(
        CashuDbContextFactory cashuDbContextFactory,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary paymentHandlers)
    {
        _cashuDbContextFactory = cashuDbContextFactory;
        _storeRepository = storeRepository;
        _paymentHandlers = paymentHandlers;
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        if (RequestsDisabled)
            return NotFound("Cashu requests are temporarily disabled.");
        return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/CreateRequest.cshtml",
            new CashuRequestCreateViewModel());
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string storeId, CashuRequestCreateViewModel vm)
    {
        if (!ModelState.IsValid)
            return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/CreateRequest.cshtml", vm);

        if (RequestsDisabled)
            return NotFound("Cashu requests are temporarily disabled.");

        var expiresAt = vm.ExpiryMinutes is > 0
            ? DateTimeOffset.UtcNow.AddMinutes(vm.ExpiryMinutes.Value)
            : (DateTimeOffset?)null;

        await using var ctx = _cashuDbContextFactory.CreateContext();

        var req = new CashuRequest
        {
            StoreId = storeId,
            AmountSats = vm.AmountSats,
            Memo = vm.Memo,
            ExpiresAt = expiresAt,
            Status = CashuRequestStatus.Pending
        };

        ctx.CashuRequests.Add(req);
        await ctx.SaveChangesAsync();

        var requestUrl = Url.ActionLink(nameof(RequestPublic),
            "CashuRequests",
            new { requestId = req.Id },
            Request.Scheme,
            Request.Host.ToUriComponent());

        var linkVm = new CashuRequestLinkViewModel
        {
            RequestId = req.Id,
            StoreId = req.StoreId,
            AmountSats = req.AmountSats,
            Memo = req.Memo,
            CreatedAt = req.CreatedAt,
            ExpiresAt = req.ExpiresAt,
            RequestUrl = requestUrl ?? string.Empty
        };

        return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/RequestLinkCreated.cshtml", linkVm);
    }

    [AllowAnonymous]
    [Route("/cashu/request/{requestId:guid}")]
    [HttpGet]
    public async Task<IActionResult> RequestPublic(Guid requestId)
    {
        if (RequestsDisabled)
            return NotFound("Cashu requests are temporarily disabled.");
        await using var ctx = _cashuDbContextFactory.CreateContext();
        var req = await ctx.CashuRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null)
            return NotFound("Request not found");

        var now = DateTimeOffset.UtcNow;
        if (req.IsExpired(now))
        {
            req.Status = CashuRequestStatus.Expired;
            await ctx.SaveChangesAsync();
            return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/RequestPublic.cshtml",
                BuildVm(req, isExpired: true, error: "Request expired"));
        }

        if (req.Status == CashuRequestStatus.Received && !string.IsNullOrEmpty(req.ReceivedToken))
        {
            return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/RequestPublic.cshtml",
                BuildVm(req, isReceived: true));
        }

        return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/RequestPublic.cshtml",
            BuildVm(req));
    }

    [AllowAnonymous]
    [Route("/cashu/request/{requestId:guid}")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestPublic(Guid requestId, string tokenText)
    {
        await using var ctx = _cashuDbContextFactory.CreateContext();
        var req = await ctx.CashuRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (req == null)
            return NotFound("Request not found");

        var now = DateTimeOffset.UtcNow;
        if (req.IsExpired(now))
        {
            req.Status = CashuRequestStatus.Expired;
            await ctx.SaveChangesAsync();
            ModelState.AddModelError(string.Empty, "Request expired");
            return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/RequestPublic.cshtml",
                BuildVm(req, isExpired: true, error: "Request expired"));
        }

        tokenText = tokenText?.Trim();
        if (string.IsNullOrEmpty(tokenText))
        {
            ModelState.AddModelError(nameof(tokenText), "Token is required");
            return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/RequestPublic.cshtml",
                BuildVm(req));
        }

        if (!CashuUtils.TryDecodeToken(tokenText, out var token))
        {
            ModelState.AddModelError(nameof(tokenText), "Invalid Cashu token format");
            return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/RequestPublic.cshtml",
                BuildVm(req, error: "Invalid Cashu token"));
        }

        // Basic amount check (warning only)
        ulong? providedAmount = token?.Tokens?
            .Where(t => t?.Proofs != null)
            .SelectMany(t => (IEnumerable<DotNut.Proof>)(t!.Proofs ?? Enumerable.Empty<DotNut.Proof>()))
            .Select(p => (ulong)p.Amount)
            .Aggregate(0UL, (acc, x) => acc + x);
        string? warning = null;
        if (req.AmountSats.HasValue && providedAmount.HasValue && providedAmount.Value != req.AmountSats.Value)
        {
            warning = $"Token amount {providedAmount.Value} sats differs from requested {req.AmountSats.Value} sats.";
        }

        req.ReceivedToken = tokenText;
        req.ReceivedMint = token?.Tokens?.FirstOrDefault()?.Mint;
        req.ReceivedAt = DateTimeOffset.UtcNow;
        req.Status = CashuRequestStatus.Received;
        req.Error = warning;
        await ctx.SaveChangesAsync();

        return View("/Plugins/BTCPayServer.Plugins.Cashu/Views/Cashu/RequestPublic.cshtml",
            BuildVm(req, isReceived: true, error: warning));
    }

    private CashuRequestPublicViewModel BuildVm(CashuRequest req, bool isExpired = false, bool isReceived = false, string? error = null)
    {
        return new CashuRequestPublicViewModel
        {
            RequestId = req.Id,
            AmountSats = req.AmountSats,
            Memo = req.Memo,
            IsExpired = isExpired || req.Status == CashuRequestStatus.Expired,
            IsReceived = isReceived || req.Status == CashuRequestStatus.Received,
            ReceivedAt = req.ReceivedAt,
            ExpiresAt = req.ExpiresAt,
            Error = error ?? req.Error,
            ReceivedToken = req.ReceivedToken,
            ReceivedMint = req.ReceivedMint
        };
    }
}

