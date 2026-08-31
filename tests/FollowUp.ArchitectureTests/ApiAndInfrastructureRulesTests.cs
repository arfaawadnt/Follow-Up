using System.Reflection;
using FluentAssertions;
using FollowUp.Api.Realtime;
using FollowUp.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace FollowUp.ArchitectureTests;

/// <summary>
/// API-surface, SignalR and background-job rules from the architect ruleset. The two endpoint rules are
/// ratchets: reviewed exceptions are filtered explicitly and everything else must stay clean.
/// </summary>
public class ApiAndInfrastructureRulesTests
{
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly Infrastructure = typeof(FollowUp.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Endpoints_do_not_use_domain_or_infrastructure_types()
    {
        var result = Types.InAssembly(Api).That().ResideInNamespace("FollowUp.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny("FollowUp.Domain", "FollowUp.Infrastructure")
            .GetResult();
        var failing = (result.FailingTypeNames ?? Array.Empty<string>())
            // Reviewed exception: the readiness probe pings the database (SELECT 1) via FollowUpDbContext.
            .Where(n => !n.Contains("HealthEndpoints"))
            .ToList();
        failing.Should().BeEmpty("endpoints bind request models and dispatch through MediatR only");
    }

    [Fact]
    public void Endpoints_do_not_call_repositories_directly_ratchet()
    {
        var result = Types.InAssembly(Api).That().ResideInNamespace("FollowUp.Api.Endpoints")
            .ShouldNot().HaveDependencyOn("FollowUp.Application.Common.Abstractions.Persistence")
            .GetResult();
        var failing = (result.FailingTypeNames ?? Array.Empty<string>())
            // FINDING (2026-08-27): GET /labs/nextcode injects ILaboratoryRepository; see the compliance report.
            .Where(n => !n.Contains("LaboratoryEndpoints"))
            .ToList();
        failing.Should().BeEmpty("reads go through query interfaces via MediatR, never repositories in endpoints");
    }

    [Fact]
    public void Hub_exposes_no_client_invokable_methods()
    {
        var invokable = typeof(NotificationsHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetBaseDefinition().DeclaringType == m.DeclaringType)
            .Select(m => m.Name)
            .ToList();
        invokable.Should().BeEmpty("group membership derives from the authenticated principal, never from client input");
    }

    [Fact]
    public void Hangfire_jobs_take_no_entities_and_own_no_persistence()
    {
        var jobs = Infrastructure.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Job", StringComparison.Ordinal))
            .ToList();
        jobs.Should().NotBeEmpty("the ruleset defines background jobs");

        var entityParams =
            (from j in jobs
             from m in j.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
             from p in m.GetParameters()
             where p.ParameterType != typeof(CancellationToken)
                   && (p.ParameterType.Namespace?.StartsWith("FollowUp.Domain", StringComparison.Ordinal) ?? false)
             select $"{j.Name}.{m.Name}({p.ParameterType.Name})").ToList();
        entityParams.Should().BeEmpty("job arguments must be stable primitives/ids, never domain entities");

        var dbCtors =
            (from j in jobs
             from p in j.GetConstructors().SelectMany(c => c.GetParameters())
             where p.ParameterType == typeof(FollowUpDbContext)
             select j.Name).ToList();
        dbCtors.Should().BeEmpty("jobs delegate to a use case/service; they never own persistence themselves");
    }
}
