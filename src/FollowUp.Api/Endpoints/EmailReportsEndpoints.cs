using FollowUp.Application.Features.EmailReports;
using MediatR;

namespace FollowUp.Api.Endpoints;

public static class EmailReportsEndpoints
{
    public sealed record SmtpConfigBody(bool Enabled, string Host, int Port, bool UseSsl, string FromAddress, string? User, string? Password);
    public sealed record TestEmailBody(string ToEmail);

    public static void MapEmailReportsEndpoints(this RouteGroupBuilder api)
    {
        // SMTP mail gateway
        api.MapGet("/email/smtp", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetSmtpConfigQuery(), ct))).WithTags("EmailReports");
        api.MapPost("/email/smtp", async (SmtpConfigBody b, IMediator m, CancellationToken ct) =>
        {
            await m.Send(new UpdateSmtpConfigCommand(b.Enabled, b.Host, b.Port, b.UseSsl, b.FromAddress, b.User, b.Password), ct);
            return Results.NoContent();
        }).WithTags("EmailReports");
        api.MapPost("/email/smtp/test", async (TestEmailBody b, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SendTestEmailCommand(b.ToEmail), ct))).WithTags("EmailReports");

        // Daily-email subscriptions
        api.MapGet("/email/subscriptions", async (IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new GetStatsEmailSubscriptionsQuery(), ct))).WithTags("EmailReports");
        api.MapPost("/email/subscriptions", async (StatsEmailSubscriptionInput b, IMediator m, CancellationToken ct) =>
        {
            var id = await m.Send(new CreateStatsEmailSubscriptionCommand(b), ct);
            return Results.Created($"/api/v1/email/subscriptions/{id}", new { id });
        }).WithTags("EmailReports");
        api.MapPut("/email/subscriptions/{id:guid}", async (Guid id, StatsEmailSubscriptionInput b, IMediator m, CancellationToken ct) =>
        { await m.Send(new UpdateStatsEmailSubscriptionCommand(id, b), ct); return Results.NoContent(); }).WithTags("EmailReports");
        api.MapDelete("/email/subscriptions/{id:guid}", async (Guid id, IMediator m, CancellationToken ct) =>
        { await m.Send(new DeleteStatsEmailSubscriptionCommand(id), ct); return Results.NoContent(); }).WithTags("EmailReports");
        api.MapPost("/email/subscriptions/{id:guid}/send-now", async (Guid id, IMediator m, CancellationToken ct) =>
            Results.Ok(await m.Send(new SendStatsEmailNowCommand(id), ct))).WithTags("EmailReports");
    }
}
