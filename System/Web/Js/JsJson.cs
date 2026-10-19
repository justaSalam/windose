using System;
using System.Collections.Generic;
using System.Text;

// ---------------------------------------------------------------------------
// JSON.parse and JSON.stringify. Hand-written, Cosmos-safe (no regex, no LINQ,
// no reflection, no char.IsXxx). Produces plain JS values via a JsRealm.
// ---------------------------------------------------------------------------

public static class JsJson
{
    // ---- parse ------------------------------------------------------------

    public static object Parse(JsRealm realm, string text)
    {
        if (text == null) throw JsErr.Syntax("JSON.parse: input is null");
        int pos = 0;
        object v = ParseValue(realm, text, ref pos);
        SkipWs(text, ref pos);
        if (pos < text.Length) throw JsErr.Syntax("JSON.parse: unexpected trailing content");
        return v;
    }

    private static object ParseValue(JsRealm realm, string s, ref int pos)
    {
        SkipWs(s, ref pos);
        if (pos >= s.Length) throw JsErr.Syntax("JSON.parse: unexpected end of input");
        char c = s[pos];
        if (c == '{') return ParseObject(realm, s, ref pos);
        if (c == '[') return ParseArray(realm, s, ref pos);
        if (c == '"') return ParseString(s, ref pos);
        if (c == 't' && Match(s, ref pos, "true")) return true;
        if (c == 'f' && Match(s, ref pos, "false")) return false;
        if (c == 'n' && Match(s, ref pos, "null")) return JsNull.Instance;
        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(s, ref pos);
        throw JsErr.Syntax("JSON.parse: unexpected character '" + c + "'");
    }

    private static JsObject ParseObject(JsRealm realm, string s, ref int pos)
    {
        JsObject o = realm.NewObject();
        pos++; // consume '{'
        SkipWs(s, ref pos);
        if (pos < s.Length && s[pos] == '}') { pos++; return o; }
        while (true)
        {
            SkipWs(s, ref pos);
            if (pos >= s.Length || s[pos] != '"') throw JsErr.Syntax("JSON.parse: expected string key");
            string key = ParseString(s, ref pos);
            SkipWs(s, ref pos);
            if (pos >= s.Length || s[pos] != ':') throw JsErr.Syntax("JSON.parse: expected ':'");
            pos++;
            object val = ParseValue(realm, s, ref pos);
            o.SetProperty(key, val);
            SkipWs(s, ref pos);
            if (pos >= s.Length) throw JsErr.Syntax("JSON.parse: unterminated object");
            if (s[pos] == ',') { pos++; continue; }
            if (s[pos] == '}') { pos++; return o; }
            throw JsErr.Syntax("JSON.parse: expected ',' or '}'");
        }
    }

    private static JsArray ParseArray(JsRealm realm, string s, ref int pos)
    {
        JsArray arr = realm.NewArray();
        pos++; // consume '['
        SkipWs(s, ref pos);
        if (pos < s.Length && s[pos] == ']') { pos++; return arr; }
        while (true)
        {
            object val = ParseValue(realm, s, ref pos);
            arr.Push(val);
            SkipWs(s, ref pos);
            if (pos >= s.Length) throw JsErr.Syntax("JSON.parse: unterminated array");
            if (s[pos] == ',') { pos++; continue; }
            if (s[pos] == ']') { pos++; return arr; }
            throw JsErr.Syntax("JSON.parse: expected ',' or ']'");
        }
    }

    private static string ParseString(string s, ref int pos)
    {
        pos++; // consume opening quote
        StringBuilder sb = new StringBuilder();
        while (true)
        {
            if (pos >= s.Length) throw JsErr.Syntax("JSON.parse: unterminated string");
            char c = s[pos++];
            if (c == '"') return sb.ToString();
            if (c == '\\')
            {
                if (pos >= s.Length) throw JsErr.Syntax("JSON.parse: bad escape");
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > s.Length) throw JsErr.Syntax("JSON.parse: bad \\u escape");
                        int code = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            int hv = HexVal(s[pos + i]);
                            if (hv < 0) throw JsErr.Syntax("JSON.parse: bad \\u escape");
                            code = code * 16 + hv;
                        }
                        pos += 4;
                        sb.Append((char)code);
                        break;
                    default: throw JsErr.Syntax("JSON.parse: bad escape '\\" + e + "'");
                }
            }
            else sb.Append(c);
        }
    }

    private static object ParseNumber(string s, ref int pos)
    {
        int start = pos;
        if (pos < s.Length && s[pos] == '-') pos++;
        while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
        if (pos < s.Length && s[pos] == '.')
        {
            pos++;
            while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
        }
        if (pos < s.Length && (s[pos] == 'e' || s[pos] == 'E'))
        {
            pos++;
            if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
            while (pos < s.Length && s[pos] >= '0' && s[pos] <= '9') pos++;
        }
        string num = s.Substring(start, pos - start);
        double d;
        if (!JsValueOps.TryParseDecimal(num, 0, num.Length, out d)) throw JsErr.Syntax("JSON.parse: bad number");
        return d;
    }

    private static bool Match(string s, ref int pos, string word)
    {
        if (pos + word.Length > s.Length) return false;
        for (int i = 0; i < word.Length; i++)
            if (s[pos + i] != word[i]) return false;
        pos += word.Length;
        return true;
    }

    private static void SkipWs(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            char c = s[pos];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') pos++;
            else break;
        }
    }

    private static int HexVal(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    // ---- stringify --------------------------------------------------------

    /// <summary>Returns the JSON text, or null when the value is undefined/function.</summary>
    public static string Stringify(JsRealm realm, object v)
    {
        StringBuilder sb = new StringBuilder();
        if (!WriteValue(realm, v, sb, 0)) return null;
        return sb.ToString();
    }

    private static bool WriteValue(JsRealm realm, object v, StringBuilder sb, int depth)
    {
        if (v is JsUndefined || v is JsFunction) return false;
        if (v is JsNull) { sb.Append("null"); return true; }
        if (v is bool) { sb.Append((bool)v ? "true" : "false"); return true; }
        if (v is double)
        {
            double d = (double)v;
            if (double.IsNaN(d) || double.IsInfinity(d)) { sb.Append("null"); return true; }
            sb.Append(JsValueOps.NumberToString(d));
            return true;
        }
        if (v is string) { WriteString((string)v, sb); return true; }
        if (v is JsSymbol) return false;

        if (v is JsArray)
        {
            JsArray arr = (JsArray)v;
            sb.Append('[');
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(',');
                if (!WriteValue(realm, arr.GetIndex(i), sb, depth + 1)) sb.Append("null");
            }
            sb.Append(']');
            return true;
        }

        if (v is JsObject)
        {
            JsObject o = (JsObject)v;
            sb.Append('{');
            List<string> keys = o.OwnKeys(true);
            bool first = true;
            for (int i = 0; i < keys.Count; i++)
            {
                object val;
                if (!o.GetProperty(keys[i], out val)) continue;
                if (val is JsUndefined || val is JsFunction) continue;
                if (!first) sb.Append(',');
                first = false;
                WriteString(keys[i], sb);
                sb.Append(':');
                if (!WriteValue(realm, val, sb, depth + 1)) sb.Append("null");
            }
            sb.Append('}');
            return true;
        }

        return false;
    }

    private static void WriteString(string s, StringBuilder sb)
    {
        sb.Append('"');
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c == '\b') sb.Append("\\b");
            else if (c == '\f') sb.Append("\\f");
            else if (c < 32)
            {
                sb.Append("\\u");
                sb.Append(HexChar((c >> 12) & 15));
                sb.Append(HexChar((c >> 8) & 15));
                sb.Append(HexChar((c >> 4) & 15));
                sb.Append(HexChar(c & 15));
            }
            else sb.Append(c);
        }
        sb.Append('"');
    }

    private static char HexChar(int v) { return v < 10 ? (char)('0' + v) : (char)('A' + (v - 10)); }
}
