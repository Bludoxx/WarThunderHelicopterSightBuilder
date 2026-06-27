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

Run("Large command stress", () =>
{
    var items = Enumerable.Range(0, 10000)
        .Select(i => new SightItem(SightItemKind.Line, i, -i, i + .25, -i - .25)).ToArray();
    var timer = Stopwatch.StartNew();
    var commands = SightLogic.Commands(items);
    Require(SightLogic.CommandRegex().Matches(commands).Count == 10000, "Command count changed.");
    Require(timer.Elapsed < TimeSpan.FromSeconds(10), $"Generation took {timer.Elapsed}.");
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
    var result = SightLogic.ReplaceSightFunction(air, command, command, Color.Cyan);
    Require(result.Contains("Color(0, 255, 255, 255)", StringComparison.Ordinal), "Color was not replaced.");
    Require(result.Contains("function helicopterRocketSightMode", StringComparison.Ordinal), "Sight function missing.");
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
    var raw = File.ReadAllBytes(path);
    Require(Encoding.ASCII.GetString(raw, 0, 4) == "VRFs", "Bad package magic.");
    var unpacked = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8, 4)));
    var packRaw = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(12, 4));
    var packing = checked((int)(packRaw >> 26));
    var packedSize = checked((int)(packRaw & 0x03FFFFFF));
    var packed = raw.AsSpan(16, packedSize).ToArray();
    Xor(packed);
    byte[] inner;
    using (var decompressor = new Decompressor()) inner = decompressor.Unwrap(packed).ToArray();
    Require(inner.Length == unpacked, "Unpacked size mismatch.");
    var names = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(4, 4)));
    var info = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(inner.AsSpan(20, 4)));
    Require(names == info, "Package tables disagree.");
    return (packing, inner.Length, names);
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
