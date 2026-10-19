using System;
using System.Collections.Generic;
using System.Text;

// ===========================================================================
// A hand-rolled CSS engine: full selector matching (descendant/child/sibling
// combinators, attribute selectors, structural pseudo-classes and
// pseudo-elements), specificity-based cascade, inheritance, @media queries,
// and a broad set of text/box properties. Written for Cosmos: no external
// dependencies, no reflection, allocation-conscious.
//
// Approximations (documented): float/absolute positioning are parsed but laid
// out as normal flow; % lengths resolve against the layout width; border-radius
// is parsed but rendered square; rgba() alpha is pre-blended onto white.
// ===========================================================================

// ---------------------------------------------------------------------------
// Computed style for one element
// ---------------------------------------------------------------------------

public enum CssDisplay : byte { Inline, Block, InlineBlock, ListItem, None, Flex, Table, TableRow, TableCell, TableCaption }
public enum CssAlign : byte { Left, Center, Right, Justify }
public enum CssVAlign : byte { Baseline, Top, Middle, Bottom, Super, Sub }
public enum CssTransform : byte { None, Upper, Lower, Capitalize }
public enum CssWhite : byte { Normal, Pre, PreWrap, PreLine, Nowrap }
public enum CssPos : byte { Static, Relative, Absolute, Fixed }
public enum CssFloat : byte { None, Left, Right }
public enum CssClear : byte { None, Left, Right, Both }
public enum CssBorder : byte { None, Solid, Dashed, Dotted, Double, Groove, Ridge, Inset, Outset }
public enum CssRepeat : byte { Repeat, NoRepeat, RepeatX, RepeatY }
public enum CssVis : byte { Visible, Hidden, Collapse }
public enum CssOverflow : byte { Visible, Hidden, Scroll, Auto }
public enum CssBox : byte { Content, Border }
public enum CssListStyle : byte
{
    Disc, Circle, Square, Decimal, DecimalLeadingZero, LowerAlpha, UpperAlpha,
    LowerRoman, UpperRoman, None
}

public sealed class CssComputed
{
    // box model / flow
    public CssDisplay Display;
    public CssPos Position;
    public CssFloat Float;
    public CssClear Clear;
    public CssBox BoxSizing;

    // text
    public bool HasColor; public uint Color;
    public int FontSize;                 // px
    public int FontWeight;               // 100..900
    public bool Italic;
    public bool Underline, LineThrough, Overline;
    public CssAlign TextAlign;
    public CssTransform TextTransform;
    public CssWhite WhiteSpace;
    public float LineHeight;             // multiplier (<=0 => normal)
    public int LetterSpacing, WordSpacing, TextIndent;
    public CssVAlign VerticalAlign;

    // box spacing
    public int MarginT, MarginB, MarginL, MarginR;
    public int PadT, PadB, PadL, PadR;

    // borders (per side)
    public CssBorder BorderTT; public uint BorderTC; public int BorderTW;
    public CssBorder BorderBB; public uint BorderBC; public int BorderBW;
    public CssBorder BorderLL; public uint BorderLC; public int BorderLW;
    public CssBorder BorderRR; public uint BorderRC; public int BorderRW;
    public int BorderRadius;

    // background
    public bool HasBg; public uint BgColor;
    public string BgImage; public CssRepeat BgRepeat; public string BgPos;

    // sizing
    public bool HasW, HasH; public int W, H;
    public int MinW, MaxW, MinH, MaxH;

    // paint
    public CssVis Visibility;
    public float Opacity;                // 0..1
    public CssOverflow OverflowX, OverflowY;

    // list
    public CssListStyle ListStyleType;

    // pseudo-element this style targets (null for the element itself)
    public string PseudoElement;

    // ---- construction ----------------------------------------------------

    public static CssComputed Initial()
    {
        CssComputed c = new CssComputed();
        c.Display = CssDisplay.Inline;
        c.Position = CssPos.Static;
        c.Float = CssFloat.None;
        c.BoxSizing = CssBox.Content;
        c.HasColor = false;
        c.FontSize = 13;
        c.FontWeight = 400;
        c.TextAlign = CssAlign.Left;
        c.TextTransform = CssTransform.None;
        c.WhiteSpace = CssWhite.Normal;
        c.LineHeight = 0;
        c.VerticalAlign = CssVAlign.Baseline;
        c.BgRepeat = CssRepeat.Repeat;
        c.BgPos = "0% 0%";
        c.MinW = 0; c.MaxW = int.MaxValue; c.MinH = 0; c.MaxH = int.MaxValue;
        c.Visibility = CssVis.Visible;
        c.Opacity = 1.0f;
        c.OverflowX = c.OverflowY = CssOverflow.Visible;
        c.ListStyleType = CssListStyle.Disc;
        return c;
    }

    public CssComputed Clone()
    {
        CssComputed c = (CssComputed)MemberwiseClone();
        return c;
    }

    /// <summary>Copy only the inherited properties from the parent computed style.</summary>
    public void InheritFrom(CssComputed parent)
    {
        if (parent == null) return;
        if (parent.HasColor) { HasColor = true; Color = parent.Color; }
        FontSize = parent.FontSize;
        FontWeight = parent.FontWeight;
        Italic = parent.Italic;
        Underline = parent.Underline; LineThrough = parent.LineThrough; Overline = parent.Overline;
        TextAlign = parent.TextAlign;
        TextTransform = parent.TextTransform;
        WhiteSpace = parent.WhiteSpace;
        LineHeight = parent.LineHeight;
        LetterSpacing = parent.LetterSpacing; WordSpacing = parent.WordSpacing; TextIndent = parent.TextIndent;
        Visibility = parent.Visibility;
        ListStyleType = parent.ListStyleType;
    }

    public bool IsBlockLevel
    {
        get
        {
            return Display == CssDisplay.Block || Display == CssDisplay.ListItem ||
                   Display == CssDisplay.Flex || Display == CssDisplay.Table ||
                   Display == CssDisplay.TableRow || Display == CssDisplay.TableCell;
        }
    }
}

// ---------------------------------------------------------------------------
// Declarations
// ---------------------------------------------------------------------------

public sealed class CssDecl
{
    public string Prop;      // lowercase
    public string Value;     // raw value text (trimmed)
    public bool Important;
    public int Order;        // source order within the sheet
}

// ---------------------------------------------------------------------------
// Selectors
// ---------------------------------------------------------------------------

public enum CssComb : byte { None, Descendant, Child, Adjacent, General }

public enum CssCondKind : byte
{
    Id, Class, HasAttr, AttrEq, AttrPrefix, AttrSuffix, AttrContains, AttrWord, AttrDash,
    FirstChild, LastChild, OnlyChild, NthChild, FirstOfType, LastOfType, NthOfType,
    Not, Empty, Link, Root
}

public sealed class CssCond
{
    public CssCondKind Kind;
    public string Str;      // id/class/attr-name/pseudo arg
    public string Val;      // attribute comparison value
    public int A, B;        // for an+b
    public CssSelector Inner;// for :not()
}

public sealed class CssCompound
{
    public string Tag;                       // null or "*" = any
    public List<CssCond> Conds;              // may be null

    public void Add(CssCond c)
    {
        if (Conds == null) Conds = new List<CssCond>();
        Conds.Add(c);
    }
}

public sealed class CssSelector
{
    public List<CssCompound> Compounds = new List<CssCompound>();
    public List<CssComb> Combinators = new List<CssComb>(); // Combinators[i] joins Compounds[i] -> [i+1]
    public string PseudoElement;            // "::before", "::first-letter", ...
    public CssSpecificity Spec;

    public void SetSpec()
    {
        int a = 0, b = 0, c = 0;
        for (int i = 0; i < Compounds.Count; i++)
        {
            CssCompound cp = Compounds[i];
            if (cp.Tag != null && cp.Tag != "*") c++;
            if (cp.Conds == null) continue;
            for (int j = 0; j < cp.Conds.Count; j++)
            {
                CssCond cd = cp.Conds[j];
                switch (cd.Kind)
                {
                    case CssCondKind.Id: a++; break;
                    case CssCondKind.Class:
                    case CssCondKind.HasAttr:
                    case CssCondKind.AttrEq:
                    case CssCondKind.AttrPrefix:
                    case CssCondKind.AttrSuffix:
                    case CssCondKind.AttrContains:
                    case CssCondKind.AttrWord:
                    case CssCondKind.AttrDash:
                    case CssCondKind.FirstChild:
                    case CssCondKind.LastChild:
                    case CssCondKind.OnlyChild:
                    case CssCondKind.NthChild:
                    case CssCondKind.FirstOfType:
                    case CssCondKind.LastOfType:
                    case CssCondKind.NthOfType:
                    case CssCondKind.Empty:
                    case CssCondKind.Link:
                    case CssCondKind.Root:
                        b++; break;
                    case CssCondKind.Not:
                        if (cd.Inner != null) { a += cd.Inner.Spec.A; b += cd.Inner.Spec.B; c += cd.Inner.Spec.C; }
                        break;
                }
            }
        }
        Spec = new CssSpecificity(a, b, c);
    }
}

public struct CssSpecificity : IComparable<CssSpecificity>
{
    public int A, B, C;
    public CssSpecificity(int a, int b, int c) { A = a; B = b; C = c; }

    public int CompareTo(CssSpecificity other)
    {
        if (A != other.A) return A - other.A;
        if (B != other.B) return B - other.B;
        return C - other.C;
    }
}

public sealed class CssRule
{
    public List<CssSelector> Selectors;
    public List<CssDecl> Decls;
    public CssMedia Media;   // null => always applies
}

public sealed class CssMedia
{
    public string Type;                 // "all", "screen", "print" (null = any)
    public List<CssMediaFeature> Features;
    public bool Negated;

    public bool Matches(int viewportW, int viewportH)
    {
        if (Type != null && Type != "all" && Type != "screen") return false; // only screen/all render
        if (Features == null) return true;
        for (int i = 0; i < Features.Count; i++)
        {
            if (!Features[i].Matches(viewportW, viewportH)) return false;
        }
        return true;
    }
}

public struct CssMediaFeature
{
    public string Name;   // "min-width", "max-width", "orientation", ...
    public string Str;    // for orientation etc.
    public int Val;       // for lengths (px)

    public bool Matches(int w, int h)
    {
        switch (Name)
        {
            case "min-width": return w >= Val;
            case "max-width": return w <= Val;
            case "min-height": return h >= Val;
            case "max-height": return h <= Val;
            case "width": return w == Val;
            case "height": return h == Val;
            case "orientation": return Str == "landscape" ? w >= h : h > w;
            case "min-resolution": case "max-resolution": case "resolution": return true; // ignore
            case "prefers-color-scheme": return Str == "light"; // we render light
            case "min-aspect-ratio": case "max-aspect-ratio": return true; // ignore
            default: return true; // unknown feature: assume match
        }
    }
}

// ---------------------------------------------------------------------------
// The stylesheet
// ---------------------------------------------------------------------------

public sealed class CssSheet
{
    public List<CssRule> Rules = new List<CssRule>();

    // ---- parsing ---------------------------------------------------------

    public static CssSheet Parse(string css)
    {
        CssSheet sheet = new CssSheet();
        if (string.IsNullOrEmpty(css)) return sheet;
        int order = 0;
        ParseBlock(sheet, css, 0, css.Length, null, ref order);
        return sheet;
    }

    // Parse rules in [start,end). `media` (if non-null) applies to every rule found.
    private static void ParseBlock(CssSheet sheet, string css, int start, int end, CssMedia media, ref int order)
    {
        int i = start;
        while (i < end)
        {
            i = SkipWs(css, i, end);
            if (i >= end) break;
            char c = css[i];

            // comments
            if (c == '/' && i + 1 < end && css[i + 1] == '*')
            {
                int ce = css.IndexOf("*/", i + 2, end - i - 2);
                i = ce < 0 ? end : ce + 2;
                continue;
            }

            // at-rules
            if (c == '@')
            {
                i = ParseAtRule(sheet, css, i, end, media, ref order);
                continue;
            }

            // qualified rule: selectors { decls }
            int brace = FindBlockStart(css, i, end);
            if (brace < 0) break;
            string selText = css.Substring(i, brace - i).Trim();
            int close = MatchBrace(css, brace, end);
            if (close < 0) close = end;
            string body = css.Substring(brace + 1, Math.Max(0, close - brace - 1));

            List<CssSelector> sels = ParseSelectorList(selText);
            if (sels != null && sels.Count > 0)
            {
                CssRule rule = new CssRule();
                rule.Selectors = sels;
                rule.Decls = ParseDecls(body, ref order);
                rule.Media = media;
                if (rule.Decls.Count > 0) sheet.Rules.Add(rule);
            }
            i = close + 1;
        }
    }

    private static int ParseAtRule(CssSheet sheet, string css, int i, int end, CssMedia outerMedia, ref int order)
    {
        int j = i + 1;
        StringBuilder name = new StringBuilder();
        while (j < end && IsIdentChar(css[j])) { name.Append(css[j]); j++; }
        string at = name.ToString();

        // skip whitespace before the prelude end
        int preludeStart = j;
        int brace = FindBlockStart(css, j, end);
        int semi = css.IndexOf(';', j, Math.Min(end, (brace < 0 ? end : brace)) - j);
        if (brace < 0 || (semi >= 0 && semi < brace))
        {
            // statement at-rule (@import, @charset) -> ignore (we don't fetch imports here)
            return semi < 0 ? end : semi + 1;
        }

        string prelude = css.Substring(preludeStart, brace - preludeStart).Trim();
        int close = MatchBrace(css, brace, end);
        if (close < 0) close = end;
        string body = css.Substring(brace + 1, Math.Max(0, close - brace - 1));

        if (at == "media")
        {
            CssMedia m = ParseMediaQuery(prelude);
            // combine with any enclosing media (we only support top-level, so outer usually null)
            if (outerMedia != null && m != null) { /* keep inner */ }
            ParseBlock(sheet, body, 0, body.Length, m, ref order);
        }
        else if (at == "supports")
        {
            // treat as always-true (we support most of what pages feature-test)
            ParseBlock(sheet, body, 0, body.Length, outerMedia, ref order);
        }
        // @font-face, @keyframes, @page, @import: parsed away / ignored
        return close + 1;
    }

    private static CssMedia ParseMediaQuery(string prelude)
    {
        CssMedia m = new CssMedia();
        m.Features = new List<CssMediaFeature>();
        List<string> parts = SplitTop(prelude, ',');
        // We collapse the comma list into a single media (first clause) for simplicity:
        // match if ANY clause matches. Represent as multiple features ORed is complex; we
        // evaluate by trying each clause. Store the first clause that has a type.
        // For correctness with ORs we store clauses in Features via a tiny wrapper:
        m.Type = null;
        CssMediaClause first = null;
        List<CssMediaClause> clauses = new List<CssMediaClause>();
        for (int i = 0; i < parts.Count; i++)
        {
            CssMediaClause cl = ParseMediaClause(parts[i]);
            if (cl != null) clauses.Add(cl);
        }
        if (clauses.Count == 0) return m;
        // store as OR: we keep them on the CssMedia via Features trick is ugly; instead
        // we set Type/Features from the first clause and rely on Matches handling clauses.
        first = clauses[0];
        m.Type = first.Type;
        m.Negated = first.Negated;
        for (int i = 0; i < first.Features.Count; i++) m.Features.Add(first.Features[i]);
        // NOTE: additional comma clauses beyond the first are approximated (rare on pages).
        return m;
    }

    private sealed class CssMediaClause
    {
        public string Type;
        public bool Negated;
        public List<CssMediaFeature> Features = new List<CssMediaFeature>();
    }

    private static CssMediaClause ParseMediaClause(string text)
    {
        CssMediaClause cl = new CssMediaClause();
        string s = text.Trim();
        if (s.Length == 0) { cl.Type = "all"; return cl; }
        int i = 0;
        if (s.Length > 4 && s.Substring(0, 4) == "not") { cl.Negated = true; i = 4; }
        while (i < s.Length && s[i] == ' ') i++;
        // type
        int ts = i;
        while (i < s.Length && IsIdentChar(s[i])) i++;
        string first = s.Substring(ts, i - ts).ToLowerInvariant();
        if (first == "only") { while (i < s.Length && s[i] == ' ') i++; ts = i; while (i < s.Length && IsIdentChar(s[i])) i++; first = s.Substring(ts, i - ts).ToLowerInvariant(); }
        if (first == "all" || first == "screen" || first == "print" || first == "speech") cl.Type = first;
        else if (first.StartsWith("min-") || first.StartsWith("max-") || first == "orientation" || first == "prefers-color-scheme") { /* feature without type */ i = ts; }
        else cl.Type = "all";
        // features
        while (i < s.Length)
        {
            while (i < s.Length && s[i] == ' ') i++;
            if (i + 3 <= s.Length && s.Substring(i, 3) == "and") i += 3;
            while (i < s.Length && s[i] == ' ') i++;
            if (i >= s.Length) break;
            if (s[i] != '(') break;
            int pe = s.IndexOf(')', i);
            if (pe < 0) break;
            string inner = s.Substring(i + 1, pe - i - 1).Trim();
            int colon = inner.IndexOf(':');
            if (colon > 0)
            {
                CssMediaFeature f = new CssMediaFeature();
                f.Name = inner.Substring(0, colon).Trim().ToLowerInvariant();
                string val = inner.Substring(colon + 1).Trim();
                if (CssValue.TryLengthPx(val, 1, 0, out int px)) f.Val = px;
                else f.Str = val.ToLowerInvariant();
                cl.Features.Add(f);
            }
            i = pe + 1;
        }
        if (cl.Type == null && cl.Features.Count == 0) cl.Type = "all";
        return cl;
    }

    // ---- selectors -------------------------------------------------------

    // ---- public query API (used by the DOM's querySelector/All) ----------

    /// <summary>Parse a selector list (comma-separated) for querying.</summary>
    public static List<CssSelector> ParseSelectors(string text)
    {
        return ParseSelectorList(text);
    }

    /// <summary>First matching descendant of <paramref name="root"/> (document order,
    /// excluding root itself, per querySelector semantics). Null if none.</summary>
    public static DomNode QuerySelector(DomNode root, List<CssSelector> sels)
    {
        if (root == null || sels == null || sels.Count == 0) return null;
        return QueryOne(root.FirstChild, sels);
    }

    /// <summary>All matching descendants of <paramref name="root"/> (document order)
    /// appended to <paramref name="outNodes"/>.</summary>
    public static void QuerySelectorAll(DomNode root, List<CssSelector> sels, List<DomNode> outNodes)
    {
        if (root == null || sels == null || sels.Count == 0 || outNodes == null) return;
        QueryAll(root.FirstChild, sels, outNodes);
    }

    private static DomNode QueryOne(DomNode node, List<CssSelector> sels)
    {
        DomNode n = node;
        while (n != null)
        {
            if (n.IsElement)
            {
                if (MatchesAny(n, sels)) return n;
                DomNode hit = QueryOne(n.FirstChild, sels);
                if (hit != null) return hit;
            }
            n = n.Next;
        }
        return null;
    }

    private static void QueryAll(DomNode node, List<CssSelector> sels, List<DomNode> outNodes)
    {
        DomNode n = node;
        while (n != null)
        {
            if (n.IsElement)
            {
                if (MatchesAny(n, sels)) outNodes.Add(n);
                QueryAll(n.FirstChild, sels, outNodes);
            }
            n = n.Next;
        }
    }

    private static bool MatchesAny(DomNode el, List<CssSelector> sels)
    {
        for (int i = 0; i < sels.Count; i++)
            if (Matches(sels[i], el)) return true;
        return false;
    }

    private static List<CssSelector> ParseSelectorList(string text)
    {
        List<CssSelector> result = new List<CssSelector>();
        List<string> parts = SplitTop(text, ',');
        for (int i = 0; i < parts.Count; i++)
        {
            CssSelector sel = ParseSelector(parts[i].Trim());
            if (sel != null) result.Add(sel);
        }
        return result;
    }

    private static CssSelector ParseSelector(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        CssSelector sel = new CssSelector();
        int i = 0;
        CssComb pend = CssComb.None;
        while (i < s.Length)
        {
            i = SkipWs(s, i, s.Length);
            if (i >= s.Length) break;

            // combinator
            char c = s[i];
            if (c == '>' || c == '+' || c == '~')
            {
                pend = c == '>' ? CssComb.Child : (c == '+' ? CssComb.Adjacent : CssComb.General);
                i++;
                continue;
            }

            CssCompound cp = ParseCompound(s, ref i);
            if (cp == null) { i++; continue; }

            if (sel.Compounds.Count > 0) sel.Combinators.Add(pend == CssComb.None ? CssComb.Descendant : pend);
            else if (pend != CssComb.None) { /* leading combinator: ignore */ }
            sel.Compounds.Add(cp);
            pend = CssComb.None;

            // pseudo-element at the end
            if (i + 1 < s.Length && s[i] == ':' && s[i + 1] == ':')
            {
                int ps = i + 2;
                int pe = ps;
                while (pe < s.Length && IsIdentChar(s[pe])) pe++;
                sel.PseudoElement = s.Substring(ps, pe - ps).ToLowerInvariant();
                i = pe;
            }
        }
        if (sel.Compounds.Count == 0) return null;
        sel.SetSpec();
        return sel;
    }

    private static CssCompound ParseCompound(string s, ref int i)
    {
        CssCompound cp = new CssCompound();
        bool any = false;
        while (i < s.Length)
        {
            char c = s[i];
            if (c == ' ' || c == '>' || c == '+' || c == '~') break;

            if (c == '*') { cp.Tag = "*"; i++; any = true; continue; }

            if (c == '.')
            {
                int st = ++i;
                while (i < s.Length && IsIdentChar(s[i])) i++;
                if (i > st) { CssCond cd = new CssCond { Kind = CssCondKind.Class, Str = s.Substring(st, i - st) }; cp.Add(cd); any = true; }
                continue;
            }

            if (c == '#')
            {
                int st = ++i;
                while (i < s.Length && IsIdentChar(s[i])) i++;
                if (i > st) { CssCond cd = new CssCond { Kind = CssCondKind.Id, Str = s.Substring(st, i - st) }; cp.Add(cd); any = true; }
                continue;
            }

            if (c == '[')
            {
                int close = s.IndexOf(']', i);
                if (close < 0) break;
                string inner = s.Substring(i + 1, close - i - 1).Trim();
                i = close + 1;
                CssCond cd = ParseAttrCond(inner);
                if (cd != null) { cp.Add(cd); any = true; }
                continue;
            }

            if (c == ':')
            {
                bool dbl = i + 1 < s.Length && s[i + 1] == ':';
                i = dbl ? i + 2 : i + 1;
                int st = i;
                while (i < s.Length && IsIdentChar(s[i])) i++;
                string pseudo = s.Substring(st, i - st).ToLowerInvariant();
                string arg = null;
                if (i < s.Length && s[i] == '(')
                {
                    int depth = 1, a = i + 1;
                    while (a < s.Length && depth > 0)
                    {
                        if (s[a] == '(') depth++;
                        else if (s[a] == ')') depth--;
                        a++;
                    }
                    arg = s.Substring(i + 1, Math.Max(0, a - i - 2));
                    i = a;
                }
                CssCond cd = ParsePseudoCond(pseudo, arg, dbl);
                if (cd != null) { cp.Add(cd); any = true; }
                continue;
            }

            if (IsIdentStart(c))
            {
                int st = i;
                while (i < s.Length && IsIdentChar(s[i])) i++;
                cp.Tag = s.Substring(st, i - st).ToLowerInvariant();
                any = true;
                continue;
            }

            i++; // unknown char, skip
        }
        return any ? cp : null;
    }

    private static CssCond ParseAttrCond(string inner)
    {
        if (string.IsNullOrEmpty(inner)) return null;
        string name = inner, op = null, val = "";
        // find operator
        string[] ops = { "^=", "$=", "*=", "~=", "|=", "=" };
        for (int k = 0; k < ops.Length; k++)
        {
            int oi = inner.IndexOf(ops[k]);
            if (oi > 0) { name = inner.Substring(0, oi).Trim(); op = ops[k]; val = inner.Substring(oi + ops[k].Length).Trim(); break; }
        }
        name = name.ToLowerInvariant();
        val = Unquote(val);
        CssCondKind kind;
        if (op == null) kind = CssCondKind.HasAttr;
        else if (op == "=") kind = CssCondKind.AttrEq;
        else if (op == "^=") kind = CssCondKind.AttrPrefix;
        else if (op == "$=") kind = CssCondKind.AttrSuffix;
        else if (op == "*=") kind = CssCondKind.AttrContains;
        else if (op == "~=") kind = CssCondKind.AttrWord;
        else kind = CssCondKind.AttrDash;
        return new CssCond { Kind = kind, Str = name, Val = val };
    }

    private static CssCond ParsePseudoCond(string pseudo, string arg, bool isElement)
    {
        switch (pseudo)
        {
            case "first-child": return new CssCond { Kind = CssCondKind.FirstChild };
            case "last-child": return new CssCond { Kind = CssCondKind.LastChild };
            case "only-child": return new CssCond { Kind = CssCondKind.OnlyChild };
            case "first-of-type": return new CssCond { Kind = CssCondKind.FirstOfType };
            case "last-of-type": return new CssCond { Kind = CssCondKind.LastOfType };
            case "only-of-type": return new CssCond { Kind = CssCondKind.OnlyChild };
            case "empty": return new CssCond { Kind = CssCondKind.Empty };
            case "root": return new CssCond { Kind = CssCondKind.Root };
            case "link": case "any-link": case "visited": return new CssCond { Kind = CssCondKind.Link };
            case "hover": case "focus": case "active": case "focus-within": case "focus-visible":
                return null; // no interactivity: never match
            case "checked": case "disabled": case "enabled": case "required": case "optional":
            case "read-only": case "read-write":
                return null;
            case "not":
                {
                    CssSelector inner = ParseSelector(arg != null ? arg.Trim() : "");
                    if (inner == null) return null;
                    return new CssCond { Kind = CssCondKind.Not, Inner = inner };
                }
            case "nth-child": case "nth-of-type":
                {
                    CssCond cd = ParseNth(arg != null ? arg.Trim() : "");
                    if (cd == null) return null;
                    cd.Kind = pseudo == "nth-child" ? CssCondKind.NthChild : CssCondKind.NthOfType;
                    return cd;
                }
            default:
                return null; // unknown pseudo: never match (safer than always matching)
        }
    }

    private static CssCond ParseNth(string s)
    {
        s = s.ToLowerInvariant().Replace(" ", "");
        if (s == "odd") return new CssCond { A = 2, B = 1 };
        if (s == "even") return new CssCond { A = 2, B = 0 };
        int sign = 1, a = 0, b = 0; bool hasA = false;
        int i = 0;
        // parse an+b
        if (s.StartsWith("n"))
        {
            a = 1; i = 1;
            hasA = true;
        }
        else
        {
            int ns = s.IndexOf('n');
            if (ns >= 0)
            {
                string anum = s.Substring(0, ns);
                if (anum == "+" ) a = 1;
                else if (anum == "-") a = -1;
                else { int.TryParse(anum, out a); }
                hasA = true;
                i = ns + 1;
            }
            else
            {
                if (int.TryParse(s, out b)) return new CssCond { A = 0, B = b };
                return null;
            }
        }
        // remainder after 'n'
        if (i < s.Length)
        {
            if (s[i] == '+') { i++; int.TryParse(s.Substring(i), out b); }
            else if (s[i] == '-') { i++; if (int.TryParse(s.Substring(i), out int v)) b = -v; }
        }
        if (!hasA) return new CssCond { A = 0, B = b };
        return new CssCond { A = a, B = b };
    }

    // ---- declarations ----------------------------------------------------

    private static List<CssDecl> ParseDecls(string body, ref int order)
    {
        List<CssDecl> list = new List<CssDecl>();
        int i = 0, len = body.Length;
        while (i < len)
        {
            i = SkipWs(body, i, len);
            if (i >= len) break;
            if (body[i] == '/' && i + 1 < len && body[i + 1] == '*')
            {
                int ce = body.IndexOf("*/", i + 2, len - i - 2);
                i = ce < 0 ? len : ce + 2;
                continue;
            }
            int colon = FindDeclColon(body, i, len);
            if (colon < 0) break;
            string prop = body.Substring(i, colon - i).Trim().ToLowerInvariant();

            int semi = FindDeclEnd(body, colon + 1, len);
            string value = body.Substring(colon + 1, semi - colon - 1).Trim();
            i = semi + 1;

            if (prop.Length == 0 || value.Length == 0) continue;

            bool important = false;
            int im = value.LastIndexOf('!');
            if (im >= 0 && im + 1 < value.Length && value.Substring(im + 1).Trim().ToLowerInvariant().StartsWith("important"))
            {
                important = true;
                value = value.Substring(0, im).Trim();
            }
            CssDecl d = new CssDecl();
            d.Prop = prop;
            d.Value = value;
            d.Important = important;
            d.Order = order++;
            list.Add(d);
        }
        return list;
    }

    private static int FindDeclColon(string s, int start, int end)
    {
        int depth = 0;
        for (int i = start; i < end; i++)
        {
            char c = s[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == '"' || c == '\'') { i = SkipString(s, i, end); }
            else if (c == ':' && depth == 0) return i;
            else if (c == ';' && depth == 0) return -1;
            else if (c == '{' && depth == 0) return -1;
        }
        return -1;
    }

    private static int FindDeclEnd(string s, int start, int end)
    {
        int depth = 0;
        for (int i = start; i < end; i++)
        {
            char c = s[i];
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == '"' || c == '\'') { i = SkipString(s, i, end); }
            else if (c == ';' && depth == 0) return i;
            else if (c == '}' && depth == 0) return i;
        }
        return end;
    }

    // ---- matching / cascade ----------------------------------------------

    /// <summary>
    /// Compute the style for an element: UA defaults -> inherited props from parent
    /// -> matched rules (sorted by specificity then source order, !important last) -> inline style.
    /// </summary>
    public CssComputed Compute(DomNode el, CssComputed parent, int viewportW, int viewportH)
    {
        CssComputed c = CssComputed.Initial();
        ApplyUaDefaults(c, el != null ? el.Tag : null);
        c.InheritFrom(parent);

        // gather matching declarations
        List<CssDecl> decls = new List<CssDecl>();
        List<CssSpecificity> specs = new List<CssSpecificity>();
        for (int r = 0; r < Rules.Count; r++)
        {
            CssRule rule = Rules[r];
            if (rule.Media != null && !rule.Media.Matches(viewportW, viewportH)) continue;
            for (int s = 0; s < rule.Selectors.Count; s++)
            {
                CssSelector sel = rule.Selectors[s];
                if (sel.PseudoElement != null) continue;   // handled separately
                if (!Matches(sel, el)) continue;
                for (int d = 0; d < rule.Decls.Count; d++)
                {
                    decls.Add(rule.Decls[d]);
                    specs.Add(sel.Spec);
                }
            }
        }

        SortDecls(decls, specs);
        for (int i = 0; i < decls.Count; i++) ApplyDecl(c, decls[i], viewportW);

        // inline style="" (highest author priority, still below !important)
        if (el != null && el.StyleAttr != null && el.StyleAttr.Length > 0)
        {
            int tmpOrder = 0;
            List<CssDecl> inline = ParseDecls(el.StyleAttr, ref tmpOrder);
            List<CssSpecificity> isp = new List<CssSpecificity>();
            CssSpecificity top = new CssSpecificity(1000, 0, 0); // beats everything
            for (int i = 0; i < inline.Count; i++) isp.Add(top);
            // keep inline !important above normal inline
            SortDecls(inline, isp);
            for (int i = 0; i < inline.Count; i++) ApplyDecl(c, inline[i], viewportW);
        }

        c.FontSize = Math.Max(6, c.FontSize);
        if (!c.HasColor) { c.Color = 0xFF000000; c.HasColor = true; } // default black
        if (c.Opacity < 0) c.Opacity = 0; else if (c.Opacity > 1) c.Opacity = 1;
        return c;
    }

    private static void SortDecls(List<CssDecl> decls, List<CssSpecificity> specs)
    {
        // stable insertion sort by (!important, specificity, order)
        for (int i = 1; i < decls.Count; i++)
        {
            CssDecl di = decls[i]; CssSpecificity si = specs[i];
            int j = i - 1;
            while (j >= 0 && Lower(decls[j], specs[j], di, si))
            {
                decls[j + 1] = decls[j]; specs[j + 1] = specs[j]; j--;
            }
            decls[j + 1] = di; specs[j + 1] = si;
        }
    }

    private static bool Lower(CssDecl a, CssSpecificity sa, CssDecl b, CssSpecificity sb)
    {
        if (a.Important != b.Important) return !a.Important; // important sorts later (higher)
        int cmp = sa.CompareTo(sb);
        if (cmp != 0) return cmp < 0;
        return a.Order < b.Order;
    }

    // ---- selector matching -----------------------------------------------

    public static bool Matches(CssSelector sel, DomNode el)
    {
        if (sel == null || el == null || !el.IsElement) return false;
        return MatchAt(sel, sel.Compounds.Count - 1, el);
    }

    private static bool MatchAt(CssSelector sel, int idx, DomNode node)
    {
        if (node == null || !node.IsElement) return false;
        if (!MatchCompound(sel.Compounds[idx], node)) return false;
        if (idx == 0) return true;

        CssComb comb = sel.Combinators[idx - 1];
        switch (comb)
        {
            case CssComb.Child:
                return MatchAt(sel, idx - 1, node.Parent);
            case CssComb.Descendant:
                for (DomNode a = node.Parent; a != null; a = a.Parent)
                    if (MatchAt(sel, idx - 1, a)) return true;
                return false;
            case CssComb.Adjacent:
                return MatchAt(sel, idx - 1, PrevElement(node));
            case CssComb.General:
                for (DomNode s = PrevElement(node); s != null; s = PrevElement(s))
                    if (MatchAt(sel, idx - 1, s)) return true;
                return false;
            default:
                return true;
        }
    }

    private static DomNode PrevElement(DomNode n)
    {
        if (n == null) return null;
        DomNode p = n.Prev;
        while (p != null && !p.IsElement) p = p.Prev;
        return p;
    }

    private static bool MatchCompound(CssCompound cp, DomNode el)
    {
        if (cp.Tag != null && cp.Tag != "*")
        {
            // allow namespaced-ish tags to match on the local name
            if (cp.Tag != el.Tag) return false;
        }
        if (cp.Conds == null) return true;
        for (int i = 0; i < cp.Conds.Count; i++)
        {
            if (!MatchCond(cp.Conds[i], el)) return false;
        }
        return true;
    }

    private static bool MatchCond(CssCond cd, DomNode el)
    {
        switch (cd.Kind)
        {
            case CssCondKind.Id:
                return el.Id != null && el.Id == cd.Str;

            case CssCondKind.Class:
                return HasClass(el, cd.Str);

            case CssCondKind.HasAttr:
                return el.GetAttr(cd.Str) != null;

            case CssCondKind.AttrEq:
                { string v = el.GetAttr(cd.Str); return v != null && v == cd.Val; }
            case CssCondKind.AttrPrefix:
                { string v = el.GetAttr(cd.Str); return v != null && v.StartsWith(cd.Val); }
            case CssCondKind.AttrSuffix:
                { string v = el.GetAttr(cd.Str); return v != null && EndsWith(v, cd.Val); }
            case CssCondKind.AttrContains:
                { string v = el.GetAttr(cd.Str); return v != null && v.IndexOf(cd.Val) >= 0; }
            case CssCondKind.AttrWord:
                {
                    string v = el.GetAttr(cd.Str);
                    if (v == null) return false;
                    List<string> toks = SplitWs(v);
                    for (int i = 0; i < toks.Count; i++) if (toks[i] == cd.Val) return true;
                    return false;
                }
            case CssCondKind.AttrDash:
                {
                    string v = el.GetAttr(cd.Str);
                    if (v == null) return false;
                    if (v == cd.Val) return true;
                    return v.Length > cd.Val.Length && v[cd.Val.Length] == '-' && v.Substring(0, cd.Val.Length) == cd.Val;
                }

            case CssCondKind.FirstChild:
                return FirstElementChild(el.Parent) == el;
            case CssCondKind.LastChild:
                return LastElementChild(el.Parent) == el;
            case CssCondKind.OnlyChild:
                return FirstElementChild(el.Parent) == el && LastElementChild(el.Parent) == el;
            case CssCondKind.Root:
                return el.Parent == null || el.Parent.Tag == "#document";

            case CssCondKind.NthChild:
                { int pos = ElementPosition(el); return MatchNth(cd.A, cd.B, pos); }
            case CssCondKind.NthOfType:
                { int pos = TypePosition(el); return MatchNth(cd.A, cd.B, pos); }
            case CssCondKind.FirstOfType:
                return TypePosition(el) == 1;
            case CssCondKind.LastOfType:
                return TypePosition(el) == LastTypePosition(el);

            case CssCondKind.Empty:
                {
                    DomNode n = el.FirstChild;
                    while (n != null)
                    {
                        if (n.IsElement) return false;
                        if (n.NodeType == DomNode.TextNode && n.Text != null && n.Text.Trim().Length > 0) return false;
                        n = n.Next;
                    }
                    return true;
                }

            case CssCondKind.Link:
                return el.Tag == "a" && el.GetAttr("href") != null;

            case CssCondKind.Not:
                return cd.Inner != null && !Matches(cd.Inner, el);
        }
        return true;
    }

    private static bool MatchNth(int a, int b, int pos)
    {
        if (pos <= 0) return false;
        if (a == 0) return pos == b;
        int diff = pos - b;
        if (diff == 0) return true;
        if (a > 0) return diff > 0 && diff % a == 0;
        return diff < 0 && (-diff) % (-a) == 0;
    }

    // Attribute conds store "name\u0001value" in Str when there's a comparison value.
    // These helpers split/return the parts.
    // (kept as extension-like statics to avoid extra fields)

    private static bool HasClass(DomNode el, string cls)
    {
        if (el.Classes == null) return false;
        for (int i = 0; i < el.Classes.Count; i++)
            if (el.Classes[i] == cls) return true;
        return false;
    }

    private static DomNode FirstElementChild(DomNode p)
    {
        if (p == null) return null;
        DomNode n = p.FirstChild;
        while (n != null && !n.IsElement) n = n.Next;
        return n;
    }

    private static DomNode LastElementChild(DomNode p)
    {
        if (p == null) return null;
        DomNode n = p.LastChild;
        while (n != null && !n.IsElement) n = n.Prev;
        return n;
    }

    private static int ElementPosition(DomNode el)
    {
        if (el == null || el.Parent == null) return 1;
        int pos = 0;
        DomNode n = el.Parent.FirstChild;
        while (n != null)
        {
            if (n.IsElement) pos++;
            if (n == el) return pos;
            n = n.Next;
        }
        return pos;
    }

    private static int TypePosition(DomNode el)
    {
        if (el == null || el.Parent == null) return 1;
        int pos = 0;
        DomNode n = el.Parent.FirstChild;
        while (n != null)
        {
            if (n.IsElement && n.Tag == el.Tag) pos++;
            if (n == el) return pos;
            n = n.Next;
        }
        return pos;
    }

    private static int LastTypePosition(DomNode el)
    {
        if (el == null || el.Parent == null) return 1;
        int pos = 0;
        DomNode n = el.Parent.FirstChild;
        while (n != null)
        {
            if (n.IsElement && n.Tag == el.Tag) pos++;
            n = n.Next;
        }
        return pos;
    }

    // ---- UA defaults -----------------------------------------------------

    public static void ApplyUaDefaults(CssComputed c, string tag)
    {
        if (tag == null) return;
        switch (tag)
        {
            case "html": case "body":
                c.Display = CssDisplay.Block;
                if (tag == "body") { c.MarginL = 8; c.MarginR = 8; c.MarginT = 8; c.MarginB = 8; }
                break;
            case "head": case "meta": case "link": case "title": case "script": case "style":
            case "noscript": case "template":
                c.Display = CssDisplay.None;
                break;
            case "div": case "section": case "article": case "aside": case "header": case "footer":
            case "nav": case "main": case "figure": case "figcaption": case "details": case "summary":
            case "fieldset": case "center": case "p": case "form":
            case "hgroup": case "search":
                c.Display = CssDisplay.Block;
                break;
            case "h1": c.Display = CssDisplay.Block; c.FontSize = 32; c.FontWeight = 700; c.MarginT = 19; c.MarginB = 19; break;
            case "h2": c.Display = CssDisplay.Block; c.FontSize = 24; c.FontWeight = 700; c.MarginT = 18; c.MarginB = 18; break;
            case "h3": c.Display = CssDisplay.Block; c.FontSize = 19; c.FontWeight = 700; c.MarginT = 17; c.MarginB = 17; break;
            case "h4": c.Display = CssDisplay.Block; c.FontSize = 16; c.FontWeight = 700; c.MarginT = 16; c.MarginB = 16; break;
            case "h5": c.Display = CssDisplay.Block; c.FontSize = 13; c.FontWeight = 700; c.MarginT = 15; c.MarginB = 15; break;
            case "h6": c.Display = CssDisplay.Block; c.FontSize = 11; c.FontWeight = 700; c.MarginT = 14; c.MarginB = 14; break;
            case "blockquote":
                c.Display = CssDisplay.Block; c.MarginL = 40; c.MarginR = 40; break;
            case "pre":
                c.Display = CssDisplay.Block; c.WhiteSpace = CssWhite.Pre; c.HasColor = true; c.Color = 0xFF7F0000; c.FontSize = 13; break;
            case "code": case "kbd": case "samp": case "tt":
                c.Italic = false; c.HasColor = true; c.Color = 0xFF7F0000; break;
            case "address":
                c.Display = CssDisplay.Block; c.Italic = true; break;
            case "ul": case "ol": case "menu":
                c.Display = CssDisplay.Block; c.MarginT = 12; c.MarginB = 12; c.MarginL = 25; c.ListStyleType = tag == "ol" ? CssListStyle.Decimal : CssListStyle.Disc; break;
            case "li":
                c.Display = CssDisplay.ListItem; break;
            case "dl": c.Display = CssDisplay.Block; c.MarginT = 12; c.MarginB = 12; break;
            case "dt": c.Display = CssDisplay.Block; c.FontWeight = 700; break;
            case "dd": c.Display = CssDisplay.Block; c.MarginL = 40; break;
            case "table": c.Display = CssDisplay.Table; c.MarginT = 12; c.MarginB = 12; break;
            case "tr": c.Display = CssDisplay.TableRow; break;
            case "td": case "th":
                c.Display = CssDisplay.TableCell; c.PadL = 4; c.PadR = 4; c.PadT = 2; c.PadB = 2;
                if (tag == "th") c.FontWeight = 700;
                break;
            case "caption": c.Display = CssDisplay.TableCaption; c.TextAlign = CssAlign.Center; break;
            case "thead": case "tbody": case "tfoot": case "colgroup": case "col":
                c.Display = CssDisplay.Block; break;
            case "hr":
                c.Display = CssDisplay.Block; c.MarginT = 12; c.MarginB = 12;
                c.BorderTT = CssBorder.Solid; c.BorderTW = 1; c.BorderTC = 0xFFBBBBBB;
                break;
            case "a":
                c.HasColor = true; c.Color = 0xFF0000FF; c.Underline = true; break;
            case "b": case "strong": c.FontWeight = 700; break;
            case "i": case "em": case "cite": case "var": c.Italic = true; break;
            case "u": case "ins": c.Underline = true; break;
            case "s": case "del": case "strike": c.LineThrough = true; break;
            case "small": c.FontSize = 11; break;
            case "big": c.FontSize = 16; break;
            case "mark": c.HasBg = true; c.BgColor = 0xFFFFFF00; break;
            case "sub": c.VerticalAlign = CssVAlign.Sub; c.FontSize = 11; break;
            case "sup": c.VerticalAlign = CssVAlign.Super; c.FontSize = 11; break;
            case "img":
                c.Display = CssDisplay.InlineBlock; break;
            case "br":
                c.Display = CssDisplay.Inline; break;
            case "button": case "input": case "select": case "textarea":
                c.HasBg = true; c.BgColor = 0xFFECECEC;
                c.BorderTT = CssBorder.Solid; c.BorderTW = 1; c.BorderTC = 0xFF888888;
                c.BorderBB = CssBorder.Solid; c.BorderBW = 1; c.BorderBC = 0xFF888888;
                c.BorderLL = CssBorder.Solid; c.BorderLW = 1; c.BorderLC = 0xFF888888;
                c.BorderRR = CssBorder.Solid; c.BorderRW = 1; c.BorderRC = 0xFF888888;
                c.PadT = 2; c.PadB = 2; c.PadL = 6; c.PadR = 6;
                break;
            case "svg": case "canvas": case "video": case "audio": case "iframe": case "embed":
            case "object": case "map": case "area":
                c.Display = CssDisplay.None; break;
            default:
                break;
        }
    }

    // ---- inline pseudo-element styles (::before/::after/::first-letter) ----

    /// <summary>Compute the style for a pseudo-element (::before/::after) of an element, if any.</summary>
    public CssComputed ComputePseudo(DomNode el, CssComputed elStyle, string pseudo, int vw, int vh, out string content)
    {
        content = null;
        CssComputed c = null;
        List<CssDecl> decls = new List<CssDecl>();
        List<CssSpecificity> specs = new List<CssSpecificity>();
        for (int r = 0; r < Rules.Count; r++)
        {
            CssRule rule = Rules[r];
            if (rule.Media != null && !rule.Media.Matches(vw, vh)) continue;
            for (int s = 0; s < rule.Selectors.Count; s++)
            {
                CssSelector sel = rule.Selectors[s];
                if (sel.PseudoElement == null) continue;
                if (sel.PseudoElement != pseudo) continue;
                if (!Matches(sel, el)) continue;
                for (int d = 0; d < rule.Decls.Count; d++)
                {
                    decls.Add(rule.Decls[d]); specs.Add(sel.Spec);
                }
            }
        }
        if (decls.Count == 0) return null;
        c = elStyle.Clone();
        c.PseudoElement = pseudo;
        SortDecls(decls, specs);
        for (int i = 0; i < decls.Count; i++)
        {
            if (decls[i].Prop == "content") content = Unquote(decls[i].Value);
            else ApplyDecl(c, decls[i], vw);
        }
        if (content == null) return null; // ::before/::after require content
        if (content == "none" || content == "normal") return null;
        return c;
    }

    // ---- property application --------------------------------------------

    private static void ApplyDecl(CssComputed c, CssDecl d, int vw)
    {
        string v = d.Value;
        switch (d.Prop)
        {
            case "display":
                {
                    string w = FirstWord(v);
                    if (w == "none") c.Display = CssDisplay.None;
                    else if (w == "block") c.Display = CssDisplay.Block;
                    else if (w == "inline") c.Display = CssDisplay.Inline;
                    else if (w == "inline-block") c.Display = CssDisplay.InlineBlock;
                    else if (w == "list-item") c.Display = CssDisplay.ListItem;
                    else if (w == "flex" || w == "inline-flex") c.Display = CssDisplay.Flex;
                    else if (w == "grid" || w == "inline-grid") c.Display = CssDisplay.Block;
                    else if (w == "table") c.Display = CssDisplay.Table;
                    else if (w == "table-row") c.Display = CssDisplay.TableRow;
                    else if (w == "table-cell") c.Display = CssDisplay.TableCell;
                    else if (w == "table-caption") c.Display = CssDisplay.TableCaption;
                    break;
                }
            case "visibility":
                {
                    string w = FirstWord(v);
                    if (w == "hidden") c.Visibility = CssVis.Hidden;
                    else if (w == "collapse") c.Visibility = CssVis.Collapse;
                    else c.Visibility = CssVis.Visible;
                    break;
                }
            case "opacity":
                {
                    if (CssValue.TryFloat(v, out float f)) c.Opacity = f;
                    break;
                }
            case "position":
                {
                    string w = FirstWord(v);
                    if (w == "relative") c.Position = CssPos.Relative;
                    else if (w == "absolute") c.Position = CssPos.Absolute;
                    else if (w == "fixed") c.Position = CssPos.Fixed;
                    else c.Position = CssPos.Static;
                    break;
                }
            case "float":
                {
                    string w = FirstWord(v);
                    if (w == "left") c.Float = CssFloat.Left;
                    else if (w == "right") c.Float = CssFloat.Right;
                    else c.Float = CssFloat.None;
                    break;
                }
            case "clear":
                {
                    string w = FirstWord(v);
                    if (w == "left") c.Clear = CssClear.Left;
                    else if (w == "right") c.Clear = CssClear.Right;
                    else if (w == "both") c.Clear = CssClear.Both;
                    else c.Clear = CssClear.None;
                    break;
                }
            case "box-sizing":
                c.BoxSizing = FirstWord(v) == "border-box" ? CssBox.Border : CssBox.Content;
                break;

            case "color":
                { if (CssValue.TryColor(v, vw, 0, out uint col)) { c.Color = col; c.HasColor = true; } break; }
            case "background": case "background-color":
                {
                    // "background" shorthand may carry image/position/repeat/color
                    if (d.Prop == "background-color")
                    {
                        if (CssValue.TryColor(v, vw, 0, out uint bg)) { c.HasBg = true; c.BgColor = bg; }
                        break;
                    }
                    ApplyBackgroundShorthand(c, v, vw);
                    break;
                }

            case "font-size":
                { if (CssValue.TryFontPx(v, c.FontSize, vw, out int px)) c.FontSize = px; break; }
            case "font-weight":
                {
                    string w = FirstWord(v);
                    if (w == "bold" || w == "bolder") c.FontWeight = 700;
                    else if (w == "normal") c.FontWeight = 400;
                    else if (w == "lighter") c.FontWeight = 300;
                    else { if (int.TryParse(w, out int n)) c.FontWeight = n; }
                    break;
                }
            case "font-style":
                c.Italic = FirstWord(v) == "italic" || FirstWord(v) == "oblique";
                break;
            case "font-family":
                // parsed and ignored (single bundled font)
                break;
            case "font":
                ApplyFontShorthand(c, v, vw);
                break;

            case "text-decoration":
            case "text-decoration-line":
                {
                    List<string> words = SplitWs(v);
                    for (int i = 0; i < words.Count; i++)
                    {
                        string w = words[i];
                        if (w == "underline") c.Underline = true;
                        else if (w == "line-through") c.LineThrough = true;
                        else if (w == "overline") c.Overline = true;
                        else if (w == "none") { c.Underline = false; c.LineThrough = false; c.Overline = false; }
                    }
                    break;
                }
            case "text-align":
                {
                    string w = FirstWord(v);
                    if (w == "center") c.TextAlign = CssAlign.Center;
                    else if (w == "right") c.TextAlign = CssAlign.Right;
                    else if (w == "justify") c.TextAlign = CssAlign.Justify;
                    else c.TextAlign = CssAlign.Left;
                    break;
                }
            case "text-transform":
                {
                    string w = FirstWord(v);
                    if (w == "uppercase") c.TextTransform = CssTransform.Upper;
                    else if (w == "lowercase") c.TextTransform = CssTransform.Lower;
                    else if (w == "capitalize") c.TextTransform = CssTransform.Capitalize;
                    else c.TextTransform = CssTransform.None;
                    break;
                }
            case "text-indent":
                { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.TextIndent = px; break; }
            case "letter-spacing":
                { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.LetterSpacing = px; break; }
            case "word-spacing":
                { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.WordSpacing = px; break; }
            case "vertical-align":
                {
                    string w = FirstWord(v);
                    if (w == "super") c.VerticalAlign = CssVAlign.Super;
                    else if (w == "sub") c.VerticalAlign = CssVAlign.Sub;
                    else if (w == "top") c.VerticalAlign = CssVAlign.Top;
                    else if (w == "middle") c.VerticalAlign = CssVAlign.Middle;
                    else if (w == "bottom") c.VerticalAlign = CssVAlign.Bottom;
                    else c.VerticalAlign = CssVAlign.Baseline;
                    break;
                }
            case "line-height":
                {
                    string w = FirstWord(v);
                    if (w == "normal") { c.LineHeight = 0; break; }
                    // unitless multiplier
                    if (w.Length > 0 && (w[0] >= '0' && w[0] <= '9' || w[0] == '.'))
                    {
                        if (CssValue.TryFloat(w, out float f)) c.LineHeight = f;
                        break;
                    }
                    if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px) && c.FontSize > 0) c.LineHeight = (float)px / c.FontSize;
                    break;
                }
            case "white-space":
                {
                    string w = FirstWord(v);
                    if (w == "pre") c.WhiteSpace = CssWhite.Pre;
                    else if (w == "pre-wrap") c.WhiteSpace = CssWhite.PreWrap;
                    else if (w == "pre-line") c.WhiteSpace = CssWhite.PreLine;
                    else if (w == "nowrap") c.WhiteSpace = CssWhite.Nowrap;
                    else c.WhiteSpace = CssWhite.Normal;
                    break;
                }
            case "overflow":
                {
                    string w = FirstWord(v);
                    CssOverflow o = w == "hidden" ? CssOverflow.Hidden : (w == "scroll" ? CssOverflow.Scroll : (w == "auto" ? CssOverflow.Auto : CssOverflow.Visible));
                    c.OverflowX = o; c.OverflowY = o;
                    break;
                }
            case "overflow-x": { string w = FirstWord(v); c.OverflowX = w == "hidden" ? CssOverflow.Hidden : (w == "scroll" ? CssOverflow.Scroll : (w == "auto" ? CssOverflow.Auto : CssOverflow.Visible)); break; }
            case "overflow-y": { string w = FirstWord(v); c.OverflowY = w == "hidden" ? CssOverflow.Hidden : (w == "scroll" ? CssOverflow.Scroll : (w == "auto" ? CssOverflow.Auto : CssOverflow.Visible)); break; }

            case "margin": ApplyMarginShorthand(c, v, c.FontSize, vw); break;
            case "margin-top": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.MarginT = px; break; }
            case "margin-bottom": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.MarginB = px; break; }
            case "margin-left": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.MarginL = px; break; }
            case "margin-right": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.MarginR = px; break; }

            case "padding": ApplyPaddingShorthand(c, v, c.FontSize, vw); break;
            case "padding-top": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadT = px; break; }
            case "padding-bottom": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadB = px; break; }
            case "padding-left": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadL = px; break; }
            case "padding-right": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadR = px; break; }

            case "border": ApplyBorderShorthand(c, v, c.FontSize, vw, 0); break;
            case "border-top": ApplyBorderShorthand(c, v, c.FontSize, vw, 1); break;
            case "border-bottom": ApplyBorderShorthand(c, v, c.FontSize, vw, 2); break;
            case "border-left": ApplyBorderShorthand(c, v, c.FontSize, vw, 3); break;
            case "border-right": ApplyBorderShorthand(c, v, c.FontSize, vw, 4); break;
            case "border-width": ApplyBorderWidthShorthand(c, v, c.FontSize, vw); break;
            case "border-color": ApplyBorderColorShorthand(c, v, vw); break;
            case "border-style": ApplyBorderStyleShorthand(c, v); break;
            case "border-top-width": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.BorderTW = px; if (c.BorderTT == CssBorder.None) c.BorderTT = CssBorder.Solid; } break; }
            case "border-bottom-width": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.BorderBW = px; if (c.BorderBB == CssBorder.None) c.BorderBB = CssBorder.Solid; } break; }
            case "border-left-width": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.BorderLW = px; if (c.BorderLL == CssBorder.None) c.BorderLL = CssBorder.Solid; } break; }
            case "border-right-width": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.BorderRW = px; if (c.BorderRR == CssBorder.None) c.BorderRR = CssBorder.Solid; } break; }
            case "border-top-color": { if (CssValue.TryColor(v, vw, 0, out uint col)) c.BorderTC = col; break; }
            case "border-bottom-color": { if (CssValue.TryColor(v, vw, 0, out uint col)) c.BorderBC = col; break; }
            case "border-left-color": { if (CssValue.TryColor(v, vw, 0, out uint col)) c.BorderLC = col; break; }
            case "border-right-color": { if (CssValue.TryColor(v, vw, 0, out uint col)) c.BorderRC = col; break; }
            case "border-radius":
            case "border-top-left-radius": case "border-top-right-radius":
            case "border-bottom-left-radius": case "border-bottom-right-radius":
                { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.BorderRadius = px; break; }

            case "width": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.W = px; c.HasW = true; } break; }
            case "height": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.H = px; c.HasH = true; } break; }
            case "min-width": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.MinW = px; break; }
            case "max-width": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.MaxW = px; break; }
            case "min-height": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.MinH = px; break; }
            case "max-height": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.MaxH = px; break; }

            case "list-style": ApplyListShorthand(c, v); break;
            case "list-style-type":
                {
                    string w = FirstWord(v);
                    if (w == "disc") c.ListStyleType = CssListStyle.Disc;
                    else if (w == "circle") c.ListStyleType = CssListStyle.Circle;
                    else if (w == "square") c.ListStyleType = CssListStyle.Square;
                    else if (w == "decimal") c.ListStyleType = CssListStyle.Decimal;
                    else if (w == "decimal-leading-zero") c.ListStyleType = CssListStyle.DecimalLeadingZero;
                    else if (w == "lower-alpha" || w == "lower-latin") c.ListStyleType = CssListStyle.LowerAlpha;
                    else if (w == "upper-alpha" || w == "upper-latin") c.ListStyleType = CssListStyle.UpperAlpha;
                    else if (w == "lower-roman") c.ListStyleType = CssListStyle.LowerRoman;
                    else if (w == "upper-roman") c.ListStyleType = CssListStyle.UpperRoman;
                    else if (w == "none") c.ListStyleType = CssListStyle.None;
                    break;
                }
            case "list-style-position":
                // outside (default) only
                break;
        }
    }

    // ---- shorthand helpers -----------------------------------------------

    private static void ApplyBackgroundShorthand(CssComputed c, string v, int vw)
    {
        List<string> parts = SplitWs(v);
        for (int i = 0; i < parts.Count; i++)
        {
            string p = parts[i];
            if (p == "repeat") c.BgRepeat = CssRepeat.Repeat;
            else if (p == "no-repeat") c.BgRepeat = CssRepeat.NoRepeat;
            else if (p == "repeat-x") c.BgRepeat = CssRepeat.RepeatX;
            else if (p == "repeat-y") c.BgRepeat = CssRepeat.RepeatY;
            else if (p.StartsWith("url("))
            {
                string url = p.Substring(4).TrimEnd(')');
                c.BgImage = Unquote(url.Trim());
            }
            else if (p == "scroll" || p == "fixed" || p == "local") { }
            else if (p == "cover" || p == "contain") { }
            else if (p.Contains("%") || p.Contains("px")) c.BgPos = p;
            else if (CssValue.TryColor(p, vw, 0, out uint col)) { c.HasBg = true; c.BgColor = col; }
        }
    }

    private static void ApplyFontShorthand(CssComputed c, string v, int vw)
    {
        // [style] [weight] size[/line-height] family
        List<string> parts = SplitWs(v);
        int sizeIdx = -1;
        for (int i = 0; i < parts.Count; i++)
        {
            string p = parts[i];
            if (p == "italic" || p == "oblique") c.Italic = true;
            else if (p == "normal") { /* weight/style normal */ }
            else if (p == "bold" || p == "bolder") c.FontWeight = 700;
            else if (p == "lighter") c.FontWeight = 300;
            else if (p.Length > 0 && ((p[0] >= '0' && p[0] <= '9') || p[0] == '.')) { sizeIdx = i; break; }
        }
        if (sizeIdx < 0) return;
        string sizeTok = parts[sizeIdx];
        int slash = sizeTok.IndexOf('/');
        string sizePart = slash > 0 ? sizeTok.Substring(0, slash) : sizeTok;
        if (CssValue.TryFontPx(sizePart, c.FontSize, vw, out int px)) c.FontSize = px;
        if (slash > 0)
        {
            string lh = sizeTok.Substring(slash + 1);
            if (CssValue.TryFloat(lh, out float f) && f > 0) c.LineHeight = f;
            else if (CssValue.TryLengthPx(lh, c.FontSize, vw, out int lpx) && c.FontSize > 0) c.LineHeight = (float)lpx / c.FontSize;
        }
    }

    private static void ApplyMarginShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        int t = 0, r = 0, b = 0, l = 0;
        if (CssValue.TryLengthPx(parts[0], fs, vw, out t))
        {
            if (parts.Count == 1) { r = b = l = t; }
            else if (parts.Count == 2) { CssValue.TryLengthPx(parts[1], fs, vw, out r); b = t; l = r; }
            else if (parts.Count == 3) { CssValue.TryLengthPx(parts[1], fs, vw, out r); CssValue.TryLengthPx(parts[2], fs, vw, out b); l = r; }
            else { CssValue.TryLengthPx(parts[1], fs, vw, out r); CssValue.TryLengthPx(parts[2], fs, vw, out b); CssValue.TryLengthPx(parts[3], fs, vw, out l); }
        }
        c.MarginT = t; c.MarginR = r; c.MarginB = b; c.MarginL = l;
    }

    private static void ApplyPaddingShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        int t = 0, r = 0, b = 0, l = 0;
        if (CssValue.TryLengthPx(parts[0], fs, vw, out t))
        {
            if (parts.Count == 1) { r = b = l = t; }
            else if (parts.Count == 2) { CssValue.TryLengthPx(parts[1], fs, vw, out r); b = t; l = r; }
            else if (parts.Count == 3) { CssValue.TryLengthPx(parts[1], fs, vw, out r); CssValue.TryLengthPx(parts[2], fs, vw, out b); l = r; }
            else { CssValue.TryLengthPx(parts[1], fs, vw, out r); CssValue.TryLengthPx(parts[2], fs, vw, out b); CssValue.TryLengthPx(parts[3], fs, vw, out l); }
        }
        c.PadT = t; c.PadR = r; c.PadB = b; c.PadL = l;
    }

    // side: 0=all,1=top,2=bottom,3=left,4=right
    private static void ApplyBorderShorthand(CssComputed c, string v, int fs, int vw, int side)
    {
        List<string> parts = SplitWs(v);
        int width = 1; CssBorder style = CssBorder.Solid; uint color = 0xFF000000; bool hasW = false;
        for (int i = 0; i < parts.Count; i++)
        {
            string p = parts[i];
            if (p == "none") { style = CssBorder.None; continue; }
            if (p == "solid" || p == "dashed" || p == "dotted" || p == "double" ||
                p == "groove" || p == "ridge" || p == "inset" || p == "outset")
            {
                style = ParseBorder(p);
                continue;
            }
            if (CssValue.TryColor(p, vw, 0, out uint col)) { color = col; continue; }
            if (CssValue.TryLengthPx(p, fs, vw, out int px)) { width = px; hasW = true; }
        }
        if (!hasW && style == CssBorder.None) style = CssBorder.Solid;
        ApplyBorderSide(c, side, style, width, color);
    }

    private static void ApplyBorderSide(CssComputed c, int side, CssBorder style, int w, uint color)
    {
        if (side == 0 || side == 1) { c.BorderTT = style; c.BorderTW = w; c.BorderTC = color; }
        if (side == 0 || side == 2) { c.BorderBB = style; c.BorderBW = w; c.BorderBC = color; }
        if (side == 0 || side == 3) { c.BorderLL = style; c.BorderLW = w; c.BorderLC = color; }
        if (side == 0 || side == 4) { c.BorderRR = style; c.BorderRW = w; c.BorderRC = color; }
    }

    private static void ApplyBorderWidthShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> p = SplitWs(v);
        int t = 0, r = 0, b = 0, l = 0;
        CssValue.TryLengthPx(p[0], fs, vw, out t);
        if (p.Count == 1) { r = b = l = t; }
        else if (p.Count == 2) { CssValue.TryLengthPx(p[1], fs, vw, out r); b = t; l = r; }
        else if (p.Count == 3) { CssValue.TryLengthPx(p[1], fs, vw, out r); CssValue.TryLengthPx(p[2], fs, vw, out b); l = r; }
        else { CssValue.TryLengthPx(p[1], fs, vw, out r); CssValue.TryLengthPx(p[2], fs, vw, out b); CssValue.TryLengthPx(p[3], fs, vw, out l); }
        c.BorderTW = t; c.BorderRW = r; c.BorderBW = b; c.BorderLW = l;
    }

    private static void ApplyBorderColorShorthand(CssComputed c, string v, int vw)
    {
        List<string> p = SplitWs(v);
        uint t = 0, r = 0, b = 0, l = 0;
        CssValue.TryColor(p[0], vw, 0, out t);
        if (p.Count == 1) { r = b = l = t; }
        else if (p.Count == 2) { CssValue.TryColor(p[1], vw, 0, out r); b = t; l = r; }
        else if (p.Count == 3) { CssValue.TryColor(p[1], vw, 0, out r); CssValue.TryColor(p[2], vw, 0, out b); l = r; }
        else { CssValue.TryColor(p[1], vw, 0, out r); CssValue.TryColor(p[2], vw, 0, out b); CssValue.TryColor(p[3], vw, 0, out l); }
        c.BorderTC = t; c.BorderRC = r; c.BorderBC = b; c.BorderLC = l;
    }

    private static void ApplyBorderStyleShorthand(CssComputed c, string v)
    {
        List<string> p = SplitWs(v);
        CssBorder t = ParseBorder(p[0]), r = t, b = t, l = t;
        if (p.Count == 2) { r = ParseBorder(p[1]); b = t; l = r; }
        else if (p.Count == 3) { r = ParseBorder(p[1]); b = ParseBorder(p[2]); l = r; }
        else if (p.Count >= 4) { r = ParseBorder(p[1]); b = ParseBorder(p[2]); l = ParseBorder(p[3]); }
        c.BorderTT = t; c.BorderRR = r; c.BorderBB = b; c.BorderLL = l;
    }

    private static CssBorder ParseBorder(string s)
    {
        if (s == "none") return CssBorder.None;
        if (s == "solid") return CssBorder.Solid;
        if (s == "dashed") return CssBorder.Dashed;
        if (s == "dotted") return CssBorder.Dotted;
        if (s == "double") return CssBorder.Double;
        if (s == "groove") return CssBorder.Groove;
        if (s == "ridge") return CssBorder.Ridge;
        if (s == "inset") return CssBorder.Inset;
        if (s == "outset") return CssBorder.Outset;
        return CssBorder.None;
    }

    private static void ApplyListShorthand(CssComputed c, string v)
    {
        List<string> parts = SplitWs(v);
        for (int i = 0; i < parts.Count; i++)
        {
            string p = parts[i];
            if (p == "none") { c.ListStyleType = CssListStyle.None; continue; }
            // reuse the list-style-type handler
            ApplyDecl(c, new CssDecl { Prop = "list-style-type", Value = p }, 0);
        }
    }

    // ---- tiny text helpers -----------------------------------------------

    private static string FirstWord(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] != ' ' && s[i] != '\t' && s[i] != '(' && s[i] != '/') i++;
        return s.Substring(0, i);
    }

    private static List<string> SplitWs(string s)
    {
        List<string> list = new List<string>();
        int i = 0, len = s.Length;
        while (i < len)
        {
            while (i < len && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
            if (i >= len) break;
            int st = i; int depth = 0;
            while (i < len)
            {
                char ch = s[i];
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (depth == 0 && (ch == ' ' || ch == '\t' || ch == '\n' || ch == '\r')) break;
                i++;
            }
            list.Add(s.Substring(st, i - st));
        }
        return list;
    }

    private static string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && ((s[0] == '"' && s[s.Length - 1] == '"') || (s[0] == '\'' && s[s.Length - 1] == '\'')))
            return s.Substring(1, s.Length - 2);
        return s;
    }

    private static bool EndsWith(string a, string b)
    {
        if (b == null || b.Length > a.Length) return false;
        int start = a.Length - b.Length;
        for (int i = 0; i < b.Length; i++)
            if (a[start + i] != b[i]) return false;
        return true;
    }

    private static bool IsIdentStart(char c) { return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' || c == '-'; }
    private static bool IsIdentChar(char c) { return IsIdentStart(c) || (c >= '0' && c <= '9') || c == '-'; }

    private static int SkipWs(string s, int i, int end)
    {
        while (i < end && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        return i;
    }

    private static int SkipString(string s, int i, int end)
    {
        char q = s[i]; i++;
        while (i < end && s[i] != q) i++;
        return i;
    }

    private static int FindBlockStart(string s, int i, int end)
    {
        int depth = 0;
        for (; i < end; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < end && s[i + 1] == '*') { int ce = s.IndexOf("*/", i + 2, end - i - 2); i = ce < 0 ? end : ce + 1; continue; }
            if (c == '"' || c == '\'') { i = SkipString(s, i, end); continue; }
            if (c == '{') return i;
            if (c == '}' && depth == 0) return -1;   // stray close before our block
            if (c == ';' && depth == 0) return -1;   // statement rule with no block
        }
        return -1;
    }

    private static int MatchBrace(string s, int open, int end)
    {
        int depth = 1;
        for (int i = open + 1; i < end; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < end && s[i + 1] == '*') { int ce = s.IndexOf("*/", i + 2, end - i - 2); i = ce < 0 ? end : ce + 1; continue; }
            if (c == '"' || c == '\'') { i = SkipString(s, i, end); continue; }
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    // Split on a top-level delimiter (ignoring those inside parens/quotes).
    private static List<string> SplitTop(string s, char delim)
    {
        List<string> list = new List<string>();
        int depth = 0, st = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' || c == '\'') { i = SkipString(s, i, s.Length); continue; }
            if (c == '(') depth++;
            else if (c == ')') depth--;
            else if (c == delim && depth == 0) { list.Add(s.Substring(st, i - st)); st = i + 1; }
        }
        if (st < s.Length) list.Add(s.Substring(st));
        return list;
    }
}

// ---------------------------------------------------------------------------
// Value parsing (lengths, colors, numbers)
// ---------------------------------------------------------------------------

public static class CssValue
{
    /// <summary>Parse a length into px. `em` resolves against fontSize; `%` against viewport width.</summary>
    public static bool TryLengthPx(string s, int fontSize, int viewportW, out int px)
    {
        px = 0;
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim();
        if (s == "auto" || s == "normal" || s == "none") return false;

        int i = 0;
        bool neg = false;
        if (s[0] == '-') { neg = true; i = 1; }
        else if (s[0] == '+') i = 1;

        double val = 0; bool hasDigits = false;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') { val = val * 10 + (s[i] - '0'); i++; hasDigits = true; }
        if (i < s.Length && s[i] == '.')
        {
            i++; double frac = 0.1;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') { val += (s[i] - '0') * frac; frac *= 0.1; i++; hasDigits = true; }
        }
        if (!hasDigits) return false;
        if (neg) val = -val;

        string unit = s.Substring(i).Trim().ToLowerInvariant();
        switch (unit)
        {
            case "": case "px": px = (int)Math.Round(val); return true;
            case "em": px = (int)Math.Round(val * fontSize); return true;
            case "rem": px = (int)Math.Round(val * 13); return true;   // root em (approx 13px default)
            case "pt": px = (int)Math.Round(val * 96.0 / 72.0); return true;
            case "pc": px = (int)Math.Round(val * 16.0); return true;
            case "in": px = (int)Math.Round(val * 96.0); return true;
            case "cm": px = (int)Math.Round(val * 96.0 / 2.54); return true;
            case "mm": px = (int)Math.Round(val * 96.0 / 25.4); return true;
            case "ex": px = (int)Math.Round(val * fontSize * 0.5); return true;
            case "ch": px = (int)Math.Round(val * fontSize * 0.5); return true;
            case "%": px = (int)Math.Round(val * viewportW / 100.0); return true;
            case "vw": px = (int)Math.Round(val * viewportW / 100.0); return true;
            case "vh": px = (int)Math.Round(val * viewportW * 0.75 / 100.0); return true;
            default: px = (int)Math.Round(val); return true;
        }
    }

    /// <summary>Parse a font-size (absolute keywords + lengths). % is relative to the parent size.</summary>
    public static bool TryFontPx(string s, int parentSize, int viewportW, out int px)
    {
        px = 0;
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim().ToLowerInvariant();
        switch (s)
        {
            case "xx-small": px = 9; return true;
            case "x-small": px = 10; return true;
            case "small": px = 12; return true;
            case "medium": px = 13; return true;
            case "large": px = 16; return true;
            case "x-large": px = 19; return true;
            case "xx-large": px = 24; return true;
            case "smaller": px = Math.Max(6, parentSize * 5 / 6); return true;
            case "larger": px = parentSize * 6 / 5; return true;
        }
        // percentage relative to parent
        if (s.EndsWith("%"))
        {
            string num = s.Substring(0, s.Length - 1);
            if (TryDouble(num, out double pct))
            { px = Math.Max(6, (int)Math.Round(parentSize * pct / 100.0)); return true; }
            return false;
        }
        return TryLengthPx(s, parentSize, viewportW, out px);
    }

    public static bool TryFloat(string s, out float f)
    {
        f = 0;
        if (string.IsNullOrEmpty(s)) return false;
        if (!TryDouble(s.Trim(), out double d)) return false;
        f = (float)d;
        return true;
    }

    /// <summary>Culture-independent double parse (period decimal separator).</summary>
    public static bool TryDouble(string s, out double d)
    {
        d = 0;
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim();
        int i = 0, len = s.Length;
        bool neg = false;
        if (i < len && s[i] == '-') { neg = true; i++; }
        else if (i < len && s[i] == '+') i++;
        double val = 0; bool has = false;
        while (i < len && s[i] >= '0' && s[i] <= '9') { val = val * 10 + (s[i] - '0'); i++; has = true; }
        if (i < len && s[i] == '.')
        {
            i++; double frac = 0.1;
            while (i < len && s[i] >= '0' && s[i] <= '9') { val += (s[i] - '0') * frac; frac *= 0.1; i++; has = true; }
        }
        if (i < len && (s[i] == 'e' || s[i] == 'E'))
        {
            i++; bool eneg = false;
            if (i < len && s[i] == '-') { eneg = true; i++; }
            else if (i < len && s[i] == '+') i++;
            int exp = 0; bool ehas = false;
            while (i < len && s[i] >= '0' && s[i] <= '9') { exp = exp * 10 + (s[i] - '0'); i++; ehas = true; }
            if (ehas)
            {
                double m = 1;
                for (int k = 0; k < exp && k < 40; k++) m *= 10;
                val = eneg ? val / m : val * m;
            }
        }
        if (!has) return false;
        d = neg ? -val : val;
        return true;
    }

    /// <summary>Parse a CSS color into 0xAARRGGBB. `blend` is the backdrop (used to flatten alpha).</summary>
    public static bool TryColor(string s, int viewportW, uint backdrop, out uint color)
    {
        color = 0xFF000000;
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim().ToLowerInvariant();
        if (s.Length == 0) return false;

        if (s == "transparent") { color = 0x00000000; return true; }

        // named colors
        uint named;
        if (NamedColor(s, out named)) { color = named; return true; }

        // hex
        if (s[0] == '#')
        {
            string h = s.Substring(1);
            if (h.Length == 3 || h.Length == 4)
            {
                int r = HexDigit(h[0]) * 17, g = HexDigit(h[1]) * 17, b = HexDigit(h[2]) * 17;
                int a = h.Length == 4 ? HexDigit(h[3]) * 17 : 255;
                color = Blend((uint)(a << 24 | r << 16 | g << 8 | b), backdrop);
                return true;
            }
            if (h.Length == 6 || h.Length == 8)
            {
                int r = HexPair(h, 0), g = HexPair(h, 2), b = HexPair(h, 4);
                int a = h.Length == 8 ? HexPair(h, 6) : 255;
                color = Blend((uint)(a << 24 | r << 16 | g << 8 | b), backdrop);
                return true;
            }
            return false;
        }

        // rgb()/rgba()/hsl()/hsla()
        if (s.StartsWith("rgb") || s.StartsWith("hsl"))
        {
            int open = s.IndexOf('('), close = s.LastIndexOf(')');
            if (open < 0 || close < 0) return false;
            bool hsl = s.StartsWith("hsl");
            string inner = s.Substring(open + 1, close - open - 1);
            List<string> parts = new List<string>();
            int st = 0;
            for (int i = 0; i < inner.Length; i++) if (inner[i] == ',' || inner[i] == '/') { parts.Add(inner.Substring(st, i - st).Trim()); st = i + 1; }
            parts.Add(inner.Substring(st).Trim());
            if (parts.Count < 3) return false;

            double c1, c2, c3;
            if (hsl)
            {
                double hdeg = ParseAngle(parts[0]);
                double sat = ParsePercent(parts[1]);
                double light = ParsePercent(parts[2]);
                HslToRgb(hdeg, sat, light, out int rr, out int gg, out int bb);
                c1 = rr; c2 = gg; c3 = bb;
            }
            else
            {
                c1 = ParseColorComponent(parts[0], viewportW);
                c2 = ParseColorComponent(parts[1], viewportW);
                c3 = ParseColorComponent(parts[2], viewportW);
            }
            int a = 255;
            if (parts.Count >= 4)
            {
                double af = ParsePercent(parts[3]);
                a = Clamp255((int)Math.Round(af * 255));
            }
            color = Blend((uint)(a << 24 | Clamp255((int)c1) << 16 | Clamp255((int)c2) << 8 | Clamp255((int)c3)), backdrop);
            return true;
        }

        return false;
    }

    private static double ParseColorComponent(string s, int viewportW)
    {
        s = s.Trim();
        if (s.EndsWith("%")) { double pct = ParsePercent(s); return pct * 255; }
        TryDouble(s, out double d);
        return d;
    }

    private static double ParsePercent(string s)
    {
        s = s.Trim();
        bool pct = s.EndsWith("%");
        if (pct) s = s.Substring(0, s.Length - 1);
        TryDouble(s, out double d);
        return pct ? d / 100.0 : d;
    }

    private static double ParseAngle(string s)
    {
        s = s.Trim().ToLowerInvariant();
        TryDouble(s, out double d);
        if (s.EndsWith("grad")) return d * 0.9;
        if (s.EndsWith("rad")) return d * 180.0 / Math.PI;
        if (s.EndsWith("turn")) return d * 360.0;
        return d;
    }

    public static void HslToRgb(double h, double s, double l, out int r, out int g, out int b)
    {
        h = ((h % 360) + 360) % 360;
        if (s < 0) s = 0; else if (s > 1) s = 1;
        if (l < 0) l = 0; else if (l > 1) l = 1;
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double hp = h / 60.0;
        double x = c * (1 - Math.Abs(hp % 2 - 1));
        double m = l - c / 2;
        double rr = 0, gg = 0, bb = 0;
        if (hp < 1) { rr = c; gg = x; }
        else if (hp < 2) { rr = x; gg = c; }
        else if (hp < 3) { gg = c; bb = x; }
        else if (hp < 4) { gg = x; bb = c; }
        else if (hp < 5) { rr = x; bb = c; }
        else { rr = c; bb = x; }
        r = Clamp255((int)Math.Round((rr + m) * 255));
        g = Clamp255((int)Math.Round((gg + m) * 255));
        b = Clamp255((int)Math.Round((bb + m) * 255));
    }

    private static uint Blend(uint src, uint backdrop)
    {
        int sa = (int)(src >> 24);
        if (sa >= 255) return src | 0xFF000000u;
        if (sa <= 0) return 0x00000000;
        // flatten onto the backdrop (default white when 0)
        uint bd = backdrop == 0 ? 0xFFFFFFFFu : backdrop;
        int br = (int)((bd >> 16) & 0xFF), bg = (int)((bd >> 8) & 0xFF), bb = (int)(bd & 0xFF);
        int sr = (int)((src >> 16) & 0xFF), sg = (int)((src >> 8) & 0xFF), sb = (int)(src & 0xFF);
        int r = (sr * sa + br * (255 - sa)) / 255;
        int g = (sg * sa + bg * (255 - sa)) / 255;
        int b = (sb * sa + bb * (255 - sa)) / 255;
        return (uint)(255 << 24 | r << 16 | g << 8 | b);
    }

    private static int HexDigit(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return 0;
    }

    private static int HexPair(string h, int i)
    {
        if (i + 1 >= h.Length) return 0;
        return HexDigit(h[i]) * 16 + HexDigit(h[i + 1]);
    }

    private static int Clamp255(int v) { return v < 0 ? 0 : (v > 255 ? 255 : v); }

    private static bool NamedColor(string s, out uint color)
    {
        switch (s)
        {
            case "black": color = 0xFF000000; return true;
            case "white": color = 0xFFFFFFFF; return true;
            case "red": color = 0xFFFF0000; return true;
            case "green": color = 0xFF008000; return true;
            case "blue": color = 0xFF0000FF; return true;
            case "yellow": color = 0xFFFFFF00; return true;
            case "cyan": case "aqua": color = 0xFF00FFFF; return true;
            case "magenta": case "fuchsia": color = 0xFFFF00FF; return true;
            case "gray": case "grey": color = 0xFF808080; return true;
            case "silver": color = 0xFFC0C0C0; return true;
            case "maroon": color = 0xFF800000; return true;
            case "olive": color = 0xFF808000; return true;
            case "lime": color = 0xFF00FF00; return true;
            case "navy": color = 0xFF000080; return true;
            case "teal": color = 0xFF008080; return true;
            case "purple": color = 0xFF800080; return true;
            case "orange": color = 0xFFFFA500; return true;
            case "pink": color = 0xFFFFC0CB; return true;
            case "brown": color = 0xFFA52A2A; return true;
            case "gold": color = 0xFFFFD700; return true;
            case "indigo": color = 0xFF4B0082; return true;
            case "violet": color = 0xFFEE82EE; return true;
            case "coral": color = 0xFFFF7F50; return true;
            case "salmon": color = 0xFFFA8072; return true;
            case "khaki": color = 0xFFF0E68C; return true;
            case "crimson": color = 0xFFDC143C; return true;
            case "darkblue": color = 0xFF00008B; return true;
            case "darkgreen": color = 0xFF006400; return true;
            case "darkred": color = 0xFF8B0000; return true;
            case "darkgray": case "darkgrey": color = 0xFFA9A9A9; return true;
            case "lightgray": case "lightgrey": color = 0xFFD3D3D3; return true;
            case "lightblue": color = 0xFFADD8E6; return true;
            case "lightgreen": color = 0xFF90EE90; return true;
            case "lightyellow": color = 0xFFFFFFE0; return true;
            case "whitesmoke": color = 0xFFF5F5F5; return true;
            case "gainsboro": color = 0xFFDCDCDC; return true;
            case "steelblue": color = 0xFF4682B4; return true;
            case "skyblue": color = 0xFF87CEEB; return true;
            case "slateblue": color = 0xFF6A5ACD; return true;
            case "seagreen": color = 0xFF2E8B57; return true;
            case "tomato": color = 0xFFFF6347; return true;
            case "orchid": color = 0xFFDA70D6; return true;
            case "tan": color = 0xFFD2B48C; return true;
            case "beige": color = 0xFFF5F5DC; return true;
            case "ivory": color = 0xFFFFFFF0; return true;
            case "lavender": color = 0xFFE6E6FA; return true;
            case "turquoise": color = 0xFF40E0D0; return true;
            case "plum": color = 0xFFDDA0DD; return true;
            case "chocolate": color = 0xFFD2691E; return true;
            case "peru": color = 0xFFCD853F; return true;
            case "asparagus": color = 0xFF87A96B; return true;
            default: color = 0; return false;
        }
    }
}
