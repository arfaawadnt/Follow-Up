using FluentValidation;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Emailing;
using FollowUp.Domain.Identity;
using MediatR;

namespace FollowUp.Application.Features.EmailReports;

// ============================ SMTP mail-gateway config ============================

public sealed record SmtpConfigDto(bool Enabled, string Host, int Port, bool UseSsl, string FromAddress, string? User, bool HasPassword);

public interface ISmtpConfigQueries
{
    Task<SmtpConfigDto> GetAsync(CancellationToken ct);
}

public sealed record GetSmtpConfigQuery : IQuery<SmtpConfigDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageEmailReports };
}

public sealed class GetSmtpConfigHandler : IQueryHandler<GetSmtpConfigQuery, SmtpConfigDto>
{
    private readonly ISmtpConfigQueries _queries;
    public GetSmtpConfigHandler(ISmtpConfigQueries queries) => _queries = queries;
    public Task<SmtpConfigDto> Handle(GetSmtpConfigQuery request, CancellationToken ct) => _queries.GetAsync(ct);
}

/// <summary>Updates the SMTP gateway. A null/blank/"********" password keeps the stored one (masked write).</summary>
public sealed record UpdateSmtpConfigCommand(bool Enabled, string Host, int Port, bool UseSsl, string FromAddress, string? User, string? Password)
    : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageEmailReports };
}

public sealed class UpdateSmtpConfigValidator : AbstractValidator<UpdateSmtpConfigCommand>
{
    public UpdateSmtpConfigValidator()
    {
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.Host).NotEmpty().When(x => x.Enabled).WithMessage("SMTP host is required to enable the gateway.");
        RuleFor(x => x.FromAddress).NotEmpty().When(x => x.Enabled).WithMessage("A From address is required to enable the gateway.");
    }
}

public sealed class UpdateSmtpConfigHandler : ICommandHandler<UpdateSmtpConfigCommand>
{
    private readonly ISmtpConfigRepository _repo;
    public UpdateSmtpConfigHandler(ISmtpConfigRepository repo) => _repo = repo;

    public async Task<Unit> Handle(UpdateSmtpConfigCommand r, CancellationToken ct)
    {
        var cfg = await _repo.GetAsync(ct);
        if (cfg is null) { cfg = SmtpConfig.Create(); _repo.Add(cfg); }
        cfg.Configure(r.Enabled, r.Host, r.Port, r.UseSsl, r.FromAddress, r.User);
        // Only overwrite the secret when a real new value is supplied (never the mask).
        if (!string.IsNullOrWhiteSpace(r.Password) && r.Password != "********")
            cfg.SetPassword(r.Password);
        return Unit.Value;
    }
}

/// <summary>Sends a one-off test email through the current gateway so the operator can verify it works.</summary>
public sealed record SendTestEmailCommand(string ToEmail) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageEmailReports };
}

public sealed class SendTestEmailValidator : AbstractValidator<SendTestEmailCommand>
{
    public SendTestEmailValidator() => RuleFor(x => x.ToEmail).NotEmpty().EmailAddress();
}

public sealed class SendTestEmailHandler : ICommandHandler<SendTestEmailCommand>
{
    private readonly IEmailSender _email;
    public SendTestEmailHandler(IEmailSender email) => _email = email;
    public async Task<Unit> Handle(SendTestEmailCommand r, CancellationToken ct)
    {
        await _email.SendAsync(r.ToEmail, "Follow-Up — SMTP test email",
            "<p>This is a test email from the Follow-Up mail gateway. If you received it, SMTP is configured correctly.</p>", ct);
        return Unit.Value;
    }
}

// ============================ Daily-email subscriptions ============================

public sealed record StatsEmailSubscriptionDto(Guid Id, string Name, bool IncludeLabStats, bool IncludeTestStats,
    bool IncludeAreaStats, string FiltersJson, IReadOnlyList<Guid> UserIds, IReadOnlyList<string> Emails,
    int SendHour, int SendMinute, int WindowDays, bool Enabled, string? LastStatus, DateTimeOffset? LastRunAt);

public interface IStatsEmailSubscriptionQueries
{
    Task<IReadOnlyList<StatsEmailSubscriptionDto>> ListAsync(CancellationToken ct);
}

public sealed record GetStatsEmailSubscriptionsQuery : IQuery<IReadOnlyList<StatsEmailSubscriptionDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageEmailReports };
}

public sealed class GetStatsEmailSubscriptionsHandler : IQueryHandler<GetStatsEmailSubscriptionsQuery, IReadOnlyList<StatsEmailSubscriptionDto>>
{
    private readonly IStatsEmailSubscriptionQueries _queries;
    public GetStatsEmailSubscriptionsHandler(IStatsEmailSubscriptionQueries queries) => _queries = queries;
    public Task<IReadOnlyList<StatsEmailSubscriptionDto>> Handle(GetStatsEmailSubscriptionsQuery request, CancellationToken ct) => _queries.ListAsync(ct);
}

public sealed record StatsEmailSubscriptionInput(string Name, bool IncludeLabStats, bool IncludeTestStats, bool IncludeAreaStats,
    string? FiltersJson, IReadOnlyList<Guid> UserIds, IReadOnlyList<string> Emails, int SendHour, int SendMinute, int WindowDays, bool Enabled);

public sealed class StatsEmailSubscriptionInputValidator : AbstractValidator<StatsEmailSubscriptionInput>
{
    public StatsEmailSubscriptionInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SendHour).InclusiveBetween(0, 23);
        RuleFor(x => x.SendMinute).InclusiveBetween(0, 59);
        RuleFor(x => x.WindowDays).InclusiveBetween(1, 90);
        RuleFor(x => x).Must(x => x.IncludeLabStats || x.IncludeTestStats || x.IncludeAreaStats)
            .WithMessage("Select at least one report.");
        RuleFor(x => x).Must(x => (x.UserIds?.Count ?? 0) > 0 || (x.Emails?.Count ?? 0) > 0)
            .WithMessage("Add at least one recipient.");
    }
}

public sealed record CreateStatsEmailSubscriptionCommand(StatsEmailSubscriptionInput Input) : ICommand<Guid>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageEmailReports };
}

public sealed class CreateStatsEmailSubscriptionValidator : AbstractValidator<CreateStatsEmailSubscriptionCommand>
{
    public CreateStatsEmailSubscriptionValidator() => RuleFor(x => x.Input).SetValidator(new StatsEmailSubscriptionInputValidator());
}

public sealed class CreateStatsEmailSubscriptionHandler : ICommandHandler<CreateStatsEmailSubscriptionCommand, Guid>
{
    private readonly IStatsEmailSubscriptionRepository _repo;
    private readonly IStatsEmailScheduler _scheduler;
    public CreateStatsEmailSubscriptionHandler(IStatsEmailSubscriptionRepository repo, IStatsEmailScheduler scheduler)
    { _repo = repo; _scheduler = scheduler; }

    public Task<Guid> Handle(CreateStatsEmailSubscriptionCommand r, CancellationToken ct)
    {
        var i = r.Input;
        var sub = StatsEmailSubscription.Create(i.Name);
        Apply(sub, i);
        _repo.Add(sub);
        _scheduler.Schedule(sub);
        return Task.FromResult(sub.Id.Value);
    }

    internal static void Apply(StatsEmailSubscription sub, StatsEmailSubscriptionInput i)
    {
        sub.Rename(i.Name);
        sub.SetReports(i.IncludeLabStats, i.IncludeTestStats, i.IncludeAreaStats);
        sub.SetFilters(i.FiltersJson);
        sub.SetRecipients(i.UserIds ?? Array.Empty<Guid>(), i.Emails ?? Array.Empty<string>());
        sub.SetSchedule(i.SendHour, i.SendMinute, i.WindowDays);
        sub.Enable(i.Enabled);
    }
}

public sealed record UpdateStatsEmailSubscriptionCommand(Guid Id, StatsEmailSubscriptionInput Input) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageEmailReports };
}

public sealed class UpdateStatsEmailSubscriptionValidator : AbstractValidator<UpdateStatsEmailSubscriptionCommand>
{
    public UpdateStatsEmailSubscriptionValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Input).SetValidator(new StatsEmailSubscriptionInputValidator());
    }
}

public sealed class UpdateStatsEmailSubscriptionHandler : ICommandHandler<UpdateStatsEmailSubscriptionCommand>
{
    private readonly IStatsEmailSubscriptionRepository _repo;
    private readonly IStatsEmailScheduler _scheduler;
    public UpdateStatsEmailSubscriptionHandler(IStatsEmailSubscriptionRepository repo, IStatsEmailScheduler scheduler)
    { _repo = repo; _scheduler = scheduler; }

    public async Task<Unit> Handle(UpdateStatsEmailSubscriptionCommand r, CancellationToken ct)
    {
        var sub = await _repo.GetByIdAsync(new StatsEmailSubscriptionId(r.Id), ct)
            ?? throw new NotFoundException("Email report", r.Id);
        CreateStatsEmailSubscriptionHandler.Apply(sub, r.Input);
        _scheduler.Schedule(sub);
        return Unit.Value;
    }
}

public sealed record DeleteStatsEmailSubscriptionCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageEmailReports };
}

public sealed class DeleteStatsEmailSubscriptionValidator : AbstractValidator<DeleteStatsEmailSubscriptionCommand>
{
    public DeleteStatsEmailSubscriptionValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class DeleteStatsEmailSubscriptionHandler : ICommandHandler<DeleteStatsEmailSubscriptionCommand>
{
    private readonly IStatsEmailSubscriptionRepository _repo;
    private readonly IStatsEmailScheduler _scheduler;
    public DeleteStatsEmailSubscriptionHandler(IStatsEmailSubscriptionRepository repo, IStatsEmailScheduler scheduler)
    { _repo = repo; _scheduler = scheduler; }

    public async Task<Unit> Handle(DeleteStatsEmailSubscriptionCommand r, CancellationToken ct)
    {
        var sub = await _repo.GetByIdAsync(new StatsEmailSubscriptionId(r.Id), ct)
            ?? throw new NotFoundException("Email report", r.Id);
        _repo.Remove(sub);
        _scheduler.Unschedule(sub.Id);
        return Unit.Value;
    }
}

/// <summary>Renders and sends a subscription immediately (preview/test), returning the delivery summary.</summary>
public sealed record SendStatsEmailNowCommand(Guid Id) : ICommand<StatsEmailRunResult>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageEmailReports };
}

public sealed class SendStatsEmailNowValidator : AbstractValidator<SendStatsEmailNowCommand>
{
    public SendStatsEmailNowValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class SendStatsEmailNowHandler : ICommandHandler<SendStatsEmailNowCommand, StatsEmailRunResult>
{
    private readonly IStatsEmailRunner _runner;
    public SendStatsEmailNowHandler(IStatsEmailRunner runner) => _runner = runner;
    public Task<StatsEmailRunResult> Handle(SendStatsEmailNowCommand r, CancellationToken ct) =>
        _runner.RunAsync(new StatsEmailSubscriptionId(r.Id), ct);
}
