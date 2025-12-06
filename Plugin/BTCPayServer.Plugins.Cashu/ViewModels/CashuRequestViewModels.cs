using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Cashu.ViewModels;

public class CashuRequestCreateViewModel
{
    [Range(1, long.MaxValue, ErrorMessage = "Amount must be at least 1 sat")]
    public ulong? AmountSats { get; set; }

    public string? Memo { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "Expiry must be >= 0 minutes")]
    public int? ExpiryMinutes { get; set; }
}

public class CashuRequestLinkViewModel
{
    public Guid RequestId { get; set; }
    public string StoreId { get; set; } = string.Empty;
    public ulong? AmountSats { get; set; }
    public string? Memo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string RequestUrl { get; set; } = string.Empty;
}

public class CashuRequestPublicViewModel
{
    public Guid RequestId { get; set; }
    public ulong? AmountSats { get; set; }
    public string? Memo { get; set; }
    public bool IsExpired { get; set; }
    public bool IsReceived { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Error { get; set; }

    public string? ReceivedToken { get; set; }
    public string? ReceivedMint { get; set; }

    public string FormattedAmount => AmountSats.HasValue ? $"{AmountSats.Value} sats" : "Any amount";
}

