using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using HttpClient = Cosmos.Network.Http.HttpClient;
using HttpResponseMessage = Cosmos.Network.Http.HttpResponseMessage;
using HttpContent = Cosmos.Network.Http.HttpContent;
using StringContent = Cosmos.Network.Http.StringContent;
using HttpRequestHeaders = Cosmos.Network.Http.Headers.HttpRequestHeaders;
using HttpWebRequest = Cosmos.Network.Http.HttpWebRequest;
using SslVerification = Cosmos.Network.Http.SslVerification;

namespace Windose.Programs
{
    internal class InternetExplorer : Window
    {
        private const string HomeUrl = "about:home";

        private const string HomeHtml = @"<html>
<head><title>Windose Home</title></head>
<body>
<h1>Welcome to Windose</h1>
<p>This page is built in and works with <b>no network</b>. Type an address above and press <b>Enter</b> to browse.</p>
<h2>What works</h2>
<ul>
<li>Static HTML pages (headings, paragraphs, lists)</li>
<li><b>Bold</b>, <i>italic</i> and <code>inline code</code></li>
<li>Clickable <a href='http://example.com'>links</a></li>
<li>Preformatted blocks and horizontal rules</li>
</ul>
<hr>
<pre>
// A preformatted code sample
fn main() {
    println(""Hello from Windose"");
}
</pre>
<p>Modern JavaScript-heavy sites will not render, but plain HTML does.</p>
</body>
</html>";

        private DockPanel root;
        private Toolbar toolbar;
        private AddressBar addressBar;
        private StatusBar statusBar;
        private ProgressBar progressBar;
        private DockPanel explorerBody;
        private WebView webView;

        private readonly List<string> backStack = new List<string>();
        private readonly List<string> forwardStack = new List<string>();
        private string currentUrl = "";

        // One cookie jar shared by the page pipeline and the script fetch/XHR
        // pipeline (stage 4). Workers lock it internally.
        private readonly CookieJar cookies = new CookieJar();

        // ---- script -> shell hooks (used by the WebView's networking thread) ----

        private string JsGetCookies(string url) { return cookies.DocumentCookieFor(url); }
        private void JsSetCookie(string cookie, string url) { cookies.AddFromSetCookie(cookie, url); }

        // Worker -> UI handoff (the worker only fills these and flips resultReady;
        // the update thread consumes them so all GUI mutation stays on one thread).
        private HtmlPage pendingDoc;
        private string pendingUrl = "";
        private string pendingTitle = "";
        private string pendingStatus = "";
        private Dictionary<string, Image> pendingImages;
        private bool resultReady;

        // Progress handoff: the fetch worker writes these (0..100, or -1 for the
        // indeterminate "connecting" marquee) and the update thread reads them so
        // all GUI mutation stays on the UI thread.
        private int pendingProgress = -1;
        private string pendingProgressText = "";
        private bool progressReady;

        static InternetExplorer()
        {
            // The Cosmos HTTP stack caps response headers at 4 KB by default, which many
            // modern sites exceed (large Set-Cookie / tracking headers) -> "headers size
            // exceeded limits". HttpClient builds an HttpWebRequest per request that reads
            // this static default, so raise it globally.
            HttpWebRequest.DefaultMaximumResponseHeadersLength = 64; // KB
        }

        public InternetExplorer(int x, int y) : base(x, y, 800, 600, "Internet Explorer", true, null)
        {
            root = new DockPanel(0, 0, Width, Height)
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(28, 2, 2, 2),
                Padding = new Thickness(0),
                useBackground = true,
            };

            webView = new WebView(0, 0, Width, Height);

            toolbar = new Toolbar(0, 0, Width, 28);

            toolbar.AddButton(new Png("/mnt/System/Icons/msg_error.png"), "Back", GoBack, 100);
            toolbar.AddButton(new Png("/mnt/System/Icons/msg_error.png"), "Forward", GoForward, 120);
            toolbar.AddButton(new Png("/mnt/System/Icons/msg_error.png"), "Refresh", Refresh, 120);
            toolbar.AddButton(new Png("/mnt/System/Icons/homepage_alt.png"), "Home", Home, 100);

            addressBar = new AddressBar(0, 0, Width);
            statusBar = new StatusBar(0, 0, Width);
            progressBar = new ProgressBar(0, 0, Width, 16)
            {
                clampSize = false,
                useBorders = false,
                trackColor = Palette.ControlWhite,
                barColor = Palette.Highlight,
                AnimationSpeed = 140f,  // ease the fill so it looks like real progress
                Indeterminate = true,   // marquee until we know real progress
                Visible = false,        // shown only while a page is loading
                Margin = new Thickness(0),
                Padding = new Thickness(0),
            };
            explorerBody = new DockPanel(0, 0, Width, Height)
            {
                clampSize = false,
                useBackground = true,
                backgroundColor = Palette.ControlWhite,
                Padding = new Thickness(0),
            };

            root.AddDockChild(toolbar, Dock.Top);
            root.AddDockChild(addressBar, Dock.Top);
            root.AddDockChild(statusBar, Dock.Bottom);
            root.AddDockChild(progressBar, Dock.Bottom);
            root.AddDockChild(explorerBody, Dock.Fill);

            explorerBody.AddDockChild(webView, Dock.Fill);

            statusBar.AddPanel("Ready");

            AddChild(root);

            // ---- wiring ----
            addressBar.addressField.EnterPressed += () => Navigate(addressBar.Address);
            webView.LinkActivated += href => Navigate(WebUrl.Resolve(currentUrl, href));

            // JS engine -> shell: script-driven navigation, reload, history, HTTP.
            webView.NavigateRequested += url => Navigate(WebUrl.Resolve(currentUrl, url));
            webView.ReloadRequested += Refresh;
            webView.HistoryBackRequested += GoBack;
            webView.HistoryForwardRequested += GoForward;
            webView.HttpFetch = HttpFetchJs;
            webView.GetCookies = JsGetCookies;
            webView.SetCookie = JsSetCookie;

            Navigate(HomeUrl);
        }

        public override void Update()
        {
            base.Update();

            if (progressReady)
            {
                progressReady = false;
                int pct = pendingProgress;
                string ptext = pendingProgressText;
                pendingProgress = -1;

                if (pct < 0)
                {
                    progressBar.Indeterminate = true;
                }
                else
                {
                    progressBar.Indeterminate = false;
                    progressBar.Value = pct;
                }
                progressBar.Visible = true;
                if (!string.IsNullOrEmpty(ptext)) SetStatus(ptext);
            }

            if (resultReady)
            {
                resultReady = false;

                HtmlPage doc = pendingDoc;
                string url = pendingUrl;
                string title = pendingTitle;
                string status = pendingStatus;
                Dictionary<string, Image> imgs = pendingImages;
                pendingDoc = null;
                pendingImages = null;

                progressBar.Visible = false;
                progressBar.Indeterminate = true;

                if (doc != null)
                {
                    webView.SetDocument(doc, url, imgs);
                    if (!string.IsNullOrEmpty(title))
                        SetTitle(title + " - Internet Explorer");
                }
                if (!string.IsNullOrEmpty(status)) SetStatus(status);
            }
        }

        private void SetStatus(string text)
        {
            statusBar.SetPanelText(0, text ?? "");
        }

        // ---- navigation ----

        private void Navigate(string rawUrl)
        {
            string url = WebUrl.Normalize(rawUrl);
            if (url == null)
            {
                SetStatus("Invalid address");
                return;
            }

            if (!string.IsNullOrEmpty(currentUrl))
            {
                backStack.Add(currentUrl);
                if (backStack.Count > 50) backStack.RemoveAt(0);
            }
            forwardStack.Clear();

            currentUrl = url;
            addressBar.Address = url;
            BeginFetch(url);
        }

        private void GoBack()
        {
            if (backStack.Count == 0) return;
            forwardStack.Add(currentUrl);
            string url = backStack[backStack.Count - 1];
            backStack.RemoveAt(backStack.Count - 1);
            currentUrl = url;
            addressBar.Address = url;
            BeginFetch(url);
        }

        private void GoForward()
        {
            if (forwardStack.Count == 0) return;
            backStack.Add(currentUrl);
            string url = forwardStack[forwardStack.Count - 1];
            forwardStack.RemoveAt(forwardStack.Count - 1);
            currentUrl = url;
            addressBar.Address = url;
            BeginFetch(url);
        }

        private void Refresh()
        {
            if (!string.IsNullOrEmpty(currentUrl)) BeginFetch(currentUrl);
        }

        private void Home()
        {
            Navigate(HomeUrl);
        }

        private void BeginFetch(string url)
        {
            string host = WebUrl.HostOf(url);
            SetStatus("Connecting to " + (host.Length > 0 ? host : url) + "...");

            string lower = url.ToLowerInvariant();

            if (lower.StartsWith("about:"))
            {
                ApplyOffline(url);
                return;
            }

            string scheme = WebUrl.SchemeOf(url);
            if (scheme != "http" && scheme != "https")
            {
                PublishError(url, "Unsupported protocol",
                    "This browser can only open http, https and about: addresses.");
                return;
            }

            // Show the loading bar right away (UI thread); the worker refines it.
            progressBar.Visible = true;
            progressBar.Indeterminate = true;
            progressBar.Value = 0;

            string target = url;
            new Thread(() => Fetch(target)).Start();
        }

        private void ApplyOffline(string url)
        {
            string lower = url.ToLowerInvariant();
            if (lower == "about:home")
            {
                HtmlPage page = HtmlPageBuilder.Parse(HomeHtml);
                Publish(page, url, page.Title, "Done");
            }
            else if (lower == "about:blank")
            {
                Publish(new HtmlPage(), url, "Blank page", "Done");
            }
            else
            {
                PublishError(url, "Unknown page", "No built-in page named " + url + ".");
            }
        }

        private void Fetch(string url)
        {
            try
            {
                string html = FetchString(url);
                int chars = html != null ? html.Length : 0;
                PublishProgress(25, "Received " + chars + " chars, parsing…");

                HtmlPage page = HtmlPageBuilder.Parse(html ?? "");
                if (string.IsNullOrEmpty(page.Title)) page.Title = WebUrl.HostOf(url);
                PublishProgress(45, "Loading stylesheets…");

                FetchStylesheets(page, url);
                PublishProgress(65, "Loading scripts…");

                FetchScripts(page, url);
                PublishProgress(80, "Loading images…");

                Dictionary<string, Image> images = FetchImages(page, url);
                PublishProgress(95, "Rendering…");

                string status = "Done — " + chars + " chars";
                if (images.Count > 0) status += ", " + images.Count + " img";
                Publish(page, url, page.Title, status, images);
            }
            catch (Exception ex)
            {
                string host = WebUrl.HostOf(url);
                string reason = DeepestMessage(ex);
                string name = host.Length > 0 ? host : url;
                string html = "<h2>Cannot display " + EscapeHtml(name) + "</h2>"
                    + "<p>" + EscapeHtml(reason) + "</p>";
                if (url.StartsWith("https://"))
                    html += "<p>Note: this site uses HTTPS. If the native TLS stack is unavailable the handshake will fail.</p>";
                HtmlPage page = HtmlPageBuilder.Parse(html);
                page.Title = "Cannot display page";
                Publish(page, url, "Cannot display page", "Error");
            }
        }

        private HttpClient NewClient()
        {
            HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            // A hobby OS ships no root-CA store, so skip certificate validation for HTTPS.
            client.SslVerification = SslVerification.NoVerification;
            // Browser identity (stage 4): sites gate content on UA + Accept.
            try
            {
                client.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windose; x86) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
                client.DefaultRequestHeaders.Add("Accept",
                    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            }
            catch { /* header rejection must not stop the request */ }
            return client;
        }

        // Follow 3xx redirects manually (Get() returns them rather than throwing, and
        // many real sites issue them). Returns the final 2xx response.
        private HttpResponseMessage GetFollowingRedirects(string url, out string finalUrl)
        {
            string current = url;
            for (int hop = 0; hop < 6; hop++)
            {
                HttpClient client = NewClient();
                AddCookieHeader(client, current);
                HttpResponseMessage response = client.Get(current);
                if (response == null) throw new Exception("No response from " + WebUrl.HostOf(current));
                StoreCookies(response, current);

                int code = (int)response.StatusCode;
                if (code >= 300 && code < 400)
                {
                    string location = ReadHeader(response, "Location");
                    if (string.IsNullOrEmpty(location))
                        throw new Exception("HTTP " + code + " redirect with no Location header.");
                    current = WebUrl.Resolve(current, location);
                    continue;
                }

                if (code < 200 || code >= 300)
                    throw new Exception("HTTP " + code + " from " + WebUrl.HostOf(current));

                finalUrl = current;
                return response;
            }
            throw new Exception("Too many redirects.");
        }

        private string FetchString(string url)
        {
            string final;
            HttpResponseMessage response = GetFollowingRedirects(url, out final);
            return response.Content != null ? response.Content.ReadAsString() : null;
        }

        private byte[] FetchBytes(string url)
        {
            string final;
            HttpResponseMessage response = GetFollowingRedirects(url, out final);
            return response.Content != null ? response.Content.ReadAsByteArray() : null;
        }

        // ---- JS fetch / XHR (runs on the WebView's fetch worker thread) -----

        /// <summary>Called by the WebView's networking thread for every script
        /// fetch()/XHR. Follows redirects, applies the browser's UA/Accept
        /// headers and merges the script's own headers. Never throws.</summary>
        private JsFetchResult HttpFetchJs(string method, string url, string headers, string body)
        {
            JsFetchResult result = new JsFetchResult();
            result.Url = url;

            try
            {
                string scheme = WebUrl.SchemeOf(url);
                if (scheme != "http" && scheme != "https")
                {
                    result.Error = "Unsupported protocol: " + scheme;
                    return result;
                }

                string finalUrl;
                HttpResponseMessage response = SendWithRedirects(method, url, headers, body, out finalUrl);

                int code = (int)response.StatusCode;
                result.Status = code;
                result.StatusText = response.ReasonPhrase != null ? response.ReasonPhrase : "";
                result.Ok = code >= 200 && code < 300;
                result.Url = finalUrl;
                result.Headers = CollectResponseHeaders(response);
                result.Body = response.Content != null ? response.Content.ReadAsString() : "";
            }
            catch (Exception ex)
            {
                result.Error = DeepestMessage(ex);
            }
            return result;
        }

        /// <summary>Send a request of any method (with optional body and script
        /// headers), following up to 6 redirects. 301/302/303 turn POST into
        /// GET (browser behaviour); 307/308 keep method and body.</summary>
        private HttpResponseMessage SendWithRedirects(string method, string url,
            string requestHeaders, string body, out string finalUrl)
        {
            string current = url;
            string m = string.IsNullOrEmpty(method) ? "GET" : method.ToUpperInvariant();
            string mediaType = ExtractHeaderValue(requestHeaders, "content-type");
            if (mediaType == null || mediaType.Length == 0)
                mediaType = "application/x-www-form-urlencoded";

            for (int hop = 0; hop < 6; hop++)
            {
                HttpClient client = NewClient();
                AddCookieHeader(client, current);
                AddRequestHeaders(client.DefaultRequestHeaders, requestHeaders);

                HttpResponseMessage response;
                if (m == "POST") response = client.Post(current, MakeContent(body, mediaType));
                else if (m == "PUT") response = client.Put(current, MakeContent(body, mediaType));
                else if (m == "PATCH") response = client.Patch(current, MakeContent(body, mediaType));
                else if (m == "DELETE") response = client.Delete(current);
                else response = client.Get(current);

                if (response == null) throw new Exception("No response from " + WebUrl.HostOf(current));
                StoreCookies(response, current);

                int code = (int)response.StatusCode;
                if (code >= 300 && code < 400)
                {
                    string location = ReadHeader(response, "Location");
                    if (string.IsNullOrEmpty(location))
                        throw new Exception("HTTP " + code + " redirect with no Location header.");
                    if (code == 301 || code == 302 || code == 303)
                    {
                        if (m == "POST" || m == "PUT" || m == "PATCH") m = "GET";
                    }
                    current = WebUrl.Resolve(current, location);
                    continue;
                }

                finalUrl = current;
                return response;
            }
            throw new Exception("Too many redirects.");
        }

        private static HttpContent MakeContent(string body, string mediaType)
        {
            if (body == null) body = "";
            return new StringContent(body, new global::System.Text.UTF8Encoding(false), mediaType);
        }

        /// <summary>Add the script-supplied "Name: value" lines to the request
        /// (the browser's own UA/Accept stay unless a script overrides them).</summary>
        private static void AddRequestHeaders(HttpRequestHeaders headers, string headerLines)
        {
            if (headers == null || string.IsNullOrEmpty(headerLines)) return;
            int start = 0;
            while (start < headerLines.Length)
            {
                int nl = headerLines.IndexOf('\n', start);
                if (nl < 0) nl = headerLines.Length;
                string line = headerLines.Substring(start, nl - start);
                if (line.Length > 0 && line[line.Length - 1] == '\r') line = line.Substring(0, line.Length - 1);
                start = nl + 1;

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                if (name.Length == 0 || value.Length == 0) continue;
                // Content-Type is carried by the body content, not the header list
                if (name.ToLowerInvariant() == "content-type") continue;
                // User-Agent is a forbidden header name (browsers block script spoofing)
                if (name.ToLowerInvariant() == "user-agent") continue;
                try { headers.Add(name, value); }
                catch { /* a bad header must not fail the whole request */ }
            }
        }

        /// <summary>Pull one "Name: value" line out of a script header block.</summary>
        private static string ExtractHeaderValue(string headerLines, string name)
        {
            if (string.IsNullOrEmpty(headerLines)) return null;
            string lower = name.ToLowerInvariant();
            int start = 0;
            while (start < headerLines.Length)
            {
                int nl = headerLines.IndexOf('\n', start);
                if (nl < 0) nl = headerLines.Length;
                string line = headerLines.Substring(start, nl - start);
                if (line.Length > 0 && line[line.Length - 1] == '\r') line = line.Substring(0, line.Length - 1);
                start = nl + 1;

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                if (line.Substring(0, colon).Trim().ToLowerInvariant() == lower)
                    return line.Substring(colon + 1).Trim();
            }
            return null;
        }

        /// <summary>Flatten the (fixed) set of headers we care about into
        /// "Name: value\r\n" lines for the JS Headers object. HttpHeaders has
        /// no enumerator, so probe by name in both header stores.</summary>
        private static string CollectResponseHeaders(HttpResponseMessage response)
        {
            string outp = "";
            string[] names = new string[]
            {
                "Content-Type", "Content-Length", "Location", "Set-Cookie",
                "Content-Encoding", "Content-Language", "Cache-Control", "Expires",
                "Last-Modified", "ETag", "Server", "Date", "WWW-Authenticate",
                "Content-Disposition", "Refresh", "X-Frame-Options",
            };
            for (int i = 0; i < names.Length; i++)
            {
                string v = ReadHeaderBestEffort(response, names[i]);
                if (v != null && v.Length > 0) outp += names[i] + ": " + v + "\r\n";
            }
            return outp;
        }

        /// <summary>Read a header from the response, falling back to the content
        /// headers (Content-Type and friends live there) and to the lowercase
        /// spelling for servers that send lowercase names.</summary>
        private static string ReadHeaderBestEffort(HttpResponseMessage response, string name)
        {
            string v = ReadHeader(response, name);
            if (v != null) return v;
            v = ReadHeader(response, name.ToLowerInvariant());
            if (v != null) return v;
            if (response.Content != null && response.Content.Headers != null)
            {
                v = ReadHeader(response.Content, name);
                if (v != null) return v;
                v = ReadHeader(response.Content, name.ToLowerInvariant());
                if (v != null) return v;
            }
            return null;
        }

        private static string ReadHeader(HttpContent content, string name)
        {
            if (content == null || content.Headers == null) return null;
            if (!content.Headers.Contains(name)) return null;
            global::System.Collections.Generic.IEnumerable<string> values = content.Headers.GetValues(name);
            foreach (string s in values) return s;
            return null;
        }

        /// <summary>Attach the jar's cookies for this URL to the request.</summary>
        private void AddCookieHeader(HttpClient client, string url)
        {
            string cookie = cookies.HeaderFor(url);
            if (cookie == null || cookie.Length == 0) return;
            try { client.DefaultRequestHeaders.Add("Cookie", cookie); }
            catch { /* a rejected cookie header must not fail the request */ }
        }

        /// <summary>Capture every Set-Cookie the response carries (a response
        /// may hold several, so walk all values rather than the first).</summary>
        private void StoreCookies(HttpResponseMessage response, string url)
        {
            if (response.Headers != null && response.Headers.Contains("Set-Cookie"))
            {
                global::System.Collections.Generic.IEnumerable<string> values = response.Headers.GetValues("Set-Cookie");
                foreach (string s in values) cookies.AddFromSetCookie(s, url);
            }
        }

        private void FetchStylesheets(HtmlPage page, string baseUrl)
        {
            if (page.StyleSheetHrefs == null || page.StyleSheetHrefs.Count == 0) return;
            string baseForCss = !string.IsNullOrEmpty(page.BaseHref) ? WebUrl.Resolve(baseUrl, page.BaseHref) : baseUrl;
            int limit = Math.Min(page.StyleSheetHrefs.Count, 8);
            for (int i = 0; i < limit; i++)
            {
                string abs = WebUrl.Resolve(baseForCss, page.StyleSheetHrefs[i]);
                try
                {
                    string css = FetchString(abs);
                    if (!string.IsNullOrEmpty(css))
                    {
                        if (page.CssText.Length > 0) page.CssText += "\n";
                        page.CssText += css;
                    }
                }
                catch { /* a missing stylesheet is not fatal */ }
            }
        }

        // Fetch external <script src> sources (in document order) so the page
        // engine can run them alongside inline scripts.
        private void FetchScripts(HtmlPage page, string baseUrl)
        {
            if (page.Scripts == null || page.Scripts.Count == 0) return;
            string baseForJs = !string.IsNullOrEmpty(page.BaseHref) ? WebUrl.Resolve(baseUrl, page.BaseHref) : baseUrl;
            int limit = Math.Min(page.Scripts.Count, 8);
            for (int i = 0; i < limit; i++)
            {
                HtmlScript s = page.Scripts[i];
                if (s.Src == null || s.Code != null) continue; // inline or already fetched
                string abs = WebUrl.Resolve(baseForJs, s.Src);
                try
                {
                    string code = FetchString(abs);
                    if (!string.IsNullOrEmpty(code)) s.Code = code;
                }
                catch { /* a missing script is not fatal */ }
            }
        }

        private Dictionary<string, Image> FetchImages(HtmlPage page, string baseUrl)
        {
            Dictionary<string, Image> images = new Dictionary<string, Image>();
            if (page.Root == null) return images;

            string baseForImages = !string.IsNullOrEmpty(page.BaseHref) ? WebUrl.Resolve(baseUrl, page.BaseHref) : baseUrl;
            List<string> srcs = new List<string>();
            CollectImgSrcs(page.Root, srcs);

            int fetched = 0;
            for (int i = 0; i < srcs.Count && fetched < 12; i++)
            {
                string src = srcs[i];
                if (string.IsNullOrEmpty(src)) continue;
                if (src.StartsWith("data:")) continue; // data: URIs unsupported

                string abs = WebUrl.Resolve(baseForImages, src);
                if (images.ContainsKey(abs)) continue;

                try
                {
                    byte[] data = FetchBytes(abs);
                    if (data == null || data.Length == 0) continue;
                    Image img = new Png(data);
                    if (img != null && img.Width > 0 && img.Height > 0)
                    {
                        images[abs] = img;
                        fetched++;
                    }
                }
                catch { /* broken/unsupported image: fall back to the alt placeholder */ }
            }
            return images;
        }

        private static void CollectImgSrcs(DomNode node, List<string> srcs)
        {
            DomNode n = node.FirstChild;
            while (n != null)
            {
                if (n.IsElement)
                {
                    if (n.Tag == "img")
                    {
                        string s = n.GetAttr("src");
                        if (!string.IsNullOrEmpty(s)) srcs.Add(s);
                    }
                    CollectImgSrcs(n, srcs);
                }
                n = n.Next;
            }
        }

        private static string EscapeHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string ReadHeader(HttpResponseMessage response, string name)
        {
            if (response.Headers == null) return null;
            if (!response.Headers.Contains(name)) return null;
            string[] values = response.Headers.GetValues(name).ToArray();
            if (values == null || values.Length == 0) return null;
            return values[0];
        }

        private static string DeepestMessage(Exception ex)
        {
            Exception deepest = ex;
            while (deepest.InnerException != null) deepest = deepest.InnerException;
            if (deepest == ex || string.IsNullOrEmpty(deepest.Message)) return ex.Message;
            return ex.Message + " — " + deepest.Message;
        }

        private void Publish(HtmlPage doc, string url, string title, string status, Dictionary<string, Image> images = null)
        {
            pendingDoc = doc;
            pendingUrl = url;
            pendingTitle = title ?? "";
            pendingStatus = status ?? "";
            pendingImages = images;
            resultReady = true;
        }

        private void PublishError(string url, string title, string message)
        {
            string html = "<h2>" + EscapeHtml(title) + "</h2><p>" + EscapeHtml(message) + "</p>";
            HtmlPage page = HtmlPageBuilder.Parse(html);
            page.Title = title;
            Publish(page, url, title, "Error");
        }

        // Called from the fetch worker thread only. pct < 0 means "still connecting"
        // (indeterminate marquee); 0..100 is real progress. Consumed on the UI thread.
        private void PublishProgress(int pct, string text)
        {
            pendingProgress = pct;
            pendingProgressText = text ?? "";
            progressReady = true;
        }
    }
}