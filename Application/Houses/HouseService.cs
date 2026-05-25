// Ported from swisseph-master/swehouse.c (swe_houses, swe_houses_ex2,
// swe_houses_armc_ex2, swe_house_pos, swe_house_name).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Houses;

/// <summary>
/// House-cusp service. Mirrors the C entry points <c>swe_houses</c>,
/// <c>swe_houses_ex2</c>, <c>swe_houses_armc_ex2</c>, <c>swe_house_pos</c>
/// and <c>swe_house_name</c> (swehouse.c#L130-L859). Stateless and
/// thread-safe; observer position is passed per call. Stateless dispatcher
/// around one <see cref="IHouseSystemImpl"/> per system; observer + flags
/// are passed per call (no <c>swe_set_*</c> globals).
/// </summary>
public sealed class HouseService
{
    private readonly CalendarService _calendar;
    private readonly AstronomicalModelOverrides _models;
    private readonly SiderealService? _sidereal;
    private readonly BodyService? _bodyService;

    // Strategy dispatch table — one instance per system, all stateless.
    private readonly EqualHouseSystem _equal = new();
    private readonly EqualMcHouseSystem _equalMc = new();
    private readonly EqualAriesAnchoredHouseSystem _equalAries = new();
    private readonly VehlowHouseSystem _vehlow = new();
    private readonly WholeSignHouseSystem _wholeSign = new();
    private readonly PorphyryHouseSystem _porphyry = new();
    private readonly SripatiHouseSystem _sripati = new();
    private readonly PullenSinusoidalDeltaHouseSystem _pullenSd = new();
    private readonly PullenSinusoidalRatioHouseSystem _pullenSr = new();
    private readonly RegiomontanusHouseSystem _regiomontanus = new();
    private readonly CampanusHouseSystem _campanus = new();
    private readonly HorizonHouseSystem _horizon = new();
    private readonly SavardAHouseSystem _savardA = new();
    private readonly PolichPageHouseSystem _polichPage = new();
    private readonly MeridianHouseSystem _meridian = new();
    private readonly MorinusHouseSystem _morinus = new();
    private readonly CarterHouseSystem _carter = new();
    private readonly AlcabitiusHouseSystem _alcabitius = new();
    private readonly KrusinskiHouseSystem _krusinski = new();
    private readonly ApcHouseSystem _apc = new();
    private readonly KochHouseSystem _koch = new();
    private readonly PlacidusHouseSystem _placidus = new();
    private readonly GauquelinHouseSystem _gauquelin = new();
    private readonly SunshineTreindlHouseSystem _sunshineI = new();
    private readonly SunshineMakranskyHouseSystem _sunshineMakransky = new();

    public HouseService(
        CalendarService calendar,
        AstronomicalModelOverrides? models = null,
        SiderealService? sidereal = null,
        BodyService? bodyService = null)
    {
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _models = models ?? AstronomicalModelOverrides.Default;
        _sidereal = sidereal;
        _bodyService = bodyService;
    }

    /// <summary>
    /// Computes cusps for the given UT instant + observer location. Mirrors
    /// <c>swe_houses_ex2</c> (swehouse.c#L207). The <paramref name="flags"/>
    /// argument honours <see cref="EphemerisFlags.NoNutation"/>,
    /// <see cref="EphemerisFlags.Sidereal"/>, and
    /// <see cref="EphemerisFlags.Speed"/>; sidereal projection requires a
    /// configured <see cref="SiderealService"/> in the underlying composition.
    /// </summary>
    public HouseResult Compute(JulianDay jdUt, double geolatDeg, double geolonDeg, HouseSystem hsys, EphemerisFlags flags = 0)
    {
        // Mirrors swe_houses_ex2 (swehouse.c#L207).
        var rd = AstronomicalConstants.RadToDeg;

        var deltaT = _calendar.DeltaT(jdUt);
        var jdEt = jdUt.Value + deltaT;
        var epsMeanDeg = Precession.MeanObliquity(jdEt, _models) * rd;
        var nut = Nutation.Compute(jdEt, _models);
        var nutLonDeg = nut.DeltaPsiRad * rd;
        var nutObqDeg = nut.DeltaEpsilonRad * rd;
        if ((flags & EphemerisFlags.NoNutation) != 0)
        {
            nutLonDeg = 0;
            nutObqDeg = 0;
        }

        // ARMC = sidereal_time(jd_ut, ε_true, Δψ) * 15° + geolon
        var sidtHours = _calendar.SiderealTime(jdUt, epsMeanDeg + nutObqDeg, nutLonDeg);
        var armcDeg = AngleMath.NormalizeDegrees(sidtHours * 15.0 + geolonDeg);

        var withSpeeds = (flags & EphemerisFlags.Speed) != 0;

        // Compute Sun declination if requested system is Sunshine.
        var sunDecDeg = 0.0;
        if (hsys is HouseSystem.SunshineTreindl or HouseSystem.SunshineMakransky)
        {
            if (_bodyService is null)
                throw new EphemerisFlagsException(
                    "Sunshine houses ('I'/'i') require a BodyService instance for Sun declination.");
            var sunFlags = EphemerisFlags.Speed | EphemerisFlags.Equatorial;
            var sun = _bodyService.ComputeUt(CelestialBody.Sun, jdUt, sunFlags);
            // Equatorial output: Position is (RA, Dec, distance) cartesian → polar.
            sunDecDeg = SunDecFromBodyState(sun);
        }

        var sidereal = (flags & EphemerisFlags.Sidereal) != 0;
        if (sidereal)
        {
            if (_sidereal is null)
                throw new EphemerisFlagsException(
                    "SEFLG_SIDEREAL was set but no SiderealService was injected into HouseService.");

            var sidFlags = _sidereal.Configuration.Flags;
            if ((sidFlags & SiderealFlags.EclipticOfT0) != 0)
                return ComputeSiderealProjection(SiderealFlags.EclipticOfT0,
                    jdEt, armcDeg, geolatDeg, epsMeanDeg, nutLonDeg, nutObqDeg,
                    hsys, withSpeeds, sunDecDeg);
            if ((sidFlags & SiderealFlags.SolarSystemPlane) != 0)
                return ComputeSiderealProjection(SiderealFlags.SolarSystemPlane,
                    jdEt, armcDeg, geolatDeg, epsMeanDeg, nutLonDeg, nutObqDeg,
                    hsys, withSpeeds, sunDecDeg);
            return ComputeSiderealTraditional(jdEt, armcDeg, geolatDeg, epsMeanDeg + nutObqDeg, hsys, withSpeeds, sunDecDeg, flags);
        }

        return ComputeFromArmc(armcDeg, geolatDeg, epsMeanDeg + nutObqDeg, hsys, withSpeeds, sunDecDeg);
    }

    /// <summary>
    /// Computes cusps from a pre-derived ARMC + obliquity. Mirrors
    /// <c>swe_houses_armc_ex2</c> (swehouse.c#L622). Useful for composite /
    /// progressed charts where no JD is given.
    /// </summary>
    public HouseResult ComputeFromArmc(double armcDeg, double geolatDeg, double obliquityDeg, HouseSystem hsys, bool withSpeeds = false, double sunDeclinationDeg = 0.0)
    {
        var ito = (hsys == HouseSystem.Gauquelin) ? 36 : 12;
        var cusps = new double[ito + 1]; // 1-indexed
        Span<double> ascmc = stackalloc double[8];
        var cuspSpeeds = withSpeeds ? new double[ito + 1] : null;
        Span<double> ascmcSpeeds = stackalloc double[8];

        var ok = ComputeFromArmcIntoCore(
            armcDeg, geolatDeg, obliquityDeg, hsys,
            cusps, ascmc, cuspSpeeds, withSpeeds ? ascmcSpeeds : default,
            sunDeclinationDeg, out var warning);

        return new HouseResult
        {
            Cusps = cusps,
            Ascendant = ascmc[0],
            MidHeaven = ascmc[1],
            Armc = ascmc[2],
            Vertex = ascmc[3],
            EquatorialAscendant = ascmc[4],
            CoAscendantKoch = ascmc[5],
            CoAscendantMunkasey = ascmc[6],
            PolarAscendant = ascmc[7],
            SunDeclination = sunDeclinationDeg,
            CuspSpeeds = cuspSpeeds,
            AscendantSpeed = withSpeeds ? ascmcSpeeds[0] : 0,
            MidHeavenSpeed = withSpeeds ? ascmcSpeeds[1] : 0,
            ArmcSpeed = withSpeeds ? ascmcSpeeds[2] : 0,
            VertexSpeed = withSpeeds ? ascmcSpeeds[3] : 0,
            EquatorialAscendantSpeed = withSpeeds ? ascmcSpeeds[4] : 0,
            CoAscendantKochSpeed = withSpeeds ? ascmcSpeeds[5] : 0,
            CoAscendantMunkaseySpeed = withSpeeds ? ascmcSpeeds[6] : 0,
            PolarAscendantSpeed = withSpeeds ? ascmcSpeeds[7] : 0,
            Warning = warning,
            FellBackToPorphyry = !ok,
        };
    }

    /// <summary>
    /// Allocation-free engine. Writes 1-indexed cusps into
    /// <paramref name="cuspsOut"/> (length 13 for most systems, 37 for
    /// Gauquelin) and the ascmc octet into <paramref name="ascmcOut"/>
    /// (length 8: asc, mc, armc, vertex, equasc, coasc1, coasc2, polasc).
    /// Speed buffers are optional.
    /// </summary>
    /// <returns>True on success, false when the system fell back to Porphyry.</returns>
    public bool ComputeFromArmcInto(
        double armcDeg, double geolatDeg, double obliquityDeg,
        HouseSystem hsys,
        Span<double> cuspsOut, Span<double> ascmcOut,
        Span<double> cuspSpeedsOut = default,
        Span<double> ascmcSpeedsOut = default,
        double sunDeclinationDeg = 0.0)
        => ComputeFromArmcIntoCore(armcDeg, geolatDeg, obliquityDeg, hsys,
            cuspsOut, ascmcOut, cuspSpeedsOut, ascmcSpeedsOut, sunDeclinationDeg, out _);

    private bool ComputeFromArmcIntoCore(
        double armcDeg, double geolatDeg, double obliquityDeg,
        HouseSystem hsys,
        Span<double> cuspsOut, Span<double> ascmcOut,
        Span<double> cuspSpeedsOut,
        Span<double> ascmcSpeedsOut,
        double sunDeclinationDeg,
        out string? warning)
    {
        armcDeg = AngleMath.NormalizeDegrees(armcDeg);
        var impl = ResolveImpl(hsys);
        var ito = impl.CuspCount;

        if (cuspsOut.Length < ito + 1)
            throw new ArgumentException($"cuspsOut must have length ≥ {ito + 1}.", nameof(cuspsOut));
        if (ascmcOut.Length < 8)
            throw new ArgumentException("ascmcOut must have length ≥ 8.", nameof(ascmcOut));

        // Clear output buffers used as accumulators.
        cuspsOut.Clear();
        ascmcOut.Clear();
        if (!cuspSpeedsOut.IsEmpty) cuspSpeedsOut.Clear();
        if (!ascmcSpeedsOut.IsEmpty) ascmcSpeedsOut.Clear();

        var ctx = HouseComputeContext.Build(
            armcDeg, geolatDeg, obliquityDeg,
            cuspsOut, ascmcOut, cuspSpeedsOut, ascmcSpeedsOut, sunDeclinationDeg);

        ComputeAxesAndStandardPoints(ref ctx);

        var ok = impl.Compute(ref ctx);
        var actualImpl = impl;
        if (!ok && ctx.FellBackToPorphyry)
        {
            // Porphyry fallback: re-run with Porphyry but keep the warning.
            // Need to recompute axes since the implementation may have
            // mutated ctx.Ac / ctx.Mc; safer to re-build ctx.
            var savedWarn = ctx.Warning;
            ctx = HouseComputeContext.Build(
                armcDeg, geolatDeg, obliquityDeg,
                cuspsOut, ascmcOut, cuspSpeedsOut, ascmcSpeedsOut, sunDeclinationDeg);
            ComputeAxesAndStandardPoints(ref ctx);
            actualImpl = _porphyry;
            actualImpl.Compute(ref ctx);
            ctx.Warning = savedWarn;
            ctx.FellBackToPorphyry = true;
        }

        // Default mirror at swehouse.c#L1985-L1991: skipped for G / Y / I / i.
        if (!actualImpl.SkipsDefaultMirror)
        {
            cuspsOut[4] = AngleMath.NormalizeDegrees(cuspsOut[10] + 180);
            cuspsOut[5] = AngleMath.NormalizeDegrees(cuspsOut[11] + 180);
            cuspsOut[6] = AngleMath.NormalizeDegrees(cuspsOut[12] + 180);
            cuspsOut[7] = AngleMath.NormalizeDegrees(cuspsOut[1] + 180);
            cuspsOut[8] = AngleMath.NormalizeDegrees(cuspsOut[2] + 180);
            cuspsOut[9] = AngleMath.NormalizeDegrees(cuspsOut[3] + 180);
            if (ctx.DoHspeed && !ctx.DoInterpol)
            {
                cuspSpeedsOut[4] = cuspSpeedsOut[10];
                cuspSpeedsOut[5] = cuspSpeedsOut[11];
                cuspSpeedsOut[6] = cuspSpeedsOut[12];
                cuspSpeedsOut[7] = cuspSpeedsOut[1];
                cuspSpeedsOut[8] = cuspSpeedsOut[2];
                cuspSpeedsOut[9] = cuspSpeedsOut[3];
            }
        }

        ComputeVertexAndCoascAndPolasc(ref ctx);

        // Fill ascmc octet.
        ascmcOut[0] = ctx.Ac;
        ascmcOut[1] = ctx.Mc;
        ascmcOut[2] = armcDeg;
        ascmcOut[3] = ctx.Ascmc[3]; // vertex
        ascmcOut[4] = ctx.Ascmc[4]; // equasc
        ascmcOut[5] = ctx.Ascmc[5]; // coasc1
        ascmcOut[6] = ctx.Ascmc[6]; // coasc2
        ascmcOut[7] = ctx.Ascmc[7]; // polasc

        if (!ascmcSpeedsOut.IsEmpty && ctx.DoSpeed)
        {
            ascmcSpeedsOut[0] = ctx.AcSpeed;
            ascmcSpeedsOut[1] = ctx.McSpeed;
            ascmcSpeedsOut[2] = ctx.ArmcSpeed;
            ascmcSpeedsOut[3] = ctx.AscmcSpeed[3];
            ascmcSpeedsOut[4] = ctx.AscmcSpeed[4];
            ascmcSpeedsOut[5] = ctx.AscmcSpeed[5];
            ascmcSpeedsOut[6] = ctx.AscmcSpeed[6];
            ascmcSpeedsOut[7] = ctx.AscmcSpeed[7];
        }

        // Cusp speeds via interpolation (swehouse.c#L697-L723) — only when
        // the system flagged DoInterpol. We do a centred FD across ±1s.
        if (ctx.DoInterpol && !cuspSpeedsOut.IsEmpty)
            ComputeInterpolatedCuspSpeeds(armcDeg, geolatDeg, obliquityDeg, hsys, sunDeclinationDeg, cuspsOut, cuspSpeedsOut);

        warning = ctx.Warning;
        return !ctx.FellBackToPorphyry;
    }

    private void ComputeAxesAndStandardPoints(ref HouseComputeContext ctx)
    {
        // MC mirrors swehouse.c#L956-L968.
        ctx.Mc = HouseAscendantMath.ArmcToMc(ctx.Th, ctx.Ekl);
        if (ctx.DoSpeed) ctx.McSpeed = HouseAscendantMath.AscDash(ctx.Th, 0, ctx.Sine, ctx.Cose);
        // Ascendant: Asc1 with pole-height = geographic latitude on ARMC + 90.
        ctx.Ac = HouseAscendantMath.Asc1(ctx.Th + 90, ctx.Fi, ctx.Sine, ctx.Cose);
        if (ctx.DoSpeed) ctx.AcSpeed = HouseAscendantMath.AscDash(ctx.Th + 90, ctx.Fi, ctx.Sine, ctx.Cose);
        ctx.Cusps[1] = ctx.Ac;
        ctx.Cusps[10] = ctx.Mc;
        if (ctx.DoHspeed)
        {
            ctx.CuspSpeeds[1] = ctx.AcSpeed;
            ctx.CuspSpeeds[10] = ctx.McSpeed;
        }
    }

    private static void ComputeVertexAndCoascAndPolasc(ref HouseComputeContext ctx)
    {
        // swehouse.c#L2001-L2048
        var fVertex = ctx.Fi >= 0 ? 90 - ctx.Fi : -90 - ctx.Fi;
        var vertex = HouseAscendantMath.Asc1(ctx.Th - 90, fVertex, ctx.Sine, ctx.Cose);
        if (System.Math.Abs(ctx.Fi) <= ctx.Ekl)
        {
            var vemc = AngleMath.DifferenceDegreesSigned(vertex, ctx.Mc);
            if (vemc > 0) vertex = AngleMath.NormalizeDegrees(vertex + 180);
        }
        ctx.Ascmc[3] = vertex;
        if (ctx.DoSpeed) ctx.AscmcSpeed[3] = HouseAscendantMath.AscDash(ctx.Th - 90, fVertex, ctx.Sine, ctx.Cose);

        // equatorial ascendant
        var th2 = AngleMath.NormalizeDegrees(ctx.Th + 90);
        double equasc;
        if (System.Math.Abs(th2 - 90) > HouseAscendantMath.VerySmall
            && System.Math.Abs(th2 - 270) > HouseAscendantMath.VerySmall)
        {
            var dr = AstronomicalConstants.DegToRad;
            var rd = AstronomicalConstants.RadToDeg;
            var tant = System.Math.Tan(th2 * dr);
            equasc = System.Math.Atan(tant / ctx.Cose) * rd;
            if (th2 > 90 && th2 <= 270) equasc = AngleMath.NormalizeDegrees(equasc + 180);
        }
        else
        {
            equasc = System.Math.Abs(th2 - 90) <= HouseAscendantMath.VerySmall ? 90 : 270;
        }
        ctx.Ascmc[4] = AngleMath.NormalizeDegrees(equasc);
        if (ctx.DoSpeed) ctx.AscmcSpeed[4] = HouseAscendantMath.AscDash(ctx.Th + 90, 0, ctx.Sine, ctx.Cose);

        // co-ascendant (W. Koch)
        ctx.Ascmc[5] = AngleMath.NormalizeDegrees(HouseAscendantMath.Asc1(ctx.Th - 90, ctx.Fi, ctx.Sine, ctx.Cose) + 180);
        if (ctx.DoSpeed) ctx.AscmcSpeed[5] = HouseAscendantMath.AscDash(ctx.Th - 90, ctx.Fi, ctx.Sine, ctx.Cose);

        // co-ascendant (M. Munkasey)
        if (ctx.Fi >= 0)
        {
            ctx.Ascmc[6] = HouseAscendantMath.Asc1(ctx.Th + 90, 90 - ctx.Fi, ctx.Sine, ctx.Cose);
            if (ctx.DoSpeed) ctx.AscmcSpeed[6] = HouseAscendantMath.AscDash(ctx.Th + 90, 90 - ctx.Fi, ctx.Sine, ctx.Cose);
        }
        else
        {
            ctx.Ascmc[6] = HouseAscendantMath.Asc1(ctx.Th + 90, -90 - ctx.Fi, ctx.Sine, ctx.Cose);
            if (ctx.DoSpeed) ctx.AscmcSpeed[6] = HouseAscendantMath.AscDash(ctx.Th + 90, -90 - ctx.Fi, ctx.Sine, ctx.Cose);
        }

        // polar ascendant (M. Munkasey)
        ctx.Ascmc[7] = HouseAscendantMath.Asc1(ctx.Th - 90, ctx.Fi, ctx.Sine, ctx.Cose);
        if (ctx.DoSpeed) ctx.AscmcSpeed[7] = HouseAscendantMath.AscDash(ctx.Th - 90, ctx.Fi, ctx.Sine, ctx.Cose);
    }

    /// <summary>
    /// Centred finite-difference cusp speeds for systems that do not have a
    /// closed-form derivative (Sripati / Pullen / Meridian / Morinus / Carter
    /// / Alcabitius / APC / Sunshine). Mirrors swehouse.c#L697-L723 — the
    /// C library uses ±1 s of ARMC and divides by 2·dt.
    /// </summary>
    private void ComputeInterpolatedCuspSpeeds(
        double armcDeg, double geolatDeg, double obliquityDeg,
        HouseSystem hsys, double sunDeclinationDeg,
        ReadOnlySpan<double> cusps, Span<double> cuspSpeedsOut)
    {
        var ito = (hsys == HouseSystem.Gauquelin) ? 36 : 12;
        const double dt = 1.0 / 86_400.0;
        var darmc = dt * HouseAscendantMath.ArmcDegreesPerDay;

        Span<double> cuspsM = stackalloc double[37];
        Span<double> cuspsP = stackalloc double[37];
        Span<double> ascmcTmp = stackalloc double[8];

        var dtUsed = dt;

        // hm1 (armc - δ) and hp1 (armc + δ).
        ComputeFromArmcInto(armcDeg - darmc, geolatDeg, obliquityDeg, hsys,
            cuspsM, ascmcTmp, default, default, sunDeclinationDeg);
        var ascM = ascmcTmp[0];
        ComputeFromArmcInto(armcDeg + darmc, geolatDeg, obliquityDeg, hsys,
            cuspsP, ascmcTmp, default, default, sunDeclinationDeg);
        var ascP = ascmcTmp[0];

        var ascCenter = ResolveAscFromCusps(cusps); // cusps[1] for most systems
        if (System.Math.Abs(AngleMath.DifferenceDegreesSigned(ascP, ascCenter)) > 90)
        {
            // upper interval crosses; use only lower interval (replace + with center)
            cuspsP.Clear();
            for (var i = 0; i <= ito; i++) cuspsP[i] = cusps[i];
            dtUsed = dt / 2;
        }
        else if (System.Math.Abs(AngleMath.DifferenceDegreesSigned(ascM, ascCenter)) > 90)
        {
            cuspsM.Clear();
            for (var i = 0; i <= ito; i++) cuspsM[i] = cusps[i];
            dtUsed = dt / 2;
        }

        for (var i = 1; i <= ito; i++)
        {
            var dx = AngleMath.DifferenceDegreesSigned(cuspsP[i], cuspsM[i]);
            cuspSpeedsOut[i] = dx / 2.0 / dtUsed;
        }
    }

    private static double ResolveAscFromCusps(ReadOnlySpan<double> cusps) => cusps[1];

    /// <summary>
    /// Mirrors <c>sidereal_houses_ecl_t0</c> / <c>sidereal_houses_ssypl</c>
    /// (swehouse.c#L318-L532): build the auxiliary armc + obliquity from
    /// <see cref="SiderealHouseProjections"/>, run the standard tropical
    /// house computation, then shift the cusps and the non-ARMC ascmc
    /// points by the projection-specific constant.
    /// </summary>
    private HouseResult ComputeSiderealProjection(
        SiderealFlags projection,
        double tjdeTt, double armcDeg, double geolatDeg,
        double meanEpsDeg, double nutLonDeg, double nutObqDeg,
        HouseSystem hsys, bool withSpeeds, double sunDecDeg)
    {
        // _sidereal is non-null — caller checked.
        var cfg = _sidereal!.Configuration;
        var t0Tt = cfg.T0;
        if (cfg.T0IsUt)
            t0Tt += _calendar.DeltaT(new JulianDay(t0Tt));

        var trueEpsDeg = meanEpsDeg + nutObqDeg;
        var aux = projection == SiderealFlags.EclipticOfT0
            ? SiderealHouseProjections.EclT0(
                tjdeTt, armcDeg, trueEpsDeg, nutLonDeg, nutObqDeg,
                t0Tt, cfg.AyanT0, _models)
            : SiderealHouseProjections.SsyPlane(
                tjdeTt, armcDeg, trueEpsDeg, nutLonDeg, nutObqDeg,
                t0Tt, cfg.AyanT0, _models);

        var result = ComputeFromArmc(aux.ArmcDeg, geolatDeg, aux.ObliquityDeg, hsys, withSpeeds, sunDecDeg);
        var shift = aux.ShiftDeg;
        var ito = (hsys == HouseSystem.Gauquelin) ? 36 : 12;
        for (var i = 1; i <= ito; i++)
            result.Cusps[i] = AngleMath.NormalizeDegrees(result.Cusps[i] - shift);
        if (hsys == HouseSystem.EqualAriesAnchored)
        {
            for (var i = 1; i <= ito; i++) result.Cusps[i] = (i - 1) * 30.0;
        }

        return result with
        {
            Ascendant = AngleMath.NormalizeDegrees(result.Ascendant - shift),
            MidHeaven = AngleMath.NormalizeDegrees(result.MidHeaven - shift),
            Vertex = AngleMath.NormalizeDegrees(result.Vertex - shift),
            EquatorialAscendant = AngleMath.NormalizeDegrees(result.EquatorialAscendant - shift),
            CoAscendantKoch = AngleMath.NormalizeDegrees(result.CoAscendantKoch - shift),
            CoAscendantMunkasey = AngleMath.NormalizeDegrees(result.CoAscendantMunkasey - shift),
            PolarAscendant = AngleMath.NormalizeDegrees(result.PolarAscendant - shift),
        };
    }

    private HouseResult ComputeSiderealTraditional(
        double tjde, double armcDeg, double geolatDeg, double trueEpsDeg,
        HouseSystem hsys, bool withSpeeds, double sunDecDeg, EphemerisFlags flags)
    {
        if (_sidereal is null)
            throw new EphemerisFlagsException(
                "SEFLG_SIDEREAL was set but no SiderealService was injected into HouseService.");

        var actualHsys = hsys;
        var isWholeSign = hsys == HouseSystem.WholeSign;
        if (isWholeSign) actualHsys = HouseSystem.Equal;

        var result = ComputeFromArmc(armcDeg, geolatDeg, trueEpsDeg, actualHsys, withSpeeds, sunDecDeg);

        var ay = _sidereal.GetAyanamsa(new JulianDay(tjde), flags);
        var ito = (hsys == HouseSystem.Gauquelin) ? 36 : 12;
        for (var i = 1; i <= ito; i++)
        {
            result.Cusps[i] = AngleMath.NormalizeDegrees(result.Cusps[i] - ay);
            if (isWholeSign) result.Cusps[i] -= result.Cusps[i] % 30.0;
        }
        if (hsys == HouseSystem.EqualAriesAnchored)
        {
            for (var i = 1; i <= ito; i++) result.Cusps[i] = (i - 1) * 30.0;
        }

        // ascmc — skip armc (slot 2)
        return result with
        {
            Ascendant = AngleMath.NormalizeDegrees(result.Ascendant - ay),
            MidHeaven = AngleMath.NormalizeDegrees(result.MidHeaven - ay),
            Vertex = AngleMath.NormalizeDegrees(result.Vertex - ay),
            EquatorialAscendant = AngleMath.NormalizeDegrees(result.EquatorialAscendant - ay),
            CoAscendantKoch = AngleMath.NormalizeDegrees(result.CoAscendantKoch - ay),
            CoAscendantMunkasey = AngleMath.NormalizeDegrees(result.CoAscendantMunkasey - ay),
            PolarAscendant = AngleMath.NormalizeDegrees(result.PolarAscendant - ay),
        };
    }

    private static double SunDecFromBodyState(BodyState equatorial)
    {
        // Equatorial flag returns cartesian (x,y,z) where polar = (RA, Dec, dist).
        var rd = AstronomicalConstants.RadToDeg;
        var x = equatorial.Position.X;
        var y = equatorial.Position.Y;
        var z = equatorial.Position.Z;
        var rxy = System.Math.Sqrt(x * x + y * y);
        return System.Math.Atan2(z, rxy) * rd;
    }

    private IHouseSystemImpl ResolveImpl(HouseSystem hsys) => hsys switch
    {
        HouseSystem.Equal => _equal,
        HouseSystem.EqualMc => _equalMc,
        HouseSystem.EqualAriesAnchored => _equalAries,
        HouseSystem.Vehlow => _vehlow,
        HouseSystem.WholeSign => _wholeSign,
        HouseSystem.Porphyry => _porphyry,
        HouseSystem.Sripati => _sripati,
        HouseSystem.PullenSinusoidalDelta => _pullenSd,
        HouseSystem.PullenSinusoidalRatio => _pullenSr,
        HouseSystem.Regiomontanus => _regiomontanus,
        HouseSystem.Campanus => _campanus,
        HouseSystem.Horizon => _horizon,
        HouseSystem.SavardA => _savardA,
        HouseSystem.PolichPage => _polichPage,
        HouseSystem.Meridian => _meridian,
        HouseSystem.Morinus => _morinus,
        HouseSystem.CarterPoliEquatorial => _carter,
        HouseSystem.Alcabitius => _alcabitius,
        HouseSystem.KrusinskiPisaGoelzer => _krusinski,
        HouseSystem.Apc => _apc,
        HouseSystem.Koch => _koch,
        HouseSystem.Placidus => _placidus,
        HouseSystem.Gauquelin => _gauquelin,
        HouseSystem.SunshineTreindl => _sunshineI,
        HouseSystem.SunshineMakransky => _sunshineMakransky,
        _ => _placidus, // C library default for unknown letter
    };

    /// <summary>
    /// Display name of a house system. Mirrors <c>swe_house_name</c>
    /// (swehouse.c#L827).
    /// </summary>
    public string GetName(HouseSystem hsys) => hsys switch
    {
        HouseSystem.Equal => "equal",
        HouseSystem.Alcabitius => "Alcabitius",
        HouseSystem.Campanus => "Campanus",
        HouseSystem.EqualMc => "equal (MC)",
        HouseSystem.CarterPoliEquatorial => "Carter poli-equ.",
        HouseSystem.Gauquelin => "Gauquelin sectors",
        HouseSystem.Horizon => "horizon/azimut",
        HouseSystem.SunshineTreindl => "Sunshine",
        HouseSystem.SunshineMakransky => "Sunshine/alt.",
        HouseSystem.SavardA => "Savard-A",
        HouseSystem.Koch => "Koch",
        HouseSystem.PullenSinusoidalDelta => "Pullen SD",
        HouseSystem.Morinus => "Morinus",
        HouseSystem.EqualAriesAnchored => "equal/1=Aries",
        HouseSystem.Porphyry => "Porphyry",
        HouseSystem.PullenSinusoidalRatio => "Pullen SR",
        HouseSystem.Regiomontanus => "Regiomontanus",
        HouseSystem.Sripati => "Sripati",
        HouseSystem.PolichPage => "Polich/Page",
        HouseSystem.KrusinskiPisaGoelzer => "Krusinski-Pisa-Goelzer",
        HouseSystem.Vehlow => "equal/Vehlow",
        HouseSystem.WholeSign => "equal/ whole sign",
        HouseSystem.Meridian => "axial rotation system/Meridian houses",
        HouseSystem.Apc => "APC houses",
        HouseSystem.Placidus => "Placidus",
        _ => "Placidus",
    };

    /// <summary>
    /// Position of an ecliptic point in the requested house system. Mirrors
    /// <c>swe_house_pos</c> (swehouse.c#L2216). The returned value is in the
    /// half-open interval <c>[1, 13)</c> for most systems, <c>[1, 37)</c>
    /// for Gauquelin.
    /// </summary>
    public double HousePosition(double armcDeg, double geolatDeg, double obliquityDeg, HouseSystem hsys, double lonDeg, double latDeg, double sunDeclinationDeg = 0.0)
        => HousePositionMath.Compute(this, armcDeg, geolatDeg, obliquityDeg, hsys, lonDeg, latDeg, sunDeclinationDeg);
}
