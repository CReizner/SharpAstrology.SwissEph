// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using System.Collections.Generic;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Houses;
using SharpAstrology.SwissEphemerides.Application.Phenomena;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Application.Stars;
using SharpAstrology.SwissEphemerides.Domain.Frames;

namespace SharpAstrology.SwissEphemerides;

/// <summary>
/// Owns one configured set of ephemeris services and any disposable
/// infrastructure they depend on (file-backed <c>.se1</c> readers, JPL
/// kernels). Build via <see cref="EphemerisContextBuilder"/>, then either
/// use the exposed services (<see cref="Bodies"/>, <see cref="Houses"/>,
/// <see cref="Calendar"/>, <see cref="Sidereal"/>, <see cref="Phenomena"/>,
/// <see cref="FixedStars"/>, <see cref="NodesAndApsides"/>) directly or
/// call <see cref="AsEphemerides"/> for the higher-level
/// <see cref="SharpAstrology.Interfaces.IEphemerides"/> view.
/// </summary>
/// <remarks>
/// Service instances are created once and reused for every call. The
/// context is safe for concurrent reads after construction. The sidereal
/// mode is fixed at build time via
/// <see cref="EphemerisContextBuilder.UseSiderealMode"/>; the
/// corresponding mutator (<see cref="SiderealService.SetMode"/>) is
/// <see langword="internal"/>, so a published <see cref="Sidereal"/>
/// surface only exposes the read-only ayanamsha query family
/// (<c>GetAyanamsa</c>, <c>GetAyanamsaUt</c>, <c>GetName</c>) — the
/// "build-time only" discipline is enforced at the type level rather than
/// via a separate read-only interface.
/// </remarks>
public sealed class EphemerisContext : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _ownedDisposables;
    private bool _disposed;

    internal EphemerisContext(
        CalendarService calendar,
        BodyService bodies,
        HouseService houses,
        SiderealService sidereal,
        AstronomicalModelOverrides models,
        PhenomenaServices phenomena,
        FixedStarService? fixedStars,
        NodesAndApsidesService nodesAndApsides,
        PlanetocentricService planetocentric,
        LunarNodeStrategy lunarNodeStrategy,
        IReadOnlyList<IDisposable> ownedDisposables)
    {
        Calendar = calendar;
        Bodies = bodies;
        Houses = houses;
        Sidereal = sidereal;
        Models = models;
        Phenomena = phenomena;
        FixedStars = fixedStars;
        NodesAndApsides = nodesAndApsides;
        Planetocentric = planetocentric;
        LunarNodeStrategy = lunarNodeStrategy;
        _ownedDisposables = ownedDisposables;
    }

    /// <summary>
    /// Calendar / time service: UTC ↔ Julian Day conversions, ΔT, mean
    /// and apparent sidereal time. Equivalent to the C library's
    /// <c>swe_julday</c> / <c>swe_deltat</c> / <c>swe_sidtime</c> family.
    /// </summary>
    public CalendarService Calendar { get; }

    /// <summary>
    /// Body-position service. Equivalent to the C library's
    /// <c>swe_calc</c> / <c>swe_calc_ut</c>.
    /// </summary>
    public BodyService Bodies { get; }

    /// <summary>
    /// House-system service. Equivalent to the C library's
    /// <c>swe_houses</c> / <c>swe_houses_ex2</c>.
    /// </summary>
    public HouseService Houses { get; }

    /// <summary>
    /// Sidereal / ayanamsha service. Equivalent to the C library's
    /// read-only <c>swe_get_ayanamsa_ex</c> / <c>swe_get_ayanamsa_ex_ut</c>
    /// / <c>swe_get_ayanamsa_name</c> family. The matching mutator
    /// (<see cref="SiderealService.SetMode"/>, mirror of
    /// <c>swe_set_sid_mode</c>) is <see langword="internal"/> by design and
    /// unreachable from external consumers — configure the mode at build
    /// time via <see cref="EphemerisContextBuilder.UseSiderealMode"/>.
    /// </summary>
    public SiderealService Sidereal { get; }

    /// <summary>
    /// Astronomical-model overrides (precession, nutation, obliquity,
    /// frame-bias) used for every computation in this context.
    /// </summary>
    public AstronomicalModelOverrides Models { get; }

    /// <summary>
    /// Phenomena surface: eclipses, occultations, rise/set, heliacal events,
    /// Gauquelin sectors, planetary phenomena, horizontal coords,
    /// refraction, longitude crossings. See <see cref="PhenomenaServices"/>.
    /// </summary>
    public PhenomenaServices Phenomena { get; }

    /// <summary>
    /// Fixed-star service. Equivalent to the C library's <c>swe_fixstar2</c>
    /// family. <see langword="null"/> when no fixed-star catalogue
    /// (<c>sefstars.txt</c>) is configured — see
    /// <see cref="EphemerisContextBuilder.UseFixedStarCatalog(string)"/>.
    /// </summary>
    public FixedStarService? FixedStars { get; }

    /// <summary>
    /// Nodes / apsides / orbital-element service. Equivalent to the C
    /// library's <c>swe_nod_aps</c>, <c>swe_get_orbital_elements</c>, and
    /// <c>swe_orbit_max_min_true_distance</c>.
    /// </summary>
    public NodesAndApsidesService NodesAndApsides { get; }

    /// <summary>
    /// Planetocentric body-position service. Equivalent to the C library's
    /// <c>swe_calc_pctr</c>. Returns the position and velocity of one body
    /// as seen from the center of another body.
    /// </summary>
    public PlanetocentricService Planetocentric { get; }

    /// <summary>
    /// Lunar-node strategy applied by the
    /// <see cref="SharpAstrology.Interfaces.IEphemerides"/> adapter when
    /// resolving <see cref="SharpAstrology.Enums.Planets.NorthNode"/> /
    /// <see cref="SharpAstrology.Enums.Planets.SouthNode"/>. Configured via
    /// <see cref="EphemerisContextBuilder.WithLunarNodeStrategy"/>; the
    /// default is <see cref="LunarNodeStrategy.TrueNode"/>.
    /// </summary>
    public LunarNodeStrategy LunarNodeStrategy { get; }

    /// <summary>
    /// Returns this context as an <see cref="SharpAstrology.Interfaces.IEphemerides"/>
    /// suitable for <c>AstrologyChart</c> and other SharpAstrology
    /// consumers. The adapter is a thin view over the same service
    /// instances and does <b>not</b> own the context: disposing the
    /// adapter leaves this context alive so callers can keep using
    /// <see cref="Bodies"/>, <see cref="Houses"/>, etc., or hand the
    /// same context to additional adapters. Dispose this context
    /// explicitly when finished.
    /// </summary>
    public global::SharpAstrology.Interfaces.IEphemerides AsEphemerides() =>
        new global::SharpAstrology.Ephemerides.SwissEphemerides(this, ownsContext: false);

    /// <summary>
    /// Releases every disposable resource owned by this context (e.g.
    /// open <c>.se1</c> file handles, JPL segment caches). Safe to call
    /// multiple times. Per-resource disposal failures are swallowed so
    /// one bad file does not leak the rest.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        foreach (var d in _ownedDisposables)
        {
            try { d.Dispose(); } catch { /* best-effort */ }
        }
        _disposed = true;
    }
}
