using System;
using BTCPayServer.Data;
using System.Text.Json.Serialization;

namespace BTCPayServer.Plugins.Cashu.Payouts.Cashu;

/// <summary>
/// Payout proof for Cashu payouts.
/// Enhanced with background detection and transaction tracking.
/// </summary>
public class CashuPayoutBlob : IPayoutProof
{
    public const string CashuPayoutBlobProofType = "CashuPayoutBlob";
    public string ProofType { get; } = CashuPayoutBlobProofType;
    
    public string Token { get; set; } = string.Empty;
    public string Mint { get; set; } = string.Empty;
    public ulong Amount { get; set; }
    
    /// <summary>
    /// Store ID for generating QR code link for pull payments
    /// </summary>
    public string? StoreId { get; set; }
    
    /// <summary>
    /// Payout ID for generating QR code link for pull payments
    /// </summary>
    public string? PayoutId { get; set; }
    
    /// <summary>
    /// Indicates whether this proof was detected in the background (e.g., via mint state monitoring)
    /// </summary>
    public bool DetectedInBackground { get; set; } = false;
    
    /// <summary>
    /// Timestamp when the token was sent/confirmed
    /// </summary>
    public DateTimeOffset? ConfirmedAt { get; set; }
    
    /// <summary>
    /// Transaction ID or mint operation ID if available
    /// </summary>
    public string? TransactionId { get; set; }
    
    [JsonIgnore]
    public string Id => Token; // Use token as ID (first part of token string)
    
    [JsonIgnore]
    public string? Link
    {
        get
        {
            // For pull payments, link to QR code view if we have store and payout IDs
            if (!string.IsNullOrEmpty(StoreId) && !string.IsNullOrEmpty(PayoutId) && !DetectedInBackground)
            {
                return $"/stores/{StoreId}/cashu/pull-payment-claim/{PayoutId}";
            }
            // Otherwise, link to mint proof if available
            return !string.IsNullOrEmpty(Mint) ? $"{Mint}/proofs/{Token}" : null;
        }
    }
}

