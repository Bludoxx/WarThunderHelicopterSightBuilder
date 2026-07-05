using System.Buffers.Binary;
using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using HeliSightBuilder.Native;
using ZstdSharp;

if (args.Length != 2)
    throw new ArgumentException("Expected source and template directories.");

var source = Path.GetFullPath(args[0]);
var template = Path.GetFullPath(args[1]);
var packageTemplate = Path.Combine(template, "pkg_user", "base.vromfs.bin");
var root = Path.Combine(Path.GetTempPath(), "HeliSightBuilderTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = new List<string>();

Run("Preset generation", () =>
{
    foreach (var name in new[] { "Dot", "Circle", "Cross", "Box", "T Sight" })
        Require(SightLogic.CommandRegex().Matches(SightLogic.Commands(SightLogic.Preset(name, 4.2, 1))).Count > 0, name);
    Require(SightLogic.Preset("Dot", 4.2, 1).Single().Kind == SightItemKind.FilledEllipse,
        "Dot preset should use one true filled ellipse.");
    Require(SightLogic.Preset("Circle", 4.2, 1).Single().Kind == SightItemKind.Ellipse,
        "Circle preset should remain an outlined ellipse.");
});

Run("Tiny and extreme geometry", () =>
{
    var commands = SightLogic.Commands([
        new(SightItemKind.Line, .0001, -.0001, .0002, -.0002),
        new(SightItemKind.Rectangle, -999999, 999999, 500000, -500000)
    ], 1);
    Require(commands.Contains("999999", StringComparison.Ordinal), "Extreme coordinate was clipped.");
    Require(SightLogic.CommandRegex().Matches(commands).Count == 2, "Tiny geometry was lost.");
});

Run("Invalid geometry rejection", () =>
{
    RequireThrows(() => SightLogic.Commands([
        new(SightItemKind.Line, double.PositiveInfinity, 0, 1, 1)
    ]));
    RequireThrows(() => SightLogic.Commands([
        new(SightItemKind.Line, EditorStateRules.MaxCoordinate + 1, 0, 1, 1)
    ]));
    RequireThrows(() => SightLogic.Commands([
        new(SightItemKind.Line, EditorStateRules.MaxCoordinate, 0, 1, 1)
    ], 2));
});

Run("Corrupted editor state recovery", () =>
{
    var corrupted = new AppState("Custom", 4.2, 1, 7003, "White", 0, 0,
        [new(SightItemKind.Line, -9_000_000_000, 1, 10_000_000_000, 2)],
        12, root, "Line", "Brackets", 0, true, 0, 0);
    var recovered = EditorStateRules.Sanitize(corrupted, out var changed);
    Require(changed, "Corrupted state was not detected.");
    Require(recovered.Zoom == EditorStateRules.DefaultZoom, "Unsafe zoom was retained.");
    Require(recovered.Grid == EditorStateRules.DefaultGrid, "Unsafe grid was retained.");
    Require(recovered.Nudge == EditorStateRules.DefaultNudge, "Unsafe nudge was retained.");
    Require(Math.Abs(recovered.Scale - 7003d / 70) < .001,
        "Legacy 7000% scale was not migrated.");
    Require(recovered.Items.Count == 1 && EditorStateRules.IsValidItem(recovered.Items[0]),
        "Corrupted geometry was retained.");

    var currentLargeScale = corrupted with
    {
        Scale = 7000,
        ScaleCalibrationVersion = 1,
        Items = [new(SightItemKind.Line, 0, 0, 1, 1)]
    };
    var current = EditorStateRules.Sanitize(currentLargeScale, out _);
    Require(Math.Abs(current.Scale - 7000) < .001 && current.ScaleCalibrationVersion == 4,
        "Existing custom visual size was not preserved by fixed-scale migration.");
    var versionThree = corrupted with
    {
        Scale = 50,
        ScaleCalibrationVersion = 3,
        Items = [new(SightItemKind.Line, -100, 0, 100, 0)]
    };
    var migrated = EditorStateRules.Sanitize(versionThree, out _);
    Require(Math.Abs(migrated.Scale - 250) < .001,
        "Version-three custom scale migration changed exported geometry.");
    Require(Math.Abs(EditorStateRules.OutputScale(100) - 1) < .001,
        "100% no longer maps to the calibrated in-game scale.");
});

Run("Large command stress", () =>
{
    var items = Enumerable.Range(0, 10000)
        .Select(i => new SightItem(SightItemKind.Line, i, -i, i + .25, -i - .25)).ToArray();
    var timer = Stopwatch.StartNew();
    var commands = SightLogic.Commands(items);
    Require(SightLogic.CommandRegex().Matches(commands).Count == 10000, "Command count changed.");
    Require(timer.Elapsed < TimeSpan.FromSeconds(10), $"Generation took {timer.Elapsed}.");
});

Run("Game line command compatibility", () =>
{
    var connected = Enumerable.Range(0, 20)
        .Select(index => new SightItem(SightItemKind.Line, index, 0, index + 1, 0));
    var compact = SightLogic.Compact(SightLogic.Commands(connected));
    var commands = SightLogic.CommandRegex().Matches(compact);
    Require(commands.Count == 4, $"Expected 4 bounded native polylines, found {commands.Count}.");
    Require(commands.All(match =>
        System.Text.RegularExpressions.Regex.Matches(match.Groups[2].Value,
            @"[-+]?(?:\d+\.\d+|\d+|\.\d+)(?:[eE][-+]?\d+)?").Count is >= 4 and <= 14),
        "A generated polyline exceeds the largest VECTOR_LINE used by the official HUD.");
    Require(compact.Contains(",20,0]", StringComparison.Ordinal),
        "Bounded polyline compilation lost the final point.");
});

Run("SVG import stress", () =>
{
    var svg = Path.Combine(root, "stress.svg");
    var body = string.Join("", Enumerable.Range(0, 2000)
        .Select(i => $"<line x1=\"{i}\" y1=\"{-i}\" x2=\"{i + 1}\" y2=\"{-i - 1}\"/>"));
    File.WriteAllText(svg, $"<svg xmlns=\"http://www.w3.org/2000/svg\">{body}</svg>");
    var imported = SightLogic.ImportSvg(svg);
    Require(imported.Count == 2000, $"Imported {imported.Count} shapes.");
});

Run("SVG path commands", () =>
{
    var svg = Path.Combine(root, "paths.svg");
    File.WriteAllText(svg, "<svg><path d=\"M 0 0 L 10 0 v 10 h -10 z\"/></svg>");
    var imported = SightLogic.ImportSvg(svg);
    Require(imported.Count == 4, $"Expected 4 path lines, found {imported.Count}.");
});

Run("SVG curves arcs and transforms", () =>
{
    var svg = Path.Combine(root, "curves.svg");
    File.WriteAllText(svg, """
        <svg viewBox="0 0 200 200">
          <g transform="translate(100 80) scale(.5 -1)">
            <path transform="rotate(15)" d="M 0 0
              C 20 -30 40 30 60 0 S 100 -30 120 0
              Q 100 40 80 20 T 40 20
              A 20 10 30 0 1 0 0 Z"/>
          </g>
        </svg>
        """);
    var imported = SightLogic.ImportSvg(svg);
    Require(imported.Count > 30, $"Curves were not flattened in enough detail ({imported.Count}).");
    Require(imported.All(EditorStateRules.IsValidItem), "Transformed curve produced invalid geometry.");
    var width = imported.Max(item => item.Bounds.Right) - imported.Min(item => item.Bounds.Left);
    var height = imported.Max(item => item.Bounds.Bottom) - imported.Min(item => item.Bounds.Top);
    Require(Math.Abs(Math.Max(width, height) - 32) < .01,
        "Transformed SVG was not normalized to the editor canvas.");
});

Run("Malformed SVG rejection", () =>
{
    var svg = Path.Combine(root, "bad.svg");
    File.WriteAllText(svg, "<svg><not-closed>");
    RequireThrows(() => SightLogic.ImportSvg(svg));
});

Run("HUD replacement and colors", () =>
{
    var air = File.ReadAllText(Path.Combine(source, "reactivegui", "airHudElems.nut"));
    var command = SightLogic.Commands(SightLogic.Preset("Dot", 4.2, 1));
    var result = SightLogic.ReplaceSightFunction(air, command, command, Color.Cyan, 3.5,
        true, 12.5, -7.25, 20);
    Require(result.Contains("Color(0, 255, 255, 255)", StringComparison.Ordinal), "Color was not replaced.");
    Require(result.Contains("fillColor = Color(0, 255, 255, 255)", StringComparison.Ordinal),
        "True dot fill was not enabled.");
    Require(result.Contains("correctRocketSightAspect(lines, width, height)", StringComparison.Ordinal),
        "Runtime HUD aspect correction is missing.");
    Require(result.Contains("rocketSightLineWidth = 3.5", StringComparison.Ordinal) &&
        result.Contains("lineWidth = hdpx(rocketSightLineWidth)", StringComparison.Ordinal),
        "Selected line width was not compiled into the HUD.");
    Require(result.Contains("rocketRangeEnabled = true", StringComparison.Ordinal) &&
        result.Contains("rocketRangeOffsetX = 12.5", StringComparison.Ordinal) &&
        result.Contains("rocketRangeOffsetY = -7.25", StringComparison.Ordinal) &&
        result.Contains("rocketRangeFontSize = 20", StringComparison.Ordinal),
        "Live range settings were not compiled into the HUD.");
    Require(result.Contains("string.format(\"%.3f km\", DistToTarget.get() / 1000.0)",
        StringComparison.Ordinal), "Live range does not use three-decimal kilometer formatting.");
    Require(result.Contains("watch = DistToTarget", StringComparison.Ordinal) &&
        result.Contains("watch = RocketSightMode", StringComparison.Ordinal),
        "Live range is not connected to the rocket CCIP distance watcher.");
    Require(!result.Contains("opacity = IsRangefinderEnabled.get() && RangefinderDist.get() > 0",
        StringComparison.Ordinal), "Live range is incorrectly gated by the laser rangefinder.");
    Require(result.Contains("function helicopterRocketSightMode", StringComparison.Ordinal), "Sight function missing.");
    Require(!result.Contains("function helicopterRocketSightFillMode", StringComparison.Ordinal),
        "Unsupported second HUD command layer was generated.");
    Require(!result.Contains("VECTOR_FILLED_ELLIPSE", StringComparison.Ordinal),
        "Editor-only filled command leaked into game HUD code.");
    var rocketAim = result[result.IndexOf("let helicopterRocketAim", StringComparison.Ordinal)..
        result.IndexOf("let turretAnglesAspect", StringComparison.Ordinal)];
    Require(rocketAim.Contains("return style.__merge({", StringComparison.Ordinal) &&
        rocketAim.Contains("children = rocketRangeEnabled ? rocketRangeText(height) : null",
            StringComparison.Ordinal) &&
        result.Contains("size = 0", StringComparison.Ordinal) &&
        System.Text.RegularExpressions.Regex.Matches(rocketAim, "ROBJ_VECTOR_CANVAS").Count == 1,
        "Live range changed the known-working single-canvas HUD structure or lost centered anchoring.");

    var outlineCircle = SightLogic.Commands([new(SightItemKind.Ellipse, 0, 0, 10, 10)]);
    var outlineResult = SightLogic.ReplaceSightFunction(air, outlineCircle, outlineCircle, Color.White);
    Require(outlineResult.Contains("fillColor = Color(255, 255, 255, 0)", StringComparison.Ordinal),
        "Circle-only sight was not kept transparent.");

    var mixed = SightLogic.Commands([
        new(SightItemKind.FilledEllipse, 0, 0, 2, 2),
        new(SightItemKind.Ellipse, 0, 0, 10, 10)
    ]);
    var mixedResult = SightLogic.ReplaceSightFunction(air, mixed, mixed, Color.White);
    Require(mixedResult.Contains("[VECTOR_ELLIPSE,0,0,2,2]", StringComparison.Ordinal) &&
        mixedResult.Contains("[VECTOR_LINE,10,0", StringComparison.Ordinal),
        "Mixed dot/circle sight was not compiled into filled-dot and outline-loop commands.");
});

Run("Install and restore workflow", () =>
{
    var game = Path.Combine(root, "War Thunder");
    var content = Path.Combine(game, "content");
    Directory.CreateDirectory(Path.Combine(game, "win64"));
    Directory.CreateDirectory(Path.Combine(content, "pkg_user"));
    File.WriteAllText(Path.Combine(game, "win64", "aces.exe"), "");
    File.WriteAllText(Path.Combine(content, "pkg_user", "base.vromfs.bin"), "previous package");
    File.WriteAllText(Path.Combine(content, "pkg_user.rq2"), "previous rq2");
    File.WriteAllText(Path.Combine(content, "pkg_user.ver"), "previous ver");

    var built = Path.Combine(root, "built-install");
    Directory.CreateDirectory(Path.Combine(built, "pkg_user"));
    File.WriteAllText(Path.Combine(built, "pkg_user", "base.vromfs.bin"), "new package");
    File.WriteAllText(Path.Combine(built, "pkg_user.rq2"), "new rq2");
    File.WriteAllText(Path.Combine(built, "pkg_user.ver"), "new ver");

    GameInstallService.Install(built, content);
    Require(File.ReadAllText(Path.Combine(content, "pkg_user", "base.vromfs.bin")) == "new package",
        "Install did not replace the package.");
    Require(GameInstallService.Restore(content), "Tracked installation was not restored.");
    Require(File.ReadAllText(Path.Combine(content, "pkg_user", "base.vromfs.bin")) == "previous package",
        "Previous package was not restored.");
    Require(File.ReadAllText(Path.Combine(content, "pkg_user.rq2")) == "previous rq2",
        "Previous rq2 file was not restored.");
});

Run("Embedded release resources", () =>
{
    var extracted = Path.Combine(root, "embedded-resources");
    EmbeddedResources.ExtractTo(extracted);
    Require(File.Exists(Path.Combine(extracted, "source", "reactivegui", "airHudElems.nut")),
        "Embedded HUD source was not extracted.");
    Require(File.Exists(Path.Combine(extracted, "template", "pkg_user", "base.vromfs.bin")),
        "Embedded package template was not extracted.");
    var sourceHud = Path.Combine(extracted, "source", "reactivegui", "airHudElems.nut");
    var dot = SightLogic.Commands(SightLogic.Preset("Dot", 4.2, 1));
    File.WriteAllText(sourceHud,
        SightLogic.ReplaceSightFunction(File.ReadAllText(sourceHud), dot, dot, Color.White, 4.25));
    var embeddedBuild = Path.Combine(root, "embedded-build.bin");
    VromfsPackage.Build(
        Path.Combine(extracted, "template", "pkg_user", "base.vromfs.bin"),
        Path.Combine(extracted, "source"),
        embeddedBuild);
    var packagedHud = Encoding.UTF8.GetString(
        ReadPackageEntry(embeddedBuild, "reactivegui/airhudelems.nut"));
    Require(packagedHud.Contains("correctRocketSightAspect(lines, width, height)",
        StringComparison.Ordinal), "Aspect correction was lost during VROMFS packaging.");
    Require(packagedHud.Contains("fillColor = Color(255, 255, 255, 255)",
        StringComparison.Ordinal), "Opaque dot fill was lost during VROMFS packaging.");
    Require(packagedHud.Contains("rocketSightLineWidth = 4.25", StringComparison.Ordinal),
        "Selected line width was lost during VROMFS packaging.");
    Require(!packagedHud.Contains("helicopterRocketSightFillMode", StringComparison.Ordinal),
        "Unsupported second HUD layer reached the VROMFS package.");
});

Run("Baseline VROMFS build", () =>
{
    var output = Path.Combine(root, "baseline.bin");
    VromfsPackage.Build(packageTemplate, source, output);
    var info = ReadPackage(output);
    Require(info.EntryCount == 6, $"Expected 6 entries, found {info.EntryCount}.");
    Require(info.Packing == 16, $"Unexpected packing {info.Packing}.");
});

Run("Oversized entry growth", () =>
{
    var expandedSource = Path.Combine(root, "expanded-source");
    CopyDirectory(source, expandedSource);
    var air = Path.Combine(expandedSource, "reactivegui", "airHudElems.nut");
    File.AppendAllText(air, "\n// stress-growth\n" + new string('x', 200000), Encoding.UTF8);
    var output = Path.Combine(root, "expanded.bin");
    VromfsPackage.Build(packageTemplate, expandedSource, output);
    var info = ReadPackage(output);
    Require(info.InnerSize > 300000, $"Expanded package inner size was only {info.InnerSize}.");
    Require(info.EntryCount == 6, "Expanded package table was damaged.");
});

Run("Repeated deterministic builds", () =>
{
    byte[]? expected = null;
    for (var i = 0; i < 20; i++)
    {
        var output = Path.Combine(root, $"repeat-{i}.bin");
        VromfsPackage.Build(packageTemplate, source, output);
        var bytes = File.ReadAllBytes(output);
        expected ??= bytes;
        Require(bytes.SequenceEqual(expected), $"Build {i} was not byte-identical.");
    }
});

Run("Invalid template rejection", () =>
{
    var invalid = Path.Combine(root, "invalid.bin");
    File.WriteAllText(invalid, "not a package");
    RequireThrows(() => VromfsPackage.Build(invalid, source, Path.Combine(root, "never.bin")));
});

Console.WriteLine();
Console.WriteLine($"PASS: {passed.Count} quality-control tests");
foreach (var name in passed) Console.WriteLine($"  {name}");
Directory.Delete(root, true);
return;

void Run(string name, Action action)
{
    action();
    passed.Add(name);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void RequireThrows(Action action)
{
    try { action(); }
    catch { return; }
    throw new InvalidOperationException("Expected an exception.");
}

static (int Packing, int InnerSize, int EntryCount) ReadPackage(string path)
{
    var (packing, unpacked, inner) = ReadInnerPackage(path);
    Require(inner.Length == unpacked, "Unpacked size mismatch.");
    var names = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(4, 4)));
    var info = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(20, 4)));
    Require(names == info, "Package tables disagree.");
    return (packing, inner.Length, names);
}

static byte[] ReadPackageEntry(string path, string requestedName)
{
    var (_, _, inner) = ReadInnerPackage(path);
    var namesOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(0, 4)));
    var namesCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(4, 4)));
    var infoOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(16, 4)));
    for (var index = 0; index < namesCount; index++)
    {
        var nameOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            inner.AsSpan(namesOffset + index * 8, 4)));
        var nameEnd = Array.IndexOf(inner, (byte)0, nameOffset);
        var name = Encoding.UTF8.GetString(inner, nameOffset, nameEnd - nameOffset);
        if (!name.Equals(requestedName, StringComparison.OrdinalIgnoreCase)) continue;
        var table = infoOffset + index * 16;
        var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(table, 4)));
        var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(table + 4, 4)));
        return inner.AsSpan(offset, size).ToArray();
    }
    throw new InvalidOperationException($"Package entry not found: {requestedName}");
}

static (int Packing, int UnpackedSize, byte[] Inner) ReadInnerPackage(string path)
{
    var raw = File.ReadAllBytes(path);
    Require(Encoding.ASCII.GetString(raw, 0, 4) == "VRFs", "Bad package magic.");
    var unpacked = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8, 4)));
    var packRaw = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(12, 4));
    var packing = checked((int)(packRaw >> 26));
    var packedSize = checked((int)(packRaw & 0x03FFFFFF));
    var packed = raw.AsSpan(16, packedSize).ToArray();
    Xor(packed);
    using var decompressor = new Decompressor();
    return (packing, unpacked, decompressor.Unwrap(packed).ToArray());
}

static void Xor(byte[] bytes)
{
    uint[] key = [0xAA55AA55, 0xF00FF00F, 0xAA55AA55, 0x12481248];
    Block(0, key);
    if (bytes.Length > 32) Block((bytes.Length & 0x03FFFFFC) - 16, key.Reverse().ToArray());
    return;
    void Block(int start, uint[] values)
    {
        for (var i = 0; i < 4; i++)
        {
            var position = start + i * 4;
            var value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position, 4)) ^ values[i];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position, 4), value);
        }
    }
}

static void CopyDirectory(string from, string to)
{
    foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
    {
        var target = Path.Combine(to, Path.GetRelativePath(from, file));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target);
    }
}
