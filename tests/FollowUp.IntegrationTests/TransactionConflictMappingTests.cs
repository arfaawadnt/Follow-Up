using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Infrastructure.Behaviors;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FollowUp.IntegrationTests;

/// <summary>
/// CPN-14: a unique-constraint violation from a concurrent insert (get-then-add races, the daily_visit slot
/// index) reached the client as an unmapped DbUpdateException → 500. TransactionBehavior now maps Postgres
/// SQLSTATE 23505 to a ConflictException (409), like it already does for the optimistic-concurrency clash.
/// </summary>
[Collection("integration")]
public sealed class TransactionConflictMappingTests
{
    private readonly IntegrationFixture _fx;
    public TransactionConflictMappingTests(IntegrationFixture fx) => _fx = fx;

    private sealed record TestCommand : IBaseCommand;

    private sealed class NoopRealtime : IRealtimeNotifier
    {
        public Task DataChangedAsync(string entityType, CancellationToken ct = default) => Task.CompletedTask;
        public Task NotifyUserAsync(Guid userId, string message, CancellationToken ct = default) => Task.CompletedTask;
    }

    [SkippableFact]
    public async Task A_unique_violation_is_surfaced_as_a_conflict_not_a_500()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var behavior = new TransactionBehavior<TestCommand, Unit>(db, new NoopRealtime());

        RequestHandlerDelegate<Unit> next = () => throw new Microsoft.EntityFrameworkCore.DbUpdateException(
            "insert failed",
            new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", "23505"));

        var act = () => behavior.Handle(new TestCommand(), next, default);

        await act.Should().ThrowAsync<ConflictException>();
    }
}
