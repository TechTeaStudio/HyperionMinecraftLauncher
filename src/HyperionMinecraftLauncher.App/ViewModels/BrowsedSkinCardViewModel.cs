using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using TechTeaStudio.HyperionMinecraftLauncher.App.Controls;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.Browser;

namespace TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;

/// <summary>
/// Per-card view-model for the Skins page "Browse skins" grid. Wraps a
/// <see cref="BrowsedSkin"/> record with an async-loaded
/// <see cref="MinifigureImage"/> bitmap that renders the assembled minifigure body
/// sprite in place of the raw 64x64 sprite-sheet earlier versions showed.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper exists because <see cref="BrowsedSkin"/> lives in
/// <c>HyperionMinecraftLauncher.Core</c> and cannot take an Avalonia dependency
/// (Avalonia.Bitmap is App-tier). Binding from XAML still works against the
/// underlying record through <see cref="Source"/>.
/// </para>
/// <para>
/// The bitmap load is fire-and-forget: the card initially shows the Steve fallback
/// from <see cref="BrowsedSkinMinifigureCache.LoadSteveFallback"/> so the grid layout
/// is stable on first paint, then swaps to the real body sprite when the
/// network round-trip completes.
/// </para>
/// </remarks>
public sealed class BrowsedSkinCardViewModel : INotifyPropertyChanged
{
    private Bitmap? _minifigureImage;

    /// <summary>The underlying record. Bind from XAML to <c>{Binding Source.UploaderName}</c> etc.</summary>
    public BrowsedSkin Source { get; }

    /// <summary>
    /// Composed minifigure body sprite. Starts as the Steve fallback (or null when
    /// the asset loader is unavailable) and is replaced with the real body sprite
    /// when the cache resolves.
    /// </summary>
    public Bitmap? MinifigureImage
    {
        get => _minifigureImage;
        private set
        {
            if (ReferenceEquals(_minifigureImage, value)) return;
            _minifigureImage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Construct a card wrapper. <paramref name="cache"/> may be null in tests; in that
    /// case <see cref="MinifigureImage"/> stays at its constructor default until a caller
    /// sets it explicitly via <see cref="SetImage(Bitmap)"/>.
    /// </summary>
    public BrowsedSkinCardViewModel(BrowsedSkin source, BrowsedSkinMinifigureCache? cache)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (cache is null) return;

        // Show the Steve fallback synchronously so the WrapPanel doesn't flicker
        // with empty tiles while the real PNG is in flight. Steve loads from a
        // bundled avares:// resource so the asset hit is cheap.
        _minifigureImage = cache.LoadSteveFallback();

        // Kick off the real-image fetch. Fire-and-forget by design: when the result
        // lands we marshal back to the UI thread before swapping the bitmap so the
        // PropertyChanged subscribers (the bound Image) refresh safely.
        _ = LoadRealImageAsync(cache);
    }

    private async Task LoadRealImageAsync(BrowsedSkinMinifigureCache cache)
    {
        try
        {
            var bitmap = await cache.GetBodyImageAsync(Source, CancellationToken.None).ConfigureAwait(false);
            if (bitmap is null) return;
            // Marshal back to the UI thread so the bound Image control picks up
            // the change without "Call from invalid thread" crashes.
            if (Dispatcher.UIThread.CheckAccess())
                MinifigureImage = bitmap;
            else
                await Dispatcher.UIThread.InvokeAsync(() => MinifigureImage = bitmap);
        }
        catch
        {
            // Cache already swallows network / decode errors and resolves to the
            // Steve fallback; an unexpected exception here is a programming bug we
            // don't want to crash the UI for.
        }
    }

    /// <summary>Test seam: set the bitmap directly without going through the cache.</summary>
    public void SetImage(Bitmap? bitmap) => MinifigureImage = bitmap;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
