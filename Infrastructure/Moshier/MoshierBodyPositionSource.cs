// Ported from swisseph-master/swemplan.c + swemmoon.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   ComputeEarthState           — swi_moshplan, do_earth branch (swemplan.c:308-339)
//                                  Earth = -(EMB ellipse - Moon geocentric / EMRAT)
//                                  steps swemplan.c:317 / :318
//   EmbofsMosh                  — embofs_mosh                   (swemplan.c:416-491)
//                                  Moon geocentric short series
//   Mean-node / mean-apogee speed-step pattern — sweph.c#L869-L876 / #L908-L916
//   Interpolated-apsides loop   — intp_apsides                  (sweph.c#L5598-L5650)
//   DegreeNormalize / RadiansDifferenceNormalized — swe_degnorm / swe_difrad2n

using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Moshier;

/// <summary>
/// File-less Moshier ephemeris source. Implements
/// <see cref="IBodyPositionSource"/> for Sun, Moon, and Mercury through Pluto.
/// Earth is handled as a separate request, derived from the Earth-Moon
/// barycenter analytical theory plus the Moon's geocentric offset.
/// </summary>
internal sealed class MoshierBodyPositionSource : IBodyPositionSource
{
    /// <inheritdoc />
    public EphemerisSource Kind => EphemerisSource.Moshier;

    /// <inheritdoc />
    public bool CanProvide(CelestialBody body) => body switch
    {
        CelestialBody.Sun
            or CelestialBody.Moon
            or CelestialBody.Mercury
            or CelestialBody.Venus
            or CelestialBody.Earth
            or CelestialBody.Mars
            or CelestialBody.Jupiter
            or CelestialBody.Saturn
            or CelestialBody.Uranus
            or CelestialBody.Neptune
            or CelestialBody.Pluto
            or CelestialBody.MeanNode
            or CelestialBody.MeanApogee
            or CelestialBody.InterpolatedApogee
            or CelestialBody.InterpolatedPerigee => true,
        _ => false,
    };

    /// <inheritdoc />
    public BodyState Compute(CelestialBody body, JulianDay jdEt, EphemerisFlags flags)
    {
        if (!CanProvide(body))
            throw new UnsupportedBodyException(body, EphemerisSource.Moshier);

        var jd = jdEt.Value;
        var inSpeed = (flags & (EphemerisFlags.Speed | EphemerisFlags.Speed3)) != 0;

        if (body == CelestialBody.Moon)
        {
            EnsureMoonInRange(jd);
            // Moon raw output is GEOCENTRIC ecliptic of date (not J2000).
            if (inSpeed)
            {
                var (p, v) = MoshierMoonTheory.ComputeCartesianOfDateWithSpeed(jd);
                return new BodyState(p, v, p.Length, EphemerisSource.Moshier, BodyStateFrame.GeocentricEclipticOfDate);
            }
            else
            {
                var p = MoshierMoonTheory.ComputeCartesianOfDate(jd);
                return new BodyState(p, default, p.Length, EphemerisSource.Moshier, BodyStateFrame.GeocentricEclipticOfDate);
            }
        }

        if (body == CelestialBody.MeanNode || body == CelestialBody.MeanApogee)
        {
            EnsureMeanNodeApogeeInRange(jd);
            return ComputeMeanNodeOrApogee(body, jd, inSpeed);
        }

        if (body == CelestialBody.InterpolatedApogee || body == CelestialBody.InterpolatedPerigee)
        {
            EnsureInterpolatedApsidesInRange(jd);
            return ComputeInterpolatedApsides(body, jd, inSpeed);
        }

        if (body == CelestialBody.Sun)
        {
            // Sun is heliocentric origin: a vector of zero.
            return new BodyState(Vec3.Zero, Vec3.Zero, 0.0, EphemerisSource.Moshier, BodyStateFrame.HeliocentricJ2000Ecliptic);
        }

        EnsurePlanetInRange(jd);

        if (body == CelestialBody.Earth)
        {
            // Earth = EMB - moonOffset / (mratio + 1). swemplan.c:308-339.
            // Frame is J2000-EQUATOR (matches the C library's swi_moshplan which
            // rotates xe to equator-of-2000 via swi_coortrf2(... -seps2000, ceps2000)).
            return ComputeEarth(jd, inSpeed);
        }

        var idx = MapPlanetIndex(body);
        if (inSpeed)
        {
            var (p, v) = MoshierPlanetTheory.ComputeCartesianJ2000WithSpeed(idx, jd);
            return new BodyState(p, v, p.Length, EphemerisSource.Moshier, BodyStateFrame.HeliocentricJ2000Ecliptic);
        }
        else
        {
            var p = MoshierPlanetTheory.ComputeCartesianJ2000(idx, jd);
            return new BodyState(p, default, p.Length, EphemerisSource.Moshier, BodyStateFrame.HeliocentricJ2000Ecliptic);
        }
    }

    /// <summary>
    /// Maps <see cref="CelestialBody"/> values for the eight non-Earth planets
    /// (Mercury..Pluto) to the C-internal Moshier table index 0..8.
    /// Earth is special-cased upstream.
    /// </summary>
    private static int MapPlanetIndex(CelestialBody body) => body switch
    {
        CelestialBody.Mercury => 0,
        CelestialBody.Venus => 1,
        CelestialBody.Mars => 3,
        CelestialBody.Jupiter => 4,
        CelestialBody.Saturn => 5,
        CelestialBody.Uranus => 6,
        CelestialBody.Neptune => 7,
        CelestialBody.Pluto => 8,
        _ => throw new System.ArgumentException($"Body {body} has no Moshier planet table.", nameof(body)),
    };

    /// <summary>
    /// Compute Earth heliocentric J2000-equator state:
    /// 1. Pull EMB heliocentric in J2000 ecliptic polar.
    /// 2. Polar → cartesian.
    /// 3. Rotate ecliptic-J2000 → equator-J2000 with mean obliquity at J2000.
    /// 4. Subtract the Moon-of-date contribution (precessed to equator-J2000).
    /// </summary>
    private static BodyState ComputeEarth(double jdEt, bool wantVelocity)
    {
        // Mean obliquity at J2000 (model-default).
        var eps2000 = Precession.MeanObliquity(AstronomicalConstants.J2000);
        var seps2000 = System.Math.Sin(eps2000);
        var ceps2000 = System.Math.Cos(eps2000);

        // 1. EMB heliocentric J2000-ecliptic cartesian.
        var pEmb = MoshierPlanetTheory.ComputeCartesianJ2000(2, jdEt);
        // 2 + 3. Rotate ecl-J2000 → equ-J2000 via swi_coortrf2(-seps2000, ceps2000).
        var pEarth = RotateEclipticToEquator(pEmb, seps2000, ceps2000);
        // 4. Subtract embofs_mosh (Moon-of-date precessed to J2000 equator).
        var moonJ2000Equator = EmbofsMosh(jdEt);
        var weight = 1.0 / (MoshierPlanetTheory.EarthMoonMassRatio + 1.0);
        pEarth = new Vec3(
            pEarth.X - moonJ2000Equator.X * weight,
            pEarth.Y - moonJ2000Equator.Y * weight,
            pEarth.Z - moonJ2000Equator.Z * weight);

        Vec3 v = default;
        if (wantVelocity)
        {
            var dt = MoshierPlanetTheory.SpeedIntervalDays;
            var pEmb2 = MoshierPlanetTheory.ComputeCartesianJ2000(2, jdEt - dt);
            var pEarth2 = RotateEclipticToEquator(pEmb2, seps2000, ceps2000);
            var moonJ2000Equator2 = EmbofsMosh(jdEt - dt);
            pEarth2 = new Vec3(
                pEarth2.X - moonJ2000Equator2.X * weight,
                pEarth2.Y - moonJ2000Equator2.Y * weight,
                pEarth2.Z - moonJ2000Equator2.Z * weight);
            v = (pEarth - pEarth2) / dt;
        }
        return new BodyState(pEarth, v, pEarth.Length, EphemerisSource.Moshier, BodyStateFrame.HeliocentricJ2000Equator);
    }

    /// <summary>
    /// Rotate ecliptic-cartesian → equator-cartesian using the supplied sin/cos
    /// of the obliquity.
    /// </summary>
    private static Vec3 RotateEclipticToEquator(Vec3 v, double seps, double ceps)
    {
        // swi_coortrf2(... -seps, ceps): y' = y·c - z·s; z' = y·s + z·c.
        return new Vec3(
            v.X,
            v.Y * ceps - v.Z * seps,
            v.Y * seps + v.Z * ceps);
    }

    /// <summary>
    /// Hand-coded short Moon series. Returns the Moon's geocentric position in
    /// the J2000 equator frame (AU) so the caller can subtract
    /// <c>moon / (m_emb + 1)</c> from the EMB to obtain Earth.
    /// </summary>
    /// <remarks>
    /// The series is a low-order solution (~2′ position residual in the moon
    /// itself) but completely sufficient for the EMB→Earth offset (a 1/82
    /// reduction of the Moon vector). Constants are kept as in the C source.
    /// </remarks>
    private static Vec3 EmbofsMosh(double tjd)
    {
        var t = (tjd - AstronomicalConstants.J1900) / 36525.0;
        // Mean anomaly of moon (MP), arcseconds → degrees fold-back via swe_degnorm.
        var a = SwiDegnorm(((1.44e-5 * t + 0.009192) * t + 477198.8491) * t + 296.104608) * AstronomicalConstants.DegToRad;
        var smp = System.Math.Sin(a);
        var cmp = System.Math.Cos(a);
        var s2mp = 2.0 * smp * cmp;
        var c2mp = cmp * cmp - smp * smp;
        // Mean elongation (D). The series uses 2D, so multiply by 2 here.
        a = SwiDegnorm(((1.9e-6 * t - 0.001436) * t + 445267.1142) * t + 350.737486);
        a = 2.0 * AstronomicalConstants.DegToRad * a;
        var s2d = System.Math.Sin(a);
        var c2d = System.Math.Cos(a);
        // Mean distance from ascending node (F).
        a = SwiDegnorm(((-3e-7 * t - 0.003211) * t + 483202.0251) * t + 11.250889) * AstronomicalConstants.DegToRad;
        var sf = System.Math.Sin(a);
        var cf = System.Math.Cos(a);
        var s2f = 2.0 * sf * cf;
        var sx = s2d * cmp - c2d * smp;
        var cx = c2d * cmp + s2d * smp;
        // Mean longitude of moon (LP). NOT normalised here — kept as a degree
        // accumulator and folded after the perturbation series.
        var L = ((1.9e-6 * t - 0.001133) * t + 481267.8831) * t + 270.434164;
        // Mean anomaly of sun (M).
        var M = SwiDegnorm(((-3.3e-6 * t - 1.50e-4) * t + 35999.0498) * t + 358.475833);

        // Ecliptic longitude perturbations (degrees).
        L = L
            + 6.288750 * smp
            + 1.274018 * sx
            + 0.658309 * s2d
            + 0.213616 * s2mp
            - 0.185596 * System.Math.Sin(AstronomicalConstants.DegToRad * M)
            - 0.114336 * s2f;

        // Ecliptic latitude (degrees → radians at end).
        var ax = smp * cf;
        sx = cmp * sf;
        var B = 5.128189 * sf
                + 0.280606 * (ax + sx)
                + 0.277693 * (ax - sx)
                + 0.173238 * (s2d * cf - c2d * sf);
        B *= AstronomicalConstants.DegToRad;

        // Parallax (degrees → radians).
        var p = 0.950724
                + 0.051818 * cmp
                + 0.009531 * cx
                + 0.007843 * c2d
                + 0.002824 * c2mp;
        p *= AstronomicalConstants.DegToRad;

        // Elongation/longitude → radians.
        L = SwiDegnorm(L);
        L *= AstronomicalConstants.DegToRad;

        // Distance in AU from parallax (Moshier uses 4.263523e-5 / sin(parallax)).
        var dist = 4.263523e-5 / System.Math.Sin(p);

        // Polar (L, B, dist) → cartesian ecliptic-of-date.
        var coslat = System.Math.Cos(B);
        var x0 = dist * coslat * System.Math.Cos(L);
        var y0 = dist * coslat * System.Math.Sin(L);
        var z0 = dist * System.Math.Sin(B);

        // Rotate ecliptic-of-date → equator-of-date with TRUE obliquity is
        // what the C library does (`swed.oec.seps`); the C `swed.oec` is the
        // mean obliquity at body epoch (set via calc_epsilon, not via
        // nutation). So we use the model-default mean obliquity.
        var epsBody = Precession.MeanObliquity(tjd);
        var seps = System.Math.Sin(epsBody);
        var ceps = System.Math.Cos(epsBody);
        var v = RotateEclipticToEquator(new Vec3(x0, y0, z0), seps, ceps);

        // Precess equator-of-date → equator-J2000.
        System.Span<double> tmp = stackalloc double[3];
        tmp[0] = v.X; tmp[1] = v.Y; tmp[2] = v.Z;
        Precession.Apply(tmp, tjd, AstronomicalConstants.J2000);
        return new Vec3(tmp[0], tmp[1], tmp[2]);
    }

    /// <summary>Reduce an angle in degrees to [0, 360).</summary>
    private static double SwiDegnorm(double d)
    {
        var x = d - System.Math.Floor(d / 360.0) * 360.0;
        if (x < 0.0) x += 360.0;
        return x;
    }

    private static void EnsurePlanetInRange(double jd)
    {
        if (jd < MoshierPlanetTheory.MoshierStartJd - 0.3 || jd > MoshierPlanetTheory.MoshierEndJd + 0.3)
        {
            throw new EphemerisDateOutOfRangeException(
                new JulianDay(jd),
                new JulianDay(MoshierPlanetTheory.MoshierStartJd),
                new JulianDay(MoshierPlanetTheory.MoshierEndJd),
                EphemerisSource.Moshier);
        }
    }

    private static void EnsureMoonInRange(double jd)
    {
        if (jd < MoshierMoonTheory.MoshierStartJd - 0.2 || jd > MoshierMoonTheory.MoshierEndJd + 0.2)
        {
            throw new EphemerisDateOutOfRangeException(
                new JulianDay(jd),
                new JulianDay(MoshierMoonTheory.MoshierStartJd),
                new JulianDay(MoshierMoonTheory.MoshierEndJd),
                EphemerisSource.Moshier);
        }
    }

    private static void EnsureMeanNodeApogeeInRange(double jd)
    {
        // Mirrors swi_mean_node / swi_mean_apog (swemmoon.c#L1505 / #L1578):
        // the elements drift outside [MOSHNDEPH_START, MOSHNDEPH_END].
        if (jd < MoshierMeanNodeAndApogee.MoshNdEphStartJd
            || jd > MoshierMeanNodeAndApogee.MoshNdEphEndJd)
        {
            throw new EphemerisDateOutOfRangeException(
                new JulianDay(jd),
                new JulianDay(MoshierMeanNodeAndApogee.MoshNdEphStartJd),
                new JulianDay(MoshierMeanNodeAndApogee.MoshNdEphEndJd),
                EphemerisSource.Moshier);
        }
    }

    private static void EnsureInterpolatedApsidesInRange(double jd)
    {
        // Mirrors the SE_INTP_APOG dispatcher gate at sweph.c#L982-L987: the
        // engine reuses the lunar Moshier perturbation series, hence the
        // narrow [MOSHLUEPH_START, MOSHLUEPH_END] = [625000.5, 2818000.5] band.
        if (jd < MoshierMoonTheory.MoshierStartJd || jd > MoshierMoonTheory.MoshierEndJd)
        {
            throw new EphemerisDateOutOfRangeException(
                new JulianDay(jd),
                new JulianDay(MoshierMoonTheory.MoshierStartJd),
                new JulianDay(MoshierMoonTheory.MoshierEndJd),
                EphemerisSource.Moshier);
        }
    }

    /// <summary>
    /// Compute geocentric mean ecliptic-of-date cartesian (and optional velocity)
    /// for the mean lunar node or mean lunar apogee. Velocity uses a one-sided
    /// finite difference at <c>jd</c> and <c>jd - MeanNodeSpeedIntervalDays</c>,
    /// with the longitude difference normalised to <c>(-π, +π]</c>.
    /// </summary>
    private static BodyState ComputeMeanNodeOrApogee(CelestialBody body, double jd, bool inSpeed)
    {
        var polar = body == CelestialBody.MeanNode
            ? MoshierMeanNodeAndApogee.ComputeMeanNodePolar(jd)
            : MoshierMeanNodeAndApogee.ComputeMeanApogeePolar(jd);

        if (!inSpeed)
        {
            var pos = Polar.PolarToCartesian(polar);
            return new BodyState(pos, default, polar.Z, EphemerisSource.Moshier, BodyStateFrame.GeocentricEclipticOfDate);
        }

        var dt = MoshierMeanNodeAndApogee.MeanNodeSpeedIntervalDays;
        var polarPrev = body == CelestialBody.MeanNode
            ? MoshierMeanNodeAndApogee.ComputeMeanNodePolar(jd - dt)
            : MoshierMeanNodeAndApogee.ComputeMeanApogeePolar(jd - dt);

        // swe_difrad2n(now, prev): wrap the difference into (-π, +π] so the
        // 360° fold-over doesn't blow up the speed estimate.
        var lonRate = SwiDifRad2N(polar.X, polarPrev.X) / dt;
        // Latitude is exactly 0 for the mean node by construction; the C
        // library forces the latitude speed to 0 there too (sweph.c#L877).
        // For the mean apogee, swi_mean_apog produces a non-zero lat which
        // C also differences (#L915-L916).
        var latRate = body == CelestialBody.MeanNode
            ? 0.0
            : (polar.Y - polarPrev.Y) / dt;
        // Distance speed is forced to 0 in the C library for both bodies
        // (sweph.c#L877 and #L917).
        var radialRate = 0.0;

        System.Span<double> polarSpeedTuple = stackalloc double[6]
        {
            polar.X, polar.Y, polar.Z,
            lonRate, latRate, radialRate,
        };
        System.Span<double> cart = stackalloc double[6];
        Polar.PolarToCartesianWithSpeed(polarSpeedTuple, cart);
        return new BodyState(
            new Vec3(cart[0], cart[1], cart[2]),
            new Vec3(cart[3], cart[4], cart[5]),
            polar.Z,
            EphemerisSource.Moshier,
            BodyStateFrame.GeocentricEclipticOfDate);
    }

    /// <summary>
    /// Compute geocentric mean ecliptic-of-date cartesian (and optional velocity)
    /// for the interpolated lunar apogee or perigee. Velocity uses a fixed
    /// 0.1-day central difference around the centre sample, with longitude
    /// wrapped to <c>(-π, +π]</c>. Latitude and distance speeds are real
    /// central differences (the interpolated apsides have a non-trivial latitude
    /// oscillation, unlike the mean apogee whose latitude speed is forced to 0).
    /// </summary>
    private static BodyState ComputeInterpolatedApsides(CelestialBody body, double jd, bool inSpeed)
    {
        var which = body == CelestialBody.InterpolatedApogee
            ? MoshierInterpolatedApsides.Apside.Apogee
            : MoshierInterpolatedApsides.Apside.Perigee;
        var polar = MoshierInterpolatedApsides.ComputePolar(jd, which);

        if (!inSpeed)
        {
            var pos = Polar.PolarToCartesian(polar);
            return new BodyState(pos, default, polar.Z, EphemerisSource.Moshier, BodyStateFrame.GeocentricEclipticOfDate);
        }

        var dt = MoshierInterpolatedApsides.SpeedIntervalDays;
        var polarPlus = MoshierInterpolatedApsides.ComputePolar(jd + dt, which);
        var polarMinus = MoshierInterpolatedApsides.ComputePolar(jd - dt, which);

        // swe_difrad2n(plus, minus) / dt / 2 — central-difference longitude
        // rate with the 360° wrap removed.
        var lonRate = SwiDifRad2N(polarPlus.X, polarMinus.X) / (2.0 * dt);
        var latRate = (polarPlus.Y - polarMinus.Y) / (2.0 * dt);
        var radialRate = (polarPlus.Z - polarMinus.Z) / (2.0 * dt);

        System.Span<double> polarSpeedTuple = stackalloc double[6]
        {
            polar.X, polar.Y, polar.Z,
            lonRate, latRate, radialRate,
        };
        System.Span<double> cart = stackalloc double[6];
        Polar.PolarToCartesianWithSpeed(polarSpeedTuple, cart);
        return new BodyState(
            new Vec3(cart[0], cart[1], cart[2]),
            new Vec3(cart[3], cart[4], cart[5]),
            polar.Z,
            EphemerisSource.Moshier,
            BodyStateFrame.GeocentricEclipticOfDate);
    }

    /// <summary>Angular difference in radians, normalised to <c>(-π, +π]</c>.</summary>
    private static double SwiDifRad2N(double a, double b)
    {
        var d = a - b;
        d -= AstronomicalConstants.TwoPi * System.Math.Floor(d / AstronomicalConstants.TwoPi);
        if (d > AstronomicalConstants.Pi) d -= AstronomicalConstants.TwoPi;
        return d;
    }
}
