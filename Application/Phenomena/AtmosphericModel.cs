// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   Sgn                 — sgn                            (swehel.c#L864)
//   TopoAltFromAppAlt   — TopoAltfromAppAlt              (swehel.c#L601)
//   AppAltFromTopoAlt   — AppAltfromTopoAlt              (swehel.c#L626)
//   HourAngleHours      — HourAngle                      (swehel.c#L662)
//   DistanceAngleRad    — DistanceAngle                  (swehel.c#L780)
//   TempEFromTempS      — TempE_from_TempS               (swehel.c#L1005)
//   PresEFromPresS      — PresE_from_PresS               (swehel.c#L1016)
//   Airmass             — Airmass                        (swehel.c#L964)
//   Xext                — Xext                           (swehel.c#L980)
//   Xlay                — Xlay                           (swehel.c#L991)
//   Kw                  — kW                             (swehel.c#L801)
//   Kr                  — kR                             (swehel.c#L848)
//   KOz                 — kOZ                            (swehel.c#L815)
//   Ka                  — ka                             (swehel.c#L881)
//   Kt                  — kt                             (swehel.c#L940)
//   DeltaMagnitudes     — Deltam                         (swehel.c#L1033)
//   DefaultParameters   — default_heliacal_parameters    (swehel.c#L1324)
//
// Reference driver: /tmp/heliacal_a_ref.c — golden values for every helper at
// representative inputs. Built against the unmodified swehel.c with file-scope
// `static` keywords stripped by sed (function-local TLS caches are kept).
//
// IMPORTANT: the shipped Swiss Ephemeris defines `SIMULATE_VICTORVB` in
// swephexp.h:451. That macro toggles three code regions in swehel.c:
//   - swehel.c#L563  — `SunRA` falls through to the cached/Moshier branch.
//   - swehel.c#L918  — `ka` clamps RH into (1e-8, 99.99999999) when VR == 0.
//   - swehel.c#L1339 — `default_heliacal_parameters` does NOT clamp RH.
// This port mirrors the shipped behaviour (RH clamped only in `ka` VR=0).

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Stateless atmospheric-physics helpers (Schaefer 2000 / Reijs / Garstang).
/// All inputs in degrees / mbar / Celsius unless otherwise noted; angles inside
/// the Haversine formula take radians. The methods are pure (no caches).
/// </summary>
internal static class AtmosphericModel
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double LapseSa = HeliacalConstants.LapseStandardAtmosphere;
    private const double ScaleHRayleigh = HeliacalConstants.ScaleHeightRayleigh;
    private const double ScaleHWater = HeliacalConstants.ScaleHeightWater;
    private const double ScaleHAerosol = HeliacalConstants.ScaleHeightAerosol;
    private const double ScaleHOzone = HeliacalConstants.ScaleHeightOzone;
    private const double EarthRa = HeliacalConstants.EarthEquatorialRadius;
    private const double Astr2Tau = HeliacalConstants.AstroMagnitudesToTau;
    private const double LowestAppAlt = HeliacalConstants.LowestAppAltDeg;
    private const double PressureRef = HeliacalConstants.AirmassReferencePressure;
    private const double C2K = HeliacalConstants.CelsiusToKelvinOffset;

    /// <summary>Hyperbolic tangent (alias to <see cref="Math.Tanh(double)"/>).</summary>
    public static double Tanh(double x) => Math.Tanh(x);

    /// <summary>Celsius → Kelvin.</summary>
    public static double Kelvin(double tempCelsius) => tempCelsius + C2K;

    /// <summary>Sign function with <c>Sgn(0) == 1</c>.</summary>
    public static int Sgn(double x) => x < 0 ? -1 : 1;

    // --- refraction (swehel-flavoured Sinclair) ------------------------------

    /// <summary>
    /// True altitude from apparent altitude (Sinclair refraction model re-cast
    /// for low-precision use). <paramref name="appAltDeg"/> below
    /// <see cref="HeliacalConstants.LowestAppAltDeg"/> is returned unchanged.
    /// </summary>
    public static double TopoAltFromAppAlt(double appAltDeg, double tempECelsius, double presEMbar)
    {
        if (appAltDeg < LowestAppAlt) return appAltDeg;
        double r = appAltDeg > 17.904104638432
            ? 0.97 / Math.Tan(appAltDeg * DegToRad)
            : (34.46 + 4.23 * appAltDeg + 0.004 * appAltDeg * appAltDeg)
              / (1 + 0.505 * appAltDeg + 0.0845 * appAltDeg * appAltDeg);
        r = (presEMbar - 80) / 930
            / (1 + 0.00008 * (r + 39) * (tempECelsius - 10)) * r;
        return appAltDeg - r / 60.0; // arcmin → deg
    }

    /// <summary>
    /// Apparent altitude from true altitude via Newton iteration over
    /// <see cref="TopoAltFromAppAlt"/>. <paramref name="useHighPrecision"/>
    /// selects 5 vs 2 iterations.
    /// </summary>
    public static double AppAltFromTopoAlt(double topoAltDeg, double tempECelsius, double presEMbar, bool useHighPrecision)
    {
        var nloop = useHighPrecision ? 5 : 2;
        double newApp = topoAltDeg;
        double newTopo = 0.0;
        double oldApp = newApp;
        double oldTopo = newTopo;
        for (var i = 0; i <= nloop; i++)
        {
            newTopo = newApp - TopoAltFromAppAlt(newApp, tempECelsius, presEMbar);
            var verschil = newApp - oldApp;
            oldApp = newTopo - oldTopo - verschil;
            verschil = (verschil != 0) && (oldApp != 0)
                ? newApp - verschil * (topoAltDeg + newTopo - newApp) / oldApp
                : topoAltDeg + newTopo;
            oldApp = newApp;
            oldTopo = newTopo;
            newApp = verschil;
        }
        var ret = topoAltDeg + newTopo;
        return ret < LowestAppAlt ? topoAltDeg : ret;
    }

    // --- spherical geometry --------------------------------------------------

    /// <summary>
    /// Hour angle (hours) for a body at <paramref name="topoAltDeg"/> with
    /// declination <paramref name="topoDeclDeg"/> at latitude
    /// <paramref name="latDeg"/>.
    /// </summary>
    public static double HourAngleHours(double topoAltDeg, double topoDeclDeg, double latDeg)
    {
        var alt = topoAltDeg * DegToRad;
        var decl = topoDeclDeg * DegToRad;
        var lat = latDeg * DegToRad;
        var ha = (Math.Sin(alt) - Math.Sin(lat) * Math.Sin(decl)) / Math.Cos(lat) / Math.Cos(decl);
        if (ha < -1) ha = -1;
        if (ha > 1) ha = 1;
        return Math.Acos(ha) / DegToRad / 15.0;
    }

    /// <summary>
    /// Great-circle separation (radians) via Haversine. All inputs in radians.
    /// </summary>
    public static double DistanceAngleRad(double latARad, double lonARad, double latBRad, double lonBRad)
    {
        var dlon = lonBRad - lonARad;
        var dlat = latBRad - latARad;
        var sindlat2 = Math.Sin(dlat / 2);
        var sindlon2 = Math.Sin(dlon / 2);
        var corde = sindlat2 * sindlat2 + Math.Cos(latARad) * Math.Cos(latBRad) * sindlon2 * sindlon2;
        if (corde > 1) corde = 1;
        return 2 * Math.Asin(Math.Sqrt(corde));
    }

    // --- meteorology ---------------------------------------------------------

    /// <summary>Eye-level temperature from surface temperature.</summary>
    public static double TempEFromTempS(double tempSCelsius, double heightMeters, double lapseRate)
        => tempSCelsius - lapseRate * heightMeters;

    /// <summary>
    /// Eye-level pressure from surface pressure (barometric formula with
    /// linear-mean temperature correction).
    /// </summary>
    public static double PresEFromPresS(double tempSCelsius, double presSMbar, double heightMeters)
        => presSMbar * Math.Exp(
            -9.80665 * 0.0289644
            / (Kelvin(tempSCelsius) + 3.25 * heightMeters / 1000.0)
            / 8.31441
            * heightMeters);

    // --- airmass primitives --------------------------------------------------

    /// <summary>
    /// Airmass at given apparent altitude and surface pressure. The zenith
    /// angle is clamped to π/2.
    /// </summary>
    public static double Airmass(double appAltODeg, double presSMbar)
    {
        var zend = (90 - appAltODeg) * DegToRad;
        if (zend > Math.PI / 2) zend = Math.PI / 2;
        var airm = 1.0 / (Math.Cos(zend) + 0.025 * Math.Exp(-11 * Math.Cos(zend)));
        return presSMbar / PressureRef * airm;
    }

    /// <summary>
    /// Schaefer's exponential-scale-height airmass for an extinguishing layer.
    /// <paramref name="zendRad"/> in radians.
    /// </summary>
    public static double Xext(double scaleHMeters, double zendRad, double presSMbar)
    {
        var s = Math.Sqrt(scaleHMeters / 1000.0);
        return presSMbar / PressureRef
               / (Math.Cos(zendRad) + 0.01 * s * Math.Exp(-30.0 / s * Math.Cos(zendRad)));
    }

    /// <summary>
    /// Airmass for a thin layer (ozone).
    /// </summary>
    public static double Xlay(double scaleHMeters, double zendRad, double presSMbar)
    {
        var a = Math.Sin(zendRad) / (1.0 + scaleHMeters / EarthRa);
        return presSMbar / PressureRef / Math.Sqrt(1.0 - a * a);
    }

    // --- extinction coefficients (Schaefer 2000) -----------------------------

    /// <summary>Water-vapour extinction coefficient.</summary>
    public static double Kw(double heightMeters, double tempSCelsius, double relHumidityPercent)
    {
        var wt = 0.031;
        wt *= 0.94 * (relHumidityPercent / 100.0)
              * Math.Exp(tempSCelsius / 15.0)
              * Math.Exp(-heightMeters / ScaleHWater);
        return wt;
    }

    /// <summary>
    /// Rayleigh extinction coefficient. Day/night vision shifts the eye's peak
    /// sensitivity (λ) — see Vistas in Astronomy p. 343. <paramref name="altSDeg"/>
    /// ≥ -12° leaves λ at 0.55 µm.
    /// </summary>
    public static double Kr(double altSDeg, double heightMeters)
    {
        var val = -altSDeg - 12;
        if (val < 0) val = 0;
        if (val > 6) val = 6;
        var changeK = 1 - 0.166667 * val;
        var lambda = 0.55 + (changeK - 1) * 0.04;
        return 0.1066
               * Math.Exp(-heightMeters / ScaleHRayleigh)
               * Math.Pow(lambda / 0.55, -4);
    }

    /// <summary>
    /// Ozone extinction coefficient. The C source caches the most-recent
    /// <c>(altS, sunra)</c> pair in TLS; this port is stateless — the underlying
    /// math is unchanged.
    /// </summary>
    public static double KOz(double altSDeg, double sunRaDeg, double latDeg)
    {
        const double oz = 0.031;
        var lt = latDeg * DegToRad;
        var koz = oz * (3.0 + 0.4 * (lt * Math.Cos(sunRaDeg * DegToRad) - Math.Cos(3 * lt))) / 3.0;
        var altslim = -altSDeg - 12;
        if (altslim < 0) altslim = 0;
        if (altslim > 6) altslim = 6;
        var changeKo = (100 - 11.6 * altslim) / 100;
        return koz * changeKo;
    }

    /// <summary>
    /// Aerosol extinction coefficient. Three branches:
    /// <list type="bullet">
    /// <item><description><c>VR ≥ 1</c>: Koshmieder/Narasimhan visibility-range conversion.</description></item>
    /// <item><description><c>0 &lt; VR &lt; 1</c>: <c>VR</c> is treated as <c>ktot</c>.</description></item>
    /// <item><description><c>VR ≤ 0</c>: Schaefer formula with RH clamped to
    /// (1e-8, 99.99999999) (mirrors the shipped C lib's <c>SIMULATE_VICTORVB</c>
    /// behaviour).</description></item>
    /// </list>
    /// Returns <c>(value, warning?)</c>; the warning is non-null when the result
    /// would go negative.
    /// </summary>
    public static (double Value, string? Warning) Ka(
        double altSDeg, double sunRaDeg, double latDeg,
        double heightMeters, double tempSCelsius, double relHumidityPercent, double meteorologicalRangeKm)
    {
        var sl = Sgn(latDeg);
        var val = -altSDeg - 12;
        if (val < 0) val = 0;
        if (val > 6) val = 6;
        var changeKa = 1 - 0.166667 * val;
        var lambda = 0.55 + (changeKa - 1) * 0.04;

        if (meteorologicalRangeKm != 0)
        {
            if (meteorologicalRangeKm >= 1)
            {
                var betaVr = 3.912 / meteorologicalRangeKm;
                var betaa = betaVr - (Kw(heightMeters, tempSCelsius, relHumidityPercent) / ScaleHWater
                                       + Kr(altSDeg, heightMeters) / ScaleHRayleigh) * 1000.0 * Astr2Tau;
                var ka = betaa * ScaleHAerosol / 1000.0 / Astr2Tau;
                return (ka, ka < 0
                    ? "The provided Meteorological range is too long, when taking into acount other atmospheric parameters"
                    : null);
            }
            else
            {
                var ka = meteorologicalRangeKm
                         - Kw(heightMeters, tempSCelsius, relHumidityPercent)
                         - Kr(altSDeg, heightMeters)
                         - KOz(altSDeg, sunRaDeg, latDeg);
                return (ka, ka < 0
                    ? "The provided atmosphic coeefficent (ktot) is too low, when taking into acount other atmospheric parameters"
                    : null);
            }
        }
        else
        {
            // SIMULATE_VICTORVB clamps RH here (swehel.c#L919-L921).
            var rh = relHumidityPercent;
            if (rh <= 0.00000001) rh = 0.00000001;
            if (rh >= 99.99999999) rh = 99.99999999;
            var ka = 0.1
                     * Math.Exp(-heightMeters / ScaleHAerosol)
                     * Math.Pow(1 - 0.32 / Math.Log(rh / 100.0), 1.33)
                     * (1 + 0.33 * sl * Math.Sin(sunRaDeg * DegToRad));
            ka *= Math.Pow(lambda / 0.55, -1.3);
            return (ka, null);
        }
    }

    /// <summary>
    /// Total extinction-coefficient selector. <paramref name="extType"/> = 0
    /// returns <c>ka</c>, 1 <c>kW</c>, 2 <c>kR</c>, 3 <c>kOZ</c>, 4 the sum
    /// (<c>ktot</c>). Negative <c>ka</c> contributions are floored to 0 in the
    /// sum branch.
    /// </summary>
    public static double Kt(
        double altSDeg, double sunRaDeg, double latDeg,
        double heightMeters, double tempSCelsius, double relHumidityPercent, double meteorologicalRangeKm,
        int extType)
    {
        double kr = 0, kw = 0, koz = 0, ka = 0;
        if (extType == 2 || extType == 4) kr = Kr(altSDeg, heightMeters);
        if (extType == 1 || extType == 4) kw = Kw(heightMeters, tempSCelsius, relHumidityPercent);
        if (extType == 3 || extType == 4) koz = KOz(altSDeg, sunRaDeg, latDeg);
        if (extType == 0 || extType == 4)
            ka = Ka(altSDeg, sunRaDeg, latDeg, heightMeters, tempSCelsius, relHumidityPercent, meteorologicalRangeKm).Value;
        if (ka < 0) ka = 0;
        return kw + kr + koz + ka;
    }

    /// <summary>
    /// Extinction along the line of sight, in magnitudes. Uses the
    /// four-component "static airmass = 0" branch (the alternative
    /// <c>Airmass</c>-only branch is dead code in the shipped lib).
    /// </summary>
    public static double DeltaMagnitudes(
        double altODeg, double altSDeg, double sunRaDeg, double latDeg,
        double heightMeters, AtmosphericConditions atm, bool useHighPrecision)
    {
        var presE = PresEFromPresS(atm.TemperatureCelsius, atm.PressureMbar, heightMeters);
        var tempE = TempEFromTempS(atm.TemperatureCelsius, heightMeters, LapseSa);
        var appAltO = AppAltFromTopoAlt(altODeg, tempE, presE, useHighPrecision);
        var zend = (90 - appAltO) * DegToRad;
        if (zend > Math.PI / 2) zend = Math.PI / 2;

        var xR = Xext(ScaleHRayleigh, zend, atm.PressureMbar);
        var xW = Xext(ScaleHWater, zend, atm.PressureMbar);
        var xA = Xext(ScaleHAerosol, zend, atm.PressureMbar);
        var xOz = Xlay(ScaleHOzone, zend, atm.PressureMbar);
        var kaVal = Ka(altSDeg, sunRaDeg, latDeg, heightMeters,
                       atm.TemperatureCelsius, atm.RelativeHumidityPercent, atm.MeteorologicalRangeKm).Value;

        return Kr(altSDeg, heightMeters) * xR
               + kaVal * xA
               + KOz(altSDeg, sunRaDeg, latDeg) * xOz
               + Kw(heightMeters, atm.TemperatureCelsius, atm.RelativeHumidityPercent) * xW;
    }

    // --- default-parameter logic ---------------------------------------------

    /// <summary>
    /// Auto-fills pressure / temperature / RH from observer altitude when
    /// <see cref="AtmosphericConditions.PressureMbar"/> is non-positive, and
    /// applies <see cref="ObserverParameters"/> defaults (age 36, SN 1,
    /// naked-eye binocular). Returns the effective values; the inputs are not
    /// mutated. Optical fields are forced to defaults unless
    /// <see cref="HeliacalFlags.OpticalParameters"/> is set.
    /// </summary>
    public static (AtmosphericConditions Atm, ObserverParameters Obs) DefaultParameters(
        AtmosphericConditions atm, double observerAltMeters, ObserverParameters obs, HeliacalFlags helflag)
    {
        var pressure = atm.PressureMbar;
        var tempC = atm.TemperatureCelsius;
        var rh = atm.RelativeHumidityPercent;
        var vr = atm.MeteorologicalRangeKm;

        if (pressure <= 0)
        {
            pressure = 1013.25 * Math.Pow(1 - 0.0065 * observerAltMeters / 288.0, 5.255);
            if (tempC == 0) tempC = 15 - 0.0065 * observerAltMeters;
            if (rh == 0) rh = 40;
            // datm[3] / VR defaults outside this function in C; we leave it untouched.
        }
        // else: under SIMULATE_VICTORVB the C clamping at swehel.c#L1340-L1341 is
        // dead code, so RH flows through unchanged.

        var age = obs.AgeYears == 0 ? 36 : obs.AgeYears;
        var sn = obs.SnellenRatio == 0 ? 1 : obs.SnellenRatio;
        var bino = obs.IsBinocular;
        var optMag = obs.OpticMagnification;
        var optDia = obs.ApertureMm;
        var optTrans = obs.Transmission;

        if ((helflag & HeliacalFlags.OpticalParameters) == 0)
        {
            // Force fields 2..5 to defaults.
            bino = false;
            optMag = 0;
            optDia = 0;
            optTrans = 0;
        }

        // OpticMagn undefined → naked eye (binocular, magnification 1).
        if (optMag == 0)
        {
            bino = true;
            optMag = 1;
        }

        return (
            new AtmosphericConditions(pressure, tempC, rh, vr),
            new ObserverParameters(age, sn, bino, optMag, optDia, optTrans));
    }
}
