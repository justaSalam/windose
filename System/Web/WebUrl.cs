using System;

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
        if (lower.StartsWith("about:")) return s;
        if (s.IndexOf("://") >= 0) return s;
        if (s.StartsWith("//")) return "http:" + s;

        return "http://" + s;
    }

    /// <summary>Resolve a (possibly relative) href against the current page URL.</summary>
    public static string Resolve(string baseHref, string href)
    {
        if (string.IsNullOrEmpty(href)) return baseHref;
        string h = href.Trim();
        if (h.Length == 0) return baseHref;

        string lower = h.ToLowerInvariant();
        if (lower.StartsWith("about:") || h.IndexOf("://") >= 0) return h;
        if (h.StartsWith("//"))
        {
            string scheme = SchemeOf(baseHref);
            if (scheme.Length == 0) scheme = "http";
            return scheme + ":" + h;
        }
        if (h.StartsWith("#")) return baseHref;

        string origin = OriginOf(baseHref);
        if (h.StartsWith("/")) return origin + h;

        string dir = DirOf(baseHref);
        return dir + h;
    }

    public static string SchemeOf(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        int idx = url.IndexOf("://");
        return idx > 0 ? url.Substring(0, idx) : "";
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
        int lastSlash = url.LastIndexOf('/');
        if (lastSlash < origin.Length) return origin + "/";
        return url.Substring(0, lastSlash + 1);
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
