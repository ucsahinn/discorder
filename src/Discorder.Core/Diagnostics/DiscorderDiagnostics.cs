using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using Discorder.Core.Configuration;

namespace Discorder.Core.Diagnostics;

public sealed class DiscorderDiagnostics : IDiscorderDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly AppPaths _paths;
    private readonly object _gate = new();

    public DiscorderDiagnostics(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public void Info(
        string source,
        string message,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        WriteEvent("info", source, message, null, details);
    }

    public void Warning(
        string source,
        string message,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        WriteEvent("warning", source, message, null, details);
    }

    public void Failure(
        string source,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        WriteEvent("error", source, message, exception, details);
    }

    public void WriteHealth(
        string status,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        try
        {
            lock (_gate)
            {
                _paths.EnsureDirectories();
                var document = new Dictionary<string, object?>
                {
                    ["generatedAt"] = DateTimeOffset.Now,
                    ["status"] = Redact(status),
                    ["version"] = GetAppVersion(),
                    ["processId"] = Environment.ProcessId,
                    ["os"] = Environment.OSVersion.VersionString,
                    ["is64BitProcess"] = Environment.Is64BitProcess,
                    ["is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem,
                    ["dataDirectory"] = Redact(_paths.DataDirectory),
                    ["logDirectory"] = Redact(_paths.LogDirectory),
                    ["details"] = RedactDetails(details)
                };

                File.WriteAllText(
                    _paths.HealthReport,
                    JsonSerializer.Serialize(document, JsonOptions),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                WriteSummaryLocked(status);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public string CreateBundle()
    {
        lock (_gate)
        {
            _paths.EnsureDirectories();
            WriteSummaryLocked("tanılama paketi hazırlanıyor");

            var timestamp = DateTimeOffset.Now.ToString(
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture);
            var finalPath = Path.Combine(
                _paths.DiagnosticBundleDirectory,
                $"discorder-diagnostics-{timestamp}.zip");
            var temporaryPath = finalPath + ".tmp";
            var stagingDirectory = Path.Combine(
                _paths.DiagnosticBundleDirectory,
                "staging-" + timestamp);

            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }

            Directory.CreateDirectory(stagingDirectory);

            try
            {
                var warnings = CopyLogFilesToStaging(stagingDirectory);
                if (warnings.Count > 0)
                {
                    File.WriteAllLines(
                        Path.Combine(stagingDirectory, "bundle-warnings.txt"),
                        warnings,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }

                if (!Directory.EnumerateFiles(stagingDirectory).Any())
                {
                    File.WriteAllText(
                        Path.Combine(stagingDirectory, "bundle-info.txt"),
                        "Tanılama klasöründe paketlenecek log dosyası bulunamadı.",
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }

                ZipFile.CreateFromDirectory(
                    stagingDirectory,
                    temporaryPath,
                    CompressionLevel.Optimal,
                    includeBaseDirectory: false);
            }
            finally
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }

            File.Move(temporaryPath, finalPath, overwrite: true);
            WriteSummaryLocked("tanılama paketi hazırlandı");
            return finalPath;
        }
    }

    private List<string> CopyLogFilesToStaging(string stagingDirectory)
    {
        var warnings = new List<string>();

        foreach (var file in Directory.EnumerateFiles(_paths.LogDirectory)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(file);
            var destination = Path.Combine(stagingDirectory, fileName);

            try
            {
                using var source = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var target = new FileStream(
                    destination,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                source.CopyTo(target);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                warnings.Add(
                    $"{fileName}: paket kopyasına alınamadı - {Redact(exception.Message)}");
            }
        }

        return warnings;
    }

    private void WriteEvent(
        string level,
        string source,
        string message,
        Exception? exception,
        IReadOnlyDictionary<string, string?>? details)
    {
        try
        {
            lock (_gate)
            {
                _paths.EnsureDirectories();
                var record = new Dictionary<string, object?>
                {
                    ["time"] = DateTimeOffset.Now,
                    ["level"] = level,
                    ["source"] = Redact(source),
                    ["message"] = Redact(message),
                    ["version"] = GetAppVersion(),
                    ["processId"] = Environment.ProcessId,
                    ["details"] = RedactDetails(details)
                };

                if (exception is not null)
                {
                    record["exception"] = new Dictionary<string, string?>
                    {
                        ["type"] = exception.GetType().FullName,
                        ["message"] = Redact(exception.Message),
                        ["stackTrace"] = Redact(exception.ToString())
                    };
                }

                File.AppendAllText(
                    _paths.EventLog,
                    JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                WriteSummaryLocked(message);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void WriteSummaryLocked(string lastStatus)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Discorder tanılama özeti");
        builder.AppendLine();
        builder.AppendLine(FormattableString.Invariant(
            $"- Üretim zamanı: {DateTimeOffset.Now:O}"));
        builder.AppendLine(FormattableString.Invariant(
            $"- Uygulama sürümü: {GetAppVersion()}"));
        builder.AppendLine(FormattableString.Invariant(
            $"- Son durum: {Redact(lastStatus)}"));
        builder.AppendLine(FormattableString.Invariant(
            $"- Süreç kimliği: {Environment.ProcessId}"));
        builder.AppendLine(FormattableString.Invariant(
            $"- İşletim sistemi: {Redact(Environment.OSVersion.VersionString)}"));
        builder.AppendLine(FormattableString.Invariant(
            $"- 64-bit süreç: {Environment.Is64BitProcess}"));
        builder.AppendLine(FormattableString.Invariant(
            $"- 64-bit Windows: {Environment.Is64BitOperatingSystem}"));
        builder.AppendLine(FormattableString.Invariant(
            $"- Veri klasörü: {Redact(_paths.DataDirectory)}"));
        builder.AppendLine(FormattableString.Invariant(
            $"- Log klasörü: {Redact(_paths.LogDirectory)}"));
        builder.AppendLine();
        builder.AppendLine("## Log dosyaları");

        foreach (var file in Directory.EnumerateFiles(_paths.LogDirectory)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            builder.AppendLine(FormattableString.Invariant(
                $"- {info.Name}: {info.Length} bayt, {info.LastWriteTimeUtc:O}"));
        }

        File.WriteAllText(
            _paths.DiagnosticSummary,
            builder.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static Dictionary<string, string?> RedactDetails(
        IReadOnlyDictionary<string, string?>? details)
    {
        if (details is null || details.Count == 0)
        {
            return [];
        }

        return details
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                pair => Redact(pair.Key) ?? string.Empty,
                pair => Redact(pair.Value),
                StringComparer.Ordinal);
    }

    private static string? Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var result = value.ReplaceLineEndings(" ");
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)] =
                "%LOCALAPPDATA%",
            [Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)] =
                "%USERPROFILE%",
            [Environment.UserName] = "%USERNAME%"
        };

        foreach (var pair in replacements
                     .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                     .OrderByDescending(pair => pair.Key.Length))
        {
            result = result.Replace(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string GetAppVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "bilinmiyor";
    }
}
