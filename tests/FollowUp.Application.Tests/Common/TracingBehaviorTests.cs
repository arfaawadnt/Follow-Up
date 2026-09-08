using System.Diagnostics;
using FluentAssertions;
using FollowUp.Application.Common.Behaviors;
using MediatR;

namespace FollowUp.Application.Tests.Common;

/// <summary>
/// Finding M-19: TracingBehavior emits a span under the "FollowUp" OpenTelemetry source for every MediatR
/// request, so AddSource("FollowUp") now has a real emitter (it previously matched none).
/// </summary>
public class TracingBehaviorTests
{
    private sealed record TestRequest : IRequest<Unit>;

    [Fact]
    public async Task Emits_a_span_under_the_FollowUp_source_per_request()
    {
        var started = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "FollowUp",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => started.Add(a.OperationName),
        };
        ActivitySource.AddActivityListener(listener);

        var behavior = new TracingBehavior<TestRequest, Unit>();
        await behavior.Handle(new TestRequest(), () => Task.FromResult(Unit.Value), CancellationToken.None);

        started.Should().Contain(nameof(TestRequest), "each request must open a FollowUp span");
    }
}
