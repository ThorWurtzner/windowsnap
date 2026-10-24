using System.Drawing.Drawing2D;
using System.Globalization;

namespace WindowSnap;

internal static class PositionDiagram
{
    private static readonly Color Accent = Color.FromArgb(58, 111, 214);
    private static readonly Color Outline = Color.FromArgb(123, 136, 154);
    private static readonly Color Screen = Color.FromArgb(235, 239, 245);

    public static void Draw(Graphics graphics, Rectangle bounds, Binding binding, int index = 0, bool showCycle = false)
    {
        if (bounds.Width < 12 || bounds.Height < 12) return;
        var state = graphics.Save();
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            var area = RectangleF.Inflate(bounds, -2, -2);
            if (showCycle && binding.Regions.Count > 1)
            {
                float size = Math.Min(13, area.Height * .45f);
                var symbol = new RectangleF(area.Right - size, area.Y + (area.Height - size) / 2, size, size);
                using var pen = new Pen(Outline, 1.2f);
                graphics.DrawArc(pen, symbol, 45, 285);
                graphics.DrawLines(pen, new PointF[] { new(symbol.Right - 1, symbol.Y - 1), new(symbol.Right, symbol.Y + 4), new(symbol.Right - 5, symbol.Y + 3) });
                area.Width -= size + 5;
            }

            switch (binding.Kind)
            {
                case ActionKind.Position when binding.Regions.Count > 0:
                    DrawRegion(graphics, area, binding.Regions[Math.Clamp(index, 0, binding.Regions.Count - 1)]);
                    break;
                case ActionKind.Maximize:
                    DrawRegion(graphics, area, new(0, 0, 1, 1));
                    break;
                case ActionKind.Center:
                    DrawRegion(graphics, area, new(.23, .2, .54, .6));
                    break;
                case ActionKind.PreviousMonitor:
                case ActionKind.NextMonitor:
                    DrawMonitors(graphics, area, binding.Kind == ActionKind.PreviousMonitor);
                    break;
                default:
                    DrawUndo(graphics, area);
                    break;
            }
        }
        finally { graphics.Restore(state); }
    }

    public static void DrawSequence(Graphics graphics, Rectangle bounds, Binding binding, Font font, bool cycleSizes = true)
    {
        int count = binding.Kind == ActionKind.Position && cycleSizes ? Math.Max(1, binding.Regions.Count) : 1;
        int captionHeight = (int)Math.Ceiling(font.GetHeight(graphics)) + 5;
        for (int index = 0; index < count; index++)
        {
            int left = bounds.Left + bounds.Width * index / count;
            int right = bounds.Left + bounds.Width * (index + 1) / count;
            var slot = new Rectangle(left, bounds.Top, right - left, bounds.Height);
            var diagram = new Rectangle(slot.X + 4, slot.Y, slot.Width - 8, Math.Max(12, slot.Height - captionHeight));
            Draw(graphics, diagram, binding, index);
            string caption = binding.Kind == ActionKind.Position && binding.Regions.Count > 0
                ? SizeCaption(binding.Regions[index]) : "";
            TextRenderer.DrawText(graphics, caption, font,
                new Rectangle(slot.X, slot.Bottom - captionHeight, slot.Width, captionHeight),
                Color.FromArgb(65, 76, 94), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private static void DrawRegion(Graphics graphics, RectangleF bounds, Region region)
    {
        var screen = FitScreen(bounds);
        using var background = new SolidBrush(Screen);
        using var border = new Pen(Outline, Math.Max(1, screen.Height / 45));
        graphics.FillRectangle(background, screen);
        var content = RectangleF.Inflate(screen, -2, -2);
        var clip = graphics.Save();
        graphics.SetClip(content, CombineMode.Intersect);
        using (var fill = new SolidBrush(region.IsValid ? Accent : Color.IndianRed))
            graphics.FillRectangle(fill, content.X + (float)region.X * content.Width, content.Y + (float)region.Y * content.Height,
                Math.Max(0, (float)region.Width * content.Width), Math.Max(0, (float)region.Height * content.Height));
        graphics.Restore(clip);
        graphics.DrawRectangle(border, screen.X, screen.Y, screen.Width, screen.Height);
        DrawStand(graphics, border, screen);
    }

    private static RectangleF FitScreen(RectangleF bounds)
    {
        float height = Math.Max(5, Math.Min(bounds.Height - 5, bounds.Width / 1.8f));
        float width = height * 1.8f;
        return new(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height - 5) / 2, width, height);
    }

    private static void DrawStand(Graphics graphics, Pen pen, RectangleF screen)
    {
        float center = screen.X + screen.Width / 2;
        graphics.DrawLine(pen, center, screen.Bottom, center, screen.Bottom + 4);
        graphics.DrawLine(pen, center - screen.Width * .16f, screen.Bottom + 4, center + screen.Width * .16f, screen.Bottom + 4);
    }

    private static void DrawMonitors(Graphics graphics, RectangleF bounds, bool previous)
    {
        float middle = bounds.X + bounds.Width / 2;
        var left = new RectangleF(bounds.X, bounds.Y, bounds.Width * .4f, bounds.Height);
        var right = new RectangleF(bounds.Right - left.Width, bounds.Y, left.Width, bounds.Height);
        DrawRegion(graphics, left, previous ? new(0, 0, 1, 1) : new(0, 0, 0, 0));
        DrawRegion(graphics, right, previous ? new(0, 0, 0, 0) : new(0, 0, 1, 1));
        float y = bounds.Y + bounds.Height * .45f;
        float length = bounds.Width * .2f;
        float end = middle + (previous ? -length : length) / 2;
        float start = middle + (previous ? length : -length) / 2;
        using var arrow = new Pen(Accent, 1.5f) { EndCap = LineCap.ArrowAnchor };
        graphics.DrawLine(arrow, start, y, end, y);
    }

    private static void DrawUndo(Graphics graphics, RectangleF bounds)
    {
        var screen = FitScreen(bounds);
        using var border = new Pen(Outline, 1);
        using var background = new SolidBrush(Screen);
        graphics.FillRectangle(background, screen);
        graphics.DrawRectangle(border, screen.X, screen.Y, screen.Width, screen.Height);
        DrawStand(graphics, border, screen);
        var arc = new RectangleF(screen.X + screen.Width * .28f, screen.Y + screen.Height * .2f, screen.Width * .45f, screen.Height * .6f);
        using var arrow = new Pen(Accent, Math.Max(1.5f, screen.Height / 18)) { EndCap = LineCap.ArrowAnchor };
        graphics.DrawArc(arrow, arc, 10, -280);
    }

    private static string SizeCaption(Region region) => Math.Abs(region.Height - 1) < .001
        ? Fraction(region.Width) : $"{Fraction(region.Width)} × {Fraction(region.Height)}";

    private static string Fraction(double value)
    {
        if (Math.Abs(value - .25) < .001) return "¼";
        if (Math.Abs(value - 1d / 3) < .001) return "⅓";
        if (Math.Abs(value - .5) < .001) return "½";
        if (Math.Abs(value - 2d / 3) < .001) return "⅔";
        if (Math.Abs(value - .75) < .001) return "¾";
        return (value * 100).ToString("0.#", CultureInfo.CurrentCulture) + "%";
    }
}
