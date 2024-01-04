using SharpAstrology.DataModels;
using SharpAstrology.Enums;
using SharpAstrology.ExtensionMethods;
using SharpAstrology.Interfaces;
using SwissEphNet;


namespace SharpAstrology.Ephemerides;

public sealed class SwissEphemerides : IEphemerides
{
    private SwissEph _eph;
    private int _calculationFlag;

    internal SwissEphemerides(SwissEph eph, EphType ephType)
    {
        _eph = eph;
        _calculationFlag = ephType switch
        {
            EphType.Moshier => SwissEph.SEFLG_MOSEPH,
            EphType.Swiss => SwissEph.SEFLG_SWIEPH,
            EphType.Jpl => SwissEph.SEFLG_JPLEPH,
            _ => throw new ArgumentException($"EphType unknown: {ephType}.")
        };
        _calculationFlag |= SwissEph.SEFLG_SPEED;
    }
    
    public double GetAyanamsa(DateTime pointInTime)
    {
        var error = string.Empty;
        var calcFlag = _calculationFlag | SwissEph.SEFLG_SIDEREAL;
        if (_eph.swe_get_ayanamsa_ex_ut(pointInTime.ToJulianDate(), calcFlag, out var ayanamsa, ref error) < 0)
        {
            throw new Exception($"Error occured while calculation ayanamsa: {error}");
        }

        return ayanamsa;
    }
    
    public PlanetPosition PlanetsPosition(Planets planet, DateTime pointInTime, EphCalculationMode mode = EphCalculationMode.Tropic)
    {
        if (pointInTime.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Provided DateTime was not of kind UTC.");
        }
        string error = string.Empty;
        var result = new double[6];
        var julianDay = pointInTime.ToJulianDate();
        var planetFlag = MapPlanet(planet);
        var calcFlag = mode == EphCalculationMode.Sidereal
            ? _calculationFlag | SwissEph.SEFLG_SIDEREAL
            : _calculationFlag;
        if (planet != Planets.Earth && planet != Planets.SouthNode) 
        { 
            if (_eph.swe_calc_ut(julianDay, planetFlag, calcFlag, result, ref error) < 0) 
            { 
                throw new Exception($"Error occured while calculation object: {error}");
            }
        }
        else 
        { 
            planetFlag = planetFlag switch 
            {
                14 => 0, //Earth to Sun
                -2 => 11 //South node to north node
            };
            if (_eph.swe_calc_ut(julianDay, planetFlag, calcFlag, result, ref error) < 0) {
                throw new Exception($"Error occured while calculation object: {error}");
            }
                
            result[0] = (result[0] + 180) % 360;
            result[1] = -result[1];
        }

        return new PlanetPosition()
        {
            Longitude = result[0],
            Latitude = result[1],
            Distance = result[2],
            SpeedLongitude = result[3],
            SpeedLatitude = result[4],
            SpeedDistance = result[5]
        };
    }
    
    public HousePosition HouseCuspPositions(DateTime pointInTime, double latitude, double longitude, 
        HouseSystems houseSystem = HouseSystems.Placidus, EphCalculationMode mode = EphCalculationMode.Tropic)
    {
        if (pointInTime.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Provided DateTime was not of kind UTC.");
        }
        var hcusps = new CPointer<double>(new double[13]);
        var ascmc = new CPointer<double>(new double[10]);
        var flag = mode == EphCalculationMode.Sidereal ? SwissEph.SEFLG_SIDEREAL : 0;
        if (_eph.swe_houses_ex(pointInTime.ToJulianDate(), (int)flag, latitude, longitude, 
                MapHouseSystems(houseSystem), hcusps, ascmc) < 0)
        {
            throw new Exception(
                $"Error occured while calculating houses: {pointInTime.ToJulianDate()} {latitude} {longitude} {houseSystem}");
        }

        return new HousePosition()
        {
            HouseCusps = hcusps
                .ToArray()
                .Skip(1)
                .Select((x,i)=>(x,i))
                .ToDictionary(val=>(Houses)(val.i+1), val=>val.x),
            Cross = new()
            {
                [Cross.Asc] = ascmc[0],
                [Cross.Mc] = ascmc[1],
                [Cross.Vertex] = ascmc[3],
                [Cross.Ic] = (ascmc[1] + 180) % 360,
                [Cross.Dc] = (ascmc[0] + 180) % 360
            }
        };
    }

    public void Dispose() => _eph.Dispose();

    private char MapHouseSystems(HouseSystems houseSystem) => houseSystem switch
    {
        HouseSystems.Alcabitus => 'B',
        HouseSystems.ApcHouses => 'Y',
        HouseSystems.AxialRotationSystem => 'X',
        HouseSystems.AzimutalSystem => 'H',       
        HouseSystems.Campanus => 'C',
        HouseSystems.Carter => 'F',               
        HouseSystems.Equal => 'E',
        HouseSystems.EqualMc => 'D',
        HouseSystems.Equal1Aries => 'N',
        HouseSystems.SunshineTreindl => 'I',
        HouseSystems.SunshineMakransky => 'i',
        HouseSystems.Koch => 'K',
        HouseSystems.KrusinskiPisaGoelzer => 'U',
        HouseSystems.Morinus => 'M',
        HouseSystems.Placidus => 'P',
        HouseSystems.PolichPage => 'T',           
        HouseSystems.Porphyrius => 'O',
        HouseSystems.PullenSd => 'L',
        HouseSystems.PullenSr => 'Q', 
        HouseSystems.Regiomontanus => 'R',
        HouseSystems.Sripati => 'S',
        HouseSystems.VehlowEqual => 'V',
        HouseSystems.WholeSign => 'W'
    };

    private int MapPlanet(Planets planet) => planet switch
    {
        Planets.Sun => 0,
        Planets.Earth => 14,
        Planets.NorthNode => 11,
        Planets.SouthNode => -2,
        Planets.Moon => 1,
        Planets.Mercury => 2,
        Planets.Venus => 3,
        Planets.Mars => 4,
        Planets.Jupiter => 5,
        Planets.Saturn => 6,
        Planets.Chiron => 15,
        Planets.Uranus => 7,
        Planets.Neptune => 8,
        Planets.Pluto => 9,
        _ => throw new ArgumentException($"Mapping not provided for {planet} in SwissEphemerides.")
    };

    
}