namespace taskTru;

internal static class VideoDetectionService
{
    private static readonly TimeSpan BatchDetectionTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly object DetectionLock = new();
    private static readonly Dictionary<nint, Task<VideoDetectionResult>> RunningDetections = [];

    public static async Task<VideoDetectionResult> TryDetectAsync(
        nint window,
        CancellationToken cancellationToken = default)
    {
        if (!NativeMethods.IsWindow(window))
            return new(false, Rectangle.Empty);

        return await TryDetectWindowAsync(
            window,
            cancellationToken);
    }

    public static async Task<IReadOnlyDictionary<nint, VideoDetectionResult>> TryDetectManyAsync(
        IEnumerable<nint> windows,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new Dictionary<nint, VideoDetectionResult>();
        nint[] candidates =
            [.. windows
                .Distinct()
                .Where(window =>
                {
                    if (NativeMethods.IsWindow(window))
                        return true;

                    results[window] = new(false, Rectangle.Empty);
                    return false;
                })];

        if (candidates.Length == 0)
            return results;

        foreach (nint window in candidates)
        {
            try
            {
                results[window] = await TryDetectWindowAsync(
                    window,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                results[window] = new(false, Rectangle.Empty);
            }
        }

        return results;
    }

    private static async Task<VideoDetectionResult> TryDetectWindowAsync(
        nint window,
        CancellationToken cancellationToken)
    {
        Task<VideoDetectionResult>? detection =
            StartDetection(window, cancellationToken);
        if (detection is null)
            return new(false, Rectangle.Empty);

        try
        {
            return await detection.WaitAsync(
                BatchDetectionTimeout,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(false, Rectangle.Empty);
        }
    }

    private static Task<VideoDetectionResult>? StartDetection(
        nint window,
        CancellationToken cancellationToken)
    {
        lock (DetectionLock)
        {
            if (RunningDetections.TryGetValue(
                    window,
                    out Task<VideoDetectionResult>? running))
            {
                return running;
            }

            Task<VideoDetectionResult> detection =
                TryDetectOneAsync(window, cancellationToken);
            RunningDetections[window] = detection;
            _ = detection.ContinueWith(
                completed =>
                {
                    lock (DetectionLock)
                    {
                        if (RunningDetections.TryGetValue(
                                window,
                                out Task<VideoDetectionResult>? current)
                            && current == completed)
                        {
                            RunningDetections.Remove(window);
                        }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return detection;
        }
    }

    private static Task<VideoDetectionResult> TryDetectOneAsync(
        nint window,
        CancellationToken cancellationToken) =>
        Task.Run<VideoDetectionResult>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return NativeMethods.IsWindow(window)
                    && VideoPlayerDetector.TryFindBounds(window, out Rectangle bounds)
                        ? new(true, bounds)
                        : new(false, Rectangle.Empty);
            },
            cancellationToken);
}

internal readonly record struct VideoDetectionResult(bool Found, Rectangle Bounds);
