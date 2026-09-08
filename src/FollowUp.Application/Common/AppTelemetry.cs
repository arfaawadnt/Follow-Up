using System.Diagnostics;

namespace FollowUp.Application.Common;

/// <summary>The application's OpenTelemetry <see cref="ActivitySource"/> (finding M-19). Program.cs registers
/// this source name with the tracer, so spans started here (one per MediatR request, via TracingBehavior) are
/// exported — previously <c>AddSource("FollowUp")</c> matched no emitter.</summary>
public static class AppTelemetry
{
    public const string SourceName = "FollowUp";
    public static readonly ActivitySource Source = new(SourceName);
}
