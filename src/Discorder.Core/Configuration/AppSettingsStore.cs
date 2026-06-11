using System.Text.Json;

namespace Discorder.Core.Configuration;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public AppSettingsStore(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public bool IsSetupConsentAccepted(string wireSockVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireSockVersion);

        lock (_gate)
        {
            var settings = Load();
            return string.Equals(
                settings.AcceptedWireSockVersion,
                wireSockVersion,
                StringComparison.Ordinal)
                && settings.AcceptedCloudflareWarpTerms;
        }
    }

    public bool IsBrowserAccessEnabled()
    {
        lock (_gate)
        {
            return Load().BrowserAccessEnabled;
        }
    }

    public bool IsBackgroundVideoEnabled()
    {
        lock (_gate)
        {
            return Load().BackgroundVideoEnabled ?? true;
        }
    }

    public bool IsRunInBackgroundOnCloseEnabled()
    {
        lock (_gate)
        {
            return Load().RunInBackgroundOnClose ?? false;
        }
    }

    public bool IsStartWithWindowsEnabled()
    {
        lock (_gate)
        {
            return Load().StartWithWindows ?? false;
        }
    }

    public bool IsWireSockInstalledByDiscorder()
    {
        lock (_gate)
        {
            return Load().WireSockInstalledByDiscorder ?? false;
        }
    }

    public void SetBrowserAccessEnabled(bool enabled)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                BrowserAccessEnabled = enabled
            };

            Save(settings);
        }
    }

    public void SetBackgroundVideoEnabled(bool enabled)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                BackgroundVideoEnabled = enabled
            };

            Save(settings);
        }
    }

    public void SetRunInBackgroundOnCloseEnabled(bool enabled)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                RunInBackgroundOnClose = enabled
            };

            Save(settings);
        }
    }

    public void SetStartWithWindowsEnabled(bool enabled)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                StartWithWindows = enabled
            };

            Save(settings);
        }
    }

    public void SetWireSockInstalledByDiscorder(bool installed)
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                WireSockInstalledByDiscorder = installed
            };

            Save(settings);
        }
    }

    public void AcceptSetupConsent(string wireSockVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireSockVersion);

        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = Load() with
            {
                AcceptedWireSockVersion = wireSockVersion,
                AcceptedCloudflareWarpTerms = true
            };

            Save(settings);
        }
    }

    private StoredSettings Load()
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            return StoredSettings.Default;
        }

        try
        {
            var json = File.ReadAllText(_paths.SettingsFile);
            return JsonSerializer.Deserialize<StoredSettings>(json)
                ?? StoredSettings.Default;
        }
        catch (JsonException)
        {
            return StoredSettings.Default;
        }
    }

    private void Save(StoredSettings settings)
    {
        var temporaryPath = _paths.SettingsFile + ".tmp";

        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _paths.SettingsFile, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record StoredSettings(
        string? AcceptedWireSockVersion,
        bool AcceptedCloudflareWarpTerms,
        bool BrowserAccessEnabled,
        bool? BackgroundVideoEnabled,
        bool? RunInBackgroundOnClose,
        bool? StartWithWindows,
        bool? WireSockInstalledByDiscorder)
    {
        public static StoredSettings Default { get; } = new(
            null,
            AcceptedCloudflareWarpTerms: false,
            BrowserAccessEnabled: false,
            BackgroundVideoEnabled: true,
            RunInBackgroundOnClose: false,
            StartWithWindows: false,
            WireSockInstalledByDiscorder: false);
    }
}
