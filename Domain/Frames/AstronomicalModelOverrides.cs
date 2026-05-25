// Ported from swisseph-master/swephexp.h (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Domain.Frames;

/// <summary>
/// Long-term precession theory selector. Mirrors the
/// <c>SEMOD_PREC_*</c> macros at <c>swephexp.h#L510-L527</c>. Values match
/// the integer IDs from the C header so that callers reading the original
/// Astrodienst documentation can reuse the same identifiers.
/// </summary>
public enum PrecessionModel
{
    /// <summary>SEMOD_PREC_IAU_1976.</summary>
    Iau1976 = 1,

    /// <summary>SEMOD_PREC_LASKAR_1986.</summary>
    Laskar1986 = 2,

    /// <summary>SEMOD_PREC_WILL_EPS_LASK — Williams precession with Laskar obliquity.</summary>
    WilliamsEpsLaskar = 3,

    /// <summary>SEMOD_PREC_WILLIAMS_1994.</summary>
    Williams1994 = 4,

    /// <summary>SEMOD_PREC_SIMON_1994.</summary>
    Simon1994 = 5,

    /// <summary>SEMOD_PREC_IAU_2000.</summary>
    Iau2000 = 6,

    /// <summary>SEMOD_PREC_BRETAGNON_2003.</summary>
    Bretagnon2003 = 7,

    /// <summary>SEMOD_PREC_IAU_2006.</summary>
    Iau2006 = 8,

    /// <summary>
    /// SEMOD_PREC_VONDRAK_2011 — long-term Vondrák / Capitaine / Wallace 2011
    /// theory. C-library default for both short- and long-term precession.
    /// </summary>
    Vondrak2011 = 9,

    /// <summary>SEMOD_PREC_OWEN_1990.</summary>
    Owen1990 = 10,

    /// <summary>SEMOD_PREC_NEWCOMB.</summary>
    Newcomb = 11,
}

/// <summary>
/// Nutation theory selector. Mirrors <c>SEMOD_NUT_*</c> at
/// <c>swephexp.h#L530-L537</c>.
/// </summary>
public enum NutationModel
{
    /// <summary>SEMOD_NUT_IAU_1980.</summary>
    Iau1980 = 1,

    /// <summary>SEMOD_NUT_IAU_CORR_1987 — IAU 1980 plus Herring 1987 corrections.</summary>
    Iau1980Corrections1987 = 2,

    /// <summary>SEMOD_NUT_IAU_2000A — full IAU 2000A (luni-solar + planetary, ~1365 terms).</summary>
    Iau2000A = 3,

    /// <summary>
    /// SEMOD_NUT_IAU_2000B — abridged IAU 2000A (77 luni-solar terms only,
    /// milliarcsecond precision). C-library default.
    /// </summary>
    Iau2000B = 4,

    /// <summary>SEMOD_NUT_WOOLARD — Woolard 1953, an incomplete legacy model.</summary>
    Woolard = 5,
}

/// <summary>
/// Frame-bias selector. Mirrors <c>SEMOD_BIAS_*</c> at
/// <c>swephexp.h#L548-L553</c>. Bias is the GCRS ↔ FK5/J2000 alignment
/// rotation applied during apparent-position computation.
/// </summary>
public enum FrameBiasModel
{
    /// <summary>SEMOD_BIAS_NONE — ignore frame bias.</summary>
    None = 1,

    /// <summary>SEMOD_BIAS_IAU2000.</summary>
    Iau2000 = 2,

    /// <summary>SEMOD_BIAS_IAU2006 — current C-library default.</summary>
    Iau2006 = 3,
}

/// <summary>
/// Immutable bag of astronomical-model selectors (precession, nutation,
/// obliquity, frame bias, sidereal-time formula, JPL Horizons-mode
/// parameters). Replaces the C library's mutable
/// <c>swed.astro_models[NSE_MODELS]</c> + <c>swe_set_astro_models</c>:
/// every model choice is per-context, never global.
/// </summary>
/// <remarks>
/// Use <see cref="Default"/> to obtain the C-library defaults (Vondrák
/// 2011 long-term precession, IAU 2000B nutation, IAU 2006 frame bias).
/// Customise via <c>with</c> expressions:
/// <code>var x = AstronomicalModelOverrides.Default with { PrecessionLongTerm = PrecessionModel.Iau2006 };</code>
/// </remarks>
public sealed record AstronomicalModelOverrides
{
    /// <summary>Long-term precession theory used outside <see cref="ShortTermSwitchOverCenturies"/>.</summary>
    public PrecessionModel PrecessionLongTerm { get; init; } = PrecessionModel.Vondrak2011;

    /// <summary>Short-term precession theory used near J2000 (within <see cref="ShortTermSwitchOverCenturies"/>).</summary>
    public PrecessionModel PrecessionShortTerm { get; init; } = PrecessionModel.Vondrak2011;

    /// <summary>Half-width of the short-term band in Julian centuries from J2000 (mirrors <c>PREC_IAU_*_CTIES</c>).</summary>
    /// <remarks>
    /// Defaults to 0.0 because Vondrák itself is the long-term default; for
    /// IAU 1976 / IAU 2000 short-term the C library uses 2.0 centuries; for
    /// IAU 2006 it uses 75.0 centuries.
    /// </remarks>
    public double ShortTermSwitchOverCenturies { get; init; }

    /// <summary>Nutation theory.</summary>
    public NutationModel Nutation { get; init; } = NutationModel.Iau2000B;

    /// <summary>Frame-bias model.</summary>
    public FrameBiasModel FrameBias { get; init; } = FrameBiasModel.Iau2006;

    /// <summary>C-library defaults: Vondrák 2011 precession / IAU 2000B nutation / IAU 2006 bias.</summary>
    public static readonly AstronomicalModelOverrides Default = new();
}
