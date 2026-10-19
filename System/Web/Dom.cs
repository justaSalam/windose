using System;
using System.Collections.Generic;
using System.Text;

// ---------------------------------------------------------------------------
// A minimal DOM: an element/text node tree produced by a tolerant HTML parser.
// This is the foundation the CSS engine matches selectors against (ancestors,
// siblings, attributes) and the layout engine walks to build the render model.
// ---------------------------------------------------------------------------

public sealed class DomNode
{
    public const byte Element = 0;
    public const byte TextNode = 1;

    public byte NodeType;
    public string Tag;        // lowercase element name (elements only)
    public string Text;       // decoded character data (text nodes only)

    public DomNode Parent;
    public DomNode FirstChild, LastChild;
    public DomNode Prev, Next;

    public int Index;         // 1-based position among *element* siblings (for :nth-child)

    // Element identity / attributes
    public string Id;
    public List<string> Classes;
    public string StyleAttr;
    public Dictionary<string, string> Attrs;   // lowercased keys

    // Resolved computed style (elements only; set by the style pass)
    public CssComputed Style;

    public bool IsElement { get { return NodeType == Element; } }

    public string GetAttr(string name)
    {
        if (Attrs == null) return null;
        string v;
        if (Attrs.TryGetValue(name, out v)) return v;
        return null;
    }

    // ---- child / sibling traversal --------------------------------------

    public DomNode FirstElementChild()
    {
        DomNode n = FirstChild;
        while (n != null && !n.IsElement) n = n.Next;
        return n;
    }

    public DomNode NextElementSibling()
    {
        DomNode n = Next;
        while (n != null && !n.IsElement) n = n.Next;
        return n;
    }

    internal void AppendChild(DomNode child)
    {
        child.Parent = this;
        if (FirstChild == null) { FirstChild = LastChild = child; }
        else { child.Prev = LastChild; LastChild.Next = child; LastChild = child; }
    }

    /// <summary>Detach this node from its parent (no-op for orphans).</summary>
    public void Unlink()
    {
        if (Parent != null)
        {
            if (Parent.FirstChild == this) Parent.FirstChild = Next;
            if (Parent.LastChild == this) Parent.LastChild = Prev;
        }
        if (Prev != null) Prev.Next = Next;
        if (Next != null) Next.Prev = Prev;
        Parent = null;
        Prev = null;
        Next = null;
    }

    /// <summary>Append a child, detaching it from any previous parent first.</summary>
    public void Append(DomNode child)
    {
        if (child == null) return;
        child.Unlink();
        AppendChild(child);
    }

    /// <summary>Insert a child before a reference node (null reference = append).</summary>
    public void InsertBefore(DomNode child, DomNode reference)
    {
        if (child == null) return;
        if (reference == null) { Append(child); return; }
        if (reference.Parent != this) { Append(child); return; }
        child.Unlink();
        child.Parent = this;
        child.Next = reference;
        child.Prev = reference.Prev;
        if (reference.Prev != null) reference.Prev.Next = child;
        else FirstChild = child;
        reference.Prev = child;
    }

    /// <summary>Remove one child (returns it, detached).</summary>
    public DomNode Remove(DomNode child)
    {
        if (child == null || child.Parent != this) return child;
        child.Unlink();
        return child;
    }

    /// <summary>Remove every child node.</summary>
    public void RemoveAllChildren()
    {
        DomNode n = FirstChild;
        while (n != null)
        {
            DomNode next = n.Next;
            n.Parent = null;
            n.Prev = null;
            n.Next = null;
            n = next;
        }
        FirstChild = null;
        LastChild = null;
    }

    internal void AddText(string text)
    {
        if (LastChild != null && LastChild.NodeType == TextNode)
        {
            LastChild.Text += text;
            return;
        }
        DomNode t = new DomNode { NodeType = TextNode, Text = text };
        AppendChild(t);
    }

    // ---- serialization (for innerHTML / outerHTML) -------------------------

    /// <summary>Serialize the children of this node to an HTML fragment.</summary>
    public string SerializeChildren()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        DomNode n = FirstChild;
        while (n != null) { HtmlSerialize.Write(sb, n); n = n.Next; }
        return sb.ToString();
    }

    /// <summary>Serialize this node (and its subtree) to HTML.</summary>
    public string SerializeNode()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        HtmlSerialize.Write(sb, this);
        return sb.ToString();
    }

    /// <summary>Element tag or "#text" — used by DOM bindings for nodeName.</summary>
    public string NodeName
    {
        get { return NodeType == TextNode ? "#text" : (Tag != null ? Tag : "#element"); }
    }

    /// <summary>Concatenated text of this subtree.</summary>
    public string TextContent
    {
        get
        {
            if (NodeType == TextNode) return Text != null ? Text : "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            CollectText(this, sb);
            return sb.ToString();
        }
    }

    private static void CollectText(DomNode node, System.Text.StringBuilder sb)
    {
        DomNode n = node.FirstChild;
        while (n != null)
        {
            if (n.NodeType == TextNode) { if (n.Text != null) sb.Append(n.Text); }
            else CollectText(n, sb);
            n = n.Next;
        }
    }
}

/// <summary>
/// Serializes DOM subtrees back to HTML (for innerHTML getters). Text is
/// escaped, void elements never get closing tags, raw-text elements
/// (script/style) keep their content unescaped.
/// </summary>
public static class HtmlSerialize
{
    public static void Write(System.Text.StringBuilder sb, DomNode node)
    {
        if (node == null) return;
        if (node.NodeType == DomNode.TextNode)
        {
            // Inside <script>/<style>, text is raw; elsewhere escape.
            if (node.Parent != null && node.Parent.IsElement &&
                (node.Parent.Tag == "script" || node.Parent.Tag == "style"))
                sb.Append(node.Text != null ? node.Text : "");
            else
                EscapeText(sb, node.Text != null ? node.Text : "");
            return;
        }

        string tag = node.Tag != null ? node.Tag : "";
        sb.Append('<').Append(tag);
        if (node.Attrs != null)
        {
            foreach (var kv in node.Attrs)
            {
                sb.Append(' ').Append(kv.Key).Append("=\"");
                EscapeAttr(sb, kv.Value != null ? kv.Value : "");
                sb.Append('"');
            }
        }
        if (IsVoid(tag)) { sb.Append('>'); return; }
        sb.Append('>');
        DomNode n = node.FirstChild;
        while (n != null) { Write(sb, n); n = n.Next; }
        sb.Append("</").Append(tag).Append('>');
    }

    private static bool IsVoid(string tag)
    {
        return tag == "area" || tag == "base" || tag == "br" || tag == "col" || tag == "embed" ||
               tag == "hr" || tag == "img" || tag == "input" || tag == "link" || tag == "meta" ||
               tag == "param" || tag == "source" || tag == "track" || tag == "wbr";
    }

    public static void EscapeText(System.Text.StringBuilder sb, string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '<') sb.Append("&lt;");
            else if (c == '>') sb.Append("&gt;");
            else if (c == '&') sb.Append("&amp;");
            else sb.Append(c);
        }
    }

    public static void EscapeAttr(System.Text.StringBuilder sb, string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '<') sb.Append("&lt;");
            else if (c == '>') sb.Append("&gt;");
            else if (c == '&') sb.Append("&amp;");
            else if (c == '"') sb.Append("&quot;");
            else sb.Append(c);
        }
    }
}

public static class HtmlDom
{
    // Elements that never have children / never get pushed on the stack.
    private static bool IsVoid(string tag)
    {
        return tag == "area" || tag == "base" || tag == "br" || tag == "col" || tag == "embed" ||
               tag == "hr" || tag == "img" || tag == "input" || tag == "link" || tag == "meta" ||
               tag == "param" || tag == "source" || tag == "track" || tag == "wbr";
    }

    // Raw-text elements: capture everything until the matching close tag verbatim.
    private static bool IsRawText(string tag)
    {
        return tag == "script" || tag == "style" || tag == "textarea" || tag == "title" || tag == "xmp";
    }

    // ---- entry point -----------------------------------------------------

    /// <summary>Parse an HTML document into a DOM tree rooted at a synthetic "#document" node.</summary>
    public static DomNode Parse(string html)
    {
        DomNode root = new DomNode { NodeType = DomNode.Element, Tag = "#document" };
        DomNode cur = root;
        int pos = 0;
        int len = html != null ? html.Length : 0;
        StringBuilder text = new StringBuilder();

        while (pos < len)
        {
            int lt = html.IndexOf('<', pos);
            if (lt < 0)
            {
                FlushText(cur, text);
                break;
            }
            if (lt > pos)
            {
                text.Append(html.Substring(pos, lt - pos));
            }

            // comment / doctype / CDATA / processing instruction
            if (lt + 1 < len && (html[lt + 1] == '!' || html[lt + 1] == '?'))
            {
                FlushText(cur, text);
                if (lt + 3 < len && html[lt + 1] == '!' && html[lt + 2] == '-' && html[lt + 3] == '-')
                {
                    int cc = html.IndexOf("-->", lt + 4);
                    pos = cc < 0 ? len : cc + 3;
                }
                else
                {
                    int gt = html.IndexOf('>', lt + 2);
                    pos = gt < 0 ? len : gt + 1;
                }
                continue;
            }

            // read the tag
            int end = html.IndexOf('>', lt + 1);
            if (end < 0) { FlushText(cur, text); pos = len; break; }

            string raw = html.Substring(lt + 1, end - lt - 1).Trim();
            pos = end + 1;
            if (raw.Length == 0) continue;

            bool closing = raw[0] == '/';
            string name, attrPart;
            if (closing) { name = raw.Substring(1).Trim().ToLowerInvariant(); attrPart = ""; }
            else
            {
                int nameEnd = 0;
                while (nameEnd < raw.Length && IsNameChar(raw[nameEnd])) nameEnd++;
                name = raw.Substring(0, nameEnd).ToLowerInvariant();
                attrPart = raw.Substring(nameEnd);
            }
            if (name.Length == 0) continue;

            if (closing)
            {
                FlushText(cur, text);
                DomNode open = FindOpen(cur, name);
                if (open != null) cur = open.Parent != null ? open.Parent : root;
                continue;
            }

            // opening tag
            bool selfClosing = raw[raw.Length - 1] == '/';
            DomNode el = new DomNode
            {
                NodeType = DomNode.Element,
                Tag = name,
            };
            ParseAttributes(el, attrPart);

            if (IsRawText(name))
            {
                cur.AppendChild(el);
                // capture verbatim until </name>
                int close = IndexOfNoCase(html, "</" + name, pos);
                if (close < 0) close = len;
                string content = html.Substring(pos, close - pos);
                if (name == "title") el.AddText(SqueezeWs(content));
                else if (content.Length > 0) el.AddText(content);
                int gt = html.IndexOf('>', close);
                pos = gt < 0 ? len : gt + 1;
                continue;
            }

            if (IsVoid(name) || selfClosing) { cur.AppendChild(el); continue; }   // no children, stay in place

            // HTML implied-end-tag rules for common block containers
            cur = AutoClose(cur, name);
            cur.AppendChild(el);
            cur = el;
        }
        FlushText(cur, text);

        return root;
    }

    // ---- helpers ---------------------------------------------------------

    private static void FlushText(DomNode cur, StringBuilder text)
    {
        if (text.Length == 0) return;
        string s = text.ToString();
        text.Length = 0;
        cur.AddText(NormalizeWs(HtmlText.DecodeEntities(s)));
    }

    // Normalise newlines but keep the text otherwise intact (layout collapses runs).
    private static string NormalizeWs(string s)
    {
        return s.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string SqueezeWs(string s)
    {
        StringBuilder sb = new StringBuilder(s.Length);
        bool lastSpace = true;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                if (!lastSpace) { sb.Append(' '); lastSpace = true; }
            }
            else { sb.Append(c); lastSpace = false; }
        }
        return sb.ToString().Trim();
    }

    private static bool IsNameChar(char c)
    {
        return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
    }

    private static DomNode FindOpen(DomNode cur, string name)
    {
        DomNode n = cur;
        while (n != null)
        {
            if (n.IsElement && string.Equals(n.Tag, name)) return n;
            n = n.Parent;
        }
        return null;
    }

    // When a new block-level tag opens, close any element that HTML would impliedly close.
    private static DomNode AutoClose(DomNode cur, string name)
    {
        // <li> closes an open <li>; <p> closes an open <p>; <td>/<th>, <tr>, <option>, etc.
        while (cur != null && cur.IsElement && ImpliesClose(cur.Tag, name))
        {
            cur = cur.Parent;
        }
        if (cur == null) return new DomNode { NodeType = DomNode.Element, Tag = "#root" };
        return cur;
    }

    private static bool ImpliesClose(string open, string next)
    {
        if (open == "p" && IsBlockStart(next)) return true;
        if (open == "li" && next == "li") return true;
        if ((open == "td" || open == "th") && (next == "td" || next == "th" || next == "tr")) return true;
        if (open == "tr" && (next == "tr" || next == "thead" || next == "tbody" || next == "tfoot")) return true;
        if ((open == "thead" || open == "tbody" || open == "tfoot") && (next == "thead" || next == "tbody" || next == "tfoot")) return true;
        if (open == "option" && (next == "option" || next == "optgroup")) return true;
        if (open == "dt" && (next == "dt" || next == "dd")) return true;
        if (open == "dd" && (next == "dt" || next == "dd")) return true;
        return false;
    }

    private static bool IsBlockStart(string tag)
    {
        switch (tag)
        {
            case "div": case "p": case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
            case "ul": case "ol": case "li": case "table": case "form": case "blockquote":
            case "section": case "article": case "aside": case "header": case "footer": case "nav":
            case "main": case "figure": case "dl": case "dt": case "dd": case "pre": case "hr":
            case "address": case "center": case "details": case "fieldset": case "figcaption":
                return true;
            default:
                return false;
        }
    }

    private static void ParseAttributes(DomNode el, string attrPart)
    {
        if (string.IsNullOrEmpty(attrPart)) return;
        int i = 0, len = attrPart.Length;
        while (i < len)
        {
            while (i < len && IsWs(attrPart[i])) i++;
            if (i >= len) break;
            if (attrPart[i] == '/' || attrPart[i] == '=') { i++; continue; }

            int start = i;
            while (i < len && IsNameChar(attrPart[i])) i++;
            string key = attrPart.Substring(start, i - start).ToLowerInvariant();
            if (key.Length == 0) { i++; continue; }

            int j = i;
            while (j < len && (attrPart[j] == ' ' || attrPart[j] == '\t')) j++;

            string value = "";
            if (j < len && attrPart[j] == '=')
            {
                j++;
                while (j < len && (attrPart[j] == ' ' || attrPart[j] == '\t')) j++;
                if (j < len && (attrPart[j] == '"' || attrPart[j] == '\''))
                {
                    char q = attrPart[j]; j++;
                    int vstart = j;
                    while (j < len && attrPart[j] != q) j++;
                    value = attrPart.Substring(vstart, j - vstart);
                    j++;
                }
                else
                {
                    int vstart = j;
                    while (j < len && !IsWs(attrPart[j]) && attrPart[j] != '>') j++;
                    value = attrPart.Substring(vstart, j - vstart);
                }
                i = j;
            }

            value = HtmlText.DecodeEntities(value);
            if (el.Attrs == null) el.Attrs = new Dictionary<string, string>();
            el.Attrs[key] = value;

            if (key == "id") el.Id = value;
            else if (key == "class") el.Classes = SplitClasses(value);
            else if (key == "style") el.StyleAttr = value;
        }
    }

    private static List<string> SplitClasses(string value)
    {
        List<string> result = new List<string>();
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && IsWs(value[i])) i++;
            if (i >= value.Length) break;
            int start = i;
            while (i < value.Length && !IsWs(value[i])) i++;
            result.Add(value.Substring(start, i - start));
        }
        return result.Count > 0 ? result : null;
    }

    private static bool IsWs(char c) { return c == ' ' || c == '\t' || c == '\n' || c == '\r'; }

    private static int IndexOfNoCase(string hay, string needle, int start)
    {
        if (start < 0) start = 0;
        int nlen = needle.Length, limit = hay.Length - nlen;
        char first = ToLower(needle[0]);
        for (int i = start; i <= limit; i++)
        {
            if (ToLower(hay[i]) != first) continue;
            int j = 1;
            while (j < nlen && ToLower(hay[i + j]) == ToLower(needle[j])) j++;
            if (j == nlen) return i;
        }
        return -1;
    }

    private static char ToLower(char c) { return (c >= 'A' && c <= 'Z') ? (char)(c + 32) : c; }
}

// ---------------------------------------------------------------------------
// A parsed page: the DOM root plus the metadata the browser needs (title,
// collected CSS, external stylesheet links) and the base URL for resolving
// relative resources.
// ---------------------------------------------------------------------------

public sealed class HtmlPage
{
    public DomNode Root;
    public string Title = "";
    public string CssText = "";
    public List<string> StyleSheetHrefs = new List<string>();
    public string BaseHref = "";

    /// <summary>Scripts in document order. Inline scripts carry Code;
    /// external scripts carry Src (the browser fetches and fills Code).</summary>
    public List<HtmlScript> Scripts = new List<HtmlScript>();
}

public sealed class HtmlScript
{
    public string Src;   // resolved absolute URL of an external script, or null
    public string Code;  // inline code, or fetched source for external scripts
}

public static class HtmlPageBuilder
{
    /// <summary>Parse an HTML document into an HtmlPage (DOM + title + CSS + links).</summary>
    public static HtmlPage Parse(string html)
    {
        HtmlPage page = new HtmlPage();
        page.Root = HtmlDom.Parse(html);
        Collect(page, page.Root);
        return page;
    }

    private static void Collect(HtmlPage page, DomNode node)
    {
        DomNode n = node.FirstChild;
        while (n != null)
        {
            if (n.IsElement)
            {
                switch (n.Tag)
                {
                    case "title":
                        if (page.Title.Length == 0) page.Title = TextOf(n);
                        break;
                    case "style":
                        if (page.CssText.Length > 0) page.CssText += "\n";
                        page.CssText += TextOf(n);
                        break;
                    case "script":
                        {
                            string src = n.GetAttr("src");
                            HtmlScript script = new HtmlScript();
                            if (src != null && src.Length > 0)
                            {
                                script.Src = src;
                            }
                            else
                            {
                                script.Code = TextOf(n);
                            }
                            page.Scripts.Add(script);
                            break;
                        }
                    case "link":
                        {
                            string rel = n.GetAttr("rel");
                            string href = n.GetAttr("href");
                            if (rel != null && href != null && rel.ToLowerInvariant().IndexOf("stylesheet") >= 0)
                                page.StyleSheetHrefs.Add(href);
                            break;
                        }
                    case "base":
                        {
                            string bhref = n.GetAttr("href");
                            if (!string.IsNullOrEmpty(bhref)) page.BaseHref = bhref;
                            break;
                        }
                }
                Collect(page, n);
            }
            n = n.Next;
        }
    }

    private static string TextOf(DomNode el)
    {
        StringBuilder sb = new StringBuilder();
        DomNode n = el.FirstChild;
        while (n != null)
        {
            if (n.NodeType == DomNode.TextNode && n.Text != null) sb.Append(n.Text);
            n = n.Next;
        }
        return sb.ToString();
    }
}

// ---------------------------------------------------------------------------
// HTML entity decoding (shared with the DOM attribute parser).
// ---------------------------------------------------------------------------

public static class HtmlText
{
    public static string DecodeEntities(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf('&') < 0) return s;
        StringBuilder sb = new StringBuilder(s.Length);
        int i = 0, len = s.Length;
        while (i < len)
        {
            char c = s[i];
            if (c != '&') { sb.Append(c); i++; continue; }
            int semi = s.IndexOf(';', i + 1);
            if (semi < 0 || semi - i > 10) { sb.Append(c); i++; continue; }
            string body = s.Substring(i + 1, semi - i - 1);
            string decoded = DecodeEntity(body);
            if (decoded == null) { sb.Append(c); i++; continue; }
            sb.Append(decoded);
            i = semi + 1;
        }
        return sb.ToString();
    }

    private static string DecodeEntity(string body)
    {
        if (body.Length == 0) return null;
        if (body[0] == '#')
        {
            int code = 0; bool ok = false;
            if (body.Length > 1 && (body[1] == 'x' || body[1] == 'X'))
            {
                for (int i = 2; i < body.Length; i++)
                {
                    char c = body[i]; int d;
                    if (c >= '0' && c <= '9') d = c - '0';
                    else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
                    else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
                    else return null;
                    code = code * 16 + d; ok = true;
                }
            }
            else
            {
                for (int i = 1; i < body.Length; i++)
                {
                    char c = body[i];
                    if (c < '0' || c > '9') return null;
                    code = code * 10 + (c - '0'); ok = true;
                }
            }
            if (!ok || code <= 0) return null;
            if (code < 32) return " ";
            return ((char)code).ToString();
        }
        switch (body.ToLowerInvariant())
        {
            case "amp": return "&";
            case "lt": return "<";
            case "gt": return ">";
            case "quot": return "\"";
            case "apos": return "'";
            case "nbsp": return " ";
            case "copy": return "\u00a9";
            case "reg": return "\u00ae";
            case "trade": return "\u2122";
            case "deg": return "\u00b0";
            case "plusmn": return "\u00b1";
            case "times": return "\u00d7";
            case "divide": return "\u00f7";
            case "frac12": return "\u00bd";
            case "hellip": return "\u2026";
            case "mdash": return "\u2014";
            case "ndash": return "\u2013";
            case "lsquo": return "\u2018";
            case "rsquo": return "\u2019";
            case "ldquo": return "\u201c";
            case "rdquo": return "\u201d";
            case "bull": return "\u2022";
            case "laquo": return "\u00ab";
            case "raquo": return "\u00bb";
            case "euro": return "\u20ac";
            case "pound": return "\u00a3";
            case "yen": return "\u00a5";
            case "cent": return "\u00a2";
            case "sect": return "\u00a7";
            case "para": return "\u00b6";
            case "middot": return "\u00b7";
            case "larr": return "\u2190";
            case "uarr": return "\u2191";
            case "rarr": return "\u2192";
            case "darr": return "\u2193";
            default: return null;
        }
    }
}
