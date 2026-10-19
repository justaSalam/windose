using System;
using System.Collections.Generic;
using System.Text;

// ---------------------------------------------------------------------------
// Self-contained JavaScript-compatible regular expression engine.
//
//  * Compiles the pattern to a small node tree, then matches with a
//    backtracking continuation-passing matcher (leftmost-first, as in JS).
//  * Hand-rolled: System.Text.RegularExpressions is NOT used anywhere.
//  * Cosmos/IL2CPU safe: no culture APIs, no char.IsXxx, no ToLower,
//    no LINQ, no reflection; only manual ASCII checks, lists and arrays.
//  * A step counter (MaxSteps) aborts pathological patterns such as
//    (a+)+b against a long run of "a" instead of hanging the kernel.
//
// Verified against real JS engines for the semantics that matter here
// (empty-match replace rules, greedy/lazy quantifier behavior, empty
// iteration handling in nested quantifiers, backreference fallbacks,
// multiline anchors over all line terminators, lookbehind preference).
// ---------------------------------------------------------------------------

public sealed class JsMatch
{
    public int Index;              // 0-based start index in the input
    public string Value;           // the whole match text
    public List<string> Groups;    // capture group 1..N text; unmatched -> ""
    public List<bool> GroupOk;     // true if that group participated
}

public sealed class JsRegExp
{
    public string Source;
    public string Flags;
    public bool Global;
    public bool IgnoreCase;
    public bool Multiline;
    public bool DotAll;
    public bool Unicode;
    public bool Sticky;
    public int LastIndex;
    public List<string> GroupNames;   // null when the pattern has no named groups

    private int _groupCount;
    public int GroupCount { get { return _groupCount; } }

    // ---- matching state (single-threaded kernel use) ----
    private string _input;
    private int[] _starts;
    private int[] _ends;
    private int _steps;
    private bool _aborted;
    private int _matchEnd;
    private Cont _findCont;

    private const int MaxSteps = 1000000;

    private Node _root;

    private delegate bool Cont(int pos);

    public delegate string ReplaceFn(List<object> args);

    public JsRegExp(string source, string flags)
    {
        if (source == null) source = "";
        if (flags == null) flags = "";
        Source = source;
        Flags = flags;

        for (int i = 0; i < flags.Length; i++)
        {
            char f = flags[i];
            if (f == 'g') { if (Global) throw new JsSyntaxError("Duplicate flag 'g'"); Global = true; }
            else if (f == 'i') { if (IgnoreCase) throw new JsSyntaxError("Duplicate flag 'i'"); IgnoreCase = true; }
            else if (f == 'm') { if (Multiline) throw new JsSyntaxError("Duplicate flag 'm'"); Multiline = true; }
            else if (f == 's') { if (DotAll) throw new JsSyntaxError("Duplicate flag 's'"); DotAll = true; }
            else if (f == 'u') { if (Unicode) throw new JsSyntaxError("Duplicate flag 'u'"); Unicode = true; }
            else if (f == 'y') { if (Sticky) throw new JsSyntaxError("Duplicate flag 'y'"); Sticky = true; }
            else throw new JsSyntaxError("Invalid regular expression flag '" + f + "'");
        }

        ParsePattern(source);

        _starts = new int[_groupCount + 1];
        _ends = new int[_groupCount + 1];
        _findCont = OnFindMatch;
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    // First match at index >= startIndex. Does not consult or update LastIndex.
    public JsMatch Find(string input, int startIndex)
    {
        if (input == null) input = "";
        _steps = 0;
        _aborted = false;
        return FindCore(input, startIndex);
    }

    public bool Test(string input)
    {
        if (input == null) input = "";
        _steps = 0;
        _aborted = false;
        return FindCore(input, 0) != null;
    }

    public string Replace(string input, string replacement)
    {
        if (input == null) input = "";
        if (replacement == null) replacement = "";
        _steps = 0;
        _aborted = false;

        StringBuilder sb = new StringBuilder();
        int lastEnd = 0;
        int pos = 0;
        bool all = Global;

        while (pos <= input.Length)
        {
            JsMatch m = FindCore(input, pos);
            if (m == null) break;
            sb.Append(input, lastEnd, m.Index - lastEnd);
            AppendSubstitution(sb, replacement, input, m);
            lastEnd = m.Index + m.Value.Length;
            if (!all) break;
            // zero-length match: advance one position so the scan progresses
            pos = m.Value.Length == 0 ? m.Index + 1 : lastEnd;
        }

        sb.Append(input, lastEnd, input.Length - lastEnd);
        return sb.ToString();
    }

    // Callback receives: matchText, group1..groupN, offset, wholeInput.
    public string ReplaceWithFn(string input, ReplaceFn callback)
    {
        if (input == null) input = "";
        if (callback == null) return input;
        _steps = 0;
        _aborted = false;

        StringBuilder sb = new StringBuilder();
        int lastEnd = 0;
        int pos = 0;
        bool all = Global;

        while (pos <= input.Length)
        {
            JsMatch m = FindCore(input, pos);
            if (m == null) break;
            sb.Append(input, lastEnd, m.Index - lastEnd);

            List<object> args = new List<object>();
            args.Add(m.Value);
            for (int g = 0; g < m.Groups.Count; g++) args.Add(m.Groups[g]);
            args.Add(m.Index);
            args.Add(input);
            string rep = callback(args);
            if (rep != null) sb.Append(rep);

            lastEnd = m.Index + m.Value.Length;
            if (!all) break;
            pos = m.Value.Length == 0 ? m.Index + 1 : lastEnd;
        }

        sb.Append(input, lastEnd, input.Length - lastEnd);
        return sb.ToString();
    }

    // Index of a named group (1-based), false if the name is unknown.
    public bool TryGetGroupIndex(string name, out int index)
    {
        index = 0;
        if (name == null || GroupNames == null) return false;
        for (int i = 0; i < GroupNames.Count; i++)
        {
            string n = GroupNames[i];
            if (n != null && n == name) { index = i + 1; return true; }
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Matching core
    // ------------------------------------------------------------------

    private bool OnFindMatch(int p)
    {
        _matchEnd = p;
        _ends[0] = p;
        return true;
    }

    private JsMatch FindCore(string input, int startIndex)
    {
        _input = input;
        int len = input.Length;
        if (startIndex < 0) startIndex = 0;
        if (startIndex > len) return null;

        for (int i = startIndex; i <= len; i++)
        {
            ResetCaptures();
            _starts[0] = i;
            _matchEnd = -1;
            bool ok = MatchNode(_root, i, _findCont);
            if (ok)
            {
                JsMatch m = new JsMatch();
                m.Index = i;
                m.Value = input.Substring(i, _matchEnd - i);
                m.Groups = new List<string>(_groupCount);
                m.GroupOk = new List<bool>(_groupCount);
                for (int g = 1; g <= _groupCount; g++)
                {
                    int s = _starts[g];
                    int e = _ends[g];
                    if (s >= 0 && e >= s)
                    {
                        m.Groups.Add(input.Substring(s, e - s));
                        m.GroupOk.Add(true);
                    }
                    else
                    {
                        m.Groups.Add("");
                        m.GroupOk.Add(false);
                    }
                }
                return m;
            }
            if (_aborted) return null;
        }
        return null;
    }

    private void ResetCaptures()
    {
        for (int g = 0; g <= _groupCount; g++) { _starts[g] = -1; _ends[g] = -1; }
    }

    private int[] SaveCaps()
    {
        if (_groupCount == 0) return null;
        int[] a = new int[(_groupCount + 1) * 2];
        for (int i = 0; i <= _groupCount; i++) { a[i] = _starts[i]; a[_groupCount + 1 + i] = _ends[i]; }
        return a;
    }

    private void RestoreCaps(int[] a)
    {
        if (a == null) return;
        for (int i = 0; i <= _groupCount; i++) { _starts[i] = a[i]; _ends[i] = a[_groupCount + 1 + i]; }
    }

    private bool MatchNode(Node n, int pos, Cont k)
    {
        if (_aborted) return false;
        if (++_steps > MaxSteps) { _aborted = true; return false; }
        return n.Match(this, pos, k);
    }

    // Backtracking repeat. count < min forces iterations (an empty body match
    // is accepted there and redundant empty iterations are skipped in one jump).
    // Once min is satisfied, greedy tries more iterations first but REJECTS
    // zero-width iterations (they are backtracked out and captures discarded,
    // exactly like V8), then falls back to stopping. Lazy stops first.
    private bool MatchRepeat(Node body, int count, int pos, int min, int max, bool greedy, Cont k)
    {
        if (_aborted) return false;
        if (++_steps > MaxSteps) { _aborted = true; return false; }

        if (count < min)
        {
            return MatchNode(body, pos, delegate(int p)
            {
                int nc = count + 1;
                if (p == pos && nc < min) nc = min;   // further empty iterations are identical
                return MatchRepeat(body, nc, p, min, max, greedy, k);
            });
        }

        if (count >= max) return k(pos);

        if (greedy)
        {
            bool any = MatchNode(body, pos, delegate(int p)
            {
                if (p == pos) return false;           // no-progress iteration: backtrack into body
                return MatchRepeat(body, count + 1, p, min, max, greedy, k);
            });
            if (any) return true;
            return k(pos);
        }
        else
        {
            if (k(pos)) return true;
            bool any = MatchNode(body, pos, delegate(int p)
            {
                if (p == pos) return false;
                return MatchRepeat(body, count + 1, p, min, max, greedy, k);
            });
            return any;
        }
    }

    // Iterative fast path for repeats of single-char bodies (a+, [a-z]*, ...).
    // Avoids deep recursion on long inputs.
    private bool MatchSimpleRepeat(Node body, int pos, int min, int max, bool greedy, Cont k)
    {
        string s = _input;
        int len = s.Length;
        int avail = 0;
        while (avail < max && pos + avail < len)
        {
            if (++_steps > MaxSteps) { _aborted = true; return false; }
            if (!body.Simple(this, pos + avail)) break;
            avail++;
        }
        if (_aborted) return false;

        if (greedy)
        {
            for (int c = avail; c >= min; c--) if (k(pos + c)) return true;
        }
        else
        {
            for (int c = min; c <= avail; c++) if (k(pos + c)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // $ substitution for String.prototype.replace
    // ------------------------------------------------------------------

    private void AppendSubstitution(StringBuilder sb, string repl, string input, JsMatch m)
    {
        int i = 0;
        int n = repl.Length;
        int gcount = _groupCount;

        while (i < n)
        {
            char c = repl[i];
            if (c != '$' || i + 1 >= n) { sb.Append(c); i++; continue; }
            char c2 = repl[i + 1];

            if (c2 == '$') { sb.Append('$'); i += 2; }
            else if (c2 == '&') { sb.Append(m.Value); i += 2; }
            else if (c2 == '`') { sb.Append(input, 0, m.Index); i += 2; }
            else if (c2 == '\'')
            {
                int e = m.Index + m.Value.Length;
                sb.Append(input, e, input.Length - e);
                i += 2;
            }
            else if (c2 >= '1' && c2 <= '9')
            {
                int v = c2 - '0';
                int next = i + 2;
                if (next < n && repl[next] >= '0' && repl[next] <= '9')
                {
                    int v2 = v * 10 + (repl[next] - '0');
                    if (v2 >= 1 && v2 <= gcount) { v = v2; next++; }
                }
                if (v >= 1 && v <= gcount)
                {
                    AppendGroup(sb, m, v);
                    i = next;
                }
                else
                {
                    sb.Append('$');   // literal; digits are appended on later passes
                    i++;
                }
            }
            else if (c2 == '0')
            {
                sb.Append('$');   // $0 is never a group reference
                i++;
            }
            else if (c2 == '<')
            {
                int close = repl.IndexOf('>', i + 2);
                if (close < 0) { sb.Append('$'); i++; continue; }
                string name = repl.Substring(i + 2, close - (i + 2));
                int gi;
                if (name.Length > 0 && TryGetGroupIndex(name, out gi))
                {
                    AppendGroup(sb, m, gi);
                    i = close + 1;
                }
                else
                {
                    // unknown name or malformed: keep the text literally (as V8 does)
                    sb.Append(repl, i, close + 1 - i);
                    i = close + 1;
                }
            }
            else { sb.Append('$'); i++; }
        }
    }

    private static void AppendGroup(StringBuilder sb, JsMatch m, int g)
    {
        if (g - 1 < m.GroupOk.Count && m.GroupOk[g - 1]) sb.Append(m.Groups[g - 1]);
        // unmatched group substitutes to the empty string
    }

    // ------------------------------------------------------------------
    // Character helpers (manual ASCII only; no culture/locale APIs)
    // ------------------------------------------------------------------

    private static char Fold(char c)
    {
        if (c >= 'A' && c <= 'Z') return (char)(c + 32);
        return c;
    }

    private static char OtherCase(char c)
    {
        if (c >= 'A' && c <= 'Z') return (char)(c + 32);
        if (c >= 'a' && c <= 'z') return (char)(c - 32);
        return c;
    }

    private static bool CharEq(char a, char b, bool ic)
    {
        if (a == b) return true;
        if (!ic) return false;
        return Fold(a) == Fold(b);
    }

    private static bool IsWord(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    private static bool IsLineTerm(char c)
    {
        return c == '\n' || c == '\r' || c == '\u2028' || c == '\u2029';
    }

    private static bool ClassHit(List<int> ranges, char c, bool ic)
    {
        for (int i = 0; i < ranges.Count; i += 2)
            if (c >= ranges[i] && c <= ranges[i + 1]) return true;
        if (ic)
        {
            char o = OtherCase(c);
            if (o != c)
            {
                for (int i = 0; i < ranges.Count; i += 2)
                    if (o >= ranges[i] && o <= ranges[i + 1]) return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Parser state
    // ------------------------------------------------------------------

    private string _ps;
    private int _pp;
    private List<NBackref> _pendRefs;   // numeric \1 backrefs, resolved after parse
    private List<string> _pnames;       // sparse name list while parsing

    private char Pk { get { return _pp < _ps.Length ? _ps[_pp] : '\0'; } }

    private void ParsePattern(string src)
    {
        _ps = src;
        _pp = 0;
        _root = ParseDisjunction();
        if (_pp < _ps.Length)
            throw new JsSyntaxError("Unexpected ')' in pattern");

        // Second pass: backreference numbers/names are only meaningful once
        // the total group count and all names are known.
        if (_pendRefs != null)
        {
            for (int i = 0; i < _pendRefs.Count; i++)
            {
                NBackref b = _pendRefs[i];
                if (b.Val >= 1 && b.Val <= _groupCount) b.IsBackref = true;
                else { b.IsBackref = false; b.Text = OctalOrLiteral(b.Digits); }
            }
        }
        if (_pendNames != null)
        {
            for (int i = 0; i < _pendNames.Count; i++)
            {
                NBackref b = _pendNames[i];
                int idx;
                if (NameToIndex(b.Name, out idx)) { b.IsBackref = true; b.Val = idx; b.Text = null; }
                else { b.IsBackref = false; b.Text = "k<" + b.Name + ">"; }  // identity escape, as in V8
            }
        }
        if (_pnames != null)
            while (_pnames.Count < _groupCount) _pnames.Add(null);
        GroupNames = _pnames;
    }

    private List<NBackref> _pendNames;  // \k<name> backrefs, resolved after parse

    private Node ParseDisjunction()
    {
        List<Node> alts = new List<Node>();
        alts.Add(ParseConcat());
        while (_pp < _ps.Length && _ps[_pp] == '|')
        {
            _pp++;
            alts.Add(ParseConcat());
        }
        if (alts.Count == 1) return alts[0];
        NAlt a = new NAlt();
        a.Alts = alts;
        return a;
    }

    private Node ParseConcat()
    {
        List<Node> items = new List<Node>();
        while (_pp < _ps.Length)
        {
            char c = _ps[_pp];
            if (c == '|' || c == ')') break;
            items.Add(ParseTerm());
        }
        if (items.Count == 1) return items[0];
        NConcat cat = new NConcat();
        cat.Items = items;   // 0 items => matches the empty string
        return cat;
    }

    private Node ParseTerm()
    {
        char c = Pk;

        // A valid quantifier with nothing to quantify is a syntax error.
        if (c == '*' || c == '+' || c == '?') throw new JsSyntaxError("Nothing to repeat");
        if (c == '{')
        {
            int mn, mx;
            bool lz;
            if (TryParseQuant(out mn, out mx, out lz)) throw new JsSyntaxError("Nothing to repeat");
            // not a quantifier form -> the '{' is a literal, parsed below
        }

        int kind;   // 0 = plain, 1 = anchor/boundary (never quantifiable),
                    // 2 = lookahead (quantifiable), 3 = lookbehind (never)
        Node atom = ParseAtom(out kind);

        int qmn, qmx;
        bool qlazy;
        if (!TryParseQuant(out qmn, out qmx, out qlazy)) return atom;
        if (kind == 1) throw new JsSyntaxError("Nothing to repeat after assertion");
        if (kind == 3) throw new JsSyntaxError("Lookbehind cannot be quantified");

        NRepeat rep = new NRepeat();
        rep.Body = atom;
        rep.Min = qmn;
        rep.Max = qmx;
        rep.Greedy = !qlazy;
        rep.SimpleBody = atom.IsSimple;

        // A second quantifier directly after a quantified term is an error
        // in modern engines (a**, a?*, a{2}{3}, ...).
        int mn2, mx2;
        bool lz2;
        if (TryParseQuant(out mn2, out mx2, out lz2)) throw new JsSyntaxError("Multiple quantifiers");
        return rep;
    }

    // Consumes a quantifier (*, +, ?, {n}, {n,}, {n,m}) plus a single
    // optional lazy '?'. Returns false without consuming anything if the
    // text at the cursor is not a valid quantifier.
    private bool TryParseQuant(out int mn, out int mx, out bool lazy)
    {
        mn = 0;
        mx = 0;
        lazy = false;
        int p = _pp;
        if (p >= _ps.Length) return false;
        char c = _ps[p];

        if (c == '*') { mn = 0; mx = int.MaxValue; p++; }
        else if (c == '+') { mn = 1; mx = int.MaxValue; p++; }
        else if (c == '?') { mn = 0; mx = 1; p++; }
        else if (c == '{')
        {
            int q = p + 1;
            int a = 0;
            bool got1 = false;
            while (q < _ps.Length && _ps[q] >= '0' && _ps[q] <= '9')
            {
                a = a * 10 + (_ps[q] - '0');
                if (a > 1000000000) throw new JsSyntaxError("Quantifier value too large");
                got1 = true;
                q++;
            }
            if (!got1) return false;
            if (q >= _ps.Length) return false;
            if (_ps[q] == '}')
            {
                mn = a; mx = a; p = q + 1;
            }
            else if (_ps[q] == ',')
            {
                q++;
                if (q < _ps.Length && _ps[q] == '}')
                {
                    mn = a; mx = int.MaxValue; p = q + 1;
                }
                else
                {
                    int b = 0;
                    bool got2 = false;
                    while (q < _ps.Length && _ps[q] >= '0' && _ps[q] <= '9')
                    {
                        b = b * 10 + (_ps[q] - '0');
                        if (b > 1000000000) throw new JsSyntaxError("Quantifier value too large");
                        got2 = true;
                        q++;
                    }
                    if (!got2 || q >= _ps.Length || _ps[q] != '}') return false;
                    mn = a; mx = b; p = q + 1;
                }
            }
            else return false;
        }
        else return false;

        if (p < _ps.Length && _ps[p] == '?') { lazy = true; p++; }
        if (mx < mn) throw new JsSyntaxError("Quantifier out of order");
        _pp = p;
        return true;
    }

    private Node ParseAtom(out int kind)
    {
        kind = 0;
        char c = _ps[_pp];
        switch (c)
        {
            case '(':
                _pp++;
                return ParseGroup(out kind);
            case '[':
                return ParseClass();
            case '.':
                _pp++;
                return new NAny();
            case '^':
                _pp++;
                kind = 1;
                return new NAnchorStart();
            case '$':
                _pp++;
                kind = 1;
                return new NAnchorEnd();
            case '\\':
                _pp++;
                return ParseEscapeAtom(out kind);
            case ')':
            case '|':
                throw new JsSyntaxError("Unexpected character in pattern");
            default:
                _pp++;
                return new NChar() { C = c };
        }
    }

    private Node ParseGroup(out int kind)
    {
        kind = 0;
        if (Pk == '?')
        {
            char n1 = _pp + 1 < _ps.Length ? _ps[_pp + 1] : '\0';
            if (n1 == ':')
            {
                _pp += 2;
                Node body = ParseDisjunction();
                ExpectRParen();
                return body;
            }
            if (n1 == '=')
            {
                _pp += 2;
                kind = 2;
                Node body = ParseDisjunction();
                ExpectRParen();
                NLook look = new NLook();
                look.Body = body; look.Positive = true; look.Behind = false; look.MaxLen = -1;
                return look;
            }
            if (n1 == '!')
            {
                _pp += 2;
                kind = 2;
                Node body = ParseDisjunction();
                ExpectRParen();
                NLook look = new NLook();
                look.Body = body; look.Positive = false; look.Behind = false; look.MaxLen = -1;
                return look;
            }
            if (n1 == '<')
            {
                char n2 = _pp + 2 < _ps.Length ? _ps[_pp + 2] : '\0';
                if (n2 == '=' || n2 == '!')
                {
                    bool positive = (n2 == '=');
                    _pp += 3;
                    kind = 3;
                    Node body = ParseDisjunction();
                    ExpectRParen();
                    NLook look = new NLook();
                    look.Body = body;
                    look.Positive = positive;
                    look.Behind = true;
                    look.MaxLen = NodeMax(body);
                    return look;
                }
                // named capture (?<name>...)
                int nameStart = _pp + 2;
                int q = nameStart;
                while (q < _ps.Length && IsNameChar(_ps[q], q == nameStart)) q++;
                if (q == nameStart || q >= _ps.Length || _ps[q] != '>')
                    throw new JsSyntaxError("Invalid capture group name");
                string name = _ps.Substring(nameStart, q - nameStart);
                _pp = q + 1;
                int idx = ++_groupCount;
                RegisterName(name, idx);
                Node nbody = ParseDisjunction();
                ExpectRParen();
                NCapture cap = new NCapture();
                cap.Index = idx;
                cap.Body = nbody;
                cap.Name = name;
                return cap;
            }
            throw new JsSyntaxError("Invalid group");
        }

        int g = ++_groupCount;
        Node body2 = ParseDisjunction();
        ExpectRParen();
        NCapture cap2 = new NCapture();
        cap2.Index = g;
        cap2.Body = body2;
        return cap2;
    }

    private void ExpectRParen()
    {
        if (_pp >= _ps.Length || _ps[_pp] != ')')
            throw new JsSyntaxError("Unterminated group");
        _pp++;
    }

    private static bool IsNameChar(char c, bool first)
    {
        if (first)
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '$';
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '$';
    }

    private void RegisterName(string name, int idx)
    {
        if (_pnames == null) _pnames = new List<string>();
        for (int i = 0; i < _pnames.Count; i++)
            if (_pnames[i] != null && _pnames[i] == name)
                throw new JsSyntaxError("Duplicate capture group name '" + name + "'");
        while (_pnames.Count < idx) _pnames.Add(null);
        _pnames[idx - 1] = name;
    }

    private bool NameToIndex(string name, out int index)
    {
        index = 0;
        if (_pnames == null || name == null) return false;
        for (int i = 0; i < _pnames.Count; i++)
        {
            string n = _pnames[i];
            if (n != null && n == name) { index = i + 1; return true; }
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Character classes
    // ------------------------------------------------------------------

    private Node ParseClass()
    {
        _pp++;   // '['
        NClass nc = new NClass();
        nc.Ranges = new List<int>();
        if (Pk == '^') { nc.Neg = true; _pp++; }

        bool first = true;
        while (true)
        {
            if (_pp >= _ps.Length) throw new JsSyntaxError("Unterminated character class");
            if (_ps[_pp] == ']' && !first) { _pp++; break; }
            first = false;

            bool isChar;
            char ch = '\0';
            List<int> set = null;
            if (_ps[_pp] == '\\') { _pp++; ParseClassEscape(out isChar, out ch, out set); }
            else { ch = _ps[_pp]; _pp++; isChar = true; }

            if (isChar)
            {
                // range?  (a-z) -- the '-' is literal when next is ']' or EOF
                if (_pp + 1 < _ps.Length && _ps[_pp] == '-' && _ps[_pp + 1] != ']')
                {
                    _pp++;   // consume '-'
                    if (_pp >= _ps.Length) throw new JsSyntaxError("Unterminated character class");
                    bool isChar2;
                    char ch2 = '\0';
                    List<int> set2 = null;
                    if (_ps[_pp] == '\\') { _pp++; ParseClassEscape(out isChar2, out ch2, out set2); }
                    else { ch2 = _ps[_pp]; _pp++; isChar2 = true; }

                    if (isChar2)
                    {
                        if (ch2 < ch) throw new JsSyntaxError("Range out of order in character class");
                        nc.Ranges.Add(ch);
                        nc.Ranges.Add(ch2);
                    }
                    else
                    {
                        // e.g. [a-\d]: the '-' cannot be a range endpoint, so it
                        // becomes a literal (this matches modern V8 behavior).
                        nc.Ranges.Add(ch);
                        nc.Ranges.Add(ch);
                        nc.Ranges.Add('-');
                        nc.Ranges.Add('-');
                        nc.Ranges.AddRange(set2);
                    }
                }
                else
                {
                    nc.Ranges.Add(ch);
                    nc.Ranges.Add(ch);
                }
            }
            else
            {
                nc.Ranges.AddRange(set);
            }
        }
        return nc;
    }

    // Parses one escape sequence inside a class (cursor just past the '\').
    private void ParseClassEscape(out bool isChar, out char ch, out List<int> set)
    {
        isChar = true;
        ch = '\0';
        set = null;
        if (_pp >= _ps.Length) throw new JsSyntaxError("Unterminated escape sequence");
        char e = _ps[_pp++];

        switch (e)
        {
            case 'd': isChar = false; set = DigitRanges(); return;
            case 'D': isChar = false; set = Complement(DigitRanges()); return;
            case 'w': isChar = false; set = WordRanges(); return;
            case 'W': isChar = false; set = Complement(WordRanges()); return;
            case 's': isChar = false; set = SpaceRanges(); return;
            case 'S': isChar = false; set = Complement(SpaceRanges()); return;
            case 'b': ch = '\b'; return;           // backspace inside a class
            case 'n': ch = '\n'; return;
            case 'r': ch = '\r'; return;
            case 't': ch = '\t'; return;
            case 'f': ch = '\f'; return;
            case 'v': ch = '\v'; return;
            case '0':
            case '1': case '2': case '3': case '4': case '5': case '6': case '7':
            case '8': case '9':
                {
                    _pp--;
                    string digits = ReadDigitRun();
                    string text = OctalOrLiteral(digits);
                    if (text.Length == 1) { ch = text[0]; return; }
                    isChar = false;
                    set = new List<int>();
                    for (int i = 0; i < text.Length; i++) { set.Add(text[i]); set.Add(text[i]); }
                    return;
                }
            case 'x': { ch = ReadFixedHex(2); return; }
            case 'u':
                {
                    string t = ReadUnicodeEscape();
                    if (t.Length == 1) { ch = t[0]; return; }
                    isChar = false;
                    set = new List<int>();
                    for (int i = 0; i < t.Length; i++) { set.Add(t[i]); set.Add(t[i]); }
                    return;
                }
            case 'c':
                {
                    if (_pp >= _ps.Length) throw new JsSyntaxError("\\c needs a letter");
                    char l = _ps[_pp++];
                    if (!((l >= 'a' && l <= 'z') || (l >= 'A' && l <= 'Z')))
                        throw new JsSyntaxError("\\c needs a letter");
                    if (l >= 'a' && l <= 'z') l = (char)(l - 32);
                    ch = (char)(l ^ 0x40);
                    return;
                }
            default:
                ch = e;   // \], \-, \\, \., any other literal
                return;
        }
    }

    // ------------------------------------------------------------------
    // Escapes outside classes
    // ------------------------------------------------------------------

    private Node ParseEscapeAtom(out int kind)
    {
        kind = 0;
        if (_pp >= _ps.Length) throw new JsSyntaxError("Unterminated escape sequence");
        char e = _ps[_pp];

        if (e >= '1' && e <= '9')
        {
            string digits = ReadDigitRun();
            NBackref b = new NBackref();
            b.Digits = digits;
            b.Val = ParseInt(digits);
            if (_pendRefs == null) _pendRefs = new List<NBackref>();
            _pendRefs.Add(b);
            return b;
        }
        if (e == '0')
        {
            string digits = ReadDigitRun();
            return TextNode(OctalOrLiteral(digits));
        }

        _pp++;
        switch (e)
        {
            case 'd': return new NClass() { Ranges = DigitRanges(), Neg = false };
            case 'D': return new NClass() { Ranges = DigitRanges(), Neg = true };
            case 'w': return new NClass() { Ranges = WordRanges(), Neg = false };
            case 'W': return new NClass() { Ranges = WordRanges(), Neg = true };
            case 's': return new NClass() { Ranges = SpaceRanges(), Neg = false };
            case 'S': return new NClass() { Ranges = SpaceRanges(), Neg = true };
            case 'b': kind = 1; return new NWordBound();
            case 'B': kind = 1; return new NNotWordBound();
            case 'n': return new NChar() { C = '\n' };
            case 'r': return new NChar() { C = '\r' };
            case 't': return new NChar() { C = '\t' };
            case 'f': return new NChar() { C = '\f' };
            case 'v': return new NChar() { C = '\v' };
            case 'x': return TextNode(ReadFixedHex(2).ToString());
            case 'u': return TextNode(ReadUnicodeEscape());
            case 'c':
                {
                    if (_pp >= _ps.Length) throw new JsSyntaxError("\\c needs a letter");
                    char l = _ps[_pp++];
                    if (!((l >= 'a' && l <= 'z') || (l >= 'A' && l <= 'Z')))
                        throw new JsSyntaxError("\\c needs a letter");
                    if (l >= 'a' && l <= 'z') l = (char)(l - 32);
                    return new NChar() { C = (char)(l ^ 0x40) };
                }
            case 'k':
                {
                    if (Pk != '<') return new NChar() { C = 'k' };
                    int nameStart = _pp + 1;
                    int q = nameStart;
                    while (q < _ps.Length && IsNameChar(_ps[q], q == nameStart)) q++;
                    if (q == nameStart || q >= _ps.Length || _ps[q] != '>')
                        throw new JsSyntaxError("Invalid backreference name");
                    string name = _ps.Substring(nameStart, q - nameStart);
                    _pp = q + 1;
                    NBackref b = new NBackref();
                    b.Name = name;
                    if (_pendNames == null) _pendNames = new List<NBackref>();
                    _pendNames.Add(b);
                    return b;
                }
            default:
                return new NChar() { C = e };   // \. \* \\ \/ \q ... any literal
        }
    }

    private static Node TextNode(string t)
    {
        if (t.Length == 1) return new NChar() { C = t[0] };
        NSeq s = new NSeq();
        s.S = t;
        return s;
    }

    // Up to 3 decimal digits (cursor unmoved if the current char is not a digit).
    private string ReadDigitRun()
    {
        int p = _pp;
        int n = 0;
        while (p < _ps.Length && _ps[p] >= '0' && _ps[p] <= '9' && n < 3)
        {
            p++;
            n++;
        }
        if (n == 0) throw new JsSyntaxError("Invalid escape sequence");
        _pp = p;
        return _ps.Substring(_pp - n, n);
    }

    // Decimal escape that is not a backreference behaves like JS's legacy
    // octal escape, or as a literal digit (8/9). Used for \0, \1, \10, ...
    private static string OctalOrLiteral(string digits)
    {
        char f = digits[0];
        if (f == '0')
        {
            int i = 1, v = 0, n = 1;
            while (i < digits.Length && n < 3 && digits[i] >= '0' && digits[i] <= '7')
            { v = v * 8 + (digits[i] - '0'); i++; n++; }
            string r = ((char)v).ToString();
            if (i < digits.Length) r += digits.Substring(i);
            return r;
        }
        if (f == '8' || f == '9') return digits;
        int maxLen = (f >= '0' && f <= '3') ? 3 : 2;
        int j = 1, val = f - '0', cnt = 1;
        while (j < digits.Length && cnt < maxLen && digits[j] >= '0' && digits[j] <= '7')
        { val = val * 8 + (digits[j] - '0'); j++; cnt++; }
        string res = ((char)val).ToString();
        if (j < digits.Length) res += digits.Substring(j);
        return res;
    }

    private static int ParseInt(string digits)
    {
        int v = 0;
        for (int i = 0; i < digits.Length; i++) v = v * 10 + (digits[i] - '0');
        return v;
    }

    private char ReadFixedHex(int count)
    {
        int v = 0;
        for (int i = 0; i < count; i++)
        {
            if (_pp >= _ps.Length) throw new JsSyntaxError("Invalid hex escape");
            char h = _ps[_pp++];
            int d;
            if (h >= '0' && h <= '9') d = h - '0';
            else if (h >= 'a' && h <= 'f') d = h - 'a' + 10;
            else if (h >= 'A' && h <= 'F') d = h - 'A' + 10;
            else throw new JsSyntaxError("Invalid hex escape");
            v = v * 16 + d;
        }
        return (char)v;
    }

    // \uHHHH or \u{H...} (accepted with or without the u flag, per spec text).
    private string ReadUnicodeEscape()
    {
        if (Pk == '{')
        {
            _pp++;
            int v = 0;
            int n = 0;
            while (_pp < _ps.Length && _ps[_pp] != '}')
            {
                char h = _ps[_pp++];
                int d;
                if (h >= '0' && h <= '9') d = h - '0';
                else if (h >= 'a' && h <= 'f') d = h - 'a' + 10;
                else if (h >= 'A' && h <= 'F') d = h - 'A' + 10;
                else throw new JsSyntaxError("Invalid \\u{} escape");
                v = v * 16 + d;
                n++;
                if (n > 6) throw new JsSyntaxError("Invalid \\u{} escape");
            }
            if (n == 0 || _pp >= _ps.Length || _ps[_pp] != '}')
                throw new JsSyntaxError("Invalid \\u{} escape");
            _pp++;
            if (v > 0x10FFFF) throw new JsSyntaxError("Code point out of range");
            if (v <= 0xFFFF) return ((char)v).ToString();
            v -= 0x10000;
            char hi = (char)(0xD800 + (v >> 10));
            char lo = (char)(0xDC00 + (v & 0x3FF));
            return new string(new char[] { hi, lo });
        }
        return ReadFixedHex(4).ToString();
    }

    // ------------------------------------------------------------------
    // Character class building blocks
    // ------------------------------------------------------------------

    private static void Add(List<int> l, int a, int b) { l.Add(a); l.Add(b); }

    private static List<int> DigitRanges()
    {
        List<int> l = new List<int>();
        Add(l, '0', '9');
        return l;
    }

    private static List<int> WordRanges()
    {
        List<int> l = new List<int>();
        Add(l, '0', '9');
        Add(l, 'A', 'Z');
        Add(l, '_', '_');
        Add(l, 'a', 'z');
        return l;
    }

    private static List<int> SpaceRanges()
    {
        List<int> l = new List<int>();
        Add(l, 0x09, 0x0D);
        Add(l, 0x20, 0x20);
        Add(l, 0xA0, 0xA0);
        Add(l, 0x1680, 0x1680);
        Add(l, 0x2000, 0x200A);
        Add(l, 0x2028, 0x2029);
        Add(l, 0x202F, 0x202F);
        Add(l, 0x205F, 0x205F);
        Add(l, 0x3000, 0x3000);
        Add(l, 0xFEFF, 0xFEFF);
        return l;
    }

    // Complement of an ordered, non-overlapping range list over the UTF-16
    // code unit space (used for \D, \W, \S inside classes).
    private static List<int> Complement(List<int> ranges)
    {
        List<int> r = new List<int>();
        int prev = 0;
        for (int i = 0; i < ranges.Count; i += 2)
        {
            int lo = ranges[i];
            int hi = ranges[i + 1];
            if (lo > prev) Add(r, prev, lo - 1);
            prev = hi + 1;
        }
        if (prev <= 0xFFFF) Add(r, prev, 0xFFFF);
        return r;
    }

    // ------------------------------------------------------------------
    // Bounded length of a node (for lookbehind scanning); -1 = unbounded
    // ------------------------------------------------------------------

    private static int NodeMax(Node n)
    {
        if (n is NChar || n is NAny || n is NClass) return 1;
        if (n is NSeq) return ((NSeq)n).S.Length;
        if (n is NAnchorStart || n is NAnchorEnd || n is NWordBound || n is NNotWordBound) return 0;
        if (n is NLook) return 0;
        if (n is NBackref) return -1;
        if (n is NCapture) return NodeMax(((NCapture)n).Body);
        if (n is NConcat)
        {
            List<Node> items = ((NConcat)n).Items;
            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                int m = NodeMax(items[i]);
                if (m < 0) return -1;
                total += m;
                if (total > 100000) return 100000;
            }
            return total;
        }
        if (n is NAlt)
        {
            List<Node> alts = ((NAlt)n).Alts;
            int best = 0;
            for (int i = 0; i < alts.Count; i++)
            {
                int m = NodeMax(alts[i]);
                if (m < 0) return -1;
                if (m > best) best = m;
            }
            return best;
        }
        if (n is NRepeat)
        {
            NRepeat r = (NRepeat)n;
            if (r.Max == int.MaxValue) return -1;
            int bm = NodeMax(r.Body);
            if (bm < 0) return -1;
            long total = (long)bm * r.Max;
            if (total > 100000) return 100000;
            return (int)total;
        }
        return -1;
    }

    // ------------------------------------------------------------------
    // Compiled pattern nodes
    // ------------------------------------------------------------------

    private abstract class Node
    {
        // True only for bodies that consume exactly one character per
        // iteration and have no side effects (safe for the iterative
        // repeat fast path).
        public virtual bool IsSimple { get { return false; } }
        public abstract bool Match(JsRegExp re, int pos, Cont k);
        public virtual bool Simple(JsRegExp re, int pos) { return false; }
    }

    // A single literal character.
    private sealed class NChar : Node
    {
        public char C;
        public override bool IsSimple { get { return true; } }
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            string s = re._input;
            if (pos < s.Length && CharEq(s[pos], C, re.IgnoreCase)) return k(pos + 1);
            return false;
        }
        public override bool Simple(JsRegExp re, int pos)
        {
            return CharEq(re._input[pos], C, re.IgnoreCase);
        }
    }

    // A literal string (multi-char escapes such as \u{1F600} or octal runs).
    private sealed class NSeq : Node
    {
        public string S;
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            string s = re._input;
            if (pos + S.Length > s.Length) return false;
            for (int i = 0; i < S.Length; i++)
                if (!CharEq(s[pos + i], S[i], re.IgnoreCase)) return false;
            return k(pos + S.Length);
        }
    }

    // '.'
    private sealed class NAny : Node
    {
        public override bool IsSimple { get { return true; } }
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            if (Simple(re, pos)) return k(pos + 1);
            return false;
        }
        public override bool Simple(JsRegExp re, int pos)
        {
            string s = re._input;
            if (pos >= s.Length) return false;
            char c = s[pos];
            if (!re.DotAll && IsLineTerm(c)) return false;
            return true;
        }
    }

    // A character class; Ranges is a flat [lo,hi,lo,hi,...] list.
    private sealed class NClass : Node
    {
        public List<int> Ranges;
        public bool Neg;
        public override bool IsSimple { get { return true; } }
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            if (Simple(re, pos)) return k(pos + 1);
            return false;
        }
        public override bool Simple(JsRegExp re, int pos)
        {
            string s = re._input;
            if (pos >= s.Length) return false;
            bool hit = ClassHit(Ranges, s[pos], re.IgnoreCase);
            return Neg ? !hit : hit;
        }
    }

    // ^ and $
    private sealed class NAnchorStart : Node
    {
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            if (pos == 0) return k(pos);
            if (re.Multiline && IsLineTerm(re._input[pos - 1])) return k(pos);
            return false;
        }
    }

    private sealed class NAnchorEnd : Node
    {
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            if (pos == re._input.Length) return k(pos);
            if (re.Multiline && IsLineTerm(re._input[pos])) return k(pos);
            return false;
        }
    }

    // \b and \B
    private sealed class NWordBound : Node
    {
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            string s = re._input;
            bool left = pos > 0 && IsWord(s[pos - 1]);
            bool right = pos < s.Length && IsWord(s[pos]);
            if (left != right) return k(pos);
            return false;
        }
    }

    private sealed class NNotWordBound : Node
    {
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            string s = re._input;
            bool left = pos > 0 && IsWord(s[pos - 1]);
            bool right = pos < s.Length && IsWord(s[pos]);
            if (left == right) return k(pos);
            return false;
        }
    }

    // Concatenation (0 items matches the empty string).
    private sealed class NConcat : Node
    {
        public List<Node> Items;
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            return MatchList(re, 0, pos, k);
        }
        private bool MatchList(JsRegExp re, int idx, int pos, Cont k)
        {
            if (idx >= Items.Count) return k(pos);
            int i = idx;
            return re.MatchNode(Items[i], pos, delegate(int p) { return MatchList(re, i + 1, p, k); });
        }
    }

    // Alternation: leftmost-first (first branch preferred), as in JS.
    private sealed class NAlt : Node
    {
        public List<Node> Alts;
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            for (int i = 0; i < Alts.Count; i++)
                if (re.MatchNode(Alts[i], pos, k)) return true;
            return false;
        }
    }

    private sealed class NRepeat : Node
    {
        public Node Body;
        public int Min;
        public int Max;        // int.MaxValue = unlimited
        public bool Greedy;
        public bool SimpleBody;
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            if (SimpleBody) return re.MatchSimpleRepeat(Body, pos, Min, Max, Greedy, k);
            return re.MatchRepeat(Body, 0, pos, Min, Max, Greedy, k);
        }
    }

    // Capturing group (Index is 1-based; slot 0 is the whole match).
    private sealed class NCapture : Node
    {
        public int Index;
        public string Name;
        public Node Body;
        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            int i = Index;
            int[] st = re._starts;
            int[] en = re._ends;
            int oldS = st[i];
            int oldE = en[i];
            st[i] = pos;
            bool ok = re.MatchNode(Body, pos, delegate(int p)
            {
                en[i] = p;
                if (k(p)) return true;
                en[i] = oldE;
                return false;
            });
            if (!ok) { st[i] = oldS; en[i] = oldE; return false; }
            return true;
        }
    }

    // Lookahead (zero-width) and lookbehind (bounded backwards scan).
    private sealed class NLook : Node
    {
        public Node Body;
        public bool Positive;
        public bool Behind;
        public int MaxLen;   // -1 = unbounded (behind only)

        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            if (!Behind)
            {
                if (Positive)
                {
                    // Successful lookahead keeps its captures (as in JS).
                    return re.MatchNode(Body, pos, delegate(int p) { return k(pos); });
                }
                int[] snap = re.SaveCaps();
                if (re.MatchNode(Body, pos, delegate(int p) { return true; }))
                {
                    re.RestoreCaps(snap);   // a matched negative lookahead must not leak captures
                    return false;
                }
                re.RestoreCaps(snap);
                return k(pos);
            }

            // Lookbehind: try start positions s so that Body matches [s,pos).
            // Ascending s prefers the longest preceding match, which is what
            // V8 does (verified: /(?<=(ab|b))/ on "abc" captures "ab").
            int low = 0;
            if (MaxLen >= 0 && MaxLen < pos) low = pos - MaxLen;

            if (Positive)
            {
                for (int s = low; s <= pos; s++)
                {
                    if (re.MatchNode(Body, s, delegate(int p) { return p == pos ? k(pos) : false; }))
                        return true;
                    if (re._aborted) return false;
                }
                return false;
            }
            else
            {
                int[] snap = re.SaveCaps();
                for (int s = low; s <= pos; s++)
                {
                    if (re.MatchNode(Body, s, delegate(int p) { return p == pos; }))
                    {
                        re.RestoreCaps(snap);
                        return false;
                    }
                    if (re._aborted) { re.RestoreCaps(snap); return false; }
                }
                re.RestoreCaps(snap);
                return k(pos);
            }
        }
    }

    // Backreference (numeric or named), or its literal fallback.
    // IsBackref/Val/Text are finalized in the parser's second pass.
    private sealed class NBackref : Node
    {
        public int Val;          // group number when IsBackref
        public string Name;      // set for \k<name>
        public string Digits;    // raw digits for numeric backrefs
        public bool IsBackref;
        public string Text;      // literal text when not a backreference

        public override bool Match(JsRegExp re, int pos, Cont k)
        {
            string s = re._input;
            if (!IsBackref)
            {
                string t = Text;
                if (t == null || pos + t.Length > s.Length) return false;
                for (int i = 0; i < t.Length; i++)
                    if (!CharEq(s[pos + i], t[i], re.IgnoreCase)) return false;
                return k(pos + t.Length);
            }

            int st = re._starts[Val];
            int en = re._ends[Val];
            // A backreference to a group that did not participate matches
            // the empty string (this is what V8 does: /(x)?\1y/ matches "y").
            if (st < 0 || en < st) return k(pos);
            int n = en - st;
            if (pos + n > s.Length) return false;
            for (int i = 0; i < n; i++)
                if (!CharEq(s[pos + i], s[st + i], re.IgnoreCase)) return false;
            return k(pos + n);
        }
    }
}
