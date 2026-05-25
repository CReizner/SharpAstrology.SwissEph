// Ported from swisseph-master/swemmoon.c — swi_intp_apsides (#L1854-L1932).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   ComputePolar       — swi_intp_apsides     (swemmoon.c#L1854-L1932)
//   SpeedIntervalDays  — speed_intv = 0.1     (sweph.c#L5605)
//   Dispatcher         — intp_apsides         (sweph.c#L5598) wraps the speed step
//                                              and standard frame pipeline.
//   mods3600 reduction at the save step is preserved (swemmoon.c#L1874-L1877).
//   Mods3600 helper    — swemmoon.c#L1730

using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Moshier;

/// <summary>
/// Moshier interpolated lunar apogee and perigee. Newton search that fits a
/// parabola through three radial samples around the current mean-anomaly guess
/// and slides toward the extremum. Five and four iterations respectively reduce
/// the residual to well below 0.001″ in longitude.
/// </summary>
/// <remarks>
/// Output is geocentric polar (longitude rad, latitude rad, distance AU)
/// referred to the mean ecliptic and equinox of date — the same frame emitted
/// by <see cref="MoshierMoonTheory.ComputePolarOfDate"/>.
/// </remarks>
internal static class MoshierInterpolatedApsides
{
    /// <summary>Speed step in days, used by the dispatcher's finite-difference velocity.</summary>
    internal const double SpeedIntervalDays = 0.1;

    // swemmoon.c#L1862-L1872 — synodic-period ratios. zMP is the anomalistic
    // month length in days; the f* fractions convert one anomalistic-month of
    // arc-second slip on MP into the corresponding slip on each other angle.
    private const double ZMP = 27.55454988;
    private const double FnF = 27.212220817 / ZMP;
    private const double Fd = 29.530588835 / ZMP;
    private const double FlP = 27.321582 / ZMP;
    private const double Fm = 365.2596359 / ZMP;
    private const double FVe = 224.7008001 / ZMP;
    private const double FEa = 365.2563629 / ZMP;
    private const double FMa = 686.9798519 / ZMP;
    private const double FJu = 4332.589348 / ZMP;
    private const double FSa = 10759.22722 / ZMP;

    /// <summary>Discriminator for the two interpolated apsides.</summary>
    internal enum Apside
    {
        /// <summary>Lunar apogee: seed mean anomaly = 648 000″ (180°).</summary>
        Apogee,
        /// <summary>Lunar perigee: seed mean anomaly = 0″.</summary>
        Perigee,
    }

    /// <summary>
    /// Geocentric mean ecliptic-of-date polar (longitude rad, latitude rad,
    /// distance AU) of the interpolated lunar apogee or perigee at
    /// <paramref name="jdEt"/>.
    /// </summary>
    /// <remarks>
    /// Inside the iteration loop each element is shifted by ±<c>dd</c> scaled
    /// by the synodic-period ratio so the perturbation series is sampled at
    /// three points around the current MP guess; the parabola vertex of
    /// (r₀, r₁, r₂) becomes the next MP.
    /// </remarks>
    internal static Vec3 ComputePolar(double jdEt, Apside which)
    {
        var raw = MoshierMoonTheory.ComputeMoonAndPlanetElements(jdEt);

        // Save lunar elements after mods3600 reduction; planet elements are
        // kept raw — swemmoon.c#L1874-L1877.
        var sNF = Mods3600(raw.NF);
        var sD = Mods3600(raw.D);
        var sLP = Mods3600(raw.SWELP);
        var sMP = Mods3600(raw.MP);

        // Seed MP at the apsis: perigee = 0, apogee = 648 000″ (180°).
        // niter = 5 for perigee, 4 for apogee — swemmoon.c#L1880-L1881.
        var mp = which == Apside.Perigee ? 0.0 : 648_000.0;
        var niter = which == Apside.Perigee ? 5 : 4;
        var dd = 18_000.0;
        var pol = default(Vec3);

        for (var iii = 0; iii <= niter; iii++)
        {
            var dmp = sMP - mp;
            var mLP = sLP - dmp;
            var mNF = sNF - dmp;
            var mD = sD - dmp;
            var mMP = sMP - dmp;

            double r0 = 0.0, r1 = 0.0, r2 = 0.0;
            for (var ii = 0; ii <= 2; ii++)
            {
                var k = ii - 1; // -1, 0, +1
                var elements = new MoshierMoonTheory.MoonAndPlanetElements(
                    SWELP: mLP + k * dd / FlP,
                    NF: mNF + k * dd / FnF,
                    MP: mMP + k * dd,
                    M: raw.M + k * dd / Fm,
                    D: mD + k * dd / Fd,
                    Ve: raw.Ve + k * dd / FVe,
                    Ea: raw.Ea + k * dd / FEa,
                    Ma: raw.Ma + k * dd / FMa,
                    Ju: raw.Ju + k * dd / FJu,
                    Sa: raw.Sa + k * dd / FSa);

                var p = MoshierMoonTheory.ComputePolarWithElements(jdEt, in elements);

                if (ii == 1) pol = p;
                if (ii == 0) r0 = p.Z;
                else if (ii == 1) r1 = p.Z;
                else r2 = p.Z;
            }

            // Parabola vertex of (r0, r1, r2) sampled at indices -1, 0, +1
            // (in units of dd). The C code multiplies by dd then subtracts dd
            // to shift the vertex onto the same axis as the samples.
            var cMp = (1.5 * r0 - 2.0 * r1 + 0.5 * r2) / (r0 + r2 - 2.0 * r1);
            cMp = cMp * dd - dd;

            mp += cMp; // mMP += cMp; MP = mMP — swemmoon.c#L1928-L1929.
            dd /= 10.0;
        }

        return pol;
    }

    /// <summary>
    /// Reduce arc-seconds modulo 1 296 000″ (= 360°), keeping the result non-negative.
    /// </summary>
    private static double Mods3600(double x)
        => x - 1_296_000.0 * System.Math.Floor(x / 1_296_000.0);
}
