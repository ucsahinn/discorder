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

    public void AcceptSetupConsent(string wireSockVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wireSockVersion);

        lock (_gate)
        {
            _paths.EnsureDirectories();
            var settings = new StoredSettings(
                wireSockVersion,
                AcceptedCloudflareWarpTerms: true);
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
    }

    private StoredSettings Load()
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            return new StoredSettings(null, AcceptedCloudflareWarpTerms: false);
        }

        try
        {
            var json = File.ReadAllText(_paths.SettingsFile);
            return JsonSerializer.Deserialize<StoredSettings>(json)
                ?? new StoredSettings(null, AcceptedCloudflareWarpTerms: false);
        }
        catch (JsonException)
        {
            return new StoredSettings(null, AcceptedCloudflareWarpTerms: false);
        }
    }

    private sealed record StoredSettings(
        string? AcceptedWireSockVersion,
        bool AcceptedCloudflareWarpTerms);
}
