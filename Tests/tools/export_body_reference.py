#!/usr/bin/env python3
"""Generate node/apogee and position-only planet fixtures from local Swiss C.

python3 Tests/tools/export_body_reference.py \
    --source ../Old/Experiments.RewriteSwissEph/swisseph-master --ephe ../ephe
"""
import argparse
import ctypes as C
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile


def cases():
    dates = (2446100.5, 2450460.5, 2451545.0, 2453560.5, 2457057.7896664813, 2460680.5)
    # Original 25 PR node cases, plus ICRS/sidereal and the shared apogee path.
    for source in (2, 4):
        for body in (11, 13):
            for tt in dates:
                for option in (0, 256, 256 | 16, 256 | 64, 256 | 1024, 256 | 131072, 256 | 65536,
                               32, 2048, 32 | 2048, 256 | 32, 256 | 2048, 256 | 32 | 2048,
                               256 | 65536 | 32, 256 | 65536 | 2048, 128):
                    yield "osculating", body, tt, source | 4096 | option
    for source in (2, 4):
        for body in (0, 2, 3, 4, 5, 6, 7, 8, 9):
            for tt in dates:
                # Standard output at all dates; additional modes at the endpoints.
                options = (0, 16, 32, 64, 1024, 2048, 32768, 131072, 65536) if tt in (dates[0], dates[-1]) else (0,)
                for option in options:
                    yield "positionOnly", body, tt, source | 4096 | option
                options = [256, 256 | 32768, 128, 256 | 32768 | 1024, 256 | 1024]
                if tt in (dates[0], 2451545.0, dates[-1]):
                    options += [256 | 16, 256 | 32, 256 | 2048, 256 | 131072,
                                256 | 128, 256 | 32768 | 16, 128 | 32768, 256 | 512]
                for option in options:
                    yield "motion", body, tt, source | 4096 | option
        # Rapidly changing light deflection near the solar limb, explicitly
        # mentioned by swi_deflect_light. The disabled-deflection control
        # separates it from light time and aberration.
        for tt in (2434871.45, 2434871.46):
            for option in (256, 256 | 512):
                yield "motion", 2, tt, source | 4096 | option


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--ephe", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "TestData/BodyReference.json")
    args = parser.parse_args()
    source, ephe = args.source.resolve(), args.ephe.resolve()
    files = ["swedate.c", "swehouse.c", "swejpl.c", "swemmoon.c", "swemplan.c",
             "sweph.c", "swephlib.c", "swecl.c", "swehel.c"]
    with tempfile.TemporaryDirectory(prefix="swiss-body-oracle-") as temp:
        library = Path(temp) / "libswe.so"
        subprocess.run(["cc", "-O2", "-fPIC", "-shared", "-o", str(library), *files, "-lm"], cwd=source, check=True)
        lib = C.CDLL(str(library))
        lib.swe_set_ephe_path.argtypes = [C.c_char_p]
        lib.swe_set_ephe_path(str(ephe).encode())
        lib.swe_set_topo.argtypes = [C.c_double] * 3
        observer = [13.405, 52.52, 34.0]
        lib.swe_set_topo(*observer)
        lib.swe_set_sid_mode.argtypes = [C.c_int, C.c_double, C.c_double]
        lib.swe_set_sid_mode(1, 0, 0)  # Lahiri
        lib.swe_calc.argtypes = [C.c_double, C.c_int, C.c_int, C.POINTER(C.c_double), C.c_char_p]
        version = C.create_string_buffer(256)
        lib.swe_version(version)
        rows = []
        for group, body, tt, flags in cases():
            vector, error = (C.c_double * 6)(), C.create_string_buffer(1024)
            returned = lib.swe_calc(tt, body, flags, vector, error)
            if returned < 0 or returned & 7 != flags & 7 or error.value:
                raise RuntimeError((body, tt, flags, returned, error.value.decode()))
            rows.append(dict(group=group, body=body, tt=tt, flags=flags, vector=list(vector)))
        sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
        result = dict(version=version.value.decode(), observer=observer, siderealMode="Lahiri",
                      sourceSha256={p.name: sha(p) for p in sorted(source.glob("*.h")) + [source / f for f in files]},
                      ephemerisSha256={f: sha(ephe / f) for f in ("sepl_18.se1", "semo_18.se1")}, cases=rows)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + "\n")
        print(f"Wrote {len(rows)} cases to {args.output}")


if __name__ == "__main__":
    main()
