namespace WindowSnap;

internal sealed class SettingsForm : Form
{
    private readonly Configuration draft;
    private readonly DataGridView grid = new();
    private readonly NumericUpDown gap = new() { Minimum = 0, Maximum = 64, Width = 65 };
    private readonly CheckBox cycle = new() { Text = "Repeat left/right within 1.5 seconds to cycle widths", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "Start WindowSnap when I sign in", AutoSize = true };
    private readonly Label status = new() { AutoSize = false, ForeColor = Color.FromArgb(146, 63, 40), Dock = DockStyle.Fill };
    private readonly Func<Configuration, bool, string?> save;
    private readonly Icon appIcon;

    public SettingsForm(Configuration config, Icon icon, string? warning, Func<Configuration, bool, string?> save)
    {
        draft = config.Clone();
        this.save = save;
        appIcon = icon;
        Text = "WindowSnap";
        Icon = icon;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(248, 249, 251);
        ForeColor = Color.FromArgb(27, 35, 48);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(750, 700);
        ClientSize = new Size(840, 830);
        AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28, 22, 28, 20), ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new(SizeType.Absolute, 77));
        layout.RowStyles.Add(new(SizeType.Absolute, 52));
        layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 46));
        layout.RowStyles.Add(new(SizeType.Absolute, 99));
        layout.RowStyles.Add(new(SizeType.Absolute, 63));
        layout.RowStyles.Add(new(SizeType.Absolute, 43));
        Controls.Add(layout);

        var heading = new Panel { Dock = DockStyle.Fill };
        heading.Controls.Add(new Label { Text = "A shortcut. A window. Exactly there.", Font = new Font("Segoe UI Semibold", 19), AutoSize = true, Location = new Point(0, 0) });
        heading.Controls.Add(new Label { Text = "WindowSnap  /  Your quiet window companion", AutoSize = true, ForeColor = Color.FromArgb(101, 111, 128), Location = new Point(1, 43) });
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(new Label { Text = "Select an action to change its shortcut. Sizes follow the active monitor.\nCentered half and three-quarters use full height; top/bottom halves suit your portrait screen.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(86, 96, 111) }, 0, 1);

        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(234, 238, 244);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = ForeColor;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        grid.ColumnHeadersHeight = 34;
        grid.RowTemplate.Height = 30;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(225, 235, 253);
        grid.DefaultCellStyle.SelectionForeColor = ForeColor;
        grid.DefaultCellStyle.Padding = new Padding(8, 0, 4, 0);
        grid.GridColor = Color.FromArgb(238, 241, 246);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ACTION", FillWeight = 58 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "SHORTCUT", FillWeight = 42 });
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };
        layout.Controls.Add(grid, 0, 2);

        var editing = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        editing.Controls.Add(Button("Change shortcut", (_, _) => EditSelected(), 139));
        editing.Controls.Add(Button("Add custom position", (_, _) => AddCustom(), 163));
        editing.Controls.Add(Button("Remove custom", (_, _) => RemoveCustom(), 132));
        editing.Controls.Add(Button("Reset defaults", (_, _) => { draft.Bindings = Configuration.Defaults().Bindings; RefreshGrid(); }, 125));
        layout.Controls.Add(editing, 0, 3);

        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        cycle.Checked = draft.CycleSizes;
        startup.Checked = Startup.Enabled;
        options.Controls.Add(cycle);
        options.Controls.Add(startup);
        var gaps = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        gaps.Controls.Add(new Label { Text = "Inset from position edges (pixels)", AutoSize = true, Margin = new Padding(0, 5, 8, 0) });
        gap.Value = draft.Gap;
        gaps.Controls.Add(gap);
        options.Controls.Add(gaps);
        layout.Controls.Add(options, 0, 4);
        status.Text = warning ?? "No polling, network requests, or telemetry. Win+Alt+arrows override Windows snapping.\nShortcuts are suspended while this window is open. Save or close to resume.";
        if (warning is null) status.ForeColor = Color.FromArgb(101, 111, 128);
        layout.Controls.Add(status, 0, 5);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var apply = Button("Save && close", (_, _) => Save(), 132);
        apply.BackColor = Color.FromArgb(36, 88, 198);
        apply.ForeColor = Color.White;
        apply.FlatStyle = FlatStyle.Flat;
        apply.FlatAppearance.BorderSize = 0;
        var cancel = Button("Cancel", (_, _) => Close(), 88);
        footer.Controls.Add(apply);
        footer.Controls.Add(cancel);
        CancelButton = cancel;
        layout.Controls.Add(footer, 0, 6);
        RefreshGrid();
    }

    private static Button Button(string text, EventHandler handler, int width)
    {
        var button = new Button { Text = text, Width = width, Height = 33, Margin = new Padding(0, 0, 8, 0), UseVisualStyleBackColor = true };
        button.Click += handler;
        return button;
    }

    private void RefreshGrid()
    {
        grid.Rows.Clear();
        foreach (var binding in draft.Bindings)
        {
            string shortcut;
            try { shortcut = string.IsNullOrWhiteSpace(binding.Shortcut) ? "Unassigned" : Shortcut.Parse(binding.Shortcut).Display; }
            catch (FormatException) { shortcut = binding.Shortcut; }
            grid.Rows.Add(binding.Name, shortcut);
        }
    }

    private Binding? Selected => grid.CurrentRow is { Index: var index } && index < draft.Bindings.Count ? draft.Bindings[index] : null;

    private void EditSelected()
    {
        if (Selected is not { } binding) return;
        using var editor = new BindingForm(binding, appIcon);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        int index = draft.Bindings.IndexOf(binding);
        draft.Bindings[index] = editor.Result;
        RefreshGrid();
        grid.CurrentCell = grid.Rows[index].Cells[0];
    }

    private void AddCustom()
    {
        if (draft.Bindings.Count >= 100) { status.Text = "You can have up to 100 actions."; return; }
        using var editor = new BindingForm(new Binding { Custom = true, Regions = [new(.25, .1, .5, .8)] }, appIcon);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        draft.Bindings.Add(editor.Result);
        RefreshGrid();
        grid.CurrentCell = grid.Rows[^1].Cells[0];
    }

    private void RemoveCustom()
    {
        if (Selected is not { Custom: true } binding) { status.Text = "Built-in actions can be unassigned using Change shortcut → Clear."; return; }
        draft.Bindings.Remove(binding);
        RefreshGrid();
    }

    private void Save()
    {
        draft.Gap = (int)gap.Value;
        draft.CycleSizes = cycle.Checked;
        string? error = save(draft, startup.Checked);
        if (error is null) { Close(); return; }
        status.Text = error;
        status.ForeColor = Color.FromArgb(146, 63, 40);
        MessageBox.Show(this, error, "Settings need attention", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

internal sealed class BindingForm : Form
{
    public Binding Result { get; }
    private readonly TextBox shortcut = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox name = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown[] values = Enumerable.Range(0, 4).Select(_ => new NumericUpDown { DecimalPlaces = 2, Maximum = 100, Dock = DockStyle.Fill }).ToArray();
    private readonly Panel preview = new() { Dock = DockStyle.Fill };
    private readonly Label error = new() { Dock = DockStyle.Fill, ForeColor = Color.Maroon };
    private ShortcutRecorder? recorder;

    public BindingForm(Binding source, Icon icon)
    {
        Result = new() { Id = source.Id, Name = source.Name, Kind = source.Kind, Shortcut = source.Shortcut, Regions = [.. source.Regions], Custom = source.Custom };
        Text = source.Custom ? "Custom position" : source.Name;
        Icon = icon;
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, source.Custom ? 450 : 195);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = source.Custom ? 10 : 4, GrowStyle = TableLayoutPanelGrowStyle.FixedSize };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 115));
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        Controls.Add(layout);
        int row = 0;
        void AddRow(string label, Control control, int height)
        {
            layout.RowStyles.Add(new(SizeType.Absolute, height));
            layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) }, 0, row);
            layout.Controls.Add(control, 1, row++);
        }
        name.Text = source.Name;
        if (source.Custom) AddRow("Name", name, 37);
        try { shortcut.Text = source.Shortcut.Length == 0 ? "Click here, then press your shortcut" : Shortcut.Parse(source.Shortcut).Display; }
        catch (FormatException) { shortcut.Text = source.Shortcut; }
        shortcut.KeyDown += CaptureShortcut;
        shortcut.Enter += (_, _) => StartRecording();
        shortcut.Leave += (_, _) => StopRecording();
        Activated += (_, _) => { if (shortcut.Focused) StartRecording(); };
        Deactivate += (_, _) => StopRecording();
        FormClosed += (_, _) => StopRecording();
        AddRow("Shortcut", shortcut, 37);
        var clear = new Button { Text = "Clear shortcut", AutoSize = true };
        clear.Click += (_, _) => { Result.Shortcut = ""; shortcut.Text = "Unassigned"; };
        AddRow("", clear, 37);
        if (source.Custom)
        {
            var region = source.Regions[0];
            double[] percentages = [region.X * 100, region.Y * 100, region.Width * 100, region.Height * 100];
            string[] labels = ["Left (%)", "Top (%)", "Width (%)", "Height (%)"];
            for (int i = 0; i < 4; i++)
            {
                values[i].Value = (decimal)percentages[i];
                values[i].ValueChanged += (_, _) => preview.Invalidate();
                AddRow(labels[i], values[i], 33);
            }
            preview.Paint += (_, e) =>
            {
                var bounds = new Rectangle(2, 5, preview.Width - 5, preview.Height - 10);
                e.Graphics.FillRectangle(Brushes.Gainsboro, bounds);
                var r = CurrentRegion;
                using var brush = new SolidBrush(r.IsValid ? Color.FromArgb(36, 88, 198) : Color.IndianRed);
                e.Graphics.FillRectangle(brush, bounds.X + (float)r.X * bounds.Width, bounds.Y + (float)r.Y * bounds.Height, (float)r.Width * bounds.Width, (float)r.Height * bounds.Height);
            };
            AddRow("Preview", preview, 90);
        }
        AddRow("", error, 36);
        var done = new Button { Text = "Done", Dock = DockStyle.Fill, Height = 32 };
        done.Click += (_, _) =>
        {
            if (source.Custom)
            {
                if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Length > 100) { error.Text = "Use a name of 1–100 characters."; return; }
                if (!CurrentRegion.IsValid) { error.Text = "The position must fit inside 100% × 100%."; return; }
                Result.Name = name.Text.Trim();
                Result.Regions = [CurrentRegion];
            }
            DialogResult = DialogResult.OK;
        };
        AddRow("", done, 33);
    }

    private Region CurrentRegion => new((double)values[0].Value / 100, (double)values[1].Value / 100, (double)values[2].Value / 100, (double)values[3].Value / 100);

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (shortcut.Focused)
        {
            CaptureShortcut(shortcut, new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void CaptureShortcut(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = e.Handled = true;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
        uint mods = (e.Control ? 2u : 0) | (e.Alt ? 1u : 0) | (e.Shift ? 4u : 0);
        if (Native.GetKeyState((int)Keys.LWin) < 0 || Native.GetKeyState((int)Keys.RWin) < 0) mods |= 8;
        SetShortcut(new Shortcut(mods, (uint)e.KeyCode));
    }

    private void SetShortcut(Shortcut key)
    {
        try { Result.Shortcut = Shortcut.Parse(key.ToString()).ToString(); shortcut.Text = key.Display; error.Text = ""; }
        catch (FormatException ex) { error.Text = ex.Message; }
    }

    private void StartRecording()
    {
        if (recorder is not null) return;
        try { recorder = new ShortcutRecorder(() => shortcut.Focused && ContainsFocus, SetShortcut); }
        catch (System.ComponentModel.Win32Exception) { error.Text = "Windows blocked the shortcut recorder."; }
    }

    private void StopRecording() { recorder?.Dispose(); recorder = null; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopRecording();
        base.Dispose(disposing);
    }
}
