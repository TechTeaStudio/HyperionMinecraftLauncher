using System;
using System.Collections.Generic;
using TechTeaStudio.HyperionMinecraftLauncher.Core.Util;

namespace TechTeaStudio.HyperionMinecraftLauncher.Core.Tests.Util;

/// <summary>
/// Smoke tests for the hand-rolled <see cref="Subject{T}"/> shim. Covers the slice we
/// actually use in the game-log piping: synchronous fan-out, multicast to multiple
/// observers, completion semantics, and the unsubscribe-on-dispose contract.
/// </summary>
public class SubjectTests
{
    [Fact]
    public void Subscribe_OnNext_DeliversValuesToSubscriber()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        subject.Subscribe(received.Add);

        subject.OnNext(1);
        subject.OnNext(2);
        subject.OnNext(3);

        Assert.Equal(new[] { 1, 2, 3 }, received);
    }

    [Fact]
    public void Subscribe_MultipleSubscribers_AllReceiveEveryEmission()
    {
        var subject = new Subject<string>();
        var a = new List<string>();
        var b = new List<string>();
        subject.Subscribe(a.Add);
        subject.Subscribe(b.Add);

        subject.OnNext("hello");
        subject.OnNext("world");

        Assert.Equal(new[] { "hello", "world" }, a);
        Assert.Equal(new[] { "hello", "world" }, b);
    }

    [Fact]
    public void Dispose_StopsFurtherEmissions()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        var sub = subject.Subscribe(received.Add);

        subject.OnNext(1);
        sub.Dispose();
        subject.OnNext(2);

        Assert.Equal(new[] { 1 }, received);
    }

    [Fact]
    public void OnCompleted_FiresOnceAndSilencesSubsequentOnNext()
    {
        var subject = new Subject<int>();
        var received = new List<int>();
        var completed = 0;
        subject.Subscribe(received.Add, () => completed++);

        subject.OnNext(1);
        subject.OnCompleted();
        subject.OnNext(2);
        // Idempotent: second OnCompleted is a no-op.
        subject.OnCompleted();

        Assert.Equal(new[] { 1 }, received);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void Subscribe_AfterCompleted_ImmediatelyFiresOnCompleted()
    {
        var subject = new Subject<int>();
        subject.OnCompleted();

        var completed = false;
        var received = new List<int>();
        subject.Subscribe(received.Add, () => completed = true);

        Assert.True(completed);
        Assert.Empty(received);
    }

    [Fact]
    public void OnNext_ContinuesEvenIfSubscriberThrows()
    {
        var subject = new Subject<int>();
        var good = new List<int>();
        subject.Subscribe(_ => throw new InvalidOperationException("bad subscriber"));
        subject.Subscribe(good.Add);

        // The throwing subscriber must not prevent the second subscriber from receiving.
        subject.OnNext(42);

        Assert.Equal(new[] { 42 }, good);
    }
}
