// Ported from swisseph-master/swemmoon.c — corr_mean_node / corr_mean_apog
// (#L1466-#L1485, #L1536-#L1556) and swi_mean_node / swi_mean_apog
// (#L1488-#L1534, #L1558-#L1624). Original license: see LICENSE.SwissEph.txt
// at the repo root.
//
// C reference (Swiss Ephemeris):
//   ComputeMeanNodePolar       — swi_mean_node              (swemmoon.c#L1493-L1534)
//   ComputeMeanApogeePolar     — swi_mean_apog              (swemmoon.c#L1564-L1624)
//   Correction                 — corr_mean_node / corr_mean_apog (swemmoon.c#L1470 / #L1540)
//   MoshNdEphStartJd / EndJd   — MOSHNDEPH_START / MOSHNDEPH_END (sweph.h:225-226)
//   MeanNodeSpeedIntervalDays  — MEAN_NODE_SPEED_INTV       (sweph.h#L300)
//   Empty-focus offset of ~4 700 km (~40′ oscillation) of the apogee is
//   neglected, matching the C library's documented choice. Distance is fixed
//   to the mean lunar distance (no per-epoch radius derivation), see swemmoon.c#L1521.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Moshier;

/// <summary>
/// Moshier-fit mean lunar node and mean lunar apogee ("Lilith" / Black Moon).
/// Both reuse the <see cref="MoshierMoonTheory.ComputeMeanElements"/> fundamental
/// arguments (SWELP, NF, MP) and apply a 100-Gregorian-year-step linear correction
/// table fitted against JPL DE-431; outside that window the correction is zero.
/// </summary>
/// <remarks>
/// Outputs are geocentric polar (longitude, latitude, distance) referred to the
/// mean ecliptic and equinox of date — no nutation, aberration, or light-time
/// correction. The supported JD range is <see cref="MoshNdEphStartJd"/> ..
/// <see cref="MoshNdEphEndJd"/>; the caller is responsible for gating it.
/// </remarks>
internal static class MoshierMeanNodeAndApogee
{
    /// <summary>Mean lunar node JD range start.</summary>
    internal const double MoshNdEphStartJd = -3_100_015.5;
    /// <summary>Mean lunar node JD range end.</summary>
    internal const double MoshNdEphEndJd = 8_000_016.5;

    /// <summary>Speed step in days, used by the dispatcher's finite-difference velocity.</summary>
    internal const double MeanNodeSpeedIntervalDays = 0.001;

    private const double Str = MoshierPlanetTheory.Str;

    /// <summary>
    /// Linearly interpolates a 100-y-step correction table at <paramref name="jdEt"/>.
    /// Returns 0 outside the JPL DE-431 window.
    /// </summary>
    private static double Correction(double[] table, double jdEt)
    {
        if (jdEt < MoshierLunarTables.JplDe431StartJd || jdEt > MoshierLunarTables.JplDe431EndJd)
            return 0.0;

        var dJ = jdEt - MoshierLunarTables.CorrJdT0Greg;
        var i = (int)System.Math.Floor(dJ / MoshierLunarTables.CorrDaysPerCentury);
        var frac = (dJ - i * MoshierLunarTables.CorrDaysPerCentury) / MoshierLunarTables.CorrDaysPerCentury;
        var c0 = table[i];
        var c1 = table[i + 1];
        return c0 + frac * (c1 - c0);
    }

    /// <summary>
    /// Mean lunar node, geocentric polar of date at <paramref name="jdEt"/>:
    /// (longitude rad, 0, distance AU). Distance is fixed to the mean lunar
    /// distance.
    /// </summary>
    internal static Vec3 ComputeMeanNodePolar(double jdEt)
    {
        var elems = MoshierMoonTheory.ComputeMeanElements(jdEt);
        var dcorArcsec = Correction(MoshierLunarTables.MeanNodeCorr, jdEt) * 3600.0;
        var lon = Mod2Pi((elems.SWELP - elems.NF - dcorArcsec) * Str);
        var distAu = AstronomicalConstants.MoonMeanDistanceMeters / AstronomicalConstants.AstronomicalUnitMeters;
        return new Vec3(lon, 0.0, distAu);
    }

    /// <summary>
    /// Mean lunar apogee (Black Moon / Lilith), geocentric polar of date at
    /// <paramref name="jdEt"/>: (longitude rad, latitude rad, distance AU).
    /// </summary>
    /// <remarks>
    /// The apogee is the antipode of the perigee on the mean lunar ellipse;
    /// because the mean lunar orbit is inclined to the ecliptic, the apogee is
    /// projected onto the ecliptic plane via an X-axis rotation by
    /// <c>-MOON_MEAN_INCL</c> followed by reapplication of the mean-node longitude.
    /// </remarks>
    internal static Vec3 ComputeMeanApogeePolar(double jdEt)
    {
        var elems = MoshierMoonTheory.ComputeMeanElements(jdEt);

        // Initial apogee polar in the mean-orbit-of-date frame:
        //   λ = (SWELP - MP)·STR + π   (antipode of mean perigee)
        //   β = 0
        //   r = a · (1 + e), in AU
        var lon0 = Mod2Pi((elems.SWELP - elems.MP) * Str + AstronomicalConstants.Pi);
        var distAu = AstronomicalConstants.MoonMeanDistanceMeters
                     * (1.0 + AstronomicalConstants.MoonMeanEccentricity)
                     / AstronomicalConstants.AstronomicalUnitMeters;

        // Apply the mean-apsides correction, then project onto the ecliptic.
        var dcorApogRad = Correction(MoshierLunarTables.MeanApsisCorr, jdEt) * AstronomicalConstants.DegToRad;
        var lon = Mod2Pi(lon0 - dcorApogRad);

        // Mean-node longitude (with its own correction in radians).
        var node = (elems.SWELP - elems.NF) * Str;
        var dcorNodeRad = Correction(MoshierLunarTables.MeanNodeCorr, jdEt) * AstronomicalConstants.DegToRad;
        node = Mod2Pi(node - dcorNodeRad);

        // Subtract the node, rotate the apogee from orbit-plane to ecliptic
        // around X by +inclination (swi_coortrf with eps = -inclination), add
        // the node back. swi_coortrf at swephlib.c#L279-L290:
        //   x'[1] =  x[1]·cos(eps) + x[2]·sin(eps)
        //   x'[2] = -x[1]·sin(eps) + x[2]·cos(eps)
        // With eps = -inclination, cos(eps)=cos(incl), sin(eps)=-sin(incl):
        //   y' =  y·cos(incl) - z·sin(incl)
        //   z' =  y·sin(incl) + z·cos(incl)
        var lonShifted = Mod2Pi(lon - node);
        var cart = Polar.PolarToCartesian(new Vec3(lonShifted, 0.0, distAu));
        var inclRad = AstronomicalConstants.MoonMeanInclinationDeg * AstronomicalConstants.DegToRad;
        var sinIncl = System.Math.Sin(inclRad);
        var cosIncl = System.Math.Cos(inclRad);
        var rotated = new Vec3(
            cart.X,
            cart.Y * cosIncl - cart.Z * sinIncl,
            cart.Y * sinIncl + cart.Z * cosIncl);
        var polar = Polar.CartesianToPolar(rotated);
        var lonFinal = Mod2Pi(polar.X + node);
        return new Vec3(lonFinal, polar.Y, polar.Z);
    }

    private static double Mod2Pi(double a)
    {
        var x = a - AstronomicalConstants.TwoPi * System.Math.Floor(a / AstronomicalConstants.TwoPi);
        return x;
    }
}
