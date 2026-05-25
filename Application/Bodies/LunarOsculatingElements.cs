// Ported from swisseph-master/sweph.c lunar_osc_elem (line 5168) — the
// osculating-Kepler-elements branch that backs SE_TRUE_NODE and SE_OSCU_APOG.
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Application.Bodies;

/// <summary>
/// Computes the osculating ascending node and apogee of the Moon from its
/// instantaneous geocentric position + velocity. Mirrors the orbital-element
/// block of <c>lunar_osc_elem</c> (swecl.c → sweph.c#L5360-L5472), stripped
/// of state caching and source dispatch — the caller supplies the three
/// (position, velocity) tuples and the orbital-element math runs in-frame.
/// </summary>
/// <remarks>
/// <para>
/// The C function takes Moon position+velocity at three epochs (t-Δ, t,
/// t+Δ) in true-ecliptic-of-date coordinates and produces:
/// </para>
/// <list type="bullet">
///   <item>True (osculating) ascending node — intersection of the lunar
///         orbital plane with the ecliptic plane.</item>
///   <item>Osculating apogee — antipode of perigee on the osculating
///         ellipse, projected onto the ecliptic plane.</item>
/// </list>
/// <para>
/// Outputs are in the same Cartesian frame as the inputs. The node distance
/// is corrected by the osculating-ellipse radius (sweph.c#L5441-L5452).
/// Velocities are estimated by central differencing across the three node /
/// apogee samples.
/// </para>
/// </remarks>
internal static class LunarOsculatingElements
{
    /// <summary>
    /// Time step (days) used for the finite-difference node-speed estimate
    /// when the underlying source is Moshier. Mirrors
    /// <c>NODE_CALC_INTV_MOSH = 0.1</c> from sweph.h (the Moshier moon
    /// theory's residual jitter requires the larger step compared with
    /// <c>NODE_CALC_INTV = 0.0001</c> used for SwissEph/JPL).
    /// </summary>
    public const double MoshierStepDays = 0.1;

    /// <summary>
    /// Time step (days) used for the finite-difference node-speed estimate
    /// when the underlying source is SwissEph (<c>.se1</c>) or JPL DE
    /// (<c>.eph</c>). Mirrors <c>NODE_CALC_INTV = 0.0001</c> from sweph.h.
    /// </summary>
    public const double SwiephJplStepDays = 0.0001;

    /// <summary>
    /// Returns the source-specific finite-difference step. Mirrors the
    /// <c>switch(epheflag)</c> cascade in sweph.c#L5252-L5354.
    /// </summary>
    public static double StepDaysFor(EphemerisSource source) => source switch
    {
        EphemerisSource.Moshier => MoshierStepDays,
        EphemerisSource.SwissEph => SwiephJplStepDays,
        EphemerisSource.Jpl => SwiephJplStepDays,
        _ => MoshierStepDays,
    };

    /// <summary>
    /// G·M(earth+moon) in AU³/day². Mirrors
    /// <c>Gmsm = GEOGCONST * (1 + 1/EARTH_MOON_MRAT) / AUNIT^3 * 86400^2</c>
    /// at sweph.c#L5399.
    /// </summary>
    public static readonly double GmsmAuPerDay2 =
        AstronomicalConstants.GeoGravConst
        * (1.0 + 1.0 / AstronomicalConstants.EarthMoonMassRatio)
        / (AstronomicalConstants.AstronomicalUnitMeters
           * AstronomicalConstants.AstronomicalUnitMeters
           * AstronomicalConstants.AstronomicalUnitMeters)
        * AstronomicalConstants.SecondsPerDay
        * AstronomicalConstants.SecondsPerDay;

    /// <summary>
    /// Result of one orbital-elements pass: node and apogee Cartesian state,
    /// in the same frame as the input moon samples.
    /// </summary>
    public readonly record struct Result(
        Vec3 NodePosition,
        Vec3 NodeVelocity,
        Vec3 ApogeePosition,
        Vec3 ApogeeVelocity);

    /// <summary>
    /// Compute true node and osculating apogee from three lunar samples.
    /// Each sample is a (position, velocity) pair at the corresponding
    /// epoch <c>t-Δ</c>, <c>t+Δ</c>, <c>t</c> (note: index 0 is t-Δ, index 1 is
    /// t+Δ, index 2 is t — same ordering as swecl.c#L5256-L5262).
    /// </summary>
    /// <param name="moonAtMinus">Moon (pos, vel) at <c>t - speedIntervalDays</c>.</param>
    /// <param name="moonAtPlus">Moon (pos, vel) at <c>t + speedIntervalDays</c>.</param>
    /// <param name="moonAtCenter">Moon (pos, vel) at <c>t</c>.</param>
    /// <param name="speedIntervalDays">
    /// Finite-difference step. Use <see cref="MoshierStepDays"/> for the
    /// Moshier moon theory; smaller values for SwissEph/JPL.
    /// </param>
    /// <param name="includeSpeed">
    /// If false, the apogee/node velocities are returned as zero and only
    /// the central sample is used. The two outer samples may pass as
    /// <c>default</c> in that case.
    /// </param>
    public static Result Compute(
        (Vec3 Position, Vec3 Velocity) moonAtMinus,
        (Vec3 Position, Vec3 Velocity) moonAtPlus,
        (Vec3 Position, Vec3 Velocity) moonAtCenter,
        double speedIntervalDays,
        bool includeSpeed)
    {
        Span<NodeSample> nodes = stackalloc NodeSample[3];
        Span<Vec3> apogees = stackalloc Vec3[3];

        // Per the C code (sweph.c#L5244-L5248), the absent samples for
        // !SEFLG_SPEED are simply skipped; we mirror the same loop bounds
        // by writing only the central index.
        var iStart = includeSpeed ? 0 : 2;

        for (var i = iStart; i < 3; i++)
        {
            var sample = i switch
            {
                0 => moonAtMinus,
                1 => moonAtPlus,
                _ => moonAtCenter,
            };
            nodes[i] = ComputeNodeDirection(sample.Position, sample.Velocity);
            apogees[i] = ComputeApogeeAndCorrectNode(ref nodes[i], sample.Position, sample.Velocity);
        }

        // Central-difference velocity for both node and apogee
        // (sweph.c#L5466-L5471 and #L5458-L5462). The C library does
        // emit a parabola-fit estimate at #L5386-L5388 too, but it is
        // dead code: the second save loop after the apogee block
        // overwrites it with the simple central difference applied to
        // the rescaled node positions.
        Vec3 nodeVel = default, apogeeVel = default;
        if (includeSpeed)
        {
            nodeVel = (nodes[1].Position - nodes[0].Position) / (speedIntervalDays * 2.0);
            apogeeVel = (apogees[1] - apogees[0]) / (speedIntervalDays * 2.0);
        }

        return new Result(
            nodes[2].Position,
            nodeVel,
            apogees[2],
            apogeeVel);
    }

    /// <summary>
    /// Direction of the ascending node from a lunar (pos, vel) sample.
    /// Mirrors the inner block of swecl.c#L5366-L5373:
    /// <c>x_node = x - (z / vz) · v</c>, scaled by <c>sign(vz)</c>.
    /// The result is a vector in the same plane as <paramref name="position"/>
    /// but with its Z component set to zero (it lies on the ecliptic plane).
    /// </summary>
    private static NodeSample ComputeNodeDirection(Vec3 position, Vec3 velocity)
    {
        // Avoid division by zero when the moon is exactly at the node
        // (vz ≈ 0). The C library applies the same epsilon (sweph.c#L5367).
        var vz = velocity.Z;
        if (System.Math.Abs(vz) < 1e-15) vz = 1e-15;

        var fac = position.Z / vz;
        var sgn = vz / System.Math.Abs(vz);

        var nx = (position.X - fac * velocity.X) * sgn;
        var ny = (position.Y - fac * velocity.Y) * sgn;
        var nz = (position.Z - fac * velocity.Z) * sgn; // = 0 by construction
        return new NodeSample(new Vec3(nx, ny, nz));
    }

    /// <summary>
    /// Computes the apogee Cartesian vector from a lunar (pos, vel) sample
    /// and corrects the node distance to the osculating-ellipse radius.
    /// Mirrors swecl.c#L5402-L5453 (orbital-elements derivation: inclination,
    /// argument of latitude, semi-major axis, eccentricity, true anomaly).
    /// </summary>
    /// <param name="node">
    /// The node-direction sample produced by
    /// <see cref="ComputeNodeDirection"/>; on output its position vector is
    /// rescaled to the osculating-ellipse node radius.
    /// </param>
    /// <param name="position">Lunar position at the same epoch as <paramref name="node"/>.</param>
    /// <param name="velocity">Lunar velocity at the same epoch as <paramref name="node"/>.</param>
    /// <returns>The Cartesian apogee position in the input frame.</returns>
    private static Vec3 ComputeApogeeAndCorrectNode(ref NodeSample node, Vec3 position, Vec3 velocity)
    {
        // Node direction unit vector in the ecliptic plane (cosnode, sinnode).
        var rxyNode = System.Math.Sqrt(node.Position.X * node.Position.X + node.Position.Y * node.Position.Y);
        var cosNode = node.Position.X / rxyNode;
        var sinNode = node.Position.Y / rxyNode;

        // Inclination — derived from the orbit-normal vector L = r × v.
        var lvec = Vec3.Cross(position, velocity);
        var rxyLnSq = lvec.X * lvec.X + lvec.Y * lvec.Y;
        var c2 = rxyLnSq + lvec.Z * lvec.Z; // |L|²
        var rxyLn = System.Math.Sqrt(rxyLnSq);
        var rxyzLn = System.Math.Sqrt(c2);
        var sinIncl = rxyLn / rxyzLn;
        var cosIncl = System.Math.Sqrt(1.0 - sinIncl * sinIncl);

        // Argument of latitude u (angle from node to body).
        var cosU = position.X * cosNode + position.Y * sinNode;
        var sinU = position.Z / sinIncl;
        var u = System.Math.Atan2(sinU, cosU);

        // Semi-major axis from vis-viva: a = 1 / (2/r - v²/μ).
        var rxyz = position.Length;
        var v2 = velocity.LengthSquared;
        var sema = 1.0 / (2.0 / rxyz - v2 / GmsmAuPerDay2);

        // Eccentricity from L² = μ·a·(1-e²) → e = sqrt(1 - L²/(μ·a)).
        var pp = c2 / GmsmAuPerDay2; // = a·(1-e²)
        var ecce = System.Math.Sqrt(1.0 - pp / sema);

        // Eccentric anomaly via the Kepler vector identities:
        //   r = a (1 - e cos E)        → cos E = (1 - r/a) / e
        //   r·v_r = sqrt(μa) e sin E  → sin E = r·v / (e √(μa))
        var cosE = (1.0 - rxyz / sema) / ecce;
        var sinE = Vec3.Dot(position, velocity) / (ecce * System.Math.Sqrt(sema * GmsmAuPerDay2));

        // True anomaly ν from E.
        var nu = 2.0 * System.Math.Atan(System.Math.Sqrt((1.0 + ecce) / (1.0 - ecce)) * sinE / (1.0 + cosE));

        // Apogee polar (in orbital plane): longitude = u - ν + π, lat = 0,
        // r = a (1 + e). Then transform back to ecliptic via:
        //   1) polar → cart in orbital plane
        //   2) X-axis rotation by -inclination (orbital → ecliptic axis)
        //   3) cart → polar; add node longitude; polar → cart.
        var apogeeLonOrbital = Mod2Pi(u - nu + AstronomicalConstants.Pi);
        var rApo = sema * (1.0 + ecce);
        var apoOrbCart = new Vec3(
            rApo * System.Math.Cos(apogeeLonOrbital),
            rApo * System.Math.Sin(apogeeLonOrbital),
            0.0);
        // swi_coortrf2(xpo, xpn, sineps, coseps): y' = y·c + z·s; z' = -y·s + z·c.
        // C calls it with (sineps = -sinIncl, coseps = cosIncl), producing
        //   y' = y·cosIncl - z·sinIncl
        //   z' = y·sinIncl + z·cosIncl
        // — i.e. an X-axis rotation by +inclination, tilting orbital plane → ecliptic.
        var s = -sinIncl;
        var c = cosIncl;
        var apoCartTilted = new Vec3(
            apoOrbCart.X,
            apoOrbCart.Y * c + apoOrbCart.Z * s,
            -apoOrbCart.Y * s + apoOrbCart.Z * c);

        // Convert to polar to add the node longitude, then back to cart.
        var apoPolar = Polar.CartesianToPolar(apoCartTilted);
        var apoLon = apoPolar.X + System.Math.Atan2(sinNode, cosNode);
        var apogeeCart = Polar.PolarToCartesian(new Vec3(apoLon, apoPolar.Y, apoPolar.Z));

        // Correct node distance: the raw node direction has length
        // |x − (z/vz)v|, which can be huge; replace it with the
        // osculating-ellipse radius at the node.
        var nuAtNode = Mod2Pi(nu - u); // negative of u-ν measured at the node line
        var cosE_node = System.Math.Cos(2.0 * System.Math.Atan(
            System.Math.Tan(nuAtNode / 2.0) / System.Math.Sqrt((1.0 + ecce) / (1.0 - ecce))));
        var rNode = sema * (1.0 - ecce * cosE_node);
        var rOldNode = node.Position.Length;
        var scale = rNode / rOldNode;
        node = new NodeSample(node.Position * scale);

        return apogeeCart;
    }

    private static double Mod2Pi(double a)
    {
        var x = a - AstronomicalConstants.TwoPi * System.Math.Floor(a / AstronomicalConstants.TwoPi);
        return x;
    }

    /// <summary>
    /// Per-epoch node sample. Holds the directional vector returned by
    /// <see cref="ComputeNodeDirection"/>. Distance is only meaningful after
    /// <see cref="ComputeApogeeAndCorrectNode"/> rescales it.
    /// </summary>
    private readonly record struct NodeSample(Vec3 Position);
}
