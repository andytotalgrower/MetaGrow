using System.Net.Http.Headers;
using System.Security.Claims;
using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Route("sample-survey-deletions")]
public sealed class SampleSurveyDeletionsController(
    ApplicationDbContext database,
    ITgsApiService tgsApi,
    ILogger<SampleSurveyDeletionsController> logger) : ControllerBase
{
    private const string StaffRoles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager + "," +
        MetaGrowRoles.Agronomist + "," + MetaGrowRoles.Accountant;
    private const string ReviewerRoles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager + "," +
        MetaGrowRoles.Accountant;

    [Authorize(Roles = StaffRoles)]
    [HttpGet("pending")]
    public async Task<ActionResult<MetaGrowSampleSurveyDeletionDto[]>> GetPending()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Forbid();
        var query = database.SampleSurveyDeletionRequests.AsNoTracking()
            .Where(request => request.Status == MetaGrowSampleSurveyDeletionStatus.Pending ||
                              request.Status == MetaGrowSampleSurveyDeletionStatus.Processing);
        if (!IsReviewer()) query = query.Where(request => request.RequestedByUserId == userId);
        return Ok((await query.OrderBy(request => request.RequestedUtc).ToListAsync())
            .Select(request => ToDto(request, request.RequestedByUserId == userId))
            .ToArray());
    }

    [Authorize(Roles = StaffRoles)]
    [HttpGet("preview/{surveyType}/{surveyId:int}")]
    public async Task<ActionResult<SampleSurveyDeletionPreviewDto>> Preview(
        MetaGrowSurveyType surveyType,
        int surveyId)
    {
        var token = AccessToken();
        if (token is null) return Forbid();
        var preview = await GetPreviewAsync(surveyType, surveyId, token);
        return preview is null
            ? StatusCode(StatusCodes.Status502BadGateway, Error(tgsApi.ErrorMessage ?? "The deletion preview could not be loaded."))
            : Ok(preview);
    }

    [Authorize(Roles = StaffRoles)]
    [HttpPost]
    public async Task<ActionResult<MetaGrowSampleSurveyDeletionDto>> Create(
        MetaGrowSampleSurveyDeletionCreateRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var token = AccessToken();
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email) || token is null) return Forbid();

        var preview = await GetPreviewAsync(request.SurveyType, request.SurveyId, token);
        if (preview is null)
            return StatusCode(StatusCodes.Status502BadGateway,
                Error(tgsApi.ErrorMessage ?? "The deletion safety check could not be completed."));
        if (!preview.CanDelete)
            return Conflict(Error(preview.BlockReason ?? "This Sample survey is not eligible for deletion."));

        var exists = await database.SampleSurveyDeletionRequests.AsNoTracking().AnyAsync(item =>
            item.SurveyType == request.SurveyType &&
            item.SurveyId == request.SurveyId &&
            (item.Status == MetaGrowSampleSurveyDeletionStatus.Pending ||
             item.Status == MetaGrowSampleSurveyDeletionStatus.Processing));
        if (exists) return Conflict(Error("A deletion request is already waiting for this Sample survey."));

        var deletion = new SampleSurveyDeletionRequest
        {
            Id = Guid.NewGuid(),
            SurveyType = request.SurveyType,
            SurveyId = preview.SurveyId,
            PropertyId = preview.PropertyId,
            PropertyName = preview.PropertyName,
            SurveyDate = preview.SurveyDate,
            ExpectedModificationDate = preview.ModificationDate,
            SampleCount = preview.SampleCount,
            PhotoCount = preview.PhotoCount,
            ActionCount = preview.ActionCount,
            LinkedSoilResultCount = preview.LinkedSoilResultCount,
            LinkedTissueResultCount = preview.LinkedTissueResultCount,
            LinkedSapResultCount = preview.LinkedSapResultCount,
            LinkedQuickSoilResultCount = preview.LinkedQuickSoilResultCount,
            LinkedLegacyResultCount = preview.LinkedLegacyResultCount,
            DeleteLinkedLabResults = request.DeleteLinkedLabResults,
            Status = MetaGrowSampleSurveyDeletionStatus.Pending,
            RequestedByUserId = userId,
            RequestedByEmail = email,
            RequestedUtc = DateTime.UtcNow
        };
        database.SampleSurveyDeletionRequests.Add(deletion);
        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateException exception)
        {
            logger.LogWarning(exception, "Duplicate Sample survey deletion request for {SurveyId}", request.SurveyId);
            return Conflict(Error("A deletion request is already waiting for this Sample survey."));
        }
        return Ok(ToDto(deletion));
    }

    [Authorize(Roles = ReviewerRoles)]
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<MetaGrowSampleSurveyDeletionDto>> Approve(
        Guid id,
        MetaGrowSampleSurveyDeletionReviewRequest request)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        var token = AccessToken();
        if (string.IsNullOrWhiteSpace(reviewerId) || string.IsNullOrWhiteSpace(reviewerEmail) || token is null)
            return Forbid();

        var canApproveOwnRequest = CanApproveOwnRequest();
        var claimed = await database.SampleSurveyDeletionRequests
            .Where(item => item.Id == id &&
                           item.Status == MetaGrowSampleSurveyDeletionStatus.Pending &&
                           (canApproveOwnRequest || item.RequestedByUserId != reviewerId))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, MetaGrowSampleSurveyDeletionStatus.Processing)
                .SetProperty(item => item.ReviewedByUserId, reviewerId)
                .SetProperty(item => item.ReviewedByEmail, reviewerEmail)
                .SetProperty(item => item.ReviewedUtc, DateTime.UtcNow)
                .SetProperty(item => item.ReviewNote, Clean(request.Note))
                .SetProperty(item => item.LastError, (string?)null));
        if (claimed == 0)
            return Conflict(Error("This request is no longer awaiting approval, or your role requires another reviewer."));

        var deletion = await database.SampleSurveyDeletionRequests.SingleAsync(item => item.Id == id);
        var result = await tgsApi.DeleteSampleSurveyForMetaGrow(
            new SampleSurveyDeletionExecutionRequest { ApprovalRequestId = id },
            token);
        if (result is null)
        {
            deletion.Status = MetaGrowSampleSurveyDeletionStatus.Pending;
            deletion.ReviewedByUserId = null;
            deletion.ReviewedByEmail = null;
            deletion.ReviewedUtc = null;
            deletion.LastError = Truncate(tgsApi.ErrorMessage ?? "The Sample survey could not be deleted.", 1000);
            await database.SaveChangesAsync();
            return Conflict(Error(deletion.LastError));
        }

        deletion.Status = MetaGrowSampleSurveyDeletionStatus.Completed;
        deletion.LastError = null;
        await database.SaveChangesAsync();
        return Ok(ToDto(deletion));
    }

    [Authorize(Roles = StaffRoles)]
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<MetaGrowSampleSurveyDeletionDto>> Cancel(Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)) return Forbid();

        var cancelled = await database.SampleSurveyDeletionRequests
            .Where(item => item.Id == id &&
                           item.Status == MetaGrowSampleSurveyDeletionStatus.Pending &&
                           item.RequestedByUserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, MetaGrowSampleSurveyDeletionStatus.Cancelled)
                .SetProperty(item => item.ReviewedByUserId, userId)
                .SetProperty(item => item.ReviewedByEmail, email)
                .SetProperty(item => item.ReviewedUtc, DateTime.UtcNow)
                .SetProperty(item => item.ReviewNote, "Cancelled by requester.")
                .SetProperty(item => item.LastError, (string?)null));
        if (cancelled == 0)
            return Conflict(Error("This request is no longer awaiting approval, or it was requested by another user."));

        var deletion = await database.SampleSurveyDeletionRequests.AsNoTracking()
            .SingleAsync(item => item.Id == id);
        return Ok(ToDto(deletion));
    }

    [Authorize(Roles = ReviewerRoles)]
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<MetaGrowSampleSurveyDeletionDto>> Reject(
        Guid id,
        MetaGrowSampleSurveyDeletionReviewRequest request)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(reviewerId) || string.IsNullOrWhiteSpace(reviewerEmail)) return Forbid();
        var deletion = await database.SampleSurveyDeletionRequests.SingleOrDefaultAsync(item =>
            item.Id == id && item.Status == MetaGrowSampleSurveyDeletionStatus.Pending);
        if (deletion is null) return Conflict(Error("This request is no longer awaiting approval."));
        deletion.Status = MetaGrowSampleSurveyDeletionStatus.Rejected;
        deletion.ReviewedByUserId = reviewerId;
        deletion.ReviewedByEmail = reviewerEmail;
        deletion.ReviewedUtc = DateTime.UtcNow;
        deletion.ReviewNote = Clean(request.Note);
        deletion.LastError = null;
        await database.SaveChangesAsync();
        return Ok(ToDto(deletion));
    }

    /// <summary>
    /// Called by TgsApi.Core with the reviewer's token. A grant exists only while this exact
    /// reviewer owns the processing request, so a role token or guessed request ID is insufficient.
    /// </summary>
    [Authorize(Roles = ReviewerRoles)]
    [HttpGet("{id:guid}/execution-grant")]
    public async Task<ActionResult<MetaGrowSampleSurveyDeletionExecutionGrant>> GetExecutionGrant(Guid id)
    {
        var reviewerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var reviewerEmail = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(reviewerId) || string.IsNullOrWhiteSpace(reviewerEmail)) return Forbid();
        var deletion = await database.SampleSurveyDeletionRequests.AsNoTracking().SingleOrDefaultAsync(item =>
            item.Id == id &&
            item.Status == MetaGrowSampleSurveyDeletionStatus.Processing &&
            item.ReviewedByUserId == reviewerId);
        if (deletion is null) return NotFound();
        return Ok(new MetaGrowSampleSurveyDeletionExecutionGrant
        {
            ApprovalRequestId = deletion.Id,
            SurveyType = deletion.SurveyType,
            SurveyId = deletion.SurveyId,
            ExpectedModificationDate = deletion.ExpectedModificationDate,
            ExpectedSampleCount = deletion.SampleCount,
            ExpectedLinkedSoilResultCount = deletion.LinkedSoilResultCount,
            ExpectedLinkedTissueResultCount = deletion.LinkedTissueResultCount,
            ExpectedLinkedSapResultCount = deletion.LinkedSapResultCount,
            ExpectedLinkedQuickSoilResultCount = deletion.LinkedQuickSoilResultCount,
            ExpectedLinkedLegacyResultCount = deletion.LinkedLegacyResultCount,
            DeleteLinkedLabResults = deletion.DeleteLinkedLabResults,
            ApprovedByUserId = reviewerId,
            ApprovedByEmail = reviewerEmail
        });
    }

    private bool IsReviewer() => User.IsInRole(MetaGrowRoles.Admin) ||
        User.IsInRole(MetaGrowRoles.AgricultureManager) || User.IsInRole(MetaGrowRoles.Accountant);
    private bool CanApproveOwnRequest() => User.IsInRole(MetaGrowRoles.Admin) ||
        User.IsInRole(MetaGrowRoles.AgricultureManager);
    private string? AccessToken() =>
        AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authorization) &&
        authorization.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            ? authorization.Parameter
            : null;
    private static MetaGrowAuthError Error(string message) => new() { Errors = [message] };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
    private async Task<SampleSurveyDeletionPreviewDto?> GetPreviewAsync(
        MetaGrowSurveyType surveyType,
        int surveyId,
        string token)
    {
        if (surveyType == MetaGrowSurveyType.Sample)
        {
            var sample = await tgsApi.GetSampleSurveyDeletionPreview(surveyId, token);
            if (sample is not null) sample.SurveyType = surveyType;
            return sample;
        }

        if (surveyType == MetaGrowSurveyType.Banana)
        {
            var editor = await tgsApi.GetBananaSurveyEditor(surveyId);
            return editor is null ? null : new SampleSurveyDeletionPreviewDto
            {
                SurveyType = surveyType,
                SurveyId = surveyId,
                PropertyId = editor.Survey.PropertyId,
                PropertyName = editor.Survey.PropertyName ?? string.Empty,
                SurveyDate = editor.Survey.SurveyDate,
                ModificationDate = editor.Survey.ModificationDate,
                SampleCount = editor.LeafCount,
                PhotoCount = editor.PhotoCount,
                ActionCount = editor.Recommendations.Count,
                CanDelete = true
            };
        }

        var editorMulti = await tgsApi.GetMultiCropSurveyEditor(surveyId);
        return editorMulti is null ? null : new SampleSurveyDeletionPreviewDto
        {
            SurveyType = surveyType,
            SurveyId = surveyId,
            PropertyId = editorMulti.Survey.PropertyId,
            PropertyName = editorMulti.Survey.PropertyName ?? string.Empty,
            SurveyDate = editorMulti.Survey.SurveyDate,
            ModificationDate = editorMulti.Survey.ModificationDate,
            SampleCount = editorMulti.Blocks.Count,
            PhotoCount = editorMulti.Survey.CountPhotos,
            ActionCount = editorMulti.Recommendations.Count,
            CanDelete = true
        };
    }
    private static MetaGrowSampleSurveyDeletionDto ToDto(
        SampleSurveyDeletionRequest request,
        bool isRequestedByCurrentUser = false) => new()
    {
        Id = request.Id,
        SurveyType = request.SurveyType,
        SurveyId = request.SurveyId,
        PropertyId = request.PropertyId,
        PropertyName = request.PropertyName,
        SurveyDate = request.SurveyDate,
        SampleCount = request.SampleCount,
        PhotoCount = request.PhotoCount,
        ActionCount = request.ActionCount,
        LinkedSoilResultCount = request.LinkedSoilResultCount,
        LinkedTissueResultCount = request.LinkedTissueResultCount,
        LinkedSapResultCount = request.LinkedSapResultCount,
        LinkedQuickSoilResultCount = request.LinkedQuickSoilResultCount,
        LinkedLegacyResultCount = request.LinkedLegacyResultCount,
        DeleteLinkedLabResults = request.DeleteLinkedLabResults,
        Status = request.Status,
        RequestedByEmail = request.RequestedByEmail,
        RequestedUtc = request.RequestedUtc,
        ReviewedByEmail = request.ReviewedByEmail,
        ReviewedUtc = request.ReviewedUtc,
        ReviewNote = request.ReviewNote,
        LastError = request.LastError,
        IsRequestedByCurrentUser = isRequestedByCurrentUser
    };
}
