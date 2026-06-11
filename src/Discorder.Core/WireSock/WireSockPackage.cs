namespace Discorder.Core.WireSock;

public static class WireSockPackage
{
    public const string Version = "1.4.7.1";
    public const string InstallerFileName =
        "wiresock-vpn-client-x64-1.4.7.1.msi";
    public const string CliExecutableFileName = "wiresock-client.exe";
    public const string WindowsX64Sha256 =
        "FA3F483DA7EA1AE6C234F95BECB0AA6A18E7EB18B944D3FFB4518D40F4292F40";
    public const string ExpectedPublisher = "IP SMIRNOV VADIM VALERIEVICH";
    public const string ExpectedProductName = "WireSock VPN Client x64";
    private static readonly string[] DownloadPathChunks =
    [
        "7dgn",
        "yow9",
        "g0nj",
        "u36l",
        "7to4",
        "wtlw",
        "y3zp",
        "ca"
    ];

    public static readonly Uri ProductPage = new(
        "https://www.wiresock.net/wiresock-vpn-client/download");

    public static readonly Uri WindowsX64Download = new(
        "https://www.wiresock.net/sdc_download/1066/?key=" +
        string.Concat(DownloadPathChunks));
}
