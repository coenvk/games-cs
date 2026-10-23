using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace RpgArt;

/// <summary>A minimal PNG encoder: 8-bit RGBA, no filtering, one IDAT chunk.</summary>
internal static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(int width, int height, Rgba[] pixels)
    {
        using var png = new MemoryStream();
        png.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // colour type: RGBA
        WriteChunk(png, "IHDR", header);

        using var data = new MemoryStream();
        using (var zlib = new ZLibStream(data, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[1 + (width * 4)]; // filter type 0 (none) leads every row
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = pixels[(y * width) + x];
                    var i = 1 + (x * 4);
                    (row[i], row[i + 1], row[i + 2], row[i + 3]) = (p.R, p.G, p.B, p.A);
                }

                zlib.Write(row);
            }
        }

        WriteChunk(png, "IDAT", data.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(MemoryStream png, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        png.Write(number);
        png.Write(typeBytes);
        png.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(number, Crc(typeBytes, data));
        png.Write(number);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type.Concat(data))
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
