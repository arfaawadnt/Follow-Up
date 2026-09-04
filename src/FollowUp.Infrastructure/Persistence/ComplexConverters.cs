using System.Text.Json;
using FollowUp.Domain.Common;
using FollowUp.Domain.Identity;
using FollowUp.Domain.Integration;
using FollowUp.Domain.Laboratories;
using FollowUp.Domain.Representatives;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FollowUp.Infrastructure.Persistence;

/// <summary>
/// Converters for value objects that have no public parameterless constructor and are therefore mapped
/// through a serializable surrogate to/from a JSON column. Each ships a <see cref="ValueComparer{T}"/> so EF
/// change-tracking works on the (logically immutable) value.
/// </summary>
internal static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}

// ---- LabCode <-> text ----

public sealed class LabCodeConverter : ValueConverter<LabCode, string>
{
    public LabCodeConverter() : base(c => c.Value, s => LabCode.Create(s)) { }
}

// ---- VisitSchedule <-> json ----

internal sealed record ScheduleSurrogate(int[] Days, string[] Times);

public sealed class VisitScheduleConverter : ValueConverter<VisitSchedule, string>
{
    public VisitScheduleConverter() : base(
        v => Json.Serialize(new ScheduleSurrogate(
            v.WorkDays.Select(d => (int)d).ToArray(),
            v.VisitTimes.Select(t => t.ToString("HH:mm:ss")).ToArray())),
        s => Rebuild(Json.Deserialize<ScheduleSurrogate>(s)))
    { }

    private static VisitSchedule Rebuild(ScheduleSurrogate s) =>
        VisitSchedule.Create(s.Days.Select(d => (DayOfWeek)d), s.Times.Select(TimeOnly.Parse));
}

public sealed class VisitScheduleComparer : ValueComparer<VisitSchedule>
{
    public VisitScheduleComparer() : base(
        (a, b) => a!.Equals(b),
        v => v.GetHashCode(),
        v => v) // immutable — safe to share the reference as the snapshot
    { }
}

// ---- OrgScope <-> json ----

internal sealed record ScopeSurrogate(string[] Branches, string[] Governorates, string[] Cities,
    string[] Areas, string[] Categories, string[] Segments);

public sealed class OrgScopeConverter : ValueConverter<OrgScope, string>
{
    public OrgScopeConverter() : base(
        v => Json.Serialize(new ScopeSurrogate(
            v.Branches.ToArray(), v.Governorates.ToArray(), v.Cities.ToArray(),
            v.Areas.ToArray(), v.Categories.ToArray(), v.Segments.ToArray())),
        s => Rebuild(Json.Deserialize<ScopeSurrogate>(s)))
    { }

    private static OrgScope Rebuild(ScopeSurrogate s) =>
        OrgScope.Create(s.Branches, s.Governorates, s.Cities, s.Areas, s.Categories, s.Segments);
}

public sealed class OrgScopeComparer : ValueComparer<OrgScope>
{
    public OrgScopeComparer() : base((a, b) => a!.Equals(b), v => v.GetHashCode(), v => v) { }
}

// ---- Role privileges (HashSet<string>) <-> json ----

public sealed class StringSetConverter : ValueConverter<IReadOnlySet<string>, string>
{
    public StringSetConverter() : base(
        v => Json.Serialize(v.ToArray()),
        s => new HashSet<string>(Json.Deserialize<string[]>(s), StringComparer.OrdinalIgnoreCase))
    { }
}

public sealed class StringSetComparer : ValueComparer<IReadOnlySet<string>>
{
    public StringSetComparer() : base(
        (a, b) => a!.SetEquals(b!),
        v => v.Aggregate(0, (h, x) => h ^ x.GetHashCode()),
        v => new HashSet<string>(v, StringComparer.OrdinalIgnoreCase))
    { }
}

// ---- Area transfer reps (List<RepresentativeId>) <-> json ----

public sealed class RepIdListConverter : ValueConverter<IReadOnlyCollection<RepresentativeId>, string>
{
    public RepIdListConverter() : base(
        v => Json.Serialize(v.Select(r => r.Value).ToArray()),
        // Tolerate non-array / empty jsonb (e.g. a legacy "" default) by treating it as an empty list.
        s => (Json.Deserialize<Guid[]>(string.IsNullOrWhiteSpace(s) || !s.TrimStart().StartsWith("[") ? "[]" : s) ?? Array.Empty<Guid>())
            .Select(g => new RepresentativeId(g)).ToList())
    { }
}

public sealed class RepIdListComparer : ValueComparer<IReadOnlyCollection<RepresentativeId>>
{
    public RepIdListComparer() : base(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (h, x) => h ^ x.GetHashCode()),
        v => v.ToList())
    { }
}

// ---- Plain string lists (e.g. lab image paths) <-> json ----

public sealed class StringListConverter : ValueConverter<IReadOnlyCollection<string>, string>
{
    public StringListConverter() : base(
        v => Json.Serialize(v.ToArray()),
        s => (Json.Deserialize<string[]>(string.IsNullOrWhiteSpace(s) || !s.TrimStart().StartsWith("[") ? "[]" : s) ?? Array.Empty<string>()).ToList())
    { }
}

public sealed class StringListComparer : ValueComparer<IReadOnlyCollection<string>>
{
    public StringListComparer() : base(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (h, x) => h ^ x.GetHashCode()),
        v => v.ToList())
    { }
}

public sealed class GuidListConverter : ValueConverter<IReadOnlyCollection<Guid>, string>
{
    public GuidListConverter() : base(
        v => Json.Serialize(v.ToArray()),
        s => (Json.Deserialize<Guid[]>(string.IsNullOrWhiteSpace(s) || !s.TrimStart().StartsWith("[") ? "[]" : s) ?? Array.Empty<Guid>()).ToList())
    { }
}

public sealed class GuidListComparer : ValueComparer<IReadOnlyCollection<Guid>>
{
    public GuidListComparer() : base(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (h, x) => h ^ x.GetHashCode()),
        v => v.ToList())
    { }
}

// ---- OracleConfig allow-listed queries <-> json ----

internal sealed record QuerySurrogate(string Name, string Sql);

public sealed class AllowListedQueryListConverter : ValueConverter<IReadOnlyCollection<AllowListedQuery>, string>
{
    public AllowListedQueryListConverter() : base(
        v => Json.Serialize(v.Select(q => new QuerySurrogate(q.Name, q.Sql)).ToArray()),
        s => Json.Deserialize<QuerySurrogate[]>(s).Select(q => AllowListedQuery.Create(q.Name, q.Sql)).ToList())
    { }
}

public sealed class AllowListedQueryListComparer : ValueComparer<IReadOnlyCollection<AllowListedQuery>>
{
    public AllowListedQueryListComparer() : base(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (h, x) => h ^ x.GetHashCode()),
        v => v.ToList())
    { }
}
