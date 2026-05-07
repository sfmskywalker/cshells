using System.Text.Json;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CShells.Tests.EndToEnd;

/// <summary>
/// Custom WebApplicationFactory that ensures the Workbench app's content root
/// is properly configured for testing and waits for shell initialization to complete.
/// </summary>
[UsedImplicitly]
public class WorkbenchApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseContentRoot(GetWorkbenchContentRoot());

        base.ConfigureWebHost(builder);
    }

    private static string GetWorkbenchContentRoot()
    {
        return GetContentRootFromMvcTestingManifest()
            ?? GetContentRootFromSolutionLayout()
            ?? GetContentRootFromCopiedOutput()
            ?? throw new InvalidOperationException(
                "Could not find Workbench content root. Expected either an MVC testing manifest entry, " +
                "a samples/CShells.Workbench source directory under CShells.sln, or copied test output content.");
    }

    private static string? GetContentRootFromMvcTestingManifest()
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "MvcTestingAppManifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        foreach (var property in manifest.RootElement.EnumerateObject())
        {
            if (property.Name.StartsWith("CShells.Workbench,", StringComparison.Ordinal) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                var path = property.Value.GetString();
                if (IsWorkbenchContentRoot(path))
                {
                    return path;
                }
            }
        }

        return null;
    }

    private static string? GetContentRootFromSolutionLayout()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "CShells.sln")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            return null;
        }

        var workbenchPath = Path.Combine(directory.FullName, "samples", "CShells.Workbench");
        return IsWorkbenchContentRoot(workbenchPath) ? workbenchPath : null;
    }

    private static string? GetContentRootFromCopiedOutput()
    {
        var outputPath = AppContext.BaseDirectory;
        return IsWorkbenchContentRoot(outputPath) ? outputPath : null;
    }

    private static bool IsWorkbenchContentRoot(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Directory.Exists(path) &&
        File.Exists(Path.Combine(path, "appsettings.json")) &&
        Directory.Exists(Path.Combine(path, "Shells"));
}
