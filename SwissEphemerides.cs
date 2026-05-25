// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using System.Collections.Generic;
using SharpAstrology.DataModels;
using SharpAstrology.Enums;
using SharpAstrology.Interfaces;
using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Calendar;
using SharpAstrology.SwissEphemerides.Application.Houses;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.Ephemerides;

/// <summary>
/// Drop-in <see cref="IEphemerides"/> implementation backed by an
/// <see cref="EphemerisContext"/>. Pass an instance of this class to
/// <c>AstrologyChart</c> and any other SharpAstrology component that
/// expects the <c>SharpAstrology.Base</c> interface.
/// </summary>
/// <remarks>
/// <para>
/// Numerical behaviour matches the underlying services exactly — this
/// adapter only translates between the <c>SharpAstrology.Base</c> enum
/// vocabulary (<see cref="Planets"/>, <see cref="HouseSystems"/>,
/// <see cref="EphCalculationMode"/>) and the port's internal types
/// before delegating to <see cref="BodyService.Compute"/> /
/// <see cref="HouseService.Compute"/>.
/// </para>
/// <para>
/// <b>Lunar nodes:</b> <see cref="Planets.NorthNode"/> resolves to the
/// flavour selected by
/// <see cref="EphemerisContextBuilder.WithLunarNodeStrategy"/> — by
/// default the <i>true</i> (osculating) lunar node, equivalent to the C
/// library's <c>SE_TRUE_NODE</c>. True-node requests use the source routing
/// configured on the context, so SwissEph/JPL sources are honoured when
/// available. Pass
/// <see cref="LunarNodeStrategy.MeanNode"/> at build time to switch to
/// <c>SE_MEAN_NODE</c>; the mean-node implementation is the analytical
/// Moshier series, matching the lower-level body service.
/// <see cref="Planets.SouthNode"/> is always the geometric antipode
/// (longitude + 180°, latitude / latitude-speed sign-flipped) of the
/// resolved north node, so the two stay
/// self-consistent. Both flavours remain reachable independently of the
/// strategy through the lower-level <see cref="EphemerisContext.Bodies"/>
/// service.
/// </para>
/// </remarks>
public sealed class SwissEphemerides : IEphemerides
{
    private readonly EphemerisContext _ctx;
    private readonly bool _ownsContext;
    private bool _disposed;

    /// <summary>
    /// Wraps an existing <see cref="EphemerisContext"/> as an
    /// <see cref="IEphemerides"/>.
    /// </summary>
    /// <param name="context">The configured context.</param>
    /// <param name="ownsContext">
    /// When <see langword="true"/>, disposing this adapter disposes
    /// <paramref name="context"/> too. Set to <see langword="false"/>
    /// (the default) when the same context backs multiple adapters or
    /// is owned by your composition root.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/> is <see langword="null"/>.
    /// </exception>
    public SwissEphemerides(EphemerisContext context, bool ownsContext = false)
    {
        _ctx = context ?? throw new ArgumentNullException(nameof(context));
        _ownsContext = ownsContext;
    }

    /// <summary>
    /// Convenience constructor that builds a default Moshier-only
    /// context — no ephemeris files required — and takes ownership of
    /// it. Use this when you only need analytical-precision positions
    /// (e.g. for charting).
    /// </summary>
    public SwissEphemerides() : this(new EphemerisContextBuilder().Build(), ownsContext: true)
    {
    }

    /// <inheritdoc />
    public double Ayanamsa(DateTime pointInTime)
    {
        EnsureUtc(pointInTime);
        var jdUt = _ctx.Calendar.UtcToJulianDay(pointInTime);
        return _ctx.Sidereal.GetAyanamsaUt(jdUt);
    }

    /// <inheritdoc />
    public PlanetPosition PlanetsPosition(
        Planets planet,
        DateTime pointInTime,
        EphCalculationMode mode = EphCalculationMode.Tropic)
    {
        EnsureUtc(pointInTime);
        var jdUt = _ctx.Calendar.UtcToJulianDay(pointInTime);
        var flags = ResolveBodyFlags(mode);

        // The South Node is the geometric antipode of the resolved North
        // Node. We compute it from the same flavour the strategy picks so
        // SouthNode = NorthNode + 180° holds for every call.
        if (planet == Planets.SouthNode)
        {
            var (northBody, northFlags) = ResolveNodeRequest(mode);
            var north = ComputeBody(northBody, jdUt, northFlags);
            return ToSouthNode(north, mode);
        }

        if (planet == Planets.NorthNode)
        {
            var (northBody, northFlags) = ResolveNodeRequest(mode);
            var state = ComputeBody(northBody, jdUt, northFlags);
            return ToPlanetPosition(state, mode);
        }

        var celestial = MapPlanet(planet);
        var requestFlags = celestial == CelestialBody.TrueNode ? TrueNodeFlags(mode) : flags;
        var requestState = ComputeBody(celestial, jdUt, requestFlags);
        return ToPlanetPosition(requestState, mode);
    }

    /// <summary>
    /// Maps the configured <see cref="LunarNodeStrategy"/> to the
    /// (CelestialBody, EphemerisFlags) tuple used by the underlying
    /// <see cref="BodyService"/>. True-node requests stay source-neutral so
    /// the context router can choose SwissEph/JPL/Moshier; mean-node requests
    /// intentionally force the Moshier analytical mean-element path because
    /// that is the only lower-level implementation currently available.
    /// </summary>
    private (CelestialBody Body, EphemerisFlags Flags) ResolveNodeRequest(EphCalculationMode mode)
    {
        var strategy = _ctx.LunarNodeStrategy;
        return strategy == LunarNodeStrategy.MeanNode
            ? (CelestialBody.MeanNode, MeanNodeFlags(mode))
            : (CelestialBody.TrueNode, TrueNodeFlags(mode));
    }

    /// <inheritdoc />
    public HousePosition HouseCuspPositions(
        DateTime pointInTime,
        double latitude,
        double longitude,
        HouseSystems houseSystems = HouseSystems.Placidus,
        EphCalculationMode mode = EphCalculationMode.Tropic)
    {
        EnsureUtc(pointInTime);
        var jdUt = _ctx.Calendar.UtcToJulianDay(pointInTime);
        var hsys = MapHouseSystem(houseSystems);
        var flags = mode == EphCalculationMode.Sidereal ? EphemerisFlags.Sidereal : EphemerisFlags.None;
        var result = _ctx.Houses.Compute(jdUt, latitude, longitude, hsys, flags);

        var cusps = new Dictionary<Houses, double>(12);
        for (var i = 1; i <= 12; i++)
        {
            cusps[(Houses)i] = result.Cusps[i];
        }

        var asc = result.Ascendant;
        var mc = result.MidHeaven;
        var dc = AstrologyMod360(asc + 180.0);
        var ic = AstrologyMod360(mc + 180.0);

        return new HousePosition
        {
            HouseCusps = cusps,
            Cross = new Dictionary<Cross, double>(5)
            {
                [Cross.Asc] = asc,
                [Cross.Dc] = dc,
                [Cross.Mc] = mc,
                [Cross.Ic] = ic,
                [Cross.Vertex] = result.Vertex,
            },
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_ownsContext) _ctx.Dispose();
        _disposed = true;
    }

    // ---- mapping helpers ---------------------------------------------------

    private static void EnsureUtc(DateTime pointInTime)
    {
        if (pointInTime.Kind != DateTimeKind.Utc)
            throw new ArgumentException(
                "DateTime must be UTC (DateTimeKind.Utc).", nameof(pointInTime));
    }

    /// <summary>
    /// <see cref="Planets"/> → <see cref="CelestialBody"/>. The astrological
    /// "lunar node" maps to the true (osculating) node by convention; the
    /// adapter's configured lunar-node strategy handles the mean-node option
    /// before this fallback mapper is reached.
    /// </summary>
    private static CelestialBody MapPlanet(Planets planet) => planet switch
    {
        Planets.Sun => CelestialBody.Sun,
        Planets.Moon => CelestialBody.Moon,
        Planets.Mercury => CelestialBody.Mercury,
        Planets.Venus => CelestialBody.Venus,
        Planets.Mars => CelestialBody.Mars,
        Planets.Jupiter => CelestialBody.Jupiter,
        Planets.Saturn => CelestialBody.Saturn,
        Planets.Uranus => CelestialBody.Uranus,
        Planets.Neptune => CelestialBody.Neptune,
        Planets.Pluto => CelestialBody.Pluto,
        Planets.NorthNode => CelestialBody.TrueNode,
        // SouthNode is computed from TrueNode upstream — never reaches here.
        Planets.Chiron => CelestialBody.Chiron,
        Planets.Earth => CelestialBody.Earth,
        Planets.Pholus => CelestialBody.Pholus,
        Planets.Ceres => CelestialBody.Ceres,
        Planets.Pallas => CelestialBody.Pallas,
        Planets.Juno => CelestialBody.Juno,
        Planets.Vesta => CelestialBody.Vesta,
        Planets.Lilith => throw new NotSupportedException(
            "Planets.Lilith is not supported by SharpAstrology.SwissEphemerides. " +
            "Previous SharpAstrology.SwissEph mapped this to numbered asteroid #1181, which " +
            "requires the seas_*.se1 asteroid files plus a public asteroid-by-ID API that is " +
            "not yet exposed by this port. See docs/PLANETS_LILITH_MIGRATION.md for the " +
            "available workarounds (Black Moon via CelestialBody.MeanApogee, or staying on " +
            "SharpAstrology.SwissEph 0.3.0)."),
        _ => throw new ArgumentOutOfRangeException(nameof(planet), planet, "Unsupported planet."),
    };

    private static HouseSystem MapHouseSystem(HouseSystems hs) => hs switch
    {
        HouseSystems.Alcabitus => HouseSystem.Alcabitius,
        HouseSystems.ApcHouses => HouseSystem.Apc,
        HouseSystems.AxialRotationSystem => HouseSystem.Meridian,
        HouseSystems.AzimutalSystem => HouseSystem.Horizon,
        HouseSystems.Campanus => HouseSystem.Campanus,
        HouseSystems.Carter => HouseSystem.CarterPoliEquatorial,
        HouseSystems.Equal => HouseSystem.Equal,
        HouseSystems.EqualMc => HouseSystem.EqualMc,
        HouseSystems.Equal1Aries => HouseSystem.EqualAriesAnchored,
        HouseSystems.SunshineTreindl => HouseSystem.SunshineTreindl,
        HouseSystems.SunshineMakransky => HouseSystem.SunshineMakransky,
        HouseSystems.Koch => HouseSystem.Koch,
        HouseSystems.KrusinskiPisaGoelzer => HouseSystem.KrusinskiPisaGoelzer,
        HouseSystems.Morinus => HouseSystem.Morinus,
        HouseSystems.Placidus => HouseSystem.Placidus,
        HouseSystems.PolichPage => HouseSystem.PolichPage,
        HouseSystems.Porphyrius => HouseSystem.Porphyry,
        HouseSystems.PullenSd => HouseSystem.PullenSinusoidalDelta,
        HouseSystems.PullenSr => HouseSystem.PullenSinusoidalRatio,
        HouseSystems.Regiomontanus => HouseSystem.Regiomontanus,
        HouseSystems.Sripati => HouseSystem.Sripati,
        HouseSystems.VehlowEqual => HouseSystem.Vehlow,
        HouseSystems.WholeSign => HouseSystem.WholeSign,
        _ => throw new ArgumentOutOfRangeException(nameof(hs), hs, "Unsupported house system."),
    };

    /// <summary>
    /// Default body flags (high-precision velocity + the user's tropical /
    /// sidereal selector). Equivalent to <c>SEFLG_SPEED [|SEFLG_SIDEREAL]</c>.
    /// </summary>
    private static EphemerisFlags ResolveBodyFlags(EphCalculationMode mode)
    {
        var flags = EphemerisFlags.Speed;
        if (mode == EphCalculationMode.Sidereal) flags |= EphemerisFlags.Sidereal;
        return flags;
    }

    /// <summary>
    /// Geometric flag set required by the lunar-osculating-elements branch
    /// of <see cref="BodyService"/> (see
    /// <see cref="LunarOsculatingElements"/>): no aberration, no
    /// gravitational deflection, no nutation, true position, no source hint.
    /// Source selection is left to the context's <c>SourceRouter</c>.
    /// Sidereal projection is layered on top by the regular
    /// <c>BodyService</c> sidereal step.
    /// </summary>
    private static EphemerisFlags TrueNodeFlags(EphCalculationMode mode) => NodeGeometryFlags(mode);

    /// <summary>
    /// Mean lunar node is currently implemented by the analytical Moshier
    /// mean-element series only, so the adapter keeps forcing that source for
    /// the mean-node strategy even inside SwissEph/JPL-capable contexts.
    /// </summary>
    private static EphemerisFlags MeanNodeFlags(EphCalculationMode mode) =>
        NodeGeometryFlags(mode) | EphemerisFlags.MoshierEph;

    private static EphemerisFlags NodeGeometryFlags(EphCalculationMode mode)
    {
        var flags = EphemerisFlags.Speed
                  | EphemerisFlags.NoNutation
                  | EphemerisFlags.TruePosition
                  | EphemerisFlags.NoAberration
                  | EphemerisFlags.NoGravDeflection;
        if (mode == EphCalculationMode.Sidereal) flags |= EphemerisFlags.Sidereal;
        return flags;
    }

    private BodyState ComputeBody(CelestialBody body, JulianDay jdUt, EphemerisFlags flags) =>
        _ctx.Bodies.ComputeUt(body, jdUt, flags);

    /// <summary>
    /// Cartesian (geocentric ecliptic-of-date or sidereal) → polar form
    /// expected by <see cref="PlanetPosition"/>: longitude/latitude in
    /// degrees, distance in AU, speeds in degrees/day and AU/day.
    /// </summary>
    private static PlanetPosition ToPlanetPosition(BodyState state, EphCalculationMode mode)
    {
        Span<double> cart =
        [
            state.Position.X, state.Position.Y, state.Position.Z,
            state.Velocity.X, state.Velocity.Y, state.Velocity.Z,
        ];
        Span<double> polar = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(cart, polar);

        return new PlanetPosition
        {
            Longitude = AstrologyMod360(polar[0] * AstronomicalConstants.RadToDeg),
            Latitude = polar[1] * AstronomicalConstants.RadToDeg,
            Distance = polar[2],
            SpeedLongitude = polar[3] * AstronomicalConstants.RadToDeg,
            SpeedLatitude = polar[4] * AstronomicalConstants.RadToDeg,
            SpeedDistance = polar[5],
        };
    }

    /// <summary>South node = (north + 180°) longitude with mirrored speed.</summary>
    private static PlanetPosition ToSouthNode(BodyState northNode, EphCalculationMode mode)
    {
        var north = ToPlanetPosition(northNode, mode);
        return new PlanetPosition
        {
            Longitude = AstrologyMod360(north.Longitude + 180.0),
            Latitude = -north.Latitude,
            Distance = north.Distance,
            SpeedLongitude = north.SpeedLongitude,
            SpeedLatitude = -north.SpeedLatitude,
            SpeedDistance = north.SpeedDistance,
        };
    }

    private static double AstrologyMod360(double deg)
    {
        var x = deg % 360.0;
        if (x < 0) x += 360.0;
        return x;
    }
}
