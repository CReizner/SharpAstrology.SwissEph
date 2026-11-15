using SharpAstrology.SwissEphemerides;
using SharpAstrology.SwissEphemerides.Application.Bodies;
using SharpAstrology.SwissEphemerides.Application.Sidereal;
using SharpAstrology.SwissEphemerides.Domain.Time;
using Xunit;

namespace SharpAstrology.SwissEph.Tests;

/// <summary>
/// The sidereal longitudes of the six derived lunar points, pinned against the Astrodienst C
/// library itself. <see cref="SiderealLunarPointTests"/> proves the internal property that
/// sidereal is tropical minus ayanamsa. That property would also hold if both values were wrong
/// in the same way, so these values come from the outside.
/// </summary>
/// <remarks>
/// <para>
/// Reference program, linked against the unmodified C sources in
/// <c>Old/Experiments.RewriteSwissEph/swisseph-master</c>:
/// <c>swe_set_ephe_path(ephe); swe_set_sid_mode(mode, 0, 0);
/// swe_calc(jd, body, SEFLG_SWIEPH | SEFLG_SPEED | SEFLG_SIDEREAL, x, serr)</c>, reading
/// <c>x[0]</c>. The julian day is Ephemeris Time in both programs, so no delta T enters the
/// comparison and the two really are evaluated at the same instant.
/// </para>
/// <para>
/// Tolerance is a fifth of an arcsecond, not the 1e-9 degrees the raw file readers reach. The
/// residual is not in the sidereal step. It is already present in the tropical longitude of the
/// same body, and it is largest for the osculating apogee at 0.09 arcseconds, because that point
/// is computed from osculating elements of the moon. The sidereal projection itself agrees with
/// the C library to better than 2e-7 arcseconds, measured as the difference between the sidereal
/// and the tropical deviation. A fifth of an arcsecond still catches the defect this file exists
/// for: before the fix the sidereal flag did nothing on these three branches, which is an error
/// of a whole ayanamsa, some twenty-four degrees.
/// </para>
/// </remarks>
public class SiderealLunarPointGoldenTests
{
    private const double FifthOfAnArcsecond = 0.2 / 3600.0;

    /// <summary>
    /// J2000 and the reference birth of the fix plan, in Lahiri and in Fagan/Bradley. Two modes,
    /// because a mode that was never applied cannot be told apart from a mode applied wrongly if
    /// only one of them is ever checked.
    /// </summary>
    public static TheoryData<SiderealMode, double, CelestialBody, double> Cases() => new()
    {
        // swe_set_sid_mode(SE_SIDM_LAHIRI), jd 2451545.0.
        { SiderealMode.Lahiri, 2451545.0, CelestialBody.TrueNode, 100.100840819990 },
        { SiderealMode.Lahiri, 2451545.0, CelestialBody.MeanNode, 101.187462718989 },
        { SiderealMode.Lahiri, 2451545.0, CelestialBody.MeanApogee, 239.611028022753 },
        { SiderealMode.Lahiri, 2451545.0, CelestialBody.OsculatingApogee, 229.140950168479 },
        { SiderealMode.Lahiri, 2451545.0, CelestialBody.InterpolatedApogee, 235.310725909865 },
        { SiderealMode.Lahiri, 2451545.0, CelestialBody.InterpolatedPerigee, 68.040186566334 },

        // swe_set_sid_mode(SE_SIDM_FAGAN_BRADLEY), jd 2451545.0.
        { SiderealMode.FaganBradley, 2451545.0, CelestialBody.TrueNode, 99.217633179264 },
        { SiderealMode.FaganBradley, 2451545.0, CelestialBody.MeanNode, 100.304255078263 },
        { SiderealMode.FaganBradley, 2451545.0, CelestialBody.MeanApogee, 238.727820382027 },
        { SiderealMode.FaganBradley, 2451545.0, CelestialBody.OsculatingApogee, 228.257742527753 },
        { SiderealMode.FaganBradley, 2451545.0, CelestialBody.InterpolatedApogee, 234.427518269139 },
        { SiderealMode.FaganBradley, 2451545.0, CelestialBody.InterpolatedPerigee, 67.156978925608 },

        // swe_set_sid_mode(SE_SIDM_LAHIRI), jd 2447408.552083333, the reference birth.
        { SiderealMode.Lahiri, 2447408.552083333, CelestialBody.TrueNode, 320.403697041627 },
        { SiderealMode.Lahiri, 2447408.552083333, CelestialBody.MeanNode, 320.386179047288 },
        { SiderealMode.Lahiri, 2447408.552083333, CelestialBody.MeanApogee, 138.845582426819 },
        { SiderealMode.Lahiri, 2447408.552083333, CelestialBody.OsculatingApogee, 122.312495260762 },
        { SiderealMode.Lahiri, 2447408.552083333, CelestialBody.InterpolatedApogee, 138.776806144841 },
        { SiderealMode.Lahiri, 2447408.552083333, CelestialBody.InterpolatedPerigee, 318.268946157342 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SiderealLongitudeMatchesTheCLibrary(
        SiderealMode mode, double jd, CelestialBody body, double expectedLongitude)
    {
        using var ctx = new EphemerisContextBuilder()
            .UseSwissEphFiles(EphePath.Resolve())
            .UseSiderealMode(mode)
            .Build();

        var state = ctx.Bodies.Compute(
            body, new JulianDay(jd),
            EphemerisFlags.Speed | EphemerisFlags.SwissEph | EphemerisFlags.Sidereal);

        Assert.Equal(0.0, SignedDifference(LongitudeOf(state), expectedLongitude), FifthOfAnArcsecond);
    }

    private static double LongitudeOf(BodyState state)
        => Normalize(Math.Atan2(state.Position.Y, state.Position.X) * 180.0 / Math.PI);

    private static double Normalize(double degrees)
    {
        var result = degrees % 360.0;
        return result < 0 ? result + 360.0 : result;
    }

    private static double SignedDifference(double a, double b)
    {
        var d = Normalize(a - b);
        return d > 180.0 ? d - 360.0 : d;
    }
}
