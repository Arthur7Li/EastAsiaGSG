using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using EastAsiaGSG.Core.Components;
using EastAsiaGSG.Core.ECS;

namespace EastAsiaGSG.Data.Map;

/// <summary>
/// Holds the cached CPU copy of provinces.png (RGB8 lossless texture where ProvinceId = (R &lt;&lt; 8) | G)
/// and provides O(1) UV and pixel coordinate mouse picking with zero raycasting.
/// </summary>
public sealed class ProvinceMapBuffer
{
    /// <summary>
    /// Sentinel value (65,535 / R=255, G=255) indicating an ocean or unmapped pixel.
    /// </summary>
    public const ushort OceanProvinceId = 0xFFFF;

    public const int BytesPerPixel = 3; // RGB8

    private readonly byte[] _rgbBytes;

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Raw RGB8 byte buffer (Width * Height * 3 bytes) where pixel (x, y) stores R = (id &gt;&gt; 8), G = (id &amp; 0xFF), B = 0.
    /// </summary>
    public byte[] RawRgbBytes => _rgbBytes;

    public ProvinceMapBuffer(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Width = width;
        Height = height;
        _rgbBytes = new byte[width * height * BytesPerPixel];
        FillOcean();
    }

    public ProvinceMapBuffer(int width, int height, byte[] rgbBytes)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }
        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }
        ArgumentNullException.ThrowIfNull(rgbBytes);
        if (rgbBytes.Length != width * height * BytesPerPixel)
        {
            throw new ArgumentException(
                $"Expected RGB8 buffer of length {width * height * BytesPerPixel}, got {rgbBytes.Length}.",
                nameof(rgbBytes));
        }

        Width = width;
        Height = height;
        _rgbBytes = rgbBytes;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EncodeProvinceId(ushort provinceId, out byte r, out byte g)
    {
        r = (byte)(provinceId >> 8);
        g = (byte)(provinceId & 0xFF);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort DecodeProvinceId(byte r, byte g)
    {
        return (ushort)((r << 8) | g);
    }

    public void FillOcean()
    {
        EncodeProvinceId(OceanProvinceId, out byte r, out byte g);
        for (int i = 0; i < _rgbBytes.Length; i += BytesPerPixel)
        {
            _rgbBytes[i] = r;
            _rgbBytes[i + 1] = g;
            _rgbBytes[i + 2] = 0;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetProvinceAtPixel(int x, int y, ushort provinceId)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        int offset = (y * Width + x) * BytesPerPixel;
        EncodeProvinceId(provinceId, out byte r, out byte g);
        _rgbBytes[offset] = r;
        _rgbBytes[offset + 1] = g;
        _rgbBytes[offset + 2] = 0;
    }

    /// <summary>
    /// Reads the ProvinceId at pixel coordinates (x, y) in O(1) time.
    /// Returns OceanProvinceId (0xFFFF) if out of bounds or on an ocean pixel.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort PickProvinceAtPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return OceanProvinceId;
        }

        int offset = (y * Width + x) * BytesPerPixel;
        return DecodeProvinceId(_rgbBytes[offset], _rgbBytes[offset + 1]);
    }

    /// <summary>
    /// Converts normalized UV coordinates [0, 1] on the map quad to pixel coordinates (x, y)
    /// and reads the ProvinceId in O(1) time with zero raycasting.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort PickProvinceAtUv(float u, float v)
    {
        if (u < 0f || u > 1f || v < 0f || v > 1f)
        {
            return OceanProvinceId;
        }

        int x = System.Math.Clamp((int)(u * Width), 0, Width - 1);
        int y = System.Math.Clamp((int)(v * Height), 0, Height - 1);
        return PickProvinceAtPixel(x, y);
    }

    /// <summary>
    /// Generates a deterministic bounded Voronoi province ID map from ProvinceData.CenterX and CenterY.
    /// </summary>
    public static ProvinceMapBuffer GenerateVoronoiFromWorld(
        SimulationWorld world,
        int width = 1024,
        int height = 1024,
        int activeProvinceCount = 120,
        float maxCoastalRadius = 60f)
    {
        ArgumentNullException.ThrowIfNull(world);
        var buffer = new ProvinceMapBuffer(width, height);

        int count = System.Math.Min(activeProvinceCount, SimulationWorld.MaxProvinces);
        float maxDistSq = maxCoastalRadius * maxCoastalRadius;
        int radiusInt = (int)System.MathF.Ceiling(maxCoastalRadius);

        ReadOnlySpan<ProvinceData> provinces = world.Provinces.AsSpan(0, count);

        int minX = width, maxX = 0, minY = height, maxY = 0;
        for (int i = 0; i < count; i++)
        {
            ref readonly ProvinceData prov = ref provinces[i];
            if (prov.OwnerTag == CountryTag.None)
            {
                continue;
            }
            if (prov.CenterX < minX) minX = prov.CenterX;
            if (prov.CenterX > maxX) maxX = prov.CenterX;
            if (prov.CenterY < minY) minY = prov.CenterY;
            if (prov.CenterY > maxY) maxY = prov.CenterY;
        }

        int startX = System.Math.Max(0, minX - radiusInt);
        int endX = System.Math.Min(width - 1, maxX + radiusInt);
        int startY = System.Math.Max(0, minY - radiusInt);
        int endY = System.Math.Min(height - 1, maxY + radiusInt);

        for (int y = startY; y <= endY; y++)
        {
            for (int x = startX; x <= endX; x++)
            {
                float bestDistSq = maxDistSq;
                ushort bestProvinceId = OceanProvinceId;

                for (int i = 0; i < count; i++)
                {
                    ref readonly ProvinceData prov = ref provinces[i];
                    if (prov.OwnerTag == CountryTag.None)
                    {
                        continue;
                    }

                    int dx = x - prov.CenterX;
                    if (dx <= -radiusInt || dx >= radiusInt)
                    {
                        continue;
                    }

                    int dy = y - prov.CenterY;
                    if (dy <= -radiusInt || dy >= radiusInt)
                    {
                        continue;
                    }

                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestProvinceId = prov.ProvinceId;
                    }
                }

                if (bestProvinceId != OceanProvinceId)
                {
                    buffer.SetProvinceAtPixel(x, y, bestProvinceId);
                }
            }
        }

        // Ensure every active province center pixel is explicitly stamped with its exact ProvinceId
        for (int i = 0; i < count; i++)
        {
            ref readonly ProvinceData prov = ref provinces[i];
            if (prov.OwnerTag != CountryTag.None)
            {
                buffer.SetProvinceAtPixel(prov.CenterX, prov.CenterY, prov.ProvinceId);
            }
        }

        return buffer;
    }

    /// <summary>
    /// Writes the RGB8 buffer as a lossless non-interlaced 24-bit RGB8 PNG file.
    /// </summary>
    public void SaveToPngFile(string filePath)
    {
        using FileStream fs = File.Create(filePath);
        WriteToPngStream(fs);
    }

    public void WriteToPngStream(Stream output)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        output.Write(signature);

        // IHDR chunk (13 bytes): Width(4), Height(4), BitDepth=8, ColorType=2 (RGB), Compression=0, Filter=0, Interlace=0
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[0..4], (uint)Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..8], (uint)Height);
        ihdr[8] = 8; // 8 bits per channel
        ihdr[9] = 2; // RGB truecolor
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(output, "IHDR"u8, ihdr);

        // IDAT chunk: ZLib-compressed scanlines with Filter=0 (None)
        int rowStride = Width * BytesPerPixel;
        byte[] rawScanlines = new byte[Height * (1 + rowStride)];
        for (int y = 0; y < Height; y++)
        {
            int dstRowOffset = y * (1 + rowStride);
            rawScanlines[dstRowOffset] = 0; // Filter type 0 (None)
            Buffer.BlockCopy(_rgbBytes, y * rowStride, rawScanlines, dstRowOffset + 1, rowStride);
        }

        using var compressedStream = new MemoryStream();
        using (var zlib = new ZLibStream(compressedStream, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(rawScanlines, 0, rawScanlines.Length);
        }
        WriteChunk(output, "IDAT"u8, compressedStream.ToArray());

        // IEND chunk
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Loads a lossless 8-bit RGB8 or RGBA8 PNG file into a cached CPU ProvinceMapBuffer.
    /// </summary>
    public static ProvinceMapBuffer LoadFromPngFile(string filePath)
    {
        using FileStream fs = File.OpenRead(filePath);
        return LoadFromPngStream(fs);
    }

    public static ProvinceMapBuffer LoadFromPngStream(Stream input)
    {
        Span<byte> sig = stackalloc byte[8];
        input.ReadExactly(sig);
        ReadOnlySpan<byte> expectedSig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!sig.SequenceEqual(expectedSig))
        {
            throw new InvalidDataException("Invalid PNG signature.");
        }

        int width = 0;
        int height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        using var idatBuffer = new MemoryStream();

        Span<byte> headerBuf = stackalloc byte[8];
        Span<byte> crcBuf = stackalloc byte[4];

        while (true)
        {
            int read = input.Read(headerBuf);
            if (read == 0)
            {
                break;
            }
            if (read != 8)
            {
                throw new InvalidDataException("Unexpected EOF reading PNG chunk header.");
            }

            uint length = BinaryPrimitives.ReadUInt32BigEndian(headerBuf[0..4]);
            ReadOnlySpan<byte> type = headerBuf[4..8];

            byte[] chunkData = new byte[length];
            if (length > 0)
            {
                input.ReadExactly(chunkData);
            }
            input.ReadExactly(crcBuf);

            if (type.SequenceEqual("IHDR"u8))
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(chunkData.AsSpan(0, 4));
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(chunkData.AsSpan(4, 4));
                bitDepth = chunkData[8];
                colorType = chunkData[9];
                if (bitDepth != 8 || (colorType != 2 && colorType != 6))
                {
                    throw new InvalidDataException(
                        $"Unsupported PNG format: bitDepth={bitDepth}, colorType={colorType}. Expected 8-bit RGB8 (2) or RGBA8 (6).");
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idatBuffer.Write(chunkData, 0, chunkData.Length);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("PNG file missing valid IHDR dimensions.");
        }

        int srcBpp = colorType == 6 ? 4 : 3;
        int srcRowBytes = width * srcBpp;
        byte[] decompressed = new byte[height * (1 + srcRowBytes)];

        idatBuffer.Position = 0;
        using (var zlib = new ZLibStream(idatBuffer, CompressionMode.Decompress))
        {
            zlib.ReadExactly(decompressed);
        }

        byte[] unpackedRows = new byte[height * srcRowBytes];
        for (int y = 0; y < height; y++)
        {
            int srcOffset = y * (1 + srcRowBytes);
            byte filter = decompressed[srcOffset];
            int rowStart = srcOffset + 1;
            int dstRowStart = y * srcRowBytes;
            int prevRowStart = (y - 1) * srcRowBytes;

            for (int i = 0; i < srcRowBytes; i++)
            {
                byte raw = decompressed[rowStart + i];
                byte a = i >= srcBpp ? unpackedRows[dstRowStart + i - srcBpp] : (byte)0;
                byte b = y > 0 ? unpackedRows[prevRowStart + i] : (byte)0;
                byte c = (i >= srcBpp && y > 0) ? unpackedRows[prevRowStart + i - srcBpp] : (byte)0;

                unpackedRows[dstRowStart + i] = filter switch
                {
                    0 => raw,
                    1 => (byte)(raw + a),
                    2 => (byte)(raw + b),
                    3 => (byte)(raw + ((a + b) >> 1)),
                    4 => (byte)(raw + PaethPredictor(a, b, c)),
                    _ => throw new InvalidDataException($"Unsupported PNG row filter {filter}.")
                };
            }
        }

        byte[] rgbBytes = new byte[width * height * BytesPerPixel];
        if (srcBpp == 3)
        {
            Buffer.BlockCopy(unpackedRows, 0, rgbBytes, 0, rgbBytes.Length);
        }
        else
        {
            int pixelCount = width * height;
            for (int p = 0; p < pixelCount; p++)
            {
                rgbBytes[p * 3] = unpackedRows[p * 4];
                rgbBytes[p * 3 + 1] = unpackedRows[p * 4 + 1];
                rgbBytes[p * 3 + 2] = unpackedRows[p * 4 + 2];
            }
        }

        return new ProvinceMapBuffer(width, height, rgbBytes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = System.Math.Abs(p - a);
        int pb = System.Math.Abs(p - b);
        int pc = System.Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }
        return pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)data.Length);
        output.Write(lengthBytes);
        output.Write(chunkType);
        if (!data.IsEmpty)
        {
            output.Write(data);
        }

        uint crc = ComputeCrc32(chunkType, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        for (int i = 0; i < type.Length; i++)
        {
            crc = UpdateCrcByte(crc, type[i]);
        }
        for (int i = 0; i < data.Length; i++)
        {
            crc = UpdateCrcByte(crc, data[i]);
        }
        return ~crc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint UpdateCrcByte(uint crc, byte b)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++)
        {
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc;
    }
}
