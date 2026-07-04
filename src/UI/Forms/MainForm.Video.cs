using System.Drawing;
using static taskTru.NativeMethods;

namespace taskTru;

// Passive video detection scheduling and auto video crop for MainForm.
public sealed partial class MainForm
{
    private void QueueVideoScan(
        List<WindowInfo> windows,
        bool force = false,
        bool scanAll = false)
    {
        if (!_settings.ScanForVideoContent)
        {
            ClearVideoDetections();
            return;
        }

        if (!force)
            return;

        if (!IsHandleCreated)
            return;

        List<WindowInfo> scanWindows =
            [.. windows.Where(window =>
                !IsCropped(window.Handle)
                && ShouldTryPassiveVideoScan(window))];
        if (scanWindows.Count == 0)
        {
            if (windows.Count == 0)
                ClearVideoDetections();
            return;
        }
        windows = scanWindows;

        if (_videoScanRunning)
            return;

        nint[] handles = SelectVideoScanHandles(windows, scanAll);
        if (handles.Length == 0)
            return;

        _videoScanRunning = true;
        _ = ScanVideoWindowsAsync(
            handles);
    }

    private void QueueStartupVideoScan()
    {
        if (_startupVideoScanQueued)
            return;

        _startupVideoScanQueued = true;
        _videoRetryArmed = false;
        QueueVideoScan(
            [.. _rows.Values.Select(row => row.Window)],
            force: true,
            scanAll: true);
    }

    private static bool ShouldTryPassiveVideoScan(WindowInfo window) =>
        PassiveVideoProcesses.Any(process =>
            window.ProcessName.Contains(
                process,
                StringComparison.OrdinalIgnoreCase));

    private nint[] SelectVideoScanHandles(
        List<WindowInfo> windows,
        bool scanAll = false)
    {
        nint[] handles =
            [.. windows
                .Select(window => window.Handle)
                .Where(handle => IsWindow(handle) && !IsCropped(handle))
                .Distinct()];
        nint foreground = GetForegroundWindow();
        if (TryGetSourceHandle(foreground, out nint sourceHandle))
            foreground = sourceHandle;
        int foregroundIndex = Array.IndexOf(handles, foreground);
        if (foregroundIndex > 0)
            (handles[0], handles[foregroundIndex]) =
                (handles[foregroundIndex], handles[0]);

        if (scanAll || handles.Length <= VideoScanBatchSize)
            return handles;

        var selected = new List<nint>(VideoScanBatchSize);
        if (handles.Contains(foreground))
            selected.Add(foreground);

        int cursor = Math.Min(_videoScanCursor, handles.Length - 1);
        for (int offset = 0;
             selected.Count < VideoScanBatchSize && offset < handles.Length;
             offset++)
        {
            nint handle = handles[(cursor + offset) % handles.Length];
            if (!selected.Contains(handle))
                selected.Add(handle);
        }

        _videoScanCursor = (cursor + VideoScanBatchSize) % handles.Length;
        return [.. selected];
    }

    private async Task ScanVideoWindowsAsync(nint[] handles)
    {
        try
        {
            IReadOnlyDictionary<nint, VideoDetectionResult> detected =
                await VideoDetectionService.TryDetectManyAsync(
                    handles,
                    _videoDetectionCancellation.Token);

            if (_disposed
                || IsDisposed
                || !_settings.ScanForVideoContent)
            {
                return;
            }

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => ApplyVideoDetections(
                        handles,
                        detected)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            ApplyVideoDetections(
                handles,
                detected);
        }
        catch
        {
            // Scanner is best-effort; stale video buttons are worse than a crash.
        }
        finally
        {
            _videoScanRunning = false;
            ScheduleUiaCleanup();
        }
    }

    private void ScheduleUiaCleanup()
    {
        if (_disposed || IsDisposed)
            return;

        _uiaCleanupTimer.Stop();
        _uiaCleanupTimer.Start();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        RequestFreshVideoScan();
    }

    // Scans are event-driven (startup, focus, tray, crop start, title change, uncrop)
    // so there is zero background cost while the user is away doing something else.
    private void RequestFreshVideoScan()
    {
        if (!_settings.ScanForVideoContent
            || _disposed
            || !IsHandleCreated)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (now - _lastFreshVideoScanTick < 3000)
            return;

        _lastFreshVideoScanTick = now;
        _videoRetryArmed = false;
        QueueVideoScan(
            [.. _rows.Values.Select(row => row.Window)],
            force: true);
    }

    private async void QueueSingleVideoScan(nint handle)
    {
        if (!_settings.ScanForVideoContent
            || _disposed
            || !IsHandleCreated
            || !_rows.TryGetValue(handle, out WindowRow? row)
            || !ShouldTryPassiveVideoScan(row.Window)
            || IsCropped(handle)
            || !IsWindow(handle))
        {
            return;
        }

        long now = Environment.TickCount64;
        if (_singleVideoScanTicks.TryGetValue(handle, out long last)
            && now - last < 2000)
        {
            return;
        }

        _singleVideoScanTicks[handle] = now;
        _videoRetryArmed = false;
        try
        {
            VideoDetectionResult result =
                await VideoDetectionService.TryDetectAsync(
                    handle,
                    _videoDetectionCancellation.Token);
            if (_disposed || IsDisposed || !_settings.ScanForVideoContent)
                return;

            ApplyVideoDetections(
                [handle],
                new Dictionary<nint, VideoDetectionResult> { [handle] = result });
        }
        catch
        {
        }
        finally
        {
            if (!_disposed && !IsDisposed)
                ScheduleUiaCleanup();
        }
    }

    private void ApplyVideoDetections(
        nint[] scannedHandles,
        IReadOnlyDictionary<nint, VideoDetectionResult> detected)
    {
        if (_disposed
            || IsDisposed
            || !_settings.ScanForVideoContent)
        {
            return;
        }

        foreach (nint handle in scannedHandles)
            _videoBounds.Remove(handle);

        bool missedCandidate = false;
        foreach ((nint handle, VideoDetectionResult result) in detected)
        {
            if (result.Found)
            {
                if (_rows.TryGetValue(handle, out WindowRow? row))
                    CacheVideoBounds(row.Window, result.Bounds);
            }
            else if (_rows.TryGetValue(handle, out WindowRow? missedRow)
                     && ShouldTryPassiveVideoScan(missedRow.Window)
                     && !IsCropped(handle))
            {
                missedCandidate = true;
            }
        }

        foreach (WindowRow row in _rows.Values)
        {
            row.SetVideoDetected(
                TryGetCachedVideoBounds(row.Window, out _));
        }

        if (missedCandidate && !_videoRetryArmed)
        {
            _videoRetryArmed = true;
            _videoRetryTimer.Stop();
            _videoRetryTimer.Start();
        }
    }

    private void ClearVideoDetections()
    {
        _videoBounds.Clear();
        foreach (WindowRow row in _rows.Values)
            row.SetVideoDetected(false);
    }

    private bool TryGetCachedVideoBounds(
        WindowInfo window,
        out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!_videoBounds.TryGetValue(
                window.Handle,
                out CachedVideoBounds? cached))
        {
            return false;
        }

        if (string.Equals(
                cached.Title,
                window.Title,
                StringComparison.Ordinal)
            && WindowGeometry.TryGetFrameBounds(
                window.Handle,
                out Rectangle frameBounds)
            && frameBounds == cached.FrameBounds)
        {
            bounds = cached.Bounds;
            return !bounds.IsEmpty;
        }

        _videoBounds.Remove(window.Handle);
        return false;
    }

    private void CacheVideoBounds(
        WindowInfo window,
        Rectangle bounds)
    {
        if (bounds.IsEmpty
            || !WindowGeometry.TryGetFrameBounds(
                window.Handle,
                out Rectangle frameBounds))
        {
            _videoBounds.Remove(window.Handle);
            return;
        }

        _videoBounds[window.Handle] = new(
            bounds,
            frameBounds,
            window.Title);
    }

    private async void StartVideoCrop(WindowInfo window, WindowState state)
    {
        if (IsCropped(window.Handle)
            || !IsWindow(window.Handle))
        {
            return;
        }

        using WaitCursorScope waitCursor = ShowWaitCursor();
        if (!TryGetCachedVideoBounds(window, out Rectangle selectedBounds))
        {
            VideoDetectionResult result;
            try
            {
                result = await VideoDetectionService.TryDetectAsync(
                    window.Handle,
                    _videoDetectionCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_disposed
                || IsDisposed
                || !IsWindow(window.Handle)
                || result is not { Found: true } found)
            {
                return;
            }

            selectedBounds = found.Bounds;
            CacheVideoBounds(window, selectedBounds);
        }

        if (selectedBounds.IsEmpty)
            return;

        if (IsIconic(window.Handle))
            ShowWindow(window.Handle, ShowWindowCommand.Restore);

        try
        {
            RegisterCrop(
                window,
                state,
                new(
                    window.Handle,
                    window.Title,
                    selectedBounds,
                    state));
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task<Rectangle?> TryGetFreshVideoBounds(WindowInfo window)
    {
        if (!_settings.ScanForVideoContent
            || !ShouldTryPassiveVideoScan(window))
        {
            return null;
        }

        using WaitCursorScope waitCursor = ShowWaitCursor();
        try
        {
            VideoDetectionResult fresh =
                await VideoDetectionService.TryDetectAsync(
                        window.Handle,
                        _videoDetectionCancellation.Token)
                    .WaitAsync(TimeSpan.FromMilliseconds(2500));
            // A completed scan is authoritative either way: cache the hit or
            // drop the stale entry so the overlay never offers dead coordinates.
            CacheVideoBounds(
                window,
                fresh.Found ? fresh.Bounds : Rectangle.Empty);
            if (_rows.TryGetValue(window.Handle, out WindowRow? row))
                row.SetVideoDetected(fresh.Found);

            return fresh.Found ? fresh.Bounds : null;
        }
        catch (TimeoutException)
        {
            // Slow scan: fall back to the cache rather than stalling the crop.
            return TryGetCachedVideoBounds(window, out Rectangle cached)
                ? cached
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ScheduleUiaCleanup();
        }
    }

    private sealed record CachedVideoBounds(
        Rectangle Bounds,
        Rectangle FrameBounds,
        string Title);
}
