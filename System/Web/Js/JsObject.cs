using System;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// The JavaScript object model: property descriptors, the prototype chain,
// arrays, functions (interpreted and native) and the property get/set/delete/
// define algorithms. Property keys are either strings or symbols.
// ---------------------------------------------------------------------------

public enum JsPropKind
{
    Data,
    Accessor,
}

public sealed class JsProperty
{
    public object Value;               // [[Value]] for data properties
    public JsFunction Getter, Setter;  // [[Get]] / [[Set]] for accessor properties
    public bool Writable = true;
    public bool Enumerable = true;
    public bool Configurable = true;
    public JsPropKind Kind;

    public static JsProperty Data(object value)
    {
        return new JsProperty { Value = value, Kind = JsPropKind.Data };
    }

    public static JsProperty DataNonEnum(object value)
    {
        return new JsProperty { Value = value, Kind = JsPropKind.Data, Enumerable = false };
    }

    public static JsProperty Accessor(JsFunction get, JsFunction set)
    {
        return new JsProperty { Getter = get, Setter = set, Kind = JsPropKind.Accessor };
    }
}

public class JsObject
{
    public JsObject Prototype;
    public bool Extensible = true;

    // RegExp instance payload (set by JsBuiltins.CreateRegExp)
    public JsRegExp RegExpValue;

    // string-keyed properties + insertion order (for for-in / JSON / Object.keys)
    private Dictionary<string, JsProperty> props;
    private List<string> order;

    // symbol-keyed properties (few; linear scan is fine)
    private Dictionary<JsSymbol, JsProperty> symProps;

    // array fast path (JsArray overrides these)
    public virtual string ClassTag { get { return "Object"; } }

    public JsObject() { }
    public JsObject(JsObject prototype) { Prototype = prototype; }

    // ---- raw storage ------------------------------------------------------

    public bool TryGetRaw(string key, out JsProperty p)
    {
        if (props == null) { p = null; return false; }
        return props.TryGetValue(key, out p);
    }

    public bool TryGetRaw(JsSymbol sym, out JsProperty p)
    {
        if (symProps == null) { p = null; return false; }
        return symProps.TryGetValue(sym, out p);
    }

    public void Define(string key, JsProperty p)
    {
        if (props == null) { props = new Dictionary<string, JsProperty>(); order = new List<string>(); }
        bool existed;
        JsProperty old;
        existed = props.TryGetValue(key, out old);
        props[key] = p;
        if (!existed) order.Add(key);
    }

    public void Define(JsSymbol sym, JsProperty p)
    {
        if (symProps == null) symProps = new Dictionary<JsSymbol, JsProperty>();
        symProps[sym] = p;
    }

    public void Remove(string key)
    {
        if (props == null) return;
        if (props.Remove(key) && order != null) order.Remove(key);
    }

    public void Remove(JsSymbol sym)
    {
        if (symProps != null) symProps.Remove(sym);
    }

    /// <summary>Enumerable string keys in insertion order (own properties only).</summary>
    public List<string> OwnKeys(bool onlyEnumerable)
    {
        List<string> result = new List<string>();
        if (order != null)
        {
            for (int i = 0; i < order.Count; i++)
            {
                string k = order[i];
                JsProperty p;
                if (props.TryGetValue(k, out p) && (!onlyEnumerable || p.Enumerable))
                    result.Add(k);
            }
        }
        return result;
    }

    public bool OwnHas(string key) { return props != null && props.ContainsKey(key); }
    public bool OwnHas(JsSymbol sym) { return symProps != null && symProps.ContainsKey(sym); }

    // ---- prototype chain lookup ------------------------------------------

    private JsProperty Find(string key)
    {
        JsObject o = this;
        while (o != null)
        {
            JsProperty p;
            if (o.TryGetRaw(key, out p)) return p;
            o = o.Prototype;
        }
        return null;
    }

    private JsProperty Find(JsSymbol sym)
    {
        JsObject o = this;
        while (o != null)
        {
            JsProperty p;
            if (o.TryGetRaw(sym, out p)) return p;
            o = o.Prototype;
        }
        return null;
    }

    /// <summary>Get a property (walking the chain), invoking accessors. Missing -> false.</summary>
    public bool GetProperty(string key, out object value)
    {
        JsProperty p = Find(key);
        if (p == null) { value = JsUndefined.Instance; return false; }
        if (p.Kind == JsPropKind.Accessor && p.Getter != null)
        {
            value = JsValueOps.Active != null
                ? JsValueOps.Active.Call(p.Getter, this, new object[0], false)
                : JsUndefined.Instance;
            return true;
        }
        if (p.Kind == JsPropKind.Accessor)
        {
            value = JsUndefined.Instance;
            return true;
        }
        value = p.Value;
        return true;
    }

    public bool GetProperty(JsSymbol sym, out object value)
    {
        JsProperty p = Find(sym);
        if (p == null) { value = JsUndefined.Instance; return false; }
        if (p.Kind == JsPropKind.Accessor && p.Getter != null)
        {
            value = JsValueOps.Active != null
                ? JsValueOps.Active.Call(p.Getter, this, new object[0], false)
                : JsUndefined.Instance;
            return true;
        }
        if (p.Kind == JsPropKind.Accessor) { value = JsUndefined.Instance; return true; }
        value = p.Value;
        return true;
    }

    public object GetPropertyOrUndefined(string key)
    {
        object v;
        GetProperty(key, out v);
        return v;
    }

    /// <summary>Set a property per ordinary [[Set]] semantics (walking the chain for setters).</summary>
    public void SetProperty(string key, object value)
    {
        JsObject o = this;
        JsProperty p = null;
        while (o != null)
        {
            JsProperty cand;
            if (o.TryGetRaw(key, out cand)) { p = cand; break; }
            o = o.Prototype;
        }

        if (p == null)
        {
            // create on the receiver
            Define(key, JsProperty.Data(value));
            return;
        }

        if (p.Kind == JsPropKind.Accessor)
        {
            if (p.Setter != null && JsValueOps.Active != null)
                JsValueOps.Active.Call(p.Setter, this, new object[] { value }, false);
            return; // no setter -> ignore (sloppy mode)
        }

        if (!p.Writable) return; // silent failure in sloppy mode
        // write to the property's own holder
        JsProperty holder = null;
        JsObject h = this;
        while (h != null)
        {
            JsProperty cand;
            if (h.TryGetRaw(key, out cand)) { holder = cand; break; }
            h = h.Prototype;
        }
        if (holder != null) holder.Value = value;
        else Define(key, JsProperty.Data(value));
    }

    public void SetProperty(JsSymbol sym, object value)
    {
        JsObject o = this;
        JsProperty p = null;
        while (o != null)
        {
            JsProperty cand;
            if (o.TryGetRaw(sym, out cand)) { p = cand; break; }
            o = o.Prototype;
        }
        if (p == null) { Define(sym, JsProperty.Data(value)); return; }
        if (p.Kind == JsPropKind.Accessor)
        {
            if (p.Setter != null && JsValueOps.Active != null)
                JsValueOps.Active.Call(p.Setter, this, new object[] { value }, false);
            return;
        }
        if (!p.Writable) return;
        p.Value = value;
    }

    public bool HasProperty(string key) { return Find(key) != null; }
    public bool HasProperty(JsSymbol sym) { return Find(sym) != null; }

    public bool DeleteProperty(string key)
    {
        JsProperty p;
        if (TryGetRaw(key, out p))
        {
            if (!p.Configurable) return false;
            Remove(key);
            return true;
        }
        return true;
    }

    public bool DeleteProperty(JsSymbol sym)
    {
        JsProperty p;
        if (TryGetRaw(sym, out p))
        {
            if (!p.Configurable) return false;
            Remove(sym);
            return true;
        }
        return true;
    }

    // ---- well-known symbol convenience ------------------------------------

    public bool TryGetWellKnown(string key, out object value)
    {
        JsSymbol sym = JsSymbols.Get(key);
        return GetProperty(sym, out value);
    }

    public void SetWellKnown(string key, object value)
    {
        SetProperty(JsSymbols.Get(key), value);
    }
}

/// <summary>Registry of well-known symbols (singleton per key).</summary>
public static class JsSymbols
{
    private static Dictionary<string, JsSymbol> map = new Dictionary<string, JsSymbol>();
    private static Dictionary<string, JsSymbol> registry = new Dictionary<string, JsSymbol>();
    private static int nextId = 1;

    public static JsSymbol Get(string wellKnownKey)
    {
        JsSymbol s;
        if (map.TryGetValue(wellKnownKey, out s)) return s;
        s = new JsSymbol(wellKnownKey, wellKnownKey.Substring(2));
        map[wellKnownKey] = s;
        return s;
    }

    public static JsSymbol For(string key)
    {
        JsSymbol s;
        if (registry.TryGetValue(key, out s)) return s;
        s = new JsSymbol("sym:" + key, key);
        s.Global = true;
        registry[key] = s;
        return s;
    }

    public static JsSymbol New(string desc)
    {
        JsSymbol s = new JsSymbol("sym#" + (nextId++), desc);
        return s;
    }

    public static string KeyFor(JsSymbol s)
    {
        if (s == null) return null;
        foreach (KeyValuePair<string, JsSymbol> kv in registry)
        {
            if (kv.Value == s) return kv.Key;
        }
        return null;
    }
}

// ---------------------------------------------------------------------------
// Arrays: dense element storage with a derived `length`.
// ---------------------------------------------------------------------------

public sealed class JsArray : JsObject
{
    public List<object> Items = new List<object>();

    public override string ClassTag { get { return "Array"; } }

    public JsArray() { }
    public JsArray(JsObject proto) : base(proto) { }

    public int Length { get { return Items.Count; } }

    public object GetIndex(int index)
    {
        if (index < 0 || index >= Items.Count) return JsUndefined.Instance;
        return Items[index];
    }

    public void SetLength(int length)
    {
        if (length < Items.Count) Items.RemoveRange(length, Items.Count - length);
        else while (Items.Count < length) Items.Add(JsUndefined.Instance);
    }

    public void Push(object v) { Items.Add(v); }

    /// <summary>Own enumerable "index-like" keys plus other string keys.</summary>
    public List<string> IndexKeys()
    {
        List<string> keys = new List<string>();
        for (int i = 0; i < Items.Count; i++) keys.Add(i.ToString());
        List<string> others = OwnKeys(true);
        for (int i = 0; i < others.Count; i++)
        {
            if (!IsArrayIndex(others[i])) keys.Add(others[i]);
        }
        return keys;
    }

    public static bool IsArrayIndex(string key)
    {
        if (key == null || key.Length == 0) return false;
        int n = 0;
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (c < '0' || c > '9') return false;
            n = n * 10 + (c - '0');
            if (n > 1000000) return true; // treat huge as index-like for enumeration
        }
        return true;
    }

    public static long KeyToIndex(string key, out bool ok)
    {
        ok = false;
        if (key == null || key.Length == 0) return -1;
        if (key.Length > 1 && key[0] == '0') return -1;
        long n = 0;
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (c < '0' || c > '9') return -1;
            n = n * 10 + (c - '0');
        }
        ok = true;
        return n;
    }
}

// ---------------------------------------------------------------------------
// Functions: interpreted (AST + closure) or native (C# delegate).
// ---------------------------------------------------------------------------

public delegate object JsNativeFn(JsCallArgs args);

public sealed class JsCallArgs
{
    public object ThisValue;
    public object[] Args;
    public bool IsConstruct;
    public JsInterpreter Interp;

    public int ArgCount { get { return Args != null ? Args.Length : 0; } }

    public object Get(int i)
    {
        if (Args == null || i >= Args.Length) return JsUndefined.Instance;
        return Args[i];
    }

    public string GetString(int i) { return JsValueOps.ToStringValue(Get(i)); }
    public double GetNumber(int i) { return JsValueOps.ToNumber(Get(i)); }
    public bool GetBool(int i) { return JsValueOps.ToBoolean(Get(i)); }
}

public class JsFunction : JsObject
{
    public string Name = "";
    public bool Strict;

    // interpreted body
    public List<JsNode> Params;         // binding patterns
    public JsNode Body;                 // JsBlock (statements) or expression (arrows)
    public bool IsExpressionBody;       // arrow with concise body
    public JsScope Closure;
    public bool IsArrow;
    public bool IsMethod;               // [[HomeObject]] set (for super)
    public bool IsGenerator;            // parsed but unsupported at runtime
    public JsObject HomeObject;         // receiver for super property access
    public JsNode RestParam;            // trailing rest binding pattern (already in Params as marker too)

    // class construction
    public JsFunction SuperConstructor; // base constructor for a derived class
    public bool IsDefaultDerived;       // synthesised derived ctor (implicit super(...))

    // arrow functions capture the enclosing execution context lexically
    public object CapturedThis;
    public JsObject CapturedHome;
    public JsFunction CapturedSuper;

    public bool IsAsync;                // parsed; runs synchronously (async unsupported)

    // native body
    public JsNativeFn Native;

    public override string ClassTag { get { return "Function"; } }

    public JsFunction() { Params = new List<JsNode>(); }
    public JsFunction(JsObject proto) : base(proto) { Params = new List<JsNode>(); }

    public bool IsNative { get { return Native != null; } }

    /// <summary>[[Construct]] availability: arrows and natives flagged non-constructable.</summary>
    public bool Constructable = true;

    public int ParamCount { get { return Params != null ? Params.Count : 0; } }
}

/// <summary>A bound function (Function.prototype.bind result).</summary>
public sealed class JsBoundFunction : JsFunction
{
    public JsFunction Target;
    public object BoundThis;
    public object[] BoundArgs;
}

/// <summary>Iterator object used by for-of / spread / destructuring.</summary>
public sealed class JsIterator : JsObject
{
    public JsFunction NextFn;
    public JsObject SourceObj;
    public bool Done;
}

/// <summary>Date object (holds a timestamp; drives the default ToPrimitive hint).</summary>
public sealed class JsDate : JsObject
{
    public double Time;

    public override string ClassTag { get { return "Date"; } }

    public JsDate() { }
    public JsDate(JsObject proto, double time) : base(proto) { Time = time; }
}

/// <summary>Map instance: keys stored in insertion order (identity for objects).</summary>
public sealed class JsMapObject : JsObject
{
    public List<object> Keys = new List<object>();
    public List<object> Values = new List<object>();

    public override string ClassTag { get { return "Map"; } }

    public JsMapObject() { }
    public JsMapObject(JsObject proto) : base(proto) { }

    public int Count { get { return Keys.Count; } }

    public int IndexOfKey(object key)
    {
        for (int i = 0; i < Keys.Count; i++)
        {
            if (JsValueOps.SameValueZero(Keys[i], key)) return i;
        }
        return -1;
    }

    public void SetKey(object key, object value)
    {
        int idx = IndexOfKey(key);
        if (idx >= 0) { Values[idx] = value; return; }
        Keys.Add(key);
        Values.Add(value);
    }

    public bool RemoveKey(object key)
    {
        int idx = IndexOfKey(key);
        if (idx < 0) return false;
        Keys.RemoveAt(idx);
        Values.RemoveAt(idx);
        return true;
    }
}

/// <summary>Set instance: unique values in insertion order.</summary>
public sealed class JsSetObject : JsObject
{
    public List<object> Items = new List<object>();

    public override string ClassTag { get { return "Set"; } }

    public JsSetObject() { }
    public JsSetObject(JsObject proto) : base(proto) { }

    public int Count { get { return Items.Count; } }

    public int IndexOf(object v)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (JsValueOps.SameValueZero(Items[i], v)) return i;
        }
        return -1;
    }

    public bool AddValue(object v)
    {
        if (IndexOf(v) >= 0) return false;
        Items.Add(v);
        return true;
    }

    public bool RemoveValue(object v)
    {
        int idx = IndexOf(v);
        if (idx < 0) return false;
        Items.RemoveAt(idx);
        return true;
    }
}

/// <summary>RegExp instance wrapper (holds the compiled JsRegExp).</summary>
public sealed class JsRegExpObject : JsObject
{
    public JsRegExp Value;

    public override string ClassTag { get { return "RegExp"; } }

    public JsRegExpObject() { }
    public JsRegExpObject(JsObject proto, JsRegExp re) : base(proto) { Value = re; RegExpValue = re; }
}
