using System.Diagnostics;

namespace WindowsComputerUse.Core;

internal static class DragGesture
{
    internal static DragPoint[] Interpolate(DragRequest? request)
    {
        if (!InputLimits.ValidDrag(request))
            throw new AutomationException("invalid_input",
                "Drag requires 2-128 points, durationMs 0-5000, and positive stepsPerSegment with at most 512 total moves.", 400);
        var drag = request!;
        var points = drag.Points!;
        var steps = InputLimits.DragStepsPerSegment(drag);
        var result = new DragPoint[1 + (points.Length - 1) * steps];
        result[0] = points[0];
        var index = 1;
        for (var segment = 1; segment < points.Length; segment++)
        {
            var start = points[segment - 1];
            var end = points[segment];
            for (var step = 1; step <= steps; step++)
            {
                // Integer arithmetic, including opposite int extremes. Truncate offsets
                // toward zero, preserve every vertex exactly, and never overflow int.
                var x = (int)(start.X + ((long)end.X - start.X) * step / steps);
                var y = (int)(start.Y + ((long)end.Y - start.Y) * step / steps);
                result[index++] = new(x, y);
            }
        }
        return result;
    }

    internal static int DueMs(int durationMs, int step, int steps) =>
        (int)((long)durationMs * step / steps);

    internal static DragMetadata Execute(DragPoint[] points, int durationMs,
        Action<DragPoint> move, Action down, Action up,
        Action<int>? delay = null, Func<long>? elapsedMs = null)
    {
        var timer = Stopwatch.StartNew();
        delay ??= Thread.Sleep;
        elapsedMs ??= () => timer.ElapsedMilliseconds;
        var emitted = 0;
        var pressed = false;
        Exception? failure = null;
        try
        {
            move(points[0]);
            emitted++;
            down();
            pressed = true;
            emitted++;
            var pacingStart = elapsedMs();
            for (var i = 1; i < points.Length; i++)
            {
                if (durationMs > 0)
                {
                    // Cumulative deadlines prevent SendInput overhead accumulating as drift.
                    var remaining = DueMs(durationMs, i, points.Length - 1) - (elapsedMs() - pacingStart);
                    if (remaining > 0) delay((int)remaining);
                }
                move(points[i]);
                emitted++;
            }
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            if (pressed)
            {
                try { up(); emitted++; }
                catch (Exception error)
                {
                    // Do not hide release failure behind an earlier movement failure.
                    var prior = failure is AutomationException automation ? automation.Code : failure?.GetType().Name;
                    throw new AutomationException("drag_release_failed",
                        $"Left-button release failed; button state is unconfirmed. Release error: {error.Message}" +
                        (prior is null ? "" : $" Earlier gesture error: {prior}."));
                }
            }
        }
        return new(timer.Elapsed.TotalMilliseconds, emitted, points.Length - 1);
    }
}
