// Ported from swisseph-master/sweph.c read_const + get_new_segment + do_fread
// and swephlib.c swi_crc32. Original license: see LICENSE.SwissEph.txt.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SharpAstrology.SwissEphemerides.Application.Bodies;

namespace SharpAstrology.SwissEphemerides.Infrastructure.SwissEph;

/// <summary>
/// Thread-safe positional reader for one <c>.se1</c> file.
/// Wraps a <see cref="SafeFileHandle"/> opened with
/// <see cref="FileOptions.RandomAccess"/> and uses
/// <see cref="RandomAccess"/> for every read so that the same handle can be
/// shared across threads. Header is parsed eagerly in the constructor;
/// segments are decompressed lazily by <see cref="ReadSegment"/>.
/// </summary>
internal sealed class Se1FileReader : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly Se1Header _header;
    private readonly long _fileLength;
    private int _disposed;

    public Se1FileReader(string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        _handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.RandomAccess);
        try
        {
            _fileLength = RandomAccess.GetLength(_handle);
            _header = ReadHeader(_handle, _fileLength, path);
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    /// <summary>Parsed file header (immutable after construction).</summary>
    public Se1Header Header => _header;

    /// <summary>The total file length in bytes (cached at open time).</summary>
    public long FileLength => _fileLength;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _handle.Dispose();
    }

    /// <summary>
    /// Returns the planet record for the given internal body number, or
    /// <see langword="null"/> if this file does not contain that body.
    /// </summary>
    public Se1PlanetData? FindPlanet(int seiBodyId)
    {
        var planets = _header.Planets;
        for (var i = 0; i < planets.Length; i++)
        {
            if (planets[i].BodyId == seiBodyId) return planets[i];
        }
        return null;
    }

    /// <summary>
    /// Reads and decompresses a single Chebyshev segment for the given body
    /// at the given segment index. Returns a new <c>3 * ncoe</c> double array
    /// (X, Y, Z coordinate coefficients concatenated). Does not apply the
    /// rotation / reference-ellipse mixing — that lives in
    /// <see cref="SegmentRotation"/>.
    /// </summary>
    public double[] ReadSegment(Se1PlanetData planet, int segmentIndex)
    {
        if (planet is null) throw new ArgumentNullException(nameof(planet));
        if ((uint)segmentIndex >= (uint)planet.SegmentCount)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));

        var swap = _header.Endianness == Se1Endianness.SwappedOrder;
        // The per-segment file position is stored as a 3-byte unsigned int
        // at planet.IndexOffset + segmentIndex * 3.
        Span<byte> trio = stackalloc byte[3];
        var indexFilePos = planet.IndexOffset + segmentIndex * 3L;
        ReadAt(_handle, trio, indexFilePos);
        var fpos = swap
            ? ((uint)trio[2]) | ((uint)trio[1] << 8) | ((uint)trio[0] << 16)
            : ((uint)trio[0]) | ((uint)trio[1] << 8) | ((uint)trio[2] << 16);

        var ncoe = planet.CoefficientCount;
        var segp = new double[3 * ncoe];

        var pos = (long)fpos;
        Span<byte> hdr = stackalloc byte[4];
        Span<uint> longs = stackalloc uint[Se1FileFormat.MaxPolynomialOrder + 1];
        Span<int> nsize = stackalloc int[6];

        // Read coefficients for 3 coordinates (X, Y, Z).
        for (var icoord = 0; icoord < 3; icoord++)
        {
            var idbl = icoord * ncoe;

            // Read the 2-byte size header. If high bit of byte 0 is set,
            // there are 6 nibble-encoded sizes (4 more bytes); else 4.
            ReadAt(_handle, hdr.Slice(0, 2), pos); pos += 2;
            int nsizes;
            int nco;
            if ((hdr[0] & 0x80) != 0)
            {
                ReadAt(_handle, hdr.Slice(2, 2), pos); pos += 2;
                nsizes = 6;
                // hdr[0] is the high-bit marker; size nibbles are in hdr[1..3].
                nsize[0] = hdr[1] >> 4;
                nsize[1] = hdr[1] & 0xf;
                nsize[2] = hdr[2] >> 4;
                nsize[3] = hdr[2] & 0xf;
                nsize[4] = hdr[3] >> 4;
                nsize[5] = hdr[3] & 0xf;
                nco = nsize[0] + nsize[1] + nsize[2] + nsize[3] + nsize[4] + nsize[5];
            }
            else
            {
                nsizes = 4;
                nsize[0] = hdr[0] >> 4;
                nsize[1] = hdr[0] & 0xf;
                nsize[2] = hdr[1] >> 4;
                nsize[3] = hdr[1] & 0xf;
                nco = nsize[0] + nsize[1] + nsize[2] + nsize[3];
            }
            if (nco > ncoe)
            {
                throw new Se1FileDamagedException(
                    $"file '{_header.FileName}': segment header advertises {nco} coefficients but body has ncoe={ncoe}.");
            }

            // Fill the coefficient buffer one chunk at a time. Each chunk i
            // (i = 0..3 → 4..1 byte ints; i = 4 → half-byte; i = 5 → quarter).
            for (var i = 0; i < nsizes; i++)
            {
                var nsi = nsize[i];
                if (nsi == 0) continue;
                if (i < 4)
                {
                    var bytesPerInt = 4 - i;
                    ReadPackedLongs(_handle, ref pos, longs.Slice(0, nsi), bytesPerInt, swap);
                    for (var m = 0; m < nsi; m++)
                    {
                        var lng = longs[m];
                        double val;
                        if ((lng & 1u) != 0u)
                            val = -((((double)(lng + 1u)) / 2.0) / 1.0e9 * planet.RMax / 2.0);
                        else
                            val = ((double)(lng / 2u)) / 1.0e9 * planet.RMax / 2.0;
                        segp[idbl++] = val;
                    }
                }
                else if (i == 4)
                {
                    // half-byte packing: two values per byte.
                    var k = (nsi + 1) / 2;
                    ReadPackedLongs(_handle, ref pos, longs.Slice(0, k), 1, swap);
                    var j = 0;
                    for (var m = 0; m < k && j < nsi; m++)
                    {
                        var lng = longs[m];
                        var o = 16u;
                        for (var n = 0; n < 2 && j < nsi; n++, j++, o /= 16u)
                        {
                            double val;
                            if ((lng & o) != 0u)
                                val = -((double)((lng + o) / o / 2u) * planet.RMax / 2.0 / 1.0e9);
                            else
                                val = (double)(lng / o / 2u) * planet.RMax / 2.0 / 1.0e9;
                            segp[idbl++] = val;
                            lng %= o;
                        }
                    }
                }
                else
                {
                    // quarter-byte packing: four values per byte.
                    var k = (nsi + 3) / 4;
                    ReadPackedLongs(_handle, ref pos, longs.Slice(0, k), 1, swap);
                    var j = 0;
                    for (var m = 0; m < k && j < nsi; m++)
                    {
                        var lng = longs[m];
                        var o = 64u;
                        for (var n = 0; n < 4 && j < nsi; n++, j++, o /= 4u)
                        {
                            double val;
                            if ((lng & o) != 0u)
                                val = -((double)((lng + o) / o / 2u) * planet.RMax / 2.0 / 1.0e9);
                            else
                                val = (double)(lng / o / 2u) * planet.RMax / 2.0 / 1.0e9;
                            segp[idbl++] = val;
                            lng %= o;
                        }
                    }
                }
            }
        }

        return segp;
    }

    /// <summary>
    /// Reads <c>count</c> packed unsigned integers from the file, each
    /// <c>bytesPerInt</c> bytes long. The values are stored as little-endian
    /// integers on disk and zero-extended to 32 bits in memory. Mirrors the
    /// C-side <c>do_fread(... corrsize=4)</c> with <c>fendian=LITENDIAN</c>
    /// and <c>freord=0</c> (the only combination the published .se1 files use
    /// — file is LE on disk and our host is LE on every platform .NET runs on).
    /// If a future BE-on-disk file appeared, <paramref name="swapBytes"/> would
    /// reverse the per-integer byte order.
    /// </summary>
    private static void ReadPackedLongs(SafeFileHandle handle, ref long pos, Span<uint> dest, int bytesPerInt, bool swapBytes)
    {
        // Stack-allocate a small buffer (max 41 * 4 = 164 bytes for our worst case).
        Span<byte> raw = stackalloc byte[(Se1FileFormat.MaxPolynomialOrder + 1) * 4];
        var totalBytes = bytesPerInt * dest.Length;
        var slice = raw.Slice(0, totalBytes);
        ReadAt(handle, slice, pos);
        pos += totalBytes;

        for (var i = 0; i < dest.Length; i++)
        {
            uint v = 0;
            var srcStart = i * bytesPerInt;
            if (swapBytes)
            {
                // file BE, host LE: file byte 0 = MSB of the integer.
                for (var j = 0; j < bytesPerInt; j++)
                    v |= ((uint)slice[srcStart + j]) << ((bytesPerInt - 1 - j) * 8);
            }
            else
            {
                // file LE: file byte j = byte j of the LE integer (LSB at j=0).
                for (var j = 0; j < bytesPerInt; j++)
                    v |= ((uint)slice[srcStart + j]) << (j * 8);
            }
            dest[i] = v;
        }
    }

    /// <summary>
    /// Reads exactly <paramref name="dest"/>.Length bytes from the file at
    /// the given offset, using positional <see cref="RandomAccess"/>.
    /// </summary>
    internal static void ReadAt(SafeFileHandle handle, Span<byte> dest, long offset)
    {
        var total = 0;
        while (total < dest.Length)
        {
            var n = RandomAccess.Read(handle, dest.Slice(total), offset + total);
            if (n == 0) throw new EndOfStreamException("unexpected EOF reading .se1 file");
            total += n;
        }
    }

    private static Se1Header ReadHeader(SafeFileHandle handle, long fileLength, string path)
    {
        // Header is text + binary. We replicate read_const() faithfully.
        var fileName = Path.GetFileName(path);

        // Pull the first AS_MAXCH * 4 bytes — large enough for the four text
        // lines and the binary tail. We don't care about precise sizing at
        // this stage; we just step a cursor.
        const int InitialBufferBytes = 8 * 1024;
        var buf = new byte[Math.Min(InitialBufferBytes, fileLength)];
        ReadAt(handle, buf, 0);

        var (line1, p1) = ReadLine(buf, 0);
        if (!TryParseVersionLine(line1, out var version))
            throw new Se1FileDamagedException($"file '{fileName}': missing or malformed version line.");

        var (line2, p2) = ReadLine(buf, p1);
        var asciiName = TrimTrailing(line2);
        if (!string.Equals(asciiName, fileName, StringComparison.OrdinalIgnoreCase))
        {
            throw new Se1FileDamagedException(
                $"file '{fileName}': filename header line says '{asciiName}'.");
        }

        // Copyright line — discarded.
        var (_, p3) = ReadLine(buf, p2);

        // For asteroid files (SEI_FILE_ANY_AST) there is an extra orbital
        // elements line. Detect via filename prefix.
        var isAstFile = fileName.StartsWith("se", StringComparison.OrdinalIgnoreCase)
            && fileName.Length > 2 && (fileName[2] == 'a' || fileName[2] == 'p')
            && !fileName.StartsWith("seas", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("sepl", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("semo", StringComparison.OrdinalIgnoreCase)
            && !fileName.StartsWith("sepm", StringComparison.OrdinalIgnoreCase);
        var elementsLine = string.Empty;
        var pCursor = p3;
        if (isAstFile)
        {
            (elementsLine, pCursor) = ReadLine(buf, p3);
        }

        // 4-byte test endian.
        if (pCursor + 4 > buf.Length) throw new Se1FileDamagedException($"file '{fileName}': truncated header.");
        var rawTest = BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(pCursor, 4));
        Se1Endianness endianness;
        if (rawTest == Se1FileFormat.FileTestEndian)
        {
            endianness = Se1Endianness.NativeOrder;
        }
        else
        {
            // Try the swapped form.
            var swapped = BinaryPrimitives.ReverseEndianness(rawTest);
            if (swapped == Se1FileFormat.FileTestEndian)
                endianness = Se1Endianness.SwappedOrder;
            else
                throw new Se1FileDamagedException($"file '{fileName}': bad endian sentinel 0x{rawTest:X8}.");
        }
        pCursor += 4;

        // 4-byte declared file length.
        var declaredLen = ReadInt32(buf, ref pCursor, endianness);
        if (declaredLen != fileLength)
            throw new Se1FileDamagedException($"file '{fileName}': declared length {declaredLen} != actual {fileLength}.");

        // 4-byte JPL DE number.
        var deNum = ReadInt32(buf, ref pCursor, endianness);

        // 8-byte tfstart, tfend.
        var tfstart = ReadDouble(buf, ref pCursor, endianness);
        var tfend = ReadDouble(buf, ref pCursor, endianness);

        // 2-byte planet count. nbytes_ipl convention: if > 256, mod-256 and
        // upgrade ipl-entry to 4 bytes.
        var rawNplan = ReadInt16(buf, ref pCursor, endianness);
        var nbytesIpl = 2;
        var nplan = (int)rawNplan;
        if (nplan > 256)
        {
            nbytesIpl = 4;
            nplan %= 256;
        }
        if (nplan < 1 || nplan > 20)
            throw new Se1FileDamagedException($"file '{fileName}': bad planet count {nplan}.");

        var ipl = new int[nplan];
        for (var i = 0; i < nplan; i++)
        {
            ipl[i] = nbytesIpl == 2
                ? ReadInt16(buf, ref pCursor, endianness)
                : ReadInt32(buf, ref pCursor, endianness);
        }

        // Asteroid name (30 bytes raw) iff this is an SEI_FILE_ANY_AST file.
        var astName = string.Empty;
        if (isAstFile)
        {
            if (pCursor + 30 > buf.Length) throw new Se1FileDamagedException($"file '{fileName}': truncated ast name.");
            astName = TrimTrailing(System.Text.Encoding.ASCII.GetString(buf, pCursor, 30));
            pCursor += 30;
            // Use the elements line so it isn't 'unused'.
            _ = elementsLine;
        }

        // 4-byte CRC.
        var crc = (uint)ReadInt32(buf, ref pCursor, endianness);
        var headerEndOffset = pCursor;

        // Verify CRC over the area before the CRC field itself.
        var crcLen = pCursor - 4;
        var crcBuf = buf.AsSpan(0, crcLen);
        var actualCrc = Se1Crc32.Compute(crcBuf);
        if (actualCrc != crc)
        {
            throw new Se1FileDamagedException(
                $"file '{fileName}': header CRC32 mismatch (declared 0x{crc:X8}, computed 0x{actualCrc:X8}).");
        }

        // 5 doubles of general constants.
        var clight = ReadDouble(buf, ref pCursor, endianness);
        var aunit = ReadDouble(buf, ref pCursor, endianness);
        var helgrav = ReadDouble(buf, ref pCursor, endianness);
        var ratme = ReadDouble(buf, ref pCursor, endianness);
        var sunR = ReadDouble(buf, ref pCursor, endianness);
        var consts = new Se1GeneralConstants(clight, aunit, helgrav, ratme, sunR);

        // Per-planet data records.
        var plans = new Se1PlanetData[nplan];
        for (var kpl = 0; kpl < nplan; kpl++)
        {
            var ipli = ipl[kpl];
            var indexOffset = ReadInt32(buf, ref pCursor, endianness);
            // 1 byte iflg, sign-extended into int.
            var iflg = (int)(sbyte)buf[pCursor]; pCursor += 1;
            // 1 byte ncoe.
            var ncoe = (int)(sbyte)buf[pCursor]; pCursor += 1;
            // 4 byte rmax-as-int.
            var rmaxL = ReadInt32(buf, ref pCursor, endianness);
            var rmax = rmaxL / 1000.0;
            if (ipli >= Se1FileFormat.PlanetaryMoonOffset && ipli < Se1FileFormat.AsteroidOffset)
            {
                if ((ipli % 100) == 99 || (ipli - 9000) / 100 == 4 /* SE_MARS */)
                    rmax = rmaxL / 1.0e6;
            }
            // 10 doubles: tfstart, tfend, dseg, telem, prot, dprot, qrot, dqrot, peri, dperi.
            var tfst = ReadDouble(buf, ref pCursor, endianness);
            var tfen = ReadDouble(buf, ref pCursor, endianness);
            var dseg = ReadDouble(buf, ref pCursor, endianness);
            var telem = ReadDouble(buf, ref pCursor, endianness);
            var prot = ReadDouble(buf, ref pCursor, endianness);
            var dprot = ReadDouble(buf, ref pCursor, endianness);
            var qrot = ReadDouble(buf, ref pCursor, endianness);
            var dqrot = ReadDouble(buf, ref pCursor, endianness);
            var peri = ReadDouble(buf, ref pCursor, endianness);
            var dperi = ReadDouble(buf, ref pCursor, endianness);

            var nndx = (int)((tfen - tfst + 0.1) / dseg);

            var refep = Array.Empty<double>();
            if ((iflg & Se1FileFormat.FlagEllipse) != 0)
            {
                refep = new double[2 * ncoe];
                for (var k = 0; k < refep.Length; k++)
                    refep[k] = ReadDouble(buf, ref pCursor, endianness);
            }

            plans[kpl] = new Se1PlanetData
            {
                BodyId = ipli,
                Flags = iflg,
                CoefficientCount = ncoe,
                IndexOffset = indexOffset,
                SegmentCount = nndx,
                JdStart = tfst,
                JdEnd = tfen,
                SegmentLengthDays = dseg,
                ElementsEpoch = telem,
                Prot = prot,
                DProt = dprot,
                Qrot = qrot,
                DQrot = dqrot,
                Peri = peri,
                DPeri = dperi,
                RMax = rmax,
                ReferenceEllipse = refep,
            };
        }

        // Use headerEndOffset variable so it isn't 'unused'.
        _ = headerEndOffset;

        return new Se1Header
        {
            FileName = fileName,
            FileVersion = version,
            JplDeNumber = deNum,
            JdStart = tfstart,
            JdEnd = tfend,
            PlanetCount = nplan,
            PlanetIds = ipl,
            Endianness = endianness,
            Planets = plans,
            AsteroidName = astName,
            FileLength = fileLength,
            HeaderCrc = crc,
            Constants = consts,
        };
    }

    private static (string Line, int NextOffset) ReadLine(byte[] buf, int start)
    {
        // Lines are terminated by CRLF. Find the CR.
        var n = buf.Length;
        for (var i = start; i < n - 1; i++)
        {
            if (buf[i] == 0x0d && buf[i + 1] == 0x0a)
            {
                var s = System.Text.Encoding.ASCII.GetString(buf, start, i - start);
                return (s, i + 2);
            }
        }
        throw new Se1FileDamagedException("missing CRLF terminator in header line.");
    }

    private static string TrimTrailing(string s)
    {
        var end = s.Length;
        while (end > 0 && (s[end - 1] == ' ' || s[end - 1] == '\t' || s[end - 1] == '\0'))
            end--;
        return s[..end];
    }

    private static bool TryParseVersionLine(string line, out int version)
    {
        // Find first digit.
        for (var i = 0; i < line.Length; i++)
        {
            if (char.IsDigit(line[i]))
            {
                var end = i;
                while (end < line.Length && char.IsDigit(line[end])) end++;
                return int.TryParse(line.AsSpan(i, end - i), out version);
            }
        }
        version = 0;
        return false;
    }

    private static int ReadInt16(byte[] buf, ref int cursor, Se1Endianness endianness)
    {
        if (cursor + 2 > buf.Length) throw new Se1FileDamagedException("truncated int16 in header.");
        var span = buf.AsSpan(cursor, 2);
        cursor += 2;
        return endianness == Se1Endianness.NativeOrder
            ? BinaryPrimitives.ReadInt16LittleEndian(span)
            : BinaryPrimitives.ReadInt16BigEndian(span);
    }

    private static int ReadInt32(byte[] buf, ref int cursor, Se1Endianness endianness)
    {
        if (cursor + 4 > buf.Length) throw new Se1FileDamagedException("truncated int32 in header.");
        var span = buf.AsSpan(cursor, 4);
        cursor += 4;
        return endianness == Se1Endianness.NativeOrder
            ? BinaryPrimitives.ReadInt32LittleEndian(span)
            : BinaryPrimitives.ReadInt32BigEndian(span);
    }

    private static double ReadDouble(byte[] buf, ref int cursor, Se1Endianness endianness)
    {
        if (cursor + 8 > buf.Length) throw new Se1FileDamagedException("truncated double in header.");
        var span = buf.AsSpan(cursor, 8);
        cursor += 8;
        return endianness == Se1Endianness.NativeOrder
            ? BinaryPrimitives.ReadDoubleLittleEndian(span)
            : BinaryPrimitives.ReadDoubleBigEndian(span);
    }
}

/// <summary>Thrown when a <c>.se1</c> file fails its self-consistency checks.</summary>
public sealed class Se1FileDamagedException : EphemerisException
{
    public Se1FileDamagedException(string message) : base($"Swiss Ephemeris file is damaged: {message}") { }
    public Se1FileDamagedException(string message, Exception inner) : base($"Swiss Ephemeris file is damaged: {message}", inner) { }
}
