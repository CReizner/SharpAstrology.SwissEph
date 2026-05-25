// Ported from swisseph-master/sweph.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Source: sweph.c
//   fixstar_format_search_name        — lines 6154-6174
//   fixstar_cut_string                — lines 6211-6306
//   load_all_fixed_stars              — lines 6324-6395
//   search_star_in_list               — lines 6674-6748
//   get_builtin_star                  — lines 6750-6803

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using SharpAstrology.SwissEphemerides.Application.Stars;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Stars;

namespace SharpAstrology.SwissEphemerides.Infrastructure.Stars;

/// <summary>
/// Lazy parser for the Swiss Ephemeris fixed-star catalogue file
/// (<c>sefstars.txt</c>). Implements the three lookup paths supported by
/// the C library: traditional name (case-insensitive, whitespace-stripped),
/// Bayer/Flamsteed designation (preceded by <c>,</c>, case-sensitive after
/// the comma) and 1-based sequential index over the alphabetically sorted
/// records. The hard-coded built-in stars from
/// <c>get_builtin_star</c> are matched <i>before</i> the file lookup, so
/// they remain available even when the catalogue file is missing.
/// </summary>
/// <remarks>
/// <para>The reader is allocation-free in the hot path after the first
/// successful lookup: file parsing happens once inside a
/// <see cref="Lazy{T}"/> with
/// <see cref="LazyThreadSafetyMode.ExecutionAndPublication"/>, after
/// which all further requests are O(1) dictionary hits or O(1) list
/// indexing.</para>
/// <para>The reader is independent of the <c>swed.is_old_starfile</c>
/// branch (<c>SE_STARFILE_OLD = fixstars.cat</c>): the caller decides which
/// stream to provide. The unit-conversion logic, however, mirrors the
/// modern <c>sefstars.txt</c> path (pm × 0.1 ″/yr scaling and parallax in
/// 0.001″). Old-file scaling is intentionally out of scope because no
/// supported user case feeds it in.</para>
/// <para><see langword="internal"/> by design: external callers wire a
/// catalogue up through
/// <see cref="SharpAstrology.SwissEphemerides.EphemerisContextBuilder.UseFixedStarCatalog(string)"/>
/// or its <see cref="System.IO.Stream"/>-factory overload; the reader
/// itself is an implementation detail of that surface.</para>
/// </remarks>
internal sealed class FixedStarCatalogReader : IFixedStarCatalog
{
    /// <summary>
    /// Standard catalogue file name shipped with Swiss Ephemeris. Mirrors
    /// <c>SE_STARFILE</c> (<c>swephexp.h#L173</c>).
    /// </summary>
    public const string DefaultFileName = "sefstars.txt";

    private readonly Func<Stream> _streamFactory;
    private readonly Lazy<ParsedCatalogue> _parsed;

    /// <summary>
    /// Creates a reader bound to the file at <paramref name="path"/>. The
    /// file is opened lazily on the first lookup. Subsequent lookups reuse
    /// the in-memory dictionaries.
    /// </summary>
    /// <param name="path">Filesystem path to <c>sefstars.txt</c>.</param>
    public FixedStarCatalogReader(string path)
        : this(() => File.OpenRead(path ?? throw new ArgumentNullException(nameof(path))))
    {
    }

    /// <summary>
    /// Creates a reader bound to a generic <see cref="Stream"/> factory.
    /// The factory is invoked exactly once, on the first lookup. Useful for
    /// embedded resources or test inputs.
    /// </summary>
    /// <param name="streamFactory">Factory delegate that yields a readable
    /// <see cref="Stream"/> over the catalogue contents. The reader takes
    /// ownership of the returned stream and disposes it after parsing.</param>
    public FixedStarCatalogReader(Func<Stream> streamFactory)
    {
        _streamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
        _parsed = new Lazy<ParsedCatalogue>(Parse, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Number of distinct Bayer/Flamsteed records loaded from the catalogue
    /// file (<c>swed.n_fixstars_real</c> in C). Triggers parsing on first
    /// access.
    /// </summary>
    public int Count => _parsed.Value.SortedAll.Count;

    /// <summary>
    /// True if the catalogue has already been parsed. False until the first
    /// successful lookup. Useful for unit-testing the lazy contract.
    /// </summary>
    public bool IsLoaded => _parsed.IsValueCreated;

    /// <summary>
    /// Resolves a search string (any of the three formats supported by the
    /// C library) to a star record. Returns <c>false</c> when no entry
    /// matches; the C library would emit a <c>swe_fixstar()</c>-prefixed
    /// error string in that case.
    /// </summary>
    /// <param name="searchName">User-supplied star name. May contain
    /// whitespace; may begin with <c>,</c> for a Bayer search; may be a
    /// purely numeric sequential index. Must not be null.</param>
    /// <param name="result">On success: the matched <see cref="FixedStar"/>
    /// together with the canonical <c>"trad,bayer"</c> name.</param>
    /// <returns>True on a successful match.</returns>
    public bool TryFind(string searchName, out FixedStarMatch result)
    {
        if (searchName is null) throw new ArgumentNullException(nameof(searchName));
        result = default;

        // Built-in catalogue (sweph.c#L6750-L6803). The C lookup uses the
        // ORIGINAL input string (case-sensitive starts/contains checks),
        // not the normalised one — we preserve this because some entries
        // (e.g. Spica) are also present in the file but the builtin path
        // takes precedence.
        if (TryGetBuiltin(searchName, out var builtin))
        {
            result = builtin;
            return true;
        }

        var normalised = NormalizeSearchKey(searchName);
        if (normalised.Length == 0)
            return false;

        var parsed = _parsed.Value;

        // Index lookup. Mirrors the isdigit() branch at sweph.c#L6684-L6699.
        if (IsAllDigits(normalised))
        {
            if (!int.TryParse(normalised, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                return false;
            if (idx < 1 || idx > parsed.SortedAll.Count)
                return false;
            var rec = parsed.SortedAll[idx - 1];
            result = new FixedStarMatch(rec.Star, rec.CanonicalName);
            return true;
        }

        // Bayer-search branch.
        if (normalised[0] == ',')
        {
            if (parsed.ByBayer.TryGetValue(normalised, out var rec))
            {
                result = new FixedStarMatch(rec.Star, rec.CanonicalName);
                return true;
            }
            return false;
        }

        // Traditional-name with embedded comma → strip everything from the
        // comma onwards and bayer-lookup. sweph.c#L6687-L6691.
        var commaIdx = normalised.IndexOf(',');
        if (commaIdx >= 0)
        {
            var bayerKey = normalised.Substring(commaIdx);
            if (parsed.ByBayer.TryGetValue(bayerKey, out var bRec))
            {
                result = new FixedStarMatch(bRec.Star, bRec.CanonicalName);
                return true;
            }
            return false;
        }

        // Pure traditional-name lookup.
        if (parsed.ByTraditionalName.TryGetValue(normalised, out var tRec))
        {
            result = new FixedStarMatch(tRec.Star, tRec.CanonicalName);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Convenience wrapper that throws when the lookup fails; mirrors the
    /// C library's <c>ERR</c> return surface. Use <see cref="TryFind"/> in
    /// non-throwing flows.
    /// </summary>
    /// <param name="searchName">Star name, Bayer designation, or 1-based sequential index.</param>
    /// <returns>The matched record + canonical name.</returns>
    /// <exception cref="KeyNotFoundException">When the search name does not match any record.</exception>
    public FixedStarMatch Find(string searchName)
    {
        if (!TryFind(searchName, out var result))
            throw new KeyNotFoundException(
                $"Fixed star \"{searchName}\" not found in catalogue.");
        return result;
    }

    /// <summary>
    /// Normalises a user-supplied search string per
    /// <c>fixstar_format_search_name</c> (<c>sweph.c#L6154-L6174</c>):
    /// strips ASCII spaces and lowercases everything <i>before</i> the
    /// first comma. Public for unit-testing the C-source parity contract.
    /// </summary>
    /// <param name="raw">Original search string.</param>
    /// <returns>Normalised search key.</returns>
    public static string NormalizeSearchKey(string raw)
    {
        if (raw is null) throw new ArgumentNullException(nameof(raw));
        if (raw.Length == 0) return string.Empty;

        // The C function additionally truncates to SWI_STAR_LENGTH (40); we
        // mirror that to keep search semantics identical.
        var working = raw.Length > SwiStarLength
            ? raw.Substring(0, SwiStarLength)
            : raw;

        var sb = new StringBuilder(working.Length);
        var seenComma = false;
        foreach (var ch in working)
        {
            if (ch == ' ') continue;
            if (!seenComma)
            {
                sb.Append(char.ToLowerInvariant(ch));
                if (ch == ',') seenComma = true;
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private const int SwiStarLength = 40;

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0) return false;
        foreach (var ch in s)
            if (ch < '0' || ch > '9') return false;
        return true;
    }

    /// <summary>
    /// Built-in star table. Mirrors <c>get_builtin_star</c>
    /// (<c>sweph.c#L6750-L6803</c>) verbatim, including the silent override
    /// of records that also exist in the file (Spica, Mula, Pushya, etc.).
    /// </summary>
    private static bool TryGetBuiltin(string original, out FixedStarMatch result)
    {
        result = default;
        if (original.Length == 0) return false;

        // strncmp(star, "spica", 5)==0 || strncmp(star, "Spica", 5)==0
        if (StartsWith(original, "spica", caseSensitive: true) || StartsWith(original, "Spica", caseSensitive: true))
        {
            result = ParseBuiltin("Spica,alVir,ICRS,13,25,11.57937,-11,09,40.7501,-42.35,-30.67,1,13.06,0.97");
            return true;
        }
        // ,zePsc OR revati|Revati prefix
        if (Contains(original, ",zePsc") || StartsWith(original, "revati", caseSensitive: true) || StartsWith(original, "Revati", caseSensitive: true))
        {
            result = ParseBuiltin("Revati,zePsc,ICRS,01,13,43.88735,+07,34,31.2745,145,-55.69,15,18.76,5.187");
            return true;
        }
        // ,deCnc OR pushya|Pushya prefix
        if (Contains(original, ",deCnc") || StartsWith(original, "pushya", caseSensitive: true) || StartsWith(original, "Pushya", caseSensitive: true))
        {
            result = ParseBuiltin("Pushya,deCnc,ICRS,08,44,41.09921,+18,09,15.5034,-17.67,-229.26,17.14,24.98,3.94");
            return true;
        }
        // ,laSco OR mula|Mula prefix
        if (Contains(original, ",laSco") || StartsWith(original, "mula", caseSensitive: true) || StartsWith(original, "Mula", caseSensitive: true))
        {
            result = ParseBuiltin("Mula,laSco,ICRS,17,33,36.52012,-37,06,13.7648,-8.53,-30.8,-3,5.71,1.62");
            return true;
        }
        if (Contains(original, ",SgrA*"))
        {
            result = ParseBuiltin("Gal. Center,SgrA*,2000,17,45,40.03599,-29,00,28.1699,-2.755718425,-5.547,0.0,0.125,999.99");
            return true;
        }
        if (Contains(original, ",GP1958"))
        {
            result = ParseBuiltin("Gal. Pole IAU1958,GP1958,1950,12,49,0.0,27,24,0.0,0.0,0.0,0.0,0.0,0.0");
            return true;
        }
        if (Contains(original, ",GPol"))
        {
            result = ParseBuiltin("Gal. Pole,GPol,ICRS,12,51,36.7151981,27,06,11.193172,0.0,0.0,0.0,0.0,0.0");
            return true;
        }
        return false;
    }

    private static bool StartsWith(string s, string prefix, bool caseSensitive)
    {
        if (s.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            var a = s[i];
            var b = prefix[i];
            if (caseSensitive)
            {
                if (a != b) return false;
            }
            else
            {
                if (char.ToLowerInvariant(a) != char.ToLowerInvariant(b)) return false;
            }
        }
        return true;
    }

    private static bool Contains(string s, string substring) => s.IndexOf(substring, StringComparison.Ordinal) >= 0;

    private ParsedCatalogue Parse()
    {
        Stream stream;
        try
        {
            stream = _streamFactory();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Fixed-star catalogue stream factory threw on first access.", ex);
        }
        if (stream is null)
            throw new InvalidOperationException("Fixed-star catalogue stream factory returned null.");

        var byTraditional = new Dictionary<string, CatalogueRecord>(StringComparer.Ordinal);
        var byBayer = new Dictionary<string, CatalogueRecord>(StringComparer.Ordinal);
        var allBayer = new List<CatalogueRecord>(capacity: 1100);

        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            string? line;
            string lastBayerSkey = string.Empty;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                if (line[0] == '#') continue;
                if (line[0] == '\r') continue;
                if (line[0] == '\n') continue;
                if (!TryParseRecord(line, out var rec)) continue;

                // (a) traditional-name index — only if record carries a trad name.
                if (!string.IsNullOrEmpty(rec.Star.TraditionalName))
                {
                    var tradKey = StripWhitespaceAndLower(rec.Star.TraditionalName);
                    if (tradKey.Length > 0 && !byTraditional.ContainsKey(tradKey))
                    {
                        byTraditional[tradKey] = rec;
                    }
                }
                // (b) Bayer-key record — only on first occurrence of each
                // Bayer designation. Mirrors sweph.c#L6372-L6373:
                //   if (strcmp(starbayer, last_starbayer) == 0) continue;
                var bayerKey = "," + StripWhitespace(rec.Star.BayerDesignation);
                if (!StringEquals(rec.Star.BayerDesignation, lastBayerSkey))
                {
                    if (!byBayer.ContainsKey(bayerKey))
                    {
                        byBayer[bayerKey] = rec;
                    }
                    allBayer.Add(rec);
                    lastBayerSkey = rec.Star.BayerDesignation;
                }
            }
        }

        // Index lookup is 1-based into the post-sort array. The C library
        // sorts by skey (string compare). For the index path we need only
        // the Bayer entries (sweph.c#L6692-L6699 checks against
        // n_fixstars_real). Stable sort by Bayer-skey to mirror C qsort
        // (qsort is unstable but the catalogue file has no skey duplicates
        // so the result is identical to a stable sort).
        allBayer.Sort((a, b) => string.CompareOrdinal("," + StripWhitespace(a.Star.BayerDesignation), "," + StripWhitespace(b.Star.BayerDesignation)));

        return new ParsedCatalogue(byTraditional, byBayer, allBayer);
    }

    private static bool StringEquals(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>
    /// Parses one CSV record from the catalogue file. Mirrors
    /// <c>fixstar_cut_string</c> (<c>sweph.c#L6211-L6306</c>) including the
    /// proper-motion / parallax / radial-velocity unit conversions.
    /// Returns false when the record is malformed (less than 14 fields).
    /// </summary>
    private static bool TryParseRecord(string line, out CatalogueRecord rec)
    {
        rec = default;
        var fields = line.Split(',');
        if (fields.Length < 14) return false;

        var traditionalRaw = fields[0].TrimEnd();
        var bayerRaw = fields[1].TrimEnd();
        // C truncates to SWI_STAR_LENGTH (40). The traditional-name limit
        // is 40 chars; bayer is limited to 39 (one byte for the leading
        // comma). We mirror the truncation.
        var traditional = TruncateLeftTrim(traditionalRaw, SwiStarLength);
        var bayer = TruncateLeftTrim(bayerRaw, SwiStarLength - 1);

        var canonicalName = string.Concat(traditional, ",", bayer);

        if (!TryAtof(fields[2], out var epochRaw)) return false;
        if (!TryAtof(fields[3], out var raH)) return false;
        if (!TryAtof(fields[4], out var raM)) return false;
        if (!TryAtof(fields[5], out var raS)) return false;
        if (!TryAtof(fields[6], out var deD)) return false;
        if (!TryAtof(fields[7], out var deM)) return false;
        if (!TryAtof(fields[8], out var deS)) return false;
        if (!TryAtof(fields[9], out var raPm)) return false;
        if (!TryAtof(fields[10], out var dePm)) return false;
        if (!TryAtof(fields[11], out var radv)) return false;
        if (!TryAtof(fields[12], out var parall)) return false;
        if (parall < 0) parall = -parall; // sweph.c#L6260 — fixes the old Rasalgheti bug.
        if (!TryAtof(fields[13], out var mag)) return false;

        // RA in degrees: (s/3600 + m/60 + h) * 15.
        var raDeg = (raS / 3600.0 + raM / 60.0 + raH) * 15.0;

        // Declination in degrees. Sign carries through the degrees field;
        // fields[6] may be "+07" / "-37" / "07" — atof picks up sign.
        // sweph.c#L6253-L6271 inspects the original string for '-'.
        var deNegative = fields[6].IndexOf('-') >= 0;
        var deDeg = deNegative
            ? -deS / 3600.0 - deM / 60.0 + deD  // deD is already negative
            : deS / 3600.0 + deM / 60.0 + deD;

        // Modern starfile path (is_old_starfile == FALSE):
        //   ra_pm /= 10.0 / 3600.0   → degrees per Julian century
        //   de_pm /= 10.0 / 3600.0
        //   parallax /= 1000.0       → 0.001″ → arcsec
        var raPmDeg = raPm / 10.0 / 3600.0;
        var dePmDeg = dePm / 10.0 / 3600.0;
        parall /= 1000.0;

        // Parallax: catalogue values >1 are encoded as distance in parsec
        // (1/parallax); ≤1 are pre-computed parallax in arcsec.
        if (parall > 1)
            parall = 1.0 / parall / 3600.0;
        else
            parall /= 3600.0;

        const double KmSToAuPerCty = 21.095; // sweph.h#L297 KM_S_TO_AU_CTY
        var radvAuCty = radv * KmSToAuPerCty;

        var degToRad = AstronomicalConstants.DegToRad;
        var raRad = raDeg * degToRad;
        var deRad = deDeg * degToRad;
        var raPmRad = raPmDeg * degToRad;
        var dePmRad = dePmDeg * degToRad;
        var parallRad = parall * degToRad;

        // sweph.c#L6295: ra_pm /= cos(de) — the catalogue's RA proper
        // motion is "pm cos δ" (a great-circle rate); the calc pipeline
        // wants a raw RA-rate, so divide.
        raPmRad /= System.Math.Cos(deRad);

        var epoch = MapEpoch(epochRaw, fields[2]);
        var star = new FixedStar(
            traditional, bayer, epoch,
            raRad, deRad,
            raPmRad, dePmRad,
            radvAuCty, parallRad,
            mag);
        rec = new CatalogueRecord(star, canonicalName);
        return true;
    }

    private static string TruncateLeftTrim(string s, int max)
    {
        var t = s.TrimStart();
        if (t.Length > max) t = t.Substring(0, max);
        return t;
    }

    private static FixedStarEpoch MapEpoch(double rawAtof, string fieldText)
    {
        var trimmed = fieldText.Trim();
        if (rawAtof >= 1949.5 && rawAtof <= 1950.5)
            return FixedStarEpoch.B1950;
        if (rawAtof >= 1999.5 && rawAtof <= 2000.5)
            return FixedStarEpoch.J2000;
        // C's atof("ICRS") == 0 → ICRS branch.
        if (rawAtof == 0.0 && string.Equals(trimmed, "ICRS", StringComparison.OrdinalIgnoreCase))
            return FixedStarEpoch.Icrs;
        return rawAtof switch
        {
            1950.0 => FixedStarEpoch.B1950,
            2000.0 => FixedStarEpoch.J2000,
            _ => FixedStarEpoch.Icrs,
        };
    }

    private static bool TryAtof(string field, out double value)
    {
        // Mirror C's atof: read leading whitespace, then sign+digits, accept
        // partial parse. We use the relaxed Double.TryParse with invariant
        // culture; non-numeric fields return 0.0 like atof.
        var s = field.Trim();
        if (s.Length == 0) { value = 0.0; return true; }
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;
        // Try a partial scan: take the leading numeric prefix.
        var i = 0;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        var hasDot = false;
        var start = i;
        while (i < s.Length)
        {
            var c = s[i];
            if (c >= '0' && c <= '9') { i++; continue; }
            if (c == '.' && !hasDot) { hasDot = true; i++; continue; }
            break;
        }
        if (i == start) { value = 0.0; return true; }
        return double.TryParse(s.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Whitespace-strip + ASCII lowercase; the same form C uses for its
    /// traditional-name skey (<c>load_all_fixed_stars</c>
    /// <c>sweph.c#L6362-L6367</c>).
    /// </summary>
    private static string StripWhitespaceAndLower(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == ' ') continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string StripWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch == ' ') continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static FixedStarMatch ParseBuiltin(string srecord)
    {
        if (!TryParseRecord(srecord, out var rec))
            throw new InvalidOperationException(
                $"Built-in star record could not be parsed: {srecord}");
        return new FixedStarMatch(rec.Star, rec.CanonicalName);
    }

    private readonly record struct CatalogueRecord(FixedStar Star, string CanonicalName);

    private sealed record ParsedCatalogue(
        Dictionary<string, CatalogueRecord> ByTraditionalName,
        Dictionary<string, CatalogueRecord> ByBayer,
        List<CatalogueRecord> SortedAll);
}
