using SharpAstrology.DataModels;
using SharpAstrology.Enums;
using SharpAstrology.Ephemerides.Enums;
using SharpAstrology.Interfaces;
using SwissEphNet;

namespace SharpAstrology.Ephemerides;

public sealed class SwissEphemeridesService
{
    readonly string _rootPathToEph;
    readonly EphType _ephType;
    
    public SwissEphemeridesService(string? rootPathToEph=null, EphType ephType=EphType.Swiss)
    {
        _rootPathToEph = rootPathToEph ?? Path.Join(AppContext.BaseDirectory, "Data", "Ephe");
        _ephType = ephType;
    }
    
    public IEphemerides CreateContext(Ayanamsas ayanamsa = Ayanamsas.FagenBradley)
    {
        var eph = new SwissEph();
        switch (_ephType)
        {
            case EphType.Swiss:
                eph.swe_set_ephe_path(_rootPathToEph);
                break;
            case EphType.Jpl:
                eph.swe_set_jpl_file(_rootPathToEph);
                break;
            case EphType.Moshier:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        eph.swe_set_sid_mode((int)MapAyanamsas(ayanamsa), 0, 0);
        
        if (_ephType is EphType.Swiss or EphType.Jpl)
        {
            eph.OnLoadFile += (s, e) => {
                e.File = File.OpenRead(e.FileName
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace("[ephe]", _rootPathToEph));
            };
        }

        return new SwissEphemerides(eph, _ephType);
    }
    
    SwissEphAyanamsas MapAyanamsas(Ayanamsas ayanamasa) => ayanamasa switch
    {
        Ayanamsas.FagenBradley => SwissEphAyanamsas.FagenBradley,
        Ayanamsas.Lahiri => SwissEphAyanamsas.Lahiri,
        Ayanamsas.Deluce => SwissEphAyanamsas.Deluce,
        Ayanamsas.Raman => SwissEphAyanamsas.Raman,
        Ayanamsas.Ushashashi => SwissEphAyanamsas.Ushashashi,
        Ayanamsas.Krishnamurti => SwissEphAyanamsas.Krishnamurti,
        Ayanamsas.DjwhalKhul => SwissEphAyanamsas.DjwhalKhul,
        Ayanamsas.Yukteshwar => SwissEphAyanamsas.Yukteshwar,
        Ayanamsas.JNBhasin => SwissEphAyanamsas.JNBhasin,
        Ayanamsas.BabylonianKugler1 => SwissEphAyanamsas.BabylonianKugler1,
        Ayanamsas.BabylonianKugler2 => SwissEphAyanamsas.BabylonianKugler2,
        Ayanamsas.BabylonianKugler3 => SwissEphAyanamsas.BabylonianKugler3,
        Ayanamsas.BabylonianHuber => SwissEphAyanamsas.BabylonianHuber,
        Ayanamsas.BabylonianEtaPiscium => SwissEphAyanamsas.BabylonianEtaPiscium,
        Ayanamsas.BabylonianAldebaran15Tau => SwissEphAyanamsas.BabylonianAldebaran15Tau,
        Ayanamsas.Hipparchos => SwissEphAyanamsas.Hipparchos,
        Ayanamsas.Sassanian => SwissEphAyanamsas.Sassanian,
        Ayanamsas.GalacticCenter0Sag => SwissEphAyanamsas.GalacticCenter0Sag,
        Ayanamsas.J2000 => SwissEphAyanamsas.J2000,
        Ayanamsas.J1900 => SwissEphAyanamsas.J1900,
        Ayanamsas.B1950 => SwissEphAyanamsas.B1950,
        Ayanamsas.Suryasiddhanta => SwissEphAyanamsas.Suryasiddhanta,
        Ayanamsas.SuryasiddhantaMeanSun => SwissEphAyanamsas.SuryasiddhantaMeanSun,
        Ayanamsas.Aryabhata => SwissEphAyanamsas.Aryabhata,
        Ayanamsas.AryabhataMeanSun => SwissEphAyanamsas.AryabhataMeanSun,
        Ayanamsas.SSRevati => SwissEphAyanamsas.SSRevati,
        Ayanamsas.SSCitra => SwissEphAyanamsas.SSCitra,
        Ayanamsas.TrueCitra => SwissEphAyanamsas.TrueCitra,
        Ayanamsas.TrueRevati => SwissEphAyanamsas.TrueRevati,
        Ayanamsas.TruePushyaPvrnRao => SwissEphAyanamsas.TruePushyaPvrnRao,
        Ayanamsas.GalacticCenterGilBrand => SwissEphAyanamsas.GalacticCenterGilBrand,
        Ayanamsas.GalacticEquatorIAU1958 => SwissEphAyanamsas.GalacticEquatorIAU1958,
        Ayanamsas.GalacticEquator => SwissEphAyanamsas.GalacticEquator,
        Ayanamsas.GalacticEquatorMidMula => SwissEphAyanamsas.GalacticEquatorMidMula,
        Ayanamsas.SkydramMardyks => SwissEphAyanamsas.SkydramMardyks,
        Ayanamsas.TrueMulaChandraHari => SwissEphAyanamsas.TrueMulaChandraHari,
        Ayanamsas.GalacticCenterMidMula => SwissEphAyanamsas.GalacticCenterMidMula,
        Ayanamsas.Aryabhata522 => SwissEphAyanamsas.Aryabhata522,
        Ayanamsas.BabylonianBritton => SwissEphAyanamsas.BabylonianBritton,
        Ayanamsas.VedicSheoran => SwissEphAyanamsas.VedicSheoran,
        Ayanamsas.CochraneGalCenter0Cap => SwissEphAyanamsas.CochraneGalCenter0Cap,
        Ayanamsas.GalacticEquatorFiorenza => SwissEphAyanamsas.GalacticEquatorFiorenza,
        Ayanamsas.VettiusValens => SwissEphAyanamsas.VettiusValens,
        Ayanamsas.Lahiri1940 => SwissEphAyanamsas.Lahiri1940,
        Ayanamsas.LahiriVP285 => SwissEphAyanamsas.LahiriVP285,
        Ayanamsas.KrishnamurtiSenthilathiban => SwissEphAyanamsas.KrishnamurtiSenthilathiban,
        Ayanamsas.LahiriICRC => SwissEphAyanamsas.LahiriICRC,
        Ayanamsas.UserDefined => SwissEphAyanamsas.UserDefined,
        _ => throw new ArgumentException($"Ayanamsa {ayanamasa} is not defined in SwissEphemerides.")
    };
}