// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Reference drivers:
//   /tmp/nodapsref.c   — mean-elements path
//   /tmp/oscapsref.c   — osculating path, orbital elements, max/min distance
// All linked against the unmodified C library; flag set is
// SEFLG_MOSEPH | SEFLG_TRUEPOS | SEFLG_NOABERR | SEFLG_NOGDEFL | SEFLG_NONUT
// | SEFLG_SPEED — the geometric-only branch where the J2000-equator round
// trip subtraction collapses to a clean helio→geo lift in mean ecliptic of
// date.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;
using Vec3 = SharpAstrology.SwissEphemerides.Domain.Mathematics.Vec3;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Nodes and apsides finder, plus closely related orbit-element entry points.
/// Implements both the mean-elements branch of <c>swe_nod_aps</c>
/// (swecl.c#L5148-L5234) and the osculating branch (swecl.c#L5234-L5388),
/// plus <c>swe_get_orbital_elements</c> (swecl.c#L5772-L5959) and
/// <c>swe_orbit_max_min_true_distance</c> (swecl.c#L6159-L6276).
/// <para>
/// <b>Ephemeris sources.</b> Routed through <see cref="SourceRouter"/>:
/// SwissEph files, JPL DE files and Moshier are all selectable. Exactly one
/// of <c>SEFLG_MOSEPH</c> / <c>SEFLG_SWIEPH</c> / <c>SEFLG_JPLEPH</c> must be
/// set per call and the chosen back-end must be configured on the router;
/// no implicit fallback is performed.
/// </para>
/// <para>
/// <b>Apparent-correction pipeline.</b> Default = full apparent positions
/// (light-time-iterated gravitational deflection, annual aberration,
/// nutation). The disabling flags <c>SEFLG_TRUEPOS</c> / <c>SEFLG_NOABERR</c>
/// / <c>SEFLG_NOGDEFL</c> / <c>SEFLG_NONUT</c> are honored individually.
/// </para>
/// <para>
/// <b>Current scope.</b>
/// </para>
/// <list type="bullet">
///   <item><description>Output frame: geocentric, tropical, ecliptic of date (true if nutation is on, mean if <c>SEFLG_NONUT</c>), polar (longitude, latitude in degrees; distance in AU).</description></item>
///   <item><description>Supported bodies: Sun, Mercury–Neptune (mean + osculating); Pluto and Moon (osculating only).</description></item>
/// </list>
/// <para>
/// The following are <i>not yet implemented</i> and throw
/// <see cref="System.NotSupportedException"/>:
/// </para>
/// <list type="bullet">
///   <item><description>Sidereal (<c>SEFLG_SIDEREAL</c>), J2000 mean equinox (<c>SEFLG_J2000</c>), equatorial (<c>SEFLG_EQUATORIAL</c>), cartesian (<c>SEFLG_XYZ</c>), radians (<c>SEFLG_RADIANS</c>) outputs.</description></item>
///   <item><description>Heliocentric / barycentric / topocentric observer frames (<c>SEFLG_HELCTR</c>, <c>SEFLG_BARYCTR</c>, <c>SEFLG_TOPOCTR</c>) — <c>swe_orbit_max_min_true_distance</c> is the one exception that accepts <c>SEFLG_HELCTR</c>/<c>SEFLG_BARYCTR</c>.</description></item>
///   <item><description>Mean nodes/apsides for Pluto and the Moon.</description></item>
///   <item><description>The <c>SEFLG_ORBEL_AA</c> alternate Gmsm summation.</description></item>
/// </list>
/// </summary>
public sealed class NodesAndApsidesService
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;

    // Au³ in metres³ — the denominator of Gmsm conversion from m³/s² to AU³/day².
    private static readonly double Au3 =
        AstronomicalConstants.AstronomicalUnitMeters
        * AstronomicalConstants.AstronomicalUnitMeters
        * AstronomicalConstants.AstronomicalUnitMeters;

    private const double Sec2 = AstronomicalConstants.SecondsPerDay * AstronomicalConstants.SecondsPerDay;

    private const EphemerisFlags SourceMask =
        EphemerisFlags.MoshierEph | EphemerisFlags.SwissEph | EphemerisFlags.JplEph;

    private readonly SourceRouter _router;
    private readonly CalendarService _calendar;
    private readonly AstronomicalModelOverrides _models;

    /// <summary>
    /// Constructs the service from a single body-position source — the
    /// historical Moshier-only entry point used by unit tests. Internally
    /// wraps the source in a <see cref="SourceRouter"/>, so the call patterns
    /// stay identical for callers that haven't migrated yet.
    /// </summary>
    internal NodesAndApsidesService(
        IBodyPositionSource source,
        CalendarService calendar,
        AstronomicalModelOverrides? models = null)
        : this(new SourceRouter(new[] { source ?? throw new ArgumentNullException(nameof(source)) }), calendar, models)
    {
    }

    /// <summary>
    /// Constructs the service from a <see cref="SourceRouter"/>. This is the
    /// Phase-3C entry point: the router can carry SwissEph and/or JPL sources
    /// alongside Moshier, and the service dispatches per call based on the
    /// source bit in the user-supplied <see cref="EphemerisFlags"/>. Mirrors
    /// the C library's <c>swi_plan_for_osc_elem</c> source-routing pattern
    /// (sweph.c#L5758-L5856).
    /// </summary>
    internal NodesAndApsidesService(
        SourceRouter router,
        CalendarService calendar,
        AstronomicalModelOverrides? models = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _models = models ?? AstronomicalModelOverrides.Default;
    }

    /// <summary>UT entry point: applies ΔT and dispatches to <see cref="Compute"/>.</summary>
    public NodesApsidesPoints ComputeUt(
        JulianDay jdUt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        NodesApsidesMethod method)
    {
        var dt = _calendar.DeltaT(jdUt);
        return Compute(new JulianDay(jdUt.Value + dt), body, ephemerisFlags, method);
    }

    /// <summary>
    /// Returns the four orbital points (ascending node, descending node,
    /// perihelion, aphelion) at <paramref name="jdEt"/> for the requested
    /// body. Both the mean (polynomial element tables) and osculating
    /// (instantaneous Kepler ellipse) branches are supported. Default
    /// frame is geocentric tropical ecliptic-of-date polar; non-default
    /// flags throw <see cref="System.NotSupportedException"/>.
    /// See the type-level remarks for the exact supported flag/body set.
    /// </summary>
    public NodesApsidesPoints Compute(
        JulianDay jdEt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        NodesApsidesMethod method)
    {
        ValidateScope(body, ephemerisFlags, method);

        if ((method & NodesApsidesMethod.Osculating) != 0)
            return ComputeOsculating(jdEt, body, ephemerisFlags, method);
        return ComputeMean(jdEt, body, ephemerisFlags, method);
    }

    // ---- Mean elements branch (swecl.c#L5148-L5234) ----------------------
    private NodesApsidesPoints ComputeMean(
        JulianDay jdEt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags,
        NodesApsidesMethod method)
    {
        // Map body to element-table row. Sun and Earth both use Earth's row.
        var ipl = (int)body;
        var iplx = OrbitalElementTables.IplToElem[ipl];

        // ---- Mean orbital elements at jdEt (lines swecl.c#L5165-L5183) ----
        var t = (jdEt.Value - AstronomicalConstants.J2000) / AstronomicalConstants.JulianCentury;
        var t2 = t * t;
        var t3 = t2 * t;

        var ep = OrbitalElementTables.Inclination[iplx];
        var incl = ep[0] + ep[1] * t + ep[2] * t2 + ep[3] * t3;
        var vincl = ep[1] / AstronomicalConstants.JulianCentury;

        ep = OrbitalElementTables.SemiMajorAxis[iplx];
        var sema = ep[0] + ep[1] * t + ep[2] * t2 + ep[3] * t3;
        var vsema = ep[1] / AstronomicalConstants.JulianCentury;

        ep = OrbitalElementTables.Eccentricity[iplx];
        var ecce = ep[0] + ep[1] * t + ep[2] * t2 + ep[3] * t3;
        var vecce = ep[1] / AstronomicalConstants.JulianCentury;

        ep = OrbitalElementTables.AscendingNode[iplx];
        Span<double> xna = stackalloc double[6];
        xna[0] = ep[0] + ep[1] * t + ep[2] * t2 + ep[3] * t3;
        xna[3] = ep[1] / AstronomicalConstants.JulianCentury;

        ep = OrbitalElementTables.Perihelion[iplx];
        Span<double> xpe = stackalloc double[6];
        xpe[0] = ep[0] + ep[1] * t + ep[2] * t2 + ep[3] * t3;
        xpe[3] = ep[1] / AstronomicalConstants.JulianCentury;

        Span<double> xnd = stackalloc double[6];
        Span<double> xap = stackalloc double[6];

        var doFocalPoint = (method & NodesApsidesMethod.FocalPoint) != 0;

        // Descending node: 180° opposite asc node.
        xnd[0] = AngleMath.NormalizeDegrees(xna[0] + 180.0);
        xnd[3] = xna[3];

        // Angular distance of perihelion from node — store in xpe[0] / xpe[3] aux slots.
        var parg = xpe[0] = AngleMath.NormalizeDegrees(xpe[0] - xna[0]);
        var pargx = xpe[3] = AngleMath.NormalizeDegrees(xpe[0] + xpe[3] - xna[3]);

        // Transform from orbital plane to mean ecliptic of date (cotrans on lon/lat).
        double lon = xpe[0], lat = xpe[1] = 0.0;
        CoordinateRotation.Rotate(ref lon, ref lat, -incl);
        xpe[0] = lon;
        xpe[1] = lat;

        double lon2 = xpe[3], lat2 = xpe[4] = 0.0;
        CoordinateRotation.Rotate(ref lon2, ref lat2, -incl - vincl);
        xpe[3] = lon2;
        xpe[4] = lat2;

        // Add node back, then derive longitude speed by differencing.
        xpe[0] = AngleMath.NormalizeDegrees(xpe[0] + xna[0]);
        xpe[3] = AngleMath.NormalizeDegrees(xpe[3] + xna[0] + xna[3]);
        xpe[3] = AngleMath.NormalizeDegrees(xpe[3] - xpe[0]);

        // Heliocentric distance of perihelion / aphelion.
        xpe[2] = sema * (1.0 - ecce);
        xpe[5] = (sema + vsema) * (1.0 - ecce - vecce) - xpe[2];

        // Aphelion: 180° from peri, latitude inverted.
        xap[0] = AngleMath.NormalizeDegrees(xpe[0] + 180.0);
        xap[1] = -xpe[1];
        xap[3] = xpe[3];
        xap[4] = -xpe[4];
        if (doFocalPoint)
        {
            xap[2] = sema * ecce * 2.0;
            xap[5] = (sema + vsema) * (ecce + vecce) * 2.0 - xap[2];
        }
        else
        {
            xap[2] = sema * (1.0 + ecce);
            xap[5] = (sema + vsema) * (1.0 + ecce + vecce) - xap[2];
        }

        // Heliocentric distance of nodes — derived from eccentric-anomaly identity.
        var ea = System.Math.Atan(System.Math.Tan(-parg * DegToRad / 2.0) * System.Math.Sqrt((1.0 - ecce) / (1.0 + ecce))) * 2.0;
        var eax = System.Math.Atan(System.Math.Tan(-pargx * DegToRad / 2.0) * System.Math.Sqrt((1.0 - ecce - vecce) / (1.0 + ecce + vecce))) * 2.0;
        xna[2] = sema * (System.Math.Cos(ea) - ecce) / System.Math.Cos(parg * DegToRad);
        xna[5] = (sema + vsema) * (System.Math.Cos(eax) - ecce - vecce) / System.Math.Cos(pargx * DegToRad);
        xna[5] -= xna[2];

        ea = System.Math.Atan(System.Math.Tan((180.0 - parg) * DegToRad / 2.0) * System.Math.Sqrt((1.0 - ecce) / (1.0 + ecce))) * 2.0;
        eax = System.Math.Atan(System.Math.Tan((180.0 - pargx) * DegToRad / 2.0) * System.Math.Sqrt((1.0 - ecce - vecce) / (1.0 + ecce + vecce))) * 2.0;
        xnd[2] = sema * (System.Math.Cos(ea) - ecce) / System.Math.Cos((180.0 - parg) * DegToRad);
        xnd[5] = (sema + vsema) * (System.Math.Cos(eax) - ecce - vecce) / System.Math.Cos((180.0 - pargx) * DegToRad);
        xnd[5] -= xnd[2];

        // Convert each (lon, lat, r) + (dλ, dβ, dr) entry to cartesian.
        Span<double> xna3 = stackalloc double[6];
        Span<double> xnd3 = stackalloc double[6];
        Span<double> xpe3 = stackalloc double[6];
        Span<double> xap3 = stackalloc double[6];
        ToCartesian(xna, xna3);
        ToCartesian(xnd, xnd3);
        ToCartesian(xpe, xpe3);
        ToCartesian(xap, xap3);

        // ---- Shared transform pipeline (swecl.c#L5434-L5628) -------------
        // Sun internally maps to Earth (ipli = SE_EARTH), so its nodes are
        // zeroed alongside Earth's; only the perihelion/aphelion negation
        // distinguishes Sun from Earth in the geocentric output.
        var zeroNodes = body == CelestialBody.Earth || body == CelestialBody.Sun;
        var bodyIsSun = body == CelestialBody.Sun;

        // Per-iteration jdLiftSun chains the C pldat side effect (see
        // ShouldMirrorPldatSideEffect): for SwissEph / JPL apparent + speed,
        // each iteration's lift uses bary Sun at (t - prev_dt) where prev_dt
        // is the previous iteration's body geocentric light-time.
        bool mirrorPldat = ShouldMirrorPldatSideEffect(jdEt, body, ephemerisFlags);
        JulianDay jdLift = jdEt;

        double dt0 = ApplyPipeline(xna3, jdEt, jdLift, bodyIsSun, zeroNodes, bodyIsMoon: false, isNode: true, ephemerisFlags);
        if (mirrorPldat) jdLift = new JulianDay(jdEt.Value - dt0);
        double dt1 = ApplyPipeline(xnd3, jdEt, jdLift, bodyIsSun, zeroNodes, bodyIsMoon: false, isNode: true, ephemerisFlags);
        if (mirrorPldat) jdLift = new JulianDay(jdEt.Value - dt1);
        double dt2 = ApplyPipeline(xpe3, jdEt, jdLift, bodyIsSun, zeroNodes, bodyIsMoon: false, isNode: false, ephemerisFlags);
        if (mirrorPldat) jdLift = new JulianDay(jdEt.Value - dt2);
        ApplyPipeline(xap3, jdEt, jdLift, bodyIsSun, zeroNodes, bodyIsMoon: false, isNode: false, ephemerisFlags);

        return new NodesApsidesPoints(
            ToOrbitalPoint(xna3),
            ToOrbitalPoint(xnd3),
            ToOrbitalPoint(xpe3),
            ToOrbitalPoint(xap3));
    }

    // ---- Osculating branch (swecl.c#L5234-L5388) -------------------------
    private NodesApsidesPoints ComputeOsculating(
        JulianDay jdEt,
        CelestialBody body,
        EphemerisFlags flags,
        NodesApsidesMethod method)
    {
        var bodyForOrbit = body == CelestialBody.Sun ? CelestialBody.Earth : body;
        var doFocalPoint = (method & NodesApsidesMethod.FocalPoint) != 0;
        var wantSpeed = (flags & EphemerisFlags.Speed) != 0;
        var bodyIsMoon = bodyForOrbit == CelestialBody.Moon;

        // Heliocentric distance estimate at jdEt — sets dt for finite-diff speed.
        double helioDist = bodyIsMoon ? 0.0 : EstimateHelioDistance(bodyForOrbit, jdEt, flags);

        // Gmsm and dt per swecl.c#L5255-L5274.
        double Gmsm, dt, dzmin;
        bool ellipseIsBary = false;
        if (bodyIsMoon)
        {
            dt = AstronomicalConstants.NodeCalcIntervalDays;
            dzmin = 1e-15;
            Gmsm = AstronomicalConstants.GeoGravConst
                   * (1.0 + 1.0 / AstronomicalConstants.EarthMoonMassRatio)
                   / Au3 * Sec2;
        }
        else
        {
            var massRatio = GetPlanetMassRatio(bodyForOrbit);
            var plm = massRatio > 0 ? 1.0 / massRatio : 0.0;
            dt = AstronomicalConstants.NodeCalcIntervalDays * 10.0 * helioDist;
            dzmin = 1e-15 * dt / AstronomicalConstants.NodeCalcIntervalDays;
            Gmsm = AstronomicalConstants.HelioGravConst * (1.0 + plm) / Au3 * Sec2;

            if ((method & NodesApsidesMethod.OsculatingBarycentric) != 0 && helioDist > 6.0)
            {
                // Moshier helio == bary, so the only effect is to skip the
                // helio→bary correction in the shared pipeline.
                ellipseIsBary = true;
            }
        }

        int istart, iend;
        if (wantSpeed) { istart = 0; iend = 2; }
        else { istart = iend = 0; dt = 0.0; }

        // 3-by-6 buffers — pos/vel for body, derived perihelion/aphelion/nodes.
        Span<double> xpos = stackalloc double[3 * 6];
        Span<double> xq = stackalloc double[3 * 3];
        Span<double> xa = stackalloc double[3 * 3];
        Span<double> xn = stackalloc double[3 * 3];
        Span<double> xs = stackalloc double[3 * 3];

        for (int i = istart; i <= iend; i++)
        {
            double tEt = (istart == iend) ? jdEt.Value : (jdEt.Value + (i - 1) * dt);
            ComputeMeanEclOfDateState(bodyForOrbit, new JulianDay(tEt), flags, xpos.Slice(i * 6, 6));
        }

        for (int i = istart; i <= iend; i++)
        {
            ApplyOsculatingMath(
                xpos.Slice(i * 6, 6),
                xq.Slice(i * 3, 3), xa.Slice(i * 3, 3),
                xn.Slice(i * 3, 3), xs.Slice(i * 3, 3),
                Gmsm, dzmin, doFocalPoint);
        }

        Span<double> xnaOut = stackalloc double[6];
        Span<double> xndOut = stackalloc double[6];
        Span<double> xpeOut = stackalloc double[6];
        Span<double> xapOut = stackalloc double[6];

        if (wantSpeed)
        {
            for (int j = 0; j < 3; j++)
            {
                xpeOut[j] = xq[1 * 3 + j];
                xpeOut[j + 3] = (xq[2 * 3 + j] - xq[0 * 3 + j]) / (2.0 * dt);
                xapOut[j] = xa[1 * 3 + j];
                xapOut[j + 3] = (xa[2 * 3 + j] - xa[0 * 3 + j]) / (2.0 * dt);
                xnaOut[j] = xn[1 * 3 + j];
                xnaOut[j + 3] = (xn[2 * 3 + j] - xn[0 * 3 + j]) / (2.0 * dt);
                xndOut[j] = xs[1 * 3 + j];
                xndOut[j + 3] = (xs[2 * 3 + j] - xs[0 * 3 + j]) / (2.0 * dt);
            }
        }
        else
        {
            for (int j = 0; j < 3; j++)
            {
                xpeOut[j] = xq[j];
                xapOut[j] = xa[j];
                xnaOut[j] = xn[j];
                xndOut[j] = xs[j];
            }
        }

        // Apply shared transform pipeline. The C code zeros the nodes when
        // ipli == SE_EARTH (which is true for both body == Earth and body == Sun
        // because of the Sun→Earth substitution upstream).
        bool isMoon = body == CelestialBody.Moon;
        bool zeroNodes = bodyForOrbit == CelestialBody.Earth;
        bool isSun = body == CelestialBody.Sun;
        bool mirrorPldat = ShouldMirrorPldatSideEffect(jdEt, body, flags);
        JulianDay jdLift = jdEt;

        double dt0 = ApplyPipeline(xnaOut, jdEt, jdLift, isSun, zeroNodes, isMoon, isNode: true, flags);
        if (mirrorPldat) jdLift = new JulianDay(jdEt.Value - dt0);
        double dt1 = ApplyPipeline(xndOut, jdEt, jdLift, isSun, zeroNodes, isMoon, isNode: true, flags);
        if (mirrorPldat) jdLift = new JulianDay(jdEt.Value - dt1);
        double dt2 = ApplyPipeline(xpeOut, jdEt, jdLift, isSun, zeroNodes, isMoon, isNode: false, flags);
        if (mirrorPldat) jdLift = new JulianDay(jdEt.Value - dt2);
        ApplyPipeline(xapOut, jdEt, jdLift, isSun, zeroNodes, isMoon, isNode: false, flags);

        // Suppress unused — ellipseIsBary affects the pipeline only via the
        // helio→bary correction we never apply for Moshier (helio == bary).
        _ = ellipseIsBary;

        return new NodesApsidesPoints(
            ToOrbitalPoint(xnaOut),
            ToOrbitalPoint(xndOut),
            ToOrbitalPoint(xpeOut),
            ToOrbitalPoint(xapOut));
    }

    /// <summary>
    /// Per-epoch orbital math from swecl.c#L5289-L5366: derive ascending /
    /// descending node, perihelion and aphelion cartesian coordinates from a
    /// 6-vector state in mean ecliptic-of-date (heliocentric for planets,
    /// geocentric for the Moon).
    /// </summary>
    private static void ApplyOsculatingMath(
        Span<double> xpos,
        Span<double> xq, Span<double> xa, Span<double> xn, Span<double> xs,
        double Gmsm, double dzmin, bool doFocalPoint)
    {
        // dzmin protects the line-of-nodes denominator from singular vz.
        if (System.Math.Abs(xpos[5]) < dzmin)
            xpos[5] = xpos[5] >= 0 ? dzmin : -dzmin;

        var fac = xpos[2] / xpos[5];
        var sgn = xpos[5] / System.Math.Abs(xpos[5]);
        Span<double> xnLocal = stackalloc double[3];
        Span<double> xsLocal = stackalloc double[3];
        for (int j = 0; j < 3; j++)
        {
            xnLocal[j] = (xpos[j] - fac * xpos[j + 3]) * sgn;
            xsLocal[j] = -xnLocal[j];
        }

        // Asc-node azimuth.
        var rxy = System.Math.Sqrt(xnLocal[0] * xnLocal[0] + xnLocal[1] * xnLocal[1]);
        var cosnode = xnLocal[0] / rxy;
        var sinnode = xnLocal[1] / rxy;

        // Inclination from r × v.
        var nx = xpos[1] * xpos[5] - xpos[2] * xpos[4];
        var ny = xpos[2] * xpos[3] - xpos[0] * xpos[5];
        var nz = xpos[0] * xpos[4] - xpos[1] * xpos[3];
        var rxy2 = nx * nx + ny * ny;
        var c2 = rxy2 + nz * nz;
        var rxyzN = System.Math.Sqrt(c2);
        var rxyN = System.Math.Sqrt(rxy2);
        var sinincl = rxyN / rxyzN;
        var cosincl = System.Math.Sqrt(1.0 - sinincl * sinincl);
        if (nz < 0) cosincl = -cosincl; // retrograde object

        // Argument of latitude.
        var cosu = xpos[0] * cosnode + xpos[1] * sinnode;
        var sinu = xpos[2] / sinincl;
        var uu = System.Math.Atan2(sinu, cosu);

        // Semi-major axis (vis-viva).
        var rxyz = System.Math.Sqrt(xpos[0] * xpos[0] + xpos[1] * xpos[1] + xpos[2] * xpos[2]);
        var v2 = xpos[3] * xpos[3] + xpos[4] * xpos[4] + xpos[5] * xpos[5];
        var sema = 1.0 / (2.0 / rxyz - v2 / Gmsm);

        // Eccentricity.
        var pp = c2 / Gmsm;
        var ecceArg = pp / sema;
        if (ecceArg > 1.0) ecceArg = 1.0;
        var ecce = System.Math.Sqrt(1.0 - ecceArg);

        // Eccentric and true anomaly.
        var ecce2 = ecce == 0 ? 1e-10 : ecce;
        var cosE = (1.0 - rxyz / sema) / ecce2;
        var sinE = (xpos[0] * xpos[3] + xpos[1] * xpos[4] + xpos[2] * xpos[5])
                   / (ecce2 * System.Math.Sqrt(sema * Gmsm));
        var nyTrue = 2.0 * System.Math.Atan(
            System.Math.Sqrt((1.0 + ecce) / (1.0 - ecce)) * sinE / (1.0 + cosE));

        // Perihelion in orbital plane: angular dist from asc node = uu - ν.
        Span<double> xqp = stackalloc double[3];
        xqp[0] = Mod2Pi(uu - nyTrue);
        xqp[1] = 0.0;
        xqp[2] = sema * (1.0 - ecce);
        // polar → cartesian (orbital plane).
        var (sinL, cosL) = System.Math.SinCos(xqp[0]);
        var (sinB, cosB) = System.Math.SinCos(xqp[1]);
        Span<double> xqc = stackalloc double[3];
        xqc[0] = xqp[2] * cosB * cosL;
        xqc[1] = xqp[2] * cosB * sinL;
        xqc[2] = xqp[2] * sinB;
        // Rotate orbital plane → ecliptic. swi_coortrf2(state, -sinincl, cosincl):
        //   y' = y * cosincl + z * (-sinincl)
        //   z' = -y * (-sinincl) + z * cosincl
        // i.e. rotation about x by +incl.
        var yo = xqc[1]; var zo = xqc[2];
        xqc[1] = yo * cosincl - zo * sinincl;
        xqc[2] = yo * sinincl + zo * cosincl;
        // cartesian → polar.
        var rxy_q = System.Math.Sqrt(xqc[0] * xqc[0] + xqc[1] * xqc[1]);
        var rxyz_q = System.Math.Sqrt(rxy_q * rxy_q + xqc[2] * xqc[2]);
        double lon_q = (rxy_q == 0) ? 0 : System.Math.Atan2(xqc[1], xqc[0]);
        double lat_q = (rxy_q == 0) ? (xqc[2] >= 0 ? System.Math.PI / 2 : -System.Math.PI / 2)
                                    : System.Math.Atan(xqc[2] / rxy_q);
        // Add node angle to longitude.
        lon_q += System.Math.Atan2(sinnode, cosnode);

        // Aphelion: lon+π, lat negated.
        var lon_a = Mod2Pi(lon_q + System.Math.PI);
        var lat_a = -lat_q;
        var rxyz_a = doFocalPoint ? sema * ecce * 2.0 : sema * (1.0 + ecce);

        // polar → cartesian for both.
        var (sinLq, cosLq) = System.Math.SinCos(lon_q);
        var (sinBq, cosBq) = System.Math.SinCos(lat_q);
        xq[0] = rxyz_q * cosBq * cosLq;
        xq[1] = rxyz_q * cosBq * sinLq;
        xq[2] = rxyz_q * sinBq;

        var (sinLa, cosLa) = System.Math.SinCos(lon_a);
        var (sinBa, cosBa) = System.Math.SinCos(lat_a);
        xa[0] = rxyz_a * cosBa * cosLa;
        xa[1] = rxyz_a * cosBa * sinLa;
        xa[2] = rxyz_a * sinBa;

        // Re-anchor node distances to the Kepler ellipse:
        // true anomaly at asc node = ν − u, at desc = +π.
        var nyAsc = Mod2Pi(nyTrue - uu);
        var nyDsc = Mod2Pi(nyAsc + System.Math.PI);
        var sqrtFac = System.Math.Sqrt((1.0 + ecce) / (1.0 - ecce));
        var cosE_n = System.Math.Cos(2.0 * System.Math.Atan(System.Math.Tan(nyAsc / 2.0) / sqrtFac));
        var cosE_n2 = System.Math.Cos(2.0 * System.Math.Atan(System.Math.Tan(nyDsc / 2.0) / sqrtFac));
        var rn = sema * (1.0 - ecce * cosE_n);
        var rn2 = sema * (1.0 - ecce * cosE_n2);
        var ro = System.Math.Sqrt(xnLocal[0] * xnLocal[0] + xnLocal[1] * xnLocal[1] + xnLocal[2] * xnLocal[2]);
        var ro2 = System.Math.Sqrt(xsLocal[0] * xsLocal[0] + xsLocal[1] * xsLocal[1] + xsLocal[2] * xsLocal[2]);
        for (int j = 0; j < 3; j++)
        {
            xn[j] = xnLocal[j] * rn / ro;
            xs[j] = xsLocal[j] * rn2 / ro2;
        }
    }

    /// <summary>
    /// Returns the seventeen Kepler elements of the body's osculating ellipse at
    /// <paramref name="jdEt"/>. Earth uses the EMB ellipse, Moon is geocentric.
    /// Lunar nodes/apogees and the Sun are not valid inputs.
    /// </summary>
    public OrbitalElements ComputeOrbitalElements(
        JulianDay jdEt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags)
    {
        ValidateOrbitalElementsBody(body);
        ValidateOrbitalElementsFlags(ephemerisFlags);
        return ComputeOrbitalElementsCore(jdEt, body, ephemerisFlags);
    }

    /// <summary>
    /// Returns the maximum, minimum and current "true" distance of the body at
    /// <paramref name="jdEt"/>. With <see cref="EphemerisFlags.Heliocentric"/>
    /// the result is based on the body's own osculating ellipse; without it,
    /// both the body's and Earth's osculating ellipses are scanned to give the
    /// geocentric extrema. Sun and Moon always use the helio path.
    /// </summary>
    public DistanceExtrema ComputeMaxMinTrueDistance(
        JulianDay jdEt,
        CelestialBody body,
        EphemerisFlags ephemerisFlags)
    {
        ValidateMaxMinDistanceBody(body);
        ValidateOrbitalElementsFlags(ephemerisFlags);

        var heliocentric = (ephemerisFlags & EphemerisFlags.Heliocentric) != 0
                           || (ephemerisFlags & EphemerisFlags.Barycentric) != 0
                           || body == CelestialBody.Sun
                           || body == CelestialBody.Moon;

        if (heliocentric)
            return ComputeMaxMinDistanceHelio(jdEt, body, ephemerisFlags);

        return ComputeMaxMinDistanceGeo(jdEt, body, ephemerisFlags);
    }

    // ---- swe_get_orbital_elements core (swecl.c#L5772-L5959) -------------
    private OrbitalElements ComputeOrbitalElementsCore(
        JulianDay jdEt,
        CelestialBody body,
        EphemerisFlags flags)
    {
        // Reference ecliptic: J2000.
        var bodyIsMoon = body == CelestialBody.Moon;
        var helioDist = bodyIsMoon ? 0.0 : EstimateHelioDistance(body, jdEt, flags);

        bool ellipseIsBary = false;
        if (!bodyIsMoon && (flags & EphemerisFlags.Barycentric) != 0 && helioDist > 6.0)
            ellipseIsBary = true;
        _ = ellipseIsBary; // Moshier helio == bary, no separate path.

        var Gmsm = ComputeGmsm(body);

        // Get state in J2000 ECLIPTIC frame, helio for planets/Earth, geo for Moon.
        Span<double> xpos = stackalloc double[6];
        ComputeJ2000EclipticState(body, jdEt, flags, xpos);

        // Kepler element extraction (swecl.c#L5820-L5914).
        var fac = xpos[2] / xpos[5];
        var sgn = xpos[5] / System.Math.Abs(xpos[5]);
        Span<double> xn = stackalloc double[3];
        for (int j = 0; j < 3; j++)
            xn[j] = (xpos[j] - fac * xpos[j + 3]) * sgn;

        var rxy = System.Math.Sqrt(xn[0] * xn[0] + xn[1] * xn[1]);
        var cosnode = xn[0] / rxy;
        var sinnode = xn[1] / rxy;

        var nx = xpos[1] * xpos[5] - xpos[2] * xpos[4];
        var ny = xpos[2] * xpos[3] - xpos[0] * xpos[5];
        var nz = xpos[0] * xpos[4] - xpos[1] * xpos[3];
        var rxy2 = nx * nx + ny * ny;
        var c2 = rxy2 + nz * nz;
        var rxyz = System.Math.Sqrt(c2);
        var rxyN = System.Math.Sqrt(rxy2);
        var sinincl = rxyN / rxyz;
        var cosincl = System.Math.Sqrt(1.0 - sinincl * sinincl);
        if (nz < 0) cosincl = -cosincl;
        var inclDeg = System.Math.Acos(cosincl) * RadToDeg;

        var cosu = xpos[0] * cosnode + xpos[1] * sinnode;
        var sinu = xpos[2] / sinincl;
        var uu = System.Math.Atan2(sinu, cosu);

        rxyz = System.Math.Sqrt(xpos[0] * xpos[0] + xpos[1] * xpos[1] + xpos[2] * xpos[2]);
        var v2 = xpos[3] * xpos[3] + xpos[4] * xpos[4] + xpos[5] * xpos[5];
        var sema = 1.0 / (2.0 / rxyz - v2 / Gmsm);

        var pp = c2 / Gmsm;
        var ecceArg = pp / sema;
        if (ecceArg > 1.0) ecceArg = 1.0;
        var ecce = System.Math.Sqrt(1.0 - ecceArg);
        var ecce2 = ecce == 0 ? 1e-10 : ecce;

        var cosE = (1.0 - rxyz / sema) / ecce2;
        var sinE = (xpos[0] * xpos[3] + xpos[1] * xpos[4] + xpos[2] * xpos[5])
                   / (ecce2 * System.Math.Sqrt(sema * Gmsm));
        var eanomDeg = AngleMath.NormalizeDegrees(System.Math.Atan2(sinE, cosE) * RadToDeg);
        var nyTrue = 2.0 * System.Math.Atan(
            System.Math.Sqrt((1.0 + ecce) / (1.0 - ecce)) * sinE / (1.0 + cosE));
        var tanomDeg = AngleMath.NormalizeDegrees(nyTrue * RadToDeg);
        // Quadrant correction (swecl.c#L5865-L5868).
        if (eanomDeg > 180 && tanomDeg < 180) tanomDeg += 180;
        if (eanomDeg < 180 && tanomDeg > 180) tanomDeg -= 180;
        var manomDeg = AngleMath.NormalizeDegrees(
            eanomDeg - ecce * RadToDeg * System.Math.Sin(eanomDeg * DegToRad));

        // Argument of perihelion (parg) — distance of perihelion from asc node.
        Span<double> xq = stackalloc double[3];
        xq[0] = Mod2Pi(uu - nyTrue);
        var pargRad = xq[0];
        xq[1] = 0.0;
        xq[2] = sema * (1.0 - ecce);
        // polar → cartesian.
        Span<double> xqc = stackalloc double[3];
        var (sinL, cosL) = System.Math.SinCos(xq[0]);
        var (sinB, cosB) = System.Math.SinCos(xq[1]);
        xqc[0] = xq[2] * cosB * cosL;
        xqc[1] = xq[2] * cosB * sinL;
        xqc[2] = xq[2] * sinB;
        // rotate orbital plane → ecliptic.
        var yo = xqc[1]; var zo = xqc[2];
        xqc[1] = yo * cosincl - zo * sinincl;
        xqc[2] = yo * sinincl + zo * cosincl;
        // cartesian → polar (we only need lon).
        var rxy_q = System.Math.Sqrt(xqc[0] * xqc[0] + xqc[1] * xqc[1]);
        var lonQrad = rxy_q == 0 ? 0.0 : System.Math.Atan2(xqc[1], xqc[0]);
        // Node lon from xn[0..2] cartesian (ecliptic): atan2(xn[1], xn[0]).
        // Following C swecl.c#L5910-L5914.
        var nodeRad = System.Math.Atan2(xn[1], xn[0]);

        var nodeDeg = nodeRad * RadToDeg;
        var pargDeg = pargRad * RadToDeg;
        var periDeg = AngleMath.NormalizeDegrees(nodeDeg + pargDeg);
        var mlonDeg = AngleMath.NormalizeDegrees(manomDeg + periDeg);
        var node360 = AngleMath.NormalizeDegrees(nodeDeg);

        // Sidereal period in sidereal years (J2000-anchored).
        double csid = sema * System.Math.Sqrt(sema);
        if (body == CelestialBody.Moon)
        {
            var semam = sema * AstronomicalConstants.AstronomicalUnitMeters / 383397772.5;
            csid = semam * System.Math.Sqrt(semam);
            csid *= 27.32166 / 365.25636300;
        }
        var dmot = 0.9856076686 / csid;
        csid *= 365.25636 / 365.242189;

        // Tropical period (swecl.c#L5923-L5933).
        var T = (jdEt.Value - AstronomicalConstants.J2000) / 365250.0;
        var T2 = T * T; var T3 = T2 * T; var T4 = T3 * T; var T5 = T4 * T;
        var pa = (50288.200 + 222.4045 * T + 0.2095 * T2 - 0.9408 * T3 - 0.0090 * T4 + 0.0010 * T5)
                 / 3600.0 / 365250.0;
        var ysid = (1295977422.83429 - 2 * 2.0441 * T - 3 * 0.00523 * T * T) / 3600.0 / 365250.0;
        ysid = 360.0 / ysid;
        var ytrop = (1296027711.03429 + 2 * 109.15809 * T + 3 * 0.07207 * T2
                     - 4 * 0.23530 * T3 - 5 * 0.00180 * T4 + 6 * 0.00020 * T5)
                    / 3600.0 / 365250.0;
        ytrop = 360.0 / ytrop;
        var ctro = 360.0 / (dmot + pa) / 365.242189;
        ctro *= ysid / ytrop;
        var csyn = body == CelestialBody.Earth ? 0.0 : 360.0 / (0.9856076686 - dmot);

        var rperi = sema * (1.0 - ecce);
        var raph = sema * (1.0 + ecce);
        var tperi = jdEt.Value - manomDeg / dmot;

        return new OrbitalElements(
            sema, ecce, inclDeg,
            node360, pargDeg, periDeg,
            manomDeg, tanomDeg, eanomDeg, mlonDeg,
            csid, dmot, ctro, csyn,
            tperi, rperi, raph);
    }

    // ---- Max/min true distance helio path (swecl.c#L6090-L6117) ----------
    private DistanceExtrema ComputeMaxMinDistanceHelio(JulianDay jdEt, CelestialBody body, EphemerisFlags flags)
    {
        var bodyForOrbit = body == CelestialBody.Sun ? CelestialBody.Earth : body;
        ValidateOrbitalElementsBody(bodyForOrbit);
        var de = ComputeOrbitalElementsCore(jdEt, bodyForOrbit, flags);
        var dmax = de.AphelionDistanceAu;
        var dmin = de.PerihelionDistanceAu;
        Span<double> pqr = stackalloc double[12];
        OscOrbitConstants(de, pqr);
        Span<double> xinner = stackalloc double[3];
        OscEclPos(de.EccentricAnomalyDeg, pqr, xinner);
        var dtrue = System.Math.Sqrt(xinner[0] * xinner[0] + xinner[1] * xinner[1] + xinner[2] * xinner[2]);
        return new DistanceExtrema(dmax, dmin, dtrue);
    }

    // ---- Max/min true distance geocentric path (swecl.c#L6159-L6276) -----
    private DistanceExtrema ComputeMaxMinDistanceGeo(JulianDay jdEt, CelestialBody body, EphemerisFlags flags)
    {
        ValidateOrbitalElementsBody(body);
        var dp = ComputeOrbitalElementsCore(jdEt, body, flags);
        var de = ComputeOrbitalElementsCore(jdEt, CelestialBody.Earth, flags);

        // Outer = larger semi-major axis.
        var bodyOuter = de.SemiMajorAxisAu > dp.SemiMajorAxisAu;
        var douter = bodyOuter ? de : dp;
        var dinner = bodyOuter ? dp : de;

        Span<double> pqro = stackalloc double[12];
        Span<double> pqri = stackalloc double[12];
        OscOrbitConstants(douter, pqro);
        OscOrbitConstants(dinner, pqri);

        Span<double> xouter = stackalloc double[3];
        Span<double> xinner = stackalloc double[3];
        OscEclPos(douter.EccentricAnomalyDeg, pqro, xouter);
        OscEclPos(dinner.EccentricAnomalyDeg, pqri, xinner);
        var rtrue = DistanceVec3(xouter, xinner);

        // Brute-force scan in 2-degree steps.
        const int ncnt = 182;
        const double dstep = 2.0;
        Span<double> maxXouter = stackalloc double[3];
        Span<double> maxXinner = stackalloc double[3];
        Span<double> minXouter = stackalloc double[3];
        Span<double> minXinner = stackalloc double[3];
        double rmax = 0, rmin = 1e8;
        double maxEano = 0, maxEani = 0, minEano = 0, minEani = 0;
        for (int j = 0; j < ncnt; j++)
        {
            var eano = j * dstep;
            OscEclPos(eano, pqro, xouter);
            for (int i = 0; i < ncnt; i++)
            {
                var eani = (double)i;
                OscEclPos(eani, pqri, xinner);
                var r = DistanceVec3(xouter, xinner);
                if (r > rmax)
                {
                    rmax = r;
                    maxEano = eano; maxEani = eani;
                    xouter.CopyTo(maxXouter);
                    xinner.CopyTo(maxXinner);
                }
                if (r < rmin)
                {
                    rmin = r;
                    minEano = eano; minEani = eani;
                    xouter.CopyTo(minXouter);
                    xinner.CopyTo(minXinner);
                }
            }
        }

        // Refine maximum.
        var eaniM = maxEani; var eanoM = maxEano;
        maxXouter.CopyTo(xouter);
        maxXinner.CopyTo(xinner);
        const int nitermax = 300;
        double rmaxsv = 0;
        for (int k = 0; k <= nitermax; k++)
        {
            OscIterateMaxDist(pqri, xinner, xouter, ref eaniM, out var rmaxA);
            OscIterateMaxDist(pqro, xouter, xinner, ref eanoM, out var rmaxB);
            rmax = rmaxB;
            if (k > 0 && System.Math.Abs(rmax - rmaxsv) < 1e-8)
                break;
            rmaxsv = rmax;
        }
        // Refine minimum.
        var eaniMin = minEani; var eanoMin = minEano;
        minXouter.CopyTo(xouter);
        minXinner.CopyTo(xinner);
        double rminsv = 0;
        for (int k = 0; k <= nitermax; k++)
        {
            OscIterateMinDist(pqri, xinner, xouter, ref eaniMin, out var rminA);
            OscIterateMinDist(pqro, xouter, xinner, ref eanoMin, out var rminB);
            rmin = rminB;
            if (k > 0 && System.Math.Abs(rmin - rminsv) < 1e-8)
                break;
            rminsv = rmin;
        }
        return new DistanceExtrema(rmax, rmin, rtrue);
    }

    // ---- Geometry helpers from swecl.c#L5962-L6085 -----------------------
    private static void OscOrbitConstants(in OrbitalElements de, Span<double> pqr)
    {
        var ecce = de.Eccentricity;
        var fac = System.Math.Sqrt((1.0 - ecce) * (1.0 + ecce));
        var (sinNode, cosNode) = System.Math.SinCos(de.LongitudeOfAscendingNodeDeg * DegToRad);
        var (sinIncl, cosIncl) = System.Math.SinCos(de.InclinationDeg * DegToRad);
        var (sinParg, cosParg) = System.Math.SinCos(de.ArgumentOfPeriapsisDeg * DegToRad);
        pqr[0] = cosParg * cosNode - sinParg * cosIncl * sinNode;
        pqr[1] = -sinParg * cosNode - cosParg * cosIncl * sinNode;
        pqr[2] = sinIncl * sinNode;
        pqr[3] = cosParg * sinNode + sinParg * cosIncl * cosNode;
        pqr[4] = -sinParg * sinNode + cosParg * cosIncl * cosNode;
        pqr[5] = -sinIncl * cosNode;
        pqr[6] = sinParg * sinIncl;
        pqr[7] = cosParg * sinIncl;
        pqr[8] = cosIncl;
        pqr[9] = de.SemiMajorAxisAu;
        pqr[10] = ecce;
        pqr[11] = fac;
    }

    private static void OscEclPos(double eanDeg, ReadOnlySpan<double> pqr, Span<double> xp)
    {
        var (sinE, cosE) = System.Math.SinCos(eanDeg * DegToRad);
        var sema = pqr[9];
        var ecce = pqr[10];
        var fac = pqr[11];
        var x0 = sema * (cosE - ecce);
        var x1 = sema * fac * sinE;
        xp[0] = pqr[0] * x0 + pqr[1] * x1;
        xp[1] = pqr[3] * x0 + pqr[4] * x1;
        xp[2] = pqr[6] * x0 + pqr[7] * x1;
    }

    private static double DistanceVec3(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        var dx = a[0] - b[0]; var dy = a[1] - b[1]; var dz = a[2] - b[2];
        return System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static void OscIterateMaxDist(
        ReadOnlySpan<double> pqr, Span<double> xa, ReadOnlySpan<double> xb,
        ref double ean, out double rmax)
    {
        const double dstepMin = 1e-6;
        ean = 0;
        OscEclPos(ean, pqr, xa);
        var r = DistanceVec3(xb, xa);
        rmax = r;
        var dstep = 1.0;
        double eanSv = 0;
        while (dstep >= dstepMin)
        {
            for (int i = 0; i < 2; i++)
            {
                while (r >= rmax)
                {
                    eanSv = ean;
                    if (i == 0) ean += dstep; else ean -= dstep;
                    OscEclPos(ean, pqr, xa);
                    r = DistanceVec3(xb, xa);
                    if (r > rmax) rmax = r;
                }
                ean = eanSv;
                r = rmax;
            }
            ean = eanSv;
            r = rmax;
            dstep /= 10.0;
        }
        ean = eanSv;
    }

    private static void OscIterateMinDist(
        ReadOnlySpan<double> pqr, Span<double> xa, ReadOnlySpan<double> xb,
        ref double ean, out double rmin)
    {
        const double dstepMin = 1e-6;
        ean = 0;
        OscEclPos(ean, pqr, xa);
        var r = DistanceVec3(xb, xa);
        rmin = r;
        var dstep = 1.0;
        double eanSv = 0;
        while (dstep >= dstepMin)
        {
            for (int i = 0; i < 2; i++)
            {
                while (r <= rmin)
                {
                    eanSv = ean;
                    if (i == 0) ean += dstep; else ean -= dstep;
                    OscEclPos(ean, pqr, xa);
                    r = DistanceVec3(xb, xa);
                    if (r < rmin) rmin = r;
                }
                ean = eanSv;
                r = rmin;
            }
            ean = eanSv;
            r = rmin;
            dstep /= 10.0;
        }
        ean = eanSv;
    }

    // ---- Frame conversion: source raw → mean ecl-of-date (helio for non-
    //      Moon, geo for Moon). Mirrors swi_plan_for_osc_elem with NONUT.
    //      Routes through SourceRouter so SwissEph and JPL inputs are handled
    //      alongside the original Moshier path.
    private void ComputeMeanEclOfDateState(CelestialBody body, JulianDay jd, EphemerisFlags flags, Span<double> xpos)
    {
        if (body == CelestialBody.Moon)
        {
            FetchMoonGeoEclOfDate(jd, flags, xpos);
            return;
        }

        // Fetch helio J2000 equator state for the body (true Earth, not EMB).
        Span<double> raw = stackalloc double[6];
        FetchBodyHelioJ2000Equator(body, jd, flags, raw);

        // Precess equ-J2000 → equ-of-date. swi_plan_for_osc_elem (sweph.c#L5787-
        // L5788) uses plain rotational precession on BOTH position and velocity
        // — no precession-rate (dpre) term. The rate term is reserved for the
        // shared output pipeline below where the apparent-velocity correction
        // is needed.
        Span<double> pos3 = stackalloc double[3] { raw[0], raw[1], raw[2] };
        Precession.Apply(pos3, AstronomicalConstants.J2000, jd.Value, _models);
        raw[0] = pos3[0]; raw[1] = pos3[1]; raw[2] = pos3[2];
        Span<double> vel3 = stackalloc double[3] { raw[3], raw[4], raw[5] };
        Precession.Apply(vel3, AstronomicalConstants.J2000, jd.Value, _models);
        raw[3] = vel3[0]; raw[4] = vel3[1]; raw[5] = vel3[2];

        // Rotate equ-of-date → ecl-of-date.
        var meanEpsRad = Precession.MeanObliquity(jd.Value, _models);
        var meanEpsDeg = meanEpsRad * RadToDeg;
        Coortrf2(raw, meanEpsDeg);

        for (int i = 0; i < 6; i++) xpos[i] = raw[i];

        // For Earth: compute EMB by adding Moon/(mratio+1). Moon is already in
        // geocentric ecl-of-date here, so the addition is in-frame.
        if (body == CelestialBody.Earth)
        {
            Span<double> moon6 = stackalloc double[6];
            FetchMoonGeoEclOfDate(jd, flags, moon6);
            var weight = 1.0 / (AstronomicalConstants.EarthMoonMassRatio + 1.0);
            for (int i = 0; i < 6; i++) xpos[i] += moon6[i] * weight;
        }
    }

    /// <summary>
    /// State in J2000 ecliptic (helio for planets/Earth/Moon-not-applicable;
    /// geo for the Moon). Used by <c>swe_get_orbital_elements</c> which
    /// references the J2000 ecliptic, not ecl-of-date. Source-routed.
    /// </summary>
    private void ComputeJ2000EclipticState(CelestialBody body, JulianDay jd, EphemerisFlags flags, Span<double> xpos)
    {
        if (body == CelestialBody.Moon)
        {
            // Get moon in geo ecl-of-date (any source); precess to J2000 ecl.
            FetchMoonGeoEclOfDate(jd, flags, xpos);
            // ecl-of-date → equ-of-date.
            var meanEpsRadM = Precession.MeanObliquity(jd.Value, _models);
            Coortrf2(xpos, -meanEpsRadM * RadToDeg);
            // equ-of-date → equ-J2000 (rotational precession on pos AND vel).
            Span<double> pos3M = stackalloc double[3] { xpos[0], xpos[1], xpos[2] };
            Precession.Apply(pos3M, jd.Value, AstronomicalConstants.J2000, _models);
            xpos[0] = pos3M[0]; xpos[1] = pos3M[1]; xpos[2] = pos3M[2];
            Span<double> vel3M = stackalloc double[3] { xpos[3], xpos[4], xpos[5] };
            Precession.Apply(vel3M, jd.Value, AstronomicalConstants.J2000, _models);
            xpos[3] = vel3M[0]; xpos[4] = vel3M[1]; xpos[5] = vel3M[2];
            // equ-J2000 → ecl-J2000.
            var eps2000RadM = Precession.MeanObliquity(AstronomicalConstants.J2000, _models);
            Coortrf2(xpos, eps2000RadM * RadToDeg);
            return;
        }

        // All non-moon bodies: get helio J2000 equator → rotate to J2000 ecliptic.
        Span<double> raw = stackalloc double[6];
        FetchBodyHelioJ2000Equator(body, jd, flags, raw);
        var eps2000Rad = Precession.MeanObliquity(AstronomicalConstants.J2000, _models);
        Coortrf2(raw, eps2000Rad * RadToDeg);

        if (body == CelestialBody.Earth)
        {
            // Add EMB delta: Moon/(mratio+1). Moon is geo ecl-of-date; precess to J2000 ecl.
            Span<double> mpos = stackalloc double[6];
            FetchMoonGeoEclOfDate(jd, flags, mpos);
            var meanEpsRadE = Precession.MeanObliquity(jd.Value, _models);
            Coortrf2(mpos, -meanEpsRadE * RadToDeg);
            Span<double> pos3E = stackalloc double[3] { mpos[0], mpos[1], mpos[2] };
            Precession.Apply(pos3E, jd.Value, AstronomicalConstants.J2000, _models);
            mpos[0] = pos3E[0]; mpos[1] = pos3E[1]; mpos[2] = pos3E[2];
            Span<double> vel3E = stackalloc double[3] { mpos[3], mpos[4], mpos[5] };
            Precession.Apply(vel3E, jd.Value, AstronomicalConstants.J2000, _models);
            mpos[3] = vel3E[0]; mpos[4] = vel3E[1]; mpos[5] = vel3E[2];
            Coortrf2(mpos, eps2000Rad * RadToDeg);
            var weight = 1.0 / (AstronomicalConstants.EarthMoonMassRatio + 1.0);
            for (int i = 0; i < 6; i++) raw[i] += mpos[i] * weight;
        }

        for (int i = 0; i < 6; i++) xpos[i] = raw[i];
    }

    private double EstimateHelioDistance(CelestialBody body, JulianDay jd, EphemerisFlags flags)
    {
        Span<double> helio = stackalloc double[6];
        FetchBodyHelioJ2000Equator(body, jd, flags, helio);
        return System.Math.Sqrt(helio[0] * helio[0] + helio[1] * helio[1] + helio[2] * helio[2]);
    }

    private static double GetPlanetMassRatio(CelestialBody body) => body switch
    {
        CelestialBody.Mercury => OrbitalElementTables.PlanetMassRatios[0],
        CelestialBody.Venus => OrbitalElementTables.PlanetMassRatios[1],
        CelestialBody.Earth => OrbitalElementTables.PlanetMassRatios[2],
        CelestialBody.Mars => OrbitalElementTables.PlanetMassRatios[3],
        CelestialBody.Jupiter => OrbitalElementTables.PlanetMassRatios[4],
        CelestialBody.Saturn => OrbitalElementTables.PlanetMassRatios[5],
        CelestialBody.Uranus => OrbitalElementTables.PlanetMassRatios[6],
        CelestialBody.Neptune => OrbitalElementTables.PlanetMassRatios[7],
        CelestialBody.Pluto => OrbitalElementTables.PlanetMassRatios[8],
        _ => 0,
    };

    private double ComputeGmsm(CelestialBody body)
    {
        if (body == CelestialBody.Moon)
        {
            return AstronomicalConstants.GeoGravConst
                   * (1.0 + 1.0 / AstronomicalConstants.EarthMoonMassRatio)
                   / Au3 * Sec2;
        }
        var massRatio = GetPlanetMassRatio(body);
        var plm = massRatio > 0 ? 1.0 / massRatio : 0.0;
        return AstronomicalConstants.HelioGravConst * (1.0 + plm) / Au3 * Sec2;
    }

    /// <summary>
    /// Enforces the Milestone-3 scope: geocentric observer, ecliptic-of-date
    /// polar output. Source bits (Moshier / SwissEph / JPL) are routed through
    /// <see cref="SourceRouter"/> since 3C. TRUEPOS / NOABERR / NOGDEFL / NONUT
    /// are honored individually since 3B; deferred frame flags (sidereal,
    /// J2000, equatorial, cartesian, radians) still throw with the equivalent
    /// C feature named.
    /// </summary>
    private void ValidateScope(CelestialBody body, EphemerisFlags flags, NodesApsidesMethod method)
    {
        ValidateSourceBits(flags, "swe_nod_aps");

        if ((flags & EphemerisFlags.Heliocentric) != 0)
            throw new NotSupportedException(
                "NodesAndApsidesService does not support SEFLG_HELCTR. "
                + "Only the default geocentric observer is implemented; "
                + "swe_orbit_max_min_true_distance accepts SEFLG_HELCTR/BARYCTR but "
                + "swe_nod_aps does not switch to a heliocentric observer.");
        if ((flags & EphemerisFlags.Barycentric) != 0)
            throw new NotSupportedException(
                "NodesAndApsidesService does not support SEFLG_BARYCTR. "
                + "Only the default geocentric observer is implemented.");
        if ((flags & EphemerisFlags.Topocentric) != 0)
            throw new NotSupportedException(
                "NodesAndApsidesService does not support SEFLG_TOPOCTR. "
                + "Topocentric parallax / observer-on-Earth output is deferred (Milestone 3B/3C).");

        if ((flags & EphemerisFlags.Sidereal) != 0)
            throw new NotSupportedException(
                "NodesAndApsidesService does not support SEFLG_SIDEREAL. "
                + "Sidereal-ayanamsha output frames are deferred.");
        if ((flags & EphemerisFlags.J2000Equinox) != 0)
            throw new NotSupportedException(
                "NodesAndApsidesService does not support SEFLG_J2000. "
                + "Output is fixed to mean ecliptic of date; J2000-equinox output is deferred.");
        if ((flags & EphemerisFlags.Equatorial) != 0)
            throw new NotSupportedException(
                "NodesAndApsidesService does not support SEFLG_EQUATORIAL. "
                + "Output is fixed to ecliptic polar; equatorial conversion is deferred.");
        if ((flags & EphemerisFlags.Cartesian) != 0)
            throw new NotSupportedException(
                "NodesAndApsidesService does not support SEFLG_XYZ. "
                + "Output is fixed to polar (longitude/latitude/distance); cartesian conversion is deferred.");
        if ((flags & EphemerisFlags.Radians) != 0)
            throw new NotSupportedException(
                "NodesAndApsidesService does not support SEFLG_RADIANS. "
                + "Output angles are always in degrees.");

        bool isOsc = (method & NodesApsidesMethod.Osculating) != 0;
        switch (body)
        {
            case CelestialBody.Sun:
            case CelestialBody.Mercury:
            case CelestialBody.Venus:
            case CelestialBody.Earth:
            case CelestialBody.Mars:
            case CelestialBody.Jupiter:
            case CelestialBody.Saturn:
            case CelestialBody.Uranus:
            case CelestialBody.Neptune:
                return;
            case CelestialBody.Pluto:
                if (!isOsc)
                    throw new NotSupportedException(
                        "Mean nodes/apsides for Pluto are not defined: the C library's "
                        + "el_incl/el_node/el_peri tables (sweph.c) only carry rows for "
                        + "Mercury..Neptune. Pass NodesApsidesMethod.Osculating instead.");
                return;
            case CelestialBody.Moon:
                if (!isOsc)
                    throw new NotSupportedException(
                        "Mean nodes/apsides for the Moon are not implemented in this port. "
                        + "swe_nod_aps routes the Moon to swi_mean_lunar_elements (sweph.c), "
                        + "which has not been ported yet. Pass NodesApsidesMethod.Osculating instead.");
                return;
            default:
                throw new NotSupportedException(
                    $"Body {body} is not a valid input for swe_nod_aps; "
                    + "supported bodies are Sun, Moon (osculating), Mercury..Neptune, Pluto (osculating).");
        }
    }

    private static void ValidateOrbitalElementsBody(CelestialBody body)
    {
        // swecl.c#L5792-L5796: lunar nodes/apogees and the Sun are not valid.
        switch (body)
        {
            case CelestialBody.Mercury:
            case CelestialBody.Venus:
            case CelestialBody.Earth:
            case CelestialBody.Mars:
            case CelestialBody.Jupiter:
            case CelestialBody.Saturn:
            case CelestialBody.Uranus:
            case CelestialBody.Neptune:
            case CelestialBody.Pluto:
            case CelestialBody.Moon:
                return;
            default:
                throw new NotSupportedException(
                    $"Orbital elements are not defined for {body}.");
        }
    }

    private static void ValidateMaxMinDistanceBody(CelestialBody body)
    {
        // swe_orbit_max_min_true_distance accepts SE_SUN/SE_MOON via the helio
        // path; geocentric path requires an Earth ellipse, so Sun maps to Earth
        // internally.
        switch (body)
        {
            case CelestialBody.Sun:
            case CelestialBody.Mercury:
            case CelestialBody.Venus:
            case CelestialBody.Earth:
            case CelestialBody.Mars:
            case CelestialBody.Jupiter:
            case CelestialBody.Saturn:
            case CelestialBody.Uranus:
            case CelestialBody.Neptune:
            case CelestialBody.Pluto:
            case CelestialBody.Moon:
                return;
            default:
                throw new NotSupportedException(
                    $"Max/min distance is not defined for {body}.");
        }
    }

    private void ValidateOrbitalElementsFlags(EphemerisFlags flags)
    {
        ValidateSourceBits(flags, "swe_get_orbital_elements / swe_orbit_max_min_true_distance");
    }

    /// <summary>
    /// Common source-flag check for the public entry points: exactly one of
    /// MoshierEph / SwissEph / JplEph must be set, and the configured router
    /// must own that source. Mirrors <c>plaus_iflag</c>'s "exactly one source
    /// bit" rule (sweph.c#L6107) plus the M-XX policy of failing fast on a
    /// missing back-end rather than silently falling back.
    /// </summary>
    private void ValidateSourceBits(EphemerisFlags flags, string cFunctionName)
    {
        var sourceBits = flags & SourceMask;
        if (System.Numerics.BitOperations.PopCount((uint)sourceBits) > 1)
            throw new NotSupportedException(
                $"{cFunctionName}: more than one ephemeris-source bit is set "
                + "(MoshierEph / SwissEph / JplEph). Pick exactly one.");
        if (sourceBits == 0)
            throw new NotSupportedException(
                $"{cFunctionName}: no ephemeris-source flag set. Pass SEFLG_MOSEPH, "
                + "SEFLG_SWIEPH, or SEFLG_JPLEPH so the source router can pick a back-end.");

        if ((sourceBits & EphemerisFlags.MoshierEph) != 0 && !_router.Has(EphemerisSource.Moshier))
            throw new NotSupportedException(
                $"{cFunctionName}: SEFLG_MOSEPH requested but the Moshier source is not configured "
                + "in this context. Re-build the EphemerisContext with Moshier enabled.");
        if ((sourceBits & EphemerisFlags.SwissEph) != 0 && !_router.Has(EphemerisSource.SwissEph))
            throw new NotSupportedException(
                $"{cFunctionName}: SEFLG_SWIEPH requested but no Swiss Ephemeris file source is configured "
                + "in this context. Build the EphemerisContext with UseSwissEphFiles(...).");
        if ((sourceBits & EphemerisFlags.JplEph) != 0 && !_router.Has(EphemerisSource.Jpl))
            throw new NotSupportedException(
                $"{cFunctionName}: SEFLG_JPLEPH requested but no JPL DE source is configured "
                + "in this context. Build the EphemerisContext with UseJplFile(...).");
    }

    private double ApplyPipeline(
        Span<double> xp,
        JulianDay jdEt,
        JulianDay jdLiftSun,
        bool bodyIsSun,
        bool bodyIsEarth,
        bool bodyIsMoon,
        bool isNode,
        EphemerisFlags flags)
    {
        // Earth nodes are zeroed (no orbital plane around the EMB).
        if (bodyIsEarth && isNode)
        {
            xp.Clear();
            return 0.0;
        }

        var inSpeed = (flags & EphemerisFlags.Speed) != 0;
        var truePos = (flags & EphemerisFlags.TruePosition) != 0;
        var noAberr = (flags & EphemerisFlags.NoAberration) != 0;
        var noGdefl = (flags & EphemerisFlags.NoGravDeflection) != 0;
        var noNut = (flags & EphemerisFlags.NoNutation) != 0;
        // swecl.c#L5119-L5122 — Moon skips both deflection and (geocentric) aberration.
        var doDefl = !truePos && !noGdefl && !bodyIsMoon;
        var doAberr = !truePos && !noAberr && !bodyIsMoon;

        var meanEpsRad = Precession.MeanObliquity(jdEt.Value, _models);
        var meanEpsDeg = meanEpsRad * RadToDeg;
        Coortrf2(xp, -meanEpsDeg);

        Span<double> xpPos = stackalloc double[3] { xp[0], xp[1], xp[2] };
        Precession.Apply(xpPos, jdEt.Value, AstronomicalConstants.J2000, _models);
        xp[0] = xpPos[0]; xp[1] = xpPos[1]; xp[2] = xpPos[2];
        Precession.ApplySpeed(xp, jdEt.Value, AstronomicalConstants.J2000, _models);

        // ---- Lift vector and deflection earthHelio (swecl.c#L5474-L5480) ----
        // C reads xsun = pldat[SEI_SUNBARY].x and xear = pldat[SEI_EARTH].x as
        // pointers into the global save area. xobs is captured ONCE before the
        // four-point loop (L5424) at time t. Inside the aberration speed-block
        // (L5503), swe_calc(t-dt, ipli, iflg0+HELCTR) updates pldat[SUNBARY]
        // and pldat[EARTH] to the iteration's body light-time (t - dt). The
        // intended "restore" call at L5534 (swe_calc(t, SE_SUN, iflg0+HELCTR))
        // returns immediately because SE_SUN with HELCTR is a no-op (sweph.c
        // L831-L835), so pldat is left at (t - dt) for the next iteration.
        // We mirror that side effect by:
        //   liftVec = baryEarth(jdEt) - barySun(jdLiftSun)
        // where jdLiftSun = jdEt for ij=0 and jdEt - prev_dt for ij≥1.
        Span<double> liftVec = stackalloc double[6];
        Span<double> earthHelioForDefl = stackalloc double[6];
        FetchLiftVectorForApplyPipeline(jdEt, jdLiftSun, flags, liftVec, earthHelioForDefl);

        // Helio→bary correction (skipped for Moshier where helio == bary). Moon
        // is handled separately: the C path adds xear before subtracting xobs,
        // which cancels for the default geocentric observer.
        if (!bodyIsMoon)
        {
            for (int i = 0; i < 6; i++) xp[i] -= liftVec[i];
        }

        if (bodyIsSun)
            for (int i = 0; i < 6; i++) xp[i] = -xp[i];

        // ---- Apparent-position corrections in J2000 equator (swecl.c#L5485-L5537) ----
        // Light-time of the *undeflected* geocentric body — C captures this
        // at swecl.c#L5488 before deflection and reuses it for the aberration
        // speed-correction at L5503. Computing it later (e.g. after aberration
        // has shifted xp[0..2]) would change the t-dt sample point and break
        // the dt-corrected speed matching for inner+outer planets.
        var dtLightTime = (doDefl || (doAberr && inSpeed))
            ? System.Math.Sqrt(xp[0] * xp[0] + xp[1] * xp[1] + xp[2] * xp[2])
              * AstronomicalConstants.LightTimeAuPerDay
            : 0.0;

        if (doDefl)
        {
            // C swi_deflect_light reads xearth = pldat[0] and psdp->x at the
            // pldat-time, i.e. helio Earth at jdLiftSun. Mirrors that.
            ReadOnlySpan<double> earthHelio = earthHelioForDefl;
            ReadOnlySpan<double> earthHelioVel = earthHelioForDefl.Slice(3, 3);
            GravitationalDeflection.Apply(xp, earthHelio, earthHelioVel, inSpeed);
        }
        if (doAberr)
        {
            // C uses the *barycentric* Earth velocity for aberration
            // (swecl.c#L5495: swi_aberr_light(xp, xobs, iflag) where
            // xobs = pldat[SEI_EARTH].x in raw bary frame). For Moshier
            // bary == helio (Sun-at-origin), so reusing earthHelio there
            // is exact. For SwissEph/JPL the Sun has a non-zero barycentric
            // velocity (~12 m/s) which shifts the aberration direction by
            // ~v/c ≈ 4e-8 rad — visible as ~2.5e-6° in the apparent output.
            Span<double> earthBary = stackalloc double[6];
            FetchEarthBaryJ2000Equator(jdEt, flags, earthBary);
            ReadOnlySpan<double> obsVel = earthBary.Slice(3, 3);
            Aberration.Apply(xp, obsVel, inSpeed);
            // Speed correction for the change in observer velocity across the
            // light-time interval (swecl.c#L5501-L5536). dt is the light-time
            // of the apparent position to the observer. C uses xobs (bary)
            // both at t and at t-dt for this difference, so we mirror that.
            if (inSpeed)
            {
                Span<double> earthBaryMinusDt = stackalloc double[6];
                FetchEarthBaryJ2000Equator(new JulianDay(jdEt.Value - dtLightTime), flags, earthBaryMinusDt);
                for (int i = 3; i < 6; i++)
                    xp[i] += earthBary[i] - earthBaryMinusDt[i];
            }
        }

        Span<double> xpPos2 = stackalloc double[3] { xp[0], xp[1], xp[2] };
        Precession.Apply(xpPos2, AstronomicalConstants.J2000, jdEt.Value, _models);
        xp[0] = xpPos2[0]; xp[1] = xpPos2[1]; xp[2] = xpPos2[2];
        Precession.ApplySpeed(xp, AstronomicalConstants.J2000, jdEt.Value, _models);

        // ---- Nutation in equator-of-date (swecl.c#L5552-L5553) ----
        // Output frame is mean ecl-of-date when NONUT, true ecl-of-date otherwise.
        // Trigonometrically, the C library composes two rotations
        //   eq_true → ecl_mean (by mean ε) → ecl_true (by Δε)
        // we collapse them into a single rotation by trueEps = meanEps + Δε.
        var epsForEclRotationDeg = meanEpsDeg;
        if (!noNut)
        {
            if (inSpeed)
            {
                Nutation.ApplyWithSpeed(xp, jdEt.Value, backward: false, _models);
            }
            else
            {
                Span<double> nutPos = stackalloc double[3] { xp[0], xp[1], xp[2] };
                var nut = Nutation.Compute(jdEt.Value, _models);
                Nutation.Apply(nutPos, nut, meanEpsRad, backward: false);
                xp[0] = nutPos[0]; xp[1] = nutPos[1]; xp[2] = nutPos[2];
            }
            var nutAngles = Nutation.Compute(jdEt.Value, _models);
            epsForEclRotationDeg = (meanEpsRad + nutAngles.DeltaEpsilonRad) * RadToDeg;
        }

        Coortrf2(xp, epsForEclRotationDeg);

        return dtLightTime;
    }

    /// <summary>
    /// Returns true when the four-point loop must mirror C's pldat side effect
    /// (swecl.c#L5503): the swe_calc(t-dt, ipli, iflg0+HELCTR) inside the
    /// aberration speed-block updates pldat[SUNBARY]/pldat[EARTH] for SwissEph
    /// / JPL, and the L5534 "restore" call is a no-op for SE_SUN+HELCTR. So
    /// iteration ij≥1 reads xsun/xear at (t - prev_dt) instead of t. Moshier's
    /// +xsun lift is gated off (L5472) and its swi_moshplan does not touch
    /// pldat[SUNBARY], so no mirroring is needed.
    /// </summary>
    private bool ShouldMirrorPldatSideEffect(JulianDay jdEt, CelestialBody body, EphemerisFlags flags)
    {
        bool isMoon = body == CelestialBody.Moon;
        bool truePos = (flags & EphemerisFlags.TruePosition) != 0;
        bool noAberr = (flags & EphemerisFlags.NoAberration) != 0;
        bool inSpeed = (flags & EphemerisFlags.Speed) != 0;
        bool doAberr = !truePos && !noAberr && !isMoon;
        if (!(doAberr && inSpeed)) return false;
        // The body for source resolution is the orbit's body — Sun maps to
        // Earth in C (line 5117). Use Earth as a stand-in here since the
        // resolver only inspects the source flags, not the body kind.
        var (source, _) = _router.Resolve(
            (flags & SourceMask) | EphemerisFlags.Speed,
            body == CelestialBody.Sun ? CelestialBody.Earth : body,
            jdEt);
        return source.Kind != EphemerisSource.Moshier;
    }

    /// <summary>
    /// Computes the helio→geo lift vector and the deflection earthHelio that
    /// mirror the C library's pldat side effect across the four-point loop in
    /// <c>swe_nod_aps</c> (swecl.c#L5474-L5490).
    ///
    /// For SwissEph / JPL the lift is <c>baryEarth(jdEt) - barySun(jdLiftSun)</c>
    /// because xobs (bary Earth) is captured ONCE at jdEt (L5424) but xsun is
    /// the live <c>pldat[SUNBARY]</c> pointer which the aberration speed-block
    /// has shifted. Deflection's earthHelio mirrors C's <c>xearth - psdp->x</c>
    /// at jdLiftSun. For Moshier the +xsun branch is skipped so jdLiftSun is
    /// unused and the result is helio Earth at jdEt.
    /// </summary>
    private void FetchLiftVectorForApplyPipeline(
        JulianDay jdEarthAtT,
        JulianDay jdSunVarying,
        EphemerisFlags flags,
        Span<double> liftVec,
        Span<double> earthHelioForDefl)
    {
        var srcFlags = (flags & SourceMask) | EphemerisFlags.Speed;
        var (source, _) = _router.Resolve(srcFlags, CelestialBody.Earth, jdEarthAtT);

        if (source.Kind == EphemerisSource.Moshier)
        {
            // Moshier: helio == bary, +xsun is skipped in C — no t1 dependence.
            FetchEarthHelioRawForLift(jdEarthAtT, flags, liftVec);
            liftVec.CopyTo(earthHelioForDefl);
            return;
        }

        // SwissEph / JPL: lift = baryEarth(t) - barySun(varying).
        Span<double> baryEarthFixed = stackalloc double[6];
        FetchEarthBaryJ2000Equator(jdEarthAtT, flags, baryEarthFixed);
        Span<double> sunBaryVarying = stackalloc double[6];
        FetchSunBaryJ2000Equator(jdSunVarying, srcFlags, sunBaryVarying);
        for (int i = 0; i < 6; i++) liftVec[i] = baryEarthFixed[i] - sunBaryVarying[i];

        if (jdSunVarying.Value == jdEarthAtT.Value)
        {
            liftVec.CopyTo(earthHelioForDefl);
        }
        else
        {
            // Deflection: helio Earth at jdLiftSun = baryEarth(varying) - barySun(varying).
            Span<double> baryEarthVarying = stackalloc double[6];
            FetchEarthBaryJ2000Equator(jdSunVarying, flags, baryEarthVarying);
            for (int i = 0; i < 6; i++) earthHelioForDefl[i] = baryEarthVarying[i] - sunBaryVarying[i];
        }
    }

    /// <summary>
    /// Earth in heliocentric J2000-equator with speed, sourced through the
    /// <see cref="SourceRouter"/>. Heap-allocating overload kept for ergonomics
    /// at the call sites that pass it through to <see cref="ApplyPipeline"/>.
    /// Note: For SwissEph / JPL the helio→geo lift in <c>swe_nod_aps</c>
    /// (swecl.c#L5468-L5480) uses raw barycentric Sun and Earth positions
    /// (no GCRS→J2000 frame bias), so this method intentionally returns the
    /// unbiased helio Earth state. The bias is applied only on the body's
    /// helio state via <see cref="FetchBodyHelioJ2000Equator"/> — matching
    /// the C library's mixed-frame composition.
    /// </summary>
    private double[] ComputeEarthHelioJ2000Equator(JulianDay jdEt, EphemerisFlags flags)
    {
        var arr = new double[6];
        FetchEarthHelioRawForLift(jdEt, flags, arr);
        return arr;
    }

    private void ComputeEarthHelioJ2000Equator(JulianDay jdEt, EphemerisFlags flags, Span<double> xear)
    {
        FetchEarthHelioRawForLift(jdEt, flags, xear);
    }

    /// <summary>
    /// Earth's heliocentric J2000-equator state used for the helio→geo lift
    /// in <see cref="ApplyPipeline"/>. For Moshier the raw source returns the
    /// helio-equator state directly (no bias). For SwissEph / JPL we read the
    /// raw barycentric Earth and Sun, subtract — and skip the GCRS→J2000 bias
    /// to mirror the C library, which uses raw <c>pldat[SEI_EARTH].x</c> and
    /// <c>pldat[SEI_SUNBARY].x</c> in the bary→geo subtraction (swecl.c#L5474-L5480).
    /// </summary>
    private void FetchEarthHelioRawForLift(JulianDay jd, EphemerisFlags flags, Span<double> xpos)
    {
        var srcFlags = (flags & SourceMask) | EphemerisFlags.Speed;
        var (source, resolved) = _router.Resolve(srcFlags, CelestialBody.Earth, jd);
        var raw = source.Compute(CelestialBody.Earth, jd, resolved);

        switch (raw.Frame)
        {
            case BodyStateFrame.HeliocentricJ2000Equator:
                Unpack(raw, xpos);
                return;
            case BodyStateFrame.HeliocentricJ2000Ecliptic:
                Unpack(raw, xpos);
                {
                    var eps2000 = Precession.MeanObliquity(AstronomicalConstants.J2000, _models);
                    Coortrf2(xpos, -eps2000 * RadToDeg);
                }
                return;
            case BodyStateFrame.BarycentricJ2000Equator:
                Unpack(raw, xpos);
                {
                    Span<double> sunBary = stackalloc double[6];
                    FetchSunBaryJ2000Equator(jd, srcFlags, sunBary);
                    for (int i = 0; i < 6; i++) xpos[i] -= sunBary[i];
                }
                return;
            default:
                throw new InvalidOperationException(
                    $"NodesAndApsidesService: unexpected Earth frame {raw.Frame} from {source.Kind}.");
        }
    }

    /// <summary>
    /// Earth in the *barycentric* J2000-equator frame, source-routed. Used
    /// by the aberration step in <see cref="ApplyPipeline"/>: the C library
    /// passes <c>pldat[SEI_EARTH].x</c> (raw bary state, no bias) as
    /// <c>xobs</c> to <c>swi_aberr_light</c>, so the observer velocity for
    /// aberration is the Earth's *barycentric* velocity, not the
    /// heliocentric one used for the helio→geo lift. For Moshier the Sun
    /// is at the origin and bary == helio for Earth, so this method
    /// short-circuits to the helio-equator state. For SwissEph / JPL we
    /// return the raw bary state (no bias), mirroring the C library's
    /// frame composition (cf. <see cref="FetchEarthHelioRawForLift"/>).
    /// </summary>
    private void FetchEarthBaryJ2000Equator(JulianDay jd, EphemerisFlags flags, Span<double> xpos)
    {
        var srcFlags = (flags & SourceMask) | EphemerisFlags.Speed;
        var (source, resolved) = _router.Resolve(srcFlags, CelestialBody.Earth, jd);
        var raw = source.Compute(CelestialBody.Earth, jd, resolved);

        switch (raw.Frame)
        {
            case BodyStateFrame.HeliocentricJ2000Equator:
                // Moshier: bary == helio (Sun at origin).
                Unpack(raw, xpos);
                return;
            case BodyStateFrame.HeliocentricJ2000Ecliptic:
                Unpack(raw, xpos);
                {
                    var eps2000 = Precession.MeanObliquity(AstronomicalConstants.J2000, _models);
                    Coortrf2(xpos, -eps2000 * RadToDeg);
                }
                return;
            case BodyStateFrame.BarycentricJ2000Equator:
                // SwissEph / JPL: source returns raw bary state directly.
                Unpack(raw, xpos);
                return;
            default:
                throw new InvalidOperationException(
                    $"NodesAndApsidesService: unexpected Earth frame {raw.Frame} from {source.Kind}.");
        }
    }

    /// <summary>
    /// Source-routed body fetch, normalised to heliocentric J2000-equator with
    /// speed. Each back-end has its own raw frame: Moshier emits helio J2000
    /// equator for Earth and helio J2000 ecliptic for the planets; SwissEph
    /// and JPL emit barycentric J2000 equator (raw .se1/JPL data is in ICRS
    /// frame and the C library applies the GCRS→J2000 frame bias twice along
    /// the swe_nod_aps path — once in <c>app_pos_etc_plan</c> (sweph.c#L2756)
    /// and again in <c>swi_plan_for_osc_elem</c> (sweph.c#L5767). To match the
    /// reference output we mirror that double application here.).
    /// </summary>
    private void FetchBodyHelioJ2000Equator(
        CelestialBody body,
        JulianDay jd,
        EphemerisFlags flags,
        Span<double> xpos)
    {
        var srcFlags = (flags & SourceMask) | EphemerisFlags.Speed;
        var (source, resolved) = _router.Resolve(srcFlags, body, jd);
        var raw = source.Compute(body, jd, resolved);

        switch (raw.Frame)
        {
            case BodyStateFrame.HeliocentricJ2000Equator:
                Unpack(raw, xpos);
                break;
            case BodyStateFrame.HeliocentricJ2000Ecliptic:
                Unpack(raw, xpos);
                {
                    var eps2000 = Precession.MeanObliquity(AstronomicalConstants.J2000, _models);
                    Coortrf2(xpos, -eps2000 * RadToDeg);
                }
                break;
            case BodyStateFrame.BarycentricJ2000Equator:
                Unpack(raw, xpos);
                {
                    Span<double> sunBary = stackalloc double[6];
                    FetchSunBaryJ2000Equator(jd, srcFlags, sunBary);
                    for (int i = 0; i < 6; i++) xpos[i] -= sunBary[i];
                }
                // Double GCRS→J2000 frame bias to match the C library's
                // swe_nod_aps composition for SwissEph / JPL inputs. The first
                // bias is applied in app_pos_etc_plan (sweph.c#L2756, inside
                // swe_calc) and the second one in swi_plan_for_osc_elem
                // (sweph.c#L5767) — both are forward bias rotations and both
                // fire when the source's DENUM ≥ 403, which is true for SwissEph
                // .se1 and JPL DE files but not for the Moshier theory. The
                // Moshier branch above (HeliocentricJ2000Equator / Ecliptic)
                // therefore skips bias entirely, matching the C short-circuit.
                CatalogFrameTransforms.IcrsBias(xpos, includeSpeed: true, backward: false, _models.FrameBias);
                CatalogFrameTransforms.IcrsBias(xpos, includeSpeed: true, backward: false, _models.FrameBias);
                break;
            default:
                throw new InvalidOperationException(
                    $"NodesAndApsidesService: unexpected raw frame {raw.Frame} for {body} from {source.Kind}.");
        }
    }

    /// <summary>
    /// Bary-Sun fetch in J2000 equator with speed. Used as the helio→bary
    /// reduction reference for SwissEph / JPL. For the Moshier source, the Sun
    /// is the heliocentric origin (zero state) — we short-circuit.
    /// </summary>
    private void FetchSunBaryJ2000Equator(JulianDay jd, EphemerisFlags srcFlags, Span<double> xpos)
    {
        if ((srcFlags & EphemerisFlags.MoshierEph) != 0
            && !_router.Has(EphemerisSource.SwissEph)
            && !_router.Has(EphemerisSource.Jpl))
        {
            xpos.Clear();
            return;
        }

        // Request the Sun barycentric — SwissEph / JPL sources expose this
        // when the Bary flag is set; otherwise the source returns helio.
        var (source, resolved) = _router.Resolve(srcFlags | EphemerisFlags.Speed, CelestialBody.Sun, jd);
        if (source.Kind == EphemerisSource.Moshier)
        {
            xpos.Clear();
            return;
        }
        var raw = source.Compute(CelestialBody.Sun, jd, resolved | EphemerisFlags.Barycentric);
        if (raw.Frame != BodyStateFrame.BarycentricJ2000Equator)
        {
            throw new InvalidOperationException(
                $"NodesAndApsidesService: expected BarycentricJ2000Equator from Sun on {source.Kind}, got {raw.Frame}.");
        }
        Unpack(raw, xpos);
    }

    /// <summary>
    /// Source-routed Moon fetch, normalised to geocentric mean ecliptic-of-date
    /// with speed. Mirrors the SEFLG_NONUT subset of
    /// <c>swi_plan_for_osc_elem</c> for the Moon (sweph.c#L5758-L5856). Moshier
    /// emits this frame natively; SwissEph and JPL emit geocentric J2000
    /// equator and need the ICRS bias + precession + obliquity rotation chain.
    /// </summary>
    private void FetchMoonGeoEclOfDate(JulianDay jd, EphemerisFlags flags, Span<double> xpos)
    {
        var srcFlags = (flags & SourceMask) | EphemerisFlags.Speed;
        var (source, resolved) = _router.Resolve(srcFlags, CelestialBody.Moon, jd);
        var raw = source.Compute(CelestialBody.Moon, jd, resolved);

        switch (raw.Frame)
        {
            case BodyStateFrame.GeocentricEclipticOfDate:
                Unpack(raw, xpos);
                break;
            case BodyStateFrame.GeocentricJ2000Equator:
                Unpack(raw, xpos);
                CatalogFrameTransforms.IcrsBias(xpos, includeSpeed: true, backward: false, _models.FrameBias);
                {
                    Span<double> p = stackalloc double[3] { xpos[0], xpos[1], xpos[2] };
                    Span<double> v = stackalloc double[3] { xpos[3], xpos[4], xpos[5] };
                    Precession.Apply(p, AstronomicalConstants.J2000, jd.Value, _models);
                    Precession.Apply(v, AstronomicalConstants.J2000, jd.Value, _models);
                    xpos[0] = p[0]; xpos[1] = p[1]; xpos[2] = p[2];
                    xpos[3] = v[0]; xpos[4] = v[1]; xpos[5] = v[2];
                }
                {
                    var meanEps = Precession.MeanObliquity(jd.Value, _models);
                    Coortrf2(xpos, meanEps * RadToDeg);
                }
                break;
            default:
                throw new InvalidOperationException(
                    $"NodesAndApsidesService: unexpected raw Moon frame {raw.Frame} from {source.Kind}.");
        }
    }

    private static void Unpack(in BodyState state, Span<double> xpos)
    {
        xpos[0] = state.Position.X;
        xpos[1] = state.Position.Y;
        xpos[2] = state.Position.Z;
        xpos[3] = state.Velocity.X;
        xpos[4] = state.Velocity.Y;
        xpos[5] = state.Velocity.Z;
    }

    private static void ToCartesian(ReadOnlySpan<double> polarDeg, Span<double> cartesian)
    {
        Span<double> polarRad = stackalloc double[6];
        polarRad[0] = polarDeg[0] * DegToRad;
        polarRad[1] = polarDeg[1] * DegToRad;
        polarRad[2] = polarDeg[2];
        polarRad[3] = polarDeg[3] * DegToRad;
        polarRad[4] = polarDeg[4] * DegToRad;
        polarRad[5] = polarDeg[5];
        Polar.PolarToCartesianWithSpeed(polarRad, cartesian);
    }

    private static void Coortrf2(Span<double> xyzWithSpeed, double epsDeg)
    {
        var sin = System.Math.Sin(epsDeg * DegToRad);
        var cos = System.Math.Cos(epsDeg * DegToRad);
        var y = xyzWithSpeed[1]; var z = xyzWithSpeed[2];
        xyzWithSpeed[1] = y * cos + z * sin;
        xyzWithSpeed[2] = -y * sin + z * cos;
        var vy = xyzWithSpeed[4]; var vz = xyzWithSpeed[5];
        xyzWithSpeed[4] = vy * cos + vz * sin;
        xyzWithSpeed[5] = -vy * sin + vz * cos;
    }

    private static OrbitalPoint ToOrbitalPoint(ReadOnlySpan<double> cartesian)
    {
        if (cartesian[0] == 0.0 && cartesian[1] == 0.0 && cartesian[2] == 0.0
            && cartesian[3] == 0.0 && cartesian[4] == 0.0 && cartesian[5] == 0.0)
        {
            return default;
        }
        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(cartesian, polar);
        return new OrbitalPoint(
            polar[0] * RadToDeg,
            polar[1] * RadToDeg,
            polar[2],
            polar[3] * RadToDeg,
            polar[4] * RadToDeg,
            polar[5]);
    }

    private static double Mod2Pi(double x)
    {
        var twoPi = 2.0 * System.Math.PI;
        var r = x - twoPi * System.Math.Floor(x / twoPi);
        if (r < 0) r += twoPi;
        return r;
    }
}
