// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SharpAstrology.Enums;
using SharpAstrology.Interfaces;
using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Sidereal;

namespace SharpAstrology.Ephemerides;

/// <summary>
///
/// </summary>
public sealed class SwissEphemeridesService
{
    private readonly string? _rootPathToEph;
    private readonly EphType _ephType;

    /// <summary>
    /// Creates the SwissEphemerides factory.
    /// </summary>
    /// <param name="rootPathToEph">
    ///     Directory containing the ephemeris files (<c>.se1</c> for
    ///     <see cref="EphType.Swiss"/>, <c>.eph</c> for <see cref="EphType.Jpl"/>).
    ///     If <see langword="null"/>, defaults to
    ///     <c>{AppContext.BaseDirectory}/ephe</c>. Ignored for
    ///     <see cref="EphType.Moshier"/>.
    /// </param>
    /// <param name="ephType">Selects the underlying ephemeris source.</param>
    public SwissEphemeridesService(string? rootPathToEph = null, EphType ephType = EphType.Swiss)
    {
        _rootPathToEph = rootPathToEph;
        _ephType = ephType;
    }

    /// <summary>
    /// Builds a configured <see cref="IEphemerides"/> instance. The caller
    /// owns the returned instance and must dispose it when finished.
    /// </summary>
    /// <param name="ayanamsa">Sidereal projection to use when callers request <see cref="EphCalculationMode.Sidereal"/> — has no effect on tropical computations. Defaults to Fagan/Bradley.</param>
    /// <exception cref="ArgumentException"><see cref="EphType"/> is not one of the defined values.</exception>
    /// <exception cref="DirectoryNotFoundException"><see cref="EphType.Jpl"/> is requested but the resolved directory does not exist.</exception>
    /// <exception cref="FileNotFoundException"><see cref="EphType.Jpl"/> is requested but no <c>*.eph</c> file is present in the resolved directory.</exception>
    public IEphemerides CreateContext(Ayanamsas ayanamsa = Ayanamsas.FagenBradley)
    {
        var builder = new EphemerisContextBuilder();

        switch (_ephType)
        {
            case EphType.Moshier:
                // Moshier is the analytical default — no file source needed.
                break;
            case EphType.Swiss:
                builder.UseSwissEphFiles(ResolveEphRoot());
                break;
            case EphType.Jpl:
                var dir = ResolveEphRoot();
                builder.UseSwissEphFiles(dir);
                builder.UseJplFile(ResolveJplFile(dir));
                break;
            default:
                throw new ArgumentException(
                    $"Unknown EphType: {_ephType}.", nameof(_ephType));
        }

        builder.UseSiderealMode(MapAyanamsa(ayanamsa));

        return new SwissEphemerides(builder.Build(), ownsContext: true);
    }

    private string ResolveEphRoot() =>
        _rootPathToEph ?? Path.Join(AppContext.BaseDirectory, "ephe");

    /// <summary>
    /// Locates the JPL DE binary inside <paramref name="dir"/>. Selects the
    /// file with the largest numeric component in its name — for a typical
    /// install containing <c>de421.eph</c>, <c>de431.eph</c> and
    /// <c>de441.eph</c>, this resolves to <c>de441.eph</c>, matching the
    /// "use the newest kernel" intuition users expect.
    /// </summary>
    internal static string ResolveJplFile(string dir)
    {
        if (!Directory.Exists(dir))
        {
            throw new DirectoryNotFoundException(
                $"JPL ephemeris directory not found: {dir}");
        }

        var ranked = Directory
            .EnumerateFiles(dir, "*.eph", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: path, Number: ExtractLeadingNumber(Path.GetFileName(path))))
            .Where(x => x.Number.HasValue)
            .OrderByDescending(x => x.Number!.Value)
            .ToList();

        if (ranked.Count == 0)
        {
            throw new FileNotFoundException(
                $"No JPL .eph file found in directory '{dir}'. " +
                "Expected files named like de441.eph, de431.eph, de421.eph.",
                Path.Join(dir, "*.eph"));
        }

        return ranked[0].Path;
    }

    internal static int? ExtractLeadingNumber(string filename)
    {
        var match = Regex.Match(filename, @"\d+");
        return match.Success && int.TryParse(match.Value, out var n) ? n : null;
    }

    /// <summary>
    /// <see cref="Ayanamsas"/> (Base) → <see cref="SiderealMode"/>. The two
    /// enums share the same integer layout for 0..46 (both ports of the
    /// C-side <c>SE_SIDM_*</c> macros); only <see cref="Ayanamsas.UserDefined"/>
    /// (sequentially 47) needs the explicit 255 remap.
    /// </summary>
    private static SiderealMode MapAyanamsa(Ayanamsas ayanamsa) =>
        ayanamsa == Ayanamsas.UserDefined
            ? SiderealMode.UserDefined
            : (SiderealMode)(int)ayanamsa;
}
