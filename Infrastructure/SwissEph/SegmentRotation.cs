// Ported from swisseph-master/sweph.c rot_back (lines 4963-5054).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Infrastructure.SwissEph;

/// <summary>
/// In-place rotation of unpacked Chebyshev coefficients from the orbital-plane
/// frame they are stored in to the J2000 ecliptic frame (planets) or the J2000
/// equator frame (Moon). Mirrors the C-side <c>rot_back</c> function — all
/// calculations are done coefficient-by-coefficient because Chebyshev
/// evaluation is linear in the coefficients.
/// </summary>
internal static class SegmentRotation
{
    private const double TwoPi = 2.0 * System.Math.PI;
    // sin/cos of obliquity of J2000, in radians. Verbatim from the C source.
    private const double SinEps2000 = 0.39777715572793088;
    private const double CosEps2000 = 0.91748206215761929;

    /// <summary>
    /// Rotates the unpacked Chebyshev coefficient block in <paramref name="segp"/>
    /// (size 3 * <see cref="Se1PlanetData.CoefficientCount"/>) in place. Returns
    /// the trailing-zeros-trimmed <c>neval</c> count: callers should evaluate
    /// only the first <c>neval</c> Chebyshev terms in each coordinate.
    /// </summary>
    public static int RotateInPlace(Se1PlanetData planet, Span<double> segp, double tseg0, bool isMoon)
    {
        if ((planet.Flags & Se1FileFormat.FlagRotate) == 0)
            return planet.CoefficientCount;

        var nco = planet.CoefficientCount;
        // Coefficient sub-spans for each coordinate.
        var chcfx = segp.Slice(0, nco);
        var chcfy = segp.Slice(nco, nco);
        var chcfz = segp.Slice(2 * nco, nco);

        var t = tseg0 + planet.SegmentLengthDays * 0.5;
        var tdiff = (t - planet.ElementsEpoch) / 365250.0;

        double qav, pav;
        if (isMoon)
        {
            var dn = planet.Prot + tdiff * planet.DProt;
            var k = (int)(dn / TwoPi);
            dn -= k * TwoPi;
            qav = (planet.Qrot + tdiff * planet.DQrot) * System.Math.Cos(dn);
            pav = (planet.Qrot + tdiff * planet.DQrot) * System.Math.Sin(dn);
        }
        else
        {
            qav = planet.Qrot + tdiff * planet.DQrot;
            pav = planet.Prot + tdiff * planet.DProt;
        }

        // If the body has a reference ellipse, its coefficients add to the
        // x/y series before the rotation. Stack-allocate an x[ncoe][3] block.
        Span<double> x = stackalloc double[3 * nco];
        for (var i = 0; i < nco; i++)
        {
            x[3 * i + 0] = chcfx[i];
            x[3 * i + 1] = chcfy[i];
            x[3 * i + 2] = chcfz[i];
        }
        if ((planet.Flags & Se1FileFormat.FlagEllipse) != 0)
        {
            var refep = planet.ReferenceEllipse;
            // Defensive: refep may be empty if file omits it but flag set; treat
            // that as "no ellipse correction".
            if (refep.Length >= 2 * nco)
            {
                var omtild = planet.Peri + tdiff * planet.DPeri;
                var k = (int)(omtild / TwoPi);
                omtild -= k * TwoPi;
                var com = System.Math.Cos(omtild);
                var som = System.Math.Sin(omtild);
                for (var i = 0; i < nco; i++)
                {
                    var refx = refep[i];
                    var refy = refep[nco + i];
                    x[3 * i + 0] = chcfx[i] + com * refx - som * refy;
                    x[3 * i + 1] = chcfy[i] + com * refy + som * refx;
                }
            }
        }

        // Equinoctial variables → orthonormal basis.
        var cosih2 = 1.0 / (1.0 + qav * qav + pav * pav);
        // uiz: orbit pole.
        var uiz0 = 2.0 * pav * cosih2;
        var uiz1 = -2.0 * qav * cosih2;
        var uiz2 = (1.0 - qav * qav - pav * pav) * cosih2;
        // uix: origin of longitudes.
        var uix0 = (1.0 + qav * qav - pav * pav) * cosih2;
        var uix1 = 2.0 * qav * pav * cosih2;
        var uix2 = -2.0 * pav * cosih2;
        // uiy: orthogonal to uix in the orbital plane.
        var uiy0 = 2.0 * qav * pav * cosih2;
        var uiy1 = (1.0 - qav * qav + pav * pav) * cosih2;
        var uiy2 = 2.0 * qav * cosih2;

        var neval = 0;
        for (var i = 0; i < nco; i++)
        {
            var xi0 = x[3 * i + 0];
            var xi1 = x[3 * i + 1];
            var xi2 = x[3 * i + 2];
            var xrot = xi0 * uix0 + xi1 * uiy0 + xi2 * uiz0;
            var yrot = xi0 * uix1 + xi1 * uiy1 + xi2 * uiz1;
            var zrot = xi0 * uix2 + xi1 * uiy2 + xi2 * uiz2;
            if (System.Math.Abs(xrot) + System.Math.Abs(yrot) + System.Math.Abs(zrot) >= 1e-14)
                neval = i;
            if (isMoon)
            {
                var newY = CosEps2000 * yrot - SinEps2000 * zrot;
                var newZ = SinEps2000 * yrot + CosEps2000 * zrot;
                yrot = newY;
                zrot = newZ;
            }
            chcfx[i] = xrot;
            chcfy[i] = yrot;
            chcfz[i] = zrot;
        }
        // C-side rot_back stores `neval = i` for the last non-trivial index,
        // and then `swi_echeb(t, coef, neval)` evaluates ncf=neval terms
        // (indices 0..neval-1). I.e., the C deliberately drops the highest
        // term — trailing coefficients hovering near the 1e-14 threshold can
        // amplify numerical noise. We mirror this verbatim.
        return neval;
    }
}
