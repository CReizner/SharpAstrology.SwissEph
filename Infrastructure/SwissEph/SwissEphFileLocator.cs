// Ported from swisseph-master/swephlib.c:3610-3691 (swi_gen_filename).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using System.IO;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Domain.Time;
using SharpAstrology.SwissEphemerides.Domain.Time.Calendar;

namespace SharpAstrology.SwissEphemerides.Infrastructure.SwissEph;

/// <summary>
/// Maps <c>(SEI body number, JD)</c> to the matching SwissEphemeris file name.
/// Mirrors the C-side <c>swi_gen_filename</c> in
/// <c>swisseph-master/swephlib.c:3610-3691</c>. The locator is also used by
/// integration tests to enumerate every file shipped at
/// <c>swisseph-master/ephe/</c>.
/// </summary>
internal sealed class SwissEphFileLocator
{
    private readonly string _ephePath;

    /// <summary>
    /// Creates a locator rooted at <paramref name="ephePath"/> (typically
    /// <c>swisseph-master/ephe/</c>). The path is not checked for existence
    /// here; resolution failures surface from <see cref="Resolve"/>.
    /// </summary>
    public SwissEphFileLocator(string ephePath)
    {
        if (ephePath is null) throw new ArgumentNullException(nameof(ephePath));
        _ephePath = ephePath;
    }

    /// <summary>The root directory in which file lookups are performed.</summary>
    public string EphePath => _ephePath;

    /// <summary>
    /// Returns the leaf file name (e.g. <c>sepl_18.se1</c>) that contains
    /// data for <paramref name="body"/> at <paramref name="jdEt"/>. Does not
    /// touch the filesystem — pure name math.
    /// </summary>
    public static string ComputeFileName(CelestialBody body, JulianDay jdEt)
    {
        var seiBody = MapToSeiInternalIndex(body);
        return ComputeFileName(seiBody, jdEt);
    }

    /// <summary>Variant of <see cref="ComputeFileName(CelestialBody,JulianDay)"/> taking the SEI internal id directly.</summary>
    public static string ComputeFileName(int seiInternalIndex, JulianDay jdEt)
    {
        var prefix = SelectPrefix(seiInternalIndex);
        if (prefix is null)
        {
            throw new ArgumentException(
                $"SEI internal index {seiInternalIndex} has no .se1 file prefix.",
                nameof(seiInternalIndex));
        }
        var icty = ComputeCenturyBlock(jdEt);
        var bcad = icty < 0 ? "m" : "_";
        var absCty = System.Math.Abs(icty);
        return $"{prefix}{bcad}{absCty:D2}.{Se1FileFormat.FileSuffix}";
    }

    /// <summary>
    /// The 6-century block index that <see cref="ComputeFileName(int,JulianDay)"/>
    /// rounds <paramref name="jdEt"/> down to. Exposed for the segment-source
    /// hot-path cache: any two JDs that share a block id map to the same
    /// .se1 file for a given body, so the path-string allocation can be
    /// skipped on cache hits. The value is signed (negative blocks correspond
    /// to BCE files prefixed with <c>m</c>) and always a multiple of
    /// <see cref="Se1FileFormat.CenturiesPerFile"/>.
    /// </summary>
    public static int ComputeCenturyBlock(JulianDay jdEt)
    {
        // Mirrors the C selector at sweph.c#L4350-L4366. Pre-1582-10-15 the
        // proleptic Julian calendar applies; the threshold tjd ≥ 2305447.5
        // (1582-10-15 12:00 ET) flips us into Gregorian.
        var gregorian = jdEt.Value >= 2305447.5;
        var system = gregorian ? CalendarSystem.Gregorian : CalendarSystem.Julian;
        var cal = JulianDayConversion.ToCalendarDate(jdEt, system);
        var year = cal.Year;
        var sgn = year < 0 ? -1 : 1;
        var icty = year / 100;
        if (sgn < 0 && (year % 100) != 0) icty -= 1;
        while (icty % Se1FileFormat.CenturiesPerFile != 0) icty--;
        return icty;
    }

    /// <summary>
    /// Resolves the full path of the .se1 file containing <paramref name="body"/>
    /// at <paramref name="jdEt"/>, by combining <see cref="EphePath"/> with
    /// <see cref="ComputeFileName(CelestialBody,JulianDay)"/>.
    /// </summary>
    public string Resolve(CelestialBody body, JulianDay jdEt)
    {
        var name = ComputeFileName(body, jdEt);
        return Path.Combine(_ephePath, name);
    }

    /// <summary>
    /// Maps the public <see cref="CelestialBody"/> enum to the SEI internal
    /// body index used by the .se1 file format.
    /// </summary>
    public static int MapToSeiInternalIndex(CelestialBody body) => body switch
    {
        CelestialBody.Sun => Se1FileFormat.SeiSunBary,
        CelestialBody.Moon => Se1FileFormat.SeiMoon,
        CelestialBody.Mercury => Se1FileFormat.SeiMercury,
        CelestialBody.Venus => Se1FileFormat.SeiVenus,
        CelestialBody.Earth => Se1FileFormat.SeiEmb,
        CelestialBody.Mars => Se1FileFormat.SeiMars,
        CelestialBody.Jupiter => Se1FileFormat.SeiJupiter,
        CelestialBody.Saturn => Se1FileFormat.SeiSaturn,
        CelestialBody.Uranus => Se1FileFormat.SeiUranus,
        CelestialBody.Neptune => Se1FileFormat.SeiNeptune,
        CelestialBody.Pluto => Se1FileFormat.SeiPluto,
        CelestialBody.Chiron => Se1FileFormat.SeiChiron,
        CelestialBody.Pholus => Se1FileFormat.SeiPholus,
        CelestialBody.Ceres => Se1FileFormat.SeiCeres,
        CelestialBody.Pallas => Se1FileFormat.SeiPallas,
        CelestialBody.Juno => Se1FileFormat.SeiJuno,
        CelestialBody.Vesta => Se1FileFormat.SeiVesta,
        _ => throw new ArgumentOutOfRangeException(nameof(body), body, "no SwissEph file mapping"),
    };

    private static string? SelectPrefix(int seiBody) => seiBody switch
    {
        Se1FileFormat.SeiMoon => "semo",
        Se1FileFormat.SeiEmb /* SeiSun, SeiEarth alias */ => "sepl",
        Se1FileFormat.SeiMercury => "sepl",
        Se1FileFormat.SeiVenus => "sepl",
        Se1FileFormat.SeiMars => "sepl",
        Se1FileFormat.SeiJupiter => "sepl",
        Se1FileFormat.SeiSaturn => "sepl",
        Se1FileFormat.SeiUranus => "sepl",
        Se1FileFormat.SeiNeptune => "sepl",
        Se1FileFormat.SeiPluto => "sepl",
        Se1FileFormat.SeiSunBary => "sepl",
        Se1FileFormat.SeiCeres => "seas",
        Se1FileFormat.SeiPallas => "seas",
        Se1FileFormat.SeiJuno => "seas",
        Se1FileFormat.SeiVesta => "seas",
        Se1FileFormat.SeiChiron => "seas",
        Se1FileFormat.SeiPholus => "seas",
        _ => null,
    };
}
