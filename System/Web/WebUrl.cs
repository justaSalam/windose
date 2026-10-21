using System;
using System.Collections.Generic;

/// <summary>Minimal URL normalisation / relative-resolution for the browser.</summary>
public static class WebUrl
{
    /// <summary>Turn user input into a navigable absolute URL, or null if empty.</summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input)) return null;
        string s = input.Trim();
        if (s.Length == 0) return null;

        string lower = s.ToLowerInvariant();
        if (lower.StartsWith("about:") || lower.StartsWith("file:") || lower.StartsWith("data:") || lower.StartsWith("javascript:") || lower.StartsWith("mailto:")) return s;
        if (s.IndexOf("://") >= 0) return s;
        if (s.StartsWith("//")) return "http:" + s;

        // Local file system paths
        if (s.StartsWith("/") || (s.Length >= 2 && s[1] == ':'))
        {
            return "file:///" + s.Replace('\\', '/').TrimStart('/');
        }

        // Search query if explicit search prefix '?' or contains whitespace or no dots and not localhost
        if (s.StartsWith("?"))
        {
            return "https://html.duckduckgo.com/html/?q=" + UriEscape(s.Substring(1).Trim());
        }
        if (s.IndexOf(' ') >= 0 || (!s.Contains(".") && !lower.StartsWith("localhost") && !s.Contains(":")))
        {
            return "https://html.duckduckgo.com/html/?q=" + UriEscape(s);
        }

        return "http://" + s;
    }

    private static string UriEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        string outp = "";
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || c == '-' || c == '_' || c == '.' || c == '~')
                outp += c;
            else if (c == ' ') outp += "+";
            else
            {
                outp += "%" + ((int)c).ToString("X2");
            }
        }
        return outp;
    }

    /// <summary>Resolve a (possibly relative) href against the current page URL.</summary>
    public static string Resolve(string baseHref, string href)
    {
        if (string.IsNullOrEmpty(href)) return baseHref;
        string h = href.Trim();
        if (h.Length == 0) return baseHref;

        string lower = h.ToLowerInvariant();
        if (lower.StartsWith("about:") || lower.StartsWith("data:") || lower.StartsWith("javascript:") || lower.StartsWith("mailto:") || lower.StartsWith("file:") || h.IndexOf("://") >= 0) return h;
        if (h.StartsWith("//"))
        {
            string scheme = SchemeOf(baseHref);
            if (scheme.Length == 0) scheme = "http";
            return scheme + ":" + h;
        }
        if (h.StartsWith("#"))
        {
            int hashIdx = baseHref.IndexOf('#');
            string baseNoHash = hashIdx >= 0 ? baseHref.Substring(0, hashIdx) : baseHref;
            return baseNoHash + h;
        }
        if (h.StartsWith("?"))
        {
            int qIdx = baseHref.IndexOf('?');
            int hashIdx = baseHref.IndexOf('#');
            int cut = baseHref.Length;
            if (qIdx >= 0) cut = qIdx;
            else if (hashIdx >= 0 && hashIdx < cut) cut = hashIdx;
            return baseHref.Substring(0, cut) + h;
        }

        string raw;
        if (h.StartsWith("/"))
        {
            raw = OriginOf(baseHref) + h;
        }
        else
        {
            string dir = DirOf(baseHref);
            raw = dir + h;
        }

        return NormalizeDotSegments(raw);
    }

    private static string NormalizeDotSegments(string url)
    {
        if (string.IsNullOrEmpty(url) || url.IndexOf("/.") < 0) return url;

        string origin = OriginOf(url);
        if (origin.Length == 0 || url.Length <= origin.Length) return url;

        string path = url.Substring(origin.Length);
        string queryAndHash = "";
        int q = path.IndexOf('?');
        int hash = path.IndexOf('#');
        int split = -1;
        if (q >= 0 && hash >= 0) split = Math.Min(q, hash);
        else if (q >= 0) split = q;
        else if (hash >= 0) split = hash;

        if (split >= 0)
        {
            queryAndHash = path.Substring(split);
            path = path.Substring(0, split);
        }

        string[] parts = path.Split('/');
        List<string> stack = new List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part == "." || (part == "" && i > 0 && i < parts.Length - 1)) continue;
            if (part == "..")
            {
                if (stack.Count > 0 && stack[stack.Count - 1] != "")
                    stack.RemoveAt(stack.Count - 1);
            }
            else
            {
                stack.Add(part);
            }
        }

        string newPath = string.Join("/", stack.ToArray());
        if (!newPath.StartsWith("/")) newPath = "/" + newPath;
        if (path.EndsWith("/") && !newPath.EndsWith("/")) newPath += "/";

        return origin + newPath + queryAndHash;
    }

    public static string SchemeOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        int idx = url.IndexOf("://");
        if (idx > 0) return url.Substring(0, idx).ToLowerInvariant();
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return "about";
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "data";
        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return "javascript";
        if (url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return "mailto";
        if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return "file";
        return "";
    }

    /// <summary>scheme://host[:port]</summary>
    public static string OriginOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        int idx = url.IndexOf("://");
        if (idx < 0) return url;
        int start = idx + 3;
        int slash = url.IndexOf('/', start);
        string authority = slash < 0 ? url.Substring(start) : url.Substring(start, slash - start);
        return url.Substring(0, idx) + "://" + authority;
    }

    /// <summary>scheme://host/last/path/  (everything up to and including the final '/')</summary>
    public static string DirOf(string url)
    {
        string origin = OriginOf(url);
        if (string.IsNullOrEmpty(url) || url.Length <= origin.Length) return origin + "/";
        string pathOnly = url;
        int q = pathOnly.IndexOf('?');
        if (q >= 0) pathOnly = pathOnly.Substring(0, q);
        int h = pathOnly.IndexOf('#');
        if (h >= 0) pathOnly = pathOnly.Substring(0, h);

        int lastSlash = pathOnly.LastIndexOf('/');
        if (lastSlash < origin.Length) return origin + "/";
        return pathOnly.Substring(0, lastSlash + 1);
    }

    /// <summary>host[:port] for status messages.</summary>
    public static string HostOf(string url)
    {
        string origin = OriginOf(url);
        if (origin.Length == 0) return "";
        int idx = origin.IndexOf("://");
        string authority = idx >= 0 ? origin.Substring(idx + 3) : origin;
        int colon = authority.IndexOf(':');
        return colon > 0 ? authority.Substring(0, colon) : authority;
    }

    /// <summary>The path portion of a URL (always starts with '/'; "/" when absent).</summary>
    public static string PathOf(string url)
    {
        string origin = OriginOf(url);
        if (origin.Length == 0 || url.Length <= origin.Length) return "/";
        string rest = url.Substring(origin.Length);
        int q = rest.IndexOf('?');
        if (q >= 0) rest = rest.Substring(0, q);
        int h = rest.IndexOf('#');
        if (h >= 0) rest = rest.Substring(0, h);
        if (rest.Length == 0) return "/";
        return rest[0] == '/' ? rest : "/" + rest;
    }
}
