// Ported from swisseph-master/sweph.h:125-194 + sweph.c read_const + get_new_segment.
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   Sei* body / file constants  — SEI_* macros        (sweph.h:125-194)
//   FileTestEndian sentinel     — read_const (.se1 file header)
//   Se1Header                   — file_data struct    (sweph.h:708-720)
//   Se1PlanetData               — plan_data struct    (sweph.h:610-656; read_const at sweph.c:4798-4873)
//   Se1GeneralConstants         — gen_const struct    (sweph.h:722-728)

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;

namespace SharpAstrology.SwissEphemerides.Infrastructure.SwissEph;

/// <summary>
/// Compile-time constants for the Astrodienst <c>.se1</c> binary file format.
/// </summary>
internal static class Se1FileFormat
{
    /// <summary>Internal Sun / EMB / Earth slot.</summary>
    public const int SeiEarth = 0;
    /// <summary>Internal Sun slot (same numeric value as Earth).</summary>
    public const int SeiSun = 0;
    /// <summary>Internal EMB slot (same numeric value as Earth).</summary>
    public const int SeiEmb = 0;
    public const int SeiMoon = 1;
    public const int SeiMercury = 2;
    public const int SeiVenus = 3;
    public const int SeiMars = 4;
    public const int SeiJupiter = 5;
    public const int SeiSaturn = 6;
    public const int SeiUranus = 7;
    public const int SeiNeptune = 8;
    public const int SeiPluto = 9;
    public const int SeiSunBary = 10;
    public const int SeiAnyBody = 11;
    public const int SeiChiron = 12;
    public const int SeiPholus = 13;
    public const int SeiCeres = 14;
    public const int SeiPallas = 15;
    public const int SeiJuno = 16;
    public const int SeiVesta = 17;

    public const int SeiNPlanets = 18;

    /// <summary>"Heliocentric" body flag.</summary>
    public const int FlagHelio = 1;
    /// <summary>"Reference ellipse rotated" flag.</summary>
    public const int FlagRotate = 2;
    /// <summary>"Reference ellipse present" flag.</summary>
    public const int FlagEllipse = 4;
    /// <summary>"EMB-heliocentric" flag.</summary>
    public const int FlagEmbHelio = 8;

    /// <summary>Planet-file index (<c>sepl_*.se1</c>).</summary>
    public const int FileIndexPlanet = 0;
    /// <summary>Moon-file index (<c>semo_*.se1</c>).</summary>
    public const int FileIndexMoon = 1;
    /// <summary>Main-asteroid file index (<c>seas_*.se1</c>).</summary>
    public const int FileIndexMainAst = 2;
    /// <summary>Numbered-asteroid file index (<c>ast*/se*.se1</c>).</summary>
    public const int FileIndexAnyAst = 3;

    /// <summary>Body identifier &gt; this number is an asteroid id.</summary>
    public const int AsteroidOffset = 10000;
    /// <summary>Body identifiers between this and <see cref="AsteroidOffset"/> are planetary moons.</summary>
    public const int PlanetaryMoonOffset = 9000;

    /// <summary>Maximum number of body slots in a single <c>.se1</c> file.</summary>
    public const int MaxPlanetsPerFile = 50;

    /// <summary>Endianness sentinel — the ASCII bytes 'a','b','c'.</summary>
    public const int FileTestEndian = 0x00616263;

    /// <summary>Polynomial order is at most this many - 1.</summary>
    public const int MaxPolynomialOrder = 40;

    /// <summary>Centuries per ephemeris file (one .se1 covers 6 centuries).</summary>
    public const int CenturiesPerFile = 6;

    /// <summary>Suffix appended to all SwissEph ephemeris files.</summary>
    public const string FileSuffix = "se1";
}

/// <summary>
/// Endianness state of an opened <c>.se1</c> file. Set after parsing the first
/// 4-byte test integer near the start of the header (ASCII "abc" + 0x00).
/// </summary>
internal enum Se1Endianness
{
    /// <summary>File and host have the same byte order — no reorder needed.</summary>
    NativeOrder = 0,
    /// <summary>File is opposite-endian to host — bytes must be reversed on every read.</summary>
    SwappedOrder = 1,
}

/// <summary>
/// One parsed <c>.se1</c> file header plus the per-body planet records.
/// </summary>
internal sealed class Se1Header
{
    public required string FileName { get; init; }
    public required int FileVersion { get; init; }
    public required int JplDeNumber { get; init; }
    public required double JdStart { get; init; }
    public required double JdEnd { get; init; }
    public required int PlanetCount { get; init; }
    public required int[] PlanetIds { get; init; }
    public required Se1Endianness Endianness { get; init; }
    public required Se1PlanetData[] Planets { get; init; }
    public required string AsteroidName { get; init; }
    public required long FileLength { get; init; }
    public required uint HeaderCrc { get; init; }
    public required Se1GeneralConstants Constants { get; init; }
}

/// <summary>
/// General constants block written near the start of every <c>.se1</c> file.
/// </summary>
internal readonly record struct Se1GeneralConstants(
    double SpeedOfLight,
    double AstronomicalUnit,
    double HelioGravConstant,
    double EarthMoonRatio,
    double SunRadius);

/// <summary>
/// One planet entry in the file's per-body table. Read once at file open and
/// used to compute the position of an arbitrary segment.
/// </summary>
internal sealed class Se1PlanetData
{
    /// <summary>Internal body number from the file.</summary>
    public required int BodyId { get; init; }
    /// <summary>Bit-packed flags: <see cref="Se1FileFormat.FlagHelio"/> | Rotate | Ellipse | EmbHelio.</summary>
    public required int Flags { get; init; }
    /// <summary>Number of Chebyshev coefficients per segment / coordinate (= polynomial order + 1).</summary>
    public required int CoefficientCount { get; init; }
    /// <summary>File offset of this body's per-segment index table.</summary>
    public required int IndexOffset { get; init; }
    /// <summary>Number of segments for this body in this file.</summary>
    public required int SegmentCount { get; init; }
    /// <summary>JD where this body's data starts (may differ from file global start).</summary>
    public required double JdStart { get; init; }
    /// <summary>JD where this body's data ends.</summary>
    public required double JdEnd { get; init; }
    /// <summary>Length (in days) of one Chebyshev segment.</summary>
    public required double SegmentLengthDays { get; init; }
    /// <summary>Reference epoch for the orbital elements.</summary>
    public required double ElementsEpoch { get; init; }
    public required double Prot { get; init; }
    public required double DProt { get; init; }
    public required double Qrot { get; init; }
    public required double DQrot { get; init; }
    public required double Peri { get; init; }
    public required double DPeri { get; init; }
    /// <summary>Normalisation factor of Chebyshev coefficients.</summary>
    public required double RMax { get; init; }
    /// <summary>Reference-ellipse Chebyshev coefficients (length 2 * <see cref="CoefficientCount"/>),
    /// or empty if <see cref="Flags"/> &amp; <see cref="Se1FileFormat.FlagEllipse"/> = 0.</summary>
    public required double[] ReferenceEllipse { get; init; }
}
