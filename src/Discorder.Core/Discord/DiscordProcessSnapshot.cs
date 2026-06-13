namespace Discorder.Core.Discord;

public sealed record DiscordProcessSnapshot(
    int RunningProcessCount,
    IReadOnlyList<string> ExecutablePaths)
{
    public bool HasRunningProcesses => RunningProcessCount > 0;

    public int KnownExecutablePathCount => ExecutablePaths.Count;
}
