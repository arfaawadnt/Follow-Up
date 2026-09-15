using FollowUp.Application.Features.Accounting;
using MediatR;

namespace FollowUp.Api.Endpoints;

/// <summary>
/// Accounting module under <c>/api/v1/accounting</c>. Handlers are thin: bind → <c>m.Send</c> → result. Authorization is
/// the usual two layers — the edge auth gate plus each request's <c>RequiredPrivileges</c> (ViewAccounting / ManageAccounting).
/// </summary>
public static class AccountingEndpoints
{
    public sealed record ReasonBody(string Name, bool IsActive = true);
    public sealed record TreasuryBody(string Name, IReadOnlyList<string> Branches, bool IsActive = true);
    public sealed record TreasuryEntryBody(Guid TreasuryId, DateOnly Date, decimal Debit, decimal Credit, Guid ReasonId, string? Notes);
    public sealed record ValidateEntryBody(decimal ReceivedAmount, string? Note);
    public sealed record PenaltyBody(DateOnly Date, Guid LaboratoryId, string AccNo, string PatientName,
        string WrongTestCode, string WrongTestName, decimal WrongValue, string RightTestCode, string RightTestName, decimal RightValue,
        string UserType, Guid? PerformedByUserId, Guid? PerformedByRepId);
    public sealed record DeductionBody(DateOnly Date, Guid AreaId, string Reason, decimal Value, string? Notes, DateOnly? PeriodFrom, DateOnly? PeriodTo,
        string? Basis = null);
    public sealed record CollectionBody(DateOnly Date, Guid LaboratoryId, string Type, IReadOnlyList<Guid> RepIds,
        decimal Cash, decimal Bank, string? Iban, string? DoneBy, string? Notes);
    public sealed record RepIncomeBody(DateOnly Date, Guid RepresentativeId, decimal Amount, string? Notes);

    public static void MapAccountingEndpoints(this RouteGroupBuilder api)
    {
        const string tag = "Accounting";

        // ---- Treasury configuration ----
        api.MapGet("/accounting/treasury/reasons", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetTreasuryReasonsQuery(), ct))).WithTags(tag);
        api.MapPost("/accounting/treasury/reasons", async (ReasonBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateTreasuryReasonCommand(b.Name), ct); return Results.Created($"/api/v1/accounting/treasury/reasons/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/accounting/treasury/reasons/{id:guid}", async (Guid id, ReasonBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateTreasuryReasonCommand(id, b.Name, b.IsActive), ct); return Results.NoContent(); }).WithTags(tag);

        api.MapGet("/accounting/treasuries", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetTreasuriesQuery(), ct))).WithTags(tag);
        api.MapPost("/accounting/treasuries", async (TreasuryBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateTreasuryCommand(b.Name, b.Branches), ct); return Results.Created($"/api/v1/accounting/treasuries/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/accounting/treasuries/{id:guid}", async (Guid id, TreasuryBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateTreasuryCommand(id, b.Name, b.Branches, b.IsActive), ct); return Results.NoContent(); }).WithTags(tag);

        // ---- Treasury account (entries) ----
        api.MapGet("/accounting/treasury/entries", async (DateOnly from, DateOnly to, Guid? treasuryId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetTreasuryEntriesQuery(from, to, treasuryId), ct))).WithTags(tag);
        api.MapPost("/accounting/treasury/entries", async (TreasuryEntryBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateTreasuryEntryCommand(b.TreasuryId, b.Date, b.Debit, b.Credit, b.ReasonId, b.Notes), ct); return Results.Created($"/api/v1/accounting/treasury/entries/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/accounting/treasury/entries/{id:guid}", async (Guid id, TreasuryEntryBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateTreasuryEntryCommand(id, b.Date, b.Debit, b.Credit, b.ReasonId, b.Notes), ct); return Results.NoContent(); }).WithTags(tag);
        api.MapDelete("/accounting/treasury/entries/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteTreasuryEntryCommand(id), ct); return Results.NoContent(); }).WithTags(tag);
        // Validation of a mirrored collection's cash (needs the treasury's Validate right).
        api.MapPost("/accounting/treasury/entries/{id:guid}/validate", async (Guid id, ValidateEntryBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new ValidateTreasuryEntryCommand(id, b.ReceivedAmount, b.Note), ct); return Results.NoContent(); }).WithTags(tag);
        // Per-role treasury rights (Roles page; ManageUsers).
        api.MapGet("/accounting/treasury/grants/{roleId:guid}", async (Guid roleId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetTreasuryGrantsQuery(roleId), ct))).WithTags(tag);
        api.MapPut("/accounting/treasury/grants/{roleId:guid}", async (Guid roleId, IReadOnlyList<TreasuryGrantInput> grants, IMediator m, CancellationToken ct) =>
        { await m.Send(new SetTreasuryGrantsCommand(roleId, grants), ct); return Results.NoContent(); }).WithTags(tag);

        // ---- Penalty statement ----
        api.MapGet("/accounting/penalties", async (DateOnly from, DateOnly to, Guid? laboratoryId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetPenaltiesQuery(from, to, laboratoryId), ct))).WithTags(tag);
        // The "User" picker of the record dialog: reps for UserType=Rep, active system users otherwise.
        api.MapGet("/accounting/penalty-actors", async (string userType, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetPenaltyActorsQuery(userType), ct))).WithTags(tag);
        api.MapPost("/accounting/penalties", async (PenaltyBody b, IMediator m, CancellationToken ct) =>
        {
            var id = await m.Send(new CreatePenaltyCommand(b.Date, b.LaboratoryId, b.AccNo, b.PatientName, b.WrongTestCode, b.WrongTestName, b.WrongValue,
                b.RightTestCode, b.RightTestName, b.RightValue, b.UserType, b.PerformedByUserId, b.PerformedByRepId), ct);
            return Results.Created($"/api/v1/accounting/penalties/{id}", new { id });
        }).WithTags(tag);
        api.MapPut("/accounting/penalties/{id:guid}", async (Guid id, PenaltyBody b, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new UpdatePenaltyCommand(id, b.Date, b.AccNo, b.PatientName, b.WrongTestCode, b.WrongTestName, b.WrongValue,
                b.RightTestCode, b.RightTestName, b.RightValue, b.UserType, b.PerformedByUserId, b.PerformedByRepId), ct);
            return Results.NoContent();
        }).WithTags(tag);
        api.MapDelete("/accounting/penalties/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeletePenaltyCommand(id), ct); return Results.NoContent(); }).WithTags(tag);

        // ---- Deductions ----
        api.MapGet("/accounting/deductions", async (DateOnly from, DateOnly to, Guid? areaId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetDeductionsQuery(from, to, areaId), ct))).WithTags(tag);
        api.MapGet("/accounting/deductions/suggest", async (Guid areaId, string reason, DateOnly from, DateOnly to, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SuggestDeductionQuery(areaId, reason, from, to), ct))).WithTags(tag);
        api.MapPost("/accounting/deductions", async (DeductionBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateDeductionCommand(b.Date, b.AreaId, b.Reason, b.Value, b.Notes, b.PeriodFrom, b.PeriodTo), ct); return Results.Created($"/api/v1/accounting/deductions/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/accounting/deductions/{id:guid}", async (Guid id, DeductionBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateDeductionCommand(id, b.Date, b.Reason, b.Value, b.Notes, b.PeriodFrom, b.PeriodTo, b.Basis), ct); return Results.NoContent(); }).WithTags(tag);
        api.MapDelete("/accounting/deductions/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteDeductionCommand(id), ct); return Results.NoContent(); }).WithTags(tag);
        // Runs the daily automation now: link unmirrored penalties + recalculate the month's Percentage Deal rows.
        api.MapPost("/accounting/deductions/recalculate", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new RecalculateDeductionsCommand(), ct))).WithTags(tag);

        // ---- Collections ----
        api.MapGet("/accounting/collections", async (DateOnly from, DateOnly to, Guid? laboratoryId, Guid? repId, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetCollectionsQuery(from, to, laboratoryId, repId), ct))).WithTags(tag);
        api.MapPost("/accounting/collections", async (CollectionBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateCollectionCommand(b.Date, b.LaboratoryId, b.Type, b.RepIds, b.Cash, b.Bank, b.Iban, b.DoneBy, b.Notes), ct); return Results.Created($"/api/v1/accounting/collections/{id}", new { id }); }).WithTags(tag);
        api.MapPut("/accounting/collections/{id:guid}", async (Guid id, CollectionBody b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateCollectionCommand(id, b.Date, b.Type, b.RepIds, b.Cash, b.Bank, b.Iban, b.DoneBy, b.Notes), ct); return Results.NoContent(); }).WithTags(tag);
        api.MapDelete("/accounting/collections/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteCollectionCommand(id), ct); return Results.NoContent(); }).WithTags(tag);

        // ---- Rep statement ----
        api.MapGet("/accounting/rep-statement/{repId:guid}", async (Guid repId, DateOnly from, DateOnly to, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetRepStatementQuery(repId, from, to), ct))).WithTags(tag);
        api.MapPost("/accounting/rep-income", async (RepIncomeBody b, IMediator m, CancellationToken ct) =>
        { var id = await m.Send(new CreateRepIncomeEntryCommand(b.Date, b.RepresentativeId, b.Amount, b.Notes), ct); return Results.Created($"/api/v1/accounting/rep-income/{id}", new { id }); }).WithTags(tag);
        api.MapDelete("/accounting/rep-income/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteRepIncomeEntryCommand(id), ct); return Results.NoContent(); }).WithTags(tag);
    }
}
