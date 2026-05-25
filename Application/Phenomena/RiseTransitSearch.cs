// Ported from swisseph-master/swecl.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Slow-path helpers for RiseTransitService:
//   - rise_set (swecl.c#L4387 swe_rise_trans_true_hor)
//   - calc_mer_trans (swecl.c#L4688)
//   - find_maximum / find_zero (swecl.c#L4133, #L4148)
//   - rdi_twilight / get_sun_rad_plus_refr (swecl.c#L4164, #L4176)
//
// The helpers are static and re-entrant: every workspace lives on the
// stack of the calling service method (stackalloc-backed Span<double>
// arrays), matching the M-14 alloc-free contract.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

internal static class RiseTransitSearch
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;
    private const double TwoHours = 1.0 / 12.0;

    // Workspace dimensions. Mirrors the C buffers at swecl.c#L4396-L4399.
    // Maximum number of culminations actually inserted in a single sweep
    // is 4 (mirrors `tculm[4]` at swecl.c#L4399). We pre-allocate space
    // for jmaxStart (15) + 4 culminations.
    private const int InitialSamples = 15;        // jmax = 14 + 1
    private const int MaxCulminations = 4;
    // C uses xh[20][6] / tc[20] / h[20] (swecl.c#L4398-L4399).
    // The +1 accommodates the tail write during the insert-shift loop.
    private const int MaxSamples = 20;

    /// <summary>
    /// Single sample in the time / height table.
    /// </summary>
    private readonly record struct Sample(double Time, double ApparentAlt, double TrueAlt);

    /// <summary>
    /// Body fetcher contract used by the search. Gets called with a UT
    /// Julian Day; returns the equatorial RA/Dec/distance triple in degrees
    /// and AU.
    /// </summary>
    /// <remarks>
    /// The fetcher signature is delegate-free to keep the search alloc-free
    /// — call sites pass a <c>ref struct</c> wrapper instead.
    /// </remarks>
    public interface IBodyFetcher
    {
        bool Fetch(double tUt, out double raDeg, out double decDeg, out double distAu);
    }

    public readonly struct PlanetFetcher : IBodyFetcher
    {
        private readonly BodyService _body;
        private readonly CelestialBody _bodyId;
        private readonly EphemerisFlags _flags;
        private readonly bool _hasObserver;
        private readonly ObserverLocation _observer;
        private readonly bool _zeroEclLat;

        public PlanetFetcher(
            BodyService body, CelestialBody bodyId, EphemerisFlags flags,
            bool hasObserver, ObserverLocation observer, bool zeroEclLat)
        {
            _body = body;
            _bodyId = bodyId;
            _flags = flags;
            _hasObserver = hasObserver;
            _observer = observer;
            _zeroEclLat = zeroEclLat;
        }

        public bool Fetch(double tUt, out double raDeg, out double decDeg, out double distAu)
        {
            ObserverLocation? obs = _hasObserver ? _observer : null;
            var bs = _body.ComputeUt(_bodyId, new Domain.Time.JulianDay(tUt), _flags, obs);
            Span<double> cart = stackalloc double[6];
            cart[0] = bs.Position.X; cart[1] = bs.Position.Y; cart[2] = bs.Position.Z;
            cart[3] = bs.Velocity.X; cart[4] = bs.Velocity.Y; cart[5] = bs.Velocity.Z;
            Span<double> polar = stackalloc double[6];
            Polar.CartesianToPolarWithSpeed(cart, polar);
            raDeg = polar[0] * RadToDeg;
            decDeg = polar[1] * RadToDeg;
            distAu = polar[2];
            if (_zeroEclLat) decDeg = 0.0;
            return true;
        }
    }

    /// <summary>
    /// Star fetcher: position cached at construction, returned for every
    /// fetch (proper motion across the 28-hour window is below the noise
    /// floor of horizon astronomy and so the C path also pre-fetches
    /// once at swecl.c#L4456).
    /// </summary>
    public readonly struct StarFetcher : IBodyFetcher
    {
        private readonly double _raDeg;
        private readonly double _decDeg;
        private readonly double _distAu;

        public StarFetcher(double raDeg, double decDeg, double distAu)
        {
            _raDeg = raDeg;
            _decDeg = decDeg;
            _distAu = distAu;
        }

        public bool Fetch(double tUt, out double raDeg, out double decDeg, out double distAu)
        {
            raDeg = _raDeg;
            decDeg = _decDeg;
            distAu = _distAu;
            return true;
        }
    }

    /// <summary>
    /// Slow-path rise / set / twilight search. Mirrors
    /// <c>swe_rise_trans_true_hor</c> minus the meridian-transit dispatch
    /// (callers split that off into <see cref="MeridianTransit"/>).
    /// </summary>
    public static (double TRet, bool Found) RiseSetSlow<TFetcher>(
        ref TFetcher fetcher,
        double tjdUt,
        bool isFixedStar,
        bool isSun,
        double bodyDiameterMeters,
        HorizontalCoordsService horizontal,
        GeographicLocation observer,
        RiseTransitFlags rsmi,
        double atPressMbar,
        double atTempC,
        double horHgtDeg,
        HorizontalConversionInput inputFrame)
        where TFetcher : struct, IBodyFetcher
    {
        // Twilight: anchor a fictitious horizon at the appropriate Sun
        // depression and force NoRefraction|DiscCenter, mirrors swecl.c#L4441.
        if (isSun &&
            (rsmi & (RiseTransitFlags.CivilTwilight | RiseTransitFlags.NauticalTwilight | RiseTransitFlags.AstronomicalTwilight)) != 0)
        {
            rsmi |= RiseTransitFlags.NoRefraction | RiseTransitFlags.DiscCenter;
            horHgtDeg = -RdiTwilightDeg(rsmi);
        }

        if ((rsmi & (RiseTransitFlags.Rise | RiseTransitFlags.Set)) == 0)
            rsmi |= RiseTransitFlags.Rise;

        var pressure = atPressMbar == 0.0
            ? 1013.25 * Math.Pow(1 - 0.0065 * observer.AltitudeMeters / 288.0, 5.255)
            : atPressMbar;

        // Disc diameter (metres). Stars are point sources (dd = 0); the
        // body table stops at Vesta and pla_diam[i] is 0 for nodes /
        // outer fictitious entries — mirrors swecl.c#L4470-L4480.
        var dd = isFixedStar || (rsmi & RiseTransitFlags.DiscCenter) != 0
            ? 0.0
            : bodyDiameterMeters;

        Span<Sample> samples = stackalloc Sample[MaxSamples];
        Span<double> cul = stackalloc double[MaxCulminations];
        var nculm = -1;

        // Initial 15-sample sweep: t = tjdUt - 2h .. tjdUt + 26h step 2h.
        var jmax = InitialSamples - 1;
        for (var ii = 0; ii < InitialSamples; ii++)
        {
            var t = tjdUt - TwoHours + ii * TwoHours;
            if (!fetcher.Fetch(t, out var raDeg, out var decDeg, out var distAu))
                return (0.0, false);
            samples[ii] = ProbeSample(
                fetcher: ref fetcher,
                horizontal,
                observer,
                t, raDeg, decDeg, distAu,
                rsmi, dd, pressure, atTempC, horHgtDeg, inputFrame);

            // Culmination detection inside the rolling triple at swecl.c#L4516-L4524.
            if (ii > 1)
            {
                var trueA = samples[ii - 2].TrueAlt;
                var trueB = samples[ii - 1].TrueAlt;
                var trueC = samples[ii].TrueAlt;
                int calcCulm = 0;
                if (trueB > trueA && trueB > trueC) calcCulm = 1;
                else if (trueB < trueA && trueB < trueC) calcCulm = 2;
                if (calcCulm != 0 && nculm < MaxCulminations - 1)
                {
                    var tcu = RefineCulmination(
                        ref fetcher, horizontal, observer,
                        t - 2 * TwoHours,
                        trueA, trueB, trueC,
                        rsmi, pressure, atTempC, horHgtDeg, inputFrame);
                    nculm++;
                    cul[nculm] = tcu;
                }
            }
        }

        // Insert each culmination into the time/altitude array, preserving
        // sorted order. Mirrors swecl.c#L4556-L4610.
        for (var i = 0; i <= nculm; i++)
        {
            var tcu = cul[i];
            for (var j = 1; j <= jmax; j++)
            {
                if (tcu < samples[j].Time)
                {
                    if (jmax + 1 >= MaxSamples) break;
                    for (var k = jmax; k >= j; k--)
                        samples[k + 1] = samples[k];

                    if (!fetcher.Fetch(tcu, out var raDeg, out var decDeg, out var distAu))
                        return (0.0, false);
                    samples[j] = ProbeSample(
                        ref fetcher, horizontal, observer,
                        tcu, raDeg, decDeg, distAu,
                        rsmi, dd, pressure, atTempC, horHgtDeg, inputFrame);
                    jmax++;
                    break;
                }
            }
        }

        // Zero-crossing scan with 20-step bisection at swecl.c#L4612-L4682.
        for (var ii = 1; ii <= jmax; ii++)
        {
            var hPrev = samples[ii - 1].ApparentAlt;
            var hCur = samples[ii].ApparentAlt;
            if (hPrev * hCur >= 0) continue;
            var rising = hPrev < hCur;
            if (rising && (rsmi & RiseTransitFlags.Rise) == 0) continue;
            if (!rising && (rsmi & RiseTransitFlags.Set) == 0) continue;

            var tLo = samples[ii - 1].Time;
            var tHi = samples[ii].Time;
            var hLo = hPrev;
            var t = (tLo + tHi) / 2;
            for (var k = 0; k < 20; k++)
            {
                t = (tLo + tHi) / 2;
                if (!fetcher.Fetch(t, out var raDeg, out var decDeg, out var distAu))
                    return (0.0, false);
                var s = ProbeSample(
                    ref fetcher, horizontal, observer,
                    t, raDeg, decDeg, distAu,
                    rsmi, dd, pressure, atTempC, horHgtDeg, inputFrame);
                if (s.ApparentAlt * hLo <= 0)
                {
                    tHi = t;
                }
                else
                {
                    tLo = t;
                    hLo = s.ApparentAlt;
                }
            }
            if (t > tjdUt) return (t, true);
        }
        return (0.0, false);
    }

    /// <summary>
    /// Meridian transit search. Mirrors <c>calc_mer_trans</c>
    /// (swecl.c#L4688). The C version uses mean GMST (not apparent) for
    /// <c>armc</c>; we replicate that exactly via
    /// <see cref="CalendarService.SiderealTime(JulianDay, double?)"/>'s
    /// no-nutation overload.
    /// </summary>
    public static (double TRet, bool Found) MeridianTransit<TFetcher>(
        ref TFetcher fetcher,
        CalendarService calendar,
        double tjdUt,
        double observerLonDeg,
        bool isLowerTransit)
        where TFetcher : struct, IBodyFetcher
    {
        var armc0Hours = calendar.SiderealTime(new Domain.Time.JulianDay(tjdUt))
                       + observerLonDeg / 15.0;
        if (armc0Hours >= 24) armc0Hours -= 24;
        if (armc0Hours < 0) armc0Hours += 24;
        var armc0 = armc0Hours * 15.0;

        if (!fetcher.Fetch(tjdUt, out var raDeg, out _, out _))
            return (0.0, false);

        var t = tjdUt;
        var arxc = isLowerTransit ? AngleMath.NormalizeDegrees(armc0 + 180.0) : armc0;
        for (var i = 0; i < 4; i++)
        {
            var mdd = AngleMath.NormalizeDegrees(raDeg - arxc);
            if (i > 0 && mdd > 180) mdd -= 360;
            t += mdd / 361.0;
            var armcHours = calendar.SiderealTime(new Domain.Time.JulianDay(t))
                          + observerLonDeg / 15.0;
            if (armcHours >= 24) armcHours -= 24;
            if (armcHours < 0) armcHours += 24;
            var armc = armcHours * 15.0;
            arxc = isLowerTransit ? AngleMath.NormalizeDegrees(armc + 180.0) : armc;
            if (!fetcher.Fetch(t, out raDeg, out _, out _))
                return (0.0, false);
        }
        return (t, true);
    }

    private static Sample ProbeSample<TFetcher>(
        ref TFetcher fetcher,
        HorizontalCoordsService horizontal,
        GeographicLocation observer,
        double t, double raDeg, double decDeg, double distAu,
        RiseTransitFlags rsmi,
        double dd,
        double pressureMbar,
        double atTempC,
        double horHgtDeg,
        HorizontalConversionInput inputFrame)
        where TFetcher : struct, IBodyFetcher
    {
        // Apparent disc radius (degrees). Mirrors swecl.c#L4490-L4491.
        var curDist = distAu;
        if ((rsmi & RiseTransitFlags.FixedDiscSize) != 0)
        {
            // ipl == SE_SUN ? 1.0 : ipl == SE_MOON ? 0.00257 : curDist;
            // We disambiguate via dd (table lookup): the only two bodies
            // whose dd is non-zero in the FixedDiscSize-affected callers
            // are Sun (1.392e9 m) and Moon (3.475e6 m).
            if (dd > 1e9) curDist = 1.0;
            else if (dd > 0) curDist = 0.00257;
        }
        var rdi = dd > 0
            ? Math.Asin(dd / 2.0 / AstronomicalConstants.AstronomicalUnitMeters / curDist) * RadToDeg
            : 0.0;
        if ((rsmi & RiseTransitFlags.DiscBottom) != 0) rdi = -rdi;

        // Horizontal: trueAlt at the geometric centre, then the disc
        // adjustment. swecl.c#L4493-L4514.
        var hor = horizontal.ToHorizontal(
            new Domain.Time.JulianDay(t),
            inputFrame,
            observer,
            pressureMbar,
            atTempC,
            raDeg,
            decDeg);
        var trueAlt = hor.TrueAltitudeDeg + rdi;

        double apparentAlt;
        if ((rsmi & RiseTransitFlags.NoRefraction) != 0)
        {
            apparentAlt = trueAlt - horHgtDeg;
        }
        else
        {
            // Run the disc-corrected true altitude back through the
            // refraction model. The C path takes a HOR2EQU detour to get
            // the new RA/Dec, then EQU2HOR reapplies refraction. We mirror
            // by calling RefracExtended directly — the round-trip carries
            // no information beyond the refraction shift, since refraction
            // only depends on the true altitude.
            var apparent = RefractionMath.RefracExtended(
                trueAlt, observer.AltitudeMeters,
                pressureMbar, atTempC,
                RefractionMath.DefaultLapseRate,
                RefractionMath.Direction.TrueToApparent, out _);
            apparentAlt = apparent - horHgtDeg;
        }

        return new Sample(t, apparentAlt, trueAlt);
    }

    private static double RefineCulmination<TFetcher>(
        ref TFetcher fetcher,
        HorizontalCoordsService horizontal,
        GeographicLocation observer,
        double tStart,
        double y0, double y1, double y2,
        RiseTransitFlags rsmi,
        double pressureMbar,
        double atTempC,
        double horHgtDeg,
        HorizontalConversionInput inputFrame)
        where TFetcher : struct, IBodyFetcher
    {
        // Quadratic fit + recursive halving — mirrors the find_maximum
        // refinement at swecl.c#L4525-L4548. Note: dd is irrelevant here;
        // the C code probes ah[1] (the centre) and not the disc-shifted
        // value, so we pass dd = 0 to ProbeSample.
        var dt = TwoHours;
        FindParabolaExtremum(y0, y1, y2, dt, out var dtInt);
        var tcu = tStart + dtInt + dt;
        for (dt /= 3; dt > 0.0001; dt /= 3)
        {
            double da = 0, db = 0, dc = 0;
            for (var i = 0; i < 3; i++)
            {
                var tt = tcu - dt + i * dt;
                if (!fetcher.Fetch(tt, out var raDeg, out var decDeg, out var distAu))
                    return tcu;
                var s = ProbeSample(
                    ref fetcher, horizontal, observer,
                    tt, raDeg, decDeg, distAu,
                    rsmi, dd: 0.0,
                    pressureMbar, atTempC, horHgtDeg, inputFrame);
                var v = s.TrueAlt - horHgtDeg;
                if (i == 0) da = v;
                else if (i == 1) db = v;
                else dc = v;
            }
            FindParabolaExtremum(da, db, dc, dt, out dtInt);
            tcu += dtInt + dt;
        }
        return tcu;
    }

    /// <summary>
    /// Quadratic-interpolation extremum finder. Mirrors <c>find_maximum</c>
    /// at swecl.c#L4133. Returns the offset from the middle sample to the
    /// fitted parabola's apex.
    /// </summary>
    private static void FindParabolaExtremum(double y0, double y1, double y2, double dx, out double dxRet)
    {
        var c = y1;
        var b = (y2 - y0) / 2.0;
        var a = (y2 + y0) / 2.0 - c;
        var x = -b / 2.0 / a;
        dxRet = (x - 1) * dx;
    }

    /// <summary>
    /// Twilight depression in degrees: civil = 6, nautical = 12,
    /// astronomical = 18. Mirrors <c>rdi_twilight</c> at swecl.c#L4164.
    /// </summary>
    public static double RdiTwilightDeg(RiseTransitFlags rsmi)
    {
        if ((rsmi & RiseTransitFlags.CivilTwilight) != 0) return 6;
        if ((rsmi & RiseTransitFlags.NauticalTwilight) != 0) return 12;
        if ((rsmi & RiseTransitFlags.AstronomicalTwilight) != 0) return 18;
        return 0;
    }
}
