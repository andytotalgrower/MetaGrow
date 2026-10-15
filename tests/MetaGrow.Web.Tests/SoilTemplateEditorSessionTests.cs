using System.Text.Json;
using ApiModels.MetaGrow;
using MetaGrow.Web.Models;
using MetaGrow.Web.Services;

namespace MetaGrow.Web.Tests;

/// <summary>Exercise editor writes against an in-memory server, never the application database.</summary>
public sealed class SoilTemplateEditorSessionTests
{
    [Fact]
    public async Task HeaderWaitsForRequiredFieldsThenCreatesOnlyOnce()
    {
        var server = new Server();
        var session = server.Session(new());
        Assert.False(await session.SaveAsync());
        session.Draft.TemplateName = "Soil";
        Assert.False(await session.SaveAsync());
        session.Draft.CropId = 1;
        Assert.True(await session.SaveAsync());
        Assert.Empty(server.Stored!.Targets);
        Assert.Equal(1, server.Writes);
        session.Draft.TemplateName = "Soil amended";
        await session.SaveAsync();
        Assert.Equal(42, server.LastRequest!.TemplateId);
        Assert.Equal("v1", server.LastRequest.Version);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public async Task ClearingNumericInputDoesNotSaveZero()
    {
        var server = new Server(); var session = server.Session(Existing());
        session.Targets[0].AcceptableRangeMin = null;
        Assert.False(await session.SaveAsync());
        Assert.Equal(0, server.Writes);
        session.Targets[0].AcceptableRangeMin = 0;
        session.Targets[0].Target = 6;
        Assert.True(await session.SaveAsync());
        Assert.Equal(0, server.Stored!.Targets[0].AcceptableRangeMin);
    }

    [Fact]
    public async Task ConfirmedDeletionPersistsImmediatelyAndRetainsUnfinishedEdits()
    {
        var server = new Server(); var session = server.Session(Existing());
        session.Draft.TemplateName = "";
        session.Targets[0].Target = null;
        await session.DeleteTargetAsync(10);
        Assert.Equal(1, server.Writes);
        Assert.Empty(server.Stored!.Targets);
        Assert.Equal("Soil", server.Stored.TemplateName);
        Assert.Equal("", session.Draft.TemplateName);
        Assert.Empty(session.Targets);
        Assert.True(session.HasChanges);
        session.Draft.TemplateName = "Renamed";
        Assert.True(await session.SaveAsync());
        Assert.Equal("v1", server.LastRequest!.Version);
    }

    [Fact]
    public async Task LegacyDuplicateGroupsCanBeCleanedUpIndividually()
    {
        var detail = Existing();
        detail.Targets.Add(new() { TargetId = 11, ItemId = 1 });
        detail.Targets.Add(new() { TargetId = 12, ItemId = 2 });
        detail.Targets.Add(new() { TargetId = 13, ItemId = 2 });
        var server = new Server(); var session = server.Session(detail);
        await session.DeleteTargetAsync(11);
        Assert.Equal(3, server.Stored!.Targets.Count);
        await session.DeleteTargetAsync(13);
        Assert.Equal(2, server.Stored.Targets.Count);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public async Task NewDuplicateRowsAreRejectedBeforeWriting()
    {
        var server = new Server(); var session = server.Session(Existing());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.AddTargetAsync(new()
            { ItemId = 1, AcceptableRangeMin = 0, Target = 1, AcceptableRangeMax = 2 }));
        Assert.Equal(0, server.Writes);
        Assert.False(session.NeedsReload);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("legacy value")]
    public async Task DeletingDuplicatePreservesOtherLegacyFormulaParameters(string? parameters)
    {
        // Model the reported failure: a potassium duplicate alongside an untouched invalid TEC multiplier.
        var detail = Existing();
        detail.Targets[0].FormulaEnum = 10; detail.Targets[0].Params = parameters;
        detail.Targets.Add(new() { TargetId = 11, ItemId = 2 });
        detail.Targets.Add(new() { TargetId = 12, ItemId = 2 });
        var server = new Server(); var session = server.Session(detail);
        await session.DeleteTargetAsync(12);
        Assert.Equal(1, server.Writes);
        Assert.Equal(new[] { 10, 11 }, server.Stored!.Targets.Select(target => target.TargetId));
        Assert.Equal(parameters, server.Stored.Targets[0].Params);
        Assert.Equal(10, server.Stored.Targets[0].FormulaEnum);
        Assert.False(session.NeedsReload);
        Assert.False(session.HasChanges);

        // Unrelated valid edits remain possible; changing the legacy multiplier itself must be valid.
        session.Draft.TemplateName = "Updated cropping";
        Assert.True(await session.SaveAsync());
        session.Targets[0].Params = "new invalid multiplier";
        Assert.False(await session.SaveAsync());
        Assert.Equal(2, server.Writes);
        session.Targets[0].Params = "2.5";
        Assert.True(await session.SaveAsync());
        Assert.Equal("2.5", server.Stored.Targets[0].Params);
    }

    [Fact]
    public async Task CopiedDuplicatesMustBeRemovedBeforeCreation()
    {
        var copy = Existing(); copy.TemplateId = 0; copy.SourceTemplateId = 42;
        copy.Targets.Add(new() { TargetId = 11, ItemId = 1 });
        var server = new Server(); var session = server.Session(copy);
        Assert.False(await session.SaveAsync());
        await session.DeleteTargetAsync(11);
        Assert.Equal(0, server.Writes);
        Assert.True(await session.SaveAsync());
        Assert.Equal(1, server.Writes);
    }

    [Fact]
    public async Task FailedReadAfterCreateCannotCauseADuplicateCreate()
    {
        var server = new Server { FailLoad = true };
        var detail = Existing(); detail.TemplateId = 0; detail.Targets = [];
        var session = server.Session(detail);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsync());
        Assert.Equal(42, session.Draft.TemplateId);
        Assert.True(session.NeedsReload);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsync());
        Assert.Equal(1, server.Writes);
    }

    [Fact]
    public async Task FailedDeletionKeepsRowAndStopsAutomaticRetries()
    {
        var server = new Server { FailSave = true }; var session = server.Session(Existing());
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.DeleteTargetAsync(10));
        Assert.Single(session.Targets);
        Assert.True(session.NeedsReload);
    }

    private static SoilTemplateDetail Existing() => new()
    {
        TemplateId = 42, TemplateName = "Soil", CropId = 1, TestTypeId = 10, Version = "original",
        Targets = [new() { TargetId = 10, ItemId = 1, AcceptableRangeMin = 0, Target = 5, AcceptableRangeMax = 10 }]
    };
    private static SoilTemplateDetail Clone(SoilTemplateDetail detail) => JsonSerializer.Deserialize<SoilTemplateDetail>(JsonSerializer.Serialize(detail))!;
    private sealed class Server
    {
        private int _writes;
        public int Writes => _writes;
        public bool FailLoad { get; init; }
        public bool FailSave { get; init; }
        public SoilTemplateDetail? Stored { get; private set; }
        public SoilTemplateDetail? LastRequest { get; private set; }
        public SoilTemplateEditorSession Session(SoilTemplateDetail detail)
        {
            Stored = Clone(detail);
            return new(detail, request =>
            {
                if (FailSave) throw new InvalidOperationException("Stale version");
                _writes++; LastRequest = Clone(request); Stored = Clone(request);
                Stored.TemplateId = 42; Stored.SourceTemplateId = 0; Stored.Version = $"v{_writes}";
                return Task.FromResult(42);
            }, _ => FailLoad ? throw new InvalidOperationException("Read failed") : Task.FromResult(Clone(Stored!)));
        }
    }
}
