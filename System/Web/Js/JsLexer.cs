using System;
using System.Collections.Generic;
using System.Text;

// ---------------------------------------------------------------------------
// JavaScript tokenizer. Produces the whole token list in a single forward
// pass (the parser backtracks by index). Template literals are handled here
// with a depth stack so `a${b{c}d}e` and nesting resolve correctly, and `/`
// is disambiguated between division and regex by a contextual flag.
// ---------------------------------------------------------------------------

public enum JsTokenType : byte
{
    Eof,
    Ident,
    Keyword,
    Number,
    String,
    Punct,
    Regex,
    TmplNoSub,  // `...` with no interpolation
    TmplStart,  // `...${
    TmplMiddle, // }...${
    TmplEnd,    // }...`
}

public sealed class JsToken
{
    public JsTokenType Type;
    public object Value;    // number|string; for Punct the operator text; template: cooked text
    public string Flags;    // regex flags
    public int Line;
    public bool CookedOk;   // template cooked string was valid

    public override string ToString()
    {
        if (Type == JsTokenType.Punct) return (string)Value;
        if (Value != null) return Value.ToString();
        return Type.ToString();
    }
}

public static class JsLexer
{
    private sealed class TmplCtx
    {
        public int Depth;
    }

    private sealed class State
    {
        public string Src;
        public int Pos;
        public int Line;
        public List<JsToken> Out;
        public List<TmplCtx> Tmpl;
        public bool RegexAllowed;
    }

    // Reserved words that can never be used as a binding identifier.
    private static readonly Dictionary<string, bool> Reserved = new Dictionary<string, bool>();
    private static readonly Dictionary<string, bool> ValueKeywords = new Dictionary<string, bool>();

    static JsLexer()
    {
        string[] words = {
            "break","case","catch","class","const","continue","debugger","default","delete",
            "do","else","enum","export","extends","false","finally","for","function","if",
            "import","in","instanceof","new","null","return","super","switch","this","throw",
            "true","try","typeof","var","void","while","with","yield"
        };
        for (int i = 0; i < words.Length; i++) Reserved[words[i]] = true;

        string[] vals = { "this","true","false","null","super" };
        for (int i = 0; i < vals.Length; i++) ValueKeywords[vals[i]] = true;
    }

    public static bool IsReserved(string word)
    {
        return Reserved.ContainsKey(word);
    }

    public static bool IsValueKeyword(string word)
    {
        return ValueKeywords.ContainsKey(word);
    }

    public static List<JsToken> Lex(string src)
    {
        State s = new State
        {
            Src = src ?? "",
            Pos = 0,
            Line = 1,
            Out = new List<JsToken>(),
            Tmpl = new List<TmplCtx>(),
            RegexAllowed = true,
        };

        while (true)
        {
            SkipTrivia(s);
            if (s.Pos >= s.Src.Length)
            {
                s.Out.Add(Tok(JsTokenType.Eof, null, s.Line));
                break;
            }

            char c = s.Src[s.Pos];

            // template resumption inside a substitution
            if (c == '}' && s.Tmpl.Count > 0)
            {
                TmplCtx top = s.Tmpl[s.Tmpl.Count - 1];
                top.Depth--;
                if (top.Depth <= 0)
                {
                    s.Pos++; // consume the '}' that closes the substitution
                    ScanTemplateChunk(s, false);
                    continue;
                }
                s.Pos++;
                s.Out.Add(Tok(JsTokenType.Punct, "}", s.Line));
                s.RegexAllowed = true;
                continue;
            }
            if (c == '{' && s.Tmpl.Count > 0)
            {
                s.Tmpl[s.Tmpl.Count - 1].Depth++;
                s.Pos++;
                s.Out.Add(Tok(JsTokenType.Punct, "{", s.Line));
                s.RegexAllowed = true;
                continue;
            }

            if (c == '`')
            {
                s.Pos++;
                ScanTemplateChunk(s, true);
                continue;
            }

            if (c == '/' && s.RegexAllowed)
            {
                ScanRegex(s);
                continue;
            }

            if (c == '"' || c == '\'')
            {
                ScanString(s, c);
                continue;
            }

            if (IsDigit(c) || (c == '.' && s.Pos + 1 < s.Src.Length && IsDigit(s.Src[s.Pos + 1])))
            {
                ScanNumber(s);
                continue;
            }

            if (IsIdentStart(c))
            {
                ScanIdent(s);
                continue;
            }

            ScanPunct(s);
        }

        return s.Out;
    }

    // ---- trivia -----------------------------------------------------------

    private static void SkipTrivia(State s)
    {
        while (s.Pos < s.Src.Length)
        {
            char c = s.Src[s.Pos];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v')
            {
                if (c == '\n') s.Line++;
                s.Pos++;
                continue;
            }
            if (c == '/' && s.Pos + 1 < s.Src.Length)
            {
                char n = s.Src[s.Pos + 1];
                if (n == '/')
                {
                    s.Pos += 2;
                    while (s.Pos < s.Src.Length && s.Src[s.Pos] != '\n') s.Pos++;
                    continue;
                }
                if (n == '*')
                {
                    s.Pos += 2;
                    while (s.Pos + 1 < s.Src.Length && !(s.Src[s.Pos] == '*' && s.Src[s.Pos + 1] == '/'))
                    {
                        if (s.Src[s.Pos] == '\n') s.Line++;
                        s.Pos++;
                    }
                    if (s.Pos + 1 < s.Src.Length) s.Pos += 2;
                    else s.Pos = s.Src.Length;
                    continue;
                }
            }
            // Unicode BOM / NBSP treated as whitespace
            if (c == ' ' || c == '﻿')
            {
                s.Pos++;
                continue;
            }
            break;
        }
    }

    // ---- identifiers ------------------------------------------------------

    private static void ScanIdent(State s)
    {
        int start = s.Pos;
        while (s.Pos < s.Src.Length && IsIdentPart(s.Src[s.Pos])) s.Pos++;
        string word = s.Src.Substring(start, s.Pos - start);

        if (Reserved.ContainsKey(word))
        {
            s.Out.Add(Tok(JsTokenType.Keyword, word, s.Line));
            s.RegexAllowed = !ValueKeywords.ContainsKey(word);
        }
        else
        {
            s.Out.Add(Tok(JsTokenType.Ident, word, s.Line));
            s.RegexAllowed = false;
        }
    }

    // ---- numbers ----------------------------------------------------------

    private static void ScanNumber(State s)
    {
        int start = s.Pos;
        int line = s.Line;

        // 0x / 0b / 0o prefixes
        if (s.Src[s.Pos] == '0' && s.Pos + 1 < s.Src.Length)
        {
            char p = s.Src[s.Pos + 1];
            if (p == 'x' || p == 'X') { s.Pos += 2; ScanRadix(s, 16); EmitNumber(s, start, line, 16); return; }
            if (p == 'b' || p == 'B') { s.Pos += 2; ScanRadix(s, 2); EmitNumber(s, start, line, 2); return; }
            if (p == 'o' || p == 'O') { s.Pos += 2; ScanRadix(s, 8); EmitNumber(s, start, line, 8); return; }
            if (p >= '0' && p <= '7')
            {
                // legacy octal literal (sloppy mode): treat as octal value
                s.Pos++;
                while (s.Pos < s.Src.Length && ((s.Src[s.Pos] >= '0' && s.Src[s.Pos] <= '7') || s.Src[s.Pos] == '_')) s.Pos++;
                EmitNumber(s, start, line, 8);
                return;
            }
        }

        while (s.Pos < s.Src.Length && (IsDigit(s.Src[s.Pos]) || s.Src[s.Pos] == '_')) s.Pos++;
        if (s.Pos < s.Src.Length && s.Src[s.Pos] == '.')
        {
            s.Pos++;
            while (s.Pos < s.Src.Length && (IsDigit(s.Src[s.Pos]) || s.Src[s.Pos] == '_')) s.Pos++;
        }
        if (s.Pos < s.Src.Length && (s.Src[s.Pos] == 'e' || s.Src[s.Pos] == 'E'))
        {
            int save = s.Pos;
            s.Pos++;
            if (s.Pos < s.Src.Length && (s.Src[s.Pos] == '+' || s.Src[s.Pos] == '-')) s.Pos++;
            if (s.Pos < s.Src.Length && (IsDigit(s.Src[s.Pos]) || s.Src[s.Pos] == '_'))
            {
                while (s.Pos < s.Src.Length && (IsDigit(s.Src[s.Pos]) || s.Src[s.Pos] == '_')) s.Pos++;
            }
            else s.Pos = save;
        }

        EmitNumber(s, start, line, 10);
    }

    private static void ScanRadix(State s, int radix)
    {
        while (s.Pos < s.Src.Length && (IsRadixDigit(s.Src[s.Pos], radix) || s.Src[s.Pos] == '_')) s.Pos++;
    }

    private static void EmitNumber(State s, int start, int line, int radix)
    {
        if (s.Pos < s.Src.Length && s.Src[s.Pos] == 'n') s.Pos++; // tolerate BigInt literal suffix
        string raw = s.Src.Substring(start, s.Pos - start);
        if (raw.IndexOf('_') >= 0) raw = raw.Replace("_", "");
        if (raw.EndsWith("n", StringComparison.OrdinalIgnoreCase)) raw = raw.Substring(0, raw.Length - 1);
        double value;
        if (radix == 10)
        {
            if (!JsValueOps.TryParseDecimal(raw, 0, raw.Length, out value)) value = double.NaN;
        }
        else if (radix == 16)
        {
            value = JsValueOps.ParseHex(raw, 2);
        }
        else
        {
            value = 0;
            for (int i = 2; i < raw.Length; i++)
            {
                int d = DigitVal(raw[i]);
                if (d < 0) break;
                value = value * radix + d;
            }
        }
        s.Out.Add(Tok(JsTokenType.Number, value, line));
        s.RegexAllowed = false;
    }

    private static int DigitVal(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }

    private static bool IsRadixDigit(char c, int radix)
    {
        if (radix == 2) return c == '0' || c == '1';
        if (radix == 8) return c >= '0' && c <= '7';
        if (radix == 16) return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        return IsDigit(c);
    }

    // ---- strings ----------------------------------------------------------

    private static void ScanString(State s, char quote)
    {
        int line = s.Line;
        s.Pos++; // opening quote
        StringBuilder sb = new StringBuilder();
        while (s.Pos < s.Src.Length)
        {
            char c = s.Src[s.Pos];
            if (c == quote) { s.Pos++; break; }
            if (c == '\n') { s.Line++; s.Pos++; sb.Append('\n'); continue; }
            if (c == '\\')
            {
                s.Pos++;
                if (s.Pos >= s.Src.Length) break;
                char e = s.Src[s.Pos];
                if (e == '\n') { s.Line++; s.Pos++; continue; }       // line continuation
                if (e == '\r') { s.Pos++; if (s.Pos < s.Src.Length && s.Src[s.Pos] == '\n') s.Pos++; continue; }
                AppendEscape(s, sb, e);
                continue;
            }
            sb.Append(c);
            s.Pos++;
        }
        s.Out.Add(Tok(JsTokenType.String, sb.ToString(), line));
        s.RegexAllowed = false;
    }

    // Decode one escape sequence starting after the backslash, given first escaped char.
    private static void AppendEscape(State s, StringBuilder sb, char e)
    {
        s.Pos++; // consume the escaped char
        switch (e)
        {
            case 'n': sb.Append('\n'); return;
            case 't': sb.Append('\t'); return;
            case 'r': sb.Append('\r'); return;
            case 'b': sb.Append('\b'); return;
            case 'f': sb.Append('\f'); return;
            case 'v': sb.Append('\v'); return;
            case '0': sb.Append('\0'); return;
            case 'x': { sb.Append(ReadHex(s, 2)); return; }
            case 'u':
                {
                    if (s.Pos < s.Src.Length && s.Src[s.Pos] == '{')
                    {
                        s.Pos++;
                        int code = 0;
                        while (s.Pos < s.Src.Length && s.Src[s.Pos] != '}')
                        {
                            int d = DigitVal(s.Src[s.Pos]);
                            if (d < 0) break;
                            code = code * 16 + d;
                            s.Pos++;
                        }
                        if (s.Pos < s.Src.Length && s.Src[s.Pos] == '}') s.Pos++;
                        sb.Append(CodePointToChar(code));
                        return;
                    }
                    sb.Append(ReadHex(s, 4));
                    return;
                }
            default:
                sb.Append(e); // unknown escape -> literal char
                return;
        }
    }

    private static char ReadHex(State s, int count)
    {
        int code = 0;
        for (int i = 0; i < count && s.Pos < s.Src.Length; i++)
        {
            int d = DigitVal(s.Src[s.Pos]);
            if (d < 0) break;
            code = code * 16 + d;
            s.Pos++;
        }
        if (code == 0) return '\0';
        if (code < 0xD800 || code > 0xDFFF) return (char)code;
        return (char)code;
    }

    private static string CodePointToChar(int code)
    {
        if (code <= 0xFFFF) return ((char)code).ToString();
        code -= 0x10000;
        char hi = (char)(0xD800 + (code >> 10));
        char lo = (char)(0xDC00 + (code & 0x3FF));
        return new string(new char[] { hi, lo });
    }

    // ---- templates --------------------------------------------------------

    private static void ScanTemplateChunk(State s, bool isStart)
    {
        int line = s.Line;
        StringBuilder cooked = new StringBuilder();
        bool valid = true;
        bool more = false; // ended with ${ (another substitution follows)

        while (s.Pos < s.Src.Length)
        {
            char c = s.Src[s.Pos];
            if (c == '`') { s.Pos++; break; }
            if (c == '$' && s.Pos + 1 < s.Src.Length && s.Src[s.Pos + 1] == '{')
            {
                s.Pos += 2;
                more = true;
                break;
            }
            if (c == '\\')
            {
                s.Pos++;
                if (s.Pos >= s.Src.Length) break;
                char e = s.Src[s.Pos];
                if (e == '\n') { s.Line++; s.Pos++; continue; }
                if (e == '\r') { s.Pos++; if (s.Pos < s.Src.Length && s.Src[s.Pos] == '\n') s.Pos++; continue; }
                // validate escape for cooked string
                int before = s.Pos;
                StringBuilder tmp = new StringBuilder();
                try
                {
                    AppendEscape(s, tmp, e);
                    cooked.Append(tmp.ToString());
                }
                catch
                {
                    valid = false;
                    cooked.Append('\\').Append(s.Src[before]);
                    s.Pos = before + 1;
                }
                continue;
            }
            if (c == '\n') s.Line++;
            cooked.Append(c);
            s.Pos++;
        }

        JsTokenType type;
        if (more)
        {
            if (isStart)
            {
                type = JsTokenType.TmplStart;
                s.Tmpl.Add(new TmplCtx { Depth = 1 });
            }
            else
            {
                type = JsTokenType.TmplMiddle;
                if (s.Tmpl.Count > 0) s.Tmpl[s.Tmpl.Count - 1].Depth = 1;
            }
        }
        else
        {
            type = isStart ? JsTokenType.TmplNoSub : JsTokenType.TmplEnd;
            if (!isStart && s.Tmpl.Count > 0) s.Tmpl.RemoveAt(s.Tmpl.Count - 1);
        }

        JsToken t = Tok(type, cooked.ToString(), line);
        t.CookedOk = valid;
        s.Out.Add(t);
        s.RegexAllowed = !more;
    }

    // ---- regex ------------------------------------------------------------

    private static void ScanRegex(State s)
    {
        int line = s.Line;
        s.Pos++; // opening '/'
        StringBuilder pat = new StringBuilder();
        bool inClass = false;
        while (s.Pos < s.Src.Length)
        {
            char c = s.Src[s.Pos];
            if (c == '\n') break; // unterminated
            if (c == '\\')
            {
                pat.Append(c);
                s.Pos++;
                if (s.Pos < s.Src.Length) { pat.Append(s.Src[s.Pos]); s.Pos++; }
                continue;
            }
            if (c == '[') inClass = true;
            else if (c == ']') inClass = false;
            else if (c == '/' && !inClass) { s.Pos++; break; }
            pat.Append(c);
            s.Pos++;
        }

        StringBuilder flags = new StringBuilder();
        while (s.Pos < s.Src.Length && IsIdentPart(s.Src[s.Pos]))
        {
            flags.Append(s.Src[s.Pos]);
            s.Pos++;
        }

        JsToken t = Tok(JsTokenType.Regex, pat.ToString(), line);
        t.Flags = flags.ToString();
        s.Out.Add(t);
        s.RegexAllowed = false;
    }

    // ---- punctuation ------------------------------------------------------

    private static void ScanPunct(State s)
    {
        string src = s.Src;
        int p = s.Pos;
        int n = src.Length;
        int line = s.Line;
        string op = null;
        int len = 0;

        // try 3-char, then 2-char, then 1-char (maximal munch)
        if (p + 2 < n) { op = TryOp(src.Substring(p, 3)); if (op != null) len = 3; }
        if (op == null && p + 1 < n) { op = TryOp(src.Substring(p, 2)); if (op != null) len = 2; }
        if (op == null) { op = TryOp(src.Substring(p, 1)); if (op != null) len = 1; }

        // `?.` is only optional-chaining when not followed by a digit (?.5)
        if (op == "?." && p + 2 < n && IsDigit(src[p + 2])) { op = "?"; len = 1; }

        if (op == null)
        {
            // unknown char: skip it as a 1-char punct so the parser reports it
            op = src.Substring(p, 1);
            len = 1;
        }

        s.Pos += len;
        s.Out.Add(Tok(JsTokenType.Punct, op, line));

        // regexAllowed after this token
        if (op == ")" || op == "]") s.RegexAllowed = false;
        else if (op == "++" || op == "--") s.RegexAllowed = false;
        else s.RegexAllowed = true;
    }

    private static string TryOp(string text)
    {
        switch (text)
        {
            // 3-char
            case "===": case "!==": case ">>>":
            case "<<=": case ">>=": case ">>>=":
            case "**=": case "...":
                return text;
            // 2-char
            case "==": case "!=": case "<=": case ">=":
            case "&&": case "||": case "??": case "?.": case "??=":
            case "&&=": case "||=":
            case "++": case "--":
            case "+=": case "-=": case "*=": case "/=": case "%=":
            case "&=": case "|=": case "^=":
            case "<<": case ">>": case "**": case "=>":
                return text;
            // 1-char
            case "{": case "}": case "(": case ")": case "[": case "]":
            case ";": case ",": case "<": case ">": case "+": case "-":
            case "*": case "/": case "%": case "&": case "|": case "^":
            case "!": case "~": case "?": case ":": case "=": case ".":
            case "@":
                return text;
            default:
                return null;
        }
    }

    // ---- helpers ----------------------------------------------------------

    private static JsToken Tok(JsTokenType type, object value, int line)
    {
        return new JsToken { Type = type, Value = value, Line = line, CookedOk = true };
    }

    public static bool IsDigit(char c) { return c >= '0' && c <= '9'; }

    public static bool IsIdentStart(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '$' || c >= 0x80;
    }

    public static bool IsIdentPart(char c)
    {
        return IsIdentStart(c) || IsDigit(c);
    }
}
