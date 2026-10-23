using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using HttpClient = Cosmos.Network.Http.HttpClient;
using HttpResponseMessage = Cosmos.Network.Http.HttpResponseMessage;
using HttpContent = Cosmos.Network.Http.HttpContent;
using StringContent = Cosmos.Network.Http.StringContent;
using HttpRequestHeaders = Cosmos.Network.Http.Headers.HttpRequestHeaders;
using HttpWebRequest = Cosmos.Network.Http.HttpWebRequest;
using SslVerification = Cosmos.Network.Http.SslVerification;
using System.IO;
using Windose.System.Kernel;
using Windose.System.Kernel.Attributes;
using Windose.Web.Tls;
using System.Text;
using System.Globalization;

namespace Windose.Programs
{
    [FileAssociation(".html")]
    [FileAssociation(".htm")]
    public class InternetExplorer : Window
    {
        private const string HomeUrl = "about:home";

        private const string HomeHtml = @"<html>
<head><title>Internet Explorer</title><style>
body { font-family: sans-serif; margin: 30px; background: #ffffff; color: #222; text-align: center; }
.hero { margin: 15px auto 25px auto; }
h1 { color: #003366; font-size: 26px; margin-bottom: 4px; }
.tagline { color: #666; font-size: 13px; margin-bottom: 20px; }
.searchbox { margin: 15px auto; }
.searchbox input[type=text] { width: 360px; padding: 6px 10px; font-size: 14px; border: 1px solid #7799bb; }
.searchbox input[type=submit] { padding: 6px 14px; font-size: 14px; background: #0066cc; color: #fff; border: 1px solid #0055aa; cursor: pointer; }
.cards { display: inline-block; text-align: left; margin-top: 15px; }
.card { display: inline-block; width: 130px; margin: 6px; padding: 10px; border: 1px solid #d0d8e0; border-radius: 4px; background: #f8fafc; text-align: center; vertical-align: top; }
.card a { color: #004499; text-decoration: none; font-weight: bold; font-size: 13px; display: block; }
.card a:hover { text-decoration: underline; }
.card .sub { color: #777; font-size: 11px; margin-top: 4px; }
.info { margin-top: 30px; color: #888; font-size: 11px; border-top: 1px solid #eee; padding-top: 12px; }
</style></head>
<body>
<div class=""hero"">
<h1>Internet Explorer</h1>
<div class=""tagline"">Windose Web Browser</div>

<div class=""searchbox"">
<form action=""https://html.duckduckgo.com/html/"" method=""get"">
<input type=""text"" name=""q"" placeholder=""Search the web with DuckDuckGo...""> &nbsp;
<input type=""submit"" value=""Search"">
</form>
</div>

<div class=""cards"">
<div class=""card""><a href=""https://html.duckduckgo.com/html/"">DuckDuckGo</a><div class=""sub"">Web Search</div></div>
<div class=""card""><a href=""http://frogfind.com"">FrogFind</a><div class=""sub"">Lightweight Search</div></div>
<div class=""card""><a href=""https://en.wikipedia.org/wiki/Main_Page"">Wikipedia</a><div class=""sub"">Free Encyclopedia</div></div>
<div class=""card""><a href=""https://news.ycombinator.com/"">Hacker News</a><div class=""sub"">Tech News</div></div>
<div class=""card""><a href=""file:///mnt/"">Local Files</a><div class=""sub"">Storage (/mnt)</div></div>
<div class=""card""><a href=""about:favorites"">Favorites</a><div class=""sub"">Saved Bookmarks</div></div>
<div class=""card""><a href=""about:history"">History</a><div class=""sub"">Visited Pages</div></div>
<div class=""card""><a href=""about:cookies"">Cookies</a><div class=""sub"">Cookie Jar</div></div>
</div>

<div class=""info"">
Windose Internet Explorer &bull; HTML / CSS Engine &bull; Native TLS 1.2 &bull; JavaScript Support
</div>
</div>
</body></html>";

        private class FavoriteItem
        {
            public string Title;
            public string Url;
        }

        private class HistoryItem
        {
            public string Title;
            public string Url;
            public DateTime VisitedAt;
        }

        private readonly List<FavoriteItem> favoritesList = new List<FavoriteItem>();
        private readonly List<HistoryItem> historyList = new List<HistoryItem>();
        private const string FavoritesFilePath = "/mnt/user/favorites.txt";
        private const string AltFavoritesFilePath = "/mnt/favorites.txt";

        private int refreshCountdownSeconds = -1;
        private string refreshTargetUrl = null;
        private long lastRefreshTick = 0;
        private string currentStatus = "Ready";

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

        // A form submitted with method=post sets this before the worker starts;
        // Fetch consumes it and posts instead of getting. Cleared on completion.
        private string pendingPostBody;
        private string pendingPostType = "application/x-www-form-urlencoded";

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
        private volatile bool cancelPendingFetch;
        private string lastRawHtml = "";

        static InternetExplorer()
        {
            // The Cosmos HTTP stack caps response headers at 4 KB by default, which many
            // modern sites exceed (large Set-Cookie / tracking headers) -> "headers size
            // exceeded limits". HttpClient builds an HttpWebRequest per request that reads
            // this static default, so raise it globally.
            HttpWebRequest.DefaultMaximumResponseHeadersLength = 128; // KB
        }

        public InternetExplorer() : this(null, 80, 60)
        {
        }

        public InternetExplorer(int x, int y) : this(null, x, y)
        {
        }

        public InternetExplorer(string initialUrl = null, int x = 80, int y = 60) : base(x, y, 800, 600, "Internet Explorer", true, null)
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

            toolbar.AddButton("Back", () => GoBack());
            toolbar.AddButton("Forward", () => GoForward());
            toolbar.AddButton("Stop", () => StopLoading());
            toolbar.AddButton("Refresh", () => Refresh());
            toolbar.AddButton("Home", () => Home());
            toolbar.AddButton("Go", () => Navigate(addressBar.Address));
            toolbar.AddButton("Favorites", () => ShowFavorites());
            toolbar.AddButton("+ Fav", () => AddCurrentToFavorites());
            toolbar.AddButton("History", () => ShowHistory());
            toolbar.AddButton("Save", () => SavePage());
            toolbar.AddButton("Source", () => ViewSource());
            toolbar.AddButton("Zoom +", () => webView.ZoomIn());
            toolbar.AddButton("Zoom -", () => webView.ZoomOut());

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

            LoadFavorites();

            // ---- wiring ----
            addressBar.addressField.EnterPressed += () => Navigate(addressBar.Address);
            addressBar.dropDownButton.leftClickAction = () => Navigate("about:history");

            webView.LinkHovered += href =>
            {
                if (!string.IsNullOrEmpty(href))
                {
                    statusBar.SetPanelText(0, WebUrl.Resolve(currentUrl, href));
                }
                else
                {
                    statusBar.SetPanelText(0, currentStatus);
                }
            };

            webView.LinkActivated += href =>
            {
                string target = WebUrl.Resolve(currentUrl, href);
                if (target.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                {
                    string jsCode = target.Substring(11).Trim();
                    if (jsCode == "void(0)" || jsCode == "void(0);" || jsCode.Length == 0 || jsCode == ";")
                        return;
                    webView.EvaluateScript(jsCode);
                    return;
                }
                if (target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus("Mail to: " + target.Substring(7));
                    return;
                }
                if (href.StartsWith("#") || (target.IndexOf('#') >= 0 && WebUrl.OriginOf(target) == WebUrl.OriginOf(currentUrl) && WebUrl.PathOf(target) == WebUrl.PathOf(currentUrl)))
                {
                    string hash = target.Substring(target.IndexOf('#') + 1);
                    if (webView.ScrollToAnchor(hash))
                    {
                        currentUrl = target;
                        addressBar.Address = target;
                        return;
                    }
                }
                Navigate(target);
            };

            // JS engine -> shell: script-driven navigation, reload, history, HTTP.
            webView.NavigateRequested += url => Navigate(WebUrl.Resolve(currentUrl, url));
            webView.ReloadRequested += Refresh;
            webView.HistoryBackRequested += GoBack;
            webView.HistoryForwardRequested += GoForward;
            webView.HttpFetch = HttpFetchJs;
            webView.GetCookies = JsGetCookies;
            webView.SetCookie = JsSetCookie;
            webView.FormPostRequested = BeginPostNavigation;

            Navigate(!string.IsNullOrEmpty(initialUrl) ? initialUrl : HomeUrl);
        }

        public override void Update()
        {
            base.Update();

            if (refreshCountdownSeconds >= 0 && !string.IsNullOrEmpty(refreshTargetUrl))
            {
                long now = DateTime.UtcNow.Ticks;
                if (now - lastRefreshTick >= 10000000L) // 1 second
                {
                    lastRefreshTick = now;
                    refreshCountdownSeconds--;
                    if (refreshCountdownSeconds <= 0)
                    {
                        string target = refreshTargetUrl;
                        refreshCountdownSeconds = -1;
                        refreshTargetUrl = null;
                        Navigate(target);
                    }
                }
            }

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
                    currentUrl = url;
                    addressBar.Address = url;
                    webView.SetDocument(doc, url, imgs);
                    if (!string.IsNullOrEmpty(title))
                        SetTitle(title + " - Internet Explorer");

                    RecordHistory(url, title);

                    if (doc.RefreshSeconds >= 0)
                    {
                        refreshCountdownSeconds = doc.RefreshSeconds;
                        refreshTargetUrl = !string.IsNullOrEmpty(doc.RefreshUrl) ? WebUrl.Resolve(url, doc.RefreshUrl) : url;
                        lastRefreshTick = DateTime.UtcNow.Ticks;
                    }
                    else
                    {
                        refreshCountdownSeconds = -1;
                        refreshTargetUrl = null;
                    }
                }
                if (!string.IsNullOrEmpty(status)) SetStatus(status);
            }
        }

        private void SetStatus(string text)
        {
            currentStatus = text ?? "";
            statusBar.SetPanelText(0, currentStatus);
        }

        // ---- navigation ----

        private void Navigate(string rawUrl)
        {
            if (rawUrl != null && rawUrl.StartsWith("media:", StringComparison.OrdinalIgnoreCase))
            {
                string target = rawUrl.Substring(6);
                LaunchTracker.Start(() => new MediaPlayer(target));
                return;
            }
            refreshCountdownSeconds = -1;
            refreshTargetUrl = null;

            string url = WebUrl.Normalize(rawUrl);
            if (url != null)
            {
                string checkUrl = url.ToLowerInvariant();
                int qIdx = checkUrl.IndexOf('?');
                if (qIdx >= 0) checkUrl = checkUrl.Substring(0, qIdx);
                if (checkUrl.EndsWith(".avi") || checkUrl.EndsWith(".mp4") || checkUrl.EndsWith(".webm") ||
                    checkUrl.EndsWith(".mkv") || checkUrl.EndsWith(".mov") || checkUrl.EndsWith(".wmv") ||
                    checkUrl.EndsWith(".flv") || checkUrl.EndsWith(".wav") || checkUrl.EndsWith(".mp3"))
                {
                    LaunchTracker.Start(() => new MediaPlayer(url));
                    return;
                }
            }
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

        /// <summary>A form was submitted with method=post. Record the body so the
        /// worker posts instead of gets, then fetch like any other navigation.</summary>
        private void BeginPostNavigation(string rawUrl, string body)
        {
            string url = WebUrl.Normalize(rawUrl);
            if (url != null)
            {
                string checkUrl = url.ToLowerInvariant();
                int qIdx = checkUrl.IndexOf('?');
                if (qIdx >= 0) checkUrl = checkUrl.Substring(0, qIdx);
                if (checkUrl.EndsWith(".avi") || checkUrl.EndsWith(".mp4") || checkUrl.EndsWith(".webm") ||
                    checkUrl.EndsWith(".mkv") || checkUrl.EndsWith(".mov") || checkUrl.EndsWith(".wmv") ||
                    checkUrl.EndsWith(".flv") || checkUrl.EndsWith(".wav") || checkUrl.EndsWith(".mp3"))
                {
                    LaunchTracker.Start(() => new MediaPlayer(url));
                    return;
                }
            }
            if (url == null) { SetStatus("Invalid address"); return; }

            if (!string.IsNullOrEmpty(currentUrl))
            {
                backStack.Add(currentUrl);
                if (backStack.Count > 50) backStack.RemoveAt(0);
            }
            forwardStack.Clear();

            currentUrl = url;
            addressBar.Address = url;

            pendingPostBody = body != null ? body : "";
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

        private void StopLoading()
        {
            cancelPendingFetch = true;
            progressBar.Visible = false;
            progressBar.Indeterminate = false;
            SetStatus("Stopped.");
        }

        private void ShowFavorites()
        {
            Navigate("about:favorites");
        }

        private void ShowHistory()
        {
            Navigate("about:history");
        }

        private void ViewSource()
        {
            Navigate("about:source");
        }

        private void SavePage()
        {
            if (string.IsNullOrEmpty(lastRawHtml))
            {
                SetStatus("Nothing to save.");
                return;
            }

            try
            {
                string dir = "/mnt/user/downloads/";
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch { dir = "/mnt/"; }
                }

                string host = WebUrl.HostOf(currentUrl);
                if (string.IsNullOrEmpty(host)) host = "page";
                char[] invalid = Path.GetInvalidFileNameChars();
                for (int i = 0; i < invalid.Length; i++) host = host.Replace(invalid[i], '_');
                string filePath = Path.Combine(dir, host + ".html");

                File.WriteAllText(filePath, lastRawHtml);
                SetStatus("Saved page to " + filePath);
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message);
            }
        }

        private void LoadFavorites()
        {
            favoritesList.Clear();
            string path = File.Exists(FavoritesFilePath) ? FavoritesFilePath
                : (File.Exists(AltFavoritesFilePath) ? AltFavoritesFilePath : null);

            if (path != null)
            {
                try
                {
                    string[] lines = File.ReadAllLines(path);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                        int pipe = line.IndexOf('|');
                        if (pipe > 0)
                        {
                            string title = line.Substring(0, pipe).Trim();
                            string fUrl = line.Substring(pipe + 1).Trim();
                            if (fUrl.Length > 0)
                                favoritesList.Add(new FavoriteItem { Title = title, Url = fUrl });
                        }
                        else
                        {
                            favoritesList.Add(new FavoriteItem { Title = line, Url = line });
                        }
                    }
                }
                catch { }
            }

            if (favoritesList.Count == 0)
            {
                favoritesList.Add(new FavoriteItem { Title = "DuckDuckGo", Url = "https://html.duckduckgo.com/html/" });
                favoritesList.Add(new FavoriteItem { Title = "FrogFind", Url = "http://frogfind.com" });
                favoritesList.Add(new FavoriteItem { Title = "Wikipedia", Url = "https://en.wikipedia.org/wiki/Main_Page" });
                favoritesList.Add(new FavoriteItem { Title = "Hacker News", Url = "https://news.ycombinator.com/" });
                favoritesList.Add(new FavoriteItem { Title = "Local Files (/mnt)", Url = "file:///mnt/" });
                favoritesList.Add(new FavoriteItem { Title = "Internet Explorer Start Page", Url = "about:home" });
                SaveFavorites();
            }
        }

        private void SaveFavorites()
        {
            try
            {
                string dir = "/mnt/user/";
                if (!Directory.Exists(dir))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch { dir = "/mnt/"; }
                }
                string path = Path.Combine(dir, "favorites.txt");
                List<string> lines = new List<string>();
                for (int i = 0; i < favoritesList.Count; i++)
                {
                    lines.Add(favoritesList[i].Title + "|" + favoritesList[i].Url);
                }
                File.WriteAllLines(path, lines.ToArray());
            }
            catch { }
        }

        private void AddCurrentToFavorites()
        {
            if (string.IsNullOrEmpty(currentUrl) || currentUrl == "about:blank")
            {
                SetStatus("Cannot add blank page to favorites.");
                return;
            }

            string title = webView.DocumentTitle;
            if (string.IsNullOrEmpty(title)) title = WebUrl.HostOf(currentUrl);
            if (string.IsNullOrEmpty(title)) title = currentUrl;

            for (int i = 0; i < favoritesList.Count; i++)
            {
                if (favoritesList[i].Url.Equals(currentUrl, StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus("Already in Favorites: " + title);
                    return;
                }
            }

            favoritesList.Add(new FavoriteItem { Title = title, Url = currentUrl });
            SaveFavorites();
            SetStatus("Added to Favorites: " + title);
        }

        private void RecordHistory(string url, string title)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("about:")) return;

            for (int i = historyList.Count - 1; i >= 0; i--)
            {
                if (historyList[i].Url.Equals(url, StringComparison.OrdinalIgnoreCase))
                {
                    historyList.RemoveAt(i);
                    break;
                }
            }

            historyList.Insert(0, new HistoryItem
            {
                Url = url,
                Title = !string.IsNullOrEmpty(title) ? title : url,
                VisitedAt = DateTime.Now
            });

            if (historyList.Count > 100)
            {
                historyList.RemoveAt(historyList.Count - 1);
            }
        }

        public override void HandleKeyboard(KeyEvent keyEvent)
        {
            bool ctrl = IsControlPressed(keyEvent);
            bool alt = (keyEvent.Modifiers & ConsoleModifiers.Alt) != 0;

            if (keyEvent.Key == Key.F5 || (ctrl && (keyEvent.Key == Key.R || keyEvent.KeyChar == 'r' || keyEvent.KeyChar == 'R')))
            {
                Refresh();
                return;
            }

            if (keyEvent.Key == Key.Escape)
            {
                StopLoading();
                return;
            }

            if (alt && keyEvent.Key == Key.LeftArrow)
            {
                GoBack();
                return;
            }

            if (alt && keyEvent.Key == Key.RightArrow)
            {
                GoForward();
                return;
            }

            if (keyEvent.Key == Key.Backspace && !IsInputFocused())
            {
                GoBack();
                return;
            }

            if (ctrl && (keyEvent.Key == Key.D || keyEvent.KeyChar == 'd' || keyEvent.KeyChar == 'D'))
            {
                AddCurrentToFavorites();
                return;
            }

            if (ctrl && (keyEvent.Key == Key.H || keyEvent.KeyChar == 'h' || keyEvent.KeyChar == 'H'))
            {
                Navigate("about:history");
                return;
            }

            if (ctrl && (keyEvent.Key == Key.U || keyEvent.KeyChar == 'u' || keyEvent.KeyChar == 'U'))
            {
                ViewSource();
                return;
            }

            if (ctrl && (keyEvent.Key == Key.S || keyEvent.KeyChar == 's' || keyEvent.KeyChar == 'S'))
            {
                SavePage();
                return;
            }

            if (ctrl && (keyEvent.Key == Key.L || keyEvent.KeyChar == 'l' || keyEvent.KeyChar == 'L'))
            {
                addressBar.addressField.RequestFocus();
                return;
            }

            if (ctrl && (keyEvent.KeyChar == '+' || keyEvent.KeyChar == '='))
            {
                webView.ZoomIn();
                return;
            }

            if (ctrl && keyEvent.KeyChar == '-')
            {
                webView.ZoomOut();
                return;
            }

            if (ctrl && keyEvent.KeyChar == '0')
            {
                webView.ResetZoom();
                return;
            }

            base.HandleKeyboard(keyEvent);
        }

        private bool IsInputFocused()
        {
            if (addressBar.addressField != null && addressBar.addressField.Focused) return true;
            if (webView != null && webView.HasFocusedWidget) return true;
            return false;
        }

        private static string ExtractQueryParam(string query, string paramName)
        {
            if (string.IsNullOrEmpty(query)) return "";
            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                string p = pairs[i];
                int eq = p.IndexOf('=');
                if (eq > 0)
                {
                    string key = p.Substring(0, eq).Trim();
                    if (string.Equals(key, paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        string val = p.Substring(eq + 1).Trim();
                        return UriUnescape(val);
                    }
                }
            }
            return "";
        }

        private static string UriUnescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string r = s.Replace('+', ' ');
            int idx = r.IndexOf('%');
            if (idx < 0) return r;
            StringBuilder sb = new StringBuilder(r.Length);
            for (int i = 0; i < r.Length; i++)
            {
                if (r[i] == '%' && i + 2 < r.Length)
                {
                    string hex = r.Substring(i + 1, 2);
                    int code;
                    if (int.TryParse(hex, NumberStyles.HexNumber, null, out code))
                    {
                        sb.Append((char)code);
                        i += 2;
                        continue;
                    }
                }
                sb.Append(r[i]);
            }
            return sb.ToString();
        }

        private void BeginFetch(string url)
        {
            cancelPendingFetch = false;
            string host = WebUrl.HostOf(url);
            SetStatus("Connecting to " + (host.Length > 0 ? host : url) + "...");

            string lower = url.ToLowerInvariant();

            if (lower.StartsWith("about:"))
            {
                ApplyOffline(url);
                return;
            }

            string scheme = WebUrl.SchemeOf(url);
            if (scheme != "http" && scheme != "https" && scheme != "file")
            {
                PublishError(url, "Unsupported protocol",
                    "This browser can only open http, https, file, and about: addresses.");
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
            else if (lower.StartsWith("about:favorites"))
            {
                int q = url.IndexOf('?');
                if (q >= 0)
                {
                    string query = url.Substring(q + 1);
                    if (query.StartsWith("delete="))
                    {
                        int idx;
                        if (int.TryParse(query.Substring(7), out idx) && idx >= 0 && idx < favoritesList.Count)
                        {
                            favoritesList.RemoveAt(idx);
                            SaveFavorites();
                        }
                    }
                    else if (query.IndexOf("name=") >= 0 && query.IndexOf("url=") >= 0)
                    {
                        string name = ExtractQueryParam(query, "name");
                        string fUrl = ExtractQueryParam(query, "url");
                        if (!string.IsNullOrEmpty(fUrl))
                        {
                            fUrl = WebUrl.Normalize(fUrl);
                            if (string.IsNullOrEmpty(name)) name = WebUrl.HostOf(fUrl);
                            if (string.IsNullOrEmpty(name)) name = fUrl;
                            favoritesList.Add(new FavoriteItem { Title = name, Url = fUrl });
                            SaveFavorites();
                        }
                    }
                }

                string favHtml = @"<html>
<head><title>Favorites</title><style>
body { font-family: sans-serif; margin: 20px; background: #ffffff; color: #222; }
h1 { color: #003366; border-bottom: 2px solid #003366; padding-bottom: 5px; font-size: 20px; }
table { width: 100%; border-collapse: collapse; margin-top: 15px; }
th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #ddd; }
th { background-color: #f2f5f8; color: #333; }
tr:hover { background-color: #f8f9fa; }
a { color: #0066cc; text-decoration: none; font-weight: bold; }
a:hover { text-decoration: underline; }
.btn { display: inline-block; padding: 3px 8px; background: #e0e0e0; border: 1px solid #999; border-radius: 3px; color: #000; text-decoration: none; font-size: 11px; }
.btn-del { color: #cc0000; }
.form-box { background: #f5f8fb; border: 1px solid #c0d0e0; padding: 12px; margin-top: 15px; border-radius: 4px; }
</style></head>
<body>
<h1>Favorites &amp; Bookmarks</h1>
<div class=""form-box"">
<form action=""about:favorites"" method=""get"">
<b>Add a Bookmark:</b> &nbsp;
Name: <input type=""text"" name=""name"" size=""20""> &nbsp;
URL: <input type=""text"" name=""url"" size=""30"" value=""http://""> &nbsp;
<input type=""submit"" value=""Add to Favorites"">
</form>
</div>
<table>
<tr><th>Title</th><th>Address</th><th>Action</th></tr>";

                for (int i = 0; i < favoritesList.Count; i++)
                {
                    FavoriteItem fav = favoritesList[i];
                    favHtml += "<tr>"
                        + "<td><a href=\"" + EscapeHtml(fav.Url) + "\">" + EscapeHtml(fav.Title) + "</a></td>"
                        + "<td style=\"color:#555; font-size:12px;\">" + EscapeHtml(fav.Url) + "</td>"
                        + "<td><a class=\"btn btn-del\" href=\"about:favorites?delete=" + i + "\">Remove</a></td>"
                        + "</tr>";
                }

                if (favoritesList.Count == 0)
                {
                    favHtml += "<tr><td colspan=\"3\" style=\"color:#888;\">No favorites saved yet.</td></tr>";
                }

                favHtml += "</table><p style=\"color:#888; font-size:11px; margin-top:20px;\">Saved to " + FavoritesFilePath + "</p></body></html>";

                HtmlPage page = HtmlPageBuilder.Parse(favHtml);
                Publish(page, "about:favorites", "Favorites", "Done");
            }
            else if (lower.StartsWith("about:history"))
            {
                int q = url.IndexOf('?');
                if (q >= 0 && url.Substring(q + 1).Contains("clear=1"))
                {
                    historyList.Clear();
                }

                string histHtml = @"<html>
<head><title>History</title><style>
body { font-family: sans-serif; margin: 20px; background: #ffffff; color: #222; }
h1 { color: #003366; border-bottom: 2px solid #003366; padding-bottom: 5px; font-size: 20px; }
table { width: 100%; border-collapse: collapse; margin-top: 15px; }
th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #ddd; }
th { background-color: #f2f5f8; color: #333; }
tr:hover { background-color: #f8f9fa; }
a { color: #0066cc; text-decoration: none; font-weight: bold; }
a:hover { text-decoration: underline; }
.btn { display: inline-block; padding: 4px 10px; background: #eee; border: 1px solid #999; border-radius: 3px; color: #333; text-decoration: none; font-size: 12px; }
.btn:hover { background: #ddd; }
</style></head>
<body>
<h1>Browsing History</h1>
<p><a class=""btn"" href=""about:history?clear=1"">Clear All History</a></p>
<table>
<tr><th>Time</th><th>Page Title</th><th>Address</th></tr>";

                for (int i = 0; i < historyList.Count; i++)
                {
                    HistoryItem item = historyList[i];
                    histHtml += "<tr>"
                        + "<td style=\"color:#888; font-size:12px; width:80px;\">" + item.VisitedAt.ToString("HH:mm:ss") + "</td>"
                        + "<td><a href=\"" + EscapeHtml(item.Url) + "\">" + EscapeHtml(item.Title) + "</a></td>"
                        + "<td style=\"color:#555; font-size:12px;\">" + EscapeHtml(item.Url) + "</td>"
                        + "</tr>";
                }

                if (historyList.Count == 0)
                {
                    histHtml += "<tr><td colspan=\"3\" style=\"color:#888;\">No history entries recorded yet.</td></tr>";
                }

                histHtml += "</table></body></html>";

                HtmlPage page = HtmlPageBuilder.Parse(histHtml);
                Publish(page, "about:history", "Browsing History", "Done");
            }
            else if (lower.StartsWith("about:cookies"))
            {
                int q = url.IndexOf('?');
                if (q >= 0 && url.Substring(q + 1).Contains("clear=1"))
                {
                    cookies.Clear();
                }

                List<Cookie> allCookies = cookies.GetAll();

                string cookieHtml = @"<html>
<head><title>Cookies</title><style>
body { font-family: sans-serif; margin: 20px; background: #ffffff; color: #222; }
h1 { color: #003366; border-bottom: 2px solid #003366; padding-bottom: 5px; font-size: 20px; }
table { width: 100%; border-collapse: collapse; margin-top: 15px; }
th, td { text-align: left; padding: 6px 10px; border-bottom: 1px solid #ddd; font-size: 13px; }
th { background-color: #f2f5f8; color: #333; }
tr:hover { background-color: #f8f9fa; }
.btn { display: inline-block; padding: 4px 10px; background: #eee; border: 1px solid #999; border-radius: 3px; color: #333; text-decoration: none; font-size: 12px; }
.badge { display: inline-block; padding: 2px 5px; border-radius: 3px; font-size: 10px; font-weight: bold; }
.sec { background: #d4edda; color: #155724; }
.http { background: #cce5ff; color: #004085; }
</style></head>
<body>
<h1>Stored Cookies</h1>
<p><a class=""btn"" href=""about:cookies?clear=1"">Clear All Cookies</a></p>
<table>
<tr><th>Domain</th><th>Path</th><th>Name</th><th>Value</th><th>Flags</th></tr>";

                for (int i = 0; i < allCookies.Count; i++)
                {
                    Cookie c = allCookies[i];
                    string flags = "";
                    if (c.Secure) flags += "<span class=\"badge sec\">Secure</span> ";
                    if (c.HttpOnly) flags += "<span class=\"badge http\">HttpOnly</span>";
                    if (flags == "") flags = "-";

                    cookieHtml += "<tr>"
                        + "<td><b>" + EscapeHtml(c.Domain) + "</b></td>"
                        + "<td>" + EscapeHtml(c.Path) + "</td>"
                        + "<td>" + EscapeHtml(c.Name) + "</td>"
                        + "<td style=\"max-width:200px; word-break:break-all;\">" + EscapeHtml(c.Value) + "</td>"
                        + "<td>" + flags + "</td>"
                        + "</tr>";
                }

                if (allCookies.Count == 0)
                {
                    cookieHtml += "<tr><td colspan=\"5\" style=\"color:#888;\">No cookies in jar.</td></tr>";
                }

                cookieHtml += "</table></body></html>";

                HtmlPage page = HtmlPageBuilder.Parse(cookieHtml);
                Publish(page, "about:cookies", "Cookies", "Done");
            }
            else if (lower == "about:source")
            {
                string srcText = string.IsNullOrEmpty(lastRawHtml)
                    ? "<em>No HTML source available for this page. Navigate to a page first, then view source.</em>"
                    : "<pre style=\"background:#f5f5f5; border:1px solid #ccc; padding:10px; font-family:monospace; font-size:12px; word-wrap:break-word; white-space:pre-wrap;\">" + EscapeHtml(lastRawHtml) + "</pre>";
                string srcHtml = "<html><head><title>Page Source</title></head><body style=\"font-family:sans-serif; margin:15px;\">"
                    + "<h3>HTML Source for " + EscapeHtml(currentUrl) + "</h3>"
                    + "<p><a href=\"#\" onclick=\"window.location.href='about:home'\" style=\"color:#0066cc;\">Back to Home</a></p>"
                    + srcText + "</body></html>";
                HtmlPage page = HtmlPageBuilder.Parse(srcHtml);
                Publish(page, url, "Page Source - " + currentUrl, "Done");
            }
            else
            {
                PublishError(url, "Unknown page", "No built-in page named " + url + ".");
            }
        }

        private static string FileUrlToLocalPath(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            string path = url;
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(7);
                if (path.StartsWith("/") && path.Length >= 3 && path[2] == ':')
                    path = path.Substring(1);
            }
            else if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(5);
                if (path.StartsWith("/") && path.Length >= 3 && path[2] == ':')
                    path = path.Substring(1);
            }

            path = path.Replace("%20", " ");
            int hash = path.IndexOf('#');
            if (hash >= 0) path = path.Substring(0, hash);
            int q = path.IndexOf('?');
            if (q >= 0) path = path.Substring(0, q);

            if (File.Exists(path) || Directory.Exists(path)) return path;
            string alt = path.Replace('/', '\\');
            if (File.Exists(alt) || Directory.Exists(alt)) return alt;
            return path;
        }

        private string LoadLocalUrl(string url, out string finalUrl)
        {
            finalUrl = url;
            string localPath = FileUrlToLocalPath(url);

            if (Directory.Exists(localPath))
            {
                return GenerateDirectoryListing(localPath, url);
            }

            if (File.Exists(localPath))
            {
                return File.ReadAllText(localPath);
            }

            throw new FileNotFoundException("Local file not found: " + localPath, localPath);
        }

        private string GenerateDirectoryListing(string dirPath, string baseUrl)
        {
            string title = "Index of " + dirPath.Replace('\\', '/');
            string html = "<html><head><title>" + EscapeHtml(title) + "</title><style>"
                + "body { font-family: monospace; margin: 20px; }"
                + "h2 { font-family: sans-serif; }"
                + "table { width: 100%; border-collapse: collapse; }"
                + "th, td { text-align: left; padding: 4px 8px; }"
                + "tr:hover { background-color: #f0f0f0; }"
                + "a { text-decoration: none; color: #0066cc; }"
                + "a:hover { text-decoration: underline; }"
                + "</style></head><body>"
                + "<h2>" + EscapeHtml(title) + "</h2><hr>"
                + "<table>"
                + "<tr><th>Name</th><th>Type</th><th>Size</th></tr>";

            string normBase = baseUrl.Replace('\\', '/');
            if (!normBase.EndsWith("/")) normBase += "/";

            if (dirPath != "/" && dirPath != "0:\\" && dirPath != "0:/" && dirPath != "/mnt")
            {
                string parentUrl = WebUrl.Resolve(normBase, "../");
                html += "<tr><td><a href=\"" + parentUrl + "\">[.. Parent Directory]</a></td><td>DIR</td><td>-</td></tr>";
            }

            try
            {
                string[] dirs = Directory.GetDirectories(dirPath);
                for (int i = 0; i < dirs.Length; i++)
                {
                    string d = dirs[i];
                    string name = Path.GetFileName(d);
                    if (string.IsNullOrEmpty(name)) name = d;
                    string target = normBase + UriEscapeSimple(name) + "/";
                    html += "<tr><td><a href=\"" + target + "\">[DIR] " + EscapeHtml(name) + "/</a></td><td>DIR</td><td>-</td></tr>";
                }

                string[] files = Directory.GetFiles(dirPath);
                for (int i = 0; i < files.Length; i++)
                {
                    string f = files[i];
                    string name = Path.GetFileName(f);
                    if (string.IsNullOrEmpty(name)) name = f;
                    string target = normBase + UriEscapeSimple(name);
                    long len = 0;
                    try { len = new FileInfo(f).Length; } catch { }
                    html += "<tr><td><a href=\"" + target + "\">" + EscapeHtml(name) + "</a></td><td>FILE</td><td>" + len + " B</td></tr>";
                }
            }
            catch (Exception ex)
            {
                html += "<tr><td colspan=\"3\" style=\"color:red;\">Error listing directory: " + EscapeHtml(ex.Message) + "</td></tr>";
            }

            html += "</table><hr><p style=\"color:#888; font-size:11px;\">Windose Internet Explorer Local Browser</p></body></html>";
            return html;
        }

        private static string UriEscapeSimple(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace(" ", "%20").Replace("#", "%23");
        }

        private void Fetch(string url)
        {
            try
            {
                cancelPendingFetch = false;
                string html;
                string finalUrl = url;

                // A pending form POST is sent through the script fetch pipeline
                // (it already handles redirects, cookies and headers).
                string postBody = pendingPostBody;
                pendingPostBody = null;
                if (postBody != null)
                {
                    JsFetchResult posted = HttpFetchJs("POST", url,
                        "Content-Type: " + pendingPostType, postBody);
                    if (posted.Error != null && posted.Error.Length > 0)
                        throw new Exception(posted.Error);
                    html = posted.Body;
                    finalUrl = posted.Url != null ? posted.Url : url;
                    currentUrl = finalUrl;
                }
                else if (WebUrl.SchemeOf(url) == "file")
                {
                    PublishProgress(10, "Opening local file…");
                    html = LoadLocalUrl(url, out finalUrl);
                }
                else
                {
                    html = FetchString(url, out finalUrl);
                }

                if (cancelPendingFetch) return;

                lastRawHtml = html ?? "";

                if (YouTubeHydrator.IsYouTubeUrl(finalUrl))
                {
                    PublishProgress(20, "Hydrating YouTube…");
                    html = YouTubeHydrator.Hydrate(html, finalUrl, u => FetchString(u));
                }

                int chars = html != null ? html.Length : 0;
                PublishProgress(25, "Received " + chars + " chars, parsing…");

                HtmlPage page = HtmlPageBuilder.Parse(html ?? "");
                if (string.IsNullOrEmpty(page.Title))
                {
                    if (WebUrl.SchemeOf(finalUrl) == "file")
                    {
                        string localPath = FileUrlToLocalPath(finalUrl);
                        string fileName = Path.GetFileName(localPath);
                        page.Title = !string.IsNullOrEmpty(fileName) ? fileName : localPath;
                    }
                    else
                    {
                        page.Title = WebUrl.HostOf(finalUrl);
                    }
                }

                if (cancelPendingFetch) return;

                PublishProgress(45, "Loading stylesheets…");
                string cssErr;
                int cssLoaded = FetchStylesheets(page, finalUrl, out cssErr);

                if (cancelPendingFetch) return;

                // Progressive render: show page structure, text and styles immediately without waiting for images/scripts
                Publish(page, finalUrl, page.Title, "Loading page…", new Dictionary<string, Image>());

                PublishProgress(65, "Loading scripts…");
                FetchScripts(page, finalUrl);

                if (cancelPendingFetch) return;

                PublishProgress(80, "Loading images…");
                Dictionary<string, Image> images = FetchImages(page, finalUrl);

                if (cancelPendingFetch) return;

                PublishProgress(95, "Rendering…");

                string status = "Done — " + chars + " chars";
                if (cssLoaded > 0) status += ", " + cssLoaded + " css";
                else if (page.StyleSheetHrefs != null && page.StyleSheetHrefs.Count > 0 && cssErr != null)
                    status += " (CSS: " + cssErr + ")";
                if (images.Count > 0) status += ", " + images.Count + " img";
                Publish(page, finalUrl, page.Title, status, images);
            }
            catch (Exception ex)
            {
                if (cancelPendingFetch) return;
                string host = WebUrl.HostOf(url);
                string reason = DeepestMessage(ex);
                string name = host.Length > 0 ? host : url;
                string httpsNote = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    ? "<p style=\"color:#666;\">Note: This site uses HTTPS. The native TLS 1.2 handshake or server cipher suite negotiation may have failed.</p>"
                    : "";

                string html = @"<html><head><title>Cannot display page</title><style>
body { font-family: sans-serif; margin: 30px; background: #ffffff; color: #222; }
h1 { color: #b30000; font-size: 22px; border-bottom: 2px solid #b30000; padding-bottom: 6px; }
.box { background: #fdf7f7; border: 1px solid #eed0d0; padding: 12px; margin: 15px 0; border-radius: 4px; }
ul { margin: 15px 0; padding-left: 20px; }
li { margin: 8px 0; }
a { color: #0066cc; text-decoration: none; font-weight: bold; }
a:hover { text-decoration: underline; }
</style></head><body>
<h1>The page cannot be displayed</h1>
<div class=""box"">
<p><b>Address:</b> " + EscapeHtml(url) + @"</p>
<p><b>Reason:</b> " + EscapeHtml(reason) + @"</p>
" + httpsNote + @"
</div>
<p><b>What you can try:</b></p>
<ul>
<li><a href=""" + EscapeHtml(url) + @""">Try connecting again (Refresh)</a></li>
<li>Go to the <a href=""about:home"">Internet Explorer Start Page</a></li>
<li>Open your <a href=""about:favorites"">Favorites</a> or <a href=""about:history"">History</a></li>
<li>Browse <a href=""file:///mnt/"">Local Files (/mnt)</a></li>
</ul>
</body></html>";

                HtmlPage page = HtmlPageBuilder.Parse(html);
                page.Title = "Cannot display page";
                Publish(page, url, "Cannot display page", "Error");
            }
        }

        private string FetchString(string url)
        {
            string dummy;
            return FetchString(url, out dummy);
        }

        private string FetchString(string url, out string finalUrl)
        {
            finalUrl = url;
            string scheme = WebUrl.SchemeOf(url);
            if (scheme == "file")
            {
                string path = FileUrlToLocalPath(url);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }

            TlsHttpResponse response = NativeHttpFetchWithRedirects("GET", url, null, null, out finalUrl);
            if (response.Status < 200 || response.Status >= 300)
                throw new Exception("HTTP " + response.Status + (string.IsNullOrEmpty(response.Reason) ? "" : " " + response.Reason) + " from " + WebUrl.HostOf(finalUrl));

            return DecodeBody(response.Body, ExtractHeaderValue(response.HeaderBlock, "content-type"));
        }

        private byte[] FetchBytes(string url)
        {
            string dummy;
            return FetchBytes(url, out dummy);
        }

        private byte[] FetchBytes(string url, out string finalUrl)
        {
            finalUrl = url;
            string scheme = WebUrl.SchemeOf(url);
            if (scheme == "file")
            {
                string path = FileUrlToLocalPath(url);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }

            TlsHttpResponse response = NativeHttpFetchWithRedirects("GET", url, null, null, out finalUrl);
            if (response.Status < 200 || response.Status >= 300)
                throw new Exception("HTTP " + response.Status + (string.IsNullOrEmpty(response.Reason) ? "" : " " + response.Reason) + " from " + WebUrl.HostOf(finalUrl));

            return response.Body;
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
                TlsHttpResponse response = NativeHttpFetchWithRedirects(method, url, headers, body, out finalUrl);

                result.Status = response.Status;
                result.StatusText = response.Reason != null ? response.Reason : "";
                result.Ok = response.Status >= 200 && response.Status < 300;
                result.Url = finalUrl;
                result.Headers = response.HeaderBlock;
                result.Body = DecodeBody(response.Body, ExtractHeaderValue(response.HeaderBlock, "content-type"));
            }
            catch (Exception ex)
            {
                result.Error = DeepestMessage(ex);
            }
            return result;
        }

        /// <summary>Perform an HTTP/HTTPS request through the hand-rolled transport stack,
        /// following up to 6 redirects and updating the cookie jar per hop.</summary>
        private TlsHttpResponse NativeHttpFetchWithRedirects(string method, string url,
            string requestHeaders, string body, out string finalUrl)
        {
            finalUrl = url;
            string current = url;
            string m = string.IsNullOrEmpty(method) ? "GET" : method.ToUpperInvariant();
            string mediaType = ExtractHeaderValue(requestHeaders, "content-type");
            if (mediaType == null || mediaType.Length == 0)
                mediaType = "application/x-www-form-urlencoded";

            for (int hop = 0; hop < 6; hop++)
            {
                if (cancelPendingFetch) throw new Exception("Operation cancelled by user.");

                string scheme = WebUrl.SchemeOf(current);
                if (scheme != "http" && scheme != "https")
                    throw new Exception("Unsupported protocol: " + scheme);

                byte[] bodyBytes = null;
                if (body != null && body.Length > 0 && (m == "POST" || m == "PUT" || m == "PATCH"))
                    bodyBytes = global::System.Text.Encoding.UTF8.GetBytes(body);

                TlsHttpResponse response;
                try
                {
                    response = TlsHttpFetch.Fetch(m, current, requestHeaders, bodyBytes,
                        mediaType, cookies.HeaderFor(current));
                }
                catch (Exception ex)
                {
                    if (hop > 0)
                    {
                        try
                        {
                            response = TlsHttpFetch.Fetch(m, current, requestHeaders, bodyBytes,
                                mediaType, cookies.HeaderFor(current));
                        }
                        catch
                        {
                            throw new Exception("Network fetch failed during redirect: " + DeepestMessage(ex));
                        }
                    }
                    else
                    {
                        throw;
                    }
                }

                if (response == null) throw new Exception("No response from " + WebUrl.HostOf(current));
                StoreCookies(response, current);

                int code = response.Status;
                if (code >= 300 && code < 400)
                {
                    string location = ExtractHeaderValue(response.HeaderBlock, "location");
                    if (string.IsNullOrEmpty(location))
                        throw new Exception("HTTP " + code + " redirect with no Location header.");
                    if (code == 301 || code == 302 || code == 303)
                    {
                        if (m == "POST" || m == "PUT" || m == "PATCH")
                        {
                            m = "GET";
                            body = null;
                        }
                    }
                    current = WebUrl.Resolve(current, location);
                    continue;
                }

                finalUrl = current;
                return response;
            }
            throw new Exception("Too many redirects.");
        }

        private void StoreCookies(TlsHttpResponse response, string url)
        {
            if (response != null && response.SetCookies != null)
            {
                for (int i = 0; i < response.SetCookies.Count; i++)
                    cookies.AddFromSetCookie(response.SetCookies[i], url);
            }
        }

        /// <summary>Response body -> string. The native path always receives the
        /// body untouched (no Accept-Encoding is ever sent), so a UTF-8 decode
        /// with BOM stripping covers the modern web.</summary>
        private static string DecodeBody(byte[] bytes, string contentType)
        {
            if (bytes == null || bytes.Length == 0) return "";

            // Check if Gzip compressed (magic bytes: 0x1F, 0x8B)
            if (bytes.Length >= 2 && bytes[0] == 0x1F && bytes[1] == 0x8B)
            {
                try
                {
                    using (global::System.IO.MemoryStream ms = new global::System.IO.MemoryStream(bytes))
                    using (global::System.IO.Compression.GZipStream gz = new global::System.IO.Compression.GZipStream(ms, global::System.IO.Compression.CompressionMode.Decompress))
                    using (global::System.IO.MemoryStream outMs = new global::System.IO.MemoryStream())
                    {
                        gz.CopyTo(outMs);
                        bytes = outMs.ToArray();
                    }
                }
                catch { }
            }

            int off = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) off = 3;
            return global::System.Text.Encoding.UTF8.GetString(bytes, off, bytes.Length - off);
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

        private int FetchStylesheets(HtmlPage page, string baseUrl, out string lastError)
        {
            lastError = null;
            if (page == null) return 0;

            string baseForCss = !string.IsNullOrEmpty(page.BaseHref) ? WebUrl.Resolve(baseUrl, page.BaseHref) : baseUrl;
            int loaded = 0;

            if (page.StyleItems != null && page.StyleItems.Count > 0)
            {
                StringBuilder combined = new StringBuilder();
                int extCount = 0;
                for (int i = 0; i < page.StyleItems.Count; i++)
                {
                    if (cancelPendingFetch) break;
                    HtmlStyleItem item = page.StyleItems[i];
                    if (!item.IsExternal)
                    {
                        if (!string.IsNullOrEmpty(item.InlineCss))
                        {
                            if (combined.Length > 0) combined.Append("\n");
                            combined.Append(item.InlineCss);
                        }
                    }
                    else if (extCount < 16)
                    {
                        extCount++;
                        string abs = WebUrl.Resolve(baseForCss, item.Href);
                        try
                        {
                            PublishProgress(45 + (extCount * 18 / 16), "Loading CSS " + extCount + "…");
                            string css = FetchString(abs);
                            if (!string.IsNullOrEmpty(css))
                            {
                                if (combined.Length > 0) combined.Append("\n");
                                combined.Append(css);
                                loaded++;
                            }
                        }
                        catch (Exception ex)
                        {
                            lastError = DeepestMessage(ex);
                        }
                    }
                }
                page.CssText = combined.ToString();
                return loaded;
            }

            if (page.StyleSheetHrefs != null && page.StyleSheetHrefs.Count > 0)
            {
                int limit = Math.Min(page.StyleSheetHrefs.Count, 4);
                for (int i = 0; i < limit; i++)
                {
                    if (cancelPendingFetch) break;
                    string abs = WebUrl.Resolve(baseForCss, page.StyleSheetHrefs[i]);
                    try
                    {
                        PublishProgress(45 + (i * 18 / limit), "Loading CSS " + (i + 1) + "/" + limit + "…");
                        string css = FetchString(abs);
                        if (!string.IsNullOrEmpty(css))
                        {
                            if (page.CssText.Length > 0) page.CssText += "\n";
                            page.CssText += css;
                            loaded++;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastError = DeepestMessage(ex);
                    }
                }
            }
            return loaded;
        }

        // Fetch external <script src> sources (in document order) so the page
        // engine can run them alongside inline scripts.
        private void FetchScripts(HtmlPage page, string baseUrl)
        {
            if (page.Scripts == null || page.Scripts.Count == 0) return;
            string baseForJs = !string.IsNullOrEmpty(page.BaseHref) ? WebUrl.Resolve(baseUrl, page.BaseHref) : baseUrl;
            int fetched = 0;
            const int MaxExternalScripts = 16;
            for (int i = 0; i < page.Scripts.Count && fetched < MaxExternalScripts; i++)
            {
                if (cancelPendingFetch) break;
                HtmlScript s = page.Scripts[i];
                if (s.Src == null || s.Code != null) continue; // inline or already fetched
                string abs = WebUrl.Resolve(baseForJs, s.Src);
                try
                {
                    PublishProgress(65 + (fetched * 15 / MaxExternalScripts), "Loading script " + (fetched + 1) + "…");
                    string code = FetchString(abs);
                    if (!string.IsNullOrEmpty(code))
                    {
                        // Protect memory from massive multi-megabyte bundles
                        if (code.Length <= 524288)
                        {
                            s.Code = code;
                            fetched++;
                        }
                    }
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
            CollectCssUrls(page.CssText, srcs);

            int fetched = 0;
            int networkFetched = 0;
            const int MaxNetworkImages = 12;
            for (int i = 0; i < srcs.Count; i++)
            {
                if (cancelPendingFetch) break;
                string src = srcs[i];
                if (string.IsNullOrEmpty(src)) continue;

                if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    if (images.ContainsKey(src)) continue;
                    try
                    {
                        int comma = src.IndexOf(',');
                        if (comma > 0)
                        {
                            string meta = src.Substring(5, comma - 5);
                            byte[] data = null;
                            if (meta.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                string b64 = src.Substring(comma + 1).Trim();
                                data = Convert.FromBase64String(b64);
                            }
                            else
                            {
                                string content = src.Substring(comma + 1);
                                string decoded = WebUrl.UrlDecode(content);
                                data = Encoding.UTF8.GetBytes(decoded);
                            }

                            if (data != null && data.Length > 0)
                            {
                                string hint = meta.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0 ? "image.svg" : null;
                                Image img = ImageLoader.FromBytes(data, hint);
                                if (img != null && img.Width > 0 && img.Height > 0)
                                {
                                    images[src] = img;
                                    fetched++;
                                }
                            }
                        }
                    }
                    catch { /* invalid base64 or unsupported image data */ }
                    continue;
                }

                if (networkFetched >= MaxNetworkImages) continue;

                string abs = WebUrl.Resolve(baseForImages, src);
                if (images.ContainsKey(abs)) continue;

                try
                {
                    byte[] data = FetchBytes(abs);
                    if (data == null || data.Length == 0) continue;
                    Image img = ImageLoader.FromBytes(data, abs);
                    if (img != null && img.Width > 0 && img.Height > 0)
                    {
                        images[abs] = img;
                        images[src] = img;
                        fetched++;
                        networkFetched++;
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
                    if (n.Tag == "img" || n.Tag == "source")
                    {
                        string s = n.GetAttr("src");
                        if (!string.IsNullOrEmpty(s) && !srcs.Contains(s)) srcs.Add(s);
                        string dataSrc = n.GetAttr("data-src");
                        if (!string.IsNullOrEmpty(dataSrc) && !srcs.Contains(dataSrc)) srcs.Add(dataSrc);
                        string srcset = n.GetAttr("srcset");
                        if (!string.IsNullOrEmpty(srcset))
                        {
                            string first = srcset.Trim();
                            int sp = first.IndexOf(' ');
                            int comma = first.IndexOf(',');
                            int cut = (sp > 0 && (comma < 0 || sp < comma)) ? sp : comma;
                            if (cut > 0) first = first.Substring(0, cut).Trim();
                            if (!string.IsNullOrEmpty(first) && !srcs.Contains(first)) srcs.Add(first);
                        }
                    }
                    else
                    {
                        string style = n.GetAttr("style");
                        if (!string.IsNullOrEmpty(style) && style.IndexOf("url(", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            CollectCssUrls(style, srcs);
                        }
                    }
                    CollectImgSrcs(n, srcs);
                }
                n = n.Next;
            }
        }

        private static void CollectCssUrls(string css, List<string> srcs)
        {
            if (string.IsNullOrEmpty(css)) return;
            int pos = 0;
            while (pos < css.Length)
            {
                int u = css.IndexOf("url(", pos, StringComparison.OrdinalIgnoreCase);
                if (u < 0) break;
                int start = u + 4;
                int end = css.IndexOf(')', start);
                if (end < 0) break;
                string raw = css.Substring(start, end - start).Trim().Trim('\'', '"');
                pos = end + 1;
                if (string.IsNullOrEmpty(raw)) continue;
                if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                string lower = raw.ToLowerInvariant();
                int q = lower.IndexOf('?');
                if (q >= 0) lower = lower.Substring(0, q);
                int h = lower.IndexOf('#');
                if (h >= 0) lower = lower.Substring(0, h);
                if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") ||
                    lower.EndsWith(".webp") || lower.EndsWith(".gif") || lower.EndsWith(".bmp") ||
                    lower.EndsWith(".ico") || lower.EndsWith(".svg"))
                {
                    if (!srcs.Contains(raw)) srcs.Add(raw);
                }
            }
        }

        private static string EscapeHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
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
            string html = @"<html><head><title>" + EscapeHtml(title) + @"</title><style>
body { font-family: sans-serif; margin: 30px; background: #ffffff; color: #222; }
h1 { color: #b30000; font-size: 22px; border-bottom: 2px solid #b30000; padding-bottom: 6px; }
.box { background: #fdf7f7; border: 1px solid #eed0d0; padding: 12px; margin: 15px 0; border-radius: 4px; }
ul { margin: 15px 0; padding-left: 20px; }
li { margin: 8px 0; }
a { color: #0066cc; text-decoration: none; font-weight: bold; }
a:hover { text-decoration: underline; }
</style></head><body>
<h1>" + EscapeHtml(title) + @"</h1>
<div class=""box"">
<p><b>Address:</b> " + EscapeHtml(url) + @"</p>
<p><b>Message:</b> " + EscapeHtml(message) + @"</p>
</div>
<p><b>What you can try:</b></p>
<ul>
<li>Go to the <a href=""about:home"">Internet Explorer Start Page</a></li>
<li>Open your <a href=""about:favorites"">Favorites</a> or <a href=""about:history"">History</a></li>
<li>Browse <a href=""file:///mnt/"">Local Files (/mnt)</a></li>
</ul>
</body></html>";
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
