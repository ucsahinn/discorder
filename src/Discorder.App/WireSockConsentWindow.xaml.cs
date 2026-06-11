using System.Diagnostics;
using System.Windows;
using Discorder.Core.WireSock;

namespace Discorder.App;

public partial class WireSockConsentWindow : Window
{
    private static readonly Uri CloudflareWarpTerms = new(
        "https://www.cloudflare.com/application/terms/");

    private readonly IWireSockBootstrapper _bootstrapper;

    public WireSockConsentWindow(IWireSockBootstrapper bootstrapper)
    {
        _bootstrapper = bootstrapper
            ?? throw new ArgumentNullException(nameof(bootstrapper));

        InitializeComponent();
        VersionText.Text =
            $"WireSock VPN Client {_bootstrapper.RequiredVersion} (Windows x64)";
    }

    private void OpenLicense_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(_bootstrapper.ProductPage);
    }

    private void OpenWarpTerms_Click(object sender, RoutedEventArgs e)
    {
        OpenUri(CloudflareWarpTerms);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private static void OpenUri(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
