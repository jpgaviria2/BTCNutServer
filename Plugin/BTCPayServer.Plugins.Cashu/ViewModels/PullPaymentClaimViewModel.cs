using BTCPayServer.Plugins.Cashu.CashuAbstractions;

namespace BTCPayServer.Plugins.Cashu.ViewModels;

public class PullPaymentClaimViewModel
{
    public string Token { get; set; } = string.Empty;
    public ulong Amount { get; set; }
    public string Unit { get; set; } = "sat";
    public string MintAddress { get; set; } = string.Empty;
    public string PayoutId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    
    public string FormatedAmount
    {
        get
        {
            var result = CashuUtils.FormatAmount(this.Amount, this.Unit);
            return $"{result.Amount} {result.Unit}";
        }
    }
}

