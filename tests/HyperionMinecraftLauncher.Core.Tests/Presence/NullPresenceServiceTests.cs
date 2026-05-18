using TechTeaStudio.HyperionMinecraftLauncher.Core.Presence;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Presence;

public class NullPresenceServiceTests
{
    [Fact]
    public void SetIdle_DoesNotThrow()
    {
        var sut = new NullPresenceService();
        var ex = Record.Exception(() => sut.SetIdle());
        Assert.Null(ex);
    }

    [Fact]
    public void SetPlaying_WithInstanceName_DoesNotThrow()
    {
        var sut = new NullPresenceService();
        var ex = Record.Exception(() => sut.SetPlaying("1.21.5", "My Modpack"));
        Assert.Null(ex);
    }

    [Fact]
    public void SetPlaying_NullInstanceName_DoesNotThrowNRE()
    {
        var sut = new NullPresenceService();
        var ex = Record.Exception(() => sut.SetPlaying("1.21.5", instanceName: null));
        Assert.Null(ex);
    }

    [Fact]
    public void Stop_DoesNotThrow()
    {
        var sut = new NullPresenceService();
        var ex = Record.Exception(() => sut.Stop());
        Assert.Null(ex);
    }

    [Fact]
    public void Methods_AreCallableMultipleTimes_InAnyOrder()
    {
        var sut = new NullPresenceService();
        sut.Stop();
        sut.SetIdle();
        sut.SetPlaying("1.20.4", null);
        sut.SetPlaying("1.21.5", "Vanilla");
        sut.SetIdle();
        sut.Stop();
        sut.Stop();
    }
}
