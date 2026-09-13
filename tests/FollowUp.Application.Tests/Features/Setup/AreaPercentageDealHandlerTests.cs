using FluentAssertions;
using FollowUp.Application.Features.Setup;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Reference;

namespace FollowUp.Application.Tests.Features.Setup;

/// <summary>
/// Area "Percentage Deal" through the create/update handlers and their validators: the deal round-trips, switching it
/// off clears the percentage, and the validators refuse a missing or out-of-range percentage while the deal is on.
/// </summary>
public class AreaPercentageDealHandlerTests
{
    private static CreateAreaHandler CreateHandler(FakeAreaRepository areas) =>
        new(areas, new FakeRepresentativeRepository(), new FakeCurrentUser());

    private static UpdateAreaHandler UpdateHandler(FakeAreaRepository areas) =>
        new(areas, new FakeRepresentativeRepository(), new FakeCurrentUser());

    [Fact]
    public async Task Create_with_the_deal_on_stores_the_percentage()
    {
        var areas = new FakeAreaRepository();

        var id = await CreateHandler(areas).Handle(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            PercentageDeal: true, Percentage: 15m), CancellationToken.None);

        var area = areas.Store.Single(a => a.Id.Value == id);
        area.PercentageDeal.Should().BeTrue();
        area.Percentage.Should().Be(15m);
    }

    [Fact]
    public async Task Create_with_the_deal_off_discards_any_stray_percentage()
    {
        var areas = new FakeAreaRepository();

        var id = await CreateHandler(areas).Handle(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            PercentageDeal: false, Percentage: 40m), CancellationToken.None);

        var area = areas.Store.Single(a => a.Id.Value == id);
        area.PercentageDeal.Should().BeFalse();
        area.Percentage.Should().BeNull("a percentage is meaningless with the deal off and must not be stored");
    }

    [Fact]
    public async Task Update_switching_the_deal_off_clears_the_percentage()
    {
        var cityId = CityId.New();
        var area = Area.Create("Nasr City", cityId, false);
        area.SetPercentageDeal(true, 20m);
        var areas = new FakeAreaRepository(); areas.Store.Add(area);

        await UpdateHandler(areas).Handle(new UpdateAreaCommand(area.Id.Value, "Nasr City", cityId.Value, false,
            PercentageDeal: false, Percentage: null), CancellationToken.None);

        area.PercentageDeal.Should().BeFalse();
        area.Percentage.Should().BeNull();
    }

    [Fact]
    public async Task Update_can_change_the_percentage_of_an_active_deal()
    {
        var cityId = CityId.New();
        var area = Area.Create("Nasr City", cityId, false);
        area.SetPercentageDeal(true, 20m);
        var areas = new FakeAreaRepository(); areas.Store.Add(area);

        await UpdateHandler(areas).Handle(new UpdateAreaCommand(area.Id.Value, "Nasr City", cityId.Value, false,
            PercentageDeal: true, Percentage: 33.33m), CancellationToken.None);

        area.Percentage.Should().Be(33.33m);
    }

    [Fact]
    public void Create_validator_requires_a_percentage_while_the_deal_is_on()
    {
        var result = new CreateAreaValidator().Validate(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            PercentageDeal: true, Percentage: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateAreaCommand.Percentage));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Create_validator_refuses_a_percentage_outside_zero_to_one_hundred(double pct)
    {
        var result = new CreateAreaValidator().Validate(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            PercentageDeal: true, Percentage: (decimal)pct));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Create_validator_ignores_the_percentage_while_the_deal_is_off()
    {
        // The domain discards it; the validator must not block the save over an irrelevant value.
        var result = new CreateAreaValidator().Validate(new CreateAreaCommand("Nasr City", Guid.NewGuid(), false, Array.Empty<Guid>(),
            PercentageDeal: false, Percentage: 999m));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Update_validator_requires_a_percentage_while_the_deal_is_on()
    {
        var result = new UpdateAreaValidator().Validate(new UpdateAreaCommand(Guid.NewGuid(), "Nasr City", Guid.NewGuid(), false,
            PercentageDeal: true, Percentage: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(UpdateAreaCommand.Percentage));
    }
}
