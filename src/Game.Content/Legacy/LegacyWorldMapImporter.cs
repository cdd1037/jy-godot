using System.Buffers.Binary;
using JinYong.Core.Maps;

namespace JinYong.Content.Legacy;

public static class LegacyWorldMapImporter
{
    public static readonly IReadOnlyList<string> LayerFiles = Array.AsReadOnly(new[]
        { "EARTH.002", "SURFACE.002", "BUILDING.002", "BUILDX.002", "BUILDY.002" });
    public static WorldMapDefinition Read(Func<string, byte[]> read)
    {
        var layers = new short[WorldMapDefinition.LayerCount][];
        for (var layer = 0; layer < layers.Length; layer++)
        {
            var bytes = read(LayerFiles[layer]);
            if (bytes.Length != WorldMapDefinition.CellCount * 2)
                throw new InvalidDataException($"{LayerFiles[layer]} must be a 480×480 signed int16le layer.");
            layers[layer] = new short[WorldMapDefinition.CellCount];
            for (var cell = 0; cell < layers[layer].Length; cell++)
                layers[layer][cell] = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(cell * 2));
        }
        return new(layers);
    }
    public static WorldTravelSnapshot ReadState(ReadOnlySpan<byte> header)
    {
        if (header.Length != 836) throw new InvalidDataException("RANGER header must contain 836 bytes.");
        var words = new int[12];
        for (var word = 0; word < words.Length; word++) words[word] = BinaryPrimitives.ReadInt16LittleEndian(header.Slice(word * 2, 2));
        return new(new(words[2], words[3]), (Direction)Math.Clamp(words[6], 0, 3), words[0] != 0,
            new(words[7], words[8]), new(words[9], words[10]), (Direction)Math.Clamp(words[11], 0, 3));
    }
}
