// Ported from swehouse.c#L1541-L1580 (case 'F' — Carter "Poli-Equatorial").
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class CarterHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.CarterPoliEquatorial;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var dr = AstronomicalConstants.DegToRad;
        var rd = AstronomicalConstants.RadToDeg;

        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0)
        {
            ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
            ctx.Cusps[1] = ctx.Ac;
        }

        // (asc, 0) on the ecliptic → equator: rectascension a.
        double lon = ctx.Ac, lat = 0;
        HouseAscendantMath.RotateEqToEclSpherical(ref lon, ref lat, -ctx.Ekl);
        var aRect = lon;

        for (var i = 2; i <= 12; i++)
        {
            if (i <= 3 || i >= 10)
            {
                var ra = AngleMath.NormalizeDegrees(aRect + (i - 1) * 30.0);
                double cusp;
                if (System.Math.Abs(ra - 90) > HouseAscendantMath.VerySmall
                    && System.Math.Abs(ra - 270) > HouseAscendantMath.VerySmall)
                {
                    var tant = System.Math.Tan(ra * dr);
                    cusp = System.Math.Atan(tant / ctx.Cose) * rd;
                    if (ra > 90 && ra <= 270) cusp = AngleMath.NormalizeDegrees(cusp + 180);
                }
                else
                {
                    cusp = System.Math.Abs(ra - 90) <= HouseAscendantMath.VerySmall ? 90 : 270;
                }
                ctx.Cusps[i] = AngleMath.NormalizeDegrees(cusp);
            }
        }
        ctx.DoInterpol = ctx.DoHspeed;
        return true;
    }
}
