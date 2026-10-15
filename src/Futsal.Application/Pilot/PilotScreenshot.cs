using System.Buffers.Binary;
using System.IO.Compression;
namespace Futsal.Application.Pilot;

public static class PilotScreenshot
{
    public const int MaxBytes = 192 * 1024;
    // Only non-interlaced, 8-bit RGB/RGBA PNG. Ancillary chunks are stripped; SVG/HTML/JPEG are rejected.
    public static byte[] Scan(byte[] data)
    {
        if (data.Length > MaxBytes || data.Length < 57 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new ArgumentException("INVALID_SCREENSHOT");
        using var output = new MemoryStream(); output.Write(data, 0, 8); using var compressed = new MemoryStream(); int offset = 8, width = 0, height = 0, channels = 0; bool ended = false, idat = false;
        while (offset < data.Length)
        {
            if (offset + 12 > data.Length) throw new ArgumentException("INVALID_SCREENSHOT"); var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)); if (length > MaxBytes || offset + 12L + length > data.Length) throw new ArgumentException("INVALID_SCREENSHOT"); int count = (int)length; var kind = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4); var block = data.AsSpan(offset + 4, count + 4); uint crc = 0xffffffff; foreach (var b in block) { crc ^= b; for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1; }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 8 + count, 4))) throw new ArgumentException("INVALID_SCREENSHOT");
            if (offset == 8 && kind != "IHDR") throw new ArgumentException("INVALID_SCREENSHOT");
            if (kind == "IHDR") { if (offset != 8 || count != 13) throw new ArgumentException("INVALID_SCREENSHOT"); width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 8, 4)); height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset + 12, 4)); var color = data[offset + 17]; channels = color == 2 ? 3 : color == 6 ? 4 : 0; if (width < 1 || height < 1 || width > 1920 || height > 1920 || width * (long)height > 2073600 || data[offset + 16] != 8 || channels == 0 || data[offset + 18] != 0 || data[offset + 19] != 0 || data[offset + 20] != 0) throw new ArgumentException("INVALID_SCREENSHOT"); }
            else if (kind == "IDAT") { idat = true; compressed.Write(data, offset + 8, count); }
            else if (kind == "IEND") { if (count != 0 || !idat || offset + 12 != data.Length) throw new ArgumentException("INVALID_SCREENSHOT"); ended = true; }
            else if (char.IsUpper(kind[0])) throw new ArgumentException("INVALID_SCREENSHOT");
            if (kind is "IHDR" or "IDAT" or "IEND") output.Write(data, offset, count + 12); offset += count + 12;
        }
        if (!ended) throw new ArgumentException("INVALID_SCREENSHOT"); compressed.Position = 0;
        try { using var z = new ZLibStream(compressed, CompressionMode.Decompress); var row = new byte[width * channels + 1]; for (var y = 0; y < height; y++) { z.ReadExactly(row); if (row[0] > 4) throw new ArgumentException("INVALID_SCREENSHOT"); } if (z.ReadByte() != -1) throw new ArgumentException("INVALID_SCREENSHOT"); } catch (InvalidDataException) { throw new ArgumentException("INVALID_SCREENSHOT"); } catch (EndOfStreamException) { throw new ArgumentException("INVALID_SCREENSHOT"); }
        return output.ToArray();
    }
}
