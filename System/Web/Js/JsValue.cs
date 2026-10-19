using System;
using System.Text;

// ---------------------------------------------------------------------------
// JavaScript value primitives and the abstract operations every engine needs:
// type tests, typeof, ToBoolean / ToNumber / ToString / ToPrimitive, loose and
// strict equality, and JS-exact number <-> string conversions.
//
// A JS value is represented as a plain `object`:
//   undefined -> JsUndefined.Instance
//   null      -> JsNull.Instance
//   boolean   -> System.Boolean
//   number    -> System.Double
//   string    -> System.String
//   symbol    -> JsSymbol
//   object    -> JsObject (or a subclass: JsArray, JsFunction, ...)
// Everything is Cosmos-safe: no reflection, no culture-aware parsing helpers.
// ---------------------------------------------------------------------------

public sealed class JsUndefined
{
    public static readonly JsUndefined Instance = new JsUndefined();
    private JsUndefined() { }
    public override string ToString() { return "undefined"; }
}

public sealed class JsNull
{
    public static readonly JsNull Instance = new JsNull();
    private JsNull() { }
    public override string ToString() { return "null"; }
}

public sealed class JsSymbol
{
    public string Key;      // unique key used for internal property storage
    public string Desc;
    public bool Global;     // created via Symbol.for()

    public JsSymbol(string key, string desc) { Key = key; Desc = desc; }

    public override string ToString() { return "Symbol(" + (Desc == null ? "" : Desc) + ")"; }
}

public static class JsValueOps
{
    /// <summary>Interpreter used when an abstract operation must call user JS (ToPrimitive).</summary>
    public static JsInterpreter Active;

    // ---- type tests -------------------------------------------------------

    public static bool IsUndefined(object v) { return v is JsUndefined; }
    public static bool IsNull(object v) { return v is JsNull; }
    public static bool IsNullish(object v) { return v is JsUndefined || v is JsNull; }
    public static bool IsNumber(object v) { return v is double; }
    public static bool IsString(object v) { return v is string; }
    public static bool IsBoolean(object v) { return v is bool; }
    public static bool IsSymbol(object v) { return v is JsSymbol; }
    public static bool IsObject(object v) { return v is JsObject; }
    public static bool IsCallable(object v) { return v is JsFunction; }
    public static bool IsPrimitive(object v) { return !(v is JsObject); }

    public static string TypeNameOf(object v)
    {
        if (v is JsUndefined) return "undefined";
        if (v is JsNull) return "object";
        if (v is bool) return "boolean";
        if (v is double) return "number";
        if (v is string) return "string";
        if (v is JsSymbol) return "symbol";
        if (v is JsFunction) return "function";
        if (v is JsObject) return "object";
        return "object";
    }

    public static string JsTypeof(object v) { return TypeNameOf(v); }

    // ---- ToBoolean --------------------------------------------------------

    public static bool ToBoolean(object v)
    {
        if (v is JsUndefined || v is JsNull) return false;
        if (v is bool) return (bool)v;
        if (v is double)
        {
            double d = (double)v;
            return d != 0 && !double.IsNaN(d);
        }
        if (v is string) return ((string)v).Length > 0;
        return true; // symbol, object, function
    }

    // ---- ToNumber ---------------------------------------------------------

    public static double ToNumber(object v)
    {
        if (v is JsUndefined) return double.NaN;
        if (v is JsNull) return 0;
        if (v is bool) return (bool)v ? 1 : 0;
        if (v is double) return (double)v;
        if (v is string) return ParseNumberString((string)v);
        if (v is JsSymbol) throw JsErr.Type("Cannot convert a Symbol value to a number");
        if (v is JsObject)
        {
            object p = ToPrimitive(v, JsHint.Number);
            return ToNumber(p);
        }
        return double.NaN;
    }

    // ---- ToString ---------------------------------------------------------

    public static string ToStringValue(object v)
    {
        if (v is JsUndefined) return "undefined";
        if (v is JsNull) return "null";
        if (v is bool) return (bool)v ? "true" : "false";
        if (v is double) return NumberToString((double)v);
        if (v is string) return (string)v;
        if (v is JsSymbol) throw JsErr.Type("Cannot convert a Symbol value to a string");
        if (v is JsObject)
        {
            object p = ToPrimitive(v, JsHint.String);
            return ToStringValue(p);
        }
        return "";
    }

    // ---- ToPrimitive ------------------------------------------------------

    public static object ToPrimitive(object v, JsHint hint)
    {
        if (!(v is JsObject)) return v;
        JsObject o = (JsObject)v;

        // Symbol.toPrimitive (exotic)
        object exotic;
        if (o.TryGetWellKnown(JsWellKnown.ToPrimitive, out exotic) && exotic is JsFunction)
        {
            object r = CallActive((JsFunction)exotic, v, new object[] { hint == JsHint.String ? "string" : "number" });
            if (!(r is JsObject)) return r;
            throw JsErr.Type("Cannot convert object to primitive value");
        }

        if (o is JsDate)
        {
            // Date: default hint behaves like string.
            if (hint == JsHint.Default) hint = JsHint.String;
        }

        return OrdinaryToPrimitive(o, hint);
    }

    private static object OrdinaryToPrimitive(JsObject o, JsHint hint)
    {
        string[] order;
        if (hint == JsHint.String) order = new string[] { "toString", "valueOf" };
        else order = new string[] { "valueOf", "toString" };

        for (int i = 0; i < 2; i++)
        {
            object m;
            if (!o.GetProperty(order[i], out m)) continue;
            if (m is JsFunction)
            {
                object r = CallActive((JsFunction)m, o, new object[0]);
                if (!(r is JsObject)) return r;
            }
            else
            {
                // non-callable: skip per spec (OrdinaryToPrimitive only calls callables)
            }
        }
        throw JsErr.Type("Cannot convert object to primitive value");
    }

    private static object CallActive(JsFunction fn, object thisVal, object[] args)
    {
        if (Active == null) return JsUndefined.Instance;
        return Active.Call(fn, thisVal, args, false);
    }

    // ---- equality ---------------------------------------------------------

    public static bool StrictEquals(object a, object b)
    {
        if (a is JsUndefined && b is JsUndefined) return true;
        if (a is JsNull && b is JsNull) return true;
        if (a is double && b is double)
        {
            double x = (double)a, y = (double)b;
            if (double.IsNaN(x) || double.IsNaN(y)) return false;
            return x == y;
        }
        if (a is string && b is string) return (string)a == (string)b;
        if (a is bool && b is bool) return (bool)a == (bool)b;
        if (a is JsSymbol && b is JsSymbol) return ReferenceEquals(a, b);
        if (a is JsObject && b is JsObject) return ReferenceEquals(a, b);
        // undefined vs null, and every cross-type pair
        if ((a is JsUndefined && b is JsNull) || (a is JsNull && b is JsUndefined)) return false;
        if (a is JsObject || b is JsObject) return false;
        if (a == null || b == null) return false;
        // different primitive types
        if (a is double == false && b is double == false &&
            a is string == false && b is string == false &&
            a is bool == false && b is bool == false) return false;
        return false;
    }

    public static bool LooseEquals(object a, object b)
    {
        if (StrictEquals(a, b)) return true;

        bool aNullish = IsNullish(a), bNullish = IsNullish(b);
        if (aNullish || bNullish) return aNullish && bNullish;

        if (a is double && b is string) return LooseEquals(a, ToNumber(b));
        if (a is string && b is double) return LooseEquals(ToNumber(a), b);
        if (a is bool) return LooseEquals(ToNumber(a), b);
        if (b is bool) return LooseEquals(a, ToNumber(b));

        if ((a is string || a is double || a is JsSymbol) && b is JsObject)
            return LooseEquals(a, ToPrimitive(b, JsHint.Default));
        if (a is JsObject && (b is string || b is double || b is JsSymbol))
            return LooseEquals(ToPrimitive(a, JsHint.Default), b);

        return false;
    }

    /// <summary>SameValueZero: like strict equality but NaN equals NaN and +0 equals -0.</summary>
    public static bool SameValueZero(object a, object b)
    {
        bool aNum = a is double, bNum = b is double;
        if (aNum && bNum)
        {
            double x = (double)a, y = (double)b;
            if (double.IsNaN(x) && double.IsNaN(y)) return true;
            return x == y;
        }
        return StrictEquals(a, b);
    }

    // ---- numeric helpers --------------------------------------------------

    public static double ToInt32(object v)
    {
        double d = ToNumber(v);
        if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
        long i = (long)Math.Truncate(d);
        long x = i % 4294967296L;
        if (x < 0) x += 4294967296L;
        if (x >= 2147483648L) x -= 4294967296L;
        return x;
    }

    public static double ToUint32(object v)
    {
        double d = ToNumber(v);
        if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
        long i = (long)Math.Truncate(d);
        long x = i % 4294967296L;
        if (x < 0) x += 4294967296L;
        return x;
    }

    // ---- number -> string (ECMAScript Number::toString) -------------------

    public static string NumberToString(double value)
    {
        if (double.IsNaN(value)) return "NaN";
        if (double.IsPositiveInfinity(value)) return "Infinity";
        if (double.IsNegativeInfinity(value)) return "-Infinity";
        if (value == 0) return "0"; // also normalises -0 to "0"

        bool neg = value < 0;
        double abs = neg ? -value : value;

        int digitsLen;
        int n;
        string digits = ExtractDigits(abs, out digitsLen, out n);
        string body = FormatJs(digits, n);
        return neg ? "-" + body : body;
    }

    // Turn a double into (minimal significant digits, n) where value == 0.<digits> * 10^n.
    private static string ExtractDigits(double abs, out int len, out int n)
    {
        string s = abs.ToString();

        int eIdx = s.IndexOf('E');
        if (eIdx < 0) eIdx = s.IndexOf('e');

        if (eIdx >= 0)
        {
            string mantPart = s.Substring(0, eIdx);
            string expPart = s.Substring(eIdx + 1);
            int E = ParseIntStrict(expPart);

            string all = mantPart.Replace(".", "");
            n = E + 1;
            string trimmed = TrimTrailingZeros(all);
            len = trimmed.Length;
            return trimmed;
        }

        int dot = s.IndexOf('.');
        if (dot < 0)
        {
            string all = s;
            n = all.Length;
            string trimmed = TrimTrailingZeros(all);
            len = trimmed.Length;
            return trimmed;
        }

        string intPart = s.Substring(0, dot);
        string fracPart = s.Substring(dot + 1);

        if (intPart == "0" || intPart.Length == 0)
        {
            int lead = 0;
            while (lead < fracPart.Length && fracPart[lead] == '0') lead++;
            n = -lead;
            string trimmed = TrimTrailingZeros(fracPart);
            len = trimmed.Length;
            return trimmed;
        }

        n = intPart.Length;
        string allDigits = intPart + fracPart;
        string t = TrimTrailingZeros(allDigits);
        len = t.Length;
        return t;
    }

    private static string FormatJs(string digits, int n)
    {
        int k = digits.Length;
        if (k == 0) return "0";

        if (k <= n && n <= 21)
        {
            StringBuilder sb = new StringBuilder(k + (n - k));
            sb.Append(digits);
            for (int i = 0; i < n - k; i++) sb.Append('0');
            return sb.ToString();
        }
        if (0 < n && n <= 21)
        {
            return digits.Substring(0, n) + "." + digits.Substring(n);
        }
        if (-6 < n && n <= 0)
        {
            StringBuilder sb = new StringBuilder(k + 2 - n);
            sb.Append("0.");
            for (int i = 0; i < -n; i++) sb.Append('0');
            sb.Append(digits);
            return sb.ToString();
        }

        // exponential
        string mant;
        if (k == 1) mant = digits;
        else mant = digits.Substring(0, 1) + "." + digits.Substring(1);
        int e = n - 1;
        return mant + "e" + (e < 0 ? "-" : "+") + Math.Abs(e).ToString();
    }

    // ---- string -> number -------------------------------------------------

    public static double ParseNumberString(string raw)
    {
        if (raw == null) return double.NaN;
        string s = TrimJsWhitespace(raw);
        if (s.Length == 0) return 0;

        if (s == "Infinity") return double.PositiveInfinity;
        if (s == "+Infinity") return double.PositiveInfinity;
        if (s == "-Infinity") return double.NegativeInfinity;
        if (s == "NaN") return double.NaN;

        // hexadecimal (no sign allowed)
        if (s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
            return ParseHex(s, 2);

        double d;
        if (TryParseDecimal(s, 0, s.Length, out d)) return d;
        return double.NaN;
    }

    // Number literal parser used by the lexer (returns false if malformed).
    public static bool TryParseDecimal(string s, int start, int end, out double value)
    {
        value = double.NaN;
        int i = start;
        if (i >= end) return false;

        bool neg = false;
        if (s[i] == '+' || s[i] == '-')
        {
            neg = s[i] == '-';
            i++;
        }

        long mant = 0;
        int sig = 0;        // digits accumulated into mant
        int fracDigits = 0; // digits after the decimal point
        bool sawDigit = false;
        bool inFrac = false;

        while (i < end)
        {
            char c = s[i];
            if (!inFrac && c >= '0' && c <= '9')
            {
                sawDigit = true;
                if (sig < 18) { mant = mant * 10 + (c - '0'); sig++; }
                i++;
            }
            else if (!inFrac && c == '.')
            {
                inFrac = true;
                i++;
            }
            else if (inFrac && c >= '0' && c <= '9')
            {
                sawDigit = true;
                if (sig < 18) { mant = mant * 10 + (c - '0'); sig++; }
                fracDigits++;
                i++;
            }
            else break;
        }

        if (!sawDigit) return false;

        int exp10 = 0;
        if (i < end && (s[i] == 'e' || s[i] == 'E'))
        {
            i++;
            bool expNeg = false;
            if (i < end && (s[i] == '+' || s[i] == '-'))
            {
                expNeg = s[i] == '-';
                i++;
            }
            if (i >= end || s[i] < '0' || s[i] > '9') return false;
            int ev = 0;
            while (i < end && s[i] >= '0' && s[i] <= '9')
            {
                ev = ev * 10 + (s[i] - '0');
                if (ev > 409) ev = 409;
                i++;
            }
            exp10 = expNeg ? -ev : ev;
        }

        if (i != end) return false;

        int scale = exp10 - fracDigits;
        value = ScaleDouble(mant, scale);
        if (neg) value = -value;
        return true;
    }

    private static double ScaleDouble(long mant, int exp)
    {
        if (mant == 0) return 0;
        if (exp > 409) return double.PositiveInfinity;
        if (exp < -409) return 0;
        double v = (double)mant;
        if (exp >= 0)
        {
            for (int i = 0; i < exp; i++) v *= 10;
        }
        else
        {
            for (int i = 0; i < -exp; i++) v /= 10;
        }
        return v;
    }

    public static double ParseHex(string s, int start)
    {
        double v = 0;
        bool any = false;
        for (int i = start; i < s.Length; i++)
        {
            char c = s[i];
            int d;
            if (c >= '0' && c <= '9') d = c - '0';
            else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
            else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
            else break;
            v = v * 16 + d;
            any = true;
        }
        if (!any) return double.NaN;
        // ignore trailing junk only if all of it is whitespace (Number() is strict, lexer is not)
        return v;
    }

    // ---- small utilities --------------------------------------------------

    public static string TrimJsWhitespace(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && IsJsSpace(s[a])) a++;
        while (b > a && IsJsSpace(s[b - 1])) b--;
        if (a == 0 && b == s.Length) return s;
        return s.Substring(a, b - a);
    }

    private static bool IsJsSpace(char c)
    {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
    }

    private static string TrimTrailingZeros(string s)
    {
        int end = s.Length;
        while (end > 1 && s[end - 1] == '0') end--;
        if (end == s.Length) return s;
        if (end == 0) return "0";
        return s.Substring(0, end);
    }

    private static int ParseIntStrict(string s)
    {
        int i = 0, sign = 1;
        if (s.Length > 0 && (s[0] == '+' || s[0] == '-'))
        {
            if (s[0] == '-') sign = -1;
            i = 1;
        }
        int v = 0;
        bool any = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c < '0' || c > '9') break;
            v = v * 10 + (c - '0');
            any = true;
            i++;
        }
        if (!any) return 0;
        return v * sign;
    }
}

public enum JsHint
{
    Default,
    Number,
    String,
}

/// <summary>Well-known symbol keys (stored under prefixed internal property keys).</summary>
public static class JsWellKnown
{
    public const string ToPrimitive = "@@toPrimitive";
    public const string Iterator = "@@iterator";
    public const string AsyncIterator = "@@asyncIterator";
    public const string ToStringTag = "@@toStringTag";
    public const string HasInstance = "@@hasInstance";
    public const string Species = "@@species";
    public const string IsConcatSpreadable = "@@isConcatSpreadable";
    public const string Unscopables = "@@unscopables";
}

// ---- JS error types surfaced as C# exceptions when a runtime fault occurs --

public sealed class JsErrorValue
{
    public string Name;
    public string Message;

    public JsErrorValue() { }

    public JsErrorValue(string name, string message)
    {
        Name = name;
        Message = message;
    }
}

public class JsThrown : Exception
{
    public object Value;
    public JsThrown(object value, string message) : base(message) { Value = value; }
}

public static class JsErr
{
    public static JsThrown Type(string m) { return new JsThrown(new JsErrorValue("TypeError", m), m); }
    public static JsThrown Range(string m) { return new JsThrown(new JsErrorValue("RangeError", m), m); }
    public static JsThrown Ref(string m) { return new JsThrown(new JsErrorValue("ReferenceError", m), m); }
    public static JsThrown Syntax(string m) { return new JsThrown(new JsErrorValue("SyntaxError", m), m); }
}

/// <summary>Thrown at parse time by the parser/lexer (a JS SyntaxError).</summary>
public class JsSyntaxError : Exception
{
    public JsSyntaxError(string message) : base(message) { }
}
