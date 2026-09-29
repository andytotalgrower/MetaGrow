using ApiModels;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

public class CropNameReviewTests
{
    [Theory]
    [InlineData("Sweet Corn", true)]
    [InlineData(" sweet  CORN ", true)]
    [InlineData("sweetcorn", false)]
    [InlineData("Sweet Cron", false)]
    [InlineData("Sweet-Corn", false)]
    public void FindsExactAndSimilarNames(string name, bool exact)
    {
        var review = CropNameReview.Check(name, 0, [new() { CropTypeId = 1, CropTypeName = "Sweet Corn" }]);
        Assert.Single(review.Candidates);
        Assert.Equal(exact, review.HasExactDuplicate);
    }

    [Fact]
    public void SearchesAcrossParentsAndDoesNotFlagUnrelatedNames()
    {
        var review = CropNameReview.Check("sweetcorn", 10, [
            new() { CropTypeId = 1, CropTypeName = "Sweet Corn", ParentCropTypeId = 20 },
            new() { CropTypeId = 2, CropTypeName = "Banana" }]);
        Assert.Equal(1, Assert.Single(review.Candidates).CropTypeId);
    }

    [Fact]
    public void ConfirmationIsInvalidatedByChangedNameParentOrCandidates()
    {
        List<CropType> crops = [new() { CropTypeId = 1, CropTypeName = "Sweet Corn" }];
        var initial = CropNameReview.Check("sweetcorn", 0, crops).Key;
        Assert.Equal(initial, CropNameReview.Check("sweetcorn", 0, crops).Key);
        Assert.NotEqual(initial, CropNameReview.Check("sweetcorn", 2, crops).Key);
        Assert.NotEqual(initial, CropNameReview.Check("Sweetcorn", 0, crops).Key);
        crops.Add(new() { CropTypeId = 3, CropTypeName = "Sweet Corns" });
        Assert.NotEqual(initial, CropNameReview.Check("sweetcorn", 0, crops).Key);
    }
}
