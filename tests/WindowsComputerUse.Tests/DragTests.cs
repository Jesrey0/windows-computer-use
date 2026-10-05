using System.Text.Json;
using WindowsComputerUse.Core;
using Xunit;

namespace WindowsComputerUse.Tests;

public class DragTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void PointCountsAreBounded(int count, bool valid) => Assert.Equal(valid,
        InputLimits.ValidDrag(new(Enumerable.Repeat(new DragPoint(0, 0), count).ToArray())));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public void DurationIsBounded(int duration, bool valid) => Assert.Equal(valid,
        InputLimits.ValidDrag(new([new(0, 0), new(10, 10)], duration)));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(511, true)]
    [InlineData(512, false)]
    [InlineData(int.MaxValue, false)]
    public void StepCountsIncludeInitialMoveAndCannotOverflow(int steps, bool valid) => Assert.Equal(valid,
        InputLimits.ValidDrag(new([new(0, 0), new(10, 10)], StepsPerSegment: steps)));

    [Fact]
    public void NullPointsAndCombinedStepBudgetAreRejected()
    {
        Assert.False(InputLimits.ValidDrag(null));
        Assert.False(InputLimits.ValidDrag(new(null)));
        Assert.False(InputLimits.ValidDrag(new([new(0, 0), null!])));
        Assert.True(InputLimits.ValidDrag(new([new(0, 0), new(1, 1), new(2, 2)], StepsPerSegment: 255)));
        Assert.False(InputLimits.ValidDrag(new([new(0, 0), new(1, 1), new(2, 2)], StepsPerSegment: 256)));
        var error = Assert.Throws<AutomationException>(() => DragGesture.Interpolate(null));
        Assert.Equal("invalid_input", error.Code);
        Assert.Equal(400, error.Status);
    }

    [Theory]
    [InlineData("{\"points\":[{\"x\":2147483648,\"y\":0},{\"x\":0,\"y\":0}]}")]
    [InlineData("{\"points\":[{\"x\":-2147483649,\"y\":0},{\"x\":0,\"y\":0}]}")]
    [InlineData("{\"points\":[{\"x\":0.5,\"y\":0},{\"x\":0,\"y\":0}]}")]
    [InlineData("{\"points\":[{\"y\":0},{\"x\":0,\"y\":0}]}")]
    [InlineData("{\"points\":[{\"x\":0},{\"x\":0,\"y\":0}]}")]
    public void CoordinatesMustBePresentAndFitInt(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DragRequest>(json, JsonOptions));

    [Fact]
    public void MinimalJsonUsesImmediateBoundedDefaults()
    {
        var request = JsonSerializer.Deserialize<DragRequest>(
            "{\"points\":[{\"x\":-10,\"y\":-20},{\"x\":20,\"y\":30}]}", JsonOptions)!;
        Assert.True(InputLimits.ValidDrag(request));
        Assert.Equal(0, request.DurationMs);
        Assert.Null(request.StepsPerSegment);
        Assert.Equal(17, DragGesture.Interpolate(request).Length);
        var maxPoints = new DragRequest(Enumerable.Repeat(new DragPoint(0, 0), 128).ToArray());
        Assert.Equal(509, DragGesture.Interpolate(maxPoints).Length);
    }

    [Fact]
    public void PolylineInterpolationPreservesVerticesAndIsDeterministic()
    {
        var request = new DragRequest([new(-5, -5), new(5, 5), new(5, -5)], StepsPerSegment: 4);
        DragPoint[] expected = [new(-5, -5), new(-3, -3), new(0, 0), new(2, 2), new(5, 5),
            new(5, 3), new(5, 0), new(5, -2), new(5, -5)];
        Assert.Equal(expected, DragGesture.Interpolate(request));
        Assert.Equal(expected, DragGesture.Interpolate(request));
        Assert.Equal(new DragPoint(5, -5), request.Points![2]);
    }

    [Fact]
    public void OppositeIntExtremesAndRepeatedVerticesAreSafe()
    {
        var request = new DragRequest([new(int.MinValue, int.MaxValue), new(int.MaxValue, int.MinValue)],
            StepsPerSegment: 2);
        Assert.Equal(new DragPoint[] { request.Points![0], new(-1, 0), request.Points[1] },
            DragGesture.Interpolate(request));
        var repeated = new DragPoint(-100, -200);
        Assert.Equal(Enumerable.Repeat(repeated, 4),
            DragGesture.Interpolate(new([repeated, repeated], StepsPerSegment: 3)));
    }

    [Fact]
    public void VirtualDesktopMappingSupportsNegativeCoordinatesAndEdges()
    {
        Assert.Equal((0, 0), NativeInput.AbsoluteCoordinates(-1920, -1080, -1920, -1080, 3840, 2160));
        Assert.Equal((65535, 65535), NativeInput.AbsoluteCoordinates(1919, 1079, -1920, -1080, 3840, 2160));
        foreach (var point in new DragPoint[] { new(-1921, 0), new(1920, 0), new(0, -1081), new(0, 1080) })
        {
            var error = Assert.Throws<AutomationException>(() => NativeInput.AbsoluteCoordinates(
                point.X, point.Y, -1920, -1080, 3840, 2160));
            Assert.Equal("invalid_coordinates", error.Code);
            Assert.Equal(400, error.Status);
        }
        Assert.Throws<AutomationException>(() => NativeInput.AbsoluteCoordinates(0, 0, 0, 0, 1, 1));
    }

    [Fact]
    public void ImmediateGestureHasExactOrderAndTelemetryWithoutPacing()
    {
        var events = new List<string>();
        var points = DragGesture.Interpolate(new([new(0, 0), new(10, 0)], StepsPerSegment: 2));
        var metadata = DragGesture.Execute(points, 0,
            point => events.Add($"move:{point.X}"), () => events.Add("down"), () => events.Add("up"),
            _ => throw new InvalidOperationException("Immediate gesture must not sleep."));
        Assert.Equal(new[] { "move:0", "down", "move:5", "move:10", "up" }, events);
        Assert.Equal(5, metadata.EmittedInputCount);
        Assert.Equal(2, metadata.Steps);
        Assert.True(metadata.ElapsedMs >= 0);
        var json = JsonSerializer.Serialize(new DragResponse(true, metadata), JsonOptions);
        Assert.Contains("\"elapsedMs\":", json);
        Assert.Contains("\"emittedInputCount\":5", json);
    }

    [Fact]
    public void MaximumGestureKeepsMovementAndInjectionCountsBounded()
    {
        var points = DragGesture.Interpolate(new([new(int.MinValue, 0), new(int.MaxValue, 0)],
            StepsPerSegment: InputLimits.MaxDragSteps - 1));
        var moves = 0;
        var buttons = 0;
        var metadata = DragGesture.Execute(points, 0, _ => moves++, () => buttons++, () => buttons++);
        Assert.Equal(512, points.Length);
        Assert.Equal(512, moves);
        Assert.Equal(2, buttons);
        Assert.Equal(511, metadata.Steps);
        Assert.Equal(514, metadata.EmittedInputCount);
        Assert.Equal(new DragPoint(int.MaxValue, 0), points[^1]);
    }

    [Fact]
    public void PacingUsesCumulativeDeadlinesAndAccountsForInputOverhead()
    {
        long now = 0;
        var delays = new List<int>();
        var points = DragGesture.Interpolate(new([new(0, 0), new(20, 0)], StepsPerSegment: 4));
        DragGesture.Execute(points, 101, _ => now += 3, () => now += 2, () => { },
            delay => { delays.Add(delay); now += delay; }, () => now);
        Assert.Equal(new[] { 25, 22, 22, 23 }, delays);
        Assert.Equal(109, now); // 5ms initial move/down + 101ms pacing + 3ms final move.
        Assert.Equal(new[] { 25, 50, 75, 101 },
            Enumerable.Range(1, 4).Select(step => DragGesture.DueMs(101, step, 4)));
    }

    [Fact]
    public void OverdueStepsDoNotAddSleep()
    {
        long now = 0;
        var points = DragGesture.Interpolate(new([new(0, 0), new(10, 0)], StepsPerSegment: 2));
        DragGesture.Execute(points, 10, _ => { }, () => { }, () => { },
            _ => throw new InvalidOperationException("Steps are overdue."), () => { now += 100; return now; });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReleaseOccursOnlyAfterSuccessfulDown(int failAt)
    {
        var events = new List<string>();
        var calls = 0;
        void Emit(string name)
        {
            events.Add(name);
            if (calls++ == failAt) throw new AutomationException("input_blocked", "Injected failure.");
        }
        var error = Assert.Throws<AutomationException>(() => DragGesture.Execute(
            [new(0, 0), new(10, 0)], 0,
            _ => Emit("move"), () => Emit("down"), () => Emit("up")));
        Assert.Equal("input_blocked", error.Code);
        if (failAt < 2) Assert.DoesNotContain("up", events);
        else Assert.Equal(new[] { "move", "down", "move", "up" }, events);
    }

    [Fact]
    public void PacingFailureStillReleasesButton()
    {
        var released = false;
        Assert.Throws<InvalidOperationException>(() => DragGesture.Execute([new(0, 0), new(1, 1)], 10,
            _ => { }, () => { }, () => released = true,
            _ => throw new InvalidOperationException("Pacing failure."), () => 0));
        Assert.True(released);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseFailureIsVisibleEvenWhenMovementAlsoFails(bool movementFails)
    {
        var moves = 0;
        var releases = 0;
        var error = Assert.Throws<AutomationException>(() => DragGesture.Execute([new(0, 0), new(1, 1)], 0,
            _ => { if (moves++ == 1 && movementFails) throw new AutomationException("input_blocked", "Move failed."); },
            () => { }, () => { releases++; throw new AutomationException("input_blocked", "Up failed."); }));
        Assert.Equal("drag_release_failed", error.Code);
        Assert.Equal(422, error.Status);
        Assert.Equal(1, releases);
        if (movementFails) Assert.Contains("Earlier gesture error: input_blocked", error.Message);
    }
}
