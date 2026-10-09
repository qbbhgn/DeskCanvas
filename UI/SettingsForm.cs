using DeskCanvas.Models;
using DeskCanvas.Services;

namespace DeskCanvas.UI;

/// <summary>DeskCanvas 工作台：壁纸库、导入和运行设置统一从左侧导航进入。</summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(247, 249, 250);
    private static readonly Color Ink = Color.FromArgb(31, 43, 52);
    private static readonly Color Muted = Color.FromArgb(104, 119, 128);
    private static readonly Color Accent = Color.FromArgb(24, 139, 125);
    private readonly WallpaperManager _manager;
    private readonly Panel _page = new() { Dock = DockStyle.Fill, Padding = new Padding(34, 30, 34, 26), BackColor = Canvas };
    private Panel _pageBody = new() { Dock = DockStyle.Fill, BackColor = Canvas };
    private readonly FlowLayoutPanel _cards = new() { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 22, 10, 12), BackColor = Canvas };
    private readonly CheckBox _autoPause = new() { Text = "全屏应用运行时自动暂停壁纸", AutoSize = true, Font = new Font("Segoe UI", 10F), ForeColor = Ink };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Muted };
    private readonly List<Button> _navigation = new();

    public SettingsForm(WallpaperManager manager)
    {
        _manager = manager;
        Text = "DeskCanvas · 壁纸工作台";
        ClientSize = new Size(1080, 700);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Canvas;
        Font = new Font("Segoe UI", 10F);
        _autoPause.CheckedChanged += (_, _) => _manager.AutoPause = _autoPause.Checked;
        BuildShell();
        ShowLibrary();
    }

    private void BuildShell()
    {
        var navigation = new TableLayoutPanel { Dock = DockStyle.Left, Width = 232, BackColor = Color.FromArgb(24, 36, 47), Padding = new Padding(18, 26, 18, 22), ColumnCount = 1, RowCount = 3 };
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var brand = new Label { Text = "DESK\nCANVAS", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 21F), ForeColor = Color.FromArgb(226, 245, 240), Padding = new Padding(2, 0, 0, 0), AutoSize = false };
        var menu = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 18, 0, 0), BackColor = Color.Transparent };
        AddNavigation(menu, "壁纸库", ShowLibrary);
        AddNavigation(menu, "创建 / 导入", ShowImport);
        AddNavigation(menu, "显示与性能", ShowSettings);
        AddNavigation(menu, "关于 DeskCanvas", ShowAbout);
        var hint = new Label { Text = "你的桌面，值得更好的风景", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(139, 165, 165), TextAlign = ContentAlignment.BottomLeft, AutoSize = false };
        navigation.Controls.Add(brand, 0, 0);
        navigation.Controls.Add(menu, 0, 1);
        navigation.Controls.Add(hint, 0, 2);
        Controls.Add(_page);
        Controls.Add(navigation);
    }

    private void AddNavigation(Control parent, string text, Action action)
    {
        var button = new Button { Text = text, Width = 194, Height = 46, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0), Font = new Font("Segoe UI Semibold", 10F), ForeColor = Color.FromArgb(204, 222, 220), BackColor = Color.Transparent, UseVisualStyleBackColor = false, Margin = new Padding(0, 0, 0, 7) };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(38, 62, 70);
        button.Click += (_, _) => action();
        _navigation.Add(button);
        parent.Controls.Add(button);
    }

    private void SetActive(string title)
    {
        foreach (var item in _navigation)
        {
            bool active = item.Text == title;
            item.BackColor = active ? Accent : Color.Transparent;
            item.ForeColor = active ? Color.White : Color.FromArgb(204, 222, 220);
        }
    }

    private Panel Header(string title, string description)
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Canvas, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 0, 0, 8) };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 110));
        header.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 23F), ForeColor = Ink, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(0, 2, 0, 2) }, 0, 0);
        header.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5F), ForeColor = Muted, AutoEllipsis = true, TextAlign = ContentAlignment.TopLeft, Padding = new Padding(0, 2, 0, 0) }, 0, 1);
        return header;
    }

    private void ResetPage(string active, string title, string description)
    {
        SetActive(active);
        _page.SuspendLayout();
        _page.Controls.Clear();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Canvas, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 110));
        _pageBody = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };
        layout.Controls.Add(Header(title, description), 0, 0);
        layout.Controls.Add(_pageBody, 0, 1);
        _page.Controls.Add(layout);
        _page.ResumeLayout(true);
    }

    private void ShowLibrary()
    {
        ResetPage("壁纸库", "壁纸库", "浏览已安装的壁纸，按分类和标签选择后应用到所有显示器。");
        _cards.Controls.Clear();
        foreach (var item in _manager.Library) _cards.Controls.Add(CreateWallpaperCard(item.Folder, item.Manifest));
        if (_cards.Controls.Count == 0) _cards.Controls.Add(EmptyState("还没有壁纸", "去创建 / 导入添加你的第一款壁纸。"));
        _pageBody.Controls.Add(_cards);
    }

    private Control CreateWallpaperCard(string folder, WallpaperManifest manifest)
    {
        var card = new Panel { Width = 300, Height = 184, Margin = new Padding(0, 0, 18, 18), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(18) };
        var title = new Label { Text = manifest.Title, Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 12F), ForeColor = Ink, AutoEllipsis = true };
        var type = new Label { Text = $"{manifest.Category}  ·  {manifest.Type}", Dock = DockStyle.Top, Height = 25, ForeColor = Accent, AutoEllipsis = true };
        var tags = new Label { Text = manifest.Tags.Count == 0 ? "暂无标签" : string.Join("   ", manifest.Tags.Select(tag => $"#{tag}")), Dock = DockStyle.Top, Height = 30, ForeColor = Muted, AutoEllipsis = true };
        var apply = new Button { Text = "应用到桌面", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9F) };
        apply.FlatAppearance.BorderSize = 0;
        apply.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 111, 101);
        apply.Click += async (_, _) => await RunActionAsync(async () =>
        {
            _manager.LoadSelection(folder);
            await _manager.ApplyToAll();
            string applied = $"已应用：{manifest.Title}（{_manager.Screens.Count} 个显示器）";
            _status.Text = _manager.LastFailures.Count == 0
                ? applied
                : $"{applied}；{_manager.LastFailures.Count} 个显示器创建失败";
            if (_manager.UsesCoverWindow) _status.Text += "\n当前系统桌面没有独立背景层，壁纸以覆盖模式运行：桌面图标会被壁纸遮住，关闭壁纸即可恢复。";
            WarnIfCoverModeHidesIcons();
        });
        card.Controls.Add(apply);
        card.Controls.Add(tags);
        card.Controls.Add(type);
        card.Controls.Add(title);
        return card;
    }

    private void ShowImport()
    {
        ResetPage("创建 / 导入", "创建 / 导入", "支持网页壁纸包，以及图片和视频文件。导入的媒体会自动生成壁纸包。");
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 24, 0, 0), BackColor = Canvas };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.Controls.Add(ImportCard("网页壁纸包", "选择包含 manifest.json 的文件夹，支持 HTML、CSS、JavaScript 和本地资源。", "选择壁纸文件夹", ImportWallpaper), 0, 0);
        body.Controls.Add(ImportCard("图片 / 视频", "导入 JPG、PNG、WebP、MP4、WebM 等常见媒体，自动生成可用壁纸。", "导入图片 / 视频", ImportMediaFile), 1, 0);
        _pageBody.Controls.Add(body);
    }

    private Control ImportCard(string title, string description, string buttonText, Action action)
    {
        var card = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0), BackColor = Color.White, Padding = new Padding(24), BorderStyle = BorderStyle.FixedSingle };
        var button = new Button { Text = buttonText, Dock = DockStyle.Bottom, Height = 38, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();
        card.Controls.Add(button);
        card.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10F), ForeColor = Muted });
        card.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI Semibold", 14F), ForeColor = Ink });
        return card;
    }

    private void ShowSettings()
    {
        ResetPage("显示与性能", "显示与性能", "控制壁纸运行状态，在全屏应用时自动降低资源占用。");
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 24, 0, 0), BackColor = Canvas };
        var card = new Panel { Width = 620, Height = 190, BackColor = Color.White, Padding = new Padding(24), BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Label { Text = "运行设置", Dock = DockStyle.Top, Height = 36, Font = new Font("Segoe UI Semibold", 13F), ForeColor = Ink });
        _autoPause.Checked = _manager.AutoPause;
        _autoPause.Dock = DockStyle.Top;
        card.Controls.Add(_autoPause);
        _status.Text = _manager.CurrentFolder is null ? "当前没有运行中的壁纸" : $"当前壁纸：{_manager.CurrentFolder}";
        _status.Dock = DockStyle.Top;
        _status.Padding = new Padding(0, 16, 0, 0);
        card.Controls.Add(_status);
        var close = new Button { Text = "关闭当前壁纸", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat };
        close.Click += (_, _) => { _manager.CloseWallpapers(); _status.Text = "当前没有运行中的壁纸"; };
        card.Controls.Add(close);
        body.Controls.Add(card);
        _pageBody.Controls.Add(body);
    }

    private void ShowAbout()
    {
        ResetPage("关于 DeskCanvas", "关于 DeskCanvas", "一个专注于桌面体验的 Windows 壁纸工作台。");
        var card = new Panel { Dock = DockStyle.Top, Height = 190, BackColor = Color.White, Padding = new Padding(24), BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Label { Text = "DeskCanvas\n\n支持静态图片、视频和 WebView2 网页壁纸包。\n所有壁纸都可以使用 manifest.json 配置分类、类型和标签。", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11F), ForeColor = Ink });
        _pageBody.Controls.Add(card);
    }

    private Label EmptyState(string title, string description)
    {
        return new Label { Text = $"{title}\n{description}", AutoSize = true, Font = new Font("Segoe UI", 11F), ForeColor = Muted, Padding = new Padding(12) };
    }

    private void ImportWallpaper()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择包含 manifest.json 的壁纸文件夹" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RunAction(() => { _manager.ImportFolder(dialog.SelectedPath); ShowLibrary(); });
    }

    private void ImportMediaFile()
    {
        using var dialog = new OpenFileDialog { Filter = "图片或视频|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp;*.mp4;*.webm;*.mov;*.avi|所有文件|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RunAction(() => { _manager.ImportFile(dialog.FileName); ShowLibrary(); });
    }

    private void RunAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private bool _coverModeNoticeShown;

    /// <summary>
    /// Windows 11 25H2 起桌面不再提供独立的背景层，挂进桌面层的窗口内容不会被合成显示，
    /// 因此壁纸改以覆盖桌面的普通窗口运行；这里说明代价（桌面图标被遮住）和恢复方式。
    /// </summary>
    private void WarnIfCoverModeHidesIcons()
    {
        if (_coverModeNoticeShown || !_manager.UsesCoverWindow) return;
        _coverModeNoticeShown = true;
        MessageBox.Show(this,
            "壁纸已应用。\n\n"
            + "当前 Windows 版本的桌面没有可供壁纸挂接的独立背景层（挂进去的内容不会显示），"
            + "因此 DeskCanvas 改用覆盖模式：壁纸作为最底层全屏窗口显示。\n\n"
            + "代价是桌面图标会被壁纸遮住。在托盘菜单选择「关闭壁纸」，或点「显示与性能 → 关闭当前壁纸」即可恢复桌面图标。",
            "DeskCanvas 提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private sealed class MonitorSelectionForm : Form
    {
        private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, CheckOnClick = true };
        private readonly List<Screen> _screens = new();
        public IReadOnlyList<Screen> SelectedScreens => _list.CheckedIndices.Cast<int>().Select(index => _screens[index]).ToList();
        public MonitorSelectionForm(IReadOnlyList<Screen> screens)
        {
            Text = "选择应用显示器";
            ClientSize = new Size(390, 300);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
            foreach (Screen screen in screens)
            {
                _screens.Add(screen);
                _list.Items.Add($"{screen.DeviceName}  {screen.Bounds.Width} × {screen.Bounds.Height}", true);
            }
            var apply = new Button { Text = "应用", DialogResult = DialogResult.OK, Width = 90, Height = 32 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 90, Height = 32 };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft };
            buttons.Controls.Add(cancel); buttons.Controls.Add(apply);
            Controls.Add(_list); Controls.Add(buttons);
            AcceptButton = apply; CancelButton = cancel;
            FormClosing += (_, e) =>
            {
                if (DialogResult == DialogResult.OK && _list.CheckedItems.Count == 0)
                {
                    MessageBox.Show(this, "请至少选择一个显示器。", "DeskCanvas", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    e.Cancel = true;
                }
            };
        }
    }
}
