namespace MetaGrow.Web.Services;

public sealed record ReportBrandOption(
    string Key,
    string Label,
    string LogoPath,
    string AltText,
    string CompanyName);

public static class ReportBranding
{
    // Keep presentation choices aligned with the account preference contract.
    public const string MetagenKey = MetaGrow.Shared.Models.ReportBrandPreference._metagen;
    public const string TotalGrowerServicesKey = MetaGrow.Shared.Models.ReportBrandPreference._tgs;

    public static IReadOnlyList<ReportBrandOption> Options { get; } =
    [
        new(
            MetagenKey,
            "Metagen",
            "/images/branding/metagen-australia.jpg",
            "Metagen Australia",
            "Metagen Pty Ltd"),
        new(
            TotalGrowerServicesKey,
            "Total Grower Services",
            "/images/branding/total-grower-services.png",
            "Total Grower Services",
            "TGS Pty Ltd")
    ];

    public static ReportBrandOption Get(string? key) =>
        Options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? Options[0];
}
