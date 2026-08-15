using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.DailyBoard.Contracts;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Features.DailyBoard.Queries;

/// <summary>Returns the follow-up board for a date (defaults to Cairo today), scoped to the caller (SRS FR-5).</summary>
public sealed record GetDailyBoardQuery(DateOnly? Date = null) : IQuery<IReadOnlyList<BoardItemDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewDailyFollowup };
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
        var date = request.Date ?? _clock.CairoToday;
        var canSeeEncrypted = _user.Has(Privileges.ShowEncryptedLabs);
        return _queries.GetBoardAsync(date, _user.Scope, canSeeEncrypted, ct);
    }
}
