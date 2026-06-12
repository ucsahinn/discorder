using System.Security.AccessControl;
using System.Security.Principal;

namespace Discorder.Core.Updates;

public static class ProtectedUpdateStaging
{
    public static string CreateVersionDirectory(
        string rootDirectory,
        string version,
        bool restrictAccess)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || version.Contains(Path.DirectorySeparatorChar)
            || version.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "Güncelleme sürümü güvenli bir klasör adı değil.",
                nameof(version));
        }

        var root = Path.GetFullPath(rootDirectory);
        var sharedRoot = Path.GetDirectoryName(root)
            ?? throw new InvalidOperationException("Güncelleme kök klasörü çözümlenemedi.");
        PrepareDirectory(sharedRoot, restrictAccess);
        PrepareDirectory(root, restrictAccess);

        var versionDirectory = Path.Combine(root, version);
        PrepareDirectory(versionDirectory, restrictAccess);
        var directory = Path.Combine(
            versionDirectory,
            Guid.NewGuid().ToString("N"));
        RejectReparsePointsInExistingAncestors(directory);
        Directory.CreateDirectory(directory);
        RejectReparsePoint(directory);
        if (restrictAccess)
        {
            RestrictToAdministrators(directory);
        }
        return directory;
    }

    private static void PrepareDirectory(string directory, bool restrictAccess)
    {
        var fullPath = Path.GetFullPath(directory);
        RejectReparsePointsInExistingAncestors(fullPath);
        Directory.CreateDirectory(fullPath);
        RejectReparsePoint(fullPath);
        if (restrictAccess)
        {
            RestrictToAdministrators(fullPath);
        }
    }

    private static void RejectReparsePointsInExistingAncestors(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException("Güncelleme yolu çözümlenemedi.");
        }

        var current = root;
        foreach (var part in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            if (Directory.Exists(current))
            {
                RejectReparsePoint(current);
            }
        }
    }

    private static void RejectReparsePoint(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (info.Exists
            && info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException(
                "Güncelleme staging klasörü junction veya symlink olamaz.");
        }
    }

    public static void RestrictToAdministrators(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var security = new DirectorySecurity();
        var inheritance = InheritanceFlags.ContainerInherit
            | InheritanceFlags.ObjectInherit;
        var administrators = new SecurityIdentifier(
            WellKnownSidType.BuiltinAdministratorsSid,
            domainSid: null);
        var system = new SecurityIdentifier(
            WellKnownSidType.LocalSystemSid,
            domainSid: null);

        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            administrators,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            system,
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(directory).SetAccessControl(security);
    }
}
