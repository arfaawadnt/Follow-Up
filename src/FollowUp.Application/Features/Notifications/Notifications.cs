using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Notifications;
using MediatR;

namespace FollowUp.Application.Features.Notifications;

// ---- Read side ----

public sealed record NotificationDto(Guid Id, string EventKey, string Title, string Body, DateTimeOffset CreatedAt, bool IsRead);
public sealed record PreferenceDto(string EventKey, bool System, bool Mail, bool WhatsApp);
public sealed record GatewayDto(string Name, bool Enabled, string MaskedSecret);
public sealed record DeliveryLogDto(Guid Id, string Channel, string Recipient, string EventKey, string Status, int Attempts, string? LastError);

public interface INotificationQueries
{
    Task<IReadOnlyList<NotificationDto>> GetFeedAsync(AppUserId userId, bool unreadOnly, CancellationToken ct);
    Task<IReadOnlyList<PreferenceDto>> GetPreferencesAsync(AppUserId userId, CancellationToken ct);
    Task<IReadOnlyList<GatewayDto>> GetGatewaysAsync(CancellationToken ct);   // secrets masked
    Task<IReadOnlyList<DeliveryLogDto>> GetLogsAsync(CancellationToken ct);
}

/// <summary>The caller's in-app notification feed (SRS FR-16; self-scoped).</summary>
public sealed record GetNotificationsQuery(bool UnreadOnly = false) : IQuery<IReadOnlyList<NotificationDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class GetNotificationsHandler : IQueryHandler<GetNotificationsQuery, IReadOnlyList<NotificationDto>>
{
    private readonly INotificationQueries _queries;
    private readonly ICurrentUser _user;
    public GetNotificationsHandler(INotificationQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<NotificationDto>> Handle(GetNotificationsQuery r, CancellationToken ct) =>
        _queries.GetFeedAsync(_user.UserId, r.UnreadOnly, ct);
}

/// <summary>Marks one feed item read; only the owner may (SRS FR-16).</summary>
public sealed record MarkNotificationReadCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class MarkNotificationReadHandler : ICommandHandler<MarkNotificationReadCommand>
{
    private readonly ISystemNotificationRepository _repo;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    public MarkNotificationReadHandler(ISystemNotificationRepository repo, ICurrentUser user, IClock clock)
    { _repo = repo; _user = user; _clock = clock; }

    public async Task<Unit> Handle(MarkNotificationReadCommand r, CancellationToken ct)
    {
        var n = await _repo.GetByIdAsync(new SystemNotificationId(r.Id), ct)
            ?? throw new NotFoundException("Notification", r.Id);
        if (n.RecipientUserId != _user.UserId)
            throw new ForbiddenException("This notification is not yours.");
        n.MarkRead(_clock.UtcNow);
        return Unit.Value;
    }
}

/// <summary>Marks all of the caller's unread items read.</summary>
public sealed record MarkAllNotificationsReadCommand : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class MarkAllNotificationsReadHandler : ICommandHandler<MarkAllNotificationsReadCommand>
{
    private readonly ISystemNotificationRepository _repo;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;
    public MarkAllNotificationsReadHandler(ISystemNotificationRepository repo, ICurrentUser user, IClock clock)
    { _repo = repo; _user = user; _clock = clock; }

    public async Task<Unit> Handle(MarkAllNotificationsReadCommand r, CancellationToken ct)
    {
        foreach (var n in await _repo.GetUnreadForUserAsync(_user.UserId, ct))
            n.MarkRead(_clock.UtcNow);
        return Unit.Value;
    }
}

/// <summary>Reads the caller's channel preferences.</summary>
public sealed record GetNotificationPreferencesQuery : IQuery<IReadOnlyList<PreferenceDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class GetNotificationPreferencesHandler : IQueryHandler<GetNotificationPreferencesQuery, IReadOnlyList<PreferenceDto>>
{
    private readonly INotificationQueries _queries;
    private readonly ICurrentUser _user;
    public GetNotificationPreferencesHandler(INotificationQueries queries, ICurrentUser user) { _queries = queries; _user = user; }
    public Task<IReadOnlyList<PreferenceDto>> Handle(GetNotificationPreferencesQuery r, CancellationToken ct) =>
        _queries.GetPreferencesAsync(_user.UserId, ct);
}

/// <summary>Updates one channel preference for the caller (default system-on).</summary>
public sealed record UpdateNotificationPreferenceCommand(string EventKey, bool System, bool Mail, bool WhatsApp)
    : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = Array.Empty<string>();
}

public sealed class UpdateNotificationPreferenceHandler : ICommandHandler<UpdateNotificationPreferenceCommand>
{
    private readonly INotificationPreferenceRepository _repo;
    private readonly ICurrentUser _user;
    public UpdateNotificationPreferenceHandler(INotificationPreferenceRepository repo, ICurrentUser user)
    { _repo = repo; _user = user; }

    public async Task<Unit> Handle(UpdateNotificationPreferenceCommand r, CancellationToken ct)
    {
        var pref = await _repo.GetAsync(_user.UserId, r.EventKey, ct);
        if (pref is null)
        {
            pref = NotificationPreference.Default(_user.UserId, r.EventKey);
            _repo.Add(pref);
        }
        pref.Set(r.System, r.Mail, r.WhatsApp);
        return Unit.Value;
    }
}

// ---- Admin: gateways (masked) + delivery logs + retry ----

public sealed record GetNotificationGatewaysQuery : IQuery<IReadOnlyList<GatewayDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}
public sealed class GetNotificationGatewaysHandler : IQueryHandler<GetNotificationGatewaysQuery, IReadOnlyList<GatewayDto>>
{
    private readonly INotificationQueries _q;
    public GetNotificationGatewaysHandler(INotificationQueries q) => _q = q;
    public Task<IReadOnlyList<GatewayDto>> Handle(GetNotificationGatewaysQuery r, CancellationToken ct) => _q.GetGatewaysAsync(ct);
}

public sealed record GetDeliveryLogsQuery : IQuery<IReadOnlyList<DeliveryLogDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}
public sealed class GetDeliveryLogsHandler : IQueryHandler<GetDeliveryLogsQuery, IReadOnlyList<DeliveryLogDto>>
{
    private readonly INotificationQueries _q;
    public GetDeliveryLogsHandler(INotificationQueries q) => _q = q;
    public Task<IReadOnlyList<DeliveryLogDto>> Handle(GetDeliveryLogsQuery r, CancellationToken ct) => _q.GetLogsAsync(ct);
}

/// <summary>Marks a failed delivery for retry (SRS FR-16; the dispatcher picks it up, JOBS-006).</summary>
public sealed record RetryDeliveryCommand(Guid Id) : ICommand, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ManageUsers };
}
public sealed class RetryDeliveryHandler : ICommandHandler<RetryDeliveryCommand>
{
    private readonly INotificationDeliveryLogRepository _repo;
    public RetryDeliveryHandler(INotificationDeliveryLogRepository repo) => _repo = repo;
    public async Task<Unit> Handle(RetryDeliveryCommand r, CancellationToken ct)
    {
        var log = await _repo.GetByIdAsync(new NotificationDeliveryLogId(r.Id), ct)
            ?? throw new NotFoundException("Delivery log", r.Id);
        log.RequeueForRetry(); // dispatcher re-attempts on its next 10s cycle (JOBS-006)
        return Unit.Value;
    }
}
