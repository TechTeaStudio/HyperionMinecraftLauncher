using System;
using System.Globalization;
using System.Threading.Tasks;
using TechTeaStudio.HyperionMinecraftLauncher.App.ViewModels;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Instances;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Instances;

/// <summary>
/// Regression tests for <see cref="MainViewModel.ApplyEditedInstanceAsync"/> - the
/// view-model side of the Edit Instance dialog.
///
/// v0.32.1: right-click "Edit instance..." crashed with a NullReferenceException
/// because <c>EditInstanceDialog.OnMinRamChanged</c> fired during XAML init (the
/// slider coerces its default value into the configured Minimum..Maximum range)
/// before the named labels existed. The dialog now no-ops until the named fields
/// are wired. These tests guard the VM half of the contract so a future refactor
/// that re-introduces an Edit-path crash (null args, auto-imported instance, or a
/// dialog that doesn't preserve identity fields) trips a unit test instead of a
/// production crash.
/// </summary>
public class ApplyEditedInstanceAsyncTests
{
    public ApplyEditedInstanceAsyncTests()
    {
        // Force English UI culture so localized log strings asserted below match the
        // source text (mirrors the convention in MainViewModelTests).
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }

    [Fact]
    public async Task NullOriginal_ThrowsArgumentNullException()
    {
        var vm = NewVm();
        var edited = MakeInstance("anything");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => vm.ApplyEditedInstanceAsync(null!, edited));
    }

    [Fact]
    public async Task NullEdited_ThrowsArgumentNullException()
    {
        var vm = NewVm();
        var original = MakeInstance("anything");

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => vm.ApplyEditedInstanceAsync(original, null!));
    }

    [Fact]
    public async Task AutoImportedOriginal_DoesNotPersist_ReturnsNull()
    {
        var service = new StubLauncherService();
        var vm = new MainViewModel(service, new RecordingLogger());

        var original = MakeInstance("auto") with { IsAutoImported = true };
        var edited = original with { Name = "Renamed" };

        var result = await vm.ApplyEditedInstanceAsync(original, edited);

        Assert.Null(result);
        Assert.Null(service.LastSavedInstance);
    }

    [Fact]
    public async Task SavedInstance_PersistsEditedFieldsAndPreservesIdentity()
    {
        var service = new StubLauncherService();
        var vm = new MainViewModel(service, new RecordingLogger());

        var original = MakeInstance("orig") with
        {
            Id = "stable-id",
            CreatedAt = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero),
            LastPlayedAt = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero),
            IsAutoImported = false,
        };
        vm.Instances.Add(original);
        vm.SelectedInstance = original;

        // Dialog "returned" a record with edited fields and a wiped identity (typical
        // when the dialog uses `original with { ... }` and doesn't restore Id/CreatedAt).
        var edited = original with
        {
            Name = "New name",
            IconKey = InstanceIcons.DiamondPickaxe,
            MinimumRamMb = 1024,
            MaximumRamMb = 4096,
            IsAutoImported = true,  // Dialog accident: must be reset.
        };

        var result = await vm.ApplyEditedInstanceAsync(original, edited);

        Assert.NotNull(result);
        // Identity preserved.
        Assert.Equal("stable-id", result!.Id);
        Assert.Equal(original.CreatedAt, result.CreatedAt);
        Assert.Equal(original.LastPlayedAt, result.LastPlayedAt);
        Assert.False(result.IsAutoImported);
        // Edited fields applied.
        Assert.Equal("New name", result.Name);
        Assert.Equal(InstanceIcons.DiamondPickaxe, result.IconKey);
        Assert.Equal(1024, result.MinimumRamMb);
        Assert.Equal(4096, result.MaximumRamMb);
        // Persisted to the store.
        Assert.NotNull(service.LastSavedInstance);
        Assert.Equal("stable-id", service.LastSavedInstance!.Id);
        // List + selection swapped to the new record.
        Assert.Single(vm.Instances);
        Assert.Same(result, vm.Instances[0]);
        Assert.Same(result, vm.SelectedInstance);
    }

    [Fact]
    public async Task SavedInstance_NullOverrides_PersistsAsInheritance()
    {
        var service = new StubLauncherService();
        var vm = new MainViewModel(service, new RecordingLogger());

        var original = MakeInstance("orig") with
        {
            MinimumRamMb = 2048,
            MaximumRamMb = 6144,
            JvmArguments = "-XX:+UseG1GC",
            GameDirectory = @"C:\tmp\old",
            ResolutionWidth = 1920,
            ResolutionHeight = 1080,
        };
        vm.Instances.Add(original);

        // Clearing all the per-instance overrides should round-trip as nulls so the
        // launcher inherits the global LauncherSettings on next launch.
        var edited = original with
        {
            MinimumRamMb = null,
            MaximumRamMb = null,
            JvmArguments = null,
            GameDirectory = null,
            ResolutionWidth = null,
            ResolutionHeight = null,
        };

        var result = await vm.ApplyEditedInstanceAsync(original, edited);

        Assert.NotNull(result);
        Assert.Null(result!.MinimumRamMb);
        Assert.Null(result.MaximumRamMb);
        Assert.Null(result.JvmArguments);
        Assert.Null(result.GameDirectory);
        Assert.Null(result.ResolutionWidth);
        Assert.Null(result.ResolutionHeight);
    }

    private static MainViewModel NewVm() =>
        new(new StubLauncherService(), new RecordingLogger());

    private static Instance MakeInstance(string id) => new()
    {
        Id = id,
        Name = "Test",
        VersionId = "1.21.5",
    };
}
