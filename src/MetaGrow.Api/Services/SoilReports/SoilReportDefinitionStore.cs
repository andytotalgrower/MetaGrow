using System.Text.Json;
using System.Text.RegularExpressions;
using MetaGrow.Api.Data;
using MetaGrow.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Services.SoilReports;

/// <summary>MetaGrow owns the live layouts. XML is used only by the one-time migration and parity tests.</summary>
public sealed partial class SoilReportDefinitionStore(ApplicationDbContext _database)
{
    public async Task<List<SoilReportDefinition>> ReadAsync(CancellationToken cancellationToken = default) =>
        await _database.SoilReportDefinitions.AsNoTracking().AsSingleQuery().OrderBy(item => item.Name).ToListAsync(cancellationToken);

    public async Task<SoilReportDefinitions> SnapshotAsync(CancellationToken cancellationToken = default) =>
        new(await ReadAsync(cancellationToken));

    public async Task<SoilReportDefinition> SaveAsync(SaveSoilDefinition request, string userId, string actor,
        CancellationToken cancellationToken = default)
    {
        var draft = request.Definition;
        var fields = await _database.SoilReportDefinitionFields.Select(field => field.Key).ToListAsync(cancellationToken);
        Validate(draft, fields, request.Reason);
        await using var transaction = await _database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var stored = await _database.SoilReportDefinitions.AsSingleQuery().SingleOrDefaultAsync(item => item.Key == draft.Key, cancellationToken)
                ?? throw new KeyNotFoundException("This report definition no longer exists.");
            if (stored.Version != draft.Version) throw new DbUpdateConcurrencyException("Another person changed this definition. Reload before editing.");
            var before = JsonSerializer.Serialize(stored);
            stored.Name = draft.Name.Trim();
            stored.Version = Guid.NewGuid();
            // Delete/reinsert the small owned layout atomically: moving a measure between sections changes its key.
            // Saving the header first also claims its concurrency version before any replacement rows are added.
            stored.Sections.Clear();
            await _database.SaveChangesAsync(cancellationToken);
            stored.Sections = JsonSerializer.Deserialize<List<SoilReportDefinitionSection>>(JsonSerializer.Serialize(draft.Sections))!;
            foreach (var section in stored.Sections)
            {
                section.Name = section.Name.Trim();
                foreach (var item in section.Items)
                {
                    item.Label = item.Label.Trim();
                    // A blank short name restores the full label and preserves the legacy snapshot representation.
                    item.ShortName = string.IsNullOrWhiteSpace(item.ShortName) ? null : item.ShortName.Trim();
                }
            }
            _database.SoilReportDefinitionAudits.Add(new()
            {
                Id = Guid.NewGuid(), DefinitionKey = stored.Key, Version = stored.Version,
                ChangedUtc = DateTime.UtcNow, ChangedByUserId = userId, ChangedBy = actor,
                Reason = request.Reason.Trim(), BeforeJson = before, AfterJson = JsonSerializer.Serialize(stored)
            });
            await _database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return stored;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _database.ChangeTracker.Clear();
            throw;
        }
    }

    public static void Validate(SoilReportDefinition definition, IReadOnlyCollection<string> availableFields, string reason)
    {
        static void Require(bool valid, string message) { if (!valid) throw new ArgumentException(message); }
        Require(!string.IsNullOrWhiteSpace(reason) && reason.Length <= 500, "Enter a change reason (up to 500 characters).");
        Require(!string.IsNullOrWhiteSpace(definition.Name) && definition.Name.Length <= 120, "Enter a definition name (up to 120 characters).");
        Require(definition.Sections is { Count: > 0 and <= 30 }, "Include between 1 and 30 sections.");
        Require(definition.Sections!.Select(section => section.Id).Distinct().Count() == definition.Sections.Count, "Section identifiers must be unique.");
        Require(definition.Sections.Any(section => section.SampleSurveyVisible && section.Items is { Count: > 0 }), "Include at least one measure visible on Sample surveys.");
        var usedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in definition.Sections)
        {
            Require(section.Id > 0 && section.Order >= 0, "Section identifiers must be positive and order cannot be negative.");
            Require(!string.IsNullOrWhiteSpace(section.Name) && section.Name.Length <= 120, "Each section requires a name (up to 120 characters).");
            Require(HexColour().IsMatch(section.Colour ?? ""), "Section colours must use #RRGGBB.");
            Require(section.Items is { Count: <= 150 }, "A section can contain up to 150 measures.");
            Require(section.Items!.Select(item => item.Id).Distinct().Count() == section.Items.Count, "Measure identifiers must be unique within a section.");
            foreach (var item in section.Items)
            {
                Require(item.Id > 0 && item.Order >= 0, "Measure identifiers must be positive and order cannot be negative.");
                Require(availableFields.Contains(item.Field), $"Unknown measure: {item.Field}.");
                Require(usedFields.Add(item.Field), $"The measure {item.Field} appears more than once.");
                Require(!string.IsNullOrWhiteSpace(item.Label) && item.Label.Length <= 160, "Each measure requires a label (up to 160 characters).");
                Require(item.ShortName is null || item.ShortName.Length <= 40, "Overview short names can contain up to 40 characters.");
                // Keep formatting numeric and bounded; arbitrary custom formats can disguise or scale results.
                Require(NumberFormat().IsMatch(item.NumberFormat ?? ""), "Use #,##0, #,##0.0 through #,##0.0000, or N0–N4 for number formats.");
                Require(string.IsNullOrEmpty(item.BackgroundColour) || HexColour().IsMatch(item.BackgroundColour), "Measure backgrounds must be blank or #RRGGBB.");
            }
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();
    [GeneratedRegex("^(#,##0(\\.0{1,4})?|[nN][0-4])$")]
    private static partial Regex NumberFormat();
}
