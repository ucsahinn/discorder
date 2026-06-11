namespace Discorder.Core.WireSock;

public sealed class WireSockLocator : IWireSockLocator
{
    public string? FindExecutable()
    {
        foreach (var root in GetProgramFileRoots())
        {
            var candidates = new[]
            {
                Path.Combine(
                    root,
                    "WireSock VPN Client",
                    "bin",
                    WireSockPackage.CliExecutableFileName),
                Path.Combine(
                    root,
                    "WireSock VPN Client",
                    WireSockPackage.CliExecutableFileName)
            };

            foreach (var candidate in candidates)
            {
                if (IsValidExecutable(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static IEnumerable<string> GetProgramFileRoots()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        return roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsValidExecutable(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && Path.GetFileName(path).Equals(
                WireSockPackage.CliExecutableFileName,
                StringComparison.OrdinalIgnoreCase)
            && File.Exists(path);
    }
}
