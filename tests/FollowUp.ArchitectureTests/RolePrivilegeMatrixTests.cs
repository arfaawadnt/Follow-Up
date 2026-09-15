using System.Text.RegularExpressions;
using FluentAssertions;
using FollowUp.Domain.Identity;

namespace FollowUp.ArchitectureTests;

/// <summary>
/// Keeps the Role Privilege page (web/src/app/features/roles/roles.component.ts) in lock-step with the backend
/// privilege catalogue (<see cref="Privileges.All"/>). The page renders a hand-written matrix of checkboxes; between
/// 2026-08-27 and 2026-09-13 seven privileges were added to the backend without a checkbox, so operators could not
/// grant them from the UI at all. This ratchet fails the build in both directions: a backend privilege with no
/// checkbox, or a checkbox naming a privilege the backend does not know (a typo would silently grant nothing).
/// </summary>
public class RolePrivilegeMatrixTests
{
    private const string RolesComponentRelativePath = "web/src/app/features/roles/roles.component.ts";

    // Privilege names appear in the component only in these shapes:
    //   view: 'X' | add: 'X' | update: 'X' | priv: 'X'      (the MATRIX rows)
    //   togglePriv('X', ...) | draft().has('X')              (the standalone Verify / Resolve checkboxes)
    private static readonly Regex PrivilegeRefs = new(
        @"(?:\b(?:view|add|update|priv)\s*:\s*'([A-Za-z]+)')|(?:togglePriv\(\s*'([A-Za-z]+)')|(?:draft\(\)\.has\(\s*'([A-Za-z]+)'\))",
        RegexOptions.Compiled);

    [Fact]
    public void Every_backend_privilege_has_a_checkbox_on_the_Role_Privilege_page()
    {
        var referenced = ReadReferencedPrivileges();

        var missing = Privileges.All.Where(p => !referenced.Contains(p)).OrderBy(p => p).ToList();

        missing.Should().BeEmpty(
            "each privilege in Privileges.All needs a row/checkbox in the MATRIX (or a standalone checkbox) in " +
            RolesComponentRelativePath + ", otherwise operators cannot grant it from the UI");
    }

    [Fact]
    public void The_Role_Privilege_page_names_only_privileges_the_backend_knows()
    {
        var referenced = ReadReferencedPrivileges();

        var unknown = referenced.Where(p => !Privileges.All.Contains(p)).OrderBy(p => p).ToList();

        unknown.Should().BeEmpty(
            "a checkbox naming a privilege absent from Privileges.All would grant nothing (typo or removed privilege)");
    }

    private static HashSet<string> ReadReferencedPrivileges()
    {
        var source = File.ReadAllText(LocateInRepo(RolesComponentRelativePath));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in PrivilegeRefs.Matches(source))
        {
            for (var g = 1; g <= 3; g++)
                if (m.Groups[g].Success) names.Add(m.Groups[g].Value);
        }
        names.Should().NotBeEmpty("the regex must still match the component's privilege references");
        return names;
    }

    /// <summary>Walks up from the test binary to the repository root (the directory holding FollowUp.sln).</summary>
    private static string LocateInRepo(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FollowUp.sln")))
            {
                var path = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Expected the Role Privilege page at {path}.", path);
                return path;
            }
        }
        throw new DirectoryNotFoundException("Could not locate the repository root (FollowUp.sln) above " + AppContext.BaseDirectory);
    }
}
