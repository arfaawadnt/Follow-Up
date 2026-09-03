using FluentAssertions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

[Collection("integration")]
public sealed class IdempotencyTests
{
    private readonly IntegrationFixture _fx;
    public IdempotencyTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Same_idempotency_key_executes_the_command_once()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        var cmd = new CreateLaboratoryCommand { Code = "MGL-IDEM", Name = "Idem Lab", Segment = "A", Governorate = "Cairo" };
        _fx.Idempotency.CurrentKey = "create-lab-key-001";
        try
        {
            Guid id1, id2;
            using (var s = _fx.Services.CreateScope())
                id1 = await s.ServiceProvider.GetRequiredService<IMediator>().Send(cmd);
            // Retry with the SAME key + same command — must NOT create a second lab (no duplicate-code conflict).
            using (var s = _fx.Services.CreateScope())
                id2 = await s.ServiceProvider.GetRequiredService<IMediator>().Send(cmd);

            id2.Should().Be(id1); // replayed result
        }
        finally
        {
            _fx.Idempotency.CurrentKey = null; // don't leak the key into other tests
        }

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        (await db.Laboratories.CountAsync(l => l.Name == "Idem Lab")).Should().Be(1);
    }
}
