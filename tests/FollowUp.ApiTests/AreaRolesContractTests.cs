using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace FollowUp.ApiTests;

/// <summary>
/// Area management roles over the wire: reps of the two new types can be created, an area accepts a manager and a
/// responsible of the matching types (and reads them back), a wrong-type assignment is a 400, and roles can be cleared.
/// </summary>
[Collection("api")]
public sealed class AreaRolesContractTests
{
    private readonly ApiFixture _fx;
    public AreaRolesContractTests(ApiFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Area_roles_round_trip_and_a_wrong_type_rep_is_rejected()
    {
        Skip.IfNot(_fx.AuthReady, "admin token unavailable (FOLLOWUP_DB / seeded admin).");
        using var c = _fx.CreateAuthedClient();
        var tag = Guid.NewGuid().ToString("N")[..8];

        var mgr = await PostIdAsync(c, "/api/v1/reps", new { fullName = $"Mgr {tag}", type = "AreaManager", goalDuration = "Monthly", salary = 0, target = 0 });
        var resp = await PostIdAsync(c, "/api/v1/reps", new { fullName = $"Resp {tag}", type = "AreaResponsible", goalDuration = "Monthly", salary = 0, target = 0 });
        var city = await PostIdAsync(c, "/api/v1/setup/cities", new { name = $"City {tag}", governorate = "Cairo" });

        var area = await PostIdAsync(c, "/api/v1/setup/areas", new
        {
            name = $"Area {tag}",
            cityId = city,
            transportationRequired = false,
            transferReps = Array.Empty<Guid>(),
            areaManagerId = mgr,
            areaResponsibleId = resp,
        });

        var rows = await c.GetFromJsonAsync<List<AreaRow>>("/api/v1/setup/areas");
        var row = rows!.Single(a => a.Id == area);
        row.AreaManagerId.Should().Be(mgr);
        row.AreaResponsibleId.Should().Be(resp);

        // An AreaResponsible offered as the manager violates the type binding → 400.
        var bad = await c.PutAsJsonAsync($"/api/v1/setup/areas/{area}",
            new { name = $"Area {tag}", cityId = city, transportationRequired = false, areaManagerId = resp, areaResponsibleId = resp });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Clearing both roles is allowed.
        var cleared = await c.PutAsJsonAsync($"/api/v1/setup/areas/{area}",
            new { name = $"Area {tag}", cityId = city, transportationRequired = false, areaManagerId = (Guid?)null, areaResponsibleId = (Guid?)null });
        cleared.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = (await c.GetFromJsonAsync<List<AreaRow>>("/api/v1/setup/areas"))!.Single(a => a.Id == area);
        after.AreaManagerId.Should().BeNull();
        after.AreaResponsibleId.Should().BeNull();
    }

    private static async Task<Guid> PostIdAsync(HttpClient c, string url, object body)
    {
        var r = await c.PostAsJsonAsync(url, body);
        r.StatusCode.Should().Be(HttpStatusCode.Created, $"POST {url} should create");
        return (await r.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private sealed record IdResponse(Guid Id);
    private sealed record AreaRow(Guid Id, Guid? AreaManagerId, Guid? AreaResponsibleId);
}
