using ApiModels;
using MetaGrow.Shared;

namespace MetaGrow.Web.Tests;

public sealed class SampleSoilResultFilterTests
{
    [Fact]
    public void Apply_FiltersCanonicalAndRawValuesAndMappingState()
    {
        SampleSoilResultGroupDto[] groups =
        [
            new()
            {
                SurveyId = 1,
                PropertyName = "Rieck Farming Kalbar",
                RawCustomerName = "RIECK FARMING KALBAR",
                AgronomistId = 10,
                LabId = 2,
                ResultCount = 1,
                MappedResultCount = 1,
                DateReceived = new DateTime(2026, 7, 29),
                Results = [new() { OrderNo = "130715851", RawFieldReference = "Paddock 1" }]
            },
            new()
            {
                SurveyId = 2,
                PropertyName = "Devaney Bananas",
                RawCustomerName = "Devany",
                AgronomistId = 20,
                LabId = 2,
                ResultCount = 2,
                MappedResultCount = 1,
                DateReceived = new DateTime(2026, 7, 21),
                Results = [new() { OrderNo = "130665776", RawFieldReference = "North" }]
            }
        ];

        Assert.Equal([1], SampleSoilResultFilter.Apply(groups, "rieck", "RIECK", 10, 2, "5851", true).Select(group => group.SurveyId));
        Assert.Equal([2], SampleSoilResultFilter.Apply(groups, null, "devany", null, null, "north", false).Select(group => group.SurveyId));
    }
}
