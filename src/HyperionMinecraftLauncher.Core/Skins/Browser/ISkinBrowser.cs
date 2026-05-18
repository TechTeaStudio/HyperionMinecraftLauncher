using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

/// <summary>
/// Community skin gallery (NameMC and similar). Returns a list of
/// <see cref="BrowsedSkin"/> records the user can preview in the UI and then push
/// to the active Mojang account via the existing <see cref="ISkinService"/>.
/// </summary>
/// <remarks>
/// IMPORTANT: NameMC publishes no official API. The current implementation
/// (<see cref="NameMcSkinBrowser"/>) parses public HTML pages and may break if
/// the site is restructured. Treat this interface as a convenience surface,
/// not a contract with any third party.
/// </remarks>
public interface ISkinBrowser
{
    /// <summary>
    /// Fetch the source's "trending" gallery. The implementation is responsible
    /// for capping the returned list at <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<BrowsedSkin>> ListTrendingAsync(int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Free-text search over the source's gallery (skin name, tag, or uploader).
    /// </summary>
    Task<IReadOnlyList<BrowsedSkin>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Download the raw 64x64 skin PNG for a previously-returned record. The
    /// returned bytes are suitable for passing straight into
    /// <c>ISkinService.UploadSkinAsync</c>.
    /// </summary>
    Task<byte[]> DownloadPngAsync(BrowsedSkin skin, CancellationToken cancellationToken);
}
