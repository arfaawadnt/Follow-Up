using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.DailyBoard.Queries;

/// <summary>Returns the follow-up board for a date range (defaults to Cairo today), optionally filtered by
/// collector rep and status, scoped to the caller (SRS FR-5).</summary>
public sealed record GetDailyBoardQuery(
    DateOnly? Start = null, DateOnly? End = null, Guid? RepId = null, string? Status = null)
    : IQuery<IReadOnlyList<BoardItemDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewDailyFollowup };
}

/// <summary>Suggested sample count for a visit's check-in popup (SRS FR-5 suggested-value helper).</summary>
public sealed record GetSuggestedSampleCountQuery(Guid VisitId) : IQuery<int?>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewDailyFollowup };
}

public sealed class GetSuggestedSampleCountHandler : IQueryHandler<GetSuggestedSampleCountQuery, int?>
{
    private readonly IDailyBoardQueries _queries;
    public GetSuggestedSampleCountHandler(IDailyBoardQueries queries) => _queries = queries;
    public Task<int?> Handle(GetSuggestedSampleCountQuery request, CancellationToken ct) =>
        _queries.GetSuggestedSampleCountAsync(request.VisitId, ct);
}

public sealed class GetDailyBoardHandler : IQueryHandler<GetDailyBoardQuery, IReadOnlyList<BoardItemDto>>
{
    private readonly IDailyBoardQueries _queries;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public GetDailyBoardHandler(IDailyBoardQueries queries, ICurrentUser user, IClock clock)
    {
        _queries = queries; _user = user; _clock = clock;
    }

    public Task<IReadOnlyList<BoardItemDto>> Handle(GetDailyBoardQuery request, CancellationToken ct)
    {
        var start = request.Start ?? _clock.CairoToday;
        var end = request.End ?? start;
        var repId = request.RepId == Guid.Empty ? null : request.RepId;
        var status = string.IsNullOrWhiteSpace(request.Status) || request.Status == "All" ? null : request.Status;
        var canSeeEncrypted = _user.Has(Privileges.ShowEncryptedLabs);
        return _queries.GetBoardAsync(start, end, repId, status, _user.Scope, canSeeEncrypted, ct);
    }
}
