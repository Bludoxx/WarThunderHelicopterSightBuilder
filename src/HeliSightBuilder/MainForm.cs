using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeliSightBuilder.Native;

public sealed class MainForm : Form
{
    private const double StandardHudHeightFraction = .018;
    private const int CommandDisplayLimit = 50_000;
    private readonly ComboBox shape = Box(["Dot", "Circle", "Cross", "Box", "T Sight", "Custom"]);
    private readonly ComboBox tool = Box(["Select", "Pan", "Line", "Circle", "Box", "Dot"]);
    private readonly ComboBox part = Box(["Crosshair", "Brackets", "Chevron", "Pipper", "Rocket Ladder", "Side Posts"]);
    private readonly ComboBox color = Box(["White", "Green", "Amber", "Red", "Cyan"]);
    private readonly ComboBox sizeProfile = Box(["Custom", "Small", "Medium", "Large", "Extra Large"]);
    private readonly ComboBox previewResolution = Box(["1920 x 1080", "2560 x 1440", "3840 x 2160", "1280 x 720"]);
    private readonly ComboBox savedDesigns = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly TextBox saveName = new() { Width = 180, PlaceholderText = "Design name" };
    private readonly NumericUpDown size = Number(4.2m, .1m, 1000000);
    private readonly NumericUpDown gap = Number(1, .1m, 1000000);
    private readonly NumericUpDown scale = Number(100, 1, 1000000, (decimal)EditorStateRules.MinimumScale);
    private readonly NumericUpDown lineWidth = Number(2, .5m, 50, .1m);
    private readonly NumericUpDown zoom = Number(100, 10, (decimal)EditorStateRules.MaximumZoom, (decimal)EditorStateRules.MinimumZoom);
    private readonly NumericUpDown grid = Number(1, .1m, 100, (decimal)EditorStateRules.MinimumGrid);
    private readonly NumericUpDown nudge = Number(.5m, .1m, 100, (decimal)EditorStateRules.MinimumNudge);
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
    private readonly CheckBox darkMode = new() { Text = "Dark mode", AutoSize = true };
    private readonly CheckBox fullScreenPreview = new() { Text = "Embed preview", AutoSize = true };
    private readonly Button openFullScreenPreview = new() { Text = "Full Screen", AutoSize = true };
    private readonly Label sizeAwareness = new() { AutoSize = false, Width = 278, Height = 38 };
    private readonly Label previewPixelSize = new()
        { AutoSize = false, Width = 145, Height = 25, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly DoubleBufferedPanel canvas = new() { BackColor = Color.FromArgb(93, 143, 186), Dock = DockStyle.Fill };
    private readonly RichTextBox commands = new()
        { Dock = DockStyle.Fill, Font = new Font("Consolas", 10), WordWrap = false, ReadOnly = true };
    private readonly TextBox output = new() { Dock = DockStyle.Fill };
    private readonly TextBox gameContent = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Label diagnostics = new() { AutoSize = true };
    private readonly Label cursorPosition = new() { Text = "X 0.00  Y 0.00", AutoSize = true };
    private readonly FlowLayoutPanel controlPanel = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(12)
    };
    private readonly List<SightItem> items = [new(SightItemKind.Ellipse, 0, 0, 2.1, 2.1)];
    private readonly Stack<AppState> undo = new();
    private readonly Stack<AppState> redo = new();
    private readonly System.Windows.Forms.Timer autosaveTimer = new() { Interval = 750 };
    private readonly string autosavePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeliSightBuilder", "autosave-native.json");
    private readonly string designsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeliSightBuilder", "designs");

    private int selected = 0;
    private readonly HashSet<int> selectedIndices = [0];
    private bool loading;
    private PointF? dragStart;
    private PointF? dragCurrent;
    private PointF pan = new(0, 0);
    private bool panning;
    private Point panStart;
    private PointF panAtStart;
    private PointF? selectionStart;
    private PointF? selectionCurrent;
    private bool additiveSelection;
    private string generatedCommands = "";
    private List<SightItem> renderedItems = [];

    public MainForm()
    {
        Text = "War Thunder Helicopter Sight Builder";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream(
            "HeliSightBuilder.Assets.HeliSightBuilder.ico"))
        {
            if (iconStream is not null)
                Icon = (Icon)new Icon(iconStream).Clone();
        }
        MinimumSize = new Size(980, 640);
        Size = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "HeliSightOutput");
        gameContent.Text = GameInstallService.DetectContentDirectory() ?? "";
        tool.Width = 150;
        zoom.Width = 82;
        previewResolution.Width = 130;
        shape.SelectedItem = "Dot"; tool.SelectedItem = "Select"; part.SelectedItem = "Crosshair"; color.SelectedItem = "White";
        sizeProfile.SelectedItem = "Medium";
        previewResolution.SelectedItem = "1920 x 1080";

        Controls.Add(BuildLayout());
        WireEvents();
        LoadAutosave();
        RefreshSavedDesigns();
        ApplyTheme();
        Record();
        Sync();
        Shown += (_, _) => BeginInvoke(() => controlPanel.AutoScrollPosition = Point.Empty);
    }

    public IReadOnlyList<string> RunInteractionQualityChecks(string? progressPath = null)
    {
        var results = new List<string>();
        if (progressPath is not null) File.WriteAllText(progressPath, "");
        var autosaveBackup = File.Exists(autosavePath) ? File.ReadAllBytes(autosavePath) : null;
        void Require(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"UI quality check failed: {name}");
            var line = $"PASS: {name}";
            results.Add(line);
            if (progressPath is not null) File.AppendAllLines(progressPath, [line]);
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
        Require(InsideClient(fullScreenPreview) && InsideClient(previewResolution),
            "minimum-size full-screen preview controls");
        Require(InsideClient(openFullScreenPreview), "minimum-size full-screen launch control");
        Require(InsideClient(output) && InsideClient(gameContent) && InsideClient(status),
            "minimum-size build and install rows visibility");
        Require(Icon is not null && Icon.Width >= 16, "embedded window logo");
        ShowFullScreenPreview();
        Application.DoEvents();
        var fullPreviewWindow = OwnedForms.SingleOrDefault();
        Require(fullPreviewWindow is not null &&
            fullPreviewWindow.FormBorderStyle == FormBorderStyle.None &&
            fullPreviewWindow.WindowState == FormWindowState.Maximized,
            "true borderless full-screen preview window");
        fullPreviewWindow!.Close();
        Require(zoom.Minimum > 0 && grid.Minimum > 0 && nudge.Minimum > 0 && scale.Minimum > 0,
            "unsafe zero numeric values are blocked");
        var largeCircle = new SightItem(SightItemKind.Ellipse, 0, 0, 100, 100);
        Require(!HitTest(largeCircle, PointF.Empty, 1), "large circle interior is not selected");
        Require(HitTest(largeCircle, new PointF(100, 0), 1), "large circle stroke is selected");
        loading = true;
        shape.SelectedItem = "Dot";
        size.Value = 4.2m;
        scale.Value = 100;
        loading = false;
        Sync();
        var dotPreviewItems = ParseCommands();
        Require(Math.Abs(GeometryExtent(dotPreviewItems) -
            EditorStateRules.RelativeSizeReferenceEnvelope) < .1,
            "dot preset follows normalized 100% size");
        Require(dotPreviewItems.Count == 1 && dotPreviewItems[0].Kind == SightItemKind.FilledEllipse,
            "dot preset is one true filled ellipse");
        zoom.Value = 10000;
        canvas.Size = new Size(900, 500);
        using (var dotBitmap = new Bitmap(canvas.Width, canvas.Height))
        using (var dotGraphics = Graphics.FromImage(dotBitmap))
        {
            dotGraphics.Clear(canvas.BackColor);
            PaintCanvas(canvas, new PaintEventArgs(dotGraphics, canvas.ClientRectangle));
            var centerX = canvas.Width / 2 + (int)pan.X + 20;
            var centerY = canvas.Height / 2 + (int)pan.Y;
            var filledPixels = Enumerable.Range(centerY - 100, 201)
                .Select(y => dotBitmap.GetPixel(centerX, y))
                .Count(pixel => pixel.R > 220 && pixel.G > 220 && pixel.B > 220);
            Require(filledPixels > 180, "dot remains solid at extreme preview zoom");
        }
        Hide();
        var corruptedState = CaptureState() with
        {
            Zoom = 0,
            Grid = 0,
            Nudge = 0,
            Items = [new(SightItemKind.Line, -9_000_000_000, 0, 10_000_000_000, 1)]
        };
        Restore(corruptedState);
        Require((double)zoom.Value == EditorStateRules.DefaultZoom &&
            (double)grid.Value == EditorStateRules.DefaultGrid &&
            (double)nudge.Value == EditorStateRules.DefaultNudge,
            "corrupted numeric state recovery");
        Require(items.All(EditorStateRules.IsValidItem), "corrupted geometry recovery");
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
        Require(Math.Abs(items[0].X2 - items[0].X1) * OutputScale > 100,
            "drawing has no artificial size cap");

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
        Require(zoom.Minimum <= .01m && zoom.Maximum >= 1_000_000m, "wide safe zoom range");

        loading = true;
        items.Clear();
        selected = -1;
        snap.Checked = false;
        grid.Value = 1;
        scale.Value = 100;
        zoom.Value = 10000;
        pan = PointF.Empty;
        loading = false;
        tool.SelectedItem = "Line";
        var tinyStart = new Point(canvas.ClientSize.Width / 2, canvas.ClientSize.Height / 2);
        var tinyEnd = new Point(tinyStart.X + 3, tinyStart.Y);
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, tinyStart.X, tinyStart.Y, 0));
        CanvasMove(canvas, new MouseEventArgs(MouseButtons.Left, 0, tinyEnd.X, tinyEnd.Y, 0));
        CanvasUp(canvas, new MouseEventArgs(MouseButtons.Left, 1, tinyEnd.X, tinyEnd.Y, 0));
        Require(items.Count == 1 && items[0].X1 != items[0].X2,
            "sub-grid line drawing remains precise");

        tool.SelectedItem = "Select";
        var linePoint = ToScreen(items[0].X1 * OutputScale, items[0].Y1 * OutputScale);
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
        Require(FindButton(this, "Install Sight") is not null &&
            FindButton(this, "Restore Original") is not null,
            "install and restore button discovery");
        Require(FindButton(controlPanel, "Auto-fit Medium") is not null &&
            FindButton(controlPanel, "Save") is not null &&
            FindButton(controlPanel, "Load") is not null,
            "size awareness and saved-design controls");
        Require(FindButton(controlPanel, "Mirror Selected (CCIP)") is not null,
            "mirror selected button discovery");
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
        Require(Math.Abs(Snap(.6) * OutputScale % GridOutputSpacing) < .000001,
            "exact grid snapping");
        pickOrigin.Checked = true;
        tool.SelectedItem = "Line";
        var preciseOrigin = ToScreen(2.15 * OutputScale, -2.15 * OutputScale);
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, (int)preciseOrigin.X, (int)preciseOrigin.Y, 0));
        var mousePixelTolerance = 1 / Pixels + .01;
        Require(Math.Abs((double)originX.Value - 2.15) <= mousePixelTolerance &&
            Math.Abs((double)originY.Value + 2.15) <= mousePixelTolerance,
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
        var dotStrokeCount = SightLogic.FilledDot(0, 0, 1.6).Count;
        Require(items.Count == countBeforeUndo + dotStrokeCount, "dot drawing");
        Undo();
        Require(items.Count == countBeforeUndo, "undo");
        Redo();
        Require(items.Count == countBeforeUndo + dotStrokeCount, "redo");

        scale.Value = 900;
        tool.SelectedItem = "Line";
        CanvasDown(canvas, new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
        CanvasMove(canvas, new MouseEventArgs(MouseButtons.Left, 0, 890, 490, 0));
        CanvasUp(canvas, new MouseEventArgs(MouseButtons.Left, 1, 890, 490, 0));
        Require(items.Count == countBeforeUndo + dotStrokeCount + 1, "extreme-scale drawing");
        Require(SightLogic.CommandRegex().Matches(generatedCommands).Count == items.Count, "command synchronization");

        items.Clear();
        items.Add(new(SightItemKind.Line, -5, 0, -3, 0));
        items.Add(new(SightItemKind.Line, 3, 0, 5, 0));
        selectedIndices.Clear();
        SelectInRectangle(new PointF(-6, -1), new PointF(0, 1), false);
        Require(ValidSelection().SequenceEqual([0]), "marquee single-area selection");
        SelectInRectangle(new PointF(0, -1), new PointF(6, 1), true);
        Require(ValidSelection().SequenceEqual([0, 1]), "additive multi-selection");
        var firstBeforeGroupNudge = items[0].X1;
        var secondBeforeGroupNudge = items[1].X1;
        Nudge(.25, 0);
        Require(Math.Abs(items[0].X1 - firstBeforeGroupNudge - .25) < .0001 &&
            Math.Abs(items[1].X1 - secondBeforeGroupNudge - .25) < .0001,
            "group nudge");
        DeleteSelected();
        Require(items.Count == 0 && selectedIndices.Count == 0, "group delete");
        originX.Value = 2;
        items.Add(new(SightItemKind.Line, 0, 0, 1, 1));
        items.Add(new(SightItemKind.Ellipse, 3, 0, 1, 2));
        items.Add(new(SightItemKind.Rectangle, -1, -1, 2, 2));
        selectedIndices.UnionWith([0, 1, 2]);
        selected = 2;
        MirrorSelected();
        Require(items.Count == 6 && ValidSelection().SequenceEqual([3, 4, 5]),
            "mirrored copies become selected");
        Require(items[3].X1 == 4 && items[3].X2 == 3 &&
            items[4].X1 == 1 && items[5].X1 == 3,
            "selected geometry mirrors across CCIP axis");
        items.Clear();
        selectedIndices.Clear();
        items.Add(new(SightItemKind.Line, -1, 0, 1, 0));
        selected = 0;
        selectedIndices.Add(0);
        Sync();

        shape.SelectedItem = "Custom";
        scale.Value = 1;
        Sync();
        Require(!sizeAwareness.Text.Contains("Warning", StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(CurrentOutputExtent() - 10) < .1,
            "small relative size has no false warning");
        scale.Value = 10000;
        Sync();
        Require(!sizeAwareness.Text.Contains("Warning", StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(CurrentOutputExtent() - 100000) < 1,
            "large relative size remains unrestricted");
        ApplyTargetSize("Medium");
        Require(Math.Abs(CurrentOutputExtent() - TargetExtent("Medium")) < 1,
            "medium perception auto-fit");
        previewResolution.SelectedItem = "1920 x 1080";
        Require(Math.Abs(PredictedPixelExtent() -
            TargetExtent("Medium") * 1080 * StandardHudHeightFraction / 100) < 1,
            "1080p in-game pixel-size prediction");
        Require(Math.Abs(76.5625 * 941 * StandardHudHeightFraction / 100 - 12.97) < .1,
            "reported 941p screenshot calibration");
        fullScreenPreview.Checked = true;
        canvas.Size = new Size(900, 500);
        using (var previewBitmap = new Bitmap(canvas.Width, canvas.Height))
        using (var previewGraphics = Graphics.FromImage(previewBitmap))
        {
            PaintCanvas(canvas, new PaintEventArgs(previewGraphics, canvas.ClientRectangle));
            var viewport = PreviewViewport(canvas.ClientSize);
            var sightPixels = 0;
            var expectedSightColor = ColorValue();
            for (var y = (int)viewport.Top; y < (int)viewport.Bottom; y++)
            for (var x = (int)viewport.Left; x < (int)viewport.Right; x++)
            {
                var pixel = previewBitmap.GetPixel(x, y);
                var colorDistance = Math.Abs(pixel.R - expectedSightColor.R) +
                    Math.Abs(pixel.G - expectedSightColor.G) +
                    Math.Abs(pixel.B - expectedSightColor.B);
                if (colorDistance < 250) sightPixels++;
            }
            Require(sightPixels > 0, "full-screen preview renders sight pixels");
        }
        fullScreenPreview.Checked = false;

        var compactDot = SightLogic.FilledDot(0, 0, 4 / OutputScale);
        Require(Math.Abs(GeometryExtent(compactDot) * OutputScale - 8) < .1,
            "compact custom dot size");

        items.Clear();
        items.Add(new(SightItemKind.Line, -5, 0, 5, 0));
        var matchedPart = MatchPartToCurrentDesign(SightLogic.Part("Brackets"));
        Require(Math.Abs(GeometryExtent(matchedPart) - GeometryExtent(items)) < .01,
            "spawned part matches current design perception");

        var beforeDenseArt = CaptureState();
        items.Clear();
        items.AddRange(Enumerable.Range(0, 30_000).Select(index =>
        {
            var y = index % 300 - 150;
            var x = index / 300d - 50;
            return new SightItem(SightItemKind.Line, x, y, x + .75, y + (index % 2 == 0 ? .5 : -.5));
        }));
        selectedIndices.Clear();
        selected = -1;
        loading = true;
        shape.SelectedItem = "Custom";
        loading = false;
        var denseSyncTimer = System.Diagnostics.Stopwatch.StartNew();
        Sync();
        denseSyncTimer.Stop();
        var densePaintTimer = System.Diagnostics.Stopwatch.StartNew();
        using (var denseBitmap = new Bitmap(900, 500))
        using (var denseGraphics = Graphics.FromImage(denseBitmap))
            PaintCanvas(canvas, new PaintEventArgs(denseGraphics, new Rectangle(0, 0, 900, 500)));
        densePaintTimer.Stop();
        Require(renderedItems.Count == 30_000 &&
            generatedCommands.StartsWith("[VECTOR_LINE", StringComparison.Ordinal) &&
            generatedCommands.EndsWith("]", StringComparison.Ordinal),
            "dense artwork keeps all exported geometry");
        Require(commands.TextLength < generatedCommands.Length && commands.TextLength < 60_000,
            "dense command display remains bounded");
        Require(denseSyncTimer.Elapsed < TimeSpan.FromSeconds(8),
            $"dense artwork synchronization ({denseSyncTimer.Elapsed.TotalSeconds:0.00}s)");
        Require(densePaintTimer.Elapsed < TimeSpan.FromSeconds(8),
            $"dense artwork paint ({densePaintTimer.Elapsed.TotalSeconds:0.00}s)");
        Restore(beforeDenseArt);

        darkMode.Checked = true;
        Require(BackColor.R < 60 && CaptureState().DarkMode, "dark theme persistence");
        darkMode.Checked = false;
        lineWidth.Value = 4.5m;
        Require(Math.Abs(CaptureState().LineWidth - 4.5) < .001, "line width persistence");

        var saveTestName = "quality-check-" + Guid.NewGuid().ToString("N");
        saveName.Text = saveTestName;
        SaveNamedDesign();
        var saveTestPath = Path.Combine(designsPath, saveTestName + ".json");
        Require(File.Exists(saveTestPath), "named design save");
        items.Clear();
        LoadNamedDesign();
        Require(items.Count == 1, "named design load");
        File.Delete(saveTestPath);
        RefreshSavedDesigns();

        var integrationBuild = Path.Combine(Path.GetTempPath(), "HeliSightBuilder",
            "ui-build-" + Guid.NewGuid().ToString("N"));
        var integrationResources = integrationBuild + "-resources";
        try
        {
            CreateInstallFiles(integrationBuild);
            EmbeddedResources.ExtractTo(integrationResources);
            var builtPackage = Path.Combine(integrationBuild, "pkg_user", "base.vromfs.bin");
            var baselinePackage = Path.Combine(integrationResources, "template", "pkg_user", "base.vromfs.bin");
            Require(!SHA256.HashData(File.ReadAllBytes(builtPackage))
                    .SequenceEqual(SHA256.HashData(File.ReadAllBytes(baselinePackage))),
                "app build embeds current sight instead of unchanged template");
        }
        finally
        {
            if (Directory.Exists(integrationBuild)) Directory.Delete(integrationBuild, true);
            if (Directory.Exists(integrationResources)) Directory.Delete(integrationResources, true);
        }

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
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));

        var controls = controlPanel;
        controls.Controls.Add(Section("Sight"));
        AddField(controls, "Shape", shape); AddField(controls, "Size", size); AddField(controls, "Center gap", gap);
        AddField(controls, "In-game size", sizeProfile);
        AddField(controls, "Design scale %", scale);
        AddField(controls, "Line width", lineWidth);
        controls.Controls.Add(sizeAwareness);
        controls.Controls.Add(Button("Auto-fit Medium", (_, _) => ApplyTargetSize("Medium")));
        controls.Controls.Add(Section("Custom design"));
        AddField(controls, "Add sight part", part);
        controls.Controls.Add(Button("Add Part", (_, _) =>
        {
            var first = items.Count;
            items.AddRange(MatchPartToCurrentDesign(SightLogic.Part(part.Text)));
            selectedIndices.Clear();
            for (var i = first; i < items.Count; i++) selectedIndices.Add(i);
            selected = items.Count - 1;
            shape.SelectedItem = "Custom";
            Changed();
        }));
        controls.Controls.Add(Button("Import SVG Line Art", ImportSvg));
        controls.Controls.Add(Row(Button("Undo", (_, _) => Undo()), Button("Redo", (_, _) => Redo())));
        AddField(controls, "Sight color", color);
        controls.Controls.Add(snap);
        controls.Controls.Add(Row(new Label { Text = "Grid", AutoSize = true }, grid, new Label { Text = "Nudge", AutoSize = true }, nudge));
        controls.Controls.Add(Section("Selected shapes"));
        controls.Controls.Add(Row(valueALabel, valueA, valueBLabel, valueB));
        controls.Controls.Add(Row(valueCLabel, valueC, valueDLabel, valueD));
        controls.Controls.Add(Button("Apply Values", (_, _) => ApplyValues()));
        controls.Controls.Add(NudgePad());
        controls.Controls.Add(Button("Mirror Selected (CCIP)", (_, _) => MirrorSelected()));
        controls.Controls.Add(Button("Delete Selected", (_, _) => DeleteSelected()));
        controls.Controls.Add(Button("Clear Custom Sight", (_, _) => { items.Clear(); selectedIndices.Clear(); selected = -1; shape.SelectedItem = "Custom"; Changed(); }));
        controls.Controls.Add(Section("Saved designs"));
        controls.Controls.Add(saveName);
        controls.Controls.Add(Row(Button("Save", (_, _) => SaveNamedDesign()), Button("Load", (_, _) => LoadNamedDesign())));
        controls.Controls.Add(savedDesigns);
        controls.Controls.Add(Button("Delete Save", (_, _) => DeleteNamedDesign()));
        controls.Controls.Add(Section("Appearance"));
        controls.Controls.Add(darkMode);
        controls.Controls.Add(Section("CCIP origin"));
        controls.Controls.Add(Row(new Label { Text = "X", AutoSize = true }, originX, new Label { Text = "Y", AutoSize = true }, originY));
        controls.Controls.Add(pickOrigin);
        controls.Controls.Add(Row(
            Button("Use Selected Center", (_, _) => UseSelectedOrigin()),
            Button("Reset Origin", (_, _) => { originX.Value = 0; originY.Value = 0; Changed(); })));
        controls.Controls.Add(Button("Reset View", (_, _) =>
        {
            pan = PointF.Empty;
            FitGameCanvasView();
            canvas.Invalidate();
        }));
        controls.Controls.Add(diagnostics);

        var main = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, Padding = new Padding(8) };
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        main.Controls.Add(canvas, 0, 0);
        var zoomRow = Row(
            new Label { Text = "Tool", AutoSize = true },
            tool,
            new Label { Text = "Zoom", AutoSize = true },
            SmallButton("-", (_, _) => ChangeZoom(0.8m)),
            zoom,
            SmallButton("+", (_, _) => ChangeZoom(1.25m)),
            cursorPosition);
        zoomRow.Dock = DockStyle.Fill; main.Controls.Add(zoomRow, 0, 1);
        var previewRow = Row(
            fullScreenPreview,
            openFullScreenPreview,
            previewResolution,
            previewPixelSize);
        previewRow.Dock = DockStyle.Fill;
        main.Controls.Add(previewRow, 0, 2);
        var technical = new GroupBox { Text = "Generated vector commands", Dock = DockStyle.Fill, Padding = new Padding(8) };
        technical.Controls.Add(commands);
        main.Controls.Add(technical, 0, 3);

        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2,
            Padding = new Padding(12, 8, 12, 8)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        bottom.Controls.Add(new Label { Text = "Output folder", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        bottom.Controls.Add(output, 1, 0);
        bottom.Controls.Add(Button("Choose Folder", ChooseOutput), 2, 0);
        bottom.Controls.Add(Button("Build install files", BuildPackage), 3, 0);
        bottom.Controls.Add(status, 4, 0);
        bottom.Controls.Add(new Label { Text = "Game content", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        bottom.Controls.Add(gameContent, 1, 1);
        bottom.Controls.Add(Button("Find / Choose", ChooseGameContent), 2, 1);
        bottom.Controls.Add(Button("Install Sight", InstallSight), 3, 1);
        bottom.Controls.Add(Button("Restore Original", RestoreOriginal), 4, 1);
        root.Controls.Add(controls, 0, 0); root.Controls.Add(main, 1, 0); root.Controls.Add(bottom, 0, 1);
        root.SetColumnSpan(bottom, 2);
        return root;
    }

    private void WireEvents()
    {
        shape.SelectedValueChanged += (_, _) => Sync();
        foreach (var numeric in new[] { size, gap, scale, lineWidth, originX, originY })
        {
            numeric.Validated += (_, _) =>
            {
                if (loading) return;
                if (numeric == scale) sizeProfile.SelectedItem = "Custom";
                Changed();
            };
            if (numeric is CommitNumericUpDown commit)
                commit.SpinCommitted += (_, _) =>
                {
                    if (loading) return;
                    if (numeric == scale) sizeProfile.SelectedItem = "Custom";
                    Changed();
                };
            numeric.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter || loading) return;
                if (numeric == scale) sizeProfile.SelectedItem = "Custom";
                Changed();
                canvas.Focus();
                e.SuppressKeyPress = true;
            };
        }
        color.SelectedValueChanged += (_, _) => { if (!loading) Changed(); };
        sizeProfile.SelectedValueChanged += (_, _) =>
        {
            if (!loading && sizeProfile.Text != "Custom") ApplyTargetSize(sizeProfile.Text);
        };
        darkMode.CheckedChanged += (_, _) =>
        {
            ApplyTheme();
            if (!loading) Changed();
        };
        fullScreenPreview.CheckedChanged += (_, _) => { UpdatePreviewPixelSize(); canvas.Invalidate(); };
        openFullScreenPreview.Click += (_, _) => ShowFullScreenPreview();
        previewResolution.SelectedValueChanged += (_, _) => { UpdatePreviewPixelSize(); canvas.Invalidate(); };
        zoom.ValueChanged += (_, _) => canvas.Invalidate();
        gameContent.Validated += (_, _) => { if (!loading) Changed(); };
        tool.SelectedValueChanged += (_, _) => UpdateCanvasCursor();
        autosaveTimer.Tick += (_, _) => { autosaveTimer.Stop(); SaveAutosave(); };
        canvas.Paint += PaintCanvas;
        canvas.MouseDown += CanvasDown; canvas.MouseMove += CanvasMove; canvas.MouseUp += CanvasUp;
        canvas.MouseWheel += (_, e) => ChangeZoom(e.Delta > 0 ? 1.2m : .8m, e.Location);
        FormClosing += (_, _) => { autosaveTimer.Stop(); SaveAutosave(); };
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) Undo();
            if (e.Control && e.KeyCode == Keys.Y) Redo();
            if (e.KeyCode == Keys.Escape) { dragStart = null; dragCurrent = null; selectionStart = null; selectionCurrent = null; panning = false; canvas.Capture = false; UpdateCanvasCursor(); canvas.Invalidate(); }
        };
        UpdateCanvasCursor();
    }

    private void Sync()
    {
        try
        {
            loading = true;
            if (items.Any(item => !EditorStateRules.IsValidItem(item)))
                throw new InvalidDataException("Invalid geometry was blocked. Reset or restore the design before building.");
            var sourceItems = shape.Text switch
            {
                "Custom" => items,
                "Dot" => CurrentSourceItems(),
                _ => SightLogic.Preset(shape.Text, (double)size.Value, (double)gap.Value)
            };
            var outputScale = OutputScale;
            generatedCommands = SightLogic.Commands(sourceItems, outputScale);
            renderedItems = sourceItems.Select(item => item.Scale(outputScale)).ToList();
            commands.Text = generatedCommands.Length <= CommandDisplayLimit
                ? generatedCommands
                : generatedCommands[..CommandDisplayLimit] +
                  $"\n\n[Display shortened. All {renderedItems.Count:N0} commands remain in the exported package.]";
        }
        catch (Exception ex)
        {
            generatedCommands = "";
            renderedItems.Clear();
            commands.Clear();
            status.Text = ex.Message;
        }
        finally { loading = false; }
        SyncValueFields(); UpdateDiagnostics(); UpdateSizeAwareness(); UpdatePreviewPixelSize(); canvas.Invalidate();
        ScheduleAutosave();
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
        (double)zoom.Value, snap.Checked, (double)grid.Value, (double)nudge.Value, gameContent.Text, 3,
        darkMode.Checked, sizeProfile.Text, (double)lineWidth.Value);
    private void Restore(AppState state)
    {
        state = EditorStateRules.Sanitize(state, out var recovered);
        loading = true;
        shape.SelectedItem = state.Shape; size.Value = Clamp(size, state.Size); gap.Value = Clamp(gap, state.Gap);
        scale.Value = Clamp(scale, state.Scale); color.SelectedItem = state.Color; originX.Value = Clamp(originX, state.OriginX);
        originY.Value = Clamp(originY, state.OriginY); items.Clear(); items.AddRange(state.Items); selected = state.Selected;
        selectedIndices.Clear(); if (selected >= 0) selectedIndices.Add(selected);
        output.Text = state.Output;
        tool.SelectedItem = string.IsNullOrWhiteSpace(state.Tool) ? "Select" : state.Tool;
        part.SelectedItem = string.IsNullOrWhiteSpace(state.Part) ? "Crosshair" : state.Part;
        zoom.Value = Clamp(zoom, state.Zoom <= 0 ? 100 : state.Zoom);
        snap.Checked = state.Snap;
        grid.Value = Clamp(grid, state.Grid <= 0 ? 1 : state.Grid);
        nudge.Value = Clamp(nudge, state.Nudge <= 0 ? .5 : state.Nudge);
        lineWidth.Value = Clamp(lineWidth, state.LineWidth);
        gameContent.Text = state.GameContent ?? GameInstallService.DetectContentDirectory() ?? "";
        darkMode.Checked = state.DarkMode;
        sizeProfile.SelectedItem = string.IsNullOrWhiteSpace(state.SizeProfile) ? "Custom" : state.SizeProfile;
        loading = false;
        ApplyTheme();
        Sync();
        if (recovered) status.Text = "Recovered from invalid saved values. Unsafe geometry was reset.";
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
            var point = ToDesign(e.Location, false);
            additiveSelection = ModifierKeys.HasFlag(Keys.Control) || ModifierKeys.HasFlag(Keys.Shift);
            if (SelectAt(point, additiveSelection)) return;
            if (!additiveSelection) selectedIndices.Clear();
            selectionStart = point;
            selectionCurrent = point;
            canvas.Capture = true;
            canvas.Invalidate();
            return;
        }
        var p = ToDesign(e.Location, !pickOrigin.Checked);
        if (pickOrigin.Checked) { originX.Value = Clamp(originX, p.X); originY.Value = Clamp(originY, p.Y); pickOrigin.Checked = false; Changed(); return; }
        dragStart = p;
        dragCurrent = p;
        if (tool.Text == "Dot")
        {
            var dot = SightLogic.FilledDot(p.X, p.Y, 4 / OutputScale);
            items.AddRange(dot);
            selectedIndices.Clear();
            for (var i = items.Count - dot.Count; i < items.Count; i++)
                selectedIndices.Add(i);
            selected = items.Count - 1;
            dragStart = null;
            Changed();
        }
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
        if (selectionStart is not null)
        {
            selectionCurrent = ToDesign(e.Location, false);
            canvas.Invalidate();
            return;
        }
        if (dragStart is null) return;
        dragCurrent = ToDrawPoint(e.Location);
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
        if (selectionStart is PointF selectionA)
        {
            var selectionB = selectionCurrent ?? ToDesign(e.Location, false);
            selectionStart = null;
            selectionCurrent = null;
            canvas.Capture = false;
            SelectInRectangle(selectionA, selectionB, additiveSelection);
            return;
        }
        if (dragStart is not PointF a || tool.Text == "Dot") return;
        var b = dragCurrent ?? ToDrawPoint(e.Location); dragStart = null; dragCurrent = null;
        if (a == b) return;
        items.Add(tool.Text switch
        {
            "Line" => new(SightItemKind.Line, a.X, a.Y, b.X, b.Y),
            "Circle" => new(SightItemKind.Ellipse, (a.X + b.X) / 2, (a.Y + b.Y) / 2, Math.Abs(b.X - a.X) / 2, Math.Abs(b.Y - a.Y) / 2),
            _ => new(SightItemKind.Rectangle, a.X, a.Y, b.X - a.X, b.Y - a.Y)
        });
        selectedIndices.Clear();
        selected = items.Count - 1;
        selectedIndices.Add(selected);
        Changed();
    }

    private PointF ToDrawPoint(Point location)
    {
        return ToDesign(location);
    }

    private void PaintCanvas(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = renderedItems.Count > 5_000
            ? System.Drawing.Drawing2D.SmoothingMode.None
            : System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if (fullScreenPreview.Checked)
        {
            PaintFullScreenPreview(e.Graphics, canvas.ClientSize);
            return;
        }
        PaintGrid(e.Graphics);
        PaintGameCanvasBoundary(e.Graphics);
        PaintOriginMarker(e.Graphics);
        var drawItems = renderedItems;
        var sightColor = ColorValue();
        var configuredLineWidth = (float)lineWidth.Value;
        using var pen = new Pen(sightColor, configuredLineWidth);
        for (var index = 0; index < drawItems.Count; index++)
        {
            var item = drawItems[index];
            var isSelected = shape.Text == "Custom" && selectedIndices.Contains(index);
            if (item.Kind == SightItemKind.Line)
            {
                var lineA = ToScreen(item.X1, item.Y1);
                var lineB = ToScreen(item.X2, item.Y2);
                var margin = configuredLineWidth + 2;
                if (Math.Max(lineA.X, lineB.X) < -margin ||
                    Math.Min(lineA.X, lineB.X) > canvas.ClientSize.Width + margin ||
                    Math.Max(lineA.Y, lineB.Y) < -margin ||
                    Math.Min(lineA.Y, lineB.Y) > canvas.ClientSize.Height + margin)
                    continue;
                pen.Color = isSelected ? Color.FromArgb(255, 247, 168) : sightColor;
                pen.Width = isSelected ? configuredLineWidth + 1 : configuredLineWidth;
                e.Graphics.DrawLine(pen, lineA, lineB);
                continue;
            }
            pen.Color = isSelected ? Color.FromArgb(255, 247, 168) : sightColor;
            pen.Width = isSelected ? configuredLineWidth + 1 : configuredLineWidth;
            var a = ToScreen(item.X1, item.Y1);
            if (item.Kind is SightItemKind.Ellipse or SightItemKind.FilledEllipse)
            {
                var rx = (float)(Math.Abs(item.X2) * Pixels); var ry = (float)(Math.Abs(item.Y2) * Pixels);
                if (item.Kind == SightItemKind.FilledEllipse)
                {
                    using var fillBrush = new SolidBrush(pen.Color);
                    e.Graphics.FillEllipse(fillBrush, a.X - rx, a.Y - ry, rx * 2, ry * 2);
                    if (isSelected) e.Graphics.DrawEllipse(pen, a.X - rx, a.Y - ry, rx * 2, ry * 2);
                }
                else
                    e.Graphics.DrawEllipse(pen, a.X - rx, a.Y - ry, rx * 2, ry * 2);
            }
            else
            {
                var b = ToScreen(item.X1 + item.X2, item.Y1 + item.Y2);
                e.Graphics.DrawRectangle(pen, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            }
        }
        if (dragStart is PointF start && dragCurrent is PointF current)
            DrawPreviewItem(e.Graphics, PreviewItem(start, current).Scale(OutputScale));
        if (selectionStart is PointF selectA && selectionCurrent is PointF selectB)
        {
            var a = ToScreen(selectA.X * OutputScale, selectA.Y * OutputScale);
            var b = ToScreen(selectB.X * OutputScale, selectB.Y * OutputScale);
            using var selectionPen = new Pen(Color.FromArgb(255, 247, 168), 1)
                { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            using var selectionBrush = new SolidBrush(Color.FromArgb(30, 255, 247, 168));
            var rectangle = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
            e.Graphics.FillRectangle(selectionBrush, rectangle);
            e.Graphics.DrawRectangle(selectionPen, rectangle);
        }
    }

    private Size PreviewResolution() => previewResolution.Text switch
    {
        "2560 x 1440" => new Size(2560, 1440),
        "3840 x 2160" => new Size(3840, 2160),
        "1280 x 720" => new Size(1280, 720),
        _ => new Size(1920, 1080)
    };

    private RectangleF PreviewViewport(Size clientSize)
    {
        var resolution = PreviewResolution();
        var availableWidth = Math.Max(1, clientSize.Width - 24);
        var availableHeight = Math.Max(1, clientSize.Height - 24);
        var scaleToFit = Math.Min(availableWidth / (double)resolution.Width,
            availableHeight / (double)resolution.Height);
        var width = (float)(resolution.Width * scaleToFit);
        var height = (float)(resolution.Height * scaleToFit);
        return new RectangleF((clientSize.Width - width) / 2,
            (clientSize.Height - height) / 2, width, height);
    }

    private void PaintFullScreenPreview(Graphics graphics, Size clientSize)
    {
        graphics.Clear(darkMode.Checked ? Color.FromArgb(24, 26, 30) : Color.FromArgb(54, 62, 70));
        var viewport = PreviewViewport(clientSize);
        using var screenBrush = new SolidBrush(Color.FromArgb(20, 24, 28));
        using var horizonBrush = new SolidBrush(Color.FromArgb(35, 42, 46));
        using var borderPen = new Pen(Color.FromArgb(100, 150, 160, 170), 1);
        graphics.FillRectangle(screenBrush, viewport);
        graphics.FillRectangle(horizonBrush, viewport.X, viewport.Y + viewport.Height * .55f,
            viewport.Width, viewport.Height * .45f);
        graphics.DrawRectangle(borderPen, viewport);

        var drawItems = renderedItems;
        var pixelsPerUnit = (float)(viewport.Height * StandardHudHeightFraction / 100);
        var center = new PointF(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);
        using var pen = new Pen(ColorValue(), Math.Max(.1f,
            viewport.Height / PreviewResolution().Height * (float)lineWidth.Value));
        foreach (var item in drawItems)
        {
            var a = new PointF(center.X + (float)item.X1 * pixelsPerUnit,
                center.Y + (float)item.Y1 * pixelsPerUnit);
            if (item.Kind == SightItemKind.Line)
            {
                var b = new PointF(center.X + (float)item.X2 * pixelsPerUnit,
                    center.Y + (float)item.Y2 * pixelsPerUnit);
                graphics.DrawLine(pen, a, b);
            }
            else if (item.Kind is SightItemKind.Ellipse or SightItemKind.FilledEllipse)
            {
                var rx = (float)Math.Abs(item.X2) * pixelsPerUnit;
                var ry = (float)Math.Abs(item.Y2) * pixelsPerUnit;
                if (item.Kind == SightItemKind.FilledEllipse)
                {
                    using var brush = new SolidBrush(ColorValue());
                    graphics.FillEllipse(brush, a.X - rx, a.Y - ry, rx * 2, ry * 2);
                }
                else
                    graphics.DrawEllipse(pen, a.X - rx, a.Y - ry, rx * 2, ry * 2);
            }
            else
            {
                var b = new PointF(a.X + (float)item.X2 * pixelsPerUnit,
                    a.Y + (float)item.Y2 * pixelsPerUnit);
                graphics.DrawRectangle(pen, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                    Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            }
        }
    }

    private void ShowFullScreenPreview()
    {
        var preview = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            WindowState = FormWindowState.Maximized,
            StartPosition = FormStartPosition.Manual,
            Bounds = Screen.FromControl(this).Bounds,
            BackColor = Color.Black,
            KeyPreview = true,
            ShowInTaskbar = false
        };
        var close = new Button
        {
            Text = "X",
            Size = new Size(42, 34),
            Location = new Point(preview.ClientSize.Width - 50, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(45, 48, 54)
        };
        close.Click += (_, _) => preview.Close();
        preview.Controls.Add(close);
        preview.Paint += (_, e) => PaintFullScreenPreview(e.Graphics, preview.ClientSize);
        preview.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) preview.Close();
        };
        preview.Show(this);
        preview.Activate();
    }

    private double PredictedPixelExtent() =>
        CurrentOutputExtent() * PreviewResolution().Height * StandardHudHeightFraction / 100;

    private void UpdatePreviewPixelSize()
    {
        var screenPercent = CurrentOutputExtent() * StandardHudHeightFraction;
        previewPixelSize.Text = $"Sight: {PredictedPixelExtent():0.0} px | {screenPercent:0.00}% screen height";
    }

    private void PaintOriginMarker(Graphics graphics)
    {
        var origin = ToScreen((double)originX.Value * OutputScale, (double)originY.Value * OutputScale);
        using var marker = new Pen(Color.FromArgb(150, 255, 212, 95), 1);
        using var labelBrush = new SolidBrush(Color.FromArgb(190, 255, 212, 95));
        graphics.DrawLine(marker, origin.X - 18, origin.Y, origin.X + 18, origin.Y);
        graphics.DrawLine(marker, origin.X, origin.Y - 18, origin.X, origin.Y + 18);
        graphics.DrawEllipse(marker, origin.X - 4, origin.Y - 4, 8, 8);
        graphics.DrawString("CCIP", Font, labelBrush, origin.X + 7, origin.Y - 17);
    }

    private void PaintGameCanvasBoundary(Graphics graphics)
    {
        var halfReference = EditorStateRules.RelativeSizeReferenceEnvelope / 2;
        var a = ToScreen(-halfReference, -halfReference);
        var b = ToScreen(halfReference, halfReference);
        using var boundaryPen = new Pen(Color.FromArgb(110, 255, 255, 255), 1)
            { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        graphics.DrawRectangle(boundaryPen, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }

    private void FitGameCanvasView()
    {
        var available = Math.Max(100, Math.Min(canvas.ClientSize.Width, canvas.ClientSize.Height) * .8);
        var extent = Math.Max(1, CurrentOutputExtent());
        var targetZoom = available / extent / .6 * 100;
        loading = true;
        zoom.Value = Clamp(zoom, targetZoom);
        loading = false;
        pan = PointF.Empty;
    }

    private void PaintGrid(Graphics graphics)
    {
        if (!snap.Checked) return;
        var spacing = GridOutputSpacing * Pixels;
        if (!double.IsFinite(spacing) || spacing <= 0) return;

        var displayedSpacing = spacing;
        if (displayedSpacing < 8)
            displayedSpacing *= Math.Ceiling(8 / displayedSpacing);
        if (displayedSpacing > 100_000) return;

        var centerX = canvas.ClientSize.Width / 2f + pan.X;
        var centerY = canvas.ClientSize.Height / 2f + pan.Y;
        var firstX = (float)(centerX - Math.Floor(centerX / displayedSpacing) * displayedSpacing);
        var firstY = (float)(centerY - Math.Floor(centerY / displayedSpacing) * displayedSpacing);
        using var gridPen = new Pen(Color.FromArgb(42, 255, 255, 255), 1);
        for (var x = firstX; x <= canvas.ClientSize.Width; x += (float)displayedSpacing)
            graphics.DrawLine(gridPen, x, 0, x, canvas.ClientSize.Height);
        for (var y = firstY; y <= canvas.ClientSize.Height; y += (float)displayedSpacing)
            graphics.DrawLine(gridPen, 0, y, canvas.ClientSize.Width, y);
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
        using var pen = new Pen(Color.White, (float)lineWidth.Value)
            { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
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
        if (source is null)
            return renderedItems;
        var result = new List<SightItem>();
        foreach (System.Text.RegularExpressions.Match m in SightLogic.CommandRegex().Matches(source))
        {
            var values = System.Text.RegularExpressions.Regex.Matches(m.Groups[2].Value, @"[-+]?(?:\d+\.\d+|\d+|\.\d+)(?:[eE][-+]?\d+)?")
                .Select(n => double.Parse(n.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (values.Length < 4) continue;
            var kind = m.Groups[1].Value switch
            {
                "VECTOR_LINE" => SightItemKind.Line,
                "VECTOR_ELLIPSE" => SightItemKind.Ellipse,
                "VECTOR_FILLED_ELLIPSE" => SightItemKind.FilledEllipse,
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
            var first = items.Count;
            items.AddRange(SightLogic.ImportSvg(dialog.FileName));
            selectedIndices.Clear();
            for (var i = first; i < items.Count; i++) selectedIndices.Add(i);
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
        var targets = ValidSelection();
        if (targets.Count == 0) return;
        foreach (var index in targets)
        {
            var item = items[index];
            items[index] = item.Kind == SightItemKind.Line
                ? item with { X1 = item.X1 + dx, Y1 = item.Y1 + dy, X2 = item.X2 + dx, Y2 = item.Y2 + dy }
                : item with { X1 = item.X1 + dx, Y1 = item.Y1 + dy };
        }
        Changed();
    }

    private void MirrorSelected()
    {
        var targets = ValidSelection();
        if (targets.Count == 0) return;
        var axis = (double)originX.Value;
        var first = items.Count;
        foreach (var index in targets)
        {
            var item = items[index];
            items.Add(item.Kind switch
            {
                SightItemKind.Line => item with
                {
                    X1 = axis * 2 - item.X1,
                    X2 = axis * 2 - item.X2
                },
                SightItemKind.Rectangle => item with
                {
                    X1 = axis * 2 - (item.X1 + item.X2)
                },
                _ => item with { X1 = axis * 2 - item.X1 }
            });
        }
        selectedIndices.Clear();
        for (var index = first; index < items.Count; index++)
            selectedIndices.Add(index);
        selected = items.Count - 1;
        shape.SelectedItem = "Custom";
        Changed();
    }

    private bool SelectAt(PointF point, bool additive = false)
    {
        var customScale = OutputScale;
        var tolerance = Math.Max(.02, 6 / (Pixels * customScale));
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (!HitTest(items[i], point, tolerance)) continue;
            if (!additive) selectedIndices.Clear();
            if (additive && selectedIndices.Contains(i))
                selectedIndices.Remove(i);
            else
                selectedIndices.Add(i);
            selected = i;
            if (!selectedIndices.Contains(selected))
                selected = selectedIndices.Count == 0 ? -1 : selectedIndices.Max();
            SyncValueFields();
            canvas.Invalidate();
            return true;
        }
        return false;
    }

    private void SelectInRectangle(PointF a, PointF b, bool additive)
    {
        if (!additive) selectedIndices.Clear();
        var area = RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        for (var i = 0; i < items.Count; i++)
        {
            var bounds = items[i].Bounds;
            var selectableBounds = RectangleF.FromLTRB(bounds.Left, bounds.Top,
                Math.Max(bounds.Right, bounds.Left + .000001f),
                Math.Max(bounds.Bottom, bounds.Top + .000001f));
            if (area.IntersectsWith(selectableBounds) || area.Contains(items[i].Center))
                selectedIndices.Add(i);
        }
        selected = selectedIndices.Count == 0 ? -1 : selectedIndices.Max();
        SyncValueFields();
        canvas.Invalidate();
    }

    private List<int> ValidSelection() =>
        selectedIndices.Where(index => index >= 0 && index < items.Count).OrderBy(index => index).ToList();

    private void DeleteSelected()
    {
        var targets = ValidSelection();
        if (targets.Count == 0) return;
        foreach (var index in targets.OrderByDescending(index => index)) items.RemoveAt(index);
        selectedIndices.Clear();
        selected = Math.Min(targets[0], items.Count - 1);
        if (selected >= 0) selectedIndices.Add(selected);
        Changed();
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
        if (item.Kind is SightItemKind.Ellipse or SightItemKind.FilledEllipse)
        {
            var rx = Math.Abs(item.X2);
            var ry = Math.Abs(item.Y2);
            var dx = point.X - item.X1;
            var dy = point.Y - item.Y1;
            if (rx <= tolerance || ry <= tolerance)
                return Math.Sqrt(dx * dx + dy * dy) <= Math.Max(rx, ry) + tolerance;
            var normalizedRadius = Math.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
            return item.Kind == SightItemKind.FilledEllipse
                ? normalizedRadius <= 1 + tolerance / Math.Min(rx, ry)
                : Math.Abs(normalizedRadius - 1) <= tolerance / Math.Min(rx, ry);
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
        valueCLabel.Text = i.Kind switch { SightItemKind.Line => "X2", SightItemKind.Ellipse or SightItemKind.FilledEllipse => "R X", _ => "W" };
        valueDLabel.Text = i.Kind switch { SightItemKind.Line => "Y2", SightItemKind.Ellipse or SightItemKind.FilledEllipse => "R Y", _ => "H" };
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
            current.Kind is SightItemKind.Ellipse or SightItemKind.FilledEllipse ? Math.Abs((double)valueC.Value) : (double)valueC.Value,
            current.Kind is SightItemKind.Ellipse or SightItemKind.FilledEllipse ? Math.Abs((double)valueD.Value) : (double)valueD.Value);
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

    private List<SightItem> CurrentSourceItems() => shape.Text switch
    {
        "Dot" => SightLogic.FilledDot(0, 0, Math.Max(8, (double)size.Value) / 2),
        "Circle" or "Cross" or "Box" or "T Sight" =>
            SightLogic.Preset(shape.Text, (double)size.Value, (double)gap.Value),
        _ => [.. items]
    };

    private static double GeometryExtent(IEnumerable<SightItem> geometry)
    {
        var list = geometry.ToList();
        if (list.Count == 0) return 0;
        var left = list.Min(item => item.Bounds.Left);
        var right = list.Max(item => item.Bounds.Right);
        var top = list.Min(item => item.Bounds.Top);
        var bottom = list.Max(item => item.Bounds.Bottom);
        return Math.Max(right - left, bottom - top);
    }

    private double CurrentOutputExtent() => GeometryExtent(CurrentSourceItems()) * OutputScale;

    private static double TargetExtent(string profile) => profile switch
    {
        "Small" => 250,
        "Medium" => 500,
        "Large" => 750,
        "Extra Large" => 1_000,
        _ => 0
    };

    private void ApplyTargetSize(string profile)
    {
        var extent = GeometryExtent(CurrentSourceItems());
        var target = TargetExtent(profile);
        if (extent <= .000001 || target <= 0) return;
        var percent = target / EditorStateRules.RelativeSizeReferenceEnvelope * 100;
        loading = true;
        scale.Value = Clamp(scale, percent);
        sizeProfile.SelectedItem = profile;
        loading = false;
        FitGameCanvasView();
        Changed();
    }

    private List<SightItem> MatchPartToCurrentDesign(List<SightItem> added)
    {
        var currentExtent = GeometryExtent(CurrentSourceItems());
        var addedExtent = GeometryExtent(added);
        if (currentExtent <= .000001 || addedExtent <= .000001) return added;
        var factor = currentExtent / addedExtent;
        return added.Select(item => item.Scale(factor)).ToList();
    }

    private void UpdateSizeAwareness()
    {
        var extent = CurrentOutputExtent();
        if (extent <= .000001)
        {
            sizeAwareness.Text = "No visible sight geometry.";
            sizeAwareness.ForeColor = darkMode.Checked ? Color.Salmon : Color.DarkRed;
            return;
        }
        sizeAwareness.Text = $"Relative size: {scale.Value:0.##}% | {extent * StandardHudHeightFraction:0.00}% screen.";
        sizeAwareness.ForeColor = darkMode.Checked ? Color.LightGreen : Color.DarkGreen;
    }

    private void RefreshSavedDesigns(string? select = null)
    {
        Directory.CreateDirectory(designsPath);
        var names = Directory.EnumerateFiles(designsPath, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        savedDesigns.Items.Clear();
        savedDesigns.Items.AddRange(names);
        if (select is not null && savedDesigns.Items.Contains(select))
            savedDesigns.SelectedItem = select;
        else if (savedDesigns.Items.Count > 0)
            savedDesigns.SelectedIndex = 0;
    }

    private string? NamedDesignPath()
    {
        var name = saveName.Text.Trim();
        if (name.Length == 0) name = savedDesigns.Text.Trim();
        if (name.Length == 0) return null;
        foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
        return Path.Combine(designsPath, name + ".json");
    }

    private void SaveNamedDesign()
    {
        var path = NamedDesignPath();
        if (path is null)
        {
            MessageBox.Show(this, "Enter a design name first.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Directory.CreateDirectory(designsPath);
        File.WriteAllText(path, JsonSerializer.Serialize(CaptureState(), new JsonSerializerOptions { WriteIndented = true }));
        var name = Path.GetFileNameWithoutExtension(path);
        RefreshSavedDesigns(name);
        saveName.Text = name;
        status.Text = $"Saved design: {name}";
    }

    private void LoadNamedDesign()
    {
        if (string.IsNullOrWhiteSpace(savedDesigns.Text)) return;
        var path = Path.Combine(designsPath, savedDesigns.Text + ".json");
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ??
            throw new InvalidDataException("The saved design is empty.");
        Restore(state);
        Record();
        saveName.Text = savedDesigns.Text;
        status.Text = $"Loaded design: {savedDesigns.Text}";
    }

    private void DeleteNamedDesign()
    {
        if (string.IsNullOrWhiteSpace(savedDesigns.Text)) return;
        var name = savedDesigns.Text;
        if (MessageBox.Show(this, $"Delete the saved design \"{name}\"?", Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        File.Delete(Path.Combine(designsPath, name + ".json"));
        RefreshSavedDesigns();
        status.Text = $"Deleted design: {name}";
    }

    private void ApplyTheme()
    {
        var dark = darkMode.Checked;
        var background = dark ? Color.FromArgb(30, 32, 36) : SystemColors.Control;
        var surface = dark ? Color.FromArgb(43, 46, 52) : SystemColors.Window;
        var foreground = dark ? Color.Gainsboro : SystemColors.ControlText;
        BackColor = background;
        ForeColor = foreground;

        void Theme(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control == canvas) continue;
                control.ForeColor = foreground;
                control.BackColor = control is TextBoxBase or ComboBox or NumericUpDown
                    ? surface
                    : background;
                if (control is Button button)
                {
                    button.UseVisualStyleBackColor = !dark;
                    if (dark) button.BackColor = surface;
                }
                Theme(control);
            }
        }
        Theme(this);
        UpdateSizeAwareness();
        Invalidate(true);
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
        try
        {
            CreateInstallFiles(output.Text);
            status.Text = $"Built: {output.Text}";
            MessageBox.Show(this, $"Built install files in:\n\n{output.Text}", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            status.Text = "Build failed";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ChooseGameContent(object? sender, EventArgs e)
    {
        var detected = GameInstallService.DetectContentDirectory();
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select War Thunder's content folder",
            InitialDirectory = GameInstallService.IsWarThunderContentDirectory(gameContent.Text)
                ? gameContent.Text
                : detected ?? ""
        };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        gameContent.Text = dialog.SelectedPath;
        Changed();
    }

    private void InstallSight(object? sender, EventArgs e)
    {
        string? temporaryOutput = null;
        try
        {
            var content = RequireGameContentDirectory();
            temporaryOutput = Path.Combine(Path.GetTempPath(), "HeliSightBuilder",
                "install-" + Guid.NewGuid().ToString("N"));
            CreateInstallFiles(temporaryOutput);
            GameInstallService.Install(temporaryOutput, content);
            gameContent.Text = content;
            status.Text = $"Installed: {content}";
            MessageBox.Show(this,
                "The custom sight was installed. Restart War Thunder if it is currently running.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Changed();
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                "Windows blocked access to the War Thunder folder. Restart this application as administrator, then try again.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            status.Text = "Install failed";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (temporaryOutput is not null && Directory.Exists(temporaryOutput))
            {
                try { Directory.Delete(temporaryOutput, true); }
                catch { }
            }
        }
    }

    private void RestoreOriginal(object? sender, EventArgs e)
    {
        try
        {
            var content = RequireGameContentDirectory();
            if (!GameInstallService.Restore(content))
            {
                var answer = MessageBox.Show(this,
                    "No backup from this application was found. Remove the active pkg_user sight files anyway?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
                GameInstallService.RemoveUntracked(content);
            }
            status.Text = "Original sight restored";
            MessageBox.Show(this,
                "The custom override was removed. Restart War Thunder if it is currently running.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                "Windows blocked access to the War Thunder folder. Restart this application as administrator, then try again.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            status.Text = "Restore failed";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string RequireGameContentDirectory()
    {
        if (GameInstallService.IsWarThunderContentDirectory(gameContent.Text))
            return Path.GetFullPath(gameContent.Text);
        var detected = GameInstallService.DetectContentDirectory();
        if (detected is not null)
        {
            gameContent.Text = detected;
            return detected;
        }
        throw new DirectoryNotFoundException(
            "War Thunder was not detected. Use Find / Choose and select the game's content folder.");
    }

    private void CreateInstallFiles(string destination)
    {
        Sync();
        if (string.IsNullOrWhiteSpace(generatedCommands))
            throw new InvalidDataException("The sight has no valid vector commands.");

        string? temporarySource = null;
        try
        {
            temporarySource = Path.Combine(Path.GetTempPath(), "HeliSightBuilder",
                "resources-" + Guid.NewGuid().ToString("N"));
            EmbeddedResources.ExtractTo(temporarySource);
            var source = Path.Combine(temporarySource, "source");
            var template = Path.Combine(temporarySource, "template");
            var air = Path.Combine(source, "reactivegui", "airHudElems.nut");
            var mode0 = Shift(generatedCommands, 0, 100);
            var mode1 = Shift(generatedCommands, 0, 0);
            var updatedHud = SightLogic.ReplaceSightFunction(
                File.ReadAllText(air), mode0, mode1, ColorValue(), (double)lineWidth.Value);
            File.WriteAllText(air, updatedHud, new UTF8Encoding(false));

            var packageDirectory = Path.Combine(destination, "pkg_user");
            Directory.CreateDirectory(packageDirectory);
            VromfsPackage.Build(Path.Combine(template, "pkg_user", "base.vromfs.bin"),
                source, Path.Combine(packageDirectory, "base.vromfs.bin"));
            File.Copy(Path.Combine(template, "pkg_user.rq2"),
                Path.Combine(destination, "pkg_user.rq2"), true);
            File.Copy(Path.Combine(template, "pkg_user.ver"),
                Path.Combine(destination, "pkg_user.ver"), true);
        }
        finally
        {
            if (temporarySource is not null && Directory.Exists(temporarySource))
            {
                try { Directory.Delete(temporarySource, true); }
                catch { }
            }
        }
    }

    private string Shift(string text, double targetX, double targetY)
    {
        var parsed = ParseCommands(text);
        if (parsed.Count == 0) return text;
        var originOutX = (double)originX.Value * OutputScale;
        var originOutY = (double)originY.Value * OutputScale;
        var dx = targetX - originOutX;
        var dy = targetY - originOutY;
        return SightLogic.Commands(parsed.Select(item => item.Kind == SightItemKind.Line
            ? item with { X1 = item.X1 + dx, Y1 = item.Y1 + dy, X2 = item.X2 + dx, Y2 = item.Y2 + dy }
            : item with { X1 = item.X1 + dx, Y1 = item.Y1 + dy }));
    }

    private void UpdateDiagnostics() =>
        diagnostics.Text = $"{renderedItems.Count:N0} vector commands | " +
            $"{Encoding.UTF8.GetByteCount(generatedCommands):N0} command bytes | package growth supported";

    private void ScheduleAutosave()
    {
        if (loading) return;
        autosaveTimer.Stop();
        autosaveTimer.Start();
    }

    private void SaveAutosave()
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(autosavePath)!);
            var state = EditorStateRules.Sanitize(CaptureState(), out _);
            temporary = autosavePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, autosavePath, true);
        }
        catch
        {
            // Autosave failure must not interrupt editing.
            if (temporary is not null && File.Exists(temporary))
            {
                try { File.Delete(temporary); }
                catch { }
            }
        }
    }

    private void LoadAutosave()
    {
        try
        {
            if (!File.Exists(autosavePath)) return;
            var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(autosavePath));
            if (state is null) return;
            var sanitized = EditorStateRules.Sanitize(state, out var recovered);
            if (recovered) QuarantineAutosave();
            Restore(sanitized);
            if (recovered)
                status.Text = "A corrupted autosave was quarantined and unsafe geometry was reset.";
        }
        catch
        {
            QuarantineAutosave();
            status.Text = "The previous autosave could not be read and was quarantined.";
        }
    }

    private void QuarantineAutosave()
    {
        if (!File.Exists(autosavePath)) return;
        try
        {
            var quarantine = autosavePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(autosavePath, quarantine, true);
        }
        catch
        {
            // A locked autosave can still be replaced by the next successful save.
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

    private double OutputScale
    {
        get
        {
            var extent = GeometryExtent(CurrentSourceItems());
            if (extent <= .000001) return 1;
            return (double)scale.Value / 100 *
                EditorStateRules.RelativeSizeReferenceEnvelope / extent;
        }
    }
    private double Pixels => .6 * (double)zoom.Value / 100;
    private PointF ToWorld(Point p)
    {
        var x = (p.X - canvas.ClientSize.Width / 2 - pan.X) / Pixels;
        var y = (p.Y - canvas.ClientSize.Height / 2 - pan.Y) / Pixels;
        return new PointF((float)SafeCoordinate(x), (float)SafeCoordinate(y));
    }
    private PointF ToDesign(Point p, bool applySnap = true)
    {
        var outputPoint = ToWorld(p);
        var customScale = OutputScale;
        var x = SafeCoordinate(outputPoint.X / customScale);
        var y = SafeCoordinate(outputPoint.Y / customScale);
        return applySnap
            ? new PointF((float)Snap(x), (float)Snap(y))
            : new PointF((float)x, (float)y);
    }
    private PointF ToScreen(double x, double y) => new(
        (float)(canvas.ClientSize.Width / 2 + pan.X + x * Pixels),
        (float)(canvas.ClientSize.Height / 2 + pan.Y + y * Pixels));
    private double Snap(double value)
    {
        if (!double.IsFinite(value)) return 0;
        if (!snap.Checked) return SafeCoordinate(value);
        var outputValue = value * OutputScale;
        return SafeCoordinate(Math.Round(outputValue / GridOutputSpacing) * GridOutputSpacing / OutputScale);
    }
    private double GridOutputSpacing => Math.Max(EditorStateRules.MinimumGrid, (double)grid.Value) * 10;
    private void ChangeZoom(decimal multiplier, Point? anchor = null)
    {
        var anchorPoint = anchor ?? new Point(canvas.ClientSize.Width / 2, canvas.ClientSize.Height / 2);
        var before = ToWorld(anchorPoint);
        zoom.Value = Clamp(zoom, (double)(zoom.Value * multiplier));
        var after = ToWorld(anchorPoint);
        pan = new PointF(
            (float)SafeCoordinate(pan.X + (after.X - before.X) * Pixels),
            (float)SafeCoordinate(pan.Y + (after.Y - before.Y) * Pixels));
        canvas.Invalidate();
    }
    private void UpdateCanvasCursor() => canvas.Cursor = tool.Text == "Pan" ? Cursors.Hand :
        tool.Text == "Select" ? Cursors.Default : Cursors.Cross;
    private static decimal Clamp(NumericUpDown control, double value)
    {
        if (!double.IsFinite(value)) return Math.Max(control.Minimum, Math.Min(control.Maximum, control.Value));
        var decimalValue = (decimal)Math.Clamp(value, (double)control.Minimum, (double)control.Maximum);
        return Math.Max(control.Minimum, Math.Min(control.Maximum, decimalValue));
    }

    private static double SafeCoordinate(double value) =>
        Math.Clamp(EditorStateRules.SafeCoordinate(value),
            -EditorStateRules.MaxCoordinate, EditorStateRules.MaxCoordinate);
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

}

public sealed record AppState(string Shape, double Size, double Gap, double Scale, string Color,
    double OriginX, double OriginY, List<SightItem> Items, int Selected, string Output,
    string Tool, string Part, double Zoom, bool Snap, double Grid, double Nudge,
    string? GameContent = null, int ScaleCalibrationVersion = 0, bool DarkMode = false,
    string SizeProfile = "Custom", double LineWidth = 2);

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
        var keep = stack.Reverse().Skip(1).ToArray(); stack.Clear(); foreach (var item in keep) stack.Push(item);
    }
}
