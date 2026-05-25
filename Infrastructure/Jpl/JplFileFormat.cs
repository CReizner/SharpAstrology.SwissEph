// Ported from swisseph-master/swejpl.h:68-82 (J_* body indices) and
// swejpl.c (file-format constants). Original license: see LICENSE.SwissEph.txt.
//
// C reference (Swiss Ephemeris):
//   Jpl* body indices              — J_* macros            (swejpl.h:68-82)
//   File-header layout offsets     — swejpl.c:206-271
//   EphemerisJdMin/Max, SegmentDaysMin/Max — swejpl.c:228
//   RecordSizeMin/Max              — fsizer                (swejpl.c:322)
//   De102FixedRecordSize / De102ReportedRecordSize — fsizer (swejpl.c:292)
//   JplEndianness                  — do_reorder            (swejpl.c:218-221)
//   JplCoeffPointer                — ipt[] triplet         (swejpl.c:255-271)
//   JplHeader                      — jpl_save struct       (swejpl.c:97-110)

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Jpl;

/// <summary>
/// Compile-time constants describing the JPL DE binary <c>.eph</c> file format.
/// </summary>
internal static class JplFileFormat
{
    /// <summary>JPL Mercury index inside the <c>ipt[]</c> table.</summary>
    public const int JplMercury = 0;
    /// <summary>JPL Venus index inside the <c>ipt[]</c> table.</summary>
    public const int JplVenus = 1;
    /// <summary>JPL Earth (= EMB inside file; geocentric Earth requires Moon-offset).</summary>
    public const int JplEarth = 2;
    /// <summary>JPL Mars index inside the <c>ipt[]</c> table.</summary>
    public const int JplMars = 3;
    public const int JplJupiter = 4;
    public const int JplSaturn = 5;
    public const int JplUranus = 6;
    public const int JplNeptune = 7;
    public const int JplPluto = 8;
    /// <summary>Geocentric Moon record (offset 9 in <c>ipt[]</c>).</summary>
    public const int JplMoon = 9;
    /// <summary>Barycentric Sun record (offset 10 in <c>ipt[]</c>).</summary>
    public const int JplSun = 10;
    /// <summary>Solar-system barycenter (always zero, no <c>ipt</c>).</summary>
    public const int JplSbary = 11;
    /// <summary>Earth-Moon barycenter (alias of <see cref="JplEarth"/>).</summary>
    public const int JplEmb = 12;
    /// <summary>Nutations (Δψ, Δε), if on file (ipt[34] &gt; 0).</summary>
    public const int JplNut = 13;
    /// <summary>Librations, if on file (ipt[37] &gt; 0).</summary>
    public const int JplLib = 14;

    /// <summary>Length of the title block in bytes (3 lines × 84 chars).</summary>
    public const int TitleBytes = 252;
    /// <summary>Length of the constant-name block: 400 names × 6 chars.</summary>
    public const int ConstantNameBytes = 6 * 400;
    /// <summary>Number of named constants stored in the second record.</summary>
    public const int ConstantCount = 400;
    /// <summary>Number of <c>ipt</c> entries (13 bodies × 3 fields = 39, but
    /// the file stores 12×3=36 followed by 3 librations after numde).</summary>
    public const int IptEntriesInHeader = 36;

    /// <summary>Lower allowed value of <c>ss[0]</c> (start JD).</summary>
    public const double EphemerisJdMin = -5_583_942.0;
    /// <summary>Upper allowed value of <c>ss[1]</c> (end JD).</summary>
    public const double EphemerisJdMax = 9_025_909.0;
    /// <summary>Lower bound of allowed segment length in days.</summary>
    public const double SegmentDaysMin = 1.0;
    /// <summary>Upper bound of allowed segment length in days.</summary>
    public const double SegmentDaysMax = 200.0;

    /// <summary>Lower bound on plausible record size.</summary>
    public const int RecordSizeMin = 1_000;
    /// <summary>Upper bound on plausible record size.</summary>
    public const int RecordSizeMax = 5_000;
    /// <summary>DE102 quirk: file lies about ksize; fixed to DE200's 1652.</summary>
    public const int De102FixedRecordSize = 1652;
    /// <summary>DE102 reported ksize that needs the fixup.</summary>
    public const int De102ReportedRecordSize = 1546;

    /// <summary>Number of single-precision words per record entry (nrecl=4).</summary>
    public const int RecordWordSizeBytes = 4;

    // ---------- Known DE numbers (informational, mirrors the file's `numde`) ----------
    public const int De102 = 102;
    public const int De200 = 200;
    public const int De403 = 403;
    public const int De404 = 404;
    public const int De405 = 405;
    public const int De406 = 406;
    public const int De410 = 410;
    public const int De413 = 413;
    public const int De414 = 414;
    public const int De418 = 418;
    public const int De421 = 421;
    public const int De431 = 431;
    public const int De441 = 441;
}

/// <summary>
/// Endianness state of a parsed JPL <c>.eph</c> file. Set by the file-header
/// plausibility check in <see cref="JplFileReader"/>: the segment-size
/// <c>ss[2]</c> (bytes 504..512) is read in native order; if it lies outside
/// <c>[1, 200]</c> the bytes are flipped and re-tried.
/// </summary>
internal enum JplEndianness
{
    /// <summary>File and host have the same byte order — no reorder needed.</summary>
    NativeOrder = 0,
    /// <summary>File is opposite-endian to host — bytes must be reversed.</summary>
    SwappedOrder = 1,
}

/// <summary>
/// Per-body coefficient layout descriptor inside a JPL file. One triplet of
/// <c>ipt[i*3..i*3+3]</c>: buffer offset, Chebyshev coefficients per component,
/// number of intervals per segment.
/// </summary>
internal readonly record struct JplCoeffPointer(int BufferOffset, int CoefficientsPerComponent, int IntervalsPerSegment);

/// <summary>
/// Parsed file-header for a JPL DE <c>.eph</c> file.
/// </summary>
internal sealed class JplHeader
{
    public required string FileName { get; init; }
    /// <summary>"DE number" stored at offset (252 + 2400 + 24 + 4) = bytes 2680..2684 in record 0.</summary>
    public required int DeNumber { get; init; }
    public required JplEndianness Endianness { get; init; }

    /// <summary>JD of first available segment.</summary>
    public required double JdStart { get; init; }
    /// <summary>JD of last available segment.</summary>
    public required double JdEnd { get; init; }
    /// <summary>Length in days of one Chebyshev interpolation segment.</summary>
    public required double SegmentLengthDays { get; init; }

    /// <summary>Astronomical unit in km (file's authoritative value).</summary>
    public required double AstronomicalUnitKm { get; init; }
    /// <summary>Earth/Moon mass ratio (file's authoritative value).</summary>
    public required double EarthMoonRatio { get; init; }

    /// <summary>13 body-coefficient pointers (offsets are 1-based as on disk).</summary>
    public required JplCoeffPointer[] BodyPointers { get; init; }
    /// <summary>Nutation pointer at <c>ipt[33..35]</c> — (offset, ncf, na). All zero if file has no nutations.</summary>
    public required JplCoeffPointer Nutations { get; init; }
    /// <summary>Libration pointer at <c>ipt[36..38]</c>. All zero if file has no librations.</summary>
    public required JplCoeffPointer Librations { get; init; }

    /// <summary>Record size in single-precision words (= bytes / 4).</summary>
    public required int RecordSizeWords { get; init; }
    /// <summary>Record size in bytes (record 0/1 hold header & constants, record 2.. hold segments).</summary>
    public int RecordSizeBytes => RecordSizeWords * JplFileFormat.RecordWordSizeBytes;
    /// <summary>Number of segments (= (JdEnd − JdStart) / SegmentLengthDays).</summary>
    public required int SegmentCount { get; init; }
    /// <summary>Number of doubles per record (RecordSizeBytes / 8).</summary>
    public required int DoublesPerRecord { get; init; }
    /// <summary>Total byte length of the file (cached at open time).</summary>
    public required long FileLength { get; init; }

    /// <summary>The 400 named constant values stored in record 1.</summary>
    public required double[] ConstantValues { get; init; }
    /// <summary>The 400 6-byte names of the constants stored in record 0.</summary>
    public required string[] ConstantNames { get; init; }
}

/// <summary>
/// Thrown when a JPL <c>.eph</c> file fails its self-consistency checks
/// (bad endian sentinel, declared length mismatch, segment-bound mismatch,
/// or implausible header values).
/// </summary>
public sealed class JplFileFormatException : EphemerisException
{
    public JplFileFormatException(string message) : base($"JPL ephemeris file is malformed: {message}") { }
    public JplFileFormatException(string message, Exception inner) : base($"JPL ephemeris file is malformed: {message}", inner) { }
}

/// <summary>
/// Thrown when the JPL ephemeris file requested by configuration is missing.
/// </summary>
public sealed class JplFileNotFoundException : EphemerisException
{
    public JplFileNotFoundException(string path) : base($"JPL ephemeris file not found: {path}")
    {
        Path = path;
    }

    public string Path { get; }
}
