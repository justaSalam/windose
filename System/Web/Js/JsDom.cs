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
    public JsDatasetObject DatasetCache;// lazily created `el.dataset`
    public JsObject AttributesCache;   // lazily created `el.attributes`

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

/// <summary>`el.dataset` — maps camelCase properties to data-* attributes.</summary>
public sealed class JsDatasetObject : JsObject
{
    public JsElementObject Owner;

    public JsDatasetObject(JsObject prototype, JsElementObject owner) : base(prototype)
    {
        Owner = owner;
    }

    public override string ClassTag { get { return "DOMStringMap"; } }

    public override bool GetProperty(string key, out object value)
    {
        string attr = "data-" + CamelToKebab(key);
        string v = Owner.Node.GetAttr(attr);
        if (v != null)
        {
            value = v;
            return true;
        }
        return base.GetProperty(key, out value);
    }

    public override void SetProperty(string key, object value)
    {
        string attr = "data-" + CamelToKebab(key);
        string v = JsValueOps.ToStringValue(value);
        JsDom.SetAttribute(Owner.Node, attr, v);
        Owner.Dom.MarkChanged();
    }

    public override bool DeleteProperty(string key)
    {
        string attr = "data-" + CamelToKebab(key);
        if (Owner.Node.Attrs != null && Owner.Node.Attrs.Remove(attr))
        {
            Owner.Dom.MarkChanged();
            return true;
        }
        return base.DeleteProperty(key);
    }

    public static string CamelToKebab(string s)
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

    public object Detail = JsUndefined.Instance;
    public double TimeStamp = 0;
    public double ClientX = 0, ClientY = 0, PageX = 0, PageY = 0, ScreenX = 0, ScreenY = 0;
    public int Button = 0, Buttons = 0;
    public bool AltKey = false, CtrlKey = false, ShiftKey = false, MetaKey = false;
    public string Key = "";
    public string Code = "";
    public int KeyCode = 0, Which = 0, CharCode = 0;

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

    public override bool GetProperty(string key, out object value)
    {
        if (base.GetProperty(key, out value)) return true;
        if (Owner != null && Owner.Node != null && !string.IsNullOrEmpty(key))
        {
            string cssName = JsDom.CamelToKebab(key);
            string v = StyleAttr.Get(Owner.Node.StyleAttr, cssName);
            value = v != null ? v : "";
            return true;
        }
        value = JsUndefined.Instance;
        return false;
    }

    public override void SetProperty(string key, object value)
    {
        if (base.HasProperty(key))
        {
            base.SetProperty(key, value);
            return;
        }
        if (Owner != null && Owner.Node != null && !string.IsNullOrEmpty(key))
        {
            string cssName = JsDom.CamelToKebab(key);
            string sVal = JsValueOps.ToStringValue(value);
            Owner.Dom.SetStyleAttrDirect(Owner.Node, cssName, sVal);
            return;
        }
        base.SetProperty(key, value);
    }
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

    /// <summary>The element the host currently gives the caret to (document.activeElement).</summary>
    public DomNode ActiveElementNode;

    /// <summary>element.focus()/blur(): the host moves its caret to the node (or null).</summary>
    public delegate void FocusRequestDelegate(DomNode node);
    public FocusRequestDelegate OnFocusRequest;

    /// <summary>form.submit()/form.reset(): the host performs the submission or reset.</summary>
    public delegate void FormSubmitDelegate(DomNode form, DomNode submitter);
    public delegate void FormResetDelegate(DomNode form);
    public FormSubmitDelegate OnFormSubmit;
    public FormResetDelegate OnFormReset;

    // computed-style queries (window.getComputedStyle): the host pushes the
    // document stylesheet + viewport via SetStylesheet; the sheet is parsed
    // lazily and only re-parsed when that changes.
    public JsObject ComputedPrototype;
    public JsObject CanvasContextPrototype;
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

    public string LocationHref
    {
        get
        {
            JsEventLoop loop = Interp != null ? Interp.Loop : null;
            return loop != null && loop.LocationHref != null ? loop.LocationHref : "";
        }
    }

    public JsDom(JsInterpreter interp, DomNode root)
    {
        Interp = interp;
        if (interp != null) interp.Dom = this;
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

        // event constructors
        Realm.Global.Define("Event", JsProperty.Data(
            Realm.MakeCtor("Event", EventCtor, 1, EventPrototype)));
        Realm.Global.Define("CustomEvent", JsProperty.Data(
            Realm.MakeCtor("CustomEvent", CustomEventCtor, 1, Realm.NewObject(EventPrototype))));
        Realm.Global.Define("MouseEvent", JsProperty.Data(
            Realm.MakeCtor("MouseEvent", MouseEventCtor, 1, Realm.NewObject(EventPrototype))));
        Realm.Global.Define("KeyboardEvent", JsProperty.Data(
            Realm.MakeCtor("KeyboardEvent", KeyboardEventCtor, 1, Realm.NewObject(EventPrototype))));
        Realm.Global.Define("FocusEvent", JsProperty.Data(
            Realm.MakeCtor("FocusEvent", EventCtor, 1, Realm.NewObject(EventPrototype))));
        Realm.Global.Define("UIEvent", JsProperty.Data(
            Realm.MakeCtor("UIEvent", EventCtor, 1, Realm.NewObject(EventPrototype))));

        BuildDomConstructors();
    }

    private object EventCtor(JsCallArgs a)
    {
        JsEventObject ev = new JsEventObject(EventPrototype);
        ev.Type = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
        ev.TimeStamp = (double)DateTime.UtcNow.Ticks / 10000.0;
        if (a.ArgCount > 1 && a.Get(1) is JsObject opts)
        {
            if (opts.GetProperty("bubbles", out object b)) ev.Bubbles = JsValueOps.ToBoolean(b);
            if (opts.GetProperty("cancelable", out object c)) ev.DefaultPrevented = false;
        }
        return ev;
    }

    private object CustomEventCtor(JsCallArgs a)
    {
        JsEventObject ev = new JsEventObject(EventPrototype);
        ev.Type = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
        ev.TimeStamp = (double)DateTime.UtcNow.Ticks / 10000.0;
        if (a.ArgCount > 1 && a.Get(1) is JsObject opts)
        {
            if (opts.GetProperty("bubbles", out object b)) ev.Bubbles = JsValueOps.ToBoolean(b);
            if (opts.GetProperty("detail", out object d)) ev.Detail = d;
        }
        return ev;
    }

    private object MouseEventCtor(JsCallArgs a)
    {
        JsEventObject ev = (JsEventObject)EventCtor(a);
        if (a.ArgCount > 1 && a.Get(1) is JsObject opts)
        {
            if (opts.GetProperty("clientX", out object cx)) ev.ClientX = JsValueOps.ToNumber(cx);
            if (opts.GetProperty("clientY", out object cy)) ev.ClientY = JsValueOps.ToNumber(cy);
            if (opts.GetProperty("pageX", out object px)) ev.PageX = JsValueOps.ToNumber(px);
            if (opts.GetProperty("pageY", out object py)) ev.PageY = JsValueOps.ToNumber(py);
            if (opts.GetProperty("button", out object btn)) ev.Button = (int)JsValueOps.ToNumber(btn);
            if (opts.GetProperty("buttons", out object btns)) ev.Buttons = (int)JsValueOps.ToNumber(btns);
            if (opts.GetProperty("altKey", out object ak)) ev.AltKey = JsValueOps.ToBoolean(ak);
            if (opts.GetProperty("ctrlKey", out object ck)) ev.CtrlKey = JsValueOps.ToBoolean(ck);
            if (opts.GetProperty("shiftKey", out object sk)) ev.ShiftKey = JsValueOps.ToBoolean(sk);
            if (opts.GetProperty("metaKey", out object mk)) ev.MetaKey = JsValueOps.ToBoolean(mk);
        }
        return ev;
    }

    private object KeyboardEventCtor(JsCallArgs a)
    {
        JsEventObject ev = (JsEventObject)EventCtor(a);
        if (a.ArgCount > 1 && a.Get(1) is JsObject opts)
        {
            if (opts.GetProperty("key", out object k)) ev.Key = JsValueOps.ToStringValue(k);
            if (opts.GetProperty("code", out object c)) ev.Code = JsValueOps.ToStringValue(c);
            if (opts.GetProperty("keyCode", out object kc)) ev.KeyCode = (int)JsValueOps.ToNumber(kc);
            if (opts.GetProperty("which", out object wh)) ev.Which = (int)JsValueOps.ToNumber(wh);
            if (opts.GetProperty("altKey", out object ak)) ev.AltKey = JsValueOps.ToBoolean(ak);
            if (opts.GetProperty("ctrlKey", out object ck)) ev.CtrlKey = JsValueOps.ToBoolean(ck);
            if (opts.GetProperty("shiftKey", out object sk)) ev.ShiftKey = JsValueOps.ToBoolean(sk);
            if (opts.GetProperty("metaKey", out object mk)) ev.MetaKey = JsValueOps.ToBoolean(mk);
        }
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

    public void MarkChanged()
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

    private object DocActiveElement(JsCallArgs a)
    {
        // the control the host currently gives the caret to (or <body>/null)
        return ActiveElementNode != null ? (object)Wrap(ActiveElementNode) : JsNull.Instance;
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
        DefineAccessor(ElementPrototype, "ownerDocument", ElOwnerDocument, null);
        DefineAccessor(ElementPrototype, "dataset", ElDataset, null);
        DefineAccessor(ElementPrototype, "attributes", ElAttributes, null);
        DefineAccessor(ElementPrototype, "innerText", ElInnerTextGet, ElInnerTextSet);
        DefineAccessor(ElementPrototype, "childElementCount", ElChildElementCount, null);

        // content
        DefineAccessor(ElementPrototype, "innerHTML", ElInnerGet, ElInnerSet);
        DefineAccessor(ElementPrototype, "outerHTML", ElOuterGet, null);
        DefineAccessor(ElementPrototype, "textContent", ElTextGet, ElTextSet);
        DefineAccessor(ElementPrototype, "documentElement", ElDocumentElement, null);
        DefineAccessor(ElementPrototype, "body", ElBody, null);
        DefineAccessor(ElementPrototype, "head", ElHead, null);

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

        // geometry & layout
        DefineAccessor(ElementPrototype, "offsetWidth", ElOffsetWidth, null);
        DefineAccessor(ElementPrototype, "offsetHeight", ElOffsetHeight, null);
        DefineAccessor(ElementPrototype, "offsetLeft", ElOffsetLeft, null);
        DefineAccessor(ElementPrototype, "offsetTop", ElOffsetTop, null);
        DefineAccessor(ElementPrototype, "offsetParent", ElOffsetParent, null);
        DefineAccessor(ElementPrototype, "clientWidth", ElClientWidth, null);
        DefineAccessor(ElementPrototype, "clientHeight", ElClientHeight, null);
        DefineAccessor(ElementPrototype, "clientLeft", ElClientLeft, null);
        DefineAccessor(ElementPrototype, "clientTop", ElClientTop, null);
        DefineAccessor(ElementPrototype, "scrollWidth", ElScrollWidth, null);
        DefineAccessor(ElementPrototype, "scrollHeight", ElScrollHeight, null);
        DefineAccessor(ElementPrototype, "scrollLeft", ElScrollLeft, ElScrollLeftSet);
        DefineAccessor(ElementPrototype, "scrollTop", ElScrollTop, ElScrollTopSet);

        // attributes
        DefineOn(ElementPrototype, "getAttribute", ElGetAttribute, 1);
        DefineOn(ElementPrototype, "setAttribute", ElSetAttribute, 2);
        DefineOn(ElementPrototype, "removeAttribute", ElRemoveAttribute, 1);
        DefineOn(ElementPrototype, "hasAttribute", ElHasAttribute, 1);
        DefineOn(ElementPrototype, "getAttributeNames", ElGetAttributeNames, 0);
        DefineOn(ElementPrototype, "toggleAttribute", ElToggleAttribute, 1);

        // mutation
        DefineOn(ElementPrototype, "appendChild", ElAppendChild, 1);
        DefineOn(ElementPrototype, "removeChild", ElRemoveChild, 1);
        DefineOn(ElementPrototype, "insertBefore", ElInsertBefore, 2);
        DefineOn(ElementPrototype, "remove", ElRemove, 0);
        DefineOn(ElementPrototype, "cloneNode", ElCloneNode, 1);
        DefineOn(ElementPrototype, "replaceChild", ElReplaceChild, 2);
        DefineOn(ElementPrototype, "hasChildNodes", ElHasChildNodes, 0);
        DefineOn(ElementPrototype, "append", ElAppend, 1);
        DefineOn(ElementPrototype, "prepend", ElPrepend, 1);
        DefineOn(ElementPrototype, "before", ElBefore, 1);
        DefineOn(ElementPrototype, "after", ElAfter, 1);
        DefineOn(ElementPrototype, "replaceWith", ElReplaceWith, 1);

        // queries
        DefineOn(ElementPrototype, "querySelector", ElQuerySelector, 1);
        DefineOn(ElementPrototype, "querySelectorAll", ElQuerySelectorAll, 1);
        DefineOn(ElementPrototype, "getElementById", ElGetElementById, 1);
        DefineOn(ElementPrototype, "getElementsByClassName", ElGetElementsByClassName, 1);
        DefineOn(ElementPrototype, "getElementsByTagName", ElGetElementsByTagName, 1);
        DefineOn(ElementPrototype, "matches", ElMatches, 1);
        DefineOn(ElementPrototype, "closest", ElClosest, 1);
        DefineOn(ElementPrototype, "contains", ElContains, 1);
        DefineOn(ElementPrototype, "getBoundingClientRect", ElGetBoundingClientRect, 0);
        DefineOn(ElementPrototype, "scrollIntoView", ElScrollIntoView, 0);

        // events
        DefineOn(ElementPrototype, "addEventListener", ElAddEventListener, 2);
        DefineOn(ElementPrototype, "removeEventListener", ElRemoveEventListener, 2);
        DefineOn(ElementPrototype, "dispatchEvent", ElDispatchEvent, 1);
        DefineOn(ElementPrototype, "click", ElClick, 0);

        // form controls (stage 3)
        DefineAccessor(ElementPrototype, "checked", ElCheckedGet, ElCheckedSet);
        DefineAccessor(ElementPrototype, "defaultChecked", ElCheckedGet, null);
        DefineAccessor(ElementPrototype, "disabled", ElDisabledGet, ElDisabledSet);
        DefineAccessor(ElementPrototype, "readOnly", ElReadonlyGet, null);
        DefineAccessor(ElementPrototype, "type", ElTypeGet, null);
        DefineAccessor(ElementPrototype, "name", ElNameGet, ElNameSet);
        DefineAccessor(ElementPrototype, "defaultValue", ElDefaultValueGet, null);
        DefineAccessor(ElementPrototype, "placeholder", ElPlaceholderGet, null);
        DefineAccessor(ElementPrototype, "selectedIndex", ElSelectedIndexGet, ElSelectedIndexSet);
        DefineAccessor(ElementPrototype, "options", ElOptions, null);
        DefineAccessor(ElementPrototype, "length", ElOptionsLength, null);
        DefineAccessor(ElementPrototype, "form", ElForm, null);
        DefineAccessor(ElementPrototype, "elements", ElFormElements, null);
        DefineAccessor(ElementPrototype, "selectionStart", ElSelectionStart, null);
        DefineAccessor(ElementPrototype, "selectionEnd", ElSelectionEnd, null);
        DefineOn(ElementPrototype, "focus", ElFocus, 0);
        DefineOn(ElementPrototype, "blur", ElBlur, 0);
        DefineOn(ElementPrototype, "select", ElSelectText, 0);
        DefineOn(ElementPrototype, "setSelectionRange", ElSetSelectionRange, 2);
        DefineOn(ElementPrototype, "submit", ElSubmit, 0);
        DefineOn(ElementPrototype, "reset", ElReset, 0);
        DefineOn(ElementPrototype, "clickSubmit", ElClickSubmit, 0);

        // HTML attributes
        DefineAccessor(ElementPrototype, "src", ElSrcGet, ElSrcSet);
        DefineAccessor(ElementPrototype, "href", ElHrefGet, ElHrefSet);
        DefineAccessor(ElementPrototype, "title", ElTitleGet, ElTitleSet);
        DefineAccessor(ElementPrototype, "alt", ElAltGet, ElAltSet);
        DefineAccessor(ElementPrototype, "target", ElTargetGet, ElTargetSet);
        DefineAccessor(ElementPrototype, "rel", ElRelGet, ElRelSet);
        DefineAccessor(ElementPrototype, "hidden", ElHiddenGet, ElHiddenSet);
        DefineAccessor(ElementPrototype, "tabIndex", ElTabIndexGet, ElTabIndexSet);
        DefineAccessor(ElementPrototype, "action", ElActionGet, ElActionSet);
        DefineAccessor(ElementPrototype, "method", ElMethodGet, ElMethodSet);
        DefineAccessor(ElementPrototype, "autocomplete", ElAutocompleteGet, ElAutocompleteSet);
        DefineAccessor(ElementPrototype, "required", ElRequiredGet, ElRequiredSet);
        DefineAccessor(ElementPrototype, "pattern", ElPatternGet, ElPatternSet);
        DefineAccessor(ElementPrototype, "min", ElMinGet, ElMinSet);
        DefineAccessor(ElementPrototype, "max", ElMaxGet, ElMaxSet);
        DefineAccessor(ElementPrototype, "step", ElStepGet, ElStepSet);
        DefineAccessor(ElementPrototype, "accept", ElAcceptGet, ElAcceptSet);
        DefineAccessor(ElementPrototype, "rows", ElRowsGet, ElRowsSet);
        DefineAccessor(ElementPrototype, "cols", ElColsGet, ElColsSet);
        DefineAccessor(ElementPrototype, "width", ElWidthGet, ElWidthSet);
        DefineAccessor(ElementPrototype, "height", ElHeightGet, ElHeightSet);
        DefineAccessor(ElementPrototype, "contentEditable", ElContentEditableGet, ElContentEditableSet);
        DefineAccessor(ElementPrototype, "isContentEditable", ElIsContentEditableGet, null);
        DefineAccessor(ElementPrototype, "autofocus", ElAutofocusGet, ElAutofocusSet);
        DefineAccessor(ElementPrototype, "multiple", ElMultipleGet, ElMultipleSet);
        DefineAccessor(ElementPrototype, "lang", ElLangGet, ElLangSet);
        DefineAccessor(ElementPrototype, "dir", ElDirGet, ElDirSet);

        // mutation & insertion
        DefineOn(ElementPrototype, "insertAdjacentHTML", ElInsertAdjacentHTML, 2);
        DefineOn(ElementPrototype, "insertAdjacentElement", ElInsertAdjacentElement, 2);
        DefineOn(ElementPrototype, "insertAdjacentText", ElInsertAdjacentText, 2);
        DefineOn(ElementPrototype, "compareDocumentPosition", ElCompareDocumentPosition, 1);
        DefineOn(ElementPrototype, "isSameNode", ElIsSameNode, 1);
        DefineOn(ElementPrototype, "isEqualNode", ElIsEqualNode, 1);
        DefineOn(ElementPrototype, "normalize", ElNormalize, 0);

        // canvas methods
        DefineOn(ElementPrototype, "getContext", ElGetContext, 1);
        DefineOn(ElementPrototype, "toDataURL", ElToDataURL, 0);
        DefineOn(ElementPrototype, "toBlob", ElToBlob, 1);

        // shadow DOM & web components & animations
        DefineOn(ElementPrototype, "attachShadow", ElAttachShadow, 1);
        DefineAccessor(ElementPrototype, "shadowRoot", ElShadowRoot, null);
        DefineAccessor(ElementPrototype, "slot", ElSlotGet, ElSlotSet);
        DefineOn(ElementPrototype, "getRootNode", ElGetRootNode, 0);
        DefineOn(ElementPrototype, "animate", ElAnimate, 2);
        DefineAccessor(ElementPrototype, "content", ElTemplateContentGet, null);
    }

    // ---- form-control accessors (stage 3) ---------------------------------

    private object ElCheckedGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        return e.Node.GetAttr("checked") != null;
    }

    private object ElCheckedSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        bool on = JsValueOps.ToBoolean(a.Get(0));
        bool was = e.Node.GetAttr("checked") != null;
        if (on == was) return JsUndefined.Instance;
        if (on) SetAttribute(e.Node, "checked", "");
        else e.Node.Attrs.Remove("checked");
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElDisabledGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("disabled") != null; }
    private object ElReadonlyGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("readonly") != null; }

    private object ElDisabledSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (JsValueOps.ToBoolean(a.Get(0))) SetAttribute(e.Node, "disabled", "");
        else e.Node.Attrs.Remove("disabled");
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElTypeGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string tag = e.Node.Tag;
        if (tag == "input") { string t = e.Node.GetAttr("type"); return t != null && t.Length > 0 ? t.ToLowerInvariant() : "text"; }
        if (tag == "button") { string t = e.Node.GetAttr("type"); return t != null && t.Length > 0 ? t.ToLowerInvariant() : "submit"; }
        return tag;
    }

    private object ElNameGet(JsCallArgs a) { JsElementObject e = A(a); string v = e.Node.GetAttr("name"); return v != null ? v : ""; }
    private object ElNameSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "name", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElDefaultValueGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node.Tag == "textarea") { string t = e.Node.TextContent; return t != null ? t : ""; }
        string v = e.Node.GetAttr("value");
        return v != null ? v : "";
    }

    private object ElPlaceholderGet(JsCallArgs a) { JsElementObject e = A(a); string v = e.Node.GetAttr("placeholder"); return v != null ? v : ""; }

    private object ElSelectedIndexGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node.Tag != "select") return (double)(-1);
        DomNode sel = SelectedOption(e.Node);
        int i = 0;
        for (DomNode opt = e.Node.FirstChild; opt != null; opt = opt.Next)
        {
            if (!opt.IsElement || opt.Tag != "option") continue;
            if (opt == sel) return (double)i;
            i++;
        }
        return (double)(-1);
    }

    private object ElSelectedIndexSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node.Tag != "select") return JsUndefined.Instance;
        int want = (int)JsValueOps.ToNumber(a.Get(0));
        int i = 0;
        DomNode chosen = null;
        for (DomNode opt = e.Node.FirstChild; opt != null; opt = opt.Next)
        {
            if (!opt.IsElement || opt.Tag != "option") continue;
            if (i == want) { chosen = opt; break; }
            i++;
        }
        if (chosen != null) { SelectOption(e.Node, chosen); MarkChanged(); }
        return JsUndefined.Instance;
    }

    private object ElOptions(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsArray arr = Realm.NewArray();
        if (e.Node.Tag == "select")
            for (DomNode opt = e.Node.FirstChild; opt != null; opt = opt.Next)
                if (opt.IsElement && opt.Tag == "option") arr.Items.Add(Wrap(opt));
        return arr;
    }

    private object ElOptionsLength(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node.Tag != "select") return (double)0;
        int n = 0;
        for (DomNode opt = e.Node.FirstChild; opt != null; opt = opt.Next)
            if (opt.IsElement && opt.Tag == "option") n++;
        return (double)n;
    }

    private object ElForm(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(AncestorForm(e.Node)); }

    private object ElFormElements(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsArray arr = Realm.NewArray();
        DomNode form = AncestorForm(e.Node);
        if (form != null) CollectControls(form, arr);
        return arr;
    }

    private static DomNode AncestorForm(DomNode node)
    {
        DomNode n = node;
        while (n != null && !(n.IsElement && n.Tag == "form")) n = n.Parent;
        return n;
    }

    private void CollectControls(DomNode node, JsArray arr)
    {
        for (DomNode c = node.FirstChild; c != null; c = c.Next)
        {
            if (!c.IsElement) continue;
            if (c.Tag == "input" || c.Tag == "select" || c.Tag == "textarea" || c.Tag == "button")
                arr.Items.Add(Wrap(c));
            CollectControls(c, arr);
        }
    }

    private object ElSelectionStart(JsCallArgs a) { JsElementObject e = A(a); return (double)SelectionOf(e.Node, "start"); }
    private object ElSelectionEnd(JsCallArgs a) { JsElementObject e = A(a); return (double)SelectionOf(e.Node, "end"); }

    private static int SelectionOf(DomNode node, string which)
    {
        if (node.Attrs == null) return 0;
        string key = "data-sel-" + which;
        string v;
        if (node.Attrs.ContainsKey(key))
        {
            int.TryParse(node.Attrs[key], out int parsed);
            return parsed;
        }
        return which == "end" ? (LiveValue(node)).Length : 0;
    }

    private object ElFocus(JsCallArgs a)
    {
        JsElementObject e = A(a);
        ActiveElementNode = e.Node;   // document.activeElement, even without a host
        if (OnFocusRequest != null) OnFocusRequest(e.Node);
        return JsUndefined.Instance;
    }

    private object ElBlur(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (ActiveElementNode == e.Node) ActiveElementNode = null;
        if (OnFocusRequest != null) OnFocusRequest(null);
        return JsUndefined.Instance;
    }

    private object ElSelectText(JsCallArgs a)
    {
        // select() highlights the whole value; we model it as a caret at the end
        JsElementObject e = A(a);
        string v = LiveValue(e.Node);
        if (e.Node.Attrs == null) e.Node.Attrs = new Dictionary<string, string>();
        e.Node.Attrs["data-sel-start"] = "0";
        e.Node.Attrs["data-sel-end"] = v.Length.ToString();
        return JsUndefined.Instance;
    }

    private object ElSetSelectionRange(JsCallArgs a)
    {
        JsElementObject e = A(a);
        int start = (int)JsValueOps.ToNumber(a.Get(0));
        int end = (int)JsValueOps.ToNumber(a.Get(1));
        string v = LiveValue(e.Node);
        if (start < 0) start = 0;
        if (end < start) end = start;
        if (end > v.Length) end = v.Length;
        if (start > v.Length) start = v.Length;
        if (e.Node.Attrs == null) e.Node.Attrs = new Dictionary<string, string>();
        e.Node.Attrs["data-sel-start"] = start.ToString();
        e.Node.Attrs["data-sel-end"] = end.ToString();
        return JsUndefined.Instance;
    }

    private object ElSubmit(JsCallArgs a)
    {
        JsElementObject e = A(a);
        DomNode form = e.Node.Tag == "form" ? e.Node : AncestorForm(e.Node);
        if (form != null && OnFormSubmit != null) OnFormSubmit(form, null);
        return JsUndefined.Instance;
    }

    private object ElReset(JsCallArgs a)
    {
        JsElementObject e = A(a);
        DomNode form = e.Node.Tag == "form" ? e.Node : AncestorForm(e.Node);
        if (form != null && OnFormReset != null) OnFormReset(form);
        return JsUndefined.Instance;
    }

    private object ElClickSubmit(JsCallArgs a)
    {
        // a script calling .click() on a submit button should submit
        JsElementObject e = A(a);
        return JsUndefined.Instance;
    }

    // identity
    private object ElTagName(JsCallArgs a) { JsElementObject e = A(a); return e.Node.Tag != null ? e.Node.Tag.ToUpperInvariant() : ""; }
    private object ElNodeName(JsCallArgs a) { JsElementObject e = A(a); return e.Node.NodeName.ToUpperInvariant(); }
    private object ElNodeType(JsCallArgs a) { JsElementObject e = A(a); return (double)(e.Node.NodeType == DomNode.TextNode ? 3 : 1); }

    private object ElIdGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.Id != null ? e.Node.Id : ""; }
    private object ElIdSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "id", JsValueOps.ToStringValue(a.Get(0))); MarkChanged(); return JsUndefined.Instance; }

    private object ElClassGet(JsCallArgs a) { JsElementObject e = A(a); string v = e.Node.GetAttr("class"); return v != null ? v : ""; }
    private object ElClassSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "class", JsValueOps.ToStringValue(a.Get(0))); MarkChanged(); return JsUndefined.Instance; }

    // value: tag-aware like a real DOM. textarea falls back to its text, and
    // select reflects the chosen option, so scripts see what layout draws.
    private object ElValueGet(JsCallArgs a) { JsElementObject e = A(a); return LiveValue(e.Node); }
    private object ElValueSet(JsCallArgs a) { JsElementObject e = A(a); SetLiveValue(e.Node, JsValueOps.ToStringValue(a.Get(0))); MarkChanged(); return JsUndefined.Instance; }

    /// <summary>The control's live value (textarea -> text, select -> option).</summary>
    public static string LiveValue(DomNode node)
    {
        if (node == null || !node.IsElement) return "";
        string tag = node.Tag;
        if (tag == "textarea")
        {
            string v = node.GetAttr("value");
            return v != null ? v : (node.TextContent ?? "");
        }
        if (tag == "select")
        {
            DomNode opt = SelectedOption(node);
            return opt != null ? OptionValue(opt) : "";
        }
        string val = node.GetAttr("value");
        return val != null ? val : "";
    }

    /// <summary>Write a control's live value. A select picks the matching option.</summary>
    public static void SetLiveValue(DomNode node, string value)
    {
        if (node == null) return;
        if (node.Tag == "select")
        {
            DomNode chosen = null;
            for (DomNode opt = node.FirstChild; opt != null; opt = opt.Next)
                if (opt.IsElement && opt.Tag == "option" && OptionValue(opt) == value) { chosen = opt; break; }
            if (chosen != null) SelectOption(node, chosen);
            return;
        }
        if (node.Tag == "textarea")
        {
            // keep the text and the value attribute in sync so both layout and
            // serialization agree after a script assignment
            SetAttribute(node, "value", value);
            node.RemoveAllChildren();
            if (value.Length > 0) node.AddText(value);
            return;
        }
        SetAttribute(node, "value", value);
    }

    /// <summary>The value a select option submits (value attr, else its text).</summary>
    public static string OptionValue(DomNode opt)
    {
        string v = opt.GetAttr("value");
        return v != null ? v : (opt.TextContent ?? "");
    }

    /// <summary>A select's chosen option element (selected attr, else the first).</summary>
    public static DomNode SelectedOption(DomNode select)
    {
        DomNode first = null;
        for (DomNode opt = select.FirstChild; opt != null; opt = opt.Next)
        {
            if (!opt.IsElement || opt.Tag != "option") continue;
            if (first == null) first = opt;
            if (opt.GetAttr("selected") != null) return opt;
        }
        return first;
    }

    /// <summary>Mark one option selected and clear the rest of its select.</summary>
    public static void SelectOption(DomNode select, DomNode chosen)
    {
        for (DomNode opt = select.FirstChild; opt != null; opt = opt.Next)
        {
            if (!opt.IsElement || opt.Tag != "option") continue;
            if (opt == chosen)
            {
                if (opt.GetAttr("selected") == null) SetAttribute(opt, "selected", "");
            }
            else if (opt.GetAttr("selected") != null) opt.Attrs.Remove("selected");
        }
    }

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

    private object ElDocumentElement(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node == null) return JsNull.Instance;
        if (e.Node.Tag != "#document" && e.Node.Tag != "html") return JsUndefined.Instance;
        DomNode html = FindByTag(e.Node, "html");
        if (html != null) return Wrap(html);
        DomNode first = e.Node.FirstElementChild();
        return first != null ? (object)Wrap(first) : JsNull.Instance;
    }

    private object ElBody(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node == null) return JsNull.Instance;
        if (e.Node.Tag != "#document" && e.Node.Tag != "html") return JsUndefined.Instance;
        DomNode b = FindByTag(e.Node, "body");
        return b != null ? (object)Wrap(b) : JsNull.Instance;
    }

    private object ElHead(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node == null) return JsNull.Instance;
        if (e.Node.Tag != "#document" && e.Node.Tag != "html") return JsUndefined.Instance;
        DomNode h = FindByTag(e.Node, "head");
        return h != null ? (object)Wrap(h) : JsNull.Instance;
    }

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

    private object ElOwnerDocument(JsCallArgs a) { return Document; }

    private object ElDataset(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.DatasetCache == null) e.DatasetCache = new JsDatasetObject(Realm.NewObject(), e);
        return e.DatasetCache;
    }

    private object ElAttributes(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsArray arr = Realm.NewArray();
        if (e.Node.Attrs != null)
        {
            foreach (KeyValuePair<string, string> kv in e.Node.Attrs)
            {
                JsObject attrObj = Realm.NewObject();
                attrObj.Define("name", JsProperty.Data(kv.Key));
                attrObj.Define("value", JsProperty.Data(kv.Value));
                attrObj.Define("specified", JsProperty.Data(true));
                arr.Items.Add(attrObj);
            }
        }
        return arr;
    }

    private object ElInnerTextGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.TextContent; }
    private object ElInnerTextSet(JsCallArgs a) { return ElTextSet(a); }

    private object ElChildElementCount(JsCallArgs a) { JsElementObject e = A(a); return (double)e.Node.ChildElementCount; }

    private object ElOffsetWidth(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node != null && (e.Node.Tag == "body" || e.Node.Tag == "html" || e.Node.Tag == "#document"))
        {
            return Interp != null && Interp.Loop != null && Interp.Loop.ViewportWidth > 0
                ? (double)Interp.Loop.ViewportWidth : 800.0;
        }
        CssComputed cs = e.Node.Style ?? ComputeStyleChain(e.Node);
        return (double)(cs != null && cs.HasW ? cs.W : (cs != null && cs.Display == CssDisplay.None ? 0 : 100));
    }
    private object ElOffsetHeight(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node != null && (e.Node.Tag == "body" || e.Node.Tag == "html" || e.Node.Tag == "#document"))
        {
            return Interp != null && Interp.Loop != null && Interp.Loop.ViewportHeight > 0
                ? (double)Interp.Loop.ViewportHeight : 600.0;
        }
        CssComputed cs = e.Node.Style ?? ComputeStyleChain(e.Node);
        return (double)(cs != null && cs.HasH ? cs.H : (cs != null && cs.Display == CssDisplay.None ? 0 : 30));
    }
    private object ElOffsetLeft(JsCallArgs a)
    {
        JsElementObject e = A(a);
        CssComputed cs = e.Node.Style ?? ComputeStyleChain(e.Node);
        return (double)(cs != null && cs.PosL != CssComputed.Auto ? cs.PosL : 0);
    }
    private object ElOffsetTop(JsCallArgs a)
    {
        JsElementObject e = A(a);
        CssComputed cs = e.Node.Style ?? ComputeStyleChain(e.Node);
        return (double)(cs != null && cs.PosT != CssComputed.Auto ? cs.PosT : 0);
    }
    private object ElOffsetParent(JsCallArgs a) { JsElementObject e = A(a); return WrapOrJsNull(e.Node.Parent); }

    private object ElClientWidth(JsCallArgs a) { return ElOffsetWidth(a); }
    private object ElClientHeight(JsCallArgs a) { return ElOffsetHeight(a); }
    private object ElClientLeft(JsCallArgs a) { JsElementObject e = A(a); CssComputed cs = e.Node.Style ?? ComputeStyleChain(e.Node); return (double)(cs != null ? cs.BorderLW : 0); }
    private object ElClientTop(JsCallArgs a) { JsElementObject e = A(a); CssComputed cs = e.Node.Style ?? ComputeStyleChain(e.Node); return (double)(cs != null ? cs.BorderTW : 0); }

    private object ElScrollWidth(JsCallArgs a) { return ElOffsetWidth(a); }
    private object ElScrollHeight(JsCallArgs a) { return ElOffsetHeight(a); }
    private object ElScrollLeft(JsCallArgs a) { return 0.0; }
    private object ElScrollLeftSet(JsCallArgs a) { return JsUndefined.Instance; }
    private object ElScrollTop(JsCallArgs a) { return 0.0; }
    private object ElScrollTopSet(JsCallArgs a) { return JsUndefined.Instance; }
    private object ElScrollIntoView(JsCallArgs a) { return JsUndefined.Instance; }

    private object ElGetBoundingClientRect(JsCallArgs a)
    {
        JsElementObject e = A(a);
        CssComputed cs = e.Node.Style ?? ComputeStyleChain(e.Node);
        double w = cs != null && cs.HasW ? cs.W : (cs != null && cs.Display == CssDisplay.None ? 0 : 100);
        double h = cs != null && cs.HasH ? cs.H : (cs != null && cs.Display == CssDisplay.None ? 0 : 30);
        double top = cs != null && cs.PosT != CssComputed.Auto ? cs.PosT : 0;
        double left = cs != null && cs.PosL != CssComputed.Auto ? cs.PosL : 0;
        JsObject rect = Realm.NewObject();
        rect.Define("x", JsProperty.Data(left));
        rect.Define("y", JsProperty.Data(top));
        rect.Define("width", JsProperty.Data(w));
        rect.Define("height", JsProperty.Data(h));
        rect.Define("top", JsProperty.Data(top));
        rect.Define("bottom", JsProperty.Data(top + h));
        rect.Define("left", JsProperty.Data(left));
        rect.Define("right", JsProperty.Data(left + w));
        return rect;
    }

    private object ElMatches(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string selStr = JsValueOps.ToStringValue(a.Get(0));
        List<CssSelector> sels = CssSheet.ParseSelectors(selStr);
        if (sels != null)
        {
            for (int i = 0; i < sels.Count; i++)
                if (CssSheet.Matches(sels[i], e.Node)) return true;
        }
        return false;
    }

    private object ElClosest(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string selStr = JsValueOps.ToStringValue(a.Get(0));
        List<CssSelector> sels = CssSheet.ParseSelectors(selStr);
        DomNode cur = e.Node;
        while (cur != null && cur.IsElement)
        {
            if (sels != null)
            {
                for (int i = 0; i < sels.Count; i++)
                    if (CssSheet.Matches(sels[i], cur)) return Wrap(cur);
            }
            cur = cur.Parent;
        }
        return JsNull.Instance;
    }

    private object ElContains(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsElementObject other = a.Get(0) as JsElementObject;
        if (other == null || other.Node == null) return false;
        return e.Node.Contains(other.Node);
    }

    private object ElReplaceChild(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsElementObject newC = RequireNode(a.Get(0));
        JsElementObject oldC = RequireNode(a.Get(1));
        e.Node.ReplaceChild(newC.Node, oldC.Node);
        MarkChanged();
        return oldC;
    }

    private object ElHasChildNodes(JsCallArgs a)
    {
        JsElementObject e = A(a);
        return e.Node.HasChildNodes();
    }

    private object ElGetElementsByClassName(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string cls = JsValueOps.ToStringValue(a.Get(0));
        List<DomNode> hits = e.Node.GetElementsByClassName(cls);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }

    private object ElGetElementsByTagName(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string tag = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        List<DomNode> hits = e.Node.GetElementsByTagName(tag);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }

    private object ElGetAttributeNames(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsArray arr = Realm.NewArray();
        if (e.Node.Attrs != null)
            foreach (string k in e.Node.Attrs.Keys) arr.Items.Add(k);
        return arr;
    }

    private object ElToggleAttribute(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string name = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
        bool has = e.Node.GetAttr(name) != null;
        bool next = a.ArgCount > 1 ? JsValueOps.ToBoolean(a.Get(1)) : !has;
        if (next) SetAttribute(e.Node, name, "");
        else if (e.Node.Attrs != null) e.Node.Attrs.Remove(name);
        MarkChanged();
        return next;
    }

    private object ElAppend(JsCallArgs a)
    {
        JsElementObject e = A(a);
        for (int i = 0; i < a.ArgCount; i++)
        {
            object v = a.Get(i);
            if (v is JsElementObject elObj) e.Node.Append(elObj.Node);
            else e.Node.AddText(JsValueOps.ToStringValue(v));
        }
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElPrepend(JsCallArgs a)
    {
        JsElementObject e = A(a);
        for (int i = a.ArgCount - 1; i >= 0; i--)
        {
            object v = a.Get(i);
            DomNode n = v is JsElementObject elObj ? elObj.Node : new DomNode { NodeType = DomNode.TextNode, Text = JsValueOps.ToStringValue(v) };
            e.Node.InsertBefore(n, e.Node.FirstChild);
        }
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElBefore(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node.Parent == null) return JsUndefined.Instance;
        for (int i = 0; i < a.ArgCount; i++)
        {
            object v = a.Get(i);
            DomNode n = v is JsElementObject elObj ? elObj.Node : new DomNode { NodeType = DomNode.TextNode, Text = JsValueOps.ToStringValue(v) };
            e.Node.Parent.InsertBefore(n, e.Node);
        }
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElAfter(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node.Parent == null) return JsUndefined.Instance;
        DomNode refNode = e.Node.Next;
        for (int i = 0; i < a.ArgCount; i++)
        {
            object v = a.Get(i);
            DomNode n = v is JsElementObject elObj ? elObj.Node : new DomNode { NodeType = DomNode.TextNode, Text = JsValueOps.ToStringValue(v) };
            e.Node.Parent.InsertBefore(n, refNode);
        }
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElReplaceWith(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node.Parent == null) return JsUndefined.Instance;
        for (int i = 0; i < a.ArgCount; i++)
        {
            object v = a.Get(i);
            DomNode n = v is JsElementObject elObj ? elObj.Node : new DomNode { NodeType = DomNode.TextNode, Text = JsValueOps.ToStringValue(v) };
            e.Node.Parent.InsertBefore(n, e.Node);
        }
        e.Node.Unlink();
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElSrcGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string src = e.Node.GetAttr("src");
        if (src == null) return "";
        return WebUrl.Resolve(LocationHref, src);
    }
    private object ElSrcSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        SetAttribute(e.Node, "src", JsValueOps.ToStringValue(a.Get(0)));
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElHrefGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string href = e.Node.GetAttr("href");
        if (href == null) return "";
        return WebUrl.Resolve(LocationHref, href);
    }
    private object ElHrefSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        SetAttribute(e.Node, "href", JsValueOps.ToStringValue(a.Get(0)));
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElTitleGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("title") != null ? e.Node.GetAttr("title") : ""; }
    private object ElTitleSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "title", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElAltGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("alt") != null ? e.Node.GetAttr("alt") : ""; }
    private object ElAltSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "alt", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElTargetGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("target") != null ? e.Node.GetAttr("target") : ""; }
    private object ElTargetSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "target", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElRelGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("rel") != null ? e.Node.GetAttr("rel") : ""; }
    private object ElRelSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "rel", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElHiddenGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("hidden") != null; }
    private object ElHiddenSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (JsValueOps.ToBoolean(a.Get(0))) SetAttribute(e.Node, "hidden", "");
        else if (e.Node.Attrs != null) e.Node.Attrs.Remove("hidden");
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElTabIndexGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string ti = e.Node.GetAttr("tabindex");
        if (int.TryParse(ti, out int v)) return (double)v;
        return 0.0;
    }
    private object ElTabIndexSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        SetAttribute(e.Node, "tabindex", ((int)JsValueOps.ToNumber(a.Get(0))).ToString());
        return JsUndefined.Instance;
    }

    private object ElActionGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("action") != null ? e.Node.GetAttr("action") : ""; }
    private object ElActionSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "action", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElMethodGet(JsCallArgs a) { JsElementObject e = A(a); return (e.Node.GetAttr("method") != null ? e.Node.GetAttr("method") : "get").ToLowerInvariant(); }
    private object ElMethodSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "method", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElAutocompleteGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("autocomplete") != null ? e.Node.GetAttr("autocomplete") : ""; }
    private object ElAutocompleteSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "autocomplete", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElRequiredGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("required") != null; }
    private object ElRequiredSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (JsValueOps.ToBoolean(a.Get(0))) SetAttribute(e.Node, "required", "");
        else if (e.Node.Attrs != null) e.Node.Attrs.Remove("required");
        return JsUndefined.Instance;
    }

    private object ElPatternGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("pattern") != null ? e.Node.GetAttr("pattern") : ""; }
    private object ElPatternSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "pattern", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElMinGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("min") != null ? e.Node.GetAttr("min") : ""; }
    private object ElMinSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "min", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElMaxGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("max") != null ? e.Node.GetAttr("max") : ""; }
    private object ElMaxSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "max", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElStepGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("step") != null ? e.Node.GetAttr("step") : ""; }
    private object ElStepSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "step", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElAcceptGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("accept") != null ? e.Node.GetAttr("accept") : ""; }
    private object ElAcceptSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "accept", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElRowsGet(JsCallArgs a) { JsElementObject e = A(a); return int.TryParse(e.Node.GetAttr("rows"), out int r) ? (double)r : 2.0; }
    private object ElRowsSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "rows", ((int)JsValueOps.ToNumber(a.Get(0))).ToString()); return JsUndefined.Instance; }

    private object ElColsGet(JsCallArgs a) { JsElementObject e = A(a); return int.TryParse(e.Node.GetAttr("cols"), out int c) ? (double)c : 20.0; }
    private object ElColsSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "cols", ((int)JsValueOps.ToNumber(a.Get(0))).ToString()); return JsUndefined.Instance; }

    private object ElWidthGet(JsCallArgs a) { JsElementObject e = A(a); return int.TryParse(e.Node.GetAttr("width"), out int w) ? (double)w : 0.0; }
    private object ElWidthSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "width", ((int)JsValueOps.ToNumber(a.Get(0))).ToString()); MarkChanged(); return JsUndefined.Instance; }

    private object ElHeightGet(JsCallArgs a) { JsElementObject e = A(a); return int.TryParse(e.Node.GetAttr("height"), out int h) ? (double)h : 0.0; }
    private object ElHeightSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "height", ((int)JsValueOps.ToNumber(a.Get(0))).ToString()); MarkChanged(); return JsUndefined.Instance; }

    private object ElContentEditableGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("contenteditable") != null ? e.Node.GetAttr("contenteditable") : "inherit"; }
    private object ElContentEditableSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "contenteditable", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElIsContentEditableGet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string ce = e.Node.GetAttr("contenteditable");
        return ce == "true" || ce == "";
    }

    private object ElAutofocusGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("autofocus") != null; }
    private object ElAutofocusSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (JsValueOps.ToBoolean(a.Get(0))) SetAttribute(e.Node, "autofocus", "");
        else if (e.Node.Attrs != null) e.Node.Attrs.Remove("autofocus");
        return JsUndefined.Instance;
    }

    private object ElMultipleGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("multiple") != null; }
    private object ElMultipleSet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (JsValueOps.ToBoolean(a.Get(0))) SetAttribute(e.Node, "multiple", "");
        else if (e.Node.Attrs != null) e.Node.Attrs.Remove("multiple");
        return JsUndefined.Instance;
    }

    private object ElLangGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("lang") != null ? e.Node.GetAttr("lang") : ""; }
    private object ElLangSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "lang", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElDirGet(JsCallArgs a) { JsElementObject e = A(a); return e.Node.GetAttr("dir") != null ? e.Node.GetAttr("dir") : ""; }
    private object ElDirSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "dir", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }

    private object ElPlaceholderSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "placeholder", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }
    private object ElTypeSet(JsCallArgs a) { JsElementObject e = A(a); SetAttribute(e.Node, "type", JsValueOps.ToStringValue(a.Get(0))); return JsUndefined.Instance; }
    private object ElReadonlySet(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (JsValueOps.ToBoolean(a.Get(0))) SetAttribute(e.Node, "readonly", "");
        else if (e.Node.Attrs != null) e.Node.Attrs.Remove("readonly");
        return JsUndefined.Instance;
    }

    private object ElInsertAdjacentHTML(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string pos = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
        string html = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
        List<DomNode> nodes = HtmlDom.ParseFragment(html);
        if (nodes.Count == 0) return JsUndefined.Instance;

        if (pos == "beforebegin")
        {
            if (e.Node.Parent != null)
            {
                for (int i = 0; i < nodes.Count; i++) e.Node.Parent.InsertBefore(nodes[i], e.Node);
                MarkChanged();
            }
        }
        else if (pos == "afterbegin")
        {
            DomNode first = e.Node.FirstChild;
            for (int i = 0; i < nodes.Count; i++) e.Node.InsertBefore(nodes[i], first);
            MarkChanged();
        }
        else if (pos == "beforeend")
        {
            for (int i = 0; i < nodes.Count; i++) e.Node.AppendChild(nodes[i]);
            MarkChanged();
        }
        else if (pos == "afterend")
        {
            if (e.Node.Parent != null)
            {
                DomNode next = e.Node.Next;
                for (int i = 0; i < nodes.Count; i++) e.Node.Parent.InsertBefore(nodes[i], next);
                MarkChanged();
            }
        }
        return JsUndefined.Instance;
    }

    private object ElInsertAdjacentElement(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string pos = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
        JsElementObject child = a.ArgCount > 1 ? a.Get(1) as JsElementObject : null;
        if (child == null || child.Node == null) return JsNull.Instance;

        if (pos == "beforebegin")
        {
            if (e.Node.Parent != null) { e.Node.Parent.InsertBefore(child.Node, e.Node); MarkChanged(); return child; }
        }
        else if (pos == "afterbegin")
        {
            e.Node.InsertBefore(child.Node, e.Node.FirstChild); MarkChanged(); return child;
        }
        else if (pos == "beforeend")
        {
            e.Node.AppendChild(child.Node); MarkChanged(); return child;
        }
        else if (pos == "afterend")
        {
            if (e.Node.Parent != null) { e.Node.Parent.InsertBefore(child.Node, e.Node.Next); MarkChanged(); return child; }
        }
        return JsNull.Instance;
    }

    private object ElInsertAdjacentText(JsCallArgs a)
    {
        JsElementObject e = A(a);
        string pos = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
        string text = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
        DomNode textNode = new DomNode { NodeType = DomNode.TextNode, Text = text };

        if (pos == "beforebegin")
        {
            if (e.Node.Parent != null) { e.Node.Parent.InsertBefore(textNode, e.Node); MarkChanged(); }
        }
        else if (pos == "afterbegin")
        {
            e.Node.InsertBefore(textNode, e.Node.FirstChild); MarkChanged();
        }
        else if (pos == "beforeend")
        {
            e.Node.AppendChild(textNode); MarkChanged();
        }
        else if (pos == "afterend")
        {
            if (e.Node.Parent != null) { e.Node.Parent.InsertBefore(textNode, e.Node.Next); MarkChanged(); }
        }
        return JsUndefined.Instance;
    }

    private object ElCompareDocumentPosition(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsElementObject other = a.ArgCount > 0 ? a.Get(0) as JsElementObject : null;
        if (other == null || other.Node == null) return (double)1;
        return (double)e.Node.CompareDocumentPosition(other.Node);
    }

    private object ElIsSameNode(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsElementObject other = a.ArgCount > 0 ? a.Get(0) as JsElementObject : null;
        return other != null && object.ReferenceEquals(e.Node, other.Node);
    }

    private object ElIsEqualNode(JsCallArgs a)
    {
        JsElementObject e = A(a);
        JsElementObject other = a.ArgCount > 0 ? a.Get(0) as JsElementObject : null;
        return other != null && e.Node.IsEqualNode(other.Node);
    }

    private object ElNormalize(JsCallArgs a)
    {
        JsElementObject e = A(a);
        e.Node.Normalize();
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object ElGetContext(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node == null || e.Node.Tag != "canvas") return JsNull.Instance;
        string ctxType = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
        if (ctxType == "2d")
        {
            if (e.GetProperty("_canvas2d", out object ctx)) return ctx;
            int w = 300, h = 150;
            string sw = e.Node.GetAttr("width");
            string sh = e.Node.GetAttr("height");
            if (!string.IsNullOrEmpty(sw) && int.TryParse(sw, out int pw) && pw > 0) w = pw;
            if (!string.IsNullOrEmpty(sh) && int.TryParse(sh, out int ph) && ph > 0) h = ph;
            if (w > 2048) w = 2048; if (h > 2048) h = 2048;
            if (e.Node.CanvasSurface == null)
            {
                e.Node.CanvasSurface = new Cosmos.Kernel.System.Graphics.Bitmap(w, h, Cosmos.Kernel.System.Graphics.ColorDepth.ColorDepth32);
            }

            JsObject newCtx = Realm.NewObject(CanvasContextPrototype);
            newCtx.Define("canvas", JsProperty.DataNonEnum(e));
            e.SetProperty("_canvas2d", newCtx);
            return newCtx;
        }
        return JsNull.Instance;
    }

    private object ElToDataURL(JsCallArgs a)
    {
        JsElementObject e = A(a);
        if (e.Node != null && e.Node.CanvasSurface is Cosmos.Kernel.System.Graphics.Bitmap bmp && bmp.RawData != null)
        {
            byte[] bmpBytes = CanvasEncodeBmp(bmp.RawData, (int)bmp.Width, (int)bmp.Height);
            if (bmpBytes != null && bmpBytes.Length > 0)
                return "data:image/bmp;base64," + Convert.ToBase64String(bmpBytes);
        }
        return "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
    }

    private static byte[] CanvasEncodeBmp(int[] pixels, int w, int h)
    {
        int rowBytes = ((w * 3 + 3) / 4) * 4;
        int imageSize = rowBytes * h;
        byte[] bmp = new byte[54 + imageSize];

        // BMP header
        bmp[0] = 0x42; bmp[1] = 0x4D; // BM
        int fileSize = 54 + imageSize;
        bmp[2] = (byte)(fileSize & 0xFF); bmp[3] = (byte)((fileSize >> 8) & 0xFF);
        bmp[4] = (byte)((fileSize >> 16) & 0xFF); bmp[5] = (byte)((fileSize >> 24) & 0xFF);
        bmp[10] = 54; // offset

        // DIB header
        bmp[14] = 40; // header size
        bmp[18] = (byte)(w & 0xFF); bmp[19] = (byte)((w >> 8) & 0xFF);
        bmp[20] = (byte)((w >> 16) & 0xFF); bmp[21] = (byte)((w >> 24) & 0xFF);
        bmp[22] = (byte)(h & 0xFF); bmp[23] = (byte)((h >> 8) & 0xFF);
        bmp[24] = (byte)((h >> 16) & 0xFF); bmp[25] = (byte)((h >> 24) & 0xFF);
        bmp[26] = 1; // planes
        bmp[28] = 24; // bpp
        bmp[34] = (byte)(imageSize & 0xFF); bmp[35] = (byte)((imageSize >> 8) & 0xFF);

        // Pixels (bottom-up, BGR)
        for (int y = 0; y < h; y++)
        {
            int srcRow = (h - 1 - y) * w;
            int dstRow = 54 + y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int c = pixels[srcRow + x];
                int dst = dstRow + x * 3;
                bmp[dst] = (byte)(c & 0xFF);         // B
                bmp[dst + 1] = (byte)((c >> 8) & 0xFF);  // G
                bmp[dst + 2] = (byte)((c >> 16) & 0xFF); // R
            }
        }
        return bmp;
    }

    private object ElToBlob(JsCallArgs a)
    {
        JsFunction cb = a.Get(0) as JsFunction;
        if (cb != null)
        {
            JsBlobObject b = new JsBlobObject(Realm.ObjectPrototype, new byte[0], "image/png");
            Realm.Interp.Call(cb, JsUndefined.Instance, new object[] { b }, false);
        }
        return JsUndefined.Instance;
    }

    private object ElAttachShadow(JsCallArgs a)
    {
        JsElementObject el = a.ThisValue as JsElementObject;
        if (el == null) return JsUndefined.Instance;
        DomNode frag = new DomNode { NodeType = DomNode.Element, Tag = "#document-fragment" };
        JsElementObject sr = Wrap(frag);
        if (sr != null)
        {
            sr.Define("host", JsProperty.Data(el));
            sr.Define("mode", JsProperty.Data("open"));
            el.Define("_shadowRoot", JsProperty.DataNonEnum(sr));
            return sr;
        }
        return JsUndefined.Instance;
    }

    private object ElShadowRoot(JsCallArgs a)
    {
        JsElementObject el = a.ThisValue as JsElementObject;
        if (el == null) return JsNull.Instance;
        JsProperty p;
        if (el.TryGetRaw("_shadowRoot", out p) || el.TryGetRaw("shadowRoot", out p))
            return p.Value;
        return JsNull.Instance;
    }

    private object ElSlotGet(JsCallArgs a)
    {
        JsElementObject e = a.ThisValue as JsElementObject;
        return (e != null && e.Node != null) ? (e.Node.GetAttr("slot") ?? "") : "";
    }

    private object ElSlotSet(JsCallArgs a)
    {
        JsElementObject e = a.ThisValue as JsElementObject;
        if (e != null && e.Node != null)
        {
            SetAttribute(e.Node, "slot", JsValueOps.ToStringValue(a.Get(0)));
            MarkChanged();
        }
        return JsUndefined.Instance;
    }

    private object ElGetRootNode(JsCallArgs a)
    {
        JsElementObject e = a.ThisValue as JsElementObject;
        if (e == null || e.Node == null) return a.ThisValue != null ? a.ThisValue : JsNull.Instance;
        DomNode cur = e.Node;
        while (cur.Parent != null)
        {
            cur = cur.Parent;
        }
        return Wrap(cur);
    }

    private object ElAnimate(JsCallArgs a)
    {
        JsObject anim = Realm.NewObject();
        JsPromise finished = new JsPromise(Realm.PromisePrototype);
        if (Interp != null && Interp.Loop != null)
            JsBrowser.FulfillPromise(Interp.Loop, finished, anim);
        anim.Define("finished", JsProperty.Data(finished));
        anim.Define("playState", JsProperty.Data("finished"));
        anim.Define("currentTime", JsProperty.Data(0.0));
        DefineOn(anim, "play", delegate { return JsUndefined.Instance; }, 0);
        DefineOn(anim, "pause", delegate { return JsUndefined.Instance; }, 0);
        DefineOn(anim, "finish", delegate { return JsUndefined.Instance; }, 0);
        DefineOn(anim, "cancel", delegate { return JsUndefined.Instance; }, 0);
        DefineOn(anim, "reverse", delegate { return JsUndefined.Instance; }, 0);
        DefineOn(anim, "addEventListener", delegate { return JsUndefined.Instance; }, 2);
        DefineOn(anim, "removeEventListener", delegate { return JsUndefined.Instance; }, 2);
        return anim;
    }

    private object ElTemplateContentGet(JsCallArgs a)
    {
        JsElementObject thisObj = a.ThisValue as JsElementObject;
        if (thisObj == null || thisObj.Node == null || thisObj.Node.Tag != "template") return JsUndefined.Instance;
        JsProperty p;
        if (thisObj.TryGetRaw("_templateContent", out p))
            return p.Value;
        DomNode frag = new DomNode { NodeType = DomNode.Element, Tag = "#document-fragment" };
        DomNode c = thisObj.Node.FirstChild;
        while (c != null)
        {
            frag.Append(c.CloneNode(true));
            c = c.Next;
        }
        JsElementObject wrapped = Wrap(frag);
        thisObj.Define("_templateContent", JsProperty.DataNonEnum(wrapped));
        return wrapped;
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
        DefineAccessor(EventPrototype, "detail", EvDetail, null);
        DefineAccessor(EventPrototype, "timeStamp", EvTimeStamp, null);
        DefineAccessor(EventPrototype, "clientX", EvClientX, null);
        DefineAccessor(EventPrototype, "clientY", EvClientY, null);
        DefineAccessor(EventPrototype, "pageX", EvPageX, null);
        DefineAccessor(EventPrototype, "pageY", EvPageY, null);
        DefineAccessor(EventPrototype, "screenX", EvScreenX, null);
        DefineAccessor(EventPrototype, "screenY", EvScreenY, null);
        DefineAccessor(EventPrototype, "button", EvButton, null);
        DefineAccessor(EventPrototype, "buttons", EvButtons, null);
        DefineAccessor(EventPrototype, "altKey", EvAltKey, null);
        DefineAccessor(EventPrototype, "ctrlKey", EvCtrlKey, null);
        DefineAccessor(EventPrototype, "shiftKey", EvShiftKey, null);
        DefineAccessor(EventPrototype, "metaKey", EvMetaKey, null);
        DefineAccessor(EventPrototype, "key", EvKey, null);
        DefineAccessor(EventPrototype, "code", EvCode, null);
        DefineAccessor(EventPrototype, "keyCode", EvKeyCode, null);
        DefineAccessor(EventPrototype, "which", EvWhich, null);
        DefineAccessor(EventPrototype, "charCode", EvCharCode, null);

        DefineOn(EventPrototype, "preventDefault", EvPreventDefault, 0);
        DefineOn(EventPrototype, "stopPropagation", EvStopPropagation, 0);
        DefineOn(EventPrototype, "stopImmediatePropagation", EvStopImmediate, 0);
        DefineOn(EventPrototype, "composedPath", EvComposedPath, 0);
        DefineOn(EventPrototype, "initEvent", delegate (JsCallArgs a)
        {
            JsEventObject ev = a.ThisValue as JsEventObject;
            if (ev != null)
            {
                ev.Type = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
                ev.Bubbles = a.ArgCount > 1 ? JsValueOps.ToBoolean(a.Get(1)) : false;
                ev.DefaultPrevented = false;
            }
            return JsUndefined.Instance;
        }, 3);
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

    private object EvDetail(JsCallArgs a) { return EV(a).Detail; }
    private object EvTimeStamp(JsCallArgs a) { return EV(a).TimeStamp; }
    private object EvClientX(JsCallArgs a) { return EV(a).ClientX; }
    private object EvClientY(JsCallArgs a) { return EV(a).ClientY; }
    private object EvPageX(JsCallArgs a) { return EV(a).PageX; }
    private object EvPageY(JsCallArgs a) { return EV(a).PageY; }
    private object EvScreenX(JsCallArgs a) { return EV(a).ScreenX; }
    private object EvScreenY(JsCallArgs a) { return EV(a).ScreenY; }
    private object EvButton(JsCallArgs a) { return (double)EV(a).Button; }
    private object EvButtons(JsCallArgs a) { return (double)EV(a).Buttons; }
    private object EvAltKey(JsCallArgs a) { return EV(a).AltKey; }
    private object EvCtrlKey(JsCallArgs a) { return EV(a).CtrlKey; }
    private object EvShiftKey(JsCallArgs a) { return EV(a).ShiftKey; }
    private object EvMetaKey(JsCallArgs a) { return EV(a).MetaKey; }
    private object EvKey(JsCallArgs a) { return EV(a).Key; }
    private object EvCode(JsCallArgs a) { return EV(a).Code; }
    private object EvKeyCode(JsCallArgs a) { return (double)EV(a).KeyCode; }
    private object EvWhich(JsCallArgs a) { return (double)EV(a).Which; }
    private object EvCharCode(JsCallArgs a) { return (double)EV(a).CharCode; }

    private object EvComposedPath(JsCallArgs a)
    {
        JsEventObject ev = EV(a);
        JsArray arr = Realm.NewArray();
        if (ev.Target != null) arr.Items.Add(ev.Target);
        if (ev.CurrentTarget != null && ev.CurrentTarget != ev.Target) arr.Items.Add(ev.CurrentTarget);
        arr.Items.Add(Document);
        arr.Items.Add(Realm.Global);
        return arr;
    }

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
        DefineAccessor(doc, "activeElement", DocActiveElement, null);

        DefineAccessor(doc, "defaultView", DocDefaultView, null);
        DefineAccessor(doc, "scrollingElement", DocScrollingElement, null);
        DefineAccessor(doc, "compatMode", DocCompatMode, null);
        DefineAccessor(doc, "characterSet", DocCharset, null);
        DefineAccessor(doc, "charset", DocCharset, null);
        DefineAccessor(doc, "inputEncoding", DocCharset, null);
        DefineAccessor(doc, "visibilityState", DocVisibilityState, null);
        DefineAccessor(doc, "hidden", DocHidden, null);
        DefineAccessor(doc, "forms", DocForms, null);
        DefineAccessor(doc, "images", DocImages, null);
        DefineAccessor(doc, "links", DocLinks, null);
        DefineAccessor(doc, "scripts", DocScripts, null);

        DefineOn(doc, "querySelector", DocQuerySelector, 1);
        DefineOn(doc, "querySelectorAll", DocQuerySelectorAll, 1);
        DefineOn(doc, "getElementById", DocGetElementById, 1);
        DefineOn(doc, "getElementsByTagName", DocGetElementsByTagName, 1);
        DefineOn(doc, "getElementsByClassName", DocGetElementsByClassName, 1);
        DefineOn(doc, "createElement", DocCreateElement, 1);
        DefineOn(doc, "createElementNS", DocCreateElementNS, 2);
        DefineOn(doc, "createDocumentFragment", DocCreateDocumentFragment, 0);
        DefineOn(doc, "createComment", DocCreateComment, 1);
        DefineOn(doc, "createTextNode", DocCreateTextNode, 1);
        DefineOn(doc, "importNode", DocImportNode, 2);
        DefineOn(doc, "adoptNode", DocAdoptNode, 1);
        DefineOn(doc, "hasFocus", DocHasFocus, 0);
        DefineOn(doc, "addEventListener", DocAddEventListener, 2);
        DefineOn(doc, "removeEventListener", DocRemoveEventListener, 2);
        DefineOn(doc, "dispatchEvent", DocDispatchEvent, 1);
        DefineOn(doc, "createRange", DocCreateRange, 0);
        DefineOn(doc, "createEvent", DocCreateEvent, 1);
        DefineOn(doc, "getElementsByName", DocGetElementsByName, 1);
        DefineOn(doc, "write", DocWrite, 1);
        DefineOn(doc, "writeln", DocWriteln, 1);
        DefineOn(doc, "open", DocOpen, 0);
        DefineOn(doc, "close", DocClose, 0);
        DefineAccessor(doc, "referrer", DocReferrer, null);
        DefineAccessor(doc, "domain", DocDomainGet, null);
        DefineOn(doc, "elementFromPoint", DocElementFromPoint, 2);
        DefineAccessor(doc, "currentScript", DocCurrentScript, null);
        DefineAccessor(doc, "styleSheets", DocStyleSheets, null);
        DefineOn(doc, "createTreeWalker", DocCreateTreeWalker, 1);
        DefineOn(doc, "createNodeIterator", DocCreateNodeIterator, 1);
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

    private object DocDefaultView(JsCallArgs a) { return Realm.Global; }
    private object DocScrollingElement(JsCallArgs a) { return DocDocumentElement(a); }
    private object DocCompatMode(JsCallArgs a) { return "CSS1Compat"; }
    private object DocCharset(JsCallArgs a) { return "UTF-8"; }
    private object DocVisibilityState(JsCallArgs a) { return "visible"; }
    private object DocHidden(JsCallArgs a) { return false; }
    private object DocHasFocus(JsCallArgs a) { return true; }

    private object DocForms(JsCallArgs a)
    {
        List<DomNode> hits = new List<DomNode>();
        CollectByTag(Root, "form", hits);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }
    private object DocImages(JsCallArgs a)
    {
        List<DomNode> hits = new List<DomNode>();
        CollectByTag(Root, "img", hits);
        CollectByTag(Root, "svg", hits);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }
    private object DocLinks(JsCallArgs a)
    {
        List<DomNode> hits = new List<DomNode>();
        CollectByTag(Root, "a", hits);
        CollectByTag(Root, "area", hits);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++)
            if (hits[i].GetAttr("href") != null) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }
    private object DocScripts(JsCallArgs a)
    {
        List<DomNode> hits = new List<DomNode>();
        CollectByTag(Root, "script", hits);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }

    private object DocGetElementsByClassName(JsCallArgs a)
    {
        string cls = JsValueOps.ToStringValue(a.Get(0));
        List<DomNode> hits = Root.GetElementsByClassName(cls);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < hits.Count; i++) arr.Items.Add(Wrap(hits[i]));
        return arr;
    }

    private object DocCreateDocumentFragment(JsCallArgs a)
    {
        DomNode frag = new DomNode { NodeType = DomNode.Element, Tag = "#document-fragment" };
        return Wrap(frag);
    }

    private object DocCreateComment(JsCallArgs a)
    {
        string t = JsValueOps.ToStringValue(a.Get(0));
        DomNode c = new DomNode { NodeType = DomNode.TextNode, Text = "<!--" + t + "-->" };
        return Wrap(c);
    }

    private object DocCreateElementNS(JsCallArgs a)
    {
        string tag = JsValueOps.ToStringValue(a.Get(1)).ToLowerInvariant();
        DomNode n = new DomNode { NodeType = DomNode.Element, Tag = tag };
        return Wrap(n);
    }

    private object DocImportNode(JsCallArgs a)
    {
        JsElementObject e = a.Get(0) as JsElementObject;
        if (e == null || e.Node == null) return JsNull.Instance;
        bool deep = a.ArgCount > 1 && JsValueOps.ToBoolean(a.Get(1));
        return Wrap(e.Node.CloneNode(deep));
    }

    private object DocAdoptNode(JsCallArgs a)
    {
        JsElementObject e = a.Get(0) as JsElementObject;
        if (e == null || e.Node == null) return JsNull.Instance;
        e.Node.Unlink();
        return e;
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

    private object DocCreateRange(JsCallArgs a)
    {
        if (Realm.Global.GetProperty("Range", out object rCtor) && rCtor is JsFunction)
        {
            return Realm.Interp.Call((JsFunction)rCtor, Realm.Global, new object[0], true);
        }
        return Realm.NewObject();
    }

    private object DocCreateEvent(JsCallArgs a)
    {
        JsEventObject ev = new JsEventObject(EventPrototype);
        ev.Type = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
        ev.TimeStamp = (double)DateTime.UtcNow.Ticks / 10000.0;
        return ev;
    }

    private object DocGetElementsByName(JsCallArgs a)
    {
        string name = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
        List<DomNode> matched = new List<DomNode>();
        CollectByName(Root, name, matched);
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < matched.Count; i++) arr.Push(Wrap(matched[i]));
        return arr;
    }

    private static void CollectByName(DomNode node, string name, List<DomNode> matched)
    {
        for (DomNode c = node.FirstChild; c != null; c = c.Next)
        {
            if (c.IsElement)
            {
                if (c.GetAttr("name") == name) matched.Add(c);
                CollectByName(c, name, matched);
            }
        }
    }

    private object DocWrite(JsCallArgs a)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < a.ArgCount; i++) sb.Append(JsValueOps.ToStringValue(a.Get(i)));
        string html = sb.ToString();
        List<DomNode> nodes = HtmlDom.ParseFragment(html);
        DomNode body = FindByTag(Root, "body");
        if (body == null) body = Root;
        for (int i = 0; i < nodes.Count; i++) body.AppendChild(nodes[i]);
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object DocWriteln(JsCallArgs a)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < a.ArgCount; i++) sb.Append(JsValueOps.ToStringValue(a.Get(i)));
        sb.Append('\n');
        string html = sb.ToString();
        List<DomNode> nodes = HtmlDom.ParseFragment(html);
        DomNode body = FindByTag(Root, "body");
        if (body == null) body = Root;
        for (int i = 0; i < nodes.Count; i++) body.AppendChild(nodes[i]);
        MarkChanged();
        return JsUndefined.Instance;
    }

    private object DocOpen(JsCallArgs a) { return Document; }
    private object DocClose(JsCallArgs a) { return Document; }
    private object DocReferrer(JsCallArgs a) { return ""; }

    private object DocDomainGet(JsCallArgs a)
    {
        string h = LocationHref;
        if (string.IsNullOrEmpty(h)) return "";
        try
        {
            int p = h.IndexOf("://");
            if (p >= 0)
            {
                int start = p + 3;
                int end = h.IndexOfAny(new char[] { '/', ':', '?', '#' }, start);
                return end >= 0 ? h.Substring(start, end - start) : h.Substring(start);
            }
        }
        catch { }
        return "";
    }

    private object DocElementFromPoint(JsCallArgs a)
    {
        DomNode body = FindByTag(Root, "body");
        DomNode res = body != null ? body : Root.FirstElementChild();
        return res != null ? (object)Wrap(res) : JsNull.Instance;
    }

    public DomNode CurrentExecutingScriptNode;
    private object DocCurrentScript(JsCallArgs a)
    {
        return CurrentExecutingScriptNode != null ? Wrap(CurrentExecutingScriptNode) : JsNull.Instance;
    }

    private object DocStyleSheets(JsCallArgs a)
    {
        JsArray arr = Realm.NewArray();
        List<DomNode> styleNodes = new List<DomNode>();
        CollectByTag(Root, "style", styleNodes);
        CollectByTag(Root, "link", styleNodes);
        for (int i = 0; i < styleNodes.Count; i++)
        {
            DomNode n = styleNodes[i];
            if (n.Tag == "link" && (n.GetAttr("rel") ?? "").IndexOf("stylesheet", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            JsObject sheet = Realm.NewObject();
            sheet.Define("type", JsProperty.Data("text/css"));
            sheet.Define("href", JsProperty.Data(n.Tag == "link" ? (n.GetAttr("href") ?? "") : ""));
            sheet.Define("ownerNode", JsProperty.Data(Wrap(n)));
            sheet.Define("cssRules", JsProperty.Data(Realm.NewArray()));
            sheet.Define("rules", JsProperty.Data(Realm.NewArray()));
            DefineOn(sheet, "insertRule", delegate (JsCallArgs args) { return 0.0; }, 1);
            DefineOn(sheet, "deleteRule", delegate (JsCallArgs args) { return JsUndefined.Instance; }, 1);
            arr.Items.Add(sheet);
        }
        return arr;
    }

    private object DocCreateTreeWalker(JsCallArgs a)
    {
        object rootVal = a.Get(0);
        JsObject tw = Realm.NewObject();
        tw.Define("root", JsProperty.Data(rootVal));
        tw.Define("currentNode", JsProperty.Data(rootVal));
        DefineOn(tw, "nextNode", delegate (JsCallArgs args)
        {
            object curVal = tw.GetPropertyOrUndefined("currentNode");
            JsElementObject curObj = curVal as JsElementObject;
            DomNode n = curObj != null ? curObj.Node : null;
            if (n != null)
            {
                DomNode next = n.FirstChild ?? n.Next ?? (n.Parent != null ? n.Parent.Next : null);
                if (next != null)
                {
                    object wrapped = Wrap(next);
                    tw.Define("currentNode", JsProperty.Data(wrapped));
                    return wrapped;
                }
            }
            return JsNull.Instance;
        }, 0);
        DefineOn(tw, "previousNode", delegate { return JsNull.Instance; }, 0);
        DefineOn(tw, "parentNode", delegate { return JsNull.Instance; }, 0);
        DefineOn(tw, "firstChild", delegate { return JsNull.Instance; }, 0);
        DefineOn(tw, "lastChild", delegate { return JsNull.Instance; }, 0);
        DefineOn(tw, "nextSibling", delegate { return JsNull.Instance; }, 0);
        DefineOn(tw, "previousSibling", delegate { return JsNull.Instance; }, 0);
        return tw;
    }

    private object DocCreateNodeIterator(JsCallArgs a)
    {
        return DocCreateTreeWalker(a);
    }

    private void BuildDomConstructors()
    {
        JsObject nodeProto = Realm.NewObject();
        JsFunction nodeCtor = Realm.MakeCtor("Node", delegate (JsCallArgs a)
        {
            throw JsErr.Type("Illegal constructor");
        }, 0, nodeProto);
        nodeCtor.Define("ELEMENT_NODE", JsProperty.DataNonEnum(1.0));
        nodeCtor.Define("ATTRIBUTE_NODE", JsProperty.DataNonEnum(2.0));
        nodeCtor.Define("TEXT_NODE", JsProperty.DataNonEnum(3.0));
        nodeCtor.Define("CDATA_SECTION_NODE", JsProperty.DataNonEnum(4.0));
        nodeCtor.Define("ENTITY_REFERENCE_NODE", JsProperty.DataNonEnum(5.0));
        nodeCtor.Define("ENTITY_NODE", JsProperty.DataNonEnum(6.0));
        nodeCtor.Define("PROCESSING_INSTRUCTION_NODE", JsProperty.DataNonEnum(7.0));
        nodeCtor.Define("COMMENT_NODE", JsProperty.DataNonEnum(8.0));
        nodeCtor.Define("DOCUMENT_NODE", JsProperty.DataNonEnum(9.0));
        nodeCtor.Define("DOCUMENT_TYPE_NODE", JsProperty.DataNonEnum(10.0));
        nodeCtor.Define("DOCUMENT_FRAGMENT_NODE", JsProperty.DataNonEnum(11.0));
        nodeCtor.Define("NOTATION_NODE", JsProperty.DataNonEnum(12.0));
        nodeCtor.Define("DOCUMENT_POSITION_DISCONNECTED", JsProperty.DataNonEnum(1.0));
        nodeCtor.Define("DOCUMENT_POSITION_PRECEDING", JsProperty.DataNonEnum(2.0));
        nodeCtor.Define("DOCUMENT_POSITION_FOLLOWING", JsProperty.DataNonEnum(4.0));
        nodeCtor.Define("DOCUMENT_POSITION_CONTAINS", JsProperty.DataNonEnum(8.0));
        nodeCtor.Define("DOCUMENT_POSITION_CONTAINED_BY", JsProperty.DataNonEnum(16.0));
        nodeCtor.Define("DOCUMENT_POSITION_IMPLEMENTATION_SPECIFIC", JsProperty.DataNonEnum(32.0));

        Realm.Global.Define("Node", JsProperty.Data(nodeCtor));

        ElementPrototype.Prototype = nodeProto;

        JsFunction elCtor = Realm.MakeCtor("Element", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, ElementPrototype);
        JsObject htmlElProto = Realm.NewObject(ElementPrototype);
        JsFunction htmlElCtor = Realm.MakeCtor("HTMLElement", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, htmlElProto);
        Realm.Global.Define("Element", JsProperty.Data(elCtor));
        Realm.Global.Define("HTMLElement", JsProperty.Data(htmlElCtor));

        string[] elementClasses = {
            "HTMLHtmlElement", "HTMLHeadElement", "HTMLBodyElement", "HTMLDivElement", "HTMLSpanElement",
            "HTMLParagraphElement", "HTMLAnchorElement", "HTMLImageElement", "HTMLButtonElement", "HTMLInputElement",
            "HTMLTextAreaElement", "HTMLSelectElement", "HTMLOptionElement", "HTMLFormElement", "HTMLTableElement",
            "HTMLTableRowElement", "HTMLTableCellElement", "HTMLUListElement", "HTMLOListElement", "HTMLLIElement",
            "HTMLScriptElement", "HTMLStyleElement", "HTMLLinkElement", "HTMLMetaElement", "HTMLIFrameElement",
            "HTMLCanvasElement", "HTMLAudioElement", "HTMLVideoElement", "HTMLUnknownElement"
        };
        for (int i = 0; i < elementClasses.Length; i++)
        {
            string name = elementClasses[i];
            JsObject subProto = Realm.NewObject(htmlElProto);
            JsFunction c = Realm.MakeCtor(name, delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, subProto);
            Realm.Global.Define(name, JsProperty.Data(c));
        }

        JsObject imgProto = Realm.NewObject(htmlElProto);
        Realm.Global.Define("Image", JsProperty.Data(Realm.MakeCtor("Image", delegate (JsCallArgs a)
        {
            DomNode img = new DomNode { NodeType = DomNode.Element, Tag = "img" };
            if (a.ArgCount > 0) SetAttribute(img, "width", JsValueOps.ToStringValue(a.Get(0)));
            if (a.ArgCount > 1) SetAttribute(img, "height", JsValueOps.ToStringValue(a.Get(1)));
            return Wrap(img);
        }, 0, imgProto)));

        JsObject audProto = Realm.NewObject(htmlElProto);
        Realm.Global.Define("Audio", JsProperty.Data(Realm.MakeCtor("Audio", delegate (JsCallArgs a)
        {
            DomNode aud = new DomNode { NodeType = DomNode.Element, Tag = "audio" };
            if (a.ArgCount > 0) SetAttribute(aud, "src", JsValueOps.ToStringValue(a.Get(0)));
            return Wrap(aud);
        }, 0, audProto)));

        JsObject docProto = Realm.NewObject(nodeProto);
        JsObject htmlDocProto = Realm.NewObject(docProto);
        Document.Prototype = htmlDocProto;
        Realm.Global.Define("Document", JsProperty.Data(Realm.MakeCtor("Document", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, docProto)));
        Realm.Global.Define("HTMLDocument", JsProperty.Data(Realm.MakeCtor("HTMLDocument", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, htmlDocProto)));
        JsObject docFragProto = Realm.NewObject(nodeProto);
        Realm.Global.Define("DocumentFragment", JsProperty.Data(Realm.MakeCtor("DocumentFragment", delegate (JsCallArgs a)
        {
            DomNode frag = new DomNode { NodeType = DomNode.Element, Tag = "#document-fragment" };
            return Wrap(frag);
        }, 0, docFragProto)));
        TextPrototype.Prototype = nodeProto;
        Realm.Global.Define("Text", JsProperty.Data(Realm.MakeCtor("Text", delegate (JsCallArgs a)
        {
            string t = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            DomNode n = new DomNode { NodeType = DomNode.TextNode, Text = t };
            return Wrap(n);
        }, 0, TextPrototype)));
        JsObject commentProto = Realm.NewObject(TextPrototype);
        Realm.Global.Define("Comment", JsProperty.Data(Realm.MakeCtor("Comment", delegate (JsCallArgs a)
        {
            string t = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            DomNode n = new DomNode { NodeType = DomNode.TextNode, Text = "<!--" + t + "-->" };
            return Wrap(n);
        }, 0, commentProto)));
        JsObject charDataProto = Realm.NewObject(TextPrototype);
        Realm.Global.Define("CharacterData", JsProperty.Data(Realm.MakeCtor("CharacterData", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, charDataProto)));

        JsObject collProto = Realm.NewObject();
        collProto.Define("length", JsProperty.DataNonEnum(0.0));
        collProto.Define("item", JsProperty.Data(Realm.MakeMethod("item", delegate (JsCallArgs a) { return JsNull.Instance; }, 1)));
        Realm.Global.Define("NodeList", JsProperty.Data(Realm.MakeCtor("NodeList", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, Realm.NewObject(collProto))));
        Realm.Global.Define("HTMLCollection", JsProperty.Data(Realm.MakeCtor("HTMLCollection", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, Realm.NewObject(collProto))));
        Realm.Global.Define("DOMTokenList", JsProperty.Data(Realm.MakeCtor("DOMTokenList", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, Realm.NewObject(collProto))));
        Realm.Global.Define("NamedNodeMap", JsProperty.Data(Realm.MakeCtor("NamedNodeMap", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, Realm.NewObject(collProto))));

        BuildRange(Realm);
        BuildCanvas2D(Realm);
    }

    private void BuildRange(JsRealm realm)
    {
        JsObject rangeProto = realm.NewObject();
        rangeProto.SetWellKnown(JsWellKnown.ToStringTag, "Range");

        rangeProto.Define("startOffset", JsProperty.DataNonEnum(0.0));
        rangeProto.Define("endOffset", JsProperty.DataNonEnum(0.0));
        rangeProto.Define("collapsed", JsProperty.DataNonEnum(true));
        rangeProto.Define("commonAncestorContainer", JsProperty.DataNonEnum(Document));
        rangeProto.Define("startContainer", JsProperty.DataNonEnum(Document));
        rangeProto.Define("endContainer", JsProperty.DataNonEnum(Document));

        JsNativeFn noop = delegate (JsCallArgs a) { return JsUndefined.Instance; };
        DefineOn(rangeProto, "setStart", noop, 2);
        DefineOn(rangeProto, "setEnd", noop, 2);
        DefineOn(rangeProto, "collapse", noop, 1);
        DefineOn(rangeProto, "selectNode", noop, 1);
        DefineOn(rangeProto, "selectNodeContents", noop, 1);
        DefineOn(rangeProto, "cloneRange", delegate (JsCallArgs a) { return a.ThisValue; }, 0);
        DefineOn(rangeProto, "detach", noop, 0);
        DefineOn(rangeProto, "deleteContents", noop, 0);

        DefineOn(rangeProto, "getBoundingClientRect", delegate (JsCallArgs a)
        {
            JsObject rect = realm.NewObject();
            rect.Define("x", JsProperty.DataNonEnum(0.0));
            rect.Define("y", JsProperty.DataNonEnum(0.0));
            rect.Define("width", JsProperty.DataNonEnum(0.0));
            rect.Define("height", JsProperty.DataNonEnum(0.0));
            rect.Define("top", JsProperty.DataNonEnum(0.0));
            rect.Define("left", JsProperty.DataNonEnum(0.0));
            rect.Define("right", JsProperty.DataNonEnum(0.0));
            rect.Define("bottom", JsProperty.DataNonEnum(0.0));
            return rect;
        }, 0);

        DefineOn(rangeProto, "getClientRects", delegate (JsCallArgs a)
        {
            return realm.NewArray();
        }, 0);

        DefineOn(rangeProto, "createContextualFragment", delegate (JsCallArgs a)
        {
            string html = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            DomNode frag = new DomNode { NodeType = DomNode.Element, Tag = "#document-fragment" };
            List<DomNode> nodes = HtmlDom.ParseFragment(html);
            for (int i = 0; i < nodes.Count; i++) frag.AppendChild(nodes[i]);
            return Wrap(frag);
        }, 1);

        realm.Global.Define("Range", JsProperty.Data(realm.MakeCtor("Range", delegate (JsCallArgs a)
        {
            return realm.NewObject(rangeProto);
        }, 0, rangeProto)));
    }

    private void BuildCanvas2D(JsRealm realm)
    {
        JsObject ctxProto = realm.NewObject();
        ctxProto.SetWellKnown(JsWellKnown.ToStringTag, "CanvasRenderingContext2D");

        ctxProto.Define("fillStyle", JsProperty.DataNonEnum("#000000"));
        ctxProto.Define("strokeStyle", JsProperty.DataNonEnum("#000000"));
        ctxProto.Define("lineWidth", JsProperty.DataNonEnum(1.0));
        ctxProto.Define("lineCap", JsProperty.DataNonEnum("butt"));
        ctxProto.Define("lineJoin", JsProperty.DataNonEnum("miter"));
        ctxProto.Define("miterLimit", JsProperty.DataNonEnum(10.0));
        ctxProto.Define("font", JsProperty.DataNonEnum("10px sans-serif"));
        ctxProto.Define("textAlign", JsProperty.DataNonEnum("start"));
        ctxProto.Define("textBaseline", JsProperty.DataNonEnum("alphabetic"));
        ctxProto.Define("globalAlpha", JsProperty.DataNonEnum(1.0));
        ctxProto.Define("globalCompositeOperation", JsProperty.DataNonEnum("source-over"));
        ctxProto.Define("shadowColor", JsProperty.DataNonEnum("rgba(0, 0, 0, 0)"));
        ctxProto.Define("shadowBlur", JsProperty.DataNonEnum(0.0));
        ctxProto.Define("shadowOffsetX", JsProperty.DataNonEnum(0.0));
        ctxProto.Define("shadowOffsetY", JsProperty.DataNonEnum(0.0));
        ctxProto.Define("imageSmoothingEnabled", JsProperty.DataNonEnum(true));

        JsNativeFn noop = delegate (JsCallArgs a) { return JsUndefined.Instance; };

        DefineOn(ctxProto, "fillRect", delegate (JsCallArgs a)
        {
            if (CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int w, out int h))
            {
                JsObject ctx = a.ThisValue as JsObject;
                string fs = ctx != null && ctx.GetProperty("fillStyle", out object fv) ? JsValueOps.ToStringValue(fv) : "#000000";
                double ga = ctx != null && ctx.GetProperty("globalAlpha", out object av) ? JsValueOps.ToNumber(av) : 1.0;
                uint c = CanvasParseColor(fs, ga);

                int rx = (int)JsValueOps.ToNumber(a.Get(0));
                int ry = (int)JsValueOps.ToNumber(a.Get(1));
                int rw = (int)JsValueOps.ToNumber(a.Get(2));
                int rh = (int)JsValueOps.ToNumber(a.Get(3));

                CanvasFillRect(bmp.RawData, w, h, rx, ry, rw, rh, c);
                MarkChanged();
            }
            return JsUndefined.Instance;
        }, 4);

        DefineOn(ctxProto, "strokeRect", delegate (JsCallArgs a)
        {
            if (CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int w, out int h))
            {
                JsObject ctx = a.ThisValue as JsObject;
                string ss = ctx != null && ctx.GetProperty("strokeStyle", out object sv) ? JsValueOps.ToStringValue(sv) : "#000000";
                double ga = ctx != null && ctx.GetProperty("globalAlpha", out object av) ? JsValueOps.ToNumber(av) : 1.0;
                double lw = ctx != null && ctx.GetProperty("lineWidth", out object lwv) ? JsValueOps.ToNumber(lwv) : 1.0;
                uint c = CanvasParseColor(ss, ga);

                int rx = (int)JsValueOps.ToNumber(a.Get(0));
                int ry = (int)JsValueOps.ToNumber(a.Get(1));
                int rw = (int)JsValueOps.ToNumber(a.Get(2));
                int rh = (int)JsValueOps.ToNumber(a.Get(3));
                int lineW = Math.Max(1, (int)lw);

                CanvasDrawLine(bmp.RawData, w, h, rx, ry, rx + rw, ry, c, lineW);
                CanvasDrawLine(bmp.RawData, w, h, rx + rw, ry, rx + rw, ry + rh, c, lineW);
                CanvasDrawLine(bmp.RawData, w, h, rx + rw, ry + rh, rx, ry + rh, c, lineW);
                CanvasDrawLine(bmp.RawData, w, h, rx, ry + rh, rx, ry, c, lineW);
                MarkChanged();
            }
            return JsUndefined.Instance;
        }, 4);

        DefineOn(ctxProto, "clearRect", delegate (JsCallArgs a)
        {
            if (CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int w, out int h))
            {
                int rx = (int)JsValueOps.ToNumber(a.Get(0));
                int ry = (int)JsValueOps.ToNumber(a.Get(1));
                int rw = (int)JsValueOps.ToNumber(a.Get(2));
                int rh = (int)JsValueOps.ToNumber(a.Get(3));

                CanvasClearRect(bmp.RawData, w, h, rx, ry, rw, rh);
                MarkChanged();
            }
            return JsUndefined.Instance;
        }, 4);

        DefineOn(ctxProto, "beginPath", delegate (JsCallArgs a)
        {
            canvasSubpaths.Clear();
            canvasSubpaths.Add(new List<CanvasPt>());
            return JsUndefined.Instance;
        }, 0);

        DefineOn(ctxProto, "closePath", delegate (JsCallArgs a)
        {
            if (canvasSubpaths.Count > 0 && canvasSubpaths[canvasSubpaths.Count - 1].Count > 1)
            {
                List<CanvasPt> pts = canvasSubpaths[canvasSubpaths.Count - 1];
                pts.Add(pts[0]);
            }
            return JsUndefined.Instance;
        }, 0);

        DefineOn(ctxProto, "moveTo", delegate (JsCallArgs a)
        {
            int x = (int)JsValueOps.ToNumber(a.Get(0));
            int y = (int)JsValueOps.ToNumber(a.Get(1));
            List<CanvasPt> pts = new List<CanvasPt>();
            pts.Add(new CanvasPt { X = x, Y = y });
            canvasSubpaths.Add(pts);
            return JsUndefined.Instance;
        }, 2);

        DefineOn(ctxProto, "lineTo", delegate (JsCallArgs a)
        {
            int x = (int)JsValueOps.ToNumber(a.Get(0));
            int y = (int)JsValueOps.ToNumber(a.Get(1));
            if (canvasSubpaths.Count == 0) canvasSubpaths.Add(new List<CanvasPt>());
            canvasSubpaths[canvasSubpaths.Count - 1].Add(new CanvasPt { X = x, Y = y });
            return JsUndefined.Instance;
        }, 2);

        DefineOn(ctxProto, "rect", delegate (JsCallArgs a)
        {
            int rx = (int)JsValueOps.ToNumber(a.Get(0));
            int ry = (int)JsValueOps.ToNumber(a.Get(1));
            int rw = (int)JsValueOps.ToNumber(a.Get(2));
            int rh = (int)JsValueOps.ToNumber(a.Get(3));
            List<CanvasPt> pts = new List<CanvasPt>
            {
                new CanvasPt { X = rx, Y = ry },
                new CanvasPt { X = rx + rw, Y = ry },
                new CanvasPt { X = rx + rw, Y = ry + rh },
                new CanvasPt { X = rx, Y = ry + rh },
                new CanvasPt { X = rx, Y = ry }
            };
            canvasSubpaths.Add(pts);
            return JsUndefined.Instance;
        }, 4);

        DefineOn(ctxProto, "arc", delegate (JsCallArgs a)
        {
            int cx = (int)JsValueOps.ToNumber(a.Get(0));
            int cy = (int)JsValueOps.ToNumber(a.Get(1));
            int radius = (int)JsValueOps.ToNumber(a.Get(2));
            double sa = a.ArgCount > 3 ? JsValueOps.ToNumber(a.Get(3)) : 0;
            double ea = a.ArgCount > 4 ? JsValueOps.ToNumber(a.Get(4)) : Math.PI * 2;
            bool ccw = a.ArgCount > 5 && JsValueOps.ToBoolean(a.Get(5));
            CanvasAddArc(cx, cy, radius, sa, ea, ccw);
            return JsUndefined.Instance;
        }, 6);

        DefineOn(ctxProto, "arcTo", noop, 5);
        DefineOn(ctxProto, "bezierCurveTo", noop, 6);
        DefineOn(ctxProto, "quadraticCurveTo", noop, 4);

        DefineOn(ctxProto, "fill", delegate (JsCallArgs a)
        {
            if (CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int w, out int h))
            {
                JsObject ctx = a.ThisValue as JsObject;
                string fs = ctx != null && ctx.GetProperty("fillStyle", out object fv) ? JsValueOps.ToStringValue(fv) : "#000000";
                double ga = ctx != null && ctx.GetProperty("globalAlpha", out object av) ? JsValueOps.ToNumber(av) : 1.0;
                uint c = CanvasParseColor(fs, ga);

                CanvasFillPath(bmp.RawData, w, h, c);
                MarkChanged();
            }
            return JsUndefined.Instance;
        }, 0);

        DefineOn(ctxProto, "stroke", delegate (JsCallArgs a)
        {
            if (CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int w, out int h))
            {
                JsObject ctx = a.ThisValue as JsObject;
                string ss = ctx != null && ctx.GetProperty("strokeStyle", out object sv) ? JsValueOps.ToStringValue(sv) : "#000000";
                double ga = ctx != null && ctx.GetProperty("globalAlpha", out object av) ? JsValueOps.ToNumber(av) : 1.0;
                double lw = ctx != null && ctx.GetProperty("lineWidth", out object lwv) ? JsValueOps.ToNumber(lwv) : 1.0;
                uint c = CanvasParseColor(ss, ga);

                CanvasStrokePath(bmp.RawData, w, h, c, Math.Max(1, (int)lw));
                MarkChanged();
            }
            return JsUndefined.Instance;
        }, 0);

        DefineOn(ctxProto, "clip", noop, 0);
        DefineOn(ctxProto, "isPointInPath", delegate (JsCallArgs a) { return false; }, 2);
        DefineOn(ctxProto, "isPointInStroke", delegate (JsCallArgs a) { return false; }, 2);

        DefineOn(ctxProto, "fillText", delegate (JsCallArgs a)
        {
            if (CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int w, out int h))
            {
                string text = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
                int tx = (int)JsValueOps.ToNumber(a.Get(1));
                int ty = (int)JsValueOps.ToNumber(a.Get(2));
                JsObject ctx = a.ThisValue as JsObject;
                string fs = ctx != null && ctx.GetProperty("fillStyle", out object fv) ? JsValueOps.ToStringValue(fv) : "#000000";
                double ga = ctx != null && ctx.GetProperty("globalAlpha", out object av) ? JsValueOps.ToNumber(av) : 1.0;
                uint c = CanvasParseColor(fs, ga);

                CanvasDrawText(bmp.RawData, w, h, text, tx, ty, c);
                MarkChanged();
            }
            return JsUndefined.Instance;
        }, 3);

        DefineOn(ctxProto, "strokeText", noop, 3);

        DefineOn(ctxProto, "measureText", delegate (JsCallArgs a)
        {
            string t = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            double w = t.Length * 8.0;
            JsObject metrics = realm.NewObject();
            metrics.Define("width", JsProperty.DataNonEnum(w));
            metrics.Define("actualBoundingBoxAscent", JsProperty.DataNonEnum(8.0));
            metrics.Define("actualBoundingBoxDescent", JsProperty.DataNonEnum(2.0));
            metrics.Define("actualBoundingBoxLeft", JsProperty.DataNonEnum(0.0));
            metrics.Define("actualBoundingBoxRight", JsProperty.DataNonEnum(w));
            return metrics;
        }, 1);

        DefineOn(ctxProto, "drawImage", delegate (JsCallArgs a)
        {
            if (a.ArgCount >= 3 && CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int cw, out int ch))
            {
                object imgArg = a.Get(0);
                JsElementObject el = imgArg as JsElementObject;
                Cosmos.Kernel.System.Graphics.Image srcImg = null;
                if (el != null && el.Node != null)
                {
                    srcImg = el.Node.CanvasSurface;
                }
                if (srcImg != null && srcImg.RawData != null)
                {
                    int dx = (int)JsValueOps.ToNumber(a.Get(1));
                    int dy = (int)JsValueOps.ToNumber(a.Get(2));
                    int dw = a.ArgCount > 3 ? (int)JsValueOps.ToNumber(a.Get(3)) : (int)srcImg.Width;
                    int dh = a.ArgCount > 4 ? (int)JsValueOps.ToNumber(a.Get(4)) : (int)srcImg.Height;
                    int sw = (int)srcImg.Width;
                    int sh = (int)srcImg.Height;

                    for (int y = 0; y < dh; y++)
                    {
                        int py = dy + y;
                        if (py < 0 || py >= ch) continue;
                        int sy = (y * sh) / dh;
                        for (int x = 0; x < dw; x++)
                        {
                            int px = dx + x;
                            if (px < 0 || px >= cw) continue;
                            int sx = (x * sw) / dw;
                            uint col = unchecked((uint)srcImg.RawData[sy * sw + sx]);
                            CanvasSetPixel(bmp.RawData, cw, ch, px, py, col);
                        }
                    }
                    MarkChanged();
                }
            }
            return JsUndefined.Instance;
        }, 3);

        DefineOn(ctxProto, "save", noop, 0);
        DefineOn(ctxProto, "restore", noop, 0);
        DefineOn(ctxProto, "scale", noop, 2);
        DefineOn(ctxProto, "rotate", noop, 1);
        DefineOn(ctxProto, "translate", noop, 2);
        DefineOn(ctxProto, "transform", noop, 6);
        DefineOn(ctxProto, "setTransform", noop, 6);
        DefineOn(ctxProto, "resetTransform", noop, 0);

        DefineOn(ctxProto, "createLinearGradient", delegate (JsCallArgs a)
        {
            JsObject grad = realm.NewObject();
            DefineOn(grad, "addColorStop", noop, 2);
            return grad;
        }, 4);
        DefineOn(ctxProto, "createRadialGradient", delegate (JsCallArgs a)
        {
            JsObject grad = realm.NewObject();
            DefineOn(grad, "addColorStop", noop, 2);
            return grad;
        }, 6);
        DefineOn(ctxProto, "createPattern", delegate (JsCallArgs a)
        {
            return realm.NewObject();
        }, 2);

        DefineOn(ctxProto, "createImageData", delegate (JsCallArgs a)
        {
            int w = (int)System.Math.Max(1, JsValueOps.ToNumber(a.Get(0)));
            int h = (int)System.Math.Max(1, a.ArgCount > 1 ? JsValueOps.ToNumber(a.Get(1)) : 1);
            JsObject imgData = realm.NewObject();
            imgData.Define("width", JsProperty.DataNonEnum((double)w));
            imgData.Define("height", JsProperty.DataNonEnum((double)h));
            byte[] bytes = new byte[w * h * 4];
            JsArrayBuffer buf = new JsArrayBuffer(realm.ObjectPrototype, bytes);
            JsTypedArray data = new JsTypedArray(realm.ObjectPrototype, buf, 0, bytes.Length, 1, "Uint8ClampedArray");
            imgData.Define("data", JsProperty.DataNonEnum(data));
            return imgData;
        }, 2);

        DefineOn(ctxProto, "getImageData", delegate (JsCallArgs a)
        {
            int sx = (int)JsValueOps.ToNumber(a.Get(0));
            int sy = (int)JsValueOps.ToNumber(a.Get(1));
            int sw = (int)System.Math.Max(1, JsValueOps.ToNumber(a.Get(2)));
            int sh = (int)System.Math.Max(1, a.ArgCount > 3 ? JsValueOps.ToNumber(a.Get(3)) : 1);
            JsObject imgData = realm.NewObject();
            imgData.Define("width", JsProperty.DataNonEnum((double)sw));
            imgData.Define("height", JsProperty.DataNonEnum((double)sh));
            byte[] bytes = new byte[sw * sh * 4];
            if (CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int cw, out int ch))
            {
                for (int y = 0; y < sh; y++)
                {
                    int py = sy + y;
                    if (py < 0 || py >= ch) continue;
                    for (int x = 0; x < sw; x++)
                    {
                        int px = sx + x;
                        if (px < 0 || px >= cw) continue;
                        int c = bmp.RawData[py * cw + px];
                        int dst = (y * sw + x) * 4;
                        bytes[dst] = (byte)((c >> 16) & 0xFF);     // R
                        bytes[dst + 1] = (byte)((c >> 8) & 0xFF);  // G
                        bytes[dst + 2] = (byte)(c & 0xFF);         // B
                        bytes[dst + 3] = (byte)((c >> 24) & 0xFF); // A
                    }
                }
            }
            JsArrayBuffer buf = new JsArrayBuffer(realm.ObjectPrototype, bytes);
            JsTypedArray data = new JsTypedArray(realm.ObjectPrototype, buf, 0, bytes.Length, 1, "Uint8ClampedArray");
            imgData.Define("data", JsProperty.DataNonEnum(data));
            return imgData;
        }, 4);

        DefineOn(ctxProto, "putImageData", delegate (JsCallArgs a)
        {
            if (a.ArgCount >= 3 && a.Get(0) is JsObject imgData && CanvasGetSurface(a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int cw, out int ch))
            {
                int dx = (int)JsValueOps.ToNumber(a.Get(1));
                int dy = (int)JsValueOps.ToNumber(a.Get(2));
                int sw = (int)JsValueOps.ToNumber(imgData.GetPropertyOrUndefined("width"));
                int sh = (int)JsValueOps.ToNumber(imgData.GetPropertyOrUndefined("height"));
                if (imgData.GetProperty("data", out object dataObj) && dataObj is JsTypedArray ta && ta.Buffer != null && ta.Buffer.Data != null)
                {
                    byte[] bytes = ta.Buffer.Data;
                    for (int y = 0; y < sh; y++)
                    {
                        int py = dy + y;
                        if (py < 0 || py >= ch) continue;
                        for (int x = 0; x < sw; x++)
                        {
                            int px = dx + x;
                            if (px < 0 || px >= cw) continue;
                            int src = (y * sw + x) * 4;
                            if (src + 3 < bytes.Length)
                            {
                                int r = bytes[src];
                                int g = bytes[src + 1];
                                int b = bytes[src + 2];
                                int alpha = bytes[src + 3];
                                bmp.RawData[py * cw + px] = (alpha << 24) | (r << 16) | (g << 8) | b;
                            }
                        }
                    }
                    MarkChanged();
                }
            }
            return JsUndefined.Instance;
        }, 3);

        CanvasContextPrototype = ctxProto;
        realm.Global.Define("CanvasRenderingContext2D", JsProperty.Data(realm.MakeCtor("CanvasRenderingContext2D", delegate (JsCallArgs a)
        {
            throw JsErr.Type("Illegal constructor");
        }, 0, ctxProto)));
    }

    private struct CanvasPt { public int X, Y; }
    private readonly List<List<CanvasPt>> canvasSubpaths = new List<List<CanvasPt>>();

    private static bool CanvasGetSurface(JsCallArgs a, out Cosmos.Kernel.System.Graphics.Bitmap bmp, out int w, out int h)
    {
        bmp = null; w = 300; h = 150;
        JsObject ctx = a.ThisValue as JsObject;
        if (ctx == null) return false;
        if (!ctx.GetProperty("canvas", out object cvs)) return false;
        JsElementObject el = cvs as JsElementObject;
        if (el == null || el.Node == null) return false;

        string sw = el.Node.GetAttr("width");
        string sh = el.Node.GetAttr("height");
        if (!string.IsNullOrEmpty(sw) && int.TryParse(sw, out int pw) && pw > 0) w = pw;
        if (!string.IsNullOrEmpty(sh) && int.TryParse(sh, out int ph) && ph > 0) h = ph;
        if (w > 2048) w = 2048; if (h > 2048) h = 2048;

        if (el.Node.CanvasSurface == null)
        {
            el.Node.CanvasSurface = new Cosmos.Kernel.System.Graphics.Bitmap(w, h, Cosmos.Kernel.System.Graphics.ColorDepth.ColorDepth32);
        }

        bmp = el.Node.CanvasSurface as Cosmos.Kernel.System.Graphics.Bitmap;
        return bmp != null && bmp.RawData != null;
    }

    private static int ConvertHexDigit(char ch)
    {
        if (ch >= '0' && ch <= '9') return ch - '0';
        if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
        if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
        return 0;
    }

    private static uint CanvasParseColor(string s, double globalAlpha)
    {
        uint c = 0xFF000000;
        if (string.IsNullOrEmpty(s)) return c;
        s = s.Trim().ToLowerInvariant();
        if (s.StartsWith("#"))
        {
            if (s.Length == 4)
            {
                int r = ConvertHexDigit(s[1]);
                int g = ConvertHexDigit(s[2]);
                int b = ConvertHexDigit(s[3]);
                c = 0xFF000000 | (uint)((r * 17) << 16) | (uint)((g * 17) << 8) | (uint)(b * 17);
            }
            else if (s.Length >= 7)
            {
                int r = (ConvertHexDigit(s[1]) << 4) | ConvertHexDigit(s[2]);
                int g = (ConvertHexDigit(s[3]) << 4) | ConvertHexDigit(s[4]);
                int b = (ConvertHexDigit(s[5]) << 4) | ConvertHexDigit(s[6]);
                c = 0xFF000000 | (uint)(r << 16) | (uint)(g << 8) | (uint)b;
            }
        }
        else if (s.StartsWith("rgb"))
        {
            int p1 = s.IndexOf('(');
            int p2 = s.IndexOf(')');
            if (p1 >= 0 && p2 > p1)
            {
                string[] parts = s.Substring(p1 + 1, p2 - p1 - 1).Split(',');
                if (parts.Length >= 3)
                {
                    int r = int.TryParse(parts[0].Trim(), out int pr) ? Math.Max(0, Math.Min(255, pr)) : 0;
                    int g = int.TryParse(parts[1].Trim(), out int pg) ? Math.Max(0, Math.Min(255, pg)) : 0;
                    int b = int.TryParse(parts[2].Trim(), out int pb) ? Math.Max(0, Math.Min(255, pb)) : 0;
                    int a = 255;
                    if (parts.Length >= 4 && float.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pa))
                        a = (int)(Math.Max(0f, Math.Min(1f, pa)) * 255);
                    c = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
                }
            }
        }
        else
        {
            switch (s)
            {
                case "red": c = 0xFFFF0000; break;
                case "green": c = 0xFF008000; break;
                case "blue": c = 0xFF0000FF; break;
                case "white": c = 0xFFFFFFFF; break;
                case "black": c = 0xFF000000; break;
                case "yellow": c = 0xFFFFFF00; break;
                case "cyan": c = 0xFF00FFFF; break;
                case "magenta": c = 0xFFFF00FF; break;
                case "gray": case "grey": c = 0xFF808080; break;
                case "orange": c = 0xFFFFA500; break;
                case "transparent": c = 0x00000000; break;
            }
        }

        if (globalAlpha < 1.0)
        {
            uint a = (c >> 24) & 0xFF;
            a = (uint)(a * Math.Max(0.0, Math.Min(1.0, globalAlpha)));
            c = (a << 24) | (c & 0x00FFFFFF);
        }
        return c;
    }

    private static void CanvasSetPixel(int[] raw, int w, int h, int x, int y, uint color)
    {
        if (x < 0 || x >= w || y < 0 || y >= h) return;
        uint sa = (color >> 24) & 0xFF;
        if (sa == 0) return;
        int idx = y * w + x;
        if (sa == 255) { raw[idx] = unchecked((int)color); return; }

        uint dst = unchecked((uint)raw[idx]);
        uint da = (dst >> 24) & 0xFF;
        uint inv = 255 - sa;
        uint r = ((color >> 16 & 0xFF) * sa + (dst >> 16 & 0xFF) * inv) / 255;
        uint g = ((color >> 8 & 0xFF) * sa + (dst >> 8 & 0xFF) * inv) / 255;
        uint b = ((color & 0xFF) * sa + (dst & 0xFF) * inv) / 255;
        uint a = sa + da * inv / 255;
        raw[idx] = unchecked((int)((a << 24) | (r << 16) | (g << 8) | b));
    }

    private static void CanvasDrawLine(int[] raw, int w, int h, int x0, int y0, int x1, int y1, uint color, int width)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy, e2;
        int rad = Math.Max(1, width / 2);

        while (true)
        {
            if (width <= 1) CanvasSetPixel(raw, w, h, x0, y0, color);
            else
            {
                for (int ny = -rad; ny <= rad; ny++)
                    for (int nx = -rad; nx <= rad; nx++)
                        CanvasSetPixel(raw, w, h, x0 + nx, y0 + ny, color);
            }
            if (x0 == x1 && y0 == y1) break;
            e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void CanvasFillPath(int[] raw, int w, int h, uint color)
    {
        int minY = h, maxY = 0;
        for (int p = 0; p < canvasSubpaths.Count; p++)
        {
            List<CanvasPt> pts = canvasSubpaths[p];
            for (int i = 0; i < pts.Count; i++)
            {
                if (pts[i].Y < minY) minY = pts[i].Y;
                if (pts[i].Y > maxY) maxY = pts[i].Y;
            }
        }
        if (minY < 0) minY = 0;
        if (maxY >= h) maxY = h - 1;

        List<int> xIntersects = new List<int>();
        for (int y = minY; y <= maxY; y++)
        {
            xIntersects.Clear();
            for (int p = 0; p < canvasSubpaths.Count; p++)
            {
                List<CanvasPt> pts = canvasSubpaths[p];
                int count = pts.Count;
                if (count < 3) continue;
                for (int i = 0; i < count; i++)
                {
                    CanvasPt p0 = pts[i];
                    CanvasPt p1 = pts[(i + 1) % count];
                    if ((p0.Y <= y && p1.Y > y) || (p1.Y <= y && p0.Y > y))
                    {
                        float t = (float)(y - p0.Y) / (p1.Y - p0.Y);
                        int x = (int)(p0.X + t * (p1.X - p0.X));
                        xIntersects.Add(x);
                    }
                }
            }
            xIntersects.Sort();
            for (int i = 0; i < xIntersects.Count - 1; i += 2)
            {
                int xStart = Math.Max(0, xIntersects[i]);
                int xEnd = Math.Min(w - 1, xIntersects[i + 1]);
                for (int x = xStart; x <= xEnd; x++)
                    CanvasSetPixel(raw, w, h, x, y, color);
            }
        }
    }

    private void CanvasStrokePath(int[] raw, int w, int h, uint color, int lineWidth)
    {
        for (int p = 0; p < canvasSubpaths.Count; p++)
        {
            List<CanvasPt> pts = canvasSubpaths[p];
            for (int i = 0; i < pts.Count - 1; i++)
            {
                CanvasDrawLine(raw, w, h, pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y, color, lineWidth);
            }
        }
    }

    private void CanvasFillRect(int[] raw, int cw, int ch, int rx, int ry, int rw, int rh, uint color)
    {
        int x0 = Math.Max(0, rx);
        int y0 = Math.Max(0, ry);
        int x1 = Math.Min(cw, rx + rw);
        int y1 = Math.Min(ch, ry + rh);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                CanvasSetPixel(raw, cw, ch, x, y, color);
            }
        }
    }

    private void CanvasClearRect(int[] raw, int cw, int ch, int rx, int ry, int rw, int rh)
    {
        int x0 = Math.Max(0, rx);
        int y0 = Math.Max(0, ry);
        int x1 = Math.Min(cw, rx + rw);
        int y1 = Math.Min(ch, ry + rh);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                raw[y * cw + x] = 0;
            }
        }
    }

    private void CanvasAddArc(int cx, int cy, int radius, double startAngle, double endAngle, bool counterclockwise)
    {
        if (canvasSubpaths.Count == 0) canvasSubpaths.Add(new List<CanvasPt>());
        List<CanvasPt> pts = canvasSubpaths[canvasSubpaths.Count - 1];

        int steps = Math.Max(12, Math.Min(64, radius * 2));
        double angleStep = (endAngle - startAngle) / steps;
        for (int i = 0; i <= steps; i++)
        {
            double a = startAngle + i * angleStep;
            int px = (int)(cx + radius * Math.Cos(a));
            int py = (int)(cy + radius * Math.Sin(a));
            pts.Add(new CanvasPt { X = px, Y = py });
        }
    }

    private void CanvasDrawText(int[] raw, int w, int h, string text, int tx, int ty, uint color)
    {
        if (string.IsNullOrEmpty(text)) return;
        int curX = tx;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            DrawSimpleChar(raw, w, h, ch, curX, ty, color);
            curX += 8;
        }
    }

    private static void DrawSimpleChar(int[] raw, int w, int h, char c, int cx, int cy, uint color)
    {
        // Simple 5x7 block matrix representation for legibility on canvas
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 6; x++)
            {
                if (x == 0 || y == 0 || (x == 5 && c >= 'A' && c <= 'Z') || (y == 4 && (c == 'A' || c == 'H' || c == 'E' || c == 'F')))
                    CanvasSetPixel(raw, w, h, cx + x, cy + y, color);
            }
        }
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
        ev.TimeStamp = (double)DateTime.UtcNow.Ticks / 10000.0;
        return DispatchExisting(target, ev);
    }

    /// <summary>Create + dispatch a mouse event carrying coordinates.</summary>
    public bool DispatchMouse(DomNode target, string type, int clientX, int clientY, int button)
    {
        JsEventObject ev = new JsEventObject(EventPrototype);
        ev.Type = type;
        ev.Target = Wrap(target);
        ev.ClientX = clientX;
        ev.ClientY = clientY;
        ev.PageX = clientX;
        ev.PageY = clientY;
        ev.Button = button;
        ev.Buttons = button == 0 ? 1 : 0;
        ev.TimeStamp = (double)DateTime.UtcNow.Ticks / 10000.0;
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
    public void SetStyleAttrDirect(DomNode n, string name, string val)
    {
        WriteStyleAttr(n, StyleAttr.Set(n.StyleAttr, name, val));
        MarkChanged();
    }

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
            if (a.ArgCount > 1)
            {
                bool force = JsValueOps.ToBoolean(a.Get(1));
                if (force) AddClass(o.Node, c); else RemoveClass(o.Node, c);
                MarkChanged();
                return force;
            }
            if (had) RemoveClass(o.Node, c); else AddClass(o.Node, c);
            MarkChanged();
            return !had;
        }, 1);
        DefineOn(cl, "contains", delegate (JsCallArgs a)
        {
            return HasClass(o.Node, JsValueOps.ToStringValue(a.Get(0)));
        }, 1);
        DefineOn(cl, "replace", delegate (JsCallArgs a)
        {
            string oldC = JsValueOps.ToStringValue(a.Get(0));
            string newC = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
            if (HasClass(o.Node, oldC))
            {
                RemoveClass(o.Node, oldC);
                AddClass(o.Node, newC);
                MarkChanged();
                return true;
            }
            return false;
        }, 2);
        DefineOn(cl, "item", delegate (JsCallArgs a)
        {
            int idx = (int)JsValueOps.ToNumber(a.Get(0));
            if (o.Node.Classes != null && idx >= 0 && idx < o.Node.Classes.Count)
                return o.Node.Classes[idx];
            return JsNull.Instance;
        }, 1);
        DefineOn(cl, "forEach", delegate (JsCallArgs a)
        {
            JsFunction fn = a.Get(0) as JsFunction;
            if (fn != null && o.Node.Classes != null)
            {
                object thisArg = a.Get(1);
                for (int i = 0; i < o.Node.Classes.Count; i++)
                    Realm.Interp.Call(fn, thisArg, new object[] { o.Node.Classes[i], (double)i, cl }, false);
            }
            return JsUndefined.Instance;
        }, 1);
        DefineOn(cl, "toString", delegate (JsCallArgs a)
        {
            return o.Node.GetAttr("class") ?? "";
        }, 0);
        DefineAccessor(cl, "length", delegate (JsCallArgs a)
        {
            return (double)(o.Node.Classes != null ? o.Node.Classes.Count : 0);
        }, null);
        DefineAccessor(cl, "value",
            delegate (JsCallArgs a) { return o.Node.GetAttr("class") ?? ""; },
            delegate (JsCallArgs a) { SetAttribute(o.Node, "class", JsValueOps.ToStringValue(a.Get(0))); MarkChanged(); return JsUndefined.Instance; });
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

    public static string CamelToKebab(string s)
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
