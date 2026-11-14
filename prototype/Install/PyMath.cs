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

    /// <summary>Python's str() of a float: the shortest text that reads back as the same double.</summary>
    public static string Repr(double value) => value.ToString("R",CultureInfo.InvariantCulture);
}

/// <summary>Build-199: Python's decimal.Decimal as the import uses it: Decimal(str(x)) of a float, products rounded to
/// the default context's 28 significant digits (halves to even), and to_integral_value with a rounding mode. The value
/// is Coefficient x 10^Exponent.</summary>
public readonly record struct PyDecimal(BigInteger Coefficient,int Exponent)
{
    private const int Precision = 28;

    /// <summary>Decimal(str(value)): exact for a double through its shortest repr.</summary>
    public static PyDecimal Of(double value) => Parse(PyMath.Repr(value));
    public static PyDecimal Of(long value) => new(value,0);

    public static PyDecimal Parse(string text)
    {
        int exponent = 0;
        int e = text.IndexOfAny(['e','E']);
        if (e >= 0) { exponent = int.Parse(text[(e+1)..],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture); text = text[..e]; }
        int dot = text.IndexOf('.');
        if (dot >= 0) { exponent -= text.Length-dot-1; text = text.Remove(dot,1); }
        return new(BigInteger.Parse(text,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture),exponent);
    }

    public static PyDecimal operator *(PyDecimal a,PyDecimal b) => Rounded(a.Coefficient*b.Coefficient,a.Exponent+b.Exponent);

    /// <summary>The context's rounding of an exact result: at most 28 significant digits, halves to even.</summary>
    private static PyDecimal Rounded(BigInteger coefficient,int exponent)
    {
        int digits = BigInteger.Abs(coefficient).ToString(CultureInfo.InvariantCulture).Length;
        if (digits <= Precision) return new(coefficient,exponent);
        int drop = digits-Precision;
        BigInteger quotient = BigInteger.DivRem(coefficient,BigInteger.Pow(10,drop),out var remainder);
        int half = (BigInteger.Abs(remainder)*2).CompareTo(BigInteger.Pow(10,drop));
        if (half > 0 || half == 0 && !quotient.IsEven) quotient += coefficient.Sign;
        return Rounded(quotient,exponent+drop);
    }

    public enum Rounding { Ceiling, HalfUp, HalfEven, Floor }

    /// <summary>to_integral_value(rounding): the integer the value rounds to.</summary>
    public BigInteger ToIntegral(Rounding rounding)
    {
        if (Exponent >= 0) return Coefficient*BigInteger.Pow(10,Exponent);
        var unit = BigInteger.Pow(10,-Exponent);
        BigInteger quotient = BigInteger.DivRem(Coefficient,unit,out var remainder); // Truncated toward zero.
        if (remainder.IsZero) return quotient;
        int half = (BigInteger.Abs(remainder)*2).CompareTo(unit);
        return rounding switch
        {
            Rounding.Ceiling => remainder.Sign > 0 ? quotient+1 : quotient,
            Rounding.Floor => remainder.Sign < 0 ? quotient-1 : quotient,
            Rounding.HalfUp => half >= 0 ? quotient+remainder.Sign : quotient,
            _ => half > 0 || half == 0 && !quotient.IsEven ? quotient+remainder.Sign : quotient,
        };
    }
}
