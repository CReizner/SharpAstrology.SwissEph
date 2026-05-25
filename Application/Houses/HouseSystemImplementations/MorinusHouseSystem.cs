// Ported from swehouse.c#L1517-L1540 (case 'M').
// Original license: see LICENSE.SwissEph.txt at the repo root.

using SharpAstrology.SwissEphemerides.Domain.Houses;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Houses.HouseSystemImplementations;

internal sealed class MorinusHouseSystem : IHouseSystemImpl
{
    public HouseSystem Identifier => HouseSystem.Morinus;
    public int CuspCount => 12;
    public bool SkipsDefaultMirror => false;

    public bool Compute(ref HouseComputeContext ctx)
    {
        var a = ctx.Th;
        for (var i = 1; i <= 12; i++)
        {
            var j = i + 10;
            if (j > 12) j -= 12;
            a = AngleMath.NormalizeDegrees(a + 30);
            // Equator point (a, 0) → ecliptic (lon, lat) via swe_cotrans(eps).
            double lon = a, lat = 0;
            HouseAscendantMath.RotateEqToEclSpherical(ref lon, ref lat, ctx.Ekl);
            ctx.Cusps[j] = lon;
        }
        var acmc = AngleMath.DifferenceDegreesSigned(ctx.Ac, ctx.Mc);
        if (acmc < 0) ctx.Ac = AngleMath.NormalizeDegrees(ctx.Ac + 180);
        ctx.DoInterpol = ctx.DoHspeed;
        return true;
    }
}
