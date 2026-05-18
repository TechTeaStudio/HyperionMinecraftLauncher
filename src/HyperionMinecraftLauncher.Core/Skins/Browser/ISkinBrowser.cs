using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

/// <summary>
/// Community skin gallery (MineSkin and similar). Returns a list of
/// <see cref="BrowsedSkin"/> records the user can preview in the UI and then push
/// to the active Mojang account via the existing <see cref="ISkinService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The live implementation in v0.32.3+ is <see cref="MineSkinBrowser"/>, which talks
/// to the public MineSkin v2 REST API. The earlier <see cref="NameMcSkinBrowser"/>
/// is kept around (marked <c>[Obsolete]</c>) for reference, but pure-HTTP access to
/// NameMC was killed by Cloudflare's JavaScript challenge.
/// </para>
/// <para>
/// Treat this interface as a convenience surface, not a contract with any third
/// party: any community source can disappear, rate-limit us, or change its schema.
/// </para>
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
