using DeskCanvas.Interop;

namespace DeskCanvas.UI;

internal sealed class DesktopToolWindow : Form
{
    private string _mode = "";
    private DateTime _strike;
    public DesktopToolWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = BackColor;
        Size = new Size(100, 120);
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= NativeMethods.WsExNoActivate | 0x20 | 0x80;
            return parameters;
        }
    }
    public void UpdateTool(Point point, string mode)
    {
        _mode = mode;
        if (mode.Length == 0) { Hide(); return; }
        Location = new Point(point.X - 50, point.Y - 40);
        if (!Visible) Show();
        Invalidate();
    }
    public void Animate() { _strike = DateTime.UtcNow; Invalidate(); }
    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        Graphics graphics = args.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TranslateTransform(50, 40);
        double elapsed = (DateTime.UtcNow - _strike).TotalSeconds;
        graphics.RotateTransform(elapsed < 0.28 ? (float)(-35 * Math.Sin(elapsed / 0.28 * Math.PI)) : -12);
        if (_mode == "swat")
        {
            using var handle = new Pen(Color.Coral, 6);
            using var grid = new Pen(Color.SeaGreen, 2);
            using var fill = new SolidBrush(Color.Honeydew);
            graphics.DrawLine(handle, 0, 8, 0, 58);
            graphics.FillRectangle(fill, -21, -28, 42, 36);
            graphics.DrawRectangle(grid, -21, -28, 42, 36);
            for (int offset = -14; offset < 21; offset += 7) graphics.DrawLine(grid, offset, -28, offset, 8);
            for (int offset = -21; offset < 8; offset += 7) graphics.DrawLine(grid, -21, offset, 21, offset);
        }
        else if (_mode == "sweep")
        {
            using var handle = new Pen(Color.Sienna, 6);
            using var bristle = new Pen(Color.Goldenrod, 3);
            using var band = new Pen(Color.SeaGreen, 5);
            graphics.DrawLine(handle, 0, 0, 18, -35);
            for (int offset = -24; offset <= 24; offset += 4) graphics.DrawLine(bristle, 0, -5, offset, 27);
            graphics.DrawLine(band, -8, 4, 8, 4);
        }
    }
}