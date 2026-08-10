namespace MetaGrow.Api.Data;

/// <summary>A current or historical email address used for future business-data matching.</summary>
public sealed class UserEmailAddress
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public bool IsConfirmed { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ConfirmedUtc { get; set; }
}
