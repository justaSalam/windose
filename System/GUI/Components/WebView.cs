using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Graphics;

/// <summary>
/// Renders a parsed web page (a <see cref="RenderPlan"/>) with scrolling, block
/// backgrounds/borders, images, styled text and clickable links. Networking and
/// parsing happen outside; this only lays out (on resize) and draws.
/// </summary>
public class WebView : Component
{
    public override bool HandlesMouseWheel => true;

    public int fontSize = 16;
    public int scrollbarSize = 16;

    /// <summary>Raised with the raw href when the user clicks a link.</summary>
    public Action<string> LinkActivated;

    /// <summary>Raised with the raw href when the user hovers over a link, or null when mouse leaves links.</summary>
    public Action<string> LinkHovered;
    private string lastHoveredLink;

    /// <summary>True when an input, textarea or button holds focus inside the page.</summary>
    public bool HasFocusedWidget => focusWidget != null;

    /// <summary>Script-requested navigation (location.href = ..., window.open).</summary>
    public Action<string> NavigateRequested;
    public Action ReloadRequested;
    public Action HistoryBackRequested;
    public Action HistoryForwardRequested;

    /// <summary>Host-provided HTTP used by fetch()/XHR. Runs on a worker thread;
    /// return null (or throw) to signal a network failure.</summary>
    public delegate JsFetchResult HttpFetchDelegate(string method, string url, string headers, string body);
    public HttpFetchDelegate HttpFetch;

    /// <summary>Host cookie jar access for document.cookie (UI thread).</summary>
    public delegate string JsCookieGetter(string url);
    public delegate void JsCookieSetter(string cookie, string url);
    public JsCookieGetter GetCookies;
    public JsCookieSetter SetCookie;

    /// <summary>A form was submitted with method=post (url, urlencoded body).
    /// The host performs the navigation; nothing happens when unhooked.</summary>
    public delegate void FormPostDelegate(string url, string body);
    public FormPostDelegate FormPostRequested;

    private HtmlPage document;
    private RenderPlan plan = new RenderPlan();
    private string baseHref = "";
    private int scrollY;
    private int layoutWidth = -1;
    private Dictionary<string, Image> images = new Dictionary<string, Image>();

    // Script engine: one interpreter + DOM per loaded page.
    private JsInterpreter js;
    private JsDom dom;
    private bool layoutDirty;

    // JS fetch/XHR: requests are executed on a worker thread and delivered
    // back on the UI thread (same handoff pattern as page loads), so scripts
    // never block and all engine code stays on one thread.
    private List<JsFetchRequest> fetchBatch;
    private List<JsFetchResult> fetchResults;
    private bool fetchResultsReady;
    private bool fetchBusy;

    // Form-control interaction (stage 3): the focused control, its caret
    // position, blink phase, and the open <select> dropdown with its rows in
    // content-space coordinates.
    private DomNode focusWidget;
    private int caret;
    private bool caretOn = true;
    private long lastBlinkTicks;
    private DomNode openSelect;
    private int selectHighlight;
    private int popX, popTop, popW, popH, popRow, popCount;

    public string DocumentTitle => document != null ? document.Title : "";
    public string BaseHref => baseHref;

    public WebView(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        Margin = new Thickness(0);
    }

    /// <summary>Swap in a freshly fetched/parsed page and reset scroll.</summary>
    public void SetDocument(HtmlPage page, string baseHref)
    {
        SetDocument(page, baseHref, null);
    }

    public void SetDocument(HtmlPage page, string baseHref, Dictionary<string, Image> pageImages)
    {
        this.document = page;
        this.baseHref = baseHref ?? (page != null ? page.BaseHref : "");
        this.images = pageImages != null ? pageImages : new Dictionary<string, Image>();
        scrollY = 0;

        // form controls belong to the old document; drop focus/dropdown state
        focusWidget = null;
        openSelect = null;
        caret = 0;
        if (lastHoveredLink != null)
        {
            lastHoveredLink = null;
            if (LinkHovered != null) LinkHovered(null);
        }

        StartEngine();
        RebuildLayout();
        MarkDirty();
    }

    /// <summary>Create a fresh interpreter + DOM for the page and run its scripts
    /// in document order, then fire DOMContentLoaded and load.</summary>
    private void StartEngine()
    {
        js = null;
        dom = null;
        if (document == null || document.Root == null) return;
        if (document.Scripts == null || document.Scripts.Count == 0) return; // static page: no engine needed

        js = new JsInterpreter();
        js.InitGlobal();

        // Event loop: location, viewport and host hooks (navigation, history).
        JsEventLoop loop = js.EnsureLoop();
        loop.LocationHref = baseHref;
        loop.NavigateHook = OnJsNavigate;
        loop.ReloadHook = OnJsReload;
        loop.HistoryBackHook = OnJsHistoryBack;
        loop.HistoryForwardHook = OnJsHistoryForward;
        loop.ViewportWidth = Width;
        loop.ViewportHeight = Height;
        loop.CookieGetter = OnJsGetCookies;
        loop.CookieSetter = OnJsSetCookie;

        dom = new JsDom(js, document.Root);
        dom.OnDomChanged = OnDomMutated;
        dom.OnFocusRequest = SetFocusWidget;
        dom.OnFormSubmit = OnJsFormSubmit;
        dom.OnFormReset = OnJsFormReset;
        dom.Install();
        dom.SetStylesheet(document.CssText, Width, Height);

        RunScripts();
        dom.FireReadyEvents();
        js.DrainJobs();   // DOMContentLoaded/load handlers may have queued reactions

        // scripts may have mutated the tree; relayout once at the end
        if (layoutDirty)
        {
            layoutDirty = false;
            RebuildLayout();
        }
    }

    private void OnJsNavigate(string url)
    {
        if (NavigateRequested != null) NavigateRequested(url);
    }
    private void OnJsReload() { if (ReloadRequested != null) ReloadRequested(); }
    private void OnJsHistoryBack(string url) { if (HistoryBackRequested != null) HistoryBackRequested(); }
    private void OnJsHistoryForward(string url) { if (HistoryForwardRequested != null) HistoryForwardRequested(); }
    private string OnJsGetCookies(string url) { return GetCookies != null ? GetCookies(url) : null; }
    private void OnJsSetCookie(string cookie, string url) { if (SetCookie != null) SetCookie(cookie, url); }

    private void RunScripts()
    {
        if (js == null || dom == null) return;
        for (int i = 0; i < document.Scripts.Count; i++)
        {
            HtmlScript s = document.Scripts[i];
            if (string.IsNullOrEmpty(s.Code)) continue; // external script failed to fetch
            try
            {
                JsProgram prog = JsParser.ParseProgram(s.Code);
                js.RunProgram(prog);
                js.DrainJobs();   // microtask checkpoint after each script
            }
            catch (JsThrown) { /* a syntax/runtime error in one script must not stop the rest */ }
            catch (Exception) { }
        }
    }

    private void OnDomMutated()
    {
        layoutDirty = true;
        MarkDirty();
    }

    /// <summary>Dispatch a DOM click event at a content-space point. Returns true
    /// if the default was prevented (host should not navigate).</summary>
    private bool DispatchClickAt(int contentX, int contentY)
    {
        if (dom == null) return false;
        DomNode node = plan.HitTestNode(contentX, contentY);
        if (node == null) return false;
        bool defaultPrevented = !dom.Dispatch(node, "click"); // Dispatch returns !defaultPrevented
        js.DrainJobs();   // handler may have queued Promise reactions
        return defaultPrevented;
    }

    // ---- form-control interaction (stage 3) -------------------------------

    /// <summary>The live value of a control (kept on the node so layout, the
    /// DOM bindings and form submission always agree).</summary>
    public static string WidgetValueOf(DomNode node)
    {
        if (node == null || !node.IsElement) return "";
        string tag = node.Tag;
        if (tag == "textarea")
        {
            string v = node.GetAttr("value");
            return v != null ? v : (node.TextContent ?? "");
        }
        if (tag == "select") return HtmlLayout.SelectedOptionLabel(node);
        if (tag == "button") return node.GetAttr("value") ?? "";
        string val = node.GetAttr("value");
        return val != null ? val : "";
    }

    /// <summary>An input's type attribute, lowercased ("text" when absent).</summary>
    public static string InputTypeOf(DomNode node)
    {
        string t = node != null ? node.GetAttr("type") : null;
        if (t == null || t.Length == 0) return "text";
        return t.ToLowerInvariant();
    }

    /// <summary>The widget kind for a node, read live (the plan may be stale).</summary>
    public static byte WidgetKindOfNode(DomNode node)
    {
        if (node == null || !node.IsElement) return WidgetKind.NotAWidget;
        string tag = node.Tag;
        if (tag == "input")
        {
            string t = InputTypeOf(node);
            if (t == "hidden") return WidgetKind.NotAWidget;
            if (t == "password") return WidgetKind.Password;
            if (t == "checkbox") return WidgetKind.Checkbox;
            if (t == "radio") return WidgetKind.Radio;
            if (t == "submit" || t == "button" || t == "reset" || t == "image" || t == "file")
                return WidgetKind.Button;
            return WidgetKind.Text;
        }
        if (tag == "button") return WidgetKind.Button;
        if (tag == "textarea") return WidgetKind.Textarea;
        if (tag == "select") return WidgetKind.Select;
        return WidgetKind.NotAWidget;
    }

    /// <summary>Write a control's live value (stashing the parse-time default
    /// the first time so <input type=reset> can restore it).</summary>
    public static void SetWidgetValue(DomNode node, string value)
    {
        if (node == null) return;
        StashWidgetDefaults(node);
        JsDom.SetAttribute(node, "value", value != null ? value : "");
    }

    /// <summary Remember a control's parse-time value/checked for reset.</summary>
    public static void StashWidgetDefaults(DomNode node)
    {
        if (node == null || node.Attrs == null && node.Tag != "textarea" && node.Tag != "input" && node.Tag != "select") return;
        if (node.Attrs == null) node.Attrs = new Dictionary<string, string>();
        if (!node.Attrs.ContainsKey("data-def-value"))
        {
            string v;
            if (node.Tag == "textarea") v = node.GetAttr("value") ?? node.TextContent ?? "";
            else if (node.Tag == "select") v = node.GetAttr("value") ?? "";
            else v = node.GetAttr("value") ?? "";
            node.Attrs["data-def-value"] = v;
        }
        if (!node.Attrs.ContainsKey("data-def-checked"))
            node.Attrs["data-def-checked"] = node.GetAttr("checked") != null ? "1" : "0";
    }

    private static string Mask(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return new string('*', s.Length);
    }

    /// <summary>Focus a form control (or null to blur). Fires focus/blur.</summary>
    private void SetFocusWidget(DomNode node)
    {
        if (focusWidget == node) return;
        DomNode old = focusWidget;
        focusWidget = node;
        caret = 0;
        caretOn = true;
        if (node != null)
        {
            RequestFocus();   // route key events here
            caret = WidgetValueOf(node).Length;
        }
        if (dom != null) dom.ActiveElementNode = node;   // document.activeElement
        if (old != null && dom != null) { dom.Dispatch(old, "blur"); DrainQuiet(); }
        if (node != null && dom != null) { dom.Dispatch(node, "focus"); DrainQuiet(); }
        MarkDirty();
    }

    // -- script-driven form submission (form.submit(), a submit button) -----

    private void OnJsFormSubmit(DomNode form, DomNode submitter)
    {
        SubmitFromControl(submitter != null ? submitter : form);
    }

    private void OnJsFormReset(DomNode form)
    {
        ResetForm(form);
    }

    private void DrainQuiet()
    {
        if (js != null) js.DrainJobs();
    }

    /// <summary>Value changed by typing: fire "input".</summary>
    private void AfterWidgetInput()
    {
        if (dom != null && focusWidget != null) { dom.Dispatch(focusWidget, "input"); DrainQuiet(); }
        MarkDirty();
    }

    /// <summary>A control's committed value changed (selection, checkbox):
    /// fire "change" (and "input" first, like browsers do).</summary>
    private void FireWidgetChange(DomNode node)
    {
        if (dom == null || node == null) return;
        dom.Dispatch(node, "input");
        dom.Dispatch(node, "change");
        DrainQuiet();
        MarkDirty();
    }

    // -- select dropdown ----------------------------------------------------

    private static List<DomNode> OptionNodes(DomNode select)
    {
        List<DomNode> opts = new List<DomNode>();
        if (select == null) return opts;
        CollectOptions(select, opts);
        return opts;
    }

    private static void CollectOptions(DomNode node, List<DomNode> opts)
    {
        for (DomNode c = node.FirstChild; c != null; c = c.Next)
        {
            if (!c.IsElement) continue;
            if (c.Tag == "option") opts.Add(c);
            else if (c.Tag == "optgroup") CollectOptions(c, opts);
        }
    }

    private RenderWidget FindWidgetFor(DomNode node)
    {
        for (int i = 0; i < plan.Widgets.Count; i++)
            if (plan.Widgets[i].Node == node) return plan.Widgets[i];
        return null;
    }

    /// <summary>Open a select's dropdown, placing the popup in content coords
    /// below the control (above it when there's no room).</summary>
    private void OpenSelectPopup(DomNode select)
    {
        RenderWidget host = FindWidgetFor(select);
        List<DomNode> opts = OptionNodes(select);
        if (host == null || opts.Count == 0) return;

        int rowH = Math.Max(18, host.FontSize + 6);
        int w = Math.Max(host.W, 140);
        int h = rowH * opts.Count;
        int top = host.Y + host.H;
        if (top - scrollY + h > Height && host.Y - scrollY - h > 0) top = host.Y - h;

        openSelect = select;
        selectHighlight = IndexOfSelected(opts, select);
        popX = host.X;
        popTop = top;
        popW = w;
        popH = h;
        popRow = rowH;
        popCount = opts.Count;
        MarkDirty();
    }

    private static int IndexOfSelected(List<DomNode> opts, DomNode select)
    {
        DomNode sel = HtmlLayout.SelectedOption(select);
        for (int i = 0; i < opts.Count; i++) if (opts[i] == sel) return i;
        return 0;
    }

    private void CloseSelectPopup()
    {
        if (openSelect == null) return;
        openSelect = null;
        MarkDirty();
    }

    /// <summary>True when a press landed inside the open dropdown; picks the
    /// option it hit (or closes the list when it lands on the list edge).</summary>
    private bool HitSelectPopup(int contentX, int contentY)
    {
        if (openSelect == null) return false;
        if (contentX < popX || contentX >= popX + popW) { CloseSelectPopup(); return true; }
        if (contentY < popTop || contentY >= popTop + popH)
        {
            // outside the list (but the press was already captured by us):
            // ignore presses on the host control (it re-toggles), close otherwise
            RenderWidget host = FindWidgetFor(openSelect);
            if (host != null && contentX >= host.X && contentX < host.X + host.W
                && contentY >= host.Y && contentY < host.Y + host.H)
            {
                PickSelectOption(openSelect, selectHighlight);
            }
            else CloseSelectPopup();
            return true;
        }
        int row = (contentY - popTop) / Math.Max(1, popRow);
        if (row >= popCount) row = popCount - 1;
        PickSelectOption(openSelect, row);
        return true;
    }

    /// <summary>Apply the highlighted option: set "selected", fire change.</summary>
    private void PickSelectOption(DomNode select, int index)
    {
        List<DomNode> opts = OptionNodes(select);
        if (index < 0 || index >= opts.Count) return;
        for (int i = 0; i < opts.Count; i++)
        {
            if (i == index)
            {
                if (opts[i].GetAttr("selected") == null) JsDom.SetAttribute(opts[i], "selected", "");
            }
            else if (opts[i].GetAttr("selected") != null) opts[i].Attrs.Remove("selected");
        }
        CloseSelectPopup();
        FireWidgetChange(select);
    }

    /// <summary>Move a select's value by delta (arrow keys when not open).</summary>
    private void MoveSelect(DomNode select, int delta)
    {
        List<DomNode> opts = OptionNodes(select);
        if (opts.Count == 0) return;
        int idx = IndexOfSelected(opts, select) + delta;
        if (idx < 0) idx = opts.Count - 1;
        if (idx >= opts.Count) idx = 0;
        PickSelectOption(select, idx);
    }

    // -- checkbox / radio ---------------------------------------------------

    private void ToggleCheckbox(DomNode node)
    {
        StashWidgetDefaults(node);
        bool next = node.GetAttr("checked") == null;
        if (next) JsDom.SetAttribute(node, "checked", "");
        else node.Attrs.Remove("checked");
        FireWidgetChange(node);
    }

    private void CheckRadio(DomNode node)
    {
        if (node.GetAttr("checked") != null) return;   // already on: no change
        string name = node.GetAttr("name");
        DomNode form = AncestorForm(node);
        DomNode scope = form != null ? form : (document != null ? document.Root : null);
        if (scope != null && name != null && name.Length > 0)
        {
            List<DomNode> controls = new List<DomNode>();
            CollectFormControls(scope, controls);
            for (int i = 0; i < controls.Count; i++)
            {
                DomNode c = controls[i];
                if (c.Tag != "input" || InputTypeOf(c) != "radio") continue;
                if (c.GetAttr("name") != name) continue;
                if (c == node) continue;
                if (c.GetAttr("checked") != null) c.Attrs.Remove("checked");
            }
        }
        StashWidgetDefaults(node);
        JsDom.SetAttribute(node, "checked", "");
        FireWidgetChange(node);
    }

    // -- click routing ------------------------------------------------------

    /// <summary>Handle a press that landed on (or inside) a form control.
    /// Called after the DOM click event was dispatched and not prevented.</summary>
    private void HandleWidgetClick(RenderWidget w, int contentX, int contentY)
    {
        byte kind = WidgetKindOfNode(w.Node);
        switch (kind)
        {
            case WidgetKind.Text:
            case WidgetKind.Password:
            case WidgetKind.Textarea:
                CloseSelectPopup();
                SetFocusWidget(w.Node);
                break;

            case WidgetKind.Checkbox:
                CloseSelectPopup();
                ToggleCheckbox(w.Node);
                break;

            case WidgetKind.Radio:
                CloseSelectPopup();
                CheckRadio(w.Node);
                break;

            case WidgetKind.Button:
            {
                CloseSelectPopup();
                string type = w.Node.Tag == "input" ? InputTypeOf(w.Node) : (w.Node.GetAttr("type") ?? "submit");
                type = type.ToLowerInvariant();
                if (type == "reset") { ResetForm(AncestorForm(w.Node)); break; }
                // submit (or the default for <button>)
                SubmitFromControl(w.Node);
                break;
            }

            case WidgetKind.Select:
                SetFocusWidget(w.Node);
                if (openSelect == w.Node) PickSelectOption(w.Node, selectHighlight);
                else OpenSelectPopup(w.Node);
                break;
        }
    }

    private static DomNode AncestorForm(DomNode node)
    {
        DomNode n = node;
        while (n != null && !(n.IsElement && n.Tag == "form")) n = n.Parent;
        return n;
    }

    private static void CollectFormControls(DomNode scope, List<DomNode> outp)
    {
        for (DomNode c = scope.FirstChild; c != null; c = c.Next)
        {
            if (!c.IsElement) continue;
            if (c.Tag == "input" || c.Tag == "select" || c.Tag == "textarea" || c.Tag == "button")
                outp.Add(c);
            CollectFormControls(c, outp);
        }
    }

    // -- form submission ----------------------------------------------------

    /// <summary>Submit the form a control belongs to (Enter, or a submit
    /// button). GET appends the query and navigates; POST asks the host.</summary>
    private void SubmitFromControl(DomNode control)
    {
        DomNode form = AncestorForm(control);
        string method = form != null ? form.GetAttr("method") : null;
        if (method == null || method.Length == 0) method = "get";
        method = method.ToLowerInvariant();
        string action = form != null ? form.GetAttr("action") : null;
        string target = action != null && action.Length > 0 ? WebUrl.Resolve(baseHref, action) : baseHref;

        string data = CollectFormData(form, control);

        if (method == "post")
        {
            if (FormPostRequested != null) FormPostRequested(target, data);
            return;
        }

        string url = target;
        if (data.Length > 0)
        {
            int hash = url.IndexOf('#');
            if (hash >= 0) url = url.Substring(0, hash);
            url += url.IndexOf('?') >= 0 ? "&" + data : "?" + data;
        }
        if (NavigateRequested != null) NavigateRequested(url);
    }

    /// <summary>Successful controls as a urlencoded query string.</summary>
    private string CollectFormData(DomNode form, DomNode submitter)
    {
        string outp = "";
        DomNode scope = form != null ? form : (document != null ? document.Root : null);
        if (scope == null) return outp;

        List<DomNode> controls = new List<DomNode>();
        CollectFormControls(scope, controls);
        for (int i = 0; i < controls.Count; i++)
        {
            DomNode c = controls[i];
            string name = c.GetAttr("name");
            if (name == null || name.Length == 0) continue;
            if (c.GetAttr("disabled") != null) continue;

            string value = null;
            string tag = c.Tag;
            if (tag == "input")
            {
                string type = InputTypeOf(c);
                if (type == "submit" || type == "image")
                {
                    if (c != submitter) continue;
                    value = c.GetAttr("value") ?? "";
                }
                else if (type == "button" || type == "reset") continue;
                else if (type == "checkbox" || type == "radio")
                {
                    if (c.GetAttr("checked") == null) continue;
                    value = c.GetAttr("value") ?? "on";
                }
                else value = WidgetValueOf(c);
            }
            else if (tag == "select")
            {
                DomNode opt = HtmlLayout.SelectedOption(c);
                if (opt == null) continue;
                value = HtmlLayout.OptionLabel(opt);
            }
            else if (tag == "textarea") value = WidgetValueOf(c);
            else if (tag == "button")
            {
                string type = c.GetAttr("type") ?? "submit";
                if (type != "submit" || c != submitter) continue;
                value = c.GetAttr("value") ?? "";
            }
            else continue;

            if (outp.Length > 0) outp += "&";
            outp += UrlEncode(name) + "=" + UrlEncode(value);
        }
        return outp;
    }

    /// <summary>Restore every control in a form to its parse-time state.</summary>
    private void ResetForm(DomNode form)
    {
        DomNode scope = form != null ? form : (document != null ? document.Root : null);
        if (scope == null) return;
        List<DomNode> controls = new List<DomNode>();
        CollectFormControls(scope, controls);
        for (int i = 0; i < controls.Count; i++)
        {
            DomNode c = controls[i];
            if (c.Attrs == null) continue;
            string def = c.Attrs.ContainsKey("data-def-value") ? c.Attrs["data-def-value"] : null;
            if (def == null) continue;
            if (c.Tag == "textarea" && c.GetAttr("value") == null && c.TextContent != def)
            {
                c.RemoveAllChildren();
                if (def.Length > 0) c.AddText(def);
            }
            JsDom.SetAttribute(c, "value", def);
            string defChecked = c.Attrs.ContainsKey("data-def-checked") ? c.Attrs["data-def-checked"] : null;
            if (c.Tag == "input" && (InputTypeOf(c) == "checkbox" || InputTypeOf(c) == "radio"))
            {
                if (defChecked == "1") JsDom.SetAttribute(c, "checked", "");
                else c.Attrs.Remove("checked");
            }
        }
        if (dom != null) { dom.Dispatch(scope, "reset"); DrainQuiet(); }
        layoutDirty = true;
        MarkDirty();
    }

    private static string UrlEncode(string s)
    {
        if (s == null) return "";
        string outp = "";
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '*')
                outp += c;
            else if (c == ' ') outp += "+";
            else if (c == '\n') outp += "%0A";
            else if (c == '\r') { }
            else outp += "%" + Hex2((int)c);
        }
        return outp;
    }

    private static string Hex2(int v)
    {
        const string digits = "0123456789ABCDEF";
        char[] buf = new char[2];
        buf[0] = digits[(v >> 4) & 15];
        buf[1] = digits[v & 15];
        return new string(buf);
    }

    /// <summary>Show a plain error/blank page without touching the network.</summary>
    public void SetMessage(string title, string message)    {
        string html = "";
        if (!string.IsNullOrEmpty(title)) html += "<h2>" + title + "</h2>";
        if (!string.IsNullOrEmpty(message)) html += "<p>" + message + "</p>";
        SetDocument(HtmlPageBuilder.Parse(html), "");
    }

    public override void Resize(int width, int height)
    {
        base.Resize(width, height);
        RebuildLayout();
    }

    /// <summary>Pump the JS event loop: deliver network results, fire timers
    /// and hand new fetch/XHR requests to the host's networking thread.</summary>
    public override void Update()
    {
        base.Update();

        // caret blink: needs redraws while a control holds focus (works even
        // on static pages that have no script engine).
        if (focusWidget != null)
        {
            long now = DateTime.UtcNow.Ticks;
            if (now - lastBlinkTicks >= 5000000L)   // ~2 Hz
            {
                lastBlinkTicks = now;
                caretOn = !caretOn;
                MarkDirty();
            }
        }

        if (js == null || js.Loop == null) return;
        PumpJs();
    }

    private void PumpJs()
    {
        JsEventLoop loop = js.Loop;

        // 1. results completed by the fetch worker -> resolve promises / XHR
        if (fetchResultsReady)
        {
            fetchResultsReady = false;
            List<JsFetchRequest> reqs = fetchBatch;
            List<JsFetchResult> results = fetchResults;
            fetchBatch = null;
            fetchResults = null;
            fetchBusy = false;
            if (reqs != null && results != null)
            {
                for (int i = 0; i < reqs.Count && i < results.Count; i++)
                    loop.DeliverFetch(reqs[i], results[i]);
            }
        }

        // 2. fire due timers (each fires -> drain microtasks inside RunDue)
        loop.RunDue(DateTime.UtcNow.Ticks);
        js.DrainJobs();

        // 3. hand newly queued requests to the networking thread
        if (!fetchBusy && loop.HasPendingFetches)
        {
            if (HttpFetch == null)
            {
                loop.FailAllFetches("network unavailable");
            }
            else
            {
                List<JsFetchRequest> batch = new List<JsFetchRequest>();
                loop.TakeFetches(batch);
                if (batch.Count > 0)
                {
                    fetchBatch = batch;
                    fetchBusy = true;
                    HttpFetchDelegate handler = HttpFetch;
                    new Thread(delegate ()
                    {
                        List<JsFetchResult> results = new List<JsFetchResult>(batch.Count);
                        for (int i = 0; i < batch.Count; i++)
                        {
                            JsFetchRequest req = batch[i];
                            JsFetchResult r;
                            try { r = handler(req.Method, req.Url, req.Headers, req.Body); }
                            catch (Exception ex) { r = new JsFetchResult(); r.Error = ex.Message; r.Url = req.Url; }
                            if (r == null) { r = new JsFetchResult(); r.Error = "no response"; r.Url = req.Url; }
                            results.Add(r);
                        }
                        fetchResults = results;
                        fetchResultsReady = true;
                    }).Start();
                }
            }
        }

        // 4. scripts may have mutated the tree (timers, handlers, fetch)
        if (layoutDirty) { layoutDirty = false; RebuildLayout(); }
    }

    public void RebuildLayout()
    {
        layoutWidth = Width;
        if (js != null && js.Loop != null)
        {
            js.Loop.ViewportWidth = Width;
            js.Loop.ViewportHeight = Height;
            if (dom != null) dom.SetStylesheet(document != null ? document.CssText : "", Width, Height);
        }
        if (document == null) { plan = new RenderPlan(); return; }
        plan = HtmlLayout.Build(document.Root, document.CssText, Width, Height, fontSize,
            (s, sz) => MeasureTextWidth(s, sz), images, baseHref);
        scrollY = Math.Max(0, Math.Min(scrollY, MaxScroll()));
    }

    public void ZoomIn()
    {
        if (fontSize < 32)
        {
            fontSize += 2;
            RebuildLayout();
            MarkDirty();
        }
    }

    public void ZoomOut()
    {
        if (fontSize > 10)
        {
            fontSize -= 2;
            RebuildLayout();
            MarkDirty();
        }
    }

    public void ResetZoom()
    {
        if (fontSize != 16)
        {
            fontSize = 16;
            RebuildLayout();
            MarkDirty();
        }
    }

    public void EvaluateScript(string code)
    {
        if (js == null || string.IsNullOrEmpty(code)) return;
        try
        {
            JsProgram prog = JsParser.ParseProgram(code);
            js.RunProgram(prog);
            js.DrainJobs();
            if (layoutDirty)
            {
                layoutDirty = false;
                RebuildLayout();
            }
        }
        catch { }
    }

    /// <summary>Scroll to an element by its ID or name attribute.</summary>
    public bool ScrollToAnchor(string anchor)
    {
        if (string.IsNullOrEmpty(anchor) || document == null || document.Root == null) return false;
        string target = anchor.TrimStart('#');
        if (target.Length == 0)
        {
            scrollY = 0;
            CloseSelectPopup();
            MarkDirty();
            return true;
        }

        DomNode node = FindNodeByIdOrName(document.Root, target);
        if (node == null) return false;

        int targetY = -1;
        for (int i = 0; i < plan.Widgets.Count; i++)
        {
            if (plan.Widgets[i].Node == node) { targetY = plan.Widgets[i].Y; break; }
        }
        if (targetY < 0)
        {
            for (int i = 0; i < plan.Lines.Count; i++)
            {
                RenderLine l = plan.Lines[i];
                if (l.Node == node) { targetY = l.Y; break; }
                for (int j = 0; j < l.Spans.Count; j++)
                {
                    if (l.Spans[j].Node == node) { targetY = l.Y; break; }
                }
                if (targetY >= 0) break;
            }
        }
        if (targetY < 0)
        {
            for (int i = 0; i < plan.Boxes.Count; i++)
            {
                if (plan.Boxes[i].Node == node) { targetY = plan.Boxes[i].Y; break; }
            }
        }

        if (targetY >= 0)
        {
            scrollY = Math.Max(0, Math.Min(targetY - 8, MaxScroll()));
            CloseSelectPopup();
            MarkDirty();
            return true;
        }
        return false;
    }

    private static DomNode FindNodeByIdOrName(DomNode parent, string idOrName)
    {
        if (parent == null) return null;
        if (parent.IsElement)
        {
            if (string.Equals(parent.Id, idOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(parent.GetAttr("name"), idOrName, StringComparison.OrdinalIgnoreCase))
                return parent;
        }
        for (DomNode c = parent.FirstChild; c != null; c = c.Next)
        {
            DomNode found = FindNodeByIdOrName(c, idOrName);
            if (found != null) return found;
        }
        return null;
    }

    public override void DrawLocal()
    {
        if (layoutWidth != Width || layoutDirty)
        {
            layoutDirty = false;
            RebuildLayout();
        }

        DrawFilledRectangle(plan.PageBg, 0, 0, Width, Height);
        if (plan.PageBgImage != null)
        {
            DrawImageStretch(plan.PageBgImage, new Rectangle(0, 0, Width, Height));
        }

        int viewTop = 2;
        int viewHeight = Height - 4;
        int viewportWidth = Math.Max(1, Width - scrollbarSize);

        // Block backgrounds/borders painted behind the text.
        for (int i = 0; i < plan.Boxes.Count; i++)
        {
            RenderBox box = plan.Boxes[i];
            if (box.Hidden) continue;
            int by = box.Y - scrollY + viewTop;
            if (by + box.H < viewTop || by > viewTop + viewHeight) continue;
            DrawBox(box, by, viewportWidth);
        }

        // Clip manually by culling lines outside the viewport.
        for (int i = 0; i < plan.Lines.Count; i++)
        {
            RenderLine line = plan.Lines[i];
            int ly = line.Y - scrollY + viewTop;
            if (ly + line.Height < viewTop || ly > viewTop + viewHeight) continue;
            DrawLineContent(line, ly, viewportWidth);
        }

        // Form controls painted above the text runs.
        for (int i = 0; i < plan.Widgets.Count; i++)
        {
            RenderWidget w = plan.Widgets[i];
            if (w.Hidden) continue;
            int wy = w.Y - scrollY + viewTop;
            if (wy + w.H < viewTop || wy > viewTop + viewHeight) continue;
            DrawWidget(w, wy);
        }
        if (openSelect != null) DrawSelectPopup(viewTop, viewHeight);

        DrawScrollbar();
    }

    // ---- widget painting (stage 3) ---------------------------------------

    private void DrawWidget(RenderWidget w, int wy)
    {
        bool focused = focusWidget == w.Node;
        Color face = w.Disabled ? Palette.ControlFace : Palette.ControlWhite;

        switch (w.Kind)
        {
            case WidgetKind.Checkbox:
            case WidgetKind.Radio:
            {
                DrawSunkenRectangle(w.X, wy, w.W, w.H);
                DrawFilledRectangle(face, w.X + 1, wy + 1, w.W - 2, w.H - 2);
                if (w.Checked)
                {
                    if (w.Kind == WidgetKind.Checkbox)
                    {
                        // two strokes forming a tick
                        DrawLine(Palette.ControlBlack, w.X + 2, wy + w.H / 2, w.X + w.W / 2 - 1, wy + w.H - 4);
                        DrawLine(Palette.ControlBlack, w.X + w.W / 2 - 1, wy + w.H - 4, w.X + w.W - 3, wy + 2);
                    }
                    else
                    {
                        int r = w.W / 2 - 3;
                        DrawFilledRectangle(Palette.ControlBlack,
                            w.X + w.W / 2 - r / 2, wy + w.H / 2 - r / 2, r, r);
                    }
                }
                if (focused && caretOn)
                    DrawFocusRing(w.X, wy, w.W, w.H);
                break;
            }

            case WidgetKind.Button:
            {
                DrawRaisedRectangle(w.X, wy, w.W, w.H);
                DrawFilledRectangle(Palette.ControlFace, w.X + 1, wy + 1, w.W - 2, w.H - 2);
                string label = w.Text != null ? w.Text : "";
                int tw = MeasureTextWidth(label, w.FontSize);
                DrawString(label, w.Disabled ? Palette.ControlShadow : Palette.ControlBlack,
                    w.X + (w.W - tw) / 2, wy + (w.H - w.FontSize) / 2 + 1, w.FontSize);
                if (focused && caretOn) DrawFocusRing(w.X, wy, w.W, w.H);
                break;
            }

            case WidgetKind.Select:
            {
                DrawSunkenRectangle(w.X, wy, w.W, w.H);
                DrawFilledRectangle(face, w.X + 1, wy + 1, w.W - 2, w.H - 2);
                // label (clipped) + the drop arrow on the right
                string label = ClipToWidth(w.Text, w.FontSize, w.W - 24);
                DrawString(label, Palette.ControlBlack, w.TextX, wy + (w.H - w.FontSize) / 2 + 1, w.FontSize);
                int ax = w.X + w.W - 14, ay = wy + w.H / 2 - 1;
                DrawLine(Palette.ControlBlack, ax, ay, ax + 8, ay);
                DrawLine(Palette.ControlBlack, ax, ay + 1, ax + 8, ay + 1);
                DrawLine(Palette.ControlBlack, ax + 1, ay + 2, ax + 7, ay + 2);
                DrawLine(Palette.ControlBlack, ax + 2, ay + 3, ax + 6, ay + 3);
                DrawLine(Palette.ControlBlack, ax + 3, ay + 4, ax + 5, ay + 4);
                DrawFilledRectangle(Palette.ControlBlack, ax + 4, ay + 5, 1, 1);
                if (focused && caretOn) DrawFocusRing(w.X, wy, w.W, w.H);
                break;
            }

            case WidgetKind.Textarea:
            {
                DrawSunkenRectangle(w.X, wy, w.W, w.H);
                DrawFilledRectangle(face, w.X + 1, wy + 1, w.W - 2, w.H - 2);
                DrawMultilineValue(w, wy, focused);
                break;
            }

            default: // text / password
            {
                DrawSunkenRectangle(w.X, wy, w.W, w.H);
                DrawFilledRectangle(face, w.X + 1, wy + 1, w.W - 2, w.H - 2);
                DrawInputValue(w, wy, focused);
                break;
            }
        }
    }

    /// <summary>The value text with the caret scrolled into view.</summary>
    private void DrawInputValue(RenderWidget w, int wy, bool focused)
    {
        string value = WidgetValueOf(w.Node);
        if (w.Kind == WidgetKind.Password) value = Mask(value);

        int size = w.FontSize;
        int tx = w.X + 5;
        int ty = wy + (w.H - size) / 2 + 1;
        int innerW = w.W - 10;
        if (innerW < 4) return;

        // scroll horizontally so the caret stays visible
        int caretIdx = focused ? Math.Max(0, Math.Min(caret, value.Length)) : value.Length;
        int caretPx = MeasureTextWidth(value.Substring(0, caretIdx), size);
        int start = 0;
        while (start < value.Length && MeasureTextWidth(value.Substring(start), size) > innerW - 2)
            start++;

        string shown = value.Substring(start);
        DrawString(shown, Palette.ControlBlack, tx, ty, size);

        if (focused && caretOn)
        {
            int cx = tx + caretPx - MeasureTextWidth(value.Substring(0, start), size);
            if (cx >= tx - 1 && cx <= tx + innerW)
                DrawLine(Palette.ControlBlack, cx, ty - 1, cx, ty + size);
        }
    }

    private void DrawMultilineValue(RenderWidget w, int wy, bool focused)
    {
        string value = WidgetValueOf(w.Node);
        int size = w.FontSize;
        int step = size + 3;
        int tx = w.X + 5;
        int top = wy + 4;
        int caretIdx = Math.Max(0, Math.Min(caret, value.Length));

        int row = 0, col = 0;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || value[i] == '\n')
            {
                string lineText = value.Substring(i - col, col);
                DrawString(ClipToWidth(lineText, size, w.W - 12), Palette.ControlBlack,
                    tx, top + row * step, size);
                row++;
                col = 0;
                if (i == value.Length) break;
            }
            else if (value[i] != '\r') col++;
        }

        if (focused && caretOn)
        {
            // walk to the caret's row/column
            int r = 0, c = 0;
            for (int i = 0; i < caretIdx; i++)
            {
                if (value[i] == '\n') { r++; c = 0; }
                else if (value[i] != '\r') c++;
            }
            string prefix = value.Substring(caretIdx - c, c);
            int cx = tx + MeasureTextWidth(prefix, size);
            int cy = top + r * step;
            if (cy < top + (w.H - 8)) DrawLine(Palette.ControlBlack, cx, cy, cx, cy + size);
        }
    }

    private void DrawFocusRing(int x, int y, int w, int h)
    {
        // dashed-ish focus indication: corner ticks
        Color c = Palette.Highlight;
        DrawFilledRectangle(c, x - 1, y - 1, 4, 1);
        DrawFilledRectangle(c, x + w - 3, y - 1, 4, 1);
        DrawFilledRectangle(c, x - 1, y + h, 4, 1);
        DrawFilledRectangle(c, x + w - 3, y + h, 4, 1);
        DrawFilledRectangle(c, x - 1, y - 1, 1, 4);
        DrawFilledRectangle(c, x + w, y - 1, 1, 4);
        DrawFilledRectangle(c, x - 1, y + h - 3, 1, 4);
        DrawFilledRectangle(c, x + w, y + h - 3, 1, 4);
    }

    private void DrawSelectPopup(int viewTop, int viewHeight)
    {
        DomNode select = openSelect;
        List<DomNode> opts = OptionNodes(select);
        if (opts.Count == 0) { openSelect = null; return; }

        RenderWidget host = FindWidgetFor(select);
        if (host == null) { openSelect = null; return; }

        int py = popTop - scrollY + viewTop;
        if (py + popH > viewTop + viewHeight && popTop - popH >= 0)
            py = popTop - popH - scrollY + viewTop;

        // keep the popup inside the visible area vertically
        if (py < viewTop) py = viewTop;
        if (py + popH > viewTop + viewHeight) py = Math.Max(viewTop, viewTop + viewHeight - popH);

        DrawSunkenRectangle(popX, py, popW, popH);
        DrawFilledRectangle(Palette.ControlWhite, popX + 1, py + 1, popW - 2, popH - 2);
        for (int i = 0; i < opts.Count; i++)
        {
            int ry = py + 1 + i * popRow;
            if (i == selectHighlight)
                DrawFilledRectangle(Palette.Highlight, popX + 1, ry, popW - 2, popRow - 1);
            string label = HtmlLayout.OptionLabel(opts[i]);
            DrawString(ClipToWidth(label, host.FontSize, popW - 10),
                i == selectHighlight ? Palette.HighlightText : Palette.ControlBlack,
                popX + 5, ry + (popRow - 1 - host.FontSize) / 2 + 1, host.FontSize);
        }
    }

    /// <summary>Trim a string so it measures no wider than maxW.</summary>
    private string ClipToWidth(string s, int fontSize, int maxW)
    {
        if (s == null || maxW <= 0) return "";
        if (MeasureTextWidth(s, fontSize) <= maxW) return s;
        int end = s.Length;
        while (end > 0 && MeasureTextWidth(s.Substring(0, end), fontSize) > maxW) end--;
        return s.Substring(0, end);
    }

    private void DrawBox(RenderBox box, int by, int viewportWidth)
    {
        int r = box.BorderRadius > 0 ? Math.Min(box.BorderRadius, Math.Min(box.W, box.H) / 2) : 0;
        if (box.HasBg)
        {
            if (r > 0) DrawFilledRoundedRectangle(box.Bg, box.X, by, box.W, box.H, r);
            else DrawFilledRectangle(box.Bg, box.X, by, box.W, box.H);
        }

        if (box.BgImage != null && box.W > 0 && box.H > 0)
        {
            DrawImageStretch(box.BgImage, new Rectangle(box.X, by, box.W, box.H));
        }

        if (r > 0 && box.BorderT > 0)
        {
            DrawRoundedRectangle(box.BorderCT, box.X, by, box.W, box.H, r);
        }
        else
        {
            if (box.BorderT > 0) DrawBorderSide(box.StyleT, box.BorderCT, box.X, by, box.W, box.BorderT, 0);
            if (box.BorderB > 0) DrawBorderSide(box.StyleB, box.BorderCB, box.X, by + box.H - box.BorderB, box.W, box.BorderB, 2);
            if (box.BorderL > 0) DrawBorderSide(box.StyleL, box.BorderCL, box.X, by, box.BorderL, box.H, 3);
            if (box.BorderR > 0) DrawBorderSide(box.StyleR, box.BorderCR, box.X + box.W - box.BorderR, by, box.BorderR, box.H, 1);
        }
    }

    // side: 0=top,1=right,2=bottom,3=left
    private void DrawBorderSide(CssBorder style, Color color, int x, int y, int w, int h, int side)
    {
        if (w <= 0 || h <= 0) return;
        switch (style)
        {
            case CssBorder.None:
                return;
            case CssBorder.Dotted:
                DrawDotted(color, x, y, w, h, side);
                return;
            case CssBorder.Dashed:
                DrawDashed(color, x, y, w, h, side);
                return;
            case CssBorder.Double:
                // approximate as a thicker solid line
                DrawFilledRectangle(color, x, y, w, h);
                return;
            default:
                DrawFilledRectangle(color, x, y, w, h);
                return;
        }
    }

    private void DrawDotted(Color c, int x, int y, int w, int h, int side)
    {
        if (side == 0 || side == 2)
        {
            for (int i = 0; i < w; i += 3) DrawFilledRectangle(c, x + i, y, 1, h);
        }
        else
        {
            for (int i = 0; i < h; i += 3) DrawFilledRectangle(c, x, y + i, w, 1);
        }
    }

    private void DrawDashed(Color c, int x, int y, int w, int h, int side)
    {
        if (side == 0 || side == 2)
        {
            for (int i = 0; i < w; i += 6) DrawFilledRectangle(c, x + i, y, Math.Min(4, w - i), h);
        }
        else
        {
            for (int i = 0; i < h; i += 6) DrawFilledRectangle(c, x, y + i, w, Math.Min(4, h - i));
        }
    }

    private void DrawLineContent(RenderLine line, int ly, int viewportWidth)
    {
        if (line.Hidden) return;

        if (line.IsRule)
        {
            DrawFilledRectangle(line.RuleColor, Math.Max(2, line.TextX), ly + line.Height / 2, viewportWidth - 4, Math.Max(1, line.Height));
            return;
        }

        if (line.IsImage)
        {
            if (line.Image != null)
            {
                DrawImageStretch(line.Image, new Rectangle(line.TextX, ly, line.ImageW, line.ImageH));
            }
            else
            {
                DrawSunkenRectangle(line.TextX, ly, line.ImageW, line.ImageH);
                DrawFilledRectangle(Color.FromArgb(235, 238, 242), line.TextX + 1, ly + 1, line.ImageW - 2, line.ImageH - 2);
                if (!string.IsNullOrEmpty(line.ImageAlt))
                {
                    int tw = MeasureTextWidth(line.ImageAlt, 14);
                    DrawString(line.ImageAlt, Color.FromArgb(90, 90, 100), line.TextX + (line.ImageW - tw) / 2, ly + line.ImageH / 2 - 8, 14);
                }
            }
            return;
        }

        if (line.Bullet != null)
            DrawString(line.Bullet, Palette.ControlBlack, line.BulletX + 2, ly + 2, fontSize);

        for (int i = 0; i < line.Spans.Count; i++)
        {
            RenderSpan s = line.Spans[i];
            if (s.Hidden || string.IsNullOrEmpty(s.Text)) continue;

            int size = s.Size > 0 ? s.Size : fontSize;
            int ty = ly + 2 + s.YOff;
            if (s.HasBg)
            {
                int bx = s.X;
                int by = ty - s.PadT;
                int bw = s.Width;
                int bh = size + 2 + s.PadT + s.PadB;
                DrawFilledRectangle(s.Bg, bx, by, bw, bh);
                if (s.BorderRadius > 0)
                {
                    DrawRectangle(Color.FromArgb(s.Bg.A, Math.Max(0, s.Bg.R - 25), Math.Max(0, s.Bg.G - 25), Math.Max(0, s.Bg.B - 25)), bx, by, bw, bh);
                }
            }

            DrawString(s.Text, s.Color, s.X + s.PadL, ty, size);
            if (s.Bold) DrawString(s.Text, s.Color, s.X + s.PadL + 1, ty, size); // faux bold
            if (s.Underline) DrawLine(s.Color, s.X + s.PadL, ty + size, s.X + s.Width - s.PadR, ty + size);
            if (s.Overline) DrawLine(s.Color, s.X + s.PadL, ty, s.X + s.Width - s.PadR, ty);
            if (s.LineThrough) DrawLine(s.Color, s.X + s.PadL, ty + size / 2, s.X + s.Width - s.PadR, ty + size / 2);
        }
    }

    private void DrawScrollbar()
    {
        int x = Width - scrollbarSize;
        if (MaxScroll() <= 0)
        {
            DrawFilledRectangle(Palette.ControlFace, x, 0, scrollbarSize, Height);
            return;
        }

        DrawFilledRectangle(Palette.ControlFace, x, 0, scrollbarSize, Height);
        DrawRaisedRectangle(x, 0, scrollbarSize, scrollbarSize);
        DrawRaisedRectangle(x, Height - scrollbarSize, scrollbarSize, scrollbarSize);
        DrawString("^", Palette.ControlBlack, x + 4, 1, 14);
        DrawString("v", Palette.ControlBlack, x + 4, Height - scrollbarSize + 1, 14);

        Rectangle thumb = GetThumb();
        DrawRaisedRectangle(thumb.X, thumb.Y, thumb.Width, thumb.Height);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY))
        {
            if (lastHoveredLink != null)
            {
                lastHoveredLink = null;
                if (LinkHovered != null) LinkHovered(null);
            }
            return false;
        }

        int localX = mouseX - AbsoluteX;
        int localY = mouseY - AbsoluteY;
        int contentX = localX;
        int contentY = localY - 2 + scrollY;

        string hovered = plan.HitTestLink(contentX, contentY);
        if (hovered != lastHoveredLink)
        {
            lastHoveredLink = hovered;
            if (LinkHovered != null) LinkHovered(hovered);
        }

        if (Mouse.scroll != 0)
        {
            ScrollBy(-(int)Mouse.scroll * 48);
            return true;
        }

        if (mouse.left == MouseEvents.Press)
        {
            if (localX >= Width - scrollbarSize)
            {
                if (localY < scrollbarSize) ScrollBy(-48);
                else if (localY >= Height - scrollbarSize) ScrollBy(48);
                else if (localY < GetThumb().Y) ScrollBy(-ViewportHeight());
                else if (localY > GetThumb().Bottom) ScrollBy(ViewportHeight());
                return true;
            }

            // A script listener may mutate the tree; relayout before hit-testing
            // so the plan matches what's on screen.
            if (layoutDirty) { layoutDirty = false; RebuildLayout(); }

            // 1. an open <select> dropdown swallows clicks inside its popup
            if (openSelect != null && HitSelectPopup(contentX, contentY))
                return true;

            // 2. the DOM click event (widget nodes are hit-testable, so a
            //    script listener on the control sees the click first)
            bool defaultPrevented = DispatchClickAt(contentX, contentY);

            // 3. form-control default actions, unless a script prevented them
            RenderWidget widget = plan.HitTestWidget(contentX, contentY);
            if (widget != null && !defaultPrevented)
            {
                if (widget.Disabled) { CloseSelectPopup(); return true; }
                HandleWidgetClick(widget, contentX, contentY);
                return true;
            }

            // 4. any other press closes the dropdown and blurs the control
            CloseSelectPopup();
            if (focusWidget != null) SetFocusWidget(null);

            if (widget != null) return true;   // disabled: swallow

            // link hit-test (content coords) — skipped if a script prevented default
            if (!defaultPrevented)
            {
                string href = plan.HitTestLink(contentX, contentY);
                if (href != null && LinkActivated != null)
                {
                    LinkActivated(href);
                    return true;
                }
            }
        }

        return true;
    }

    // ---- keyboard: typing into form controls (stage 3) --------------------

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        // an open dropdown owns the keyboard until it closes
        if (openSelect != null)
        {
            switch (keyEvent.Key)
            {
                case Key.UpArrow:
                    if (selectHighlight > 0) { selectHighlight--; MarkDirty(); }
                    return;
                case Key.DownArrow:
                    if (selectHighlight < popCount - 1) { selectHighlight++; MarkDirty(); }
                    return;
                case Key.Home: selectHighlight = 0; MarkDirty(); return;
                case Key.End: selectHighlight = popCount - 1; MarkDirty(); return;
                case Key.Enter: PickSelectOption(openSelect, selectHighlight); return;
                case Key.Escape: CloseSelectPopup(); return;
            }
            return;
        }

        if (focusWidget == null || !focusWidget.IsElement) return;
        byte kind = WidgetKindOfNode(focusWidget);

        if (kind == WidgetKind.Checkbox || kind == WidgetKind.Radio)
        {
            // space toggles a focused checkbox/radio
            if (GetPrintableCharacter(keyEvent) == ' ')
            {
                if (kind == WidgetKind.Checkbox) ToggleCheckbox(focusWidget);
                else CheckRadio(focusWidget);
            }
            else if (keyEvent.Key == Key.Escape) SetFocusWidget(null);
            return;
        }

        if (kind == WidgetKind.Button)
        {
            if (keyEvent.Key == Key.Enter || GetPrintableCharacter(keyEvent) == ' ')
            {
                string type = focusWidget.Tag == "input"
                    ? InputTypeOf(focusWidget)
                    : (focusWidget.GetAttr("type") ?? "submit");
                type = type.ToLowerInvariant();
                if (type == "reset") ResetForm(AncestorForm(focusWidget));
                else SubmitFromControl(focusWidget);
            }
            else if (keyEvent.Key == Key.Escape) SetFocusWidget(null);
            return;
        }

        if (kind == WidgetKind.Select)
        {
            switch (keyEvent.Key)
            {
                case Key.UpArrow: MoveSelect(focusWidget, -1); return;
                case Key.DownArrow: MoveSelect(focusWidget, 1); return;
                case Key.Escape: SetFocusWidget(null); return;
                case Key.Enter: OpenSelectPopup(focusWidget); return;
            }
            return;
        }

        // text-like controls: text/password/textarea
        string value = WidgetValueOf(focusWidget);
        int ci = Math.Max(0, Math.Min(caret, value.Length));
        bool multiline = kind == WidgetKind.Textarea;

        if (dom != null)
        {
            dom.Dispatch(focusWidget, "keydown");
            DrainQuiet();
        }

        switch (keyEvent.Key)
        {
            case Key.LeftArrow:
                caret = ci - 1 < 0 ? 0 : ci - 1;
                caretOn = true; MarkDirty(); return;
            case Key.RightArrow:
                caret = ci + 1 > value.Length ? value.Length : ci + 1;
                caretOn = true; MarkDirty(); return;
            case Key.Home:
                if (multiline)
                {
                    int nl2 = value.LastIndexOf('\n', Math.Max(0, ci - 1));
                    caret = nl2 < 0 ? 0 : nl2 + 1;
                }
                else caret = 0;
                caretOn = true; MarkDirty(); return;
            case Key.End:
                if (multiline)
                {
                    int nx = value.IndexOf('\n', ci);
                    caret = nx < 0 ? value.Length : nx;
                }
                else caret = value.Length;
                caretOn = true; MarkDirty(); return;
            case Key.Backspace:
                if (ci > 0)
                {
                    SetWidgetValue(focusWidget, value.Substring(0, ci - 1) + value.Substring(ci));
                    caret = ci - 1;
                    caretOn = true;
                    AfterWidgetInput();
                }
                return;
            case Key.Delete:
                if (ci < value.Length)
                {
                    SetWidgetValue(focusWidget, value.Substring(0, ci) + value.Substring(ci + 1));
                    caretOn = true;
                    AfterWidgetInput();
                }
                return;
            case Key.Enter:
                if (multiline)
                {
                    SetWidgetValue(focusWidget, value.Substring(0, ci) + "\n" + value.Substring(ci));
                    caret = ci + 1;
                    caretOn = true;
                    AfterWidgetInput();
                }
                else
                {
                    // Enter in a single-line field submits the form (browser behaviour)
                    SubmitFromControl(focusWidget);
                }
                return;
            case Key.Escape:
                SetFocusWidget(null);
                return;
        }

        char printable = GetPrintableCharacter(keyEvent);
        if (printable != '\0')
        {
            SetWidgetValue(focusWidget, value.Substring(0, ci) + printable + value.Substring(ci));
            caret = ci + 1;
            caretOn = true;
            AfterWidgetInput();
            if (dom != null) { dom.Dispatch(focusWidget, "keyup"); DrainQuiet(); }
        }
    }

    private void ScrollBy(int amount)
    {
        int next = Math.Max(0, Math.Min(MaxScroll(), scrollY + amount));
        if (next == scrollY) return;
        scrollY = next;
        CloseSelectPopup();   // the popup's anchor scrolled away
        MarkDirty();
    }

    private Rectangle GetThumb()
    {
        int trackTop = scrollbarSize;
        int trackHeight = Math.Max(1, Height - scrollbarSize * 2);
        int totalHeight = Math.Max(ViewportHeight(), plan.ContentHeight);
        int thumbHeight = Math.Max(16, trackHeight * ViewportHeight() / Math.Max(1, totalHeight));
        int travel = Math.Max(0, trackHeight - thumbHeight);
        int thumbY = trackTop + (MaxScroll() == 0 ? 0 : scrollY * travel / MaxScroll());
        return new Rectangle(Width - scrollbarSize, thumbY, scrollbarSize, thumbHeight);
    }

    private int ViewportHeight() => Math.Max(1, Height - 4);
    private int MaxScroll() => Math.Max(0, plan.ContentHeight - ViewportHeight());

    public override string GetComponentName() => "WebView";
}
