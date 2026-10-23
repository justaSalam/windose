using System;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// The browser-side event loop for the JS engine. Three kinds of work:
//
//   microtasks - Promise reaction jobs. Drained whenever the JS stack is empty
//                (after scripts, events, timers and network results), so
//                `p.then(...)` always runs after the current script.
//   macrotasks - setTimeout/setInterval timers, fired from the host's Update
//                tick when they come due.
//   fetches    - fetch()/XHR requests queued by scripts. The host (WebView)
//                ships them to its networking worker and delivers results back
//                on the UI thread, so scripts never block on the network and
//                no engine code runs off-thread.
//
// Cosmos-safe: plain lists + delegates; no threads, LINQ or reflection here.
// ---------------------------------------------------------------------------

public sealed class JsTimer
{
    public int Id;
    public long DueAt;      // DateTime.UtcNow.Ticks
    public long Interval;   // ticks between repeats; 0 = one-shot
    public JsFunction Fn;
    public object[] Args;
    public bool Cleared;
}

/// <summary>A network request queued by fetch() or XHR.send().</summary>
public sealed class JsFetchRequest
{
    public int Id;
    public string Url;
    public string Method;
    public string Headers;
    public string Body;
    public Action<JsFetchResult> Done;
}

/// <summary>The host's answer to a JsFetchRequest.</summary>
public sealed class JsFetchResult
{
    public bool Ok;
    public int Status;
    public string StatusText = "";
    public string Headers = "";
    public string Body = "";
    public string BodyText { get { return Body; } set { Body = value; } }
    public string Url = "";
    public string Error;   // non-null = network failure -> the promise rejects
}

public sealed class JsEventLoop
{
    public JsInterpreter Interp;

    // microtask queue (FIFO; jobs may append while we drain)
    private readonly List<Action> jobs = new List<Action>();
    private int jobPos;
    private bool draining;

    // timers
    private readonly List<JsTimer> timers = new List<JsTimer>();
    private int nextTimerId = 1;

    // in-flight network requests
    private readonly List<JsFetchRequest> fetches = new List<JsFetchRequest>();
    private int nextFetchId = 1;

    // host hooks + page state the window bindings read
    public Action<string> NavigateHook;    // location.href = ..., window.open
    public Action ReloadHook;
    public Action<string> HistoryBackHook;
    public Action<string> HistoryForwardHook;
    public Action PumpHook;

    /// <summary>Await a promise synchronously within the event loop, pumping pending jobs until settled.</summary>
    public object AwaitPromise(JsPromise promise, int timeoutMs = 15000)
    {
        if (promise == null) return JsUndefined.Instance;

        // Drain any immediately queued microtasks first
        DrainJobs();

        if (promise.State == JsPromise.Fulfilled) return promise.Result;
        if (promise.State == JsPromise.Rejected)
            throw new JsThrown(promise.Result, JsValueOps.ToStringValue(promise.Result));

        long startTicks = DateTime.UtcNow.Ticks;
        long timeoutTicks = (long)timeoutMs * TimeSpan.TicksPerMillisecond;

        while (promise.State == JsPromise.Pending)
        {
            if (PumpHook != null)
            {
                try { PumpHook(); } catch { }
            }
            RunDue(DateTime.UtcNow.Ticks);
            DrainJobs();

            if (promise.State != JsPromise.Pending) break;

            if (DateTime.UtcNow.Ticks - startTicks > timeoutTicks)
            {
                throw JsErr.Type("Async operation timed out");
            }

            try { global::System.Threading.Thread.Sleep(5); } catch { }
        }

        if (promise.State == JsPromise.Fulfilled) return promise.Result;
        if (promise.State == JsPromise.Rejected)
            throw new JsThrown(promise.Result, JsValueOps.ToStringValue(promise.Result));

        return promise.Result;
    }

    // document.cookie, backed by the host's jar (url -> "a=b; c=d", and the
    // reverse for assignments). Both run on the UI thread.
    public delegate string CookieGetterDelegate(string url);
    public delegate void CookieSetterDelegate(string cookie, string url);
    public CookieGetterDelegate CookieGetter;
    public CookieSetterDelegate CookieSetter;

    public string LocationHref = "about:blank";
    public string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/120.0.0.0 Safari/537.36";
    public string Platform = "Win32";
    public string Language = "en-US";

    /// <summary>Viewport size reported by window.innerWidth/Height.</summary>
    public int ViewportWidth = 1024;
    public int ViewportHeight = 768;

    /// <summary>Cap so a runaway Promise chain cannot hang the OS.</summary>
    public int MaxMicrotasks = 100000;

    public static long Now() { return DateTime.UtcNow.Ticks; }

    // ---- microtasks -------------------------------------------------------

    public void EnqueueJob(Action job)
    {
        if (job != null) jobs.Add(job);
    }

    /// <summary>Run queued Promise reactions until the queue drains (bounded).</summary>
    public void DrainJobs()
    {
        if (Interp != null) Interp.ResetSteps();
        if (draining) return;          // a job may re-enter; the outer loop finishes
        draining = true;
        try
        {
            int guard = 0;
            while (jobPos < jobs.Count)
            {
                if (++guard > MaxMicrotasks)
                {
                    ReportUncaught(null, "Promise chain did not settle (microtask limit reached)");
                    break;
                }
                Action job = jobs[jobPos++];
                try { job(); }
                catch (JsThrown jt) { ReportUncaught(jt.Value, jt.Message); }
                catch (Exception ex) { ReportUncaught(null, ex.Message); }
            }
        }
        finally
        {
            jobs.Clear();
            jobPos = 0;
            draining = false;
        }
    }

    /// <summary>Report an uncaught error the way a browser would: to the console.</summary>
    public void ReportUncaught(object value, string message)
    {
        string name = "Error";
        string msg = message != null ? message : "";

        JsErrorValue jev = value as JsErrorValue;
        if (jev != null)
        {
            if (!string.IsNullOrEmpty(jev.Name)) name = jev.Name;
            if (!string.IsNullOrEmpty(jev.Message)) msg = jev.Message;
        }
        else if (!(value is JsUndefined) && value != null)
        {
            JsObject jo = value as JsObject;
            if (jo != null)
            {
                object n;
                if (jo.GetProperty("name", out n) && n is string) name = (string)n;
                object m;
                if (jo.GetProperty("message", out m)) msg = JsValueOps.ToStringValue(m);
            }
            else
            {
                msg = JsValueOps.ToStringValue(value);
            }
        }

        if (JsBuiltins.ConsoleOutput != null)
            JsBuiltins.ConsoleOutput("Uncaught " + name + ": " + msg);
    }

    // ---- timers (macrotasks) ----------------------------------------------

    public int SetTimeout(JsFunction fn, object[] args, double delayMs, double intervalMs)
    {
        long delay = (long)(delayMs * (double)TimeSpan.TicksPerMillisecond);
        if (delay < 0) delay = 0;

        long interval = 0;
        if (intervalMs > 0)
        {
            interval = (long)(intervalMs * (double)TimeSpan.TicksPerMillisecond);
            if (interval < 1) interval = 1;
        }

        JsTimer t = new JsTimer();
        t.Id = nextTimerId++;
        t.DueAt = Now() + delay;
        t.Interval = interval;
        t.Fn = fn;
        t.Args = args;
        timers.Add(t);
        return t.Id;
    }

    public void ClearTimer(double id)
    {
        int n = (int)id;
        for (int i = 0; i < timers.Count; i++)
        {
            if (timers[i].Id == n)
            {
                timers[i].Cleared = true;
                timers.RemoveAt(i);
                return;
            }
        }
    }

    public bool HasTimers { get { return timers.Count > 0; } }
    public bool HasWork { get { return timers.Count > 0 || jobs.Count > 0 || fetches.Count > 0; } }

    /// <summary>Fire every timer due at (or before) now, then drain microtasks.</summary>
    public void RunDue(long now)
    {
        if (timers.Count == 0) return;

        List<JsTimer> due = null;
        for (int i = 0; i < timers.Count; i++)
        {
            JsTimer t = timers[i];
            if (t.DueAt <= now)
            {
                if (due == null) due = new List<JsTimer>();
                due.Add(t);
            }
        }
        if (due == null) return;

        for (int i = 0; i < due.Count; i++)
        {
            JsTimer t = due[i];
            if (t.Cleared) continue;
            // Reschedule (or drop) before running so a callback that clears or
            // re-arms this timer cannot make us fire it twice.
            if (t.Interval > 0) t.DueAt = Now() + t.Interval;
            else timers.Remove(t);

            try
            {
                JsValueOps.Active = Interp;
                Interp.Call(t.Fn, Interp.Realm.Global, t.Args, false);
            }
            catch (JsThrown jt) { ReportUncaught(jt.Value, jt.Message); }
            catch (Exception) { /* a broken timer must not take the host down */ }
        }

        DrainJobs();
    }

    // ---- fetch / XHR (macrotasks, executed off-thread by the host) --------

    public int EnqueueFetch(string url, string method, string headers, string body, Action<JsFetchResult> done)
    {
        JsFetchRequest r = new JsFetchRequest();
        r.Id = nextFetchId++;
        r.Url = url != null ? url : "";
        r.Method = string.IsNullOrEmpty(method) ? "GET" : method;
        r.Headers = headers != null ? headers : "";
        r.Body = body;
        r.Done = done;
        fetches.Add(r);
        return r.Id;
    }

    public bool HasPendingFetches { get { return fetches.Count > 0; } }

    /// <summary>Host (UI thread): move queued requests into its own batch.</summary>
    public void TakeFetches(List<JsFetchRequest> sink)
    {
        if (sink == null) return;
        for (int i = 0; i < fetches.Count; i++) sink.Add(fetches[i]);
        fetches.Clear();
    }

    /// <summary>Host (UI thread): hand a result back; runs callbacks + microtasks.</summary>
    public void DeliverFetch(JsFetchRequest req, JsFetchResult result)
    {
        if (req == null) return;
        try
        {
            if (req.Done != null) req.Done(result);
        }
        catch (JsThrown jt) { ReportUncaught(jt.Value, jt.Message); }
        catch (Exception ex) { ReportUncaught(null, ex.Message); }
        DrainJobs();
    }

    /// <summary>Reject everything still queued (no host networking available).</summary>
    public void FailAllFetches(string error)
    {
        if (fetches.Count == 0) return;
        List<JsFetchRequest> batch = new List<JsFetchRequest>();
        TakeFetches(batch);
        for (int i = 0; i < batch.Count; i++)
        {
            JsFetchResult r = new JsFetchResult();
            r.Error = error != null ? error : "Network error";
            r.Url = batch[i].Url;
            DeliverFetch(batch[i], r);
        }
    }

    // ---- navigation -------------------------------------------------------

    public void Navigate(string url)
    {
        if (NavigateHook != null) NavigateHook(url);
        else ReportUncaught(null, "navigation is not available");
    }

    public void Reload()
    {
        if (ReloadHook != null) ReloadHook();
    }

    public void HistoryBack(string url)
    {
        if (HistoryBackHook != null) HistoryBackHook(url);
    }

    public void HistoryForward(string url)
    {
        if (HistoryForwardHook != null) HistoryForwardHook(url);
    }
}
