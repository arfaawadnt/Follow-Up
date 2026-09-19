using System.Text.Json;
using FollowUp.Application.Common.Messaging;
using FollowUp.Application.Common.Models;
using FollowUp.Domain.Identity;
using FluentValidation;

namespace FollowUp.Application.Features.Audit;

/// <summary>One changed field of an audit entry: the property name and its value before / after (flattened to text).</summary>
public sealed record AuditChangeDto(string Field, string? Before, string? After);

public sealed record AuditRowDto(
    Guid Id, DateTimeOffset OccurredAt, string Actor, string Entity, string EntityId, string Action,
    string? Before, string? After, string? CorrelationId, IReadOnlyList<AuditChangeDto>? Changes = null);

/// <summary>Read-side query interface for the audit trail (SRS FR-20).</summary>
public interface IAuditQueries
{
    Task<PagedResult<AuditRowDto>> SearchAsync(AuditSearchCriteria criteria, CancellationToken ct);
    /// <summary>The full history of ONE record (every create / update / delete), oldest first, with the changed fields.</summary>
    Task<IReadOnlyList<AuditRowDto>> ForEntityAsync(string entity, string entityId, CancellationToken ct);
    /// <summary>The distinct entity type names and actor names present in the trail (the page's filter pickers).</summary>
    Task<(IReadOnlyList<string> Entities, IReadOnlyList<string> Actors)> FacetsAsync(CancellationToken ct);
}

public sealed record AuditSearchCriteria : ListQuery
{
    public string? Entity { get; init; }
    public string? EntityId { get; init; }
    public string? Actor { get; init; }
    public string? Action { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>Queries the immutable audit trail (SRS FR-20). ViewAuditTrail (implied by ManageUsers).</summary>
public sealed record GetAuditQuery : IQuery<PagedResult<AuditRowDto>>, IAuthorizedRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Entity { get; init; }
    public string? EntityId { get; init; }
    public string? Actor { get; init; }
    public string? Action { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }

    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAuditTrail };
}

public sealed class GetAuditHandler : IQueryHandler<GetAuditQuery, PagedResult<AuditRowDto>>
{
    private readonly IAuditQueries _queries;

    public GetAuditHandler(IAuditQueries queries) => _queries = queries;

    public Task<PagedResult<AuditRowDto>> Handle(GetAuditQuery request, CancellationToken ct)
    {
        var criteria = new AuditSearchCriteria
        {
            Page = request.Page,
            PageSize = request.PageSize,
            Entity = request.Entity,
            EntityId = request.EntityId,
            Actor = request.Actor,
            Action = request.Action,
            From = request.From,
            To = request.To,
        };
        return _queries.SearchAsync(criteria, ct);
    }
}

/// <summary>The "log" button of a record (2026-09-20): who created / changed it, when, and what changed — every page
/// shows it for its rows, so it needs only ViewAuditTrail, not the admin-only user management.</summary>
public sealed record GetEntityAuditQuery(string Entity, string EntityId) : IQuery<IReadOnlyList<AuditRowDto>>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAuditTrail };
}

public sealed class GetEntityAuditValidator : AbstractValidator<GetEntityAuditQuery>
{
    public GetEntityAuditValidator()
    {
        RuleFor(x => x.Entity).NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9_]+$");
        RuleFor(x => x.EntityId).NotEmpty().MaximumLength(100);
    }
}

public sealed class GetEntityAuditHandler : IQueryHandler<GetEntityAuditQuery, IReadOnlyList<AuditRowDto>>
{
    private readonly IAuditQueries _queries;
    public GetEntityAuditHandler(IAuditQueries queries) => _queries = queries;
    public Task<IReadOnlyList<AuditRowDto>> Handle(GetEntityAuditQuery request, CancellationToken ct) =>
        _queries.ForEntityAsync(request.Entity.Trim(), request.EntityId.Trim(), ct);
}

public sealed record AuditFacetsDto(IReadOnlyList<string> Entities, IReadOnlyList<string> Actors);

public sealed record GetAuditFacetsQuery : IQuery<AuditFacetsDto>, IAuthorizedRequest
{
    public IReadOnlyCollection<string> RequiredPrivileges { get; } = new[] { Privileges.ViewAuditTrail };
}

public sealed class GetAuditFacetsHandler : IQueryHandler<GetAuditFacetsQuery, AuditFacetsDto>
{
    private readonly IAuditQueries _queries;
    public GetAuditFacetsHandler(IAuditQueries queries) => _queries = queries;
    public async Task<AuditFacetsDto> Handle(GetAuditFacetsQuery request, CancellationToken ct)
    {
        var (entities, actors) = await _queries.FacetsAsync(ct);
        return new AuditFacetsDto(entities, actors);
    }
}

/// <summary>
/// Turns an audit entry's before / after snapshots (the interceptor serializes every scalar property of the aggregate,
/// CLR-shaped: <c>{"Amount":12.5}</c> for Money, <c>{"Value":"guid"}</c> for a typed id, <c>{"Id":1,"Name":"Rep"}</c>
/// for an enumeration) into the list of fields that changed, each flattened to a display string. Provenance stamps and
/// concurrency tokens are noise and are left out. A Create lists every field that has a value; a Delete every field it had.
/// </summary>
public static class AuditDiff
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "RowVersion", "Version", "ContentVersion", "DomainEvents" };

    public static IReadOnlyList<AuditChangeDto> Compute(string? beforeJson, string? afterJson)
    {
        var before = Parse(beforeJson); var after = Parse(afterJson);
        var fields = before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Where(f => !Ignored.Contains(f)).ToList();
        var changes = new List<AuditChangeDto>();
        foreach (var f in fields)
        {
            before.TryGetValue(f, out var b); after.TryGetValue(f, out var a);
            if (beforeJson is null && afterJson is not null) { if (a is not null) changes.Add(new AuditChangeDto(f, null, a)); }
            else if (afterJson is null && beforeJson is not null) { if (b is not null) changes.Add(new AuditChangeDto(f, b, null)); }
            else if (!string.Equals(b, a, StringComparison.Ordinal)) changes.Add(new AuditChangeDto(f, b, a));
        }
        return changes.OrderBy(c => c.Field, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, string?> Parse(string? json)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var p in doc.RootElement.EnumerateObject()) result[p.Name] = Flatten(p.Value);
        }
        catch (JsonException) { /* a malformed legacy snapshot shows as "no changes" rather than failing the page */ }
        return result;
    }

    /// <summary>Display text for a snapshot value: scalars as-is; the single-value wrappers (typed ids, Money, enumerations)
    /// unwrapped; anything else as compact JSON.</summary>
    public static string? Flatten(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Null or JsonValueKind.Undefined: return null;
            case JsonValueKind.String:
                {
                    var s = e.GetString();
                    // Timestamps serialize with the full offset precision; keep them readable.
                    return s is not null && s.Length >= 19 && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                        ? dt.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture) : s;
                }
            case JsonValueKind.Number:
                // Normalized so a value read back from the database (2000.00) never differs from the one written (2000).
                return e.TryGetDecimal(out var dec) ? dec.ToString("0.############", System.Globalization.CultureInfo.InvariantCulture) : e.GetRawText();
            case JsonValueKind.True: return "true";
            case JsonValueKind.False: return "false";
            case JsonValueKind.Array:
                {
                    var items = e.EnumerateArray().Select(Flatten).ToList();
                    return items.Count == 0 ? "" : items.All(i => i is not null && !i.StartsWith('{')) ? string.Join(", ", items) : e.GetRawText();
                }
            case JsonValueKind.Object:
                {
                    var props = e.EnumerateObject().ToList();
                    if (props.Count == 1 && props[0].Name is "Value" or "Amount") return Flatten(props[0].Value);
                    var name = props.FirstOrDefault(p => p.Name == "Name");
                    if (name.Value.ValueKind == JsonValueKind.String && props.Count <= 3 && props.Any(p => p.Name == "Id")) return name.Value.GetString();
                    var amount = props.FirstOrDefault(p => p.Name == "Amount");
                    if (amount.Value.ValueKind == JsonValueKind.Number && props.Count <= 2) return Flatten(amount.Value);
                    return e.GetRawText();
                }
            default: return e.GetRawText();
        }
    }
}
