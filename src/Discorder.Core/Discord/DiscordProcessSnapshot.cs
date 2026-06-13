namespace Discorder.Core.Discord;

public sealed record DiscordProcessSnapshot
{
    public DiscordProcessSnapshot(
        int runningProcessCount,
        IReadOnlyList<string> executablePaths)
        : this(runningProcessCount, executablePaths, [])
    {
    }

    public DiscordProcessSnapshot(
        int runningProcessCount,
        IReadOnlyList<string> executablePaths,
        IReadOnlyList<int> processIds)
    {
        RunningProcessCount = runningProcessCount;
        ExecutablePaths = executablePaths;
        ProcessIds = processIds;
    }

    public int RunningProcessCount { get; }

    public IReadOnlyList<string> ExecutablePaths { get; }

    public IReadOnlyList<int> ProcessIds { get; }

    public bool HasRunningProcesses => RunningProcessCount > 0;

    public int KnownExecutablePathCount => ExecutablePaths.Count;
}
