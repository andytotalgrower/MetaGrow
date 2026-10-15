using System.Text.Json;
using System.Xml.Linq;
using MetaGrow.Api.Data;
using MetaGrow.Api.Data.Migrations;
using MetaGrow.Api.Services.SoilReports;
using MetaGrow.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MetaGrow.Api.Tests;

public sealed class SoilReportDefinitionTests
{
    [Fact]
    public void Migration_preserves_every_legacy_section_measure_and_display_setting()
    {
        var definitions = SeedDefinitions();
        Assert.Equal(12, definitions.Count);
        Assert.Equal(50, definitions.Sum(definition => definition.Sections.Count));
        Assert.Equal(432, definitions.Sum(definition => definition.Sections.Sum(section => section.Items.Count)));
        foreach (var definition in definitions)
        {
            var xml = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SoilDefinitions", definition.Key));
            foreach (var original in xml.Root!.Elements("ReportSection"))
            {
                var section = Assert.Single(definition.Sections, section => section.Id == (int)original.Element("SectionId")!);
                Assert.Equal((string?)original.Element("SectionName"), section.Name);
                Assert.Equal((string?)original.Element("Colour"), section.Colour);
                Assert.Equal((int)original.Element("DisplayOrder")!, section.Order);
                Assert.Equal(section.Name != "Finger Count", section.SampleSurveyVisible);
                foreach (var row in xml.Root.Elements("ReportItem").Where(row => (int)row.Element("SectionId")! == section.Id))
                {
                    var item = Assert.Single(section.Items, item => item.Field == (string?)row.Element("ObjectName"));
                    Assert.Equal((int)row.Element("ItemId")!, item.LegacyItemId);
                    Assert.Equal((string?)row.Element("ObjectName"), item.Field);
                    Assert.Equal((string?)row.Element("DisplayText"), item.Label);
                    Assert.Equal((int)row.Element("DisplayOrder")!, item.Order);
                    Assert.Equal((string?)row.Element("NumberFormat"), item.NumberFormat);
                    Assert.Equal((bool?)row.Element("ShowIfZero") ?? true, item.ShowIfZero);
                    Assert.Equal((string?)row.Element("BgColour") == "TotalBgColor" ? "#ffffff" : null, item.BackgroundColour);
                }
            }
            SoilReportDefinitionStore.Validate(definition, Fields().Select(field => field.Key).ToList(), "Converted legacy layout");
        }
        // The additive migration must never drop unrelated Identity tables or contain TotalGS operations.
        var operations = new AddSoilReportDefinitions().UpOperations;
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or AlterColumnOperation or SqlOperation);
        Assert.All(operations.OfType<CreateTableOperation>(), operation => Assert.StartsWith("SoilReportDefinition", operation.Name));
    }

    [Fact]
    public async Task Saves_are_atomic_audited_and_reject_stale_edits_while_old_snapshots_remain_unchanged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
        await using var database = new ApplicationDbContext(options);
        await database.Database.EnsureCreatedAsync();
        database.SoilReportDefinitionFields.AddRange(Fields());
        database.SoilReportDefinitions.AddRange(SeedDefinitions());
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var store = new SoilReportDefinitionStore(database);
        var snapshot = await store.SnapshotAsync();
        var before = snapshot.Get("LabReportDefinition.xml").First().Label;
        var edit = (await store.ReadAsync()).Single(definition => definition.Key == "LabReportDefinition.xml");
        var stale = Clone(edit);
        edit.Sections[0].Items[0].Label = "Reviewed pH label";
        edit.Sections[0].Items[0].ShortName = "  pH  ";
        // Moving a measure changes the owned key and must not trigger duplicate bindings or partial saves.
        var moved = edit.Sections[0].Items[1];
        edit.Sections[0].Items.Remove(moved);
        edit.Sections[1].Items.Add(moved);
        var saved = await store.SaveAsync(new(edit, "Clarify labels"), "user-1", "Agronomy manager");
        Assert.NotEqual(stale.Version, saved.Version);
        Assert.Equal(before, snapshot.Get("LabReportDefinition.xml").First().Label);
        Assert.Null(snapshot.Get("LabReportDefinition.xml").First().ShortName);
        Assert.Equal("pH", (await store.SnapshotAsync()).Get("LabReportDefinition.xml").First().ShortName);
        Assert.NotEqual(snapshot.VersionHash, (await store.SnapshotAsync()).VersionHash);
        var audit = Assert.Single(await database.SoilReportDefinitionAudits.ToListAsync());
        Assert.Equal("user-1", audit.ChangedByUserId);
        Assert.Equal("Clarify labels", audit.Reason);
        Assert.Contains("Reviewed pH label", audit.AfterJson);
        Assert.Contains("\"ShortName\":\"pH\"", audit.AfterJson);
        Assert.DoesNotContain("ShortName", audit.BeforeJson);
        Assert.DoesNotContain("Reviewed pH label", audit.BeforeJson);
        stale.Name = "Stale rename";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.SaveAsync(new(stale, "Old edit"), "user-2", "Other manager"));
        Assert.Single(await database.SoilReportDefinitionAudits.ToListAsync());
        var persisted = (await store.ReadAsync()).Single(definition => definition.Key == edit.Key);
        Assert.Equal(saved.Version, persisted.Version);
        Assert.Equal(edit.Sections.Sum(section => section.Items.Count), persisted.Sections.Sum(section => section.Items.Count));

        // A later validation failure must leave both data and history untouched.
        persisted.Sections[0].Items.Add(Clone(persisted.Sections[1].Items[0]));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new(persisted, "Invalid duplicate"), "user-1", "Manager"));
        Assert.Single(await database.SoilReportDefinitionAudits.ToListAsync());

        // Fail after the old child rows were deleted. The transaction must restore the entire prior layout.
        await database.Database.ExecuteSqlRawAsync("CREATE TRIGGER RejectDefinitionAudit BEFORE INSERT ON SoilReportDefinitionAudits BEGIN SELECT RAISE(ABORT, 'Simulated audit failure'); END;");
        var failedEdit = (await store.ReadAsync()).Single(definition => definition.Key == edit.Key);
        failedEdit.Name = "Must roll back";
        await Assert.ThrowsAsync<DbUpdateException>(() => store.SaveAsync(new(failedEdit, "Test rollback"), "user-1", "Manager"));
        var restored = (await store.ReadAsync()).Single(definition => definition.Key == edit.Key);
        Assert.Equal(saved.Name, restored.Name);
        Assert.Equal(saved.Version, restored.Version);
        Assert.Equal(new SoilReportDefinitions([saved]).VersionHash, new SoilReportDefinitions([restored]).VersionHash);
        Assert.Single(await database.SoilReportDefinitionAudits.ToListAsync());
    }

    [Fact]
    public void Snapshot_hash_is_independent_of_query_row_order_and_contains_hidden_legacy_sections()
    {
        var first = SeedDefinitions();
        var expected = new SoilReportDefinitions(first);
        first.Reverse();
        foreach (var definition in first)
        {
            definition.Sections.Reverse();
            foreach (var section in definition.Sections) section.Items.Reverse();
        }
        var actual = new SoilReportDefinitions(first);
        Assert.Equal(expected.VersionHash, actual.VersionHash);
        Assert.Contains(actual.Snapshot.SelectMany(definition => definition.Sections), section => section.Name == "Finger Count");
        Assert.DoesNotContain(actual.Get("LabReportDefinition_Soil_Banana.xml"), item => item.Section == "Finger Count");
        first[0].Name = "Mutated input";
        Assert.DoesNotContain(actual.Snapshot, definition => definition.Name == "Mutated input");
    }

    [Theory]
    [InlineData("Bananas", "LabReportDefinition_Soil_Banana.xml")]
    [InlineData("Sugar cane", "LabReportDefinition_Soil_Sugar.xml")]
    [InlineData("Avocado", "LabReportDefinition.xml")]
    public void Legacy_crop_selection_uses_database_definitions(string crop, string key)
    {
        var definitions = new SoilReportDefinitions(SeedDefinitions());
        var selected = definitions.Select(crop, new Dictionary<string, double?>());
        Assert.Equal(key, selected);
        Assert.NotEmpty(definitions.Get(selected));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("percent-format")]
    [InlineData("unsafe-colour")]
    [InlineData("empty-name")]
    [InlineData("empty-report")]
    [InlineData("long-short-name")]
    public void Invalid_definition_changes_are_rejected(string scenario)
    {
        var definition = SeedDefinitions()[0];
        var section = definition.Sections[0];
        var item = section.Items[0];
        switch (scenario)
        {
            case "unknown": item.Field = "ArbitraryDatabaseColumn"; break;
            case "duplicate": section.Items.Add(Clone(item)); break;
            case "percent-format": item.NumberFormat = "0%"; break;
            case "unsafe-colour": section.Colour = "red; display:none"; break;
            case "empty-name": definition.Name = " "; break;
            case "empty-report": definition.Sections.ForEach(section => section.SampleSurveyVisible = false); break;
            case "long-short-name": item.ShortName = new string('x', 41); break;
        }
        Assert.Throws<ArgumentException>(() => SoilReportDefinitionStore.Validate(definition, Fields().Select(field => field.Key).ToList(), "Test change"));
    }

    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

    [Fact]
    public void Short_name_migration_is_nullable_and_does_not_rewrite_existing_definitions()
    {
        // Adding optional display metadata must not invalidate old definitions or alter totalgs.
        var column = Assert.IsType<AddColumnOperation>(Assert.Single(new AddSoilReportShortNames().UpOperations));
        Assert.Equal("SoilReportDefinitionItems", column.Table);
        Assert.Equal("ShortName", column.Name);
        Assert.True(column.IsNullable);
        Assert.Equal(40, column.MaxLength);
        Assert.DoesNotContain("ShortName", JsonSerializer.Serialize(new SoilReportDefinitions(SeedDefinitions()).Snapshot));
    }

    // Read the actual migration operations, not a second test-only copy of the seed data.
    private static List<Dictionary<string, object?>> Rows(string table)
    {
        var operation = Assert.Single(new AddSoilReportDefinitions().UpOperations.OfType<InsertDataOperation>(), operation => operation.Table == table);
        return Enumerable.Range(0, operation.Values.GetLength(0)).Select(row => Enumerable.Range(0, operation.Columns.Length)
            .ToDictionary(column => operation.Columns[column], column => operation.Values[row, column])).ToList();
    }
    private static List<SoilDefinitionField> Fields() => Rows("SoilReportDefinitionFields")
        .Select(row => new SoilDefinitionField((string)row["Key"]!, (string)row["Label"]!)).ToList();
    internal static List<SoilReportDefinition> SeedDefinitions()
    {
        var headers = Rows("SoilReportDefinitions");
        var sections = Rows("SoilReportDefinitionSections");
        var measures = Rows("SoilReportDefinitionItems");
        return headers.Select(header =>
        {
            var definition = JsonSerializer.Deserialize<SoilReportDefinition>(JsonSerializer.Serialize(header))!;
            definition.Sections = sections.Where(row => Equals(row["DefinitionKey"], definition.Key)).Select(row =>
            {
                var section = JsonSerializer.Deserialize<SoilReportDefinitionSection>(JsonSerializer.Serialize(row))!;
                section.Items = measures.Where(item => Equals(item["DefinitionKey"], definition.Key) && Equals(item["SectionId"], section.Id))
                    .Select(item => JsonSerializer.Deserialize<SoilReportDefinitionItem>(JsonSerializer.Serialize(item))!).ToList();
                return section;
            }).ToList();
            return definition;
        }).ToList();
    }
}
