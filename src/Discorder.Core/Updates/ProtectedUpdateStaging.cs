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

        var directory = Path.Combine(
            rootDirectory,
            version,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (restrictAccess)
        {
            RestrictToAdministrators(directory);
        }
        return directory;
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
