using System.Text.Json;
using ApiModels.MetaGrow;
using MetaGrow.Web.Models;

namespace MetaGrow.Web.Services;

/// <summary>Owns the saved version separately from unfinished edits, so confirmed deletion is immediate and atomic.</summary>
public sealed class SoilTemplateEditorSession
{
    private readonly Func<SoilTemplateDetail, Task<int>> _save;
    private readonly Func<int, Task<SoilTemplateDetail>> _load;
    private SoilTemplateDetail? _saved;
    private string _savedSnapshot = "";

    public SoilTemplateDetail Draft { get; private set; }
    public List<SoilTemplateTargetEditor> Targets { get; private set; }
    public bool IsSaving { get; private set; }
    public bool NeedsReload { get; private set; }
    public bool HasChanges => Snapshot(Draft, Targets) != _savedSnapshot;

    public SoilTemplateEditorSession(SoilTemplateDetail detail, Func<SoilTemplateDetail, Task<int>> save,
        Func<int, Task<SoilTemplateDetail>> load)
    {
        _save = save;
        _load = load;
        Draft = Clone(detail);
        Targets = detail.Targets.Select(SoilTemplateTargetEditor.FromTarget).ToList();
        if (detail.TemplateId > 0) AcceptSaved(detail);
    }

    public List<string> ValidationErrors()
    {
        // Never coerce an empty numeric editor to zero just to satisfy a NOT NULL database column.
        if (Targets.Any(target => !target.IsComplete)) return ["Enter a minimum, target and maximum for every test item."];
        return SoilTemplateRules.Validate(Candidate(), _saved?.Targets);
    }

    public async Task<bool> SaveAsync()
    {
        if (!HasChanges) return true;
        if (ValidationErrors().Count > 0) return false;
        await Persist(Candidate());
        return true;
    }

    public async Task AddTargetAsync(SoilTemplateTargetEditor target)
    {
        // The popup is separate from the grid: no incomplete or duplicate placeholder is ever inserted.
        var candidate = Candidate();
        candidate.Targets.Add(target.ToTarget());
        await Persist(candidate);
    }

    public async Task DeleteTargetAsync(int targetId)
    {
        // A copy has not been persisted yet; removing its duplicate rows makes it eligible for creation.
        if (_saved is null && Draft.TemplateId == 0)
        {
            Targets.RemoveAll(target => target.TargetId == targetId);
            return;
        }
        if (_saved is null || !_saved.Targets.Any(target => target.TargetId == targetId))
            throw new InvalidOperationException("Reload the template before removing this target.");
        var candidate = Clone(_saved);
        candidate.Targets.RemoveAll(target => target.TargetId == targetId);
        // Delete from the last confirmed version, retaining unrelated unfinished edits in the editor.
        await Persist(candidate, targetId);
    }

    private async Task Persist(SoilTemplateDetail candidate, int? deletedTargetId = null)
    {
        if (IsSaving || NeedsReload) throw new InvalidOperationException("Wait for saving to finish, or reload the template.");
        var errors = SoilTemplateRules.Validate(candidate, _saved?.Targets);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        IsSaving = true;
        try
        {
            var id = await _save(candidate);
            if (id <= 0) throw new InvalidOperationException("The save could not be confirmed.");
            // Keep the returned identity even if the follow-up read fails. Never create the same draft twice.
            Draft.TemplateId = id;
            Draft.SourceTemplateId = 0;
            var saved = await _load(id);
            AcceptSaved(saved);
            if (deletedTargetId.HasValue)
            {
                Draft.Version = saved.Version;
                Targets.RemoveAll(target => target.TargetId == deletedTargetId.Value);
            }
            else
            {
                Draft = Clone(saved);
                Targets = saved.Targets.Select(SoilTemplateTargetEditor.FromTarget).ToList();
            }
        }
        catch
        {
            // A failed response can hide a committed write. Stop automatic retries until the user reloads.
            NeedsReload = true;
            throw;
        }
        finally { IsSaving = false; }
    }

    private void AcceptSaved(SoilTemplateDetail saved)
    {
        _saved = Clone(saved);
        _savedSnapshot = Snapshot(saved, saved.Targets.Select(SoilTemplateTargetEditor.FromTarget));
    }

    private SoilTemplateDetail Candidate()
    {
        var candidate = Clone(Draft);
        candidate.Targets = Targets.Select(target => target.ToTarget()).ToList();
        return candidate;
    }

    private static string Snapshot(SoilTemplateDetail header, IEnumerable<SoilTemplateTargetEditor> targets)
    {
        var copy = Clone(header);
        copy.Targets = []; // Target editors are authoritative, including blank numeric values.
        return JsonSerializer.Serialize(new { Header = copy, Targets = targets });
    }

    private static SoilTemplateDetail Clone(SoilTemplateDetail detail) =>
        JsonSerializer.Deserialize<SoilTemplateDetail>(JsonSerializer.Serialize(detail))!;
}
