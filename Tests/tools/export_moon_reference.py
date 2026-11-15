#!/usr/bin/env python3
"""Regenerate lunar golden vectors from the unmodified local Swiss C sources.

From the repository root:
python3 Tests/tools/export_moon_reference.py \
    --source ../Old/Experiments.RewriteSwissEph/swisseph-master --ephe ../ephe

Normal .NET test runs use the checked-in vectors and need no C compiler/library.
"""

import argparse
import ctypes as C
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--ephe", type=Path, required=True)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "TestData/MoonReference.json")
    args = parser.parse_args()
    source = args.source.resolve()
    ephe = args.ephe.resolve()
    files = ["swedate.c", "swehouse.c", "swejpl.c", "swemmoon.c", "swemplan.c",
             "sweph.c", "swephlib.c", "swecl.c", "swehel.c"]
    with tempfile.TemporaryDirectory(prefix="swiss-moon-oracle-") as temp:
        library = Path(temp) / "libswe.so"
        subprocess.run(["cc", "-O2", "-fPIC", "-shared", "-o", str(library),
                        *files, "-lm"], cwd=source, check=True)
        lib = C.CDLL(str(library))
        lib.swe_version.argtypes = [C.c_char_p]
        version = C.create_string_buffer(256)
        lib.swe_version(version)
        lib.swe_set_ephe_path.argtypes = [C.c_char_p]
        lib.swe_set_ephe_path(str(ephe).encode())
        lib.swe_set_topo.argtypes = [C.c_double] * 3
        lib.swe_calc.argtypes = [C.c_double, C.c_int, C.c_int, C.POINTER(C.c_double), C.c_char_p]
        observer = [13.405, 52.52, 34.0]  # longitude, latitude, height in metres
        lib.swe_set_topo(*observer)
        cases = []
        # Exercise both the default frame bias and the explicit ICRS bypass.
        options = [0, 256, 256 | 16, 256 | 32, 256 | 64, 256 | 1024,
                   256 | 2048, 256 | 32768, 32768, 256 | 32768 | 16,
                   256 | 32768 | 1024, 256 | 8, 128, 128 | 256, 128 | 32768]
        for backend in (2, 4):
            for tt in (2440000.5, 2451545.0, 2457057.7896664813, 2460680.5):
                for option in [option | frame for frame in (0, 131072) for option in options]:
                    flags = backend | 4096 | option
                    vector = (C.c_double * 6)()
                    error = C.create_string_buffer(1024)
                    returned = lib.swe_calc(tt, 1, flags, vector, error)
                    if returned < 0 or returned & 7 != backend or error.value:
                        raise RuntimeError((tt, flags, returned, error.value.decode()))
                    cases.append({"tt": tt, "flags": flags, "vector": list(vector)})
        result = {
            "version": version.value.decode(),
            "sourceSha256": {p.name: sha256(p) for p in sorted(source.glob("*.h")) + [source / f for f in files]},
            "ephemerisSha256": {f: sha256(ephe / f) for f in ("sepl_18.se1", "semo_18.se1")},
            "observer": observer,
            "cases": cases,
        }
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + "\n")
        print(f"Wrote {len(cases)} cases from Swiss Ephemeris {result['version']} to {args.output}")


if __name__ == "__main__":
    main()
