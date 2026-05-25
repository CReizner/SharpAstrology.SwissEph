// Ported from swisseph-master/swephlib.c:3748-3774 (init_crc32 + swi_crc32).
// Original license: see LICENSE.SwissEph.txt at the repo root.

using System;

namespace SharpAstrology.SwissEphemerides.Infrastructure.SwissEph;

/// <summary>
/// Non-reflected CRC-32 (AUTODIN II / Ethernet polynomial, MSB-first byte
/// processing) as used to validate <c>.se1</c> headers. Mirrors the C-side
/// <c>swi_crc32</c> in <c>swephlib.c</c>:
/// <code>
/// crc = 0xffffffff;
/// for each byte p:
///     crc = (crc &lt;&lt; 8) ^ table[(crc &gt;&gt; 24) ^ p];
/// return ~crc;
/// </code>
/// </summary>
internal static class Se1Crc32
{
    private const uint Polynomial = 0x04C11DB7u;
    private static readonly uint[] _table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var c = i << 24;
            for (var j = 0; j < 8; j++)
                c = (c & 0x80000000u) != 0 ? (c << 1) ^ Polynomial : (c << 1);
            t[i] = c;
        }
        return t;
    }

    /// <summary>Computes the CRC-32 (MSB-first, init 0xFFFFFFFF, final XOR all-ones).</summary>
    public static uint Compute(ReadOnlySpan<byte> buf)
    {
        uint crc = 0xffffffffu;
        var t = _table;
        for (var i = 0; i < buf.Length; i++)
        {
            crc = (crc << 8) ^ t[(crc >> 24) ^ buf[i]];
        }
        return ~crc;
    }
}
