using System.Net.Http.Headers;
using ApiModels;
using ApiModels.MetaGrow;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Authorize(Roles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager)]
[Route("block-merges")]
public sealed class BlockMergesController(ITgsApiService tgsApi) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<ActionResult<MultiCropBlockMergePreview>> Preview(MultiCropBlockMergePlan plan)
    {
        if (!TryToken(out var token)) return Forbid();
        if (plan is null || !plan.IsValid) return BadRequest(Error("Select two different multi-crop blocks within a farm."));
        return Reply(await tgsApi.PreviewMultiCropBlockMerge(plan, token));
    }

    [HttpPost("execute")]
    public async Task<ActionResult<MultiCropBlockMergeStatus>> Execute(MultiCropBlockMergeExecution execution)
    {
        if (!TryToken(out var token)) return Forbid();
        if (execution is null || execution.Plan is null || !execution.Plan.IsValid || execution.RequestId == Guid.Empty ||
            execution.Fingerprint is not { Length: 64 } || !execution.Fingerprint.All(Uri.IsHexDigit))
            return BadRequest(Error("A complete, reviewed block merge is required."));
        return Reply(await tgsApi.ExecuteMultiCropBlockMerge(execution, token));
    }

    [HttpGet("{requestId:guid}/status")]
    public async Task<ActionResult<MultiCropBlockMergeStatus>> Status(Guid requestId)
    {
        if (!TryToken(out var token)) return Forbid();
        if (requestId == Guid.Empty) return BadRequest(Error("A merge request ID is required."));
        return Reply(await tgsApi.GetMultiCropBlockMergeStatus(requestId, token));
    }

    private ActionResult<T> Reply<T>(T? value) where T : class => value is not null ? Ok(value) :
        Conflict(Error(tgsApi.ErrorMessage ?? "Block merging is unavailable. Check request status before retrying."));

    private bool TryToken(out string token)
    {
        token = "";
        if (!User.IsInRole(MetaGrowRoles.Admin) && !User.IsInRole(MetaGrowRoles.AgricultureManager)) return false;
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) ||
            !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(header.Parameter)) return false;
        token = header.Parameter;
        return true;
    }

    private static MetaGrowAuthError Error(string message) => new() { Errors = [message] };
}
