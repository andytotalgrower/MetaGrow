using ApiModels.MetaGrow;

namespace MetaGrow.Web.Models;

/// <summary>Nullable editor values distinguish an unfinished field from a deliberately entered zero.</summary>
public sealed class SoilTemplateTargetEditor
{
    public int TargetId { get; set; }
    public int ItemId { get; set; }
    public double? AcceptableRangeMin { get; set; }
    public double? AcceptableRangeMax { get; set; }
    public double? Target { get; set; }
    public int FormulaEnum { get; set; }
    public string? Params { get; set; }

    public bool IsComplete => AcceptableRangeMin.HasValue && AcceptableRangeMax.HasValue && Target.HasValue;

    public SoilTemplateTarget ToTarget() => new()
    {
        TargetId = TargetId, ItemId = ItemId,
        AcceptableRangeMin = AcceptableRangeMin ?? throw new InvalidOperationException("Enter a minimum."),
        AcceptableRangeMax = AcceptableRangeMax ?? throw new InvalidOperationException("Enter a maximum."),
        Target = Target ?? throw new InvalidOperationException("Enter a target."),
        FormulaEnum = FormulaEnum, Params = Params
    };

    public static SoilTemplateTargetEditor FromTarget(SoilTemplateTarget target) => new()
    {
        TargetId = target.TargetId, ItemId = target.ItemId,
        AcceptableRangeMin = target.AcceptableRangeMin, AcceptableRangeMax = target.AcceptableRangeMax,
        Target = target.Target, FormulaEnum = target.FormulaEnum, Params = target.Params
    };
}
