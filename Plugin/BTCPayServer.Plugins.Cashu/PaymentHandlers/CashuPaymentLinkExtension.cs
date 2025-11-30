using BTCPayServer.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;

namespace BTCPayServer.Plugins.Cashu.PaymentHandlers;

/// <summary>
/// Payment link extension for Cashu that generates payment request URIs (NUT-18 format)
/// and integrates with other payment methods in BIP21 format.
/// </summary>
public class CashuPaymentLinkExtension : IPaymentLinkExtension
{
    public PaymentMethodId PaymentMethodId { get; } = CashuPlugin.CashuPmid;

    public string GetPaymentLink(PaymentPrompt prompt, IUrlHelper? urlHelper)
    {
        // For Cashu, the destination is already a NUT-18 payment request
        // We can return it directly or wrap it in a custom URI scheme if needed
        
        // Get other payment methods if available for BIP21-style integration
        var onchain = prompt.ParentEntity.GetPaymentPrompt(PaymentTypes.CHAIN.GetPaymentMethodId("BTC"));
        var ln = prompt.ParentEntity.GetPaymentPrompt(PaymentTypes.LN.GetPaymentMethodId("BTC"));
        var lnurl = prompt.ParentEntity.GetPaymentPrompt(PaymentTypes.LNURL.GetPaymentMethodId("BTC"));

        var amount = prompt.Calculate().Due;
        
        // For Cashu, we primarily return the NUT-18 payment request
        // If other payment methods are available, we could potentially create a BIP21-style URI
        // that includes the Cashu payment request as a parameter
        
        // For now, return the Cashu payment request directly
        // The payment request is already in NUT-18 format and can be used by Cashu wallets
        if (!string.IsNullOrEmpty(prompt.Destination))
        {
            return prompt.Destination;
        }

        // Fallback: if no destination is set, return empty string
        return string.Empty;
    }
}

