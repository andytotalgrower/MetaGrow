using ApiModels;
using ApiModels.MetaGrow;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MetaGrow.Api.Controllers;

[ApiController]
[Route("multicrop")]
[Authorize(Roles = MetaGrowRoles.Admin + "," + MetaGrowRoles.AgricultureManager + "," + MetaGrowRoles.Agronomist)]
public sealed class MultiCropSurveysController(
    ITgsApiService tgsApi,
    ILogger<MultiCropSurveysController> logger) : ControllerBase
{
    [HttpGet("surveys")]
    [ProducesResponseType<IReadOnlyList<MultiCropSurveySummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MultiCropSurveySummaryDto>>> GetSurveys(
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        if (startDate == default || endDate == default)
            return BadRequest("startDate and endDate are required.");

        if (endDate.Date < startDate.Date)
            return BadRequest("endDate must be on or after startDate.");

        var surveys = await tgsApi.GetMultiCropSurveySummaries(startDate.Date, endDate.Date);
        if (surveys is null)
        {
            logger.LogError("TGS API failed to return multi-crop surveys: {Error}", tgsApi.ErrorMessage);
            return Problem("The multi-crop surveys could not be loaded from the TGS API.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Ok(surveys);
    }

    [HttpGet("survey-types")]
    [ProducesResponseType<IReadOnlyList<MultiCropSurveyTypeDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MultiCropSurveyTypeDto>>> GetSurveyTypes()
    {
        var surveyTypes = await tgsApi.GetMultiCropSurveyTypes();
        if (surveyTypes is null)
        {
            logger.LogError("TGS API failed to return multi-crop survey types: {Error}", tgsApi.ErrorMessage);
            return Problem("The multi-crop survey types could not be loaded from the TGS API.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Ok(surveyTypes);
    }
}
