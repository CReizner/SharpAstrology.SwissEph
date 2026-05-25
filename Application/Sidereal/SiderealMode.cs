// Ported from swisseph-master/swephexp.h SE_SIDM_* macros (lines 238-288).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Application.Sidereal;

/// <summary>
/// Predefined ayanamshas. Mirrors the C-side <c>SE_SIDM_*</c> integer
/// constants (swephexp.h#L238-L288). Numeric values are kept identical
/// to the C macros so callers reading the original Astrodienst
/// documentation can reuse the same identifiers.
/// </summary>
/// <remarks>
/// The 47 predefined modes are stored in <see cref="AyanamshaTable"/>;
/// <see cref="UserDefined"/> (255) is filled at runtime via
/// <c>SiderealService.SetMode(UserDefined, t0, ayanT0, ...)</c>.
/// </remarks>
public enum SiderealMode
{
    /// <summary>SE_SIDM_FAGAN_BRADLEY — default Fagan/Bradley ayanamsha (1950.0 epoch).</summary>
    FaganBradley = 0,

    /// <summary>SE_SIDM_LAHIRI — standard Lahiri (Calendar Reform Committee 1956).</summary>
    Lahiri = 1,

    /// <summary>SE_SIDM_DELUCE — Robert DeLuce (Constellational Astrology, 1 Jan 1 BC).</summary>
    DeLuce = 2,

    /// <summary>SE_SIDM_RAMAN — B. V. Raman (Hindu Predictive Astrology, 1938).</summary>
    Raman = 3,

    /// <summary>SE_SIDM_USHASHASHI — Usha/Shashi (1978).</summary>
    UshaShashi = 4,

    /// <summary>SE_SIDM_KRISHNAMURTI — K. S. Krishnamurti.</summary>
    Krishnamurti = 5,

    /// <summary>SE_SIDM_DJWHAL_KHUL — Djwhal Khul (Aquarius ingress 2117).</summary>
    DjwhalKhul = 6,

    /// <summary>SE_SIDM_YUKTESHWAR — Sri Yukteshwar (1920).</summary>
    Yukteshwar = 7,

    /// <summary>SE_SIDM_JN_BHASIN — J.N. Bhasin.</summary>
    JNBhasin = 8,

    /// <summary>SE_SIDM_BABYL_KUGLER1 — Babylonian / Kugler 1.</summary>
    BabylonianKugler1 = 9,

    /// <summary>SE_SIDM_BABYL_KUGLER2 — Babylonian / Kugler 2.</summary>
    BabylonianKugler2 = 10,

    /// <summary>SE_SIDM_BABYL_KUGLER3 — Babylonian / Kugler 3.</summary>
    BabylonianKugler3 = 11,

    /// <summary>SE_SIDM_BABYL_HUBER — Babylonian / Huber (Centaurus 1958).</summary>
    BabylonianHuber = 12,

    /// <summary>SE_SIDM_BABYL_ETPSC — Babylonian / Mercier (eta Piscium).</summary>
    BabylonianEtaPiscium = 13,

    /// <summary>SE_SIDM_ALDEBARAN_15TAU — Babylonian, Aldebaran at 15° Taurus.</summary>
    AldebaranAt15Taurus = 14,

    /// <summary>SE_SIDM_HIPPARCHOS — Hipparchos.</summary>
    Hipparchos = 15,

    /// <summary>SE_SIDM_SASSANIAN — Sassanian.</summary>
    Sassanian = 16,

    /// <summary>SE_SIDM_GALCENT_0SAG — Galactic Centre at 0° Sagittarius (star-based).</summary>
    GalacticCenter0Sag = 17,

    /// <summary>SE_SIDM_J2000 — J2000.</summary>
    J2000 = 18,

    /// <summary>SE_SIDM_J1900 — J1900.</summary>
    J1900 = 19,

    /// <summary>SE_SIDM_B1950 — B1950.</summary>
    B1950 = 20,

    /// <summary>SE_SIDM_SURYASIDDHANTA — Suryasiddhanta (mean equinox at Aries).</summary>
    Suryasiddhanta = 21,

    /// <summary>SE_SIDM_SURYASIDDHANTA_MSUN — Suryasiddhanta, mean Sun.</summary>
    SuryasiddhantaMeanSun = 22,

    /// <summary>SE_SIDM_ARYABHATA — Aryabhata.</summary>
    Aryabhata = 23,

    /// <summary>SE_SIDM_ARYABHATA_MSUN — Aryabhata, mean Sun.</summary>
    AryabhataMeanSun = 24,

    /// <summary>SE_SIDM_SS_REVATI — SS Revati / zeta Psc at polar long. 359°50'.</summary>
    SsRevati = 25,

    /// <summary>SE_SIDM_SS_CITRA — SS Citra / Spica at polar long. 180°.</summary>
    SsCitra = 26,

    /// <summary>SE_SIDM_TRUE_CITRA — Spica exactly at 0° Libra (star-based).</summary>
    TrueCitra = 27,

    /// <summary>SE_SIDM_TRUE_REVATI — zeta Psc exactly at 29°50' Pisces (star-based).</summary>
    TrueRevati = 28,

    /// <summary>SE_SIDM_TRUE_PUSHYA — delta Cnc exactly at 16° Cancer (star-based).</summary>
    TruePushya = 29,

    /// <summary>SE_SIDM_GALCENT_RGILBRAND — R. Gil Brand Galactic Centre (star-based).</summary>
    GalacticCenterGilBrand = 30,

    /// <summary>SE_SIDM_GALEQU_IAU1958 — Galactic Equator IAU 1958 (star-based).</summary>
    GalacticEquatorIau1958 = 31,

    /// <summary>SE_SIDM_GALEQU_TRUE — Galactic Equator true (star-based).</summary>
    GalacticEquatorTrue = 32,

    /// <summary>SE_SIDM_GALEQU_MULA — Galactic Equator mid-Mula (star-based).</summary>
    GalacticEquatorMula = 33,

    /// <summary>SE_SIDM_GALALIGN_MARDYKS — Skydram / Galactic Alignment (R. Mardyks).</summary>
    SkydramMardyks = 34,

    /// <summary>SE_SIDM_TRUE_MULA — True Mula / lambda Sco (star-based, Chandra Hari).</summary>
    TrueMulaChandraHari = 35,

    /// <summary>SE_SIDM_GALCENT_MULA_WILHELM — Dhruva / Galactic Centre / Mid-Mula (Wilhelm, star-based).</summary>
    GalacticCenterMidMulaWilhelm = 36,

    /// <summary>SE_SIDM_ARYABHATA_522 — Aryabhata 522 / Kali 3623, Ujjain.</summary>
    Aryabhata522 = 37,

    /// <summary>SE_SIDM_BABYL_BRITTON — Babylonian (Britton 2010).</summary>
    BabylonianBritton = 38,

    /// <summary>SE_SIDM_TRUE_SHEORAN — "Vedic" / Sunil Sheoran (star-based).</summary>
    VedicSheoran = 39,

    /// <summary>SE_SIDM_GALCENT_COCHRANE — Galactic Centre at 0° Capricorn (Cochrane, star-based).</summary>
    CochraneGalacticCenter = 40,

    /// <summary>SE_SIDM_GALEQU_FIORENZA — "Galactic Equatorial" (N. A. Fiorenza).</summary>
    GalacticEquatorFiorenza = 41,

    /// <summary>SE_SIDM_VALENS_MOON — Vettius Valens (Moon, Holden 1995).</summary>
    VettiusValens = 42,

    /// <summary>SE_SIDM_LAHIRI_1940 — Lahiri (1940), Panchanga darpan.</summary>
    Lahiri1940 = 43,

    /// <summary>SE_SIDM_LAHIRI_VP285 — Lahiri VP285 (mean sun at 360° in 285 CE).</summary>
    LahiriVp285 = 44,

    /// <summary>SE_SIDM_KRISHNAMURTI_VP291 — Krishnamurti from mean equinox 291 (Senthilathiban).</summary>
    KrishnamurtiVp291 = 45,

    /// <summary>SE_SIDM_LAHIRI_ICRC — Lahiri ICRC (Calendar Reform Committee 1956, no IAE 1985 correction).</summary>
    LahiriIcrc = 46,

    /// <summary>SE_SIDM_USER — User-defined ayanamsha; T0 is TT unless <see cref="SiderealFlags.UserT0IsUt"/> is set.</summary>
    UserDefined = 255,
}
