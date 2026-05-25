// Ported from swisseph-master/swejpl.c — swi_pleph (line 362), state (line 652),
// interp (line 472). Original license: see LICENSE.SwissEph.txt.

using System;
using System.IO;
using System.Threading;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;
using SharpAstrology.SwissEphemerides.Domain.Time;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Jpl;

/// <summary>
/// Body-position source that reads JPL DE <c>.eph</c> files
/// (DE200/DE403/DE404/DE405/DE406/DE431/DE441 etc.). Implements
/// <see cref="IBodyPositionSource"/> for Sun, Moon, Earth, Mercury–Pluto and
/// the Earth-Moon barycenter.
/// </summary>
/// <remarks>
/// <para>
/// The returned <see cref="BodyState.Frame"/> is
/// <see cref="BodyStateFrame.BarycentricJ2000Equator"/> for Sun, Earth, EMB
/// and the planets, and <see cref="BodyStateFrame.GeocentricJ2000Equator"/>
/// for the Moon — matching the JPL DE native frame ("earth mean equator and
/// equinox of epoch", with planets either heliocentric or barycentric per
/// the <c>do_bary</c> flag — we always select <c>do_bary=true</c>).
/// </para>
/// <para>
/// Units: AU and AU/day. The on-disk values are km / day-fraction; we apply
/// the conversion <c>aufac = 1 / eh_au</c> exactly once during interpolation,
/// matching <c>swejpl.c:817-827</c>.
/// </para>
/// <para>
/// Frame corrections (precession to date, nutation, light-time,
/// aberration, gravitational deflection) live in the body service's
/// correction pipeline, not here.
/// </para>
/// </remarks>
internal sealed class JplBodyPositionSource : IBodyPositionSource, IDisposable
{
    /// <summary>Default LRU capacity (in segments) when none specified.</summary>
    public const int DefaultSegmentCacheCapacity = 32;

    private readonly JplFileReader _reader;
    private readonly JplSegmentCache _segmentCache;
    private readonly bool _ownsReader;
    private int _disposed;

    /// <inheritdoc />
    public EphemerisSource Kind => EphemerisSource.Jpl;

    /// <summary>The JPL DE number reported in the file header.</summary>
    public int DeNumber => _reader.Header.DeNumber;

    /// <summary>The shared segment cache. Public so benchmarks can read its hit-rate.</summary>
    public JplSegmentCache SegmentCache => _segmentCache;

    /// <summary>Convenience accessor for the parsed header.</summary>
    internal JplHeader Header => _reader.Header;

    /// <summary>
    /// Opens the file at <paramref name="filePath"/> and constructs a source.
    /// The file handle is closed by <see cref="Dispose"/>.
    /// </summary>
    public JplBodyPositionSource(string filePath, int segmentCacheCapacity = DefaultSegmentCacheCapacity)
    {
        if (filePath is null) throw new ArgumentNullException(nameof(filePath));
        _reader = new JplFileReader(filePath);
        _segmentCache = new JplSegmentCache(segmentCacheCapacity);
        _ownsReader = true;
    }

    /// <summary>
    /// Constructs a source over an externally-owned reader. The caller retains
    /// ownership of the reader; <see cref="Dispose"/> on this source does not
    /// close the file.
    /// </summary>
    internal JplBodyPositionSource(JplFileReader reader, int segmentCacheCapacity = DefaultSegmentCacheCapacity)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _segmentCache = new JplSegmentCache(segmentCacheCapacity);
        _ownsReader = false;
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
            or CelestialBody.Pluto => true,
        _ => false,
    };

    /// <inheritdoc />
    public BodyState Compute(CelestialBody body, JulianDay jdEt, EphemerisFlags flags)
    {
        if (!CanProvide(body))
            throw new UnsupportedBodyException(body, EphemerisSource.Jpl);
        var inSpeed = (flags & (EphemerisFlags.Speed | EphemerisFlags.Speed3)) != 0;

        var et = jdEt.Value;
        if (et < _reader.Header.JdStart || et > _reader.Header.JdEnd)
        {
            throw new EphemerisDateOutOfRangeException(
                jdEt,
                new JulianDay(_reader.Header.JdStart),
                new JulianDay(_reader.Header.JdEnd),
                EphemerisSource.Jpl);
        }

        // Map CelestialBody → JPL body index.
        // The Moon stays geocentric; everything else returns barycentric.
        switch (body)
        {
            case CelestialBody.Moon:
                {
                    var (pos, vel) = InterpolateBody(et, JplFileFormat.JplMoon, inSpeed);
                    return new BodyState(pos, vel, pos.Length, EphemerisSource.Jpl, BodyStateFrame.GeocentricJ2000Equator);
                }
            case CelestialBody.Sun:
                {
                    // Slot 10 (J_SUN) holds barycentric Sun coefficients on disk
                    // (the C code treats `pvsun` as a separate buffer scaled the
                    // same way). swejpl.c:823-827.
                    var (pos, vel) = InterpolateBody(et, JplFileFormat.JplSun, inSpeed);
                    return new BodyState(pos, vel, pos.Length, EphemerisSource.Jpl, BodyStateFrame.BarycentricJ2000Equator);
                }
            case CelestialBody.Earth:
                {
                    // True Earth = EMB − Moon / (emrat + 1). swejpl.c:436-438.
                    var (emb, embV) = InterpolateBody(et, JplFileFormat.JplEarth, inSpeed);
                    var (moon, moonV) = InterpolateBody(et, JplFileFormat.JplMoon, inSpeed);
                    var w = 1.0 / (_reader.Header.EarthMoonRatio + 1.0);
                    var pos = new Vec3(emb.X - moon.X * w, emb.Y - moon.Y * w, emb.Z - moon.Z * w);
                    var vel = inSpeed
                        ? new Vec3(embV.X - moonV.X * w, embV.Y - moonV.Y * w, embV.Z - moonV.Z * w)
                        : default;
                    return new BodyState(pos, vel, pos.Length, EphemerisSource.Jpl, BodyStateFrame.BarycentricJ2000Equator);
                }
            case CelestialBody.Mercury:
            case CelestialBody.Venus:
            case CelestialBody.Mars:
            case CelestialBody.Jupiter:
            case CelestialBody.Saturn:
            case CelestialBody.Uranus:
            case CelestialBody.Neptune:
            case CelestialBody.Pluto:
                {
                    var jplIdx = MapPlanetToJplIndex(body);
                    var (pos, vel) = InterpolateBody(et, jplIdx, inSpeed);
                    return new BodyState(pos, vel, pos.Length, EphemerisSource.Jpl, BodyStateFrame.BarycentricJ2000Equator);
                }
            default:
                throw new UnsupportedBodyException(body, EphemerisSource.Jpl);
        }
    }

    private static int MapPlanetToJplIndex(CelestialBody body) => body switch
    {
        CelestialBody.Mercury => JplFileFormat.JplMercury,
        CelestialBody.Venus => JplFileFormat.JplVenus,
        CelestialBody.Mars => JplFileFormat.JplMars,
        CelestialBody.Jupiter => JplFileFormat.JplJupiter,
        CelestialBody.Saturn => JplFileFormat.JplSaturn,
        CelestialBody.Uranus => JplFileFormat.JplUranus,
        CelestialBody.Neptune => JplFileFormat.JplNeptune,
        CelestialBody.Pluto => JplFileFormat.JplPluto,
        _ => throw new ArgumentOutOfRangeException(nameof(body)),
    };

    /// <summary>
    /// Loads the segment record for <paramref name="et"/> (caches on miss),
    /// then evaluates the Chebyshev sub-interval for body
    /// <paramref name="jplIndex"/>. Returns AU and AU/day.
    /// </summary>
    private (Vec3 Pos, Vec3 Vel) InterpolateBody(double et, int jplIndex, bool inSpeed)
    {
        var hdr = _reader.Header;

        // Mirrors swejpl.c:783-797: "midnight before epoch" / "fraction since".
        var s = et - 0.5;
        var etMn = Math.Floor(s);
        var etFr = s - etMn;
        etMn += 0.5;

        var nrZero = (int)((etMn - hdr.JdStart) / hdr.SegmentLengthDays);
        if (etMn == hdr.JdEnd) nrZero--; // end-point edge case (swejpl.c:795).
        if (nrZero < 0) nrZero = 0;
        if (nrZero >= hdr.SegmentCount) nrZero = hdr.SegmentCount - 1;

        var key = new JplSegmentKey(_reader, nrZero);
        var loaderState = new SegmentLoaderState(_reader);
        var seg = _segmentCache.GetOrAdd(key, loaderState, _segmentLoader);

        // t in [0..1] across the segment (swejpl.c:797).
        var segStartJd = nrZero * hdr.SegmentLengthDays + hdr.JdStart;
        var t = (etMn - segStartJd + etFr) / hdr.SegmentLengthDays;

        var ptr = hdr.BodyPointers[jplIndex];
        var bufStart = ptr.BufferOffset - 1; // ipt is 1-based.
        var ncf = ptr.CoefficientsPerComponent;
        var na = ptr.IntervalsPerSegment;
        const int ncm = 3; // x, y, z

        return InterpolateChebyshev(seg.Buffer, bufStart, t, hdr.SegmentLengthDays, ncf, ncm, na, inSpeed, hdr.AstronomicalUnitKm);
    }

    /// <summary>
    /// Closure-free wrapper passing the reader through to the static loader.
    /// </summary>
    private readonly record struct SegmentLoaderState(JplFileReader Reader);

    private static readonly Func<JplSegmentKey, SegmentLoaderState, JplCachedSegment> _segmentLoader = LoadSegment;

    private static JplCachedSegment LoadSegment(JplSegmentKey key, SegmentLoaderState state)
    {
        var buf = state.Reader.ReadSegment(key.SegmentIndex);
        return new JplCachedSegment
        {
            Buffer = buf,
            JdSegmentStart = buf[0],
            JdSegmentEnd = buf[1],
        };
    }

    /// <summary>
    /// Interp routine: evaluates Chebyshev polynomial coefficients for one
    /// body block. Mirrors <c>interp</c> in <c>swejpl.c:472-591</c> with
    /// <c>ifl=1</c> (position) or <c>ifl=2</c> (position + velocity).
    /// </summary>
    /// <remarks>
    /// Hot path: uses <c>stackalloc Span&lt;double&gt;</c> for the 21 + 21
    /// Chebyshev recurrence working buffers (max ncf = 14 in DE405). Returns
    /// AU / (AU/day) — the unit conversion happens here, not in the caller.
    /// </remarks>
    private static (Vec3 Pos, Vec3 Vel) InterpolateChebyshev(
        double[] buf,
        int bufStart,
        double t,
        double segmentDays,
        int ncf,
        int ncm,
        int na,
        bool inSpeed,
        double auKm)
    {
        // dt1 = floor(t); ni = sub-interval index; tc = chebyshev time in [-1,+1]
        var dt1 = (t >= 0) ? Math.Floor(t) : -Math.Floor(-t);
        var temp = na * t;
        var ni = (int)(temp - dt1);
        if (ni < 0) ni = 0;
        if (ni >= na) ni = na - 1;
        var tc = ((temp % 1.0) + dt1) * 2.0 - 1.0;
        if (tc < -1.0) tc = -1.0;
        else if (tc > 1.0) tc = 1.0;

        // ----- Build pc[0..ncf) — Tn(tc) values via Chebyshev recurrence.
        Span<double> pc = stackalloc double[Math.Max(ncf, 4)];
        pc[0] = 1.0;
        pc[1] = tc;
        var twot = tc + tc;
        for (var i = 2; i < ncf; i++)
            pc[i] = twot * pc[i - 1] - pc[i - 2];

        // ----- Position: pv[k] = Σⱼ pc[j] * buf[bufStart + j + (k + ni*ncm) * ncf]
        Span<double> pv = stackalloc double[ncm];
        for (var k = 0; k < ncm; k++)
        {
            double sum = 0;
            var coeffBase = bufStart + (k + ni * ncm) * ncf;
            for (var j = ncf - 1; j >= 0; j--)
                sum += pc[j] * buf[coeffBase + j];
            pv[k] = sum;
        }

        // Convert km → AU.
        var aufac = 1.0 / auKm;
        var posVec = new Vec3(pv[0] * aufac, pv[1] * aufac, pv[2] * aufac);

        if (!inSpeed) return (posVec, default);

        // ----- Velocity coefficients: vc[0]=0, vc[1]=1, vc[2]=2*twot,
        // vc[i] = twot*vc[i-1] + 2*pc[i-1] - vc[i-2]   (swejpl.c:540-545).
        Span<double> vc = stackalloc double[Math.Max(ncf, 4)];
        vc[0] = 0.0;
        vc[1] = 1.0;
        if (ncf >= 3) vc[2] = twot + twot;
        for (var i = 3; i < ncf; i++)
            vc[i] = twot * vc[i - 1] + pc[i - 1] + pc[i - 1] - vc[i - 2];

        // bma = (na + na) / intv ; intv = segmentDays (swejpl.c:540)
        var bma = (na + na) / segmentDays;
        Span<double> vel = stackalloc double[ncm];
        for (var k = 0; k < ncm; k++)
        {
            double sum = 0;
            var coeffBase = bufStart + (k + ni * ncm) * ncf;
            for (var j = ncf - 1; j >= 1; j--)
                sum += vc[j] * buf[coeffBase + j];
            vel[k] = sum * bma;
        }
        var velVec = new Vec3(vel[0] * aufac, vel[1] * aufac, vel[2] * aufac);
        return (posVec, velVec);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsReader) _reader.Dispose();
    }
}
