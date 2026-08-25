using ApiModels;
using Metagen.Shared.Services;

namespace MetaGrow.Web.Services;

/// <summary>
/// Accepts cookie-authenticated DxUpload requests and forwards banana survey images to TGS.
/// </summary>
public static class BananaPhotoEndpoints
{
    private const long MaxPhotoBytes = 20 * 1024 * 1024;

    public static IEndpointConventionBuilder MapBananaPhotoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/uploads/surveys/banana/{surveyId:int}/photos", async (
            int surveyId,
            int blockId,
            int plantNo,
            int displayOrder,
            string? comments,
            IFormFile file,
            ITgsApiService tgsApi) =>
        {
            if (surveyId <= 0 || blockId <= 0)
                return Results.BadRequest(new { message = "A survey and block are required." });

            var extension = Path.GetExtension(file.FileName);
            if (!extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(new { message = "Only JPEG images can be uploaded." });

            if (file.Length <= 0 || file.Length > MaxPhotoBytes)
                return Results.BadRequest(new { message = "The JPEG must be between 1 byte and 20 MB." });

            var fileNameNoExtension = $"bs{DateTime.Now:yyyyMMddHHmmss}";
            var storedFileName = $"{surveyId}.{fileNameNoExtension}.jpg";
            var uploadName = $"{Path.GetFileNameWithoutExtension(file.FileName)}.jpg";

            await using var stream = file.OpenReadStream();
            if (!await tgsApi.UploadBananaSurveyPhoto(
                    stream,
                    uploadName,
                    "image/jpeg",
                    surveyId,
                    fileNameNoExtension))
                return Results.Problem(tgsApi.ErrorMessage ?? "The image file could not be uploaded.");

            var created = await tgsApi.CreateBananaSurveyPhoto(new SurveyPhoto
            {
                SurveyId = surveyId,
                BlockId = blockId,
                PlantNo = Math.Max(1, plantNo),
                FileName = storedFileName,
                Comments = comments?.Trim(),
                DisplayOrder = Math.Max(1, displayOrder)
            });

            return created == null
                ? Results.Problem(tgsApi.ErrorMessage ?? "The image record could not be added to the survey.")
                : Results.Ok(created);
        }).RequireAuthorization().DisableAntiforgery();
    }
}
