using System;
using System.Collections.Generic;
using System.Text;

// ---------------------------------------------------------------------------
// The standard library: global constructors (Object, Array, String, Number,
// Function, Error types, RegExp, Date, Map, Set, JSON, Math, console) and all
// the prototype methods. Installed into a JsRealm by Install(). Everything is
// hand-written and Cosmos-safe.
// ---------------------------------------------------------------------------

public static class JsBuiltins
{
    // ---- error objects ----------------------------------------------------

    public static object CreateErrorObject(JsRealm realm, string name, string message)
    {
        JsObject proto = realm.ObjectPrototype;
        if (name == "TypeError") proto = realm.TypeErrorPrototype;
        else if (name == "RangeError") proto = realm.RangeErrorPrototype;
        else if (name == "SyntaxError") proto = realm.SyntaxErrorPrototype;
        else if (name == "ReferenceError") proto = realm.ReferenceErrorPrototype;
        else if (name == "EvalError") proto = realm.EvalErrorPrototype;
        else if (name == "URIError") proto = realm.URIErrorPrototype;
        else proto = realm.ErrorPrototype;

        JsObject e = new JsObject(proto);
        e.Define("message", JsProperty.Data(message ?? ""));
        e.Define("name", JsProperty.Data(name ?? "Error"));
        e.Define("stack", JsProperty.DataNonEnum(message ?? ""));
        return e;
    }

    public static object CreateRegExp(JsRealm realm, string pattern, string flags)
    {
        JsRegExp re = new JsRegExp(pattern, flags);
        JsObject o = new JsObject(realm.RegExpPrototype);
        o.RegExpValue = re;
        o.Define("source", JsProperty.DataNonEnum(re.Source));
        o.Define("flags", JsProperty.DataNonEnum(re.Flags));
        o.Define("global", JsProperty.DataNonEnum(re.Global));
        o.Define("ignoreCase", JsProperty.DataNonEnum(re.IgnoreCase));
        o.Define("multiline", JsProperty.DataNonEnum(re.Multiline));
        o.Define("dotAll", JsProperty.DataNonEnum(re.DotAll));
        o.Define("lastIndex", JsProperty.Data(0.0));
        return o;
    }

    // ---- install ----------------------------------------------------------

    public static void Install(JsRealm realm)
    {
        BuildPrototypes(realm);
        BuildObject(realm);
        BuildFunction(realm);
        BuildArray(realm);
        BuildString(realm);
        BuildNumber(realm);
        BuildBoolean(realm);
        BuildError(realm);
        BuildMath(realm);
        BuildJSON(realm);
        BuildConsole(realm);
        BuildSymbol(realm);
        BuildRegExp(realm);
        BuildDate(realm);
        BuildMapSet(realm);
        BuildGlobalFunctions(realm);
        // browser globals: Promise, timers, fetch, XHR, location, navigator...
        // (needs realm.Interp so it can attach the event loop)
        JsBrowser.Install(realm);
        HideIntrinsicMembers(realm);
    }

    // Intrinsic prototype members (and namespace-object members) are
    // non-enumerable in real JS, so `for..in` / Object.keys never surface them.
    // We define them with JsProperty.Data (enumerable) for brevity, then flip
    // the flag here in one pass over the intrinsics.
    private static void HideIntrinsicMembers(JsRealm realm)
    {
        HideOn(realm.ObjectPrototype);
        HideOn(realm.FunctionPrototype);
        HideOn(realm.ArrayPrototype);
        HideOn(realm.StringPrototype);
        HideOn(realm.NumberPrototype);
        HideOn(realm.BooleanPrototype);
        HideOn(realm.SymbolPrototype);
        HideOn(realm.ErrorPrototype);
        HideOn(realm.TypeErrorPrototype);
        HideOn(realm.RangeErrorPrototype);
        HideOn(realm.SyntaxErrorPrototype);
        HideOn(realm.ReferenceErrorPrototype);
        HideOn(realm.EvalErrorPrototype);
        HideOn(realm.URIErrorPrototype);
        HideOn(realm.RegExpPrototype);
        HideOn(realm.DatePrototype);
        HideOn(realm.MapPrototype);
        HideOn(realm.SetPrototype);
        HideOn(realm.IteratorPrototype);
        HideOn(realm.PromisePrototype);
        HideOn(realm.ResponsePrototype);
        HideOn(realm.XhrPrototype);

        // namespace objects: Math, JSON, console keep their methods non-enumerable too
        HideOn(realm.MathObj);
        HideOn(realm.JsonObj);
        HideOn(realm.ConsoleObj);
    }

    private static void HideOn(JsObject o)
    {
        if (o == null) return;
        List<string> keys = o.OwnKeys(false);
        for (int i = 0; i < keys.Count; i++)
        {
            JsProperty p;
            if (o.TryGetRaw(keys[i], out p)) p.Enumerable = false;
        }
    }

    private static void BuildPrototypes(JsRealm realm)
    {
        realm.ObjectPrototype = new JsObject(null);
        realm.FunctionPrototype = new JsObject(realm.ObjectPrototype);
        realm.ArrayPrototype = new JsArray(realm.ObjectPrototype);
        realm.StringPrototype = new JsObject(realm.ObjectPrototype);
        realm.NumberPrototype = new JsObject(realm.ObjectPrototype);
        realm.BooleanPrototype = new JsObject(realm.ObjectPrototype);
        realm.SymbolPrototype = new JsObject(realm.ObjectPrototype);
        realm.ErrorPrototype = new JsObject(realm.ObjectPrototype);
        realm.TypeErrorPrototype = new JsObject(realm.ErrorPrototype);
        realm.RangeErrorPrototype = new JsObject(realm.ErrorPrototype);
        realm.SyntaxErrorPrototype = new JsObject(realm.ErrorPrototype);
        realm.ReferenceErrorPrototype = new JsObject(realm.ErrorPrototype);
        realm.EvalErrorPrototype = new JsObject(realm.ErrorPrototype);
        realm.URIErrorPrototype = new JsObject(realm.ErrorPrototype);
        realm.RegExpPrototype = new JsObject(realm.ObjectPrototype);
        realm.DatePrototype = new JsObject(realm.ObjectPrototype);
        realm.MapPrototype = new JsObject(realm.ObjectPrototype);
        realm.SetPrototype = new JsObject(realm.ObjectPrototype);
        realm.PromisePrototype = new JsObject(realm.ObjectPrototype);
        realm.IteratorPrototype = new JsObject(realm.ObjectPrototype);

        // Function.prototype is itself callable (a no-op)
        realm.FunctionPrototype.Define("length", JsProperty.DataNonEnum(0.0));
        realm.FunctionPrototype.Define("name", JsProperty.DataNonEnum(""));
        realm.FunctionPrototype.Define("toString", JsProperty.Data(
            realm.MakeMethod("toString", delegate (JsCallArgs a) { return "function () { [native code] }"; }, 0)));
    }

    private static void SetTag(JsRealm realm, JsObject proto, string tag)
    {
        proto.SetWellKnown(JsWellKnown.ToStringTag, tag);
    }

    // ---- Object -----------------------------------------------------------

    private static void BuildObject(JsRealm realm)
    {
        JsObject op = realm.ObjectPrototype;
        SetTag(realm, op, "Object");

        op.Define("toString", JsProperty.Data(realm.MakeMethod("toString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            if (t == null) return "[object Undefined]";
            if (t is JsUndefined) return "[object Undefined]";
            if (t is JsNull) return "[object Null]";
            return "[object " + ObjectClassTag(t) + "]";
        }, 0)));

        op.Define("toLocaleString", JsProperty.Data(realm.MakeMethod("toLocaleString", delegate (JsCallArgs a)
        {
            object v = JsValueOps.ToPrimitive(a.ThisValue, JsHint.String);
            return JsValueOps.ToStringValue(v);
        }, 0)));

        op.Define("valueOf", JsProperty.Data(realm.MakeMethod("valueOf", delegate (JsCallArgs a)
        {
            return a.ThisValue;
        }, 0)));

        op.Define("hasOwnProperty", JsProperty.Data(realm.MakeMethod("hasOwnProperty", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            if (!(t is JsObject)) return false;
            object key = realm.Interp.ToPropertyKey(a.Get(0));
            string ks = key as string;
            if (ks != null) return ((JsObject)t).OwnHas(ks);
            if (key is JsSymbol) return ((JsObject)t).OwnHas((JsSymbol)key);
            return false;
        }, 1)));

        op.Define("propertyIsEnumerable", JsProperty.Data(realm.MakeMethod("propertyIsEnumerable", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            if (!(t is JsObject)) return false;
            object key = realm.Interp.ToPropertyKey(a.Get(0));
            string ks = key as string;
            if (ks == null) return false;
            JsProperty p;
            if (((JsObject)t).TryGetRaw(ks, out p)) return p.Enumerable;
            return false;
        }, 1)));

        op.Define("isPrototypeOf", JsProperty.Data(realm.MakeMethod("isPrototypeOf", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (!(v is JsObject)) return false;
            JsObject p = ((JsObject)v).Prototype;
            object t = a.ThisValue;
            while (p != null) { if (ReferenceEquals(p, t)) return true; p = p.Prototype; }
            return false;
        }, 1)));

        op.Define("toJSON", JsProperty.Data(realm.MakeMethod("toJSON", delegate (JsCallArgs a)
        {
            return a.ThisValue;
        }, 1)));

        // __proto__ accessor
        op.Define("__proto__", JsProperty.Accessor(
            realm.MakeMethod("get __proto__", delegate (JsCallArgs a)
            {
                object t = a.ThisValue;
                return t is JsObject ? (object)((JsObject)t).Prototype : (object)JsUndefined.Instance;
            }, 0),
            realm.MakeMethod("set __proto__", delegate (JsCallArgs a)
            {
                object t = a.ThisValue;
                object v = a.Get(0);
                if (t is JsObject && v is JsObject) ((JsObject)t).Prototype = (JsObject)v;
                return JsUndefined.Instance;
            }, 1)));

        // Object constructor
        realm.ObjectCtor = realm.MakeCtor("Object", ObjectCtorFn, 1, op);
        realm.Global.Define("Object", JsProperty.Data(realm.ObjectCtor));

        realm.ObjectCtor.Define("keys", JsProperty.Data(realm.MakeMethod("keys", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            JsArray outArr = realm.NewArray();
            if (v is JsArray)
            {
                List<string> keys = ((JsArray)v).IndexKeys();
                for (int i = 0; i < keys.Count; i++) outArr.Push(keys[i]);
            }
            else if (v is JsObject)
            {
                List<string> keys = ((JsObject)v).OwnKeys(true);
                for (int i = 0; i < keys.Count; i++) outArr.Push(keys[i]);
            }
            return outArr;
        }, 1)));

        realm.ObjectCtor.Define("values", JsProperty.Data(realm.MakeMethod("values", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            JsArray outArr = realm.NewArray();
            List<string> keys = GetKeysList(v);
            for (int i = 0; i < keys.Count; i++) outArr.Push(realm.Interp.GetMemberDirect(v, keys[i]));
            return outArr;
        }, 1)));

        realm.ObjectCtor.Define("entries", JsProperty.Data(realm.MakeMethod("entries", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            JsArray outArr = realm.NewArray();
            List<string> keys = GetKeysList(v);
            for (int i = 0; i < keys.Count; i++)
            {
                JsArray pair = realm.NewArray();
                pair.Push(keys[i]);
                pair.Push(realm.Interp.GetMemberDirect(v, keys[i]));
                outArr.Push(pair);
            }
            return outArr;
        }, 1)));

        realm.ObjectCtor.Define("assign", JsProperty.Data(realm.MakeMethod("assign", delegate (JsCallArgs a)
        {
            object target = a.Get(0);
            if (!(target is JsObject)) target = realm.NewObject();
            for (int i = 1; i < a.ArgCount; i++)
            {
                object src = a.Get(i);
                if (src is JsArray)
                {
                    List<string> keys = ((JsArray)src).IndexKeys();
                    for (int k = 0; k < keys.Count; k++)
                        realm.Interp.SetMember(target, keys[k], realm.Interp.GetMemberDirect(src, keys[k]));
                }
                else if (src is JsObject)
                {
                    List<string> keys = ((JsObject)src).OwnKeys(true);
                    for (int k = 0; k < keys.Count; k++)
                        realm.Interp.SetMember(target, keys[k], realm.Interp.GetMemberDirect(src, keys[k]));
                }
            }
            return target;
        }, 2)));

        realm.ObjectCtor.Define("create", JsProperty.Data(realm.MakeMethod("create", delegate (JsCallArgs a)
        {
            object proto = a.Get(0);
            JsObject o = new JsObject(proto as JsObject);
            object props = a.Get(1);
            if (props is JsObject) DefineProperties(realm, o, props);
            return o;
        }, 2)));

        realm.ObjectCtor.Define("getPrototypeOf", JsProperty.Data(realm.MakeMethod("getPrototypeOf", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            return v is JsObject ? (object)((JsObject)v).Prototype : (object)JsUndefined.Instance;
        }, 1)));

        realm.ObjectCtor.Define("setPrototypeOf", JsProperty.Data(realm.MakeMethod("setPrototypeOf", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            object p = a.Get(1);
            if (v is JsObject) ((JsObject)v).Prototype = p as JsObject;
            return v;
        }, 2)));

        realm.ObjectCtor.Define("freeze", JsProperty.Data(realm.MakeMethod("freeze", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (v is JsObject)
            {
                JsObject o = (JsObject)v;
                o.Extensible = false;
                List<string> keys = o.OwnKeys(false);
                for (int i = 0; i < keys.Count; i++)
                {
                    JsProperty p;
                    if (o.TryGetRaw(keys[i], out p)) { p.Writable = false; p.Configurable = false; }
                }
            }
            return v;
        }, 1)));

        realm.ObjectCtor.Define("isFrozen", JsProperty.Data(realm.MakeMethod("isFrozen", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            return v is JsObject ? (object)!((JsObject)v).Extensible : (object)true;
        }, 1)));

        realm.ObjectCtor.Define("defineProperty", JsProperty.Data(realm.MakeMethod("defineProperty", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (!(v is JsObject)) return v;
            object key = realm.Interp.ToPropertyKey(a.Get(1));
            object desc = a.Get(2);
            string ks = key as string;
            if (ks == null) return v;
            DefineOneProperty(realm, (JsObject)v, ks, desc);
            return v;
        }, 3)));

        realm.ObjectCtor.Define("defineProperties", JsProperty.Data(realm.MakeMethod("defineProperties", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (v is JsObject) DefineProperties(realm, (JsObject)v, a.Get(1));
            return v;
        }, 2)));

        realm.ObjectCtor.Define("getOwnPropertyNames", JsProperty.Data(realm.MakeMethod("getOwnPropertyNames", delegate (JsCallArgs a)
        {
            JsArray outArr = realm.NewArray();
            object v = a.Get(0);
            if (v is JsArray)
            {
                for (int i = 0; i < ((JsArray)v).Length; i++) outArr.Push(i.ToString());
                List<string> others = ((JsArray)v).OwnKeys(false);
                for (int i = 0; i < others.Count; i++) outArr.Push(others[i]);
            }
            else if (v is JsObject)
            {
                List<string> keys = ((JsObject)v).OwnKeys(false);
                for (int i = 0; i < keys.Count; i++) outArr.Push(keys[i]);
            }
            return outArr;
        }, 1)));

        realm.ObjectCtor.Define("fromEntries", JsProperty.Data(realm.MakeMethod("fromEntries", delegate (JsCallArgs a)
        {
            JsObject o = realm.NewObject();
            List<object> items = realm.Interp.ToList(a.Get(0));
            for (int i = 0; i < items.Count; i++)
            {
                List<object> pair = realm.Interp.ToList(items[i]);
                if (pair.Count >= 2)
                    o.SetProperty(JsValueOps.ToStringValue(pair[0]), pair[1]);
            }
            return o;
        }, 1)));
    }

    private static List<string> GetKeysList(object v)
    {
        if (v is JsArray) return ((JsArray)v).IndexKeys();
        if (v is JsObject) return ((JsObject)v).OwnKeys(true);
        return new List<string>();
    }

    private static object ObjectCtorFn(JsCallArgs a)
    {
        JsRealm realm = a.Interp.Realm;
        if (a.ArgCount == 0 || a.Get(0) is JsUndefined || a.Get(0) is JsNull)
            return realm.NewObject();
        object v = a.Get(0);
        if (v is JsObject) return v;
        // wrap primitive
        JsObject proto = a.Interp.PrototypeOfValue(v);
        JsObject o = new JsObject(proto);
        o.SetProperty("value", v);
        return o;
    }

    private static void DefineProperties(JsRealm realm, JsObject target, object props)
    {
        if (!(props is JsObject)) return;
        List<string> keys = ((JsObject)props).OwnKeys(false);
        for (int i = 0; i < keys.Count; i++)
        {
            object desc = realm.Interp.GetMemberDirect(props, keys[i]);
            DefineOneProperty(realm, target, keys[i], desc);
        }
    }

    private static void DefineOneProperty(JsRealm realm, JsObject target, string key, object desc)
    {
        if (!(desc is JsObject)) return;
        JsObject d = (JsObject)desc;
        JsProperty p = new JsProperty();
        object get, set, val, wr, en, cf;
        bool hasGet = d.GetProperty("get", out get) && get is JsFunction;
        bool hasSet = d.GetProperty("set", out set) && set is JsFunction;
        if (hasGet || hasSet)
        {
            p.Kind = JsPropKind.Accessor;
            p.Getter = hasGet ? (JsFunction)get : null;
            p.Setter = hasSet ? (JsFunction)set : null;
            p.Enumerable = d.GetProperty("enumerable", out en) && JsValueOps.ToBoolean(en);
            p.Configurable = d.GetProperty("configurable", out cf) && JsValueOps.ToBoolean(cf);
        }
        else
        {
            p.Kind = JsPropKind.Data;
            p.Value = d.GetProperty("value", out val) ? val : JsUndefined.Instance;
            p.Writable = !(d.GetProperty("writable", out wr)) || JsValueOps.ToBoolean(wr);
            p.Enumerable = d.GetProperty("enumerable", out en) && JsValueOps.ToBoolean(en);
            p.Configurable = d.GetProperty("configurable", out cf) && JsValueOps.ToBoolean(cf);
        }
        target.Define(key, p);
    }

    private static string ObjectClassTag(object v)
    {
        if (v is JsArray) return "Array";
        if (v is JsFunction) return "Function";
        if (v is JsDate) return "Date";
        if (v is JsRegExpObject) return "RegExp";
        if (v is JsMapObject) return "Map";
        if (v is JsSetObject) return "Set";
        return "Object";
    }

    // ---- Function ---------------------------------------------------------

    private static void BuildFunction(JsRealm realm)
    {
        JsObject fp = realm.FunctionPrototype;
        fp.Define("call", JsProperty.Data(realm.MakeMethod("call", delegate (JsCallArgs a)
        {
            object fn = a.ThisValue;
            object thisArg = a.Get(0);
            List<object> args = new List<object>();
            for (int i = 1; i < a.ArgCount; i++) args.Add(a.Get(i));
            return realm.Interp.Call(fn, thisArg, args.ToArray(), false);
        }, 1)));

        fp.Define("apply", JsProperty.Data(realm.MakeMethod("apply", delegate (JsCallArgs a)
        {
            object fn = a.ThisValue;
            object thisArg = a.Get(0);
            List<object> list = a.ArgCount > 1 ? realm.Interp.ToList(a.Get(1)) : new List<object>();
            return realm.Interp.Call(fn, thisArg, list.ToArray(), false);
        }, 2)));

        fp.Define("bind", JsProperty.Data(realm.MakeMethod("bind", delegate (JsCallArgs a)
        {
            object target = a.ThisValue;
            if (!(target is JsFunction)) throw JsErr.Type("Function.prototype.bind called on a non-function");
            object thisArg = a.Get(0);
            object[] boundArgs = new object[a.ArgCount > 1 ? a.ArgCount - 1 : 0];
            for (int i = 1; i < a.ArgCount; i++) boundArgs[i - 1] = a.Get(i);
            JsBoundFunction bf = new JsBoundFunction();
            bf.Prototype = realm.FunctionPrototype;
            bf.Target = (JsFunction)target;
            bf.BoundThis = thisArg;
            bf.BoundArgs = boundArgs;
            bf.Name = ((JsFunction)target).Name;
            bf.Constructable = false;
            return bf;
        }, 1)));

        fp.Define("toString", JsProperty.Data(realm.MakeMethod("toString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            JsFunction fn = t as JsFunction;
            if (fn != null) return "function " + (fn.Name ?? "") + "() { [native code] }";
            return "function () { [native code] }";
        }, 0)));

        realm.FunctionCtor = realm.MakeCtor("Function", delegate (JsCallArgs a)
        {
            // Function("a","b","return a+b") — compile a body
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < a.ArgCount; i++)
            {
                if (i == a.ArgCount - 1) break;
                if (i > 0) sb.Append(",");
                sb.Append(JsValueOps.ToStringValue(a.Get(i)));
            }
            string body = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(a.ArgCount - 1)) : "";
            string src = "function anonymous(" + sb + ") {\n" + body + "\n}";
            JsProgram prog = JsParser.ParseProgram(src);
            JsFunctionDecl fd = null;
            if (prog.Body.Count > 0 && prog.Body[0].Kind == JsKind.FunctionDecl) fd = (JsFunctionDecl)prog.Body[0];
            if (fd == null) throw JsErr.Syntax("Invalid function body");
            JsFunction f = realm.Interp.MakeFunctionPublic(fd.Fn, realm.Interp.GlobalScope);
            return f;
        }, 1, realm.FunctionPrototype);
        // Function.prototype is its own prototype
        realm.FunctionCtor.Define("prototype", JsProperty.Data(realm.FunctionPrototype));
        realm.Global.Define("Function", JsProperty.Data(realm.FunctionCtor));
    }

    // ---- Array ------------------------------------------------------------

    private static JsArray ThisArray(JsCallArgs a)
    {
        return a.ThisValue as JsArray;
    }

    private static object ArrElem(JsArray arr, int i) { return arr != null ? arr.GetIndex(i) : JsUndefined.Instance; }

    private static int ToIndex(object v, int def)
    {
        double d = JsValueOps.ToNumber(v);
        if (double.IsNaN(d)) return 0;
        int n = (int)d;
        if (n < 0) n = 0;
        return n;
    }

    private static int ToInteger(object v)
    {
        double d = JsValueOps.ToNumber(v);
        if (double.IsNaN(d)) return 0;
        return (int)d;
    }

    private static int ToLength(object v)
    {
        double d = JsValueOps.ToNumber(v);
        if (double.IsNaN(d) || d <= 0) return 0;
        if (d > 2000000) return 2000000;
        return (int)d;
    }

    private static void PushArgs(JsArray target, JsCallArgs a, int from)
    {
        for (int i = from; i < a.ArgCount; i++) target.Push(a.Get(i));
    }

    private static bool HasDontEnumLengthBug() { return false; }

    public static void BuildArray(JsRealm realm)
    {
        JsObject ap = realm.ArrayPrototype;
        SetTag(realm, ap, "Array");

        // length lives on the prototype for non-array objects; arrays use Items
        ap.Define("length", JsProperty.DataNonEnum(0.0));

        ap.Define("push", JsProperty.Data(realm.MakeMethod("push", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return (double)(a.ArgCount);
            for (int i = 0; i < a.ArgCount; i++) arr.Push(a.Get(i));
            return (double)arr.Length;
        }, 1)));

        ap.Define("pop", JsProperty.Data(realm.MakeMethod("pop", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null || arr.Length == 0) return JsUndefined.Instance;
            object v = arr.GetIndex(arr.Length - 1);
            arr.SetLength(arr.Length - 1);
            return v;
        }, 0)));

        ap.Define("shift", JsProperty.Data(realm.MakeMethod("shift", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null || arr.Length == 0) return JsUndefined.Instance;
            object v = arr.GetIndex(0);
            arr.Items.RemoveAt(0);
            return v;
        }, 0)));

        ap.Define("unshift", JsProperty.Data(realm.MakeMethod("unshift", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return 0.0;
            for (int i = a.ArgCount - 1; i >= 0; i--) arr.Items.Insert(0, a.Get(i));
            return (double)arr.Length;
        }, 1)));

        ap.Define("splice", JsProperty.Data(realm.MakeMethod("splice", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray result = realm.NewArray();
            if (arr == null) return result;
            int len = arr.Length;
            int start = ToInteger(a.Get(0));
            if (start < 0) start = System.Math.Max(len + start, 0);
            else if (start > len) start = len;
            int deleteCount = a.ArgCount <= 1 ? len - start
                : a.ArgCount == 2 ? ToInteger(a.Get(1))
                : System.Math.Max(0, System.Math.Min(ToInteger(a.Get(1)), len - start));
            for (int i = 0; i < deleteCount; i++) result.Push(arr.GetIndex(start + i));
            arr.Items.RemoveRange(start, deleteCount);
            for (int i = a.ArgCount - 1; i >= 2; i--) arr.Items.Insert(start, a.Get(i));
            return result;
        }, 2)));

        ap.Define("slice", JsProperty.Data(realm.MakeMethod("slice", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray result = realm.NewArray();
            if (arr == null) return result;
            int len = arr.Length;
            int start = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            int end = a.ArgCount > 1 ? ToInteger(a.Get(1)) : len;
            if (start < 0) start = System.Math.Max(len + start, 0);
            if (end < 0) end = System.Math.Max(len + end, 0);
            else if (end > len) end = len;
            for (int i = start; i < end; i++) result.Push(arr.GetIndex(i));
            return result;
        }, 2)));

        ap.Define("concat", JsProperty.Data(realm.MakeMethod("concat", delegate (JsCallArgs a)
        {
            JsArray result = realm.NewArray();
            JsArray self = ThisArray(a);
            if (self != null) for (int i = 0; i < self.Length; i++) result.Push(self.GetIndex(i));
            for (int i = 0; i < a.ArgCount; i++)
            {
                object v = a.Get(i);
                if (v is JsArray)
                {
                    JsArray sub = (JsArray)v;
                    for (int k = 0; k < sub.Length; k++) result.Push(sub.GetIndex(k));
                }
                else result.Push(v);
            }
            return result;
        }, 1)));

        ap.Define("join", JsProperty.Data(realm.MakeMethod("join", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            string sep = a.ArgCount > 0 && !(a.Get(0) is JsUndefined) ? JsValueOps.ToStringValue(a.Get(0)) : ",";
            if (arr == null) return "";
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) sb.Append(sep);
                object v = arr.GetIndex(i);
                if (!(v is JsUndefined) && !(v is JsNull)) sb.Append(JsValueOps.ToStringValue(v));
            }
            return sb.ToString();
        }, 1)));

        ap.Define("indexOf", JsProperty.Data(realm.MakeMethod("indexOf", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return -1.0;
            object search = a.Get(0);
            int from = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
            if (from < 0) from = System.Math.Max(arr.Length + from, 0);
            for (int i = from; i < arr.Length; i++)
                if (JsValueOps.StrictEquals(arr.GetIndex(i), search)) return (double)i;
            return -1.0;
        }, 1)));

        ap.Define("lastIndexOf", JsProperty.Data(realm.MakeMethod("lastIndexOf", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return -1.0;
            object search = a.Get(0);
            int from = a.ArgCount > 1 ? ToInteger(a.Get(1)) : arr.Length - 1;
            if (from < 0) from = arr.Length + from;
            if (from > arr.Length - 1) from = arr.Length - 1;
            for (int i = from; i >= 0; i--)
                if (JsValueOps.StrictEquals(arr.GetIndex(i), search)) return (double)i;
            return -1.0;
        }, 1)));

        ap.Define("includes", JsProperty.Data(realm.MakeMethod("includes", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return false;
            object search = a.Get(0);
            for (int i = 0; i < arr.Length; i++)
                if (JsValueOps.SameValueZero(arr.GetIndex(i), search)) return true;
            return false;
        }, 1)));

        ap.Define("reverse", JsProperty.Data(realm.MakeMethod("reverse", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return a.ThisValue;
            int i = 0, j = arr.Length - 1;
            while (i < j)
            {
                object t = arr.Items[i];
                arr.Items[i] = arr.Items[j];
                arr.Items[j] = t;
                i++; j--;
            }
            return arr;
        }, 0)));

        ap.Define("sort", JsProperty.Data(realm.MakeMethod("sort", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return a.ThisValue;
            JsFunction cmp = a.ArgCount > 0 && a.Get(0) is JsFunction ? (JsFunction)a.Get(0) : null;
            // insertion sort (stable; fine for small arrays)
            for (int i = 1; i < arr.Length; i++)
            {
                object key = arr.Items[i];
                int j = i - 1;
                while (j >= 0 && SortLess(a, cmp, key, arr.Items[j]))
                {
                    arr.Items[j + 1] = arr.Items[j];
                    j--;
                }
                arr.Items[j + 1] = key;
            }
            return arr;
        }, 1)));

        ap.Define("forEach", JsProperty.Data(realm.MakeMethod("forEach", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return JsUndefined.Instance;
            object thisArg = a.Get(1);
            for (int i = 0; i < arr.Length; i++)
                realm.Interp.Call(fn, thisArg, new object[] { arr.GetIndex(i), (double)i, arr }, false);
            return JsUndefined.Instance;
        }, 1)));

        ap.Define("map", JsProperty.Data(realm.MakeMethod("map", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray result = realm.NewArray();
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return result;
            object thisArg = a.Get(1);
            for (int i = 0; i < arr.Length; i++)
                result.Push(realm.Interp.Call(fn, thisArg, new object[] { arr.GetIndex(i), (double)i, arr }, false));
            return result;
        }, 1)));

        ap.Define("filter", JsProperty.Data(realm.MakeMethod("filter", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray result = realm.NewArray();
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return result;
            object thisArg = a.Get(1);
            for (int i = 0; i < arr.Length; i++)
            {
                object v = arr.GetIndex(i);
                if (JsValueOps.ToBoolean(realm.Interp.Call(fn, thisArg, new object[] { v, (double)i, arr }, false)))
                    result.Push(v);
            }
            return result;
        }, 1)));

        ap.Define("reduce", JsProperty.Data(realm.MakeMethod("reduce", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) throw JsErr.Type("reduce of non-function");
            int i = 0;
            object acc;
            if (a.ArgCount > 1) { acc = a.Get(1); }
            else
            {
                if (arr.Length == 0) throw JsErr.Type("Reduce of empty array with no initial value");
                acc = arr.GetIndex(0); i = 1;
            }
            for (; i < arr.Length; i++)
                acc = realm.Interp.Call(fn, JsUndefined.Instance, new object[] { acc, arr.GetIndex(i), (double)i, arr }, false);
            return acc;
        }, 1)));

        ap.Define("reduceRight", JsProperty.Data(realm.MakeMethod("reduceRight", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) throw JsErr.Type("reduceRight of non-function");
            int i = arr.Length - 1;
            object acc;
            if (a.ArgCount > 1) { acc = a.Get(1); }
            else
            {
                if (arr.Length == 0) throw JsErr.Type("Reduce of empty array with no initial value");
                acc = arr.GetIndex(i); i--;
            }
            for (; i >= 0; i--)
                acc = realm.Interp.Call(fn, JsUndefined.Instance, new object[] { acc, arr.GetIndex(i), (double)i, arr }, false);
            return acc;
        }, 1)));

        ap.Define("find", JsProperty.Data(realm.MakeMethod("find", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return JsUndefined.Instance;
            object thisArg = a.Get(1);
            for (int i = 0; i < arr.Length; i++)
            {
                object v = arr.GetIndex(i);
                if (JsValueOps.ToBoolean(realm.Interp.Call(fn, thisArg, new object[] { v, (double)i, arr }, false)))
                    return v;
            }
            return JsUndefined.Instance;
        }, 1)));

        ap.Define("findIndex", JsProperty.Data(realm.MakeMethod("findIndex", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return -1.0;
            object thisArg = a.Get(1);
            for (int i = 0; i < arr.Length; i++)
            {
                if (JsValueOps.ToBoolean(realm.Interp.Call(fn, thisArg, new object[] { arr.GetIndex(i), (double)i, arr }, false)))
                    return (double)i;
            }
            return -1.0;
        }, 1)));

        ap.Define("every", JsProperty.Data(realm.MakeMethod("every", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return true;
            object thisArg = a.Get(1);
            for (int i = 0; i < arr.Length; i++)
                if (!JsValueOps.ToBoolean(realm.Interp.Call(fn, thisArg, new object[] { arr.GetIndex(i), (double)i, arr }, false)))
                    return false;
            return true;
        }, 1)));

        ap.Define("some", JsProperty.Data(realm.MakeMethod("some", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return false;
            object thisArg = a.Get(1);
            for (int i = 0; i < arr.Length; i++)
                if (JsValueOps.ToBoolean(realm.Interp.Call(fn, thisArg, new object[] { arr.GetIndex(i), (double)i, arr }, false)))
                    return true;
            return false;
        }, 1)));

        ap.Define("fill", JsProperty.Data(realm.MakeMethod("fill", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return a.ThisValue;
            object value = a.Get(0);
            int len = arr.Length;
            int start = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
            int end = a.ArgCount > 2 ? ToInteger(a.Get(2)) : len;
            if (start < 0) start = System.Math.Max(len + start, 0);
            if (end < 0) end = System.Math.Max(len + end, 0);
            for (int i = start; i < end && i < len; i++) arr.Items[i] = value;
            return arr;
        }, 1)));

        ap.Define("copyWithin", JsProperty.Data(realm.MakeMethod("copyWithin", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return a.ThisValue;
            int len = arr.Length;
            int target = ToInteger(a.Get(0));
            int start = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
            int end = a.ArgCount > 2 ? ToInteger(a.Get(2)) : len;
            if (target < 0) target += len;
            if (start < 0) start += len;
            if (end < 0) end += len;
            int count = System.Math.Min(end - start, len - target);
            for (int i = 0; i < count; i++) arr.Items[target + i] = arr.Items[start + i];
            return arr;
        }, 2)));

        ap.Define("flat", JsProperty.Data(realm.MakeMethod("flat", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray result = realm.NewArray();
            if (arr == null) return result;
            int depth = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 1;
            if (depth < 0) depth = 0;
            FlatInto(realm, arr, result, depth);
            return result;
        }, 0)));

        ap.Define("flatMap", JsProperty.Data(realm.MakeMethod("flatMap", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray result = realm.NewArray();
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return result;
            object thisArg = a.Get(1);
            for (int i = 0; i < arr.Length; i++)
            {
                object v = arr.GetIndex(i);
                object mapped = realm.Interp.Call(fn, thisArg, new object[] { v, (double)i, arr }, false);
                if (mapped is JsArray)
                {
                    JsArray sub = (JsArray)mapped;
                    for (int k = 0; k < sub.Length; k++) result.Push(sub.GetIndex(k));
                }
                else result.Push(mapped);
            }
            return result;
        }, 1)));

        // ---- Array constructor ----
        realm.ArrayCtor = realm.MakeCtor("Array", delegate (JsCallArgs a)
        {
            JsArray arr = realm.NewArray();
            if (a.ArgCount == 1 && a.Get(0) is double)
            {
                int len = ToLength(a.Get(0));
                arr.SetLength(len);
            }
            else
            {
                PushArgs(arr, a, 0);
            }
            return arr;
        }, 1, ap);
        realm.Global.Define("Array", JsProperty.Data(realm.ArrayCtor));

        realm.ArrayCtor.Define("isArray", JsProperty.Data(realm.MakeMethod("isArray", delegate (JsCallArgs a)
        {
            return a.Get(0) is JsArray;
        }, 1)));

        realm.ArrayCtor.Define("from", JsProperty.Data(realm.MakeMethod("from", delegate (JsCallArgs a)
        {
            JsArray result = realm.NewArray();
            object src = a.Get(0);
            JsFunction fn = a.ArgCount > 1 && a.Get(1) is JsFunction ? (JsFunction)a.Get(1) : null;
            object thisArg = fn != null ? a.Get(1) : JsUndefined.Instance;
            if (src is JsArray)
            {
                JsArray s = (JsArray)src;
                for (int i = 0; i < s.Length; i++)
                {
                    object v = s.GetIndex(i);
                    result.Push(fn != null ? realm.Interp.Call(fn, thisArg, new object[] { v, (double)i }, false) : v);
                }
            }
            else
            {
                List<object> list = realm.Interp.ToList(src);
                for (int i = 0; i < list.Count; i++)
                    result.Push(fn != null ? realm.Interp.Call(fn, thisArg, new object[] { list[i], (double)i }, false) : list[i]);
            }
            return result;
        }, 1)));

        realm.ArrayCtor.Define("of", JsProperty.Data(realm.MakeMethod("of", delegate (JsCallArgs a)
        {
            JsArray arr = realm.NewArray();
            PushArgs(arr, a, 0);
            return arr;
        }, 1)));
    }

    private static bool SortLess(JsCallArgs a, JsFunction cmp, object x, object y)
    {
        if (cmp != null)
        {
            JsInterpreter interp = a.Interp;
            object r = interp.Call(cmp, JsUndefined.Instance, new object[] { x, y }, false);
            return JsValueOps.ToNumber(r) < 0;
        }
        string sx = JsValueOps.ToStringValue(x);
        string sy = JsValueOps.ToStringValue(y);
        return string.CompareOrdinal(sx, sy) < 0;
    }

    private static void FlatInto(JsRealm realm, JsArray src, JsArray target, int depth)
    {
        for (int i = 0; i < src.Length; i++)
        {
            object v = src.GetIndex(i);
            if (depth > 0 && v is JsArray) FlatInto(realm, (JsArray)v, target, depth - 1);
            else target.Push(v);
        }
    }

    // ---- String ------------------------------------------------------------

    private static string TcLower(string s)
    {
        char[] c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
        {
            if (c[i] >= 'A' && c[i] <= 'Z') c[i] = (char)(c[i] + 32);
        }
        return new string(c);
    }

    private static string TcUpper(string s)
    {
        char[] c = s.ToCharArray();
        for (int i = 0; i < c.Length; i++)
        {
            if (c[i] >= 'a' && c[i] <= 'z') c[i] = (char)(c[i] - 32);
        }
        return new string(c);
    }

    private static int IndexOfFrom(string s, string search, int from)
    {
        if (from < 0) from = 0;
        if (from > s.Length) from = s.Length;
        if (search.Length == 0) return from <= s.Length ? from : s.Length;
        for (int i = from; i + search.Length <= s.Length; i++)
        {
            bool ok = true;
            for (int k = 0; k < search.Length; k++) if (s[i + k] != search[k]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    private static int LastIndexOfFrom(string s, string search, int from)
    {
        if (from > s.Length - search.Length) from = s.Length - search.Length;
        if (search.Length == 0) return from < 0 ? 0 : from;
        for (int i = from; i >= 0; i--)
        {
            bool ok = true;
            for (int k = 0; k < search.Length; k++) if (s[i + k] != search[k]) { ok = false; break; }
            if (ok) return i;
        }
        return -1;
    }

    private static bool StartsWith(string s, string p)
    {
        if (p.Length > s.Length) return false;
        for (int i = 0; i < p.Length; i++) if (s[i] != p[i]) return false;
        return true;
    }

    private static bool EndsWith(string s, string p)
    {
        if (p.Length > s.Length) return false;
        int off = s.Length - p.Length;
        for (int i = 0; i < p.Length; i++) if (s[off + i] != p[i]) return false;
        return true;
    }

    public static void BuildString(JsRealm realm)
    {
        JsObject sp = realm.StringPrototype;
        SetTag(realm, sp, "String");
        sp.Define("length", JsProperty.DataNonEnum(0.0));

        JsNativeFn strMethod = null; // placeholder to keep pattern clear

        DefineStr(realm, "charAt", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int i = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            if (i < 0 || i >= s.Length) return "";
            return s[i].ToString();
        });
        DefineStr(realm, "charCodeAt", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int i = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            if (i < 0 || i >= s.Length) return double.NaN;
            return (double)s[i];
        });
        DefineStr(realm, "codePointAt", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int i = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            if (i < 0 || i >= s.Length) return JsUndefined.Instance;
            return (double)s[i];
        });
        DefineStr(realm, "concat", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            StringBuilder sb = new StringBuilder(s);
            for (int i = 0; i < a.ArgCount; i++) sb.Append(JsValueOps.ToStringValue(a.Get(i)));
            return sb.ToString();
        });
        DefineStr(realm, "includes", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            string search = JsValueOps.ToStringValue(a.Get(0));
            return IndexOfFrom(s, search, 0) >= 0;
        });
        DefineStr(realm, "startsWith", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            return StartsWith(s, JsValueOps.ToStringValue(a.Get(0)));
        });
        DefineStr(realm, "endsWith", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            return EndsWith(s, JsValueOps.ToStringValue(a.Get(0)));
        });
        DefineStr(realm, "indexOf", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            string search = JsValueOps.ToStringValue(a.Get(0));
            int from = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
            return (double)IndexOfFrom(s, search, from);
        });
        DefineStr(realm, "lastIndexOf", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            string search = JsValueOps.ToStringValue(a.Get(0));
            int from = a.ArgCount > 1 ? ToInteger(a.Get(1)) : s.Length;
            return (double)LastIndexOfFrom(s, search, from);
        });
        DefineStr(realm, "localeCompare", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            string o = JsValueOps.ToStringValue(a.Get(0));
            return (double)string.CompareOrdinal(s, o);
        });
        DefineStr(realm, "slice", 2, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int start = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            int end = a.ArgCount > 1 && !(a.Get(1) is JsUndefined) ? ToInteger(a.Get(1)) : s.Length;
            if (start < 0) start = System.Math.Max(s.Length + start, 0);
            else if (start > s.Length) start = s.Length;
            if (end < 0) end = System.Math.Max(s.Length + end, 0);
            else if (end > s.Length) end = s.Length;
            if (end < start) end = start;
            return s.Substring(start, end - start);
        });
        DefineStr(realm, "substring", 2, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int start = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            int end = a.ArgCount > 1 && !(a.Get(1) is JsUndefined) ? ToInteger(a.Get(1)) : s.Length;
            if (start < 0) start = 0; if (start > s.Length) start = s.Length;
            if (end < 0) end = 0; if (end > s.Length) end = s.Length;
            if (end < start) { int t = start; start = end; end = t; }
            return s.Substring(start, end - start);
        });
        DefineStr(realm, "toLowerCase", 0, delegate (JsRealm r, string s, JsCallArgs a) { return TcLower(s); });
        DefineStr(realm, "toUpperCase", 0, delegate (JsRealm r, string s, JsCallArgs a) { return TcUpper(s); });
        DefineStr(realm, "trim", 0, delegate (JsRealm r, string s, JsCallArgs a) { return s.Trim(); });
        DefineStr(realm, "trimStart", 0, delegate (JsRealm r, string s, JsCallArgs a) { return s.TrimStart(); });
        DefineStr(realm, "trimEnd", 0, delegate (JsRealm r, string s, JsCallArgs a) { return s.TrimEnd(); });
        DefineStr(realm, "repeat", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int n = ToInteger(a.Get(0));
            if (n < 0) throw JsErr.Range("Invalid count value");
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        });
        DefineStr(realm, "padStart", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int target = ToLength(a.Get(0));
            string fill = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : " ";
            if (fill.Length == 0) fill = " ";
            if (s.Length >= target) return s;
            StringBuilder sb = new StringBuilder();
            int need = target - s.Length;
            while (sb.Length < need) sb.Append(fill);
            sb.Length = need;
            return sb.ToString() + s;
        });
        DefineStr(realm, "padEnd", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int target = ToLength(a.Get(0));
            string fill = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : " ";
            if (fill.Length == 0) fill = " ";
            if (s.Length >= target) return s;
            StringBuilder sb = new StringBuilder(s);
            int need = target - s.Length;
            StringBuilder fb = new StringBuilder();
            while (fb.Length < need) fb.Append(fill);
            fb.Length = need;
            return sb.ToString() + fb.ToString();
        });
        DefineStr(realm, "split", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            JsArray result = r.NewArray();
            object sep = a.Get(0);
            if (sep is JsUndefined || sep is JsNull)
            {
                result.Push(s);
                return result;
            }
            if (sep is JsObject && ((JsObject)sep).RegExpValue != null)
            {
                JsRegExp re = ((JsObject)sep).RegExpValue;
                int last = 0;
                JsMatch m;
                while ((m = re.Find(s, last)) != null)
                {
                    if (m.Index > last) result.Push(s.Substring(last, m.Index - last));
                    else result.Push("");
                    last = m.Index + m.Value.Length;
                    if (m.Value.Length == 0) { last++; if (last > s.Length) break; }
                }
                if (last <= s.Length) result.Push(s.Substring(last));
                return result;
            }
            string str = JsValueOps.ToStringValue(sep);
            if (str.Length == 0)
            {
                for (int i = 0; i < s.Length; i++) result.Push(s[i].ToString());
                return result;
            }
            int idx = 0;
            int found;
            while ((found = IndexOfFrom(s, str, idx)) >= 0)
            {
                result.Push(s.Substring(idx, found - idx));
                idx = found + str.Length;
            }
            result.Push(s.Substring(idx));
            return result;
        });
        DefineStr(realm, "toString", 0, delegate (JsRealm r, string s, JsCallArgs a) { return s; });
        DefineStr(realm, "valueOf", 0, delegate (JsRealm r, string s, JsCallArgs a) { return s; });

        sp.Define("at", JsProperty.Data(realm.MakeMethod("at", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.ThisValue);
            int i = ToInteger(a.Get(0));
            if (i < 0) i = s.Length + i;
            if (i < 0 || i >= s.Length) return JsUndefined.Instance;
            return s[i].ToString();
        }, 1)));

        sp.Define("match", JsProperty.Data(realm.MakeMethod("match", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.ThisValue);
            object p = a.Get(0);
            if (p is JsObject && ((JsObject)p).RegExpValue != null)
            {
                JsRegExp re = ((JsObject)p).RegExpValue;
                if (!re.Global)
                {
                    JsMatch m = re.Find(s, 0);
                    return BuildMatchResult(realm, s, m);
                }
                JsArray all = realm.NewArray();
                int last = 0;
                JsMatch m2;
                while ((m2 = re.Find(s, last)) != null)
                {
                    all.Push(m2.Value);
                    last = m2.Index + m2.Value.Length;
                    if (m2.Value.Length == 0) { last++; if (last > s.Length) break; }
                }
                if (all.Length == 0) return JsNull.Instance;
                return all;
            }
            string pat = JsValueOps.ToStringValue(p);
            JsRegExp literal = new JsRegExp(pat, "");
            JsMatch lm = literal.Find(s, 0);
            return BuildMatchResult(realm, s, lm);
        }, 1)));

        sp.Define("search", JsProperty.Data(realm.MakeMethod("search", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.ThisValue);
            object p = a.Get(0);
            JsRegExp re;
            if (p is JsObject && ((JsObject)p).RegExpValue != null) re = ((JsObject)p).RegExpValue;
            else re = new JsRegExp(JsValueOps.ToStringValue(p), "");
            JsMatch m = re.Find(s, 0);
            return m != null ? (object)(double)m.Index : (object)JsNull.Instance;
        }, 1)));

        sp.Define("replaceAll", JsProperty.Data(realm.MakeMethod("replaceAll", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.ThisValue);
            object p = a.Get(0);
            string rep = JsValueOps.ToStringValue(a.Get(1));
            if (p is JsObject && ((JsObject)p).RegExpValue != null)
            {
                JsRegExp re = ((JsObject)p).RegExpValue;
                if (!re.Global) throw JsErr.Type("replaceAll requires a global RegExp");
                return re.Replace(s, rep);
            }
            string search = JsValueOps.ToStringValue(p);
            if (search.Length == 0) return s;
            StringBuilder sb = new StringBuilder();
            int idx = 0;
            int found;
            while ((found = IndexOfFrom(s, search, idx)) >= 0)
            {
                sb.Append(s, idx, found - idx).Append(rep);
                idx = found + search.Length;
            }
            sb.Append(s, idx, s.Length - idx);
            return sb.ToString();
        }, 2)));

        sp.Define("replace", JsProperty.Data(realm.MakeMethod("replace", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.ThisValue);
            object p = a.Get(0);
            object rep = a.Get(1);
            if (p is JsObject && ((JsObject)p).RegExpValue != null)
            {
                JsRegExp re = ((JsObject)p).RegExpValue;
                if (rep is JsFunction)
                {
                    return ReplaceWithFn(realm, s, re, (JsFunction)rep);
                }
                return re.Replace(s, JsValueOps.ToStringValue(rep));
            }
            string search = JsValueOps.ToStringValue(p);
            int found = IndexOfFrom(s, search, 0);
            if (found < 0) return s;
            string replacement = JsValueOps.ToStringValue(rep);
            return s.Substring(0, found) + replacement + s.Substring(found + search.Length);
        }, 2)));

        // String.fromCharCode / fromCodePoint
        realm.StringCtor = realm.MakeCtor("String", delegate (JsCallArgs a)
        {
            return a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
        }, 1, sp);
        realm.Global.Define("String", JsProperty.Data(realm.StringCtor));

        realm.StringCtor.Define("fromCharCode", JsProperty.Data(realm.MakeMethod("fromCharCode", delegate (JsCallArgs a)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < a.ArgCount; i++) sb.Append((char)(int)JsValueOps.ToNumber(a.Get(i)));
            return sb.ToString();
        }, 1)));

        realm.StringCtor.Define("raw", JsProperty.Data(realm.MakeMethod("raw", delegate (JsCallArgs a)
        {
            object src = a.Get(0);
            StringBuilder sb = new StringBuilder();
            List<object> subs = new List<object>();
            if (src is JsArray)
            {
                JsArray sa = (JsArray)src;
                for (int i = 0; i < sa.Length; i++) subs.Add(sa.GetIndex(i));
            }
            for (int i = 0; i < subs.Count; i++)
            {
                sb.Append(JsValueOps.ToStringValue(subs[i]));
                if (i + 1 < a.ArgCount) sb.Append(JsValueOps.ToStringValue(a.Get(i + 1)));
            }
            return sb.ToString();
        }, 1)));
    }

    private delegate object StrMethod(JsRealm realm, string s, JsCallArgs a);

    private static void DefineStr(JsRealm realm, string name, int length, StrMethod fn)
    {
        realm.StringPrototype.Define(name, JsProperty.Data(realm.MakeMethod(name, delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.ThisValue);
            return fn(realm, s, a);
        }, length)));
    }

    private static object BuildMatchResult(JsRealm realm, string s, JsMatch m)
    {
        if (m == null) return JsNull.Instance;
        JsArray arr = realm.NewArray();
        arr.Push(m.Value);
        if (m.Groups != null)
            for (int i = 0; i < m.Groups.Count; i++)
                arr.Push(m.GroupOk != null && i < m.GroupOk.Count && m.GroupOk[i] ? (object)m.Groups[i] : (object)JsUndefined.Instance);
        arr.SetProperty("index", (double)m.Index);
        arr.SetProperty("input", s);
        return arr;
    }

    private static object ReplaceWithFn(JsRealm realm, string s, JsRegExp re, JsFunction fn)
    {
        StringBuilder sb = new StringBuilder();
        int last = 0;
        JsMatch m;
        while ((m = re.Find(s, last)) != null)
        {
            sb.Append(s, last, m.Index - last);
            List<object> callArgs = new List<object>();
            callArgs.Add(m.Value);
            for (int i = 0; i < m.Groups.Count; i++)
                callArgs.Add(m.GroupOk != null && i < m.GroupOk.Count && m.GroupOk[i] ? (object)m.Groups[i] : (object)JsUndefined.Instance);
            callArgs.Add((double)m.Index);
            callArgs.Add(s);
            object r = realm.Interp.Call(fn, JsUndefined.Instance, callArgs.ToArray(), false);
            sb.Append(JsValueOps.ToStringValue(r));
            last = m.Index + m.Value.Length;
            if (m.Value.Length == 0) { last++; if (last > s.Length) break; }
        }
        if (last < s.Length) sb.Append(s, last, s.Length - last);
        return sb.ToString();
    }

    // ---- Number ------------------------------------------------------------

    public static void BuildNumber(JsRealm realm)
    {
        JsObject np = realm.NumberPrototype;
        SetTag(realm, np, "Number");
        np.Define("valueOf", JsProperty.Data(realm.MakeMethod("valueOf", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            if (t is JsObject && ((JsObject)t).OwnHas("value"))
            {
                object v;
                ((JsObject)t).GetProperty("value", out v);
                return v;
            }
            return JsValueOps.ToNumber(t);
        }, 0)));

        np.Define("toString", JsProperty.Data(realm.MakeMethod("toString", delegate (JsCallArgs a)
        {
            double d = JsValueOps.ToNumber(a.ThisValue);
            int radix = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 10;
            if (radix == 10) return JsValueOps.NumberToString(d);
            return NumberToBase(d, radix);
        }, 1)));

        np.Define("toFixed", JsProperty.Data(realm.MakeMethod("toFixed", delegate (JsCallArgs a)
        {
            double d = JsValueOps.ToNumber(a.ThisValue);
            int digits = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            if (digits < 0 || digits > 20) throw JsErr.Range("toFixed digits out of range");
            return FormatFixed(d, digits);
        }, 1)));

        np.Define("toPrecision", JsProperty.Data(realm.MakeMethod("toPrecision", delegate (JsCallArgs a)
        {
            double d = JsValueOps.ToNumber(a.ThisValue);
            if (a.ArgCount == 0 || a.Get(0) is JsUndefined) return JsValueOps.NumberToString(d);
            int prec = ToInteger(a.Get(0));
            if (prec < 1 || prec > 21) throw JsErr.Range("toPrecision precision out of range");
            return FormatPrecision(d, prec);
        }, 1)));

        np.Define("toLocaleString", JsProperty.Data(realm.MakeMethod("toLocaleString", delegate (JsCallArgs a)
        {
            return JsValueOps.NumberToString(JsValueOps.ToNumber(a.ThisValue));
        }, 0)));

        realm.NumberCtor = realm.MakeCtor("Number", delegate (JsCallArgs a)
        {
            return a.ArgCount > 0 ? JsValueOps.ToNumber(a.Get(0)) : 0.0;
        }, 1, np);
        realm.Global.Define("Number", JsProperty.Data(realm.NumberCtor));

        realm.NumberCtor.Define("EPSILON", JsProperty.DataNonEnum(2.220446049250313e-16));
        realm.NumberCtor.Define("MAX_SAFE_INTEGER", JsProperty.DataNonEnum(9007199254740991.0));
        realm.NumberCtor.Define("MIN_SAFE_INTEGER", JsProperty.DataNonEnum(-9007199254740991.0));
        realm.NumberCtor.Define("MAX_VALUE", JsProperty.DataNonEnum(double.MaxValue));
        realm.NumberCtor.Define("MIN_VALUE", JsProperty.DataNonEnum(5e-324));
        realm.NumberCtor.Define("POSITIVE_INFINITY", JsProperty.DataNonEnum(double.PositiveInfinity));
        realm.NumberCtor.Define("NEGATIVE_INFINITY", JsProperty.DataNonEnum(double.NegativeInfinity));
        realm.NumberCtor.Define("NaN", JsProperty.DataNonEnum(double.NaN));

        realm.NumberCtor.Define("isFinite", JsProperty.Data(realm.MakeMethod("isFinite", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (!(v is JsNumberWrapperCheck)) { if (!(v is double)) return false; }
            double d = (double)v;
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }, 1)));

        realm.NumberCtor.Define("isInteger", JsProperty.Data(realm.MakeMethod("isInteger", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (!(v is double)) return false;
            double d = (double)v;
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            return d == System.Math.Floor(d);
        }, 1)));

        realm.NumberCtor.Define("isNaN", JsProperty.Data(realm.MakeMethod("isNaN", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (!(v is double)) return false;
            return double.IsNaN((double)v);
        }, 1)));

        realm.NumberCtor.Define("isSafeInteger", JsProperty.Data(realm.MakeMethod("isSafeInteger", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (!(v is double)) return false;
            double d = (double)v;
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            if (d != System.Math.Floor(d)) return false;
            return System.Math.Abs(d) <= 9007199254740991.0;
        }, 1)));

        realm.NumberCtor.Define("parseFloat", JsProperty.Data(realm.MakeMethod("parseFloat", delegate (JsCallArgs a)
        {
            return ParseNumberString(JsValueOps.ToStringValue(a.Get(0)));
        }, 1)));

        realm.NumberCtor.Define("parseInt", JsProperty.Data(realm.MakeMethod("parseInt", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.Get(0));
            int radix = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
            return ParseIntRadix(s, radix);
        }, 2)));
    }

    private sealed class JsNumberWrapperCheck { } // never instantiated; used only for `is` pattern

    private static string NumberToBase(double d, int radix)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
        if (d == 0) return "0";
        bool neg = d < 0;
        long n = (long)System.Math.Abs(d);
        string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        StringBuilder sb = new StringBuilder();
        while (n > 0)
        {
            sb.Insert(0, digits[(int)(n % radix)]);
            n /= radix;
        }
        if (neg) sb.Insert(0, '-');
        return sb.ToString();
    }

    private static string FormatFixed(double d, int digits)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
        return d.ToString("F" + digits);
    }

    private static string FormatPrecision(double d, int prec)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsInfinity(d)) return d > 0 ? "Infinity" : "-Infinity";
        return d.ToString("G" + prec);
    }

    private static double ParseIntRadix(string s, int radix)
    {
        int i = 0;
        while (i < s.Length && IsSpaceChar(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        if (radix == 0)
        {
            if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X')) { radix = 16; i += 2; }
            else radix = 10;
        }
        if (radix < 2 || radix > 36) return double.NaN;
        double val = 0;
        bool any = false;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            int dv;
            if (c >= '0' && c <= '9') dv = c - '0';
            else if (c >= 'a' && c <= 'z') dv = c - 'a' + 10;
            else if (c >= 'A' && c <= 'Z') dv = c - 'A' + 10;
            else break;
            if (dv >= radix) break;
            val = val * radix + dv;
            any = true;
        }
        if (!any) return double.NaN;
        return neg ? -val : val;
    }

    private static bool IsSpaceChar(char c)
    {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v' || c == '\u00a0' || c == '\u2028' || c == '\u2029';
    }

    private static double ParseNumberString(string s)
    {
        return ParseIntRadixOrFloat(s);
    }

    private static double ParseIntRadixOrFloat(string s)
    {
        int i = 0;
        while (i < s.Length && IsSpaceChar(s[i])) i++;
        bool neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        // hex
        if (i + 1 < s.Length && s[i] == '0' && (s[i + 1] == 'x' || s[i + 1] == 'X'))
        {
            double h = ParseIntRadix(s.Substring(i), 16);
            return double.IsNaN(h) ? double.NaN : (neg ? -System.Math.Abs(h) : System.Math.Abs(h));
        }
        StringBuilder sb = new StringBuilder();
        bool any = false;
        for (; i < s.Length; i++)
        {
            char c = s[i];
            if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
            {
                // stop on a trailing sign that isn't part of an exponent
                sb.Append(c);
                any = true;
            }
            else break;
        }
        if (!any) return double.NaN;
        string t = sb.ToString();
        double d;
        if (JsValueOps.TryParseDecimal(t, 0, t.Length, out d)) return neg ? -d : d;
        return double.NaN;
    }

    // ---- Boolean -----------------------------------------------------------

    public static void BuildBoolean(JsRealm realm)
    {
        JsObject bp = realm.BooleanPrototype;
        SetTag(realm, bp, "Boolean");
        bp.Define("valueOf", JsProperty.Data(realm.MakeMethod("valueOf", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            if (t is JsObject && ((JsObject)t).OwnHas("value"))
            {
                object v;
                ((JsObject)t).GetProperty("value", out v);
                return v;
            }
            return JsValueOps.ToBoolean(t);
        }, 0)));

        realm.BooleanCtor = realm.MakeCtor("Boolean", delegate (JsCallArgs a)
        {
            return a.ArgCount > 0 ? JsValueOps.ToBoolean(a.Get(0)) : false;
        }, 1, bp);
        realm.Global.Define("Boolean", JsProperty.Data(realm.BooleanCtor));
    }

    // ---- Error -------------------------------------------------------------

    private static JsFunction MakeErrorCtor(JsRealm realm, string name, JsObject proto)
    {
        JsFunction ctor = realm.MakeCtor(name, delegate (JsCallArgs a)
        {
            object msg = a.Get(0);
            JsObject e = new JsObject(proto);
            e.Define("name", JsProperty.Data(name));
            e.Define("message", JsProperty.Data(msg is JsUndefined ? "" : JsValueOps.ToStringValue(msg)));
            e.Define("stack", JsProperty.DataNonEnum(name + ": " + (msg is JsUndefined ? "" : JsValueOps.ToStringValue(msg))));
            return e;
        }, 1, proto);
        proto.Define("name", JsProperty.DataNonEnum(name));
        proto.Define("message", JsProperty.DataNonEnum(""));
        return ctor;
    }

    public static void BuildError(JsRealm realm)
    {
        realm.ErrorCtor = MakeErrorCtor(realm, "Error", realm.ErrorPrototype);
        realm.TypeErrorCtor = MakeErrorCtor(realm, "TypeError", realm.TypeErrorPrototype);
        realm.RangeErrorCtor = MakeErrorCtor(realm, "RangeError", realm.RangeErrorPrototype);
        realm.SyntaxErrorCtor = MakeErrorCtor(realm, "SyntaxError", realm.SyntaxErrorPrototype);
        realm.ReferenceErrorCtor = MakeErrorCtor(realm, "ReferenceError", realm.ReferenceErrorPrototype);
        realm.EvalErrorCtor = MakeErrorCtor(realm, "EvalError", realm.EvalErrorPrototype);
        realm.URICtor = MakeErrorCtor(realm, "URIError", realm.URIErrorPrototype);

        realm.Global.Define("Error", JsProperty.Data(realm.ErrorCtor));
        realm.Global.Define("TypeError", JsProperty.Data(realm.TypeErrorCtor));
        realm.Global.Define("RangeError", JsProperty.Data(realm.RangeErrorCtor));
        realm.Global.Define("SyntaxError", JsProperty.Data(realm.SyntaxErrorCtor));
        realm.Global.Define("ReferenceError", JsProperty.Data(realm.ReferenceErrorCtor));
        realm.Global.Define("EvalError", JsProperty.Data(realm.EvalErrorCtor));
        realm.Global.Define("URIError", JsProperty.Data(realm.URICtor));
    }

    // ---- Math --------------------------------------------------------------

    public static void BuildMath(JsRealm realm)
    {
        JsObject m = realm.NewObject();
        realm.MathObj = m;
        m.SetWellKnown(JsWellKnown.ToStringTag, "Math");
        m.Define("PI", JsProperty.DataNonEnum(System.Math.PI));
        m.Define("E", JsProperty.DataNonEnum(System.Math.E));
        m.Define("LN2", JsProperty.DataNonEnum(0.6931471805599453));
        m.Define("LN10", JsProperty.DataNonEnum(2.302585092994046));
        m.Define("LOG2E", JsProperty.DataNonEnum(1.4426950408889634));
        m.Define("LOG10E", JsProperty.DataNonEnum(0.4342944819032518));
        m.Define("SQRT1_2", JsProperty.DataNonEnum(0.7071067811865476));
        m.Define("SQRT2", JsProperty.DataNonEnum(1.4142135623730951));
        m.Define("abs", MathFn1(realm, "abs", System.Math.Abs));
        m.Define("floor", MathFn1(realm, "floor", System.Math.Floor));
        m.Define("ceil", MathFn1(realm, "ceil", System.Math.Ceiling));
        m.Define("round", MathFn1(realm, "round", RoundHalfUp));
        m.Define("trunc", MathFn1(realm, "trunc", System.Math.Truncate));
        m.Define("sign", JsProperty.Data(realm.MakeMethod("sign", delegate (JsCallArgs a)
        {
            double d = JsValueOps.ToNumber(a.Get(0));
            if (double.IsNaN(d)) return double.NaN;
            if (d > 0) return 1.0;
            if (d < 0) return -1.0;
            return d;
        }, 1)));
        m.Define("sqrt", MathFn1(realm, "sqrt", System.Math.Sqrt));
        m.Define("cbrt", MathFn1(realm, "cbrt", CubeRoot));
        m.Define("pow", MathFn2(realm, "pow", System.Math.Pow));
        m.Define("exp", MathFn1(realm, "exp", System.Math.Exp));
        m.Define("log", MathFn1(realm, "log", System.Math.Log));
        m.Define("log2", MathFn1(realm, "log2", Log2));
        m.Define("log10", MathFn1(realm, "log10", System.Math.Log10));
        m.Define("sin", MathFn1(realm, "sin", System.Math.Sin));
        m.Define("cos", MathFn1(realm, "cos", System.Math.Cos));
        m.Define("tan", MathFn1(realm, "tan", System.Math.Tan));
        m.Define("asin", MathFn1(realm, "asin", System.Math.Asin));
        m.Define("acos", MathFn1(realm, "acos", System.Math.Acos));
        m.Define("atan", MathFn1(realm, "atan", System.Math.Atan));
        m.Define("atan2", MathFn2(realm, "atan2", System.Math.Atan2));
        m.Define("hypot", JsProperty.Data(realm.MakeMethod("hypot", delegate (JsCallArgs a)
        {
            double sum = 0;
            for (int i = 0; i < a.ArgCount; i++)
            {
                double d = JsValueOps.ToNumber(a.Get(i));
                if (double.IsInfinity(d)) return double.PositiveInfinity;
                sum += d * d;
            }
            return System.Math.Sqrt(sum);
        }, 2)));
        m.Define("max", JsProperty.Data(realm.MakeMethod("max", delegate (JsCallArgs a)
        {
            double best = double.NegativeInfinity;
            for (int i = 0; i < a.ArgCount; i++)
            {
                double d = JsValueOps.ToNumber(a.Get(i));
                if (double.IsNaN(d)) return double.NaN;
                if (d > best) best = d;
            }
            return best;
        }, 2)));
        m.Define("min", JsProperty.Data(realm.MakeMethod("min", delegate (JsCallArgs a)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < a.ArgCount; i++)
            {
                double d = JsValueOps.ToNumber(a.Get(i));
                if (double.IsNaN(d)) return double.NaN;
                if (d < best) best = d;
            }
            return best;
        }, 2)));
        m.Define("random", JsProperty.Data(realm.MakeMethod("random", delegate (JsCallArgs a)
        {
            if (_rng == null) _rng = new Random(12345);
            return _rng.NextDouble();
        }, 0)));

        realm.Global.Define("Math", JsProperty.Data(m));
    }

    private static Random _rng;

    private static double RoundHalfUp(double d) { return System.Math.Floor(d + 0.5); }
    private static double CubeRoot(double d) { return System.Math.Sign(d) * System.Math.Pow(System.Math.Abs(d), 1.0 / 3.0); }
    private static double Log2(double d) { return System.Math.Log(d) / System.Math.Log(2); }

    private static JsProperty MathFn1(JsRealm realm, string name, System.Func<double, double> f)
    {
        return JsProperty.Data(realm.MakeMethod(name, delegate (JsCallArgs a)
        {
            return f(JsValueOps.ToNumber(a.Get(0)));
        }, 1));
    }

    private static JsProperty MathFn2(JsRealm realm, string name, System.Func<double, double, double> f)
    {
        return JsProperty.Data(realm.MakeMethod(name, delegate (JsCallArgs a)
        {
            return f(JsValueOps.ToNumber(a.Get(0)), JsValueOps.ToNumber(a.Get(1)));
        }, 2));
    }

    // ---- JSON --------------------------------------------------------------

    public static void BuildJSON(JsRealm realm)
    {
        JsObject json = realm.NewObject();
        realm.JsonObj = json;
        json.SetWellKnown(JsWellKnown.ToStringTag, "JSON");

        json.Define("parse", JsProperty.Data(realm.MakeMethod("parse", delegate (JsCallArgs a)
        {
            string text = JsValueOps.ToStringValue(a.Get(0));
            return JsJson.Parse(realm, text);
        }, 2)));

        json.Define("stringify", JsProperty.Data(realm.MakeMethod("stringify", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            string s = JsJson.Stringify(realm, v);
            return s == null ? (object)JsUndefined.Instance : (object)s;
        }, 3)));

        realm.Global.Define("JSON", JsProperty.Data(json));
    }

    // ---- console -----------------------------------------------------------

    public static void BuildConsole(JsRealm realm)
    {
        JsObject con = realm.NewObject();
        realm.ConsoleObj = con;

        JsNativeFn log = delegate (JsCallArgs a)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < a.ArgCount; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(InspectValue(a.Get(i)));
            }
            if (ConsoleOutput != null) ConsoleOutput(sb.ToString());
            return JsUndefined.Instance;
        };

        con.Define("log", JsProperty.Data(realm.MakeMethod("log", log, 0)));
        con.Define("info", JsProperty.Data(realm.MakeMethod("info", log, 0)));
        con.Define("warn", JsProperty.Data(realm.MakeMethod("warn", log, 0)));
        con.Define("error", JsProperty.Data(realm.MakeMethod("error", log, 0)));
        con.Define("debug", JsProperty.Data(realm.MakeMethod("debug", log, 0)));

        con.Define("assert", JsProperty.Data(realm.MakeMethod("assert", delegate (JsCallArgs a)
        {
            if (!JsValueOps.ToBoolean(a.Get(0)))
            {
                string msg = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "Assertion failed";
                if (ConsoleOutput != null) ConsoleOutput("Assertion failed: " + msg);
            }
            return JsUndefined.Instance;
        }, 1)));

        con.Define("table", JsProperty.Data(realm.MakeMethod("table", log, 0)));
        con.Define("dir", JsProperty.Data(realm.MakeMethod("dir", log, 1)));

        realm.Global.Define("console", JsProperty.Data(con));
    }

    /// <summary>Hook the host can set to receive console output.</summary>
    public static System.Action<string> ConsoleOutput;

    private static string InspectValue(object v)
    {
        if (v is JsUndefined) return "undefined";
        if (v is JsNull) return "null";
        if (v is string) return (string)v;
        if (v is bool) return (bool)v ? "true" : "false";
        if (v is double) return JsValueOps.NumberToString((double)v);
        if (v is JsObject) return JsValueOps.ToStringValue(v);
        return JsValueOps.ToStringValue(v);
    }

    // ---- Symbol ------------------------------------------------------------

    public static void BuildSymbol(JsRealm realm)
    {
        JsObject sp = realm.SymbolPrototype;
        SetTag(realm, sp, "Symbol");
        sp.Define("toString", JsProperty.Data(realm.MakeMethod("toString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            if (t is JsSymbol) return "Symbol(" + ((JsSymbol)t).Desc + ")";
            return "Symbol()";
        }, 0)));
        sp.Define("valueOf", JsProperty.Data(realm.MakeMethod("valueOf", delegate (JsCallArgs a)
        {
            return a.ThisValue;
        }, 0)));

        JsFunction ctor = realm.MakeCtor("Symbol", delegate (JsCallArgs a)
        {
            string desc = a.ArgCount > 0 && !(a.Get(0) is JsUndefined) ? JsValueOps.ToStringValue(a.Get(0)) : "";
            return JsSymbols.New(desc);
        }, 0, sp);
        realm.SymbolCtor = ctor;
        ctor.Constructable = false;
        realm.Global.Define("Symbol", JsProperty.Data(ctor));

        ctor.Define("iterator", JsProperty.DataNonEnum(JsSymbols.Get(JsWellKnown.Iterator)));
        ctor.Define("asyncIterator", JsProperty.DataNonEnum(JsSymbols.Get(JsWellKnown.AsyncIterator)));
        ctor.Define("toStringTag", JsProperty.DataNonEnum(JsSymbols.Get(JsWellKnown.ToStringTag)));
        ctor.Define("hasInstance", JsProperty.DataNonEnum(JsSymbols.Get(JsWellKnown.HasInstance)));
        ctor.Define("toPrimitive", JsProperty.DataNonEnum(JsSymbols.Get(JsWellKnown.ToPrimitive)));

        ctor.Define("for", JsProperty.Data(realm.MakeMethod("for", delegate (JsCallArgs a)
        {
            string key = JsValueOps.ToStringValue(a.Get(0));
            return JsSymbols.For(key);
        }, 1)));

        ctor.Define("keyFor", JsProperty.Data(realm.MakeMethod("keyFor", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (v is JsSymbol) return JsSymbols.KeyFor((JsSymbol)v);
            throw JsErr.Type("Symbol.keyFor requires a symbol");
        }, 1)));
    }

    // ---- RegExp ------------------------------------------------------------

    public static void BuildRegExp(JsRealm realm)
    {
        JsObject rp = realm.RegExpPrototype;
        SetTag(realm, rp, "RegExp");

        rp.Define("source", JsProperty.DataNonEnum("(?:)"));
        rp.Define("flags", JsProperty.DataNonEnum(""));
        rp.Define("global", JsProperty.DataNonEnum(false));
        rp.Define("ignoreCase", JsProperty.DataNonEnum(false));
        rp.Define("multiline", JsProperty.DataNonEnum(false));
        rp.Define("dotAll", JsProperty.DataNonEnum(false));
        rp.Define("sticky", JsProperty.DataNonEnum(false));
        rp.Define("unicode", JsProperty.DataNonEnum(false));
        rp.Define("lastIndex", JsProperty.Data(0.0));

        rp.Define("exec", JsProperty.Data(realm.MakeMethod("exec", delegate (JsCallArgs a)
        {
            JsRegExp re = GetRegExp(a.ThisValue);
            string s = JsValueOps.ToStringValue(a.Get(0));
            if (re == null) return JsNull.Instance;
            int start = re.Global || re.Sticky ? re.LastIndex : 0;
            if (start > s.Length) { re.LastIndex = 0; return JsNull.Instance; }
            JsMatch m = re.Find(s, start);
            if (m == null) { re.LastIndex = 0; return JsNull.Instance; }
            re.LastIndex = m.Index + m.Value.Length;
            return BuildMatchResult(realm, s, m);
        }, 1)));

        rp.Define("test", JsProperty.Data(realm.MakeMethod("test", delegate (JsCallArgs a)
        {
            JsRegExp re = GetRegExp(a.ThisValue);
            string s = JsValueOps.ToStringValue(a.Get(0));
            if (re == null) return false;
            int start = re.Global || re.Sticky ? re.LastIndex : 0;
            if (start > s.Length) { re.LastIndex = 0; return false; }
            JsMatch m = re.Find(s, start);
            if (m == null) { re.LastIndex = 0; return false; }
            re.LastIndex = m.Index + m.Value.Length;
            return true;
        }, 1)));

        rp.Define("toString", JsProperty.Data(realm.MakeMethod("toString", delegate (JsCallArgs a)
        {
            JsRegExp re = GetRegExp(a.ThisValue);
            if (re == null) return "/(?:)/";
            return "/" + re.Source + "/" + re.Flags;
        }, 0)));

        realm.RegExpCtor = realm.MakeCtor("RegExp", delegate (JsCallArgs a)
        {
            object p = a.Get(0);
            object f = a.Get(1);
            if (p is JsObject && ((JsObject)p).RegExpValue != null && (f is JsUndefined))
            {
                JsRegExp existing = ((JsObject)p).RegExpValue;
                return CreateRegExp(realm, existing.Source, existing.Flags);
            }
            string pattern = p is JsUndefined ? "" : JsValueOps.ToStringValue(p);
            string flags = f is JsUndefined ? "" : JsValueOps.ToStringValue(f);
            return CreateRegExp(realm, pattern, flags);
        }, 2, rp);
        realm.Global.Define("RegExp", JsProperty.Data(realm.RegExpCtor));
    }

    private static JsRegExp GetRegExp(object v)
    {
        if (v is JsObject)
        {
            JsObject o = (JsObject)v;
            if (o.RegExpValue != null) return o.RegExpValue;
            if (o is JsRegExpObject) return ((JsRegExpObject)o).Value;
        }
        return null;
    }

    // ---- Date --------------------------------------------------------------

    public static void BuildDate(JsRealm realm)
    {
        JsObject dp = realm.DatePrototype;
        SetTag(realm, dp, "Date");

        dp.Define("valueOf", JsProperty.Data(realm.MakeMethod("valueOf", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            if (t is JsDate) return ((JsDate)t).Time;
            return JsValueOps.ToNumber(t);
        }, 0)));

        dp.Define("getTime", JsProperty.Data(realm.MakeMethod("getTime", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            if (t is JsDate) return ((JsDate)t).Time;
            return double.NaN;
        }, 0)));

        dp.Define("toString", JsProperty.Data(realm.MakeMethod("toString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate ? ((JsDate)t).Time : JsValueOps.ToNumber(t);
            return FormatJsDate(time);
        }, 0)));

        dp.Define("toISOString", JsProperty.Data(realm.MakeMethod("toISOString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate ? ((JsDate)t).Time : JsValueOps.ToNumber(t);
            return FormatIsoDate(time);
        }, 0)));

        realm.DateCtor = realm.MakeCtor("Date", delegate (JsCallArgs a)
        {
            if (a.IsConstruct)
            {
                double time;
                if (a.ArgCount == 0) time = NowMs();
                else if (a.ArgCount == 1) time = JsValueOps.ToNumber(a.Get(0));
                else time = NowMs();
                return new JsDate(realm.DatePrototype, time);
            }
            return FormatJsDate(NowMs());
        }, 1, dp);
        realm.Global.Define("Date", JsProperty.Data(realm.DateCtor));

        realm.DateCtor.Define("now", JsProperty.Data(realm.MakeMethod("now", delegate (JsCallArgs a)
        {
            return NowMs();
        }, 0)));

        realm.DateCtor.Define("parse", JsProperty.Data(realm.MakeMethod("parse", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.Get(0));
            double t;
            if (TryParseIsoDate(s, out t)) return t;
            return double.NaN;
        }, 1)));
    }

    private static double NowMs()
    {
        return (double)System.DateTime.UtcNow.Ticks / 10000.0 - 62135596800000.0;
    }

    private static string Pad2(int n) { return n < 10 ? "0" + n : n.ToString(); }

    private static string FormatJsDate(double time)
    {
        if (double.IsNaN(time)) return "Invalid Date";
        System.DateTime dt = TicksToDt(time);
        return dt.Year.ToString() + "-" + Pad2(dt.Month) + "-" + Pad2(dt.Day) +
            " " + Pad2(dt.Hour) + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + " GMT";
    }

    private static string FormatIsoDate(double time)
    {
        if (double.IsNaN(time)) throw JsErr.Range("Invalid time value");
        System.DateTime dt = TicksToDt(time);
        return dt.Year.ToString() + "-" + Pad2(dt.Month) + "-" + Pad2(dt.Day) +
            "T" + Pad2(dt.Hour) + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + ".000Z";
    }

    private static System.DateTime TicksToDt(double time)
    {
        long ticks = (long)(time * 10000.0) + 621355968000000000L;
        if (ticks < 0) ticks = 0;
        return new System.DateTime(ticks, System.DateTimeKind.Utc);
    }

    private static bool TryParseIsoDate(string s, out double time)
    {
        time = double.NaN;
        if (s == null || s.Length < 10) return false;
        int y, mo, d;
        if (!TryParseInt(s, 0, 4, out y)) return false;
        if (s[4] != '-') return false;
        if (!TryParseInt(s, 5, 2, out mo)) return false;
        if (s[7] != '-') return false;
        if (!TryParseInt(s, 8, 2, out d)) return false;
        try
        {
            System.DateTime dt = new System.DateTime(y, mo, d, 0, 0, 0, System.DateTimeKind.Utc);
            time = (double)(dt.Ticks - 621355968000000000L) / 10000.0;
            return true;
        }
        catch { return false; }
    }

    private static bool TryParseInt(string s, int start, int len, out int val)
    {
        val = 0;
        if (start + len > s.Length) return false;
        for (int i = 0; i < len; i++)
        {
            char c = s[start + i];
            if (c < '0' || c > '9') return false;
            val = val * 10 + (c - '0');
        }
        return true;
    }

    // ---- Map / Set ---------------------------------------------------------

    public static void BuildMapSet(JsRealm realm)
    {
        // Map
        JsObject mp = realm.MapPrototype;
        SetTag(realm, mp, "Map");
        mp.Define("size", JsProperty.DataNonEnum(0.0));
        mp.Define("get", JsProperty.Data(realm.MakeMethod("get", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            if (map == null) return JsUndefined.Instance;
            int idx = map.IndexOfKey(a.Get(0));
            return idx >= 0 ? map.Values[idx] : JsUndefined.Instance;
        }, 1)));
        mp.Define("set", JsProperty.Data(realm.MakeMethod("set", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            if (map == null) return a.ThisValue;
            map.SetKey(a.Get(0), a.Get(1));
            return map;
        }, 2)));
        mp.Define("has", JsProperty.Data(realm.MakeMethod("has", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            return map != null && map.IndexOfKey(a.Get(0)) >= 0;
        }, 1)));
        mp.Define("delete", JsProperty.Data(realm.MakeMethod("delete", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            return map != null && map.RemoveKey(a.Get(0));
        }, 1)));
        mp.Define("clear", JsProperty.Data(realm.MakeMethod("clear", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            if (map != null) { map.Keys.Clear(); map.Values.Clear(); }
            return JsUndefined.Instance;
        }, 0)));
        mp.Define("forEach", JsProperty.Data(realm.MakeMethod("forEach", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            JsFunction fn = a.Get(0) as JsFunction;
            if (map == null || fn == null) return JsUndefined.Instance;
            object thisArg = a.Get(1);
            for (int i = 0; i < map.Count; i++)
                realm.Interp.Call(fn, thisArg, new object[] { map.Values[i], map.Keys[i], map }, false);
            return JsUndefined.Instance;
        }, 1)));

        realm.MapCtor = realm.MakeCtor("Map", delegate (JsCallArgs a)
        {
            JsMapObject map = new JsMapObject(realm.MapPrototype);
            if (a.ArgCount > 0)
            {
                List<object> items = realm.Interp.ToList(a.Get(0));
                for (int i = 0; i < items.Count; i++)
                {
                    List<object> pair = realm.Interp.ToList(items[i]);
                    if (pair.Count >= 2) map.SetKey(pair[0], pair[1]);
                }
            }
            return map;
        }, 0, mp);
        realm.Global.Define("Map", JsProperty.Data(realm.MapCtor));

        // Set
        JsObject sp = realm.SetPrototype;
        SetTag(realm, sp, "Set");
        sp.Define("size", JsProperty.DataNonEnum(0.0));
        sp.Define("add", JsProperty.Data(realm.MakeMethod("add", delegate (JsCallArgs a)
        {
            JsSetObject set = a.ThisValue as JsSetObject;
            if (set == null) return a.ThisValue;
            set.AddValue(a.Get(0));
            return set;
        }, 1)));
        sp.Define("has", JsProperty.Data(realm.MakeMethod("has", delegate (JsCallArgs a)
        {
            JsSetObject set = a.ThisValue as JsSetObject;
            return set != null && set.IndexOf(a.Get(0)) >= 0;
        }, 1)));
        sp.Define("delete", JsProperty.Data(realm.MakeMethod("delete", delegate (JsCallArgs a)
        {
            JsSetObject set = a.ThisValue as JsSetObject;
            return set != null && set.RemoveValue(a.Get(0));
        }, 1)));
        sp.Define("clear", JsProperty.Data(realm.MakeMethod("clear", delegate (JsCallArgs a)
        {
            JsSetObject set = a.ThisValue as JsSetObject;
            if (set != null) set.Items.Clear();
            return JsUndefined.Instance;
        }, 0)));
        sp.Define("forEach", JsProperty.Data(realm.MakeMethod("forEach", delegate (JsCallArgs a)
        {
            JsSetObject set = a.ThisValue as JsSetObject;
            JsFunction fn = a.Get(0) as JsFunction;
            if (set == null || fn == null) return JsUndefined.Instance;
            object thisArg = a.Get(1);
            for (int i = 0; i < set.Count; i++)
                realm.Interp.Call(fn, thisArg, new object[] { set.Items[i], set.Items[i], set }, false);
            return JsUndefined.Instance;
        }, 1)));

        realm.SetCtor = realm.MakeCtor("Set", delegate (JsCallArgs a)
        {
            JsSetObject set = new JsSetObject(realm.SetPrototype);
            if (a.ArgCount > 0)
            {
                List<object> items = realm.Interp.ToList(a.Get(0));
                for (int i = 0; i < items.Count; i++) set.AddValue(items[i]);
            }
            return set;
        }, 0, sp);
        realm.Global.Define("Set", JsProperty.Data(realm.SetCtor));

        MapSetSizeProps(realm, mp, sp);
    }

    private static void MapSetSizeProps(JsRealm realm, JsObject mp, JsObject sp)
    {
        // size as accessor reading the live count
        mp.Define("size", JsProperty.Accessor(
            realm.MakeMethod("get size", delegate (JsCallArgs a)
            {
                JsMapObject map = a.ThisValue as JsMapObject;
                return map != null ? (object)(double)map.Count : (object)0.0;
            }, 0), null));
        sp.Define("size", JsProperty.Accessor(
            realm.MakeMethod("get size", delegate (JsCallArgs a)
            {
                JsSetObject set = a.ThisValue as JsSetObject;
                return set != null ? (object)(double)set.Count : (object)0.0;
            }, 0), null));
    }

    // ---- global functions --------------------------------------------------

    public static void BuildGlobalFunctions(JsRealm realm)
    {
        realm.Global.Define("isNaN", JsProperty.Data(realm.MakeMethod("isNaN", delegate (JsCallArgs a)
        {
            return double.IsNaN(JsValueOps.ToNumber(a.Get(0)));
        }, 1)));

        realm.Global.Define("isFinite", JsProperty.Data(realm.MakeMethod("isFinite", delegate (JsCallArgs a)
        {
            double d = JsValueOps.ToNumber(a.Get(0));
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }, 1)));

        realm.Global.Define("parseInt", JsProperty.Data(realm.MakeMethod("parseInt", delegate (JsCallArgs a)
        {
            string s = JsValueOps.ToStringValue(a.Get(0));
            int radix = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
            return ParseIntRadix(s, radix);
        }, 2)));

        realm.Global.Define("parseFloat", JsProperty.Data(realm.MakeMethod("parseFloat", delegate (JsCallArgs a)
        {
            return ParseNumberString(JsValueOps.ToStringValue(a.Get(0)));
        }, 1)));

        realm.Global.Define("eval", JsProperty.Data(realm.MakeMethod("eval", delegate (JsCallArgs a)
        {
            string code = JsValueOps.ToStringValue(a.Get(0));
            try
            {
                JsProgram prog = JsParser.ParseProgram(code);
                return realm.Interp.RunProgram(prog);
            }
            catch (JsSyntaxError se)
            {
                throw new JsThrown(JsBuiltins.CreateErrorObject(realm, "SyntaxError", se.Message), se.Message);
            }
        }, 1)));

        realm.Global.Define("undefined", JsProperty.DataNonEnum(JsUndefined.Instance));
        realm.Global.Define("NaN", JsProperty.DataNonEnum(double.NaN));
        realm.Global.Define("Infinity", JsProperty.DataNonEnum(double.PositiveInfinity));

        realm.Global.Define("encodeURIComponent", JsProperty.Data(realm.MakeMethod("encodeURIComponent", delegate (JsCallArgs a)
        {
            return UriEncode(JsValueOps.ToStringValue(a.Get(0)), false);
        }, 1)));

        realm.Global.Define("decodeURIComponent", JsProperty.Data(realm.MakeMethod("decodeURIComponent", delegate (JsCallArgs a)
        {
            return UriDecode(JsValueOps.ToStringValue(a.Get(0)));
        }, 1)));

        realm.Global.Define("encodeURI", JsProperty.Data(realm.MakeMethod("encodeURI", delegate (JsCallArgs a)
        {
            return UriEncode(JsValueOps.ToStringValue(a.Get(0)), true);
        }, 1)));

        realm.Global.Define("decodeURI", JsProperty.Data(realm.MakeMethod("decodeURI", delegate (JsCallArgs a)
        {
            return UriDecode(JsValueOps.ToStringValue(a.Get(0)));
        }, 1)));

        BuildIteratorMethods(realm);
    }

    private static void BuildIteratorMethods(JsRealm realm)
    {
        // Array.prototype.values etc return iterators; wire @@iterator on prototypes
        if (realm.ArrayPrototype != null)
        {
            object values;
            if (realm.ArrayPrototype.GetProperty("values", out values))
                realm.ArrayPrototype.SetWellKnown(JsWellKnown.Iterator, values);
        }
    }

    private static string UriEncode(string s, bool isUri)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            char c = (char)b;
            bool unreserved =
                (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                c == '-' || c == '_' || c == '.' || c == '!' || c == '~' || c == '*' || c == '\'' || c == '(' || c == ')';
            if (unreserved || (isUri && (c == ';' || c == '/' || c == '?' || c == ':' || c == '@' || c == '&' || c == '=' || c == '+' || c == '$' || c == ',' || c == '#' || c == '[' || c == ']')))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%');
                sb.Append(HexDigit(b >> 4));
                sb.Append(HexDigit(b & 15));
            }
        }
        return sb.ToString();
    }

    private static char HexDigit(int v) { return v < 10 ? (char)('0' + v) : (char)('A' + (v - 10)); }

    private static string UriDecode(string s)
    {
        List<byte> bytes = new List<byte>();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '%' && i + 2 < s.Length)
            {
                int hi = HexVal(s[i + 1]), lo = HexVal(s[i + 2]);
                if (hi >= 0 && lo >= 0) { bytes.Add((byte)(hi * 16 + lo)); i += 2; continue; }
            }
            // encode single char as UTF-8
            byte[] enc = Encoding.UTF8.GetBytes(s[i].ToString());
            for (int k = 0; k < enc.Length; k++) bytes.Add(enc[k]);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static int HexVal(char c)
    {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        if (c >= 'A' && c <= 'F') return c - 'A' + 10;
        return -1;
    }
}




