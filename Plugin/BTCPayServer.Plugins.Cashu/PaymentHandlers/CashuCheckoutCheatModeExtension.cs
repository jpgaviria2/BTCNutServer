using System;
using System.Threading.Tasks;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Plugins.Cashu.PaymentHandlers;

/// <summary>
/// Cheat mode extension for Cashu to support automated testing in development mode.
/// Allows minting test tokens for invoice payment during development.
/// </summary>
public class CashuCheckoutCheatModeExtension : ICheckoutCheatModeExtension
{
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly InvoiceRepository _invoiceRepository;
    private readonly CashuPaymentService _cashuPaymentService;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;

    public CashuCheckoutCheatModeExtension(
        CashuDbContextFactory cashuDbContextFactory,
        InvoiceRepository invoiceRepository,
        CashuPaymentService cashuPaymentService,
        PaymentMethodHandlerDictionary paymentHandlers)
    {
        _cashuDbContextFactory = cashuDbContextFactory;
        _invoiceRepository = invoiceRepository;
        _cashuPaymentService = cashuPaymentService;
        _paymentHandlers = paymentHandlers;
    }

    public bool Handle(PaymentMethodId paymentMethodId) => paymentMethodId == CashuPlugin.CashuPmid;

    public Task<ICheckoutCheatModeExtension.MineBlockResult> MineBlock(
        ICheckoutCheatModeExtension.MineBlockContext mineBlockContext)
    {
        // Cashu doesn't mine blocks - it's a mint-based system
        // Return success to indicate the operation is not applicable
        return Task.FromResult(new ICheckoutCheatModeExtension.MineBlockResult());
    }

    public async Task<ICheckoutCheatModeExtension.PayInvoiceResult> PayInvoice(
        ICheckoutCheatModeExtension.PayInvoiceContext payInvoiceContext)
    {
        // For Cashu cheat mode, we would need to:
        // 1. Get a test mint URL (from configuration or environment)
        // 2. Mint test tokens for the invoice amount
        // 3. Create a Cashu token and submit it as payment
        
        // This is a placeholder implementation - in a real scenario, you would:
        // - Check if we're in development mode
        // - Get the store's Cashu configuration
        // - Use a test mint to create tokens
        // - Process the payment
        
        // For now, return a result indicating the payment was simulated
        // In a full implementation, you would actually mint tokens and process the payment
        
        var invoice = payInvoiceContext.Invoice;
        var amount = Money.Coins(payInvoiceContext.Amount);

        // Get Cashu payment method handler
        var handler = _paymentHandlers[CashuPlugin.CashuPmid] as CashuPaymentMethodHandler;
        if (handler == null)
        {
            throw new InvalidOperationException("Cashu payment method handler not found");
        }

        // Note: In a real implementation, you would:
        // 1. Get a test mint URL (e.g., from environment variable or config)
        // 2. Mint tokens using the mint API
        // 3. Create a Cashu token from the minted proofs
        // 4. Process the payment using CashuPaymentService
        
        // For now, return a simulated transaction ID
        // In production, this should actually mint and process tokens
        var simulatedTokenId = $"cheat-{Guid.NewGuid()}";
        
        return new ICheckoutCheatModeExtension.PayInvoiceResult(simulatedTokenId);
    }
}

