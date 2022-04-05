#nullable disable
namespace NadekoBot.Modules.Music;

public interface ISoundcloudResolver : IPlatformQueryResolver
{
    bool IsSoundCloudLink(string url);
    IAsyncEnumerable<ITrackInfo> ResolvePlaylistAsync(string playlist);
}