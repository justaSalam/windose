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

public enum CssDisplay : byte { Inline, Block, InlineBlock, ListItem, None, Flex, Table, TableRow, TableCell, TableCaption, Grid, Contents }
public enum CssAlign : byte { Left, Center, Right, Justify }
public enum CssVAlign : byte { Baseline, Top, Middle, Bottom, Super, Sub }
public enum CssTransform : byte { None, Upper, Lower, Capitalize }
public enum CssWhite : byte { Normal, Pre, PreWrap, PreLine, Nowrap }
public enum CssPos : byte { Static, Relative, Absolute, Fixed, Sticky }
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

// flexbox
public enum CssFlexDir : byte { Row, RowReverse, Column, ColumnReverse }
public enum CssFlexWrap : byte { NoWrap, Wrap, WrapReverse }
public enum CssJustify : byte { FlexStart, FlexEnd, Center, SpaceBetween, SpaceAround, SpaceEvenly }
public enum CssAlignItems : byte { Auto, Stretch, FlexStart, FlexEnd, Center, Baseline }

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
    public bool MarginTAuto, MarginBAuto, MarginLAuto, MarginRAuto;
    public int PadT, PadB, PadL, PadR;

    // positioned offsets (top/right/bottom/left). auto is stored as
    // int.MinValue so "0" stays a real, distinct value.
    public int PosT, PosR, PosB, PosL;
    public const int Auto = int.MinValue;

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
    public bool IsWPct, IsHPct; public float WPct, HPct;
    public int MinW, MaxW, MinH, MaxH;
    public bool IsMinWPct, IsMaxWPct, IsMinHPct, IsMaxHPct;
    public float MinWPct, MaxWPct, MinHPct, MaxHPct;

    // flexbox
    public CssFlexDir FlexDirection;
    public CssFlexWrap FlexWrap;
    public CssJustify JustifyContent;
    public CssAlignItems AlignItems;      // Auto = initial (stretch)
    public CssAlignItems AlignSelf;       // Auto = inherit AlignItems
    public float FlexGrow, FlexShrink;
    public bool HasFlexBasis; public int FlexBasis;   // px; else auto
    public int GapRow, GapCol;
    public int Order;

    // CSS grid
    public string GridTemplateColumns;
    public string GridTemplateRows;
    public string GridColumn;
    public string GridRow;
    public string Transform;

    // CSS custom properties (--*) and their resolution state
    public Dictionary<string, string> CustomProps;   // element's own --vars (may be null)
    public Dictionary<string, string> ResolvedVars;  // cascade result used for var() lookup

    // paint
    public CssVis Visibility;
    public float Opacity;                // 0..1
    public CssOverflow OverflowX, OverflowY;
    public string BoxShadow;
    public int ZIndex;

    // UI & modern properties
    public string Cursor;
    public string UserSelect;
    public string PointerEvents;
    public string WordBreak;
    public string TextOverflow;
    public float AspectRatio;
    public string ObjectFit;

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
        c.PosT = c.PosR = c.PosB = c.PosL = CssComputed.Auto;
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
        c.FlexDirection = CssFlexDir.Row;
        c.FlexWrap = CssFlexWrap.NoWrap;
        c.JustifyContent = CssJustify.FlexStart;
        c.AlignItems = CssAlignItems.Auto;   // resolves to Stretch
        c.AlignSelf = CssAlignItems.Auto;    // resolves to AlignItems
        c.FlexGrow = 0; c.FlexShrink = 1;
        c.HasFlexBasis = false;
        c.GapRow = 0; c.GapCol = 0;
        c.Order = 0;
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
    Not, Empty, Link, Root,
    Checked, Disabled, Enabled, Focus, Is, Where, OnlyOfType, NthLastChild, Required, Optional, ReadOnly, ReadWrite, Has, Never
}

public sealed class CssCond
{
    public CssCondKind Kind;
    public string Str;      // id/class/attr-name/pseudo arg
    public string Val;      // attribute comparison value
    public int A, B;        // for an+b
    public CssSelector Inner;// for :not()
    public List<CssSelector> InnerList; // for :is(), :where()
    public bool CaseInsensitive;
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
                    case CssCondKind.Checked:
                    case CssCondKind.Disabled:
                    case CssCondKind.Enabled:
                    case CssCondKind.Focus:
                    case CssCondKind.OnlyOfType:
                    case CssCondKind.NthLastChild:
                    case CssCondKind.Required:
                    case CssCondKind.Optional:
                    case CssCondKind.ReadOnly:
                    case CssCondKind.ReadWrite:
                        b++; break;
                    case CssCondKind.Not:
                        if (cd.Inner != null) { a += cd.Inner.Spec.A; b += cd.Inner.Spec.B; c += cd.Inner.Spec.C; }
                        break;
                    case CssCondKind.Is:
                    case CssCondKind.Where:
                        if (cd.InnerList != null && cd.InnerList.Count > 0 && cd.Kind == CssCondKind.Is)
                        {
                            int maxA = 0, maxB = 0, maxC = 0;
                            for (int k = 0; k < cd.InnerList.Count; k++)
                            {
                                if (cd.InnerList[k].Spec.A > maxA || (cd.InnerList[k].Spec.A == maxA && cd.InnerList[k].Spec.B > maxB))
                                {
                                    maxA = cd.InnerList[k].Spec.A;
                                    maxB = cd.InnerList[k].Spec.B;
                                    maxC = cd.InnerList[k].Spec.C;
                                }
                            }
                            a += maxA; b += maxB; c += maxC;
                        }
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
        bool matches = true;
        if (Type != null && Type != "all" && Type != "screen") matches = false;
        if (matches && Features != null)
        {
            for (int i = 0; i < Features.Count; i++)
            {
                if (!Features[i].Matches(viewportW, viewportH)) { matches = false; break; }
            }
        }
        return Negated ? !matches : matches;
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
            if (brace < 0)
            {
                // Recovery: don't break out of the whole stylesheet on a stray character
                int nextSemi = css.IndexOf(';', i, Math.Min(500, end - i));
                if (nextSemi >= 0) i = nextSemi + 1;
                else i++;
                continue;
            }
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
        string at = name.ToString().ToLowerInvariant();

        int stmtSemi = FindStatementEnd(css, j, end);
        int brace = FindBlockStart(css, j, end);

        if (stmtSemi >= 0 && (brace < 0 || stmtSemi < brace))
        {
            // statement at-rule (@import, @charset, @namespace) -> skip past semicolon
            return stmtSemi + 1;
        }

        if (brace < 0) return end;

        string prelude = css.Substring(j, brace - j).Trim();
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
        else if (at == "supports" || at == "layer")
        {
            // treat as always-true (we support most of what pages feature-test)
            ParseBlock(sheet, body, 0, body.Length, outerMedia, ref order);
        }
        // @font-face, @keyframes, @page, @import: parsed away / ignored
        return close + 1;
    }

    private static int FindStatementEnd(string s, int i, int end)
    {
        int depth = 0;
        while (i < end)
        {
            char c = s[i];
            if (c == '/' && i + 1 < end && s[i + 1] == '*')
            {
                int ce = s.IndexOf("*/", i + 2, end - i - 2);
                i = ce < 0 ? end : ce + 2;
                continue;
            }
            if (c == '"' || c == '\'')
            {
                i = SkipString(s, i, end);
                continue;
            }
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { if (depth > 0) depth--; i++; continue; }
            if (c == ';' && depth == 0) return i;
            if (c == '{' && depth == 0) return -1;
            i++;
        }
        return -1;
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
        if (s.StartsWith("not ", StringComparison.OrdinalIgnoreCase)) { cl.Negated = true; i = 4; }
        else if (s.Equals("not", StringComparison.OrdinalIgnoreCase)) { cl.Negated = true; i = 3; }
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

            // pseudo-element at the end (CSS3 :: or CSS2 :)
            if (i < s.Length && s[i] == ':')
            {
                bool dbl = i + 1 < s.Length && s[i + 1] == ':';
                int ps = dbl ? i + 2 : i + 1;
                int pe = ps;
                while (pe < s.Length && IsIdentChar(s[pe])) pe++;
                string pName = s.Substring(ps, pe - ps).ToLowerInvariant();
                if (dbl || pName == "before" || pName == "after" || pName == "first-line" || pName == "first-letter" || pName == "placeholder")
                {
                    sel.PseudoElement = pName;
                    i = pe;
                }
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
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < s.Length)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i]);
                        i++;
                        continue;
                    }
                    if (IsIdentChar(s[i]))
                    {
                        sb.Append(s[i]);
                        i++;
                        continue;
                    }
                    break;
                }
                if (sb.Length > 0) { CssCond cd = new CssCond { Kind = CssCondKind.Class, Str = sb.ToString() }; cp.Add(cd); any = true; }
                continue;
            }

            if (c == '#')
            {
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < s.Length)
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i]);
                        i++;
                        continue;
                    }
                    if (IsIdentChar(s[i]))
                    {
                        sb.Append(s[i]);
                        i++;
                        continue;
                    }
                    break;
                }
                if (sb.Length > 0) { CssCond cd = new CssCond { Kind = CssCondKind.Id, Str = sb.ToString() }; cp.Add(cd); any = true; }
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
                int peek = dbl ? i + 2 : i + 1;
                int st = peek;
                while (peek < s.Length && IsIdentChar(s[peek])) peek++;
                string pName = s.Substring(st, peek - st).ToLowerInvariant();
                if (dbl || pName == "before" || pName == "after" || pName == "first-line" || pName == "first-letter" || pName == "placeholder")
                {
                    break;
                }
                i = peek;
                string pseudo = pName;
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
                else if (!dbl) { cp.Add(new CssCond { Kind = CssCondKind.Never }); any = true; }
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
        bool caseInsensitive = false;
        if (val.EndsWith(" i") || val.EndsWith(" I"))
        {
            caseInsensitive = true;
            val = val.Substring(0, val.Length - 2).Trim();
        }
        else if (val.EndsWith(" s") || val.EndsWith(" S"))
        {
            val = val.Substring(0, val.Length - 2).Trim();
        }
        val = Unquote(val);
        CssCondKind kind;
        if (op == null) kind = CssCondKind.HasAttr;
        else if (op == "=") kind = CssCondKind.AttrEq;
        else if (op == "^=") kind = CssCondKind.AttrPrefix;
        else if (op == "$=") kind = CssCondKind.AttrSuffix;
        else if (op == "*=") kind = CssCondKind.AttrContains;
        else if (op == "~=") kind = CssCondKind.AttrWord;
        else kind = CssCondKind.AttrDash;
        return new CssCond { Kind = kind, Str = name, Val = val, CaseInsensitive = caseInsensitive };
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
            case "only-of-type": return new CssCond { Kind = CssCondKind.OnlyOfType };
            case "empty": return new CssCond { Kind = CssCondKind.Empty };
            case "root": return new CssCond { Kind = CssCondKind.Root };
            case "link": case "any-link": case "visited": return new CssCond { Kind = CssCondKind.Link };
            case "hover": case "focus": case "active": case "focus-within": case "focus-visible":
                return new CssCond { Kind = CssCondKind.Focus };
            case "checked": return new CssCond { Kind = CssCondKind.Checked };
            case "disabled": return new CssCond { Kind = CssCondKind.Disabled };
            case "enabled": return new CssCond { Kind = CssCondKind.Enabled };
            case "required": return new CssCond { Kind = CssCondKind.Required };
            case "optional": return new CssCond { Kind = CssCondKind.Optional };
            case "read-only": return new CssCond { Kind = CssCondKind.ReadOnly };
            case "read-write": return new CssCond { Kind = CssCondKind.ReadWrite };
            case "is":
            case "where":
                {
                    if (string.IsNullOrEmpty(arg)) return null;
                    List<CssSelector> list = CssSheet.ParseSelectors(arg);
                    if (list == null || list.Count == 0) return null;
                    return new CssCond { Kind = pseudo == "is" ? CssCondKind.Is : CssCondKind.Where, InnerList = list };
                }
            case "has":
                {
                    if (string.IsNullOrEmpty(arg)) return null;
                    List<CssSelector> list = CssSheet.ParseSelectors(arg);
                    if (list == null || list.Count == 0) return null;
                    return new CssCond { Kind = CssCondKind.Has, InnerList = list };
                }
            case "not":
                {
                    CssSelector inner = ParseSelector(arg != null ? arg.Trim() : "");
                    if (inner == null) return null;
                    return new CssCond { Kind = CssCondKind.Not, Inner = inner };
                }
            case "nth-child": case "nth-of-type": case "nth-last-child":
                {
                    CssCond cd = ParseNth(arg != null ? arg.Trim() : "");
                    if (cd == null) return null;
                    if (pseudo == "nth-child") cd.Kind = CssCondKind.NthChild;
                    else if (pseudo == "nth-of-type") cd.Kind = CssCondKind.NthOfType;
                    else cd.Kind = CssCondKind.NthLastChild;
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
            if (colon < 0)
            {
                int brace = body.IndexOf('{', i);
                int nextSemi = body.IndexOf(';', i);
                if (brace >= 0 && (nextSemi < 0 || brace < nextSemi))
                {
                    int close = MatchBrace(body, brace, len);
                    i = close < 0 ? len : close + 1;
                    continue;
                }
                if (nextSemi >= 0) { i = nextSemi + 1; continue; }
                break;
            }
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
            else if (c == '"' || c == '\'') { i = SkipString(s, i, end) - 1; }
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
            else if (c == '"' || c == '\'') { i = SkipString(s, i, end) - 1; }
            else if (c == ';' && depth == 0) return i;
            else if (c == '}' && depth == 0) return i;
        }
        return end;
    }

    public sealed class RuleRef
    {
        public CssRule Rule;
        public CssSelector Selector;
    }

    private Dictionary<string, List<RuleRef>> idRules;
    private Dictionary<string, List<RuleRef>> classRules;
    private Dictionary<string, List<RuleRef>> tagRules;
    private List<RuleRef> universalRules;
    private List<RuleRef> pseudoRules;

    private void EnsureIndexed()
    {
        if (idRules != null) return;
        idRules = new Dictionary<string, List<RuleRef>>();
        classRules = new Dictionary<string, List<RuleRef>>();
        tagRules = new Dictionary<string, List<RuleRef>>();
        universalRules = new List<RuleRef>();
        pseudoRules = new List<RuleRef>();

        for (int r = 0; r < Rules.Count; r++)
        {
            CssRule rule = Rules[r];
            for (int s = 0; s < rule.Selectors.Count; s++)
            {
                CssSelector sel = rule.Selectors[s];
                if (sel.PseudoElement != null)
                {
                    pseudoRules.Add(new RuleRef { Rule = rule, Selector = sel });
                    continue;
                }
                if (sel.Compounds == null || sel.Compounds.Count == 0) continue;
                CssCompound rightmost = sel.Compounds[sel.Compounds.Count - 1];

                string idKey = null;
                string classKey = null;
                if (rightmost.Conds != null)
                {
                    for (int c = 0; c < rightmost.Conds.Count; c++)
                    {
                        if (rightmost.Conds[c].Kind == CssCondKind.Id && idKey == null)
                            idKey = rightmost.Conds[c].Str;
                        else if (rightmost.Conds[c].Kind == CssCondKind.Class && classKey == null)
                            classKey = rightmost.Conds[c].Str;
                    }
                }

                RuleRef rs = new RuleRef { Rule = rule, Selector = sel };
                if (idKey != null)
                {
                    List<RuleRef> list;
                    if (!idRules.TryGetValue(idKey, out list)) { list = new List<RuleRef>(); idRules[idKey] = list; }
                    list.Add(rs);
                }
                else if (classKey != null)
                {
                    List<RuleRef> list;
                    if (!classRules.TryGetValue(classKey, out list)) { list = new List<RuleRef>(); classRules[classKey] = list; }
                    list.Add(rs);
                }
                else if (rightmost.Tag != null && rightmost.Tag != "*")
                {
                    List<RuleRef> list;
                    if (!tagRules.TryGetValue(rightmost.Tag, out list)) { list = new List<RuleRef>(); tagRules[rightmost.Tag] = list; }
                    list.Add(rs);
                }
                else
                {
                    universalRules.Add(rs);
                }
            }
        }
    }

    // ---- matching / cascade ----------------------------------------------

    /// <summary>
    /// Compute the style for an element: UA defaults -> inherited props from parent
    /// -> matched rules (sorted by specificity then source order, !important last) -> inline style.
    /// </summary>
    public CssComputed Compute(DomNode el, CssComputed parent, int viewportW, int viewportH)
    {
        CssComputed c = CssComputed.Initial();
        c.InheritFrom(parent);
        ApplyUaDefaults(c, el);
        if (el != null && el.IsElement) ApplyPresentationalHints(c, el, viewportW);

        EnsureIndexed();

        // gather matching declarations
        List<CssDecl> decls = new List<CssDecl>();
        List<CssSpecificity> specs = new List<CssSpecificity>();

        if (el != null && el.IsElement)
        {
            // 1. Universal rules
            for (int i = 0; i < universalRules.Count; i++)
            {
                RuleRef rr = universalRules[i];
                if (rr.Rule.Media != null && !rr.Rule.Media.Matches(viewportW, viewportH)) continue;
                if (!Matches(rr.Selector, el)) continue;
                for (int d = 0; d < rr.Rule.Decls.Count; d++)
                {
                    decls.Add(rr.Rule.Decls[d]);
                    specs.Add(rr.Selector.Spec);
                }
            }

            // 2. Tag rules
            if (el.Tag != null)
            {
                List<RuleRef> tList;
                if (tagRules.TryGetValue(el.Tag, out tList))
                {
                    for (int i = 0; i < tList.Count; i++)
                    {
                        RuleRef rr = tList[i];
                        if (rr.Rule.Media != null && !rr.Rule.Media.Matches(viewportW, viewportH)) continue;
                        if (!Matches(rr.Selector, el)) continue;
                        for (int d = 0; d < rr.Rule.Decls.Count; d++)
                        {
                            decls.Add(rr.Rule.Decls[d]);
                            specs.Add(rr.Selector.Spec);
                        }
                    }
                }
            }

            // 3. Class rules
            if (el.Classes != null)
            {
                for (int ci = 0; ci < el.Classes.Count; ci++)
                {
                    string cls = el.Classes[ci];
                    bool dup = false;
                    for (int k = 0; k < ci; k++) { if (el.Classes[k] == cls) { dup = true; break; } }
                    if (dup) continue;

                    List<RuleRef> cList;
                    if (classRules.TryGetValue(cls, out cList))
                    {
                        for (int i = 0; i < cList.Count; i++)
                        {
                            RuleRef rr = cList[i];
                            if (rr.Rule.Media != null && !rr.Rule.Media.Matches(viewportW, viewportH)) continue;
                            if (!Matches(rr.Selector, el)) continue;
                            for (int d = 0; d < rr.Rule.Decls.Count; d++)
                            {
                                decls.Add(rr.Rule.Decls[d]);
                                specs.Add(rr.Selector.Spec);
                            }
                        }
                    }
                }
            }

            // 4. ID rules
            if (el.Id != null)
            {
                List<RuleRef> idList;
                if (idRules.TryGetValue(el.Id, out idList))
                {
                    for (int i = 0; i < idList.Count; i++)
                    {
                        RuleRef rr = idList[i];
                        if (rr.Rule.Media != null && !rr.Rule.Media.Matches(viewportW, viewportH)) continue;
                        if (!Matches(rr.Selector, el)) continue;
                        for (int d = 0; d < rr.Rule.Decls.Count; d++)
                        {
                            decls.Add(rr.Rule.Decls[d]);
                            specs.Add(rr.Selector.Spec);
                        }
                    }
                }
            }
        }

        SortDecls(decls, specs);

        // ---- custom properties (--*) + var() substitution ----
        // Custom props cascade like normal props (specificity then order). We build
        // a per-element var map: inherited from parent, then overridden by this
        // element's own --props in cascade order. Inline --props win last.
        Dictionary<string, string> vars = null;
        if (parent != null && parent.ResolvedVars != null)
        {
            vars = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kv in parent.ResolvedVars) vars[kv.Key] = kv.Value;
        }
        for (int i = 0; i < decls.Count; i++)
        {
            string p = decls[i].Prop;
            if (p.Length > 1 && p[0] == '-' && p[1] == '-')
            {
                if (vars == null) vars = new Dictionary<string, string>();
                vars[p] = decls[i].Value;
            }
        }
        if (el != null && el.StyleAttr != null && el.StyleAttr.Length > 0)
        {
            int tmp0 = 0;
            List<CssDecl> ivars = ParseDecls(el.StyleAttr, ref tmp0);
            for (int i = 0; i < ivars.Count; i++)
            {
                string p = ivars[i].Prop;
                if (p.Length > 1 && p[0] == '-' && p[1] == '-')
                {
                    if (vars == null) vars = new Dictionary<string, string>();
                    vars[p] = ivars[i].Value;
                }
            }
        }
        // resolve var() references that point at other custom props (self/nested)
        if (vars != null)
        {
            List<string> keys = new List<string>();
            foreach (KeyValuePair<string, string> kv in vars) keys.Add(kv.Key);
            for (int i = 0; i < keys.Count; i++)
            {
                string resolved;
                vars.TryGetValue(keys[i], out resolved);
                string sub = SubstituteVars(resolved, vars, 0);
                if (sub != null) vars[keys[i]] = sub;
            }
        }
        c.ResolvedVars = vars;

        // apply non-custom declarations with var() substituted into each value
        for (int i = 0; i < decls.Count; i++)
        {
            string p = decls[i].Prop;
            if (p.Length > 1 && p[0] == '-' && p[1] == '-') continue;   // already collected
            string val = SubstituteVars(decls[i].Value, vars, 0);
            if (val == null) continue;   // invalid var() -> declaration becomes unset
            ApplyDecl(c, p, val, viewportW, parent);
        }

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
            for (int i = 0; i < inline.Count; i++)
            {
                string p = inline[i].Prop;
                if (p.Length > 1 && p[0] == '-' && p[1] == '-') continue;   // already collected
                string val = SubstituteVars(inline[i].Value, vars, 0);
                if (val == null) continue;
                ApplyDecl(c, p, val, viewportW, parent);
            }
        }

        c.FontSize = Math.Max(6, c.FontSize);
        if (!c.HasColor) { c.Color = 0xFF000000; c.HasColor = true; } // default black
        if (c.Opacity < 0) c.Opacity = 0; else if (c.Opacity > 1) c.Opacity = 1;
        if (el != null && el.GetAttr("hidden") != null) c.Display = CssDisplay.None;
        return c;
    }

    // Replace var(--name, fallback) references in a value using the custom-property
    // map. Returns the substituted string, or null if the value is invalid (a var()
    // with no fallback that resolves to nothing). Cosmos-safe: manual scanning.
    private static string SubstituteVars(string value, Dictionary<string, string> vars, int depth)
    {
        if (value == null || depth > 8) return value;
        int vi = IndexOfVar(value, 0);
        if (vi < 0) return value;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length);
        int pos = 0;
        while (vi >= 0)
        {
            sb.Append(value, pos, vi - pos);
            // find the matching close paren for the '(' right after "var"
            int open = vi + 3;                 // index of '('
            int close = MatchParen(value, open);
            string inner = close > open ? value.Substring(open + 1, close - open - 1) : "";
            // split name and fallback at the first top-level comma
            string name, fallback;
            int comma = TopLevelComma(inner);
            if (comma >= 0) { name = inner.Substring(0, comma); fallback = inner.Substring(comma + 1); }
            else { name = inner; fallback = null; }

            string repl = null;
            string key = name.Trim();
            if (vars != null)
            {
                vars.TryGetValue(key, out repl);
            }
            if (repl != null && repl.Length > 0)
            {
                repl = SubstituteVars(repl, vars, depth + 1);
            }
            else if (fallback != null)
            {
                repl = SubstituteVars(fallback.Trim(), vars, depth + 1);
            }
            else
            {
                // Tailwind CSS sets --tw-bg-opacity / --tw-text-opacity / --tw-border-opacity.
                // If referenced without a fallback and unset, default to 1 (full opacity).
                if (key == "--tw-bg-opacity" || key == "--tw-text-opacity" || key == "--tw-border-opacity")
                    repl = "1";
                else
                    return null;   // missing custom property, no fallback -> invalid
            }
            if (repl == null) return null;
            sb.Append(repl);

            pos = close + 1;
            if (pos >= value.Length) { vi = -1; break; }
            vi = IndexOfVar(value, pos);
        }
        sb.Append(value, pos, value.Length - pos);
        return sb.ToString();
    }

    private static int IndexOfVar(string s, int start)
    {
        if (s == null) return -1;
        for (int i = start; i + 3 < s.Length; i++)
            if (s[i] == 'v' && s[i + 1] == 'a' && s[i + 2] == 'r' && s[i + 3] == '(') return i;
        return -1;
    }

    // index of the ')' matching the '(' at open (paren-depth aware, skips quotes)
    private static int MatchParen(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '"') { i = SkipString(s, i, s.Length); continue; }
            if (ch == '(') depth++;
            else if (ch == ')') { depth--; if (depth == 0) return i; }
        }
        return s.Length - 1;
    }

    // index of the first ',' at paren depth 0, or -1
    private static int TopLevelComma(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '"') { i = SkipString(s, i, s.Length); continue; }
            if (ch == '(') depth++;
            else if (ch == ')') depth--;
            else if (ch == ',' && depth == 0) return i;
        }
        return -1;
    }

    private static void SortDecls(List<CssDecl> decls, List<CssSpecificity> specs)
    {
        // stable insertion sort in ascending priority order: (!important, specificity, order)
        // Lower priority decls are placed first, higher priority decls are placed last,
        // so applying in 0..N-1 order allows higher priority decls to override lower.
        for (int i = 1; i < decls.Count; i++)
        {
            CssDecl di = decls[i]; CssSpecificity si = specs[i];
            int j = i - 1;
            while (j >= 0 && Higher(decls[j], specs[j], di, si))
            {
                decls[j + 1] = decls[j]; specs[j + 1] = specs[j]; j--;
            }
            decls[j + 1] = di; specs[j + 1] = si;
        }
    }

    private static bool Higher(CssDecl a, CssSpecificity sa, CssDecl b, CssSpecificity sb)
    {
        if (a.Important != b.Important) return a.Important; // important has higher priority
        int cmp = sa.CompareTo(sb);
        if (cmp != 0) return cmp > 0;
        return a.Order > b.Order;
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
                {
                    string v = el.GetAttr(cd.Str);
                    if (v == null) return false;
                    return cd.CaseInsensitive ? string.Equals(v, cd.Val, StringComparison.OrdinalIgnoreCase) : v == cd.Val;
                }
            case CssCondKind.AttrPrefix:
                {
                    string v = el.GetAttr(cd.Str);
                    if (v == null) return false;
                    return cd.CaseInsensitive ? v.StartsWith(cd.Val, StringComparison.OrdinalIgnoreCase) : v.StartsWith(cd.Val);
                }
            case CssCondKind.AttrSuffix:
                {
                    string v = el.GetAttr(cd.Str);
                    if (v == null) return false;
                    return cd.CaseInsensitive ? EndsWith(v.ToLowerInvariant(), cd.Val.ToLowerInvariant()) : EndsWith(v, cd.Val);
                }
            case CssCondKind.AttrContains:
                {
                    string v = el.GetAttr(cd.Str);
                    if (v == null) return false;
                    return cd.CaseInsensitive ? v.IndexOf(cd.Val, StringComparison.OrdinalIgnoreCase) >= 0 : v.IndexOf(cd.Val) >= 0;
                }
            case CssCondKind.AttrWord:
                {
                    string v = el.GetAttr(cd.Str);
                    if (v == null) return false;
                    List<string> toks = SplitWs(v);
                    for (int i = 0; i < toks.Count; i++)
                    {
                        if (cd.CaseInsensitive ? string.Equals(toks[i], cd.Val, StringComparison.OrdinalIgnoreCase) : toks[i] == cd.Val)
                            return true;
                    }
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
                return el.Parent == null || el.Parent.Tag == "#document" || el.Tag == "html";

            case CssCondKind.NthChild:
                { int pos = ElementPosition(el); return MatchNth(cd.A, cd.B, pos); }
            case CssCondKind.NthOfType:
                { int pos = TypePosition(el); return MatchNth(cd.A, cd.B, pos); }
            case CssCondKind.FirstOfType:
                return TypePosition(el) == 1;
            case CssCondKind.LastOfType:
                return TypePosition(el) == LastTypePosition(el);
            case CssCondKind.OnlyOfType:
                return TypePosition(el) == 1 && LastTypePosition(el) == 1;

            case CssCondKind.NthLastChild:
                { int total = TotalElementSiblings(el); int pos = ElementPosition(el); return MatchNth(cd.A, cd.B, total - pos + 1); }

            case CssCondKind.Checked:
                return el.GetAttr("checked") != null;
            case CssCondKind.Disabled:
                return el.GetAttr("disabled") != null;
            case CssCondKind.Enabled:
                return (el.Tag == "input" || el.Tag == "button" || el.Tag == "select" || el.Tag == "textarea") && el.GetAttr("disabled") == null;
            case CssCondKind.Focus:
                return false;
            case CssCondKind.Required:
                return el.GetAttr("required") != null;
            case CssCondKind.Optional:
                return el.GetAttr("required") == null;
            case CssCondKind.ReadOnly:
                return el.GetAttr("readonly") != null;
            case CssCondKind.ReadWrite:
                return (el.Tag == "input" || el.Tag == "textarea") && el.GetAttr("readonly") == null && el.GetAttr("disabled") == null;

            case CssCondKind.Is:
            case CssCondKind.Where:
                if (cd.InnerList != null)
                {
                    for (int k = 0; k < cd.InnerList.Count; k++)
                        if (Matches(cd.InnerList[k], el)) return true;
                }
                return false;

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

            case CssCondKind.Never:
                return false;

            case CssCondKind.Has:
                if (cd.InnerList != null)
                {
                    for (int k = 0; k < cd.InnerList.Count; k++)
                        if (HasDescendantMatching(el, cd.InnerList[k])) return true;
                }
                return false;

            case CssCondKind.Not:
                return cd.Inner != null && !Matches(cd.Inner, el);
        }
        return true;
    }

    private static bool HasDescendantMatching(DomNode root, CssSelector sel)
    {
        if (root == null || sel == null) return false;
        List<DomNode> stack = new List<DomNode>();
        DomNode c = root.FirstChild;
        while (c != null)
        {
            if (c.IsElement) stack.Add(c);
            c = c.Next;
        }
        while (stack.Count > 0)
        {
            DomNode curr = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            if (Matches(sel, curr)) return true;
            DomNode ch = curr.FirstChild;
            while (ch != null)
            {
                if (ch.IsElement) stack.Add(ch);
                ch = ch.Next;
            }
        }
        return false;
    }

    private static int TotalElementSiblings(DomNode el)
    {
        if (el == null || el.Parent == null) return 1;
        int count = 0;
        DomNode n = el.Parent.FirstChild;
        while (n != null)
        {
            if (n.IsElement) count++;
            n = n.Next;
        }
        return count;
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

    private static bool IsAllHex(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (!((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')))
                return false;
        }
        return true;
    }

    private static void ApplyPresentationalHints(CssComputed c, DomNode el, int viewportW)
    {
        string tag = el.Tag != null ? el.Tag.ToLowerInvariant() : "";

        // HTML5 hidden attribute: [hidden] { display: none; }
        if (el.GetAttr("hidden") != null)
        {
            c.Display = CssDisplay.None;
        }

        // align attribute
        string align = el.GetAttr("align");
        if (!string.IsNullOrEmpty(align))
        {
            align = align.Trim().ToLowerInvariant();
            if (tag == "table")
            {
                if (align == "center") { c.MarginLAuto = true; c.MarginRAuto = true; }
                else if (align == "left") c.Float = CssFloat.Left;
                else if (align == "right") c.Float = CssFloat.Right;
            }
            else if (tag == "img" || tag == "input" || tag == "object")
            {
                if (align == "left") c.Float = CssFloat.Left;
                else if (align == "right") c.Float = CssFloat.Right;
                else if (align == "middle") c.VerticalAlign = CssVAlign.Middle;
                else if (align == "top") c.VerticalAlign = CssVAlign.Top;
                else if (align == "bottom") c.VerticalAlign = CssVAlign.Bottom;
            }
            else
            {
                if (align == "center") c.TextAlign = CssAlign.Center;
                else if (align == "right") c.TextAlign = CssAlign.Right;
                else if (align == "justify") c.TextAlign = CssAlign.Justify;
                else if (align == "left") c.TextAlign = CssAlign.Left;
            }
        }

        // valign attribute
        string valign = el.GetAttr("valign");
        if (!string.IsNullOrEmpty(valign))
        {
            valign = valign.Trim().ToLowerInvariant();
            if (valign == "top") c.VerticalAlign = CssVAlign.Top;
            else if (valign == "middle" || valign == "center") c.VerticalAlign = CssVAlign.Middle;
            else if (valign == "bottom") c.VerticalAlign = CssVAlign.Bottom;
            else if (valign == "baseline") c.VerticalAlign = CssVAlign.Baseline;
        }

        // bgcolor attribute
        string bgcolor = el.GetAttr("bgcolor");
        if (!string.IsNullOrEmpty(bgcolor))
        {
            bgcolor = bgcolor.Trim();
            if (bgcolor.Length == 6 && !bgcolor.StartsWith("#") && IsAllHex(bgcolor))
                bgcolor = "#" + bgcolor;
            if (CssValue.TryColor(bgcolor, viewportW, 0, out uint bgCol))
            {
                c.BgColor = bgCol;
                c.HasBg = true;
            }
        }

        // color attribute (e.g. <font color="...">)
        string colorAttr = el.GetAttr("color");
        if (!string.IsNullOrEmpty(colorAttr))
        {
            colorAttr = colorAttr.Trim();
            if (colorAttr.Length == 6 && !colorAttr.StartsWith("#") && IsAllHex(colorAttr))
                colorAttr = "#" + colorAttr;
            if (CssValue.TryColor(colorAttr, viewportW, 0, out uint fgCol))
            {
                c.Color = fgCol;
                c.HasColor = true;
            }
        }

        // width and height attributes (tables, cells, images, hr, svg)
        if (tag == "table" || tag == "td" || tag == "th" || tag == "hr" || tag == "img" || tag == "iframe" || tag == "svg")
        {
            string wAttr = el.GetAttr("width");
            if (!string.IsNullOrEmpty(wAttr))
            {
                if (CssValue.TryLengthPx(wAttr, c.FontSize, viewportW, out int wpx) && wpx > 0)
                {
                    c.W = wpx;
                    c.HasW = true;
                }
            }
            string hAttr = el.GetAttr("height");
            if (!string.IsNullOrEmpty(hAttr))
            {
                if (CssValue.TryLengthPx(hAttr, c.FontSize, viewportW, out int hpx) && hpx > 0)
                {
                    c.H = hpx;
                    c.HasH = true;
                }
            }
        }

        // border attribute (tables)
        if (tag == "table")
        {
            string borderAttr = el.GetAttr("border");
            if (!string.IsNullOrEmpty(borderAttr) && int.TryParse(borderAttr.Trim(), out int bwidth))
            {
                if (bwidth > 0)
                {
                    c.BorderTW = c.BorderBW = c.BorderLW = c.BorderRW = bwidth;
                    c.BorderTT = c.BorderBB = c.BorderLL = c.BorderRR = CssBorder.Solid;
                    c.BorderTC = c.BorderBC = c.BorderLC = c.BorderRC = 0xFF888888;
                }
            }
        }

        // nowrap attribute
        if (el.GetAttr("nowrap") != null)
        {
            c.WhiteSpace = CssWhite.Nowrap;
        }
    }

    // ---- UA defaults -----------------------------------------------------

    private static bool HasOnlyFallbackOrEmptyChildren(DomNode parent)
    {
        if (parent == null) return false;
        bool hasFallback = false;
        for (DomNode c = parent.FirstChild; c != null; c = c.Next)
        {
            if (!c.IsElement)
            {
                if (!string.IsNullOrWhiteSpace(c.Text)) return false;
                continue;
            }
            if (c.Tag == "noscript" || c.Tag == "template")
            {
                hasFallback = true;
                continue;
            }
            if (c.Tag != "script" && c.Tag != "style" && c.Tag != "meta" && c.Tag != "link")
            {
                return false;
            }
        }
        return hasFallback;
    }

    public static void ApplyUaDefaults(CssComputed c, string tag)
    {
        if (tag == null) return;
        ApplyUaDefaults(c, new DomNode { Tag = tag, NodeType = DomNode.Element });
    }

    public static void ApplyUaDefaults(CssComputed c, DomNode el)
    {
        if (el == null) return;
        string tag = el.Tag;
        if (tag == null) return;
        switch (tag)
        {
            case "html": case "body":
                c.Display = CssDisplay.Block;
                if (tag == "body") { c.MarginL = 8; c.MarginR = 8; c.MarginT = 8; c.MarginB = 8; }
                break;
            case "head": case "meta": case "link": case "title": case "script": case "style":
                c.Display = CssDisplay.None;
                break;
            case "noscript": case "template":
                if (el.Parent != null && HasOnlyFallbackOrEmptyChildren(el.Parent))
                    c.Display = CssDisplay.Block;
                else
                    c.Display = CssDisplay.None;
                break;
            case "div": case "section": case "article": case "aside": case "header": case "footer":
            case "nav": case "main": case "figure": case "figcaption": case "details": case "summary":
            case "fieldset": case "p": case "form":
            case "hgroup": case "search":
                c.Display = CssDisplay.Block;
                break;
            case "center":
                c.Display = CssDisplay.Block;
                c.TextAlign = CssAlign.Center;
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
            case "svg":
            case "canvas":
            case "video":
            case "audio":
            case "iframe":
            case "embed":
                c.Display = CssDisplay.InlineBlock;
                break;
            case "object": case "map": case "area":
                c.Display = CssDisplay.None; break;
            default:
                if (tag.IndexOf('-') >= 0)
                {
                    c.Display = CssDisplay.Block;
                }
                break;
        }
    }

    // ---- inline pseudo-element styles (::before/::after/::first-letter) ----

    /// <summary>Compute the style for a pseudo-element (::before/::after) of an element, if any.</summary>
    public CssComputed ComputePseudo(DomNode el, CssComputed elStyle, string pseudo, int vw, int vh, out string content)
    {
        content = null;
        if (el == null || !el.IsElement) return null;
        EnsureIndexed();
        if (pseudoRules.Count == 0) return null;

        CssComputed c = null;
        List<CssDecl> decls = new List<CssDecl>();
        List<CssSpecificity> specs = new List<CssSpecificity>();
        for (int r = 0; r < pseudoRules.Count; r++)
        {
            RuleRef rr = pseudoRules[r];
            if (rr.Selector.PseudoElement != pseudo) continue;
            if (rr.Rule.Media != null && !rr.Rule.Media.Matches(vw, vh)) continue;
            if (!Matches(rr.Selector, el)) continue;
            for (int d = 0; d < rr.Rule.Decls.Count; d++)
            {
                decls.Add(rr.Rule.Decls[d]); specs.Add(rr.Selector.Spec);
            }
        }
        if (decls.Count == 0) return null;
        c = elStyle.Clone();
        c.PseudoElement = pseudo;
        SortDecls(decls, specs);
        for (int i = 0; i < decls.Count; i++)
        {
            string p = decls[i].Prop;
            if (p.Length > 1 && p[0] == '-' && p[1] == '-') continue;
            string val = SubstituteVars(decls[i].Value, c.ResolvedVars, 0);
            if (val == null) continue;
            if (p == "content") content = Unquote(val);
            else ApplyDecl(c, p, val, vw, elStyle);
        }
        if (content == null) return null; // ::before/::after require content
        if (content == "none" || content == "normal") return null;
        return c;
    }

    // ---- property application --------------------------------------------

    private static void ApplyDecl(CssComputed c, string prop, string value, int vw, CssComputed parent = null)
    {
        string v = value;
        if (prop.Length > 1 && prop[0] == '-' && prop[1] == '-') return;   // custom props handled in Compute
        switch (prop)
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
                    else if (w == "grid" || w == "inline-grid") c.Display = CssDisplay.Grid;
                    else if (w == "flow-root") c.Display = CssDisplay.Block;
                    else if (w == "table") c.Display = CssDisplay.Table;
                    else if (w == "table-row") c.Display = CssDisplay.TableRow;
                    else if (w == "table-cell") c.Display = CssDisplay.TableCell;
                    else if (w == "table-caption") c.Display = CssDisplay.TableCaption;
                    else if (w == "contents") c.Display = CssDisplay.Contents;
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
                    else if (w == "sticky") c.Position = CssPos.Sticky;
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
                {
                    if (v == "inherit")
                    {
                        if (parent != null && parent.HasColor) { c.Color = parent.Color; c.HasColor = true; }
                        break;
                    }
                    uint backdrop = c.HasColor ? c.Color : (parent != null && parent.HasColor ? parent.Color : 0xFF000000);
                    if (CssValue.TryColor(v, vw, backdrop, out uint col)) { c.Color = col; c.HasColor = true; }
                    break;
                }
            case "background": case "background-color":
                {
                    // "background" shorthand may carry image/position/repeat/color
                    if (prop == "background-color")
                    {
                        uint backdrop = c.HasColor ? c.Color : (parent != null && parent.HasColor ? parent.Color : 0xFF000000);
                        if (CssValue.TryColor(v, vw, backdrop, out uint bg)) { c.HasBg = true; c.BgColor = bg; }
                        break;
                    }
                    ApplyBackgroundShorthand(c, v, vw);
                    break;
                }
            case "background-image":
                {
                    string p = v.Trim();
                    if (p.StartsWith("url("))
                    {
                        string url = p.Substring(4).TrimEnd(')');
                        c.BgImage = Unquote(url.Trim());
                    }
                    else if (p == "none")
                    {
                        c.BgImage = null;
                    }
                    break;
                }
            case "background-repeat":
                {
                    string p = FirstWord(v);
                    if (p == "no-repeat") c.BgRepeat = CssRepeat.NoRepeat;
                    else if (p == "repeat-x") c.BgRepeat = CssRepeat.RepeatX;
                    else if (p == "repeat-y") c.BgRepeat = CssRepeat.RepeatY;
                    else if (p == "repeat") c.BgRepeat = CssRepeat.Repeat;
                    break;
                }
            case "background-position":
            case "background-position-x":
            case "background-position-y":
                {
                    c.BgPos = v.Trim();
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
                    if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px) && c.FontSize > 0)
                    {
                        c.LineHeight = (float)px / c.FontSize;
                        break;
                    }
                    // unitless multiplier (pure number like 1.5)
                    bool isUnitless = true;
                    for (int chIdx = 0; chIdx < w.Length; chIdx++)
                    {
                        char ch = w[chIdx];
                        if (!(ch >= '0' && ch <= '9' || ch == '.')) { isUnitless = false; break; }
                    }
                    if (isUnitless && CssValue.TryFloat(w, out float f))
                    {
                        c.LineHeight = f;
                        break;
                    }
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
            case "margin-inline": ApplyMarginInlineShorthand(c, v, c.FontSize, vw); break;
            case "margin-block": ApplyMarginBlockShorthand(c, v, c.FontSize, vw); break;
            case "margin-inline-start":
                {
                    if (FirstWord(v) == "auto") { c.MarginL = 0; c.MarginLAuto = true; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.MarginL = px; c.MarginLAuto = false; }
                    break;
                }
            case "margin-inline-end":
                {
                    if (FirstWord(v) == "auto") { c.MarginR = 0; c.MarginRAuto = true; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.MarginR = px; c.MarginRAuto = false; }
                    break;
                }
            case "margin-block-start":
                {
                    if (FirstWord(v) == "auto") { c.MarginT = 0; c.MarginTAuto = true; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.MarginT = px; c.MarginTAuto = false; }
                    break;
                }
            case "margin-block-end":
                {
                    if (FirstWord(v) == "auto") { c.MarginB = 0; c.MarginBAuto = true; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.MarginB = px; c.MarginBAuto = false; }
                    break;
                }
            case "margin-top":
                {
                    if (FirstWord(v) == "auto") { c.MarginT = 0; c.MarginTAuto = true; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.MarginT = px; c.MarginTAuto = false; }
                    break;
                }
            case "margin-bottom":
                {
                    if (FirstWord(v) == "auto") { c.MarginB = 0; c.MarginBAuto = true; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.MarginB = px; c.MarginBAuto = false; }
                    break;
                }
            case "margin-left":
                {
                    if (FirstWord(v) == "auto") { c.MarginL = 0; c.MarginLAuto = true; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.MarginL = px; c.MarginLAuto = false; }
                    break;
                }
            case "margin-right":
                {
                    if (FirstWord(v) == "auto") { c.MarginR = 0; c.MarginRAuto = true; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.MarginR = px; c.MarginRAuto = false; }
                    break;
                }

            case "padding": ApplyPaddingShorthand(c, v, c.FontSize, vw); break;
            case "padding-inline": ApplyPaddingInlineShorthand(c, v, c.FontSize, vw); break;
            case "padding-block": ApplyPaddingBlockShorthand(c, v, c.FontSize, vw); break;
            case "padding-inline-start": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadL = px; break; }
            case "padding-inline-end": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadR = px; break; }
            case "padding-block-start": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadT = px; break; }
            case "padding-block-end": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadB = px; break; }
            case "padding-top": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadT = px; break; }
            case "padding-bottom": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadB = px; break; }
            case "padding-left": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadL = px; break; }
            case "padding-right": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PadR = px; break; }

            // positioned offsets & insets
            case "inset": ApplyInsetShorthand(c, v, c.FontSize, vw); break;
            case "inset-inline": ApplyInsetInlineShorthand(c, v, c.FontSize, vw); break;
            case "inset-block": ApplyInsetBlockShorthand(c, v, c.FontSize, vw); break;
            case "inset-inline-start": { if (FirstWord(v) == "auto") c.PosL = CssComputed.Auto; else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PosL = px; break; }
            case "inset-inline-end": { if (FirstWord(v) == "auto") c.PosR = CssComputed.Auto; else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PosR = px; break; }
            case "inset-block-start": { if (FirstWord(v) == "auto") c.PosT = CssComputed.Auto; else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PosT = px; break; }
            case "inset-block-end": { if (FirstWord(v) == "auto") c.PosB = CssComputed.Auto; else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PosB = px; break; }
            case "top": { if (FirstWord(v) == "auto") c.PosT = CssComputed.Auto; else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PosT = px; break; }
            case "right": { if (FirstWord(v) == "auto") c.PosR = CssComputed.Auto; else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PosR = px; break; }
            case "bottom": { if (FirstWord(v) == "auto") c.PosB = CssComputed.Auto; else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PosB = px; break; }
            case "left": { if (FirstWord(v) == "auto") c.PosL = CssComputed.Auto; else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) c.PosL = px; break; }
            case "z-index":
                {
                    string w = FirstWord(v);
                    if (w == "auto") c.ZIndex = 0;
                    else if (int.TryParse(w, out int z)) c.ZIndex = z;
                    break;
                }

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
                {
                    string first = FirstWord(v);
                    if (CssValue.TryLengthPx(first, c.FontSize, vw, out int px)) c.BorderRadius = px;
                    break;
                }

            case "inline-size":
            case "width":
                {
                    string tr = v != null ? v.Trim() : "";
                    if (tr.EndsWith("%") && CssValue.TryFloat(tr.Substring(0, tr.Length - 1), out float pct))
                    {
                        c.IsWPct = true; c.WPct = pct; c.W = (int)Math.Round(pct * vw / 100.0); c.HasW = true;
                    }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px))
                    {
                        c.IsWPct = false; c.W = px; c.HasW = true;
                    }
                    break;
                }
            case "block-size":
            case "height":
                {
                    string tr = v != null ? v.Trim() : "";
                    if (tr.EndsWith("%") && CssValue.TryFloat(tr.Substring(0, tr.Length - 1), out float pct))
                    {
                        c.IsHPct = true; c.HPct = pct; c.H = (int)Math.Round(pct * (vw * 0.75) / 100.0); c.HasH = true;
                    }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px))
                    {
                        c.IsHPct = false; c.H = px; c.HasH = true;
                    }
                    break;
                }
            case "min-inline-size":
            case "min-width":
                {
                    string tr = v != null ? v.Trim() : "";
                    if (tr.EndsWith("%") && CssValue.TryFloat(tr.Substring(0, tr.Length - 1), out float pct))
                    {
                        c.IsMinWPct = true; c.MinWPct = pct; c.MinW = (int)Math.Round(pct * vw / 100.0);
                    }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.IsMinWPct = false; c.MinW = px; }
                    break;
                }
            case "max-inline-size":
            case "max-width":
                {
                    string tr = v != null ? v.Trim() : "";
                    if (tr.EndsWith("%") && CssValue.TryFloat(tr.Substring(0, tr.Length - 1), out float pct))
                    {
                        c.IsMaxWPct = true; c.MaxWPct = pct; c.MaxW = (int)Math.Round(pct * vw / 100.0);
                    }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.IsMaxWPct = false; c.MaxW = px; }
                    break;
                }
            case "min-block-size":
            case "min-height":
                {
                    string tr = v != null ? v.Trim() : "";
                    if (tr.EndsWith("%") && CssValue.TryFloat(tr.Substring(0, tr.Length - 1), out float pct))
                    {
                        c.IsMinHPct = true; c.MinHPct = pct; c.MinH = (int)Math.Round(pct * (vw * 0.75) / 100.0);
                    }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.IsMinHPct = false; c.MinH = px; }
                    break;
                }
            case "max-block-size":
            case "max-height":
                {
                    string tr = v != null ? v.Trim() : "";
                    if (tr.EndsWith("%") && CssValue.TryFloat(tr.Substring(0, tr.Length - 1), out float pct))
                    {
                        c.IsMaxHPct = true; c.MaxHPct = pct; c.MaxH = (int)Math.Round(pct * (vw * 0.75) / 100.0);
                    }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int px)) { c.IsMaxHPct = false; c.MaxH = px; }
                    break;
                }

            // ---- flexbox ----
            case "flex-direction":
                {
                    string w = FirstWord(v);
                    if (w == "row-reverse") c.FlexDirection = CssFlexDir.RowReverse;
                    else if (w == "column") c.FlexDirection = CssFlexDir.Column;
                    else if (w == "column-reverse") c.FlexDirection = CssFlexDir.ColumnReverse;
                    else c.FlexDirection = CssFlexDir.Row;
                    break;
                }
            case "flex-wrap":
                {
                    string w = FirstWord(v);
                    if (w == "wrap") c.FlexWrap = CssFlexWrap.Wrap;
                    else if (w == "wrap-reverse") c.FlexWrap = CssFlexWrap.WrapReverse;
                    else c.FlexWrap = CssFlexWrap.NoWrap;
                    break;
                }
            case "place-items":
                {
                    List<string> parts = SplitWs(v);
                    if (parts.Count > 0)
                    {
                        ApplyDecl(c, "align-items", parts[0], vw, parent);
                        ApplyDecl(c, "justify-items", parts.Count > 1 ? parts[1] : parts[0], vw, parent);
                    }
                    break;
                }
            case "place-content":
                {
                    List<string> parts = SplitWs(v);
                    if (parts.Count > 0)
                    {
                        ApplyDecl(c, "align-content", parts[0], vw, parent);
                        ApplyDecl(c, "justify-content", parts.Count > 1 ? parts[1] : parts[0], vw, parent);
                    }
                    break;
                }
            case "justify-content": case "justify-items": case "justify-self":
                {
                    string w = FirstWord(v);
                    if (w == "flex-end" || w == "end") c.JustifyContent = CssJustify.FlexEnd;
                    else if (w == "center") c.JustifyContent = CssJustify.Center;
                    else if (w == "space-between") c.JustifyContent = CssJustify.SpaceBetween;
                    else if (w == "space-around") c.JustifyContent = CssJustify.SpaceAround;
                    else if (w == "space-evenly") c.JustifyContent = CssJustify.SpaceEvenly;
                    else c.JustifyContent = CssJustify.FlexStart;
                    break;
                }
            case "align-items":
                {
                    string w = FirstWord(v);
                    if (w == "stretch") c.AlignItems = CssAlignItems.Stretch;
                    else if (w == "flex-start" || w == "start") c.AlignItems = CssAlignItems.FlexStart;
                    else if (w == "flex-end" || w == "end") c.AlignItems = CssAlignItems.FlexEnd;
                    else if (w == "center") c.AlignItems = CssAlignItems.Center;
                    else if (w == "baseline") c.AlignItems = CssAlignItems.Baseline;
                    else c.AlignItems = CssAlignItems.Auto;
                    break;
                }
            case "align-self":
                {
                    string w = FirstWord(v);
                    if (w == "auto") c.AlignSelf = CssAlignItems.Auto;
                    else if (w == "stretch") c.AlignSelf = CssAlignItems.Stretch;
                    else if (w == "flex-start" || w == "start") c.AlignSelf = CssAlignItems.FlexStart;
                    else if (w == "flex-end" || w == "end") c.AlignSelf = CssAlignItems.FlexEnd;
                    else if (w == "center") c.AlignSelf = CssAlignItems.Center;
                    else if (w == "baseline") c.AlignSelf = CssAlignItems.Baseline;
                    break;
                }
            case "flex-grow": { if (CssValue.TryFloat(v, out float fg) && fg >= 0) c.FlexGrow = fg; break; }
            case "flex-shrink": { if (CssValue.TryFloat(v, out float fs) && fs >= 0) c.FlexShrink = fs; break; }
            case "flex-basis":
                {
                    if (FirstWord(v) == "auto") { c.HasFlexBasis = false; c.FlexBasis = 0; }
                    else if (CssValue.TryLengthPx(v, c.FontSize, vw, out int fb)) { c.HasFlexBasis = true; c.FlexBasis = fb; }
                    break;
                }
            case "flex": ApplyFlexShorthand(c, v, c.FontSize, vw); break;
            case "gap": ApplyGapShorthand(c, v, c.FontSize, vw); break;
            case "row-gap": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int rg)) c.GapRow = Math.Max(0, rg); break; }
            case "column-gap": { if (CssValue.TryLengthPx(v, c.FontSize, vw, out int cg)) c.GapCol = Math.Max(0, cg); break; }
            case "order": { if (CssValue.TryFloat(v, out float ord)) c.Order = (int)ord; break; }

            // ---- CSS Grid ----
            case "grid-template-columns": c.GridTemplateColumns = v.Trim(); break;
            case "grid-template-rows": c.GridTemplateRows = v.Trim(); break;
            case "grid-column": case "grid-column-start": case "grid-column-end": c.GridColumn = v.Trim(); break;
            case "grid-row": case "grid-row-start": case "grid-row-end": c.GridRow = v.Trim(); break;
            case "grid-gap": ApplyGapShorthand(c, v, c.FontSize, vw); break;
            case "transform": c.Transform = v.Trim(); break;
            case "box-shadow": c.BoxShadow = v.Trim(); break;

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

            case "aspect-ratio":
                {
                    string p = v.Trim();
                    int slash = p.IndexOf('/');
                    if (slash > 0)
                    {
                        if (CssValue.TryFloat(p.Substring(0, slash).Trim(), out float w) &&
                            CssValue.TryFloat(p.Substring(slash + 1).Trim(), out float h) && h > 0)
                        {
                            c.AspectRatio = w / h;
                        }
                    }
                    else if (CssValue.TryFloat(p, out float r) && r > 0)
                    {
                        c.AspectRatio = r;
                    }
                    break;
                }
            case "object-fit":
                c.ObjectFit = FirstWord(v);
                break;
            case "cursor":
                c.Cursor = FirstWord(v);
                break;
            case "user-select":
                c.UserSelect = FirstWord(v);
                break;
            case "pointer-events":
                c.PointerEvents = FirstWord(v);
                break;
            case "word-break":
            case "overflow-wrap":
                c.WordBreak = FirstWord(v);
                break;
            case "text-overflow":
                c.TextOverflow = FirstWord(v);
                break;
            case "transition":
            case "animation":
            case "outline":
            case "outline-color":
            case "outline-width":
            case "outline-style":
            case "all":
            case "clip-path":
            case "filter":
            case "backdrop-filter":
            case "will-change":
            case "contain":
            case "appearance":
            case "-webkit-appearance":
            case "touch-action":
            case "scroll-behavior":
            case "font-display":
            case "writing-mode":
            case "hyphens":
                // recognized CSS properties
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
            else if (p == "none") { c.BgImage = null; }
            else if (p == "transparent") { c.HasBg = false; }
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

    private static bool ParseMarginValue(string s, int fs, int vw, out int px, out bool isAuto)
    {
        if (s == "auto") { px = 0; isAuto = true; return true; }
        isAuto = false;
        return CssValue.TryLengthPx(s, fs, vw, out px);
    }

    private static void ApplyMarginShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        int t = 0, r = 0, b = 0, l = 0;
        bool tAuto = false, rAuto = false, bAuto = false, lAuto = false;
        ParseMarginValue(parts[0], fs, vw, out t, out tAuto);
        if (parts.Count == 1)
        {
            r = b = l = t;
            rAuto = bAuto = lAuto = tAuto;
        }
        else if (parts.Count == 2)
        {
            ParseMarginValue(parts[1], fs, vw, out r, out rAuto);
            b = t; bAuto = tAuto;
            l = r; lAuto = rAuto;
        }
        else if (parts.Count == 3)
        {
            ParseMarginValue(parts[1], fs, vw, out r, out rAuto);
            ParseMarginValue(parts[2], fs, vw, out b, out bAuto);
            l = r; lAuto = rAuto;
        }
        else
        {
            ParseMarginValue(parts[1], fs, vw, out r, out rAuto);
            ParseMarginValue(parts[2], fs, vw, out b, out bAuto);
            if (parts.Count >= 4) ParseMarginValue(parts[3], fs, vw, out l, out lAuto);
        }
        c.MarginT = t; c.MarginR = r; c.MarginB = b; c.MarginL = l;
        c.MarginTAuto = tAuto; c.MarginRAuto = rAuto; c.MarginBAuto = bAuto; c.MarginLAuto = lAuto;
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

    private static void ApplyMarginInlineShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        int l = 0, r = 0;
        bool lAuto = false, rAuto = false;
        ParseMarginValue(parts[0], fs, vw, out l, out lAuto);
        if (parts.Count == 1) { r = l; rAuto = lAuto; }
        else { ParseMarginValue(parts[1], fs, vw, out r, out rAuto); }
        c.MarginL = l; c.MarginR = r;
        c.MarginLAuto = lAuto; c.MarginRAuto = rAuto;
    }

    private static void ApplyMarginBlockShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        int t = 0, b = 0;
        bool tAuto = false, bAuto = false;
        ParseMarginValue(parts[0], fs, vw, out t, out tAuto);
        if (parts.Count == 1) { b = t; bAuto = tAuto; }
        else { ParseMarginValue(parts[1], fs, vw, out b, out bAuto); }
        c.MarginT = t; c.MarginB = b;
        c.MarginTAuto = tAuto; c.MarginBAuto = bAuto;
    }

    private static void ApplyPaddingInlineShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        int l = 0, r = 0;
        if (CssValue.TryLengthPx(parts[0], fs, vw, out l))
        {
            r = l;
            if (parts.Count > 1) CssValue.TryLengthPx(parts[1], fs, vw, out r);
        }
        c.PadL = l; c.PadR = r;
    }

    private static void ApplyPaddingBlockShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        int t = 0, b = 0;
        if (CssValue.TryLengthPx(parts[0], fs, vw, out t))
        {
            b = t;
            if (parts.Count > 1) CssValue.TryLengthPx(parts[1], fs, vw, out b);
        }
        c.PadT = t; c.PadB = b;
    }

    private static void ParseInsetValue(string s, int fs, int vw, out int px)
    {
        if (s == "auto") px = CssComputed.Auto;
        else if (CssValue.TryLengthPx(s, fs, vw, out int p)) px = p;
        else px = CssComputed.Auto;
    }

    private static void ApplyInsetShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        int t = CssComputed.Auto, r = CssComputed.Auto, b = CssComputed.Auto, l = CssComputed.Auto;
        ParseInsetValue(parts[0], fs, vw, out t);
        if (parts.Count == 1)
        {
            r = b = l = t;
        }
        else if (parts.Count == 2)
        {
            ParseInsetValue(parts[1], fs, vw, out r);
            b = t; l = r;
        }
        else if (parts.Count == 3)
        {
            ParseInsetValue(parts[1], fs, vw, out r);
            ParseInsetValue(parts[2], fs, vw, out b);
            l = r;
        }
        else
        {
            ParseInsetValue(parts[1], fs, vw, out r);
            ParseInsetValue(parts[2], fs, vw, out b);
            ParseInsetValue(parts[3], fs, vw, out l);
        }
        c.PosT = t; c.PosR = r; c.PosB = b; c.PosL = l;
    }

    private static void ApplyInsetInlineShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        int l = CssComputed.Auto, r = CssComputed.Auto;
        ParseInsetValue(parts[0], fs, vw, out l);
        r = l;
        if (parts.Count > 1) ParseInsetValue(parts[1], fs, vw, out r);
        c.PosL = l; c.PosR = r;
    }

    private static void ApplyInsetBlockShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        int t = CssComputed.Auto, b = CssComputed.Auto;
        ParseInsetValue(parts[0], fs, vw, out t);
        b = t;
        if (parts.Count > 1) ParseInsetValue(parts[1], fs, vw, out b);
        c.PosT = t; c.PosB = b;
    }

    // flex: none | <grow> <shrink>? <basis>?  (basis "auto" or a length, grow/shrink unitless)
    private static void ApplyFlexShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        string first = parts[0];
        if (first == "none") { c.FlexGrow = 0; c.FlexShrink = 0; c.HasFlexBasis = true; c.FlexBasis = 0; return; }
        if (first == "auto") { c.FlexGrow = 1; c.FlexShrink = 1; c.HasFlexBasis = false; c.FlexBasis = 0; return; }
        if (first == "initial") { c.FlexGrow = 0; c.FlexShrink = 1; c.HasFlexBasis = true; c.FlexBasis = 0; return; }

        // numeric-first form: "1", "1 2", "1 2 30px", "1 1 auto"
        int idx = 0;
        if (CssValue.TryFloat(parts[0], out float g)) { c.FlexGrow = g; idx = 1; }
        else { c.FlexGrow = 0; c.FlexShrink = 1; c.HasFlexBasis = true; c.FlexBasis = 0; return; }

        if (idx < parts.Count && CssValue.TryFloat(parts[idx], out float s)) { c.FlexShrink = s; idx++; }
        else c.FlexShrink = 1;

        if (idx < parts.Count)
        {
            if (parts[idx] == "auto") { c.HasFlexBasis = false; c.FlexBasis = 0; }
            else if (CssValue.TryLengthPx(parts[idx], fs, vw, out int fb)) { c.HasFlexBasis = true; c.FlexBasis = fb; }
        }
        else
        {
            // "flex: <grow>" defaults basis to 0%
            c.HasFlexBasis = true; c.FlexBasis = 0;
        }
    }

    // gap: <row-gap> <column-gap>?  (a lone value sets both)
    private static void ApplyGapShorthand(CssComputed c, string v, int fs, int vw)
    {
        List<string> parts = SplitWs(v);
        if (parts.Count == 0) return;
        if (CssValue.TryLengthPx(parts[0], fs, vw, out int a))
        {
            c.GapRow = Math.Max(0, a);
            if (parts.Count >= 2 && CssValue.TryLengthPx(parts[1], fs, vw, out int b2)) c.GapCol = Math.Max(0, b2);
            else c.GapCol = c.GapRow;
        }
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
            ApplyDecl(c, "list-style-type", p, 0);
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
        while (i < end && s[i] != q)
        {
            if (s[i] == '\\' && i + 1 < end) i++;
            i++;
        }
        return i < end ? i + 1 : end;
    }

    private static int FindBlockStart(string s, int i, int end)
    {
        int depth = 0;
        for (; i < end; i++)
        {
            char c = s[i];
            if (c == '/' && i + 1 < end && s[i + 1] == '*') { int ce = s.IndexOf("*/", i + 2, end - i - 2); i = ce < 0 ? end : ce + 1; continue; }
            if (c == '"' || c == '\'') { i = SkipString(s, i, end) - 1; continue; }
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
            if (c == '"' || c == '\'') { i = SkipString(s, i, end) - 1; continue; }
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

        // math functions: calc() / min() / max() / clamp()
        if (s.Length > 5 && IsMathStart(s))
            return TryMathPx(s, fontSize, viewportW, out px);

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

    // ---- math functions: calc() / min() / max() / clamp() ------------------

    private static bool IsMathStart(string s)
    {
        return StartsWithFunc(s, "calc(") || StartsWithFunc(s, "min(") ||
               StartsWithFunc(s, "max(") || StartsWithFunc(s, "clamp(");
    }

    private static bool StartsWithFunc(string s, string func)
    {
        if (s.Length < func.Length) return false;
        for (int i = 0; i < func.Length; i++)
        {
            char a = s[i], b = func[i];
            if (a >= 'A' && a <= 'Z') a = (char)(a + 32);
            if (a != b) return false;
        }
        return true;
    }

    private struct MathScan { public string S; public int I; public int Fs; public int Vw; }

    private static void MathSkipWs(ref MathScan p)
    {
        while (p.I < p.S.Length && (p.S[p.I] == ' ' || p.S[p.I] == '\t' || p.S[p.I] == '\n' || p.S[p.I] == '\r')) p.I++;
    }

    private static bool TryMathPx(string s, int fontSize, int viewportW, out int px)
    {
        px = 0;
        MathScan p = new MathScan { S = s, I = 0, Fs = fontSize, Vw = viewportW };
        double v;
        if (!ParseMathExpr(ref p, out v)) return false;
        MathSkipWs(ref p);
        if (p.I != p.S.Length) return false;   // trailing junk
        px = (int)Math.Round(v);
        return true;
    }

    // expr := term (('+' | '-') term)*
    private static bool ParseMathExpr(ref MathScan p, out double result)
    {
        result = 0;
        double term;
        if (!ParseMathTerm(ref p, out term)) return false;
        result = term;
        MathSkipWs(ref p);
        while (p.I < p.S.Length && (p.S[p.I] == '+' || p.S[p.I] == '-'))
        {
            bool add = p.S[p.I] == '+';
            p.I++;
            double rhs;
            if (!ParseMathTerm(ref p, out rhs)) return false;
            result = add ? result + rhs : result - rhs;
            MathSkipWs(ref p);
        }
        return true;
    }

    // term := factor (('*' | '/') factor)*
    private static bool ParseMathTerm(ref MathScan p, out double result)
    {
        result = 0;
        double factor;
        if (!ParseMathFactor(ref p, out factor)) return false;
        result = factor;
        MathSkipWs(ref p);
        while (p.I < p.S.Length && (p.S[p.I] == '*' || p.S[p.I] == '/'))
        {
            char op = p.S[p.I];
            p.I++;
            double rhs;
            if (!ParseMathFactor(ref p, out rhs)) return false;
            if (op == '*') result = result * rhs;
            else result = rhs != 0 ? result / rhs : 0;
            MathSkipWs(ref p);
        }
        return true;
    }

    // factor := '(' expr ')' | '-' factor | number[unit] | func '(' args ')'
    private static bool ParseMathFactor(ref MathScan p, out double result)
    {
        result = 0;
        MathSkipWs(ref p);
        if (p.I >= p.S.Length) return false;
        char c = p.S[p.I];

        if (c == '(')
        {
            p.I++;
            if (!ParseMathExpr(ref p, out result)) return false;
            MathSkipWs(ref p);
            if (p.I < p.S.Length && p.S[p.I] == ')') { p.I++; return true; }
            return false;
        }
        if (c == '-')
        {
            p.I++;
            if (!ParseMathFactor(ref p, out result)) return false;
            result = -result;
            return true;
        }
        if (c == '+') { p.I++; return ParseMathFactor(ref p, out result); }

        // function or number
        if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
        {
            int start = p.I;
            while (p.I < p.S.Length && ((p.S[p.I] >= 'a' && p.S[p.I] <= 'z') || (p.S[p.I] >= 'A' && p.S[p.I] <= 'Z'))) p.I++;
            string fn = p.S.Substring(start, p.I - start).ToLowerInvariant();
            MathSkipWs(ref p);
            if (p.I >= p.S.Length || p.S[p.I] != '(') return false;
            p.I++;
            return ParseMathFunc(ref p, fn, out result);
        }

        return ParseMathNumber(ref p, out result);
    }

    private static bool ParseMathFunc(ref MathScan p, string fn, out double result)
    {
        result = 0;
        double a0, a1, a2;
        // first arg
        if (!ParseMathExpr(ref p, out a0)) return false;
        MathSkipWs(ref p);
        bool hasComma = p.I < p.S.Length && p.S[p.I] == ',';
        if (hasComma) p.I++;

        if (fn == "calc")
        {
            // calc takes a single expression
            MathSkipWs(ref p);
            if (p.I < p.S.Length && p.S[p.I] == ')') { p.I++; result = a0; return true; }
            return false;
        }

        if (fn == "clamp")
        {
            if (!ParseMathExpr(ref p, out a1)) return false;
            MathSkipWs(ref p);
            if (p.I < p.S.Length && p.S[p.I] == ',') p.I++;
            if (!ParseMathExpr(ref p, out a2)) return false;
            MathSkipWs(ref p);
            if (p.I < p.S.Length && p.S[p.I] == ')') p.I++;
            // clamp(min, val, max)
            double mn = a0, val = a1, mx = a2;
            result = val < mn ? mn : (val > mx ? mx : val);
            return true;
        }

        // min / max over one or more comma-separated args
        double best = a0;
        while (hasComma)
        {
            double arg;
            if (!ParseMathExpr(ref p, out arg)) return false;
            if (fn == "min") { if (arg < best) best = arg; }
            else { if (arg > best) best = arg; }
            MathSkipWs(ref p);
            hasComma = p.I < p.S.Length && p.S[p.I] == ',';
            if (hasComma) p.I++;
        }
        MathSkipWs(ref p);
        if (p.I < p.S.Length && p.S[p.I] == ')') p.I++;
        result = best;
        return true;
    }

    private static bool ParseMathNumber(ref MathScan p, out double result)
    {
        result = 0;
        int i = p.I;
        bool neg = false;
        if (i < p.S.Length && p.S[i] == '-') { neg = true; i++; }
        else if (i < p.S.Length && p.S[i] == '+') i++;

        double val = 0; bool hasDigits = false;
        while (i < p.S.Length && p.S[i] >= '0' && p.S[i] <= '9') { val = val * 10 + (p.S[i] - '0'); i++; hasDigits = true; }
        if (i < p.S.Length && p.S[i] == '.')
        {
            i++; double frac = 0.1;
            while (i < p.S.Length && p.S[i] >= '0' && p.S[i] <= '9') { val += (p.S[i] - '0') * frac; frac *= 0.1; i++; hasDigits = true; }
        }
        if (!hasDigits) return false;
        if (neg) val = -val;

        int us = i;
        while (i < p.S.Length && ((p.S[i] >= 'a' && p.S[i] <= 'z') || (p.S[i] >= 'A' && p.S[i] <= 'Z') || p.S[i] == '%')) i++;
        string unit = p.S.Substring(us, i - us).ToLowerInvariant();
        p.I = i;

        switch (unit)
        {
            case "": case "px": result = val; break;
            case "em": result = val * p.Fs; break;
            case "rem": result = val * 13; break;
            case "pt": result = val * 96.0 / 72.0; break;
            case "pc": result = val * 16.0; break;
            case "in": result = val * 96.0; break;
            case "cm": result = val * 96.0 / 2.54; break;
            case "mm": result = val * 96.0 / 25.4; break;
            case "ex": case "ch": result = val * p.Fs * 0.5; break;
            case "%": result = val * p.Vw / 100.0; break;
            case "vw": result = val * p.Vw / 100.0; break;
            case "vh": result = val * p.Vw * 0.75 / 100.0; break;
            default: result = val; break;
        }
        return true;
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
        if (s == "currentcolor") { color = backdrop != 0 ? backdrop : 0xFF000000; return true; }

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
            StringBuilder curPart = new StringBuilder();
            int parenDepth = 0;
            for (int i = 0; i < inner.Length; i++)
            {
                char ch = inner[i];
                if (ch == '(') parenDepth++;
                else if (ch == ')') { if (parenDepth > 0) parenDepth--; }

                if (parenDepth == 0 && (ch == ',' || ch == '/' || ch == ' ' || ch == '\t'))
                {
                    if (curPart.Length > 0)
                    {
                        parts.Add(curPart.ToString());
                        curPart.Length = 0;
                    }
                }
                else
                {
                    curPart.Append(ch);
                }
            }
            if (curPart.Length > 0) parts.Add(curPart.ToString());
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
