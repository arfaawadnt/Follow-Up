using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Setup;
using FollowUp.Application.Tests.Common;

namespace FollowUp.Application.Tests.Features.Setup;

public class RefItemHandlerTests
{
    [Fact]
    public async Task Creates_ref_item_then_rejects_duplicate_of_same_type_and_code()
    {
        var repo = new FakeRefItemRepository();
        var handler = new CreateRefItemHandler(repo);

        await handler.Handle(new CreateRefItemCommand("Governorate", "CAI", "Cairo", "القاهرة"), CancellationToken.None);
        repo.Store.Should().ContainSingle();

        var act = () => handler.Handle(new CreateRefItemCommand("Governorate", "CAI", "Cairo Again", null), CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Rejects_unknown_ref_type()
    {
        var repo = new FakeRefItemRepository();
        var handler = new CreateRefItemHandler(repo);

        var act = () => handler.Handle(new CreateRefItemCommand("Nonsense", "X", "X", null), CancellationToken.None);
        await act.Should().ThrowAsync<Domain.Common.DomainException>();
    }
}
