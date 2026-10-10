using System.Drawing.Drawing2D;

namespace DeskCanvas.UI;

internal static class PrototypeTheme
{
    internal static readonly Color Canvas = Color.FromArgb(248, 249, 252);
    internal static readonly Color Sidebar = Color.FromArgb(23, 38, 50);
    internal static readonly Color Ink = Color.FromArgb(18, 27, 36);
    internal static readonly Color Muted = Color.FromArgb(77, 85, 92);
    internal static readonly Color Accent = Color.FromArgb(9, 157, 149);
    internal static readonly Color Border = Color.FromArgb(205, 208, 212);
    internal static Font Font(float pixels = 24, bool bold = false) =>
        new("Microsoft YaHei UI", pixels, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    internal static GraphicsPath Round(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) { path.AddRectangle(bounds); return path; }
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void FillRound(Graphics graphics, Color color, RectangleF bounds, float radius)
    {
        using var path = Round(bounds, radius);
        using var brush = new SolidBrush(color);
        graphics.FillPath(brush, path);
    }
}

internal class BufferedPanel : Panel
{
    internal BufferedPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
}

internal sealed class SurfacePanel : BufferedPanel
{
    internal int CornerRadius { get; set; } = 10;
    internal SurfacePanel()
    {
        BackColor = PrototypeTheme.Canvas;
        Padding = new Padding(30, 26, 30, 30);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        for (int i = 5; i > 0; i--)
        {
            var shadow = new RectangleF(3 * scale, (3 + i) * scale, Width - 7 * scale, Height - 10 * scale);
            PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(4, 49, 59, 70), shadow, CornerRadius * scale);
        }
        using var shape = PrototypeTheme.Round(new RectangleF(1, 1, Width - 6 * scale, Height - 9 * scale), CornerRadius * scale);
        using var fill = new SolidBrush(Color.White);
        using var border = new Pen(PrototypeTheme.Border, scale);
        e.Graphics.FillPath(fill, shape);
        e.Graphics.DrawPath(border, shape);
    }
}

internal sealed class PrototypeButton : Button
{
    internal Color FillColor { get; set; } = PrototypeTheme.Accent;
    internal Color OutlineColor { get; set; } = Color.Transparent;
    internal bool Active { get; set; }
    internal bool Navigation { get; set; }
    private bool _hover;

    internal PrototypeButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = PrototypeTheme.Font(24);
        ForeColor = Color.White;
        Cursor = Cursors.Hand;
        BackColor = Color.White;
        UseVisualStyleBackColor = false;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        var bounds = new RectangleF(scale, scale, Width - 2 * scale, Height - 2 * scale);
        Color color = Navigation ? Active ? PrototypeTheme.Accent : _hover ? Color.FromArgb(34, 57, 69) : BackColor : FillColor;
        if (!Enabled) color = Color.FromArgb(177, 189, 198);
        else if (_hover && !Navigation && FillColor != Color.White) color = ControlPaint.Dark(color, .08f);
        using var shape = PrototypeTheme.Round(bounds, 4 * scale);
        using var fill = new SolidBrush(color);
        e.Graphics.FillPath(fill, shape);
        if (OutlineColor != Color.Transparent)
        {
            using var pen = new Pen(OutlineColor, 1.5f * scale);
            e.Graphics.DrawPath(pen, shape);
        }
        var textBounds = Navigation
            ? new Rectangle((int)(30 * scale), 0, Width - (int)(35 * scale), Height)
            : Rectangle.Inflate(ClientRectangle, -(int)(10 * scale), -2);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, Enabled ? ForeColor : Color.White,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
            (Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}

internal sealed class ToggleSwitch : CheckBox
{
    internal ToggleSwitch()
    {
        AutoSize = false;
        Size = new Size(320, 38);
        Font = PrototypeTheme.Font(24);
        ForeColor = PrototypeTheme.Ink;
        BackColor = Color.White;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        var track = new RectangleF(1, (Height - 30 * scale) / 2, 64 * scale, 30 * scale);
        PrototypeTheme.FillRound(e.Graphics, Enabled && Checked ? PrototypeTheme.Accent : Color.FromArgb(170, 180, 187), track, 15 * scale);
        float left = Checked ? track.Right - 28 * scale : track.Left + 2 * scale;
        using var white = new SolidBrush(Color.White);
        e.Graphics.FillEllipse(white, left, track.Top + 2 * scale, 26 * scale, 26 * scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(80 * scale), 0, Math.Max(0, Width - (int)(80 * scale)), Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}

internal sealed class PreviewPanel : Control
{
    internal Image? Preview { get; set; }
    internal string? Caption { get; set; }
    internal bool FillPreview { get; set; }
    internal bool Selected { get; set; }
    internal string Placeholder { get; set; } = "NO PREVIEW";
    internal PreviewPanel()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 27, FontStyle.Regular, GraphicsUnit.Pixel);
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var clip = PrototypeTheme.Round(bounds, 4 * DeviceDpi / 96f);
        e.Graphics.SetClip(clip);
        e.Graphics.Clear(Color.FromArgb(25, 39, 47));
        if (Preview is not null)
        {
            float ratio = FillPreview ? Math.Max((float)Width / Preview.Width, (float)Height / Preview.Height) : Math.Min((float)Width / Preview.Width, (float)Height / Preview.Height);
            var target = new RectangleF((Width - Preview.Width * ratio) / 2, (Height - Preview.Height * ratio) / 2, Preview.Width * ratio, Preview.Height * ratio);
            e.Graphics.DrawImage(Preview, target);
        }
        else TextRenderer.DrawText(e.Graphics, Placeholder, Font, bounds, Color.FromArgb(231, 235, 238), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (!string.IsNullOrEmpty(Caption))
        {
            int height = (int)(34 * DeviceDpi / 96f);
            var captionBounds = new Rectangle(0, Height - height, Width, height);
            using var shade = new SolidBrush(Color.FromArgb(175, 0, 0, 0));
            e.Graphics.FillRectangle(shade, captionBounds);
            TextRenderer.DrawText(e.Graphics, Caption, Font, Rectangle.Inflate(captionBounds, -8, 0), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        e.Graphics.ResetClip();
        if (Selected || (Focused && ShowFocusCues))
        {
            using var border = new Pen(Color.FromArgb(83, 174, 231), 3 * DeviceDpi / 96f);
            e.Graphics.DrawRectangle(border, Rectangle.Inflate(bounds, -2, -2));
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) Preview?.Dispose(); base.Dispose(disposing); }
}

// The prototype's monitor artwork is drawn at a fixed design size, then scaled
// as one surface so resizing never distorts the screen, stand, or small tiles.
internal sealed class ImportArtwork : Control
{
    internal bool Media { get; init; }
    internal ImportArtwork() { DoubleBuffered = true; BackColor = Color.White; SetStyle(ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(247, 250, 252), new RectangleF(1, 1, Width - 2, Height - 2), 8);
        using (var path = PrototypeTheme.Round(new RectangleF(1, 1, Width - 2, Height - 2), 8))
        using (var pen = new Pen(Color.FromArgb(225, 232, 236))) e.Graphics.DrawPath(pen, path);
        var state = e.Graphics.Save();
        float scale = Math.Min(Width / 400f, Height / 280f);
        e.Graphics.TranslateTransform((Width - 400 * scale) / 2, (Height - 280 * scale) / 2);
        e.Graphics.ScaleTransform(scale, scale);
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(210, 235, 243), new RectangleF(93, 48, 218, 140), 8);
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(158, 216, 231), new RectangleF(95, 35, 100, 54), 8);
        using (var stand = new SolidBrush(Color.FromArgb(196, 210, 219)))
        {
            e.Graphics.FillPolygon(stand, new[] { new Point(186, 183), new Point(217, 183), new Point(222, 207), new Point(181, 207) });
        }
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(208, 221, 229), new RectangleF(165, 206, 72, 6), 3);
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(23, 48, 65), new RectangleF(86, 55, 225, 134), 9);
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(229, 238, 244), new RectangleF(87, 179, 223, 18), 5);
        using (var screen = new LinearGradientBrush(new Rectangle(94, 62, 208, 114), Color.FromArgb(178, 228, 240), Color.FromArgb(105, 191, 211), 45f))
        using (var path = PrototypeTheme.Round(new RectangleF(94, 62, 208, 114), 5)) e.Graphics.FillPath(screen, path);
        if (!Media) PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(244, 253, 255), new RectangleF(155, 91, 99, 59), 5);
        using (var back = new SolidBrush(Color.FromArgb(116, 202, 218)))
            e.Graphics.FillPolygon(back, Media ? new[] { new Point(107, 154), new Point(287, 78), new Point(298, 87), new Point(298, 170) } : new[] { new Point(175, 145), new Point(223, 98), new Point(254, 144) });
        using (var mountain = new SolidBrush(Color.FromArgb(10, 164, 166)))
            e.Graphics.FillPolygon(mountain, Media ? new[] { new Point(106, 168), new Point(222, 111), new Point(244, 168) } : new[] { new Point(151, 150), new Point(185, 116), new Point(217, 150) });
        using (var mountain = new SolidBrush(Color.FromArgb(29, 179, 184)))
            e.Graphics.FillPolygon(mountain, Media ? new[] { new Point(172, 166), new Point(245, 137), new Point(290, 168) } : new[] { new Point(207, 150), new Point(234, 119), new Point(258, 150) });
        using (var line = new Pen(Color.FromArgb(224, 232, 237))) e.Graphics.DrawLine(line, 80, 234, 320, 234);
        DrawTile(e.Graphics, 37, 212, Color.FromArgb(24, 167, 174), Media ? "image" : "package");
        if (Media) DrawTile(e.Graphics, 177, 209, Color.FromArgb(54, 181, 193), "heart");
        DrawTile(e.Graphics, 317, 212, Media ? Color.FromArgb(38, 170, 175) : Color.FromArgb(233, 182, 112), Media ? "image" : "folder");
        e.Graphics.Restore(state);
    }
    private static void DrawTile(Graphics graphics, int x, int y, Color color, string kind)
    {
        PrototypeTheme.FillRound(graphics, Color.FromArgb(243, 250, 253), new RectangleF(x, y, 48, 43), 4);
        using var pen = new Pen(Color.FromArgb(162, 203, 216), 1);
        using var outline = PrototypeTheme.Round(new RectangleF(x, y, 48, 43), 4);
        graphics.DrawPath(pen, outline);
        using var brush = new SolidBrush(color);
        if (kind == "heart")
        {
            graphics.FillEllipse(brush, x + 13, y + 13, 14, 14);
            graphics.FillEllipse(brush, x + 23, y + 13, 14, 14);
            graphics.FillPolygon(brush, new[] { new Point(x + 13, y + 21), new Point(x + 37, y + 21), new Point(x + 25, y + 34) });
        }
        else if (kind == "folder")
        {
            PrototypeTheme.FillRound(graphics, color, new RectangleF(x + 9, y + 15, 31, 19), 2);
            graphics.FillRectangle(brush, x + 9, y + 10, 17, 10);
        }
        else if (kind == "package")
        {
            using var edge = new Pen(color, 2);
            graphics.DrawRectangle(edge, x + 10, y + 10, 28, 25);
            graphics.FillRectangle(brush, x + 19, y + 4, 12, 5);
            graphics.FillRectangle(brush, x + 15, y + 20, 20, 4);
        }
        else
        {
            graphics.FillPolygon(brush, new[] { new Point(x + 8, y + 33), new Point(x + 23, y + 17), new Point(x + 38, y + 33) });
            graphics.FillEllipse(brush, x + 10, y + 10, 7, 7);
        }
    }
}

internal sealed class BrandMark : Control
{
    internal BrandMark() { DoubleBuffered = true; BackColor = Color.White; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = Math.Min(Width / 100f, Height / 94f);
        e.Graphics.TranslateTransform((Width - 100 * scale) / 2, (Height - 94 * scale) / 2);
        e.Graphics.ScaleTransform(scale, scale);
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(191, 203, 211), new RectangleF(32, 57, 28, 31), 4);
        using (var fill = new LinearGradientBrush(new Rectangle(6, 3, 50, 62), Color.FromArgb(20, 98, 126), Color.FromArgb(8, 163, 170), 0f))
        using (var path = PrototypeTheme.Round(new RectangleF(6, 3, 50, 62), 4)) e.Graphics.FillPath(fill, path);
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(192, 198, 205), new RectangleF(60, 8, 39, 51), 4);
        PrototypeTheme.FillRound(e.Graphics, Color.FromArgb(12, 117, 138), new RectangleF(57, 68, 36, 22), 4);
    }
}

internal sealed class PrototypeCheckBox : CheckBox
{
    internal bool ReadOnly { get; init; }
    internal PrototypeCheckBox()
    {
        AutoSize = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnClick(EventArgs e) { if (!ReadOnly) base.OnClick(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        float scale = DeviceDpi / 96f;
        DrawCheck(e.Graphics, new RectangleF(1, (Height - 22 * scale) / 2, 22 * scale, 22 * scale), Checked, scale);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle((int)(32 * scale), 0, Math.Max(0, Width - (int)(32 * scale)), Height), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues && !ReadOnly) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
    internal static void DrawCheck(Graphics graphics, RectangleF bounds, bool check, float scale)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        PrototypeTheme.FillRound(graphics, check ? Color.FromArgb(15, 110, 166) : Color.White, bounds, 3 * scale);
        using var path = PrototypeTheme.Round(bounds, 3 * scale);
        using var border = new Pen(check ? Color.FromArgb(22, 98, 143) : Color.FromArgb(167, 177, 185), scale);
        graphics.DrawPath(border, path);
        if (!check) return;
        using var tick = new Pen(Color.White, 1.8f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLines(tick, new[] { new PointF(bounds.X + 5 * scale, bounds.Y + 11 * scale), new PointF(bounds.X + 9 * scale, bounds.Y + 15 * scale), new PointF(bounds.X + 17 * scale, bounds.Y + 6 * scale) });
    }
}

internal sealed class PrototypeCheckedListBox : ListBox
{
    private readonly HashSet<object> _checked = new();
    internal IReadOnlyList<object> CheckedItems => Items.Cast<object>().Where(_checked.Contains).ToList();
    internal PrototypeCheckedListBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        BorderStyle = BorderStyle.FixedSingle;
        BackColor = Color.White;
        ForeColor = PrototypeTheme.Ink;
        ItemHeight = 47;
    }
    internal void AddItem(object item, bool check) { Items.Add(item); if (check) _checked.Add(item); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ItemHeight = (int)(47 * DeviceDpi / 96f); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ItemHeight = (int)(47 * DeviceDpi / 96f); }
    private void Toggle(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        object item = Items[index];
        if (!_checked.Remove(item)) _checked.Add(item);
        Invalidate(GetItemRectangle(index));
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) Toggle(IndexFromPoint(e.Location)); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space) { Toggle(SelectedIndex); e.Handled = true; }
        base.OnKeyDown(e);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        using var background = new SolidBrush(Color.White);
        e.Graphics.FillRectangle(background, e.Bounds);
        float scale = DeviceDpi / 96f;
        PrototypeCheckBox.DrawCheck(e.Graphics, new RectangleF(e.Bounds.Left + 10 * scale, e.Bounds.Top + (e.Bounds.Height - 22 * scale) / 2, 22 * scale, 22 * scale), _checked.Contains(Items[e.Index]), scale);
        TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, new Rectangle(e.Bounds.Left + (int)(44 * scale), e.Bounds.Top, Math.Max(0, e.Bounds.Width - (int)(48 * scale)), e.Bounds.Height), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        using var line = new Pen(Color.FromArgb(224, 226, 230));
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}

internal sealed class PrototypeComboBox : ComboBox
{
    private int _logicalItemHeight = 36;
    internal int LogicalItemHeight
    {
        get => _logicalItemHeight;
        set { _logicalItemHeight = value; UpdateItemHeight(); }
    }
    internal PrototypeComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 36;
        IntegralHeight = false;
        BackColor = Color.White;
    }
    private void UpdateItemHeight() => ItemHeight = (int)Math.Round(_logicalItemHeight * DeviceDpi / 96f);
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateItemHeight(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); UpdateItemHeight(); }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground();
        string text = e.Index < 0 ? Text : Items[e.Index]?.ToString() ?? "";
        var rectangle = e.Bounds;
        rectangle.X += (int)(8 * DeviceDpi / 96f);
        rectangle.Width = Math.Max(0, rectangle.Width - (int)(12 * DeviceDpi / 96f));
        TextRenderer.DrawText(e.Graphics, text, Font, rectangle, (e.State & DrawItemState.Selected) != 0 ? SystemColors.HighlightText : ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}

internal sealed class SearchTextBox : TextBox
{
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg is not (0x317 or 0x318)) return;
        if (TextLength > 0 || Focused || string.IsNullOrEmpty(PlaceholderText)) return;
        if (message.WParam != IntPtr.Zero)
        {
            using var graphics = Graphics.FromHdc(message.WParam);
            TextRenderer.DrawText(graphics, PlaceholderText, Font, ClientRectangle, Color.FromArgb(94, 100, 108), TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
        }
    }
}

internal sealed class CapabilityLabel : Control
{
    internal string Kind { get; init; } = "image";
    internal CapabilityLabel() { DoubleBuffered = true; BackColor = Color.White; ForeColor = PrototypeTheme.Ink; }
    protected override void OnPaint(PaintEventArgs e)
    {
        float scale = DeviceDpi / 96f;
        Size textSize = TextRenderer.MeasureText(Text, Font);
        int icon = (int)(24 * scale), total = icon + (int)(14 * scale) + textSize.Width;
        int x = Math.Max(0, (Width - total) / 2), y = (Height - icon) / 2;
        var color = Kind == "video" ? Color.FromArgb(41, 136, 198) : Color.FromArgb(38, 185, 172);
        PrototypeTheme.FillRound(e.Graphics, color, new RectangleF(x, y, icon, icon), 3 * scale);
        using var light = new SolidBrush(Color.FromArgb(186, 238, 235));
        if (Kind == "video") e.Graphics.FillPolygon(light, new[] { new Point(x + icon / 3, y + icon / 4), new Point(x + icon / 3, y + icon * 3 / 4), new Point(x + icon * 3 / 4, y + icon / 2) });
        else if (Kind == "web") { e.Graphics.FillRectangle(light, x + icon / 5, y + icon / 4, icon / 2, icon / 7); e.Graphics.FillRectangle(light, x + icon / 5, y + icon / 4, icon / 7, icon / 2); }
        else e.Graphics.FillRectangle(light, x + icon / 6, y + icon / 2, icon * 2 / 3, icon / 4);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x + icon + (int)(14 * scale), 0, Math.Max(0, Width - x - icon - (int)(14 * scale)), Height), ForeColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}
