# SharpAstrology.SwissEph - Ephemerides for SharpAstrology

## About
This package provides an implementation of the IEphemerides interface from [SharpAstrology.Base](https://github.com/CReizner/SharpAstrology.Base). It uses the [SwissEphNet](https://github.com/ygrenier/SwissEphNet) project, which provides bindings for the C-library [swisseph](https://github.com/aloistr/swisseph).

## Simple usage
```C#
using System.Text.Json;
using SharpAstrology.Enums;
using SharpAstrology.Ephemerides;
using SharpAstrology.Interfaces;

// Use Moshier, so you don't need the swiss eph files or JPL files
var ephemeridesService = new SwissEphemeridesService(ephType: EphType.Moshier);
IEphemerides eph = ephemeridesService.CreateContext();

// Calculate positional information for Sun, Moon and Venus on January 1st 2024
//
// Use UTC time or it will throw
var pointInTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

var sunPosition = eph.PlanetsPosition(Planets.Sun, pointInTime);
Console.WriteLine(JsonSerializer.Serialize(sunPosition));
// {
// "Longitude":280.0390123591898,
// "Latitude":0.00016036961321834312,
// "Distance":0.9833183562567887,
// "SpeedLongitude":1.0189783966864485,
// "SpeedLatitude":-3.780307931918674E-05,
// "SpeedDistance":-1.1317598853344111E-05
// }

var moonPosition = eph.PlanetsPosition(Planets.Moon, pointInTime);
Console.WriteLine(JsonSerializer.Serialize(moonPosition));
// {
// "Longitude":155.9922487287969,
// "Latitude":3.567636008224304,
// "Distance":0.0027048134096186458,
// "SpeedLongitude":11.848401014998416,
// "SpeedLatitude":-0.7336498562140658,
// "SpeedDistance":4.901106714722023E-06
// }

var venusPosition = eph.PlanetsPosition(Planets.Venus, pointInTime);
Console.WriteLine(JsonSerializer.Serialize(venusPosition));
// {
// "Longitude":242.6123495376748,
// "Latitude":1.9498137075115765,
// "Distance":1.1819079026603012,
// "SpeedLongitude":1.2159947235695916,
// "SpeedLatitude":-0.029584567802327894,
// "SpeedDistance":0.006288212288100938
// }

// Calculate house cusp longitudes in the porphyrius system for location London 
var houseCusps = eph.HouseCuspPositions(pointInTime, 51.509865, -0.118092, HouseSystems.Porphyrius);
Console.WriteLine(houseCusps.Cross[Cross.Asc]);
// 187,0750455769693

Console.WriteLine(houseCusps.Cross[Cross.Mc]);
// 99,22014310617769

Console.WriteLine(houseCusps.HouseCusps[Houses.House1]);
// 187,0750455769693

eph.Dispose();

// Calculate sidereal position for Mars with Lahiri ayanamsa
eph = ephemeridesService.CreateContext(Ayanamsas.Lahiri);
var marsPosition = eph.PlanetsPosition(Planets.Mars, pointInTime, EphCalculationMode.Sidereal);
Console.WriteLine(JsonSerializer.Serialize(marsPosition));
// {
// "Longitude":243.11756696456447,
// "Latitude":-0.5505143969483922,
// "Distance":2.423806669715993,
// "SpeedLongitude":0.7413840544065141,
// "SpeedLatitude":-0.00973948506357604,
// "SpeedDistance":-0.003035312479835423
// }
```

## Use swiss eph files or JPL files for more precision
Follow the instruction from the original [swisseph project](https://github.com/aloistr/swisseph) and download the files you want. Than save to files into a folder and use the path to that folder in the SwissEphemeridesService constructor.

```C#
using SharpAstrology.Ephemerides;
using SharpAstrology.Interfaces;

SwissEphemeridesService ephemeridesService;
IEphemerides eph;

// for swiss eph files
ephemeridesService = new SwissEphemeridesService(rootPathToEph: "[PATH_TO_SWISSEPH_ROOT_FOLDER]", EphType.Swiss);
eph = ephemeridesService.CreateContext();

// for JPL files
ephemeridesService = new SwissEphemeridesService(rootPathToEph: "[PATH_TO_JPL_ROOT_FOLDER]", EphType.Jpl);
eph = ephemeridesService.CreateContext();
```


