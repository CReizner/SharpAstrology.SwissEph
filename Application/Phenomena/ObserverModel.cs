// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Reference driver: /tmp/heliacal_a_ref.c — covers PupilDia, CVA and the four
// branches of OpticFactor (eye/telescope × intensity/background) in both
// photopic and scotopic vision regimes.

using System;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Observer-physiology helpers from swehel.c (Garstang 2000 pupil model,
/// Schaefer 1993 critical visual angle, optic factor). All functions are
/// stateless; the upstream C library has no caches here.
/// </summary>
internal static class ObserverModel
{
    private const double ScotopicCva = HeliacalConstants.ScotopicThresholdCva;
    private const double ScotopicOptic = HeliacalConstants.ScotopicThresholdOpticFactor;
    private const double RefAge = HeliacalConstants.PupilReferenceAgeYears;

    /// <summary>
    /// Pupil diameter in mm given observer age and background brightness
    /// (nL). Mirrors <c>PupilDia</c> at swehel.c#L202 (Garstang 2000).
    /// </summary>
    public static double PupilDiaMm(double ageYears, double brightnessNL)
        => (0.534 - 0.00211 * ageYears
            - (0.236 - 0.00127 * ageYears) * Math.Tanh(0.4 * Math.Log10(brightnessNL) - 2.2))
           * 10.0;

    /// <summary>
    /// Critical visual angle (degrees). Mirrors <c>CVA</c> at swehel.c#L180.
    /// Below the brightness threshold (or when
    /// <see cref="HeliacalFlags.VisLimScotopic"/> is set) the scotopic branch
    /// applies and the result is capped at <c>900 / 3600</c> deg.
    /// </summary>
    public static double CriticalVisualAngleDeg(double brightnessNL, double snellenRatio, HeliacalFlags helFlags)
    {
        var isScotopic = brightnessNL < ScotopicCva;
        if ((helFlags & HeliacalFlags.VisLimPhotopic) != 0) isScotopic = false;
        if ((helFlags & HeliacalFlags.VisLimScotopic) != 0) isScotopic = true;

        if (isScotopic)
        {
            var v = 380.0 / snellenRatio * Math.Pow(10, 0.3 * Math.Pow(brightnessNL, -0.29));
            if (v > 900) v = 900;
            return v / 60.0 / 60.0;
        }

        return 40.0 / snellenRatio
               * Math.Pow(10, 8.28 * Math.Pow(brightnessNL, -0.29))
               / 60.0 / 60.0;
    }

    /// <summary>
    /// Schaefer's optic factor (intensity vs. background) for the
    /// visibility-limit equation. Mirrors <c>OpticFactor</c> at
    /// swehel.c#L224. <paramref name="typeFactor"/> = 0 returns the
    /// intensity factor, 1 the background factor.
    ///
    /// The C source treats <c>OpticMag == 1</c> specially (forces
    /// <c>OpticTrans = 1</c>, <c>OpticDia = Pst</c>); this port does the same
    /// on local copies, so the caller's <see cref="ObserverParameters"/> is
    /// untouched.
    ///
    /// <paramref name="objectName"/> is currently dead-coded in the C source
    /// (only "moon" is matched and the body of that branch is empty); we keep
    /// the parameter so that future ports of the moon-size logic don't break
    /// the call site.
    /// </summary>
    public static double OpticFactor(
        double backgroundBrightnessNL,
        double extinctionMagnitudes,
        ObserverParameters observer,
        string objectName,
        int typeFactor,
        HeliacalFlags helFlags)
    {
        var snI = observer.SnellenRatio;
        if (snI <= 0.00000001) snI = 0.00000001;

        // Reference pupil diameter at age 23 (Garstang's standard).
        var pst = PupilDiaMm(RefAge, backgroundBrightnessNL);

        var binocular = observer.IsBinocular ? 1.0 : 0.0;
        var opticMag = observer.OpticMagnification;
        var opticDia = observer.ApertureMm;
        var opticTrans = observer.Transmission;

        // Eye case: OpticMag == 1 forces OpticTrans = 1 and OpticDia = Pst
        // (swehel.c#L239-L242).
        if (opticMag == 1)
        {
            opticTrans = 1;
            opticDia = pst;
        }

        // Schaefer 1993 colour indices (objectName-specific moon override is
        // dead in the C source; ObjectSize stays at 0).
        const double cIb = 0.7;
        const double cIi = 0.5;
        const double objectSize = 0.0;
        _ = objectName; // reserved for future moon-size handling

        var fb = binocular == 0 ? 1.41 : 1.0;

        var isScotopic = backgroundBrightnessNL < ScotopicOptic;
        if ((helFlags & HeliacalFlags.VisLimPhotopic) != 0) isScotopic = false;
        if ((helFlags & HeliacalFlags.VisLimScotopic) != 0) isScotopic = true;

        double fe, fsc, fci, fcb;
        if (isScotopic)
        {
            fe = Math.Pow(10, 0.48 * extinctionMagnitudes);
            var num = 1 - Math.Pow(pst / 124.4, 4);
            var den = 1 - Math.Pow(opticDia / opticMag / 124.4, 4);
            fsc = num / den;
            if (fsc > 1) fsc = 1;
            fci = Math.Pow(10, -0.4 * (1 - cIi / 2.0));
            fcb = Math.Pow(10, -0.4 * (1 - cIb / 2.0));
        }
        else
        {
            fe = Math.Pow(10, 0.4 * extinctionMagnitudes);
            var ratio = opticDia / opticMag;
            var num = Math.Pow(ratio / pst, 2)
                      * (1 - Math.Exp(-Math.Pow(pst / 6.2, 2)));
            var den = 1 - Math.Exp(-Math.Pow(ratio / 6.2, 2));
            fsc = num / den;
            if (fsc > 1) fsc = 1;
            fci = 1;
            fcb = 1;
        }

        var ft = 1.0 / opticTrans;
        var fpRatio = pst / (opticMag * PupilDiaMm(observer.AgeYears, backgroundBrightnessNL));
        var fp = fpRatio * fpRatio;
        if (fp < 1) fp = 1;
        var fa = Math.Pow(pst / opticDia, 2);
        var fr = (1 + 0.03 * Math.Pow(opticMag * objectSize / CriticalVisualAngleDeg(backgroundBrightnessNL, snI, helFlags), 2))
                 / (snI * snI);
        var fm = opticMag * opticMag;

        return typeFactor == 0
            ? fb * fe * ft * fp * fa * fr * fsc * fci
            : fb * ft * fp * fa * fm * fsc * fcb;
    }
}
