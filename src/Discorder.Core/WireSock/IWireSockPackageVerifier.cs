namespace Discorder.Core.WireSock;

public interface IWireSockPackageVerifier
{
    void VerifyInstaller(string installerPath);

    void VerifyClient(string executablePath);
}
