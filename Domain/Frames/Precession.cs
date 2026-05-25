// Ported from swisseph-master/swephlib.c (Astrodienst Swiss Ephemeris).
// Original license: see LICENSE.SwissEph.txt at the repo root.
//
// Source: swephlib.c
//   pre_pecl   (Vondrák ecliptic pole)            — lines 577-616
//   pre_pequ   (Vondrák equator pole)             — lines 619-653
//   pre_pmat   (Vondrák precession matrix)        — lines 665-694
//   swi_ldp_peps (Vondrák long-term ε / dpre)     — lines 535-569
//   swi_epsiln (mean obliquity, all variants)     — lines 887-969
//   precess_1  (IAU 1976/2000/2006/Bretagnon)     — lines 1023-1168
//   precess_2  (Williams/Simon/Laskar)            — lines 1219-1326
//   swi_precess (model dispatch)                  — lines 1373-1430
//   epsiln_owen_1986                              — lines 828-854
//   owen_pre_matrix                               — lines 764-826

using System;
using SharpAstrology.SwissEphemerides.Domain.Constants;
using SharpAstrology.SwissEphemerides.Domain.Mathematics;

namespace SharpAstrology.SwissEphemerides.Domain.Frames;

/// <summary>
/// Precession of the equator and ecliptic. Provides mean obliquity and the
/// rotation that maps an ICRS-equatorial vector at one epoch to the same
/// frame at another epoch. All available astronomical-model variants are
/// reachable through <see cref="AstronomicalModelOverrides"/>.
/// </summary>
internal static class Precession
{
    private const double DegToRad = AstronomicalConstants.DegToRad;
    private const double RadToDeg = AstronomicalConstants.RadToDeg;
    private const double J2000 = AstronomicalConstants.J2000;
    private const double JulianCentury = AstronomicalConstants.JulianCentury;
    private const double TwoPi = AstronomicalConstants.TwoPi;
    private const double B1850 = 2_396_758.20833333; // tropical-J reference for Newcomb (B1850 here matches swephlib J2000 - cties)

    private const double PrecIau1976Centuries = 2.0;
    private const double PrecIau2000Centuries = 2.0;
    private const double PrecIau2006Centuries = 75.0;

    private const double ArcSecondsToRadians = DegToRad / 3600.0;

    // Vondrák 2011 long-term polynomial / periodic coefficients.
    // swephlib.c#L478-L494 (peps), #L498-L515 (pecl), #L519-L532 (pequ).
    // The arrays preserve the swephlib row ordering: [0]=period, [1]=cos_p, [2]=cos_q, [3]=sin_p, [4]=sin_q
    // (or x/y for the equator-pole table).

    private static readonly double[] s_pepsPolP = { +8134.017132, +5043.0520035, -0.00710733, +0.000000271 };
    private static readonly double[] s_pepsPolQ = { +84028.206305, +0.3624445, -0.00004039, -0.000000110 };
    private static readonly double[] s_pepsPeriod =
    {
        +409.90, +396.15, +537.22, +402.90, +417.15, +288.92, +4043.00, +306.00, +277.00, +203.00,
    };
    private static readonly double[] s_pepsCosP =
    {
        -6908.287473, -3198.706291, +1453.674527, -857.748557, +1173.231614, -156.981465, +371.836550, -216.619040, +193.691479, +11.891524,
    };
    private static readonly double[] s_pepsCosQ =
    {
        +753.872780, -247.805823, +379.471484, -53.880558, -90.109153, -353.600190, -63.115353, -28.248187, +17.703387, +38.911307,
    };
    private static readonly double[] s_pepsSinP =
    {
        -2845.175469, +449.844989, -1255.915323, +886.736783, +418.887514, +997.912441, -240.979710, +76.541307, -36.788069, -170.964086,
    };
    private static readonly double[] s_pepsSinQ =
    {
        -1704.720302, -862.308358, +447.832178, -889.571909, +190.402846, -56.564991, -296.222622, -75.859952, +67.473503, +3.014055,
    };

    private static readonly double[] s_peclPolP = { +5851.607687, -0.1189000, -0.00028913, +0.000000101 };
    private static readonly double[] s_peclPolQ = { -1600.886300, +1.1689818, -0.00000020, -0.000000437 };
    private static readonly double[] s_peclPeriod = { 708.15, 2309, 1620, 492.2, 1183, 622, 882, 547 };
    private static readonly double[] s_peclCosP =
        { -5486.751211, -17.127623, -617.517403, 413.44294, 78.614193, -180.732815, -87.676083, 46.140315 };
    private static readonly double[] s_peclCosQ =
    {
        // typo fixed per A&A 541, C1 (2012); 198.296701 not 198.296071
        -684.66156, 2446.28388, 399.671049, -356.652376, -186.387003, -316.80007, 198.296701, 101.135679,
    };
    private static readonly double[] s_peclSinP =
        { 667.66673, -2354.886252, -428.152441, 376.202861, 184.778874, 335.321713, -185.138669, -120.97283 };
    private static readonly double[] s_peclSinQ =
        { -5523.863691, -549.74745, -310.998056, 421.535876, -36.776172, -145.278396, -34.74445, 22.885731 };

    private static readonly double[] s_pequPolX = { +5453.282155, +0.4252841, -0.00037173, -0.000000152 };
    private static readonly double[] s_pequPolY = { -73750.930350, -0.7675452, -0.00018725, +0.000000231 };
    private static readonly double[] s_pequPeriod =
    {
        256.75, 708.15, 274.2, 241.45, 2309, 492.2, 396.1, 288.9, 231.1, 1610, 620, 157.87, 220.3, 1200,
    };
    private static readonly double[] s_pequCosX =
    {
        -819.940624, -8444.676815, 2600.009459, 2755.17563, -167.659835, 871.855056, 44.769698,
        -512.313065, -819.415595, -538.071099, -189.793622, -402.922932, 179.516345, -9.814756,
    };
    private static readonly double[] s_pequCosY =
    {
        75004.344875, 624.033993, 1251.136893, -1102.212834, -2660.66498, 699.291817, 153.16722,
        -950.865637, 499.754645, -145.18821, 558.116553, -23.923029, -165.405086, 9.344131,
    };
    private static readonly double[] s_pequSinX =
    {
        81491.287984, 787.163481, 1251.296102, -1257.950837, -2966.79973, 639.744522, 131.600209,
        -445.040117, 584.522874, -89.756563, 524.42963, -13.549067, -210.157124, -44.919798,
    };
    private static readonly double[] s_pequSinY =
    {
        1558.515853, 7774.939698, -2219.534038, -2523.969396, 247.850422, -846.485643, -1393.124055,
        368.526116, 749.045012, 444.704518, 235.934465, 374.049623, -171.33018, -22.899655,
    };

    private const double VondrakEps0Arcsec = 84381.406;

    // Williams 1994 / Simon 1994 / Laskar 1986 — pAcof, nodecof, inclcof.
    // swephlib.c#L1175-L1217.

    private static readonly double[] s_pAcofWilliams =
    {
        -8.66e-10, -4.759e-8, 2.424e-7, 1.3095e-5, 1.7451e-4, -1.8055e-3,
        -0.235316, 0.076, 110.5407, 50287.70000,
    };
    private static readonly double[] s_nodeCofWilliams =
    {
        6.6402e-16, -2.69151e-15, -1.547021e-12, 7.521313e-12, 1.9e-10,
        -3.54e-9, -1.8103e-7, 1.26e-7, 7.436169e-5,
        -0.04207794833, 3.052115282424,
    };
    private static readonly double[] s_inclCofWilliams =
    {
        1.2147e-16, 7.3759e-17, -8.26287e-14, 2.503410e-13, 2.4650839e-11,
        -5.4000441e-11, 1.32115526e-9, -6.012e-7, -1.62442e-5,
        0.00227850649, 0.0,
    };

    private static readonly double[] s_pAcofSimon =
    {
        -8.66e-10, -4.759e-8, 2.424e-7, 1.3095e-5, 1.7451e-4, -1.8055e-3,
        -0.235316, 0.07732, 111.2022, 50288.200,
    };
    private static readonly double[] s_nodeCofSimon =
    {
        6.6402e-16, -2.69151e-15, -1.547021e-12, 7.521313e-12, 1.9e-10,
        -3.54e-9, -1.8103e-7, 2.579e-8, 7.4379679e-5,
        -0.0420782900, 3.0521126906,
    };
    private static readonly double[] s_inclCofSimon =
    {
        1.2147e-16, 7.3759e-17, -8.26287e-14, 2.503410e-13, 2.4650839e-11,
        -5.4000441e-11, 1.32115526e-9, -5.99908e-7, -1.624383e-5,
        0.002278492868, 0.0,
    };

    private static readonly double[] s_pAcofLaskar =
    {
        -8.66e-10, -4.759e-8, 2.424e-7, 1.3095e-5, 1.7451e-4, -1.8055e-3,
        -0.235316, 0.07732, 111.1971, 50290.966,
    };
    private static readonly double[] s_nodeCofLaskar =
    {
        6.6402e-16, -2.69151e-15, -1.547021e-12, 7.521313e-12, 6.3190131e-10,
        -3.48388152e-9, -1.813065896e-7, 2.75036225e-8, 7.4394531426e-5,
        -0.042078604317, 3.052112654975,
    };
    private static readonly double[] s_inclCofLaskar =
    {
        1.2147e-16, 7.3759e-17, -8.26287e-14, 2.503410e-13, 2.4650839e-11,
        -5.4000441e-11, 1.32115526e-9, -5.998737027e-7, -1.6242797091e-5,
        0.002278495537, 0.0,
    };

    // Owen 1990 polynomial coefficients. swephlib.c#L715-L745.
    // Flattened (5 segments × 10 Chebyshev columns) so the data lands in the
    // PE-image RVA blob; consumers slice by `icof * OwenStride`.
    private const int OwenStride = 10;

    private static ReadOnlySpan<double> s_owenEps0 =>
    [
        23.699391439256386, 5.2330816033981775e-1, -5.6259493384864815e-2, -8.2033318431602032e-3, 6.6774163554156385e-4, 2.4931584012812606e-5, -3.1313623302407878e-6, 2.0343814827951515e-7, 2.9182026615852936e-8, -4.1118760893281951e-9,
        24.124759551704588, -1.2094875596566286e-1, -8.3914869653015218e-2, 3.5357075322387405e-3, 6.4557467824807032e-4, -2.5092064378707704e-5, -1.7631607274450848e-6, 1.3363622791424094e-7, 1.5577817511054047e-8, -2.4613907093017122e-9,
        23.439103144206208, -4.9386077073143590e-1, -2.3965445283267805e-4, 8.6637485629656489e-3, -5.2828151901367600e-5, -4.3951004595359217e-5, -1.1058785949914705e-6, 6.2431490022621172e-8, 3.4725376218710764e-8, 1.3658853127005757e-9,
        22.724671295125046, -1.6041813558650337e-1, 7.0646783888132504e-2, 1.4967806745062837e-3, -6.6857270989190734e-4, 5.7578378071604775e-6, 3.3738508454638728e-6, -2.2917813537654764e-7, -2.1019907929218137e-8, 4.3139832091694682e-9,
        22.914636050333696, 3.2123508304962416e-1, 3.6633220173792710e-2, -5.9228324767696043e-3, -1.882379107379328e-4, 3.2274552870236244e-5, 4.9052463646336507e-7, -5.9064298731578425e-8, -2.0485712675098837e-8, -6.2163304813908160e-10,
    ];

    private static ReadOnlySpan<double> s_owenPsia =>
    [
        -218.57864954903122, 51.752257487741612, 1.3304715765661958e-1, 9.2048123521890745e-2, -6.0877528127241278e-3, -7.0013893644531700e-5, -4.9217728385458495e-5, -1.8578234189053723e-6, 7.4396426162029877e-7, -5.9157528981843864e-9,
        -111.94350527506128, 55.175558131675861, 4.7366115762797613e-1, -4.7701750975398538e-2, -9.2445765329325809e-3, 7.0962838707454917e-4, 1.5140455277814658e-4, -7.7813159018954928e-7, -2.4729402281953378e-6, -1.0898887008726418e-7,
        -2.041452011529441e-1, 55.969995858494106, -1.9295093699770936e-1, -5.6819574830421158e-3, 1.1073687302518981e-2, -9.0868489896815619e-5, -1.1999773777895820e-4, 9.9748697306154409e-6, 5.7911493603430550e-7, -2.3647526839778175e-7,
        111.61366860604471, 56.404525305162447, 4.4403302410703782e-1, 7.1490030578883907e-2, -4.9184559079790816e-3, -1.3912698949042046e-3, -6.8490613661884005e-5, 1.2394328562905297e-6, 1.7719847841480384e-6, 2.4889095220628068e-7,
        228.40683531269390, 60.056143904919826, 2.9583200718478960e-2, -1.5710838319490748e-1, -7.0017356811600801e-3, 3.3009615142224537e-3, 2.0318123852537664e-4, -6.5840216067828310e-5, -5.9077673352976155e-6, 1.3983942185303064e-6,
    ];

    private static ReadOnlySpan<double> s_owenOma =>
    [
        25.541291140949806, 2.377889511272162e-1, -3.7337334723142133e-1, 2.4579295485161534e-2, 4.3840999514263623e-3, -3.1126873333599556e-4, -9.8443045771748915e-6, -7.9403103080496923e-7, 1.0840116743893556e-9, 9.2865105216887919e-9,
        24.429357654237926, -9.5205745947740161e-1, 8.6738296270534816e-2, 3.0061543426062955e-2, -4.1532480523019988e-3, -3.7920928393860939e-4, 3.5117012399609737e-5, 4.6811877283079217e-6, -8.1836046585546861e-8, -6.1803706664211173e-8,
        23.450465062489337, -9.7259278279739817e-2, 1.1082286925130981e-2, -3.1469883339372219e-2, -1.0041906996819648e-4, 5.6455168475133958e-4, -8.4403910211030209e-6, -3.8269157371098435e-6, 3.1422585261198437e-7, 9.3481729116773404e-9,
        22.581778052947806, -8.7069701538602037e-1, -9.8140710050197307e-2, 2.6025931340678079e-2, 4.8165322168786755e-3, -1.906558772193363e-4, -4.6838759635421777e-5, -1.6608525315998471e-6, -3.2347811293516124e-8, 2.8104728109642000e-9,
        21.518861835737142, 2.0494789509441385e-1, 3.5193604846503161e-1, 1.5305977982348925e-2, -7.5015367726336455e-3, -4.0322553186065610e-4, 1.0655320434844041e-4, 7.1792339586935752e-6, -1.603874697543020e-6, -1.613563462813512e-7,
    ];

    private static ReadOnlySpan<double> s_owenChia =>
    [
        8.2378850337329404e-1, -3.7443109739678667, 4.0143936898854026e-1, 8.1822830214590811e-2, -8.5978790792656293e-3, -2.8350488448426132e-5, -4.2474671728156727e-5, -1.6214840884656678e-6, 7.8560442001953050e-7, -1.032016641696707e-8,
        -2.1726062070318606, 7.8470515033132925e-1, 4.4044931004195718e-1, -8.0671247169971653e-2, -8.9672662444325007e-3, 9.2248978383109719e-4, 1.5143472266372874e-4, -1.6387009056475679e-6, -2.4405558979328144e-6, -1.0148113464009015e-7,
        -4.8518673570735556e-1, 1.0016737299946743e-1, -4.7074888613099918e-1, -5.8604054305076092e-3, 1.4300208240553435e-2, -6.7127991650300028e-5, -1.3703764889645475e-4, 9.0505213684444634e-6, 6.0368690647808607e-7, -2.2135404747652171e-7,
        -2.0950740076326087, -9.4447359463206877e-1, 4.0940512860493755e-1, 1.0261699700263508e-1, -5.3133241571955160e-3, -1.6634631550720911e-3, -5.9477519536647907e-5, 2.9651387319208926e-6, 1.6434499452070584e-6, 2.3720647656961084e-7,
        6.3315163285678715e-1, 3.5241082918420464, 2.1223076605364606e-1, -1.5648122502767368e-1, -9.1964075390801980e-3, 3.3896161239812411e-3, 2.1485178626085787e-4, -6.6261759864793735e-5, -5.9257969712852667e-6, 1.3918759086160525e-6,
    ];

    private static ReadOnlySpan<double> s_owenT0 => [-3392455.5, -470455.5, 2451544.5, 5373544.5, 8295544.5];

    /// <summary>
    /// Mean obliquity of the ecliptic (radians). Covers all non-JPL-Horizons
    /// branches selectable through <see cref="AstronomicalModelOverrides"/>.
    /// </summary>
    /// <param name="jdTt">Julian Day in TT.</param>
    /// <param name="overrides">Model selectors. Defaults to Vondrák 2011.</param>
    /// <returns>Mean obliquity in radians.</returns>
    public static double MeanObliquity(double jdTt, AstronomicalModelOverrides? overrides = null)
    {
        var models = overrides ?? AstronomicalModelOverrides.Default;
        var precModel = models.PrecessionLongTerm;
        var precModelShort = models.PrecessionShortTerm;
        var ctySwitch = models.ShortTermSwitchOverCenturies;
        var t = (jdTt - J2000) / JulianCentury;

        if (precModelShort == PrecessionModel.Iau1976 && System.Math.Abs(t) <= ctySwitch)
            return Iau1976Eps(t);
        if (precModel == PrecessionModel.Iau1976)
            return Iau1976Eps(t);
        if (precModelShort == PrecessionModel.Iau2000 && System.Math.Abs(t) <= ctySwitch)
            return Iau2000Eps(t);
        if (precModel == PrecessionModel.Iau2000)
            return Iau2000Eps(t);
        if (precModelShort == PrecessionModel.Iau2006 && System.Math.Abs(t) <= ctySwitch)
            return Iau2006Eps(t);
        if (precModel == PrecessionModel.Newcomb)
            return NewcombEps(jdTt);
        if (precModel == PrecessionModel.Iau2006)
            return Iau2006Eps(t);
        if (precModel == PrecessionModel.Bretagnon2003)
            return Bretagnon2003Eps(t);
        if (precModel == PrecessionModel.Simon1994)
            return Simon1994Eps(t);
        if (precModel == PrecessionModel.Williams1994)
            return Williams1994Eps(t);
        if (precModel == PrecessionModel.Laskar1986 || precModel == PrecessionModel.WilliamsEpsLaskar)
            return LaskarEps(t);
        if (precModel == PrecessionModel.Owen1990)
            return OwenEps(jdTt);

        // SEMOD_PREC_VONDRAK_2011 (default).
        VondrakLongTermEps(jdTt, out _, out var eps);
        return eps;
    }

    private static double Iau1976Eps(double t)
    {
        var p = System.Math.FusedMultiplyAdd(1.813e-3, t, -5.9e-4);
        p = System.Math.FusedMultiplyAdd(p, t, -46.8150);
        p = System.Math.FusedMultiplyAdd(p * t, 1.0, 84381.448);
        return p * ArcSecondsToRadians;
    }

    private static double Iau2000Eps(double t)
    {
        var p = System.Math.FusedMultiplyAdd(1.813e-3, t, -5.9e-4);
        p = System.Math.FusedMultiplyAdd(p, t, -46.84024);
        p = System.Math.FusedMultiplyAdd(p * t, 1.0, 84381.406);
        return p * ArcSecondsToRadians;
    }

    private static double Iau2006Eps(double t)
    {
        var p = System.Math.FusedMultiplyAdd(-4.34e-8, t, -5.76e-7);
        p = System.Math.FusedMultiplyAdd(p, t, 2.0034e-3);
        p = System.Math.FusedMultiplyAdd(p, t, -1.831e-4);
        p = System.Math.FusedMultiplyAdd(p, t, -46.836769);
        p = System.Math.FusedMultiplyAdd(p * t, 1.0, 84381.406);
        return p * ArcSecondsToRadians;
    }

    private static double Bretagnon2003Eps(double t)
    {
        var p = System.Math.FusedMultiplyAdd(-3e-11, t, -2.48e-8);
        p = System.Math.FusedMultiplyAdd(p, t, -5.23e-7);
        p = System.Math.FusedMultiplyAdd(p, t, 1.99911e-3);
        p = System.Math.FusedMultiplyAdd(p, t, -1.667e-4);
        p = System.Math.FusedMultiplyAdd(p, t, -46.836051);
        p = System.Math.FusedMultiplyAdd(p * t, 1.0, 84381.40880);
        return p * ArcSecondsToRadians;
    }

    private static double Simon1994Eps(double t)
    {
        var p = System.Math.FusedMultiplyAdd(2.5e-8, t, -5.1e-7);
        p = System.Math.FusedMultiplyAdd(p, t, 1.9989e-3);
        p = System.Math.FusedMultiplyAdd(p, t, -1.52e-4);
        p = System.Math.FusedMultiplyAdd(p, t, -46.80927);
        p = System.Math.FusedMultiplyAdd(p * t, 1.0, 84381.412);
        return p * ArcSecondsToRadians;
    }

    private static double Williams1994Eps(double t)
    {
        var p = System.Math.FusedMultiplyAdd(-1.0e-6, t, 2.0e-3);
        p = System.Math.FusedMultiplyAdd(p, t, -1.74e-4);
        p = System.Math.FusedMultiplyAdd(p, t, -46.833960);
        p = System.Math.FusedMultiplyAdd(p * t, 1.0, 84381.409);
        return p * ArcSecondsToRadians;
    }

    private static double LaskarEps(double tCenturies)
    {
        var t = tCenturies / 10.0;
        var p = System.Math.FusedMultiplyAdd(2.45e-10, t, 5.79e-9);
        p = System.Math.FusedMultiplyAdd(p, t, 2.787e-7);
        p = System.Math.FusedMultiplyAdd(p, t, 7.12e-7);
        p = System.Math.FusedMultiplyAdd(p, t, -3.905e-5);
        p = System.Math.FusedMultiplyAdd(p, t, -2.4967e-3);
        p = System.Math.FusedMultiplyAdd(p, t, -5.138e-3);
        p = System.Math.FusedMultiplyAdd(p, t, 1.99925);
        p = System.Math.FusedMultiplyAdd(p, t, -0.0155);
        p = System.Math.FusedMultiplyAdd(p, t, -468.093);
        p = System.Math.FusedMultiplyAdd(p * t, 1.0, 84381.448);
        return p * ArcSecondsToRadians;
    }

    private static double NewcombEps(double jd)
    {
        // swephlib.c#L924-L926, t measured from JD 2396758.0 (≈ B1850.0 epoch).
        var tn = (jd - 2_396_758.0) / 36525.0;
        var arcseconds = 0.0017 * tn * tn * tn - 0.0085 * tn * tn - 46.837 * tn + 84451.68;
        return arcseconds * ArcSecondsToRadians;
    }

    private static double OwenEps(double jd)
    {
        GetOwenT0(jd, out var t0, out var icof);
        Span<double> tau = stackalloc double[10];
        Span<double> k = stackalloc double[10];
        BuildOwenChebyshev(jd, t0, tau, k);
        var coef = s_owenEps0.Slice(icof * OwenStride, OwenStride);
        var eps = 0.0;
        for (var i = 0; i < 10; i++)
            eps += k[i] * coef[i];
        return eps * DegToRad;
    }

    private static void GetOwenT0(double jd, out double t0, out int icof)
    {
        // swephlib.c#L747-L761
        t0 = s_owenT0[0];
        var j = 0;
        for (var i = 1; i < 5; i++)
        {
            if (jd >= (s_owenT0[i - 1] + s_owenT0[i]) / 2)
            {
                t0 = s_owenT0[i];
                j++;
            }
        }
        icof = j;
    }

    private static void BuildOwenChebyshev(double jd, double t0, Span<double> tau, Span<double> k)
    {
        // swephlib.c#L770-L787 — Chebyshev polynomial basis up to T9.
        tau[0] = 0;
        tau[1] = (jd - t0) / 36525.0 / 40.0;
        for (var i = 2; i <= 9; i++)
            tau[i] = tau[1] * tau[i - 1];
        k[0] = 1;
        k[1] = tau[1];
        k[2] = 2 * tau[2] - 1;
        k[3] = 4 * tau[3] - 3 * tau[1];
        k[4] = 8 * tau[4] - 8 * tau[2] + 1;
        k[5] = 16 * tau[5] - 20 * tau[3] + 5 * tau[1];
        k[6] = 32 * tau[6] - 48 * tau[4] + 18 * tau[2] - 1;
        k[7] = 64 * tau[7] - 112 * tau[5] + 56 * tau[3] - 7 * tau[1];
        k[8] = 128 * tau[8] - 256 * tau[6] + 160 * tau[4] - 32 * tau[2] + 1;
        k[9] = 256 * tau[9] - 576 * tau[7] + 432 * tau[5] - 120 * tau[3] + 9 * tau[1];
    }

    /// <summary>
    /// Vondrák 2011 long-term general precession in longitude (dpre) and
    /// mean obliquity (eps), both in radians. Public so <see cref="ApplySpeed"/>
    /// can read the precession rate without re-running the full Vondrák series.
    /// </summary>
    public static void VondrakLongTerm(double jdTt, out double dpre, out double eps)
        => VondrakLongTermEps(jdTt, out dpre, out eps);

    /// <summary>
    /// Vondrák 2011 long-term general precession in longitude (dpre) and
    /// mean obliquity (eps), both in radians.
    /// </summary>
    private static void VondrakLongTermEps(double jdTt, out double dpre, out double eps)
    {
        var t = (jdTt - J2000) / 36525.0;
        var p = 0.0;
        var q = 0.0;
        // periodic terms
        for (var i = 0; i < 10; i++)
        {
            var w = TwoPi * t;
            var a = w / s_pepsPeriod[i];
            var s = System.Math.Sin(a);
            var c = System.Math.Cos(a);
            p += c * s_pepsCosP[i] + s * s_pepsSinP[i];
            q += c * s_pepsCosQ[i] + s * s_pepsSinQ[i];
        }
        // polynomial terms
        var w2 = 1.0;
        for (var i = 0; i < 4; i++)
        {
            p += s_pepsPolP[i] * w2;
            q += s_pepsPolQ[i] * w2;
            w2 *= t;
        }
        dpre = p * ArcSecondsToRadians;
        eps = q * ArcSecondsToRadians;
    }

    /// <summary>
    /// Builds the precession matrix that maps an equatorial position from
    /// J2000 to the equator-and-equinox of date <paramref name="jdTt"/>.
    /// Multiplying by the matrix is equivalent to <see cref="Apply"/> with
    /// <c>jdSourceTt = J2000</c> and <c>jdTargetTt = jdTt</c>.
    /// </summary>
    public static Matrix3x3 BuildMatrixFromJ2000(double jdTt, AstronomicalModelOverrides? overrides = null)
    {
        Span<double> column0 = stackalloc double[3];
        Span<double> column1 = stackalloc double[3];
        Span<double> column2 = stackalloc double[3];
        column0[0] = 1; column0[1] = 0; column0[2] = 0;
        column1[0] = 0; column1[1] = 1; column1[2] = 0;
        column2[0] = 0; column2[1] = 0; column2[2] = 1;
        Apply(column0, J2000, jdTt, overrides);
        Apply(column1, J2000, jdTt, overrides);
        Apply(column2, J2000, jdTt, overrides);
        return new Matrix3x3(
            column0[0], column1[0], column2[0],
            column0[1], column1[1], column2[1],
            column0[2], column1[2], column2[2]);
    }

    /// <summary>
    /// Rotates an equatorial-Cartesian position vector from one TT epoch to
    /// another. Single-step rotations always go via J2000; chained
    /// source-to-target moves are built by precessing source→J2000→target.
    /// </summary>
    /// <param name="xyz">In/out — vector of length ≥ 3 in equatorial-rect units.</param>
    /// <param name="jdSourceTt">Source-frame TT epoch.</param>
    /// <param name="jdTargetTt">Target-frame TT epoch.</param>
    /// <param name="overrides">Model selectors.</param>
    public static void Apply(Span<double> xyz, double jdSourceTt, double jdTargetTt, AstronomicalModelOverrides? overrides = null)
    {
        if (xyz.Length < 3)
            throw new ArgumentException("Vector span must contain at least 3 doubles.", nameof(xyz));
        if (jdSourceTt == jdTargetTt)
            return;
        if (jdSourceTt == J2000)
        {
            PrecessFromJ2000(xyz, jdTargetTt, overrides ?? AstronomicalModelOverrides.Default);
            return;
        }
        if (jdTargetTt == J2000)
        {
            PrecessToJ2000(xyz, jdSourceTt, overrides ?? AstronomicalModelOverrides.Default);
            return;
        }
        // Two-step: source → J2000 → target (matches the documented C usage,
        // see comment in swephlib.c#L1361-L1372).
        var models = overrides ?? AstronomicalModelOverrides.Default;
        PrecessToJ2000(xyz, jdSourceTt, models);
        PrecessFromJ2000(xyz, jdTargetTt, models);
    }

    /// <summary>
    /// Velocity-aware precession. Performs the rotational precession on the
    /// velocity component AND adds the precession-rate (general precession in
    /// longitude) so the velocity reflects the change of equinox. The caller
    /// supplies the full 6-component state because the rate term is applied
    /// in ecliptic-polar coordinates and needs the position vector to read
    /// the longitude direction.
    /// </summary>
    /// <param name="state">In/out — six doubles (pos[0..2] + vel[3..5]) at the source frame.</param>
    /// <param name="jdSourceTt">Source-frame TT epoch.</param>
    /// <param name="jdTargetTt">Target-frame TT epoch. One of the two epochs MUST be J2000.</param>
    /// <param name="overrides">Model selectors.</param>
    public static void ApplySpeed(Span<double> state, double jdSourceTt, double jdTargetTt, AstronomicalModelOverrides? overrides = null)
    {
        if (state.Length < 6)
            throw new ArgumentException("State span must contain at least 6 doubles.", nameof(state));
        // Note: unlike Apply (position-only), we cannot short-circuit when
        // source == target. The C library's swi_precess_speed adds the
        // precession-rate (dPre) term unconditionally, even when the
        // rotation degenerates to identity at <i>tjd</i> = J2000. Skipping
        // the function here drops a ≈ 6.7e-7 rad/day longitude rate that
        // shows up in any fixed-star or body velocity evaluated at exactly
        // J2000.
        var models = overrides ?? AstronomicalModelOverrides.Default;
        // direction: J_TO_J2000 → fac = -1, oe = oec2000.
        //            J2000_TO_J → fac = +1, oe = oec at jdTargetTt.
        // C uses pdp->teval as the body's TT epoch in both branches; the rate
        // term at sweph.c#L3576-L3580 reads dpre/dpre2 at THAT epoch. Mirrors:
        //   if (J_TO_J2000) { oe = swed.oec2000; t = body's epoch; fac = -1 }
        //   else            { oe = swed.oec;     t = body's epoch; fac = +1 }
        // The body's epoch is whichever side is non-J2000.
        double tBody, fac;
        Span<double> oeSc = stackalloc double[2]; // [seps, ceps]
        // jdSourceTt == J2000 must be checked FIRST: when both endpoints
        // happen to equal J2000 (e.g. body / fixstar requests evaluated at
        // exactly the J2000 epoch), the user-supplied call shape carries
        // the intended direction. The fixstar and body pipelines always
        // call ApplySpeed(J2000, tjd) for J2000_TO_J — fac = +1, oe = oec
        // at the body's epoch — so we must not silently flip the sign by
        // matching the J_TO_J2000 branch on a degenerate equality.
        if (jdSourceTt == J2000)
        {
            // J2000_TO_J: target is body epoch.
            tBody = jdTargetTt;
            fac = +1.0;
            var epsBody = MeanObliquity(tBody, models);
            oeSc[0] = System.Math.Sin(epsBody);
            oeSc[1] = System.Math.Cos(epsBody);
        }
        else if (jdTargetTt == J2000)
        {
            // J_TO_J2000: source is body epoch.
            tBody = jdSourceTt;
            fac = -1.0;
            var eps2000 = MeanObliquity(J2000, models);
            oeSc[0] = System.Math.Sin(eps2000);
            oeSc[1] = System.Math.Cos(eps2000);
        }
        else
        {
            // Two-step via J2000 (same as Apply): source → J2000 → target.
            ApplySpeed(state, jdSourceTt, J2000, models);
            ApplySpeed(state, J2000, jdTargetTt, models);
            return;
        }

        // 1. Rotational precession on the velocity component.
        Span<double> vel = state.Slice(3, 3);
        Apply(vel, jdSourceTt, jdTargetTt, models);

        // 2. Add the precession-rate term. C does:
        //      swi_coortrf2(xx, xx, seps, ceps);          // equ → ecl (pos)
        //      swi_coortrf2(xx+3, xx+3, seps, ceps);      // equ → ecl (vel)
        //      swi_cartpol_sp(xx, xx);
        //      xx[3] += dPrec * fac;     // dpre/day at the body epoch
        //      swi_polcart_sp(xx, xx);
        //      swi_coortrf2(xx, xx, -seps, ceps);         // ecl → equ
        //      swi_coortrf2(xx+3, xx+3, -seps, ceps);
        // We use FrameTransform.EquatorialToEcliptic which is exactly
        // swi_coortrf2(*, *, sineps, coseps) when called with positive ε,
        // and EclipticToEquatorial which is the negated variant.
        var seps = oeSc[0];
        var ceps = oeSc[1];
        Span<double> sixD = stackalloc double[6];
        // Pos to ecliptic.
        var px = state[0]; var py = state[1]; var pz = state[2];
        var vx = state[3]; var vy = state[4]; var vz = state[5];
        // swi_coortrf2(seps, ceps): y' = y·c + z·s; z' = -y·s + z·c.
        sixD[0] = px;
        sixD[1] = py * ceps + pz * seps;
        sixD[2] = -py * seps + pz * ceps;
        sixD[3] = vx;
        sixD[4] = vy * ceps + vz * seps;
        sixD[5] = -vy * seps + vz * ceps;

        Span<double> polarSix = stackalloc double[6];
        Polar.CartesianToPolarWithSpeed(sixD, polarSix);

        // Precession rate, model-dependent (sweph.c#L3574-L3582). For the
        // Vondrák long-term default we read dpre at t and at t+1 and use the
        // numerical difference (rate per day). Other models use the
        // legacy 50.290966 + 0.0222226·T (arcseconds per year on Julian century).
        var precModel = models.PrecessionLongTerm;
        double dPreRate;
        if (precModel == PrecessionModel.Vondrak2011)
        {
            VondrakLongTermEps(tBody, out var dpre, out _);
            VondrakLongTermEps(tBody + 1.0, out var dpre2, out _);
            dPreRate = dpre2 - dpre; // radians per day
        }
        else
        {
            var tprec = (tBody - J2000) / 36525.0;
            dPreRate = (50.290966 + 0.0222226 * tprec) / 3600.0 / 365.25 * DegToRad;
        }
        polarSix[3] += dPreRate * fac;

        Polar.PolarToCartesianWithSpeed(polarSix, sixD);

        // ecl → equ via swi_coortrf2(-seps, ceps): y' = y·c - z·s; z' = y·s + z·c.
        state[0] = sixD[0];
        state[1] = sixD[1] * ceps - sixD[2] * seps;
        state[2] = sixD[1] * seps + sixD[2] * ceps;
        state[3] = sixD[3];
        state[4] = sixD[4] * ceps - sixD[5] * seps;
        state[5] = sixD[4] * seps + sixD[5] * ceps;
    }

    private static void PrecessFromJ2000(Span<double> xyz, double jdTargetTt, AstronomicalModelOverrides models)
        => PrecessSingleStep(xyz, jdTargetTt, direction: -1, models);

    private static void PrecessToJ2000(Span<double> xyz, double jdSourceTt, AstronomicalModelOverrides models)
        => PrecessSingleStep(xyz, jdSourceTt, direction: 1, models);

    private static void PrecessSingleStep(Span<double> xyz, double jdEpoch, int direction, AstronomicalModelOverrides models)
    {
        if (jdEpoch == J2000)
            return;
        var t = (jdEpoch - J2000) / JulianCentury;
        var precLong = models.PrecessionLongTerm;
        var precShort = models.PrecessionShortTerm;
        var ctySwitch = models.ShortTermSwitchOverCenturies;

        if (precShort == PrecessionModel.Iau1976 && System.Math.Abs(t) <= ctySwitch)
        {
            Precess1(xyz, jdEpoch, direction, PrecessionModel.Iau1976);
            return;
        }
        if (precLong == PrecessionModel.Iau1976) { Precess1(xyz, jdEpoch, direction, PrecessionModel.Iau1976); return; }

        if (precShort == PrecessionModel.Iau2000 && System.Math.Abs(t) <= ctySwitch)
        {
            Precess1(xyz, jdEpoch, direction, PrecessionModel.Iau2000);
            return;
        }
        if (precLong == PrecessionModel.Iau2000) { Precess1(xyz, jdEpoch, direction, PrecessionModel.Iau2000); return; }

        if (precShort == PrecessionModel.Iau2006 && System.Math.Abs(t) <= ctySwitch)
        {
            Precess1(xyz, jdEpoch, direction, PrecessionModel.Iau2006);
            return;
        }
        if (precLong == PrecessionModel.Iau2006) { Precess1(xyz, jdEpoch, direction, PrecessionModel.Iau2006); return; }

        if (precLong == PrecessionModel.Bretagnon2003)
        {
            Precess1(xyz, jdEpoch, direction, PrecessionModel.Bretagnon2003);
            return;
        }
        if (precLong == PrecessionModel.Newcomb)
        {
            Precess1(xyz, jdEpoch, direction, PrecessionModel.Newcomb);
            return;
        }
        if (precLong == PrecessionModel.Laskar1986)
        {
            Precess2(xyz, jdEpoch, direction, models, PrecessionModel.Laskar1986);
            return;
        }
        if (precLong == PrecessionModel.Simon1994)
        {
            Precess2(xyz, jdEpoch, direction, models, PrecessionModel.Simon1994);
            return;
        }
        if (precLong == PrecessionModel.Williams1994 || precLong == PrecessionModel.WilliamsEpsLaskar)
        {
            Precess2(xyz, jdEpoch, direction, models, PrecessionModel.Williams1994);
            return;
        }
        if (precLong == PrecessionModel.Owen1990)
        {
            Precess3Owen(xyz, jdEpoch, direction);
            return;
        }
        // SEMOD_PREC_VONDRAK_2011 (default).
        Precess3Vondrak(xyz, jdEpoch, direction);
    }

    /// <summary>Polynomial Z / z / θ precession (IAU 1976 / 2000 / 2006 / Bretagnon).</summary>
    private static void Precess1(Span<double> r, double j, int direction, PrecessionModel method)
    {
        var t = (j - J2000) / JulianCentury;
        double zz, z, th;
        switch (method)
        {
            case PrecessionModel.Iau1976:
                zz = ((0.017998 * t + 0.30188) * t + 2306.2181) * t * ArcSecondsToRadians;
                z = ((0.018203 * t + 1.09468) * t + 2306.2181) * t * ArcSecondsToRadians;
                th = ((-0.041833 * t - 0.42665) * t + 2004.3109) * t * ArcSecondsToRadians;
                break;
            case PrecessionModel.Iau2000:
                zz = (((((-0.0000002 * t - 0.0000327) * t + 0.0179663) * t + 0.3019015) * t + 2306.0809506) * t + 2.5976176) * ArcSecondsToRadians;
                z = (((((-0.0000003 * t - 0.000047) * t + 0.0182237) * t + 1.0947790) * t + 2306.0803226) * t - 2.5976176) * ArcSecondsToRadians;
                th = ((((-0.0000001 * t - 0.0000601) * t - 0.0418251) * t - 0.4269353) * t + 2004.1917476) * t * ArcSecondsToRadians;
                break;
            case PrecessionModel.Iau2006:
                zz = (((((-0.0000003173 * t - 0.000005971) * t + 0.01801828) * t + 0.2988499) * t + 2306.083227) * t + 2.650545) * ArcSecondsToRadians;
                z = (((((-0.0000002904 * t - 0.000028596) * t + 0.01826837) * t + 1.0927348) * t + 2306.077181) * t - 2.650545) * ArcSecondsToRadians;
                th = ((((-0.00000011274 * t - 0.000007089) * t - 0.04182264) * t - 0.4294934) * t + 2004.191903) * t * ArcSecondsToRadians;
                break;
            case PrecessionModel.Bretagnon2003:
                zz = ((((((-0.00000000013 * t - 0.0000003040) * t - 0.000005708) * t + 0.01801752) * t + 0.3023262) * t + 2306.080472) * t + 2.72767) * ArcSecondsToRadians;
                z = ((((((-0.00000000005 * t - 0.0000002486) * t - 0.000028276) * t + 0.01826676) * t + 1.0956768) * t + 2306.076070) * t - 2.72767) * ArcSecondsToRadians;
                th = ((((((0.000000000009 * t + 0.00000000036) * t - 0.0000001127) * t - 0.000007291) * t - 0.04182364) * t - 0.4266980) * t + 2004.190936) * t * ArcSecondsToRadians;
                break;
            case PrecessionModel.Newcomb:
                {
                    // Kinoshita 1975 expansion, swephlib.c#L1100-L1116.
                    var mills = 365242.198782;
                    var t1 = (J2000 - B1850) / mills;
                    var t2 = (j - B1850) / mills;
                    var tt = t2 - t1;
                    var tt2 = tt * tt;
                    var tt3 = tt2 * tt;
                    var z1 = 23035.5548 + 139.720 * t1 + 0.069 * t1 * t1;
                    zz = z1 * tt + (30.242 - 0.269 * t1) * tt2 + 17.996 * tt3;
                    z = z1 * tt + (109.478 - 0.387 * t1) * tt2 + 18.324 * tt3;
                    th = (20051.125 - 85.294 * t1 - 0.365 * t1 * t1) * tt + (-42.647 - 0.365 * t1) * tt2 - 41.802 * tt3;
                    zz *= ArcSecondsToRadians;
                    z *= ArcSecondsToRadians;
                    th *= ArcSecondsToRadians;
                    break;
                }
            default:
                return;
        }

        var sinth = System.Math.Sin(th);
        var costh = System.Math.Cos(th);
        var sinZ = System.Math.Sin(zz);
        var cosZ = System.Math.Cos(zz);
        var sinz = System.Math.Sin(z);
        var cosz = System.Math.Cos(z);
        var aa = cosZ * costh;
        var bb = sinZ * costh;

        Span<double> x = stackalloc double[3];
        if (direction < 0)
        {
            // From J2000.0 to J
            x[0] = (aa * cosz - sinZ * sinz) * r[0]
                   - (bb * cosz + cosZ * sinz) * r[1]
                   - sinth * cosz * r[2];
            x[1] = (aa * sinz + sinZ * cosz) * r[0]
                   - (bb * sinz - cosZ * cosz) * r[1]
                   - sinth * sinz * r[2];
            x[2] = cosZ * sinth * r[0]
                   - sinZ * sinth * r[1]
                   + costh * r[2];
        }
        else
        {
            // From J to J2000.0
            x[0] = (aa * cosz - sinZ * sinz) * r[0]
                   + (aa * sinz + sinZ * cosz) * r[1]
                   + cosZ * sinth * r[2];
            x[1] = -(bb * cosz + cosZ * sinz) * r[0]
                   - (bb * sinz - cosZ * cosz) * r[1]
                   - sinZ * sinth * r[2];
            x[2] = -sinth * cosz * r[0]
                   - sinth * sinz * r[1]
                   + costh * r[2];
        }
        r[0] = x[0]; r[1] = x[1]; r[2] = x[2];
    }

    /// <summary>Williams / Simon / Laskar — port of <c>precess_2</c>.</summary>
    private static void Precess2(Span<double> r, double j, int direction, AstronomicalModelOverrides models, PrecessionModel method)
    {
        if (j == J2000)
            return;
        double[] pAcof, nodeCof, inclCof;
        switch (method)
        {
            case PrecessionModel.Laskar1986:
                pAcof = s_pAcofLaskar; nodeCof = s_nodeCofLaskar; inclCof = s_inclCofLaskar; break;
            case PrecessionModel.Simon1994:
                pAcof = s_pAcofSimon; nodeCof = s_nodeCofSimon; inclCof = s_inclCofSimon; break;
            case PrecessionModel.Williams1994:
                pAcof = s_pAcofWilliams; nodeCof = s_nodeCofWilliams; inclCof = s_inclCofWilliams; break;
            default:
                pAcof = s_pAcofLaskar; nodeCof = s_nodeCofLaskar; inclCof = s_inclCofLaskar; break;
        }
        var t = (j - J2000) / JulianCentury;
        double eps;
        if (direction == 1)
            eps = MeanObliquity(j, models);
        else
            eps = MeanObliquity(J2000, models);
        var sineps = System.Math.Sin(eps);
        var coseps = System.Math.Cos(eps);
        Span<double> x = stackalloc double[3];
        x[0] = r[0];
        var zz = coseps * r[1] + sineps * r[2];
        x[2] = -sineps * r[1] + coseps * r[2];
        x[1] = zz;
        // Precession in longitude
        t /= 10.0;
        var pA = pAcof[0];
        for (var i = 1; i < 10; i++)
            pA = pA * t + pAcof[i];
        pA *= ArcSecondsToRadians * t;
        // Node of moving ecliptic on J2000 ecliptic
        var w = nodeCof[0];
        for (var i = 1; i < 11; i++)
            w = w * t + nodeCof[i];
        // Rotate about z axis to the node
        double zr = direction == 1 ? w + pA : w;
        var bb = System.Math.Cos(zr);
        var aa = System.Math.Sin(zr);
        zr = bb * x[0] + aa * x[1];
        x[1] = -aa * x[0] + bb * x[1];
        x[0] = zr;
        // Rotate about new x axis by inclination of moving ecliptic on J2000 ecliptic
        var z2 = inclCof[0];
        for (var i = 1; i < 11; i++)
            z2 = z2 * t + inclCof[i];
        if (direction == 1)
            z2 = -z2;
        bb = System.Math.Cos(z2);
        aa = System.Math.Sin(z2);
        z2 = bb * x[1] + aa * x[2];
        x[2] = -aa * x[1] + bb * x[2];
        x[1] = z2;
        // Rotate about new z axis back from the node
        zr = direction == 1 ? -w : -w - pA;
        bb = System.Math.Cos(zr);
        aa = System.Math.Sin(zr);
        zr = bb * x[0] + aa * x[1];
        x[1] = -aa * x[0] + bb * x[1];
        x[0] = zr;
        // Rotate about x axis to final equator
        if (direction == 1)
            eps = MeanObliquity(J2000, models);
        else
            eps = MeanObliquity(j, models);
        sineps = System.Math.Sin(eps);
        coseps = System.Math.Cos(eps);
        z2 = coseps * x[1] - sineps * x[2];
        x[2] = sineps * x[1] + coseps * x[2];
        x[1] = z2;
        r[0] = x[0]; r[1] = x[1]; r[2] = x[2];
    }

    /// <summary>Vondrák 2011 — port of <c>precess_3</c> with <c>SEMOD_PREC_VONDRAK_2011</c>.</summary>
    private static void Precess3Vondrak(Span<double> r, double j, int direction)
    {
        Span<double> pmat = stackalloc double[9];
        BuildVondrakPMat(j, pmat);
        ApplyPMat(r, pmat, direction);
    }

    /// <summary>Owen 1990 — port of <c>precess_3</c> with <c>SEMOD_PREC_OWEN_1990</c>.</summary>
    private static void Precess3Owen(Span<double> r, double j, int direction)
    {
        Span<double> pmat = stackalloc double[9];
        BuildOwenPMat(j, pmat);
        ApplyPMat(r, pmat, direction);
    }

    private static void ApplyPMat(Span<double> r, ReadOnlySpan<double> pmat, int direction)
    {
        Span<double> x = stackalloc double[3];
        if (direction == -1)
        {
            // From J2000.0 to J — multiply by P
            for (var i = 0; i < 3; i++)
            {
                var jOff = i * 3;
                x[i] = r[0] * pmat[jOff + 0] + r[1] * pmat[jOff + 1] + r[2] * pmat[jOff + 2];
            }
        }
        else
        {
            // From J to J2000.0 — multiply by P^T
            for (var i = 0; i < 3; i++)
                x[i] = r[0] * pmat[i + 0] + r[1] * pmat[i + 3] + r[2] * pmat[i + 6];
        }
        r[0] = x[0]; r[1] = x[1]; r[2] = x[2];
    }

    private static void BuildVondrakPMat(double jdTt, Span<double> rp)
    {
        Span<double> peqr = stackalloc double[3];
        Span<double> pecl = stackalloc double[3];
        VondrakPequ(jdTt, peqr);
        VondrakPecl(jdTt, pecl);
        // equinox = peqr × pecl (normalized)
        Span<double> v = stackalloc double[3];
        v[0] = peqr[1] * pecl[2] - peqr[2] * pecl[1];
        v[1] = peqr[2] * pecl[0] - peqr[0] * pecl[2];
        v[2] = peqr[0] * pecl[1] - peqr[1] * pecl[0];
        var w = System.Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
        Span<double> eqx = stackalloc double[3];
        eqx[0] = v[0] / w;
        eqx[1] = v[1] / w;
        eqx[2] = v[2] / w;
        // v = peqr × eqx
        v[0] = peqr[1] * eqx[2] - peqr[2] * eqx[1];
        v[1] = peqr[2] * eqx[0] - peqr[0] * eqx[2];
        v[2] = peqr[0] * eqx[1] - peqr[1] * eqx[0];
        rp[0] = eqx[0]; rp[1] = eqx[1]; rp[2] = eqx[2];
        rp[3] = v[0]; rp[4] = v[1]; rp[5] = v[2];
        rp[6] = peqr[0]; rp[7] = peqr[1]; rp[8] = peqr[2];
    }

    private static void VondrakPecl(double jdTt, Span<double> vec)
    {
        var t = (jdTt - J2000) / 36525.0;
        var p = 0.0;
        var q = 0.0;
        for (var i = 0; i < 8; i++)
        {
            var w = TwoPi * t;
            var a = w / s_peclPeriod[i];
            var s = System.Math.Sin(a);
            var c = System.Math.Cos(a);
            p += c * s_peclCosP[i] + s * s_peclSinP[i];
            q += c * s_peclCosQ[i] + s * s_peclSinQ[i];
        }
        var w2 = 1.0;
        for (var i = 0; i < 4; i++)
        {
            p += s_peclPolP[i] * w2;
            q += s_peclPolQ[i] * w2;
            w2 *= t;
        }
        p *= ArcSecondsToRadians;
        q *= ArcSecondsToRadians;
        var z = 1 - p * p - q * q;
        z = z < 0 ? 0 : System.Math.Sqrt(z);
        var eps0 = VondrakEps0Arcsec * ArcSecondsToRadians;
        var s2 = System.Math.Sin(eps0);
        var c2 = System.Math.Cos(eps0);
        vec[0] = p;
        vec[1] = -q * c2 - z * s2;
        vec[2] = -q * s2 + z * c2;
    }

    private static void VondrakPequ(double jdTt, Span<double> veq)
    {
        var t = (jdTt - J2000) / 36525.0;
        var x = 0.0;
        var y = 0.0;
        for (var i = 0; i < 14; i++)
        {
            var w = TwoPi * t;
            var a = w / s_pequPeriod[i];
            var s = System.Math.Sin(a);
            var c = System.Math.Cos(a);
            x += c * s_pequCosX[i] + s * s_pequSinX[i];
            y += c * s_pequCosY[i] + s * s_pequSinY[i];
        }
        var w2 = 1.0;
        for (var i = 0; i < 4; i++)
        {
            x += s_pequPolX[i] * w2;
            y += s_pequPolY[i] * w2;
            w2 *= t;
        }
        x *= ArcSecondsToRadians;
        y *= ArcSecondsToRadians;
        veq[0] = x;
        veq[1] = y;
        var w3 = x * x + y * y;
        veq[2] = w3 < 1 ? System.Math.Sqrt(1 - w3) : 0;
    }

    private static void BuildOwenPMat(double jdTt, Span<double> rp)
    {
        // swephlib.c#L764-L826
        GetOwenT0(jdTt, out var t0, out var icof);
        Span<double> tau = stackalloc double[10];
        Span<double> k = stackalloc double[10];
        BuildOwenChebyshev(jdTt, t0, tau, k);

        double psia = 0, oma = 0, chia = 0;
        var psiCoef = s_owenPsia.Slice(icof * OwenStride, OwenStride);
        var omaCoef = s_owenOma.Slice(icof * OwenStride, OwenStride);
        var chiaCoef = s_owenChia.Slice(icof * OwenStride, OwenStride);
        for (var i = 0; i < 10; i++)
        {
            psia += k[i] * psiCoef[i];
            oma += k[i] * omaCoef[i];
            chia += k[i] * chiaCoef[i];
        }
        var eps0 = 84381.448 / 3600.0 * DegToRad;
        psia *= DegToRad;
        chia *= DegToRad;
        oma *= DegToRad;
        var coseps0 = System.Math.Cos(eps0);
        var sineps0 = System.Math.Sin(eps0);
        var coschia = System.Math.Cos(chia);
        var sinchia = System.Math.Sin(chia);
        var cospsia = System.Math.Cos(psia);
        var sinpsia = System.Math.Sin(psia);
        var cosoma = System.Math.Cos(oma);
        var sinoma = System.Math.Sin(oma);

        rp[0] = coschia * cospsia + sinchia * cosoma * sinpsia;
        rp[1] = (-coschia * sinpsia + sinchia * cosoma * cospsia) * coseps0 + sinchia * sinoma * sineps0;
        rp[2] = (-coschia * sinpsia + sinchia * cosoma * cospsia) * sineps0 - sinchia * sinoma * coseps0;
        rp[3] = -sinchia * cospsia + coschia * cosoma * sinpsia;
        rp[4] = (sinchia * sinpsia + coschia * cosoma * cospsia) * coseps0 + coschia * sinoma * sineps0;
        rp[5] = (sinchia * sinpsia + coschia * cosoma * cospsia) * sineps0 - coschia * sinoma * coseps0;
        rp[6] = sinoma * sinpsia;
        rp[7] = sinoma * cospsia * coseps0 - cosoma * sineps0;
        rp[8] = sinoma * cospsia * sineps0 + cosoma * coseps0;
    }
}
