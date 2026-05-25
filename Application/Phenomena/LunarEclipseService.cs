// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   ComputeAt                          — swe_lun_eclipse_how              (swecl.c#L3190-L3236)
//   LunEclipseHow                      — lun_eclipse_how                  (swecl.c#L3237-L3363)
//   FindNextGlobal                     — swe_lun_eclipse_when             (swecl.c#L3378-L3605)
//   FindNextLocal                      — swe_lun_eclipse_when_loc         (swecl.c#L3633-L3728)
//   SelenocentricSunEarthEdgeAngle     — geocentric-max parabola metric   (swecl.c#L3491-L3510)
//   LunarPhaseMetric                   — phase contact metric             (swecl.c#L3573-L3577)
//   RefineLunarPhaseEdge               — Newton-style edge refinement     (swecl.c#L3587-L3601)
//   GeoAltMin / GeoAltMax              — SEI_ECL_GEOALT_MIN/MAX
//   DcoreLunar                         — dcore[0..4]
//
// Reference drivers:
//   /tmp/lunecliperef.c    — single-time geometry goldens (Phase 5a/b).
//   /tmp/lunwhenref.c      — global-search goldens (Phase 5e Followup).
//   /tmp/lunwhenlocref.c   — local-search goldens (Phase 5e Followup).
// Driver sources are reproduced as comments in the corresponding test
// files so the test suite documents how each golden was produced.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Common;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Lunar-eclipse service. Mirrors <c>swe_lun_eclipse_*</c>
/// (swecl.c#L3190 ff.). Phase-1 implements <see cref="ComputeAt"/>
/// (≡ <c>swe_lun_eclipse_how</c>); Phase-5e adds <see cref="FindNextGlobal"/>
/// and <see cref="FindNextLocal"/> (≡ <c>swe_lun_eclipse_when</c> and
/// <c>swe_lun_eclipse_when_loc</c>). Star occultations remain in the
/// occultation triad (Phase 5f). The caller-supplied <c>ifl</c> is masked
/// to the source bits inside the entry points, so <c>NoNutation</c> /
/// frame bits do not propagate.
/// </summary>
public sealed class LunarEclipseService
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;

    private const EphemerisFlags EphMask =
        EphemerisFlags.JplEph | EphemerisFlags.SwissEph | EphemerisFlags.MoshierEph;

    /// <summary>Minimum permitted observer altitude (m).</summary>
    private const double GeoAltMin = -500.0;

    /// <summary>Maximum permitted observer altitude (m).</summary>
    private const double GeoAltMax = 25_000.0;

    /// <summary>Defensive cap on the lunation-counter sweep (~80 years).</summary>
    private const int MaxLunationSearchSteps = 1000;

    private const double TwoHoursInDays = 2.0 / 24.0;
    private const double TenMinutesInDays = 10.0 / 24.0 / 60.0;

    private readonly BodyService _body;
    private readonly CalendarService _calendar;
    private readonly HorizontalCoordsService? _horizontal;
    private readonly RiseTransitService? _riseTransit;

    /// <summary>
    /// Constructs the service in geometry-only mode. This overload
    /// supports <see cref="ComputeAt"/>; the search routines
    /// (<see cref="FindNextGlobal"/>, <see cref="FindNextLocal"/>)
    /// require the four-argument overload that also takes horizontal
    /// and rise/transit collaborators.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// Either <paramref name="body"/> or <paramref name="calendar"/>
    /// is <see langword="null"/>.
    /// </exception>
    public LunarEclipseService(BodyService body, CalendarService calendar)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _horizontal = null;
        _riseTransit = null;
    }

    /// <summary>
    /// Constructs the service with full search support. The horizontal-coords
    /// service is used by <see cref="FindNextLocal"/> for Moon az/alt at each
    /// contact; the rise/set service is used to detect moonrise / moonset
    /// during the eclipse.
    /// </summary>
    public LunarEclipseService(
        BodyService body,
        CalendarService calendar,
        HorizontalCoordsService horizontal,
        RiseTransitService riseTransit)
    {
        _body = body ?? throw new ArgumentNullException(nameof(body));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _horizontal = horizontal ?? throw new ArgumentNullException(nameof(horizontal));
        _riseTransit = riseTransit ?? throw new ArgumentNullException(nameof(riseTransit));
    }

    /// <summary>
    /// Determines the lunar-eclipse type and umbral / penumbral magnitudes
    /// at the moment <paramref name="jdUt"/>. Mirrors <c>swe_lun_eclipse_how</c>.
    /// Returns <see cref="EclipseTypeFlags.None"/> when no lunar eclipse is
    /// taking place.
    /// </summary>
    public LunarEclipseReport ComputeAt(JulianDay jdUt, EphemerisFlags ephemerisFlags)
    {
        var ifl = ephemerisFlags & EphMask;
        var (type, attrs, geom, _) = LunEclipseHow(jdUt, ifl, observer: null);
        return new LunarEclipseReport(type, attrs, geom);
    }

    // -----------------------------------------------------------------
    // when — global lunar eclipse search (swecl.c#L3378)
    // -----------------------------------------------------------------

    /// <summary>
    /// Finds the next geocentric lunar eclipse after <paramref name="startUt"/>
    /// (or the previous one when <paramref name="backward"/> is <c>true</c>).
    /// Mirrors <c>swe_lun_eclipse_when</c>. The
    /// <paramref name="eclipseTypeFilter"/> may be set to one of
    /// <see cref="EclipseTypeFlags.Total"/>, <see cref="EclipseTypeFlags.Partial"/>,
    /// or <see cref="EclipseTypeFlags.Penumbral"/> (or any union of those)
    /// to skip eclipses outside the desired set;
    /// <see cref="EclipseTypeFlags.None"/> requests "any of total / partial /
    /// penumbral".
    /// </summary>
    /// <exception cref="System.ArgumentException">
    /// Thrown when the filter requests <see cref="EclipseTypeFlags.Annular"/>
    /// or <see cref="EclipseTypeFlags.AnnularTotal"/>; lunar eclipses cannot
    /// be annular.
    /// </exception>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> with the located eclipse, or
    /// <c>default(LunarEclipseGlobalSearchReport)</c> plus a non-null
    /// <see cref="EphemerisResult{T}.Warning"/> when the search exhausts
    /// its safety cap without finding a match.
    /// </returns>
    public EphemerisResult<LunarEclipseGlobalSearchReport> FindNextGlobal(
        JulianDay startUt,
        EphemerisFlags ephemerisFlags,
        EclipseTypeFlags eclipseTypeFilter = EclipseTypeFlags.None,
        bool backward = false)
    {
        var ifl = ephemerisFlags & EphMask;
        var ifltype = NormalizeLunarFilter(eclipseTypeFilter);
        var direction = backward ? -1 : 1;

        // K-Lunation count since J2000 (mirrors swecl.c#L3417).
        var k = (double)(int)((startUt.Value - AstronomicalConstants.J2000) / 365.2425 * 12.3685);
        k -= direction;

        for (var step = 0; step < MaxLunationSearchSteps; step++)
        {
            k += direction;
            var report = TryLunarEclipseAtLunation(k, ifl, ifltype, startUt, backward);
            if (report.HasValue) return EphemerisResult<LunarEclipseGlobalSearchReport>.Ok(report.Value);
        }

        return EphemerisResult<LunarEclipseGlobalSearchReport>.WithWarning(
            default,
            "Lunar-eclipse search exceeded the safety cap of " +
            $"{MaxLunationSearchSteps} lunation steps without finding a match.");
    }

    private LunarEclipseGlobalSearchReport? TryLunarEclipseAtLunation(
        double k, EphemerisFlags ifl, EclipseTypeFlags ifltype, JulianDay startUt, bool backward)
    {
        var kk = k + 0.5;
        var t = kk / 1236.85;
        var t2 = t * t;
        var t3 = t2 * t;
        var t4 = t3 * t;

        // Argument of latitude — Ff > 21° && < 159° excludes all lunar eclipses
        // (mirrors swecl.c#L3426-L3434).
        var ff = AngleMath.NormalizeDegrees(160.7108 + 390.67050274 * kk
                                            - 0.0016341 * t2
                                            - 0.00000227 * t3
                                            + 0.000000011 * t4);
        if (ff > 180.0) ff -= 180.0;
        if (ff > 21.0 && ff < 159.0) return null;

        // Approximate TT of geocentric maximum (Meeus, German p. 381).
        var tjd = 2451550.09765 + 29.530588853 * kk
                                + 0.0001337 * t2
                                - 0.000000150 * t3
                                + 0.00000000073 * t4;
        var m = AngleMath.NormalizeDegrees(2.5534 + 29.10535669 * kk
                                                  - 0.0000218 * t2
                                                  - 0.00000011 * t3);
        var mm = AngleMath.NormalizeDegrees(201.5643 + 385.81693528 * kk
                                                     + 0.1017438 * t2
                                                     + 0.00001239 * t3
                                                     + 0.000000058 * t4);
        var om = AngleMath.NormalizeDegrees(124.7746 - 1.56375580 * kk
                                                    + 0.0020691 * t2
                                                    + 0.00000215 * t3);
        var e = 1.0 - 0.002516 * t - 0.0000074 * t2;
        var a1 = AngleMath.NormalizeDegrees(299.77 + 0.107408 * kk - 0.009173 * t2);
        var mr = m * DegToRad;
        var mmr = mm * DegToRad;
        var fr = ff * DegToRad;
        var omr = om * DegToRad;
        var f1 = fr - 0.02665 * System.Math.Sin(omr) * DegToRad;
        var a1r = a1 * DegToRad;
        tjd = tjd - 0.4075 * System.Math.Sin(mmr)
                  + 0.1721 * e * System.Math.Sin(mr)
                  + 0.0161 * System.Math.Sin(2 * mmr)
                  - 0.0097 * System.Math.Sin(2 * f1)
                  + 0.0073 * e * System.Math.Sin(mmr - mr)
                  - 0.0050 * e * System.Math.Sin(mmr + mr)
                  - 0.0023 * System.Math.Sin(mmr - 2 * f1)
                  + 0.0021 * e * System.Math.Sin(2 * mr)
                  + 0.0012 * System.Math.Sin(mmr + 2 * f1)
                  + 0.0006 * e * System.Math.Sin(2 * mmr + mr)
                  - 0.0004 * System.Math.Sin(3 * mmr)
                  - 0.0003 * e * System.Math.Sin(mr + 2 * f1)
                  + 0.0003 * System.Math.Sin(a1r)
                  - 0.0002 * e * System.Math.Sin(mr - 2 * f1)
                  - 0.0002 * e * System.Math.Sin(2 * mmr - mr)
                  - 0.0002 * System.Math.Sin(omr);

        // Refine TT max via parabola fit on selenocentric Sun-Earth edge angle.
        var dtStart = (tjd < 2_100_000.0 || tjd > 2_500_000.0) ? 5.0 : 0.1;
        const double dtDiv = 4.0;
        var iflag = EphemerisFlags.Equatorial | ifl;
        Span<double> dc = stackalloc double[3];
        for (var dt = dtStart; dt > 0.001; dt /= dtDiv)
        {
            var tt = tjd - dt;
            for (var i = 0; i < 3; i++)
            {
                dc[i] = SelenocentricSunEarthEdgeAngle(new JulianDay(tt), iflag);
                tt += dt;
            }
            ParabolaFit.FindMaximum(dc[0], dc[1], dc[2], dt, out var dtInt, out _);
            tjd += dtInt + dt;
        }

        // TT → UT via 3-fold ΔT iteration (swecl.c#L3514-L3516).
        var maxUt = tjd - _calendar.DeltaT(new JulianDay(tjd));
        maxUt = tjd - _calendar.DeltaT(new JulianDay(maxUt));
        maxUt = tjd - _calendar.DeltaT(new JulianDay(maxUt));

        // Classify via lun_eclipse_how (swecl.c#L3517).
        var (type, _, _, _) = LunEclipseHow(new JulianDay(maxUt), ifl, observer: null);
        if (type == EclipseTypeFlags.None) return null;

        // Strict-direction check (swecl.c#L3524-L3527).
        if ((backward && maxUt >= startUt.Value - 0.0001)
            || (!backward && maxUt <= startUt.Value + 0.0001))
            return null;

        // Filter checks (swecl.c#L3533-L3545).
        if ((ifltype & EclipseTypeFlags.Penumbral) == 0 && (type & EclipseTypeFlags.Penumbral) != 0)
            return null;
        if ((ifltype & EclipseTypeFlags.Partial) == 0 && (type & EclipseTypeFlags.Partial) != 0)
            return null;
        if ((ifltype & EclipseTypeFlags.Total) == 0 && (type & EclipseTypeFlags.Total) != 0)
            return null;

        // Phase contacts (swecl.c#L3552-L3603).
        // o = 0 for penumbral (just penumbra contacts)
        // o = 1 for partial (penumbra + partial)
        // o = 2 for total (penumbra + partial + totality)
        var phaseLast = (type & EclipseTypeFlags.Penumbral) != 0 ? 0
                      : (type & EclipseTypeFlags.Partial) != 0 ? 1
                      : 2;
        var dta = TwoHoursInDays;

        double penumbraBegin = 0, penumbraEnd = 0;
        double? partialBegin = null, partialEnd = null;
        double? totalityBegin = null, totalityEnd = null;

        Span<double> dcSpan = stackalloc double[3];
        for (var n = 0; n <= phaseLast; n++)
        {
            // Sample phase metric at maxUt − dta, maxUt, maxUt + dta.
            var ts = maxUt - dta;
            for (var i = 0; i < 3; i++)
            {
                dcSpan[i] = LunarPhaseMetric(n, new JulianDay(ts), ifl);
                ts += dta;
            }
            if (!ParabolaFit.FindZero(dcSpan[0], dcSpan[1], dcSpan[2], dta, out var dt1, out var dt2))
            {
                dt1 = -dta;
                dt2 = dta;
            }
            var beginTime = maxUt + dt1 + dta;
            var endTime = maxUt + dt2 + dta;
            var dtb = (dt1 + dta) / 2;

            // Refine begin/end via three Newton-style steps.
            for (var mIter = 0; mIter < 3; mIter++)
            {
                var dtRef = dtb / System.Math.Pow(2.0, mIter + 1);
                beginTime = RefineLunarPhaseEdge(n, beginTime, dtRef, ifl);
                endTime = RefineLunarPhaseEdge(n, endTime, dtRef, ifl);
            }

            switch (n)
            {
                case 0: penumbraBegin = beginTime; penumbraEnd = endTime; break;
                case 1: partialBegin = beginTime; partialEnd = endTime; break;
                case 2: totalityBegin = beginTime; totalityEnd = endTime; break;
            }
        }

        return new LunarEclipseGlobalSearchReport(
            EclipseType: type,
            MaximumTime: new JulianDay(maxUt),
            PartialBeginTime: partialBegin is null ? null : new JulianDay(partialBegin.Value),
            PartialEndTime: partialEnd is null ? null : new JulianDay(partialEnd.Value),
            TotalityBeginTime: totalityBegin is null ? null : new JulianDay(totalityBegin.Value),
            TotalityEndTime: totalityEnd is null ? null : new JulianDay(totalityEnd.Value),
            PenumbraBeginTime: new JulianDay(penumbraBegin),
            PenumbraEndTime: new JulianDay(penumbraEnd));
    }

    /// <summary>
    /// Selenocentric Sun-Earth edge angular distance (degrees) at TT time —
    /// the parabola-refinement metric for the geocentric maximum.
    /// </summary>
    private double SelenocentricSunEarthEdgeAngle(JulianDay jdEt, EphemerisFlags iflag)
    {
        var sun = _body.Compute(CelestialBody.Sun, jdEt, iflag);
        var moon = _body.Compute(CelestialBody.Moon, jdEt, iflag);

        // Selenocentric Sun: rs - rm.
        var sx = sun.Position.X - moon.Position.X;
        var sy = sun.Position.Y - moon.Position.Y;
        var sz = sun.Position.Z - moon.Position.Z;
        var ds = System.Math.Sqrt(sx * sx + sy * sy + sz * sz);
        // Selenocentric Earth: -rm.
        var ex = -moon.Position.X;
        var ey = -moon.Position.Y;
        var ez = -moon.Position.Z;
        var dm = System.Math.Sqrt(ex * ex + ey * ey + ez * ez);

        var dot = (sx * ex + sy * ey + sz * ez) / (ds * dm);
        if (dot > 1.0) dot = 1.0;
        else if (dot < -1.0) dot = -1.0;
        var dctr = System.Math.Acos(dot) * RadToDeg;
        var rearth = System.Math.Asin(EclipseConstants.EarthRadiusAu / dm) * RadToDeg;
        var rsun = System.Math.Asin(EclipseConstants.SunRadiusAu / ds) * RadToDeg;
        return dctr - (rearth + rsun);
    }

    /// <summary>
    /// Phase metric for the lunar contact-finding loop.
    /// n=0 penumbra: D0/2 + RMOON/cosf2 - r0
    /// n=1 partial: d0/2 + RMOON/cosf1 - r0
    /// n=2 totality: d0/2 - RMOON/cosf1 - r0
    /// All quantities in AU.
    /// </summary>
    private double LunarPhaseMetric(int n, JulianDay jdUt, EphemerisFlags ifl)
    {
        var (_, _, geom, _) = LunEclipseHow(jdUt, ifl, observer: null);
        const double rmoon = EclipseConstants.MoonRadiusAu;
        return n switch
        {
            0 => geom.PenumbraDiameterAu / 2.0
                 + rmoon / geom.PenumbraConeCosine
                 - geom.ShadowAxisDistanceFromSelenocenterAu,
            1 => geom.UmbraDiameterAu / 2.0
                 + rmoon / geom.UmbraConeCosine
                 - geom.ShadowAxisDistanceFromSelenocenterAu,
            _ => geom.UmbraDiameterAu / 2.0
                 - rmoon / geom.UmbraConeCosine
                 - geom.ShadowAxisDistanceFromSelenocenterAu,
        };
    }

    /// <summary>
    /// One Newton-style refinement step for a lunar phase contact.
    /// </summary>
    private double RefineLunarPhaseEdge(int n, double ut, double dt, EphemerisFlags ifl)
    {
        Span<double> dc = stackalloc double[2];
        for (var i = 0; i < 2; i++)
            dc[i] = LunarPhaseMetric(n, new JulianDay(ut - dt + i * dt), ifl);
        var slope = (dc[1] - dc[0]) / dt;
        if (slope == 0) return ut;
        return ut - dc[1] / slope;
    }

    private static EclipseTypeFlags NormalizeLunarFilter(EclipseTypeFlags filter)
    {
        // C source (swecl.c#L3403-L3411) explicitly errors when only
        // SE_ECL_ANNULAR / SE_ECL_ANNULAR_TOTAL is requested for lunar.
        if (filter != EclipseTypeFlags.None
            && (filter & ~(EclipseTypeFlags.Annular | EclipseTypeFlags.AnnularTotal)) == 0)
        {
            throw new ArgumentException(
                "Annular lunar eclipses do not exist.", nameof(filter));
        }
        // Strip annular bits — they're meaningless for lunar.
        filter &= ~(EclipseTypeFlags.Annular | EclipseTypeFlags.AnnularTotal
                  | EclipseTypeFlags.Central | EclipseTypeFlags.NonCentral);
        if (filter == EclipseTypeFlags.None)
            filter = EclipseTypeFlags.Total | EclipseTypeFlags.Penumbral | EclipseTypeFlags.Partial;
        return filter;
    }

    // -----------------------------------------------------------------
    // when_loc — local lunar eclipse search (swecl.c#L3633)
    // -----------------------------------------------------------------

    /// <summary>
    /// Finds the next lunar eclipse visible from <paramref name="observer"/>
    /// after <paramref name="startUt"/> (or the previous one when
    /// <paramref name="backward"/> is <c>true</c>). Mirrors
    /// <c>swe_lun_eclipse_when_loc</c>: starts from the geocentric search
    /// result, then trims phases below the horizon and returns the moments
    /// of moonrise / moonset when those occur during the eclipse.
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// Thrown when the observer altitude is outside the valid range
    /// [-500 m, 25 000 m] enforced by the C source.
    /// </exception>
    /// <returns>
    /// An <see cref="EphemerisResult{T}"/> with the located eclipse, or
    /// <c>default(LunarEclipseLocalSearchReport)</c> plus a non-null
    /// <see cref="EphemerisResult{T}.Warning"/> when no visible eclipse is
    /// found within the safety cap.
    /// </returns>
    public EphemerisResult<LunarEclipseLocalSearchReport> FindNextLocal(
        JulianDay startUt,
        EphemerisFlags ephemerisFlags,
        GeographicLocation observer,
        bool backward = false)
    {
        if (_horizontal is null || _riseTransit is null)
        {
            throw new InvalidOperationException(
                "FindNextLocal requires the 4-arg constructor with " +
                "HorizontalCoordsService and RiseTransitService.");
        }
        if (observer.AltitudeMeters < GeoAltMin || observer.AltitudeMeters > GeoAltMax)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observer),
                $"Observer altitude must be between {GeoAltMin:F0} and {GeoAltMax:F0} metres.");
        }

        var ifl = ephemerisFlags & EphMask;

        // Mirrors the C `next_lun_ecl` loop (swecl.c#L3645-L3725) — re-enter
        // the global search when no contact is visible at the observer or
        // when the entire eclipse stays below the horizon.
        var searchStart = startUt;
        for (var attempt = 0; attempt < MaxLunationSearchSteps; attempt++)
        {
            var globalResult = FindNextGlobal(searchStart, ephemerisFlags, EclipseTypeFlags.None, backward);
            if (globalResult.HasWarning)
            {
                return EphemerisResult<LunarEclipseLocalSearchReport>.WithWarning(
                    default, globalResult.Warning!);
            }
            var globalReport = globalResult.Value;

            // tret[0..7] from the global pass — preserve as nullable doubles.
            var maxUt = globalReport.MaximumTime.Value;
            double? p0 = globalReport.PartialBeginTime is { } pb ? pb.Value : null;
            double? p3 = globalReport.PartialEndTime is { } pe ? pe.Value : null;
            double? t4 = globalReport.TotalityBeginTime is { } tb ? tb.Value : null;
            double? t5 = globalReport.TotalityEndTime is { } te ? te.Value : null;
            double? pen6 = globalReport.PenumbraBeginTime.Value;
            double? pen7 = globalReport.PenumbraEndTime.Value;

            // Visibility loop — iterate i = 7..2,0 (skip i=1 unused) checking
            // app_alt > 0 at each contact (swecl.c#L3653-L3671).
            var visibilityFlags = EclipseTypeFlags.None;
            LunarEclipseAttributes maxAttrs = default;
            EclipseTypeFlags typeAtMax = EclipseTypeFlags.None;
            var anyVisible = false;
            for (var i = 7; i >= 0; i--)
            {
                if (i == 1) continue;
                double? tContact = i switch
                {
                    0 => maxUt,
                    2 => p0,
                    3 => p3,
                    4 => t4,
                    5 => t5,
                    6 => pen6,
                    7 => pen7,
                    _ => null,
                };
                if (tContact is null) continue;
                var (typeFlags, attrs, _, _) =
                    LunEclipseHow(new JulianDay(tContact.Value), ifl, observer);
                if (attrs.MoonApparentAltitudeDeg > 0)
                {
                    anyVisible = true;
                    visibilityFlags |= EclipseTypeFlags.Visible;
                    visibilityFlags |= i switch
                    {
                        0 => EclipseTypeFlags.MaxVisible,
                        2 => EclipseTypeFlags.PartialBeginVisible,
                        3 => EclipseTypeFlags.PartialEndVisible,
                        4 => EclipseTypeFlags.TotalBeginVisible,
                        5 => EclipseTypeFlags.TotalEndVisible,
                        6 => EclipseTypeFlags.PenumbralBeginVisible,
                        7 => EclipseTypeFlags.PenumbralEndVisible,
                        _ => EclipseTypeFlags.None,
                    };
                }
                if (i == 0)
                {
                    maxAttrs = attrs;
                    typeAtMax = typeFlags;
                }
            }

            if (!anyVisible)
            {
                searchStart = new JulianDay(backward ? maxUt - 25.0 : maxUt + 25.0);
                continue;
            }

            // Moonrise / moonset on the eclipse day (swecl.c#L3681-L3715).
            var (moonriseJd, mrFound) = TryFindMoonRiseSet(pen6!.Value - 0.001, observer, ifl, RiseTransitFlags.Rise);
            var (moonsetJd, msFound) = TryFindMoonRiseSet(pen6!.Value - 0.001, observer, ifl, RiseTransitFlags.Set);
            var moonriseUt = moonriseJd.Value;
            var moonsetUt = moonsetJd.Value;

            if (mrFound && msFound)
            {
                // Eclipse window entirely below the horizon
                // (swecl.c#L3686-L3692).
                if (moonsetUt < pen6.Value
                    || (moonsetUt > moonriseUt && moonriseUt > pen7!.Value))
                {
                    searchStart = new JulianDay(backward ? maxUt - 25.0 : maxUt + 25.0);
                    continue;
                }
            }

            // Trim phases that fall outside [moonrise, moonset]; if a horizon
            // crossing happens during the eclipse, override tret[0] with it
            // when the geocentric maximum itself is below the horizon.
            double tjdMax = maxUt;
            double? finalMoonrise = null, finalMoonset = null;
            if (mrFound && moonriseUt > pen6.Value && moonriseUt < pen7!.Value)
            {
                pen6 = null; // tret[6] is zeroed
                if (p0.HasValue && moonriseUt > p0.Value) p0 = null;
                if (p3.HasValue && moonriseUt > p3.Value) p3 = null;
                if (t4.HasValue && moonriseUt > t4.Value) t4 = null;
                if (t5.HasValue && moonriseUt > t5.Value) t5 = null;
                finalMoonrise = moonriseUt;
                if (moonriseUt > maxUt) tjdMax = moonriseUt;
            }
            if (msFound && moonsetUt > pen6.GetValueOrDefault(double.MinValue) && moonsetUt < pen7!.Value)
            {
                pen7 = null;
                if (p0.HasValue && moonsetUt < p0.Value) p0 = null;
                if (p3.HasValue && moonsetUt < p3.Value) p3 = null;
                if (t4.HasValue && moonsetUt < t4.Value) t4 = null;
                if (t5.HasValue && moonsetUt < t5.Value) t5 = null;
                finalMoonset = moonsetUt;
                if (moonsetUt < maxUt) tjdMax = moonsetUt;
            }

            // Recompute attr at adjusted tjdMax — swecl.c#L3716-L3725.
            var (typeAtAdjMax, attrsAtAdjMax, _, _) =
                LunEclipseHow(new JulianDay(tjdMax), ifl, observer);
            if (typeAtAdjMax == EclipseTypeFlags.None)
            {
                searchStart = new JulianDay(backward ? maxUt - 25.0 : maxUt + 25.0);
                continue;
            }
            maxAttrs = attrsAtAdjMax;
            typeAtMax = typeAtAdjMax;

            // Compose final flags: visibility bits + eclipse-type bits at adj max.
            var retFlags = visibilityFlags
                         | (typeAtMax & EclipseTypeFlags.AllTypesLunar);

            return EphemerisResult<LunarEclipseLocalSearchReport>.Ok(new LunarEclipseLocalSearchReport(
                EclipseType: retFlags,
                MaximumTime: new JulianDay(tjdMax),
                PartialBeginTime: p0 is null ? null : new JulianDay(p0.Value),
                PartialEndTime: p3 is null ? null : new JulianDay(p3.Value),
                TotalityBeginTime: t4 is null ? null : new JulianDay(t4.Value),
                TotalityEndTime: t5 is null ? null : new JulianDay(t5.Value),
                PenumbraBeginTime: pen6 is null ? null : new JulianDay(pen6.Value),
                PenumbraEndTime: pen7 is null ? null : new JulianDay(pen7.Value),
                MoonriseDuringEclipseTime: finalMoonrise is null ? null : new JulianDay(finalMoonrise.Value),
                MoonsetDuringEclipseTime: finalMoonset is null ? null : new JulianDay(finalMoonset.Value),
                Attributes: maxAttrs));
        }

        return EphemerisResult<LunarEclipseLocalSearchReport>.WithWarning(
            default,
            "Local lunar-eclipse search exceeded the safety cap of " +
            $"{MaxLunationSearchSteps} retries without finding a visible eclipse.");
    }

    private (JulianDay Jd, bool Found) TryFindMoonRiseSet(
        double startUt, GeographicLocation observer, EphemerisFlags ifl, RiseTransitFlags rise)
    {
        var res = _riseTransit!.Find(
            new JulianDay(startUt),
            CelestialBody.Moon,
            EphemerisFlags.Equatorial | EphemerisFlags.Topocentric | ifl,
            rise | RiseTransitFlags.DiscBottom,
            observer,
            atPressMbar: 0.0,
            atTempC: 0.0);
        return (res.Value, !res.HasWarning);
    }

    // -----------------------------------------------------------------
    // lun_eclipse_how — geometry + classification helper (swecl.c#L3237).
    // Returns tuple (type, attributes, geometry, dcore) so the search
    // routines can reuse dcore[0..4] without recomputing positions.
    // -----------------------------------------------------------------

    private (EclipseTypeFlags Type, LunarEclipseAttributes Attrs, LunarEclipseGeometry Geom, DcoreLunar Dcore)
        LunEclipseHow(JulianDay jdUt, EphemerisFlags ifl, GeographicLocation? observer)
    {
        var dt = _calendar.DeltaT(jdUt);
        var jdEt = new JulianDay(jdUt.Value + dt);
        var fetchFlags = EphemerisFlags.Equatorial | ifl;

        var moonState = _body.Compute(CelestialBody.Moon, jdEt, fetchFlags);
        var sunState = _body.Compute(CelestialBody.Sun, jdEt, fetchFlags);

        Span<double> rm = stackalloc double[3];
        rm[0] = moonState.Position.X; rm[1] = moonState.Position.Y; rm[2] = moonState.Position.Z;
        Span<double> rs = stackalloc double[3];
        rs[0] = sunState.Position.X; rs[1] = sunState.Position.Y; rs[2] = sunState.Position.Z;

        var dm = System.Math.Sqrt(rm[0] * rm[0] + rm[1] * rm[1] + rm[2] * rm[2]);
        var ds = System.Math.Sqrt(rs[0] * rs[0] + rs[1] * rs[1] + rs[2] * rs[2]);

        var cosDctr = (rs[0] * rm[0] + rs[1] * rm[1] + rs[2] * rm[2]) / (ds * dm);
        if (cosDctr > 1) cosDctr = 1; else if (cosDctr < -1) cosDctr = -1;
        var dctr = System.Math.Acos(cosDctr) * RadToDeg;

        // Selenocentric sun: rs - rm.
        Span<double> rsSeleno = stackalloc double[3];
        rsSeleno[0] = rs[0] - rm[0];
        rsSeleno[1] = rs[1] - rm[1];
        rsSeleno[2] = rs[2] - rm[2];
        Span<double> rmSeleno = stackalloc double[3];
        rmSeleno[0] = -rm[0];
        rmSeleno[1] = -rm[1];
        rmSeleno[2] = -rm[2];

        Span<double> e = stackalloc double[3];
        e[0] = rmSeleno[0] - rsSeleno[0];
        e[1] = rmSeleno[1] - rsSeleno[1];
        e[2] = rmSeleno[2] - rsSeleno[2];
        var dsm = System.Math.Sqrt(e[0] * e[0] + e[1] * e[1] + e[2] * e[2]);
        e[0] /= dsm; e[1] /= dsm; e[2] /= dsm;

        var f1 = (EclipseConstants.SunRadiusAu - EclipseConstants.EarthRadiusAu) / dsm;
        var cosf1 = System.Math.Sqrt(1 - f1 * f1);
        var f2 = (EclipseConstants.SunRadiusAu + EclipseConstants.EarthRadiusAu) / dsm;
        var cosf2 = System.Math.Sqrt(1 - f2 * f2);

        var s0 = -(rmSeleno[0] * e[0] + rmSeleno[1] * e[1] + rmSeleno[2] * e[2]);
        var r0 = System.Math.Sqrt(dm * dm - s0 * s0);

        var d0 = System.Math.Abs(s0 / dsm * (EclipseConstants.SunDiameterAu - EclipseConstants.EarthDiameterAu) - EclipseConstants.EarthDiameterAu)
                 * (1 + 1.0 / 50.0) / cosf1;
        var D0 = (s0 / dsm * (EclipseConstants.SunDiameterAu + EclipseConstants.EarthDiameterAu) + EclipseConstants.EarthDiameterAu)
                 * (1 + 1.0 / 50.0) / cosf2;
        d0 /= cosf1;
        D0 /= cosf2;
        d0 *= 0.99405;
        D0 *= 0.98813;

        const double rmoon = EclipseConstants.MoonRadiusAu;
        const double dmoon = 2 * rmoon;

        EclipseTypeFlags retc = EclipseTypeFlags.None;
        double umbralMag = 0.0;
        if (d0 / 2 >= r0 + rmoon / cosf1)
        {
            retc = EclipseTypeFlags.Total;
            umbralMag = (d0 / 2 - r0 + rmoon) / dmoon;
        }
        else if (d0 / 2 >= r0 - rmoon / cosf1)
        {
            retc = EclipseTypeFlags.Partial;
            umbralMag = (d0 / 2 - r0 + rmoon) / dmoon;
        }
        else if (D0 / 2 >= r0 - rmoon / cosf2)
        {
            retc = EclipseTypeFlags.Penumbral;
            umbralMag = 0.0;
        }

        var penumbralMag = (D0 / 2 - r0 + rmoon) / dmoon;
        double moonSunAngularDist = retc != EclipseTypeFlags.None ? 180.0 - System.Math.Abs(dctr) : 0.0;

        var (sarosSeries, sarosMember) = SarosTables.LookupLunar(jdUt.Value);

        // attr[4..6] — Moon az/alt at the observer (swecl.c#L3215-L3226).
        double moonAz = 0, moonTrueAlt = 0, moonAppAlt = 0;
        if (observer is { } obs && _horizontal is not null)
        {
            // Topocentric Moon, equatorial polar, nutation-on (matches the
            // C wrapper which clears SEFLG_TOPOCTR before lun_eclipse_how
            // and re-adds it for the az/alt fetch).
            var topoMoon = _body.Compute(
                CelestialBody.Moon,
                jdEt,
                EphemerisFlags.Equatorial | EphemerisFlags.Topocentric | ifl,
                new ObserverLocation(obs.LongitudeDeg, obs.LatitudeDeg, obs.AltitudeMeters));
            // Cartesian → polar (RA/Dec) in deg.
            Span<double> moonCart6 = stackalloc double[6];
            moonCart6[0] = topoMoon.Position.X; moonCart6[1] = topoMoon.Position.Y; moonCart6[2] = topoMoon.Position.Z;
            Span<double> moonPolar = stackalloc double[6];
            Polar.CartesianToPolarWithSpeed(moonCart6, moonPolar);
            // The C wrapper passes atpress=0, atemp=10 to swe_azalt
            // (default since geopos[3]=0, geopos[4]=0); HorizontalCoordsService
            // mirrors the same defaulting.
            var horiz = _horizontal.ToHorizontal(
                jdUt, HorizontalConversionInput.FromEquatorial,
                obs, atPressMbar: 0.0, atTempC: 0.0,
                moonPolar[0] * RadToDeg, moonPolar[1] * RadToDeg);
            moonAz = horiz.AzimuthDeg;
            moonTrueAlt = horiz.TrueAltitudeDeg;
            moonAppAlt = horiz.ApparentAltitudeDeg;
            // C zeros retc when Moon is below the horizon (swecl.c#L3225-L3226).
            if (moonAppAlt <= 0) retc = EclipseTypeFlags.None;
        }

        var attrs = new LunarEclipseAttributes(
            UmbralMagnitude: umbralMag,
            PenumbralMagnitude: penumbralMag,
            MoonBodyAngularDistanceDeg: moonSunAngularDist,
            UmbralMagnitudeAlias: umbralMag,
            SarosSeriesNumber: sarosSeries,
            SarosSeriesMemberNumber: sarosMember,
            MoonAzimuthDeg: moonAz,
            MoonTrueAltitudeDeg: moonTrueAlt,
            MoonApparentAltitudeDeg: moonAppAlt);

        var geom = new LunarEclipseGeometry(
            ShadowAxisDistanceFromSelenocenterAu: r0,
            UmbraDiameterAu: d0,
            PenumbraDiameterAu: D0,
            UmbraConeCosine: cosf1,
            PenumbraConeCosine: cosf2);

        return (retc, attrs, geom, new DcoreLunar(r0, d0, D0, cosf1, cosf2));
    }

    /// <summary>Lunar-pipeline analogue of the Swiss Ephemeris <c>dcore[0..4]</c> array.</summary>
    private readonly record struct DcoreLunar(
        double ShadowAxisFromSelenocenterAu,
        double UmbraDiameterAu,
        double PenumbraDiameterAu,
        double UmbraConeCosine,
        double PenumbraConeCosine);

}
