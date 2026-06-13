namespace Discorder.Core.Discord;

public interface IDiscordProcessInspector
{
    DiscordProcessSnapshot Capture();
}
