using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// YouTube SPA Hydrator for Windose Internet Explorer.
/// YouTube normally serves an empty skeleton shell (masthead-skeleton and home-page-skeleton)
/// with video data embedded in JSON (ytInitialData) or loaded dynamically via Polymer.
/// This hydrator extracts video cards, watch details, search results and related suggestions
/// and transforms the skeleton into a rich, modern, responsive YouTube interface.
/// </summary>
public static class YouTubeHydrator
{
    public struct VideoItem
    {
        public string Id;
        public string Title;
        public string Channel;
        public string ThumbnailUrl;
        public string Duration;
        public string Views;
        public string PublishedTime;
        public string Description;
    }

    public static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        string host = WebUrl.HostOf(url);
        if (string.IsNullOrEmpty(host)) return false;
        host = host.ToLowerInvariant();
        return host == "youtube.com" || host.EndsWith(".youtube.com") || host == "youtu.be";
    }

    public static string Hydrate(string rawHtml, string url, Func<string, string> fetcher)
    {
        if (string.IsNullOrEmpty(rawHtml) || !IsYouTubeUrl(url)) return rawHtml;

        try
        {
            string lowerUrl = url.ToLowerInvariant();
            if (lowerUrl.Contains("/watch") || lowerUrl.Contains("youtu.be/"))
            {
                return HydrateWatchPage(rawHtml, url);
            }
            if (lowerUrl.Contains("/results"))
            {
                return HydrateSearchPage(rawHtml, url);
            }
            return HydrateHomePage(rawHtml, url, fetcher);
        }
        catch
        {
            // If hydration fails for any reason, return the original HTML safely
            return rawHtml;
        }
    }

    private static string HydrateHomePage(string html, string url, Func<string, string> fetcher)
    {
        List<VideoItem> videos = ExtractVideos(html, 30);
        if (videos.Count == 0)
        {
            videos = ExtractLockupVideos(html, 30);
        }

        // If YouTube served an empty home feed (because watch history is turned off for clean sessions),
        // fetch trending videos so the home page has real content.
        if (videos.Count == 0 && fetcher != null)
        {
            try
            {
                string trendHtml = fetcher("https://www.youtube.com/results?search_query=trending");
                if (!string.IsNullOrEmpty(trendHtml))
                {
                    videos = ExtractVideos(trendHtml, 30);
                }
            }
            catch { }
        }

        StringBuilder sb = new StringBuilder(html.Length > 0 ? html.Length : 8192);
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>YouTube</title>");
        AppendSharedStyles(sb);
        sb.Append("</head><body>");

        AppendMasthead(sb, "");

        // Category filter chips
        sb.Append("<div class=\"chips-bar\">");
        sb.Append("<a href=\"https://www.youtube.com/\" class=\"chip chip-active\">All</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=trending\" class=\"chip\">Trending</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=music\" class=\"chip\">Music</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=gaming\" class=\"chip\">Gaming</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=news\" class=\"chip\">News</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=podcasts\" class=\"chip\">Podcasts</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=technology\" class=\"chip\">Technology</a>");
        sb.Append("</div>");

        // Video Grid
        sb.Append("<div class=\"content-area\"><div class=\"video-grid\">");
        for (int i = 0; i < videos.Count; i++)
        {
            AppendVideoCard(sb, videos[i]);
        }
        if (videos.Count == 0)
        {
            sb.Append("<div class=\"empty-msg\">No videos found. Use the search bar above to explore YouTube.</div>");
        }
        sb.Append("</div></div>");

        AppendFooter(sb);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string HydrateSearchPage(string html, string url)
    {
        string query = ExtractQueryParam(url, "search_query");
        List<VideoItem> videos = ExtractVideos(html, 30);
        if (videos.Count == 0) videos = ExtractLockupVideos(html, 30);

        StringBuilder sb = new StringBuilder(html.Length > 0 ? html.Length : 8192);
        string displayQuery = EscapeHtml(query);
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + (string.IsNullOrEmpty(displayQuery) ? "YouTube Search" : displayQuery + " - YouTube") + "</title>");
        AppendSharedStyles(sb);
        sb.Append("</head><body>");

        AppendMasthead(sb, query);

        // Filter chips
        sb.Append("<div class=\"chips-bar\">");
        sb.Append("<span class=\"chip chip-active\">Results for \"" + displayQuery + "\"</span>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=" + UriEscape(query) + "+today\" class=\"chip\">Today</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=" + UriEscape(query) + "+channel\" class=\"chip\">Channels</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=" + UriEscape(query) + "+playlist\" class=\"chip\">Playlists</a>");
        sb.Append("</div>");

        // Search List
        sb.Append("<div class=\"content-area\"><div class=\"search-list\">");
        for (int i = 0; i < videos.Count; i++)
        {
            AppendSearchResultRow(sb, videos[i]);
        }
        if (videos.Count == 0)
        {
            sb.Append("<div class=\"empty-msg\">No results found for \"" + displayQuery + "\".</div>");
        }
        sb.Append("</div></div>");

        AppendFooter(sb);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string HydrateWatchPage(string html, string url)
    {
        string videoId = ExtractQueryParam(url, "v");
        if (string.IsNullOrEmpty(videoId) && url.Contains("youtu.be/"))
        {
            int sl = url.IndexOf("youtu.be/") + 9;
            int q = url.IndexOf('?', sl);
            videoId = q >= 0 ? url.Substring(sl, q - sl) : url.Substring(sl);
        }

        // Title
        string title = ExtractMeta(html, "og:title");
        if (string.IsNullOrEmpty(title))
        {
            int t1 = html.IndexOf("<title>");
            if (t1 >= 0)
            {
                int t2 = html.IndexOf("</title>", t1);
                if (t2 > 0)
                {
                    title = html.Substring(t1 + 7, t2 - (t1 + 7)).Trim();
                    if (title.EndsWith(" - YouTube", StringComparison.OrdinalIgnoreCase))
                        title = title.Substring(0, title.Length - 10).Trim();
                }
            }
        }
        if (string.IsNullOrEmpty(title)) title = "YouTube Video";

        // Channel & description
        string channel = ExtractJsonString(html, 0, "ownerText", "shortBylineText");
        if (string.IsNullOrEmpty(channel)) channel = ExtractMeta(html, "author");
        if (string.IsNullOrEmpty(channel)) channel = "YouTube Creator";

        string description = ExtractMeta(html, "og:description");
        if (string.IsNullOrEmpty(description)) description = ExtractMeta(html, "description");

        string views = ExtractJsonString(html, 0, "viewCount", "simpleText");
        if (string.IsNullOrEmpty(views) || views.IndexOf("view", StringComparison.OrdinalIgnoreCase) < 0)
            views = "Popular Video";

        List<VideoItem> related = ExtractLockupVideos(html, 20);
        if (related.Count == 0) related = ExtractVideos(html, 20);

        StringBuilder sb = new StringBuilder(html.Length > 0 ? html.Length : 8192);
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + EscapeHtml(title) + " - YouTube</title>");
        AppendSharedStyles(sb);
        sb.Append("</head><body>");

        AppendMasthead(sb, "");

        sb.Append("<div class=\"content-area\"><div class=\"watch-layout\">");

        // Primary watch column
        sb.Append("<div class=\"watch-primary\">");
        sb.Append("<div class=\"player-container\">");
        string thumbUrl = "https://i.ytimg.com/vi/" + videoId + "/hqdefault.jpg";
        sb.Append("<a href=\"media:https://www.youtube.com/watch?v=" + videoId + "\">");
        sb.Append("<img class=\"player-poster\" src=\"" + thumbUrl + "\" alt=\"Video Player\">");
        sb.Append("<div class=\"play-btn-overlay\">&#9658;</div>");
        sb.Append("</a>");
        sb.Append("<div class=\"player-controls-bar\"><a class=\"wmp-play-btn\" href=\"media:https://www.youtube.com/watch?v=" + videoId + "\">&#9658; Play in Windows Media Player</a></div>");
        sb.Append("</div>");

        sb.Append("<h1 class=\"watch-title\">" + EscapeHtml(title) + "</h1>");

        sb.Append("<div class=\"watch-info-bar\">");
        sb.Append("<div class=\"channel-box\">");
        sb.Append("<div class=\"channel-avatar-circle\">" + (channel.Length > 0 ? channel.Substring(0, 1).ToUpperInvariant() : "Y") + "</div>");
        sb.Append("<div class=\"channel-name\">" + EscapeHtml(channel) + "</div>");
        sb.Append("<span class=\"subscribe-btn\">Subscribe</span>");
        sb.Append("</div>");
        sb.Append("<div class=\"watch-stats\">" + EscapeHtml(views) + "</div>");
        sb.Append("</div>");

        if (!string.IsNullOrEmpty(description))
        {
            sb.Append("<div class=\"watch-desc-box\">");
            sb.Append("<p>" + EscapeHtml(description).Replace("\n", "<br>") + "</p>");
            sb.Append("</div>");
        }
        sb.Append("</div>");

        // Secondary / Related column
        sb.Append("<div class=\"watch-secondary\">");
        sb.Append("<div class=\"up-next-header\">Up Next</div>");
        for (int i = 0; i < related.Count; i++)
        {
            if (related[i].Id == videoId) continue; // skip current video
            AppendRelatedVideoCard(sb, related[i]);
        }
        sb.Append("</div>");

        sb.Append("</div></div>");

        AppendFooter(sb);
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendMasthead(StringBuilder sb, string query)
    {
        sb.Append("<div class=\"yt-masthead\">");
        sb.Append("<div class=\"masthead-left\">");
        sb.Append("<a href=\"https://www.youtube.com/\" class=\"yt-logo-link\">");
        sb.Append("<span class=\"yt-icon\">&#9654;</span><span class=\"yt-logo-text\">YouTube</span>");
        sb.Append("</a>");
        sb.Append("</div>");

        sb.Append("<div class=\"masthead-center\">");
        sb.Append("<form action=\"https://www.youtube.com/results\" method=\"get\" class=\"search-form\">");
        sb.Append("<input type=\"text\" name=\"search_query\" class=\"search-input\" placeholder=\"Search YouTube...\" value=\"" + EscapeHtml(query) + "\">");
        sb.Append("<input type=\"submit\" value=\"Search\" class=\"search-submit\">");
        sb.Append("</form>");
        sb.Append("</div>");

        sb.Append("<div class=\"masthead-right\">");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=trending\" class=\"nav-link\">Trending</a>");
        sb.Append("<a href=\"https://www.youtube.com/results?search_query=music\" class=\"nav-link\">Music</a>");
        sb.Append("<a href=\"about:home\" class=\"nav-link\">Home</a>");
        sb.Append("</div>");
        sb.Append("</div>");
    }

    private static void AppendVideoCard(StringBuilder sb, VideoItem v)
    {
        string watchUrl = "https://www.youtube.com/watch?v=" + v.Id;
        sb.Append("<div class=\"video-card\">");
        sb.Append("<div class=\"thumb-wrapper\">");
        sb.Append("<a href=\"" + watchUrl + "\">");
        sb.Append("<img class=\"video-thumb\" src=\"" + v.ThumbnailUrl + "\" alt=\"thumbnail\">");
        if (!string.IsNullOrEmpty(v.Duration))
        {
            sb.Append("<span class=\"duration-badge\">" + EscapeHtml(v.Duration) + "</span>");
        }
        sb.Append("</a>");
        sb.Append("</div>");

        sb.Append("<div class=\"video-meta\">");
        sb.Append("<a href=\"" + watchUrl + "\" class=\"video-title\">" + EscapeHtml(v.Title) + "</a>");
        sb.Append("<div class=\"video-channel\">" + EscapeHtml(v.Channel) + "</div>");
        string meta = "";
        if (!string.IsNullOrEmpty(v.Views)) meta += v.Views;
        if (!string.IsNullOrEmpty(v.PublishedTime))
        {
            if (meta.Length > 0) meta += " &bull; ";
            meta += v.PublishedTime;
        }
        if (meta.Length > 0) sb.Append("<div class=\"video-subtext\">" + meta + "</div>");
        sb.Append("</div>");
        sb.Append("</div>");
    }

    private static void AppendSearchResultRow(StringBuilder sb, VideoItem v)
    {
        string watchUrl = "https://www.youtube.com/watch?v=" + v.Id;
        sb.Append("<div class=\"search-row\">");
        sb.Append("<div class=\"search-thumb-box\">");
        sb.Append("<a href=\"" + watchUrl + "\">");
        sb.Append("<img class=\"search-thumb\" src=\"" + v.ThumbnailUrl + "\" alt=\"thumbnail\">");
        if (!string.IsNullOrEmpty(v.Duration))
        {
            sb.Append("<span class=\"duration-badge\">" + EscapeHtml(v.Duration) + "</span>");
        }
        sb.Append("</a>");
        sb.Append("</div>");

        sb.Append("<div class=\"search-details\">");
        sb.Append("<a href=\"" + watchUrl + "\" class=\"search-title\">" + EscapeHtml(v.Title) + "</a>");
        string sub = "";
        if (!string.IsNullOrEmpty(v.Channel)) sub += v.Channel;
        if (!string.IsNullOrEmpty(v.Views))
        {
            if (sub.Length > 0) sub += " &bull; ";
            sub += v.Views;
        }
        if (!string.IsNullOrEmpty(v.PublishedTime))
        {
            if (sub.Length > 0) sub += " &bull; ";
            sub += v.PublishedTime;
        }
        if (sub.Length > 0) sb.Append("<div class=\"search-meta\">" + sub + "</div>");
        if (!string.IsNullOrEmpty(v.Description))
        {
            sb.Append("<div class=\"search-snippet\">" + EscapeHtml(v.Description) + "</div>");
        }
        sb.Append("</div>");
        sb.Append("</div>");
    }

    private static void AppendRelatedVideoCard(StringBuilder sb, VideoItem v)
    {
        string watchUrl = "https://www.youtube.com/watch?v=" + v.Id;
        sb.Append("<div class=\"related-card\">");
        sb.Append("<div class=\"related-thumb-box\">");
        sb.Append("<a href=\"" + watchUrl + "\">");
        sb.Append("<img class=\"related-thumb\" src=\"" + v.ThumbnailUrl + "\" alt=\"thumbnail\">");
        if (!string.IsNullOrEmpty(v.Duration))
        {
            sb.Append("<span class=\"duration-badge small-badge\">" + EscapeHtml(v.Duration) + "</span>");
        }
        sb.Append("</a>");
        sb.Append("</div>");

        sb.Append("<div class=\"related-details\">");
        sb.Append("<a href=\"" + watchUrl + "\" class=\"related-title\">" + EscapeHtml(v.Title) + "</a>");
        if (!string.IsNullOrEmpty(v.Channel))
        {
            sb.Append("<div class=\"related-channel\">" + EscapeHtml(v.Channel) + "</div>");
        }
        string sub = "";
        if (!string.IsNullOrEmpty(v.Views)) sub += v.Views;
        if (!string.IsNullOrEmpty(v.PublishedTime))
        {
            if (sub.Length > 0) sub += " &bull; ";
            sub += v.PublishedTime;
        }
        if (sub.Length > 0) sb.Append("<div class=\"related-subtext\">" + sub + "</div>");
        sb.Append("</div>");
        sb.Append("</div>");
    }

    private static void AppendFooter(StringBuilder sb)
    {
        sb.Append("<div class=\"yt-footer\">");
        sb.Append("<p>&copy; 2026 YouTube, LLC &bull; Windose Web Engine &bull; Native Fast Hydration</p>");
        sb.Append("</div>");
    }

    private static void AppendSharedStyles(StringBuilder sb)
    {
        sb.Append("<style>");
        sb.Append("body { margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background: #0f0f0f; color: #f1f1f1; }");
        sb.Append("a { text-decoration: none; color: inherit; }");
        sb.Append(".yt-masthead { display: flex; align-items: center; justify-content: space-between; padding: 8px 16px; background: #0f0f0f; border-bottom: 1px solid #272727; position: sticky; top: 0; z-index: 100; }");
        sb.Append(".masthead-left { display: flex; align-items: center; }");
        sb.Append(".yt-logo-link { display: flex; align-items: center; gap: 6px; font-weight: bold; font-size: 18px; color: #fff; letter-spacing: -0.5px; }");
        sb.Append(".yt-icon { background: #ff0000; color: #fff; padding: 2px 7px; border-radius: 6px; font-size: 13px; font-weight: bold; }");
        sb.Append(".yt-logo-text { font-family: 'Oswald', sans-serif, Impact; font-weight: 700; letter-spacing: -0.8px; font-size: 20px; }");
        sb.Append(".masthead-center { flex: 1; max-width: 500px; margin: 0 20px; }");
        sb.Append(".search-form { display: flex; width: 100%; margin: 0; }");
        sb.Append(".search-input { flex: 1; padding: 7px 12px; background: #121212; border: 1px solid #303030; border-radius: 20px 0 0 20px; color: #fff; font-size: 14px; outline: none; }");
        sb.Append(".search-input:focus { border-color: #1c62b9; }");
        sb.Append(".search-submit { padding: 7px 16px; background: #222222; border: 1px solid #303030; border-left: none; border-radius: 0 20px 20px 0; color: #fff; cursor: pointer; font-size: 13px; font-weight: 500; }");
        sb.Append(".search-submit:hover { background: #272727; }");
        sb.Append(".masthead-right { display: flex; align-items: center; gap: 14px; }");
        sb.Append(".nav-link { color: #aaa; font-size: 13px; font-weight: 500; }");
        sb.Append(".nav-link:hover { color: #fff; }");
        sb.Append(".chips-bar { display: flex; gap: 8px; padding: 10px 16px; background: #0f0f0f; overflow-x: auto; border-bottom: 1px solid #222; }");
        sb.Append(".chip { display: inline-block; padding: 5px 12px; background: #272727; border-radius: 8px; font-size: 13px; font-weight: 500; color: #f1f1f1; white-space: nowrap; }");
        sb.Append(".chip:hover { background: #3f3f3f; }");
        sb.Append(".chip-active { background: #f1f1f1; color: #0f0f0f; }");
        sb.Append(".chip-active:hover { background: #fff; }");
        sb.Append(".content-area { padding: 16px; max-width: 1280px; margin: 0 auto; }");
        sb.Append(".video-grid { display: flex; flex-wrap: wrap; gap: 16px; }");
        sb.Append(".video-card { width: 230px; margin-bottom: 12px; }");
        sb.Append(".thumb-wrapper { position: relative; width: 100%; border-radius: 8px; overflow: hidden; background: #222; aspect-ratio: 16 / 9; }");
        sb.Append(".video-thumb { width: 100%; height: 130px; display: block; object-fit: cover; }");
        sb.Append(".duration-badge { position: absolute; bottom: 6px; right: 6px; background: rgba(0,0,0,0.8); color: #fff; font-size: 11px; font-weight: bold; padding: 1px 4px; border-radius: 4px; }");
        sb.Append(".small-badge { bottom: 4px; right: 4px; font-size: 10px; padding: 1px 3px; }");
        sb.Append(".video-meta { padding-top: 8px; }");
        sb.Append(".video-title { display: block; font-size: 14px; font-weight: 600; line-height: 1.3; color: #f1f1f1; margin-bottom: 4px; max-height: 2.6em; overflow: hidden; }");
        sb.Append(".video-title:hover { color: #fff; }");
        sb.Append(".video-channel { font-size: 12px; color: #aaa; margin-bottom: 2px; }");
        sb.Append(".video-subtext { font-size: 12px; color: #aaa; }");
        sb.Append(".search-list { display: flex; flex-direction: column; gap: 16px; }");
        sb.Append(".search-row { display: flex; gap: 16px; }");
        sb.Append(".search-thumb-box { position: relative; width: 200px; flex-shrink: 0; border-radius: 8px; overflow: hidden; background: #222; }");
        sb.Append(".search-thumb { width: 200px; height: 112px; display: block; object-fit: cover; }");
        sb.Append(".search-details { flex: 1; }");
        sb.Append(".search-title { font-size: 16px; font-weight: 600; color: #f1f1f1; display: block; margin-bottom: 6px; }");
        sb.Append(".search-meta { font-size: 12px; color: #aaa; margin-bottom: 8px; }");
        sb.Append(".search-snippet { font-size: 12px; color: #888; line-height: 1.4; max-height: 2.8em; overflow: hidden; }");
        sb.Append(".watch-layout { display: flex; gap: 24px; }");
        sb.Append(".watch-primary { flex: 2; }");
        sb.Append(".player-container { position: relative; width: 100%; background: #000; border-radius: 12px; overflow: hidden; }");
                sb.Append(".player-controls-bar { background: #1a1a1a; padding: 10px 16px; border-radius: 0 0 8px 8px; border-top: 1px solid #333; }");
        sb.Append(".wmp-play-btn { display: inline-block; background: #0078d7; color: #fff; font-weight: 600; padding: 8px 18px; border-radius: 6px; text-decoration: none; font-size: 13px; }");
        sb.Append(".wmp-play-btn:hover { background: #1084e3; }");
        sb.Append(".player-poster { width: 100%; height: 360px; display: block; object-fit: cover; }");
        sb.Append(".play-btn-overlay { position: absolute; top: 50%; left: 50%; transform: translate(-50%, -50%); width: 68px; height: 48px; background: rgba(255,0,0,0.85); color: #fff; border-radius: 12px; display: flex; align-items: center; justify-content: center; font-size: 24px; box-shadow: 0 4px 15px rgba(0,0,0,0.5); }");
        sb.Append(".watch-title { font-size: 18px; font-weight: 700; margin: 12px 0 8px 0; line-height: 1.3; }");
        sb.Append(".watch-info-bar { display: flex; justify-content: space-between; align-items: center; padding-bottom: 12px; border-bottom: 1px solid #222; margin-bottom: 12px; }");
        sb.Append(".channel-box { display: flex; align-items: center; gap: 10px; }");
        sb.Append(".channel-avatar-circle { width: 36px; height: 36px; border-radius: 50%; background: #555; color: #fff; display: flex; align-items: center; justify-content: center; font-weight: bold; font-size: 15px; }");
        sb.Append(".channel-name { font-size: 14px; font-weight: 600; color: #fff; }");
        sb.Append(".subscribe-btn { display: inline-block; padding: 6px 14px; background: #fff; color: #000; border-radius: 18px; font-size: 12px; font-weight: 600; cursor: pointer; }");
        sb.Append(".watch-stats { font-size: 13px; color: #aaa; }");
        sb.Append(".watch-desc-box { background: #222; padding: 12px; border-radius: 8px; font-size: 13px; color: #ddd; line-height: 1.5; }");
        sb.Append(".watch-secondary { flex: 1; min-width: 280px; }");
        sb.Append(".up-next-header { font-size: 15px; font-weight: 700; margin-bottom: 12px; }");
        sb.Append(".related-card { display: flex; gap: 8px; margin-bottom: 10px; }");
        sb.Append(".related-thumb-box { position: relative; width: 120px; height: 68px; flex-shrink: 0; border-radius: 6px; overflow: hidden; background: #222; }");
        sb.Append(".related-thumb { width: 120px; height: 68px; display: block; object-fit: cover; }");
        sb.Append(".related-details { flex: 1; }");
        sb.Append(".related-title { font-size: 13px; font-weight: 600; line-height: 1.2; display: block; color: #f1f1f1; margin-bottom: 3px; max-height: 2.4em; overflow: hidden; }");
        sb.Append(".related-channel { font-size: 11px; color: #aaa; }");
        sb.Append(".related-subtext { font-size: 11px; color: #888; }");
        sb.Append(".empty-msg { padding: 40px; text-align: center; color: #888; font-size: 15px; }");
        sb.Append(".yt-footer { border-top: 1px solid #222; margin-top: 30px; padding: 20px 16px; text-align: center; color: #666; font-size: 12px; }");
        sb.Append("</style>");
    }

    public static List<VideoItem> ExtractVideos(string html, int max)
    {
        List<VideoItem> list = new List<VideoItem>();
        if (string.IsNullOrEmpty(html)) return list;
        int idx = 0;
        while ((idx = html.IndexOf("\"videoRenderer\":{", idx)) >= 0 && list.Count < max)
        {
            VideoItem v = new VideoItem();
            int idIdx = html.IndexOf("\"videoId\":\"", idx);
            if (idIdx >= 0 && idIdx < idx + 400)
            {
                int endId = html.IndexOf('"', idIdx + 11);
                if (endId > 0) v.Id = html.Substring(idIdx + 11, endId - (idIdx + 11));
            }

            if (string.IsNullOrEmpty(v.Id)) { idx += 17; continue; }

            int titleIdx = html.IndexOf("\"title\":{", idx);
            if (titleIdx >= 0 && titleIdx < idx + 1200)
            {
                v.Title = ExtractJsonString(html, titleIdx, "text", "simpleText");
            }

            int ownerIdx = html.IndexOf("\"ownerText\":{", idx);
            if (ownerIdx < 0 || ownerIdx > idx + 2500) ownerIdx = html.IndexOf("\"shortBylineText\":{", idx);
            if (ownerIdx < 0 || ownerIdx > idx + 2500) ownerIdx = html.IndexOf("\"longBylineText\":{", idx);
            if (ownerIdx >= 0 && ownerIdx < idx + 2500)
            {
                v.Channel = ExtractJsonString(html, ownerIdx, "text", "simpleText");
            }

            int durIdx = html.IndexOf("\"lengthText\":{", idx);
            if (durIdx >= 0 && durIdx < idx + 3000)
            {
                v.Duration = ExtractJsonString(html, durIdx, "simpleText", "text");
            }

            int viewIdx = html.IndexOf("\"viewCountText\":{", idx);
            if (viewIdx >= 0 && viewIdx < idx + 3500)
            {
                v.Views = ExtractJsonString(html, viewIdx, "simpleText", "text");
            }

            int pubIdx = html.IndexOf("\"publishedTimeText\":{", idx);
            if (pubIdx >= 0 && pubIdx < idx + 3500)
            {
                v.PublishedTime = ExtractJsonString(html, pubIdx, "simpleText", "text");
            }

            int descIdx = html.IndexOf("\"detailedMetadataSnippets\":", idx);
            if (descIdx < 0 || descIdx > idx + 4500) descIdx = html.IndexOf("\"descriptionSnippet\":{", idx);
            if (descIdx >= 0 && descIdx < idx + 4500)
            {
                v.Description = ExtractJsonString(html, descIdx, "text", "simpleText");
            }

            v.ThumbnailUrl = "https://i.ytimg.com/vi/" + v.Id + "/mqdefault.jpg";
            list.Add(v);
            idx += 17;
        }
        return list;
    }

    public static List<VideoItem> ExtractLockupVideos(string html, int max)
    {
        List<VideoItem> list = new List<VideoItem>();
        if (string.IsNullOrEmpty(html)) return list;
        int idx = 0;
        while ((idx = html.IndexOf("\"lockupViewModel\":{", idx)) >= 0 && list.Count < max)
        {
            VideoItem v = new VideoItem();
            int idIdx = html.IndexOf("\"animationActivationTargetId\":\"", idx);
            if (idIdx >= 0 && idIdx < idx + 1500)
            {
                int endId = html.IndexOf('"', idIdx + 31);
                if (endId > 0) v.Id = html.Substring(idIdx + 31, endId - (idIdx + 31));
            }
            if (string.IsNullOrEmpty(v.Id))
            {
                int viIdx = html.IndexOf("/vi/", idx);
                if (viIdx >= 0 && viIdx < idx + 1500)
                {
                    int endVi = html.IndexOf('/', viIdx + 4);
                    if (endVi > 0) v.Id = html.Substring(viIdx + 4, endVi - (viIdx + 4));
                }
            }

            if (string.IsNullOrEmpty(v.Id)) { idx += 19; continue; }

            int titleIdx = html.IndexOf("\"lockupMetadataViewModel\":{", idx);
            if (titleIdx >= 0 && titleIdx < idx + 3000)
            {
                int cIdx = html.IndexOf("\"content\":\"", titleIdx);
                if (cIdx >= 0 && cIdx < titleIdx + 300)
                {
                    int endC = html.IndexOf('"', cIdx + 11);
                    if (endC > 0) v.Title = html.Substring(cIdx + 11, endC - (cIdx + 11));
                }
            }

            int durIdx = html.IndexOf("\"thumbnailBadgeViewModel\":{", idx);
            if (durIdx >= 0 && durIdx < idx + 2000)
            {
                int tIdx = html.IndexOf("\"text\":\"", durIdx);
                if (tIdx >= 0 && tIdx < durIdx + 200)
                {
                    int endT = html.IndexOf('"', tIdx + 8);
                    if (endT > 0) v.Duration = html.Substring(tIdx + 8, endT - (tIdx + 8));
                }
            }

            int chanIdx = html.IndexOf("\"a11yLabel\":\"", idx);
            if (chanIdx >= 0 && chanIdx < idx + 3500)
            {
                int endCh = html.IndexOf('"', chanIdx + 13);
                if (endCh > 0)
                {
                    string ch = html.Substring(chanIdx + 13, endCh - (chanIdx + 13));
                    if (ch.StartsWith("Go to channel ")) ch = ch.Substring(14);
                    v.Channel = ch;
                }
            }

            v.ThumbnailUrl = "https://i.ytimg.com/vi/" + v.Id + "/mqdefault.jpg";
            list.Add(v);
            idx += 19;
        }
        return list;
    }

    private static string ExtractMeta(string html, string prop)
    {
        if (string.IsNullOrEmpty(html)) return "";
        string tag1 = "property=\"" + prop + "\" content=\"";
        int i1 = html.IndexOf(tag1, StringComparison.OrdinalIgnoreCase);
        if (i1 < 0)
        {
            string tag2 = "name=\"" + prop + "\" content=\"";
            i1 = html.IndexOf(tag2, StringComparison.OrdinalIgnoreCase);
            if (i1 >= 0) i1 += tag2.Length;
        }
        else
        {
            i1 += tag1.Length;
        }

        if (i1 >= 0)
        {
            int end = html.IndexOf('"', i1);
            if (end > 0) return html.Substring(i1, end - i1);
        }
        return "";
    }

    private static string ExtractQueryParam(string url, string name)
    {
        if (string.IsNullOrEmpty(url)) return "";
        int q = url.IndexOf('?');
        if (q < 0) return "";
        string query = url.Substring(q + 1);
        string[] parts = query.Split('&');
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            int eq = p.IndexOf('=');
            if (eq > 0)
            {
                string key = p.Substring(0, eq).Trim();
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    string val = p.Substring(eq + 1).Trim();
                    return UriUnescape(val);
                }
            }
        }
        return "";
    }

    private static string ExtractJsonString(string s, int start, string prop1, string prop2)
    {
        int p1 = s.IndexOf("\"" + prop1 + "\":\"", start);
        int p2 = s.IndexOf("\"" + prop2 + "\":\"", start);
        int target = -1;
        int propLen = 0;
        if (p1 >= 0 && (p2 < 0 || p1 < p2)) { target = p1; propLen = prop1.Length + 4; }
        else if (p2 >= 0) { target = p2; propLen = prop2.Length + 4; }
        if (target >= 0 && target < start + 2000)
        {
            int end = s.IndexOf('"', target + propLen);
            if (end > 0) return UnescapeJson(s.Substring(target + propLen, end - (target + propLen)));
        }
        return "";
    }

    private static string UnescapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("\\u0026", "&");
    }

    private static string EscapeHtml(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    private static string UriUnescape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("+", " ").Replace("%20", " ");
    }

    private static string UriEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace(" ", "+").Replace("&", "%26");
    }
}
