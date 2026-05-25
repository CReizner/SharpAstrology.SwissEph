// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Phenomena;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Application.Phenomena;

/// <summary>
/// Visual-limiting-magnitude math from swehel.c (Schaefer 1993). Pure helper —
/// the public-API entry point <c>swe_vis_limit_mag</c> sits in
/// <see cref="HeliacalService"/>; this class is the inner core.
/// </summary>
internal static class VisibilityLimitMath
{
    private const double ScotopicThreshold = HeliacalConstants.ScotopicThresholdVisLimMagn;
    private const double BNightRef = HeliacalConstants.BNightReferenceNL;
    private const double BNightFactor = HeliacalConstants.BNightFactor;

    /// <summary>
    /// Bit set on <see cref="VisibilityLimit.ScotopicFlag"/> when scotopic
    /// vision regime is selected. Mirrors the |1 in swehel.c#L1414.
    /// </summary>
    public const int FlagScotopic = 1;

    /// <summary>
    /// Bit set on <see cref="VisibilityLimit.ScotopicFlag"/> when the sky is
    /// near the photopic/scotopic boundary (within <see cref="HeliacalConstants.BNightFactor"/>
    /// of <see cref="HeliacalConstants.BNightReferenceNL"/>). Mirrors
    /// swehel.c#L1422-L1423.
    /// </summary>
    public const int FlagBoundary = 2;

    /// <summary>
    /// Visual limiting magnitude for a point source at the given line of
    /// sight. Mirrors <c>VisLimMagn</c> at swehel.c#L1382. Returns the
    /// magnitude and the scotopic-flag bitmask (<see cref="FlagScotopic"/> /
    /// <see cref="FlagBoundary"/>).
    /// </summary>
    public static (double Magnitude, int ScotopicFlag) Compute(
        ObserverParameters observer,
        double altODeg, double aziODeg, double altMDeg, double aziMDeg,
        JulianDay jdUt, double altSDeg, double aziSDeg, double sunRaDeg,
        double latDeg, double heightMeters,
        AtmosphericConditions atm,
        HeliacalFlags helFlags)
    {
        var bsk = SkyBrightnessModel.Bsky(altODeg, aziODeg, altMDeg, aziMDeg, jdUt,
                                          altSDeg, aziSDeg, sunRaDeg, latDeg, heightMeters,
                                          atm, useHighPrecision: (helFlags & HeliacalFlags.HighPrecision) != 0);
        var kX = AtmosphericModel.DeltaMagnitudes(altODeg, altSDeg, sunRaDeg, latDeg, heightMeters,
                                                   atm, useHighPrecision: (helFlags & HeliacalFlags.HighPrecision) != 0);

        var corrFactor1 = ObserverModel.OpticFactor(bsk, kX, observer, objectName: string.Empty,
                                                    typeFactor: 1, helFlags);
        var corrFactor2 = ObserverModel.OpticFactor(bsk, kX, observer, objectName: string.Empty,
                                                    typeFactor: 0, helFlags);

        var isScotopic = bsk < ScotopicThreshold;
        if ((helFlags & HeliacalFlags.VisLimPhotopic) != 0) isScotopic = false;
        if ((helFlags & HeliacalFlags.VisLimScotopic) != 0) isScotopic = true;

        double c1, c2;
        var scotopicFlag = 0;
        if (isScotopic)
        {
            c1 = 1.5848931924611e-10; // 10^(-9.8)
            c2 = 0.012589254117942;   // 10^(-1.9)
            scotopicFlag |= FlagScotopic;
        }
        else
        {
            c1 = 4.4668359215096e-9;  // 10^(-8.35)
            c2 = 1.2589254117942e-6;  // 10^(-5.9)
        }

        if (BNightRef * BNightFactor > bsk && BNightRef / BNightFactor < bsk)
            scotopicFlag |= FlagBoundary;

        bsk *= corrFactor1;
        var oneSqrt = 1.0 + Math.Sqrt(c2 * bsk);
        var th = c1 * oneSqrt * oneSqrt * corrFactor2;

        var magnitude = -16.57 - 2.5 * Math.Log10(th);
        return (magnitude, scotopicFlag);
    }
}
