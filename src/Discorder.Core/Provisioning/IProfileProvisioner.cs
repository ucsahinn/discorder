namespace Discorder.Core.Provisioning;

public interface IProfileProvisioner
{
    Task<string> EnsureProfileAsync(
        IReadOnlyList<string> allowedApplications,
        CancellationToken cancellationToken);
}
