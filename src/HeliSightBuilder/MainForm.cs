using System.Text;
using System.Text.Json;

namespace HeliSightBuilder.Native;

public sealed class MainForm : Form
{
    private readonly ComboBox shape = Box(["Dot", "Circle", "Cross", "Box", "T Sight", "Custom"]);
    private readonly ComboBox tool = Box(["Select", "Pan", "Line", "Circle", "Box", "Dot"]);
    private readonly ComboBox part = Box(["Crosshair", "Brackets", "Chevron", "Pipper", "Rocket Ladder", "Side Posts"]);
    private readonly ComboBox color = Box(["White", "Green", "Amber", "Red", "Cyan"]);
    private readonly NumericUpDown size = Number(4.2m, .1m, 1000000);
    private readonly NumericUpDown gap = Number(1, .1m, 1000000);
    private readonly NumericUpDown scale = Number(100, 1, 1000000);
    private readonly NumericUpDown zoom = Number(100, 10, 1000);
    private readonly NumericUpDown grid = Number(1, .1m, 100);
    private readonly NumericUpDown nudge = Number(.5m, .1m, 100);
    private readonly NumericUpDown originX = Number(0, .1m, 1000000, -1000000);
    private readonly NumericUpDown originY = Number(0, .1m, 1000000, -1000000);
    private readonly Label valueALabel = new() { Text = "X", AutoSize = true };
    private readonly Label valueBLabel = new() { Text = "Y", AutoSize = true };
    private readonly Label valueCLabel = new() { Text = "R X", AutoSize = true };
    private readonly Label valueDLabel = new() { Text = "R Y", AutoSize = true };
    private readonly NumericUpDown valueA = Number(0, .1m, 1000000, -1000000);
    private readonly NumericUpDown valueB = Number(0, .1m, 1000000, -1000000);
    private readonly NumericUpDown valueC = Number(0, .1m, 1000000, -1000000);
    private readonly NumericUpDown valueD = Number(0, .1m, 1000000, -1000000);
    private readonly CheckBox snap = new() { Text = "Snap to grid", Checked = true, AutoSize = true };
    private readonly CheckBox pickOrigin = new() { Text = "Pick CCIP origin on canvas", AutoSize = true };
    private readonly DoubleBufferedPanel canvas = new() { BackColor = Color.FromArgb(93, 143, 186), Dock = DockStyle.Fill };
    private readonly RichTextBox commands = new() { Dock = DockStyle.Fill, Font = new Font("Consolas", 10), WordWrap = false };
    private readonly TextBox output = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label diagnostics = new() { AutoSize = true };
    private readonly Label cursorPosition = new() { Text = "X 0.00  Y 0.00", AutoSize = true };
    private readonly FlowLayoutPanel controlPanel = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        AutoScroll = true, Padding = new Padding(12)
    };
    private readonly List<SightItem> items = [new(SightItemKind.Ellipse, 0, 0, 2.1, 2.1)];
    private readonly Stack<AppState> undo = new();
    private readonly Stack<AppState> redo = new();
    private readonly string autosavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeliSightBuilder", "autosave-native.json");

    private int selected = 0;
    private bool loading;
    private PointF? dragStart;
    private PointF? dragCurrent;
    private PointF pan = new(0, 0);
    private bool panning;
    private Point panStart;
    private PointF panAtStart;

    public MainForm()
    {
        Text = "War Thunder Helicopter Sight Builder";
        MinimumSize = new Size(980, 640);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "HeliSightOutput");
        tool.Width = 150;
        zoom.Width = 82;
        shape.SelectedItem = "Dot"; tool.SelectedItem = "Select"; part.SelectedItem = "Crosshair"; color.SelectedItem = "White";

        Controls.Add(BuildLayout());
        WireEvents();
        LoadAutosave();
        Record();
        Sync();
        Shown += (_, _) => BeginInvoke(() => controlPanel.AutoScrollPosition = Point.Empty);
    }

    public IReadOnlyList<string> RunInteractionQualityChecks()
    {
        var results = new List<string>();
        var autosaveBackup = File.Exists(autosavePath) ? File.ReadAllBytes(autosavePath) : null;
        void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"UI quality check failed: {name}");
            results.Add($"PASS: {name}");
        }

        CreateControl();
        Show();
        Application.DoEvents();
        Size = MinimumSize;
        PerformLayout();
        Application.DoEvents();
        bool InsideClient(Control control)
        {
            var bounds = RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            return ClientRectangle.IntersectsWith(bounds) &&
                bounds.Left >= 0 && bounds.Top >= 0 &&
                bounds.Right <= ClientRectangle.Right && bounds.Bottom <= ClientRectangle.Bottom;
        }
        Require(InsideClient(canvas), "minimum-size canvas visibility");
        Require(InsideClient(tool) && InsideClient(zoom), "minimum-size canvas toolbar visibility");
        Require(InsideClient(output) && InsideClient(status), "minimum-size build row visibility");
        var largeCircle = new SightItem(SightItemKind.Ellipse, 0, 0, 100, 100);
        Require(!HitTest(largeCircle, PointF.Empty, 1), "large circle interior is not selected");
        Require(HitTest(largeCircle, new PointF(100, 0), 1), "large circle stroke is selected");
        Hide();
        canvas.Size = new Size(900, 500);
        loading = true;
        items.Clear();
        selected = -1;
        shape.SelectedItem = "Custom";
        scale.Value = 100;
        zoom.Value = 100;
        snap.Checked = false;
        pan = PointF.Empty;
        loading = false;
        Sync();

        tool.SelectedItem = "Line";
        Require(shape.Text == "Custom", "custom mode activation");
        Require(tool.Text == "Line", "line tool activation");
        Require(!pickOrigin.Checked, "origin picker inactive");
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, 100, 100, 0));
        CanvasMove(canvas, new MouseEventArgs(MouseButtons.Left, 0, 800, 400, 0));
        Require(dragStart is not null && dragCurrent is not null, "live drag preview state");
        CanvasUp(canvas, new MouseEventArgs(MouseButtons.Left, 1, 800, 400, 0));
        Require(items.Count == 1 && items[0].Kind == SightItemKind.Line, "long line drawing");
        Require(Math.Abs(items[0].X2 - items[0].X1) > 100, "drawing has no artificial size cap");

        tool.SelectedItem = "Pan";
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, 200, 200, 0));
        CanvasMove(canvas, new MouseEventArgs(MouseButtons.Left, 0, 350, 300, 0));
        CanvasUp(canvas, new MouseEventArgs(MouseButtons.Left, 1, 350, 300, 0));
        Require(Math.Abs(pan.X - 150) < .01 && Math.Abs(pan.Y - 100) < .01, "left-drag panning");

        tool.SelectedItem = "Line";
        var selectPanStart = pan;
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Right, 1, 300, 200, 0));
        CanvasMove(canvas, new MouseEventArgs(MouseButtons.Right, 0, 420, 270, 0));
        CanvasUp(canvas, new MouseEventArgs(MouseButtons.Right, 1, 420, 270, 0));
        Require(Math.Abs(pan.X - selectPanStart.X - 120) < .01 && Math.Abs(pan.Y - selectPanStart.Y - 70) < .01,
            "right-drag panning from drawing tool");

        var anchor = new Point(600, 250);
        var beforeZoom = ToWorld(anchor);
        ChangeZoom(1.25m, anchor);
        var afterZoom = ToWorld(anchor);
        Require(Math.Abs(beforeZoom.X - afterZoom.X) < .01 && Math.Abs(beforeZoom.Y - afterZoom.Y) < .01, "cursor-anchored zoom");

        tool.SelectedItem = "Select";
        var linePoint = ToScreen(items[0].X1, items[0].Y1);
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, (int)linePoint.X, (int)linePoint.Y, 0));
        CanvasUp(canvas, new MouseEventArgs(MouseButtons.Left, 1, (int)linePoint.X, (int)linePoint.Y, 0));
        Require(selected == 0, "shape selection");
        var oldX = items[0].X1;
        nudge.Value = .25m;
        Button? FindButton(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                if (child is Button button && button.Text == text) return button;
                var nested = FindButton(child, text);
                if (nested is not null) return nested;
            }
            return null;
        }
        var rightButton = FindButton(controlPanel, "Right");
        Require(rightButton is not null, "direction button discovery");
        Show();
        Application.DoEvents();
        rightButton!.PerformClick();
        Require(Math.Abs(items[0].X1 - oldX - .25) < .0001, "precise nudge");
        FindButton(controlPanel, "Left")!.PerformClick();
        Require(Math.Abs(items[0].X1 - oldX) < .0001, "left nudge button");
        var oldY = items[0].Y1;
        FindButton(controlPanel, "Up")!.PerformClick();
        Require(Math.Abs(items[0].Y1 - oldY + .25) < .0001, "up nudge button");
        FindButton(controlPanel, "Down")!.PerformClick();
        Require(Math.Abs(items[0].Y1 - oldY) < .0001, "down nudge button");
        Hide();
        UseSelectedOrigin();
        Require(Math.Abs((double)originX.Value - items[0].Center.X) < .01, "selected CCIP origin");

        snap.Checked = true;
        grid.Value = 1;
        pickOrigin.Checked = true;
        tool.SelectedItem = "Line";
        var preciseOrigin = ToScreen(2.15, -2.15);
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, (int)preciseOrigin.X, (int)preciseOrigin.Y, 0));
        Require(Math.Abs((double)originX.Value - 2.15) < .1 && Math.Abs((double)originY.Value + 2.15) < .1,
            "unsnapped decimal CCIP origin");

        Show();
        originX.Focus();
        originX.Text = "-2.15";
        Require(originX.Text.Contains("-2.15", StringComparison.Ordinal), "partial numeric text remains editable");
        canvas.Focus();
        Application.DoEvents();
        Require(Math.Abs((double)originX.Value + 2.15) < .001, "decimal numeric commit");
        Hide();

        var countBeforeUndo = items.Count;
        tool.SelectedItem = "Dot";
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, 450, 250, 0));
        Require(items.Count == countBeforeUndo + 1, "dot drawing");
        Undo();
        Require(items.Count == countBeforeUndo, "undo");
        Redo();
        Require(items.Count == countBeforeUndo + 1, "redo");

        scale.Value = 10000;
        tool.SelectedItem = "Line";
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
        CanvasMove(canvas, new MouseEventArgs(MouseButtons.Left, 0, 890, 490, 0));
        CanvasUp(canvas, new MouseEventArgs(MouseButtons.Left, 1, 890, 490, 0));
        Require(items.Count == countBeforeUndo + 2, "extreme-scale drawing");
        Require(SightLogic.CommandRegex().Matches(commands.Text).Count == items.Count, "command synchronization");

        SaveAutosave();
        Require(File.Exists(autosavePath) && new FileInfo(autosavePath).Length > 0, "autosave write");
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(autosavePath));
        Require(state is not null && state.Items.Count == items.Count, "autosave parse");
        if (autosaveBackup is null) File.Delete(autosavePath);
        else File.WriteAllBytes(autosavePath, autosaveBackup);
        return results;
    }

    private Control BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        var controls = controlPanel;
        controls.Controls.Add(Section("Sight"));
        AddField(controls, "Shape", shape); AddField(controls, "Size", size); AddField(controls, "Center gap", gap);
        AddField(controls, "Custom scale %", scale);
        controls.Controls.Add(Section("Custom design"));
        AddField(controls, "Add sight part", part);
        controls.Controls.Add(Button("Add Part", (_, _) => { items.AddRange(SightLogic.Part(part.Text)); selected = items.Count - 1; shape.SelectedItem = "Custom"; Changed(); }));
        controls.Controls.Add(Button("Import SVG Line Art", ImportSvg));
        controls.Controls.Add(Row(Button("Undo", (_, _) => Undo()), Button("Redo", (_, _) => Redo())));
        AddField(controls, "Sight color", color);
        controls.Controls.Add(snap);
        controls.Controls.Add(Row(new Label { Text = "Grid", AutoSize = true }, grid, new Label { Text = "Nudge", AutoSize = true }, nudge));
        controls.Controls.Add(Section("Selected shape"));
        controls.Controls.Add(Row(valueALabel, valueA, valueBLabel, valueB));
        controls.Controls.Add(Row(valueCLabel, valueC, valueDLabel, valueD));
        controls.Controls.Add(Button("Apply Values", (_, _) => ApplyValues()));
        controls.Controls.Add(NudgePad());
        controls.Controls.Add(Button("Delete Selected", (_, _) => { if (selected >= 0 && selected < items.Count) { items.RemoveAt(selected); selected = Math.Min(selected, items.Count - 1); Changed(); } }));
        controls.Controls.Add(Button("Clear Custom Sight", (_, _) => { items.Clear(); selected = -1; shape.SelectedItem = "Custom"; Changed(); }));
        controls.Controls.Add(Section("CCIP origin"));
        controls.Controls.Add(Row(new Label { Text = "X", AutoSize = true }, originX, new Label { Text = "Y", AutoSize = true }, originY));
        controls.Controls.Add(pickOrigin);
        controls.Controls.Add(Row(
            Button("Use Selected Center", (_, _) => UseSelectedOrigin()),
            Button("Reset Origin", (_, _) => { originX.Value = 0; originY.Value = 0; Changed(); })));
        controls.Controls.Add(Button("Center Preview", (_, _) => { pan = PointF.Empty; canvas.Invalidate(); }));
        controls.Controls.Add(diagnostics);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(8) };
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        main.Controls.Add(canvas, 0, 0);
        var zoomRow = Row(new Label { Text = "Tool", AutoSize = true }, tool,
            new Label { Text = "Zoom", AutoSize = true }, SmallButton("-", (_, _) => ChangeZoom(.8m)),
            zoom, SmallButton("+", (_, _) => ChangeZoom(1.25m)), cursorPosition);
        zoomRow.Dock = DockStyle.Fill; main.Controls.Add(zoomRow, 0, 1);
        var technical = new GroupBox { Text = "Generated vector commands", Dock = DockStyle.Fill, Padding = new Padding(8) };
        technical.Controls.Add(commands);
        main.Controls.Add(technical, 0, 2);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, Padding = new Padding(12, 8, 12, 8) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        bottom.Controls.Add(new Label { Text = "Output folder", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        bottom.Controls.Add(output, 1, 0);
        bottom.Controls.Add(Button("Choose Folder", ChooseOutput), 2, 0);
        bottom.Controls.Add(Button("Build install files", BuildPackage), 3, 0);
        bottom.Controls.Add(status, 4, 0);
        root.Controls.Add(controls, 0, 0); root.Controls.Add(main, 1, 0); root.Controls.Add(bottom, 0, 1);
        root.SetColumnSpan(bottom, 2);
        return root;
    }

    private void WireEvents()
    {
        shape.SelectedValueChanged += (_, _) => Sync();
        foreach (var numeric in new[] { size, gap, scale, originX, originY })
        {
            numeric.Validated += (_, _) => { if (!loading) Changed(); };
            if (numeric is CommitNumericUpDown commit)
                commit.SpinCommitted += (_, _) => { if (!loading) Changed(); };
            numeric.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter || loading) return;
                Changed();
                canvas.Focus();
                e.SuppressKeyPress = true;
            };
        }
        color.SelectedValueChanged += (_, _) => { if (!loading) Changed(); };
        zoom.ValueChanged += (_, _) => canvas.Invalidate();
        tool.SelectedValueChanged += (_, _) => UpdateCanvasCursor();
        commands.TextChanged += (_, _) => { if (!loading) { UpdateDiagnostics(); canvas.Invalidate(); } };
        canvas.Paint += PaintCanvas;
        canvas.MouseDown += CanvasDown; canvas.MouseMove += CanvasMove; canvas.MouseUp += CanvasUp;
        canvas.MouseWheel += (_, e) => ChangeZoom(e.Delta > 0 ? 1.2m : .8m, e.Location);
        FormClosing += (_, _) => SaveAutosave();
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) Undo();
            if (e.Control && e.KeyCode == Keys.Y) Redo();
            if (e.KeyCode == Keys.Escape) { dragStart = null; dragCurrent = null; panning = false; canvas.Capture = false; UpdateCanvasCursor(); canvas.Invalidate(); }
        };
        UpdateCanvasCursor();
    }

    private void Sync()
    {
        try
        {
            loading = true;
            commands.Text = shape.Text == "Custom"
                ? SightLogic.Commands(items, (double)scale.Value / 100)
                : SightLogic.Commands(SightLogic.Preset(shape.Text, (double)size.Value, (double)gap.Value));
        }
        catch { commands.Clear(); }
        finally { loading = false; }
        SyncValueFields(); UpdateDiagnostics(); canvas.Invalidate(); SaveAutosave();
    }

    private void Changed() { if (loading) return; Record(); redo.Clear(); Sync(); }
    private void Record()
    {
        var state = CaptureState();
        if (undo.TryPeek(out var previous) && JsonSerializer.Serialize(previous) == JsonSerializer.Serialize(state)) return;
        undo.Push(state);
        while (undo.Count > 80) undo.RemoveBottom();
    }
    private AppState CaptureState() => new(shape.Text, (double)size.Value, (double)gap.Value, (double)scale.Value, color.Text,
        (double)originX.Value, (double)originY.Value, [.. items], selected, output.Text, tool.Text, part.Text,
        (double)zoom.Value, snap.Checked, (double)grid.Value, (double)nudge.Value);
    private void Restore(AppState state)
    {
        loading = true;
        shape.SelectedItem = state.Shape; size.Value = Clamp(size, state.Size); gap.Value = Clamp(gap, state.Gap);
        scale.Value = Clamp(scale, state.Scale); color.SelectedItem = state.Color; originX.Value = Clamp(originX, state.OriginX);
        originY.Value = Clamp(originY, state.OriginY); items.Clear(); items.AddRange(state.Items); selected = state.Selected; output.Text = state.Output;
        tool.SelectedItem = string.IsNullOrWhiteSpace(state.Tool) ? "Select" : state.Tool;
        part.SelectedItem = string.IsNullOrWhiteSpace(state.Part) ? "Crosshair" : state.Part;
        zoom.Value = Clamp(zoom, state.Zoom <= 0 ? 100 : state.Zoom);
        snap.Checked = state.Snap;
        grid.Value = Clamp(grid, state.Grid <= 0 ? 1 : state.Grid);
        nudge.Value = Clamp(nudge, state.Nudge <= 0 ? .5 : state.Nudge);
        loading = false; Sync();
    }
    private void Undo() { if (undo.Count < 2) return; redo.Push(undo.Pop()); Restore(undo.Peek()); }
    private void Redo() { if (redo.Count == 0) return; var state = redo.Pop(); undo.Push(state); Restore(state); }

    private void CanvasDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right ||
            (tool.Text == "Pan" && e.Button == MouseButtons.Left) ||
            (e.Button == MouseButtons.Left && ModifierKeys.HasFlag(Keys.Space)))
        {
            panning = true;
            panStart = e.Location;
            panAtStart = pan;
            canvas.Capture = true;
            canvas.Cursor = Cursors.Hand;
            return;
        }
        if (e.Button != MouseButtons.Left || shape.Text != "Custom") return;
        if (tool.Text == "Select" && !pickOrigin.Checked)
        {
            SelectAt(ToDesign(e.Location, false));
            return;
        }
        var p = ToDesign(e.Location, !pickOrigin.Checked);
        if (pickOrigin.Checked) { originX.Value = Clamp(originX, p.X); originY.Value = Clamp(originY, p.Y); pickOrigin.Checked = false; Changed(); return; }
        dragStart = p;
        dragCurrent = p;
        if (tool.Text == "Dot") { items.Add(new(SightItemKind.Ellipse, p.X, p.Y, 1.6, 1.6)); selected = items.Count - 1; dragStart = null; Changed(); }
    }
    private void CanvasMove(object? sender, MouseEventArgs e)
    {
        var cursorWorld = ToDesign(e.Location, false);
        cursorPosition.Text = $"X {cursorWorld.X:0.00}  Y {cursorWorld.Y:0.00}";
        if (panning)
        {
            pan = new PointF(panAtStart.X + e.X - panStart.X, panAtStart.Y + e.Y - panStart.Y);
            canvas.Invalidate();
            return;
        }
        if (dragStart is null) return;
        dragCurrent = ToDesign(e.Location);
        canvas.Invalidate();
    }
    private void CanvasUp(object? sender, MouseEventArgs e)
    {
        if (panning)
        {
            panning = false;
            canvas.Capture = false;
            UpdateCanvasCursor();
            return;
        }
        if (dragStart is not PointF a || tool.Text == "Dot") return;
        var b = dragCurrent ?? ToDesign(e.Location); dragStart = null; dragCurrent = null;
        if (Math.Abs(a.X - b.X) < .02 && Math.Abs(a.Y - b.Y) < .02) return;
        items.Add(tool.Text switch {
            "Line" => new(SightItemKind.Line,a.X,a.Y,b.X,b.Y),
            "Circle" => new(SightItemKind.Ellipse,(a.X+b.X)/2,(a.Y+b.Y)/2,Math.Abs(b.X-a.X)/2,Math.Abs(b.Y-a.Y)/2),
            _ => new(SightItemKind.Rectangle,a.X,a.Y,b.X-a.X,b.Y-a.Y)
        });
        selected = items.Count - 1; Changed();
    }

    private void PaintCanvas(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var drawItems = ParseCommands();
        var sightColor = ColorValue();
        using var pen = new Pen(sightColor, 2);
        for (var index = 0; index < drawItems.Count; index++)
        {
            var item = drawItems[index];
            pen.Color = shape.Text == "Custom" && index == selected ? Color.FromArgb(255, 247, 168) : sightColor;
            pen.Width = shape.Text == "Custom" && index == selected ? 3 : 2;
            var a = ToScreen(item.X1, item.Y1);
            if (item.Kind == SightItemKind.Line) { var b = ToScreen(item.X2, item.Y2); e.Graphics.DrawLine(pen, a, b); }
            else if (item.Kind == SightItemKind.Ellipse)
            {
                var rx = (float)(Math.Abs(item.X2) * Pixels); var ry = (float)(Math.Abs(item.Y2) * Pixels);
                e.Graphics.DrawEllipse(pen, a.X-rx, a.Y-ry, rx*2, ry*2);
            }
            else
            {
                var b=ToScreen(item.X1+item.X2,item.Y1+item.Y2);
                e.Graphics.DrawRectangle(pen,Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Abs(b.X-a.X),Math.Abs(b.Y-a.Y));
            }
        }
        var origin = ToScreen((double)originX.Value * (double)scale.Value / 100, (double)originY.Value * (double)scale.Value / 100);
        using var marker = new Pen(Color.FromArgb(255,212,95),2);
        e.Graphics.DrawLine(marker,origin.X-22,origin.Y,origin.X+22,origin.Y); e.Graphics.DrawLine(marker,origin.X,origin.Y-22,origin.X,origin.Y+22);
        e.Graphics.DrawEllipse(marker,origin.X-5,origin.Y-5,10,10); e.Graphics.DrawString("CCIP",Font,Brushes.Gold,origin.X+8,origin.Y-18);
        if (dragStart is PointF start && dragCurrent is PointF current)
            DrawPreviewItem(e.Graphics, PreviewItem(start, current).Scale((double)scale.Value / 100));
    }

    private SightItem PreviewItem(PointF start, PointF current) => tool.Text switch
    {
        "Line" => new(SightItemKind.Line, start.X, start.Y, current.X, current.Y),
        "Circle" => new(SightItemKind.Ellipse, (start.X + current.X) / 2, (start.Y + current.Y) / 2,
            Math.Abs(current.X - start.X) / 2, Math.Abs(current.Y - start.Y) / 2),
        _ => new(SightItemKind.Rectangle, start.X, start.Y, current.X - start.X, current.Y - start.Y)
    };

    private void DrawPreviewItem(Graphics graphics, SightItem item)
    {
        using var pen = new Pen(Color.White, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        var a = ToScreen(item.X1, item.Y1);
        if (item.Kind == SightItemKind.Line)
        {
            graphics.DrawLine(pen, a, ToScreen(item.X2, item.Y2));
            return;
        }
        if (item.Kind == SightItemKind.Ellipse)
        {
            var rx = (float)(Math.Abs(item.X2) * Pixels);
            var ry = (float)(Math.Abs(item.Y2) * Pixels);
            graphics.DrawEllipse(pen, a.X - rx, a.Y - ry, rx * 2, ry * 2);
            return;
        }
        var b = ToScreen(item.X1 + item.X2, item.Y1 + item.Y2);
        graphics.DrawRectangle(pen, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    private List<SightItem> ParseCommands(string? source = null)
    {
        var result = new List<SightItem>();
        foreach (System.Text.RegularExpressions.Match m in SightLogic.CommandRegex().Matches(source ?? commands.Text))
        {
            var values = System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value, @"[-+]?(?:\d+\.\d+|\d+|\.\d+)(?:[eE][-+]?\d+)?")
                .Select(n => double.Parse(n.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (values.Length < 4) continue;
            var kind = m.Groups[1].Value switch
            {
                "VECTOR_LINE" => SightItemKind.Line,
                "VECTOR_ELLIPSE" => SightItemKind.Ellipse,
                _ => SightItemKind.Rectangle
            };
            result.Add(new(kind, values[0], values[1], values[2], values[3]));
        }
        return result;
    }

    private void ImportSvg(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog { Filter = "SVG line art (*.svg)|*.svg|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            items.AddRange(SightLogic.ImportSvg(dialog.FileName));
            selected = items.Count - 1;
            shape.SelectedItem = "Custom";
            Changed();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Nudge(double dx, double dy)
    {
        if (selected < 0 || selected >= items.Count) return;
        var item = items[selected];
        items[selected] = item.Kind == SightItemKind.Line
            ? item with { X1 = item.X1 + dx, Y1 = item.Y1 + dy, X2 = item.X2 + dx, Y2 = item.Y2 + dy }
            : item with { X1 = item.X1 + dx, Y1 = item.Y1 + dy };
        Changed();
    }

    private bool SelectAt(PointF point)
    {
        var customScale = Math.Max(.01, (double)scale.Value / 100);
        var tolerance = Math.Max(.02, 6 / (Pixels * customScale));
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!HitTest(items[i], point, tolerance)) continue;
            selected = i;
            SyncValueFields();
            canvas.Invalidate();
            return true;
        }
        return false;
    }
    private static bool HitTest(SightItem item, PointF point, double tolerance)
    {
        if (item.Kind == SightItemKind.Line)
        {
            var dx = item.X2 - item.X1;
            var dy = item.Y2 - item.Y1;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < .000001)
                return Math.Sqrt(Math.Pow(point.X - item.X1, 2) + Math.Pow(point.Y - item.Y1, 2)) <= tolerance;
            var t = Math.Clamp(((point.X - item.X1) * dx + (point.Y - item.Y1) * dy) / lengthSquared, 0, 1);
            var nearestX = item.X1 + t * dx;
            var nearestY = item.Y1 + t * dy;
            return Math.Sqrt(Math.Pow(point.X - nearestX, 2) + Math.Pow(point.Y - nearestY, 2)) <= tolerance;
        }
        if (item.Kind == SightItemKind.Ellipse)
        {
            var rx = Math.Abs(item.X2);
            var ry = Math.Abs(item.Y2);
            var dx = point.X - item.X1;
            var dy = point.Y - item.Y1;
            if (rx <= tolerance || ry <= tolerance)
                return Math.Sqrt(dx * dx + dy * dy) <= Math.Max(rx, ry) + tolerance;
            var normalizedRadius = Math.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
            return Math.Abs(normalizedRadius - 1) <= tolerance / Math.Min(rx, ry);
        }

        var left = Math.Min(item.X1, item.X1 + item.X2);
        var right = Math.Max(item.X1, item.X1 + item.X2);
        var top = Math.Min(item.Y1, item.Y1 + item.Y2);
        var bottom = Math.Max(item.Y1, item.Y1 + item.Y2);
        if (point.X < left - tolerance || point.X > right + tolerance ||
            point.Y < top - tolerance || point.Y > bottom + tolerance)
            return false;
        var edgeDistance = Math.Min(
            Math.Min(Math.Abs(point.X - left), Math.Abs(point.X - right)),
            Math.Min(Math.Abs(point.Y - top), Math.Abs(point.Y - bottom)));
        return edgeDistance <= tolerance;
    }
    private void SyncValueFields()
    {
        if (selected < 0 || selected >= items.Count) return;
        var i = items[selected];
        valueALabel.Text = i.Kind == SightItemKind.Line ? "X1" : "X";
        valueBLabel.Text = i.Kind == SightItemKind.Line ? "Y1" : "Y";
        valueCLabel.Text = i.Kind switch { SightItemKind.Line => "X2", SightItemKind.Ellipse => "R X", _ => "W" };
        valueDLabel.Text = i.Kind switch { SightItemKind.Line => "Y2", SightItemKind.Ellipse => "R Y", _ => "H" };
        loading = true;
        valueA.Value = Clamp(valueA, i.X1);
        valueB.Value = Clamp(valueB, i.Y1);
        valueC.Value = Clamp(valueC, i.X2);
        valueD.Value = Clamp(valueD, i.Y2);
        loading = false;
    }

    private void ApplyValues()
    {
        if (selected < 0 || selected >= items.Count) return;
        var current = items[selected];
        items[selected] = new(current.Kind, (double)valueA.Value, (double)valueB.Value,
            current.Kind == SightItemKind.Ellipse ? Math.Abs((double)valueC.Value) : (double)valueC.Value,
            current.Kind == SightItemKind.Ellipse ? Math.Abs((double)valueD.Value) : (double)valueD.Value);
        Changed();
    }
    private void UseSelectedOrigin()
    {
        if (selected < 0 || selected >= items.Count) return;
        var point = items[selected].Center;
        originX.Value = Clamp(originX, point.X);
        originY.Value = Clamp(originY, point.Y);
        Changed();
    }

    private void ChooseOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = output.Text };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        output.Text = dialog.SelectedPath;
        Changed();
    }

    private void BuildPackage(object? sender, EventArgs e)
    {
        string? temp = null;
        try
        {
            var root = AppContext.BaseDirectory;
            var source = Path.Combine(root, "Resources", "source");
            var template = Path.Combine(root, "Resources", "template");
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException("Bundled source resources are missing.");

            temp = Path.Combine(Path.GetTempPath(), "HeliSightBuilder", Guid.NewGuid().ToString("N"));
            CopyDirectory(source, temp);
            var air = Path.Combine(temp, "reactivegui", "airHudElems.nut");
            var commandText = commands.Text;
            var mode0 = Shift(commandText, 0, 100);
            var mode1 = Shift(commandText, 0, 0);
            var updatedHud = SightLogic.ReplaceSightFunction(File.ReadAllText(air), mode0, mode1, ColorValue());
            File.WriteAllText(air, updatedHud, new UTF8Encoding(false));

            var packageDirectory = Path.Combine(output.Text, "pkg_user");
            Directory.CreateDirectory(packageDirectory);
            VromfsPackage.Build(Path.Combine(template, "pkg_user", "base.vromfs.bin"), temp,
                Path.Combine(packageDirectory, "base.vromfs.bin"));
            File.Copy(Path.Combine(template, "pkg_user.rq2"), Path.Combine(output.Text, "pkg_user.rq2"), true);
            File.Copy(Path.Combine(template, "pkg_user.ver"), Path.Combine(output.Text, "pkg_user.ver"), true);
            status.Text = $"Built: {output.Text}";
            MessageBox.Show(this, $"Built install files in:\n\n{output.Text}", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            status.Text = "Build failed";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (temp is not null && Directory.Exists(temp))
            {
                try { Directory.Delete(temp, true); }
                catch { }
            }
        }
    }

    private string Shift(string text, double targetX, double targetY)
    {
        var parsed = ParseCommands(text);
        if (parsed.Count == 0) return text;
        var originOutX = (double)originX.Value * (double)scale.Value / 100;
        var originOutY = (double)originY.Value * (double)scale.Value / 100;
        var dx = targetX - originOutX;
        var dy = targetY - originOutY;
        return SightLogic.Commands(parsed.Select(item => item.Kind == SightItemKind.Line
            ? item with { X1 = item.X1 + dx, Y1 = item.Y1 + dy, X2 = item.X2 + dx, Y2 = item.Y2 + dy }
            : item with { X1 = item.X1 + dx, Y1 = item.Y1 + dy }));
    }

    private void UpdateDiagnostics() =>
        diagnostics.Text = $"{ParseCommands().Count} vector commands | " +
            $"{Encoding.UTF8.GetByteCount(commands.Text):N0} command bytes | package growth supported";

    private void SaveAutosave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(autosavePath)!);
            File.WriteAllText(autosavePath, JsonSerializer.Serialize(CaptureState()));
        }
        catch
        {
            // Autosave failure must not interrupt editing.
        }
    }

    private void LoadAutosave()
    {
        try
        {
            if (!File.Exists(autosavePath)) return;
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(autosavePath));
            if (state is not null) Restore(state);
        }
        catch
        {
            // Ignore invalid or inaccessible autosave data.
        }
    }

    private Color ColorValue() => color.Text switch
    {
        "Green" => Color.FromArgb(60, 255, 90),
        "Amber" => Color.FromArgb(255, 190, 40),
        "Red" => Color.FromArgb(255, 70, 70),
        "Cyan" => Color.FromArgb(80, 230, 255),
        _ => Color.White
    };

    private double Pixels => 6 * (double)zoom.Value / 100;
    private PointF ToWorld(Point p) => new(
        (float)((p.X - canvas.ClientSize.Width / 2 - pan.X) / Pixels),
        (float)((p.Y - canvas.ClientSize.Height / 2 - pan.Y) / Pixels));
    private PointF ToDesign(Point p, bool applySnap = true)
    {
        var outputPoint = ToWorld(p);
        var customScale = Math.Max(.01, (double)scale.Value / 100);
        var x = outputPoint.X / customScale;
        var y = outputPoint.Y / customScale;
        return applySnap
            ? new PointF((float)Snap(x), (float)Snap(y))
            : new PointF((float)x, (float)y);
    }
    private PointF ToScreen(double x, double y) => new(
        (float)(canvas.ClientSize.Width / 2 + pan.X + x * Pixels),
        (float)(canvas.ClientSize.Height / 2 + pan.Y + y * Pixels));
    private double Snap(double value) =>
        snap.Checked ? Math.Round(value / (double)grid.Value) * (double)grid.Value : value;
    private void ChangeZoom(decimal multiplier, Point? anchor = null)
    {
        var anchorPoint = anchor ?? new Point(canvas.ClientSize.Width / 2, canvas.ClientSize.Height / 2);
        var before = ToWorld(anchorPoint);
        zoom.Value = Clamp(zoom, (double)(zoom.Value * multiplier));
        var after = ToWorld(anchorPoint);
        pan = new PointF(pan.X + (float)((after.X - before.X) * Pixels), pan.Y + (float)((after.Y - before.Y) * Pixels));
        canvas.Invalidate();
    }
    private void UpdateCanvasCursor() => canvas.Cursor = tool.Text == "Pan" ? Cursors.Hand :
        tool.Text == "Select" ? Cursors.Default : Cursors.Cross;
    private static decimal Clamp(NumericUpDown control, double value) =>
        Math.Max(control.Minimum, Math.Min(control.Maximum, (decimal)value));
    private static ComboBox Box(string[] values) => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 280,
        DataSource = values
    };
    private static NumericUpDown Number(decimal value, decimal increment, decimal max, decimal min = 0) =>
        new CommitNumericUpDown
        {
            Value = value,
            Increment = increment,
            Maximum = max,
            Minimum = min,
            DecimalPlaces = 2,
            Width = 120
        };
    private static Button Button(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(120, 30) };
        button.Click += handler;
        return button;
    }
    private static Button SmallButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, Size = new Size(34, 28), Margin = new Padding(3) };
        button.Click += handler;
        return button;
    }
    private Control NudgePad()
    {
        var pad = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 3, Margin = new Padding(3, 2, 3, 8) };
        Button NudgeButton(string text, EventHandler handler)
        {
            var button = new Button { Text = text, Size = new Size(72, 28), Margin = new Padding(2) };
            button.Click += handler;
            return button;
        }
        pad.Controls.Add(NudgeButton("Up", (_, _) => Nudge(0, -(double)nudge.Value)), 1, 0);
        pad.Controls.Add(NudgeButton("Left", (_, _) => Nudge(-(double)nudge.Value, 0)), 0, 1);
        pad.Controls.Add(NudgeButton("Down", (_, _) => Nudge(0, (double)nudge.Value)), 1, 1);
        pad.Controls.Add(NudgeButton("Right", (_, _) => Nudge((double)nudge.Value, 0)), 2, 1);
        return pad;
    }
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 3, 0, 3)
        };
        row.Controls.AddRange(controls);
        return row;
    }
    private static Label Section(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Width = 278,
        Height = 30,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Padding = new Padding(0, 10, 0, 0),
        Margin = new Padding(3, 5, 3, 0)
    };
    private static void AddField(FlowLayoutPanel panel, string label, Control control)
    {
        panel.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 1) });
        panel.Controls.Add(control);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }
}

public sealed record AppState(string Shape, double Size, double Gap, double Scale, string Color,
    double OriginX, double OriginY, List<SightItem> Items, int Selected, string Output,
    string Tool, string Part, double Zoom, bool Snap, double Grid, double Nudge);

public sealed class DoubleBufferedPanel : Panel
{
    public DoubleBufferedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        TabStop = true;
    }
}

public sealed class CommitNumericUpDown : NumericUpDown
{
    public event EventHandler? SpinCommitted;
    public override void UpButton() { base.UpButton(); SpinCommitted?.Invoke(this, EventArgs.Empty); }
    public override void DownButton() { base.DownButton(); SpinCommitted?.Invoke(this, EventArgs.Empty); }
}

internal static class StackExtensions
{
    public static void RemoveBottom<T>(this Stack<T> stack)
    {
        var keep=stack.Reverse().Skip(1).ToArray();stack.Clear();foreach(var item in keep)stack.Push(item);
    }
}
