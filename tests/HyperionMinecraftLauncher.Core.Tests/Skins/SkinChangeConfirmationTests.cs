using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Auth;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Launcher;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Logging;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Skins.History;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Skins;

/// <summary>
/// A skin change reaches Mojang only after the player confirms it, and the skin it replaces is
/// kept in Recent skins so one click brings it back.
/// </summary>
public class SkinChangeConfirmationTests : IDisposable
{
    private static readonly byte[] NewSkin = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 1, 1 };
    private static readonly byte[] OldSkin = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 2, 2, 2 };

    private readonly string _historyRoot = Path.Combine(Path.GetTempPath(), "hypmcl-confirm-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_historyRoot)) Directory.Delete(_historyRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task DeclinedConfirmation_UploadsNothing()
    {
        var (vm, skins, history) = await SignedInVmAsync();
        SkinChangeConfirmation? asked = null;
        vm.SetSkinChangeConfirmRequest((request, _) => { asked = request; return Task.FromResult(false); });
        var applied = false;
        vm.SkinApplied += (_, _) => applied = true;

        await vm.ReapplyHistoricSkinCommand.ExecuteAsync(vm.SkinHistory[0]);

        Assert.NotNull(asked);
        Assert.Null(skins.LastUploadedBytes);
        Assert.False(applied);
        Assert.Single(await history.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Confirmation_ShowsTheNewSkinAndThenUploadsIt()
    {
        var (vm, skins, _) = await SignedInVmAsync();
        SkinChangeConfirmation? asked = null;
        vm.SetSkinChangeConfirmRequest((request, _) => { asked = request; return Task.FromResult(true); });

        await vm.ReapplyHistoricSkinCommand.ExecuteAsync(vm.SkinHistory[0]);

        Assert.Equal(NewSkin, asked!.NewPngBytes);
        Assert.Equal(SkinVariant.Slim, asked.Variant);
        Assert.Equal("Notch", asked.AccountName);
        Assert.Equal(NewSkin, skins.LastUploadedBytes);
    }

    [Fact]
    public async Task ConfirmedChange_KeepsTheReplacedSkinInRecentSkins()
    {
        var (vm, _, history) = await SignedInVmAsync();
        vm.SetSkinChangeConfirmRequest((_, _) => Task.FromResult(true));
        vm.RememberCurrentSkin(OldSkin, SkinVariant.Classic);

        await vm.ReapplyHistoricSkinCommand.ExecuteAsync(vm.SkinHistory[0]);

        var entries = await history.ListAsync(CancellationToken.None);
        var backup = Assert.Single(entries, e => File.ReadAllBytes(e.FilePath).SequenceEqual(OldSkin));
        Assert.Equal(SkinVariant.Classic, backup.Variant);
    }

    [Fact]
    public async Task ReplacedSkinAlreadyInRecentSkins_IsNotDuplicated()
    {
        var (vm, _, history) = await SignedInVmAsync();
        await history.AppendAsync(OldSkin, SkinVariant.Classic, CancellationToken.None);
        await vm.ReloadHistoryAsync();
        vm.SetSkinChangeConfirmRequest((_, _) => Task.FromResult(true));
        vm.RememberCurrentSkin(OldSkin, SkinVariant.Classic);

        await vm.ReapplyHistoricSkinCommand.ExecuteAsync(vm.SkinHistory.First(e => File.ReadAllBytes(e.FilePath).SequenceEqual(NewSkin)));

        var entries = await history.ListAsync(CancellationToken.None);
        Assert.Equal(1, entries.Count(e => File.ReadAllBytes(e.FilePath).SequenceEqual(OldSkin)));
    }

    [Fact]
    public void SamePicture_IgnoresHowThePngWasEncoded()
    {
        // Mojang re-encodes uploaded skins, so the copy fetched back differs byte-wise from ours.
        var red = Png(SKColors.Red);
        var redReencoded = red.Concat(new byte[] { 0, 0, 0, 0 }).ToArray();

        Assert.True(SkinPictures.Same(red, redReencoded));
        Assert.False(SkinPictures.Same(red, Png(SKColors.Blue)));
        Assert.True(SkinPictures.Same(OldSkin, OldSkin.ToArray()));
        Assert.False(SkinPictures.Same(OldSkin, NewSkin));
    }

    [Fact]
    public async Task ConfirmedChange_AfterSigningOut_DoesNotBackUpThePreviousAccountsSkin()
    {
        var (vm, _, history) = await SignedInVmAsync();
        vm.SetSkinChangeConfirmRequest((_, _) => Task.FromResult(true));
        vm.RememberCurrentSkin(OldSkin, SkinVariant.Classic);
        await vm.SignOutCommand.ExecuteAsync();
        await vm.SignInMicrosoftCommand.ExecuteAsync();
        await vm.ReloadHistoryAsync();

        await vm.ReapplyHistoricSkinCommand.ExecuteAsync(vm.SkinHistory[0]);

        var entries = await history.ListAsync(CancellationToken.None);
        Assert.DoesNotContain(entries, e => File.ReadAllBytes(e.FilePath).SequenceEqual(OldSkin));
    }

    private static byte[] Png(SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task<(MainViewModel Vm, RecordingSkinService Skins, FileSkinHistoryStore History)> SignedInVmAsync()
    {
        var history = new FileSkinHistoryStore(_historyRoot);
        await history.AppendAsync(NewSkin, SkinVariant.Slim, CancellationToken.None);
        var skins = new RecordingSkinService();
        var auth = new FakeMicrosoftAuthService
        {
            ResultToReturn = new AuthResult { Username = "Notch", Uuid = "069a79f444e94726a5befca90e38aaf5", AccessToken = "t", IsOffline = false },
        };
        var vm = new MainViewModel(new StubLauncherService(), new RecordingLogger(),
            microsoftAuth: auth, settingsStore: null, skinService: skins, skinHistory: history);
        await vm.SignInMicrosoftCommand.ExecuteAsync();
        await vm.ReloadHistoryAsync();
        return (vm, skins, history);
    }

    private sealed class RecordingSkinService : ISkinService
    {
        public byte[]? LastUploadedBytes { get; private set; }

        public Task UploadSkinAsync(string accessToken, byte[] pngBytes, SkinVariant variant, CancellationToken cancellationToken)
        {
            LastUploadedBytes = pngBytes;
            return Task.CompletedTask;
        }

        public Task<PlayerProfile> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
            => Task.FromResult(new PlayerProfile { Id = "id", Name = "Notch", Skins = Array.Empty<OwnedSkin>(), Capes = Array.Empty<OwnedCape>() });

        public Task SetActiveCapeAsync(string accessToken, string capeId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ClearActiveCapeAsync(string accessToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
