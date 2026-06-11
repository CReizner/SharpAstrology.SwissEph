// Ported from swisseph-master/sweph.c sweph() (lines 2125-2358) and
// sweplan() (lines 1820-1968). Original license: see LICENSE.SwissEph.txt.

using System;
using System.Collections.Concurrent;
using System.IO;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Infrastructure.SwissEph;

/// <summary>
/// Body-position source that reads Astrodienst <c>.se1</c> compressed Chebyshev
/// segments. Implements <see cref="IBodyPositionSource"/> for Sun, Moon,
/// Mercury–Pluto, and the major asteroids (Ceres, Pallas, Juno, Vesta, Chiron,
/// Pholus). Files are opened lazily via <see cref="SwissEphFileLocator"/>;
/// segments are cached in <see cref="SegmentCache"/> and shared across threads.
/// </summary>
/// <remarks>
/// The returned <see cref="BodyState.Frame"/> is always
/// <see cref="BodyStateFrame.BarycentricJ2000Equator"/> for the Sun,
/// Earth and the planets, and
/// <see cref="BodyStateFrame.GeocentricJ2000Equator"/> for the Moon —
/// matching the C-side <c>plan_data.x</c> contents after
/// <c>sweplan()</c>. The body service rotates these into the requested
/// apparent frame as part of its correction pipeline.
/// </remarks>
internal sealed class SwissEphBodyPositionSource : IBodyPositionSource, IDisposable
{
    private readonly SwissEphFileLocator _locator;
    private readonly SegmentCache _segmentCache;
    private readonly ConcurrentDictionary<string, Se1FileReader> _openFiles = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Per-(seiBody, century-block) reader cache. Lookup is alloc-free —
    /// <see cref="long"/> keys avoid the per-call <see cref="Path.Combine"/>
    /// + <see cref="SwissEphFileLocator.ComputeFileName(int, JulianDay)"/>
    /// string allocations that would otherwise show up as ~150 B per
    /// <c>swe_calc</c> in the M-14 zero-allocation hot-path budget. The
    /// underlying <see cref="Se1FileReader"/> instances stay deduplicated
    /// in <see cref="_openFiles"/>; this dictionary just routes the
    /// (body, block) tuple straight to the right reader without hitting
    /// the path machinery.
    /// </summary>
    private readonly ConcurrentDictionary<long, Se1FileReader> _readersByBlock = new();
    private int _disposed;

    /// <summary>Default LRU capacity (in segments) when none specified.</summary>
    public const int DefaultSegmentCacheCapacity = 256;

    /// <summary>Earth/Moon mass ratio (AA 2006 K7), mirrors C define EARTH_MOON_MRAT.</summary>
    private const double EarthMoonMassRatio = 1.0 / 0.0123000383;

    /// <summary>The directory containing the <c>.se1</c> files.</summary>
    public string EphemerisPath => _locator.EphePath;

    /// <summary>The shared segment cache. Public so benchmarks can read its hit-rate.</summary>
    public SegmentCache SegmentCache => _segmentCache;

    /// <inheritdoc />
    public EphemerisSource Kind => EphemerisSource.SwissEph;

    public SwissEphBodyPositionSource(string ephemerisPath, int segmentCacheCapacity = DefaultSegmentCacheCapacity)
    {
        if (ephemerisPath is null) throw new ArgumentNullException(nameof(ephemerisPath));
        _locator = new SwissEphFileLocator(ephemerisPath);
        _segmentCache = new SegmentCache(segmentCacheCapacity);
    }

    /// <inheritdoc />
    public bool CanProvide(CelestialBody body) => body switch
    {
        CelestialBody.Sun
            or CelestialBody.Moon
            or CelestialBody.Mercury
            or CelestialBody.Venus
            or CelestialBody.Earth
            or CelestialBody.Mars
            or CelestialBody.Jupiter
            or CelestialBody.Saturn
            or CelestialBody.Uranus
            or CelestialBody.Neptune
            or CelestialBody.Pluto
            or CelestialBody.Chiron
            or CelestialBody.Pholus
            or CelestialBody.Ceres
            or CelestialBody.Pallas
            or CelestialBody.Juno
            or CelestialBody.Vesta => true,
        _ => false,
    };

    /// <inheritdoc />
    public BodyState Compute(CelestialBody body, JulianDay jdEt, EphemerisFlags flags)
    {
        if (!CanProvide(body))
            throw new UnsupportedBodyException(body, EphemerisSource.SwissEph);

        var inSpeed = (flags & (EphemerisFlags.Speed | EphemerisFlags.Speed3)) != 0;

        // Top-level dispatch (mirrors sweplan() in sweph.c).
        return body switch
        {
            CelestialBody.Moon => ComputeMoonGeocentric(jdEt, inSpeed),
            CelestialBody.Sun => ComputeSunBarycentric(jdEt, inSpeed),
            CelestialBody.Earth => ComputeEarthBarycentric(jdEt, inSpeed),
            _ => ComputePlanetBarycentric(body, jdEt, inSpeed),
        };
    }

    private BodyState ComputeMoonGeocentric(JulianDay jdEt, bool inSpeed)
    {
        var (pos, vel) = ReadFromFile(CelestialBody.Moon, Se1FileFormat.SeiMoon, jdEt, inSpeed);
        return new BodyState(pos, vel, pos.Length, EphemerisSource.SwissEph, BodyStateFrame.GeocentricJ2000Equator);
    }

    /// <summary>
    /// Computes barycentric Sun. Mirrors the EMBHEL trick in <c>sweph()</c>
    /// (sweph.c:2312-2331): the planet file at SEI_SUNBARY actually stores
    /// heliocentric Earth, so bary Sun = bary EMB − helio Earth.
    /// </summary>
    private BodyState ComputeSunBarycentric(JulianDay jdEt, bool inSpeed)
    {
        var (helEarth, helEarthV) = ReadFromFile(CelestialBody.Sun, Se1FileFormat.SeiSunBary, jdEt, inSpeed);
        var (baryEmb, baryEmbV) = ReadFromFile(CelestialBody.Sun, Se1FileFormat.SeiEmb, jdEt, inSpeed);
        var pos = baryEmb - helEarth;
        var vel = inSpeed ? (baryEmbV - helEarthV) : default;
        return new BodyState(pos, vel, pos.Length, EphemerisSource.SwissEph, BodyStateFrame.BarycentricJ2000Equator);
    }

    /// <summary>
    /// Computes barycentric Earth. Mirrors <c>embofs()</c> in <c>sweph.c:5062-5067</c>:
    /// Earth = EMB − Moon / (mratio + 1).
    /// </summary>
    private BodyState ComputeEarthBarycentric(JulianDay jdEt, bool inSpeed)
    {
        var (baryEmb, baryEmbV) = ReadFromFile(CelestialBody.Earth, Se1FileFormat.SeiEmb, jdEt, inSpeed);
        var (moonGeo, moonGeoV) = ReadFromFile(CelestialBody.Moon, Se1FileFormat.SeiMoon, jdEt, inSpeed);
        var w = 1.0 / (EarthMoonMassRatio + 1.0);
        var pos = new Vec3(baryEmb.X - moonGeo.X * w, baryEmb.Y - moonGeo.Y * w, baryEmb.Z - moonGeo.Z * w);
        var vel = default(Vec3);
        if (inSpeed)
        {
            vel = new Vec3(baryEmbV.X - moonGeoV.X * w, baryEmbV.Y - moonGeoV.Y * w, baryEmbV.Z - moonGeoV.Z * w);
        }
        return new BodyState(pos, vel, pos.Length, EphemerisSource.SwissEph, BodyStateFrame.BarycentricJ2000Equator);
    }

    /// <summary>
    /// Computes barycentric position of an inner / outer planet or major
    /// asteroid. Mirrors <c>sweplan()</c>'s "planet" branch (sweph.c:1940-1962):
    /// reads file content; if SEI_FLG_HELIO is set, adds barycentric Sun.
    /// Asteroids (SEI index ≥ SEI_ANYBODY) are stored heliocentric without the
    /// HELIO flag, so they always get the Sun added — mirrors the
    /// <c>ipl >= SEI_ANYBODY</c> branch in <c>sweph()</c> (sweph.c:2335-2348).
    /// </summary>
    private BodyState ComputePlanetBarycentric(CelestialBody body, JulianDay jdEt, bool inSpeed)
    {
        var seiBody = SwissEphFileLocator.MapToSeiInternalIndex(body);
        var (filePos, fileVel, planet) = ReadFromFileWithRecord(body, seiBody, jdEt, inSpeed);
        var isAsteroid = seiBody >= Se1FileFormat.SeiAnyBody;
        if ((planet.Flags & Se1FileFormat.FlagHelio) != 0 || isAsteroid)
        {
            // Convert heliocentric → barycentric by adding barycentric Sun.
            var (helEarth, helEarthV) = ReadFromFile(CelestialBody.Sun, Se1FileFormat.SeiSunBary, jdEt, inSpeed);
            var (baryEmb, baryEmbV) = ReadFromFile(CelestialBody.Sun, Se1FileFormat.SeiEmb, jdEt, inSpeed);
            var bary = new Vec3(
                filePos.X + (baryEmb.X - helEarth.X),
                filePos.Y + (baryEmb.Y - helEarth.Y),
                filePos.Z + (baryEmb.Z - helEarth.Z));
            var baryV = inSpeed
                ? new Vec3(
                    fileVel.X + (baryEmbV.X - helEarthV.X),
                    fileVel.Y + (baryEmbV.Y - helEarthV.Y),
                    fileVel.Z + (baryEmbV.Z - helEarthV.Z))
                : default;
            return new BodyState(bary, baryV, bary.Length, EphemerisSource.SwissEph, BodyStateFrame.BarycentricJ2000Equator);
        }
        return new BodyState(filePos, fileVel, filePos.Length, EphemerisSource.SwissEph, BodyStateFrame.BarycentricJ2000Equator);
    }

    private (Vec3 Pos, Vec3 Vel) ReadFromFile(CelestialBody body, int seiBody, JulianDay jdEt, bool inSpeed)
    {
        var (p, v, _) = ReadFromFileWithRecord(body, seiBody, jdEt, inSpeed);
        return (p, v);
    }

    private (Vec3 Pos, Vec3 Vel, Se1PlanetData Record) ReadFromFileWithRecord(CelestialBody body, int seiBody, JulianDay jdEt, bool inSpeed)
    {
        var reader = GetReaderForBlock(seiBody, jdEt);

        var planet = reader.FindPlanet(seiBody)
            ?? throw new UnsupportedBodyException(body, EphemerisSource.SwissEph);

        if (jdEt.Value < planet.JdStart || jdEt.Value > planet.JdEnd)
        {
            throw new EphemerisDateOutOfRangeException(
                jdEt,
                new JulianDay(planet.JdStart),
                new JulianDay(planet.JdEnd),
                EphemerisSource.SwissEph);
        }

        var iseg = (int)((jdEt.Value - planet.JdStart) / planet.SegmentLengthDays);
        if (iseg < 0) iseg = 0;
        if (iseg >= planet.SegmentCount) iseg = planet.SegmentCount - 1;

        var key = new SegmentKey(reader, seiBody, iseg);
        // Use the state-passing overload so the hot path doesn't allocate a closure.
        var loaderState = new SegmentLoaderState(reader, planet);
        var segment = _segmentCache.GetOrAdd(key, loaderState, _segmentLoader);

        var t = (jdEt.Value - segment.JdSegmentStart) / planet.SegmentLengthDays;
        t = t * 2.0 - 1.0;

        var coeffs = segment.Coefficients;
        var neval2 = segment.CoefficientsToEvaluate;
        var nco = segment.CoefficientsPerCoordinate;
        var px = ChebyshevSeries.Evaluate(t, new ReadOnlySpan<double>(coeffs, 0, neval2));
        var py = ChebyshevSeries.Evaluate(t, new ReadOnlySpan<double>(coeffs, nco, neval2));
        var pz = ChebyshevSeries.Evaluate(t, new ReadOnlySpan<double>(coeffs, 2 * nco, neval2));
        var pos = new Vec3(px, py, pz);
        var vel = default(Vec3);
        if (inSpeed)
        {
            var dseg = planet.SegmentLengthDays;
            var vx = ChebyshevSeries.EvaluateDerivative(t, new ReadOnlySpan<double>(coeffs, 0, neval2)) / dseg * 2.0;
            var vy = ChebyshevSeries.EvaluateDerivative(t, new ReadOnlySpan<double>(coeffs, nco, neval2)) / dseg * 2.0;
            var vz = ChebyshevSeries.EvaluateDerivative(t, new ReadOnlySpan<double>(coeffs, 2 * nco, neval2)) / dseg * 2.0;
            vel = new Vec3(vx, vy, vz);
        }

        return (pos, vel, planet);
    }

    private Se1FileReader OpenOrGetReader(string fullPath)
    {
        return _openFiles.GetOrAdd(fullPath, static p => new Se1FileReader(p));
    }

    /// <summary>
    /// Hot-path entry: returns the <see cref="Se1FileReader"/> for the
    /// (body, JD) pair without touching the path-string machinery on cache
    /// hit. The cache key is <c>(seiBody, century-block-id)</c>; on miss we
    /// fall back to the slow path that computes the filename, joins the
    /// ephemeris directory, and routes through <see cref="OpenOrGetReader"/>.
    /// Multiple <paramref name="seiBody"/> values that share a single .se1
    /// file (e.g. all the planets share <c>sepl_*</c>) end up with separate
    /// dictionary entries pointing at the same reader; that's intentional —
    /// it lets the lookup be a flat dictionary read with no extra
    /// indirection.
    /// </summary>
    private Se1FileReader GetReaderForBlock(int seiBody, JulianDay jdEt)
    {
        var blockId = SwissEphFileLocator.ComputeCenturyBlock(jdEt);
        var key = ((long)(uint)seiBody << 32) | (uint)blockId;
        if (_readersByBlock.TryGetValue(key, out var cached)) return cached;

        // Miss path — pay the string allocations once per (body, block).
        var fileName = SwissEphFileLocator.ComputeFileName(seiBody, jdEt);
        var fullPath = Path.Combine(_locator.EphePath, fileName);
        var reader = OpenOrGetReader(fullPath);
        _readersByBlock.TryAdd(key, reader);
        return reader;
    }

    /// <summary>
    /// State carried into the static segment-loader lambda. Using a struct
    /// here keeps the hot path closure-free.
    /// </summary>
    private readonly record struct SegmentLoaderState(Se1FileReader Reader, Se1PlanetData Planet);

    /// <summary>Static loader avoids a per-call closure allocation in <see cref="Compute"/>.</summary>
    private static readonly Func<SegmentKey, SegmentLoaderState, CachedSegment> _segmentLoader = LoadSegment;

    private static CachedSegment LoadSegment(SegmentKey key, SegmentLoaderState state)
    {
        var planet = state.Planet;
        var rawCoeffs = state.Reader.ReadSegment(planet, key.SegmentIndex);
        var jdSegStart = planet.JdStart + key.SegmentIndex * planet.SegmentLengthDays;
        var isMoon = planet.BodyId == Se1FileFormat.SeiMoon;
        var neval = SegmentRotation.RotateInPlace(planet, rawCoeffs, jdSegStart, isMoon);
        return new CachedSegment
        {
            Coefficients = rawCoeffs,
            JdSegmentStart = jdSegStart,
            JdSegmentEnd = jdSegStart + planet.SegmentLengthDays,
            CoefficientsPerCoordinate = planet.CoefficientCount,
            CoefficientsToEvaluate = neval,
        };
    }

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var reader in _openFiles.Values)
        {
            reader.Dispose();
        }
        _openFiles.Clear();
    }
}
