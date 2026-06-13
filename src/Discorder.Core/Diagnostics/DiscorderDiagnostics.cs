using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Discorder.Core.Configuration;

namespace Discorder.Core.Diagnostics;

public sealed class DiscorderDiagnostics : IDiscorderDiagnostics
{
    private static readonly TimeSpan DefaultSummaryWriteInterval = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly Regex SensitiveAssignmentPattern = new(
        @"(?ix)
        \b(
            privatekey|presharedkey|token|access[_-]?token|refresh[_-]?token|
            password|passwd|secret|client[_-]?secret|cookie|authorization
        )\b
        \s*[:=]\s*
        [^\r\n]+",
        RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{8,}",
        RegexOptions.Compiled);

    private readonly AppPaths _paths;
    private readonly TimeSpan _summaryWriteInterval;
    private readonly object _gate = new();
    private DateTimeOffset _lastSummaryWriteUtc = DateTimeOffset.MinValue;
    private string? _pendingSummaryStatus;
    private bool _summaryFlushScheduled;

    public DiscorderDiagnostics(
        AppPaths paths,
        TimeSpan? summaryWriteInterval = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _summaryWriteInterval = summaryWriteInterval ?? DefaultSummaryWriteInterval;
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
                    ["runtime"] = CaptureRuntimeMetrics().ToReport(),
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
                WriteRuntimeSnapshotToStaging(stagingDirectory);

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

        foreach (var file in EnumerateExpectedLogFiles())
        {
            if (!File.Exists(file))
            {
                continue;
            }

            var fileName = Path.GetFileName(file);
            var destination = Path.Combine(stagingDirectory, fileName);

            try
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    warnings.Add($"{fileName}: reparse point olduğu için pakete alınmadı");
                    continue;
                }

                using var source = new StreamReader(
                    new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete),
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true);
                using var target = new StreamWriter(
                    new FileStream(
                        destination,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                while (source.ReadLine() is { } line)
                {
                    target.WriteLine(Redact(line));
                }
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

    private IEnumerable<string> EnumerateExpectedLogFiles()
    {
        yield return _paths.EventLog;
        yield return _paths.ErrorLog;
        yield return _paths.HealthReport;
        yield return _paths.DiagnosticSummary;
        yield return _paths.TunnelLog;
        yield return Path.Combine(_paths.LogDirectory, "update.log");
    }

    private static void WriteRuntimeSnapshotToStaging(string stagingDirectory)
    {
        File.WriteAllText(
            Path.Combine(stagingDirectory, "runtime.json"),
            JsonSerializer.Serialize(CaptureRuntimeMetrics().ToReport(), JsonOptions),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
                MaybeWriteSummaryLocked(
                    message,
                    force: !string.Equals(level, "info", StringComparison.Ordinal));
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void MaybeWriteSummaryLocked(string lastStatus, bool force)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = now - _lastSummaryWriteUtc;
        if (force || elapsed >= _summaryWriteInterval)
        {
            _pendingSummaryStatus = null;
            WriteSummaryLocked(lastStatus);
            return;
        }

        _pendingSummaryStatus = lastStatus;
        if (_summaryFlushScheduled)
        {
            return;
        }

        _summaryFlushScheduled = true;
        var delay = _summaryWriteInterval - elapsed;
        _ = FlushPendingSummaryAfterDelayAsync(delay);
    }

    private async Task FlushPendingSummaryAfterDelayAsync(TimeSpan delay)
    {
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay).ConfigureAwait(false);
        }

        try
        {
            lock (_gate)
            {
                _summaryFlushScheduled = false;
                if (string.IsNullOrWhiteSpace(_pendingSummaryStatus))
                {
                    return;
                }

                var status = _pendingSummaryStatus;
                _pendingSummaryStatus = null;
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

    private void WriteSummaryLocked(string lastStatus)
    {
        var runtime = CaptureRuntimeMetrics();
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
        builder.AppendLine("## Performans");
        builder.AppendLine(FormattableString.Invariant(
            $"- Working set: {runtime.WorkingSetMegabytes:0.0} MB"));
        builder.AppendLine(FormattableString.Invariant(
            $"- Private memory: {runtime.PrivateMemoryMegabytes:0.0} MB"));
        builder.AppendLine(FormattableString.Invariant(
            $"- GC heap: {runtime.GcHeapMegabytes:0.0} MB"));
        builder.AppendLine(FormattableString.Invariant(
            $"- Managed memory: {runtime.ManagedMemoryMegabytes:0.0} MB"));
        builder.AppendLine(FormattableString.Invariant(
            $"- Handle/thread: {runtime.HandleCount}/{runtime.ThreadCount}"));
        if (runtime.UptimeSeconds is not null)
        {
            builder.AppendLine(FormattableString.Invariant(
                $"- Çalışma süresi: {runtime.UptimeSeconds:0} saniye"));
        }

        builder.AppendLine();
        builder.AppendLine("## Log dosyaları");

        foreach (var file in EnumerateExpectedLogFiles()
                     .Where(File.Exists)
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
        _pendingSummaryStatus = null;
        _lastSummaryWriteUtc = DateTimeOffset.UtcNow;
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

        result = SensitiveAssignmentPattern.Replace(
            result,
            match =>
            {
                var equalsIndex = match.Value.IndexOf('=');
                var colonIndex = match.Value.IndexOf(':');
                var separatorIndex = (equalsIndex, colonIndex) switch
                {
                    (>= 0, >= 0) => Math.Min(equalsIndex, colonIndex),
                    (>= 0, _) => equalsIndex,
                    (_, >= 0) => colonIndex,
                    _ => -1
                };
                if (separatorIndex < 0)
                {
                    return "[REDACTED]";
                }

                return match.Value[..(separatorIndex + 1)] + " [REDACTED]";
            });
        result = BearerPattern.Replace(result, "Bearer [REDACTED]");

        return result;
    }

    private static string GetAppVersion()
    {
        return Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "bilinmiyor";
    }

    private static RuntimeMetrics CaptureRuntimeMetrics()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            var gcInfo = GC.GetGCMemoryInfo();
            var managedMemoryBytes = GC.GetTotalMemory(forceFullCollection: false);
            double? uptimeSeconds = null;

            try
            {
                uptimeSeconds = (DateTimeOffset.Now - process.StartTime).TotalSeconds;
            }
            catch (Exception exception)
                when (exception is InvalidOperationException
                    or NotSupportedException
                    or System.ComponentModel.Win32Exception)
            {
            }

            return new RuntimeMetrics(
                process.WorkingSet64,
                process.PrivateMemorySize64,
                managedMemoryBytes,
                gcInfo.HeapSizeBytes,
                gcInfo.FragmentedBytes,
                process.HandleCount,
                process.Threads.Count,
                uptimeSeconds);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException
                or NotSupportedException
                or System.ComponentModel.Win32Exception)
        {
            return RuntimeMetrics.Empty;
        }
    }

    private sealed record RuntimeMetrics(
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        long ManagedMemoryBytes,
        long GcHeapBytes,
        long GcFragmentedBytes,
        int HandleCount,
        int ThreadCount,
        double? UptimeSeconds)
    {
        public static RuntimeMetrics Empty { get; } = new(
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            null);

        public double WorkingSetMegabytes => ToMegabytes(WorkingSetBytes);

        public double PrivateMemoryMegabytes => ToMegabytes(PrivateMemoryBytes);

        public double ManagedMemoryMegabytes => ToMegabytes(ManagedMemoryBytes);

        public double GcHeapMegabytes => ToMegabytes(GcHeapBytes);

        public Dictionary<string, object?> ToReport()
        {
            return new Dictionary<string, object?>
            {
                ["workingSetBytes"] = WorkingSetBytes,
                ["workingSetMb"] = WorkingSetMegabytes,
                ["privateMemoryBytes"] = PrivateMemoryBytes,
                ["privateMemoryMb"] = PrivateMemoryMegabytes,
                ["managedMemoryBytes"] = ManagedMemoryBytes,
                ["managedMemoryMb"] = ManagedMemoryMegabytes,
                ["gcHeapBytes"] = GcHeapBytes,
                ["gcHeapMb"] = GcHeapMegabytes,
                ["gcFragmentedBytes"] = GcFragmentedBytes,
                ["gcFragmentedMb"] = ToMegabytes(GcFragmentedBytes),
                ["handleCount"] = HandleCount,
                ["threadCount"] = ThreadCount,
                ["uptimeSeconds"] = UptimeSeconds
            };
        }

        private static double ToMegabytes(long bytes)
        {
            return Math.Round(bytes / 1024d / 1024d, 1);
        }
    }
}
