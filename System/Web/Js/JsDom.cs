using System;
using System.Collections.Generic;
using System.Text;

// ---------------------------------------------------------------------------
// Real DOM bindings for the JS engine. Wraps the browser's DomNode tree as
// JsObjects so scripts can query (querySelector), read/write (innerHTML,
// textContent, attributes, style), mutate (appendChild, remove, insertBefore),
// and receive events (addEventListener + click bubbling, DOMContentLoaded,
// load). One JsDom owns one interpreter + one document; a mutation callback
// lets the host (WebView) relayout after scripts touch the tree.
//
// Cosmos-safe: no LINQ, no reflection, no regex, no char.IsXxx, manual
// case folding.
// ---------------------------------------------------------------------------

/// <summary>Wraps a DomNode as a JsObject so the script can touch the live tree.</summary>
public sealed class JsElementObject : JsObject
{
    public DomNode Node;
    public JsDom Dom;
    public Dictionary<string, List<JsFunction>> Listeners;
    public JsStyleObject StyleCache;   // lazily created `el.style`
    public JsObject ClassListCache;    // lazily created `el.classList`

    public JsElementObject(DomNode node, JsObject prototype, JsDom dom)
        : base(prototype)
    {
        Node = node;
        Dom = dom;
        Listeners = new Dictionary<string, List<JsFunction>>();
    }

    public override string ClassTag
    {
        get { return Node != null && Node.NodeType == DomNode.TextNode ? "Text" : "HTMLElement"; }
    }
}

/// <summary>A DOM Event passed to listeners.</summary>
public sealed class JsEventObject : JsObject
{
    public string Type;
    public object Target;
    public object CurrentTarget;
    public bool Bubbles = true;
    public bool DefaultPrevented;
    public bool PropagationStopped;
    public bool StopImmediate;

    public JsEventObject(JsObject prototype) : base(prototype) { }

    public override string ClassTag { get { return "Event"; } }
}

/// <summary>`el.style` — a live view over the node's style attribute.</summary>
public sealed class JsStyleObject : JsObject
{
    public JsElementObject Owner;
    public JsStyleObject(JsObject prototype, JsElementObject owner) : base(prototype)
    {
        Owner = owner;
    }
    public override string ClassTag { get { return "CSSStyleDeclaration"; } }
}

/// <summary>A snapshot of an element's computed style (window.getComputedStyle).
/// Read-only: assignments through accessors are ignored.</summary>
public sealed class JsComputedStyleObject : JsObject
{
    public CssComputed Style;

    public JsComputedStyleObject(JsObject prototype, CssComputed style) : base(prototype)
    {
        Style = style;
    }

    public override string ClassTag { get { return "CSSStyleDeclaration"; } }

    public static string ColorToHex(uint c)
    {
        int r = (int)((c >> 16) & 0xFF);
        int g = (int)((c >> 8) & 0xFF);
        int b = (int)(c & 0xFF);
        return "#" + Hex2(r) + Hex2(g) + Hex2(b);
    }

    private static string Hex2(int v)
    {
        const string digits = "0123456789abcdef";
        char[] buf = new char[2];
        buf[0] = digits[(v >> 4) & 15];
        buf[1] = digits[v & 15];
        return new string(buf);
    }

    /// <summary>Format a fractional number the way CSS does (0.5, 1, 2.25).</summary>
    public static string NumStr(float f)
    {
        bool neg = f < 0;
        if (neg) f = -f;
        int whole = (int)f;
        int h = (int)((f - whole) * 100 + 0.5f);
        if (h >= 100) { whole++; h = 0; }
        string s = neg ? "-" : "";
        s += whole.ToString();
        if (h == 0) return s;
        string hs = h.ToString();
        if (h < 10) hs = "0" + hs;
        if (hs.Length == 2 && hs[1] == '0') hs = hs.Substring(0, 1);
        return s + "." + hs;
    }

    private static string BorderForm(CssBorder b)
    {
        switch (b)
        {
            case CssBorder.Solid: return "solid";
            case CssBorder.Dashed: return "dashed";
            case CssBorder.Dotted: return "dotted";
            case CssBorder.Double: return "double";
            case CssBorder.Groove: return "groove";
            case CssBorder.Ridge: return "ridge";
            case CssBorder.Inset: return "inset";
            case CssBorder.Outset: return "outset";
        }
        return "none";
    }

    /// <summary>Format a CSS longhand (kebab-case) from the computed style.
    /// Unknown-to-the-engine properties report their initial value.</summary>
    public string Get(string name)
    {
        CssComputed s = Style;
        if (s == null) return "";
        switch (name)
        {
            // ---- flow / box ----
            case "display":
                switch (s.Display)
                {
                    case CssDisplay.Block: return "block";
                    case CssDisplay.InlineBlock: return "inline-block";
                    case CssDisplay.ListItem: return "list-item";
                    case CssDisplay.None: return "none";
                    case CssDisplay.Flex: return "flex";
                    case CssDisplay.Table: return "table";
                    case CssDisplay.TableRow: return "table-row";
                    case CssDisplay.TableCell: return "table-cell";
                    case CssDisplay.TableCaption: return "table-caption";
                }
                return "inline";
            case "position":
                switch (s.Position)
                {
                    case CssPos.Relative: return "relative";
                    case CssPos.Absolute: return "absolute";
                    case CssPos.Fixed: return "fixed";
                }
                return "static";
            case "float":
                return s.Float == CssFloat.Left ? "left" : (s.Float == CssFloat.Right ? "right" : "none");
            case "clear":
                switch (s.Clear)
                {
                    case CssClear.Left: return "left";
                    case CssClear.Right: return "right";
                    case CssClear.Both: return "both";
                }
                return "none";
            case "box-sizing":
                return s.BoxSizing == CssBox.Border ? "border-box" : "content-box";
            case "visibility":
                return s.Visibility == CssVis.Hidden ? "hidden"
                    : (s.Visibility == CssVis.Collapse ? "collapse" : "visible");
            case "overflow":
                if (s.OverflowX == s.OverflowY) return Overf(s.OverflowX);
                return Overf(s.OverflowX) + " " + Overf(s.OverflowY);
            case "overflow-x": return Overf(s.OverflowX);
            case "overflow-y": return Overf(s.OverflowY);

            // ---- sizing ----
            case "width": return s.HasW ? s.W + "px" : "auto";
            case "height": return s.HasH ? s.H + "px" : "auto";
            case "min-width": return s.MinW + "px";
            case "max-width": return s.MaxW == int.MaxValue ? "none" : s.MaxW + "px";
            case "min-height": return s.MinH + "px";
            case "max-height": return s.MaxH == int.MaxValue ? "none" : s.MaxH + "px";

            // ---- spacing ----
            case "margin-top": return s.MarginT + "px";
            case "margin-right": return s.MarginR + "px";
            case "margin-bottom": return s.MarginB + "px";
            case "margin-left": return s.MarginL + "px";
            case "padding-top": return s.PadT + "px";
            case "padding-right": return s.PadR + "px";
            case "padding-bottom": return s.PadB + "px";
            case "padding-left": return s.PadL + "px";

            // ---- borders ----
            case "border-top-width": return s.BorderTW + "px";
            case "border-right-width": return s.BorderRW + "px";
            case "border-bottom-width": return s.BorderBW + "px";
            case "border-left-width": return s.BorderLW + "px";
            case "border-top-style": return BorderForm(s.BorderTT);
            case "border-right-style": return BorderForm(s.BorderRR);
            case "border-bottom-style": return BorderForm(s.BorderBB);
            case "border-left-style": return BorderForm(s.BorderLL);
            case "border-top-color": return ColorToHex(s.BorderTC);
            case "border-right-color": return ColorToHex(s.BorderRC);
            case "border-bottom-color": return ColorToHex(s.BorderBC);
            case "border-left-color": return ColorToHex(s.BorderLC);
            case "border-width":
                return Same4(s.BorderTW, s.BorderRW, s.BorderBW, s.BorderLW)
                    ? s.BorderTW + "px"
                    : s.BorderTW + "px " + s.BorderRW + "px " + s.BorderBW + "px " + s.BorderLW + "px";
            case "border-style":
                return (s.BorderTT == s.BorderRR && s.BorderRR == s.BorderBB && s.BorderBB == s.BorderLL)
                    ? BorderForm(s.BorderTT)
                    : BorderForm(s.BorderTT) + " " + BorderForm(s.BorderRR) + " " +
                      BorderForm(s.BorderBB) + " " + BorderForm(s.BorderLL);
            case "border-color":
                return (s.BorderTC == s.BorderRC && s.BorderRC == s.BorderBC && s.BorderBC == s.BorderLC)
                    ? ColorToHex(s.BorderTC)
                    : ColorToHex(s.BorderTC) + " " + ColorToHex(s.BorderRC) + " " +
                      ColorToHex(s.BorderBC) + " " + ColorToHex(s.BorderLC);
            case "border-radius": return s.BorderRadius + "px";

            // ---- background ----
            case "background-color":
                return s.HasBg ? ColorToHex(s.BgColor) : "rgba(0, 0, 0, 0)";
            case "background-image":
                return !string.IsNullOrEmpty(s.BgImage) ? s.BgImage : "none";
            case "background-repeat":
                switch (s.BgRepeat)
                {
                    case CssRepeat.NoRepeat: return "no-repeat";
                    case CssRepeat.RepeatX: return "repeat-x";
                    case CssRepeat.RepeatY: return "repeat-y";
                }
                return "repeat";
            case "background-position": return s.BgPos != null ? s.BgPos : "0% 0%";

            // ---- text ----
            case "color": return s.HasColor ? ColorToHex(s.Color) : "#000000";
            case "font-size": return s.FontSize + "px";
            case "font-weight": return s.FontWeight.ToString();
            case "font-style": return s.Italic ? "italic" : "normal";
            case "font-family": return "sans-serif";   // engine does not track families
            case "line-height": return s.LineHeight > 0 ? NumStr(s.LineHeight) : "normal";
            case "letter-spacing": return s.LetterSpacing == 0 ? "normal" : s.LetterSpacing + "px";
            case "word-spacing": return s.WordSpacing == 0 ? "normal" : s.WordSpacing + "px";
            case "text-indent": return s.TextIndent + "px";
            case "text-align":
                switch (s.TextAlign)
                {
                    case CssAlign.Center: return "center";
                    case CssAlign.Right: return "right";
                    case CssAlign.Justify: return "justify";
                }
                return "left";
            case "text-transform":
                switch (s.TextTransform)
                {
                    case CssTransform.Upper: return "uppercase";
                    case CssTransform.Lower: return "lowercase";
                    case CssTransform.Capitalize: return "capitalize";
                }
                return "none";
            case "text-decoration":
                if (!s.Underline && !s.LineThrough && !s.Overline) return "none";
                {
                    string d = "";
                    if (s.Underline) d = "underline";
                    if (s.LineThrough) d = d.Length > 0 ? d + " line-through" : "line-through";
                    if (s.Overline) d = d.Length > 0 ? d + " overline" : "overline";
                    return d;
                }
            case "white-space":
                switch (s.WhiteSpace)
                {
                    case CssWhite.Pre: return "pre";
                    case CssWhite.PreWrap: return "pre-wrap";
                    case CssWhite.PreLine: return "pre-line";
                    case CssWhite.Nowrap: return "nowrap";
                }
                return "normal";
            case "vertical-align":
                switch (s.VerticalAlign)
                {
                    case CssVAlign.Top: return "top";
                    case CssVAlign.Middle: return "middle";
                    case CssVAlign.Bottom: return "bottom";
                    case CssVAlign.Super: return "super";
                    case CssVAlign.Sub: return "sub";
                }
                return "baseline";

            // ---- paint ----
            case "opacity": return NumStr(s.Opacity);
            // not tracked by the engine yet: report initial values
            case "z-index": return "auto";
            case "cursor": return "auto";
            case "top": case "left": case "right": case "bottom": return "auto";

            // ---- flexbox ----
            case "flex-direction":
                switch (s.FlexDirection)
                {
                    case CssFlexDir.RowReverse: return "row-reverse";
                    case CssFlexDir.Column: return "column";
                    case CssFlexDir.ColumnReverse: return "column-reverse";
                }
                return "row";
            case "flex-wrap":
                switch (s.FlexWrap)
                {
                    case CssFlexWrap.Wrap: return "wrap";
                    case CssFlexWrap.WrapReverse: return "wrap-reverse";
                }
                return "nowrap";
            case "justify-content":
                switch (s.JustifyContent)
                {
                    case CssJustify.FlexEnd: return "flex-end";
                    case CssJustify.Center: return "center";
                    case CssJustify.SpaceBetween: return "space-between";
                    case CssJustify.SpaceAround: return "space-around";
                    case CssJustify.SpaceEvenly: return "space-evenly";
                }
                return "flex-start";
            case "align-items":
                switch (s.AlignItems)
                {
                    case CssAlignItems.FlexStart: return "flex-start";
                    case CssAlignItems.FlexEnd: return "flex-end";
                    case CssAlignItems.Center: return "center";
                    case CssAlignItems.Baseline: return "baseline";
                }
                return "normal";
            case "align-self":
                if (s.AlignSelf == CssAlignItems.Auto) return "auto";
                switch (s.AlignSelf)
                {
                    case CssAlignItems.FlexStart: return "flex-start";
                    case CssAlignItems.FlexEnd: return "flex-end";
                    case CssAlignItems.Center: return "center";
                    case CssAlignItems.Baseline: return "baseline";
                }
                return "stretch";
            case "flex-grow": return NumStr(s.FlexGrow);
            case "flex-shrink": return NumStr(s.FlexShrink);
            case "flex-basis": return s.HasFlexBasis ? s.FlexBasis + "px" : "auto";
            case "order": return s.Order.ToString();
            case "gap":
                return s.GapRow == s.GapCol ? s.GapRow + "px" : s.GapRow + "px " + s.GapCol + "px";
            case "row-gap": return s.GapRow + "px";
            case "column-gap": return s.GapCol + "px";
        }
        return "";
    }

    private static string Overf(CssOverflow o)
    {
        switch (o)
        {
            case CssOverflow.Hidden: return "hidden";
            case CssOverflow.Scroll: return "scroll";
            case CssOverflow.Auto: return "auto";
        }
        return "visible";
    }

    private static bool Same4(int a, int b, int c, int d)
    {
        return a == b && b == c && c == d;
    }
}

public sealed class JsDom
{
    public JsInterpreter Interp;
    public JsRealm Realm;
    public DomNode Root;                 // the #root document node

    public JsObject Document;            // window.document
    public JsObject ElementPrototype;
    public JsObject TextPrototype;
    public JsObject EventPrototype;
    public JsObject StylePrototype;

    private Dictionary<DomNode, JsElementObject> cache;
    private Dictionary<string, List<JsFunction>> documentListeners;
    private Dictionary<string, List<JsFunction>> windowListeners;

    /// <summary>Raised after any DOM mutation the scripts cause (host relayouts).</summary>
    public Action OnDomChanged;

    // computed-style queries (window.getComputedStyle): the host pushes the
    // document stylesheet + viewport via SetStylesheet; the sheet is parsed
    // lazily and only re-parsed when that changes.
    public JsObject ComputedPrototype;
    private CssSheet styleSheet;
    private string styleCss = "";
    private int styleVw = 1024, styleVh = 768;

    private static readonly string[] StyleProps = new string[]
    {
        "display", "color", "backgroundColor", "backgroundImage",
        "width", "height", "minWidth", "minHeight", "maxWidth", "maxHeight",
        "margin", "marginTop", "marginRight", "marginBottom", "marginLeft",
        "padding", "paddingTop", "paddingRight", "paddingBottom", "paddingLeft",
        "fontSize", "fontWeight", "fontStyle", "fontFamily",
        "textAlign", "lineHeight", "letterSpacing", "textTransform",
        "textDecoration", "whiteSpace", "overflow", "visibility", "cursor",
        "border", "borderWidth", "borderColor", "borderRadius", "borderStyle",
        "position", "top", "left", "right", "bottom", "zIndex", "opacity",
        "float", "clear", "verticalAlign",
    };

    public JsDom(JsInterpreter interp, DomNode root)
    {
        Interp = interp;
        Realm = interp.Realm;
        Root = root;
        cache = new Dictionary<DomNode, JsElementObject>();
        documentListeners = new Dictionary<string, List<JsFunction>>();
        windowListeners = new Dictionary<string, List<JsFunction>>();
    }

    // ---- install ----------------------------------------------------------

    /// <summary>Build prototypes, document and window bindings on the global.</summary>
    public void Install()
    {
        ElementPrototype = Realm.NewObject();
        TextPrototype = Realm.NewObject();
        EventPrototype = Realm.NewObject();
        StylePrototype = Realm.NewObject();
        Realm.EventPrototype = EventPrototype;   // shared with XHR events

        BuildElementPrototype();
        BuildTextPrototype();
        BuildEventPrototype();
        BuildComputedPrototype();

        // document object
        Document = Realm.NewObject();
        BuildDocument(Document);

        // window bindings on the global object
        Realm.Global.Define("document", JsProperty.Data(Document));
        DefineOn(Realm.Global, "addEventListener", WindowAddListener, 2);
        DefineOn(Realm.Global, "removeEventListener", WindowRemoveListener, 2);
        DefineOn(Realm.Global, "getComputedStyle", WindowGetComputedStyle, 2);

        // `new Event(type)` constructor
        Realm.Global.Define("Event", JsProperty.Data(
            Realm.MakeCtor("Event", EventCtor, 1, EventPrototype)));
    }

    private object EventCtor(JsCallArgs a)
    {
        JsEventObject ev = new JsEventObject(EventPrototype);
        ev.Type = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
        return ev;
    }

    // ---- helpers ----------------------------------------------------------

    private void DefineOn(JsObject o, string name, JsNativeFn fn, int len)
    {
        o.Define(name, JsProperty.Data(Realm.MakeMethod(name, fn, len)));
    }

    private void DefineAccessor(JsObject o, string name, JsNativeFn get, JsNativeFn set)
    {
        JsFunction g = get != null ? Realm.MakeMethod("get " + name, get, 0) : null;
        JsFunction s = set != null ? Realm.MakeMethod("set " + name, set, 1) : null;
        o.Define(name, JsProperty.Accessor(g, s));
    }

    private void MarkChanged()
    {
        if (OnDomChanged != null) OnDomChanged();
    }

    // ---- stylesheet context (for window.getComputedStyle) ------------------

    /// <summary>Give the DOM the document stylesheet + viewport size so scripts
    /// can query computed styles. Called by the host on load and on resize.</summary>
    public void SetStylesheet(string cssText, int viewportW, int viewportH)
    {
        styleCss = cssText != null ? cssText : "";
        styleVw = viewportW > 0 ? viewportW : 1024;
        styleVh = viewportH > 0 ? viewportH : 768;
        styleSheet = null; // rebuilt lazily on next getComputedStyle
    }

    /// <summary>Walk up to the document element computing a parent style chain.</summary>
    private CssComputed ComputeStyleChain(DomNode el)
    {
        if (el == null || !el.IsElement) return null;
        if (styleSheet == null)
        {
            // If the host hasn't pushed CSS, self-discover <style> elements so
            // getComputedStyle still sees the page's rules (harness / no host).
            if (styleCss.Length == 0) styleCss = CollectStyleText(Root);
            styleSheet = CssSheet.Parse(styleCss);
        }
        // Build the ancestor chain (root -> el) so inherited props resolve.
        List<DomNode> chain = new List<DomNode>();
        DomNode cur = el;
        while (cur != null && cur.IsElement) { chain.Add(cur); cur = cur.Parent; }
        CssComputed parent = null;
        for (int i = chain.Count - 1; i >= 0; i--)
            parent = styleSheet.Compute(chain[i], parent, styleVw, styleVh);
        return parent;
    }

    /// <summary>Concatenate the text of every &lt;style&gt; element in the tree.</summary>
    private string CollectStyleText(DomNode node)
    {
        StringBuilder sb = new StringBuilder();
        CollectStyleTextInto(node, sb);
        return sb.ToString();
    }

    private void CollectStyleTextInto(DomNode node, StringBuilder sb)
    {
        if (node == null) return;
        if (node.IsElement && node.Tag == "style")
        {
            string t = node.TextContent;
            if (!string.IsNullOrEmpty(t)) { sb.Append(t); sb.Append('\n'); }
        }
        for (DomNode c = node.FirstChild; c != null; c = c.Next)
            CollectStyleTextInto(c, sb);
    }

    private object WindowGetComputedStyle(JsCallArgs a)
    {
        JsElementObject el = a.Get(0) as JsElementObject;
        if (el == null || el.Node == null || !el.Node.IsElement) return new JsComputedStyleObject(ComputedPrototype, null);
        CssComputed c = ComputeStyleChain(el.Node);
        return new JsComputedStyleObject(ComputedPrototype, c);
    }

    // ---- computed style prototype (window.getComputedStyle) ---------------

    private void BuildComputedPrototype()
    {
        ComputedPrototype = Realm.NewObject();
        DefineOn(ComputedPrototype, "getPropertyValue", CsGetProperty, 1);
        DefineOn(ComputedPrototype, "getPropertyPriority", CsGetPriority, 1);

        // camelCase IDL accessors backed by the kebab-case formatter
        for (int i = 0; i < ComputedProps.Length; i += 2)
        {
            string camel = ComputedProps[i];
            string kebab = ComputedProps[i + 1];
            DefineAccessor(ComputedPrototype, camel,
                delegate (JsCallArgs a)
                {
                    JsComputedStyleObject c = a.ThisValue as JsComputedStyleObject;
                    return c != null ? (object)c.Get(kebab) : (object)"";
                }, null);
        }
    }

    // camel name, kebab name pairs for the common IDL attributes
    private static readonly string[] ComputedProps = new string[]
    {
        "display", "display",
        "position", "position",
        "top", "top", "left", "left", "right", "right", "bottom", "bottom",
        "width", "width", "height", "height",
        "minWidth", "min-width", "minHeight", "min-height",
        "maxWidth", "max-width", "maxHeight", "max-height",
        "marginTop", "margin-top", "marginRight", "margin-right",
        "marginBottom", "margin-bottom", "marginLeft", "margin-left",
        "paddingTop", "padding-top", "paddingRight", "padding-right",
        "paddingBottom", "padding-bottom", "paddingLeft", "padding-left",
        "color", "color",
        "backgroundColor", "background-color",
        "fontSize", "font-size", "fontWeight", "font-weight", "fontStyle", "font-style",
        "fontFamily", "font-family", "lineHeight", "line-height",
        "textAlign", "text-align", "letterSpacing", "letter-spacing",
        "textTransform", "text-transform", "textDecoration", "text-decoration",
        "whiteSpace", "white-space", "visibility", "visibility",
        "overflow", "overflow", "opacity", "opacity",
        "zIndex", "z-index", "float", "float", "clear", "clear",
        "borderRadius", "border-radius",
        "flexDirection", "flex-direction", "flexWrap", "flex-wrap",
        "justifyContent", "justify-content", "alignItems", "align-items",
        "alignSelf", "align-self", "flexGrow", "flex-grow",
        "flexShrink", "flex-shrink", "flexBasis", "flex-basis",
        "gap", "gap", "rowGap", "row-gap", "columnGap", "column-gap",
    };

    private object CsGetProperty(JsCallArgs a)
    {
        JsComputedStyleObject c = a.ThisValue as JsComputedStyleObject;
        if (c == null) return "";
        return c.Get(JsValueOps.ToStringValue(a.Get(0)));
    }

    private object CsGetPriority(JsCallArgs a) { return ""; }

    private object DocUrl(JsCallArgs a)
    {
        JsEventLoop loop = Interp.Loop;
        return loop != null ? (object)loop.LocationHref : (object)"";
    }
    private object DocLocationGet(JsCallArgs a)
    {
        return Realm.LocationObj != null ? (object)Realm.LocationObj : JsNull.Instance;
    }
    private object DocLocationSet(JsCallArgs a)
    {
        // document.location = "url" navigates, same as window.location
        JsEventLoop loop = Interp.Loop;
        if (loop != null) loop.Navigate(JsValueOps.ToStringValue(a.Get(0)));
        return JsUndefined.Instance;
    }

    private object DocCookieGet(JsCallArgs a)
    {
        // document.cookie reads the host jar for the current page URL
        JsEventLoop loop = Interp.Loop;
        if (loop == null || loop.CookieGetter == null) return "";
        string v = loop.CookieGetter(loop.LocationHref);
        return v != null ? (object)v : (object)"";
    }

    private object DocCookieSet(JsCallArgs a)
    {
        // an assignment stores one cookie ("name=value; Path=/; Secure")
        JsEventLoop loop = Interp.Loop;
        if (loop != null && loop.CookieSetter != null)
            loop.CookieSetter(JsValueOps.ToStringValue(a.Get(0)), loop.LocationHref);
        return JsUndefined.Instance;
    }

    /// <summary>Wrap a node (cached so identity and listeners survive queries).</summary>
    public JsElementObject Wrap(DomNode node)
    {
        if (node == null) return null;
        JsElementObject o;
        if (cache.TryGetValue(node, out o)) return o;
        JsObject proto = node.NodeType == DomNode.TextNode ? TextPrototype : ElementPrototype;
        o = new JsElementObject(node, proto, this);
        cache[node] = o;
        return o;
    }

    private object WrapOrJsNull(DomNode node)
    {
        JsElementObject w = Wrap(node);
        return w != null ? (object)w : JsNull.Instance;
    }

    // ---- element prototype ------------------------------------------------

    private void BuildElementPrototype()
    {
        // identity
        DefineAccessor(ElementPrototype, "tagName", ElTagName, null);
        DefineAccessor(ElementPrototype, "nodeName", ElNodeName, null);
        DefineAccessor(ElementPrototype, "nodeType", ElNodeType, null);
        DefineAccessor(ElementPrototype, "id", ElIdGet, ElIdSet);
        DefineAccessor(ElementPrototype, "className", ElClassGet, ElClassSet);
        DefineAccessor(ElementPrototype, "value", ElValueGet, ElValueSet);

        // content
        DefineAccessor(ElementPrototype, "innerHTML", ElInnerGet, ElInnerSet);
        DefineAccessor(ElementPrototype, "outerHTML", ElOuterGet, null);
        DefineAccessor(ElementPrototype, "textContent", ElTextGet, ElTextSet);

        // tree
        DefineAccessor(ElementPrototype, "children", ElChildren, null);
        DefineAccessor(ElementPrototype, "childNodes", ElChildNodes, null);
        DefineAccessor(ElementPrototype, "parentNode", ElParent, null);
        DefineAccessor(ElementPrototype, "parentElement", ElParent, null);
        DefineAccessor(ElementPrototype, "firstElementChild", ElFirstEl, null);
        DefineAccessor(ElementPrototype, "lastElementChild", ElLastEl, null);
        DefineAccessor(ElementPrototype, "nextElementSibling", ElNextEl, null);
        DefineAccessor(ElementPrototype, "previousElementSibling", ElPrevEl, null);
        DefineAccessor(ElementPrototype, "firstChild", ElFirstChild, null);
        DefineAccessor(ElementPrototype, "lastChild", ElLastChild, null);
        DefineAccessor(ElementPrototype, "nextSibling", ElNextSib, null);
        DefineAccessor(ElementPrototype, "previousSibling", ElPrevSib, null);

        // style + class
        DefineAccessor(ElementPrototype, "style", ElStyle, null);
        DefineAccessor(ElementPrototype, "classList", ElClassList, null);

        // attributes
        DefineOn(ElementPrototype, "getAttribute", ElGetAttribute, 1);
        DefineOn(ElementPrototype, "setAttribute", ElSetAttribute, 2);
        DefineOn(ElementPrototype, "removeAttribute", ElRemoveAttribute, 1);
        DefineOn(ElementPrototype, "hasAttribute", ElHasAttribute, 1);

        // mutation
        DefineOn(ElementPrototype, "appendChild", ElAppendChild, 1);
        DefineOn(ElementPrototype, "removeChild", ElRemoveChild, 1);
        DefineOn(ElementPrototype, "insertBefore", ElInsertBefore, 2);
        DefineOn(ElementPrototype, "remove", ElRemove, 0);
        DefineOn(ElementPrototype, "cloneNode", ElCloneNode, 1);

        // queries
        DefineOn(ElementPrototype, "querySelector", ElQuerySelector, 1);
        DefineOn(ElementPrototype, "querySelectorAll", ElQuerySelectorAll, 1);
        DefineOn(ElementPrototype, "getElementById", ElGetElementById, 1);

        // events
        DefineOn(ElementPrototype, "addEventListener", ElAddEventListener, 2);
        DefineOn(ElementPrototype, "removeEventListener", ElRemoveEventListener, 2);
        DefineOn(ElementPrototype, "dispatchEvent", ElDispatchEvent, 1);
        DefineOn(ElementPrototype, "click", ElClick, 0);
    }

    // identity
    private object ElTagName(JsCallArgs a) { JsElementObject e = A(a); return e.Node.Tag != null ? e.Node.Tag.ToUpperInvariant() : ""; }
    private object ElNodeName(JsCallArgs a) { JsElementObject e = A(a); return e.Node.NodeName.ToUpperInvariant(); }
    private object ElNodeType(JsCallArgs a) { JsElementObject e = A(a); return (double)(e.Node.NodeType == DomNode.TextNode ? 3 : 1); }

    private object ElIdGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.Id != null ? e.Node.Id : ""; }
    private object ElIdSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "id", JsValueOps.ToStringValue(a.Get(0))); MarkChanged(); return JsUndefined.Instance; }

    private object ElClassGet(JsCallArgs a) { JsElementObject e = A(a); string v = e.Node.GetAttr("class"); return v != null ? v : ""; }
    private object ElClassSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "class", JsValueOps.ToStringValue(a.Get(0))); MarkChanged(); return JsUndefined.Instance; }

    private object ElValueGet(JsCallArgs a) { JsElementObject e = A(a); string v = e.Node.GetAttr("value"); return v != null ? v : ""; }
    private object ElValueSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "value", JsValueOps.ToStringValue(a.Get(0))); MarkChanged(); return JsUndefined.Instance; }

    // content
    private object ElInnerGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.SerializeChildren(); }
    private object ElInnerSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string html = JsValueOps.ToStringValue(a.Get(0));
        SetInnerHtml(e.Node, html);
        MarkChanged();
        return JsUndefined.Instance;
    }
    private object ElOuterGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.SerializeNode(); }

    private object ElTextGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.TextContent; }
    private object ElTextSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string t = JsValueOps.ToStringValue(a.Get(0));
        e.Node.RemoveAllChildren();
        if (t != null && t.Length > 0) e.Node.AddText(t);
        MarkChanged();
        return JsUndefined.Instance;
    }

    // tree navigation (return JsNull when absent, matching real DOM)
    private object ElChildren(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsArray arr = Realm.NewArray();
        DomNode n = e.Node.FirstElementChild();
        while (n != null) { arr.Items.Add(Wrap(n)); n = n.NextElementSibling(); }
        return arr;
    }

    private object ElChildNodes(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsArray arr = Realm.NewArray();
        DomNode n = e.Node.FirstChild;
        while (n != null) { arr.Items.Add(Wrap(n)); n = n.Next; }
        return arr;
    }

    private object ElParent(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(e.Node.Parent); }
    private object ElFirstEl(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(e.Node.FirstElementChild()); }
    private object ElLastEl(JsCallArgs a)
    {
        JsElementObject e = A(a);
        DomNode n = e.Node.LastChild;
        while (n != null && !n.IsElement) n = n.Prev;
        return WrapOrJsNull(n);
    }
    private object ElNextEl(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(e.Node.NextElementSibling()); }
    private object ElPrevEl(JsCallArgs a)
    {
        JsElementObject e = A(a);
        DomNode n = e.Node.Prev;
        while (n != null && !n.IsElement) n = n.Prev;
        return WrapOrJsNull(n);
    }
    private object ElFirstChild(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(e.Node.FirstChild); }
    private object ElLastChild(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(e.Node.LastChild); }
    private object ElNextSib(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(e.Node.Next); }
    private object ElPrevSib(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(e.Node.Prev); }

    // style / classList (lazily built per element)
    private object ElStyle(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.StyleCache == null) e.StyleCache = BuildStyleObject(e);
        return e.StyleCache;
    }

    private object ElClassList(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.ClassListCache == null) e.ClassListCache = BuildClassList(e);
        return e.ClassListCache;
    }

    // attributes
    private object ElGetAttribute(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string name = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        string v = e.Node.GetAttr(name);
        return v != null ? (object)v : JsNull.Instance;
    }
    private object ElSetAttribute(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string name = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        string val = JsValueOps.ToStringValue(a.Get(1));
        SetAttribute(e.Node, name, val);
        MarkChanged();
        return JsUndefined.Instance;
    }
    private object ElRemoveAttribute(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string name = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        if (e.Node.Attrs != null) e.Node.Attrs.Remove(name);
        if (name == "id") e.Node.Id = null;
        else if (name == "class") e.Node.Classes = null;
        else if (name == "style") e.Node.StyleAttr = null;
        MarkChanged();
        return JsUndefined.Instance;
    }
    private object ElHasAttribute(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string name = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        return e.Node.GetAttr(name) != null;
    }

    // mutation
    private object ElAppendChild(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsElementObject child = RequireNode(a.Get(0));
        e.Node.Append(child.Node);
        MarkChanged();
        return child;
    }
    private object ElRemoveChild(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsElementObject child = RequireNode(a.Get(0));
        e.Node.Remove(child.Node);
        MarkChanged();
        return child;
    }
    private object ElInsertBefore(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsElementObject child = RequireNode(a.Get(0));
        object refVal = a.Get(1);
        JsElementObject refNode = refVal is JsElementObject ? (JsElementObject)refVal : null;
        e.Node.InsertBefore(child.Node, refNode != null ? refNode.Node : null);
        MarkChanged();
        return child;
    }
    private object ElRemove(JsCallArgs a)
    {
        JsElementObject e = A(a);
        e.Node.Unlink();
        MarkChanged();
        return JsUndefined.Instance;
    }
    private object ElCloneNode(JsCallArgs a)
    {
        JsElementObject e = A(a);
        bool deep = a.ArgCount > 0 && JsValueOps.ToBoolean(a.Get(0));
        DomNode copy = CloneNode(e.Node, deep);
        MarkChanged();
        return Wrap(copy);
    }

    // queries
    private object ElQuerySelector(JsCallArgs a)
    {
        JsElementObject e = A(a);
        List<CssSelector> sels = CssSheet.ParseSelectors(JsValueOps.ToStringValue(a.Get(0)));
        DomNode hit = CssSheet.QuerySelector(e.Node, sels);
        return WrapOrJsNull(hit);
    }
    private object ElQuerySelectorAll(JsCallArgs a)
    {
        JsElementObject e = A(a);
        List<CssSelector> sels = CssSheet.ParseSelectors(JsValueOps.ToStringValue(a.Get(0)));
        List<DomNode> hits = new List<DomNode>();
        CssSheet.QuerySelectorAll(e.Node, sels, hits);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }
    private object ElGetElementById(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string id = JsValueOps.ToStringValue(a.Get(0));
        return WrapOrJsNull(FindById(e.Node, id));
    }

    // events
    private object ElAddEventListener(JsCallArgs a)
    {
        JsElementObject e = A(a);
        AddListener(e.Listeners, a);
        return JsUndefined.Instance;
    }
    private object ElRemoveEventListener(JsCallArgs a)
    {
        JsElementObject e = A(a);
        RemoveListener(e.Listeners, a);
        return JsUndefined.Instance;
    }
    private object ElDispatchEvent(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsEventObject ev = a.Get(0) as JsEventObject;
        if (ev == null) throw JsErr.Type("dispatchEvent expects an Event");
        DispatchExisting(e.Node, ev);
        return !ev.DefaultPrevented;
    }
    private object ElClick(JsCallArgs a)
    {
        JsElementObject e = A(a);
        Dispatch(e.Node, "click");
        return JsUndefined.Instance;
    }

    // ---- text prototype ---------------------------------------------------

    private void BuildTextPrototype()
    {
        DefineAccessor(TextPrototype, "nodeType", ElNodeType, null);
        DefineAccessor(TextPrototype, "nodeName", ElNodeName, null);
        DefineAccessor(TextPrototype, "nodeValue", TextValueGet, TextValueSet);
        DefineAccessor(TextPrototype, "data", TextValueGet, TextValueSet);
        DefineAccessor(TextPrototype, "textContent", TextValueGet, TextValueSet);
        DefineAccessor(TextPrototype, "parentNode", ElParent, null);
    }

    private object TextValueGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        return e.Node.Text != null ? e.Node.Text : "";
    }
    private object TextValueSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        e.Node.Text = JsValueOps.ToStringValue(a.Get(0));
        MarkChanged();
        return JsUndefined.Instance;
    }

    // ---- event prototype --------------------------------------------------

    private void BuildEventPrototype()
    {
        DefineAccessor(EventPrototype, "type", EvType, null);
        DefineAccessor(EventPrototype, "target", EvTarget, null);
        DefineAccessor(EventPrototype, "currentTarget", EvCurrentTarget, null);
        DefineAccessor(EventPrototype, "bubbles", EvBubbles, null);
        DefineAccessor(EventPrototype, "defaultPrevented", EvDefaultPrevented, null);
        DefineOn(EventPrototype, "preventDefault", EvPreventDefault, 0);
        DefineOn(EventPrototype, "stopPropagation", EvStopPropagation, 0);
        DefineOn(EventPrototype, "stopImmediatePropagation", EvStopImmediate, 0);
    }

    private static JsEventObject EV(JsCallArgs a)
    {
        JsEventObject ev = a.ThisValue as JsEventObject;
        if (ev == null) throw JsErr.Type("receiver is not an Event");
        return ev;
    }
    private object EvType(JsCallArgs a) { return EV(a).Type; }
    private object EvTarget(JsCallArgs a) { object t = EV(a).Target; return t != null ? t : JsNull.Instance; }
    private object EvCurrentTarget(JsCallArgs a) { object t = EV(a).CurrentTarget; return t != null ? t : JsNull.Instance; }
    private object EvBubbles(JsCallArgs a) { return EV(a).Bubbles; }
    private object EvDefaultPrevented(JsCallArgs a) { return EV(a).DefaultPrevented; }
    private object EvPreventDefault(JsCallArgs a) { EV(a).DefaultPrevented = true; return JsUndefined.Instance; }
    private object EvStopPropagation(JsCallArgs a) { EV(a).PropagationStopped = true; return JsUndefined.Instance; }
    private object EvStopImmediate(JsCallArgs a) { JsEventObject ev = EV(a); ev.StopImmediate = true; ev.PropagationStopped = true; return JsUndefined.Instance; }

    // ---- document ---------------------------------------------------------

    private void BuildDocument(JsObject doc)
    {
        DefineAccessor(doc, "documentElement", DocDocumentElement, null);
        DefineAccessor(doc, "body", DocBody, null);
        DefineAccessor(doc, "head", DocHead, null);
        DefineAccessor(doc, "title", DocTitleGet, DocTitleSet);
        DefineAccessor(doc, "readyState", DocReadyState, null);
        DefineAccessor(doc, "nodeType", DocNodeType, null);
        DefineAccessor(doc, "URL", DocUrl, null);
        DefineAccessor(doc, "location", DocLocationGet, DocLocationSet);
        DefineAccessor(doc, "cookie", DocCookieGet, DocCookieSet);

        DefineOn(doc, "querySelector", DocQuerySelector, 1);
        DefineOn(doc, "querySelectorAll", DocQuerySelectorAll, 1);
        DefineOn(doc, "getElementById", DocGetElementById, 1);
        DefineOn(doc, "getElementsByTagName", DocGetElementsByTagName, 1);
        DefineOn(doc, "createElement", DocCreateElement, 1);
        DefineOn(doc, "createTextNode", DocCreateTextNode, 1);
        DefineOn(doc, "addEventListener", DocAddEventListener, 2);
        DefineOn(doc, "removeEventListener", DocRemoveEventListener, 2);
        DefineOn(doc, "dispatchEvent", DocDispatchEvent, 1);
    }

    // a document is not a JsElementObject; it dispatches against Root
    private object DocDocumentElement(JsCallArgs a)
    {
        DomNode html = FindByTag(Root, "html");
        if (html != null) return Wrap(html);
        // fragment documents: first element child of root
        DomNode first = Root.FirstElementChild();
        return first != null ? (object)Wrap(first) : JsNull.Instance;
    }
    private object DocBody(JsCallArgs a)
    {
        DomNode b = FindByTag(Root, "body");
        return b != null ? (object)Wrap(b) : JsNull.Instance;
    }
    private object DocHead(JsCallArgs a)
    {
        DomNode h = FindByTag(Root, "head");
        return h != null ? (object)Wrap(h) : JsNull.Instance;
    }
    private object DocTitleGet(JsCallArgs a)
    {
        DomNode t = FindByTag(Root, "title");
        return t != null ? (object)t.TextContent : (object)"";
    }
    private object DocTitleSet(JsCallArgs a)
    {
        string v = JsValueOps.ToStringValue(a.Get(0));
        DomNode t = FindByTag(Root, "title");
        if (t == null)
        {
            DomNode html = FindByTag(Root, "html");
            DomNode head = FindByTag(Root, "head");
            DomNode host = head != null ? head : (html != null ? html : Root);
            t = new DomNode { NodeType = DomNode.Element, Tag = "title" };
            host.Append(t);
        }
        t.RemoveAllChildren();
        if (v != null && v.Length > 0) t.AddText(v);
        MarkChanged();
        return JsUndefined.Instance;
    }
    private object DocReadyState(JsCallArgs a) { return ReadyState; }
    private object DocNodeType(JsCallArgs a) { return (double)9; }

    private object DocQuerySelector(JsCallArgs a)
    {
        List<CssSelector> sels = CssSheet.ParseSelectors(JsValueOps.ToStringValue(a.Get(0)));
        DomNode hit = CssSheet.QuerySelector(Root, sels);
        return WrapOrJsNull(hit);
    }
    private object DocQuerySelectorAll(JsCallArgs a)
    {
        List<CssSelector> sels = CssSheet.ParseSelectors(JsValueOps.ToStringValue(a.Get(0)));
        List<DomNode> hits = new List<DomNode>();
        CssSheet.QuerySelectorAll(Root, sels, hits);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }
    private object DocGetElementById(JsCallArgs a)
    {
        string id = JsValueOps.ToStringValue(a.Get(0));
        return WrapOrJsNull(FindById(Root, id));
    }
    private object DocGetElementsByTagName(JsCallArgs a)
    {
        string tag = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        List<DomNode> hits = new List<DomNode>();
        CollectByTag(Root, tag, hits);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }
    private object DocCreateElement(JsCallArgs a)
    {
        string tag = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        DomNode n = new DomNode { NodeType = DomNode.Element, Tag = tag };
        return Wrap(n);
    }
    private object DocCreateTextNode(JsCallArgs a)
    {
        string t = JsValueOps.ToStringValue(a.Get(0));
        DomNode n = new DomNode { NodeType = DomNode.TextNode, Text = t };
        return Wrap(n);
    }
    private object DocAddEventListener(JsCallArgs a) { AddListener(documentListeners, a); return JsUndefined.Instance; }
    private object DocRemoveEventListener(JsCallArgs a) { RemoveListener(documentListeners, a); return JsUndefined.Instance; }
    private object DocDispatchEvent(JsCallArgs a)
    {
        JsEventObject ev = a.Get(0) as JsEventObject;
        if (ev == null) throw JsErr.Type("dispatchEvent expects an Event");
        DispatchExisting(Root, ev);
        return !ev.DefaultPrevented;
    }

    // window-level listeners
    private object WindowAddListener(JsCallArgs a) { AddListener(windowListeners, a); return JsUndefined.Instance; }
    private object WindowRemoveListener(JsCallArgs a) { RemoveListener(windowListeners, a); return JsUndefined.Instance; }

    // ---- public event dispatch -------------------------------------------

    private string ReadyState = "complete";

    /// <summary>Create + dispatch a synthetic event of the given type at a node.
    /// Returns true if the default was NOT prevented (host should proceed).</summary>
    public bool Dispatch(DomNode target, string type)
    {
        JsEventObject ev = new JsEventObject(EventPrototype);
        ev.Type = type;
        ev.Target = Wrap(target);
        return DispatchExisting(target, ev);
    }

    /// <summary>Dispatch a pre-built event, bubbling from target up to document then window.</summary>
    public bool DispatchExisting(DomNode target, JsEventObject ev)
    {
        ev.PropagationStopped = false;
        ev.StopImmediate = false;

        DomNode n = target;
        while (n != null && n != Root && !ev.StopImmediate && !ev.PropagationStopped)
        {
            JsElementObject w = Wrap(n);
            ev.CurrentTarget = w;
            FireListeners(w, w.Listeners, ev);
            n = n.Parent;
        }
        if (!ev.StopImmediate && !ev.PropagationStopped)
        {
            ev.CurrentTarget = Document;
            FireListeners(Document, documentListeners, ev);
        }
        if (!ev.StopImmediate && !ev.PropagationStopped && ev.Bubbles)
        {
            ev.CurrentTarget = Realm.Global;
            FireListeners(Realm.Global, windowListeners, ev);
        }
        return !ev.DefaultPrevented;
    }

    private void FireListeners(JsObject obj, Dictionary<string, List<JsFunction>> store, JsEventObject ev)
    {
        List<JsFunction> fns;
        if (store != null && store.TryGetValue(ev.Type, out fns))
        {
            List<JsFunction> copy = new List<JsFunction>(fns);
            for (int i = 0; i < copy.Count; i++)
            {
                TryCall(copy[i], obj, ev);
                if (ev.StopImmediate) return;
            }
        }
        // inline handler property (el.onclick = fn)
        JsProperty p;
        if (obj.TryGetRaw("on" + ev.Type, out p) && p.Kind == JsPropKind.Data && p.Value is JsFunction)
        {
            TryCall((JsFunction)p.Value, obj, ev);
        }
    }

    private void TryCall(JsFunction fn, object thisVal, JsEventObject ev)
    {
        try { Interp.Call(fn, thisVal, new object[] { ev }, false); }
        catch (JsThrown) { /* listener error must not break dispatch */ }
        catch (Exception) { }
    }

    private void AddListener(Dictionary<string, List<JsFunction>> store, JsCallArgs a)
    {
        string type = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        JsFunction fn = a.Get(1) as JsFunction;
        if (fn == null) return;
        List<JsFunction> fns;
        if (!store.TryGetValue(type, out fns)) { fns = new List<JsFunction>(); store[type] = fns; }
        if (!fns.Contains(fn)) fns.Add(fn);
    }

    private void RemoveListener(Dictionary<string, List<JsFunction>> store, JsCallArgs a)
    {
        string type = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        JsFunction fn = a.Get(1) as JsFunction;
        List<JsFunction> fns;
        if (fn == null || !store.TryGetValue(type, out fns)) return;
        fns.Remove(fn);
    }

    // ---- ready events -----------------------------------------------------

    /// <summary>Fire DOMContentLoaded (document + window) then load (window).</summary>
    public void FireReadyEvents()
    {
        ReadyState = "interactive";
        JsEventObject dc = new JsEventObject(EventPrototype);
        dc.Type = "DOMContentLoaded";
        dc.Target = Document;
        dc.CurrentTarget = Document;
        FireListeners(Document, documentListeners, dc);
        if (dc.Bubbles && !dc.PropagationStopped && !dc.StopImmediate)
        {
            dc.CurrentTarget = Realm.Global;
            FireListeners(Realm.Global, windowListeners, dc);
        }

        ReadyState = "complete";
        JsEventObject load = new JsEventObject(EventPrototype);
        load.Type = "load";
        load.Target = Realm.Global;
        load.CurrentTarget = Realm.Global;
        FireListeners(Realm.Global, windowListeners, load);
    }

    // ---- style object -----------------------------------------------------

    private JsStyleObject BuildStyleObject(JsElementObject owner)
    {
        JsStyleObject style = new JsStyleObject(StylePrototype, owner);
        DefineAccessor(style, "cssText", StyleCssTextGet, StyleCssTextSet);
        DefineOn(style, "setProperty", StyleSetProperty, 2);
        DefineOn(style, "getPropertyValue", StyleGetProperty, 1);
        DefineOn(style, "removeProperty", StyleRemoveProperty, 1);
        for (int i = 0; i < StyleProps.Length; i++)
        {
            string js = StyleProps[i];
            string css = CamelToKebab(js);
            DefineAccessor(style, js, MakeStyleGetter(css), MakeStyleSetter(css));
        }
        return style;
    }

    private JsNativeFn MakeStyleGetter(string cssName)
    {
        return delegate (JsCallArgs a)
        {
            JsStyleObject s = a.ThisValue as JsStyleObject;
            if (s == null) return "";
            return StyleAttr.Get(s.Owner.Node.StyleAttr, cssName);
        };
    }
    private JsNativeFn MakeStyleSetter(string cssName)
    {
        return delegate (JsCallArgs a)
        {
            JsStyleObject s = a.ThisValue as JsStyleObject;
            if (s == null) return JsUndefined.Instance;
            string v = JsValueOps.ToStringValue(a.Get(0));
            WriteStyleAttr(s.Owner.Node, StyleAttr.Set(s.Owner.Node.StyleAttr, cssName, v));
            MarkChanged();
            return JsUndefined.Instance;
        };
    }

    // keep Node.StyleAttr and Attrs["style"] in sync (they are one value in the DOM)
    private static void WriteStyleAttr(DomNode n, string value)
    {
        n.StyleAttr = value;
        if (n.Attrs == null) n.Attrs = new Dictionary<string, string>();
        n.Attrs["style"] = value;
    }

    private object StyleCssTextGet(JsCallArgs a)
    {
        JsStyleObject s = a.ThisValue as JsStyleObject;
        if (s == null) return "";
        return s.Owner.Node.StyleAttr != null ? s.Owner.Node.StyleAttr : "";
    }
    private object StyleCssTextSet(JsCallArgs a)
    {
        JsStyleObject s = a.ThisValue as JsStyleObject;
        if (s == null) return JsUndefined.Instance;
        WriteStyleAttr(s.Owner.Node, JsValueOps.ToStringValue(a.Get(0)));
        MarkChanged();
        return JsUndefined.Instance;
    }
    private object StyleSetProperty(JsCallArgs a)
    {
        JsStyleObject s = a.ThisValue as JsStyleObject;
        if (s == null) return JsUndefined.Instance;
        string name = JsValueOps.ToStringValue(a.Get(0));
        string val = JsValueOps.ToStringValue(a.Get(1));
        WriteStyleAttr(s.Owner.Node, StyleAttr.Set(s.Owner.Node.StyleAttr, name, val));
        MarkChanged();
        return JsUndefined.Instance;
    }
    private object StyleGetProperty(JsCallArgs a)
    {
        JsStyleObject s = a.ThisValue as JsStyleObject;
        if (s == null) return "";
        string name = JsValueOps.ToStringValue(a.Get(0));
        return StyleAttr.Get(s.Owner.Node.StyleAttr, name);
    }
    private object StyleRemoveProperty(JsCallArgs a)
    {
        JsStyleObject s = a.ThisValue as JsStyleObject;
        if (s == null) return JsUndefined.Instance;
        string name = JsValueOps.ToStringValue(a.Get(0));
        string old = StyleAttr.Get(s.Owner.Node.StyleAttr, name);
        WriteStyleAttr(s.Owner.Node, StyleAttr.Remove(s.Owner.Node.StyleAttr, name));
        MarkChanged();
        return old;
    }

    // ---- classList object -------------------------------------------------

    private JsObject BuildClassList(JsElementObject owner)
    {
        JsObject cl = Realm.NewObject();
        // capture the owner in closures
        JsElementObject o = owner;
        DefineOn(cl, "add", delegate (JsCallArgs a)
        {
            for (int i = 0; i < a.ArgCount; i++) AddClass(o.Node, JsValueOps.ToStringValue(a.Get(i)));
            MarkChanged();
            return JsUndefined.Instance;
        }, 1);
        DefineOn(cl, "remove", delegate (JsCallArgs a)
        {
            for (int i = 0; i < a.ArgCount; i++) RemoveClass(o.Node, JsValueOps.ToStringValue(a.Get(i)));
            MarkChanged();
            return JsUndefined.Instance;
        }, 1);
        DefineOn(cl, "toggle", delegate (JsCallArgs a)
        {
            string c = JsValueOps.ToStringValue(a.Get(0));
            bool had = HasClass(o.Node, c);
            if (had) RemoveClass(o.Node, c); else AddClass(o.Node, c);
            MarkChanged();
            return !had;
        }, 1);
        DefineOn(cl, "contains", delegate (JsCallArgs a)
        {
            return HasClass(o.Node, JsValueOps.ToStringValue(a.Get(0)));
        }, 1);
        DefineAccessor(cl, "length", delegate (JsCallArgs a)
        {
            return (double)(o.Node.Classes != null ? o.Node.Classes.Count : 0);
        }, null);
        return cl;
    }

    // ---- static DOM helpers ----------------------------------------------

    /// <summary>Set an attribute, keeping the id/class/style cached fields in sync.</summary>
    public static void SetAttribute(DomNode n, string key, string value)
    {
        if (n.Attrs == null) n.Attrs = new Dictionary<string, string>();
        n.Attrs[key] = value;
        if (key == "id") n.Id = value;
        else if (key == "class") n.Classes = SplitClasses(value);
        else if (key == "style") n.StyleAttr = value;
    }

    private static List<string> SplitClasses(string value)
    {
        List<string> result = new List<string>();
        if (value == null) return result;
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && IsWs(value[i])) i++;
            int start = i;
            while (i < value.Length && !IsWs(value[i])) i++;
            if (i > start) result.Add(value.Substring(start, i - start));
        }
        return result;
    }

    private static bool IsWs(char c)
    {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
    }

    private static void AddClass(DomNode n, string c)
    {
        if (c == null || c.Length == 0) return;
        if (n.Classes == null) n.Classes = new List<string>();
        if (!n.Classes.Contains(c)) n.Classes.Add(c);
        SyncClassAttr(n);
    }
    private static void RemoveClass(DomNode n, string c)
    {
        if (n.Classes != null)
        {
            n.Classes.Remove(c);
            if (n.Classes.Count == 0) n.Classes = null;
        }
        SyncClassAttr(n);
    }
    private static bool HasClass(DomNode n, string c)
    {
        return n.Classes != null && n.Classes.Contains(c);
    }
    private static void SyncClassAttr(DomNode n)
    {
        if (n.Attrs == null) n.Attrs = new Dictionary<string, string>();
        if (n.Classes == null) { n.Attrs.Remove("class"); return; }
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < n.Classes.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(n.Classes[i]);
        }
        n.Attrs["class"] = sb.ToString();
    }

    /// <summary>Replace a node's children with an HTML fragment (innerHTML setter).</summary>
    public static void SetInnerHtml(DomNode node, string html)
    {
        node.RemoveAllChildren();
        if (html == null || html.Length == 0) return;
        DomNode frag = HtmlDom.Parse(html);
        DomNode c = frag.FirstChild;
        while (c != null)
        {
            DomNode next = c.Next;
            node.Append(c);
            c = next;
        }
    }

    /// <summary>Deep or shallow clone of a node (detached).</summary>
    public static DomNode CloneNode(DomNode node, bool deep)
    {
        DomNode copy = new DomNode();
        copy.NodeType = node.NodeType;
        copy.Tag = node.Tag;
        copy.Text = node.Text;
        copy.Id = node.Id;
        copy.StyleAttr = node.StyleAttr;
        if (node.Classes != null) copy.Classes = new List<string>(node.Classes);
        if (node.Attrs != null)
        {
            copy.Attrs = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kv in node.Attrs) copy.Attrs[kv.Key] = kv.Value;
        }
        if (deep)
        {
            DomNode c = node.FirstChild;
            while (c != null) { copy.Append(CloneNode(c, true)); c = c.Next; }
        }
        return copy;
    }

    private static DomNode FindById(DomNode root, string id)
    {
        if (id == null) return null;
        DomNode n = root.FirstChild;
        while (n != null)
        {
            if (n.IsElement)
            {
                if (id == n.Id) return n;
                DomNode hit = FindById(n, id);
                if (hit != null) return hit;
            }
            n = n.Next;
        }
        return null;
    }

    private static DomNode FindByTag(DomNode root, string tag)
    {
        DomNode n = root.FirstChild;
        while (n != null)
        {
            if (n.IsElement)
            {
                if (tag == n.Tag) return n;
                DomNode hit = FindByTag(n, tag);
                if (hit != null) return hit;
            }
            n = n.Next;
        }
        return null;
    }

    private static void CollectByTag(DomNode root, string tag, List<DomNode> outNodes)
    {
        DomNode n = root.FirstChild;
        while (n != null)
        {
            if (n.IsElement)
            {
                if (tag == "*" || tag == n.Tag) outNodes.Add(n);
                CollectByTag(n, tag, outNodes);
            }
            n = n.Next;
        }
    }

    private static string CamelToKebab(string s)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= 'A' && c <= 'Z') { sb.Append('-'); sb.Append((char)(c + 32)); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // `A` = assert element receiver (used where only elements make sense)
    private static JsElementObject A(JsCallArgs a)
    {
        JsElementObject e = a.ThisValue as JsElementObject;
        if (e == null) throw JsErr.Type("Illegal invocation");
        return e;
    }

    private JsElementObject RequireNode(object v)
    {
        JsElementObject n = v as JsElementObject;
        if (n == null) throw JsErr.Type("expected a DOM node");
        return n;
    }
}

/// <summary>Reads/writes declarations inside a style attribute string.</summary>
public static class StyleAttr
{
    public static string Get(string attr, string cssName)
    {
        if (string.IsNullOrEmpty(attr)) return "";
        int i = 0;
        while (i < attr.Length)
        {
            while (i < attr.Length && IsWs(attr[i])) i++;
            int ns = i;
            while (i < attr.Length && attr[i] != ':' && attr[i] != ';') i++;
            if (i >= attr.Length) break;
            if (attr[i] != ':') { i++; continue; }
            string pname = attr.Substring(ns, i - ns).Trim();
            i++; // skip ':'
            int vs = i;
            while (i < attr.Length && attr[i] != ';') i++;
            string val = attr.Substring(vs, i - vs).Trim();
            if (FoldEq(pname, cssName)) return val;
            if (i < attr.Length && attr[i] == ';') i++;
        }
        return "";
    }

    public static string Set(string attr, string cssName, string value)
    {
        StringBuilder sb = new StringBuilder();
        bool replaced = false;
        int i = 0;
        if (attr != null)
        {
            while (i < attr.Length)
            {
                while (i < attr.Length && IsWs(attr[i])) i++;
                int ns = i;
                while (i < attr.Length && attr[i] != ':' && attr[i] != ';') i++;
                if (i >= attr.Length) break;
                if (attr[i] != ':') { i++; continue; }
                string pname = attr.Substring(ns, i - ns).Trim();
                i++;
                int vs = i;
                while (i < attr.Length && attr[i] != ';') i++;
                string val = attr.Substring(vs, i - vs).Trim();
                if (FoldEq(pname, cssName))
                {
                    if (!replaced)
                    {
                        AppendDecl(sb, cssName, value);
                        replaced = true;
                    }
                }
                else AppendDecl(sb, pname, val);
                if (i < attr.Length && attr[i] == ';') i++;
            }
        }
        if (!replaced) AppendDecl(sb, cssName, value);
        return sb.ToString();
    }

    public static string Remove(string attr, string cssName)
    {
        StringBuilder sb = new StringBuilder();
        if (attr != null)
        {
            int i = 0;
            while (i < attr.Length)
            {
                while (i < attr.Length && IsWs(attr[i])) i++;
                int ns = i;
                while (i < attr.Length && attr[i] != ':' && attr[i] != ';') i++;
                if (i >= attr.Length) break;
                if (attr[i] != ':') { i++; continue; }
                string pname = attr.Substring(ns, i - ns).Trim();
                i++;
                int vs = i;
                while (i < attr.Length && attr[i] != ';') i++;
                string val = attr.Substring(vs, i - vs).Trim();
                if (!FoldEq(pname, cssName)) AppendDecl(sb, pname, val);
                if (i < attr.Length && attr[i] == ';') i++;
            }
        }
        return sb.ToString();
    }

    private static void AppendDecl(StringBuilder sb, string name, string value)
    {
        if (sb.Length > 0) sb.Append(' ');
        sb.Append(name).Append(": ").Append(value);
    }

    private static bool IsWs(char c)
    {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';
    }

    private static bool FoldEq(string a, string b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            char x = a[i], y = b[i];
            if (x >= 'A' && x <= 'Z') x += (char)32;
            if (y >= 'A' && y <= 'Z') y += (char)32;
            if (x != y) return false;
        }
        return true;
    }
}
