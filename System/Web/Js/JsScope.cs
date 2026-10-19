using System;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// Lexical environments. A scope chain provides variable resolution for
// closures. `var` and function declarations bind in the nearest *function*
// scope; `let`/`const` bind in the current *block* scope (with a temporal dead
// zone). The outermost scope is the global object, so top-level variables are
// literally properties of `window`.
// ---------------------------------------------------------------------------

public sealed class JsBinding
{
    public object Value;
    public bool Const;
    public bool TDZ;          // declared but not yet initialised (let/const)
    public bool FunctionDecl; // hoisted function declaration
}

public class JsScope
{
    public JsScope Parent;
    public Dictionary<string, JsBinding> Vars;
    public bool IsFunction;      // `var`/function-declaration target
    public bool IsBlock;         // let/const target
    public JsScope FunctionScope; // nearest function/global scope (for var hoisting)

    // global scope: variables live on the global object instead of `Vars`
    public bool IsGlobal;
    public JsObject GlobalObject;

    public JsScope() { Vars = new Dictionary<string, JsBinding>(); }

    public static JsScope CreateGlobal(JsObject globalObj)
    {
        return new JsScope
        {
            IsGlobal = true,
            IsFunction = true,
            GlobalObject = globalObj,
            FunctionScope = null,
            Parent = null,
        };
    }

    public JsScope PushBlock()
    {
        JsScope s = new JsScope { IsBlock = true, Parent = this, FunctionScope = FunctionScope ?? (IsFunction ? this : null) };
        if (IsGlobal) s.FunctionScope = this;
        return s;
    }

    public JsScope PushFunction()
    {
        return new JsScope { IsFunction = true, Parent = this, FunctionScope = null };
    }

    // ---- declaration -----------------------------------------------------

    /// <summary>Declare `var`/function in the nearest function scope (hoisting).</summary>
    public void DeclareVar(string name, object value, bool isFunctionDecl)
    {
        JsScope target = FunctionScope ?? (IsFunction || IsGlobal ? this : this);
        // walk to nearest function scope
        JsScope s = this;
        while (s != null && !s.IsFunction && !s.IsGlobal) s = s.FunctionScope ?? s.Parent;
        if (s != null) target = s;

        if (target.IsGlobal)
        {
            if (!target.GlobalObject.OwnHas(name))
                target.GlobalObject.Define(name, JsProperty.Data(value));
            else
                target.GlobalObject.SetProperty(name, value);
            return;
        }

        JsBinding b;
        if (!target.Vars.TryGetValue(name, out b))
        {
            target.Vars[name] = new JsBinding { Value = value, FunctionDecl = isFunctionDecl };
        }
        else
        {
            // execution-time var/param initializer always assigns; hoisting
            // (HoistVar) only creates the binding as undefined beforehand.
            b.Value = value;
            if (isFunctionDecl) { b.FunctionDecl = true; b.TDZ = false; }
        }
    }

    /// <summary>Declare let/const/class in the current (block) scope.</summary>
    public void DeclareLet(string name, object value, bool isConst)
    {
        if (IsGlobal)
        {
            if (!GlobalObject.OwnHas(name)) GlobalObject.Define(name, JsProperty.Data(value));
            else GlobalObject.SetProperty(name, value);
            return;
        }
        JsBinding b;
        if (Vars.TryGetValue(name, out b))
        {
            b.Value = value;
            b.Const = isConst;
            b.TDZ = false;
        }
        else
        {
            Vars[name] = new JsBinding { Value = value, Const = isConst, TDZ = false };
        }
    }

    /// <summary>Pre-declare a let/const name in TDZ state (hoisting within a block).</summary>
    public void HoistLet(string name, bool isConst)
    {
        if (IsGlobal) return;
        if (Vars.ContainsKey(name)) return;
        Vars[name] = new JsBinding { Value = JsUndefined.Instance, Const = isConst, TDZ = true };
    }

    public void HoistVar(string name)
    {
        JsScope s = this;
        while (s != null && !s.IsFunction && !s.IsGlobal) s = s.FunctionScope ?? s.Parent;
        if (s == null) return;
        if (s.IsGlobal)
        {
            if (!s.GlobalObject.OwnHas(name)) s.GlobalObject.Define(name, JsProperty.Data(JsUndefined.Instance));
            return;
        }
        if (!s.Vars.ContainsKey(name))
            s.Vars[name] = new JsBinding { Value = JsUndefined.Instance };
    }

    // ---- resolution ------------------------------------------------------

    /// <summary>Find the scope owning `name`, walking the chain. Returns null if unbound.</summary>
    public JsScope ResolveScope(string name)
    {
        if (IsGlobal)
        {
            if (GlobalObject.OwnHas(name)) return this;
            // fall through to parent (there is none) -> treat global object props as always resolvable
            return null;
        }
        JsScope s = this;
        while (s != null)
        {
            if (s.IsGlobal)
            {
                if (s.GlobalObject.OwnHas(name)) return s;
                return null; // global scope is the root; missing => ReferenceError
            }
            if (s.Vars.ContainsKey(name)) return s;
            s = s.Parent;
        }
        return null;
    }

    /// <summary>Global object property present? (global vars are window props).</summary>
    public bool HasVariable(string name)
    {
        if (IsGlobal) return GlobalObject.OwnHas(name);
        JsScope s = this;
        while (s != null)
        {
            if (s.IsGlobal) return s.GlobalObject.OwnHas(name);
            if (s.Vars.ContainsKey(name)) return true;
            s = s.Parent;
        }
        return false;
    }

    public object GetVariable(string name)
    {
        if (IsGlobal)
        {
            object v;
            if (GlobalObject.GetProperty(name, out v)) return v;
            throw JsErr.Ref(name + " is not defined");
        }
        JsScope s = this;
        while (s != null)
        {
            if (s.IsGlobal)
            {
                object v;
                if (s.GlobalObject.GetProperty(name, out v)) return v;
                throw JsErr.Ref(name + " is not defined");
            }
            JsBinding b;
            if (s.Vars.TryGetValue(name, out b))
            {
                if (b.TDZ) throw JsErr.Ref("Cannot access '" + name + "' before initialization");
                return b.Value;
            }
            s = s.Parent;
        }
        throw JsErr.Ref(name + " is not defined");
    }

    /// <summary>Assign to an existing binding. Returns false if not found anywhere.</summary>
    public bool SetVariable(string name, object value)
    {
        if (IsGlobal)
        {
            if (GlobalObject.OwnHas(name)) { GlobalObject.SetProperty(name, value); return true; }
            return false;
        }
        JsScope s = this;
        while (s != null)
        {
            if (s.IsGlobal)
            {
                if (s.GlobalObject.OwnHas(name)) { s.GlobalObject.SetProperty(name, value); return true; }
                return false;
            }
            JsBinding b;
            if (s.Vars.TryGetValue(name, out b))
            {
                if (b.TDZ) throw JsErr.Ref("Cannot access '" + name + "' before initialization");
                if (b.Const) throw JsErr.Type("Assignment to constant variable '" + name + "'");
                b.Value = value;
                return true;
            }
            s = s.Parent;
        }
        return false;
    }

    /// <summary>Assign or create a global (for undeclared assignment in sloppy mode).</summary>
    public void SetOrCreateGlobal(string name, object value)
    {
        JsScope s = this;
        while (s != null && !s.IsGlobal) s = s.Parent;
        if (s != null && s.IsGlobal)
        {
            s.GlobalObject.SetProperty(name, value);
            return;
        }
        // no global scope (shouldn't happen): just define locally
        SetVariable(name, value);
    }
}
