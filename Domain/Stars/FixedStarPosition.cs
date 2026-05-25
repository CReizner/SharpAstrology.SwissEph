// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Domain.Stars;

/// <summary>
/// Apparent position of a fixed star, mirroring the
/// <c>swe_fixstar2</c> output triple <c>xx[0..5]</c> together with the
/// canonical name written back into the input pointer. Coordinate
/// interpretation follows the requested
/// <see cref="SharpAstrology.SwissEphemerides.Domain.Stars.FixedStarPosition"/>
/// flag combination (cartesian when XYZ, polar otherwise; longitude/latitude
/// in degrees unless the radians bit is set).
/// </summary>
/// <param name="Name">Canonical name in <c>"trad,bayer"</c> form.
///   Mirrors the value the C function writes into the input <c>star</c> buffer.</param>
/// <param name="Position">Position triple. In polar form: longitude (or RA),
///   latitude (or declination), distance in AU. In cartesian form: X, Y, Z
///   in AU.</param>
/// <param name="Velocity">Velocity triple in the same coordinate system as
///   <paramref name="Position"/>.</param>
/// <param name="Distance">Distance in AU. Mirrors <c>xx[2]</c> when Position
///   carries the polar triple; equal to <c>|Position|</c> when XYZ is
///   requested.</param>
/// <param name="Magnitude">Apparent V magnitude carried over from the
///   catalogue record (<c>fixed_star.mag</c>).</param>
public readonly record struct FixedStarPosition(
    string Name,
    Vec3 Position,
    Vec3 Velocity,
    double Distance,
    double Magnitude);
