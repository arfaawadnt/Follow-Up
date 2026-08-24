using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;

namespace FollowUp.Application.Tests.Features.Laboratories;

public class CreateLaboratoryHandlerTests
{
    private static CreateLaboratoryCommand ValidCommand() => new()
    {
        Code = "MGL-0001",
        Name = "Nile Diagnostics",
        Segment = "B",
        Governorate = "Cairo",
        WorkDays = new[] { "Monday", "Wednesday" },
        VisitTimes = new[] { "09:00", "14:30" },
        Contacts = new[] { new NewContact("Dr. Sara", "Manager", "01000000000", new DateOnly(1985, 6, 1)) },
    };

    [Fact]
    public async Task Creates_a_laboratory_and_returns_its_id()
    {
        var repo = new FakeLaboratoryRepository();
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.AddLabs } };
        var handler = new CreateLaboratoryHandler(repo, user, new FakeSetupQueries());

        var id = await handler.Handle(ValidCommand(), CancellationToken.None);

        id.Should().NotBeEmpty();
        repo.Store.Should().ContainSingle();
        var lab = repo.Store[0];
        lab.Code.Value.Should().Be("MGL-0001");
        lab.Contacts.Should().ContainSingle();
        lab.Schedule.VisitTimes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Rejects_duplicate_code_with_conflict()
    {
        var repo = new FakeLaboratoryRepository();
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.AddLabs } };
        var handler = new CreateLaboratoryHandler(repo, user, new FakeSetupQueries());
        await handler.Handle(ValidCommand(), CancellationToken.None);

        var act = () => handler.Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Rejects_creation_outside_scope()
    {
        var repo = new FakeLaboratoryRepository();
        // Scope limited to Giza; the command targets Cairo.
        var scope = OrgScope.Create(new[] { "*" }, new[] { "Giza" }, new[] { "*" },
            new[] { "*" }, new[] { "*" }, new[] { "*" });
        var user = new FakeCurrentUser { Privileges = new HashSet<string> { Privileges.AddLabs }, Scope = scope };
        var handler = new CreateLaboratoryHandler(repo, user, new FakeSetupQueries());

        var act = () => handler.Handle(ValidCommand(), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        repo.Store.Should().BeEmpty();
    }
}
