using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;

namespace FollowUp.ArchitectureTests;

/// <summary>
/// Enforces Clean Architecture dependency direction (architect ruleset): Api → Infrastructure → Application →
/// Domain, and Domain references no framework/infrastructure. CI-gated, so a violation fails the build.
/// </summary>
public class DependencyDirectionTests
{
    private static readonly Assembly Domain = typeof(FollowUp.Domain.Common.Entity<int>).Assembly;
    private static readonly Assembly Application = typeof(FollowUp.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(FollowUp.Infrastructure.DependencyInjection).Assembly;

    private const string ApplicationNs = "FollowUp.Application";
    private const string InfrastructureNs = "FollowUp.Infrastructure";
    private const string ApiNs = "FollowUp.Api";

    [Fact]
    public void Domain_should_not_depend_on_any_other_layer()
    {
        var result = Types.InAssembly(Domain).Should()
            .NotHaveDependencyOnAny(ApplicationNs, InfrastructureNs, ApiNs)
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Domain_should_not_depend_on_frameworks_or_infrastructure_libraries()
    {
        var result = Types.InAssembly(Domain).Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "Npgsql", "MediatR",
                "Microsoft.AspNetCore", "Serilog", "FluentValidation", "Hangfire")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Application_should_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssembly(Application).Should()
            .NotHaveDependencyOnAny(InfrastructureNs, ApiNs)
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Application_should_not_depend_on_efcore_or_aspnetcore()
    {
        // EF Core, Npgsql and ASP.NET belong to Infrastructure/Api — the Application stays persistence- and
        // transport-agnostic (abstractions only).
        var result = Types.InAssembly(Application).Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore", "Hangfire")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    [Fact]
    public void Infrastructure_should_not_depend_on_api()
    {
        var result = Types.InAssembly(Infrastructure).Should()
            .NotHaveDependencyOnAny(ApiNs)
            .GetResult();
        result.IsSuccessful.Should().BeTrue(Because(result));
    }

    private static string Because(TestResult result) =>
        result.IsSuccessful ? string.Empty
            : "these types violate the dependency direction: " +
              string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());
}
