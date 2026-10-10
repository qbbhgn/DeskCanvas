using System.Diagnostics;
using System.Runtime.InteropServices;
using DeskCanvas.Models;
using DeskCanvas.Services;

namespace DeskCanvas.UI;

internal sealed partial class SettingsForm : Form
{
    private readonly WallpaperManager _manager;
    private readonly BufferedPanel _page = new() { Dock = DockStyle.Fill, BackColor = PrototypeTheme.Canvas };
    private Panel _pageBody = new();
    private FlowLayoutPanel? _cards;
    private readonly List<PrototypeButton> _navigation = new();
    private IReadOnlyList<(string Folder, WallpaperManifest Manifest)> _libraryItems = Array.Empty<(string, WallpaperManifest)>();
    private TextBox? _librarySearch;
    private ComboBox? _libraryCategory;
    private Label? _libraryCount;
    private BufferedPanel? _libraryDetails;
    private string? _selectedLibraryFolder;
    private static readonly Color LibraryBackground = Color.FromArgb(35, 36, 38);
    private static readonly Color LibrarySurface = Color.FromArgb(47, 48, 51);
    private Label? _status;
    private bool _coverModeNoticeShown;
    private string _activePage = "壁纸库";
    private readonly int _constructionDpi;
    private int U(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    // Unparented controls start at the system DPI; WinForms scales their fonts when attached.
    private Font Face(float pixels = 24, bool bold = false) => PrototypeTheme.Font(pixels * _constructionDpi / 96f, bold);

    public SettingsForm(WallpaperManager manager)
    {
        _manager = manager;
        using (var control = new Control()) _constructionDpi = control.DeviceDpi;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi);
        FormBorderStyle = FormBorderStyle.None;
        Text = "DeskCanvas · 壁纸工作台";
        ClientSize = new Size(U(1600), U(960));
        MinimumSize = new Size(U(1100), U(720));
        Padding = new Padding(U(6));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = PrototypeTheme.Canvas;
        Font = Face(22);
        DoubleBuffered = true;
        UpdateMaximizedBounds();
        BuildShell(); ShowLibrary();
        Shown += (_, _) =>
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(Width, area.Width - U(30)), height = Math.Min(Height, area.Height - U(30));
            MinimumSize = new Size(Math.Min(U(1100), width), Math.Min(U(720), height));
            Size = new Size(width, height);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        };
    }

    private void BuildShell()
    {
        _page.Padding = new Padding(U(40), U(36), U(30), U(24));
        var sidebar = new BufferedPanel { Dock = DockStyle.Left, Width = U(170), BackColor = LibrarySurface };
        sidebar.Controls.Add(new Label { Text = "DeskCanvas", Location = new Point(U(16), U(22)), Size = new Size(U(150), U(40)), Font = Face(23, true), ForeColor = Color.White, BackColor = sidebar.BackColor });
        AddNavigation(sidebar, "壁纸库", ShowLibrary);
        AddNavigation(sidebar, "创建 / 导入", ShowImport);
        AddNavigation(sidebar, "显示与性能", ShowSettings);
        AddNavigation(sidebar, "关于 DeskCanvas", ShowAbout);
        var content = new BufferedPanel { Dock = DockStyle.Fill, BackColor = PrototypeTheme.Canvas };
        content.Controls.Add(_page); content.Controls.Add(sidebar); Controls.Add(content);
        var titlebar = new BufferedPanel { Dock = DockStyle.Top, Height = U(48), BackColor = LibrarySurface };
        titlebar.Controls.Add(new BrandMark { Location = new Point(U(16), U(12)), Size = new Size(U(26), U(26)), BackColor = titlebar.BackColor });
        titlebar.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); } };
        titlebar.DoubleClick += (_, _) => ToggleMaximize();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = U(220), WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = titlebar.BackColor };
        foreach (var (text, action) in new (string, Action)[] { ("—", () => WindowState = FormWindowState.Minimized), ("□", ToggleMaximize), ("×", Close) })
        {
            var button = new PrototypeButton { Text = text, Width = U(72), Height = U(46), Font = Face(28), FillColor = titlebar.BackColor, BackColor = titlebar.BackColor, ForeColor = Color.White, Margin = Padding.Empty, TabStop = false };
            button.Click += (_, _) => action(); buttons.Controls.Add(button);
        }
        titlebar.Controls.Add(buttons); Controls.Add(titlebar);
    }
    private void UpdateMaximizedBounds()
    {
        Screen screen = Screen.FromControl(this);
        Rectangle area = screen.WorkingArea;
        area.Offset(-screen.Bounds.X, -screen.Bounds.Y);
        MaximizedBounds = area;
    }
    private void ToggleMaximize() { UpdateMaximizedBounds(); WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; }
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x84 || WindowState != FormWindowState.Normal || message.Result != new IntPtr(1)) return;
        long position = message.LParam.ToInt64();
        var p = PointToClient(new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff)));
        int edge = U(6); bool left = p.X < edge, right = p.X >= ClientSize.Width - edge, top = p.Y < edge, bottom = p.Y >= ClientSize.Height - edge;
        message.Result = new IntPtr(top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 1);
    }
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    private void AddNavigation(Control sidebar, string text, Action action)
    {
        var button = new PrototypeButton { Text = text, Navigation = true, Location = new Point(U(8), U(88 + _navigation.Count * 48)), Size = new Size(U(154), U(40)), BackColor = LibrarySurface, Font = Face(17), ForeColor = Color.FromArgb(237, 242, 245) };
        button.Click += (_, _) => action(); _navigation.Add(button); sidebar.Controls.Add(button);
    }
    private Label TextLabel(string text, float size = 24, bool bold = false, Color? color = null) => new()
    {
        Text = text, Font = Face(size, bold), ForeColor = color ?? PrototypeTheme.Ink, BackColor = Color.White,
        AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty
    };
    private PrototypeButton Button(string text, Action? action = null, Color? color = null, bool danger = false)
    {
        var button = new PrototypeButton { Text = text, Height = U(48), Font = Face(23), FillColor = color ?? PrototypeTheme.Accent, BackColor = Color.White };
        if (danger) { button.FillColor = Color.White; button.ForeColor = Color.FromArgb(174, 38, 38); button.OutlineColor = Color.FromArgb(192, 44, 48); }
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }
    private void ResetPage(string active, string title, string description)
    {
        _page.BackColor = PrototypeTheme.Canvas;
        _page.Padding = new Padding(U(40), U(36), U(30), U(24));
        _libraryDetails = null;
        _activePage = active;
        foreach (var item in _navigation) { item.Active = item.Text == active; item.Invalidate(); }
        _page.SuspendLayout();
        foreach (Control old in _page.Controls.Cast<Control>().ToArray()) old.Dispose();
        _page.Controls.Clear(); _cards = null; _librarySearch = null; _libraryCategory = null; _libraryCount = null; _status = null;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = PrototypeTheme.Canvas, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, U(140))); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new BufferedPanel { Dock = DockStyle.Fill, BackColor = PrototypeTheme.Canvas, Margin = Padding.Empty };
        var heading = TextLabel(title, 50); heading.BackColor = PrototypeTheme.Canvas;
        var subtitle = TextLabel(description, 22, color: PrototypeTheme.Muted); subtitle.BackColor = PrototypeTheme.Canvas; subtitle.AutoEllipsis = false; subtitle.TextAlign = ContentAlignment.TopLeft;
        header.Controls.AddRange(new Control[] { heading, subtitle });
        void ArrangeHeader()
        {
            heading.SetBounds(U(8), 0, Math.Max(0, header.Width - U(8)), U(70));
            subtitle.SetBounds(U(8), U(74), heading.Width, U(56));
        }
        header.Resize += (_, _) => ArrangeHeader(); ArrangeHeader();
        _pageBody = new BufferedPanel { Dock = DockStyle.Fill, BackColor = PrototypeTheme.Canvas, Margin = Padding.Empty };
        layout.Controls.Add(header, 0, 0); layout.Controls.Add(_pageBody, 0, 1); _page.Controls.Add(layout); _page.ResumeLayout(true);
    }
    private void ShowLibrary()
    {
        ResetPage("壁纸库", "壁纸库", "");
        foreach (Control old in _page.Controls.Cast<Control>().ToArray()) old.Dispose();
        _page.Controls.Clear();
        _page.Padding = new Padding(U(16));
        _page.BackColor = LibraryBackground;
        _libraryItems = _manager.Library.OrderBy(item => item.Manifest.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = LibraryBackground, Margin = Padding.Empty };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, U(420)));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var browser = new BufferedPanel { Dock = DockStyle.Fill, BackColor = LibraryBackground, Margin = new Padding(0, 0, U(16), 0) };
        _libraryDetails = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = LibrarySurface, Margin = Padding.Empty };
        split.Controls.Add(browser, 0, 0); split.Controls.Add(_libraryDetails, 1, 0); _page.Controls.Add(split);
        var toolbar = new BufferedPanel { Dock = DockStyle.Top, Height = U(88), Margin = Padding.Empty, BackColor = LibraryBackground };
        var heading = TextLabel("已安装", 22, true, Color.White); heading.BackColor = LibraryBackground; heading.SetBounds(0, 0, U(120), U(32));
        var frame = new Panel { BackColor = LibrarySurface, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(U(8), U(6), U(8), U(5)) };
        _librarySearch = new SearchTextBox { Dock = DockStyle.Fill, PlaceholderText = "搜索壁纸", BorderStyle = BorderStyle.None, Font = Face(16), ForeColor = Color.White, BackColor = LibrarySurface };
        frame.Controls.Add(_librarySearch);
        _libraryCategory = new PrototypeComboBox { Font = Face(16), ForeColor = PrototypeTheme.Ink };
        _libraryCategory.Items.Add("全部分类");
        foreach (string category in _libraryItems.SelectMany(item => item.Manifest.GetCategories(item.Folder)).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(value => value)) _libraryCategory.Items.Add(category);
        _libraryCategory.SelectedIndex = 0;
        _libraryCount = TextLabel("", 14, color: Color.Silver); _libraryCount.BackColor = LibraryBackground; _libraryCount.TextAlign = ContentAlignment.MiddleRight;
        toolbar.Controls.AddRange(new Control[] { heading, frame, _libraryCategory, _libraryCount });
        void ArrangeToolbar()
        {
            int searchWidth = Math.Max(U(80), toolbar.Width - U(158));
            frame.SetBounds(0, U(40), searchWidth, U(34));
            _libraryCategory.SetBounds(searchWidth + U(10), U(40), U(148), U(34));
            _libraryCount.SetBounds(Math.Max(0, toolbar.Width - U(100)), 0, U(100), U(32));
        }
        toolbar.Resize += (_, _) => ArrangeToolbar(); ArrangeToolbar();
        _cards = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, BackColor = LibraryBackground, Padding = Padding.Empty, Margin = Padding.Empty };
        _cards.Resize += (_, _) => LayoutWallpaperCards(); browser.Controls.Add(_cards); browser.Controls.Add(toolbar);
        _librarySearch.TextChanged += (_, _) => RefreshLibraryCards(); _libraryCategory.SelectedIndexChanged += (_, _) => RefreshLibraryCards(); RefreshLibraryCards();
    }
    private void RefreshLibraryCards()
    {
        if (_cards is null || _librarySearch is null || _libraryCategory is null) return;
        _cards.SuspendLayout(); foreach (Control child in _cards.Controls.Cast<Control>().ToArray()) child.Dispose(); _cards.Controls.Clear();
        string query = _librarySearch.Text.Trim(), category = _libraryCategory.SelectedItem?.ToString() ?? "全部分类";
        var filtered = _libraryItems.Where(item => (category == "全部分类" || item.Manifest.GetCategories(item.Folder).Contains(category, StringComparer.CurrentCultureIgnoreCase)) &&
            (query.Length == 0 || string.Join("\n", item.Manifest.Title, item.Manifest.Author, string.Join(' ', item.Manifest.GetCategories(item.Folder)), string.Join(' ', item.Manifest.Tags)).Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
        foreach (var item in filtered) _cards.Controls.Add(CreateWallpaperCard(item.Folder, item.Manifest));
        if (filtered.Count == 0) _cards.Controls.Add(new Label { Text = _libraryItems.Count == 0 ? "还没有壁纸" : "没有匹配的壁纸", AutoSize = true, Font = Face(17), ForeColor = Color.Silver, Padding = new Padding(U(16)) });
        if (_libraryCount is not null) _libraryCount.Text = $"{filtered.Count} / {_libraryItems.Count}";
        _cards.ResumeLayout(true); LayoutWallpaperCards();
        var selected = filtered.FirstOrDefault(item => item.Folder == _selectedLibraryFolder);
        if (selected.Manifest is null) selected = filtered.FirstOrDefault();
        ShowWallpaperDetails(selected.Folder, selected.Manifest);
    }
    private void LayoutWallpaperCards()
    {
        if (_cards is null) return;
        int available = Math.Max(U(100), _cards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        int columns = Math.Max(1, available / U(170));
        int width = Math.Min(U(200), Math.Max(U(90), available / columns - U(8))), height = width;
        var cards = _cards.Controls.OfType<PreviewPanel>().ToList();
        foreach (PreviewPanel card in cards)
        {
            card.Size = new Size(width, height);
            card.Margin = new Padding(0, 0, U(8), U(8));
        }

        int rows = (cards.Count + columns - 1) / columns;
        _cards.AutoScrollMinSize = rows == 0
            ? Size.Empty
            : new Size(0, rows * (height + U(8)));
    }
    private Control CreateWallpaperCard(string folder, WallpaperManifest manifest)
    {
        var card = new PreviewPanel { Width = U(162), Height = U(162), Font = Face(15), Caption = manifest.Title, FillPreview = true, Cursor = Cursors.Hand, Tag = folder, TabStop = true, AccessibleName = manifest.Title, AccessibleRole = AccessibleRole.PushButton };
        LoadLibraryPreview(card, folder, manifest);
        card.Click += (_, _) => ShowWallpaperDetails(folder, manifest);
        card.KeyDown += (_, args) => { if (args.KeyCode is Keys.Enter or Keys.Space) { ShowWallpaperDetails(folder, manifest); args.Handled = true; args.SuppressKeyPress = true; } };
        return card;
    }

    private void LoadLibraryPreview(PreviewPanel preview, string folder, WallpaperManifest manifest)
    {
        string? path = _manager.GetPreviewPath(folder, manifest);
        if (path is not null)
        {
            try
            {
                using var source = Image.FromFile(path);
                float ratio = Math.Min(1, 1000f / Math.Max(source.Width, source.Height));
                var thumbnail = new Bitmap(Math.Max(1, (int)(source.Width * ratio)), Math.Max(1, (int)(source.Height * ratio)));
                using var graphics = Graphics.FromImage(thumbnail); graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, thumbnail.Width, thumbnail.Height); preview.Preview = thumbnail;
            }
            catch (Exception ex) { StartupLog.Write($"无法读取预览图：{path}", ex); }
        }
    }

    private void ShowWallpaperDetails(string? folder, WallpaperManifest? manifest)
    {
        if (_libraryDetails is null) return;
        _selectedLibraryFolder = folder;
        if (_cards is not null)
            foreach (PreviewPanel card in _cards.Controls.OfType<PreviewPanel>()) { card.Selected = Equals(card.Tag, folder); card.Invalidate(); }
        _libraryDetails.SuspendLayout();
        foreach (Control old in _libraryDetails.Controls.Cast<Control>().ToArray()) old.Dispose();
        _libraryDetails.Controls.Clear();
        if (folder is null || manifest is null)
        {
            var empty = TextLabel("未选择壁纸", 20, color: Color.Silver); empty.BackColor = LibrarySurface; empty.Dock = DockStyle.Top; empty.Height = U(60);
            _libraryDetails.Controls.Add(empty); _libraryDetails.ResumeLayout(true); return;
        }
        var content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = LibrarySurface, Padding = new Padding(U(18)), Margin = Padding.Empty };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Add(Control control, int height)
        {
            int row = content.RowCount++;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, U(height)));
            control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 0, 0, U(10)); control.BackColor = LibrarySurface;
            content.Controls.Add(control, 0, row);
        }
        content.RowCount = 0;
        var preview = new PreviewPanel { Font = Face(18) }; LoadLibraryPreview(preview, folder, manifest); Add(preview, 230);
        Add(TextLabel(manifest.Title, 24, true, Color.White), 48);
        Add(TextLabel($"作者：{manifest.Author}", 15, color: Color.Silver), 36);
        var apply = Button("应用到桌面"); apply.Font = Face(17); apply.FillColor = Color.FromArgb(65, 133, 183);
        apply.Click += async (_, _) => await RunActionAsync(async () => { _manager.LoadSelection(folder); await _manager.ApplyToAll(); RefreshRuntimeState(); WarnIfCoverModeHidesIcons(); });
        Add(apply, 48);
        Add(TextLabel("壁纸详情", 18, true, Color.White), 42);
        Add(TextLabel($"类型：{manifest.Type}    分类：{string.Join(" / ", manifest.GetCategories(folder))}", 15, color: Color.Silver), 38);
        Add(TextLabel($"帧率：{manifest.RecommendedFps} FPS    版本：{manifest.Version}", 15, color: Color.Silver), 38);
        Add(TextLabel($"交互：{(manifest.Interactive ? "支持" : "无")}    音频响应：{(manifest.AudioReactive ? "支持" : "无")}", 15, color: Color.Silver), 38);
        var description = TextLabel(string.IsNullOrWhiteSpace(manifest.Description) ? "暂无介绍" : manifest.Description, 16, color: Color.Gainsboro);
        description.AutoEllipsis = false; description.TextAlign = ContentAlignment.TopLeft;
        int textHeight = TextRenderer.MeasureText(description.Text, description.Font, new Size(U(360), int.MaxValue), TextFormatFlags.WordBreak).Height;
        Add(description, Math.Max(80, (int)Math.Ceiling(textHeight * 96f / DeviceDpi) + 20));
        var tags = TextLabel(manifest.Tags.Count == 0 ? "暂无标签" : string.Join("   ", manifest.Tags.Select(tag => $"#{tag}")), 14, color: Color.Silver); Add(tags, 42);
        if (_manager.CanDelete(folder))
        {
            var delete = Button("删除壁纸", () => DeleteWallpaper(folder)); delete.Font = Face(16); delete.FillColor = Color.FromArgb(111, 55, 58); Add(delete, 46);
        }
        _libraryDetails.Controls.Add(content); _libraryDetails.ResumeLayout(true);
    }

    private void DeleteWallpaper(string folder)
    {
        string title = Path.GetFileName(folder);
        try { title = WallpaperManifest.Load(folder).Title; } catch { }
        if (MessageBox.Show(this, $"确定删除壁纸“{title}”吗？\n此操作会删除壁纸文件，且无法撤销。", "删除壁纸", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        RunAction(() => { _manager.DeleteWallpaper(folder); ShowLibrary(); });
    }
    private void ShowImport()
    {
        ResetPage("创建 / 导入", "创建 / 导入", "支持网页壁纸包，以及图片和视频文件。导入的媒体会自动生成壁纸包。");
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = PrototypeTheme.Canvas, Margin = Padding.Empty, Padding = new Padding(0, U(10), 0, 0) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.Controls.Add(ImportCard("网页壁纸包", "选择包含 manifest.json 的文件夹，支持 HTML、CSS、JavaScript 和本地资源。", "选择壁纸文件夹", ImportWallpaper, false), 0, 0);
        body.Controls.Add(ImportCard("图片 / 视频", "导入 JPG、PNG、WebP、MP4、WebM 等常见媒体，自动生成可用壁纸。", "导入图片 / 视频", ImportMediaFile, true), 1, 0); _pageBody.Controls.Add(body);
    }
    private Control ImportCard(string titleText, string description, string buttonText, Action action, bool media)
    {
        var card = new SurfacePanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, media ? 0 : U(20), 0) };
        var title = TextLabel(titleText, 32, true); var copy = TextLabel(description, 22); copy.TextAlign = ContentAlignment.TopLeft; copy.AutoEllipsis = false;
        var art = new ImportArtwork { Media = media }; var button = Button(buttonText, action); card.Controls.AddRange(new Control[] { title, copy, art, button });
        void Arrange()
        {
            int inset = U(32), width = Math.Max(1, card.Width - U(70)); title.SetBounds(inset, U(26), width, U(48));
            int copyHeight = TextRenderer.MeasureText(copy.Text, copy.Font, new Size(Math.Max(1, width - U(12)), int.MaxValue), TextFormatFlags.WordBreak).Height + U(12);
            copy.SetBounds(inset, U(76), width, copyHeight);
            button.SetBounds(inset, card.Height - U(80), width, U(48));
            int artTop = Math.Max(U(208), copy.Bottom + U(18));
            art.SetBounds(inset, artTop, width, Math.Max(U(50), button.Top - U(26) - artTop));
        }
        card.Resize += (_, _) => Arrange(); Arrange(); return card;
    }
    private void ShowAbout()
    {
        ResetPage("关于 DeskCanvas", "关于 DeskCanvas", "一个专注于桌面体验的 Windows 壁纸工作台。");
        var scroll = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = PrototypeTheme.Canvas };
        var card = new SurfacePanel { Margin = Padding.Empty, CornerRadius = 22 };
        var logo = new BrandMark(); var name = TextLabel("DeskCanvas", 44, true); name.TextAlign = ContentAlignment.MiddleCenter;
        var copy = TextLabel("支持静态图片、视频和 WebView2 网页壁纸包。\n所有壁纸都可以使用 manifest.json 配置分类、类型和标签。", 24); copy.TextAlign = ContentAlignment.MiddleCenter; copy.AutoEllipsis = false;
        var line = new Panel { Height = U(1), BackColor = Color.FromArgb(222, 225, 228) };
        var capabilities = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, BackColor = Color.White };
        foreach (var (caption, kind) in new[] { ("静态图片", "image"), ("视频壁纸", "video"), ("网页壁纸包", "web") })
        {
            capabilities.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            var label = new CapabilityLabel { Text = caption, Kind = kind, Font = Face(24), Dock = DockStyle.Fill }; capabilities.Controls.Add(label);
        }
        var docs = Button("查看文档", OpenDocumentation, Color.FromArgb(104, 131, 149)); card.Controls.AddRange(new Control[] { logo, name, copy, line, capabilities, docs });
        void Arrange()
        {
            int width = card.Width, height = card.Height; logo.SetBounds((width - U(90)) / 2, Math.Max(U(35), (int)(height * .09)), U(90), U(90));
            name.SetBounds(U(24), logo.Bottom + U(16), width - U(54), U(66));
            int copyHeight = TextRenderer.MeasureText(copy.Text, copy.Font, new Size(Math.Max(1, width - U(66)), int.MaxValue), TextFormatFlags.WordBreak).Height + U(12);
            copy.SetBounds(U(25), name.Bottom + U(20), width - U(54), copyHeight);
            line.SetBounds(U(20), Math.Max(copy.Bottom + U(36), (int)(height * .64)), width - U(46), U(1)); capabilities.SetBounds(U(32), line.Bottom + U(12), width - U(70), U(70));
            docs.SetBounds((width - U(220)) / 2, Math.Max(capabilities.Bottom + U(24), height - U(108)), U(220), U(58));
        }
        card.Resize += (_, _) => Arrange(); scroll.Controls.Add(card); _pageBody.Controls.Add(scroll);
        void ArrangePage()
        {
            card.SetBounds(0, scroll.AutoScrollPosition.Y, Math.Max(U(600), scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth), Math.Max(U(560), scroll.ClientSize.Height));
            scroll.AutoScrollMinSize = new Size(0, card.Height);
        }
        scroll.Resize += (_, _) => ArrangePage(); ArrangePage(); Arrange();
    }
    private void OpenDocumentation() => RunAction(() =>
    {
        string path = Path.Combine(AppContext.BaseDirectory, "README.md"); if (!File.Exists(path)) path = Path.Combine(Directory.GetCurrentDirectory(), "README.md");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到使用文档", path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    });
    private void SetStatus(string message) { if (_status is { IsDisposed: false }) _status.Text = message; }
    private sealed record WallpaperChoice(string? Folder, string Title) { public override string ToString() => Title; }
    private void ImportWallpaper()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择包含 manifest.json 的壁纸文件夹" };
        if (dialog.ShowDialog(this) == DialogResult.OK) RunAction(() => { _manager.ImportFolder(dialog.SelectedPath); ShowLibrary(); });
    }
    private void ImportMediaFile()
    {
        using var dialog = new OpenFileDialog { Filter = "图片或视频|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp;*.mp4;*.webm;*.mov;*.avi|所有文件|*.*" };
        if (dialog.ShowDialog(this) == DialogResult.OK) RunAction(() => { _manager.ImportFile(dialog.FileName); ShowLibrary(); });
    }
    private void RunAction(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private async Task RunActionAsync(Func<Task> action) { try { await action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    private void WarnIfCoverModeHidesIcons()
    {
        if (_coverModeNoticeShown || !_manager.UsesCoverWindow) return;
        _coverModeNoticeShown = true;
        MessageBox.Show(this, "壁纸已应用。\n\n当前 Windows 桌面没有可供壁纸挂接的独立背景层，DeskCanvas 使用覆盖模式，桌面图标会被壁纸遮住。\n\n在托盘菜单选择“关闭壁纸”，或点击“显示与性能 → 关闭当前壁纸”即可恢复桌面图标。", "DeskCanvas 提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
