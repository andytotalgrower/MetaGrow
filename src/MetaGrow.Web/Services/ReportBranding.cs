namespace MetaGrow.Web.Services;

public sealed record ReportBrandOption(
    string Key,
    string Label,
    string LogoPath,
    string AltText);

public static class ReportBranding
{
    public const string MetagenKey = "metagen";
    public const string TotalGrowerServicesKey = "total-grower-services";

    public static IReadOnlyList<ReportBrandOption> Options { get; } =
    [
        new(
            MetagenKey,
            "Metagen",
            "/images/branding/metagen-australia.jpg",
            "Metagen Australia"),
        new(
            TotalGrowerServicesKey,
            "Total Grower Services",
            "/images/branding/total-grower-services.png",
            "Total Grower Services")
    ];

    public static ReportBrandOption Get(string? key) =>
        Options.FirstOrDefault(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase))
        ?? Options[0];
}
