using System.Globalization;
using System.Text;

namespace MetaGrow.Web.Services;

public static class MultiCropReportFileName
{
    private const int MaximumFarmNameLength = 32;

    public static string Build(string? propertyName, int propertyId, DateTime surveyDate)
    {
        var farmName = ToFileNameSegment(propertyName);
        if (string.IsNullOrEmpty(farmName))
        {
            farmName = propertyId > 0 ? $"farm-{propertyId}" : "farm";
        }

        if (farmName.Length > MaximumFarmNameLength)
        {
            farmName = farmName[..MaximumFarmNameLength].TrimEnd('-');
        }

        return $"mcs_{farmName}_{surveyDate:yyyyMMdd}.pdf";
    }

    private static string ToFileNameSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var result = new StringBuilder(value.Length);
        var separatorPending = false;

        foreach (var character in value.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && result.Length > 0)
                {
                    result.Append('-');
                }

                result.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else
            {
                separatorPending = result.Length > 0;
            }
        }

        return result.ToString();
    }
}
