using FluentAssertions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

[Collection("integration")]
public sealed class WritePathTests
{
    private readonly IntegrationFixture _fx;
    public WritePathTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Create_laboratory_persists_state_audit_and_outbox_in_one_transaction()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set — integration DB unavailable.");
        await _fx.ResetAsync();

        // Act — dispatch the command through the full MediatR pipeline (auth, validation, transaction, interceptor).
        Guid labId;
        using (var scope = _fx.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            labId = await mediator.Send(new CreateLaboratoryCommand
            {
                Code = "MGL-9001",
                Name = "Integration Lab",
                Segment = "A",
                Governorate = "Cairo",
                WorkDays = new[] { "Sunday", "Tuesday" },
                VisitTimes = new[] { "08:30", "13:00" },
                Contacts = new[] { new NewContact("Dr. Integration", "Manager", "01000000000", new DateOnly(1990, 1, 1)) },
            });
        }

        // Assert — state, contacts, audit trail, and outbox all committed together.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

            var lab = await db.Laboratories.FirstOrDefaultAsync(l => l.Id == new Domain.Laboratories.LaboratoryId(labId));
            lab.Should().NotBeNull();
            lab!.Code.Value.Should().Be("MGL-9001");
            lab.Contacts.Should().ContainSingle();
            lab.Schedule.VisitTimes.Should().HaveCount(2);

            var audit = await db.AuditEntries.Where(a => a.Entity == "Laboratory").ToListAsync();
            audit.Should().ContainSingle(a => a.Action == "Create");
            audit[0].Actor.Should().Be("integration-tester");

            var outbox = await db.OutboxMessages.ToListAsync();
            outbox.Should().Contain(m => m.Type == "LaboratoryRegistered");
        }
    }

    [SkippableFact]
    public async Task Duplicate_code_rolls_back_no_partial_state()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set — integration DB unavailable.");
        await _fx.ResetAsync();

        async Task<Guid> Create() =>
            await Send(new CreateLaboratoryCommand { Code = "MGL-DUP", Name = "Dup", Segment = "B", Governorate = "Cairo" });

        await Create();
        var act = async () => await Create();
        await act.Should().ThrowAsync<Application.Common.Exceptions.ConflictException>();

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        (await db.Laboratories.CountAsync(l => l.Name == "Dup")).Should().Be(1); // second attempt left nothing behind
    }

    private async Task<Guid> Send(CreateLaboratoryCommand cmd)
    {
        using var scope = _fx.Services.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return await mediator.Send(cmd);
    }
}
