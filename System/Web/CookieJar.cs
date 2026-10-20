using System;
using System.Collections.Generic;

/// <summary>A single stored HTTP cookie.</summary>
public sealed class Cookie
{
    public string Name;
    public string Value;
    public string Domain;        // lowercase host (no leading dot)
    public string Path;          // at least "/"
    public bool Secure;          // https only
    public bool HttpOnly;        // not exposed to document.cookie
    public long ExpiresAtTicks;  // 0 => session cookie (kept until the OS ends)
}

/// <summary>
/// Minimal in-memory cookie jar (RFC 6265 subset): Set-Cookie parsing,
/// domain/path matching, the Secure flag and Max-Age expiry. Shared by the
/// page-load pipeline and the script fetch/XHR pipeline, both of which run
/// on worker threads, so every access is locked.
/// </summary>
public sealed class CookieJar
{
    private readonly List<Cookie> cookies = new List<Cookie>();
    private readonly object gate = new object();

    /// <summary>Parse a Set-Cookie header (or a document.cookie assignment)
    /// against the URL it arrived on and store it. Cookies that fail the
    /// domain check, or carry an empty name, are dropped silently.</summary>
    public void AddFromSetCookie(string setCookie, string requestUrl)
    {
        if (string.IsNullOrEmpty(setCookie)) return;
        string host = WebUrl.HostOf(requestUrl).ToLowerInvariant();
        string reqPath = WebUrl.PathOf(requestUrl);
        bool secureRequest = WebUrl.SchemeOf(requestUrl) == "https";
        if (host.Length == 0) return;

        // first segment is "name=value"; the rest are attributes
        int semi = setCookie.IndexOf(';');
        string pair = semi >= 0 ? setCookie.Substring(0, semi) : setCookie;
        int eq = pair.IndexOf('=');
        if (eq <= 0) return;                       // no name, or empty name
        string name = pair.Substring(0, eq).Trim();
        string value = pair.Substring(eq + 1).Trim();
        if (name.Length == 0) return;

        Cookie c = new Cookie();
        c.Name = name;
        c.Value = value;
        c.Domain = host;
        c.Path = DefaultPath(reqPath);
        c.Secure = false;
        c.HttpOnly = false;
        c.ExpiresAtTicks = 0;

        bool reject = false;
        if (semi >= 0)
        {
            int pos = semi + 1;
            while (pos <= setCookie.Length)
            {
                int next = setCookie.IndexOf(';', pos);
                if (next < 0) next = setCookie.Length;
                string attr = setCookie.Substring(pos, next - pos).Trim();
                pos = next + 1;
                if (attr.Length == 0) continue;

                int aeq = attr.IndexOf('=');
                string aname = (aeq >= 0 ? attr.Substring(0, aeq) : attr).Trim().ToLowerInvariant();
                string aval = aeq >= 0 ? attr.Substring(aeq + 1).Trim() : "";

                if (aname == "domain")
                {
                    string d = aval.ToLowerInvariant();
                    while (d.Length > 0 && d[0] == '.') d = d.Substring(1);
                    // a cookie may only be set for its host or a parent domain
                    if (d.Length == 0 || !DomainMatches(d, host)) { reject = true; break; }
                    c.Domain = d;
                }
                else if (aname == "path")
                {
                    if (aval.Length > 0 && aval[0] == '/') c.Path = aval;
                }
                else if (aname == "secure")
                {
                    c.Secure = true;
                }
                else if (aname == "httponly")
                {
                    c.HttpOnly = true;
                }
                else if (aname == "max-age")
                {
                    long seconds = ParseInt64(aval);
                    if (seconds <= 0) c.ExpiresAtTicks = 1;           // delete now
                    else c.ExpiresAtTicks = DateTime.UtcNow.Ticks + seconds * 10000000L;
                }
                // "expires" needs full HTTP-date parsing; those cookies are
                // treated as session cookies (a documented approximation).
            }
        }
        if (reject) return;
        if (c.Secure && !secureRequest) return;    // Secure cookies only over https

        lock (gate)
        {
            // a same-name/same-domain/same-path cookie is replaced
            for (int i = cookies.Count - 1; i >= 0; i--)
            {
                Cookie old = cookies[i];
                if (old.Name == c.Name && old.Domain == c.Domain && old.Path == c.Path)
                    cookies.RemoveAt(i);
            }
            if (c.ExpiresAtTicks != 1) cookies.Add(c);
            PruneExpired();
        }
    }

    /// <summary>The Cookie request header value for a URL, or null when the
    /// jar has nothing to send.</summary>
    public string HeaderFor(string requestUrl)
    {
        string host = WebUrl.HostOf(requestUrl).ToLowerInvariant();
        string reqPath = WebUrl.PathOf(requestUrl);
        bool secureRequest = WebUrl.SchemeOf(requestUrl) == "https";

        lock (gate)
        {
            PruneExpired();
            string outp = null;
            for (int i = 0; i < cookies.Count; i++)
            {
                Cookie c = cookies[i];
                if (c.Secure && !secureRequest) continue;
                if (!DomainMatches(c.Domain, host)) continue;
                if (!PathMatches(c.Path, reqPath)) continue;
                outp = outp == null ? c.Name + "=" + c.Value : outp + "; " + c.Name + "=" + c.Value;
            }
            return outp;
        }
    }

    /// <summary>document.cookie for a URL: the non-HttpOnly cookies that
    /// domain/path match (Secure cookies are omitted over plain http).</summary>
    public string DocumentCookieFor(string requestUrl)
    {
        string host = WebUrl.HostOf(requestUrl).ToLowerInvariant();
        string reqPath = WebUrl.PathOf(requestUrl);
        bool secureRequest = WebUrl.SchemeOf(requestUrl) == "https";

        lock (gate)
        {
            PruneExpired();
            string outp = null;
            for (int i = 0; i < cookies.Count; i++)
            {
                Cookie c = cookies[i];
                if (c.HttpOnly) continue;
                if (c.Secure && !secureRequest) continue;
                if (!DomainMatches(c.Domain, host)) continue;
                if (!PathMatches(c.Path, reqPath)) continue;
                outp = outp == null ? c.Name + "=" + c.Value : outp + "; " + c.Name + "=" + c.Value;
            }
            return outp;
        }
    }

    // ---- helpers ----------------------------------------------------------

    private void PruneExpired()
    {
        // caller holds the lock
        long now = DateTime.UtcNow.Ticks;
        for (int i = cookies.Count - 1; i >= 0; i--)
            if (cookies[i].ExpiresAtTicks != 0 && cookies[i].ExpiresAtTicks <= now)
                cookies.RemoveAt(i);
    }

    /// <summary>host == domain, or host is a subdomain of domain.</summary>
    private static bool DomainMatches(string domain, string host)
    {
        if (domain == host) return true;
        if (host.Length <= domain.Length + 1) return false;
        if (host[host.Length - domain.Length - 1] != '.') return false;
        return EndsWith(host, domain);
    }

    /// <summary>cookie path is the request path, a prefix ending in '/', or a
    /// prefix that lands on a '/' boundary.</summary>
    private static bool PathMatches(string cookiePath, string requestPath)
    {
        if (cookiePath == null || cookiePath.Length == 0) cookiePath = "/";
        if (requestPath == null || requestPath.Length == 0) requestPath = "/";
        if (requestPath == cookiePath) return true;
        if (!StartsWith(requestPath, cookiePath)) return false;
        if (cookiePath[cookiePath.Length - 1] == '/') return true;
        return requestPath[cookiePath.Length] == '/';
    }

    /// <summary>The directory part of the request path (default "/").</summary>
    private static string DefaultPath(string requestPath)
    {
        if (requestPath == null || requestPath.Length == 0 || requestPath[0] != '/') return "/";
        int last = requestPath.LastIndexOf('/');
        if (last <= 0) return "/";
        return requestPath.Substring(0, last);
    }

    private static bool StartsWith(string s, string prefix)
    {
        if (s.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
            if (s[i] != prefix[i]) return false;
        return true;
    }

    private static bool EndsWith(string s, string suffix)
    {
        if (s.Length < suffix.Length) return false;
        for (int i = 0; i < suffix.Length; i++)
            if (s[s.Length - suffix.Length + i] != suffix[i]) return false;
        return true;
    }

    /// <summary>Parse a leading integer (culture-free; digits only).</summary>
    private static long ParseInt64(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        bool neg = false;
        int i = 0;
        if (s[0] == '-') { neg = true; i = 1; }
        long v = 0;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if (c < '0' || c > '9') break;
            v = v * 10 + (c - '0');
            if (v > 4000000000L) break;
        }
        return neg ? -v : v;
    }
}
