// Ported from swisseph-master/swejpl.c — fsizer (line 189), state (line 652),
// read_const_jpl (line 859), reorder (line 895), swi_open_jpl_file (line 924).
// Original license: see LICENSE.SwissEph.txt.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Jpl;

/// <summary>
/// Thread-safe positional reader for one JPL DE <c>.eph</c> file. Uses
/// <see cref="SafeFileHandle"/> + <see cref="RandomAccess"/> so the same
/// underlying file descriptor can be shared across threads. Header is parsed
/// eagerly in the constructor; per-segment Chebyshev coefficient blocks are
/// loaded lazily by <see cref="ReadSegment"/> and cached upstream by
/// <see cref="JplSegmentCache"/>.
/// </summary>
internal sealed class JplFileReader : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly JplHeader _header;
    private int _disposed;

    public JplFileReader(string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new JplFileNotFoundException(path);
        _handle = File.OpenHandle(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileOptions.RandomAccess);
        try
        {
            var fileLength = RandomAccess.GetLength(_handle);
            _header = ReadHeader(_handle, fileLength, path);
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    /// <summary>The parsed file header (immutable after construction).</summary>
    public JplHeader Header => _header;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _handle.Dispose();
    }

    /// <summary>
    /// Reads the entire <see cref="JplHeader.DoublesPerRecord"/>-double
    /// record for segment <paramref name="segmentIndex"/> (0-based: index 0
    /// covers <c>[JdStart, JdStart + SegmentLengthDays)</c>). Records 0 and 1
    /// in the file contain the header / constants; segment data begins at
    /// record 2, so on-disk offset = <c>(segmentIndex + 2) * RecordSizeBytes</c>.
    /// Returns a freshly-allocated array of length
    /// <see cref="JplHeader.DoublesPerRecord"/>.
    /// </summary>
    public double[] ReadSegment(int segmentIndex)
    {
        if ((uint)segmentIndex >= (uint)_header.SegmentCount)
            throw new ArgumentOutOfRangeException(nameof(segmentIndex));

        var ncoeffs = _header.DoublesPerRecord;
        var buf = new double[ncoeffs];
        Span<byte> byteSpan = System.Runtime.InteropServices.MemoryMarshal.AsBytes(buf.AsSpan());
        var fileOffset = (long)(segmentIndex + 2) * _header.RecordSizeBytes;
        ReadAt(_handle, byteSpan.Slice(0, ncoeffs * 8), fileOffset);

        if (_header.Endianness == JplEndianness.SwappedOrder)
        {
            for (var i = 0; i < buf.Length; i++)
            {
                // Reverse 8 bytes in-place.
                var slice = byteSpan.Slice(i * 8, 8);
                slice.Reverse();
            }
        }

        return buf;
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
            if (n == 0) throw new EndOfStreamException("unexpected EOF reading JPL file");
            total += n;
        }
    }

    private static JplHeader ReadHeader(SafeFileHandle handle, long fileLength, string path)
    {
        var fileName = Path.GetFileName(path);

        // -------- Read record 0 header bits up through ipt + lpt --------
        // Layout:
        //   [0..252)     = title (3 lines × 84 bytes)
        //   [252..2652)  = ch_cnam (400 × 6 bytes)
        //   [2652..2676) = ss[3] (start, end, segment-length, doubles)
        //   [2676..2680) = ncon (int32)
        //   [2680..2688) = au (double)
        //   [2688..2696) = emrat (double)
        //   [2696..2840) = ipt[36] (12 × 3 × int32)
        //   [2840..2844) = numde (int32)
        //   [2844..2856) = lpt[3] (libration ipt × int32)
        const int InitialBytes = 4096; // generously covers the 2856-byte record-0 prefix.
        if (fileLength < InitialBytes)
            throw new JplFileFormatException($"file '{fileName}' is too short ({fileLength} bytes).");
        var prefix = new byte[InitialBytes];
        ReadAt(handle, prefix, 0);

        // ---- title ----
        var titleSpan = prefix.AsSpan(0, JplFileFormat.TitleBytes);
        // Some JPL files have NULL-padding; we don't rely on the title beyond logging. Discard.
        _ = titleSpan;

        // ---- 400 6-byte constant names ----
        var nameStart = JplFileFormat.TitleBytes;
        var names = new string[JplFileFormat.ConstantCount];
        for (var i = 0; i < JplFileFormat.ConstantCount; i++)
        {
            var offset = nameStart + i * 6;
            names[i] = Encoding.ASCII.GetString(prefix, offset, 6).TrimEnd();
        }
        var ssOffset = nameStart + JplFileFormat.ConstantNameBytes; // = 2652

        // ---- ss[3]: detect endianness via plausibility test on ss[2] ----
        var rawSs = prefix.AsSpan(ssOffset, 24);
        var ss0Native = BinaryPrimitives.ReadDoubleLittleEndian(rawSs.Slice(0, 8));
        var ss1Native = BinaryPrimitives.ReadDoubleLittleEndian(rawSs.Slice(8, 8));
        var ss2Native = BinaryPrimitives.ReadDoubleLittleEndian(rawSs.Slice(16, 8));

        JplEndianness endianness;
        double jdStart;
        double jdEnd;
        double segmentDays;
        if (ss2Native >= JplFileFormat.SegmentDaysMin && ss2Native <= JplFileFormat.SegmentDaysMax)
        {
            endianness = JplEndianness.NativeOrder;
            jdStart = ss0Native;
            jdEnd = ss1Native;
            segmentDays = ss2Native;
        }
        else
        {
            endianness = JplEndianness.SwappedOrder;
            jdStart = BinaryPrimitives.ReadDoubleBigEndian(rawSs.Slice(0, 8));
            jdEnd = BinaryPrimitives.ReadDoubleBigEndian(rawSs.Slice(8, 8));
            segmentDays = BinaryPrimitives.ReadDoubleBigEndian(rawSs.Slice(16, 8));
        }

        // Plausibility: start/end bounds + segment length, mirroring swejpl.c:228.
        if (jdStart < JplFileFormat.EphemerisJdMin
            || jdEnd > JplFileFormat.EphemerisJdMax
            || segmentDays < JplFileFormat.SegmentDaysMin
            || segmentDays > JplFileFormat.SegmentDaysMax)
        {
            throw new JplFileFormatException(
                $"file '{fileName}' has invalid header: ss=({jdStart}, {jdEnd}, {segmentDays}).");
        }
        if (jdEnd <= jdStart)
            throw new JplFileFormatException($"file '{fileName}': end JD must exceed start.");

        var pCursor = ssOffset + 24; // 2676

        // ---- ncon, au, emrat ----
        _ = ReadInt32(prefix, ref pCursor, endianness); // ncon (we use the names we already read).
        var au = ReadDouble(prefix, ref pCursor, endianness);
        var emrat = ReadDouble(prefix, ref pCursor, endianness);

        // ---- ipt[36] ----
        var ipt = new int[39];
        for (var i = 0; i < JplFileFormat.IptEntriesInHeader; i++)
            ipt[i] = ReadInt32(prefix, ref pCursor, endianness);

        // ---- numde ----
        var deNumber = ReadInt32(prefix, ref pCursor, endianness);

        // ---- librations lpt[3] copied into ipt[36..38] ----
        for (var i = 0; i < 3; i++)
            ipt[i + 36] = ReadInt32(prefix, ref pCursor, endianness);

        // ---- compute record size from the highest-offset block (swejpl.c:275-287) ----
        var (kmx, khi) = (0, 0);
        for (var i = 0; i < 13; i++)
        {
            if (ipt[i * 3] > kmx)
            {
                kmx = ipt[i * 3];
                khi = i + 1;
            }
        }
        var nd = (khi == 12) ? 2 : 3;
        var ksize = (ipt[khi * 3 - 3] + nd * ipt[khi * 3 - 2] * ipt[khi * 3 - 1] - 1) * 2;
        // DE102 quirk — the file embeds 424 fill bytes, so the computed ksize is
        // wrong; bump it to DE200's 1652. swejpl.c:292.
        if (ksize == JplFileFormat.De102ReportedRecordSize)
            ksize = JplFileFormat.De102FixedRecordSize;
        if (ksize < JplFileFormat.RecordSizeMin || ksize > JplFileFormat.RecordSizeMax)
            throw new JplFileFormatException($"file '{fileName}': implausible ksize={ksize}.");

        var recordSizeBytes = ksize * JplFileFormat.RecordWordSizeBytes;

        // ---- read the 400 cval[] doubles from record 1 ----
        var constants = new double[JplFileFormat.ConstantCount];
        var cvalBytes = JplFileFormat.ConstantCount * 8; // 3200
        if (recordSizeBytes + cvalBytes > fileLength)
            throw new JplFileFormatException($"file '{fileName}': truncated before constant block.");
        var cvalRaw = new byte[cvalBytes];
        ReadAt(handle, cvalRaw, recordSizeBytes);
        for (var i = 0; i < JplFileFormat.ConstantCount; i++)
        {
            var slice = cvalRaw.AsSpan(i * 8, 8);
            constants[i] = endianness == JplEndianness.NativeOrder
                ? BinaryPrimitives.ReadDoubleLittleEndian(slice)
                : BinaryPrimitives.ReadDoubleBigEndian(slice);
        }

        // ---- segment count + total expected length (swejpl.c:737-754) ----
        var segmentCount = (int)((jdEnd - jdStart) / segmentDays);
        if (segmentCount <= 0)
            throw new JplFileFormatException($"file '{fileName}': computed zero segments.");

        // Sum of all Chebyshev doubles per segment (each body's ncf*na*3 doubles, nutation 2-comp).
        long doublesPerSeg = 0;
        for (var i = 0; i < 13; i++)
        {
            var k = (i == 11) ? 2 : 3;
            doublesPerSeg += (long)ipt[i * 3 + 1] * ipt[i * 3 + 2] * k;
        }
        // Plus 2 doubles per segment for start/end JDs (the leading two doubles of each record).
        var doubleBytesPerSegment = (doublesPerSeg + 2) * 8;
        var headerBytes = 2L * recordSizeBytes;
        var expectedLen = headerBytes + (long)segmentCount * doubleBytesPerSegment;
        // Some shipped files are exactly one record longer than the strict expected length.
        if (fileLength != expectedLen && fileLength - expectedLen != recordSizeBytes)
        {
            throw new JplFileFormatException(
                $"file '{fileName}' has length {fileLength} but expected {expectedLen} (or +{recordSizeBytes}).");
        }

        // ---- spot-check: record-2 first two doubles must equal jdStart .. jdStart+segmentDays.
        // And the last segment's final JD must equal jdEnd. (swejpl.c:763-779)
        var ts = new double[2];
        var ts2 = new double[2];
        var sample = new byte[16];
        ReadAt(handle, sample, 2L * recordSizeBytes);
        ts[0] = ReadDoubleAt(sample, 0, endianness);
        ts[1] = ReadDoubleAt(sample, 8, endianness);
        ReadAt(handle, sample, ((long)segmentCount + 2 - 1) * recordSizeBytes);
        ts2[0] = ReadDoubleAt(sample, 0, endianness);
        ts2[1] = ReadDoubleAt(sample, 8, endianness);
        if (ts[0] != jdStart || ts2[1] != jdEnd)
        {
            throw new JplFileFormatException(
                $"file '{fileName}': segment-bound check failed ({ts[0]} != {jdStart} || {ts2[1]} != {jdEnd}).");
        }

        // ---- build body-pointer records ----
        var bodyPtrs = new JplCoeffPointer[13];
        for (var i = 0; i < 13; i++)
        {
            bodyPtrs[i] = new JplCoeffPointer(ipt[i * 3], ipt[i * 3 + 1], ipt[i * 3 + 2]);
        }
        var nut = new JplCoeffPointer(ipt[33], ipt[34], ipt[35]);
        var lib = new JplCoeffPointer(ipt[36], ipt[37], ipt[38]);

        return new JplHeader
        {
            FileName = fileName,
            DeNumber = deNumber,
            Endianness = endianness,
            JdStart = jdStart,
            JdEnd = jdEnd,
            SegmentLengthDays = segmentDays,
            AstronomicalUnitKm = au,
            EarthMoonRatio = emrat,
            BodyPointers = bodyPtrs,
            Nutations = nut,
            Librations = lib,
            RecordSizeWords = ksize,
            SegmentCount = segmentCount,
            DoublesPerRecord = recordSizeBytes / 8,
            FileLength = fileLength,
            ConstantNames = names,
            ConstantValues = constants,
        };
    }

    private static int ReadInt32(byte[] buf, ref int cursor, JplEndianness endianness)
    {
        if (cursor + 4 > buf.Length) throw new JplFileFormatException("truncated int32 in header.");
        var span = buf.AsSpan(cursor, 4);
        cursor += 4;
        return endianness == JplEndianness.NativeOrder
            ? BinaryPrimitives.ReadInt32LittleEndian(span)
            : BinaryPrimitives.ReadInt32BigEndian(span);
    }

    private static double ReadDouble(byte[] buf, ref int cursor, JplEndianness endianness)
    {
        if (cursor + 8 > buf.Length) throw new JplFileFormatException("truncated double in header.");
        var span = buf.AsSpan(cursor, 8);
        cursor += 8;
        return endianness == JplEndianness.NativeOrder
            ? BinaryPrimitives.ReadDoubleLittleEndian(span)
            : BinaryPrimitives.ReadDoubleBigEndian(span);
    }

    private static double ReadDoubleAt(byte[] buf, int offset, JplEndianness endianness)
    {
        var span = buf.AsSpan(offset, 8);
        return endianness == JplEndianness.NativeOrder
            ? BinaryPrimitives.ReadDoubleLittleEndian(span)
            : BinaryPrimitives.ReadDoubleBigEndian(span);
    }
}
