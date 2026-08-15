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

    public TransactionBehavior(FollowUpDbContext dbContext) => _dbContext = dbContext;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IBaseCommand)
            return await next();

        // A single SaveChanges is atomic; wrap explicitly so a handler that performs several operations —
        // and the audit/outbox writes triggered on save — commit or roll back together.
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);
            var response = await next();
            await _dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return response;
        });
    }
}
