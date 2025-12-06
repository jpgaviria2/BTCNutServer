using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.Cashu.Data.Models;

public enum CashuClaimStatus
{
    Pending = 0,
    Claimed = 1,
    Expired = 2,
    Failed = 3
}

public class CashuClaim
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Required] public string StoreId { get; set; } = default!;
    [Required] public ulong AmountSats { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ClaimedAt { get; set; }

    public CashuClaimStatus Status { get; set; } = CashuClaimStatus.Pending;

    public string? Token { get; set; }
    public string? Mint { get; set; }
    public string? Error { get; set; }

    public bool IsExpired(DateTimeOffset nowUtc) =>
        ExpiresAt.HasValue && nowUtc >= ExpiresAt.Value;
}

