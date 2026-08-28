using System.Text;
using FluentAssertions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Laboratories.CreateLaboratory;
using FollowUp.Application.Features.Laboratories.GetLaboratoryById;
using FollowUp.Application.Features.Laboratories.UpdateLaboratory;
using FollowUp.Application.Features.Laboratories.UploadImage;
using FollowUp.Application.Features.Setup;
using FollowUp.Domain.Common;
using FollowUp.Domain.Compensation;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

[Collection("integration")]
public sealed class ConcurrencyAndEndpointTests
{
    private readonly IntegrationFixture _fx;
    public ConcurrencyAndEndpointTests(IntegrationFixture fx) => _fx = fx;

    private async Task<T> Send<T>(IRequest<T> request)
    {
        using var scope = _fx.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    [SkippableFact]
    public async Task Stale_update_is_rejected_with_a_conflict()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync();

        var id = await Send(new CreateLaboratoryCommand { Code = "MGL-CC1", Name = "Concurrency Lab", Segment = "A", Governorate = "Cairo" });
        var v1 = (await Send(new GetLaboratoryByIdQuery(id))).RowVersion;

        // First update with the current version succeeds (and bumps the row version).
        await Send(new UpdateLaboratoryCommand { Id = id, RowVersion = v1, Name = "Renamed Once", Segment = "A", Governorate = "Cairo" });

        // Second update using the now-stale version is rejected.
        var stale = () => Send(new UpdateLaboratoryCommand { Id = id, RowVersion = v1, Name = "Renamed Again", Segment = "A", Governorate = "Cairo" });
        await stale.Should().ThrowAsync<ConflictException>();

        // A fresh read + version succeeds again.
        var v2 = (await Send(new GetLaboratoryByIdQuery(id))).RowVersion;
        v2.Should().NotBe(v1);
        await Send(new UpdateLaboratoryCommand { Id = id, RowVersion = v2, Name = "Renamed Fresh", Segment = "B", Governorate = "Cairo" });
        (await Send(new GetLaboratoryByIdQuery(id))).Name.Should().Be("Renamed Fresh");
    }

    [SkippableFact]
    public async Task Concurrent_updates_to_a_newly_versioned_aggregate_are_rejected()
    {
        // Proves the xmin concurrency token added to the previously last-writer-wins aggregates is live
        // (CompensationConfig here — CPN-9; the same mapping covers Complaint/AppUser/Role/DailyVisit).
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            if (!await db.CompensationConfigs.AnyAsync())
            {
                db.CompensationConfigs.Add(CompensationConfig.Create(5m, 100m, new Money(500m),
                    new[] { new LoyaltyTier("Gold", 100m, 500) }));
                await db.SaveChangesAsync();
            }
        }

        // Two independent contexts (separate connections) read the same row — hence the same xmin.
        using var s1 = _fx.Services.CreateScope();
        using var s2 = _fx.Services.CreateScope();
        var db1 = s1.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var db2 = s2.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var c1 = await db1.CompensationConfigs.FirstAsync();
        var c2 = await db2.CompensationConfigs.FirstAsync();

        c1.SetCommission(6m, 100m, new Money(500m));
        c2.SetCommission(7m, 100m, new Money(500m));

        await db1.SaveChangesAsync(); // first writer wins and bumps xmin

        var stale = async () => await db2.SaveChangesAsync(); // now-stale xmin -> concurrency conflict
        await stale.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [SkippableFact]
    public async Task Image_upload_sniffs_type_and_rejects_non_images()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        // A real PNG header is accepted and stored.
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        var path = await Send(new UploadLabImageCommand(png));
        path.Should().StartWith("/uploads/").And.EndWith(".png");

        // A text file (wrong magic bytes) is refused regardless of any claimed type.
        var bad = () => Send(new UploadLabImageCommand(Encoding.UTF8.GetBytes("not an image")));
        await bad.Should().ThrowAsync<ValidationException>();
    }

    [SkippableFact]
    public async Task Settings_mask_secret_values_on_read()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        await Send<MediatR.Unit>(new UpsertSettingCommand("smtp.password", "super-secret", IsSecret: true));
        var settings = await Send(new GetSettingsQuery());

        var secret = settings.First(s => s.Key == "smtp.password");
        secret.IsSecret.Should().BeTrue();
        secret.Value.Should().Be("********");
    }

    [SkippableFact]
    public async Task Retention_enforces_a_30_day_minimum()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        var tooLow = () => Send<MediatR.Unit>(new SetRetentionCommand(10));
        await tooLow.Should().ThrowAsync<ValidationException>();

        await Send<MediatR.Unit>(new SetRetentionCommand(45));
        (await Send(new GetRetentionQuery())).Days.Should().Be(45);
    }
}
