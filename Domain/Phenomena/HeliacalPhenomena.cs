// Ported from swisseph-master/swehel.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Phenomena;

/// <summary>
/// 28-value heliacal phenomena snapshot returned by
/// <c>swe_heliacal_pheno_ut</c> (swehel.c#L1862-L2073). Indexed names follow
/// the <c>darr[0..27]</c> assignments at the bottom of the C function.
/// Julian-day fields adopt <see cref="HeliacalConstants.TjdInvalid"/>
/// (= 99999999) when the underlying calculation returned the upstream
/// <c>TJD_INVALID</c> sentinel ("not applicable" / "no rise"); raw output
/// is preserved verbatim — clients can compare against the constant.
/// </summary>
/// <param name="ObjectTopocentricAltitudeDeg">Topocentric object altitude (deg). darr[0] = AltO.</param>
/// <param name="ObjectApparentAltitudeDeg">Apparent (refracted) object altitude (deg). darr[1] = AppAltO.</param>
/// <param name="ObjectGeocentricAltitudeDeg">Geocentric object altitude (deg) — ignores parallax. darr[2] = GeoAltO.</param>
/// <param name="ObjectAzimuthDeg">Object azimuth (deg, north = 0). darr[3] = AziO.</param>
/// <param name="SunTopocentricAltitudeDeg">Topocentric sun altitude (deg). darr[4] = AltS.</param>
/// <param name="SunAzimuthDeg">Sun azimuth (deg, north = 0). darr[5] = AziS.</param>
/// <param name="ActualArcVisionisAltitudeDeg">Vertical arc-visionis at <c>jdUt</c>: <c>AltO − AltS</c>. darr[6] = TAVact.</param>
/// <param name="ActualArcVisionisDeg">Geocentric arc visionis: <c>TAVact + ParO</c>. darr[7] = ARCVact.</param>
/// <param name="DeltaAzimuthDeg">Sun−object azimuth difference (deg). darr[8] = DAZact.</param>
/// <param name="ArcLightDeg">Geocentric arc light (object–sun separation, deg). darr[9] = ARCLact.</param>
/// <param name="ExtinctionCoefficient">Total atmospheric extinction coefficient k (Schaefer 1993). darr[10] = kact.</param>
/// <param name="MinimumArcVisionisDeg">Minimum arc visionis from the local-minimum walkthrough (deg). darr[11] = MinTAV.</param>
/// <param name="FirstVisibilityJdUt">JD UT of first visibility in walkthrough — <see cref="HeliacalConstants.TjdInvalid"/> if N/A. darr[12] = TfirstVR.</param>
/// <param name="BestVisibilityJdUt">JD UT of optimum visibility ("birth" point) — <see cref="HeliacalConstants.TjdInvalid"/> if N/A. darr[13] = TbVR.</param>
/// <param name="LastVisibilityJdUt">JD UT of last visibility in walkthrough — <see cref="HeliacalConstants.TjdInvalid"/> if N/A. darr[14] = TlastVR.</param>
/// <param name="YallopBestJdUt">Yallop "best time" JD (lunar only): <c>(4·RiseSetO + 5·RiseSetS) / 9</c>. darr[15] = TbYallop.</param>
/// <param name="MoonCrescentWidthDeg">Yallop crescent width [deg] (lunar only, else 0). darr[16] = WMoon.</param>
/// <param name="MoonYallopQ">Yallop visibility quality factor q (lunar only, else 0). darr[17] = qYal.</param>
/// <param name="MoonYallopCriterion">Yallop visibility class A..F (1..6) for q (lunar only, else 0). darr[18] = qCrit.</param>
/// <param name="ParallaxObjectDeg">Parallax in altitude: <c>GeoAltO − AltO</c>. darr[19] = ParO.</param>
/// <param name="ObjectMagnitude">Apparent magnitude of the object. darr[20] = MagnO.</param>
/// <param name="ObjectRiseSetJdUt">Rise/set JD UT of the object (cf. <c>TypeEvent</c>). darr[21] = RiseSetO.</param>
/// <param name="SunRiseSetJdUt">Rise/set JD UT of the sun (cf. <c>TypeEvent</c>). darr[22] = RiseSetS.</param>
/// <param name="LagDays">Object lag relative to sun (days). darr[23] = Lag.</param>
/// <param name="VisibilityWindowDays">Walkthrough visibility window length: <c>TlastVR − TfirstVR</c> — <see cref="HeliacalConstants.TjdInvalid"/> when both are missing. darr[24] = TvisVR.</param>
/// <param name="MoonCrescentLengthDeg">Sultan (2005) crescent length [deg] (lunar only, else 0). darr[25] = LMoon.</param>
/// <param name="ElongationDeg">Sun–object elongation (deg) — equals <see cref="ArcLightDeg"/> for fixed stars. darr[26] = elong.</param>
/// <param name="IlluminatedFractionPercent">Object illuminated fraction (%, 0–100). darr[27] = illum.</param>
public readonly record struct HeliacalPhenomena(
    double ObjectTopocentricAltitudeDeg,
    double ObjectApparentAltitudeDeg,
    double ObjectGeocentricAltitudeDeg,
    double ObjectAzimuthDeg,
    double SunTopocentricAltitudeDeg,
    double SunAzimuthDeg,
    double ActualArcVisionisAltitudeDeg,
    double ActualArcVisionisDeg,
    double DeltaAzimuthDeg,
    double ArcLightDeg,
    double ExtinctionCoefficient,
    double MinimumArcVisionisDeg,
    double FirstVisibilityJdUt,
    double BestVisibilityJdUt,
    double LastVisibilityJdUt,
    double YallopBestJdUt,
    double MoonCrescentWidthDeg,
    double MoonYallopQ,
    double MoonYallopCriterion,
    double ParallaxObjectDeg,
    double ObjectMagnitude,
    double ObjectRiseSetJdUt,
    double SunRiseSetJdUt,
    double LagDays,
    double VisibilityWindowDays,
    double MoonCrescentLengthDeg,
    double ElongationDeg,
    double IlluminatedFractionPercent);
