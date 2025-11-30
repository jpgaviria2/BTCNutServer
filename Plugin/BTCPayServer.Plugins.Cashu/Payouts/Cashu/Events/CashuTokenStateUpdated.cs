using System;

namespace BTCPayServer.Plugins.Cashu.Payouts.Cashu.Events;

/// <summary>
/// Event published when Cashu token state changes (e.g., token spent/received)
/// Used for background monitoring of payout confirmations
/// </summary>
public class CashuTokenStateUpdated
{
    public string Token { get; set; } = string.Empty;
    public string Mint { get; set; } = string.Empty;
    public bool IsSpent { get; set; }
    public ulong Amount { get; set; }
    public string? TransactionId { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

