using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Reforged.Install;

/// <summary>Build-211: Python's random.Random as the Sandbox's tile scattering uses it: seeded with a string (version
/// 2: the text's bytes and their SHA-512 as one big-endian integer, its 32-bit words little end first, through
/// init_by_array), the Mersenne Twister's 32-bit outputs, getrandbits(k) for k up to 32 and choice() by _randbelow's
/// rejection of draws past the length.</summary>
public sealed class PyRandom
{
    private const int N = 624, M = 397;
    private readonly uint[] _mt = new uint[N];
    private int _index = N+1;

    public PyRandom(string seed)
    {
        byte[] text = Encoding.UTF8.GetBytes(seed);
        var n = new BigInteger([.. text,.. SHA512.HashData(text)],isUnsigned: true,isBigEndian: true);
        long bits = (long)n.GetBitLength();
        int used = bits == 0 ? 1 : (int)((bits-1)/32+1);
        byte[] little = n.ToByteArray(isUnsigned: true,isBigEndian: false);
        var key = new uint[used];
        for (int i = 0; i < used; i++)
            for (int b = 0; b < 4 && 4*i+b < little.Length; b++) key[i] |= (uint)little[4*i+b] << (8*b);
        InitByArray(key);
    }

    private void InitGenrand(uint s)
    {
        _mt[0] = s;
        for (int i = 1; i < N; i++) _mt[i] = 1812433253U*(_mt[i-1] ^ (_mt[i-1] >> 30))+(uint)i;
        _index = N;
    }

    private void InitByArray(uint[] key)
    {
        InitGenrand(19650218U);
        int i = 1, j = 0;
        for (int k = Math.Max(N,key.Length); k > 0; k--)
        {
            _mt[i] = (_mt[i] ^ ((_mt[i-1] ^ (_mt[i-1] >> 30))*1664525U))+key[j]+(uint)j;
            i++; j++;
            if (i >= N) { _mt[0] = _mt[N-1]; i = 1; }
            if (j >= key.Length) j = 0;
        }
        for (int k = N-1; k > 0; k--)
        {
            _mt[i] = (_mt[i] ^ ((_mt[i-1] ^ (_mt[i-1] >> 30))*1566083941U))-(uint)i;
            i++;
            if (i >= N) { _mt[0] = _mt[N-1]; i = 1; }
        }
        _mt[0] = 0x80000000U;
    }

    public uint NextUInt32()
    {
        const uint Upper = 0x80000000U, Lower = 0x7fffffffU, Matrix = 0x9908b0dfU;
        if (_index >= N)
        {
            int kk;
            for (kk = 0; kk < N-M; kk++)
            {
                uint y = (_mt[kk] & Upper) | (_mt[kk+1] & Lower);
                _mt[kk] = _mt[kk+M] ^ (y >> 1) ^ ((y & 1) != 0 ? Matrix : 0);
            }
            for (; kk < N-1; kk++)
            {
                uint y = (_mt[kk] & Upper) | (_mt[kk+1] & Lower);
                _mt[kk] = _mt[kk+(M-N)] ^ (y >> 1) ^ ((y & 1) != 0 ? Matrix : 0);
            }
            uint last = (_mt[N-1] & Upper) | (_mt[0] & Lower);
            _mt[N-1] = _mt[M-1] ^ (last >> 1) ^ ((last & 1) != 0 ? Matrix : 0);
            _index = 0;
        }
        uint value = _mt[_index++];
        value ^= value >> 11;
        value ^= (value << 7) & 0x9d2c5680U;
        value ^= (value << 15) & 0xefc60000U;
        value ^= value >> 18;
        return value;
    }

    /// <summary>getrandbits(k), 0 &lt; k &lt;= 32.</summary>
    public uint GetRandBits(int k) => NextUInt32() >> (32-k);

    /// <summary>_randbelow(n): k = n.bit_length() bits drawn until one is below n.</summary>
    public int RandBelow(int n)
    {
        int k = 32-BitOperations.LeadingZeroCount((uint)n);
        uint r = GetRandBits(k);
        while (r >= n) r = GetRandBits(k);
        return (int)r;
    }

    public T Choice<T>(IReadOnlyList<T> sequence) => sequence.Count == 0 ? throw new IndexOutOfRangeException("Cannot choose from an empty sequence") : sequence[RandBelow(sequence.Count)];
}
