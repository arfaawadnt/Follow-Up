using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Behaviors;

/// <summary>
/// The unit-of-work boundary for commands (ADR-0005: the DbContext IS the UoW — no wrapper). Runs the
/// handler, then commits once via <c>SaveChanges</c>, at which point the audit/outbox interceptor writes the
/// audit trail and outbox rows in the SAME transaction. Queries bypass this behavior entirely.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly FollowUpDbContext _dbContext;
    private readonly IRealtimeNotifier _realtime;

    public TransactionBehavior(FollowUpDbContext dbContext, IRealtimeNotifier realtime)
    {
        _dbContext = dbContext;
        _realtime = realtime;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IBaseCommand)
            return await next();

        // A single SaveChanges is atomic; wrap explicitly so a handler that performs several operations —
        // and the audit/outbox writes triggered on save — commit or roll back together.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        TResponse response;
        try
        {
            response = await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
                var r = await next();
                await _dbContext.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return r;
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            // Optimistic-concurrency clash (xmin token) — surface as 409 (SRS FR-3/FR-4).
            throw new ConflictException("The record was modified by someone else. Reload and try again.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // A concurrent insert violated a unique constraint (a get-then-add race, the daily_visit slot index,
            // BRD-9) — surface as 409 instead of an unmapped DbUpdateException → 500 (finding CPN-14).
            throw new ConflictException("That record conflicts with an existing one. Reload and try again.");
        }

        // Post-commit refetch hint to connected clients (Workflows §2.1). Best-effort — never fail the command.
        try { await _realtime.DataChangedAsync(typeof(TRequest).Name, ct); } catch { /* hints are best-effort */ }
        return response;
    }
}
