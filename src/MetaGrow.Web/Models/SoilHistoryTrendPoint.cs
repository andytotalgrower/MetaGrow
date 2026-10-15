namespace MetaGrow.Web.Models;

/// <summary>A saved survey average at its chronological sampling-round position; null retains a gap.</summary>
public sealed record SoilHistoryTrendPoint(int Round, double? Value);
