using ApiModels;
using Metagen.Shared.Models;
using Metagen.Shared.Services;

namespace MetaGrow.Web.Tests;

public class PropertyDuplicateDetectionTests
{
    private readonly StringSimilarityService _service = new();

    [Fact]
    public void FindsNamesThatOnlyDifferBySpacingAndPunctuation()
    {
        var candidates = Find("O'Brien Farms", "O Brien Farms");

        var candidate = Assert.Single(candidates);
        Assert.Equal(PropertyDuplicateConfidence.VeryHigh, candidate.Confidence);
        Assert.Equal(0, candidate.EditDistance);
    }

    [Fact]
    public void FindsSingleCharacterTypingErrors()
    {
        var candidates = Find("Green Valley Farms", "Green Valey Farms");

        var candidate = Assert.Single(candidates);
        Assert.Equal(PropertyDuplicateConfidence.Likely, candidate.Confidence);
        Assert.Equal(1, candidate.EditDistance);
    }

    [Fact]
    public void FindsTwoCharacterDifferencesOnlyForLongerNames()
    {
        var candidates = Find("Riverbend Orchards", "Riverband Orchard");

        var candidate = Assert.Single(candidates);
        Assert.Equal(PropertyDuplicateConfidence.Possible, candidate.Confidence);
        Assert.Equal(2, candidate.EditDistance);
    }

    [Fact]
    public void DoesNotFlagDifferentNumberedFarms()
    {
        var candidates = Find("Test Farm 1", "Test Farm 2");

        Assert.Empty(candidates);
    }

    [Fact]
    public void DoesNotUseOneCharacterMatchingForVeryShortNames()
    {
        var candidates = Find("ABC", "ADC");

        Assert.Empty(candidates);
    }

    [Fact]
    public void ExcludesPropertiesCalledMetagen()
    {
        var candidates = Find("Metagen", " metagen ");

        Assert.Empty(candidates);
    }

    private List<PropertyDuplicateCandidate> Find(string first, string second) =>
        _service.FindPotentialDuplicateProperties(
        [
            new Property { PropertyId = 1, PropertyName = first, IsActive = true },
            new Property { PropertyId = 2, PropertyName = second, IsActive = true }
        ]);
}
