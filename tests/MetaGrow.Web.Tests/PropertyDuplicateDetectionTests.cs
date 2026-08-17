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

    [Fact]
    public void BestMatchIgnoresCaseSpacingAndBusinessSuffixVariants()
    {
        var properties = Properties("Bundaberg Farming Co.", "Alloway Farm");

        var match = _service.FindBestMatchingProperty("BUNDABERG FARMING COMPANY PTY LTD", properties);

        Assert.Equal("Bundaberg Farming Co.", match?.PropertyName);
    }

    [Fact]
    public void BestMatchAcceptsAnObviousTypingError()
    {
        var properties = Properties("Green Valley Farms", "Blue Hills");

        var match = _service.FindBestMatchingProperty("Green Valey Farms", properties);

        Assert.Equal("Green Valley Farms", match?.PropertyName);
    }

    [Fact]
    public void BestMatchDoesNotGuessWhenTwoPropertiesAreEquallyPlausible()
    {
        var properties = Properties("Smith Farm North", "Smith Farm South");

        var match = _service.FindBestMatchingProperty("Smith Farm", properties);

        Assert.Null(match);
    }

    [Fact]
    public void BestMatchDoesNotCrossNumberedFarms()
    {
        var properties = Properties("Test Farm 1", "Test Farm 2");

        var match = _service.FindBestMatchingProperty("Test Farm 2", properties);

        Assert.Equal("Test Farm 2", match?.PropertyName);
    }

    [Fact]
    public void BestBlockMatchIgnoresCaseSpacingAndPunctuation()
    {
        var blocks = Blocks("Blueberry Block A", "Avocado 1");

        var match = _service.FindBestMatchingBlock("blueberry-block-a", blocks);

        Assert.Equal("Blueberry Block A", match?.BlockName);
    }

    [Fact]
    public void BestBlockMatchAcceptsAnObviousTypingError()
    {
        var blocks = Blocks("Peanut Leaf", "North Paddock");

        var match = _service.FindBestMatchingBlock("Peenut Leaf", blocks);

        Assert.Equal("Peanut Leaf", match?.BlockName);
    }

    [Fact]
    public void BestBlockMatchDoesNotGuessBetweenDuplicateNames()
    {
        var blocks = Blocks("Block 1", "Block 1");

        var match = _service.FindBestMatchingBlock("Block 1", blocks);

        Assert.Null(match);
    }

    [Fact]
    public void BestBlockMatchDoesNotCrossNumberedBlocks()
    {
        var blocks = Blocks("Block 1", "Block 2");

        var match = _service.FindBestMatchingBlock("Block 2", blocks);

        Assert.Equal("Block 2", match?.BlockName);
    }

    private List<PropertyDuplicateCandidate> Find(string first, string second) =>
        _service.FindPotentialDuplicateProperties(
        [
            new Property { PropertyId = 1, PropertyName = first, IsActive = true },
            new Property { PropertyId = 2, PropertyName = second, IsActive = true }
        ]);

    private static List<Property> Properties(params string[] names) => names
        .Select((name, index) => new Property { PropertyId = index + 1, PropertyName = name, IsActive = true })
        .ToList();

    private static List<Block> Blocks(params string[] names) => names
        .Select((name, index) => new Block { BlockId = index + 1, BlockName = name, IsActive = true })
        .ToList();
}
