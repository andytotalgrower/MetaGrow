using System.Reflection;
using System.Security.Claims;
using ApiModels;
using ApiModels.MetaGrow;
using MetaGrow.Api.Controllers;
using Metagen.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MetaGrow.Api.Tests;

public class BlockMergeApiTests
{
    [Fact]
    public async Task Agronomist_cannot_preview_execute_or_inspect_merge_receipts()
    {
        var (controller, api) = Create(MetaGrowRoles.Agronomist);
        Assert.IsType<ForbidResult>((await controller.Preview(Plan())).Result);
        Assert.IsType<ForbidResult>((await controller.Execute(Execution())).Result);
        Assert.IsType<ForbidResult>((await controller.Status(Guid.NewGuid())).Result);
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task Same_block_and_missing_review_are_rejected_before_calling_tgs()
    {
        var (controller, api) = Create(MetaGrowRoles.Admin);
        Assert.IsType<BadRequestObjectResult>((await controller.Preview(new() { PropertyId = 1, SourceBlockId = 2, TargetBlockId = 2 })).Result);
        var execution = Execution();
        execution.Fingerprint = "";
        Assert.IsType<BadRequestObjectResult>((await controller.Execute(execution)).Result);
        Assert.Empty(api.Calls);
    }

    [Theory]
    [InlineData(MetaGrowRoles.Admin)]
    [InlineData(MetaGrowRoles.AgricultureManager)]
    public async Task Reviewer_identity_token_and_same_request_are_forwarded_without_a_second_preview(string role)
    {
        var (controller, api) = Create(role);
        var execution = Execution();
        Assert.IsType<OkObjectResult>((await controller.Execute(execution)).Result);
        Assert.Equal([nameof(ITgsApiService.ExecuteMultiCropBlockMerge)], api.Calls);
        Assert.Same(execution, api.Execution);
        Assert.Equal("user-token", api.Token);
    }

    [Fact]
    public async Task Unavailable_status_is_not_reported_as_not_found_or_failed()
    {
        var (controller, api) = Create(MetaGrowRoles.Admin);
        api.Unavailable = true;
        Assert.IsType<ConflictObjectResult>((await controller.Status(Guid.NewGuid())).Result);
    }

    private static MultiCropBlockMergePlan Plan() => new() { PropertyId = 1, SourceBlockId = 2, TargetBlockId = 3 };
    private static MultiCropBlockMergeExecution Execution() => new() { RequestId = Guid.NewGuid(), Plan = Plan(), Fingerprint = new('A', 64) };
    private static (BlockMergesController, FakeTgs) Create(string role)
    {
        var service = DispatchProxy.Create<ITgsApiService, FakeTgs>();
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test")) };
        context.Request.Headers.Authorization = "Bearer user-token";
        return (new(service) { ControllerContext = new() { HttpContext = context } }, (FakeTgs)service);
    }

    public class FakeTgs : DispatchProxy
    {
        public List<string> Calls { get; } = [];
        public MultiCropBlockMergeExecution? Execution { get; private set; }
        public string? Token { get; private set; }
        public bool Unavailable { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "get_ErrorMessage") return "Unavailable";
            Calls.Add(method.Name);
            Token = (string)args![1]!;
            if (method.Name == nameof(ITgsApiService.ExecuteMultiCropBlockMerge))
            {
                Execution = (MultiCropBlockMergeExecution)args[0]!;
                return Task.FromResult<MultiCropBlockMergeStatus?>(new() { RequestId = Execution.RequestId, Status = "Completed" });
            }
            if (method.Name == nameof(ITgsApiService.GetMultiCropBlockMergeStatus))
                return Task.FromResult<MultiCropBlockMergeStatus?>(Unavailable ? null : new() { RequestId = (Guid)args[0]!, Status = "NotFound" });
            throw new InvalidOperationException(method.Name);
        }
    }
}
