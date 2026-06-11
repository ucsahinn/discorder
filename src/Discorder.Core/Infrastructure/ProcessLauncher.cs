using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Discorder.Core.Infrastructure;

public sealed class ProcessLauncher : IProcessLauncher
{
    public IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"{Path.GetFileName(executable)} başlatılamadı.");
        }

        return new ManagedProcess(process, logPath);
    }

    private sealed class ManagedProcess : IManagedProcess
    {
        private readonly Process _process;
        private readonly StreamWriter _log;
        private readonly Task _stdoutPump;
        private readonly Task _stderrPump;
        private bool _disposed;

        public ManagedProcess(Process process, string logPath)
        {
            _process = process;
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            _log = new StreamWriter(
                new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            _process.Exited += OnExited;
            _stdoutPump = PumpAsync(_process.StandardOutput, "OUT");
            _stderrPump = PumpAsync(_process.StandardError, "ERR");
            WriteLog("SYS", $"WireSock süreci başladı. PID={_process.Id}");
        }

        public event EventHandler? Exited;

        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        public int? ExitCode => HasExited ? _process.ExitCode : null;

        public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (HasExited)
            {
                return;
            }

            try
            {
                _process.CloseMainWindow();
                using var timeoutSource = new CancellationTokenSource(timeout);
                using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    timeoutSource.Token);
                await _process.WaitForExitAsync(linkedSource.Token);
            }
            catch (OperationCanceledException)
            {
                if (!HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync(cancellationToken);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _process.Exited -= OnExited;

            if (!HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await Task.WhenAll(_stdoutPump, _stderrPump);
            WriteLog(
                "SYS",
                $"WireSock süreci kapandı. Kod={ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "bilinmiyor"}");
            await _log.DisposeAsync();
            _process.Dispose();
        }

        private async Task PumpAsync(StreamReader reader, string channel)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                WriteLog(channel, line);
            }
        }

        private void OnExited(object? sender, EventArgs e)
        {
            Exited?.Invoke(this, EventArgs.Empty);
        }

        private void WriteLog(string channel, string message)
        {
            lock (_log)
            {
                _log.WriteLine(
                    $"{DateTimeOffset.Now:O} [{channel}] {message.ReplaceLineEndings(" ")}");
            }
        }
    }
}
