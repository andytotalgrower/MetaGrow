using ApiModels;

namespace MetaGrow.Web.Services;

public sealed record PropertyLinkedDataCategory(
    string SingularLabel,
    string PluralLabel,
    long RowCount,
    string? FinderPath = null)
{
    public string DisplayText => $"{RowCount:N0} {(RowCount == 1 ? SingularLabel : PluralLabel)}";
}

public static class PropertyDependencyPresentation
{
    public static IReadOnlyList<PropertyLinkedDataCategory> Summarize(
        PropertyDependencySummary? summary)
    {
        if (summary is null) return [];

        return summary.Dependencies
            .Where(item => item.RowCount > 0)
            .GroupBy(item => CategoryFor(item.TableName))
            .Select(group => new PropertyLinkedDataCategory(
                group.Key.Singular,
                group.Key.Plural,
                group.Sum(item => item.RowCount),
                group.Key.FinderPath))
            .OrderByDescending(item => item.RowCount)
            .ThenBy(item => item.PluralLabel)
            .ToList();
    }

    private static (string Singular, string Plural, string? FinderPath) CategoryFor(string? tableName)
    {
        var name = tableName ?? string.Empty;

        if (ContainsAny(name, "LabTissue"))
            return ("tissue lab record", "tissue lab records", "/surveys/samples/lab-results?type=tissue");
        if (ContainsAny(name, "LabQuickSoil"))
            return ("quick soil lab record", "quick soil lab records", "/surveys/samples/lab-results?type=quick-soil");
        if (ContainsAny(name, "LabSoil", "SoilReport") ||
            name.Equals("TgsSoil", StringComparison.OrdinalIgnoreCase))
            return ("soil lab record", "soil lab records", "/surveys/samples/lab-results?type=soil");
        if (ContainsAny(name, "LabSap"))
            return ("sap lab record", "sap lab records", "/surveys/samples/lab-results?type=sap");
        if (ContainsAny(name, "SampleSurvey", "TgsSample"))
            return ("sample survey record", "sample survey records", null);
        if (name.Equals("TgsFarmSurvey", StringComparison.OrdinalIgnoreCase))
            return ("multi-crop survey", "multi-crop surveys", "/surveys/multicrop");
        if (ContainsAny(name, "FarmSurvey", "CropBlock", "FarmYield", "PapayaSurvey"))
            return ("multi-crop survey data row", "multi-crop survey data rows", "/surveys/multicrop");
        if (name.Equals("TgsVisit", StringComparison.OrdinalIgnoreCase))
            return ("banana survey", "banana surveys", "/surveys/banana");
        if (ContainsAny(name, "Visit"))
            return ("banana survey data row", "banana survey data rows", "/surveys/banana");
        if (ContainsAny(name, "TgsBlock", "BunchSurvey", "FingerCount", "HotSpot", "TgsLeaf", "SurveySpot"))
            return ("banana survey record", "banana survey records", "/surveys/banana");
        if (ContainsAny(name, "Billable", "PurchaseOrder", "Quotation"))
            return ("billing or purchase-order record", "billing or purchase-order records", null);
        if (ContainsAny(name, "PropertyGroup", "SiloRecipient", "UserProperty", "ReportBlockGroup"))
            return ("reporting or access record", "reporting or access records", null);
        if (ContainsAny(name, "Comment", "ContactLog"))
            return ("note or contact record", "note or contact records", null);
        if (ContainsAny(name, "CustomReport"))
            return ("custom report", "custom reports", null);

        return ("other linked record", "other linked records", null);
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
