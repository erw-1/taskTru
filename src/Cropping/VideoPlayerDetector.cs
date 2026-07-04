using System.Runtime.InteropServices;
using static taskTru.NativeMethods;
using static taskTru.UiAutomation;

namespace taskTru;

internal static class VideoPlayerDetector
{
    private const int MinimumPlayerWidth = 160;
    private const int MinimumPlayerHeight = 90;

    // Filtering offscreen/disabled elements server-side and caching every property
    // the scorer reads keeps the scan to one cross-process round-trip per FindAll
    // instead of several per element; browser trees have thousands of elements.
    public static bool TryFindBounds(
        nint target,
        out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!IsWindow(target)
            || !WindowGeometry.TryGetFrameBounds(target, out Rectangle targetBounds))
        {
            return false;
        }

        IUIAutomationElement? player = null;
        try
        {
            IUIAutomation automation = Instance;
            IUIAutomationElement root =
                automation.ElementFromHandle(target);
            IUIAutomationCondition candidateCondition =
                automation.CreateAndCondition(
                    automation.CreatePropertyCondition(
                        IsOffscreenProperty,
                        false),
                    automation.CreatePropertyCondition(
                        IsEnabledProperty,
                        true));
            IUIAutomationCacheRequest candidateCache =
                automation.CreateCacheRequest();
            candidateCache.AddProperty(BoundingRectangleProperty);
            candidateCache.AddProperty(ClassNameProperty);
            candidateCache.AddProperty(AutomationIdProperty);
            candidateCache.AddProperty(NameProperty);
            candidateCache.AddProperty(ControlTypeProperty);
            IUIAutomationElementArray elements = root.FindAllBuildCache(
                TreeScopeDescendants,
                candidateCondition,
                candidateCache);
            int bestScore = 0;
            long bestArea = 0;
            int count = elements.Length;

            for (int index = 0; index < count; index++)
            {
                IUIAutomationElement element = elements.GetElement(index);
                if (!TryGetCandidate(
                        element,
                        targetBounds,
                        out Rectangle candidate,
                        out int score))
                {
                    continue;
                }

                long area =
                    (long)candidate.Width * candidate.Height;
                if (score < bestScore
                    || (score == bestScore && area <= bestArea))
                    continue;

                bestScore = score;
                bestArea = area;
                player = element;
                bounds = candidate;
            }
        }
        catch (Exception exception)
            when (IsAutomationFailure(exception))
        {
            bounds = Rectangle.Empty;
            return false;
        }

        if (player is not null
            && TryGetVideoSurfaceBounds(
                player,
                targetBounds,
                out Rectangle videoBounds))
        {
            bounds = videoBounds;
        }

        return player is not null
            && !bounds.IsEmpty;
    }

    private static bool TryGetVideoSurfaceBounds(
        IUIAutomationElement player,
        Rectangle targetBounds,
        out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        long bestArea = 0;

        try
        {
            IUIAutomation automation = Instance;
            IUIAutomationCacheRequest surfaceCache =
                automation.CreateCacheRequest();
            surfaceCache.AddProperty(BoundingRectangleProperty);
            surfaceCache.AddProperty(ClassNameProperty);
            IUIAutomationElementArray elements = player.FindAllBuildCache(
                TreeScopeDescendants,
                automation.CreatePropertyCondition(
                    IsOffscreenProperty,
                    false),
                surfaceCache);
            int count = elements.Length;

            for (int index = 0; index < count; index++)
            {
                IUIAutomationElement element = elements.GetElement(index);
                string className = CachedString(element, ClassNameProperty);
                if ((!Contains(className, "video-stream")
                        && !Contains(className, "html5-main-video"))
                    || !TryGetCachedBounds(
                        element,
                        out Rectangle candidate))
                {
                    continue;
                }

                candidate = Rectangle.Intersect(
                    candidate,
                    targetBounds);
                long area =
                    (long)candidate.Width * candidate.Height;
                if (candidate.Width < MinimumPlayerWidth
                    || candidate.Height < MinimumPlayerHeight
                    || area <= bestArea)
                {
                    continue;
                }

                bestArea = area;
                bounds = candidate;
            }
        }
        catch (Exception exception)
            when (IsAutomationFailure(exception))
        {
            return false;
        }

        return !bounds.IsEmpty;
    }

    private static bool TryGetCandidate(
        IUIAutomationElement element,
        Rectangle targetBounds,
        out Rectangle bounds,
        out int score)
    {
        bounds = Rectangle.Empty;
        score = 0;

        try
        {
            if (!TryGetCachedBounds(
                    element,
                    out Rectangle elementBounds))
            {
                return false;
            }

            Rectangle intersection = Rectangle.Intersect(
                elementBounds,
                targetBounds);
            if (intersection.Width < MinimumPlayerWidth
                || intersection.Height < MinimumPlayerHeight)
            {
                return false;
            }

            long elementArea =
                (long)elementBounds.Width * elementBounds.Height;
            long intersectionArea =
                (long)intersection.Width * intersection.Height;
            if (intersectionArea < elementArea * 0.8)
                return false;

            score = Score(element);
            bounds = intersection;
            return score >= 100;
        }
        catch (Exception exception)
            when (IsAutomationFailure(exception))
        {
            return false;
        }
    }

    private static int Score(IUIAutomationElement element)
    {
        string className = CachedString(element, ClassNameProperty);
        string automationId = CachedString(element, AutomationIdProperty);
        string name = CachedString(element, NameProperty);
        int controlType =
            element.GetCachedPropertyValue(ControlTypeProperty)
                is int cachedControlType
                ? cachedControlType
                : 0;
        int score = 0;
        if (Contains(className, "html5-video-player"))
        {
            score += 500;
        }

        if (string.Equals(
                automationId,
                "movie_player",
                StringComparison.OrdinalIgnoreCase))
        {
            score += 400;
        }
        else if (Contains(automationId, "video")
                 || Contains(automationId, "player"))
        {
            score += 160;
        }

        if (Contains(name, "video player"))
            score += 250;
        else if (Contains(name, "video"))
            score += 80;

        if (controlType is GroupControlType or PaneControlType)
        {
            score += 30;
        }

        return score;
    }

    private static string CachedString(
        IUIAutomationElement element,
        int propertyId) =>
        element.GetCachedPropertyValue(propertyId) as string
        ?? string.Empty;

    private static bool TryGetCachedBounds(
        IUIAutomationElement element,
        out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        // The cached rectangle VARIANT is a double[4] of left, top, width, height.
        if (element.GetCachedPropertyValue(BoundingRectangleProperty)
                is not double[] rect
            || rect.Length != 4)
        {
            return false;
        }

        return TryConvertBounds(
            rect[0],
            rect[1],
            rect[2],
            rect[3],
            out bounds);
    }

    private static bool TryConvertBounds(
        double x,
        double y,
        double width,
        double height,
        out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (double.IsNaN(x)
            || double.IsNaN(y)
            || double.IsNaN(width)
            || double.IsNaN(height)
            || double.IsInfinity(x)
            || double.IsInfinity(y)
            || double.IsInfinity(width)
            || double.IsInfinity(height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        int left = (int)Math.Floor(x);
        int top = (int)Math.Floor(y);
        int right = (int)Math.Ceiling(x + width);
        int bottom = (int)Math.Ceiling(y + height);
        if (right <= left || bottom <= top)
            return false;

        bounds = Rectangle.FromLTRB(
            left,
            top,
            right,
            bottom);
        return true;
    }

    private static bool Contains(string value, string search) =>
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static bool IsAutomationFailure(Exception exception) =>
        exception is COMException
            or InvalidOperationException
            or InvalidCastException
            or NotSupportedException;
}
