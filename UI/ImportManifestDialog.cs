using DeskCanvas.Models;

namespace DeskCanvas.UI;

internal sealed class ImportManifestDialog : Form
{
    private readonly int _constructionDpi;
    private readonly string _folder;
    private readonly TextBox _title = new();
    private readonly TextBox _author = new();
    private readonly TextBox _description = new();
    private readonly TextBox _tags = new();
    private readonly ComboBox _type = new();
    private readonly ComboBox _category = new();
    private readonly ComboBox _entry = new();
    private readonly ComboBox _preview = new();
    private readonly NumericUpDown _fps = new();
    private readonly CheckBox _interactive = new();

    private int U(int value) => (int)Math.Round(value * DeviceDpi / 96f);
    private Font Face(float pixels = 16, bool bold = false) => PrototypeTheme.Font(pixels * _constructionDpi / 96f, bold);

    public WallpaperManifest Manifest { get; private set; } = new();

    public ImportManifestDialog(string folder, int constructionDpi)
    {
        _folder = Path.GetFullPath(folder);
        _constructionDpi = constructionDpi;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(DeviceDpi, DeviceDpi);
        Text = "配置壁纸";
        Icon = Program.AppIcon;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(31, 33, 36);
        ForeColor = Color.FromArgb(231, 235, 238);
        Font = Face();
        ClientSize = new Size(U(760), U(650));
        BuildContent();
    }

    private void BuildContent()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = U(92), BackColor = Color.FromArgb(42, 45, 49), Padding = new Padding(U(30), U(18), U(30), U(10)) };
        header.Controls.Add(new Label { Text = "配置新壁纸", Dock = DockStyle.Top, Height = U(38), Font = Face(25, true), ForeColor = Color.White });
        header.Controls.Add(new Label { Text = "这个文件夹没有 manifest.json，请确认壁纸的基本信息。", Dock = DockStyle.Top, Height = U(27), Font = Face(14), ForeColor = Color.FromArgb(164, 173, 180) });

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(U(30), U(22), U(30), U(10)), BackColor = BackColor, ColumnCount = 2, RowCount = 7 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int index = 0; index < 7; index++) content.RowStyles.Add(new RowStyle(SizeType.Absolute, U(index == 2 ? 82 : 62)));
        AddField(content, "壁纸名称", _title, 0, 0, Path.GetFileName(_folder));
        AddField(content, "作者", _author, 1, 0, "未知作者");
        AddField(content, "类型", _type, 0, 1);
        AddField(content, "分类", _category, 1, 1);
        AddField(content, "入口文件", _entry, 0, 2);
        AddField(content, "预览图（可选）", _preview, 1, 2);
        AddField(content, "标签（逗号分隔）", _tags, 0, 3);
        AddField(content, "目标帧率", _fps, 1, 3);
        Panel descriptionGroup = AddField(content, "描述", _description, 0, 4, "");
        content.SetColumnSpan(descriptionGroup, 2);
        _description.Multiline = true;
        _description.Height = U(48);
        _description.ScrollBars = ScrollBars.Vertical;
        _interactive.Text = "允许交互";
        _interactive.Checked = true;
        _interactive.AutoSize = true;
        _interactive.Font = Face(15);
        _interactive.ForeColor = ForeColor;
        _interactive.BackColor = BackColor;
        content.Controls.Add(_interactive, 0, 5);

        var note = new Label { Text = "入口文件和预览图均来自当前文件夹，导入后会自动生成 manifest.json。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(145, 154, 161), Font = Face(13), TextAlign = ContentAlignment.MiddleLeft };
        content.Controls.Add(note, 0, 6); content.SetColumnSpan(note, 2);

        PopulateFiles();
        var cancel = new PrototypeButton { Text = "取消", Width = U(120), Height = U(44), FillColor = Color.FromArgb(67, 71, 76), BackColor = BackColor, Font = Face(16) };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        var confirm = new PrototypeButton { Text = "导入壁纸", Width = U(150), Height = U(44), FillColor = PrototypeTheme.Accent, BackColor = BackColor, Font = Face(16) };
        confirm.Click += (_, _) => Confirm();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = U(70), FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(U(30), U(10), U(30), U(14)), BackColor = Color.FromArgb(42, 45, 49) };
        actions.Controls.Add(cancel); actions.Controls.Add(confirm);
        Controls.Add(content); Controls.Add(actions); Controls.Add(header);
        AcceptButton = confirm; CancelButton = cancel;
    }

    private Panel AddField(TableLayoutPanel layout, string caption, Control control, int column, int row, string? value = null)
    {
        var group = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, U(12), U(8)), BackColor = BackColor };
        var label = new Label { Text = caption, Dock = DockStyle.Top, Height = U(24), Font = Face(13, true), ForeColor = Color.FromArgb(177, 185, 191) };
        control.Dock = DockStyle.Fill;
        control.Font = Face(15);
        control.BackColor = Color.FromArgb(52, 55, 60);
        control.ForeColor = Color.White;
        if (value is not null && control is TextBox textBox) textBox.Text = value;
        group.Controls.Add(control); group.Controls.Add(label); layout.Controls.Add(group, column, row); return group;
    }

    private void PopulateFiles()
    {
        var files = Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_folder, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string file in files) _entry.Items.Add(file);
        string? defaultEntry = files.FirstOrDefault(path => Path.GetExtension(path).Equals(".html", StringComparison.OrdinalIgnoreCase)) ?? files.FirstOrDefault();
        if (defaultEntry is not null) _entry.SelectedItem = defaultEntry;
        _entry.DropDownStyle = ComboBoxStyle.DropDownList;
        _preview.Items.Add("（无预览图）");
        foreach (string file in files.Where(IsPreviewFile)) _preview.Items.Add(file);
        _preview.SelectedIndex = 0;
        _type.Items.AddRange(new object[] { "网页壁纸", "视频壁纸", "图片壁纸" });
        _type.SelectedIndex = 0;
        _category.Items.AddRange(new object[] { "特效壁纸", "动态壁纸", "静态壁纸", "自然", "游戏", "抽象" });
        _category.SelectedIndex = 0;
        _fps.Minimum = 1; _fps.Maximum = 240; _fps.Value = 60; _fps.Increment = 5;
    }

    private static bool IsPreviewFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp";

    private void Confirm()
    {
        if (_entry.SelectedItem is not string entry || string.IsNullOrWhiteSpace(entry))
        {
            MessageBox.Show(this, "请选择壁纸入口文件。", "配置不完整", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
        }
        string entryPath = Path.GetFullPath(Path.Combine(_folder, entry.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(entryPath))
        {
            MessageBox.Show(this, "入口文件不存在，请重新选择。", "配置无效", MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
        }
        string type = _type.SelectedIndex switch { 1 => "video", 2 => "image", _ => "web" };
        Manifest = new WallpaperManifest
        {
            Id = Guid.NewGuid().ToString("N"), Title = _title.Text.Trim(), Author = _author.Text.Trim(), Description = _description.Text.Trim(),
            Type = type, Category = _category.Text, Entry = entry, Preview = _preview.SelectedIndex > 0 ? _preview.SelectedItem?.ToString() : null,
            Interactive = type == "web" && _interactive.Checked, RecommendedFps = (int)_fps.Value,
            Tags = _tags.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
        if (string.IsNullOrWhiteSpace(Manifest.Title)) Manifest.Title = Path.GetFileName(_folder);
        if (string.IsNullOrWhiteSpace(Manifest.Author)) Manifest.Author = "未知作者";
        DialogResult = DialogResult.OK;
    }
}