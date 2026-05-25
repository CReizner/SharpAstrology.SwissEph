// Original license: see LICENSE.SwissEph.txt at the repo root.

namespace SharpAstrology.SwissEphemerides.Application.Houses;

/// <summary>
/// House-system selector. Numeric value matches the single-letter code used
/// by the Swiss Ephemeris C API (<c>swe_houses(... int hsys ...)</c>) — see
/// <c>swe_house_name</c> at swehouse.c#L827 for the canonical mapping.
/// Lower-case <c>i</c> (Sunshine alternative) is preserved verbatim because
/// the C library treats it as a distinct system.
/// </summary>
public enum HouseSystem : byte
{
    /// <summary>'A' / 'E' — equal houses; 1st cusp = ascendant, 30° each.</summary>
    Equal = (byte)'A',

    /// <summary>'B' — Alcabitius semi-arc.</summary>
    Alcabitius = (byte)'B',

    /// <summary>'C' — Campanus.</summary>
    Campanus = (byte)'C',

    /// <summary>'D' — equal houses, 1st cusp = MC − 90°.</summary>
    EqualMc = (byte)'D',

    /// <summary>'F' — Carter's poli-equatorial.</summary>
    CarterPoliEquatorial = (byte)'F',

    /// <summary>'G' — 36 Gauquelin sectors.</summary>
    Gauquelin = (byte)'G',

    /// <summary>'H' — horizon / azimuth.</summary>
    Horizon = (byte)'H',

    /// <summary>'I' — Sunshine houses (Treindl solution).</summary>
    SunshineTreindl = (byte)'I',

    /// <summary>'i' — Sunshine houses (Makransky solution). Distinct from 'I'.</summary>
    SunshineMakransky = (byte)'i',

    /// <summary>'J' — Savard-A (supposed Albategnius).</summary>
    SavardA = (byte)'J',

    /// <summary>'K' — Koch.</summary>
    Koch = (byte)'K',

    /// <summary>'L' — Pullen SD ("sinusoidal delta", ex Neo-Porphyry).</summary>
    PullenSinusoidalDelta = (byte)'L',

    /// <summary>'M' — Morinus.</summary>
    Morinus = (byte)'M',

    /// <summary>'N' — equal, 1st cusp anchored to 0° Aries.</summary>
    EqualAriesAnchored = (byte)'N',

    /// <summary>'O' — Porphyry.</summary>
    Porphyry = (byte)'O',

    /// <summary>'P' — Placidus (default).</summary>
    Placidus = (byte)'P',

    /// <summary>'Q' — Pullen SR ("sinusoidal ratio").</summary>
    PullenSinusoidalRatio = (byte)'Q',

    /// <summary>'R' — Regiomontanus.</summary>
    Regiomontanus = (byte)'R',

    /// <summary>'S' — Sripati.</summary>
    Sripati = (byte)'S',

    /// <summary>'T' — Polich/Page ("topocentric").</summary>
    PolichPage = (byte)'T',

    /// <summary>'U' — Krusinski-Pisa-Goelzer.</summary>
    KrusinskiPisaGoelzer = (byte)'U',

    /// <summary>'V' — equal, Vehlow (Asc shifted by −15°).</summary>
    Vehlow = (byte)'V',

    /// <summary>'W' — equal, whole-sign houses.</summary>
    WholeSign = (byte)'W',

    /// <summary>'X' — meridian / axial-rotation system.</summary>
    Meridian = (byte)'X',

    /// <summary>'Y' — APC houses (Knegt).</summary>
    Apc = (byte)'Y',
}
