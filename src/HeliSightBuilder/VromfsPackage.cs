using System.Buffers.Binary;
using ZstdSharp;

namespace HeliSightBuilder.Native;

public static class VromfsPackage
{
    private static readonly uint[] XorPattern = [0xAA55AA55, 0xF00FF00F, 0xAA55AA55, 0x12481248];
    private const uint SizeMask = 0x03FFFFFF;

    private sealed record Entry(string Name, int Offset, int Size, int TablePosition);

    public static void Build(string templatePath, string sourceDirectory, string outputPath)
    {
        var raw = File.ReadAllBytes(templatePath);
        if (raw.Length < 16 || raw[0] != (byte)'V' || raw[1] != (byte)'R' || raw[2] != (byte)'F' || raw[3] != (byte)'s')
            throw new InvalidDataException("The template is not a War Thunder VROMFS package.");

        var unpackedSize = checked((int)ReadU32(raw, 8));
        var packRaw = ReadU32(raw, 12);
        var packing = packRaw >> 26;
        var packedSize = checked((int)(packRaw & SizeMask));
        if (packing != 16) throw new InvalidDataException($"Unsupported VROMFS packing type: {packing}.");

        var packed = raw.AsSpan(16, packedSize).ToArray();
        XorObfuscate(packed);
        byte[] inner;
        using (var decompressor = new Decompressor())
            inner = decompressor.Unwrap(packed).ToArray();
        if (inner.Length != unpackedSize)
            throw new InvalidDataException($"Unexpected unpacked size: {inner.Length}, expected {unpackedSize}.");

        using var data = new MemoryStream(inner.Length + 64 * 1024);
        data.Write(inner);
        foreach (var entry in Entries(inner))
        {
            var candidate = Candidate(sourceDirectory, entry.Name);
            if (candidate is null) continue;
            var replacement = File.ReadAllBytes(candidate);
            if (replacement.Length <= entry.Size)
            {
                data.Position = entry.Offset;
                data.Write(replacement);
                for (var i = replacement.Length; i < entry.Size; i++) data.WriteByte(0x20);
            }
            else
            {
                var newOffset = checked((int)data.Length);
                data.Position = data.Length;
                data.Write(replacement);
                data.Position = entry.TablePosition;
                WriteU32(data, checked((uint)newOffset));
                WriteU32(data, checked((uint)replacement.Length));
            }
        }

        var rebuilt = data.ToArray();
        byte[] compressed;
        using (var compressor = new Compressor(19))
            compressed = compressor.Wrap(rebuilt).ToArray();
        XorObfuscate(compressed);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var output = File.Create(outputPath);
        output.Write("VRFs"u8);
        output.Write([0, 0, (byte)'P', (byte)'C']);
        WriteU32(output, checked((uint)rebuilt.Length));
        WriteU32(output, (16u << 26) | checked((uint)compressed.Length));
        output.Write(compressed);
    }

    private static List<Entry> Entries(byte[] inner)
    {
        var namesOffset = checked((int)ReadU32(inner, 0));
        var namesCount = checked((int)ReadU32(inner, 4));
        var infoOffset = checked((int)ReadU32(inner, 16));
        var infoCount = checked((int)ReadU32(inner, 20));
        if (namesCount != infoCount) throw new InvalidDataException("The package tables do not match.");

        var result = new List<Entry>();
        for (var i = 0; i < namesCount; i++)
        {
            var nameOffset = checked((int)ReadU32(inner, namesOffset + i * 8));
            var end = Array.IndexOf(inner, (byte)0, nameOffset);
            var name = System.Text.Encoding.UTF8.GetString(inner, nameOffset, end - nameOffset);
            var table = infoOffset + i * 16;
            if (name is "version" or "nm" || name.EndsWith("dict", StringComparison.Ordinal)) continue;
            result.Add(new(name, checked((int)ReadU32(inner, table)), checked((int)ReadU32(inner, table + 4)), table));
        }
        return result;
    }

    private static string? Candidate(string sourceDirectory, string name)
    {
        var direct = Path.Combine(sourceDirectory, name.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(direct)) return direct;
        var alias = name switch
        {
            "reactivegui/airhudelems.nut" => "reactivegui/airHudElems.nut",
            "ui/gameuiskin/ccip_rocket_sight.svg" => "gameuiskin/ccip_rocket_sight.svg",
            "ui/gameuiskin/rocket_sight.svg" => "gameuiskin/rocket_sight.svg",
            _ => null
        };
        if (alias is null) return null;
        var alternative = Path.Combine(sourceDirectory, alias.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(alternative) ? alternative : null;
    }

    private static uint ReadU32(ReadOnlySpan<byte> bytes, int position) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position, 4));

    private static void WriteU32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void XorObfuscate(byte[] bytes)
    {
        if (bytes.Length < 16) return;
        XorBlock(bytes, 0, XorPattern);
        if (bytes.Length <= 32) return;
        var middle = (bytes.Length & 0x03FFFFFC) - 16;
        XorBlock(bytes, middle, XorPattern.Reverse().ToArray());
    }

    private static void XorBlock(byte[] bytes, int start, uint[] key)
    {
        for (var i = 0; i < 4; i++)
        {
            var position = start + i * 4;
            var value = ReadU32(bytes, position) ^ key[i];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position, 4), value);
        }
    }
}
