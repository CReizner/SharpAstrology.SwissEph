// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using System.Collections.Generic;
using System.IO;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Houses;
using SharpAstrology.SwissEphemerides.Application.Phenomena;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Application.Stars;
using SharpAstrology.SwissEphemerides.Domain.Frames;
using SharpAstrology.SwissEphemerides.Infrastructure.Jpl;
using SharpAstrology.SwissEphemerides.Infrastructure.Moshier;
using SharpAstrology.SwissEphemerides.Infrastructure.Stars;
using SharpAstrology.SwissEphemerides.Infrastructure.SwissEph;

namespace SharpAstrology.SwissEphemerides;

/// <summary>
/// Fluent builder for <see cref="EphemerisContext"/>. Replaces the C
/// library's global <c>swe_set_ephe_path</c> / <c>swe_set_jpl_file</c> /
/// <c>swe_set_sid_mode</c> mutators with an explicit, immutable context
/// you can wire up once and share.
/// </summary>
/// <remarks>
/// At least one body-position source must be enabled. Moshier is on by
/// default — leave it on, or call <see cref="DisableMoshier"/> if you
/// want hard errors instead of analytic fallback when a SwissEph
/// segment or JPL chunk is missing. When more than one source is
/// configured, the context routes per call based on the
/// <see cref="EphemerisFlags"/> the caller passes
/// (<c>JplEph</c> / <c>SwissEph</c> / <c>MoshierEph</c>).
/// </remarks>
/// <example>
/// <code>
/// using var ctx = new EphemerisContextBuilder()
///     .UseSwissEphFiles("/usr/share/sweph")
///     .UseJplFile("/data/de441.eph")
///     .Build();
/// IEphemerides eph = ctx.AsEphemerides();
/// </code>
/// </example>
public sealed class EphemerisContextBuilder
{
    private string? _swissEphPath;
    private int _swissEphSegmentCacheCapacity = SwissEphBodyPositionSource.DefaultSegmentCacheCapacity;
    private string? _jplFilePath;
    private int _jplSegmentCacheCapacity = JplBodyPositionSource.DefaultSegmentCacheCapacity;
    private bool _includeMoshier = true;
    private AstronomicalModelOverrides? _models;
    private (SiderealMode mode, double t0, double ayanT0, SiderealFlags flags)? _sidereal;
    private string? _fixedStarCatalogPath;
    private Func<Stream>? _fixedStarCatalogStreamFactory;
    private LunarNodeStrategy _lunarNodeStrategy = LunarNodeStrategy.TrueNode;

    /// <summary>
    /// Enables the Swiss Ephemeris back-end and points it at a directory of
    /// <c>.se1</c> segment files. Files are opened lazily on first request;
    /// segments not found in the directory fall through to JPL or Moshier
    /// depending on the call's <see cref="EphemerisFlags"/>.
    /// </summary>
    /// <param name="path">Directory containing the <c>.se1</c> files.</param>
    /// <param name="segmentCacheCapacity">
    /// Maximum number of decoded Chebyshev segments to keep cached per
    /// body. Larger caches trade resident-set size for fewer file
    /// reads when batch-processing across many dates. Defaults to
    /// <c>SwissEphBodyPositionSource.DefaultSegmentCacheCapacity</c>.
    /// </param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="path"/> is <see langword="null"/>.
    /// </exception>
    public EphemerisContextBuilder UseSwissEphFiles(string path, int? segmentCacheCapacity = null)
    {
        _swissEphPath = path ?? throw new ArgumentNullException(nameof(path));
        if (segmentCacheCapacity is { } cap) _swissEphSegmentCacheCapacity = cap;
        return this;
    }

    /// <summary>
    /// Enables the JPL back-end and points it at a JPL DE binary file
    /// (e.g. <c>de441.eph</c>). The source is consulted for any call
    /// that includes <see cref="EphemerisFlags.JplEph"/>.
    /// </summary>
    /// <param name="filePath">Path to the JPL DE file.</param>
    /// <param name="segmentCacheCapacity">
    /// Maximum number of decoded Chebyshev chunks to keep cached.
    /// Defaults to <c>JplBodyPositionSource.DefaultSegmentCacheCapacity</c>.
    /// </param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="filePath"/> is <see langword="null"/>.
    /// </exception>
    public EphemerisContextBuilder UseJplFile(string filePath, int? segmentCacheCapacity = null)
    {
        _jplFilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        if (segmentCacheCapacity is { } cap) _jplSegmentCacheCapacity = cap;
        return this;
    }

    /// <summary>
    /// Disables the Moshier analytical fall-back. With Moshier off, calls
    /// for which neither SwissEph nor JPL has data throw instead of
    /// degrading to lower precision. Moshier is enabled by default.
    /// </summary>
    /// <returns>The builder, for chaining.</returns>
    public EphemerisContextBuilder DisableMoshier()
    {
        _includeMoshier = false;
        return this;
    }

    /// <summary>
    /// Overrides the precession / nutation / obliquity / frame-bias models
    /// the context uses. Defaults to
    /// <see cref="AstronomicalModelOverrides.Default"/> (IAU 2006 / IAU
    /// 2000B), matching the C library's compile-time defaults.
    /// </summary>
    /// <param name="models">The model overrides to use.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="models"/> is <see langword="null"/>.
    /// </exception>
    public EphemerisContextBuilder UseModels(AstronomicalModelOverrides models)
    {
        _models = models ?? throw new ArgumentNullException(nameof(models));
        return this;
    }

    /// <summary>
    /// Selects a sidereal (ayanamsha) mode for the
    /// <see cref="EphemerisContext.Sidereal"/> service. Equivalent to a
    /// <c>swe_set_sid_mode</c> call. The default
    /// (<see cref="SiderealMode.FaganBradley"/>) is only used when
    /// <see cref="EphemerisFlags.Sidereal"/> is requested at call site.
    /// </summary>
    /// <param name="mode">Built-in or user-defined sidereal preset.</param>
    /// <param name="t0">
    /// Reference epoch (Julian Day, TT) for user-defined modes; ignored
    /// for built-ins.
    /// </param>
    /// <param name="ayanT0">
    /// Ayanamsha value at <paramref name="t0"/> for user-defined modes.
    /// </param>
    /// <param name="flags">Optional sidereal projection flags.</param>
    /// <returns>The builder, for chaining.</returns>
    public EphemerisContextBuilder UseSiderealMode(
        SiderealMode mode,
        double t0 = 0,
        double ayanT0 = 0,
        SiderealFlags flags = SiderealFlags.None)
    {
        _sidereal = (mode, t0, ayanT0, flags);
        return this;
    }

    /// <summary>
    /// Points the context at a fixed-star catalogue file
    /// (<c>sefstars.txt</c>). Required for star-anchored sidereal modes
    /// (the <c>True *</c> family and the Galactic-Centre / Equator
    /// variants) and for direct fixed-star lookups via the underlying
    /// <see cref="FixedStarService"/>. When the SwissEph back-end is
    /// also configured via <see cref="UseSwissEphFiles"/>, the builder
    /// automatically falls back to <c>{swissEphPath}/sefstars.txt</c>
    /// if this method is not called explicitly.
    /// </summary>
    /// <param name="path">Filesystem path to <c>sefstars.txt</c>.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is
    /// <see langword="null"/>.</exception>
    public EphemerisContextBuilder UseFixedStarCatalog(string path)
    {
        _fixedStarCatalogPath = path ?? throw new ArgumentNullException(nameof(path));
        _fixedStarCatalogStreamFactory = null;
        return this;
    }

    /// <summary>
    /// Points the context at a fixed-star catalogue produced by a
    /// generic <see cref="Stream"/> factory. Useful for embedded
    /// resources or test fixtures.
    /// </summary>
    /// <param name="streamFactory">Factory delegate yielding the
    /// catalogue contents on demand. Invoked once per context.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="streamFactory"/> is <see langword="null"/>.</exception>
    public EphemerisContextBuilder UseFixedStarCatalog(Func<Stream> streamFactory)
    {
        _fixedStarCatalogStreamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
        _fixedStarCatalogPath = null;
        return this;
    }

    /// <summary>
    /// Selects which lunar-node flavour the
    /// <see cref="SharpAstrology.Interfaces.IEphemerides"/> adapter resolves
    /// for <see cref="SharpAstrology.Enums.Planets.NorthNode"/> and
    /// <see cref="SharpAstrology.Enums.Planets.SouthNode"/>. Defaults to
    /// <see cref="LunarNodeStrategy.TrueNode"/> (osculating node, the
    /// <c>0.1.0-alpha</c> behaviour); pass
    /// <see cref="LunarNodeStrategy.MeanNode"/> to switch the adapter to
    /// the C library's <c>SE_MEAN_NODE</c>. The lower-level
    /// <see cref="EphemerisContext.Bodies"/> service exposes both flavours
    /// independently of this setting.
    /// </summary>
    /// <param name="strategy">Lunar-node strategy.</param>
    /// <returns>The builder, for chaining.</returns>
    public EphemerisContextBuilder WithLunarNodeStrategy(LunarNodeStrategy strategy)
    {
        _lunarNodeStrategy = strategy;
        return this;
    }

    /// <summary>
    /// Materialises a fresh <see cref="EphemerisContext"/> from the
    /// configured sources and models. The returned context is independent
    /// of the builder; you can keep building or discard the builder
    /// after calling <c>Build</c>.
    /// </summary>
    /// <returns>The constructed context.</returns>
    /// <exception cref="InvalidOperationException">
    /// No body-position source is enabled — SwissEph, JPL and Moshier
    /// are all off. Enable at least one of them.
    /// </exception>
    public EphemerisContext Build()
    {
        var models = _models ?? AstronomicalModelOverrides.Default;

        var sources = new List<IBodyPositionSource>(3);
        var disposables = new List<IDisposable>(2);

        if (_jplFilePath is not null)
        {
            var jpl = new JplBodyPositionSource(_jplFilePath, _jplSegmentCacheCapacity);
            sources.Add(jpl);
            disposables.Add(jpl);
        }
        if (_swissEphPath is not null)
        {
            var swe = new SwissEphBodyPositionSource(_swissEphPath, _swissEphSegmentCacheCapacity);
            sources.Add(swe);
            disposables.Add(swe);
        }
        if (_includeMoshier)
        {
            sources.Add(new MoshierBodyPositionSource());
        }

        if (sources.Count == 0)
        {
            throw new InvalidOperationException(
                "EphemerisContextBuilder.Build: at least one body-position source must be enabled "
                + "(UseSwissEphFiles, UseJplFile, or leave Moshier enabled).");
        }

        var calendar = new CalendarService();
        var router = new SourceRouter(sources);
        var sidereal = new SiderealService(calendar, models);
        if (_sidereal is { } cfg)
        {
            sidereal.SetMode(cfg.mode, cfg.t0, cfg.ayanT0, cfg.flags);
        }

        var bodies = new BodyService(router, calendar, models, sidereal);
        calendar.AttachSunPositionProvider(new BodyServiceSunPositionProvider(bodies));
        var houses = new HouseService(calendar, models, sidereal, bodies);

        // Wire up the fixed-star service after BodyService is built (FixedStarService
        // depends on it). The reverse cycle — SiderealService asking the FixedStarService
        // for star-anchored ayanamshas — is broken via SiderealService.AttachFixedStarService.
        FixedStarService? fixedStars = null;
        var catalog = ResolveFixedStarCatalog();
        if (catalog is not null)
        {
            fixedStars = new FixedStarService(catalog, bodies, calendar, models, sidereal);
            sidereal.AttachFixedStarService(fixedStars);
        }

        // Phenomena composition. Order matters: the search-capable eclipse /
        // occultation / Gauquelin / heliacal services depend on horizontal
        // and rise/transit collaborators that must already exist.
        var horizontal = new HorizontalCoordsService(calendar, models);
        var refraction = new RefractionService();
        var planetary = new PlanetaryPhenomenaService(bodies, calendar);
        var crossings = new CrossingsService(bodies, calendar);
        var riseTransit = new RiseTransitService(bodies, calendar, horizontal, fixedStars);
        var solarEclipse = new SolarEclipseService(bodies, calendar, horizontal, riseTransit, models);
        var lunarEclipse = new LunarEclipseService(bodies, calendar, horizontal, riseTransit);
        var lunarOccultation = fixedStars is not null
            ? new LunarOccultationService(bodies, calendar, horizontal, riseTransit, fixedStars)
            : new LunarOccultationService(bodies, calendar, horizontal, riseTransit);
#pragma warning disable SE0001 // HeliacalService is Experimental; intentional opt-in here.
        var heliacal = new HeliacalService(bodies, horizontal, planetary, riseTransit);
#pragma warning restore SE0001
        var gauquelin = new GauquelinSectorService(bodies, calendar, houses, riseTransit, models);

        var phenomena = new PhenomenaServices(
            solarEclipse,
            lunarEclipse,
            lunarOccultation,
            riseTransit,
            heliacal,
            gauquelin,
            planetary,
            horizontal,
            refraction,
            crossings);

        // Nodes/apsides routes through the same SourceRouter as BodyService
        // since Milestone 3C: Moshier remains the analytical fall-back, while
        // SwissEph and JPL handle their own back-ends when configured. The
        // service mirrors swi_plan_for_osc_elem (sweph.c#L5758-L5856), which
        // dispatches by source bit rather than hard-coding a single back-end.
        var nodesAndApsides = new NodesAndApsidesService(router, calendar, models);

        // Planetocentric service (swe_calc_pctr). Reuses the configured
        // BodyService for source routing + file fall-back; sidereal mode is
        // shared with the geocentric pipeline.
        var planetocentric = new PlanetocentricService(bodies, calendar, models, sidereal);

        return new EphemerisContext(
            calendar,
            bodies,
            houses,
            sidereal,
            models,
            phenomena,
            fixedStars,
            nodesAndApsides,
            planetocentric,
            _lunarNodeStrategy,
            disposables);
    }

    private IFixedStarCatalog? ResolveFixedStarCatalog()
    {
        if (_fixedStarCatalogStreamFactory is { } factory)
            return new FixedStarCatalogReader(factory);
        if (_fixedStarCatalogPath is { } path)
            return new FixedStarCatalogReader(path);
        if (_swissEphPath is { } swePath)
        {
            var derived = Path.Combine(swePath, FixedStarCatalogReader.DefaultFileName);
            if (File.Exists(derived))
                return new FixedStarCatalogReader(derived);
        }
        return null;
    }
}
