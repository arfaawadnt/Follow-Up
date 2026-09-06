using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FluentValidation;

namespace FollowUp.Application.Features.Setup;

/// <summary>
/// Re-runs the income-based segment auto-assignment on demand for a chosen month (default: the previous calendar
/// month). Same logic as the month-start Hangfire job — assigns every lab to the segment whose target-income band
/// contains the lab's achieved income for the month. Operator-triggered from the Segment setup page.
/// </summary>
public sealed record AssignLabSegmentsCommand(string? Month = null) : ICommand<SegmentAssignmentResult>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.SetupRefs };
}

public sealed class AssignLabSegmentsValidator : AbstractValidator<AssignLabSegmentsCommand>
{
    public AssignLabSegmentsValidator()
    {
        RuleFor(x => x.Month).Matches(@"^\d{4}-(0[1-9]|1[0-2])$")
            .When(x => !string.IsNullOrWhiteSpace(x.Month))
            .WithMessage("Month must be in yyyy-MM format.");
    }
}

public sealed class AssignLabSegmentsHandler : ICommandHandler<AssignLabSegmentsCommand, SegmentAssignmentResult>
{
    private readonly ISegmentAssignmentRunner _runner;
    private readonly IClock _clock;
    public AssignLabSegmentsHandler(ISegmentAssignmentRunner runner, IClock clock) { _runner = runner; _clock = clock; }

    public Task<SegmentAssignmentResult> Handle(AssignLabSegmentsCommand request, CancellationToken ct)
    {
        var month = ParseMonth(request.Month) ?? PreviousMonth(_clock.CairoToday);
        return _runner.RunAsync(month, manual: true, ct);
    }

    private static YearMonth? ParseMonth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('-');
        return new YearMonth(int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static YearMonth PreviousMonth(DateOnly today)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        return YearMonth.From(firstOfThisMonth.AddDays(-1));
    }
}
