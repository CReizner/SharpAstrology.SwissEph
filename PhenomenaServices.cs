// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// C reference (Swiss Ephemeris):
//   SolarEclipse      — swe_sol_eclipse_*
//   LunarEclipse      — swe_lun_eclipse_*
//   LunarOccultation  — swe_lun_occult_*
//   RiseTransit       — swe_rise_trans
//   Heliacal          — swe_heliacal_*
//   Gauquelin         — swe_gauquelin_sector
//   Planetary         — swe_pheno
//   Horizontal        — swe_azalt / swe_azalt_rev
//   Refraction        — swe_refrac / swe_refrac_extended
//   Crossings         — swe_solcross / swe_mooncross

using System;
using SharpAstrology.SwissEphemerides.Application.Phenomena;

#pragma warning disable SE0001 // HeliacalService is marked Experimental — exposing it on the public surface is intentional, callers opt in by reading the property.

namespace SharpAstrology.SwissEphemerides;

/// <summary>
/// Aggregate phenomena surface exposed by <see cref="EphemerisContext.Phenomena"/>.
/// Bundles the stateless phenomena services into a single typed bag — eclipses,
/// occultations, rise/transit, heliacal events, Gauquelin sectors, planetary
/// phenomena, horizontal coords, refraction, and longitude crossings — so callers
/// can reach them without re-instantiating the dependency graph.
/// </summary>
/// <remarks>
/// The composition is performed once by
/// <see cref="EphemerisContextBuilder.Build"/>; instances are reused for
/// the lifetime of the owning context. Like the underlying services, the
/// facade itself is stateless and thread-safe to the same extent as the
/// shared <see cref="EphemerisContext.Bodies"/> and
/// <see cref="EphemerisContext.Sidereal"/> services it depends on.
/// </remarks>
public sealed class PhenomenaServices
{
    internal PhenomenaServices(
        SolarEclipseService solarEclipse,
        LunarEclipseService lunarEclipse,
        LunarOccultationService lunarOccultation,
        RiseTransitService riseTransit,
        HeliacalService heliacal,
        GauquelinSectorService gauquelin,
        PlanetaryPhenomenaService planetary,
        HorizontalCoordsService horizontal,
        RefractionService refraction,
        CrossingsService crossings)
    {
        SolarEclipse = solarEclipse ?? throw new ArgumentNullException(nameof(solarEclipse));
        LunarEclipse = lunarEclipse ?? throw new ArgumentNullException(nameof(lunarEclipse));
        LunarOccultation = lunarOccultation ?? throw new ArgumentNullException(nameof(lunarOccultation));
        RiseTransit = riseTransit ?? throw new ArgumentNullException(nameof(riseTransit));
        Heliacal = heliacal ?? throw new ArgumentNullException(nameof(heliacal));
        Gauquelin = gauquelin ?? throw new ArgumentNullException(nameof(gauquelin));
        Planetary = planetary ?? throw new ArgumentNullException(nameof(planetary));
        Horizontal = horizontal ?? throw new ArgumentNullException(nameof(horizontal));
        Refraction = refraction ?? throw new ArgumentNullException(nameof(refraction));
        Crossings = crossings ?? throw new ArgumentNullException(nameof(crossings));
    }

    /// <summary>Solar eclipse finder.</summary>
    public SolarEclipseService SolarEclipse { get; }

    /// <summary>Lunar eclipse finder.</summary>
    public LunarEclipseService LunarEclipse { get; }

    /// <summary>Lunar / planetary occultation finder.</summary>
    public LunarOccultationService LunarOccultation { get; }

    /// <summary>Rise / set / transit / twilight finder.</summary>
    public RiseTransitService RiseTransit { get; }

    /// <summary>Heliacal-event surface.</summary>
    public HeliacalService Heliacal { get; }

    /// <summary>Gauquelin-sector finder.</summary>
    public GauquelinSectorService Gauquelin { get; }

    /// <summary>Phase / magnitude / elongation.</summary>
    public PlanetaryPhenomenaService Planetary { get; }

    /// <summary>Ecliptic ↔ horizontal coords.</summary>
    public HorizontalCoordsService Horizontal { get; }

    /// <summary>Atmospheric refraction.</summary>
    public RefractionService Refraction { get; }

    /// <summary>Longitude-crossing finder.</summary>
    public CrossingsService Crossings { get; }
}
