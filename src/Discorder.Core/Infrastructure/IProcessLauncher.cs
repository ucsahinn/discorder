namespace Discorder.Core.Infrastructure;

public interface IProcessLauncher
{
    IManagedProcess Start(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath);
}
