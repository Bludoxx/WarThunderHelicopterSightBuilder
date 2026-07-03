using System.Reflection;

namespace HeliSightBuilder.Native;

public static class EmbeddedResources
{
    private static readonly (string Name, string RelativePath)[] Files =
    [
        ("HeliSightBuilder.Resources.source.gameuiskin.ccip_rocket_sight.svg",
            @"source\gameuiskin\ccip_rocket_sight.svg"),
        ("HeliSightBuilder.Resources.source.gameuiskin.rocket_sight.svg",
            @"source\gameuiskin\rocket_sight.svg"),
        ("HeliSightBuilder.Resources.source.reactivegui.airHudElems.nut",
            @"source\reactivegui\airHudElems.nut"),
        ("HeliSightBuilder.Resources.source.ui.gameuiskin.ccip_rocket_sight.svg",
            @"source\ui\gameuiskin\ccip_rocket_sight.svg"),
        ("HeliSightBuilder.Resources.source.ui.gameuiskin.rocket_sight.svg",
            @"source\ui\gameuiskin\rocket_sight.svg"),
        ("HeliSightBuilder.Resources.template.pkg_user.rq2",
            @"template\pkg_user.rq2"),
        ("HeliSightBuilder.Resources.template.pkg_user.ver",
            @"template\pkg_user.ver"),
        ("HeliSightBuilder.Resources.template.pkg_user.base.vromfs.bin",
            @"template\pkg_user\base.vromfs.bin")
    ];

    public static void ExtractTo(string destination)
    {
        var assembly = typeof(EmbeddedResources).Assembly;
        foreach (var (name, relativePath) in Files)
        {
            using var source = assembly.GetManifestResourceStream(name) ??
                throw new InvalidDataException($"Embedded resource is missing: {name}");
            var target = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = File.Create(target);
            source.CopyTo(output);
        }
    }
}
