namespace SegmentedRegex.Tests;

/// <summary>
/// Drives the capture and crawl machinery directly. The emitted body is the only real caller, so
/// until Phase 2 exists these exercise it through a derived probe.
/// </summary>
public class SegExRunnerTests
{
    /// <summary>Exposes the protected runner surface to the tests. Never scans.</summary>
    private sealed class Probe : SegExRunner
    {
        // 'protected', not 'protected internal': a cross-assembly override cannot keep the internal
        // half. This is what the emitted Runner in a consuming project will have to declare too.
        protected override void Scan(SegmentedSpan inputSpan) => throw new NotSupportedException();

        internal Probe(int capCount, TimeSpan timeout = default)
        {
            InitializeForScan(capCount, 100, 0, timeout == default ? Regex.InfiniteMatchTimeout : timeout, "test");
        }

        internal void DoCapture(int capnum, int start, int end) => Capture(capnum, start, end);
        internal void DoTransferCapture(int capnum, int uncapnum, int start, int end) => TransferCapture(capnum, uncapnum, start, end);
        internal void DoUncapture() => Uncapture();
        internal int Mark() => Crawlpos();
        internal bool Matched(int cap) => IsMatched(cap);
        internal int IndexOf(int cap) => MatchIndex(cap);
        internal int LengthOf(int cap) => MatchLength(cap);
        internal void DoCheckTimeout() => CheckTimeout();
        internal bool Found => FoundMatch;
        internal void DoTidy() => Tidy();
        internal int Captures(int cap) => CaptureCount(cap);
        internal (int Index, int Length) CaptureAt(int cap, int i) => (CaptureIndex(cap, i), CaptureLength(cap, i));
    }

    [Fact]
    public void ReportsNoMatchUntilGroupZeroIsCaptured()
    {
        var runner = new Probe(3);

        Assert.False(runner.Found);
        Assert.False(runner.Matched(0));
        Assert.False(runner.Matched(1));

        runner.DoCapture(0, 2, 7);

        Assert.True(runner.Found);
        Assert.True(runner.Matched(0));
        Assert.Equal(2, runner.IndexOf(0));
        Assert.Equal(5, runner.LengthOf(0));
    }

    [Fact]
    public void NormalizesReversedCaptureBounds()
    {
        var runner = new Probe(2);
        runner.DoCapture(1, 9, 4);

        Assert.Equal(4, runner.IndexOf(1));
        Assert.Equal(5, runner.LengthOf(1));
    }

    [Fact]
    public void ReportsTheMostRecentCaptureOfAGroup()
    {
        var runner = new Probe(2);
        runner.DoCapture(1, 0, 2);
        runner.DoCapture(1, 5, 9);

        Assert.Equal(5, runner.IndexOf(1));
        Assert.Equal(4, runner.LengthOf(1));

        runner.DoTidy();
        Assert.Equal(2, runner.Captures(1));
        Assert.Equal((0, 2), runner.CaptureAt(1, 0));
        Assert.Equal((5, 4), runner.CaptureAt(1, 1));
    }

    [Fact]
    public void UncaptureRewindsToACrawlMark()
    {
        var runner = new Probe(3);
        runner.DoCapture(1, 0, 2);

        // The emitted body records a mark, speculatively captures, then unwinds on backtrack.
        var mark = runner.Mark();
        runner.DoCapture(1, 4, 6);
        runner.DoCapture(2, 7, 9);
        Assert.True(runner.Matched(2));

        while (runner.Mark() > mark)
        {
            runner.DoUncapture();
        }

        Assert.False(runner.Matched(2));
        Assert.True(runner.Matched(1));
        Assert.Equal(0, runner.IndexOf(1));
        Assert.Equal(2, runner.LengthOf(1));
    }

    [Fact]
    public void GrowsTheCaptureArrayAndKeepsEveryCapture()
    {
        var runner = new Probe(2);
        const int Count = 200;

        for (var i = 0; i < Count; i++)
        {
            runner.DoCapture(1, i * 2, (i * 2) + 1);
        }

        runner.DoTidy();
        Assert.Equal(Count, runner.Captures(1));
        for (var i = 0; i < Count; i++)
        {
            Assert.Equal((i * 2, 1), runner.CaptureAt(1, i));
        }
    }

    [Fact]
    public void GrowsTheCrawlStackAndStillUnwindsCompletely()
    {
        var runner = new Probe(2);
        // Comfortably past the initial 32-entry crawl stack, to force it to double.
        const int Count = 500;

        var mark = runner.Mark();
        for (var i = 0; i < Count; i++)
        {
            runner.DoCapture(1, i, i + 1);
        }
        Assert.Equal(mark + Count, runner.Mark());

        while (runner.Mark() > mark)
        {
            runner.DoUncapture();
        }

        Assert.False(runner.Matched(1));
        Assert.Equal(mark, runner.Mark());
    }

    [Fact]
    public void BalancingDropsTheTransferredCapture()
    {
        // Traced against upstream's algorithm: group 1 holds (0,2) then (4,2); balancing it away
        // appends a back-reference pair, and Tidy compacts that back down to just the first capture.
        var runner = new Probe(2);
        runner.DoCapture(1, 0, 2);
        runner.DoCapture(1, 4, 6);
        Assert.Equal(2, runner.Captures(1));

        runner.DoTransferCapture(-1, 1, 4, 6);
        runner.DoTidy();

        Assert.Equal(1, runner.Captures(1));
        Assert.Equal((0, 2), runner.CaptureAt(1, 0));
    }

    [Fact]
    public void BalancingCanFeedATransferTargetGroup()
    {
        var runner = new Probe(3);
        runner.DoCapture(1, 2, 4);
        runner.DoCapture(1, 6, 8);

        // Group 2 receives the span between the balanced group and the current position.
        runner.DoTransferCapture(2, 1, 8, 10);
        runner.DoTidy();

        Assert.Equal(1, runner.Captures(2));
        Assert.True(runner.Matched(2));
    }

    [Fact]
    public void TidyIsANoOpWithoutBalancing()
    {
        var runner = new Probe(2);
        runner.DoCapture(0, 0, 5);
        runner.DoCapture(1, 1, 3);

        runner.DoTidy();
        runner.DoTidy();

        Assert.Equal(1, runner.Captures(0));
        Assert.Equal((1, 2), runner.CaptureAt(1, 0));
    }

    [Fact]
    public void CheckTimeoutIsInertWhenTheTimeoutIsInfinite()
    {
        var runner = new Probe(1);
        for (var i = 0; i < 1000; i++)
        {
            runner.DoCheckTimeout();
        }
    }

    [Fact]
    public void CheckTimeoutThrowsOnceTheDeadlineHasPassed()
    {
        var runner = new Probe(1, TimeSpan.FromMilliseconds(1));

        // TickCount64 has coarse resolution, so wait well past the deadline rather than racing it.
        Thread.Sleep(50);

        var ex = Assert.Throws<RegexMatchTimeoutException>(runner.DoCheckTimeout);
        Assert.Equal("test", ex.Pattern);
    }
}
