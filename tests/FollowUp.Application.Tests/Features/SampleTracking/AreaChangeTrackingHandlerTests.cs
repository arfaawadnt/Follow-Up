using FluentAssertions;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Features.SampleTracking;
using FollowUp.Application.Tests.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Laboratories;
using Microsoft.Extensions.Logging.Abstractions;
using DomainSampleTracking = FollowUp.Domain.Operations.SampleTracking;

namespace FollowUp.Application.Tests.Features.SampleTracking;

public class AreaChangeTrackingHandlerTests
{
    private static readonly DateOnly Day = new(2026, 8, 27);

    private sealed class FakeTrackingQueries : ISampleTrackingQueries
    {
        public List<DateOnly> Dates { get; init; } = new();
        public Dictionary<(string Area, DateOnly Date), int> Sums { get; init; } = new();
        public bool Throw { get; init; }

        public Task<IReadOnlyList<DateOnly>> GetReceivedVisitDatesAsync(LaboratoryId laboratoryId, CancellationToken ct)
        {
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult<IReadOnlyList<DateOnly>>(Dates);
        }

        public Task<int> SumReceivedSamplesAsync(string area, DateOnly date, CancellationToken ct) =>
            Task.FromResult(Sums.GetValueOrDefault((area, date)));

        public Task<IReadOnlyList<SampleTrackingDto>> ListAsync(DateOnly start, DateOnly end, OrgScope scope, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SampleLifecycleReportRowDto>> ReportAsync(DateOnly from, DateOnly to, OrgScope scope, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SampleLifecycleRowDto>> LifecycleAsync(DateOnly from, DateOnly to, OrgScope scope, bool canSeeEncrypted, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static AreaChangeTrackingHandler Handler(FakeSampleTrackingRepository repo, FakeTrackingQueries queries) =>
        new(repo, queries, NullLogger<AreaChangeTrackingHandler>.Instance);

    [Fact]
    public async Task Moves_received_totals_to_the_new_area_and_drops_the_emptied_row()
    {
        var repo = new FakeSampleTrackingRepository();
        var oldRow = DomainSampleTracking.Open("Cairo", Day);
        oldRow.SetCount(6);
        repo.Store.Add(oldRow);
        var queries = new FakeTrackingQueries
        {
            Dates = { Day },
            Sums = { [("Cairo", Day)] = 0, [("Giza", Day)] = 6 }
        };

        await Handler(repo, queries).Handle(
            new DomainEventNotification(new LaboratoryAreaChanged(LaboratoryId.New(), "Cairo", "Giza")), CancellationToken.None);

        repo.Store.Should().ContainSingle(r => r.Area == "Giza" && r.Date == Day && r.Count == 6);
        repo.Store.Should().NotContain(r => r.Area == "Cairo"); // untouched empty row is dropped
    }

    [Fact]
    public async Task Keeps_an_emptied_row_that_staff_already_worked()
    {
        var repo = new FakeSampleTrackingRepository();
        var oldRow = DomainSampleTracking.Open("Cairo", Day);
        oldRow.RecordDataEntry(6, "admin", DateTimeOffset.UtcNow);
        repo.Store.Add(oldRow);
        var queries = new FakeTrackingQueries
        {
            Dates = { Day },
            Sums = { [("Cairo", Day)] = 0, [("Giza", Day)] = 6 }
        };

        await Handler(repo, queries).Handle(
            new DomainEventNotification(new LaboratoryAreaChanged(LaboratoryId.New(), "Cairo", "Giza")), CancellationToken.None);

        repo.Store.Should().Contain(oldRow); // same instance survives, not a recreated one
        oldRow.Count.Should().Be(0);
        oldRow.DataEntry.Should().NotBeNull(); // assignment history preserved
        repo.Store.Should().Contain(r => r.Area == "Giza" && r.Count == 6);
    }

    [Fact]
    public async Task Keeps_a_notes_only_emptied_row()
    {
        var repo = new FakeSampleTrackingRepository();
        var oldRow = DomainSampleTracking.Open("Cairo", Day);
        oldRow.SetCount(6);
        oldRow.SetNotes("recount pending");
        repo.Store.Add(oldRow);
        var queries = new FakeTrackingQueries
        {
            Dates = { Day },
            Sums = { [("Cairo", Day)] = 0, [("Giza", Day)] = 6 }
        };

        await Handler(repo, queries).Handle(
            new DomainEventNotification(new LaboratoryAreaChanged(LaboratoryId.New(), "Cairo", "Giza")), CancellationToken.None);

        repo.Store.Should().Contain(oldRow); // a note makes the row worked — never dropped
        oldRow.Count.Should().Be(0);
    }

    [Fact]
    public async Task Updates_an_existing_destination_row_instead_of_duplicating()
    {
        var repo = new FakeSampleTrackingRepository();
        var giza = DomainSampleTracking.Open("Giza", Day);
        giza.SetCount(3);
        repo.Store.Add(giza);
        var queries = new FakeTrackingQueries
        {
            Dates = { Day },
            Sums = { [("Giza", Day)] = 9 }
        };

        await Handler(repo, queries).Handle(
            new DomainEventNotification(new LaboratoryAreaChanged(LaboratoryId.New(), null, "Giza")), CancellationToken.None);

        repo.Store.Should().ContainSingle(r => r.Area == "Giza");
        giza.Count.Should().Be(9);
    }

    [Fact]
    public async Task Redelivery_reaches_the_same_state()
    {
        var repo = new FakeSampleTrackingRepository();
        var queries = new FakeTrackingQueries
        {
            Dates = { Day },
            Sums = { [("Cairo", Day)] = 0, [("Giza", Day)] = 6 }
        };
        var notification = new DomainEventNotification(new LaboratoryAreaChanged(LaboratoryId.New(), "Cairo", "Giza"));

        await Handler(repo, queries).Handle(notification, CancellationToken.None);
        await Handler(repo, queries).Handle(notification, CancellationToken.None);

        repo.Store.Should().ContainSingle(r => r.Area == "Giza" && r.Date == Day && r.Count == 6);
    }

    [Fact]
    public async Task Backfills_rows_when_an_area_is_assigned_for_the_first_time()
    {
        var repo = new FakeSampleTrackingRepository();
        var d1 = Day; var d2 = Day.AddDays(-1);
        var queries = new FakeTrackingQueries
        {
            Dates = { d1, d2 },
            Sums = { [("Cairo", d1)] = 6, [("Cairo", d2)] = 4 }
        };

        await Handler(repo, queries).Handle(
            new DomainEventNotification(new LaboratoryAreaChanged(LaboratoryId.New(), null, "Cairo")), CancellationToken.None);

        repo.Store.Should().HaveCount(2);
        repo.Store.Should().Contain(r => r.Area == "Cairo" && r.Date == d1 && r.Count == 6);
        repo.Store.Should().Contain(r => r.Area == "Cairo" && r.Date == d2 && r.Count == 4);
    }

    [Fact]
    public async Task Does_nothing_when_the_lab_never_received_samples()
    {
        var repo = new FakeSampleTrackingRepository();
        var queries = new FakeTrackingQueries(); // no dates

        await Handler(repo, queries).Handle(
            new DomainEventNotification(new LaboratoryAreaChanged(LaboratoryId.New(), "Cairo", "Giza")), CancellationToken.None);

        repo.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Ignores_unrelated_domain_events()
    {
        var repo = new FakeSampleTrackingRepository();
        var queries = new FakeTrackingQueries { Throw = true }; // would blow up if consulted

        await Handler(repo, queries).Handle(
            new DomainEventNotification(new LaboratoryScheduleChanged(LaboratoryId.New())), CancellationToken.None);

        repo.Store.Should().BeEmpty();
    }

    [Fact]
    public async Task Propagates_failures_so_the_outbox_retries_the_message()
    {
        var repo = new FakeSampleTrackingRepository();
        var queries = new FakeTrackingQueries { Throw = true };

        var act = async () => await Handler(repo, queries).Handle(
            new DomainEventNotification(new LaboratoryAreaChanged(LaboratoryId.New(), "Cairo", "Giza")), CancellationToken.None);

        // The recompute is idempotent, so the bounded outbox retry must see the failure.
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
