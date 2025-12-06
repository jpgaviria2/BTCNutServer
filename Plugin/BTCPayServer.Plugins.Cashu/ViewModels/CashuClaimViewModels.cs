using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Cashu.ViewModels;

public class CashuClaimCreateViewModel
{
    [Required]
    [Range(1, long.MaxValue, ErrorMessage = "Amount must be at least 1 sat")]
    public ulong AmountSats { get; set; } = 1;

    // Optional expiry in minutes; null or 0 means no expiry
    [Range(0, int.MaxValue, ErrorMessage = "Expiry must be >= 0 minutes")]
    public int? ExpiryMinutes { get; set; }
}

public class CashuClaimLinkViewModel
{
    public Guid ClaimId { get; set; }
    public string StoreId { get; set; } = string.Empty;
    public ulong AmountSats { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string ClaimUrl { get; set; } = string.Empty;
}

public class CashuClaimPublicViewModel
{
    public Guid ClaimId { get; set; }
    public ulong AmountSats { get; set; }
    public string? Token { get; set; }
    public string? Mint { get; set; }
    public string? Error { get; set; }
    public bool IsClaimed { get; set; }
    public bool IsExpired { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }

    public string FormattedAmount => $"{AmountSats} sats";
}

