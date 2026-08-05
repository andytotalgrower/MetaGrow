namespace MetaGrow.Shared;

/// <summary>
/// Identifies a top-level MetaGrow work area without coupling shared contracts to the Blazor UI.
/// </summary>
public sealed record MetaGrowModule(string Name, string Route);
