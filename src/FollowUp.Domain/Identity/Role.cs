using FollowUp.Domain.Common;

namespace FollowUp.Domain.Identity;

public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// A dynamic, data-defined role (SRS FR-2). Bundles a privilege set, default language/theme, and the
/// six-dimension org scope. The built-in <c>admin</c> role and in-use roles are protected from deletion.
/// </summary>
public sealed class Role : AggregateRoot<RoleId>, IVersioned, IAuditable
{
    private readonly HashSet<string> _privileges = new(StringComparer.OrdinalIgnoreCase);

    private Role() { } // EF

    private Role(RoleId id, string name, string defaultLanguage, string defaultTheme, OrgScope scope, bool isBuiltIn)
        : base(id)
    {
        Name = name;
        DefaultLanguage = defaultLanguage;
        DefaultTheme = defaultTheme;
        Scope = scope;
        IsBuiltIn = isBuiltIn;
    }

    public string Name { get; private set; } = null!;

    /// <summary>Optimistic-concurrency token (Postgres xmin); concurrent edits conflict (409). Finding IDN-4.</summary>
    public uint RowVersion { get; private set; }
    public string DefaultLanguage { get; private set; } = "en";
    public string DefaultTheme { get; private set; } = "light";
    public OrgScope Scope { get; private set; } = null!;
    public bool IsBuiltIn { get; private set; }

    public IReadOnlySet<string> Privileges => _privileges;

    /// <summary>The effective privilege set with coarse grants expanded (SRS §2.1).</summary>
    public IReadOnlySet<string> EffectivePrivileges => Identity.Privileges.Expand(_privileges);

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = null!;
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public static Role Create(string name, IEnumerable<string> privileges, string defaultLanguage,
        string defaultTheme, OrgScope scope, bool isBuiltIn = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Role name is required.");
        var role = new Role(RoleId.New(), name.Trim(), defaultLanguage, defaultTheme, scope, isBuiltIn);
        role.SetPrivileges(privileges);
        return role;
    }

    public void SetPrivileges(IEnumerable<string> privileges)
    {
        _privileges.Clear();
        foreach (var p in privileges ?? Enumerable.Empty<string>())
        {
            if (!Identity.Privileges.All.Contains(p))
                throw new DomainException($"Unknown privilege '{p}'.");
            _privileges.Add(p);
        }
    }

    public void SetScope(OrgScope scope) => Scope = scope ?? OrgScope.Deny;

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Role name is required.");
        Name = name.Trim();
    }

    public void SetDefaults(string language, string theme)
    {
        DefaultLanguage = language;
        DefaultTheme = theme;
    }

    public bool Has(string privilege) => EffectivePrivileges.Contains(privilege);
}
