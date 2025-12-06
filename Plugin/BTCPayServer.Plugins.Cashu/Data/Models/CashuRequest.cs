using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BTCPayServer.Plugins.Cashu.Data.Models;

public enum CashuRequestStatus
{
    Pending = 0,
    Received = 1,
    Expired = 2,
    Failed = 3
}

public class CashuRequest
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Required] public string StoreId { get; set; } = default!;

    public ulong? AmountSats { get; set; }
    public string? Memo { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }

    public CashuRequestStatus Status { get; set; } = CashuRequestStatus.Pending;

    public string? ReceivedToken { get; set; }
    public string? ReceivedMint { get; set; }
    public string? Error { get; set; }

    public bool IsExpired(DateTimeOffset nowUtc) =>
        ExpiresAt.HasValue && nowUtc >= ExpiresAt.Value;
}

