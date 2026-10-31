using System.Text.RegularExpressions;
using TicketCore.Common;

namespace TicketCore.Core.Tests;

public class PermissionCatalogTests
{
    [Fact]
    public void Seed_script_matches_code_catalog()
    {
        var script = File.ReadAllText(FindRepoFile("src/TicketCore.Database/Scripts/0002_SeedPermissionsAndRoles.sql"));
        var permissionBlock = script[..script.IndexOf("INSERT INTO dbo.Role ", StringComparison.Ordinal)];
        var seeded = Regex.Matches(permissionBlock, @"^\('([a-z.]+)',", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var inCode = Permissions.Catalog.Select(p => p.Key).ToHashSet();

        Assert.Equal(inCode.Order(), seeded.Order());
    }

    [Fact]
    public void Catalog_keys_are_unique()
    {
        var keys = Permissions.Catalog.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not find {relativePath} above {AppContext.BaseDirectory}.");
    }
}
