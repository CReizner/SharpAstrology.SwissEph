// Ported from swisseph-master/swephexp.h SE_SIDBIT_* macros (lines 221-235).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Application.Sidereal;

/// <summary>
/// Bit flags that modify the ayanamsha computation. Mirrors the C-side
/// <c>SE_SIDBIT_*</c> constants (swephexp.h#L221-L235).
/// </summary>
[Flags]
public enum SiderealFlags
{
    /// <summary>No flags set — default behaviour.</summary>
    None = 0,

    /// <summary>SE_SIDBIT_ECL_T0 — project planetary positions onto the ecliptic at T0.</summary>
    EclipticOfT0 = 256,

    /// <summary>SE_SIDBIT_SSY_PLANE — project planetary positions onto the solar system plane.</summary>
    SolarSystemPlane = 512,

    /// <summary>SE_SIDBIT_USER_UT — for <see cref="SiderealMode.UserDefined"/>, T0 is UT instead of TT.</summary>
    UserT0IsUt = 1024,

    /// <summary>SE_SIDBIT_ECL_DATE — measure ayanamsha on the ecliptic of date (alternative algorithm).</summary>
    EclipticOfDate = 2048,

    /// <summary>SE_SIDBIT_NO_PREC_OFFSET — disable the precession-model offset correction.</summary>
    NoPrecessionOffset = 4096,

    /// <summary>SE_SIDBIT_PREC_ORIG — compute ayanamsha using its original precession model.</summary>
    PrecessionOriginal = 8192,
}
