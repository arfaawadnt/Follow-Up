using FollowUp.Application.Features.Compensation;
using FollowUp.Application.Features.Integration;
using FollowUp.Application.Features.LabStats;
using FollowUp.Application.Features.TestCatalogue;
using MediatR;

namespace FollowUp.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public sealed record SetTargetBody(Guid LaboratoryId, int MonthlyTarget);
    public sealed record SaveCommissionBody(Guid RepresentativeId, int Period);
    public sealed record SaveAllCommissionsBody(int Period);
    public sealed record ImportBody(byte[] Content);
    public sealed record IntegrationConfigBody(bool Enabled, int IntervalHours);
    public sealed record SyncStatsBody(DateOnly From, DateOnly To);
    // CPN-15: dedicated body + typed response shapes (no anonymous objects / command-as-wire-body).
    public sealed record CompensationConfigBody(decimal CommissionRatePercent, decimal BonusThresholdPercent,
        decimal BonusAmount, IReadOnlyList<LoyaltyTierInput> Tiers);
    public sealed record RecalculatedDto(int Recalculated);
    public sealed record SavedDto(int Saved);

    public static void MapCompensationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/loyalty", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetLoyaltyQuery(), ct))).WithTags("Loyalty")
            .Produces<IReadOnlyList<LoyaltyRowDto>>();
        api.MapGet("/loyalty/ledger/{labId:guid}", async (Guid labId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetLoyaltyLedgerQuery(labId), ct))).WithTags("Loyalty")
            .Produces<IReadOnlyList<LoyaltyLedgerDto>>();
        api.MapPost("/loyalty/target", async (SetTargetBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new SetLabTargetCommand(b.LaboratoryId, b.MonthlyTarget), ct); return Results.NoContent(); }).WithTags("Loyalty")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);
        api.MapPost("/loyalty/recalculate", async (IMediator m, CancellationToken ct) =>
        { var n = await m.Send(new RecalculateAllLoyaltyCommand(), ct); return Results.Ok(new RecalculatedDto(n)); }).WithTags("Loyalty")
            .Produces<RecalculatedDto>();

        api.MapGet("/commissions", async (int period, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetCommissionsQuery(period), ct))).WithTags("Commissions")
            .Produces<IReadOnlyList<CommissionDto>>();
        api.MapPost("/commissions/save", async (SaveCommissionBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new SaveCommissionCommand(b.RepresentativeId, b.Period), ct); return Results.NoContent(); }).WithTags("Commissions")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);
        api.MapPost("/commissions/save-all", async (SaveAllCommissionsBody b, IMediator m, CancellationToken ct) =>
        { var n = await m.Send(new SaveAllCommissionsCommand(b.Period), ct); return Results.Ok(new SavedDto(n)); }).WithTags("Commissions")
            .Produces<SavedDto>();

        api.MapGet("/setup/compensation-config", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetCompensationConfigQuery(), ct))).WithTags("Compensation")
            .Produces<CompensationConfigDto>();
        api.MapPost("/setup/compensation-config", async (CompensationConfigBody b, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new SetCompensationConfigCommand(b.CommissionRatePercent, b.BonusThresholdPercent, b.BonusAmount, b.Tiers), ct);
            return Results.NoContent();
        }).WithTags("Compensation")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status400BadRequest);
    }

    public static void MapStatsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/labstats", async (DateOnly from, DateOnly to, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetLabStatsQuery(from, to), ct))).WithTags("LabStats");
        api.MapPost("/labstats/import", async (ImportBody b, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ImportLabStatsCommand(b.Content), ct))).WithTags("LabStats");
        api.MapPost("/labstats/sync", async (SyncStatsBody b, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SyncLabStatsCommand(b.From, b.To), ct))).WithTags("LabStats");

        api.MapGet("/test-groups", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetTestGroupsQuery(), ct))).WithTags("TestCatalogue");
        api.MapPost("/test-groups", async (CreateTestGroupCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/test-groups/{id}", new { id }); }).WithTags("TestCatalogue");
        api.MapPut("/test-groups/{id:guid}", async (Guid id, UpdateTestGroupCommand cmd, IMediator m, CancellationToken ct) =>
        { await m.Send(cmd with { Id = id }, ct); return Results.NoContent(); }).WithTags("TestCatalogue");
        api.MapDelete("/test-groups/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteTestGroupCommand(id), ct); return Results.NoContent(); }).WithTags("TestCatalogue");

        api.MapGet("/test-setups", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetTestSetupsQuery(), ct))).WithTags("TestCatalogue");
        api.MapPost("/test-setups", async (CreateTestSetupCommand cmd, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(cmd, ct); return Results.Created($"/api/v1/test-setups/{id}", new { id }); }).WithTags("TestCatalogue");
        api.MapPut("/test-setups/{id:guid}", async (Guid id, UpdateTestSetupCommand cmd, IMediator m, CancellationToken ct) =>
        { await m.Send(cmd with { Id = id }, ct); return Results.NoContent(); }).WithTags("TestCatalogue");
        api.MapDelete("/test-setups/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteTestSetupCommand(id), ct); return Results.NoContent(); }).WithTags("TestCatalogue");

        api.MapGet("/test-statistics", async (DateOnly from, DateOnly to, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetTestStatsQuery(from, to), ct))).WithTags("TestCatalogue");
        api.MapPost("/test-statistics/import", async (ImportBody b, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new ImportTestStatsCommand(b.Content), ct))).WithTags("TestCatalogue");
        api.MapPost("/test-statistics/sync", async (SyncStatsBody b, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SyncTestStatsCommand(b.From, b.To), ct))).WithTags("TestCatalogue");
    }

    public static void MapIntegrationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/integration/config", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetIntegrationConfigQuery(), ct))).WithTags("Integration");
        api.MapPost("/integration/config", async (IntegrationConfigBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateIntegrationConfigCommand(b.Enabled, b.IntervalHours), ct); return Results.NoContent(); }).WithTags("Integration");
        api.MapPost("/integration/sync-now", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SyncOracleNowCommand(), ct))).WithTags("Integration");
    }
}
