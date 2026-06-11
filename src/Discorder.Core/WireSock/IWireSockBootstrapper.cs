namespace Discorder.Core.WireSock;

public interface IWireSockBootstrapper
{
    string RequiredVersion { get; }

    Uri ProductPage { get; }

    bool IsInstalled { get; }

    bool IsSetupConsentAccepted { get; }

    void AcceptSetupConsent();

    Task<string> EnsureInstalledAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}
