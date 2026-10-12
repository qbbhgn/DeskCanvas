using System.Text.Json;
using DeskCanvas.Models;
using DeskCanvas.Services;

namespace DeskCanvas.UI;

internal sealed partial class SettingsForm
{
    internal void RefreshRuntimeState()
    {
        if (IsDisposed || _activePage != "显示与性能") return;
        int offset = _pageBody.Controls.Count > 0 && _pageBody.Controls[0] is Panel scroll ? -scroll.AutoScrollPosition.Y : 0;
        ShowSettings();
        if (_pageBody.Controls[0] is Panel refreshed) refreshed.AutoScrollPosition = new Point(0, offset);
    }
    private void ShowSettings()
    {
        ResetPage("显示与性能", "显示与性能", "控制壁纸运行状态，在全屏应用时自动降低资源占用。");
        var scroll = new BufferedPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = PrototypeTheme.Canvas };
        WallpaperManifest? manifest = null;
        if (_manager.CurrentFolder is { } folder)
        {
            try { manifest = WallpaperManifest.Load(folder); }
            catch (Exception ex) { StartupLog.Write("读取当前壁纸属性失败", ex); }
        }
        var sections = new List<Control> { CreateRuntimeCard(manifest), CreateRunControlCard(), CreateEnabledWallpapersCard(), CreatePlaylistCard(), CreateMonitorAssignmentsCard() };
        foreach (Control section in sections) scroll.Controls.Add(section);
        void Arrange()
        {
            int width = Math.Max(U(620), scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - U(4)), y = U(4);
            foreach (Control section in sections) { section.SetBounds(0, y + scroll.AutoScrollPosition.Y, width, section.Height); y += section.Height + U(12); }
            scroll.AutoScrollMinSize = new Size(0, y + U(10));
        }
        scroll.Resize += (_, _) => Arrange(); _pageBody.Controls.Add(scroll); Arrange();
    }
    private SurfacePanel CreateRuntimeCard(WallpaperManifest? manifest)
    {
        var card = new SurfacePanel(); var heading = TextLabel("当前壁纸属性", 26, true); var rows = new List<Control>();
        if (manifest is not null && manifest.Properties.Count > 0)
            foreach (var item in manifest.Properties) rows.Add(CreatePropertyEditor(manifest, item.Key, item.Value));
        else rows.Add(TextLabel("当前壁纸没有可调节属性", 22, color: PrototypeTheme.Muted));
        var pathRow = new Panel { BackColor = Color.White }; var pathTitle = TextLabel("当前壁纸：", 22);
        _status = TextLabel(_manager.CurrentFolder ?? "当前没有运行中的壁纸", 22); Label pathValue = _status;
        pathRow.Controls.AddRange(new Control[] { pathTitle, pathValue });
        pathRow.Resize += (_, _) => { pathTitle.SetBounds(0, 0, U(180), U(38)); pathValue.SetBounds(U(185), 0, Math.Max(0, pathRow.Width - U(185)), U(38)); };
        rows.Add(pathRow);
        var intervalRow = new Panel { BackColor = Color.White }; var intervalTitle = TextLabel("Rotation Interval", 21);
        var interval = new NumericUpDown { Minimum = 1, Maximum = 1440, Value = _manager.PlaylistIntervalMinutes, Font = Face(22), BorderStyle = BorderStyle.None, BackColor = Color.White };
        interval.ValueChanged += (_, _) => _manager.PlaylistIntervalMinutes = (int)interval.Value;
        intervalRow.Controls.AddRange(new Control[] { intervalTitle, interval });
        intervalRow.Resize += (_, _) => { intervalTitle.SetBounds(0, 0, U(205), U(38)); interval.SetBounds(U(205), U(4), U(130), U(32)); }; rows.Add(intervalRow);
        var autoPause = new PrototypeCheckBox { Text = "全屏应用运行时暂停壁纸", Checked = _manager.AutoPause, Font = Face(22), ForeColor = PrototypeTheme.Ink, BackColor = Color.White, AutoSize = false };
        autoPause.CheckedChanged += (_, _) => _manager.AutoPause = autoPause.Checked;
        var startup = new PrototypeCheckBox { Text = "开机自动启动并恢复壁纸", Checked = _manager.StartupEnabled, Font = Face(22), ForeColor = PrototypeTheme.Ink, BackColor = Color.White, AutoSize = false };
        startup.CheckedChanged += (_, _) => _manager.StartupEnabled = startup.Checked;
        var rotation = new ToggleSwitch { Text = "启用壁纸轮播", Checked = _manager.PlaylistEnabled, Font = Face(22) };
        rotation.CheckedChanged += (_, _) => _manager.PlaylistEnabled = rotation.Checked;
        var run = Button("运行设置", () => SetStatus("运行设置已保存"));
        card.Height = U(226 + rows.Count * 40); card.Controls.Add(heading);
        foreach (Control row in rows) card.Controls.Add(row);
        card.Controls.AddRange(new Control[] { autoPause, startup, rotation, run });
        void Arrange()
        {
            int inset = U(30), width = card.Width - U(64), y = U(62); heading.SetBounds(inset, U(17), width, U(40));
            foreach (Control row in rows) { row.SetBounds(inset, y, width, U(40)); y += U(40); }
            autoPause.SetBounds(inset, y + U(2), (int)(width * .59), U(42)); rotation.SetBounds(inset + (int)(width * .59), y + U(2), (int)(width * .41), U(42));
            startup.SetBounds(inset, y + U(50), width, U(42)); run.SetBounds(inset, y + U(102), width, U(48));
        }
        card.Resize += (_, _) => Arrange(); Arrange(); return card;
    }
    private Control CreatePropertyEditor(WallpaperManifest manifest, string key, WallpaperProperty property)
    {
        string? editorFolder = _manager.CurrentFolder;
        var row = new Panel { BackColor = Color.White }; var label = TextLabel(key + ":", 22);
        JsonElement value = _manager.GetPropertyValues(manifest).TryGetValue(key, out var saved) ? saved : property.Default;
        Control input;
        switch (property.Type.Trim().ToLowerInvariant())
        {
            case "number":
            {
                decimal min = ToDecimal(property.Min, -1000000), max = ToDecimal(property.Max, 1000000); if (min > max) (min, max) = (max, min);
                var number = new NumericUpDown { Minimum = min, Maximum = max, Increment = Math.Max(.01m, ToDecimal(property.Step, 1)), DecimalPlaces = 2, Value = Math.Clamp(ToDecimal(value, 0), min, max), Font = Face(22), BorderStyle = BorderStyle.None };
                number.ValueChanged += async (_, _) => await UpdatePropertyAsync(editorFolder, key, (double)number.Value); input = number; break;
            }
            case "bool": case "boolean":
            {
                var check = new ToggleSwitch { Text = "启用", Checked = value.ValueKind == JsonValueKind.True, Font = Face(22) };
                check.CheckedChanged += async (_, _) => await UpdatePropertyAsync(editorFolder, key, check.Checked); input = check; break;
            }
            case "select": case "option":
            {
                var select = new PrototypeComboBox { LogicalItemHeight = 26, Font = Face(22) }; select.Items.AddRange(property.Options.Cast<object>().ToArray());
                int index = value.ValueKind == JsonValueKind.String ? select.Items.IndexOf(value.GetString()!) : -1; select.SelectedIndex = index >= 0 ? index : select.Items.Count > 0 ? 0 : -1;
                select.SelectedIndexChanged += async (_, _) => { if (select.SelectedItem is not null) await UpdatePropertyAsync(editorFolder, key, select.SelectedItem.ToString()!); }; input = select; break;
            }
            default:
            {
                var text = new TextBox { Text = value.ValueKind == JsonValueKind.String ? value.GetString() : "", Font = Face(22), BorderStyle = BorderStyle.FixedSingle };
                text.Leave += async (_, _) => await UpdatePropertyAsync(editorFolder, key, text.Text ?? string.Empty); input = text; break;
            }
        }
        row.Controls.AddRange(new[] { label, input });
        row.Resize += (_, _) => { label.SetBounds(0, 0, U(180), U(38)); input.SetBounds(U(185), U(3), Math.Min(U(270), Math.Max(0, row.Width - U(185))), U(34)); }; return row;
    }
    private SurfacePanel CreateRunControlCard()
    {
        var card = new SurfacePanel { Height = U(130) }; var heading = TextLabel("运行控制", 26, true);
        var close = Button("关闭当前壁纸", () => { _manager.CloseWallpapers(); RefreshRuntimeState(); SetStatus("当前没有运行中的壁纸"); }, danger: true);
        card.Controls.AddRange(new Control[] { heading, close }); card.Resize += (_, _) => { heading.SetBounds(U(30), U(14), card.Width - U(60), U(38)); close.SetBounds(U(30), U(61), U(225), U(45)); }; return card;
    }
    private SurfacePanel CreateEnabledWallpapersCard()
    {
        var folders = _manager.RunningWallpaperFolders; var card = new SurfacePanel { Height = U(80 + Math.Max(1, folders.Count) * 38) }; var heading = TextLabel("已启用壁纸", 26, true);
        card.Controls.Add(heading); var rows = new List<Control>();
        foreach (string folder in folders)
        {
            string title;
            try { title = WallpaperManifest.Load(folder).Title; } catch { title = Path.GetFileName(folder); }
            var check = new PrototypeCheckBox { Text = title, Checked = true, ReadOnly = true, Font = Face(22), BackColor = Color.White };
            rows.Add(check); card.Controls.Add(check);
        }
        if (rows.Count == 0) { var label = TextLabel("当前没有运行中的壁纸", 22, color: PrototypeTheme.Muted); rows.Add(label); card.Controls.Add(label); }
        card.Resize += (_, _) => { heading.SetBounds(U(30), U(15), card.Width - U(64), U(38)); for (int i = 0; i < rows.Count; i++) rows[i].SetBounds(U(30), U(57 + i * 38), card.Width - U(64), U(38)); }; return card;
    }
    private SurfacePanel CreatePlaylistCard()
    {
        var library = _manager.Library.ToList(); int listHeight = Math.Clamp(library.Count * 47 + 4, 70, 200);
        var card = new SurfacePanel { Height = U(202 + listHeight) }; var heading = TextLabel("壁纸轮播播放列表", 28, true); var hint = TextLabel("选择要轮播的壁纸。空选择表示使用全部库。", 22);
        var selected = new HashSet<string>(_manager.PlaylistFolders, StringComparer.OrdinalIgnoreCase);
        var list = new PrototypeCheckedListBox { Font = Face(24) };
        foreach (var item in library) list.AddItem(new WallpaperChoice(item.Folder, item.Manifest.Title), selected.Count == 0 || selected.Contains(item.Folder));
        var save = Button("保存", () => RunAction(() => { _manager.SetPlaylist(list.CheckedItems.OfType<WallpaperChoice>().Select(item => item.Folder!)); SetStatus("播放列表已保存"); }));
        var rotate = Button("Rotate", color: Color.FromArgb(174, 188, 204)); rotate.ForeColor = PrototypeTheme.Ink;
        rotate.Click += async (_, _) => await RunActionAsync(async () =>
        {
            _manager.SetPlaylist(list.CheckedItems.OfType<WallpaperChoice>().Select(item => item.Folder!)); _manager.PlaylistEnabled = true;
            bool changed = await _manager.RotatePlaylistAsync(true);
            if (_activePage == "显示与性能") { RefreshRuntimeState(); SetStatus(changed ? "已切换到下一张壁纸" : "没有可用壁纸"); }
        });
        card.Controls.AddRange(new Control[] { heading, hint, list, save, rotate });
        card.Resize += (_, _) =>
        {
            int width = card.Width - U(68); heading.SetBounds(U(34), U(20), width, U(48)); hint.SetBounds(U(34), U(73), width, U(38)); list.SetBounds(U(34), U(122), width, U(listHeight));
            save.SetBounds(U(34), U(142 + listHeight), U(140), U(42)); rotate.SetBounds(U(190), U(142 + listHeight), U(140), U(42));
        }; return card;
    }
    private SurfacePanel CreateMonitorAssignmentsCard()
    {
        var library = _manager.Library.ToList(); var screens = _manager.Screens; var card = new SurfacePanel { Height = U(78 + Math.Max(1, screens.Count) * 105) };
        var heading = TextLabel("显示器壁纸", 28, true); card.Controls.Add(heading); var rows = new List<Control>();
        foreach (Screen screen in screens)
        {
            var row = new Panel { BackColor = Color.White }; var device = TextLabel(screen.DeviceName, 22);
            var choices = new List<WallpaperChoice> { new(null, "使用全局壁纸") }; choices.AddRange(library.Select(item => new WallpaperChoice(item.Folder, item.Manifest.Title)));
            var combo = new PrototypeComboBox { Font = Face(22) }; combo.Items.AddRange(choices.Cast<object>().ToArray());
            string? assigned = _manager.GetAssignedWallpaperFolder(screen); int selected = assigned is null ? 0 : choices.FindIndex(item => string.Equals(item.Folder, assigned, StringComparison.OrdinalIgnoreCase)); combo.SelectedIndex = selected >= 0 ? selected : 0;
            combo.SelectedIndexChanged += async (_, _) =>
            {
                if (combo.SelectedItem is not WallpaperChoice choice) return;
                await RunActionAsync(async () => { _manager.SetWallpaperFolder(screen, choice.Folder); await _manager.ApplyToAll(); RefreshRuntimeState(); SetStatus($"已为 {screen.DeviceName} 应用 {choice.Title}"); WarnIfCoverModeHidesIcons(); });
            };
            var reset = Button("使用全局壁纸", color: Color.FromArgb(94, 124, 151)); reset.Click += async (_, _) =>
            {
                if (combo.SelectedIndex != 0) combo.SelectedIndex = 0;
                else await RunActionAsync(async () => { _manager.ClearWallpaperFolder(screen); await _manager.ApplyToAll(); RefreshRuntimeState(); SetStatus($"{screen.DeviceName} 使用全局壁纸"); });
            };
            row.Controls.AddRange(new Control[] { device, combo, reset });
            row.Resize += (_, _) => { device.SetBounds(0, 0, (int)(row.Width * .4), U(40)); combo.SetBounds((int)(row.Width * .42), U(2), (int)(row.Width * .54), U(42)); reset.SetBounds(0, U(54), U(200), U(42)); };
            rows.Add(row); card.Controls.Add(row);
        }
        if (screens.Count == 0) { var empty = TextLabel("未检测到显示器", 22); rows.Add(empty); card.Controls.Add(empty); }
        card.Resize += (_, _) => { heading.SetBounds(U(34), U(18), card.Width - U(68), U(45)); for (int i = 0; i < rows.Count; i++) rows[i].SetBounds(U(34), U(72 + i * 105), card.Width - U(68), U(100)); }; return card;
    }
    private async Task UpdatePropertyAsync(string? editorFolder, string key, object value)
    {
        if (!string.Equals(editorFolder, _manager.CurrentFolder, StringComparison.OrdinalIgnoreCase))
        {
            RefreshRuntimeState();
            return;
        }
        try { await _manager.SetPropertyAsync(key, JsonSerializer.SerializeToElement(value)); }
        catch (Exception ex) { StartupLog.Write($"保存壁纸属性失败：{key}", ex); SetStatus(ex.Message); }
    }
    private static decimal ToDecimal(double? value, decimal fallback) => value.HasValue && double.IsFinite(value.Value) && value.Value >= (double)decimal.MinValue && value.Value <= (double)decimal.MaxValue ? (decimal)value.Value : fallback;
    private static decimal ToDecimal(JsonElement value, decimal fallback) => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number) ? number : fallback;
}
