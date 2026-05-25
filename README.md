# SharpAstrology.SwissEph - Ephemerides for SharpAstrology

## About
This package provides an implementation of the IEphemerides interface from [SharpAstrology.Base](https://github.com/CReizner/SharpAstrology.Base). It uses the [SwissEphNet](https://github.com/ygrenier/SwissEphNet) project, which provides bindings for the C-library [swisseph](https://github.com/aloistr/swisseph).

## SharpAstrology Packages
| Package                                                                                                                | Description                                  | Licence  |
|:-----------------------------------------------------------------------------------------------------------------------|:---------------------------------------------|:--------:|
| [SharpAstrology.Base](https://github.com/CReizner/SharpAstrology.Base)                                                 | Base library                                 |   MIT    |
| [SharpAstrology.SwissEph](https://github.com/CReizner/SharpAstrology.SwissEph)                                         | Ephemerides package based on SwissEphNet     | AGPL-3.0 |
| [SharpAstrology.HumanDesign](https://github.com/CReizner/SharpAstrology.HumanDesign)                                   |  Extensions for the Human Design system      |   MIT    |
| [SharpAstrology.HumanDesign.BlazorComponents](https://github.com/CReizner/SharpAstrology.HumanDesign.BlazorComponents) | Human Design charts as Blazor components      |   MIT    |
| [SharpAstrology.Vedic](https://github.com/CReizner/SharpAstrology.Vedic)                                               | Extensions for Vedic astrology systems        |   MIT    |
| [SharpAstrology.West](https://github.com/CReizner/SharpAstrology.West)                                                 | Extensions for western astrology systems      |   MIT    |
| [SharpAstrology.West.BlazorComponents](https://github.com/CReizner/SharpAstrology.West.BlazorComponents)               | Western astrology charts as Blazor components |   MIT    |

## Install
```shell
dotnet add package SharpAstrology.SwissEph
```

## Simple usage
```C#
using System.Text.Json;
using SharpAstrology.Enums;
using SharpAstrology.Ephemerides;
using SharpAstrology.Interfaces;

// Use Moshier, so you don't need the swiss eph files or JPL files
var ephemeridesService = new SwissEphemeridesService(ephType: EphType.Moshier);

// Calculate positional information for Sun, Moon and Venus on January 1st 2024
//
// Use UTC time or it will throw
var pointInTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

using (IEphemerides eph = ephemeridesService.CreateContext())
{
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
    // 187,07504557640635

    Console.WriteLine(houseCusps.Cross[Cross.Mc]);
    // 99,22014310543506

    Console.WriteLine(houseCusps.HouseCusps[Houses.House1]);
    // 187,07504557640635
}

// Calculate sidereal position for Mars with Lahiri ayanamsa
using (IEphemerides eph = ephemeridesService.CreateContext(Ayanamsas.Lahiri))
{
    var marsPosition = eph.PlanetsPosition(Planets.Mars, pointInTime, EphCalculationMode.Sidereal);
    Console.WriteLine(JsonSerializer.Serialize(marsPosition));
    // {
    // "Longitude":243.11750614191982,
    // "Latitude":-0.5505163268692346,
    // "Distance":2.423806754147523,
    // "SpeedLongitude"::0.7413840351773113,
    // "SpeedLatitude":-0.009739411236392903,
    // "SpeedDistance":-0.00303531177482181
    // }
}
```

## Use swiss eph files or JPL files for more precision
Follow the instruction from the original [swisseph project](https://github.com/aloistr/swisseph) and download the files you want. Than save to files into a folder and use the path to that folder in the SwissEphemeridesService constructor.

```C#
using SharpAstrology.Ephemerides;
using SharpAstrology.Interfaces;

SwissEphemeridesService ephemeridesService;

// for swiss eph files
ephemeridesService = new SwissEphemeridesService(rootPathToEph: "[PATH_TO_SWISSEPH_ROOT_FOLDER]", EphType.Swiss);
using (IEphemerides eph = ephemeridesService.CreateContext())
{
    // calculate positions here
}

// for JPL files
ephemeridesService = new SwissEphemeridesService(rootPathToEph: "[PATH_TO_JPL_ROOT_FOLDER]", EphType.Jpl);
using (IEphemerides eph = ephemeridesService.CreateContext())
{
    // calculate positions here
}
```
