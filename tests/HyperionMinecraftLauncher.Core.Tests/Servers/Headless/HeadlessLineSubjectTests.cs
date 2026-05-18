using System;
using System.Collections.Generic;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Servers.Headless;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Servers.Headless;

/// <summary>
/// Verifies the minimal in-tree subject we use to fan out stdout lines from
/// <see cref="ProcessHeadlessServer"/> to UI subscribers.
/// </summary>
public sealed class HeadlessLineSubjectTests
{
    [Fact]
    public void OnNext_DeliversToEverySubscriber()
    {
        var subject = new HeadlessLineSubject();
        var a = new List<string>();
        var b = new List<string>();
        subject.Subscribe(new RecordingObserver(a));
        subject.Subscribe(new RecordingObserver(b));

        subject.OnNext("hello");
        subject.OnNext("world");

        Assert.Equal(new[] { "hello", "world" }, a);
        Assert.Equal(new[] { "hello", "world" }, b);
    }

    [Fact]
    public void OnCompleted_NotifiesAndStopsFurtherEmissions()
    {
        var subject = new HeadlessLineSubject();
        var lines = new List<string>();
        var completed = false;
        subject.Subscribe(new RecordingObserver(lines, () => completed = true));

        subject.OnNext("alpha");
        subject.OnCompleted();
        subject.OnNext("ignored"); // dropped because we're past completion

        Assert.Equal(new[] { "alpha" }, lines);
        Assert.True(completed);
    }

    [Fact]
    public void Subscribe_AfterCompleted_FiresOnCompletedImmediately()
    {
        var subject = new HeadlessLineSubject();
        subject.OnCompleted();

        var lines = new List<string>();
        var completed = false;
        subject.Subscribe(new RecordingObserver(lines, () => completed = true));

        Assert.True(completed);
        Assert.Empty(lines);
    }

    [Fact]
    public void Dispose_Subscription_StopsDelivery()
    {
        var subject = new HeadlessLineSubject();
        var lines = new List<string>();
        var sub = subject.Subscribe(new RecordingObserver(lines));

        subject.OnNext("one");
        sub.Dispose();
        subject.OnNext("two");

        Assert.Equal(new[] { "one" }, lines);
    }

    private sealed class RecordingObserver : IObserver<string>
    {
        private readonly List<string> _buffer;
        private readonly Action? _onCompleted;
        public RecordingObserver(List<string> buffer, Action? onCompleted = null)
        {
            _buffer = buffer;
            _onCompleted = onCompleted;
        }
        public void OnNext(string value) => _buffer.Add(value);
        public void OnError(Exception error) { }
        public void OnCompleted() => _onCompleted?.Invoke();
    }
}
