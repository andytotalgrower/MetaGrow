namespace MetaGrow.Shared.Models;

/// <summary>Account-level report branding, stored in MetaGrow Identity rather than totalgs.</summary>
public sealed class ReportBrandPreference
{
    public const string _metagen = "metagen";
    public const string _tgs = "total-grower-services";
    public string BrandKey { get; set; } = _metagen;

    public static bool IsSupported(string? key) => key is _metagen or _tgs;
    public static string Normalize(string? key) => IsSupported(key) ? key! : _metagen;
}
