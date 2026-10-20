using System;
using System.Collections.Generic;
using System.Text;

// ---------------------------------------------------------------------------
// Browser-flavoured globals for the JS engine:
//
//   Promise (+ then/catch/finally, resolve/reject/all/race/allSettled/any)
//   setTimeout / setInterval / clearTimeout / clearInterval / queueMicrotask
//   fetch -> Response (text/json/headers), XMLHttpRequest
//   location, navigator, history, performance, alert/confirm/prompt,
//   scrollTo + innerWidth/Height and friends.
//
// Networking never blocks: fetch()/XHR just queue a JsFetchRequest on the
// JsEventLoop; the host WebView ships those to its fetch worker and delivers
// results back on the UI thread, which resolves the promise and drains the
// microtask queue.
//
// Cosmos-safe: no LINQ, no regex, no reflection, no char.IsXxx, manual case
// folding throughout.
// ---------------------------------------------------------------------------

/// <summary>A Promise instance: pending -> fulfilled | rejected.</summary>
public sealed class JsPromise : JsObject
{
    public const int Pending = 0;
    public const int Fulfilled = 1;
    public const int Rejected = 2;

    public int State;
    public object Result;
    public List<JsReaction> Reactions = new List<JsReaction>();

    public JsPromise(JsObject prototype) : base(prototype) { }

    public override string ClassTag { get { return "Promise"; } }
}

/// <summary>One subscription registered by then/catch/finally (or adoption).</summary>
public sealed class JsReaction
{
    public JsFunction OnFulfilled;
    public JsFunction OnRejected;
    public JsPromise Next;      // null: "internal" reaction (promise adoption)
}

/// <summary>An XMLHttpRequest instance.</summary>
public sealed class JsXhrObject : JsObject
{
    public JsRealm Realm;
    public JsEventLoop Loop;

    public string Method = "GET";
    public string Url = "";
    public string RequestHeaders = "";
    public string RequestBody;
    public bool Async = true;

    public int ReadyState;
    public int Status;
    public string StatusText = "";
    public string ResponseText = "";
    public string ResponseHeaders = "";
    public string ResponseUrl = "";
    public string ResponseType = "";
    public bool Aborted;

    public readonly Dictionary<string, List<JsFunction>> Listeners =
        new Dictionary<string, List<JsFunction>>();

    public JsXhrObject(JsObject prototype, JsRealm realm, JsEventLoop loop) : base(prototype)
    {
        Realm = realm;
        Loop = loop;
    }

    public override string ClassTag { get { return "XMLHttpRequest"; } }

    /// <summary>Fire an XHR event at this object (inline handler + listeners).</summary>
    public void Fire(string type)
    {
        JsEventObject ev = new JsEventObject(Realm.EventPrototype);
        ev.Type = type;
        ev.Target = this;
        ev.CurrentTarget = this;
        ev.Bubbles = false;

        List<JsFunction> fns;
        if (Listeners.TryGetValue(type, out fns))
        {
            List<JsFunction> copy = new List<JsFunction>(fns);
            for (int i = 0; i < copy.Count; i++)
            {
                try { Loop.Interp.Call(copy[i], this, new object[] { ev }, false); }
                catch (JsThrown) { }
                catch (Exception) { }
            }
        }

        JsProperty p;
        if (TryGetRaw("on" + type, out p) && p.Kind == JsPropKind.Data && p.Value is JsFunction)
        {
            try { Loop.Interp.Call((JsFunction)p.Value, this, new object[] { ev }, false); }
            catch (JsThrown) { }
            catch (Exception) { }
        }
    }
}

public static class JsBrowser
{
    public static void Install(JsRealm realm)
    {
        JsEventLoop loop = realm.Interp != null ? realm.Interp.EnsureLoop() : null;
        if (loop == null) return;   // no interpreter: nothing to wire

        BuildPromise(realm, loop);
        BuildTimers(realm, loop);
        BuildFetch(realm, loop);
        BuildXhr(realm, loop);
        BuildLocation(realm, loop);
        BuildNavigator(realm, loop);
        BuildHistory(realm, loop);
        BuildWindow(realm, loop);
    }

    // ---- small helpers ----------------------------------------------------

    private static void DefineMethod(JsRealm realm, JsObject o, string name, JsNativeFn fn, int len)
    {
        o.Define(name, JsProperty.DataNonEnum(realm.MakeMethod(name, fn, len)));
    }

    private static void DefineAccessor(JsRealm realm, JsObject o, string name, JsNativeFn get, JsNativeFn set)
    {
        o.Define(name, JsProperty.Accessor(
            get != null ? realm.MakeMethod("get " + name, get, 0) : null,
            set != null ? realm.MakeMethod("set " + name, set, 1) : null));
    }

    private static object[] Tail(JsCallArgs a, int from)
    {
        int n = a.ArgCount - from;
        if (n <= 0) return new object[0];
        object[] r = new object[n];
        for (int i = 0; i < n; i++) r[i] = a.Get(from + i);
        return r;
    }

    private static string DescribeValue(object v)
    {
        if (v == null || v is JsUndefined) return "undefined";
        if (v is JsNull) return "null";
        if (v is JsFunction) return "function";
        return JsValueOps.TypeNameOf(v);
    }

    // ---- Promise ----------------------------------------------------------

    private static void BuildPromise(JsRealm realm, JsEventLoop loop)
    {
        JsObject proto = realm.PromisePrototype;
        proto.SetWellKnown(JsWellKnown.ToStringTag, "Promise");

        realm.PromiseCtor = realm.MakeCtor("Promise", delegate (JsCallArgs a)
        {
            if (!a.IsConstruct) throw JsErr.Type("Promise constructor cannot be called without 'new'");
            object exec = a.Get(0);
            if (!(exec is JsFunction))
                throw JsErr.Type("Promise resolver " + DescribeValue(exec) + " is not a function");

            JsPromise p = new JsPromise(proto);
            JsFunction resolveFn = MakeSettler(realm, loop, p, true);
            JsFunction rejectFn = MakeSettler(realm, loop, p, false);
            try
            {
                a.Interp.Call(exec, JsUndefined.Instance, new object[] { resolveFn, rejectFn }, false);
            }
            catch (JsThrown jt) { RejectPromise(loop, p, jt.Value); }
            return p;
        }, 1, proto);
        realm.Global.Define("Promise", JsProperty.Data(realm.PromiseCtor));

        // then(onFulfilled, onRejected)
        proto.Define("then", JsProperty.DataNonEnum(realm.MakeMethod("then", delegate (JsCallArgs a)
        {
            JsPromise self = RequirePromise(a.ThisValue, "then");
            JsFunction onF = a.Get(0) as JsFunction;
            JsFunction onR = a.Get(1) as JsFunction;
            JsPromise next = new JsPromise(proto);
            JsReaction r = new JsReaction();
            r.OnFulfilled = onF;
            r.OnRejected = onR;
            r.Next = next;
            ScheduleReaction(loop, self, r);
            return next;
        }, 2)));

        // catch(onRejected) === then(undefined, onRejected)
        proto.Define("catch", JsProperty.DataNonEnum(realm.MakeMethod("catch", delegate (JsCallArgs a)
        {
            JsPromise self = RequirePromise(a.ThisValue, "catch");
            JsFunction onR = a.Get(0) as JsFunction;
            JsPromise next = new JsPromise(proto);
            JsReaction r = new JsReaction();
            r.OnRejected = onR;
            r.Next = next;
            ScheduleReaction(loop, self, r);
            return next;
        }, 1)));

        // finally(onFinally): run the callback on either path, then settle the
        // derived promise with the ORIGINAL value/reason (its return value is
        // ignored; a thrown callback rejects).
        proto.Define("finally", JsProperty.DataNonEnum(realm.MakeMethod("finally", delegate (JsCallArgs a)
        {
            JsPromise self = RequirePromise(a.ThisValue, "finally");
            JsFunction cb = a.Get(0) as JsFunction;
            JsPromise next = new JsPromise(proto);
            JsReaction r = new JsReaction();
            r.Next = next;
            if (cb != null)
            {
                r.OnFulfilled = realm.MakeMethod("finallyFulfilled", delegate (JsCallArgs w)
                {
                    object v = w.ArgCount > 0 ? w.Get(0) : JsUndefined.Instance;
                    loop.Interp.Call(cb, JsUndefined.Instance, new object[0], false);
                    return v;
                }, 0);
                r.OnRejected = realm.MakeMethod("finallyRejected", delegate (JsCallArgs w)
                {
                    object e = w.ArgCount > 0 ? w.Get(0) : JsUndefined.Instance;
                    loop.Interp.Call(cb, JsUndefined.Instance, new object[0], false);
                    throw new JsThrown(e, JsValueOps.ToStringValue(e));
                }, 0);
            }
            ScheduleReaction(loop, self, r);
            return next;
        }, 1)));

        // ---- statics ----
        realm.PromiseCtor.Define("resolve", JsProperty.DataNonEnum(realm.MakeMethod("resolve", delegate (JsCallArgs a)
        {
            object v = a.ArgCount > 0 ? a.Get(0) : JsUndefined.Instance;
            JsPromise p = new JsPromise(proto);
            FulfillPromise(loop, p, v);
            return p;
        }, 1)));

        realm.PromiseCtor.Define("reject", JsProperty.DataNonEnum(realm.MakeMethod("reject", delegate (JsCallArgs a)
        {
            JsPromise p = new JsPromise(proto);
            RejectPromise(loop, p, a.ArgCount > 0 ? a.Get(0) : JsUndefined.Instance);
            return p;
        }, 1)));

        realm.PromiseCtor.Define("all", JsProperty.DataNonEnum(realm.MakeMethod("all", delegate (JsCallArgs a)
        {
            List<object> items = a.Interp.ToList(a.Get(0));
            JsPromise outer = new JsPromise(proto);
            if (items.Count == 0) { FulfillPromise(loop, outer, realm.NewArray()); return outer; }

            object[] results = new object[items.Count];
            int remaining = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                JsReaction r = new JsReaction();
                r.OnFulfilled = realm.MakeMethod("onFulfilled", delegate (JsCallArgs c)
                {
                    results[idx] = c.ArgCount > 0 ? c.Get(0) : JsUndefined.Instance;
                    remaining--;
                    if (remaining == 0)
                    {
                        JsArray arr = realm.NewArray();
                        for (int k = 0; k < results.Length; k++) arr.Items.Add(results[k]);
                        FulfillPromise(loop, outer, arr);
                    }
                    return JsUndefined.Instance;
                }, 1);
                r.OnRejected = realm.MakeMethod("onRejected", delegate (JsCallArgs c)
                {
                    RejectPromise(loop, outer, c.ArgCount > 0 ? c.Get(0) : JsUndefined.Instance);
                    return JsUndefined.Instance;
                }, 1);
                ScheduleReaction(loop, Watched(realm, loop, items[i]), r);
            }
            return outer;
        }, 1)));

        realm.PromiseCtor.Define("race", JsProperty.DataNonEnum(realm.MakeMethod("race", delegate (JsCallArgs a)
        {
            List<object> items = a.Interp.ToList(a.Get(0));
            JsPromise outer = new JsPromise(proto);
            for (int i = 0; i < items.Count; i++)
            {
                JsReaction r = new JsReaction();
                r.OnFulfilled = realm.MakeMethod("onFulfilled", delegate (JsCallArgs c)
                {
                    FulfillPromise(loop, outer, c.ArgCount > 0 ? c.Get(0) : JsUndefined.Instance);
                    return JsUndefined.Instance;
                }, 1);
                r.OnRejected = realm.MakeMethod("onRejected", delegate (JsCallArgs c)
                {
                    RejectPromise(loop, outer, c.ArgCount > 0 ? c.Get(0) : JsUndefined.Instance);
                    return JsUndefined.Instance;
                }, 1);
                ScheduleReaction(loop, Watched(realm, loop, items[i]), r);
            }
            return outer;
        }, 1)));

        realm.PromiseCtor.Define("allSettled", JsProperty.DataNonEnum(realm.MakeMethod("allSettled", delegate (JsCallArgs a)
        {
            List<object> items = a.Interp.ToList(a.Get(0));
            JsPromise outer = new JsPromise(proto);
            if (items.Count == 0) { FulfillPromise(loop, outer, realm.NewArray()); return outer; }

            object[] results = new object[items.Count];
            int remaining = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                int idx = i;
                JsReaction r = new JsReaction();
                r.OnFulfilled = realm.MakeMethod("onFulfilled", delegate (JsCallArgs c)
                {
                    JsObject rec = realm.NewObject();
                    rec.Define("status", JsProperty.DataNonEnum("fulfilled"));
                    rec.Define("value", JsProperty.DataNonEnum(c.ArgCount > 0 ? c.Get(0) : JsUndefined.Instance));
                    results[idx] = rec;
                    if (--remaining == 0) FulfillPromise(loop, outer, ToArray(realm, results));
                    return JsUndefined.Instance;
                }, 1);
                r.OnRejected = realm.MakeMethod("onRejected", delegate (JsCallArgs c)
                {
                    JsObject rec = realm.NewObject();
                    rec.Define("status", JsProperty.DataNonEnum("rejected"));
                    rec.Define("reason", JsProperty.DataNonEnum(c.ArgCount > 0 ? c.Get(0) : JsUndefined.Instance));
                    results[idx] = rec;
                    if (--remaining == 0) FulfillPromise(loop, outer, ToArray(realm, results));
                    return JsUndefined.Instance;
                }, 1);
                ScheduleReaction(loop, Watched(realm, loop, items[i]), r);
            }
            return outer;
        }, 1)));

        realm.PromiseCtor.Define("any", JsProperty.DataNonEnum(realm.MakeMethod("any", delegate (JsCallArgs a)
        {
            List<object> items = a.Interp.ToList(a.Get(0));
            JsPromise outer = new JsPromise(proto);
            if (items.Count == 0)
            {
                RejectPromise(loop, outer, JsBuiltins.CreateErrorObject(realm, "AggregateError", "All promises were rejected"));
                return outer;
            }
            int failed = 0;
            object lastReason = JsUndefined.Instance;
            for (int i = 0; i < items.Count; i++)
            {
                JsReaction r = new JsReaction();
                r.OnFulfilled = realm.MakeMethod("onFulfilled", delegate (JsCallArgs c)
                {
                    FulfillPromise(loop, outer, c.ArgCount > 0 ? c.Get(0) : JsUndefined.Instance);
                    return JsUndefined.Instance;
                }, 1);
                r.OnRejected = realm.MakeMethod("onRejected", delegate (JsCallArgs c)
                {
                    lastReason = c.ArgCount > 0 ? c.Get(0) : JsUndefined.Instance;
                    if (++failed == items.Count)
                        RejectPromise(loop, outer, JsBuiltins.CreateErrorObject(realm, "AggregateError", "All promises were rejected"));
                    return JsUndefined.Instance;
                }, 1);
                ScheduleReaction(loop, Watched(realm, loop, items[i]), r);
            }
            return outer;
        }, 1)));

        realm.PromiseCtor.Define("withResolvers", JsProperty.DataNonEnum(realm.MakeMethod("withResolvers", delegate (JsCallArgs a)
        {
            JsObject o = realm.NewObject();
            JsPromise p = new JsPromise(proto);
            o.Define("promise", JsProperty.DataNonEnum(p));
            o.Define("resolve", JsProperty.DataNonEnum(MakeSettler(realm, loop, p, true)));
            o.Define("reject", JsProperty.DataNonEnum(MakeSettler(realm, loop, p, false)));
            return o;
        }, 0)));
    }

    private static JsPromise RequirePromise(object self, string who)
    {
        JsPromise p = self as JsPromise;
        if (p == null) throw JsErr.Type("Promise.prototype." + who + " called on non-promise");
        return p;
    }

    private static JsArray ToArray(JsRealm realm, object[] items)
    {
        JsArray arr = realm.NewArray();
        for (int i = 0; i < items.Length; i++) arr.Items.Add(items[i]);
        return arr;
    }

    /// <summary>The promise to watch for a value passed to Promise.all/race/...</summary>
    private static JsPromise Watched(JsRealm realm, JsEventLoop loop, object v)
    {
        JsPromise p = v as JsPromise;
        if (p != null) return p;
        p = new JsPromise(realm.PromisePrototype);
        FulfillPromise(loop, p, v);
        return p;
    }

    private static JsFunction MakeSettler(JsRealm realm, JsEventLoop loop, JsPromise target, bool fulfill)
    {
        bool[] settled = new bool[1];
        string name = fulfill ? "resolve" : "reject";
        return realm.MakeMethod(name, delegate (JsCallArgs a)
        {
            if (settled[0]) return JsUndefined.Instance;
            settled[0] = true;
            object v = a.ArgCount > 0 ? a.Get(0) : JsUndefined.Instance;
            if (fulfill) FulfillPromise(loop, target, v);
            else RejectPromise(loop, target, v);
            return JsUndefined.Instance;
        }, 1);
    }

    public static void RejectPromise(JsEventLoop loop, JsPromise p, object reason)
    {
        if (p.State != JsPromise.Pending) return;
        p.State = JsPromise.Rejected;
        p.Result = reason;
        FlushReactions(loop, p);
    }

    /// <summary>Fulfill (or adopt a promise/thenable).</summary>
    public static void FulfillPromise(JsEventLoop loop, JsPromise p, object value)
    {
        if (p.State != JsPromise.Pending) return;

        // adopting another promise
        JsPromise inner = value as JsPromise;
        if (inner != null)
        {
            if (inner == p) return;    // resolving with itself: stays pending (cycle)
            JsReaction r = new JsReaction();
            r.OnFulfilled = loop.Interp.Realm.MakeMethod("resolve", delegate (JsCallArgs a)
            {
                FulfillPromise(loop, p, a.ArgCount > 0 ? a.Get(0) : JsUndefined.Instance);
                return JsUndefined.Instance;
            }, 1);
            r.OnRejected = loop.Interp.Realm.MakeMethod("reject", delegate (JsCallArgs a)
            {
                RejectPromise(loop, p, a.ArgCount > 0 ? a.Get(0) : JsUndefined.Instance);
                return JsUndefined.Instance;
            }, 1);
            ScheduleReaction(loop, inner, r);
            return;
        }

        // adopting a thenable (an object with a callable `then`)
        JsObject jo = value as JsObject;
        if (jo != null)
        {
            object thenVal;
            if (jo.GetProperty("then", out thenVal) && thenVal is JsFunction)
            {
                bool[] settled = new bool[1];
                JsFunction resolveFn = loop.Interp.Realm.MakeMethod("resolve", delegate (JsCallArgs a)
                {
                    if (settled[0]) return JsUndefined.Instance;
                    settled[0] = true;
                    FulfillPromise(loop, p, a.ArgCount > 0 ? a.Get(0) : JsUndefined.Instance);
                    return JsUndefined.Instance;
                }, 1);
                JsFunction rejectFn = loop.Interp.Realm.MakeMethod("reject", delegate (JsCallArgs a)
                {
                    if (settled[0]) return JsUndefined.Instance;
                    settled[0] = true;
                    RejectPromise(loop, p, a.ArgCount > 0 ? a.Get(0) : JsUndefined.Instance);
                    return JsUndefined.Instance;
                }, 1);
                try { loop.Interp.Call((JsFunction)thenVal, jo, new object[] { resolveFn, rejectFn }, false); }
                catch (JsThrown jt)
                {
                    if (!settled[0]) { settled[0] = true; RejectPromise(loop, p, jt.Value); }
                }
                catch (Exception) { if (!settled[0]) { settled[0] = true; RejectPromise(loop, p, JsUndefined.Instance); } }
                return;
            }
        }

        p.State = JsPromise.Fulfilled;
        p.Result = value;
        FlushReactions(loop, p);
    }

    private static void ScheduleReaction(JsEventLoop loop, JsPromise p, JsReaction r)
    {
        if (p.State == JsPromise.Pending)
        {
            p.Reactions.Add(r);
            return;
        }
        bool fulfilled = p.State == JsPromise.Fulfilled;
        object value = p.Result;
        loop.EnqueueJob(delegate { RunReaction(loop, r, value, fulfilled); });
    }

    private static void FlushReactions(JsEventLoop loop, JsPromise p)
    {
        if (p.Reactions.Count == 0) return;
        List<JsReaction> list = p.Reactions;
        p.Reactions = new List<JsReaction>();
        bool fulfilled = p.State == JsPromise.Fulfilled;
        object value = p.Result;
        for (int i = 0; i < list.Count; i++)
        {
            JsReaction r = list[i];
            loop.EnqueueJob(delegate { RunReaction(loop, r, value, fulfilled); });
        }
    }

    private static void RunReaction(JsEventLoop loop, JsReaction r, object value, bool fulfilled)
    {
        JsFunction handler = fulfilled ? r.OnFulfilled : r.OnRejected;
        if (handler == null)
        {
            // no handler: pass the outcome through to the derived promise
            if (r.Next != null)
            {
                if (fulfilled) FulfillPromise(loop, r.Next, value);
                else RejectPromise(loop, r.Next, value);
            }
            return;
        }

        object result;
        try
        {
            result = loop.Interp.Call(handler, JsUndefined.Instance, new object[] { value }, false);
        }
        catch (JsThrown jt)
        {
            if (r.Next != null) RejectPromise(loop, r.Next, jt.Value);
            return;
        }
        catch (Exception ex)
        {
            if (r.Next != null)
                RejectPromise(loop, r.Next, JsBuiltins.CreateErrorObject(loop.Interp.Realm, "Error", ex.Message));
            return;
        }
        if (r.Next != null) FulfillPromise(loop, r.Next, result);
    }

    // ---- timers / microtasks ----------------------------------------------

    private static void BuildTimers(JsRealm realm, JsEventLoop loop)
    {
        realm.Global.Define("setTimeout", JsProperty.Data(realm.MakeMethod("setTimeout", delegate (JsCallArgs a)
        {
            JsFunction fn = RequireFunction(a.Get(0), "setTimeout");
            double delay = a.ArgCount > 1 ? JsValueOps.ToNumber(a.Get(1)) : 0;
            if (double.IsNaN(delay) || delay < 0) delay = 0;
            return (double)loop.SetTimeout(fn, Tail(a, 2), delay, 0);
        }, 2)));

        realm.Global.Define("setInterval", JsProperty.Data(realm.MakeMethod("setInterval", delegate (JsCallArgs a)
        {
            JsFunction fn = RequireFunction(a.Get(0), "setInterval");
            double delay = a.ArgCount > 1 ? JsValueOps.ToNumber(a.Get(1)) : 0;
            if (double.IsNaN(delay) || delay < 0) delay = 0;
            return (double)loop.SetTimeout(fn, Tail(a, 2), delay, delay);
        }, 2)));

        JsNativeFn clear = delegate (JsCallArgs a)
        {
            loop.ClearTimer(JsValueOps.ToNumber(a.Get(0)));
            return JsUndefined.Instance;
        };
        realm.Global.Define("clearTimeout", JsProperty.Data(realm.MakeMethod("clearTimeout", clear, 1)));
        realm.Global.Define("clearInterval", JsProperty.Data(realm.MakeMethod("clearInterval", clear, 1)));

        realm.Global.Define("queueMicrotask", JsProperty.Data(realm.MakeMethod("queueMicrotask", delegate (JsCallArgs a)
        {
            JsFunction fn = RequireFunction(a.Get(0), "queueMicrotask");
            loop.EnqueueJob(delegate
            {
                try { loop.Interp.Call(fn, loop.Interp.Realm.Global, new object[0], false); }
                catch (JsThrown jt) { loop.ReportUncaught(jt.Value, jt.Message); }
                catch (Exception ex) { loop.ReportUncaught(null, ex.Message); }
            });
            return JsUndefined.Instance;
        }, 1)));

        // requestAnimationFrame: approximate as a ~16 ms timeout.
        realm.Global.Define("requestAnimationFrame", JsProperty.Data(realm.MakeMethod("requestAnimationFrame", delegate (JsCallArgs a)
        {
            JsFunction fn = RequireFunction(a.Get(0), "requestAnimationFrame");
            return (double)loop.SetTimeout(fn, new object[] { (double)(DateTime.UtcNow.Ticks % 1000000000L) / 10000.0 }, 16, 0);
        }, 1)));
        realm.Global.Define("cancelAnimationFrame", JsProperty.Data(realm.MakeMethod("cancelAnimationFrame", clear, 1)));
    }

    private static JsFunction RequireFunction(object v, string who)
    {
        JsFunction fn = v as JsFunction;
        if (fn == null) throw JsErr.Type(who + " handler " + DescribeValue(v) + " is not a function");
        return fn;
    }

    // ---- fetch ------------------------------------------------------------

    private static void BuildFetch(JsRealm realm, JsEventLoop loop)
    {
        realm.ResponsePrototype = realm.NewObject();
        realm.ResponsePrototype.SetWellKnown(JsWellKnown.ToStringTag, "Response");

        realm.Global.Define("fetch", JsProperty.Data(realm.MakeMethod("fetch", delegate (JsCallArgs a)
        {
            string input = JsValueOps.ToStringValue(a.Get(0));
            string url = WebUrl.Resolve(loop.LocationHref, input);
            string method = "GET";
            string headers = "";
            string body = null;

            if (a.ArgCount > 1)
            {
                JsObject init = a.Get(1) as JsObject;
                if (init != null)
                {
                    object v;
                    if (init.GetProperty("method", out v)) method = JsValueOps.ToStringValue(v);
                    if (init.GetProperty("headers", out v)) headers = FlattenHeaders(v);
                    if (init.GetProperty("body", out v) && !(v is JsUndefined) && v != null)
                        body = JsValueOps.ToStringValue(v);
                }
            }

            JsPromise resp = new JsPromise(realm.PromisePrototype);
            loop.EnqueueFetch(url, method, headers, body, delegate (JsFetchResult res)
            {
                if (res == null || res.Error != null)
                    RejectPromise(loop, resp, JsBuiltins.CreateErrorObject(realm, "TypeError", "Network request failed"));
                else
                    FulfillPromise(loop, resp, MakeResponse(realm, loop, res));
            });
            return resp;
        }, 2)));
    }

    /// <summary>headers option (object / array / string) -> "Name: value" lines.</summary>
    private static string FlattenHeaders(object v)
    {
        StringBuilder sb = new StringBuilder();
        JsArray arr = v as JsArray;
        if (arr != null)
        {
            for (int i = 0; i < arr.Items.Count; i++)
            {
                object k = null, val = null;

                // [name, value] array pair (the common HeadersInit form)
                JsArray pairA = arr.Items[i] as JsArray;
                if (pairA != null)
                {
                    if (pairA.Items.Count >= 2) { k = pairA.Items[0]; val = pairA.Items[1]; }
                }
                else
                {
                    JsObject pair = arr.Items[i] as JsObject;
                    if (pair != null)
                    {
                        object idx;
                        if (pair.GetProperty("0", out idx) && idx != null) k = idx;
                        else pair.GetProperty("name", out k);
                        if (pair.GetProperty("1", out idx) && idx != null) val = idx;
                        else pair.GetProperty("value", out val);
                    }
                }
                if (k == null || k is JsUndefined || val == null || val is JsUndefined) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(JsValueOps.ToStringValue(k)).Append(": ").Append(JsValueOps.ToStringValue(val));
            }
            return sb.ToString();
        }
        if (v is string) return (string)v;

        JsObject o = v as JsObject;
        if (o != null)
        {
            List<string> keys = o.OwnKeys(true);
            for (int i = 0; i < keys.Count; i++)
            {
                object val;
                if (!o.GetProperty(keys[i], out val)) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(keys[i]).Append(": ").Append(JsValueOps.ToStringValue(val));
            }
            return sb.ToString();
        }
        return "";
    }

    private static JsObject MakeResponse(JsRealm realm, JsEventLoop loop, JsFetchResult res)
    {
        string body = res.Body != null ? res.Body : "";
        string headerText = res.Headers != null ? res.Headers : "";

        JsObject o = new JsObject(realm.ResponsePrototype);
        o.Define("ok", JsProperty.DataNonEnum(res.Status >= 200 && res.Status < 300));
        o.Define("status", JsProperty.DataNonEnum((double)res.Status));
        o.Define("statusText", JsProperty.DataNonEnum(res.StatusText != null ? res.StatusText : ""));
        o.Define("url", JsProperty.DataNonEnum(res.Url != null ? res.Url : ""));
        o.Define("redirected", JsProperty.DataNonEnum(false));
        o.Define("type", JsProperty.DataNonEnum("basic"));
        o.Define("headers", JsProperty.DataNonEnum(MakeHeaders(realm, headerText)));

        o.Define("text", JsProperty.DataNonEnum(realm.MakeMethod("text", delegate (JsCallArgs a)
        {
            return Resolved(realm, loop, body);
        }, 0)));

        o.Define("json", JsProperty.DataNonEnum(realm.MakeMethod("json", delegate (JsCallArgs a)
        {
            try
            {
                return Resolved(realm, loop, JsJson.Parse(realm, body));
            }
            catch (Exception)
            {
                JsPromise bad = new JsPromise(realm.PromisePrototype);
                RejectPromise(loop, bad, JsBuiltins.CreateErrorObject(realm, "SyntaxError", "Unexpected token in JSON"));
                return bad;
            }
        }, 0)));

        return o;
    }

    private static JsObject MakeHeaders(JsRealm realm, string raw)
    {
        JsObject h = realm.NewObject();
        h.Define("get", JsProperty.DataNonEnum(realm.MakeMethod("get", delegate (JsCallArgs a)
        {
            string name = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
            string v = FindHeader(raw, name);
            return v != null ? (object)v : (object)JsNull.Instance;
        }, 1)));
        h.Define("has", JsProperty.DataNonEnum(realm.MakeMethod("has", delegate (JsCallArgs a)
        {
            string name = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
            return FindHeader(raw, name) != null;
        }, 1)));
        h.Define("forEach", JsProperty.DataNonEnum(realm.MakeMethod("forEach", delegate (JsCallArgs a)
        {
            JsFunction cb = a.Get(0) as JsFunction;
            if (cb == null) return JsUndefined.Instance;
            object thisArg = a.ArgCount > 1 ? a.Get(1) : JsUndefined.Instance;
            int start = 0;
            while (start < raw.Length)
            {
                int nl = raw.IndexOf('\n', start);
                if (nl < 0) nl = raw.Length;
                string line = raw.Substring(start, nl - start).Trim();
                start = nl + 1;
                if (line.Length == 0) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim();
                try { realm.Interp.Call(cb, thisArg, new object[] { value, name, h }, false); }
                catch (JsThrown) { }
                catch (Exception) { }
            }
            return JsUndefined.Instance;
        }, 1)));
        return h;
    }


    /// <summary>Case-insensitive lookup over "Name: value\r\n" header lines.</summary>
    private static string FindHeader(string raw, string lowerName)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        int start = 0;
        while (start < raw.Length)
        {
            int nl = raw.IndexOf('\n', start);
            if (nl < 0) nl = raw.Length;
            string line = raw.Substring(start, nl - start).Trim();
            start = nl + 1;
            if (line.Length == 0) continue;
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string name = line.Substring(0, colon).Trim();
            if (FoldTo(name) == lowerName) return line.Substring(colon + 1).Trim();
        }
        return null;
    }

    private static string FoldTo(string s)
    {
        if (s == null) return "";
        char[] c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
            if (c[i] >= 'A' && c[i] <= 'Z') c[i] = (char)(c[i] + 32);
        return new string(c);
    }

    private static JsPromise Resolved(JsRealm realm, JsEventLoop loop, object v)
    {
        JsPromise p = new JsPromise(realm.PromisePrototype);
        FulfillPromise(loop, p, v);
        return p;
    }

    // ---- XMLHttpRequest ---------------------------------------------------

    private static void BuildXhr(JsRealm realm, JsEventLoop loop)
    {
        JsObject proto = realm.NewObject();
        realm.XhrPrototype = proto;
        proto.SetWellKnown(JsWellKnown.ToStringTag, "XMLHttpRequest");
        proto.Define("UNSENT", JsProperty.DataNonEnum(0.0));
        proto.Define("OPENED", JsProperty.DataNonEnum(1.0));
        proto.Define("HEADERS_RECEIVED", JsProperty.DataNonEnum(2.0));
        proto.Define("LOADING", JsProperty.DataNonEnum(3.0));
        proto.Define("DONE", JsProperty.DataNonEnum(4.0));

        DefineMethod(realm, proto, "open", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "open");
            x.Method = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToUpperInvariant() : "GET";
            string url = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
            x.Url = WebUrl.Resolve(loop.LocationHref, url);
            x.Async = a.ArgCount < 3 || JsValueOps.ToBoolean(a.Get(2));  // async flag: we always run async
            x.ReadyState = 1;
            x.Status = 0;
            x.StatusText = "";
            x.ResponseText = "";
            x.ResponseHeaders = "";
            x.ResponseUrl = "";
            x.Aborted = false;
            x.Fire("readystatechange");
            return JsUndefined.Instance;
        }, 3);

        DefineMethod(realm, proto, "setRequestHeader", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "setRequestHeader");
            string name = JsValueOps.ToStringValue(a.Get(0)).Trim();
            string value = JsValueOps.ToStringValue(a.Get(1)).Trim();
            if (name.Length == 0) return JsUndefined.Instance;
            if (x.RequestHeaders.Length > 0) x.RequestHeaders += "\r\n";
            x.RequestHeaders += name + ": " + value;
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, proto, "send", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "send");
            if (x.ReadyState != 1) throw JsErr.Type("XHR send() before open()");
            string body = null;
            if (a.ArgCount > 0 && !(a.Get(0) is JsUndefined) && a.Get(0) != null)
                body = JsValueOps.ToStringValue(a.Get(0));
            x.RequestBody = body;
            x.ReadyState = 2;
            x.Fire("readystatechange");

            string method = x.Method, url = x.Url, headers = x.RequestHeaders;
            loop.EnqueueFetch(url, method, headers, body, delegate (JsFetchResult res)
            {
                if (x.Aborted) return;
                if (res == null || res.Error != null)
                {
                    x.Status = 0;
                    x.StatusText = "";
                    x.ResponseText = "";
                    x.ReadyState = 4;
                    x.Fire("readystatechange");
                    x.Fire("error");
                    x.Fire("loadend");
                    return;
                }
                x.Status = res.Status;
                x.StatusText = res.StatusText != null ? res.StatusText : "";
                x.ResponseHeaders = res.Headers != null ? res.Headers : "";
                x.ResponseUrl = res.Url != null ? res.Url : url;
                x.ResponseText = res.Body != null ? res.Body : "";
                x.ReadyState = 4;
                x.Fire("readystatechange");
                x.Fire("load");
                x.Fire("loadend");
            });
            return JsUndefined.Instance;
        }, 1);

        DefineMethod(realm, proto, "abort", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "abort");
            if (x.ReadyState == 0 || x.ReadyState == 4) return JsUndefined.Instance;
            x.Aborted = true;
            x.ReadyState = 0;
            x.Fire("readystatechange");
            x.Fire("abort");
            x.Fire("loadend");
            return JsUndefined.Instance;
        }, 0);

        DefineMethod(realm, proto, "getAllResponseHeaders", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "getAllResponseHeaders");
            return x.ReadyState >= 2 ? (object)x.ResponseHeaders : (object)"";
        }, 0);

        DefineMethod(realm, proto, "getResponseHeader", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "getResponseHeader");
            if (x.ReadyState < 2) return JsNull.Instance;
            string name = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
            string v = FindHeader(x.ResponseHeaders, name);
            return v != null ? (object)v : (object)JsNull.Instance;
        }, 2);

        DefineMethod(realm, proto, "overrideMimeType", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);

        // readyState / status / response accessors
        DefineAccessor(realm, proto, "readyState", delegate (JsCallArgs a)
        {
            return (double)RequireXhr(a.ThisValue, "readyState").ReadyState;
        }, null);
        DefineAccessor(realm, proto, "status", delegate (JsCallArgs a)
        {
            return (double)RequireXhr(a.ThisValue, "status").Status;
        }, null);
        DefineAccessor(realm, proto, "statusText", delegate (JsCallArgs a)
        {
            return RequireXhr(a.ThisValue, "statusText").StatusText;
        }, null);
        DefineAccessor(realm, proto, "responseText", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "responseText");
            return x.ReadyState >= 2 ? (object)x.ResponseText : (object)"";
        }, null);
        DefineAccessor(realm, proto, "responseURL", delegate (JsCallArgs a)
        {
            return RequireXhr(a.ThisValue, "responseURL").ResponseUrl;
        }, null);
        DefineAccessor(realm, proto, "response", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "response");
            if (x.ReadyState < 2) return JsUndefined.Instance;
            if (x.ResponseType == "json")
            {
                try { return JsJson.Parse(x.Realm, x.ResponseText); }
                catch (Exception) { return JsNull.Instance; }
            }
            if (x.ResponseType == "arraybuffer" || x.ResponseType == "blob") return JsNull.Instance;
            return x.ResponseText;
        }, null);
        DefineAccessor(realm, proto, "responseType", delegate (JsCallArgs a)
        {
            return RequireXhr(a.ThisValue, "responseType").ResponseType;
        }, delegate (JsCallArgs a)
        {
            RequireXhr(a.ThisValue, "responseType").ResponseType = JsValueOps.ToStringValue(a.Get(0));
            return JsUndefined.Instance;
        });
        DefineAccessor(realm, proto, "withCredentials", delegate (JsCallArgs a) { return false; }, delegate (JsCallArgs a) { return JsUndefined.Instance; });

        // events. The prototype carries readable defaults; each INSTANCE also
        // gets an own `on*` slot (created below) so that `x.onload = fn` lands
        // on the instance rather than being written through to the shared
        // prototype (SetProperty writes to the property's own holder).
        string[] events = new string[] { "readystatechange", "load", "error", "timeout", "abort", "loadend", "progress" };
        for (int i = 0; i < events.Length; i++)
        {
            string type = events[i];
            proto.Define("on" + type, JsProperty.DataNonEnum(JsNull.Instance));
        }
        DefineMethod(realm, proto, "addEventListener", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "addEventListener");
            string type = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
            JsFunction fn = a.Get(1) as JsFunction;
            if (fn == null) return JsUndefined.Instance;
            List<JsFunction> fns;
            if (!x.Listeners.TryGetValue(type, out fns)) { fns = new List<JsFunction>(); x.Listeners[type] = fns; }
            if (!fns.Contains(fn)) fns.Add(fn);
            return JsUndefined.Instance;
        }, 2);
        DefineMethod(realm, proto, "removeEventListener", delegate (JsCallArgs a)
        {
            JsXhrObject x = RequireXhr(a.ThisValue, "removeEventListener");
            string type = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
            JsFunction fn = a.Get(1) as JsFunction;
            List<JsFunction> fns;
            if (fn != null && x.Listeners.TryGetValue(type, out fns)) fns.Remove(fn);
            return JsUndefined.Instance;
        }, 2);

        realm.Global.Define("XMLHttpRequest", JsProperty.Data(
            realm.MakeCtor("XMLHttpRequest", delegate (JsCallArgs a)
            {
                JsXhrObject x = new JsXhrObject(proto, realm, loop);
                for (int i = 0; i < events.Length; i++)
                    x.Define("on" + events[i], JsProperty.DataNonEnum(JsNull.Instance));
                return x;
            }, 0, proto)));
    }

    private static JsXhrObject RequireXhr(object self, string who)
    {
        JsXhrObject x = self as JsXhrObject;
        if (x == null) throw JsErr.Type("Illegal invocation: XMLHttpRequest.prototype." + who);
        return x;
    }

    // ---- location ---------------------------------------------------------

    private static void BuildLocation(JsRealm realm, JsEventLoop loop)
    {
        JsObject loc = realm.NewObject();
        realm.LocationObj = loc;

        DefineAccessor(realm, loc, "href",
            delegate (JsCallArgs a) { return loop.LocationHref; },
            delegate (JsCallArgs a)
            {
                loop.Navigate(WebUrl.Resolve(loop.LocationHref, JsValueOps.ToStringValue(a.Get(0))));
                return JsUndefined.Instance;
            });

        DefineAccessor(realm, loc, "protocol", Parts_get(loop, PartScheme), SetHrefIgnored());
        DefineAccessor(realm, loc, "host", Parts_get(loop, PartHostPort), SetHrefIgnored());
        DefineAccessor(realm, loc, "hostname", Parts_get(loop, PartHost), SetHrefIgnored());
        DefineAccessor(realm, loc, "port", Parts_get(loop, PartPort), SetHrefIgnored());
        DefineAccessor(realm, loc, "pathname", Parts_get(loop, PartPath), SetHrefIgnored());
        DefineAccessor(realm, loc, "search", Parts_get(loop, PartSearch), SetHrefIgnored());
        DefineAccessor(realm, loc, "hash", Parts_get(loop, PartHash), SetHrefIgnored());
        DefineAccessor(realm, loc, "origin", Parts_get(loop, PartOrigin), null);

        DefineMethod(realm, loc, "assign", delegate (JsCallArgs a)
        {
            loop.Navigate(WebUrl.Resolve(loop.LocationHref, JsValueOps.ToStringValue(a.Get(0))));
            return JsUndefined.Instance;
        }, 1);
        DefineMethod(realm, loc, "replace", delegate (JsCallArgs a)
        {
            loop.Navigate(WebUrl.Resolve(loop.LocationHref, JsValueOps.ToStringValue(a.Get(0))));
            return JsUndefined.Instance;
        }, 1);
        DefineMethod(realm, loc, "reload", delegate (JsCallArgs a)
        {
            loop.Reload();
            return JsUndefined.Instance;
        }, 0);
        DefineMethod(realm, loc, "toString", delegate (JsCallArgs a) { return loop.LocationHref; }, 0);
        DefineMethod(realm, loc, "toJSON", delegate (JsCallArgs a) { return loop.LocationHref; }, 0);

        realm.Global.Define("location", JsProperty.Data(loc));
    }

    private const int PartScheme = 0, PartHostPort = 1, PartHost = 2, PartPort = 3,
                      PartPath = 4, PartSearch = 5, PartHash = 6, PartOrigin = 7;

    private static JsNativeFn Parts_get(JsEventLoop loop, int which)
    {
        return delegate (JsCallArgs a)
        {
            string scheme, host, port, path, query, hash;
            SplitUrl(loop.LocationHref, out scheme, out host, out port, out path, out query, out hash);
            switch (which)
            {
                case PartScheme: return scheme;
                case PartHostPort:
                    if (port.Length == 0) return host;
                    return host + ":" + port;
                case PartHost: return host;
                case PartPort: return port;
                case PartPath: return path;
                case PartSearch: return query;
                case PartHash: return hash;
                case PartOrigin:
                    if (scheme.Length == 0) return "null";
                    return scheme + "//" + host + (port.Length > 0 ? ":" + port : "");
            }
            return "";
        };
    }

    private static JsNativeFn SetHrefIgnored()
    {
        // assigning location.pathname etc. is a no-op in practice; href is the
        // supported way to navigate, and hosts almost never set the parts.
        return delegate (JsCallArgs a) { return JsUndefined.Instance; };
    }

    /// <summary>Parse a URL into scheme/host/port/path/query/hash pieces.</summary>
    public static void SplitUrl(string url, out string scheme, out string host, out string port,
        out string path, out string query, out string hash)
    {
        scheme = ""; host = ""; port = ""; path = "/"; query = ""; hash = "";
        if (string.IsNullOrEmpty(url)) return;

        string rest = url;
        int hi = rest.IndexOf('#');
        if (hi >= 0) { hash = rest.Substring(hi); rest = rest.Substring(0, hi); }
        int qi = rest.IndexOf('?');
        if (qi >= 0) { query = rest.Substring(qi); rest = rest.Substring(0, qi); }

        int si = rest.IndexOf("://");
        if (si > 0)
        {
            scheme = rest.Substring(0, si) + ":";
            int start = si + 3;
            int slash = rest.IndexOf('/', start);
            string authority = slash < 0 ? rest.Substring(start) : rest.Substring(start, slash - start);
            int colon = authority.IndexOf(':');
            if (colon > 0 && authority.LastIndexOf(':') == colon)
            {
                host = authority.Substring(0, colon);
                port = authority.Substring(colon + 1);
            }
            else host = authority;
            path = slash < 0 ? "/" : rest.Substring(slash);
            if (path.Length == 0) path = "/";
            return;
        }

        // opaque (about:blank) or scheme-relative-less path
        int colon2 = rest.IndexOf(':');
        if (colon2 > 0)
        {
            scheme = rest.Substring(0, colon2 + 1);
            path = rest.Substring(colon2 + 1);
            if (path.Length == 0) path = "";
        }
        else path = rest.Length > 0 ? rest : "/";
    }

    // ---- navigator --------------------------------------------------------

    private static void BuildNavigator(JsRealm realm, JsEventLoop loop)
    {
        JsObject nav = realm.NewObject();
        realm.NavigatorObj = nav;

        DefineAccessor(realm, nav, "userAgent", delegate (JsCallArgs a) { return loop.UserAgent; }, null);
        DefineAccessor(realm, nav, "appVersion", delegate (JsCallArgs a) { return loop.UserAgent; }, null);
        DefineAccessor(realm, nav, "platform", delegate (JsCallArgs a) { return loop.Platform; }, null);
        DefineAccessor(realm, nav, "language", delegate (JsCallArgs a) { return loop.Language; }, null);
        DefineAccessor(realm, nav, "vendor", delegate (JsCallArgs a) { return ""; }, null);
        DefineAccessor(realm, nav, "onLine", delegate (JsCallArgs a) { return true; }, null);
        DefineAccessor(realm, nav, "cookieEnabled", delegate (JsCallArgs a) { return true; }, null);

        nav.Define("languages", JsProperty.DataNonEnum(MakeStringArray(realm, loop.Language)));
        nav.Define("hardwareConcurrency", JsProperty.DataNonEnum(2.0));
        nav.Define("maxTouchPoints", JsProperty.DataNonEnum(0.0));
        nav.Define("product", JsProperty.DataNonEnum("Gecko"));
        nav.Define("appName", JsProperty.DataNonEnum("Netscape"));
        nav.Define("appCodeName", JsProperty.DataNonEnum("Mozilla"));
        nav.Define("webdriver", JsProperty.DataNonEnum(false));
        DefineMethod(realm, nav, "javaEnabled", delegate (JsCallArgs a) { return false; }, 0);
        DefineMethod(realm, nav, "getGamepads", delegate (JsCallArgs a) { return realm.NewArray(); }, 0);

        realm.Global.Define("navigator", JsProperty.Data(nav));
    }

    private static JsArray MakeStringArray(JsRealm realm, string s)
    {
        JsArray arr = realm.NewArray();
        arr.Items.Add(s);
        return arr;
    }

    // ---- history ----------------------------------------------------------

    private static void BuildHistory(JsRealm realm, JsEventLoop loop)
    {
        JsObject h = realm.NewObject();
        h.Define("length", JsProperty.DataNonEnum(1.0));
        h.Define("state", JsProperty.DataNonEnum(JsNull.Instance));

        DefineMethod(realm, h, "pushState", delegate (JsCallArgs a)
        {
            object st = a.ArgCount > 0 ? a.Get(0) : JsNull.Instance;
            h.Define("state", JsProperty.DataNonEnum(st));
            return JsUndefined.Instance;
        }, 2);
        DefineMethod(realm, h, "replaceState", delegate (JsCallArgs a)
        {
            object st = a.ArgCount > 0 ? a.Get(0) : JsNull.Instance;
            h.Define("state", JsProperty.DataNonEnum(st));
            return JsUndefined.Instance;
        }, 2);
        DefineMethod(realm, h, "back", delegate (JsCallArgs a) { loop.HistoryBack(loop.LocationHref); return JsUndefined.Instance; }, 0);
        DefineMethod(realm, h, "forward", delegate (JsCallArgs a) { loop.HistoryForward(loop.LocationHref); return JsUndefined.Instance; }, 0);
        DefineMethod(realm, h, "go", delegate (JsCallArgs a)
        {
            double d = JsValueOps.ToNumber(a.Get(0));
            if (d < 0) loop.HistoryBack(loop.LocationHref);
            else if (d > 0) loop.HistoryForward(loop.LocationHref);
            return JsUndefined.Instance;
        }, 1);

        realm.Global.Define("history", JsProperty.Data(h));
    }

    // ---- window helpers ---------------------------------------------------

    private static void BuildWindow(JsRealm realm, JsEventLoop loop)
    {
        // dialogs: report to the console instead of blocking the OS
        realm.Global.Define("alert", JsProperty.Data(realm.MakeMethod("alert", delegate (JsCallArgs a)
        {
            if (JsBuiltins.ConsoleOutput != null)
                JsBuiltins.ConsoleOutput("[alert] " + JsValueOps.ToStringValue(a.Get(0)));
            return JsUndefined.Instance;
        }, 1)));
        realm.Global.Define("confirm", JsProperty.Data(realm.MakeMethod("confirm", delegate (JsCallArgs a)
        {
            if (JsBuiltins.ConsoleOutput != null)
                JsBuiltins.ConsoleOutput("[confirm] " + JsValueOps.ToStringValue(a.Get(0)));
            return true;
        }, 1)));
        realm.Global.Define("prompt", JsProperty.Data(realm.MakeMethod("prompt", delegate (JsCallArgs a)
        {
            if (JsBuiltins.ConsoleOutput != null)
                JsBuiltins.ConsoleOutput("[prompt] " + JsValueOps.ToStringValue(a.Get(0)));
            return a.ArgCount > 1 ? a.Get(1) : (object)"";
        }, 2)));

        // viewport (updated by the host on resize)
        DefineAccessor(realm, realm.Global, "innerWidth", delegate (JsCallArgs a) { return (double)loop.ViewportWidth; }, null);
        DefineAccessor(realm, realm.Global, "innerHeight", delegate (JsCallArgs a) { return (double)loop.ViewportHeight; }, null);
        DefineAccessor(realm, realm.Global, "outerWidth", delegate (JsCallArgs a) { return (double)loop.ViewportWidth; }, null);
        DefineAccessor(realm, realm.Global, "outerHeight", delegate (JsCallArgs a) { return (double)loop.ViewportHeight; }, null);
        DefineAccessor(realm, realm.Global, "scrollX", delegate (JsCallArgs a) { return 0.0; }, null);
        DefineAccessor(realm, realm.Global, "scrollY", delegate (JsCallArgs a) { return 0.0; }, null);
        DefineAccessor(realm, realm.Global, "pageXOffset", delegate (JsCallArgs a) { return 0.0; }, null);
        DefineAccessor(realm, realm.Global, "pageYOffset", delegate (JsCallArgs a) { return 0.0; }, null);
        DefineAccessor(realm, realm.Global, "devicePixelRatio", delegate (JsCallArgs a) { return 1.0; }, null);
        DefineAccessor(realm, realm.Global, "screenX", delegate (JsCallArgs a) { return 0.0; }, null);
        DefineAccessor(realm, realm.Global, "screenY", delegate (JsCallArgs a) { return 0.0; }, null);

        // scrolling: accepted, but the host owns the scroll offset
        JsNativeFn scrollNoop = delegate (JsCallArgs a) { return JsUndefined.Instance; };
        realm.Global.Define("scrollTo", JsProperty.Data(realm.MakeMethod("scrollTo", scrollNoop, 2)));
        realm.Global.Define("scroll", JsProperty.Data(realm.MakeMethod("scroll", scrollNoop, 2)));
        realm.Global.Define("scrollBy", JsProperty.Data(realm.MakeMethod("scrollBy", scrollNoop, 2)));
        realm.Global.Define("focus", JsProperty.Data(realm.MakeMethod("focus", scrollNoop, 0)));
        realm.Global.Define("blur", JsProperty.Data(realm.MakeMethod("blur", scrollNoop, 0)));
        realm.Global.Define("moveTo", JsProperty.Data(realm.MakeMethod("moveTo", scrollNoop, 2)));
        realm.Global.Define("resizeTo", JsProperty.Data(realm.MakeMethod("resizeTo", scrollNoop, 2)));

        // frame/window aliases: a top-level page is its own parent/top/frames
        realm.Global.Define("parent", JsProperty.Data(realm.Global));
        realm.Global.Define("top", JsProperty.Data(realm.Global));
        realm.Global.Define("frames", JsProperty.Data(realm.Global));
        realm.Global.Define("closed", JsProperty.DataNonEnum(false));
        realm.Global.Define("name", JsProperty.DataNonEnum(""));

        // performance.now()
        JsObject perf = realm.NewObject();
        long start = DateTime.UtcNow.Ticks;
        DefineMethod(realm, perf, "now", delegate (JsCallArgs a)
        {
            return (double)(DateTime.UtcNow.Ticks - start) / 10000.0;
        }, 0);
        DefineAccessor(realm, perf, "timeOrigin", delegate (JsCallArgs a)
        {
            return (double)start / 10000.0 - 62135596800000.0;
        }, null);
        DefineMethod(realm, perf, "mark", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);
        DefineMethod(realm, perf, "measure", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);
        realm.Global.Define("performance", JsProperty.Data(perf));
    }
}
