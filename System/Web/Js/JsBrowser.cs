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
        BuildStorage(realm);
        BuildScreen(realm, loop);
        BuildMedia(realm, loop);
        BuildBase64(realm);
        BuildUrl(realm, loop);
        BuildFormData(realm);
        BuildBlob(realm, loop);
        BuildAbort(realm);
        BuildCrypto(realm);
        BuildObservers(realm);
        BuildDomParser(realm);
        BuildWebSocket(realm, loop);
        BuildIndexedDb(realm, loop);
        BuildWorker(realm, loop);
        BuildCustomElements(realm, loop);
        BuildStructuredClone(realm);
        BuildTextEncoding(realm);
        BuildCssObject(realm);
        BuildEventSource(realm);
    }

    // ---- small helpers ----------------------------------------------------

    private static int ToInteger(object v)
    {
        double d = JsValueOps.ToNumber(v);
        if (double.IsNaN(d) || d == 0.0) return 0;
        if (double.IsPositiveInfinity(d)) return int.MaxValue;
        if (double.IsNegativeInfinity(d)) return int.MinValue;
        return (int)d;
    }

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

    /// <summary>Convert any value or thenable to a JsPromise instance.</summary>
    public static JsPromise CoerceToPromise(JsRealm realm, JsEventLoop loop, object v)
    {
        JsPromise p = v as JsPromise;
        if (p != null) return p;
        p = new JsPromise(realm.PromisePrototype);
        FulfillPromise(loop, p, v);
        return p;
    }

    /// <summary>The promise to watch for a value passed to Promise.all/race/...</summary>
    private static JsPromise Watched(JsRealm realm, JsEventLoop loop, object v)
    {
        return CoerceToPromise(realm, loop, v);
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

        // requestIdleCallback / cancelIdleCallback
        realm.Global.Define("requestIdleCallback", JsProperty.Data(realm.MakeMethod("requestIdleCallback", delegate (JsCallArgs a)
        {
            JsFunction fn = RequireFunction(a.Get(0), "requestIdleCallback");
            JsObject deadline = realm.NewObject();
            deadline.Define("didTimeout", JsProperty.DataNonEnum(false));
            DefineMethod(realm, deadline, "timeRemaining", delegate (JsCallArgs da) { return 50.0; }, 0);
            return (double)loop.SetTimeout(fn, new object[] { deadline }, 1, 0);
        }, 1)));
        realm.Global.Define("cancelIdleCallback", JsProperty.Data(realm.MakeMethod("cancelIdleCallback", clear, 1)));
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

        JsObject headersProto = realm.NewObject();
        headersProto.SetWellKnown(JsWellKnown.ToStringTag, "Headers");

        DefineMethod(realm, headersProto, "get", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            string name = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
            if (self != null && self.Map.TryGetValue(name, out string v)) return v;
            return JsNull.Instance;
        }, 1);

        DefineMethod(realm, headersProto, "has", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            string name = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
            return self != null && self.Map.ContainsKey(name);
        }, 1);

        DefineMethod(realm, headersProto, "set", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            if (self != null)
            {
                string name = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
                string val = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
                self.Map[name] = val;
            }
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, headersProto, "append", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            if (self != null)
            {
                string name = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
                string val = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
                if (self.Map.ContainsKey(name)) self.Map[name] = self.Map[name] + ", " + val;
                else self.Map[name] = val;
            }
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, headersProto, "delete", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            if (self != null)
            {
                string name = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "";
                self.Map.Remove(name);
            }
            return JsUndefined.Instance;
        }, 1);

        DefineMethod(realm, headersProto, "forEach", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            JsFunction cb = a.Get(0) as JsFunction;
            if (self == null || cb == null) return JsUndefined.Instance;
            object thisArg = a.ArgCount > 1 ? a.Get(1) : JsUndefined.Instance;
            foreach (var kvp in self.Map)
            {
                try { realm.Interp.Call(cb, thisArg, new object[] { kvp.Value, kvp.Key, self }, false); }
                catch (JsThrown) { }
                catch (Exception) { }
            }
            return JsUndefined.Instance;
        }, 1);

        DefineMethod(realm, headersProto, "entries", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            JsArray arr = realm.NewArray();
            if (self != null)
            {
                foreach (var kvp in self.Map)
                {
                    JsArray pair = realm.NewArray();
                    pair.Items.Add(kvp.Key);
                    pair.Items.Add(kvp.Value);
                    arr.Items.Add(pair);
                }
            }
            return arr;
        }, 0);

        DefineMethod(realm, headersProto, "keys", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            JsArray arr = realm.NewArray();
            if (self != null)
            {
                foreach (var kvp in self.Map) arr.Items.Add(kvp.Key);
            }
            return arr;
        }, 0);

        DefineMethod(realm, headersProto, "values", delegate (JsCallArgs a)
        {
            JsHeadersObject self = a.ThisValue as JsHeadersObject;
            JsArray arr = realm.NewArray();
            if (self != null)
            {
                foreach (var kvp in self.Map) arr.Items.Add(kvp.Value);
            }
            return arr;
        }, 0);

        realm.Global.Define("Headers", JsProperty.Data(realm.MakeCtor("Headers", delegate (JsCallArgs a)
        {
            object init = a.ArgCount > 0 ? a.Get(0) : null;
            return MakeHeaders(realm, headersProto, init);
        }, 0, headersProto)));

        realm.Global.Define("Response", JsProperty.Data(realm.MakeCtor("Response", delegate (JsCallArgs a)
        {
            string b = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            JsFetchResult res = new JsFetchResult { Ok = true, Status = 200, StatusText = "OK", Body = b };
            return MakeResponse(realm, loop, res, headersProto);
        }, 0, realm.ResponsePrototype)));

        realm.Global.Define("Request", JsProperty.Data(realm.MakeCtor("Request", delegate (JsCallArgs a)
        {
            string url = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            JsObject req = realm.NewObject();
            req.Define("url", JsProperty.Data(url));
            req.Define("method", JsProperty.Data("GET"));
            req.Define("headers", JsProperty.Data(MakeHeaders(realm, headersProto, "")));
            return req;
        }, 1, realm.NewObject())));

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
                    FulfillPromise(loop, resp, MakeResponse(realm, loop, res, headersProto));
            });
            return resp;
        }, 2)));
    }

    /// <summary>headers option (Headers / object / array / string) -> "Name: value" lines.</summary>
    private static string FlattenHeaders(object v)
    {
        if (v == null || v is JsUndefined || v is JsNull) return "";
        if (v is string s) return s;
        StringBuilder sb = new StringBuilder();

        JsHeadersObject ho = v as JsHeadersObject;
        if (ho != null)
        {
            foreach (var kvp in ho.Map)
            {
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(kvp.Key).Append(": ").Append(kvp.Value);
            }
            return sb.ToString();
        }

        JsArray arr = v as JsArray;
        if (arr != null)
        {
            for (int i = 0; i < arr.Items.Count; i++)
            {
                object k = null, val = null;
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

    private static JsObject MakeResponse(JsRealm realm, JsEventLoop loop, JsFetchResult res, JsObject headersProto = null)
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
        o.Define("headers", JsProperty.DataNonEnum(MakeHeaders(realm, headersProto, headerText)));

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

        o.Define("arrayBuffer", JsProperty.DataNonEnum(realm.MakeMethod("arrayBuffer", delegate (JsCallArgs a)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            JsArrayBuffer ab = new JsArrayBuffer(realm.ObjectPrototype, bytes);
            return Resolved(realm, loop, ab);
        }, 0)));

        o.Define("blob", JsProperty.DataNonEnum(realm.MakeMethod("blob", delegate (JsCallArgs a)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            string ct = FindHeader(headerText, "content-type");
            JsProperty bp;
            JsObject blobProto = (realm.Global.TryGetRaw("Blob", out bp) && bp.Value is JsFunction fn && fn.Prototype is JsObject fnp) ? fnp : realm.NewObject();
            JsBlobObject blob = new JsBlobObject(blobProto, bytes, ct != null ? ct : "");
            return Resolved(realm, loop, blob);
        }, 0)));

        o.Define("clone", JsProperty.DataNonEnum(realm.MakeMethod("clone", delegate (JsCallArgs a)
        {
            return MakeResponse(realm, loop, res, headersProto);
        }, 0)));

        return o;
    }

    private static JsHeadersObject MakeHeaders(JsRealm realm, JsObject headersProto, object init)
    {
        if (headersProto == null)
        {
            JsProperty hp;
            headersProto = (realm.Global.TryGetRaw("Headers", out hp) && hp.Value is JsFunction fn && fn.Prototype is JsObject fnp) ? fnp : realm.NewObject();
        }
        JsHeadersObject h = new JsHeadersObject(headersProto);

        if (init is JsHeadersObject srcHo)
        {
            foreach (var kvp in srcHo.Map) h.Map[kvp.Key] = kvp.Value;
        }
        else if (init is string raw && !string.IsNullOrEmpty(raw))
        {
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
                string name = line.Substring(0, colon).Trim().ToLowerInvariant();
                string value = line.Substring(colon + 1).Trim();
                if (h.Map.ContainsKey(name)) h.Map[name] = h.Map[name] + ", " + value;
                else h.Map[name] = value;
            }
        }
        else if (init is JsArray arr)
        {
            for (int i = 0; i < arr.Items.Count; i++)
            {
                if (arr.Items[i] is JsArray pair && pair.Items.Count >= 2)
                {
                    string k = JsValueOps.ToStringValue(pair.Items[0]).ToLowerInvariant();
                    string val = JsValueOps.ToStringValue(pair.Items[1]);
                    if (h.Map.ContainsKey(k)) h.Map[k] = h.Map[k] + ", " + val;
                    else h.Map[k] = val;
                }
            }
        }
        else if (init is JsObject obj)
        {
            List<string> keys = obj.OwnKeys(true);
            for (int i = 0; i < keys.Count; i++)
            {
                if (obj.GetProperty(keys[i], out object val))
                {
                    string k = keys[i].ToLowerInvariant();
                    h.Map[k] = JsValueOps.ToStringValue(val);
                }
            }
        }
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
        DefineAccessor(realm, nav, "vendor", delegate (JsCallArgs a) { return "Google Inc."; }, null);
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

        // clipboard
        JsObject clip = realm.NewObject();
        DefineMethod(realm, clip, "writeText", delegate (JsCallArgs a)
        {
            JsPromise p = new JsPromise(realm.PromisePrototype);
            FulfillPromise(loop, p, JsUndefined.Instance);
            return p;
        }, 1);
        DefineMethod(realm, clip, "readText", delegate (JsCallArgs a)
        {
            JsPromise p = new JsPromise(realm.PromisePrototype);
            FulfillPromise(loop, p, "");
            return p;
        }, 0);
        nav.Define("clipboard", JsProperty.Data(clip));

        // sendBeacon
        DefineMethod(realm, nav, "sendBeacon", delegate (JsCallArgs a)
        {
            return true;
        }, 2);

        // serviceWorker
        JsObject sw = realm.NewObject();
        DefineMethod(realm, sw, "register", delegate (JsCallArgs a)
        {
            JsPromise p = new JsPromise(realm.PromisePrototype);
            RejectPromise(loop, p, JsBuiltins.CreateErrorObject(realm, "TypeError", "ServiceWorker not supported"));
            return p;
        }, 1);
        sw.Define("ready", JsProperty.Data(new JsPromise(realm.PromisePrototype)));
        DefineMethod(realm, sw, "addEventListener", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);
        DefineMethod(realm, sw, "removeEventListener", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);
        nav.Define("serviceWorker", JsProperty.Data(sw));

        // connection (NetworkInformation API)
        JsObject conn = realm.NewObject();
        conn.Define("effectiveType", JsProperty.Data("4g"));
        conn.Define("rtt", JsProperty.Data(50.0));
        conn.Define("downlink", JsProperty.Data(10.0));
        conn.Define("saveData", JsProperty.Data(false));
        conn.Define("type", JsProperty.Data("wifi"));
        DefineMethod(realm, conn, "addEventListener", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);
        DefineMethod(realm, conn, "removeEventListener", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);
        nav.Define("connection", JsProperty.Data(conn));
        nav.Define("networkInformation", JsProperty.Data(conn));

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
        double epochMs = (double)start / 10000.0 - 62135596800000.0;
        DefineMethod(realm, perf, "now", delegate (JsCallArgs a)
        {
            return (double)(DateTime.UtcNow.Ticks - start) / 10000.0;
        }, 0);
        DefineAccessor(realm, perf, "timeOrigin", delegate (JsCallArgs a)
        {
            return epochMs;
        }, null);
        DefineMethod(realm, perf, "mark", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);
        DefineMethod(realm, perf, "measure", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);
        DefineMethod(realm, perf, "clearMarks", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 0);
        DefineMethod(realm, perf, "clearMeasures", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 0);
        DefineMethod(realm, perf, "getEntries", delegate (JsCallArgs a) { return realm.NewArray(); }, 0);
        DefineMethod(realm, perf, "getEntriesByType", delegate (JsCallArgs a) { return realm.NewArray(); }, 1);
        DefineMethod(realm, perf, "getEntriesByName", delegate (JsCallArgs a) { return realm.NewArray(); }, 1);

        // performance.timing
        JsObject timing = realm.NewObject();
        timing.Define("navigationStart", JsProperty.Data(epochMs));
        timing.Define("unloadEventStart", JsProperty.Data(0.0));
        timing.Define("unloadEventEnd", JsProperty.Data(0.0));
        timing.Define("redirectStart", JsProperty.Data(0.0));
        timing.Define("redirectEnd", JsProperty.Data(0.0));
        timing.Define("fetchStart", JsProperty.Data(epochMs));
        timing.Define("domainLookupStart", JsProperty.Data(epochMs));
        timing.Define("domainLookupEnd", JsProperty.Data(epochMs));
        timing.Define("connectStart", JsProperty.Data(epochMs));
        timing.Define("connectEnd", JsProperty.Data(epochMs));
        timing.Define("secureConnectionStart", JsProperty.Data(epochMs));
        timing.Define("requestStart", JsProperty.Data(epochMs));
        timing.Define("responseStart", JsProperty.Data(epochMs));
        timing.Define("responseEnd", JsProperty.Data(epochMs + 50.0));
        timing.Define("domLoading", JsProperty.Data(epochMs + 60.0));
        timing.Define("domInteractive", JsProperty.Data(epochMs + 100.0));
        timing.Define("domContentLoadedEventStart", JsProperty.Data(epochMs + 120.0));
        timing.Define("domContentLoadedEventEnd", JsProperty.Data(epochMs + 130.0));
        timing.Define("domComplete", JsProperty.Data(epochMs + 150.0));
        timing.Define("loadEventStart", JsProperty.Data(epochMs + 160.0));
        timing.Define("loadEventEnd", JsProperty.Data(epochMs + 170.0));
        perf.Define("timing", JsProperty.Data(timing));

        realm.Global.Define("performance", JsProperty.Data(perf));

        // getSelection
        JsObject sel = realm.NewObject();
        sel.Define("rangeCount", JsProperty.DataNonEnum(0.0));
        sel.Define("isCollapsed", JsProperty.DataNonEnum(true));
        sel.Define("type", JsProperty.DataNonEnum("None"));
        DefineMethod(realm, sel, "getRangeAt", delegate (JsCallArgs a) { return JsNull.Instance; }, 1);
        DefineMethod(realm, sel, "addRange", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);
        DefineMethod(realm, sel, "removeRange", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);
        DefineMethod(realm, sel, "removeAllRanges", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 0);
        DefineMethod(realm, sel, "empty", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 0);
        DefineMethod(realm, sel, "collapse", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);
        DefineMethod(realm, sel, "selectAllChildren", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);
        DefineMethod(realm, sel, "toString", delegate (JsCallArgs a) { return ""; }, 0);
        realm.Global.Define("getSelection", JsProperty.Data(realm.MakeMethod("getSelection", delegate (JsCallArgs a)
        {
            return sel;
        }, 0)));
    }

    // ---- Storage (localStorage / sessionStorage) ---------------------------

    private static void BuildStorage(JsRealm realm)
    {
        JsObject proto = realm.NewObject();
        DefineMethod(realm, proto, "getItem", delegate (JsCallArgs a)
        {
            JsStorageObject s = a.ThisValue as JsStorageObject;
            if (s == null) return JsNull.Instance;
            string key = JsValueOps.ToStringValue(a.Get(0));
            string v = s.GetItem(key);
            return v != null ? (object)v : JsNull.Instance;
        }, 1);

        DefineMethod(realm, proto, "setItem", delegate (JsCallArgs a)
        {
            JsStorageObject s = a.ThisValue as JsStorageObject;
            if (s == null) return JsUndefined.Instance;
            string key = JsValueOps.ToStringValue(a.Get(0));
            string val = JsValueOps.ToStringValue(a.Get(1));
            s.SetItem(key, val);
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, proto, "removeItem", delegate (JsCallArgs a)
        {
            JsStorageObject s = a.ThisValue as JsStorageObject;
            if (s == null) return JsUndefined.Instance;
            string key = JsValueOps.ToStringValue(a.Get(0));
            s.RemoveItem(key);
            return JsUndefined.Instance;
        }, 1);

        DefineMethod(realm, proto, "clear", delegate (JsCallArgs a)
        {
            JsStorageObject s = a.ThisValue as JsStorageObject;
            if (s != null) s.Clear();
            return JsUndefined.Instance;
        }, 0);

        DefineMethod(realm, proto, "key", delegate (JsCallArgs a)
        {
            JsStorageObject s = a.ThisValue as JsStorageObject;
            if (s == null) return JsNull.Instance;
            int idx = (int)JsValueOps.ToNumber(a.Get(0));
            string k = s.Key(idx);
            return k != null ? (object)k : JsNull.Instance;
        }, 1);

        DefineAccessor(realm, proto, "length", delegate (JsCallArgs a)
        {
            JsStorageObject s = a.ThisValue as JsStorageObject;
            return (double)(s != null ? s.Length : 0);
        }, null);

        JsStorageObject localStorage = new JsStorageObject(proto);
        JsStorageObject sessionStorage = new JsStorageObject(proto);

        realm.Global.Define("localStorage", JsProperty.Data(localStorage));
        realm.Global.Define("sessionStorage", JsProperty.Data(sessionStorage));
        realm.Global.Define("Storage", JsProperty.Data(realm.MakeCtor("Storage", delegate (JsCallArgs a) { return new JsStorageObject(proto); }, 0, proto)));
    }

    // ---- Screen -----------------------------------------------------------

    private static void BuildScreen(JsRealm realm, JsEventLoop loop)
    {
        JsObject scr = realm.NewObject();
        DefineAccessor(realm, scr, "width", delegate (JsCallArgs a) { return (double)loop.ViewportWidth; }, null);
        DefineAccessor(realm, scr, "height", delegate (JsCallArgs a) { return (double)loop.ViewportHeight; }, null);
        DefineAccessor(realm, scr, "availWidth", delegate (JsCallArgs a) { return (double)loop.ViewportWidth; }, null);
        DefineAccessor(realm, scr, "availHeight", delegate (JsCallArgs a) { return (double)loop.ViewportHeight; }, null);
        DefineAccessor(realm, scr, "colorDepth", delegate (JsCallArgs a) { return 32.0; }, null);
        DefineAccessor(realm, scr, "pixelDepth", delegate (JsCallArgs a) { return 32.0; }, null);
        realm.Global.Define("screen", JsProperty.Data(scr));
    }

    // ---- matchMedia -------------------------------------------------------

    private static void BuildMedia(JsRealm realm, JsEventLoop loop)
    {
        realm.Global.Define("matchMedia", JsProperty.Data(realm.MakeMethod("matchMedia", delegate (JsCallArgs a)
        {
            string query = JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant();
            bool matches = false;
            if (query.IndexOf("prefers-color-scheme: dark") >= 0) matches = false;
            else if (query.IndexOf("prefers-color-scheme: light") >= 0) matches = true;
            else if (query.IndexOf("min-width") >= 0) matches = true;
            else if (query.IndexOf("all") >= 0 || query.IndexOf("screen") >= 0) matches = true;

            JsObject mql = realm.NewObject();
            mql.Define("matches", JsProperty.Data(matches));
            mql.Define("media", JsProperty.Data(query));
            JsNativeFn noop = delegate (JsCallArgs args) { return JsUndefined.Instance; };
            DefineMethod(realm, mql, "addListener", noop, 1);
            DefineMethod(realm, mql, "removeListener", noop, 1);
            DefineMethod(realm, mql, "addEventListener", noop, 2);
            DefineMethod(realm, mql, "removeEventListener", noop, 2);
            DefineMethod(realm, mql, "dispatchEvent", delegate (JsCallArgs args) { return true; }, 1);
            return mql;
        }, 1)));
    }

    // ---- Base64 (btoa / atob) ----------------------------------------------

    private static void BuildBase64(JsRealm realm)
    {
        realm.Global.Define("btoa", JsProperty.Data(realm.MakeMethod("btoa", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.Get(0));
            byte[] bytes = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) bytes[i] = (byte)(s[i] & 0xFF);
            return Convert.ToBase64String(bytes);
        }, 1)));

        realm.Global.Define("atob", JsProperty.Data(realm.MakeMethod("atob", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.Get(0)).Trim();
            try
            {
                byte[] bytes = Convert.FromBase64String(s);
                StringBuilder sb = new StringBuilder(bytes.Length);
                for (int i = 0; i < bytes.Length; i++) sb.Append((char)bytes[i]);
                return sb.ToString();
            }
            catch (Exception)
            {
                throw JsErr.Type("Invalid base64 string");
            }
        }, 1)));
    }

    // ---- URL & URLSearchParams --------------------------------------------

    private static void BuildUrl(JsRealm realm, JsEventLoop loop)
    {
        JsObject uspProto = realm.NewObject();

        DefineMethod(realm, uspProto, "get", delegate (JsCallArgs a)
        {
            JsUrlSearchParams s = a.ThisValue as JsUrlSearchParams;
            if (s == null) return JsNull.Instance;
            string key = JsValueOps.ToStringValue(a.Get(0));
            for (int i = 0; i < s.Params.Count; i++)
                if (s.Params[i].Key == key) return s.Params[i].Value;
            return JsNull.Instance;
        }, 1);

        DefineMethod(realm, uspProto, "getAll", delegate (JsCallArgs a)
        {
            JsUrlSearchParams s = a.ThisValue as JsUrlSearchParams;
            JsArray arr = realm.NewArray();
            if (s == null) return arr;
            string key = JsValueOps.ToStringValue(a.Get(0));
            for (int i = 0; i < s.Params.Count; i++)
                if (s.Params[i].Key == key) arr.Items.Add(s.Params[i].Value);
            return arr;
        }, 1);

        DefineMethod(realm, uspProto, "has", delegate (JsCallArgs a)
        {
            JsUrlSearchParams s = a.ThisValue as JsUrlSearchParams;
            if (s == null) return false;
            string key = JsValueOps.ToStringValue(a.Get(0));
            for (int i = 0; i < s.Params.Count; i++)
                if (s.Params[i].Key == key) return true;
            return false;
        }, 1);

        DefineMethod(realm, uspProto, "set", delegate (JsCallArgs a)
        {
            JsUrlSearchParams s = a.ThisValue as JsUrlSearchParams;
            if (s == null) return JsUndefined.Instance;
            string key = JsValueOps.ToStringValue(a.Get(0));
            string val = JsValueOps.ToStringValue(a.Get(1));
            bool setFirst = false;
            for (int i = s.Params.Count - 1; i >= 0; i--)
            {
                if (s.Params[i].Key == key)
                {
                    if (!setFirst) { s.Params[i] = new KeyValuePair<string, string>(key, val); setFirst = true; }
                    else s.Params.RemoveAt(i);
                }
            }
            if (!setFirst) s.Params.Add(new KeyValuePair<string, string>(key, val));
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, uspProto, "append", delegate (JsCallArgs a)
        {
            JsUrlSearchParams s = a.ThisValue as JsUrlSearchParams;
            if (s == null) return JsUndefined.Instance;
            string key = JsValueOps.ToStringValue(a.Get(0));
            string val = JsValueOps.ToStringValue(a.Get(1));
            s.Params.Add(new KeyValuePair<string, string>(key, val));
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, uspProto, "delete", delegate (JsCallArgs a)
        {
            JsUrlSearchParams s = a.ThisValue as JsUrlSearchParams;
            if (s == null) return JsUndefined.Instance;
            string key = JsValueOps.ToStringValue(a.Get(0));
            for (int i = s.Params.Count - 1; i >= 0; i--)
                if (s.Params[i].Key == key) s.Params.RemoveAt(i);
            return JsUndefined.Instance;
        }, 1);

        DefineMethod(realm, uspProto, "toString", delegate (JsCallArgs a)
        {
            JsUrlSearchParams s = a.ThisValue as JsUrlSearchParams;
            return s != null ? s.ToString() : "";
        }, 0);

        DefineMethod(realm, uspProto, "forEach", delegate (JsCallArgs a)
        {
            JsUrlSearchParams s = a.ThisValue as JsUrlSearchParams;
            JsFunction cb = a.Get(0) as JsFunction;
            if (s != null && cb != null)
            {
                for (int i = 0; i < s.Params.Count; i++)
                    a.Interp.Call(cb, a.ThisValue, new object[] { s.Params[i].Value, s.Params[i].Key, s }, false);
            }
            return JsUndefined.Instance;
        }, 1);

        realm.Global.Define("URLSearchParams", JsProperty.Data(realm.MakeCtor("URLSearchParams", delegate (JsCallArgs a)
        {
            JsUrlSearchParams usp = new JsUrlSearchParams(uspProto);
            if (a.ArgCount > 0) usp.Parse(JsValueOps.ToStringValue(a.Get(0)));
            return usp;
        }, 0, uspProto)));

        JsObject urlProto = realm.NewObject();

        DefineAccessor(realm, urlProto, "href",
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; return u != null ? u.Href : ""; },
            delegate (JsCallArgs a)
            {
                JsUrlObject u = a.ThisValue as JsUrlObject;
                if (u != null)
                {
                    string target = WebUrl.Resolve(loop.LocationHref, JsValueOps.ToStringValue(a.Get(0)));
                    SplitUrl(target, out u.Scheme, out u.Host, out u.Port, out u.Path, out u.Query, out u.Hash);
                    if (u.SearchParams != null) u.SearchParams.Parse(u.Query);
                }
                return JsUndefined.Instance;
            });

        DefineAccessor(realm, urlProto, "origin",
            delegate (JsCallArgs a)
            {
                JsUrlObject u = a.ThisValue as JsUrlObject;
                if (u == null) return "";
                string p = u.Port.Length > 0 && u.Port != "80" && u.Port != "443" ? ":" + u.Port : "";
                return u.Scheme + "://" + u.Host + p;
            }, null);

        DefineAccessor(realm, urlProto, "protocol",
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; return u != null ? u.Scheme + ":" : ""; },
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; if (u != null) u.Scheme = JsValueOps.ToStringValue(a.Get(0)).TrimEnd(':'); return JsUndefined.Instance; });

        DefineAccessor(realm, urlProto, "host",
            delegate (JsCallArgs a)
            {
                JsUrlObject u = a.ThisValue as JsUrlObject;
                if (u == null) return "";
                string p = u.Port.Length > 0 && u.Port != "80" && u.Port != "443" ? ":" + u.Port : "";
                return u.Host + p;
            }, null);

        DefineAccessor(realm, urlProto, "hostname",
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; return u != null ? u.Host : ""; },
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; if (u != null) u.Host = JsValueOps.ToStringValue(a.Get(0)); return JsUndefined.Instance; });

        DefineAccessor(realm, urlProto, "port",
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; return u != null ? u.Port : ""; },
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; if (u != null) u.Port = JsValueOps.ToStringValue(a.Get(0)); return JsUndefined.Instance; });

        DefineAccessor(realm, urlProto, "pathname",
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; return u != null ? u.Path : "/"; },
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; if (u != null) u.Path = JsValueOps.ToStringValue(a.Get(0)); return JsUndefined.Instance; });

        DefineAccessor(realm, urlProto, "search",
            delegate (JsCallArgs a)
            {
                JsUrlObject u = a.ThisValue as JsUrlObject;
                if (u == null) return "";
                string q = u.SearchParams != null ? u.SearchParams.ToString() : u.Query;
                return q.Length > 0 ? (q[0] == '?' ? q : "?" + q) : "";
            },
            delegate (JsCallArgs a)
            {
                JsUrlObject u = a.ThisValue as JsUrlObject;
                if (u != null)
                {
                    u.Query = JsValueOps.ToStringValue(a.Get(0));
                    if (u.SearchParams != null) u.SearchParams.Parse(u.Query);
                }
                return JsUndefined.Instance;
            });

        DefineAccessor(realm, urlProto, "hash",
            delegate (JsCallArgs a)
            {
                JsUrlObject u = a.ThisValue as JsUrlObject;
                if (u == null) return "";
                return u.Hash.Length > 0 ? (u.Hash[0] == '#' ? u.Hash : "#" + u.Hash) : "";
            },
            delegate (JsCallArgs a) { JsUrlObject u = a.ThisValue as JsUrlObject; if (u != null) u.Hash = JsValueOps.ToStringValue(a.Get(0)); return JsUndefined.Instance; });

        DefineAccessor(realm, urlProto, "searchParams",
            delegate (JsCallArgs a)
            {
                JsUrlObject u = a.ThisValue as JsUrlObject;
                return u != null && u.SearchParams != null ? (object)u.SearchParams : JsNull.Instance;
            }, null);

        DefineMethod(realm, urlProto, "toString", delegate (JsCallArgs a)
        {
            JsUrlObject u = a.ThisValue as JsUrlObject;
            return u != null ? u.Href : "";
        }, 0);

        DefineMethod(realm, urlProto, "toJSON", delegate (JsCallArgs a)
        {
            JsUrlObject u = a.ThisValue as JsUrlObject;
            return u != null ? u.Href : "";
        }, 0);

        JsFunction urlCtor = realm.MakeCtor("URL", delegate (JsCallArgs a)
        {
            string urlStr = JsValueOps.ToStringValue(a.Get(0));
            string baseStr = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : loop.LocationHref;
            string resolved = WebUrl.Resolve(baseStr, urlStr);

            JsUrlObject u = new JsUrlObject(urlProto);
            SplitUrl(resolved, out u.Scheme, out u.Host, out u.Port, out u.Path, out u.Query, out u.Hash);
            u.SearchParams = new JsUrlSearchParams(uspProto);
            u.SearchParams.Parse(u.Query);
            return u;
        }, 1, urlProto);

        DefineMethod(realm, urlCtor, "createObjectURL", delegate (JsCallArgs a)
        {
            return "blob:windose-" + Guid.NewGuid().ToString("N");
        }, 1);
        DefineMethod(realm, urlCtor, "revokeObjectURL", delegate (JsCallArgs a)
        {
            return JsUndefined.Instance;
        }, 1);

        realm.Global.Define("URL", JsProperty.Data(urlCtor));
    }

    // ---- FormData ---------------------------------------------------------

    private static void BuildFormData(JsRealm realm)
    {
        JsObject proto = realm.NewObject();

        DefineMethod(realm, proto, "append", delegate (JsCallArgs a)
        {
            JsFormData fd = a.ThisValue as JsFormData;
            if (fd == null) return JsUndefined.Instance;
            string k = JsValueOps.ToStringValue(a.Get(0));
            string v = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
            fd.Entries.Add(new KeyValuePair<string, string>(k, v));
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, proto, "get", delegate (JsCallArgs a)
        {
            JsFormData fd = a.ThisValue as JsFormData;
            if (fd == null) return JsNull.Instance;
            string k = JsValueOps.ToStringValue(a.Get(0));
            for (int i = 0; i < fd.Entries.Count; i++)
                if (fd.Entries[i].Key == k) return fd.Entries[i].Value;
            return JsNull.Instance;
        }, 1);

        DefineMethod(realm, proto, "getAll", delegate (JsCallArgs a)
        {
            JsFormData fd = a.ThisValue as JsFormData;
            JsArray arr = realm.NewArray();
            if (fd == null) return arr;
            string k = JsValueOps.ToStringValue(a.Get(0));
            for (int i = 0; i < fd.Entries.Count; i++)
                if (fd.Entries[i].Key == k) arr.Items.Add(fd.Entries[i].Value);
            return arr;
        }, 1);

        DefineMethod(realm, proto, "has", delegate (JsCallArgs a)
        {
            JsFormData fd = a.ThisValue as JsFormData;
            if (fd == null) return false;
            string k = JsValueOps.ToStringValue(a.Get(0));
            for (int i = 0; i < fd.Entries.Count; i++)
                if (fd.Entries[i].Key == k) return true;
            return false;
        }, 1);

        DefineMethod(realm, proto, "set", delegate (JsCallArgs a)
        {
            JsFormData fd = a.ThisValue as JsFormData;
            if (fd == null) return JsUndefined.Instance;
            string k = JsValueOps.ToStringValue(a.Get(0));
            string v = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
            bool setFirst = false;
            for (int i = fd.Entries.Count - 1; i >= 0; i--)
            {
                if (fd.Entries[i].Key == k)
                {
                    if (!setFirst) { fd.Entries[i] = new KeyValuePair<string, string>(k, v); setFirst = true; }
                    else fd.Entries.RemoveAt(i);
                }
            }
            if (!setFirst) fd.Entries.Add(new KeyValuePair<string, string>(k, v));
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, proto, "delete", delegate (JsCallArgs a)
        {
            JsFormData fd = a.ThisValue as JsFormData;
            if (fd == null) return JsUndefined.Instance;
            string k = JsValueOps.ToStringValue(a.Get(0));
            for (int i = fd.Entries.Count - 1; i >= 0; i--)
                if (fd.Entries[i].Key == k) fd.Entries.RemoveAt(i);
            return JsUndefined.Instance;
        }, 1);

        DefineMethod(realm, proto, "forEach", delegate (JsCallArgs a)
        {
            JsFormData fd = a.ThisValue as JsFormData;
            JsFunction cb = a.Get(0) as JsFunction;
            if (fd != null && cb != null)
            {
                for (int i = 0; i < fd.Entries.Count; i++)
                    a.Interp.Call(cb, a.ThisValue, new object[] { fd.Entries[i].Value, fd.Entries[i].Key, fd }, false);
            }
            return JsUndefined.Instance;
        }, 1);

        realm.Global.Define("FormData", JsProperty.Data(realm.MakeCtor("FormData", delegate (JsCallArgs a)
        {
            JsFormData fd = new JsFormData(proto);
            if (a.ArgCount > 0 && a.Get(0) is JsElementObject el && el.Node != null)
            {
                // collect form inputs
                List<DomNode> inputs = new List<DomNode>();
                CollectFormControls(el.Node, inputs);
                for (int i = 0; i < inputs.Count; i++)
                {
                    DomNode inp = inputs[i];
                    string name = inp.GetAttr("name");
                    if (!string.IsNullOrEmpty(name))
                    {
                        string val = inp.GetAttr("value") ?? inp.TextContent ?? "";
                        fd.Entries.Add(new KeyValuePair<string, string>(name, val));
                    }
                }
            }
            return fd;
        }, 0, proto)));
    }

    private static void CollectFormControls(DomNode node, List<DomNode> list)
    {
        for (DomNode c = node.FirstChild; c != null; c = c.Next)
        {
            if (c.IsElement)
            {
                if (c.Tag == "input" || c.Tag == "select" || c.Tag == "textarea") list.Add(c);
                CollectFormControls(c, list);
            }
        }
    }

    // ---- Blob / File ------------------------------------------------------

    private static void BuildBlob(JsRealm realm, JsEventLoop loop)
    {
        JsObject blobProto = realm.NewObject();
        blobProto.SetWellKnown(JsWellKnown.ToStringTag, "Blob");
        DefineAccessor(realm, blobProto, "size", delegate (JsCallArgs a)
        {
            JsBlobObject b = a.ThisValue as JsBlobObject;
            return b != null ? (object)(double)b.Size : (object)0.0;
        }, null);
        DefineAccessor(realm, blobProto, "type", delegate (JsCallArgs a)
        {
            JsBlobObject b = a.ThisValue as JsBlobObject;
            return b != null ? b.ContentType : "";
        }, null);
        DefineMethod(realm, blobProto, "slice", delegate (JsCallArgs a)
        {
            JsBlobObject b = a.ThisValue as JsBlobObject;
            if (b == null || b.Data == null) return new JsBlobObject(blobProto, new byte[0], "");
            int start = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            int end = a.ArgCount > 1 ? ToInteger(a.Get(1)) : b.Data.Length;
            string ct = a.ArgCount > 2 ? JsValueOps.ToStringValue(a.Get(2)) : "";
            if (start < 0) start = System.Math.Max(0, b.Data.Length + start); else if (start > b.Data.Length) start = b.Data.Length;
            if (end < 0) end = System.Math.Max(0, b.Data.Length + end); else if (end > b.Data.Length) end = b.Data.Length;
            int span = System.Math.Max(0, end - start);
            byte[] sliced = new byte[span];
            if (span > 0) Array.Copy(b.Data, start, sliced, 0, span);
            return new JsBlobObject(blobProto, sliced, ct);
        }, 3);
        DefineMethod(realm, blobProto, "text", delegate (JsCallArgs a)
        {
            JsBlobObject b = a.ThisValue as JsBlobObject;
            string s = (b != null && b.Data != null) ? Encoding.UTF8.GetString(b.Data) : "";
            JsPromise p = new JsPromise(realm.PromisePrototype);
            FulfillPromise(loop, p, s);
            return p;
        }, 0);
        DefineMethod(realm, blobProto, "arrayBuffer", delegate (JsCallArgs a)
        {
            JsBlobObject b = a.ThisValue as JsBlobObject;
            byte[] d = (b != null && b.Data != null) ? b.Data : new byte[0];
            byte[] copy = new byte[d.Length];
            Array.Copy(d, copy, d.Length);
            JsArrayBuffer ab = new JsArrayBuffer(realm.ObjectPrototype, copy);
            JsPromise p = new JsPromise(realm.PromisePrototype);
            FulfillPromise(loop, p, ab);
            return p;
        }, 0);

        realm.Global.Define("Blob", JsProperty.Data(realm.MakeCtor("Blob", delegate (JsCallArgs a)
        {
            byte[] bytes = CollectBlobBytes(realm, a.Get(0));
            string type = "";
            if (a.ArgCount > 1 && a.Get(1) is JsObject opts && opts.GetProperty("type", out object tVal))
                type = JsValueOps.ToStringValue(tVal);
            return new JsBlobObject(blobProto, bytes, type);
        }, 0, blobProto)));

        JsObject fileProto = realm.NewObject();
        fileProto.SetPrototype(blobProto);
        fileProto.SetWellKnown(JsWellKnown.ToStringTag, "File");
        DefineAccessor(realm, fileProto, "name", delegate (JsCallArgs a)
        {
            JsFileObject f = a.ThisValue as JsFileObject;
            return f != null ? f.FileName : "";
        }, null);
        DefineAccessor(realm, fileProto, "lastModified", delegate (JsCallArgs a)
        {
            JsFileObject f = a.ThisValue as JsFileObject;
            return f != null ? (object)f.LastModified : (object)0.0;
        }, null);

        realm.Global.Define("File", JsProperty.Data(realm.MakeCtor("File", delegate (JsCallArgs a)
        {
            byte[] bytes = CollectBlobBytes(realm, a.Get(0));
            string name = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
            string type = "";
            if (a.ArgCount > 2 && a.Get(2) is JsObject opts && opts.GetProperty("type", out object tVal))
                type = JsValueOps.ToStringValue(tVal);
            return new JsFileObject(fileProto, bytes, type, name, (double)DateTime.UtcNow.Ticks / 10000.0 - 62135596800000.0);
        }, 2, fileProto)));
    }

    private static byte[] CollectBlobBytes(JsRealm realm, object partsVal)
    {
        List<byte> bytes = new List<byte>();
        if (partsVal is JsArray arr)
        {
            for (int i = 0; i < arr.Items.Count; i++)
                AppendBlobPart(bytes, arr.Items[i]);
        }
        else if (partsVal != null && !(partsVal is JsUndefined))
        {
            AppendBlobPart(bytes, partsVal);
        }
        return bytes.ToArray();
    }

    private static void AppendBlobPart(List<byte> list, object part)
    {
        if (part is JsBlobObject b && b.Data != null)
        {
            list.AddRange(b.Data);
        }
        else if (part is JsArrayBuffer ab && ab.Data != null)
        {
            list.AddRange(ab.Data);
        }
        else if (part is JsTypedArray ta && ta.Buffer != null && ta.Buffer.Data != null)
        {
            int start = ta.ByteOffset;
            int count = ta.Length * ta.BytesPerElement;
            for (int i = 0; i < count && (start + i) < ta.Buffer.Data.Length; i++)
                list.Add(ta.Buffer.Data[start + i]);
        }
        else
        {
            string s = JsValueOps.ToStringValue(part);
            byte[] raw = Encoding.UTF8.GetBytes(s);
            list.AddRange(raw);
        }
    }

    // ---- AbortController / AbortSignal -------------------------------------

    private static void BuildAbort(JsRealm realm)
    {
        JsObject sigProto = realm.NewObject();
        sigProto.Define("aborted", JsProperty.Data(false));
        JsNativeFn noop = delegate (JsCallArgs a) { return JsUndefined.Instance; };
        DefineMethod(realm, sigProto, "addEventListener", noop, 2);
        DefineMethod(realm, sigProto, "removeEventListener", noop, 2);
        realm.Global.Define("AbortSignal", JsProperty.Data(realm.MakeCtor("AbortSignal", delegate (JsCallArgs a) { return new JsObject(sigProto); }, 0, sigProto)));

        JsObject ctrlProto = realm.NewObject();
        DefineMethod(realm, ctrlProto, "abort", delegate (JsCallArgs a)
        {
            JsObject self = a.ThisValue as JsObject;
            if (self != null && self.GetProperty("signal", out object sig) && sig is JsObject sigObj)
                sigObj.Define("aborted", JsProperty.Data(true));
            return JsUndefined.Instance;
        }, 0);

        realm.Global.Define("AbortController", JsProperty.Data(realm.MakeCtor("AbortController", delegate (JsCallArgs a)
        {
            JsObject ctrl = new JsObject(ctrlProto);
            JsObject sig = new JsObject(sigProto);
            sig.Define("aborted", JsProperty.Data(false));
            ctrl.Define("signal", JsProperty.Data(sig));
            return ctrl;
        }, 0, ctrlProto)));
    }

    // ---- Crypto -----------------------------------------------------------

    private static void BuildCrypto(JsRealm realm)
    {
        JsObject crypto = realm.NewObject();
        Random rng = new Random();
        DefineMethod(realm, crypto, "getRandomValues", delegate (JsCallArgs a)
        {
            object arr = a.Get(0);
            if (arr is JsTypedArray ta && ta.Buffer != null && ta.Buffer.Data != null)
            {
                for (int i = 0; i < ta.Length; i++)
                    ta.SetElement(i, (double)rng.Next(0, 256));
            }
            else if (arr is JsArray ja)
            {
                for (int i = 0; i < ja.Items.Count; i++) ja.Items[i] = (double)rng.Next(0, 256);
            }
            return arr;
        }, 1);

        DefineMethod(realm, crypto, "randomUUID", delegate (JsCallArgs a)
        {
            byte[] b = new byte[16];
            rng.NextBytes(b);
            b[6] = (byte)((b[6] & 0x0F) | 0x40); // v4
            b[8] = (byte)((b[8] & 0x3F) | 0x80); // variant
            StringBuilder sb = new StringBuilder(36);
            for (int i = 0; i < 16; i++)
            {
                if (i == 4 || i == 6 || i == 8 || i == 10) sb.Append('-');
                sb.Append(b[i].ToString("x2"));
            }
            return sb.ToString();
        }, 0);

        JsObject subtle = realm.NewObject();
        subtle.SetWellKnown(JsWellKnown.ToStringTag, "SubtleCrypto");

        DefineMethod(realm, subtle, "digest", delegate (JsCallArgs a)
        {
            string algo = "";
            if (a.ArgCount > 0)
            {
                object a0 = a.Get(0);
                if (a0 is string str) algo = str.ToUpperInvariant();
                else if (a0 is JsObject obj && obj.GetProperty("name", out object n)) algo = JsValueOps.ToStringValue(n).ToUpperInvariant();
            }
            byte[] dataBytes = new byte[0];
            if (a.ArgCount > 1)
            {
                object a1 = a.Get(1);
                if (a1 is JsTypedArray ta && ta.Buffer != null && ta.Buffer.Data != null)
                {
                    int offset = ta.ByteOffset;
                    int count = Math.Min(ta.Length * ta.BytesPerElement, ta.Buffer.Data.Length - offset);
                    dataBytes = new byte[count];
                    Array.Copy(ta.Buffer.Data, offset, dataBytes, 0, count);
                }
                else if (a1 is JsArrayBuffer ab && ab.Data != null)
                {
                    dataBytes = (byte[])ab.Data.Clone();
                }
                else if (a1 is string s)
                {
                    dataBytes = Encoding.UTF8.GetBytes(s);
                }
            }

            byte[] hash;
            if (algo.IndexOf("384") >= 0)
            {
                using (var sha = System.Security.Cryptography.SHA384.Create())
                    hash = sha.ComputeHash(dataBytes);
            }
            else if (algo.IndexOf("512") >= 0)
            {
                using (var sha = System.Security.Cryptography.SHA512.Create())
                    hash = sha.ComputeHash(dataBytes);
            }
            else if (algo.IndexOf("SHA-1") >= 0 || algo == "SHA1")
            {
                using (var sha = System.Security.Cryptography.SHA1.Create())
                    hash = sha.ComputeHash(dataBytes);
            }
            else
            {
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    hash = sha.ComputeHash(dataBytes);
            }

            JsArrayBuffer resBuf = new JsArrayBuffer(realm.ObjectPrototype, hash);
            JsPromise p = new JsPromise(realm.PromisePrototype);
            JsEventLoop loop = realm.Interp != null ? realm.Interp.EnsureLoop() : null;
            FulfillPromise(loop, p, resBuf);
            return p;
        }, 2);

        crypto.Define("subtle", JsProperty.Data(subtle));
        realm.Global.Define("crypto", JsProperty.Data(crypto));
    }

    // ---- Observers (Intersection, Resize, Mutation) -----------------------

    private static void BuildObservers(JsRealm realm)
    {
        JsNativeFn noop = delegate (JsCallArgs a) { return JsUndefined.Instance; };

        // IntersectionObserver
        JsObject ioProto = realm.NewObject();
        DefineMethod(realm, ioProto, "observe", noop, 1);
        DefineMethod(realm, ioProto, "unobserve", noop, 1);
        DefineMethod(realm, ioProto, "disconnect", noop, 0);
        DefineMethod(realm, ioProto, "takeRecords", delegate (JsCallArgs a) { return realm.NewArray(); }, 0);
        realm.Global.Define("IntersectionObserver", JsProperty.Data(realm.MakeCtor("IntersectionObserver", delegate (JsCallArgs a) { return new JsObject(ioProto); }, 1, ioProto)));

        // ResizeObserver
        JsObject roProto = realm.NewObject();
        DefineMethod(realm, roProto, "observe", noop, 1);
        DefineMethod(realm, roProto, "unobserve", noop, 1);
        DefineMethod(realm, roProto, "disconnect", noop, 0);
        realm.Global.Define("ResizeObserver", JsProperty.Data(realm.MakeCtor("ResizeObserver", delegate (JsCallArgs a) { return new JsObject(roProto); }, 1, roProto)));

        // MutationObserver
        JsObject moProto = realm.NewObject();
        DefineMethod(realm, moProto, "observe", noop, 2);
        DefineMethod(realm, moProto, "disconnect", noop, 0);
        DefineMethod(realm, moProto, "takeRecords", delegate (JsCallArgs a) { return realm.NewArray(); }, 0);
        realm.Global.Define("MutationObserver", JsProperty.Data(realm.MakeCtor("MutationObserver", delegate (JsCallArgs a) { return new JsObject(moProto); }, 1, moProto)));
    }

    // ---- DOMParser --------------------------------------------------------

    private static void BuildDomParser(JsRealm realm)
    {
        JsObject dpProto = realm.NewObject();
        DefineMethod(realm, dpProto, "parseFromString", delegate (JsCallArgs a)
        {
            string markup = JsValueOps.ToStringValue(a.Get(0));
            DomNode root = HtmlDom.Parse(markup);
            if (realm.Interp != null && realm.Interp.Dom != null)
            {
                return realm.Interp.Dom.Wrap(root);
            }
            return JsNull.Instance;
        }, 2);
        realm.Global.Define("DOMParser", JsProperty.Data(realm.MakeCtor("DOMParser", delegate (JsCallArgs a) { return new JsObject(dpProto); }, 0, dpProto)));
    }

    private static void BuildWebSocket(JsRealm realm, JsEventLoop loop)
    {
        JsObject wsProto = realm.NewObject();
        wsProto.SetWellKnown(JsWellKnown.ToStringTag, "WebSocket");

        wsProto.Define("CONNECTING", JsProperty.DataNonEnum(0.0));
        wsProto.Define("OPEN", JsProperty.DataNonEnum(1.0));
        wsProto.Define("CLOSING", JsProperty.DataNonEnum(2.0));
        wsProto.Define("CLOSED", JsProperty.DataNonEnum(3.0));

        DefineMethod(realm, wsProto, "send", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 1);
        DefineMethod(realm, wsProto, "close", delegate (JsCallArgs a)
        {
            JsObject self = a.ThisValue as JsObject;
            if (self != null)
            {
                self.SetProperty("readyState", 3.0);
                if (self.GetProperty("onclose", out object fn) && fn is JsFunction closeFn)
                {
                    JsObject closeEv = realm.NewObject();
                    closeEv.Define("type", JsProperty.DataNonEnum("close"));
                    closeEv.Define("code", JsProperty.DataNonEnum(1000.0));
                    closeEv.Define("reason", JsProperty.DataNonEnum("Normal Closure"));
                    closeEv.Define("wasClean", JsProperty.DataNonEnum(true));
                    realm.Interp.Call(closeFn, self, new object[] { closeEv }, false);
                }
            }
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, wsProto, "addEventListener", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);
        DefineMethod(realm, wsProto, "removeEventListener", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);

        realm.Global.Define("WebSocket", JsProperty.Data(realm.MakeCtor("WebSocket", delegate (JsCallArgs a)
        {
            string url = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            string protocol = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
            JsObject ws = new JsObject(wsProto);
            ws.Define("url", JsProperty.DataNonEnum(url));
            ws.Define("protocol", JsProperty.DataNonEnum(protocol));
            ws.Define("readyState", JsProperty.DataNonEnum(0.0)); // CONNECTING
            ws.Define("bufferedAmount", JsProperty.DataNonEnum(0.0));
            ws.Define("extensions", JsProperty.DataNonEnum(""));
            ws.Define("binaryType", JsProperty.DataNonEnum("blob"));

            if (loop != null)
            {
                loop.EnqueueJob(delegate
                {
                    ws.SetProperty("readyState", 1.0); // OPEN
                    if (ws.GetProperty("onopen", out object fn) && fn is JsFunction openFn)
                    {
                        JsObject ev = realm.NewObject();
                        ev.Define("type", JsProperty.DataNonEnum("open"));
                        realm.Interp.Call(openFn, ws, new object[] { ev }, false);
                    }
                });
            }
            return ws;
        }, 1, wsProto)));
    }

    private static void BuildIndexedDb(JsRealm realm, JsEventLoop loop)
    {
        Dictionary<string, Dictionary<string, Dictionary<string, object>>> dbs =
            new Dictionary<string, Dictionary<string, Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);

        JsObject idbFactory = realm.NewObject();
        idbFactory.SetWellKnown(JsWellKnown.ToStringTag, "IDBFactory");

        DefineMethod(realm, idbFactory, "open", delegate (JsCallArgs a)
        {
            string dbName = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "default";
            double version = a.ArgCount > 1 ? JsValueOps.ToNumber(a.Get(1)) : 1.0;

            if (!dbs.ContainsKey(dbName))
                dbs[dbName] = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

            var dbStores = dbs[dbName];

            JsObject dbObj = realm.NewObject();
            dbObj.SetWellKnown(JsWellKnown.ToStringTag, "IDBDatabase");
            dbObj.Define("name", JsProperty.DataNonEnum(dbName));
            dbObj.Define("version", JsProperty.DataNonEnum(version));

            DefineMethod(realm, dbObj, "createObjectStore", delegate (JsCallArgs csa)
            {
                string storeName = JsValueOps.ToStringValue(csa.Get(0));
                if (!dbStores.ContainsKey(storeName))
                    dbStores[storeName] = new Dictionary<string, object>();
                JsObject storeObj = realm.NewObject();
                storeObj.SetWellKnown(JsWellKnown.ToStringTag, "IDBObjectStore");
                storeObj.Define("name", JsProperty.DataNonEnum(storeName));
                return storeObj;
            }, 1);

            DefineMethod(realm, dbObj, "transaction", delegate (JsCallArgs ta)
            {
                string storeName = ta.ArgCount > 0 ? JsValueOps.ToStringValue(ta.Get(0)) : "";
                JsObject tx = realm.NewObject();
                tx.SetWellKnown(JsWellKnown.ToStringTag, "IDBTransaction");
                tx.Define("db", JsProperty.DataNonEnum(dbObj));

                DefineMethod(realm, tx, "objectStore", delegate (JsCallArgs osa)
                {
                    string sname = osa.ArgCount > 0 ? JsValueOps.ToStringValue(osa.Get(0)) : storeName;
                    if (!dbStores.ContainsKey(sname)) dbStores[sname] = new Dictionary<string, object>();
                    var storeMap = dbStores[sname];

                    JsObject os = realm.NewObject();
                    os.SetWellKnown(JsWellKnown.ToStringTag, "IDBObjectStore");
                    os.Define("name", JsProperty.DataNonEnum(sname));

                    DefineMethod(realm, os, "put", delegate (JsCallArgs pa)
                    {
                        object val = pa.Get(0);
                        string k = pa.ArgCount > 1 ? JsValueOps.ToStringValue(pa.Get(1)) : ("key_" + storeMap.Count);
                        storeMap[k] = val;
                        return MakeIdbRequest(realm, loop, val);
                    }, 2);

                    DefineMethod(realm, os, "add", delegate (JsCallArgs pa)
                    {
                        object val = pa.Get(0);
                        string k = pa.ArgCount > 1 ? JsValueOps.ToStringValue(pa.Get(1)) : ("key_" + storeMap.Count);
                        storeMap[k] = val;
                        return MakeIdbRequest(realm, loop, val);
                    }, 2);

                    DefineMethod(realm, os, "get", delegate (JsCallArgs ga)
                    {
                        string k = JsValueOps.ToStringValue(ga.Get(0));
                        object val = storeMap.TryGetValue(k, out object found) ? found : JsUndefined.Instance;
                        return MakeIdbRequest(realm, loop, val);
                    }, 1);

                    DefineMethod(realm, os, "getAll", delegate (JsCallArgs gaa)
                    {
                        List<object> all = new List<object>(storeMap.Values);
                        return MakeIdbRequest(realm, loop, realm.NewArray(all));
                    }, 0);

                    DefineMethod(realm, os, "delete", delegate (JsCallArgs da)
                    {
                        string k = JsValueOps.ToStringValue(da.Get(0));
                        storeMap.Remove(k);
                        return MakeIdbRequest(realm, loop, JsUndefined.Instance);
                    }, 1);

                    DefineMethod(realm, os, "clear", delegate (JsCallArgs ca)
                    {
                        storeMap.Clear();
                        return MakeIdbRequest(realm, loop, JsUndefined.Instance);
                    }, 0);

                    return os;
                }, 1);

                return tx;
            }, 2);

            DefineMethod(realm, dbObj, "close", delegate (JsCallArgs ca) { return JsUndefined.Instance; }, 0);

            JsObject openReq = MakeIdbRequest(realm, loop, dbObj);
            openReq.Define("result", JsProperty.DataNonEnum(dbObj));

            if (loop != null)
            {
                loop.EnqueueJob(delegate
                {
                    if (openReq.GetProperty("onupgradeneeded", out object ufn) && ufn is JsFunction upFn)
                    {
                        JsObject ev = realm.NewObject();
                        ev.Define("type", JsProperty.DataNonEnum("upgradeneeded"));
                        ev.Define("target", JsProperty.DataNonEnum(openReq));
                        realm.Interp.Call(upFn, openReq, new object[] { ev }, false);
                    }
                    if (openReq.GetProperty("onsuccess", out object sfn) && sfn is JsFunction scFn)
                    {
                        JsObject ev = realm.NewObject();
                        ev.Define("type", JsProperty.DataNonEnum("success"));
                        ev.Define("target", JsProperty.DataNonEnum(openReq));
                        realm.Interp.Call(scFn, openReq, new object[] { ev }, false);
                    }
                });
            }

            return openReq;
        }, 2);

        realm.Global.Define("indexedDB", JsProperty.Data(idbFactory));
    }

    private static JsObject MakeIdbRequest(JsRealm realm, JsEventLoop loop, object result)
    {
        JsObject req = realm.NewObject();
        req.SetWellKnown(JsWellKnown.ToStringTag, "IDBRequest");
        req.Define("readyState", JsProperty.DataNonEnum("done"));
        req.Define("result", JsProperty.DataNonEnum(result != null ? result : JsUndefined.Instance));
        req.Define("error", JsProperty.DataNonEnum(JsNull.Instance));

        if (loop != null)
        {
            loop.EnqueueJob(delegate
            {
                if (req.GetProperty("onsuccess", out object sfn) && sfn is JsFunction scFn)
                {
                    JsObject ev = realm.NewObject();
                    ev.Define("type", JsProperty.DataNonEnum("success"));
                    ev.Define("target", JsProperty.DataNonEnum(req));
                    realm.Interp.Call(scFn, req, new object[] { ev }, false);
                }
            });
        }
        return req;
    }

    private static void BuildWorker(JsRealm realm, JsEventLoop loop)
    {
        JsObject workerProto = realm.NewObject();
        workerProto.SetWellKnown(JsWellKnown.ToStringTag, "Worker");

        DefineMethod(realm, workerProto, "postMessage", delegate (JsCallArgs a)
        {
            JsObject self = a.ThisValue as JsObject;
            object data = a.Get(0);
            if (self != null && loop != null)
            {
                loop.EnqueueJob(delegate
                {
                    if (self.GetProperty("onmessage", out object mfn) && mfn is JsFunction fn)
                    {
                        JsObject ev = realm.NewObject();
                        ev.Define("type", JsProperty.DataNonEnum("message"));
                        ev.Define("data", JsProperty.DataNonEnum(data));
                        realm.Interp.Call(fn, self, new object[] { ev }, false);
                    }
                });
            }
            return JsUndefined.Instance;
        }, 1);

        DefineMethod(realm, workerProto, "terminate", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 0);
        DefineMethod(realm, workerProto, "addEventListener", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);
        DefineMethod(realm, workerProto, "removeEventListener", delegate (JsCallArgs a) { return JsUndefined.Instance; }, 2);

        realm.Global.Define("Worker", JsProperty.Data(realm.MakeCtor("Worker", delegate (JsCallArgs a)
        {
            string url = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            JsObject w = new JsObject(workerProto);
            w.Define("url", JsProperty.DataNonEnum(url));
            return w;
        }, 1, workerProto)));
    }

    // ---- customElements & structuredClone ----------------------------------

    private static void BuildCustomElements(JsRealm realm, JsEventLoop loop)
    {
        JsObject registry = realm.NewObject();
        Dictionary<string, object> definedElements = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        DefineMethod(realm, registry, "define", delegate (JsCallArgs a)
        {
            string name = JsValueOps.ToStringValue(a.Get(0));
            object ctor = a.Get(1);
            if (!string.IsNullOrEmpty(name))
            {
                definedElements[name] = ctor;
            }
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, registry, "get", delegate (JsCallArgs a)
        {
            string name = JsValueOps.ToStringValue(a.Get(0));
            object ctor;
            if (name != null && definedElements.TryGetValue(name, out ctor))
                return ctor;
            return JsUndefined.Instance;
        }, 1);

        DefineMethod(realm, registry, "whenDefined", delegate (JsCallArgs a)
        {
            JsPromise p = new JsPromise(realm.PromisePrototype);
            string name = JsValueOps.ToStringValue(a.Get(0));
            object ctor;
            if (name != null && definedElements.TryGetValue(name, out ctor))
                FulfillPromise(loop, p, ctor);
            else
                FulfillPromise(loop, p, JsUndefined.Instance);
            return p;
        }, 1);

        DefineMethod(realm, registry, "upgrade", delegate (JsCallArgs a)
        {
            return JsUndefined.Instance;
        }, 1);

        realm.Global.Define("customElements", JsProperty.Data(registry));
        JsFunction cerCtor = realm.MakeCtor("CustomElementRegistry", delegate (JsCallArgs a) { throw JsErr.Type("Illegal constructor"); }, 0, registry);
        realm.Global.Define("CustomElementRegistry", JsProperty.Data(cerCtor));
    }

    private static void BuildStructuredClone(JsRealm realm)
    {
        realm.Global.Define("structuredClone", JsProperty.Data(realm.MakeMethod("structuredClone", delegate (JsCallArgs a)
        {
            object val = a.Get(0);
            if (val is JsObject obj)
            {
                try
                {
                    string json = JsJson.Stringify(realm, obj);
                    if (json != null) return JsJson.Parse(realm, json);
                }
                catch { }
                return obj;
            }
            return val;
        }, 1)));
    }

    private static void BuildTextEncoding(JsRealm realm)
    {
        // TextEncoder
        JsObject teProto = realm.NewObject();
        teProto.Define("encoding", JsProperty.DataNonEnum("utf-8"));
        DefineMethod(realm, teProto, "encode", delegate (JsCallArgs a)
        {
            string s = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            JsArrayBuffer buf = new JsArrayBuffer(realm.ObjectPrototype, bytes);
            return new JsTypedArray(realm.ObjectPrototype, buf, 0, bytes.Length, 1, "Uint8Array");
        }, 1);

        DefineMethod(realm, teProto, "encodeInto", delegate (JsCallArgs a)
        {
            string s = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            JsTypedArray dest = a.ArgCount > 1 ? a.Get(1) as JsTypedArray : null;
            JsObject result = realm.NewObject();
            if (dest == null || dest.Buffer == null || dest.Buffer.Data == null)
            {
                result.Define("read", JsProperty.Data(0.0));
                result.Define("written", JsProperty.Data(0.0));
                return result;
            }
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            int available = dest.Length;
            int toCopy = bytes.Length < available ? bytes.Length : available;
            int offset = dest.ByteOffset;
            for (int i = 0; i < toCopy; i++)
            {
                dest.Buffer.Data[offset + i] = bytes[i];
            }
            result.Define("read", JsProperty.Data((double)s.Length));
            result.Define("written", JsProperty.Data((double)toCopy));
            return result;
        }, 2);

        realm.Global.Define("TextEncoder", JsProperty.Data(realm.MakeCtor("TextEncoder", delegate (JsCallArgs a)
        {
            JsObject o = new JsObject(teProto);
            o.Define("encoding", JsProperty.DataNonEnum("utf-8"));
            return o;
        }, 0, teProto)));

        // TextDecoder
        JsObject tdProto = realm.NewObject();
        tdProto.Define("encoding", JsProperty.DataNonEnum("utf-8"));
        DefineMethod(realm, tdProto, "decode", delegate (JsCallArgs a)
        {
            if (a.ArgCount == 0 || a.Get(0) == null || a.Get(0) is JsUndefined) return "";
            object input = a.Get(0);
            byte[] bytes = null;
            if (input is JsTypedArray ta && ta.Buffer != null && ta.Buffer.Data != null)
            {
                bytes = new byte[ta.Length];
                Array.Copy(ta.Buffer.Data, ta.ByteOffset, bytes, 0, ta.Length);
            }
            else if (input is JsArrayBuffer ab && ab.Data != null)
            {
                bytes = ab.Data;
            }
            else if (input is JsArray arr)
            {
                bytes = new byte[arr.Items.Count];
                for (int i = 0; i < arr.Items.Count; i++) bytes[i] = (byte)JsValueOps.ToNumber(arr.Items[i]);
            }
            else if (input is byte[] rawBytes)
            {
                bytes = rawBytes;
            }
            if (bytes == null || bytes.Length == 0) return "";
            try { return Encoding.UTF8.GetString(bytes); }
            catch { return ""; }
        }, 1);

        realm.Global.Define("TextDecoder", JsProperty.Data(realm.MakeCtor("TextDecoder", delegate (JsCallArgs a)
        {
            string enc = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)).ToLowerInvariant() : "utf-8";
            JsObject o = new JsObject(tdProto);
            o.Define("encoding", JsProperty.DataNonEnum(enc.Length > 0 ? enc : "utf-8"));
            return o;
        }, 0, tdProto)));
    }

    private static void BuildCssObject(JsRealm realm)
    {
        JsObject css = realm.NewObject();

        DefineMethod(realm, css, "supports", delegate (JsCallArgs a)
        {
            if (a.ArgCount == 0) return false;
            if (a.ArgCount >= 2)
            {
                string prop = JsValueOps.ToStringValue(a.Get(0)).Trim().ToLowerInvariant();
                string val = JsValueOps.ToStringValue(a.Get(1)).Trim();
                return IsSupportedCssProperty(prop);
            }
            else
            {
                string condition = JsValueOps.ToStringValue(a.Get(0)).Trim();
                if (condition.StartsWith("(") && condition.EndsWith(")"))
                {
                    condition = condition.Substring(1, condition.Length - 2).Trim();
                }
                int colon = condition.IndexOf(':');
                if (colon > 0)
                {
                    string prop = condition.Substring(0, colon).Trim().ToLowerInvariant();
                    return IsSupportedCssProperty(prop);
                }
                return true;
            }
        }, 1);

        DefineMethod(realm, css, "escape", delegate (JsCallArgs a)
        {
            string s = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            return EscapeCssIdentifier(s);
        }, 1);

        realm.Global.Define("CSS", JsProperty.Data(css));
    }

    private static bool IsSupportedCssProperty(string prop)
    {
        if (string.IsNullOrEmpty(prop)) return false;
        if (prop.StartsWith("--")) return true;
        switch (prop)
        {
            case "display":
            case "color":
            case "background":
            case "background-color":
            case "background-image":
            case "background-size":
            case "background-position":
            case "background-repeat":
            case "width":
            case "height":
            case "min-width":
            case "max-width":
            case "min-height":
            case "max-height":
            case "inline-size":
            case "block-size":
            case "min-inline-size":
            case "max-inline-size":
            case "min-block-size":
            case "max-block-size":
            case "margin":
            case "margin-top":
            case "margin-right":
            case "margin-bottom":
            case "margin-left":
            case "margin-inline":
            case "margin-block":
            case "margin-inline-start":
            case "margin-inline-end":
            case "margin-block-start":
            case "margin-block-end":
            case "padding":
            case "padding-top":
            case "padding-right":
            case "padding-bottom":
            case "padding-left":
            case "padding-inline":
            case "padding-block":
            case "padding-inline-start":
            case "padding-inline-end":
            case "padding-block-start":
            case "padding-block-end":
            case "border":
            case "border-color":
            case "border-width":
            case "border-style":
            case "border-radius":
            case "font":
            case "font-size":
            case "font-family":
            case "font-weight":
            case "font-style":
            case "line-height":
            case "text-align":
            case "text-decoration":
            case "text-transform":
            case "position":
            case "top":
            case "left":
            case "right":
            case "bottom":
            case "inset":
            case "inset-inline":
            case "inset-block":
            case "z-index":
            case "opacity":
            case "visibility":
            case "overflow":
            case "overflow-x":
            case "overflow-y":
            case "flex":
            case "flex-direction":
            case "flex-wrap":
            case "flex-flow":
            case "flex-grow":
            case "flex-shrink":
            case "flex-basis":
            case "justify-content":
            case "align-items":
            case "align-content":
            case "align-self":
            case "gap":
            case "row-gap":
            case "column-gap":
            case "place-items":
            case "place-content":
            case "grid":
            case "grid-template-columns":
            case "grid-template-rows":
            case "box-sizing":
            case "cursor":
            case "transform":
            case "transition":
            case "animation":
                return true;
            default:
                return false;
        }
    }

    private static string EscapeCssIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == 0) sb.Append('\uFFFD');
            else if ((c >= 1 && c <= 31) || c == 127 || (i == 0 && c >= '0' && c <= '9') || (i == 1 && c >= '0' && c <= '9' && s[0] == '-'))
            {
                sb.Append('\\').Append(((int)c).ToString("x")).Append(' ');
            }
            else if (c == '-' || c == '_' || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c >= 128)
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('\\').Append(c);
            }
        }
        return sb.ToString();
    }

    private static void BuildEventSource(JsRealm realm)
    {
        JsEventLoop loop = realm.Interp != null ? realm.Interp.EnsureLoop() : null;
        JsObject esProto = realm.NewObject();
        esProto.Define("CONNECTING", JsProperty.Data(0.0));
        esProto.Define("OPEN", JsProperty.Data(1.0));
        esProto.Define("CLOSED", JsProperty.Data(2.0));

        DefineMethod(realm, esProto, "close", delegate (JsCallArgs a)
        {
            JsEventSourceObject self = a.ThisValue as JsEventSourceObject;
            if (self != null)
            {
                self.ReadyState = 2; // CLOSED
            }
            return JsUndefined.Instance;
        }, 0);

        DefineMethod(realm, esProto, "addEventListener", delegate (JsCallArgs a)
        {
            JsEventSourceObject self = a.ThisValue as JsEventSourceObject;
            if (self != null && a.ArgCount >= 2 && a.Get(1) is JsFunction fn)
            {
                string ev = JsValueOps.ToStringValue(a.Get(0));
                List<JsFunction> list;
                if (!self.Listeners.TryGetValue(ev, out list))
                {
                    list = new List<JsFunction>();
                    self.Listeners[ev] = list;
                }
                list.Add(fn);
            }
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, esProto, "removeEventListener", delegate (JsCallArgs a)
        {
            JsEventSourceObject self = a.ThisValue as JsEventSourceObject;
            if (self != null && a.ArgCount >= 2 && a.Get(1) is JsFunction fn)
            {
                string ev = JsValueOps.ToStringValue(a.Get(0));
                if (self.Listeners.TryGetValue(ev, out List<JsFunction> list))
                {
                    list.Remove(fn);
                }
            }
            return JsUndefined.Instance;
        }, 2);

        DefineMethod(realm, esProto, "dispatchEvent", delegate (JsCallArgs a)
        {
            return true;
        }, 1);

        JsFunction esCtor = realm.MakeCtor("EventSource", delegate (JsCallArgs a)
        {
            string urlStr = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            string resolvedUrl = loop != null ? WebUrl.Resolve(loop.LocationHref, urlStr) : urlStr;
            JsEventSourceObject es = new JsEventSourceObject(esProto);
            es.Url = resolvedUrl;
            es.ReadyState = 1; // OPEN
            es.Define("url", JsProperty.Data(resolvedUrl));
            es.Define("readyState", JsProperty.Accessor(realm.MakeMethod("get readyState", delegate (JsCallArgs ga) { return (double)es.ReadyState; }, 0), null));
            es.Define("withCredentials", JsProperty.Data(false));
            return es;
        }, 1, esProto);

        esCtor.Define("CONNECTING", JsProperty.Data(0.0));
        esCtor.Define("OPEN", JsProperty.Data(1.0));
        esCtor.Define("CLOSED", JsProperty.Data(2.0));

        realm.Global.Define("EventSource", JsProperty.Data(esCtor));
    }
}

// ---------------------------------------------------------------------------
// Storage & URL data models
// ---------------------------------------------------------------------------

public sealed class JsStorageObject : JsObject
{
    private Dictionary<string, string> items = new Dictionary<string, string>();
    private List<string> keys = new List<string>();

    public JsStorageObject(JsObject proto) : base(proto) { }
    public override string ClassTag { get { return "Storage"; } }

    public override bool GetProperty(string key, out object value)
    {
        if (items.TryGetValue(key, out string v))
        {
            value = v;
            return true;
        }
        return base.GetProperty(key, out value);
    }

    public override void SetProperty(string key, object value)
    {
        if (HasProperty(key) && (key == "getItem" || key == "setItem" || key == "removeItem" || key == "clear" || key == "key" || key == "length"))
        {
            base.SetProperty(key, value);
            return;
        }
        SetItem(key, JsValueOps.ToStringValue(value));
    }

    public override bool DeleteProperty(string key)
    {
        RemoveItem(key);
        return base.DeleteProperty(key);
    }

    public string GetItem(string key)
    {
        if (items.TryGetValue(key, out string v)) return v;
        return null;
    }

    public void SetItem(string key, string val)
    {
        if (!items.ContainsKey(key)) keys.Add(key);
        items[key] = val;
    }

    public void RemoveItem(string key)
    {
        if (items.Remove(key)) keys.Remove(key);
    }

    public void Clear()
    {
        items.Clear();
        keys.Clear();
    }

    public string Key(int index)
    {
        if (index >= 0 && index < keys.Count) return keys[index];
        return null;
    }

    public int Length { get { return keys.Count; } }
}

public sealed class JsUrlSearchParams : JsObject
{
    public List<KeyValuePair<string, string>> Params = new List<KeyValuePair<string, string>>();
    public JsUrlSearchParams(JsObject proto) : base(proto) { }
    public override string ClassTag { get { return "URLSearchParams"; } }

    public void Parse(string query)
    {
        Params.Clear();
        if (string.IsNullOrEmpty(query)) return;
        if (query[0] == '?') query = query.Substring(1);
        string[] pairs = query.Split('&');
        for (int i = 0; i < pairs.Length; i++)
        {
            if (pairs[i].Length == 0) continue;
            int eq = pairs[i].IndexOf('=');
            if (eq >= 0)
            {
                string k = WebUrl.UrlDecode(pairs[i].Substring(0, eq));
                string v = WebUrl.UrlDecode(pairs[i].Substring(eq + 1));
                Params.Add(new KeyValuePair<string, string>(k, v));
            }
            else
            {
                string k = WebUrl.UrlDecode(pairs[i]);
                Params.Add(new KeyValuePair<string, string>(k, ""));
            }
        }
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < Params.Count; i++)
        {
            if (i > 0) sb.Append('&');
            sb.Append(Params[i].Key).Append('=').Append(Params[i].Value);
        }
        return sb.ToString();
    }
}

public sealed class JsUrlObject : JsObject
{
    public string Scheme = "https", Host = "", Port = "", Path = "/", Query = "", Hash = "";
    public JsUrlSearchParams SearchParams;
    public JsUrlObject(JsObject proto) : base(proto) { }
    public override string ClassTag { get { return "URL"; } }

    public string Href
    {
        get
        {
            StringBuilder sb = new StringBuilder();
            if (Scheme.Length > 0) sb.Append(Scheme).Append("://");
            sb.Append(Host);
            if (Port.Length > 0 && Port != "80" && Port != "443") sb.Append(':').Append(Port);
            sb.Append(Path);
            string q = SearchParams != null ? SearchParams.ToString() : (Query.Length > 0 ? (Query[0] == '?' ? Query.Substring(1) : Query) : "");
            if (q.Length > 0) sb.Append('?').Append(q);
            if (Hash.Length > 0) { if (Hash[0] != '#') sb.Append('#'); sb.Append(Hash); }
            return sb.ToString();
        }
    }
}

public sealed class JsFormData : JsObject
{
    public List<KeyValuePair<string, string>> Entries = new List<KeyValuePair<string, string>>();
    public JsFormData(JsObject proto) : base(proto) { }
    public override string ClassTag { get { return "FormData"; } }
}

public class JsBlobObject : JsObject
{
    public byte[] Data;
    public string ContentType;
    public int Size { get { return Data != null ? Data.Length : 0; } }
    public override string ClassTag { get { return "Blob"; } }

    public JsBlobObject(JsObject proto, byte[] data, string contentType) : base(proto)
    {
        Data = data != null ? data : new byte[0];
        ContentType = contentType != null ? contentType : "";
    }
}

public class JsFileObject : JsBlobObject
{
    public string FileName;
    public double LastModified;
    public override string ClassTag { get { return "File"; } }

    public JsFileObject(JsObject proto, byte[] data, string contentType, string fileName, double lastModified)
        : base(proto, data, contentType)
    {
        FileName = fileName != null ? fileName : "";
        LastModified = lastModified;
    }
}

public sealed class JsHeadersObject : JsObject
{
    public Dictionary<string, string> Map = new Dictionary<string, string>();
    public JsHeadersObject(JsObject proto) : base(proto) { }
    public override string ClassTag { get { return "Headers"; } }
}

public sealed class JsEventSourceObject : JsObject
{
    public string Url = "";
    public int ReadyState = 0; // 0=CONNECTING, 1=OPEN, 2=CLOSED
    public readonly Dictionary<string, List<JsFunction>> Listeners = new Dictionary<string, List<JsFunction>>();

    public JsEventSourceObject(JsObject proto) : base(proto) { }
    public override string ClassTag { get { return "EventSource"; } }
}
