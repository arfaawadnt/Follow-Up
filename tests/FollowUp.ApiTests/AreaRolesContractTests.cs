using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace FollowUp.ApiTests;

/// <summary>
/// Management roles over the wire: reps of the AreaManager / LabResponsible types can be created, an area accepts a
/// manager of the matching type (and reads it back, and can clear it), a lab accepts a responsible of type LabResponsible
/// (and reads it back), and a wrong-type assignment is a 400 on both. The area body no longer carries a responsible.
/// </summary>
[Collection("api")]
public sealed class AreaRolesContractTests
{
    private readonly ApiFixture _fx;
    public AreaRolesContractTests(ApiFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Area_manager_and_lab_responsible_round_trip_and_a_wrong_type_rep_is_rejected()
    {
        Skip.IfNot(_fx.AuthReady, "admin token unavailable (FOLLOWUP_DB / seeded admin).");
        using var c = _fx.CreateAuthedClient();
        var tag = Guid.NewGuid().ToString("N")[..8];

        var mgr = await PostIdAsync(c, "/api/v1/reps", new { fullName = $"Mgr {tag}", type = "AreaManager", goalDuration = "Monthly", salary = 0, target = 0 });
        var resp = await PostIdAsync(c, "/api/v1/reps", new { fullName = $"Resp {tag}", type = "LabResponsible", goalDuration = "Monthly", salary = 0, target = 0 });
        var oldName = await c.PostAsJsonAsync("/api/v1/reps", new { fullName = $"Old {tag}", type = "AreaResponsible", goalDuration = "Monthly", salary = 0, target = 0 });
        oldName.StatusCode.Should().Be(HttpStatusCode.BadRequest, "the AreaResponsible type no longer exists");
        var city = await PostIdAsync(c, "/api/v1/setup/cities", new { name = $"City {tag}", governorate = "Cairo" });

        // ---- Area: manager only.
        var area = await PostIdAsync(c, "/api/v1/setup/areas", new
        {
            name = $"Area {tag}",
            cityId = city,
            transportationRequired = false,
            transferReps = Array.Empty<Guid>(),
            areaManagerId = mgr,
        });
        var row = (await c.GetFromJsonAsync<List<AreaRow>>("/api/v1/setup/areas"))!.Single(a => a.Id == area);
        row.AreaManagerId.Should().Be(mgr);

        // A LabResponsible offered as the manager violates the type binding → 400.
        var bad = await c.PutAsJsonAsync($"/api/v1/setup/areas/{area}",
            new { name = $"Area {tag}", cityId = city, transportationRequired = false, areaManagerId = resp });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Clearing the manager is allowed.
        var cleared = await c.PutAsJsonAsync($"/api/v1/setup/areas/{area}",
            new { name = $"Area {tag}", cityId = city, transportationRequired = false, areaManagerId = (Guid?)null });
        cleared.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetFromJsonAsync<List<AreaRow>>("/api/v1/setup/areas"))!.Single(a => a.Id == area).AreaManagerId.Should().BeNull();

        // ---- Lab: responsible of type LabResponsible, read back on the detail and the list.
        var code = $"MGL-LR{tag[..6].ToUpperInvariant()}";
        var lab = await PostIdAsync(c, "/api/v1/labs", new
        {
            code,
            name = $"Lab {tag}",
            segment = "A",
            governorate = "Cairo",
            workDays = Array.Empty<string>(),
            visitTimes = Array.Empty<string>(),
            responsibleRepId = resp,
        });
        var detail = await c.GetFromJsonAsync<LabDetailRow>($"/api/v1/labs/{lab}");
        detail!.ResponsibleRepId.Should().Be(resp);
        var list = await c.GetFromJsonAsync<Paged<LabListRow>>($"/api/v1/labs?search={Uri.EscapeDataString($"Lab {tag}")}&pageSize=5");
        list!.Items.Should().Contain(l => l.Id == lab && l.Responsible == $"Resp {tag}");

        // An AreaManager offered as the lab responsible → 400, and no lab is created.
        var badLab = await c.PostAsJsonAsync("/api/v1/labs", new
        {
            code = $"MGL-LX{tag[..6].ToUpperInvariant()}",
            name = $"Lab X {tag}",
            segment = "A",
            governorate = "Cairo",
            workDays = Array.Empty<string>(),
            visitTimes = Array.Empty<string>(),
            responsibleRepId = mgr,
        });
        badLab.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<Guid> PostIdAsync(HttpClient c, string url, object body)
    {
        var r = await c.PostAsJsonAsync(url, body);
        r.StatusCode.Should().Be(HttpStatusCode.Created, $"POST {url} should create");
        return (await r.Content.ReadFromJsonAsync<IdResponse>())!.Id;
    }

    private sealed record IdResponse(Guid Id);
    private sealed record AreaRow(Guid Id, Guid? AreaManagerId);
    private sealed record LabDetailRow(Guid Id, Guid? ResponsibleRepId);
    private sealed record LabListRow(Guid Id, string? Responsible);
    private sealed record Paged<T>(List<T> Items);
}
