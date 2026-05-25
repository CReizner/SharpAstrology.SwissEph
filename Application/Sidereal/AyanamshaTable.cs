// Ported from swisseph-master/sweph.h aya_init[] (lines 351-596).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Frames;

namespace SharpAstrology.SwissEphemerides.Application.Sidereal;

/// <summary>
/// Canonical preset table of ayanamshas. One entry per
/// <see cref="SiderealMode"/> value 0..46 (47 = SE_NSIDM_PREDEF).
/// Verbatim port of the C struct array <c>aya_init[]</c> at
/// <c>sweph.h#L351-L596</c>.
/// </summary>
internal static class AyanamshaTable
{
    /// <summary>Number of predefined modes (SE_NSIDM_PREDEF).</summary>
    public const int PredefinedCount = 47;

    // Reference epochs taken from sweph.h (J1900, J2000, B1950).
    private const double J1900 = 2_415_020.0;
    private const double J2000 = 2_451_545.0;
    private const double B1950 = 2_433_282.42345905;

    /// <summary>
    /// Indexed by integer cast of <see cref="SiderealMode"/> for predefined
    /// values 0..46. <see cref="SiderealMode.UserDefined"/> (255) is filled
    /// at runtime from the caller's arguments and is therefore not part of
    /// this table.
    /// </summary>
    public static readonly AyanamshaPreset[] Presets = new AyanamshaPreset[PredefinedCount]
    {
        // 0: Fagan/Bradley — sweph.h#L372.
        new(2433282.42346, 24.042044444, T0IsUt: false, PrecessionModel.Newcomb),
        // 1: Lahiri — sweph.h#L387.
        new(2435553.5, 23.250182778 - 0.004658035, T0IsUt: false, PrecessionModel.Iau1976),
        // 2: De Luce — sweph.h#L397.
        new(1721057.5, 0, T0IsUt: true, null),
        // 3: Raman — sweph.h#L404.
        new(J1900, 360 - 338.98556, T0IsUt: false, PrecessionModel.Newcomb),
        // 4: Usha/Shashi — sweph.h#L410. (prec_offset = -1 in C, treated as null.)
        new(J1900, 360 - 341.33904, T0IsUt: false, null),
        // 5: Krishnamurti — sweph.h#L422.
        new(J1900, 360 - 337.636111, T0IsUt: false, PrecessionModel.Newcomb),
        // 6: Djwhal Khool — sweph.h#L429.
        new(J1900, 360 - 333.0369024, T0IsUt: false, null),
        // 7: Sri Yukteshwar — sweph.h#L443. (prec_offset = -1.)
        new(J1900, 360 - 338.917778, T0IsUt: false, null),
        // 8: J. N. Bhasin — sweph.h#L448. (prec_offset = -1.)
        new(J1900, 360 - 338.634444, T0IsUt: false, null),
        // 9: Babylonian / Kugler 1 — sweph.h#L453.
        new(1684532.5, -5.66667, T0IsUt: true, null),
        // 10: Babylonian / Kugler 2 — sweph.h#L454.
        new(1684532.5, -4.26667, T0IsUt: true, null),
        // 11: Babylonian / Kugler 3 — sweph.h#L455.
        new(1684532.5, -3.41667, T0IsUt: true, null),
        // 12: Babylonian / Huber — sweph.h#L461.
        new(1684532.5, -4.46667, T0IsUt: true, null),
        // 13: Babylonian / Mercier (eta Piscium) — sweph.h#L464.
        new(1673941, -5.079167, T0IsUt: true, null),
        // 14: Babylonian / Aldebaran = 15 Tau — sweph.h#L467.
        new(1684532.5, -4.44138598, T0IsUt: true, null),
        // 15: Hipparchos — sweph.h#L470.
        new(1674484.0, -9.33333, T0IsUt: true, null),
        // 16: Sassanian — sweph.h#L473.
        new(1927135.8747793, 0, T0IsUt: true, null),
        // 17: Galactic Centre at 0 Sagittarius (star-based) — sweph.h#L476.
        new(0, 0, T0IsUt: false, null),
        // 18: J2000 — sweph.h#L479.
        new(J2000, 0, T0IsUt: false, null),
        // 19: J1900 — sweph.h#L482.
        new(J1900, 0, T0IsUt: false, null),
        // 20: B1950 — sweph.h#L485.
        new(B1950, 0, T0IsUt: false, null),
        // 21: Suryasiddhanta — sweph.h#L490.
        new(1903396.8128654, 0, T0IsUt: true, null),
        // 22: Suryasiddhanta, mean Sun — sweph.h#L494.
        new(1903396.8128654, -0.21463395, T0IsUt: true, null),
        // 23: Aryabhata — sweph.h#L497.
        new(1903396.7895321, 0, T0IsUt: true, null),
        // 24: Aryabhata, mean Sun — sweph.h#L500.
        new(1903396.7895321, -0.23763238, T0IsUt: true, null),
        // 25: SS Revati — sweph.h#L503.
        new(1903396.8128654, -0.79167046, T0IsUt: true, null),
        // 26: SS Citra — sweph.h#L506.
        new(1903396.8128654, 2.11070444, T0IsUt: true, null),
        // 27: True Citra (star-based) — sweph.h#L509.
        new(0, 0, T0IsUt: false, null),
        // 28: True Revati (star-based) — sweph.h#L512.
        new(0, 0, T0IsUt: false, null),
        // 29: True Pushya (star-based) — sweph.h#L515.
        new(0, 0, T0IsUt: false, null),
        // 30: Galactic Centre Gil Brand (star-based) — sweph.h#L519.
        new(0, 0, T0IsUt: false, null),
        // 31: Galactic Equator IAU 1958 (star-based) — sweph.h#L523.
        new(0, 0, T0IsUt: false, null),
        // 32: Galactic Equator true (star-based) — sweph.h#L528.
        new(0, 0, T0IsUt: false, null),
        // 33: Galactic Equator mid-Mula (star-based) — sweph.h#L532.
        new(0, 0, T0IsUt: false, null),
        // 34: Skydram / Mardyks — sweph.h#L536.
        new(2451079.734892000, 30, T0IsUt: false, null),
        // 35: True Mula / Chandra Hari (star-based) — sweph.h#L539.
        new(0, 0, T0IsUt: false, null),
        // 36: Dhruva / Galactic Centre / Mid-Mula — Wilhelm (star-based) — sweph.h#L542.
        new(0, 0, T0IsUt: false, null),
        // 37: Aryabhata 522 (Kali 3623, Ujjain) — sweph.h#L546.
        new(1911797.740782065, 0, T0IsUt: true, null),
        // 38: Babylonian / Britton 2010 — sweph.h#L552. (prec_offset = -1.)
        new(1721057.5, -3.2, T0IsUt: true, null),
        // 39: "Vedic" / Sheoran (star-based) — sweph.h#L556.
        new(0, 0, T0IsUt: false, null),
        // 40: Cochrane (Galactic Centre at 0 Capricorn, star-based) — sweph.h#L559.
        new(0, 0, T0IsUt: false, null),
        // 41: "Galactic Equatorial" / Fiorenza — sweph.h#L562.
        new(2451544.5, 25.0, T0IsUt: true, null),
        // 42: Vettius Valens (Moon, derived from Holden 1995, 1 Jan 150 CE julian) — sweph.h#L566. (prec_offset = -1.)
        new(1775845.5, -2.9422, T0IsUt: true, null),
        // 43: Lahiri 1940 — sweph.h#L570.
        new(J1900, 22.44597222, T0IsUt: false, PrecessionModel.Newcomb),
        // 44: Lahiri VP285 — sweph.h#L575.
        new(1825235.2458513028, 0.0, T0IsUt: false, null),
        // 45: Krishnamurti VP291 — sweph.h#L582.
        new(1827424.752255678, 0.0, T0IsUt: false, null),
        // 46: Lahiri ICRC — sweph.h#L594.
        new(2435553.5, 23.25 - 0.00464207, T0IsUt: false, PrecessionModel.Newcomb),
    };

    /// <summary>
    /// Display names from <c>swe_get_ayanamsa_name</c>. Mirrors the static
    /// <c>ayanamsa_name[]</c> array at <c>sweph.c#L130-L177</c>.
    /// </summary>
    public static readonly string[] Names = new string[PredefinedCount]
    {
        "Fagan/Bradley",
        "Lahiri",
        "De Luce",
        "Raman",
        "Usha/Shashi",
        "Krishnamurti",
        "Djwhal Khul",
        "Yukteshwar",
        "J.N. Bhasin",
        "Babylonian/Kugler 1",
        "Babylonian/Kugler 2",
        "Babylonian/Kugler 3",
        "Babylonian/Huber",
        "Babylonian/Eta Piscium",
        "Babylonian/Aldebaran = 15 Tau",
        "Hipparchos",
        "Sassanian",
        "Galact. Center = 0 Sag",
        "J2000",
        "J1900",
        "B1950",
        "Suryasiddhanta",
        "Suryasiddhanta, mean Sun",
        "Aryabhata",
        "Aryabhata, mean Sun",
        "SS Revati",
        "SS Citra",
        "True Citra",
        "True Revati",
        "True Pushya (PVRN Rao)",
        "Galactic Center (Gil Brand)",
        "Galactic Equator (IAU1958)",
        "Galactic Equator",
        "Galactic Equator mid-Mula",
        "Skydram (Mardyks)",
        "True Mula (Chandra Hari)",
        "Dhruva/Gal.Center/Mula (Wilhelm)",
        "Aryabhata 522",
        "Babylonian/Britton",
        "\"Vedic\"/Sheoran",
        "Cochrane (Gal.Center = 0 Cap)",
        "Galactic Equator (Fiorenza)",
        "Vettius Valens",
        "Lahiri 1940",
        "Lahiri VP285",
        "Krishnamurti-Senthilathiban",
        "Lahiri ICRC",
    };

    /// <summary>
    /// Reports whether a mode is anchored to a fixed star or the galactic
    /// centre / equator and therefore requires the fixed-star catalog
    /// (the "True *" family + Galactic Centre / Equator variants). Modes
    /// for which this returns <see langword="true"/> have a populated
    /// row in <see cref="StarAnchoredPresets"/>.
    /// </summary>
    /// <param name="mode">The sidereal mode to test.</param>
    /// <returns><see langword="true"/> if a fixed-star source is needed.</returns>
    public static bool RequiresFixedStarSource(SiderealMode mode) => mode switch
    {
        SiderealMode.GalacticCenter0Sag => true,
        SiderealMode.TrueCitra => true,
        SiderealMode.TrueRevati => true,
        SiderealMode.TruePushya => true,
        SiderealMode.GalacticCenterGilBrand => true,
        SiderealMode.GalacticEquatorIau1958 => true,
        SiderealMode.GalacticEquatorTrue => true,
        SiderealMode.GalacticEquatorMula => true,
        SiderealMode.TrueMulaChandraHari => true,
        SiderealMode.GalacticCenterMidMulaWilhelm => true,
        SiderealMode.VedicSheoran => true,
        SiderealMode.CochraneGalacticCenter => true,
        _ => false,
    };

    /// <summary>
    /// Resolves the per-mode anchor data for a star-based ayanamsha.
    /// Mirrors the literal star names and constants embedded in the
    /// per-mode <c>if</c>-branches of <c>swi_get_ayanamsa_ex</c> at
    /// sweph.c#L3050-L3149.
    /// </summary>
    /// <remarks>
    /// The <c>0.3819660113</c> factor in
    /// <see cref="SiderealMode.GalacticCenterGilBrand"/> is the golden
    /// section <c>(3 − √5)/2</c>; the <c>6.6666666667°</c> offset in
    /// <see cref="SiderealMode.GalacticEquatorMula"/> is half of one
    /// 13°20′ lunar mansion. Both are reproduced verbatim from the C
    /// source; the small mantissa rounding is intentional for
    /// bit-for-bit C parity.
    /// </remarks>
    public static bool TryGetStarAnchoredPreset(SiderealMode mode, out StarAnchoredPreset preset)
    {
        preset = mode switch
        {
            // sweph.c#L3050: True Citra (Spica exactly at 0 Libra).
            SiderealMode.TrueCitra => new("Spica", 180.0, StarAnchoredKind.Ecliptic),
            // sweph.c#L3058: True Revati (zeta Psc exactly at 29°50' Pisces).
            SiderealMode.TrueRevati => new(",zePsc", 359.8333333333, StarAnchoredKind.Ecliptic),
            // sweph.c#L3065: True Pushya (delta Cnc at 16 Cancer).
            SiderealMode.TruePushya => new(",deCnc", 106.0, StarAnchoredKind.Ecliptic),
            // sweph.c#L3072: True Sheoran (Asellus Australis = delta Cnc).
            SiderealMode.VedicSheoran => new(",deCnc", 103.49264221625, StarAnchoredKind.Ecliptic),
            // sweph.c#L3079: True Mula (lambda Sco = Mula).
            SiderealMode.TrueMulaChandraHari => new(",laSco", 240.0, StarAnchoredKind.Ecliptic),
            // sweph.c#L3086: Galactic Centre at 0 Sag (Sgr A*).
            SiderealMode.GalacticCenter0Sag => new(",SgrA*", 240.0, StarAnchoredKind.Ecliptic),
            // sweph.c#L3094: Galactic Centre Cochrane (Sgr A* at 0 Cap).
            SiderealMode.CochraneGalacticCenter => new(",SgrA*", 270.0, StarAnchoredKind.Ecliptic),
            // sweph.c#L3102: Galactic Centre Gil Brand (Sgr A* at golden section between
            // 0 Sco and 0 Aqu — 210° + 90°·((3−√5)/2)).
            SiderealMode.GalacticCenterGilBrand => new(",SgrA*", 210.0 + 90.0 * 0.3819660113, StarAnchoredKind.Ecliptic),
            // sweph.c#L3110: Galactic Centre mid-Mula Wilhelm — RA via armc_to_mc, then
            // subtract 246.6666666667°.
            SiderealMode.GalacticCenterMidMulaWilhelm => new(",SgrA*", 246.6666666667, StarAnchoredKind.EquatorialArmcToMc),
            // sweph.c#L3122: Galactic Equator IAU 1958 (galactic pole 1958 at 150°).
            SiderealMode.GalacticEquatorIau1958 => new(",GP1958", 150.0, StarAnchoredKind.EclipticTruePosition),
            // sweph.c#L3129: Galactic Equator True (modern galactic pole at 150°).
            SiderealMode.GalacticEquatorTrue => new(",GPol", 150.0, StarAnchoredKind.EclipticTruePosition),
            // sweph.c#L3136: Galactic Equator Mula (modern galactic pole + 6°40′).
            SiderealMode.GalacticEquatorMula => new(",GPol", 150.0 + 6.6666666667, StarAnchoredKind.EclipticTruePosition),
            _ => default,
        };
        return preset.StarName is not null;
    }
}
