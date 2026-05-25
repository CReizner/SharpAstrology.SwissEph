// Column-major projections of the IAU 2000A nutation tables, generated once
// from the row-major data so the vectorised inner loop can stream contiguous
// coefficient columns through Vector<double> / TensorPrimitives.
//
// Lives in its own class — the type initialiser runs only when the columns
// are first accessed, at which point NutationTables is already fully
// initialised. (Putting these as static fields on the partial class fails
// because static-field-init order across partial files is unspecified.)

namespace SharpAstrology.SwissEphemerides.Domain.Frames;

internal static class NutationTablesColumnMajor
{
    /// <summary>Luni-solar argument multipliers per fundamental, length 678 each.</summary>
    public static readonly double[] LsArgM = BuildLsArgs(0);
    public static readonly double[] LsArgSm = BuildLsArgs(1);
    public static readonly double[] LsArgF = BuildLsArgs(2);
    public static readonly double[] LsArgD = BuildLsArgs(3);
    public static readonly double[] LsArgOm = BuildLsArgs(4);

    /// <summary>Luni-solar coefficients per kind, length 678 each (units 0.1 µas, _T scaled by t in caller).</summary>
    public static readonly double[] LsPsiSinConst = BuildLsCoeffs(0);
    public static readonly double[] LsPsiSinT = BuildLsCoeffs(1);
    public static readonly double[] LsPsiCos = BuildLsCoeffs(2);
    public static readonly double[] LsEpsCosConst = BuildLsCoeffs(3);
    public static readonly double[] LsEpsCosT = BuildLsCoeffs(4);
    public static readonly double[] LsEpsSin = BuildLsCoeffs(5);

    /// <summary>Planetary argument multipliers per fundamental, length 687 each.</summary>
    public static readonly double[] PlArgL = BuildPlArgs(0);
    public static readonly double[] PlArgLsu = BuildPlArgs(1);
    public static readonly double[] PlArgF = BuildPlArgs(2);
    public static readonly double[] PlArgD = BuildPlArgs(3);
    public static readonly double[] PlArgOm = BuildPlArgs(4);
    public static readonly double[] PlArgMe = BuildPlArgs(5);
    public static readonly double[] PlArgVe = BuildPlArgs(6);
    public static readonly double[] PlArgEa = BuildPlArgs(7);
    public static readonly double[] PlArgMa = BuildPlArgs(8);
    public static readonly double[] PlArgJu = BuildPlArgs(9);
    public static readonly double[] PlArgSa = BuildPlArgs(10);
    public static readonly double[] PlArgUr = BuildPlArgs(11);
    public static readonly double[] PlArgNe = BuildPlArgs(12);
    public static readonly double[] PlArgPa = BuildPlArgs(13);

    /// <summary>Planetary coefficients (sin/cos for psi/eps), length 687 each.</summary>
    public static readonly double[] PlPsiSin = BuildPlCoeffs(0);
    public static readonly double[] PlPsiCos = BuildPlCoeffs(1);
    public static readonly double[] PlEpsSin = BuildPlCoeffs(2);
    public static readonly double[] PlEpsCos = BuildPlCoeffs(3);

    private static double[] BuildLsArgs(int col)
    {
        var n = NutationTables.LuniSolarTerms;
        var dst = new double[n];
        for (var i = 0; i < n; i++)
            dst[i] = NutationTables.LuniSolarArgs[i * 5 + col];
        return dst;
    }

    private static double[] BuildLsCoeffs(int col)
    {
        var n = NutationTables.LuniSolarTerms;
        var dst = new double[n];
        for (var i = 0; i < n; i++)
            dst[i] = NutationTables.LuniSolarCoeffs[i * 6 + col];
        return dst;
    }

    private static double[] BuildPlArgs(int col)
    {
        var n = NutationTables.PlanetaryTerms;
        var dst = new double[n];
        for (var i = 0; i < n; i++)
            dst[i] = NutationTables.PlanetaryArgs[i * 14 + col];
        return dst;
    }

    private static double[] BuildPlCoeffs(int col)
    {
        var n = NutationTables.PlanetaryTerms;
        var dst = new double[n];
        for (var i = 0; i < n; i++)
            dst[i] = NutationTables.PlanetaryCoeffs[i * 4 + col];
        return dst;
    }
}
