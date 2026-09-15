using System.Text.RegularExpressions;
using FluentAssertions;

namespace FollowUp.ArchitectureTests;

/// <summary>
/// Every data table in the Angular feature pages must sit directly inside a <c>.grid-scroll</c> wrapper. That
/// wrapper is what gives all grids their frozen header (sticky within the grid's own scroll region) and what
/// <c>GridKeyboardNavService</c> keys off to provide arrow-key navigation — a table outside it silently gets
/// neither. Introduced 2026-09-15 when both behaviours were rolled out to all 50+ grids at once.
/// </summary>
public class GridConventionTests
{
    private static readonly Regex TemplateLiteral = new(@"template:\s*`(?<tpl>[\s\S]*?)`\s*,", RegexOptions.Compiled);
    private static readonly Regex TableOpen = new(@"<table\b", RegexOptions.Compiled);

    [Fact]
    public void Every_data_table_in_a_feature_page_is_wrapped_in_grid_scroll()
    {
        var featuresDir = Path.Combine(RepoRoot(), "web", "src", "app", "features");
        Directory.Exists(featuresDir).Should().BeTrue($"expected the Angular feature pages at {featuresDir}");

        var offenders = new List<string>();
        var tablesSeen = 0;

        foreach (var file in Directory.EnumerateFiles(featuresDir, "*.component.ts", SearchOption.AllDirectories).OrderBy(f => f))
        {
            var source = File.ReadAllText(file);
            var tplMatch = TemplateLiteral.Match(source);
            if (!tplMatch.Success) continue;
            var tpl = tplMatch.Groups["tpl"].Value;

            foreach (Match table in TableOpen.Matches(tpl))
            {
                tablesSeen++;
                // The wrapper must be the nearest enclosing <div>, i.e. the last <div …> opened before this <table>
                // with no other <div> in between — allowing only whitespace between the two tags.
                var before = tpl.Substring(0, table.Index).TrimEnd();
                var wrapped = Regex.IsMatch(before, @"<div\b[^>]*\bclass=""[^""]*\bgrid-scroll\b[^""]*""[^>]*>$");
                if (!wrapped)
                {
                    var line = tpl.Substring(0, table.Index).Count(c => c == '\n') + source.Substring(0, tplMatch.Groups["tpl"].Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetRelativePath(RepoRoot(), file)}:{line}");
                }
            }
        }

        tablesSeen.Should().BeGreaterThan(40, "the regex must still find the app's data tables");
        offenders.Should().BeEmpty(
            "every <table> in a feature template must be the direct child of <div class=\"grid-scroll\"> — " +
            "that wrapper provides the frozen header and arrow-key navigation");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FollowUp.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Could not locate the repository root (FollowUp.sln) above " + AppContext.BaseDirectory);
    }
}
