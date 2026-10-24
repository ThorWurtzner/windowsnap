namespace WindowSnap;

public readonly record struct Box(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool Near(Box other, int tolerance = 3) =>
        Math.Abs(X - other.X) <= tolerance && Math.Abs(Y - other.Y) <= tolerance &&
        Math.Abs(Width - other.Width) <= tolerance && Math.Abs(Height - other.Height) <= tolerance;
}

public readonly record struct Region(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) && double.IsFinite(Height) &&
        X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.000001 && Y + Height <= 1.000001;
}

public static class Geometry
{
    public static Box Place(Box work, Region region, int gap = 0)
    {
        if (!region.IsValid || work.Width <= 0 || work.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(region));
        // Round edges, rather than widths, so adjacent thirds share the same boundary.
        int left = work.X + Round(work.Width * region.X);
        int top = work.Y + Round(work.Height * region.Y);
        int right = work.X + Round(work.Width * (region.X + region.Width));
        int bottom = work.Y + Round(work.Height * (region.Y + region.Height));
        int inset = Math.Clamp(gap, 0, Math.Max(0, (Math.Min(right - left, bottom - top) - 1) / 2));
        return new(left + inset, top + inset, Math.Max(1, right - left - inset * 2), Math.Max(1, bottom - top - inset * 2));
    }

    public static Region Relative(Box window, Box work)
    {
        double width = Math.Clamp((double)window.Width / work.Width, 0.01, 1);
        double height = Math.Clamp((double)window.Height / work.Height, 0.01, 1);
        return new(Math.Clamp((double)(window.X - work.X) / work.Width, 0, 1 - width),
            Math.Clamp((double)(window.Y - work.Y) / work.Height, 0, 1 - height), width, height);
    }

    public static int NextMonitor(IReadOnlyList<Box> monitors, int current, int direction)
    {
        if (monitors.Count == 0 || current < 0 || current >= monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(current));
        var ordered = Enumerable.Range(0, monitors.Count).OrderBy(i => monitors[i].X).ThenBy(i => monitors[i].Y).ToArray();
        int index = Array.IndexOf(ordered, current);
        return ordered[(index + (direction < 0 ? -1 : 1) + ordered.Length) % ordered.Length];
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
