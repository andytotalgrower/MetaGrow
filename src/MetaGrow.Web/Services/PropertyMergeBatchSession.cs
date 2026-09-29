using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Shared;
using Metagen.Shared.Services;

namespace MetaGrow.Web.Services;

public interface IPropertyMergeBatchGateway
{
    Task<(PropertyMergePreview? Preview, string? Error)> PreviewAsync(PropertyMergePreviewRequest plan);
    Task<(MetaGrowPropertyMergeRequestDto? Result, string? Error)> ExecuteAsync(ReviewedPropertyMergeRequest request);
    Task<(MetaGrowPropertyMergeRequestDto[]? Results, string? Error)> StatusAsync(Guid requestId);
}

public sealed class PropertyMergeBatchGateway(ITgsApiService tgsApi, PropertyMergeApiClient merges) : IPropertyMergeBatchGateway
{
    public async Task<(PropertyMergePreview?, string?)> PreviewAsync(PropertyMergePreviewRequest plan)
    {
        var preview = await tgsApi.GetPropertyMergePreview(plan);
        return (preview, preview is null ? tgsApi.ErrorMessage : null);
    }

    public Task<(MetaGrowPropertyMergeRequestDto?, string?)> ExecuteAsync(ReviewedPropertyMergeRequest request) =>
        merges.ExecuteReviewedAsync(request);

    public Task<(MetaGrowPropertyMergeRequestDto[]?, string?)> StatusAsync(Guid requestId) =>
        merges.GetExecutionStatusAsync(requestId);
}

public enum BatchMergeState { Selected, Ready, NeedsReview, Merging, Completed, VerifyResult }

public sealed class BatchMergeItem(int sourceId, string name)
{
    public int SourceId { get; } = sourceId;
    public string Name { get; } = name;
    public Guid RequestId { get; internal set; } = Guid.NewGuid();
    public BatchMergeState State { get; internal set; }
    public PropertyMergePreview? Preview { get; internal set; }
    public string? ReviewedHash { get; internal set; }
    public string? Message { get; internal set; }
    public PropertyMergePreviewRequest? Plan => Preview is null ? null : PropertyMergeBatchReview.Plan(Preview);
}

/// <summary>One fixed destination; each submitted merge retains its own execution receipt.</summary>
public sealed class PropertyMergeBatchSession(int targetId, IPropertyMergeBatchGateway gateway)
{
    private readonly List<BatchMergeItem> _items = [];
    private bool _stopped;
    public int TargetId { get; } = targetId;
    public IReadOnlyList<BatchMergeItem> Items => _items;
    public bool Running { get; private set; }
    public bool CanRun => !Running && _items.Any(item => item.State == BatchMergeState.Ready) &&
        _items.All(item => item.State is BatchMergeState.Ready or BatchMergeState.Completed);

    public void Select(int sourceId, string name, bool selected)
    {
        if (Running || sourceId <= 0 || sourceId == TargetId) return;
        var item = _items.FirstOrDefault(item => item.SourceId == sourceId);
        if (selected && item is null) _items.Add(new(sourceId, name));
        else if (!selected && item is not null && item.State is not (BatchMergeState.Completed or BatchMergeState.VerifyResult))
            _items.Remove(item);
    }

    public void Stop() => _stopped = true;

    public async Task PrepareAsync(BatchMergeItem item)
    {
        if (Running || !_items.Contains(item) || item.State is BatchMergeState.Completed or BatchMergeState.VerifyResult) return;
        try
        {
            var (preview, error) = await gateway.PreviewAsync(item.Plan ?? PropertyMergeBatchReview.InitialPlan(item.SourceId, TargetId));
            if (preview is null) { NeedsReview(item, error ?? "The live merge check could not be loaded."); return; }
            AcceptReview(item, preview);
        }
        catch (Exception exception) { NeedsReview(item, "The live merge check failed. " + exception.Message); }
    }

    public void AcceptReview(BatchMergeItem item, PropertyMergePreview preview)
    {
        if (Running || !_items.Contains(item) || item.State is BatchMergeState.Completed or BatchMergeState.VerifyResult) return;
        ValidateIdentity(item, preview);
        item.Preview = preview;
        item.ReviewedHash = PropertyMergeBatchReview.Fingerprint(preview);
        item.State = preview.CanRequestMerge ? BatchMergeState.Ready : BatchMergeState.NeedsReview;
        item.Message = preview.CanRequestMerge ? null : CollisionMessage(preview);
    }

    public async Task RunAsync(Func<Task> changed)
    {
        if (!CanRun || _stopped) return;
        Running = true;
        try
        {
            foreach (var item in _items.Where(item => item.State != BatchMergeState.Completed))
            {
                if (_stopped) break;
                var submitted = false;
                try
                {
                    item.State = BatchMergeState.Merging;
                    item.Message = "Checking current farm data…";
                    await changed();
                    var (live, error) = await gateway.PreviewAsync(item.Plan!);
                    if (_stopped) { item.State = BatchMergeState.Ready; break; }
                    if (live is null) { NeedsReview(item, error ?? "The live merge check failed."); break; }
                    ValidateIdentity(item, live);
                    if (!live.CanRequestMerge || PropertyMergeBatchReview.Fingerprint(live) != item.ReviewedHash)
                    {
                        item.Preview = live;
                        NeedsReview(item, live.CanRequestMerge
                            ? "Farm fields or block matches changed. Review this farm again before continuing."
                            : CollisionMessage(live));
                        break;
                    }

                    item.Message = "Merging into the selected destination…";
                    await changed();
                    if (_stopped) { item.State = BatchMergeState.Ready; break; }
                    submitted = true;
                    var (result, executeError) = await gateway.ExecuteAsync(new ReviewedPropertyMergeRequest
                    {
                        RequestId = item.RequestId, Plan = item.Plan!, PreviewHash = item.ReviewedHash!
                    });
                    if (result is not null && IsCompleted(item, result))
                    {
                        item.State = BatchMergeState.Completed;
                        item.Message = "Merge completed.";
                    }
                    else
                    {
                        item.State = BatchMergeState.VerifyResult;
                        item.Message = (executeError ?? "Completion was not confirmed.") + " Check the result before continuing.";
                        break;
                    }
                }
                catch (Exception exception)
                {
                    item.State = submitted ? BatchMergeState.VerifyResult : BatchMergeState.NeedsReview;
                    item.Message = submitted
                        ? "The connection was interrupted. Check the result before retrying."
                        : "The live merge check failed. " + exception.Message;
                    break;
                }
                finally { await changed(); }
            }
        }
        finally { Running = false; await changed(); }
    }

    public async Task VerifyAsync(BatchMergeItem item)
    {
        if (Running || item.State != BatchMergeState.VerifyResult) return;
        try
        {
            var (results, error) = await gateway.StatusAsync(item.RequestId);
            if (results is null) { item.Message = error ?? "The merge result could not be checked."; return; }
            var result = results.SingleOrDefault();
            if (result is not null && IsCompleted(item, result))
            {
                item.State = BatchMergeState.Completed;
                item.Message = "Merge completed.";
            }
            else if (result is null || result.Status is MetaGrowPropertyMergeStatus.Failed or MetaGrowPropertyMergeStatus.Rejected)
            {
                // Keep the same ID when the first request may still be arriving at the server.
                if (result is not null) item.RequestId = Guid.NewGuid();
                NeedsReview(item, "Review the live data again before retrying. " + result?.LastError);
            }
            else item.Message = "This merge is still being processed. Check the result again shortly.";
        }
        catch (Exception) { item.Message = "The merge result could not be checked. Try checking again shortly."; }
    }

    private bool IsCompleted(BatchMergeItem item, MetaGrowPropertyMergeRequestDto result) =>
        result.Id == item.RequestId && result.SourcePropertyId == item.SourceId &&
        result.TargetPropertyId == TargetId && result.Status == MetaGrowPropertyMergeStatus.Completed;

    private void ValidateIdentity(BatchMergeItem item, PropertyMergePreview preview)
    {
        if (preview.Source.PropertyId != item.SourceId || preview.Target.PropertyId != TargetId ||
            preview.FieldDifferences.Any(field => field.FieldName == "PropertyName" && field.ValueSource != PropertyMergeFieldValue.Target))
            throw new InvalidOperationException("The merge must preserve the selected destination and its name.");
    }

    private static void NeedsReview(BatchMergeItem item, string message)
    {
        item.State = BatchMergeState.NeedsReview;
        item.ReviewedHash = null;
        item.Message = message;
    }

    private static string CollisionMessage(PropertyMergePreview preview) =>
        preview.PropertyCollisions.FirstOrDefault(collision => collision.IsBlocking)?.Message ??
        "Review the block decisions and resolve any collisions before merging.";
}
