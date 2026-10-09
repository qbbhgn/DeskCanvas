using System.Text.Json;
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
    private readonly CheckBox _playlistEnabled = new() { Text = "Enable wallpaper rotation", AutoSize = true, Font = new Font("Segoe UI", 10F), ForeColor = Ink };
    private readonly NumericUpDown _playlistInterval = new() { Minimum = 1, Maximum = 1440, Increment = 1, Width = 90, DecimalPlaces = 0 };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Muted };
    private readonly List<Button> _navigation = new();
    private IReadOnlyList<(string Folder, WallpaperManifest Manifest)> _libraryItems = Array.Empty<(string Folder, WallpaperManifest Manifest)>();
    private TextBox? _librarySearch;
    private ComboBox? _libraryCategory;
    private Label? _libraryCount;

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
        _playlistEnabled.CheckedChanged += (_, _) => _manager.PlaylistEnabled = _playlistEnabled.Checked;
        _playlistInterval.ValueChanged += (_, _) => _manager.PlaylistIntervalMinutes = (int)_playlistInterval.Value;
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
        _libraryItems = _manager.Library
            .OrderBy(item => item.Manifest.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Canvas,
            Margin = new Padding(0),
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        _librarySearch = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "搜索标题、作者或标签",
            Margin = new Padding(0, 0, 12, 0),
            BorderStyle = BorderStyle.FixedSingle,
        };
        _librarySearch.TextChanged += (_, _) => RefreshLibraryCards();

        _libraryCategory = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 12, 0),
        };
        _libraryCategory.Items.Add("全部分类");
        foreach (string category in _libraryItems
                     .Select(item => item.Manifest.Category)
                     .Where(category => !string.IsNullOrWhiteSpace(category))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase))
            _libraryCategory.Items.Add(category);
        _libraryCategory.SelectedIndex = 0;
        _libraryCategory.SelectedIndexChanged += (_, _) => RefreshLibraryCards();

        _libraryCount = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleRight,
            AutoEllipsis = true,
        };
        toolbar.Controls.Add(_librarySearch, 0, 0);
        toolbar.Controls.Add(_libraryCategory, 1, 0);
        toolbar.Controls.Add(_libraryCount, 2, 0);
        _pageBody.Controls.Add(_cards);
        _pageBody.Controls.Add(toolbar);
        RefreshLibraryCards();
    }

    private void RefreshLibraryCards()
    {
        if (_librarySearch is null || _libraryCategory is null) return;
        _cards.SuspendLayout();
        foreach (Control child in _cards.Controls.Cast<Control>().ToList()) child.Dispose();
        _cards.Controls.Clear();

        string query = _librarySearch.Text.Trim();
        string category = _libraryCategory.SelectedItem?.ToString() ?? string.Empty;
        bool allCategories = string.IsNullOrWhiteSpace(category) || category == "全部分类";
        var filtered = _libraryItems.Where(item =>
        {
            WallpaperManifest manifest = item.Manifest;
            bool matchesCategory = allCategories || string.Equals(manifest.Category, category, StringComparison.CurrentCultureIgnoreCase);
            if (!matchesCategory) return false;
            if (query.Length == 0) return true;
            return string.Join("\n", manifest.Title, manifest.Author, manifest.Category, manifest.Type, string.Join(' ', manifest.Tags))
                .Contains(query, StringComparison.CurrentCultureIgnoreCase);
        }).ToList();

        foreach (var item in filtered) _cards.Controls.Add(CreateWallpaperCard(item.Folder, item.Manifest));
        if (filtered.Count == 0)
        {
            string title = _libraryItems.Count == 0 ? "还没有壁纸" : "没有匹配的壁纸";
            string description = _libraryItems.Count == 0 ? "去创建 / 导入添加你的第一款壁纸。" : "尝试修改搜索关键词或分类。";
            _cards.Controls.Add(EmptyState(title, description));
        }
        if (_libraryCount is not null) _libraryCount.Text = $"{filtered.Count} / {_libraryItems.Count}";
        _cards.ResumeLayout(true);
    }

    private Control CreateWallpaperCard(string folder, WallpaperManifest manifest)
    {
        var card = new Panel { Width = 300, Height = 278, Margin = new Padding(0, 0, 18, 18), BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(10) };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.White };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        content.Controls.Add(CreatePreview(folder, manifest), 0, 0);
        content.Controls.Add(new Label { Text = manifest.Title, Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 12F), ForeColor = Ink, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        content.Controls.Add(new Label { Text = $"{manifest.Category}  ·  {manifest.Type}", Dock = DockStyle.Fill, ForeColor = Accent, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        content.Controls.Add(new Label { Text = manifest.Tags.Count == 0 ? "暂无标签" : string.Join("   ", manifest.Tags.Select(tag => $"#{tag}")), Dock = DockStyle.Fill, ForeColor = Muted, AutoEllipsis = true, TextAlign = ContentAlignment.TopLeft }, 0, 3);
        var apply = new Button { Text = "应用到桌面", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 9F), Margin = new Padding(0, 3, 0, 0) };
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
        content.Controls.Add(apply, 0, 4);
        card.Controls.Add(content);
        return card;
    }

    private Control CreatePreview(string folder, WallpaperManifest manifest)
    {
        var preview = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(27, 40, 48), Margin = new Padding(0, 0, 0, 4) };
        string? path = _manager.GetPreviewPath(folder, manifest);
        if (path is not null)
        {
            try
            {
                using var source = Image.FromFile(path);
                const int previewWidth = 278;
                const int previewHeight = 104;
                var bitmap = new Bitmap(previewWidth, previewHeight);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.FromArgb(27, 40, 48));
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    float scale = Math.Min((float)previewWidth / source.Width, (float)previewHeight / source.Height);
                    int width = Math.Max(1, (int)Math.Round(source.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(source.Height * scale));
                    var destination = new Rectangle((previewWidth - width) / 2, (previewHeight - height) / 2, width, height);
                    graphics.DrawImage(source, destination);
                }
                preview.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = bitmap, SizeMode = PictureBoxSizeMode.Zoom, BackColor = preview.BackColor });
                return preview;
            }
            catch (Exception ex) { StartupLog.Write($"无法读取壁纸预览图：{path}", ex); }
        }
        preview.Controls.Add(new Label
        {
            Text = manifest.Type.Equals("video", StringComparison.OrdinalIgnoreCase) ? "VIDEO" : "NO PREVIEW",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(181, 208, 203),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 10F),
        });
        return preview;
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
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 24, 0, 0), BackColor = Canvas, AutoScroll = true };
        WallpaperManifest? manifest = null;
        if (!string.IsNullOrWhiteSpace(_manager.CurrentFolder))
        {
            try { manifest = WallpaperManifest.Load(_manager.CurrentFolder); }
            catch (Exception ex) { StartupLog.Write("读取当前壁纸属性失败", ex); }
        }
        int propertyCount = manifest?.Properties.Count ?? 0;
        var card = new Panel { Width = 620, Height = 300 + propertyCount * 52, BackColor = Color.White, Padding = new Padding(24), BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Label { Text = "运行设置", Dock = DockStyle.Top, Height = 36, Font = new Font("Segoe UI Semibold", 13F), ForeColor = Ink });
        _autoPause.Checked = _manager.AutoPause;
        _autoPause.Dock = DockStyle.Top;
        card.Controls.Add(_autoPause);
        _playlistEnabled.Checked = _manager.PlaylistEnabled;
        _playlistEnabled.Dock = DockStyle.Top;
        card.Controls.Add(_playlistEnabled);
        _playlistInterval.Value = Math.Clamp(_manager.PlaylistIntervalMinutes, 1, 1440);
        var playlistIntervalRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.White, Padding = new Padding(0, 6, 0, 0) };
        playlistIntervalRow.Controls.Add(new Label { Text = "Rotation interval (minutes)", Width = 190, Height = 24, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft });
        playlistIntervalRow.Controls.Add(_playlistInterval);
        card.Controls.Add(playlistIntervalRow);
        _status.Text = _manager.CurrentFolder is null ? "当前没有运行中的壁纸" : $"当前壁纸：{_manager.CurrentFolder}";
        _status.Dock = DockStyle.Top;
        _status.Padding = new Padding(0, 16, 0, 0);
        card.Controls.Add(_status);
        if (manifest is not null && manifest.Properties.Count > 0)
        {
            var properties = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = manifest.Properties.Count * 42 + 8,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.White,
                Padding = new Padding(0, 10, 0, 0)
            };
            foreach (var item in manifest.Properties)
                properties.Controls.Add(CreatePropertyEditor(manifest, item.Key, item.Value));
            card.Controls.Add(properties);
            card.Controls.Add(new Label { Text = "当前壁纸属性", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 11F), ForeColor = Ink });
        }
        var close = new Button { Text = "关闭当前壁纸", Dock = DockStyle.Bottom, Height = 34, FlatStyle = FlatStyle.Flat };
        close.Click += (_, _) => { _manager.CloseWallpapers(); _status.Text = "当前没有运行中的壁纸"; };
        card.Controls.Add(close);
        card.Location = new Point(0, 0);
        body.Controls.Add(card);
        Control playlistCard = CreatePlaylistCard();
        playlistCard.Location = new Point(0, card.Bottom + 18);
        body.Controls.Add(playlistCard);
        Control monitorCard = CreateMonitorAssignmentsCard();
        monitorCard.Location = new Point(0, playlistCard.Bottom + 18);
        body.Controls.Add(monitorCard);
        body.AutoScrollMinSize = new Size(0, monitorCard.Bottom + 24);
        _pageBody.Controls.Add(body);
    }

    private Control CreatePlaylistCard()
    {
        var library = _manager.Library.ToList();
        int listHeight = Math.Clamp(72 + library.Count * 34, 100, 220);
        var card = new Panel { Width = 620, Height = listHeight + 112, BackColor = Color.White, Padding = new Padding(24), BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(new Label { Text = "Wallpaper rotation playlist", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI Semibold", 12F), ForeColor = Ink });
        var hint = new Label { Text = "Select wallpapers to rotate. An empty selection uses the full library.", Dock = DockStyle.Top, Height = 26, ForeColor = Muted, AutoEllipsis = true };
        card.Controls.Add(hint);
        var choices = library.Select(item => new PlaylistChoice(item.Folder, item.Manifest.Title)).ToList();
        var selectedFolders = new HashSet<string>(_manager.PlaylistFolders, StringComparer.OrdinalIgnoreCase);
        var list = new CheckedListBox { Dock = DockStyle.Top, Height = listHeight, CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
        foreach (PlaylistChoice choice in choices)
        {
            int index = list.Items.Add(choice);
            list.SetItemChecked(index, selectedFolders.Count == 0 || selectedFolders.Contains(choice.Folder));
        }
        if (choices.Count == 0) list.Items.Add("No wallpapers available");
        card.Controls.Add(list);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.White, Padding = new Padding(0, 6, 0, 0) };
        var save = new Button { Text = "Save playlist", Width = 120, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Accent, ForeColor = Color.White };
        save.FlatAppearance.BorderSize = 0;
        save.Click += (_, _) =>
        {
            _manager.SetPlaylist(list.CheckedItems.OfType<PlaylistChoice>().Select(item => item.Folder));
            _status.Text = $"Playlist saved ({_manager.PlaylistFolders.Count} selected)";
        };
        var rotate = new Button { Text = "Rotate now", Width = 110, Height = 30, FlatStyle = FlatStyle.Flat };
        rotate.Click += async (_, _) => await RunActionAsync(async () =>
        {
            if (!_manager.PlaylistEnabled) _manager.PlaylistEnabled = true;
            bool changed = await _manager.RotatePlaylistAsync(true);
            _status.Text = changed ? "Wallpaper rotated" : "No valid wallpapers available";
        });
        buttons.Controls.Add(save);
        buttons.Controls.Add(rotate);
        card.Controls.Add(buttons);
        return card;
    }

    private Control CreateMonitorAssignmentsCard()
    {
        var library = _manager.Library.ToList();
        var screens = _manager.Screens;
        int height = 72 + Math.Max(1, screens.Count) * 48;
        var card = new Panel { Width = 620, Height = height, BackColor = Color.White, Padding = new Padding(24), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 18, 0, 0) };
        card.Controls.Add(new Label { Text = "显示器壁纸", Dock = DockStyle.Top, Height = 30, Font = new Font("Segoe UI Semibold", 12F), ForeColor = Ink });
        var rows = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 8, 0, 0), BackColor = Color.White };
        foreach (Screen screen in screens) rows.Controls.Add(CreateMonitorAssignmentRow(screen, library));
        if (screens.Count == 0) rows.Controls.Add(new Label { Text = "未检测到显示器", AutoSize = true, ForeColor = Muted });
        card.Controls.Add(rows);
        return card;
    }

    private Control CreateMonitorAssignmentRow(Screen screen, IReadOnlyList<(string Folder, WallpaperManifest Manifest)> library)
    {
        var row = new FlowLayoutPanel { Width = 550, Height = 38, WrapContents = false, Margin = new Padding(0, 0, 0, 5), BackColor = Color.White };
        row.Controls.Add(new Label { Text = screen.DeviceName, Width = 205, Height = 30, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink });
        var choices = new List<WallpaperChoice> { new(null, "使用全局壁纸") };
        choices.AddRange(library.Select(item => new WallpaperChoice(item.Folder, item.Manifest.Title)));
        var combo = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(choices.Cast<object>().ToArray());
        string? assigned = _manager.GetAssignedWallpaperFolder(screen);
        int selected = assigned is null ? 0 : choices.FindIndex(item => string.Equals(item.Folder, assigned, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = selected >= 0 ? selected : 0;
        combo.SelectedIndexChanged += async (_, _) =>
        {
            if (combo.SelectedItem is not WallpaperChoice choice) return;
            try
            {
                _manager.SetWallpaperFolder(screen, choice.Folder);
                await _manager.ApplyToAll();
                _status.Text = $"已将 {screen.DeviceName} 的壁纸切换成 {choice.Title}";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "壁纸切换失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        row.Controls.Add(combo);
        return row;
    }

    private sealed record WallpaperChoice(string? Folder, string Title)
    {
        public override string ToString() => Title;
    }

    private sealed record PlaylistChoice(string Folder, string Title)
    {
        public override string ToString() => Title;
    }

    private Control CreatePropertyEditor(WallpaperManifest manifest, string key, WallpaperProperty property)
    {
        var row = new FlowLayoutPanel { Width = 550, Height = 36, WrapContents = false, Margin = new Padding(0, 0, 0, 6), BackColor = Color.White };
        row.Controls.Add(new Label { Text = key, Width = 170, Height = 30, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink });
        JsonElement value = _manager.GetPropertyValues(manifest).TryGetValue(key, out var saved) ? saved : property.Default;
        string type = property.Type.Trim().ToLowerInvariant();
        switch (type)
        {
            case "number":
            {
                decimal min = ToDecimal(property.Min, -1000000m);
                decimal max = ToDecimal(property.Max, 1000000m);
                if (min > max) (min, max) = (max, min);
                decimal step = Math.Max(0.01m, ToDecimal(property.Step, 1));
                var input = new NumericUpDown { Width = 150, Minimum = min, Maximum = max, Increment = step, DecimalPlaces = 2, Value = Math.Clamp(ToDecimal(value, 0), min, max) };
                input.ValueChanged += async (_, _) => await UpdatePropertyAsync(key, (double)input.Value);
                row.Controls.Add(input);
                break;
            }
            case "boolean":
            case "bool":
            {
                var input = new CheckBox { Text = "启用", Width = 150, Checked = value.ValueKind == JsonValueKind.True };
                input.CheckedChanged += async (_, _) => await UpdatePropertyAsync(key, input.Checked);
                row.Controls.Add(input);
                break;
            }
            case "select":
            case "option":
            {
                var input = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
                input.Items.AddRange(property.Options.Cast<object>().ToArray());
                string selected = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
                int index = input.Items.IndexOf(selected);
                input.SelectedIndex = index >= 0 ? index : (input.Items.Count > 0 ? 0 : -1);
                input.SelectedIndexChanged += async (_, _) => { if (input.SelectedItem is not null) await UpdatePropertyAsync(key, input.SelectedItem.ToString()!); };
                row.Controls.Add(input);
                break;
            }
            default:
            {
                var input = new TextBox { Width = 220, Text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty };
                input.Leave += async (_, _) => await UpdatePropertyAsync(key, input.Text);
                row.Controls.Add(input);
                break;
            }
        }
        return row;
    }

    private async Task UpdatePropertyAsync(string key, object value)
    {
        try { await _manager.SetPropertyAsync(key, JsonSerializer.SerializeToElement(value)); }
        catch (Exception ex) { StartupLog.Write($"保存壁纸属性失败：{key}", ex); }
    }

    private static decimal ToDecimal(double? value, decimal fallback) => value.HasValue ? (decimal)value.Value : fallback;

    private static decimal ToDecimal(JsonElement value, decimal fallback)
    {
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number) ? number : fallback;
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
