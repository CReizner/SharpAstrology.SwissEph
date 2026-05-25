// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Application.Houses;

/// <summary>
/// Result of a house-system computation. Mirrors the C-side <c>cusp[]</c>
/// (12 entries — or 36 for Gauquelin sectors) plus the eight ascmc points
/// listed in <c>swe_houses_armc_ex2</c> at swehouse.c#L194-L202.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Cusps"/> is 1-indexed: <c>Cusps[0]</c> is unused (kept zero so
/// the slot mapping mirrors the C API), <c>Cusps[1..12]</c> hold the houses
/// (<c>Cusps[1..36]</c> for Gauquelin).
/// </para>
/// <para>
/// Speed arrays are <c>null</c> unless the call requested speeds; their
/// length matches <see cref="Cusps"/>.
/// </para>
/// <para>
/// For zero-allocation hot paths use
/// <see cref="HouseService.ComputeFromArmcInto"/> which writes directly
/// into caller-owned <see cref="Span{T}"/>s and skips this allocation.
/// </para>
/// </remarks>
public sealed record HouseResult
{
    public required double[] Cusps { get; init; }

    public required double Ascendant { get; init; }
    public required double MidHeaven { get; init; }
    public required double Armc { get; init; }
    public required double Vertex { get; init; }
    public required double EquatorialAscendant { get; init; }
    public required double CoAscendantKoch { get; init; }
    public required double CoAscendantMunkasey { get; init; }
    public required double PolarAscendant { get; init; }

    /// <summary>
    /// Sun declination filled in by the wrapper for Sunshine houses; zero
    /// otherwise. Mirrors the C library's <c>ascmc[9]</c> slot.
    /// </summary>
    public double SunDeclination { get; init; }

    public double[]? CuspSpeeds { get; init; }
    public double AscendantSpeed { get; init; }
    public double MidHeavenSpeed { get; init; }
    public double ArmcSpeed { get; init; }
    public double VertexSpeed { get; init; }
    public double EquatorialAscendantSpeed { get; init; }
    public double CoAscendantKochSpeed { get; init; }
    public double CoAscendantMunkaseySpeed { get; init; }
    public double PolarAscendantSpeed { get; init; }

    /// <summary>
    /// Non-fatal warning emitted by the C library (e.g. "within polar
    /// circle, switched to Porphyry"); <c>null</c> on success.
    /// </summary>
    public string? Warning { get; init; }

    /// <summary>
    /// True when the requested system fell back to Porphyry due to a polar-
    /// circle constraint. Mirrors the C return value <c>ERR</c>.
    /// </summary>
    public bool FellBackToPorphyry { get; init; }
}
