using System.Reflection;
using System.Security.Claims;
using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Api.Controllers;
using MetaGrow.Api.Data;
using MetaGrow.Shared;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace MetaGrow.Api.Tests;

public class PropertyMergeBatchApiTests
{
    [Fact]
    public async Task Existing_single_merge_still_executes_without_a_batch_hash()
    {
        using var fixture = new Fixture();
        var result = await fixture.Controller.ExecuteImmediately(new() { Plan = new() { SourcePropertyId = 1, TargetPropertyId = 10 } });
        var dto = Assert.IsType<MetaGrowPropertyMergeRequestDto>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(MetaGrowPropertyMergeStatus.Completed, dto.Status);
        Assert.Single(fixture.Api.Executions);
    }

    [Fact]
    public async Task Changed_batch_preview_is_rejected_before_creating_or_executing_a_merge()
    {
        using var fixture = new Fixture();
        var request = fixture.Request();
        fixture.Api.Preview.Source.IncludedRowCount++;
        var result = await fixture.Controller.ExecuteReviewed(request);
        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Empty(fixture.Api.Executions);
        Assert.Empty(fixture.Database.PropertyMergeRequests);
    }

    [Fact]
    public async Task Completed_request_is_returned_when_same_batch_item_is_retried()
    {
        using var fixture = new Fixture();
        var request = fixture.Request();
        var first = await fixture.Controller.ExecuteReviewed(request);
        var second = await fixture.Controller.ExecuteReviewed(request);
        Assert.IsType<OkObjectResult>(first.Result);
        Assert.IsType<OkObjectResult>(second.Result);
        Assert.Single(fixture.Api.Executions);
        var status = await fixture.Controller.GetExecutionStatus(request.RequestId);
        var dto = Assert.Single(Assert.IsType<MetaGrowPropertyMergeRequestDto[]>(Assert.IsType<OkObjectResult>(status.Result).Value));
        Assert.Equal(MetaGrowPropertyMergeStatus.Completed, dto.Status);
    }

    [Fact]
    public async Task Existing_source_request_prevents_a_second_execution()
    {
        using var fixture = new Fixture();
        fixture.Database.PropertyMergeRequests.Add(new()
        {
            Id = Guid.NewGuid(), SourcePropertyId = 1, TargetPropertyId = 10,
            RequestedByUserId = "reviewer", RequestedByEmail = "reviewer@example.com",
            Status = MetaGrowPropertyMergeStatus.Processing, RequestedUtc = DateTime.UtcNow
        });
        await fixture.Database.SaveChangesAsync();
        var result = await fixture.Controller.ExecuteReviewed(fixture.Request());
        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Empty(fixture.Api.Executions);
    }

    [Fact]
    public async Task Batch_cannot_overwrite_destination_name_even_with_a_matching_hash()
    {
        using var fixture = new Fixture();
        fixture.Api.Preview.FieldDifferences = [new() { FieldName = "PropertyName", SourceValue = "Wrong", TargetValue = "Right", ValueSource = PropertyMergeFieldValue.Source }];
        var result = await fixture.Controller.ExecuteReviewed(fixture.Request());
        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Empty(fixture.Api.Executions);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public ApplicationDbContext Database { get; }
        public FakeTgs Api { get; }
        public PropertyMergesController Controller { get; }
        public Fixture()
        {
            _connection.Open();
            Database = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);
            Database.Database.EnsureCreated();
            var service = DispatchProxy.Create<ITgsApiService, FakeTgs>();
            Api = (FakeTgs)service;
            var http = new DefaultHttpContext();
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "reviewer"), new Claim(ClaimTypes.Email, "reviewer@example.com"),
                new Claim(ClaimTypes.Role, MetaGrowRoles.Admin)
            }, "test"));
            http.Request.Headers.Authorization = "Bearer test-token";
            Controller = new(Database, service, NullLogger<PropertyMergesController>.Instance)
            {
                ControllerContext = new() { HttpContext = http }
            };
        }
        public ReviewedPropertyMergeRequest Request() => new()
        {
            RequestId = Guid.NewGuid(), Plan = PropertyMergeBatchReview.Plan(Api.Preview),
            PreviewHash = PropertyMergeBatchReview.Fingerprint(Api.Preview)
        };
        public void Dispose() { Database.Dispose(); _connection.Dispose(); }
    }

    public class FakeTgs : DispatchProxy
    {
        public PropertyMergePreview Preview { get; } = new()
        {
            Source = new() { PropertyId = 1, PropertyName = "Variant" },
            Target = new() { PropertyId = 10, PropertyName = "Destination" }
        };
        public List<PropertyMergeExecutionRequest> Executions { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(ITgsApiService.GetPropertyMergePreview)) return Task.FromResult<PropertyMergePreview?>(Preview);
            if (method.Name == nameof(ITgsApiService.ExecutePropertyMerge))
            {
                var request = (PropertyMergeExecutionRequest)args![0]!;
                Executions.Add(request);
                return Task.FromResult<PropertyMergeExecutionResult?>(new()
                {
                    RequestId = request.RequestId, SourcePropertyId = request.Plan.SourcePropertyId,
                    TargetPropertyId = request.Plan.TargetPropertyId, SourcePropertyName = "Variant",
                    TargetPropertyName = "Destination", CompletedUtc = DateTime.UtcNow, PropertyMergeId = 123
                });
            }
            if (method.Name == "get_ErrorMessage") return null;
            throw new NotSupportedException(method.Name);
        }
    }
}
