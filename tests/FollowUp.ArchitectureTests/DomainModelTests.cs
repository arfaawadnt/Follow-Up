using System.Reflection;
using FluentAssertions;
using FollowUp.Domain.Common;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.ArchitectureTests;

/// <summary>
/// Domain-model integrity rules from the architect ruleset: encapsulated state (no anemic model),
/// EF-only constructors, immutable value objects, and concurrency tokens wherever an aggregate declares one.
/// </summary>
public class DomainModelTests
{
    private static readonly Assembly Domain = typeof(Entity<int>).Assembly;

    private static bool DerivesFromEntity(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(Entity<>)) return true;
        return false;
    }

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    [Fact]
    public void Entities_have_no_public_mutable_setters()
    {
        var offenders =
            (from t in Domain.GetTypes()
             where DerivesFromEntity(t)
             from p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
             let s = p.SetMethod
             where s is not null && s.IsPublic && !IsInitOnly(s)
             select $"{t.Name}.{p.Name}").ToList();
        offenders.Should().BeEmpty("domain entity state must change only through behavior methods");
    }

    [Fact]
    public void Entity_collections_are_exposed_read_only()
    {
        var forbidden = new[]
        {
            typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(ISet<>),
            typeof(HashSet<>), typeof(Dictionary<,>), typeof(IDictionary<,>),
        };
        var offenders =
            (from t in Domain.GetTypes()
             where DerivesFromEntity(t)
             from p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
             where p.PropertyType.IsGenericType && forbidden.Contains(p.PropertyType.GetGenericTypeDefinition())
             select $"{t.Name}.{p.Name} ({p.PropertyType.Name})").ToList();
        offenders.Should().BeEmpty("aggregate collections must surface as IReadOnlyCollection<T> over a private field");
    }

    [Fact]
    public void Concrete_entities_have_a_non_public_parameterless_constructor_for_EF()
    {
        var offenders =
            (from t in Domain.GetTypes()
             where DerivesFromEntity(t) && !t.IsAbstract
             let ctors = t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
             where !ctors.Any(c => c.GetParameters().Length == 0 && !c.IsPublic)
             select t.Name).ToList();
        offenders.Should().BeEmpty("EF Core materializes entities through a non-public parameterless constructor");
    }

    [Fact]
    public void Value_objects_are_immutable()
    {
        var offenders =
            (from t in Domain.GetTypes()
             where typeof(ValueObject).IsAssignableFrom(t) && t != typeof(ValueObject)
             from p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
             let s = p.SetMethod
             where s is not null && s.IsPublic && !IsInitOnly(s)
             select $"{t.Name}.{p.Name}").ToList();
        offenders.Should().BeEmpty("value objects are replaced, never mutated");
    }

    [Fact]
    public void Versioned_aggregates_map_a_concurrency_token()
    {
        using var ctx = CreateOfflineContext();
        var versioned = ctx.Model.GetEntityTypes()
            .Where(e => typeof(IVersioned).IsAssignableFrom(e.ClrType))
            .ToList();
        versioned.Should().NotBeEmpty("IVersioned marks the aggregates subject to conflicting updates");
        var missing = versioned
            .Where(e => e.FindProperty(nameof(IVersioned.RowVersion))?.IsConcurrencyToken != true)
            .Select(e => e.ClrType.Name).ToList();
        missing.Should().BeEmpty("every IVersioned aggregate must map RowVersion as a concurrency token");
    }

    [Fact]
    public void Ef_model_builds_offline()
    {
        // Building the model validates every configuration/converter without touching a database.
        using var ctx = CreateOfflineContext();
        ctx.Model.GetEntityTypes().Should().NotBeEmpty();
    }

    private static FollowUpDbContext CreateOfflineContext()
    {
        var options = new DbContextOptionsBuilder<FollowUpDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new FollowUpDbContext(options);
    }
}
