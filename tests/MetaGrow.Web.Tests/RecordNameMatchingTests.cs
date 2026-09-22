using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public class RecordNameMatchingTests
{
    [Theory]
    [InlineData(" River  Farm ", "river farm", true)]
    [InlineData("River\tFarm", "River Farm", true)]
    [InlineData("River-Farm", "River Farm", false)]
    [InlineData("", "", false)]
    [InlineData("River Farm", "Rvier Farm", false)]
    public void SameName_IgnoresCaseAndWhitespaceOnly(string a, string b, bool expected) =>
        Assert.Equal(expected, RecordNameMatching.SameName(a, b));

    [Theory]
    [InlineData("River Farm", "Rvier Farm", true)]
    [InlineData("North Paddock", "North Padock", true)]
    [InlineData("River-Farm", "River Farm", true)]
    [InlineData("Block 1", "Block 2", false)]
    [InlineData("Farm 12", "Farm 13", false)]
    [InlineData("A", "B", false)]
    [InlineData("River Farm", "Hill Farm", false)]
    [InlineData("", "Hill Farm", false)]
    public void LikelyDuplicate_CatchesTyposButSeparatesNumbers(string a, string b, bool expected) =>
        Assert.Equal(expected, RecordNameMatching.LikelyDuplicate(a, b));

    [Fact]
    public void PartialTypingSuggestsButDoesNotWarnOnSave()
    {
        Assert.True(RecordNameMatching.Suggest("riv", "River Farm"));
        Assert.False(RecordNameMatching.LikelyDuplicate("riv", "River Farm"));
        Assert.False(RecordNameMatching.Suggest("r", "River Farm"));
    }
}
