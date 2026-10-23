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
        BuildIntl(realm);
        BuildMapSet(realm);
        BuildWeakMapSet(realm);
        BuildTypedArrays(realm);
        BuildProxyReflect(realm);
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

        realm.ObjectCtor.Define("hasOwn", JsProperty.Data(realm.MakeMethod("hasOwn", delegate (JsCallArgs a)
        {
            object target = a.Get(0);
            if (target == null || target is JsUndefined || target is JsNull)
                throw JsErr.Type("Cannot convert undefined or null to object");
            string prop = JsValueOps.ToStringValue(a.Get(1));
            if (target is JsObject o)
            {
                return o.HasOwnProperty(prop);
            }
            return false;
        }, 2)));

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

        realm.ObjectCtor.Define("getOwnPropertyDescriptor", JsProperty.Data(realm.MakeMethod("getOwnPropertyDescriptor", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            if (!(v is JsObject)) return JsUndefined.Instance;
            JsObject o = (JsObject)v;
            string key = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "undefined";

            if (o is JsArray && JsArray.IsArrayIndex(key))
            {
                bool ok;
                long idx = JsArray.KeyToIndex(key, out ok);
                JsArray arr = (JsArray)o;
                if (ok && idx >= 0 && idx < arr.Length)
                {
                    JsObject desc = realm.NewObject();
                    desc.SetProperty("value", arr.GetIndex((int)idx));
                    desc.SetProperty("writable", true);
                    desc.SetProperty("enumerable", true);
                    desc.SetProperty("configurable", true);
                    return desc;
                }
            }

            JsProperty p;
            if (o.TryGetRaw(key, out p))
            {
                JsObject desc = realm.NewObject();
                if (p.IsAccessor)
                {
                    desc.SetProperty("get", p.Getter != null ? (object)p.Getter : JsUndefined.Instance);
                    desc.SetProperty("set", p.Setter != null ? (object)p.Setter : JsUndefined.Instance);
                }
                else
                {
                    desc.SetProperty("value", p.Value);
                    desc.SetProperty("writable", p.Writable);
                }
                desc.SetProperty("enumerable", p.Enumerable);
                desc.SetProperty("configurable", p.Configurable);
                return desc;
            }
            return JsUndefined.Instance;
        }, 2)));

        realm.ObjectCtor.Define("getOwnPropertyDescriptors", JsProperty.Data(realm.MakeMethod("getOwnPropertyDescriptors", delegate (JsCallArgs a)
        {
            JsObject result = realm.NewObject();
            object v = a.Get(0);
            if (!(v is JsObject)) return result;
            JsObject o = (JsObject)v;
            List<string> keys = o.OwnKeys(false);
            if (o is JsArray)
            {
                for (int i = 0; i < ((JsArray)o).Length; i++)
                {
                    string ks = i.ToString();
                    JsObject d = realm.NewObject();
                    d.SetProperty("value", ((JsArray)o).GetIndex(i));
                    d.SetProperty("writable", true);
                    d.SetProperty("enumerable", true);
                    d.SetProperty("configurable", true);
                    result.SetProperty(ks, d);
                }
            }
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                if (o is JsArray && JsArray.IsArrayIndex(k)) continue;
                JsProperty p;
                if (o.TryGetRaw(k, out p))
                {
                    JsObject desc = realm.NewObject();
                    if (p.IsAccessor)
                    {
                        desc.SetProperty("get", p.Getter != null ? (object)p.Getter : JsUndefined.Instance);
                        desc.SetProperty("set", p.Setter != null ? (object)p.Setter : JsUndefined.Instance);
                    }
                    else
                    {
                        desc.SetProperty("value", p.Value);
                        desc.SetProperty("writable", p.Writable);
                    }
                    desc.SetProperty("enumerable", p.Enumerable);
                    desc.SetProperty("configurable", p.Configurable);
                    result.SetProperty(k, desc);
                }
            }
            return result;
        }, 1)));

        realm.ObjectCtor.Define("getOwnPropertySymbols", JsProperty.Data(realm.MakeMethod("getOwnPropertySymbols", delegate (JsCallArgs a)
        {
            return realm.NewArray();
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
        realm.FunctionCallMethod = realm.MakeMethod("call", delegate (JsCallArgs a)
        {
            object fn = a.ThisValue;
            object thisArg = a.Get(0);
            List<object> args = new List<object>();
            for (int i = 1; i < a.ArgCount; i++) args.Add(a.Get(i));
            return realm.Interp.Call(fn, thisArg, args.ToArray(), false);
        }, 1);
        fp.Define("call", JsProperty.Data(realm.FunctionCallMethod));

        realm.FunctionApplyMethod = realm.MakeMethod("apply", delegate (JsCallArgs a)
        {
            object fn = a.ThisValue;
            object thisArg = a.Get(0);
            List<object> list = a.ArgCount > 1 ? realm.Interp.ToList(a.Get(1)) : new List<object>();
            return realm.Interp.Call(fn, thisArg, list.ToArray(), false);
        }, 2);
        fp.Define("apply", JsProperty.Data(realm.FunctionApplyMethod));

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

        ap.Define("at", JsProperty.Data(realm.MakeMethod("at", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            if (arr == null) return JsUndefined.Instance;
            int idx = ToInteger(a.Get(0));
            int len = arr.Length;
            if (idx < 0) idx += len;
            if (idx < 0 || idx >= len) return JsUndefined.Instance;
            return arr.GetIndex(idx);
        }, 1)));

        ap.Define("findLast", JsProperty.Data(realm.MakeMethod("findLast", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return JsUndefined.Instance;
            object thisArg = a.Get(1);
            for (int i = arr.Length - 1; i >= 0; i--)
            {
                object v = arr.GetIndex(i);
                if (JsValueOps.ToBoolean(realm.Interp.Call(fn, thisArg, new object[] { v, (double)i, arr }, false)))
                    return v;
            }
            return JsUndefined.Instance;
        }, 1)));

        ap.Define("findLastIndex", JsProperty.Data(realm.MakeMethod("findLastIndex", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsFunction fn = a.Get(0) as JsFunction;
            if (arr == null || fn == null) return -1.0;
            object thisArg = a.Get(1);
            for (int i = arr.Length - 1; i >= 0; i--)
            {
                object v = arr.GetIndex(i);
                if (JsValueOps.ToBoolean(realm.Interp.Call(fn, thisArg, new object[] { v, (double)i, arr }, false)))
                    return (double)i;
            }
            return -1.0;
        }, 1)));

        ap.Define("toReversed", JsProperty.Data(realm.MakeMethod("toReversed", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray res = realm.NewArray();
            if (arr == null) return res;
            for (int i = arr.Length - 1; i >= 0; i--) res.Push(arr.GetIndex(i));
            return res;
        }, 0)));

        ap.Define("toSorted", JsProperty.Data(realm.MakeMethod("toSorted", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray copy = realm.NewArray();
            if (arr == null) return copy;
            for (int i = 0; i < arr.Length; i++) copy.Push(arr.GetIndex(i));
            JsFunction cmp = a.ArgCount > 0 && a.Get(0) is JsFunction ? (JsFunction)a.Get(0) : null;
            for (int i = 1; i < copy.Length; i++)
            {
                object key = copy.Items[i];
                int j = i - 1;
                while (j >= 0 && SortLess(a, cmp, key, copy.Items[j]))
                {
                    copy.Items[j + 1] = copy.Items[j];
                    j--;
                }
                copy.Items[j + 1] = key;
            }
            return copy;
        }, 1)));

        ap.Define("toSpliced", JsProperty.Data(realm.MakeMethod("toSpliced", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray copy = realm.NewArray();
            if (arr == null) return copy;
            for (int i = 0; i < arr.Length; i++) copy.Push(arr.GetIndex(i));
            int len = copy.Length;
            int start = ToInteger(a.Get(0));
            if (start < 0) start = System.Math.Max(len + start, 0);
            else if (start > len) start = len;
            int deleteCount = a.ArgCount <= 1 ? len - start
                : a.ArgCount == 2 ? ToInteger(a.Get(1))
                : System.Math.Max(0, System.Math.Min(ToInteger(a.Get(1)), len - start));
            copy.Items.RemoveRange(start, deleteCount);
            for (int i = a.ArgCount - 1; i >= 2; i--) copy.Items.Insert(start, a.Get(i));
            return copy;
        }, 2)));

        ap.Define("with", JsProperty.Data(realm.MakeMethod("with", delegate (JsCallArgs a)
        {
            JsArray arr = ThisArray(a);
            JsArray copy = realm.NewArray();
            if (arr == null) return copy;
            int len = arr.Length;
            int idx = ToInteger(a.Get(0));
            if (idx < 0) idx += len;
            if (idx < 0 || idx >= len) throw JsErr.Range("Invalid index: " + idx);
            for (int i = 0; i < len; i++) copy.Push(i == idx ? a.Get(1) : arr.GetIndex(i));
            return copy;
        }, 2)));

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

        DefineStr(realm, "at", 1, delegate (JsRealm r, string s, JsCallArgs a)
        {
            int idx = ToInteger(a.Get(0));
            if (idx < 0) idx += s.Length;
            if (idx < 0 || idx >= s.Length) return JsUndefined.Instance;
            return s[idx].ToString();
        });

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
            double num = JsValueOps.ToNumber(a.ThisValue);
            string style = "decimal";
            string currency = "USD";
            bool grouping = true;
            int minFrac = 0;
            int maxFrac = 3;
            if (a.ArgCount > 1 && a.Get(1) is JsObject opts)
            {
                if (opts.GetProperty("style", out object s)) style = JsValueOps.ToStringValue(s);
                if (opts.GetProperty("currency", out object c)) currency = JsValueOps.ToStringValue(c);
                if (opts.GetProperty("useGrouping", out object g)) grouping = JsValueOps.ToBoolean(g);
                if (opts.GetProperty("minimumFractionDigits", out object mn)) minFrac = (int)JsValueOps.ToNumber(mn);
                if (opts.GetProperty("maximumFractionDigits", out object mx)) maxFrac = (int)JsValueOps.ToNumber(mx);
            }
            return FormatNumber(num, style, currency, grouping, minFrac, maxFrac);
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
        m.Define("clz32", JsProperty.Data(realm.MakeMethod("clz32", delegate (JsCallArgs a)
        {
            uint u = (uint)JsValueOps.ToNumber(a.Get(0));
            if (u == 0) return 32.0;
            int count = 0;
            while ((u & 0x80000000U) == 0) { count++; u <<= 1; }
            return (double)count;
        }, 1)));
        m.Define("imul", JsProperty.Data(realm.MakeMethod("imul", delegate (JsCallArgs a)
        {
            int a32 = (int)(uint)JsValueOps.ToNumber(a.Get(0));
            int b32 = (int)(uint)JsValueOps.ToNumber(a.Get(1));
            return (double)(a32 * b32);
        }, 2)));

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

    private static readonly string[] DateDayNames = new string[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    private static readonly string[] DateMonthNames = new string[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

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

        // Getters
        dp.Define("getFullYear", JsProperty.Data(realm.MakeMethod("getFullYear", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Year;
        }, 0)));

        dp.Define("getUTCFullYear", JsProperty.Data(realm.MakeMethod("getUTCFullYear", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Year;
        }, 0)));

        dp.Define("getYear", JsProperty.Data(realm.MakeMethod("getYear", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)(TicksToDt(time).Year - 1900);
        }, 0)));

        dp.Define("getMonth", JsProperty.Data(realm.MakeMethod("getMonth", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)(TicksToDt(time).Month - 1);
        }, 0)));

        dp.Define("getUTCMonth", JsProperty.Data(realm.MakeMethod("getUTCMonth", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)(TicksToDt(time).Month - 1);
        }, 0)));

        dp.Define("getDate", JsProperty.Data(realm.MakeMethod("getDate", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Day;
        }, 0)));

        dp.Define("getUTCDate", JsProperty.Data(realm.MakeMethod("getUTCDate", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Day;
        }, 0)));

        dp.Define("getDay", JsProperty.Data(realm.MakeMethod("getDay", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)((int)TicksToDt(time).DayOfWeek);
        }, 0)));

        dp.Define("getUTCDay", JsProperty.Data(realm.MakeMethod("getUTCDay", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)((int)TicksToDt(time).DayOfWeek);
        }, 0)));

        dp.Define("getHours", JsProperty.Data(realm.MakeMethod("getHours", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Hour;
        }, 0)));

        dp.Define("getUTCHours", JsProperty.Data(realm.MakeMethod("getUTCHours", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Hour;
        }, 0)));

        dp.Define("getMinutes", JsProperty.Data(realm.MakeMethod("getMinutes", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Minute;
        }, 0)));

        dp.Define("getUTCMinutes", JsProperty.Data(realm.MakeMethod("getUTCMinutes", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Minute;
        }, 0)));

        dp.Define("getSeconds", JsProperty.Data(realm.MakeMethod("getSeconds", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Second;
        }, 0)));

        dp.Define("getUTCSeconds", JsProperty.Data(realm.MakeMethod("getUTCSeconds", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Second;
        }, 0)));

        dp.Define("getMilliseconds", JsProperty.Data(realm.MakeMethod("getMilliseconds", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Millisecond;
        }, 0)));

        dp.Define("getUTCMilliseconds", JsProperty.Data(realm.MakeMethod("getUTCMilliseconds", delegate (JsCallArgs a)
        {
            double time = a.ThisValue is JsDate jd ? jd.Time : JsValueOps.ToNumber(a.ThisValue);
            return double.IsNaN(time) ? double.NaN : (double)TicksToDt(time).Millisecond;
        }, 0)));

        dp.Define("getTimezoneOffset", JsProperty.Data(realm.MakeMethod("getTimezoneOffset", delegate (JsCallArgs a)
        {
            return 0.0;
        }, 0)));

        // Setters
        dp.Define("setTime", JsProperty.Data(realm.MakeMethod("setTime", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            double ms = a.ArgCount > 0 ? JsValueOps.ToNumber(a.Get(0)) : double.NaN;
            if (jd != null) jd.Time = ms;
            return ms;
        }, 1)));

        dp.Define("setFullYear", JsProperty.Data(realm.MakeMethod("setFullYear", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int y = (int)JsValueOps.ToNumber(a.Get(0));
            int m = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : (dt.Month - 1);
            int d = a.ArgCount > 2 ? (int)JsValueOps.ToNumber(a.Get(2)) : dt.Day;
            jd.Time = MakeUtcMs(y, m, d, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
            return jd.Time;
        }, 3)));

        dp.Define("setUTCFullYear", JsProperty.Data(realm.MakeMethod("setUTCFullYear", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int y = (int)JsValueOps.ToNumber(a.Get(0));
            int m = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : (dt.Month - 1);
            int d = a.ArgCount > 2 ? (int)JsValueOps.ToNumber(a.Get(2)) : dt.Day;
            jd.Time = MakeUtcMs(y, m, d, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
            return jd.Time;
        }, 3)));

        dp.Define("setMonth", JsProperty.Data(realm.MakeMethod("setMonth", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int m = (int)JsValueOps.ToNumber(a.Get(0));
            int d = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : dt.Day;
            jd.Time = MakeUtcMs(dt.Year, m, d, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
            return jd.Time;
        }, 2)));

        dp.Define("setUTCMonth", JsProperty.Data(realm.MakeMethod("setUTCMonth", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int m = (int)JsValueOps.ToNumber(a.Get(0));
            int d = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : dt.Day;
            jd.Time = MakeUtcMs(dt.Year, m, d, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
            return jd.Time;
        }, 2)));

        dp.Define("setDate", JsProperty.Data(realm.MakeMethod("setDate", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int d = (int)JsValueOps.ToNumber(a.Get(0));
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, d, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
            return jd.Time;
        }, 1)));

        dp.Define("setUTCDate", JsProperty.Data(realm.MakeMethod("setUTCDate", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int d = (int)JsValueOps.ToNumber(a.Get(0));
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, d, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
            return jd.Time;
        }, 1)));

        dp.Define("setHours", JsProperty.Data(realm.MakeMethod("setHours", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int h = (int)JsValueOps.ToNumber(a.Get(0));
            int min = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : dt.Minute;
            int s = a.ArgCount > 2 ? (int)JsValueOps.ToNumber(a.Get(2)) : dt.Second;
            int ms = a.ArgCount > 3 ? (int)JsValueOps.ToNumber(a.Get(3)) : dt.Millisecond;
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, dt.Day, h, min, s, ms);
            return jd.Time;
        }, 4)));

        dp.Define("setUTCHours", JsProperty.Data(realm.MakeMethod("setUTCHours", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int h = (int)JsValueOps.ToNumber(a.Get(0));
            int min = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : dt.Minute;
            int s = a.ArgCount > 2 ? (int)JsValueOps.ToNumber(a.Get(2)) : dt.Second;
            int ms = a.ArgCount > 3 ? (int)JsValueOps.ToNumber(a.Get(3)) : dt.Millisecond;
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, dt.Day, h, min, s, ms);
            return jd.Time;
        }, 4)));

        dp.Define("setMinutes", JsProperty.Data(realm.MakeMethod("setMinutes", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int min = (int)JsValueOps.ToNumber(a.Get(0));
            int s = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : dt.Second;
            int ms = a.ArgCount > 2 ? (int)JsValueOps.ToNumber(a.Get(2)) : dt.Millisecond;
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, dt.Day, dt.Hour, min, s, ms);
            return jd.Time;
        }, 3)));

        dp.Define("setUTCMinutes", JsProperty.Data(realm.MakeMethod("setUTCMinutes", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int min = (int)JsValueOps.ToNumber(a.Get(0));
            int s = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : dt.Second;
            int ms = a.ArgCount > 2 ? (int)JsValueOps.ToNumber(a.Get(2)) : dt.Millisecond;
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, dt.Day, dt.Hour, min, s, ms);
            return jd.Time;
        }, 3)));

        dp.Define("setSeconds", JsProperty.Data(realm.MakeMethod("setSeconds", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int s = (int)JsValueOps.ToNumber(a.Get(0));
            int ms = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : dt.Millisecond;
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, dt.Day, dt.Hour, dt.Minute, s, ms);
            return jd.Time;
        }, 2)));

        dp.Define("setUTCSeconds", JsProperty.Data(realm.MakeMethod("setUTCSeconds", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int s = (int)JsValueOps.ToNumber(a.Get(0));
            int ms = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : dt.Millisecond;
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, dt.Day, dt.Hour, dt.Minute, s, ms);
            return jd.Time;
        }, 2)));

        dp.Define("setMilliseconds", JsProperty.Data(realm.MakeMethod("setMilliseconds", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int ms = (int)JsValueOps.ToNumber(a.Get(0));
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, dt.Day, dt.Hour, dt.Minute, dt.Second, ms);
            return jd.Time;
        }, 1)));

        dp.Define("setUTCMilliseconds", JsProperty.Data(realm.MakeMethod("setUTCMilliseconds", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            int ms = (int)JsValueOps.ToNumber(a.Get(0));
            jd.Time = MakeUtcMs(dt.Year, dt.Month - 1, dt.Day, dt.Hour, dt.Minute, dt.Second, ms);
            return jd.Time;
        }, 1)));

        dp.Define("setYear", JsProperty.Data(realm.MakeMethod("setYear", delegate (JsCallArgs a)
        {
            JsDate jd = a.ThisValue as JsDate;
            if (jd == null) return double.NaN;
            int y = (int)JsValueOps.ToNumber(a.Get(0));
            if (y >= 0 && y <= 99) y += 1900;
            DateTime dt = double.IsNaN(jd.Time) ? System.DateTime.UtcNow : TicksToDt(jd.Time);
            jd.Time = MakeUtcMs(y, dt.Month - 1, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Millisecond);
            return jd.Time;
        }, 1)));

        // Formatters
        dp.Define("toString", JsProperty.Data(realm.MakeMethod("toString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            return FormatJsDate(time);
        }, 0)));

        dp.Define("toISOString", JsProperty.Data(realm.MakeMethod("toISOString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            return FormatIsoDate(time);
        }, 0)));

        dp.Define("toUTCString", JsProperty.Data(realm.MakeMethod("toUTCString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            return FormatUtcDate(time);
        }, 0)));

        dp.Define("toGMTString", JsProperty.Data(realm.MakeMethod("toGMTString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            return FormatUtcDate(time);
        }, 0)));

        dp.Define("toDateString", JsProperty.Data(realm.MakeMethod("toDateString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            if (double.IsNaN(time)) return "Invalid Date";
            DateTime dt = TicksToDt(time);
            return DateDayNames[(int)dt.DayOfWeek] + " " + DateMonthNames[dt.Month - 1] + " " + Pad2(dt.Day) + " " + dt.Year;
        }, 0)));

        dp.Define("toTimeString", JsProperty.Data(realm.MakeMethod("toTimeString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            if (double.IsNaN(time)) return "Invalid Date";
            DateTime dt = TicksToDt(time);
            return Pad2(dt.Hour) + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + " GMT";
        }, 0)));

        dp.Define("toLocaleString", JsProperty.Data(realm.MakeMethod("toLocaleString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            if (double.IsNaN(time)) return "Invalid Date";
            DateTime dt = TicksToDt(time);
            int h = dt.Hour % 12; if (h == 0) h = 12;
            string ampm = dt.Hour >= 12 ? "PM" : "AM";
            return dt.Month + "/" + dt.Day + "/" + dt.Year + ", " + h + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + " " + ampm;
        }, 0)));

        dp.Define("toLocaleDateString", JsProperty.Data(realm.MakeMethod("toLocaleDateString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            if (double.IsNaN(time)) return "Invalid Date";
            DateTime dt = TicksToDt(time);
            return dt.Month + "/" + dt.Day + "/" + dt.Year;
        }, 0)));

        dp.Define("toLocaleTimeString", JsProperty.Data(realm.MakeMethod("toLocaleTimeString", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            if (double.IsNaN(time)) return "Invalid Date";
            DateTime dt = TicksToDt(time);
            int h = dt.Hour % 12; if (h == 0) h = 12;
            string ampm = dt.Hour >= 12 ? "PM" : "AM";
            return h + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + " " + ampm;
        }, 0)));

        dp.Define("toJSON", JsProperty.Data(realm.MakeMethod("toJSON", delegate (JsCallArgs a)
        {
            object t = a.ThisValue;
            double time = t is JsDate jd ? jd.Time : JsValueOps.ToNumber(t);
            if (double.IsNaN(time)) return JsNull.Instance;
            return FormatIsoDate(time);
        }, 0)));

        realm.DateCtor = realm.MakeCtor("Date", delegate (JsCallArgs a)
        {
            if (a.IsConstruct)
            {
                double time;
                if (a.ArgCount == 0)
                {
                    time = NowMs();
                }
                else if (a.ArgCount == 1)
                {
                    object arg = a.Get(0);
                    if (arg is string s)
                    {
                        double parsed;
                        if (TryParseWebDate(s, out parsed)) time = parsed;
                        else time = double.NaN;
                    }
                    else if (arg is JsDate jd)
                    {
                        time = jd.Time;
                    }
                    else
                    {
                        time = JsValueOps.ToNumber(arg);
                    }
                }
                else
                {
                    int y = (int)JsValueOps.ToNumber(a.Get(0));
                    if (y >= 0 && y <= 99) y += 1900;
                    int m = (int)JsValueOps.ToNumber(a.Get(1));
                    int d = a.ArgCount > 2 ? (int)JsValueOps.ToNumber(a.Get(2)) : 1;
                    int h = a.ArgCount > 3 ? (int)JsValueOps.ToNumber(a.Get(3)) : 0;
                    int min = a.ArgCount > 4 ? (int)JsValueOps.ToNumber(a.Get(4)) : 0;
                    int sec = a.ArgCount > 5 ? (int)JsValueOps.ToNumber(a.Get(5)) : 0;
                    int ms = a.ArgCount > 6 ? (int)JsValueOps.ToNumber(a.Get(6)) : 0;
                    time = MakeUtcMs(y, m, d, h, min, sec, ms);
                }
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
            if (TryParseWebDate(s, out t)) return t;
            return double.NaN;
        }, 1)));

        realm.DateCtor.Define("UTC", JsProperty.Data(realm.MakeMethod("UTC", delegate (JsCallArgs a)
        {
            if (a.ArgCount == 0) return double.NaN;
            int y = (int)JsValueOps.ToNumber(a.Get(0));
            if (y >= 0 && y <= 99) y += 1900;
            int m = a.ArgCount > 1 ? (int)JsValueOps.ToNumber(a.Get(1)) : 0;
            int d = a.ArgCount > 2 ? (int)JsValueOps.ToNumber(a.Get(2)) : 1;
            int h = a.ArgCount > 3 ? (int)JsValueOps.ToNumber(a.Get(3)) : 0;
            int min = a.ArgCount > 4 ? (int)JsValueOps.ToNumber(a.Get(4)) : 0;
            int sec = a.ArgCount > 5 ? (int)JsValueOps.ToNumber(a.Get(5)) : 0;
            int ms = a.ArgCount > 6 ? (int)JsValueOps.ToNumber(a.Get(6)) : 0;
            return MakeUtcMs(y, m, d, h, min, sec, ms);
        }, 7)));
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
        return DateDayNames[(int)dt.DayOfWeek] + " " + DateMonthNames[dt.Month - 1] + " " + Pad2(dt.Day) +
            " " + dt.Year + " " + Pad2(dt.Hour) + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + " GMT";
    }

    private static string FormatIsoDate(double time)
    {
        if (double.IsNaN(time)) throw JsErr.Range("Invalid time value");
        System.DateTime dt = TicksToDt(time);
        return dt.Year.ToString() + "-" + Pad2(dt.Month) + "-" + Pad2(dt.Day) +
            "T" + Pad2(dt.Hour) + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + ".000Z";
    }

    private static string FormatUtcDate(double time)
    {
        if (double.IsNaN(time)) return "Invalid Date";
        System.DateTime dt = TicksToDt(time);
        return DateDayNames[(int)dt.DayOfWeek] + ", " + Pad2(dt.Day) + " " + DateMonthNames[dt.Month - 1] +
            " " + dt.Year + " " + Pad2(dt.Hour) + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + " GMT";
    }

    private static double MakeUtcMs(int y, int m, int d, int h, int min, int s, int ms)
    {
        try
        {
            while (m < 0) { y--; m += 12; }
            while (m > 11) { y++; m -= 12; }
            if (y < 1) y = 1;
            if (y > 9999) y = 9999;
            System.DateTime dt = new System.DateTime(y, m + 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            dt = dt.AddDays(d - 1).AddHours(h).AddMinutes(min).AddSeconds(s).AddMilliseconds(ms);
            return (double)(dt.Ticks - 621355968000000000L) / 10000.0;
        }
        catch
        {
            return double.NaN;
        }
    }

    private static System.DateTime TicksToDt(double time)
    {
        long ticks = (long)(time * 10000.0) + 621355968000000000L;
        if (ticks < 0) ticks = 0;
        if (ticks > 3155378975999999999L) ticks = 3155378975999999999L;
        return new System.DateTime(ticks, System.DateTimeKind.Utc);
    }

    private static bool TryParseWebDate(string s, out double time)
    {
        time = double.NaN;
        if (string.IsNullOrEmpty(s)) return false;
        string trimmed = s.Trim();
        try
        {
            System.DateTime dt;
            if (System.DateTime.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt))
            {
                time = (double)(dt.Ticks - 621355968000000000L) / 10000.0;
                return true;
            }
            if (System.DateTime.TryParseExact(trimmed, "r", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out dt))
            {
                time = (double)(dt.Ticks - 621355968000000000L) / 10000.0;
                return true;
            }
        }
        catch { }
        return TryParseIsoDate(trimmed, out time);
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

    // ---- Intl --------------------------------------------------------------

    private static void DefineMethod(JsRealm realm, JsObject o, string name, JsNativeFn fn, int len)
    {
        o.Define(name, JsProperty.DataNonEnum(realm.MakeMethod(name, fn, len)));
    }

    public static void BuildIntl(JsRealm realm)
    {
        JsObject intl = realm.NewObject();
        intl.SetWellKnown(JsWellKnown.ToStringTag, "Intl");

        // Intl.NumberFormat
        JsObject nfProto = realm.NewObject();
        nfProto.SetWellKnown(JsWellKnown.ToStringTag, "Intl.NumberFormat");
        DefineMethod(realm, nfProto, "format", delegate (JsCallArgs a)
        {
            JsObject self = a.ThisValue as JsObject;
            string style = "decimal";
            string currency = "USD";
            bool grouping = true;
            int minFrac = 0;
            int maxFrac = 3;
            if (self != null)
            {
                if (self.GetProperty("_style", out object st)) style = JsValueOps.ToStringValue(st);
                if (self.GetProperty("_currency", out object cur)) currency = JsValueOps.ToStringValue(cur);
                if (self.GetProperty("_grouping", out object gp)) grouping = JsValueOps.ToBoolean(gp);
                if (self.GetProperty("_minFrac", out object mn)) minFrac = (int)JsValueOps.ToNumber(mn);
                if (self.GetProperty("_maxFrac", out object mx)) maxFrac = (int)JsValueOps.ToNumber(mx);
            }
            double n = a.ArgCount > 0 ? JsValueOps.ToNumber(a.Get(0)) : 0.0;
            return FormatNumber(n, style, currency, grouping, minFrac, maxFrac);
        }, 1);

        DefineMethod(realm, nfProto, "formatToParts", delegate (JsCallArgs a)
        {
            JsArray arr = realm.NewArray();
            JsObject part = realm.NewObject();
            part.Define("type", JsProperty.Data("integer"));
            part.Define("value", JsProperty.Data(FormatNumber(a.ArgCount > 0 ? JsValueOps.ToNumber(a.Get(0)) : 0.0, "decimal", "USD", true, 0, 3)));
            arr.Items.Add(part);
            return arr;
        }, 1);

        DefineMethod(realm, nfProto, "resolvedOptions", delegate (JsCallArgs a)
        {
            JsObject self = a.ThisValue as JsObject;
            JsObject ro = realm.NewObject();
            ro.Define("locale", JsProperty.Data("en-US"));
            ro.Define("numberingSystem", JsProperty.Data("latn"));
            ro.Define("style", JsProperty.Data(self != null && self.GetProperty("_style", out object st) ? st : "decimal"));
            ro.Define("currency", JsProperty.Data(self != null && self.GetProperty("_currency", out object c) ? c : "USD"));
            ro.Define("minimumFractionDigits", JsProperty.Data(self != null && self.GetProperty("_minFrac", out object mn) ? mn : 0.0));
            ro.Define("maximumFractionDigits", JsProperty.Data(self != null && self.GetProperty("_maxFrac", out object mx) ? mx : 3.0));
            ro.Define("useGrouping", JsProperty.Data(self != null && self.GetProperty("_grouping", out object gp) ? gp : true));
            return ro;
        }, 0);

        JsFunction nfCtor = realm.MakeCtor("NumberFormat", delegate (JsCallArgs a)
        {
            JsObject nf = new JsObject(nfProto);
            string style = "decimal";
            string currency = "USD";
            bool grouping = true;
            int minFrac = 0;
            int maxFrac = 3;
            if (a.ArgCount > 1 && a.Get(1) is JsObject opts)
            {
                if (opts.GetProperty("style", out object s)) style = JsValueOps.ToStringValue(s);
                if (opts.GetProperty("currency", out object c)) currency = JsValueOps.ToStringValue(c);
                if (opts.GetProperty("useGrouping", out object g)) grouping = JsValueOps.ToBoolean(g);
                if (opts.GetProperty("minimumFractionDigits", out object mn)) minFrac = (int)JsValueOps.ToNumber(mn);
                if (opts.GetProperty("maximumFractionDigits", out object mx)) maxFrac = (int)JsValueOps.ToNumber(mx);
            }
            if (style == "currency") { minFrac = 2; maxFrac = 2; }
            else if (style == "percent") { minFrac = 0; maxFrac = 0; }
            nf.Define("_style", JsProperty.DataNonEnum(style));
            nf.Define("_currency", JsProperty.DataNonEnum(currency));
            nf.Define("_grouping", JsProperty.DataNonEnum(grouping));
            nf.Define("_minFrac", JsProperty.DataNonEnum((double)minFrac));
            nf.Define("_maxFrac", JsProperty.DataNonEnum((double)maxFrac));
            return nf;
        }, 0, nfProto);
        intl.Define("NumberFormat", JsProperty.Data(nfCtor));

        // Intl.DateTimeFormat
        JsObject dtfProto = realm.NewObject();
        dtfProto.SetWellKnown(JsWellKnown.ToStringTag, "Intl.DateTimeFormat");
        DefineMethod(realm, dtfProto, "format", delegate (JsCallArgs a)
        {
            double time = NowMs();
            if (a.ArgCount > 0)
            {
                object arg = a.Get(0);
                if (arg is JsDate jd) time = jd.Time;
                else time = JsValueOps.ToNumber(arg);
            }
            if (double.IsNaN(time)) return "Invalid Date";
            DateTime dt = TicksToDt(time);
            int h = dt.Hour % 12; if (h == 0) h = 12;
            string ampm = dt.Hour >= 12 ? "PM" : "AM";
            return dt.Month + "/" + dt.Day + "/" + dt.Year + ", " + h + ":" + Pad2(dt.Minute) + ":" + Pad2(dt.Second) + " " + ampm;
        }, 1);

        DefineMethod(realm, dtfProto, "formatToParts", delegate (JsCallArgs a)
        {
            JsArray arr = realm.NewArray();
            JsObject p = realm.NewObject();
            p.Define("type", JsProperty.Data("literal"));
            p.Define("value", JsProperty.Data(""));
            arr.Items.Add(p);
            return arr;
        }, 1);

        DefineMethod(realm, dtfProto, "resolvedOptions", delegate (JsCallArgs a)
        {
            JsObject ro = realm.NewObject();
            ro.Define("locale", JsProperty.Data("en-US"));
            ro.Define("calendar", JsProperty.Data("gregory"));
            ro.Define("numberingSystem", JsProperty.Data("latn"));
            ro.Define("timeZone", JsProperty.Data("UTC"));
            return ro;
        }, 0);

        JsFunction dtfCtor = realm.MakeCtor("DateTimeFormat", delegate (JsCallArgs a)
        {
            return new JsObject(dtfProto);
        }, 0, dtfProto);
        intl.Define("DateTimeFormat", JsProperty.Data(dtfCtor));

        // Intl.RelativeTimeFormat
        JsObject rtfProto = realm.NewObject();
        rtfProto.SetWellKnown(JsWellKnown.ToStringTag, "Intl.RelativeTimeFormat");
        DefineMethod(realm, rtfProto, "format", delegate (JsCallArgs a)
        {
            double val = a.ArgCount > 0 ? JsValueOps.ToNumber(a.Get(0)) : 0.0;
            string unit = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)).ToLowerInvariant() : "day";
            if (unit.EndsWith("s")) unit = unit.Substring(0, unit.Length - 1);
            if (val == 0) return "now";
            if (val < 0)
            {
                double pos = -val;
                return (pos == 1 ? "1 " + unit + " ago" : pos.ToString() + " " + unit + "s ago");
            }
            return (val == 1 ? "in 1 " + unit : "in " + val.ToString() + " " + unit + "s");
        }, 2);

        DefineMethod(realm, rtfProto, "formatToParts", delegate (JsCallArgs a)
        {
            JsArray arr = realm.NewArray();
            JsObject p = realm.NewObject();
            p.Define("type", JsProperty.Data("literal"));
            p.Define("value", JsProperty.Data(""));
            arr.Items.Add(p);
            return arr;
        }, 2);

        DefineMethod(realm, rtfProto, "resolvedOptions", delegate (JsCallArgs a)
        {
            JsObject ro = realm.NewObject();
            ro.Define("locale", JsProperty.Data("en-US"));
            ro.Define("numeric", JsProperty.Data("always"));
            return ro;
        }, 0);

        JsFunction rtfCtor = realm.MakeCtor("RelativeTimeFormat", delegate (JsCallArgs a)
        {
            return new JsObject(rtfProto);
        }, 0, rtfProto);
        intl.Define("RelativeTimeFormat", JsProperty.Data(rtfCtor));

        // Intl.PluralRules
        JsObject prProto = realm.NewObject();
        DefineMethod(realm, prProto, "select", delegate (JsCallArgs a)
        {
            double n = a.ArgCount > 0 ? Math.Abs(JsValueOps.ToNumber(a.Get(0))) : 0.0;
            return n == 1.0 ? "one" : "other";
        }, 1);
        DefineMethod(realm, prProto, "resolvedOptions", delegate (JsCallArgs a)
        {
            JsObject ro = realm.NewObject();
            ro.Define("locale", JsProperty.Data("en-US"));
            ro.Define("type", JsProperty.Data("cardinal"));
            return ro;
        }, 0);
        JsFunction prCtor = realm.MakeCtor("PluralRules", delegate (JsCallArgs a) { return new JsObject(prProto); }, 0, prProto);
        intl.Define("PluralRules", JsProperty.Data(prCtor));

        // Intl.Collator
        JsObject colProto = realm.NewObject();
        DefineMethod(realm, colProto, "compare", delegate (JsCallArgs a)
        {
            string s1 = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
            string s2 = a.ArgCount > 1 ? JsValueOps.ToStringValue(a.Get(1)) : "";
            int c = string.Compare(s1, s2, StringComparison.OrdinalIgnoreCase);
            return (double)(c < 0 ? -1 : (c > 0 ? 1 : 0));
        }, 2);
        DefineMethod(realm, colProto, "resolvedOptions", delegate (JsCallArgs a)
        {
            JsObject ro = realm.NewObject();
            ro.Define("locale", JsProperty.Data("en-US"));
            ro.Define("sensitivity", JsProperty.Data("variant"));
            return ro;
        }, 0);
        JsFunction colCtor = realm.MakeCtor("Collator", delegate (JsCallArgs a) { return new JsObject(colProto); }, 0, colProto);
        intl.Define("Collator", JsProperty.Data(colCtor));

        // Intl.ListFormat
        JsObject lfProto = realm.NewObject();
        DefineMethod(realm, lfProto, "format", delegate (JsCallArgs a)
        {
            object arg = a.Get(0);
            if (arg is JsArray ja && ja.Items.Count > 0)
            {
                if (ja.Items.Count == 1) return JsValueOps.ToStringValue(ja.Items[0]);
                if (ja.Items.Count == 2) return JsValueOps.ToStringValue(ja.Items[0]) + " and " + JsValueOps.ToStringValue(ja.Items[1]);
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < ja.Items.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    if (i == ja.Items.Count - 1) sb.Append("and ");
                    sb.Append(JsValueOps.ToStringValue(ja.Items[i]));
                }
                return sb.ToString();
            }
            return "";
        }, 1);
        JsFunction lfCtor = realm.MakeCtor("ListFormat", delegate (JsCallArgs a) { return new JsObject(lfProto); }, 0, lfProto);
        intl.Define("ListFormat", JsProperty.Data(lfCtor));

        // Intl.DisplayNames
        JsObject dnProto = realm.NewObject();
        DefineMethod(realm, dnProto, "of", delegate (JsCallArgs a)
        {
            return a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "";
        }, 1);
        JsFunction dnCtor = realm.MakeCtor("DisplayNames", delegate (JsCallArgs a) { return new JsObject(dnProto); }, 2, dnProto);
        intl.Define("DisplayNames", JsProperty.Data(dnCtor));

        // Intl.Locale
        JsObject locProto = realm.NewObject();
        JsFunction locCtor = realm.MakeCtor("Locale", delegate (JsCallArgs a)
        {
            string tag = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : "en-US";
            JsObject loc = new JsObject(locProto);
            loc.Define("baseName", JsProperty.Data(tag));
            int hyphen = tag.IndexOf('-');
            string lang = hyphen > 0 ? tag.Substring(0, hyphen) : tag;
            string region = hyphen > 0 && hyphen + 1 < tag.Length ? tag.Substring(hyphen + 1) : "";
            loc.Define("language", JsProperty.Data(lang));
            loc.Define("region", JsProperty.Data(region));
            return loc;
        }, 1, locProto);
        intl.Define("Locale", JsProperty.Data(locCtor));

        // Intl.getCanonicalLocales
        DefineMethod(realm, intl, "getCanonicalLocales", delegate (JsCallArgs a)
        {
            JsArray arr = realm.NewArray();
            if (a.ArgCount > 0)
            {
                object arg = a.Get(0);
                if (arg is JsArray ja)
                {
                    for (int i = 0; i < ja.Items.Count; i++)
                        arr.Items.Add(JsValueOps.ToStringValue(ja.Items[i]));
                }
                else
                {
                    arr.Items.Add(JsValueOps.ToStringValue(arg));
                }
            }
            return arr;
        }, 1);

        realm.Global.Define("Intl", JsProperty.Data(intl));
    }

    private static string FormatNumber(double d, string style, string currency, bool useGrouping, int minFrac, int maxFrac)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";

        bool isNeg = d < 0;
        double abs = Math.Abs(d);
        if (style == "percent") abs *= 100.0;

        double factor = Math.Pow(10, Math.Min(maxFrac, 10));
        abs = Math.Round(abs * factor) / factor;

        long intPart = (long)Math.Floor(abs);
        double fracPart = abs - intPart;

        string intStr = intPart.ToString();
        if (useGrouping && intStr.Length > 3)
        {
            StringBuilder sb = new StringBuilder();
            int rem = intStr.Length % 3;
            if (rem > 0)
            {
                sb.Append(intStr.Substring(0, rem));
                if (intStr.Length > rem) sb.Append(',');
            }
            for (int i = rem; i < intStr.Length; i += 3)
            {
                sb.Append(intStr.Substring(i, 3));
                if (i + 3 < intStr.Length) sb.Append(',');
            }
            intStr = sb.ToString();
        }

        string fracStr = "";
        if (maxFrac > 0)
        {
            long fracNum = (long)Math.Round(fracPart * factor);
            fracStr = fracNum.ToString().PadLeft(maxFrac, '0');
            int trimLen = fracStr.Length;
            while (trimLen > minFrac && fracStr[trimLen - 1] == '0') trimLen--;
            fracStr = fracStr.Substring(0, trimLen);
        }

        string numStr = (isNeg ? "-" : "") + intStr + (fracStr.Length > 0 ? "." + fracStr : "");
        if (style == "percent") return numStr + "%";
        if (style == "currency")
        {
            if (currency == "USD") return (isNeg ? "-$" : "$") + intStr + (fracStr.Length > 0 ? "." + fracStr : "");
            if (currency == "EUR") return (isNeg ? "-€" : "€") + intStr + (fracStr.Length > 0 ? "." + fracStr : "");
            if (currency == "GBP") return (isNeg ? "-£" : "£") + intStr + (fracStr.Length > 0 ? "." + fracStr : "");
            return (isNeg ? "-" : "") + currency + " " + intStr + (fracStr.Length > 0 ? "." + fracStr : "");
        }
        return numStr;
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

    // ---- WeakMap / WeakSet -------------------------------------------------

    public static void BuildWeakMapSet(JsRealm realm)
    {
        // WeakMap
        JsObject wmp = realm.NewObject();
        SetTag(realm, wmp, "WeakMap");
        wmp.Define("get", JsProperty.Data(realm.MakeMethod("get", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            if (map == null) return JsUndefined.Instance;
            object key = a.Get(0);
            if (!(key is JsObject)) return JsUndefined.Instance;
            int idx = map.IndexOfKey(key);
            return idx >= 0 ? map.Values[idx] : JsUndefined.Instance;
        }, 1)));
        wmp.Define("set", JsProperty.Data(realm.MakeMethod("set", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            if (map == null) return a.ThisValue;
            object key = a.Get(0);
            if (!(key is JsObject)) throw JsErr.Type("Invalid value used as weak map key");
            map.SetKey(key, a.Get(1));
            return map;
        }, 2)));
        wmp.Define("has", JsProperty.Data(realm.MakeMethod("has", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            if (map == null) return false;
            object key = a.Get(0);
            if (!(key is JsObject)) return false;
            return map.IndexOfKey(key) >= 0;
        }, 1)));
        wmp.Define("delete", JsProperty.Data(realm.MakeMethod("delete", delegate (JsCallArgs a)
        {
            JsMapObject map = a.ThisValue as JsMapObject;
            if (map == null) return false;
            object key = a.Get(0);
            if (!(key is JsObject)) return false;
            return map.RemoveKey(key);
        }, 1)));

        realm.Global.Define("WeakMap", JsProperty.Data(realm.MakeCtor("WeakMap", delegate (JsCallArgs a)
        {
            JsMapObject map = new JsMapObject(wmp);
            if (a.ArgCount > 0)
            {
                List<object> items = realm.Interp.ToList(a.Get(0));
                for (int i = 0; i < items.Count; i++)
                {
                    List<object> pair = realm.Interp.ToList(items[i]);
                    if (pair.Count >= 2 && pair[0] is JsObject) map.SetKey(pair[0], pair[1]);
                }
            }
            return map;
        }, 0, wmp)));

        // WeakSet
        JsObject wsp = realm.NewObject();
        SetTag(realm, wsp, "WeakSet");
        wsp.Define("add", JsProperty.Data(realm.MakeMethod("add", delegate (JsCallArgs a)
        {
            JsSetObject set = a.ThisValue as JsSetObject;
            if (set == null) return a.ThisValue;
            object v = a.Get(0);
            if (!(v is JsObject)) throw JsErr.Type("Invalid value used in weak set");
            set.AddValue(v);
            return set;
        }, 1)));
        wsp.Define("has", JsProperty.Data(realm.MakeMethod("has", delegate (JsCallArgs a)
        {
            JsSetObject set = a.ThisValue as JsSetObject;
            if (set == null) return false;
            object v = a.Get(0);
            if (!(v is JsObject)) return false;
            return set.IndexOf(v) >= 0;
        }, 1)));
        wsp.Define("delete", JsProperty.Data(realm.MakeMethod("delete", delegate (JsCallArgs a)
        {
            JsSetObject set = a.ThisValue as JsSetObject;
            if (set == null) return false;
            object v = a.Get(0);
            if (!(v is JsObject)) return false;
            return set.RemoveValue(v);
        }, 1)));

        realm.Global.Define("WeakSet", JsProperty.Data(realm.MakeCtor("WeakSet", delegate (JsCallArgs a)
        {
            JsSetObject set = new JsSetObject(wsp);
            if (a.ArgCount > 0)
            {
                List<object> items = realm.Interp.ToList(a.Get(0));
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] is JsObject) set.AddValue(items[i]);
                }
            }
            return set;
        }, 0, wsp)));
    }

    // ---- TypedArrays -------------------------------------------------------

    public static void BuildTypedArrays(JsRealm realm)
    {
        JsObject abProto = realm.NewObject();
        SetTag(realm, abProto, "ArrayBuffer");
        abProto.Define("byteLength", JsProperty.Accessor(
            realm.MakeMethod("get byteLength", delegate (JsCallArgs a)
            {
                JsArrayBuffer ab = a.ThisValue as JsArrayBuffer;
                return ab != null ? (object)(double)ab.ByteLength : (object)0.0;
            }, 0), null));
        abProto.Define("slice", JsProperty.Data(realm.MakeMethod("slice", delegate (JsCallArgs a)
        {
            JsArrayBuffer ab = a.ThisValue as JsArrayBuffer;
            if (ab == null) return realm.NewObject();
            int len = ab.ByteLength;
            int begin = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
            int end = a.ArgCount > 1 ? ToInteger(a.Get(1)) : len;
            if (begin < 0) begin = System.Math.Max(0, len + begin); else if (begin > len) begin = len;
            if (end < 0) end = System.Math.Max(0, len + end); else if (end > len) end = len;
            int span = System.Math.Max(0, end - begin);
            byte[] sliced = new byte[span];
            if (span > 0 && ab.Data != null) Array.Copy(ab.Data, begin, sliced, 0, span);
            return new JsArrayBuffer(abProto, sliced);
        }, 2)));

        realm.Global.Define("ArrayBuffer", JsProperty.Data(realm.MakeCtor("ArrayBuffer", delegate (JsCallArgs a)
        {
            int len = a.ArgCount > 0 ? (int)System.Math.Max(0, JsValueOps.ToNumber(a.Get(0))) : 0;
            return new JsArrayBuffer(abProto, len);
        }, 1, abProto)));

        string[] kinds = { "Uint8Array", "Int8Array", "Uint8ClampedArray", "Int16Array", "Uint16Array", "Int32Array", "Uint32Array", "Float32Array", "Float64Array" };
        int[] bpes = { 1, 1, 1, 2, 2, 4, 4, 4, 8 };

        for (int k = 0; k < kinds.Length; k++)
        {
            string kind = kinds[k];
            int bpe = bpes[k];

            JsObject proto = realm.NewObject();
            SetTag(realm, proto, kind);
            proto.Define("BYTES_PER_ELEMENT", JsProperty.DataNonEnum((double)bpe));

            proto.Define("set", JsProperty.Data(realm.MakeMethod("set", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                if (target == null) return JsUndefined.Instance;
                object src = a.Get(0);
                int offset = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
                if (src is JsTypedArray srcTa)
                {
                    for (int i = 0; i < srcTa.Length && (offset + i) < target.Length; i++)
                        target.SetElement(offset + i, srcTa.GetElement(i));
                }
                else if (src is JsArray srcArr)
                {
                    for (int i = 0; i < srcArr.Length && (offset + i) < target.Length; i++)
                        target.SetElement(offset + i, JsValueOps.ToNumber(srcArr.GetIndex(i)));
                }
                return JsUndefined.Instance;
            }, 2)));

            proto.Define("subarray", JsProperty.Data(realm.MakeMethod("subarray", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                if (target == null) return a.ThisValue;
                int begin = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
                int end = a.ArgCount > 1 ? ToInteger(a.Get(1)) : target.Length;
                if (begin < 0) begin = System.Math.Max(0, target.Length + begin); else if (begin > target.Length) begin = target.Length;
                if (end < 0) end = System.Math.Max(0, target.Length + end); else if (end > target.Length) end = target.Length;
                int span = System.Math.Max(0, end - begin);
                return new JsTypedArray(target.Prototype, target.Buffer, target.ByteOffset + begin * target.BytesPerElement, span, target.BytesPerElement, target.ArrayKind);
            }, 2)));

            proto.Define("slice", JsProperty.Data(realm.MakeMethod("slice", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                if (target == null) return a.ThisValue;
                int begin = a.ArgCount > 0 ? ToInteger(a.Get(0)) : 0;
                int end = a.ArgCount > 1 ? ToInteger(a.Get(1)) : target.Length;
                if (begin < 0) begin = System.Math.Max(0, target.Length + begin); else if (begin > target.Length) begin = target.Length;
                if (end < 0) end = System.Math.Max(0, target.Length + end); else if (end > target.Length) end = target.Length;
                int span = System.Math.Max(0, end - begin);
                JsArrayBuffer newBuf = new JsArrayBuffer(abProto, span * target.BytesPerElement);
                JsTypedArray res = new JsTypedArray(target.Prototype, newBuf, 0, span, target.BytesPerElement, target.ArrayKind);
                for (int i = 0; i < span; i++) res.SetElement(i, target.GetElement(begin + i));
                return res;
            }, 2)));

            proto.Define("forEach", JsProperty.Data(realm.MakeMethod("forEach", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                JsFunction fn = a.Get(0) as JsFunction;
                if (target == null || fn == null) return JsUndefined.Instance;
                object thisArg = a.Get(1);
                for (int i = 0; i < target.Length; i++)
                    realm.Interp.Call(fn, thisArg, new object[] { target.GetElement(i), (double)i, target }, false);
                return JsUndefined.Instance;
            }, 1)));

            proto.Define("join", JsProperty.Data(realm.MakeMethod("join", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                if (target == null) return "";
                string sep = a.ArgCount > 0 ? JsValueOps.ToStringValue(a.Get(0)) : ",";
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < target.Length; i++)
                {
                    if (i > 0) sb.Append(sep);
                    sb.Append(target.GetElement(i));
                }
                return sb.ToString();
            }, 1)));

            proto.Define("toString", JsProperty.Data(realm.MakeMethod("toString", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                if (target == null) return "";
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < target.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(target.GetElement(i));
                }
                return sb.ToString();
            }, 0)));

            proto.Define("indexOf", JsProperty.Data(realm.MakeMethod("indexOf", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                if (target == null) return -1.0;
                double search = JsValueOps.ToNumber(a.Get(0));
                int from = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
                if (from < 0) from = System.Math.Max(0, target.Length + from);
                for (int i = from; i < target.Length; i++)
                {
                    if (target.GetElement(i) == search) return (double)i;
                }
                return -1.0;
            }, 2)));

            proto.Define("includes", JsProperty.Data(realm.MakeMethod("includes", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                if (target == null) return false;
                double search = JsValueOps.ToNumber(a.Get(0));
                for (int i = 0; i < target.Length; i++)
                {
                    if (target.GetElement(i) == search) return true;
                }
                return false;
            }, 1)));

            proto.Define("fill", JsProperty.Data(realm.MakeMethod("fill", delegate (JsCallArgs a)
            {
                JsTypedArray target = a.ThisValue as JsTypedArray;
                if (target == null) return a.ThisValue;
                double val = JsValueOps.ToNumber(a.Get(0));
                int start = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
                int end = a.ArgCount > 2 ? ToInteger(a.Get(2)) : target.Length;
                if (start < 0) start = System.Math.Max(0, target.Length + start);
                if (end < 0) end = System.Math.Max(0, target.Length + end);
                if (end > target.Length) end = target.Length;
                for (int i = start; i < end; i++) target.SetElement(i, val);
                return target;
            }, 3)));

            string curKind = kind;
            int curBpe = bpe;
            JsObject curProto = proto;

            JsFunction ctor = realm.MakeCtor(curKind, delegate (JsCallArgs a)
            {
                object arg0 = a.Get(0);
                if (arg0 is JsArrayBuffer ab)
                {
                    int byteOffset = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
                    int length;
                    if (a.ArgCount > 2)
                    {
                        length = ToInteger(a.Get(2));
                    }
                    else
                    {
                        length = System.Math.Max(0, (ab.ByteLength - byteOffset) / curBpe);
                    }
                    return new JsTypedArray(curProto, ab, byteOffset, length, curBpe, curKind);
                }
                else if (arg0 is JsTypedArray srcTa)
                {
                    JsArrayBuffer newBuf = new JsArrayBuffer(abProto, srcTa.Length * curBpe);
                    JsTypedArray res = new JsTypedArray(curProto, newBuf, 0, srcTa.Length, curBpe, curKind);
                    for (int i = 0; i < srcTa.Length; i++) res.SetElement(i, srcTa.GetElement(i));
                    return res;
                }
                else if (arg0 is JsArray srcArr)
                {
                    JsArrayBuffer newBuf = new JsArrayBuffer(abProto, srcArr.Length * curBpe);
                    JsTypedArray res = new JsTypedArray(curProto, newBuf, 0, srcArr.Length, curBpe, curKind);
                    for (int i = 0; i < srcArr.Length; i++) res.SetElement(i, JsValueOps.ToNumber(srcArr.GetIndex(i)));
                    return res;
                }
                else
                {
                    int length = (int)System.Math.Max(0, JsValueOps.ToNumber(arg0));
                    JsArrayBuffer newBuf = new JsArrayBuffer(abProto, length * curBpe);
                    return new JsTypedArray(curProto, newBuf, 0, length, curBpe, curKind);
                }
            }, 1, curProto);

            ctor.Define("BYTES_PER_ELEMENT", JsProperty.DataNonEnum((double)curBpe));
            realm.Global.Define(curKind, JsProperty.Data(ctor));
        }

        JsObject dvProto = realm.NewObject();
        SetTag(realm, dvProto, "DataView");
        dvProto.Define("getInt8", JsProperty.Data(realm.MakeMethod("getInt8", delegate (JsCallArgs a)
        {
            JsDataView dv = a.ThisValue as JsDataView;
            if (dv == null || dv.Buffer == null || dv.Buffer.Data == null) return 0.0;
            int offset = dv.ByteOffset + ToInteger(a.Get(0));
            if (offset < 0 || offset >= dv.Buffer.Data.Length) return 0.0;
            return (double)(sbyte)dv.Buffer.Data[offset];
        }, 1)));
        dvProto.Define("getUint8", JsProperty.Data(realm.MakeMethod("getUint8", delegate (JsCallArgs a)
        {
            JsDataView dv = a.ThisValue as JsDataView;
            if (dv == null || dv.Buffer == null || dv.Buffer.Data == null) return 0.0;
            int offset = dv.ByteOffset + ToInteger(a.Get(0));
            if (offset < 0 || offset >= dv.Buffer.Data.Length) return 0.0;
            return (double)dv.Buffer.Data[offset];
        }, 1)));
        dvProto.Define("setInt8", JsProperty.Data(realm.MakeMethod("setInt8", delegate (JsCallArgs a)
        {
            JsDataView dv = a.ThisValue as JsDataView;
            if (dv != null && dv.Buffer != null && dv.Buffer.Data != null)
            {
                int offset = dv.ByteOffset + ToInteger(a.Get(0));
                if (offset >= 0 && offset < dv.Buffer.Data.Length)
                    dv.Buffer.Data[offset] = (byte)(sbyte)JsValueOps.ToNumber(a.Get(1));
            }
            return JsUndefined.Instance;
        }, 2)));
        dvProto.Define("setUint8", JsProperty.Data(realm.MakeMethod("setUint8", delegate (JsCallArgs a)
        {
            JsDataView dv = a.ThisValue as JsDataView;
            if (dv != null && dv.Buffer != null && dv.Buffer.Data != null)
            {
                int offset = dv.ByteOffset + ToInteger(a.Get(0));
                if (offset >= 0 && offset < dv.Buffer.Data.Length)
                    dv.Buffer.Data[offset] = (byte)JsValueOps.ToNumber(a.Get(1));
            }
            return JsUndefined.Instance;
        }, 2)));

        realm.Global.Define("DataView", JsProperty.Data(realm.MakeCtor("DataView", delegate (JsCallArgs a)
        {
            JsArrayBuffer ab = a.Get(0) as JsArrayBuffer;
            if (ab == null) throw JsErr.Type("First argument to DataView constructor must be an ArrayBuffer");
            int byteOffset = a.ArgCount > 1 ? ToInteger(a.Get(1)) : 0;
            int byteLength = a.ArgCount > 2 ? ToInteger(a.Get(2)) : (ab.ByteLength - byteOffset);
            return new JsDataView(dvProto, ab, byteOffset, byteLength);
        }, 1, dvProto)));
    }

    private static void BuildProxyReflect(JsRealm realm)
    {
        realm.ProxyCtor = realm.MakeCtor("Proxy", delegate (JsCallArgs a)
        {
            JsObject target = a.Get(0) as JsObject;
            JsObject handler = a.Get(1) as JsObject;
            if (target == null || handler == null)
                throw JsErr.Type("Cannot create proxy with a non-object as target or handler");
            return new JsProxyObject(target, handler);
        }, 2, realm.NewObject());
        realm.Global.Define("Proxy", JsProperty.Data(realm.ProxyCtor));

        realm.ReflectObj = realm.NewObject();
        realm.ReflectObj.SetWellKnown(JsWellKnown.ToStringTag, "Reflect");

        realm.ReflectObj.Define("get", JsProperty.Data(realm.MakeMethod("get", delegate (JsCallArgs a)
        {
            JsObject o = a.Get(0) as JsObject;
            if (o == null) throw JsErr.Type("Reflect.get called on non-object");
            string key = JsValueOps.ToStringValue(a.Get(1));
            o.GetProperty(key, out object val);
            return val;
        }, 2)));

        realm.ReflectObj.Define("set", JsProperty.Data(realm.MakeMethod("set", delegate (JsCallArgs a)
        {
            JsObject o = a.Get(0) as JsObject;
            if (o == null) throw JsErr.Type("Reflect.set called on non-object");
            string key = JsValueOps.ToStringValue(a.Get(1));
            o.SetProperty(key, a.Get(2));
            return true;
        }, 3)));

        realm.ReflectObj.Define("has", JsProperty.Data(realm.MakeMethod("has", delegate (JsCallArgs a)
        {
            JsObject o = a.Get(0) as JsObject;
            if (o == null) throw JsErr.Type("Reflect.has called on non-object");
            string key = JsValueOps.ToStringValue(a.Get(1));
            return o.HasProperty(key);
        }, 2)));

        realm.ReflectObj.Define("deleteProperty", JsProperty.Data(realm.MakeMethod("deleteProperty", delegate (JsCallArgs a)
        {
            JsObject o = a.Get(0) as JsObject;
            if (o == null) throw JsErr.Type("Reflect.deleteProperty called on non-object");
            string key = JsValueOps.ToStringValue(a.Get(1));
            return o.DeleteProperty(key);
        }, 2)));

        realm.ReflectObj.Define("apply", JsProperty.Data(realm.MakeMethod("apply", delegate (JsCallArgs a)
        {
            JsFunction fn = a.Get(0) as JsFunction;
            if (fn == null) throw JsErr.Type("Reflect.apply target is not a function");
            object thisArg = a.Get(1);
            object[] args = new object[0];
            if (a.Get(2) is JsArray arr) args = arr.Items.ToArray();
            return realm.Interp.Call(fn, thisArg, args, false);
        }, 3)));

        realm.ReflectObj.Define("construct", JsProperty.Data(realm.MakeMethod("construct", delegate (JsCallArgs a)
        {
            JsFunction fn = a.Get(0) as JsFunction;
            if (fn == null) throw JsErr.Type("Reflect.construct target is not a constructor");
            object[] args = new object[0];
            if (a.Get(1) is JsArray arr) args = arr.Items.ToArray();
            return realm.Interp.Call(fn, JsUndefined.Instance, args, true);
        }, 2)));

        realm.ReflectObj.Define("ownKeys", JsProperty.Data(realm.MakeMethod("ownKeys", delegate (JsCallArgs a)
        {
            JsObject o = a.Get(0) as JsObject;
            if (o == null) throw JsErr.Type("Reflect.ownKeys called on non-object");
            List<string> keys = o.OwnKeys(false);
            List<object> objs = new List<object>(keys.Count);
            for (int i = 0; i < keys.Count; i++) objs.Add(keys[i]);
            return realm.NewArray(objs);
        }, 1)));

        realm.ReflectObj.Define("getPrototypeOf", JsProperty.Data(realm.MakeMethod("getPrototypeOf", delegate (JsCallArgs a)
        {
            JsObject o = a.Get(0) as JsObject;
            if (o == null) throw JsErr.Type("Reflect.getPrototypeOf called on non-object");
            return o.Prototype != null ? (object)o.Prototype : JsNull.Instance;
        }, 1)));

        realm.ReflectObj.Define("setPrototypeOf", JsProperty.Data(realm.MakeMethod("setPrototypeOf", delegate (JsCallArgs a)
        {
            JsObject o = a.Get(0) as JsObject;
            if (o == null) throw JsErr.Type("Reflect.setPrototypeOf called on non-object");
            JsObject proto = a.Get(1) as JsObject;
            o.SetPrototype(proto);
            return true;
        }, 2)));

        realm.Global.Define("Reflect", JsProperty.Data(realm.ReflectObj));
    }

    private static object CloneStructured(JsRealm realm, object v, Dictionary<object, object> seen)
    {
        if (v == null || v is JsUndefined || v is JsNull || v is bool || v is double || v is string || v is JsSymbol)
            return v;

        if (seen.ContainsKey(v)) return seen[v];

        if (v is JsArray arr)
        {
            JsArray copy = realm.NewArray();
            seen[v] = copy;
            for (int i = 0; i < arr.Items.Count; i++)
            {
                copy.Push(CloneStructured(realm, arr.Items[i], seen));
            }
            return copy;
        }

        if (v is JsDate date)
        {
            JsDate copy = new JsDate(realm.DatePrototype, date.TimeValue);
            seen[v] = copy;
            return copy;
        }

        if (v is JsRegExpObject re)
        {
            JsRegExpObject copy = new JsRegExpObject(realm.RegExpPrototype, re.Value);
            seen[v] = copy;
            return copy;
        }

        if (v is JsArrayBuffer buf)
        {
            byte[] copyData = new byte[buf.ByteLength];
            if (buf.Data != null) Array.Copy(buf.Data, copyData, buf.ByteLength);
            JsArrayBuffer copyBuf = new JsArrayBuffer(realm.ObjectPrototype, copyData);
            seen[v] = copyBuf;
            return copyBuf;
        }

        if (v is JsTypedArray ta)
        {
            JsArrayBuffer clonedBuf = (JsArrayBuffer)CloneStructured(realm, ta.Buffer, seen);
            JsTypedArray copyTa = new JsTypedArray(ta.Prototype, clonedBuf, ta.ByteOffset, ta.Length, ta.BytesPerElement, ta.ArrayKind);
            seen[v] = copyTa;
            return copyTa;
        }

        if (v is JsObject obj)
        {
            JsObject copy = realm.NewObject();
            seen[v] = copy;
            List<string> keys = obj.OwnKeys(true);
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                if (obj.GetProperty(k, out object propVal))
                {
                    copy.SetProperty(k, CloneStructured(realm, propVal, seen));
                }
            }
            return copy;
        }

        return v;
    }

    // ---- global functions --------------------------------------------------

    public static void BuildGlobalFunctions(JsRealm realm)
    {
        realm.Global.Define("structuredClone", JsProperty.Data(realm.MakeMethod("structuredClone", delegate (JsCallArgs a)
        {
            object v = a.Get(0);
            return CloneStructured(realm, v, new Dictionary<object, object>());
        }, 1)));

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




