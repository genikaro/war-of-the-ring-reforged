using System.Globalization;
using System.Numerics;

namespace Reforged.Install;

/// <summary>Build-196: Python's rounding, for ports that must give the import's numbers exactly.</summary>
public static class PyMath
{
    /// <summary>Python round(x, digits): the exact binary value rounded to <paramref name="digits"/> decimals, halves to
    /// even, then the double nearest that decimal (Math.Round scales first and can land on a false half).</summary>
    public static double Round(double value,int digits)
    {
        if (!double.IsFinite(value) || value == 0) return value;
        long bits = BitConverter.DoubleToInt64Bits(value);
        bool negative = bits < 0;
        int exponent = (int)((bits >> 52) & 0x7FF);
        long fraction = bits & 0xFFFFFFFFFFFFFL;
        BigInteger mantissa = exponent == 0 ? fraction : fraction | (1L << 52);
        int shift = (exponent == 0 ? 1 : exponent)-1075; // value = mantissa * 2^shift
        BigInteger scale = BigInteger.Pow(10,Math.Max(digits,0));
        BigInteger numerator = mantissa*scale, denominator = BigInteger.One;
        if (shift >= 0) numerator <<= shift; else denominator <<= -shift;
        if (digits < 0) denominator *= BigInteger.Pow(10,-digits);
        BigInteger quotient = BigInteger.DivRem(numerator,denominator,out var remainder);
        int half = (remainder*2).CompareTo(denominator);
        if (half > 0 || half == 0 && !quotient.IsEven) quotient += 1;
        string text = (negative ? "-" : "")+quotient.ToString(CultureInfo.InvariantCulture)+"e"+(-digits).ToString(CultureInfo.InvariantCulture);
        return double.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture);
    }
}
