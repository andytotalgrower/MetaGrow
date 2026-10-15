using ApiModels.MetaGrow;
using MetaGrow.Api.Data;
using MetaGrow.Shared.Models;

namespace MetaGrow.Api.Services.SoilReports;

public sealed partial class SoilReportService
{
    private static TissueReportAssembler.Scope TissueScope(SoilReportState state, SoilReportSource source) =>
        TissueReportAssembler.Prepare(source, Deserialize<List<SoilReportManualMatch>>(state.SampleMatchesJson),
            Deserialize<List<SoilReportNotSampled>>(state.NotSampledJson));

    private static SoilReportMatchingReview TissueMatchingReview(SoilReportState state, SoilReportSource source)
    {
        var review = TissueReportAssembler.Review(TissueScope(state, source), Deserialize<List<SoilReportManualMatch>>(state.TissueMatchesJson));
        review.Version = state.Version; review.CanEdit = state.Status is "Editing" or "Ready for QA";
        return review;
    }

    private static List<TissueReportCropChoice> TissueCrops(SoilReportState state, SoilReportSource source)
    {
        var scope = TissueScope(state, source);
        return TissueReportAssembler.Crops(source, scope,
            TissueReportAssembler.Review(scope, Deserialize<List<SoilReportManualMatch>>(state.TissueMatchesJson)));
    }
}
