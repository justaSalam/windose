using System;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// The realm: the set of intrinsics (prototypes + constructors + global) a
// script sees. JsBuiltins.Install populates it.
// ---------------------------------------------------------------------------

public sealed class JsRealm
{
    // prototypes
    public JsObject ObjectPrototype;
    public JsObject FunctionPrototype;
    public JsArray ArrayPrototype;
    public JsObject StringPrototype, NumberPrototype, BooleanPrototype, SymbolPrototype;
    public JsObject ErrorPrototype, TypeErrorPrototype, RangeErrorPrototype, SyntaxErrorPrototype;
    public JsObject ReferenceErrorPrototype, EvalErrorPrototype, URIErrorPrototype;
    public JsObject RegExpPrototype, DatePrototype, MapPrototype, SetPrototype, PromisePrototype;
    public JsObject IteratorPrototype;
    public JsArray IteratorPrototypeArray; // unused placeholder

    // browser globals (JsBrowser): response/XHR prototypes + shared singletons
    public JsObject ResponsePrototype, XhrPrototype;
    public JsObject LocationObj, NavigatorObj;
    // published by JsDom.Install (XHR events need it even before listeners exist)
    public JsObject EventPrototype;

    // namespace objects + global
    public JsObject Global;
    public JsObject MathObj, JsonObj, ConsoleObj, ReflectObj, AtomicsObj;

    // constructors
    public JsFunction ObjectCtor, FunctionCtor, ArrayCtor, StringCtor, NumberCtor, BooleanCtor;
    public JsFunction SymbolCtor, ErrorCtor, TypeErrorCtor, RangeErrorCtor, SyntaxErrorCtor;
    public JsFunction ReferenceErrorCtor, EvalErrorCtor, URICtor, RegExpCtor, DateCtor;
    public JsFunction MapCtor, SetCtor, PromiseCtor, ReflectObjCtor, ProxyCtor;
    public JsFunction FunctionCallMethod, FunctionApplyMethod;

    public JsInterpreter Interp;

    public JsObject NewObject() { return new JsObject(ObjectPrototype); }
    public JsObject NewObject(JsObject proto) { return new JsObject(proto != null ? proto : ObjectPrototype); }
    public JsArray NewArray() { return new JsArray(ArrayPrototype); }

    public JsArray NewArray(List<object> items)
    {
        JsArray a = new JsArray(ArrayPrototype);
        if (items != null) a.Items = items;
        return a;
    }

    public JsFunction MakeMethod(string name, JsNativeFn fn, int length)
    {
        JsFunction f = new JsFunction(FunctionPrototype);
        f.Name = name;
        f.Native = fn;
        f.Constructable = false;
        f.Define("length", JsProperty.DataNonEnum(length));
        f.Define("name", JsProperty.DataNonEnum(name));
        f.Define("length", JsProperty.DataNonEnum(length));
        return f;
    }

    public JsFunction MakeCtor(string name, JsNativeFn fn, int length, JsObject prototype)
    {
        JsFunction f = new JsFunction(FunctionPrototype);
        f.Name = name;
        f.Native = fn;
        f.Constructable = true;
        f.Define("length", JsProperty.DataNonEnum(length));
        f.Define("name", JsProperty.DataNonEnum(name));
        if (prototype != null)
        {
            prototype.Define("constructor", JsProperty.DataNonEnum(f));
            f.Define("prototype", JsProperty.Data(prototype));
        }
        return f;
    }

    public static JsRealm Create(JsInterpreter interp)
    {
        JsRealm r = new JsRealm();
        r.Interp = interp;
        JsBuiltins.Install(r);
        return r;
    }
}

// ---------------------------------------------------------------------------
// The tree-walking interpreter.
// ---------------------------------------------------------------------------

public enum CompType : byte { Normal, Return, Break, Continue }

public struct Completion
{
    public CompType Type;
    public object Value;
    public string Label;

    public static Completion Norm() { return new Completion { Type = CompType.Normal }; }
    public static Completion Ret(object v) { return new Completion { Type = CompType.Return, Value = v }; }
    public static Completion Brk(string label) { return new Completion { Type = CompType.Break, Label = label }; }
    public static Completion Cont(string label) { return new Completion { Type = CompType.Continue, Label = label }; }
}

public enum BindMode : byte { Var, Let, Const, Assign, Param }

public class JsInterpreter
{
    public JsRealm Realm;
    public JsScope GlobalScope;
    public JsDom Dom;

    // execution context (saved/restored around calls)
    private object _this;
    private JsObject _home;
    private JsFunction _superCtor;

    // step budget to stop runaway scripts from hanging the OS
    private int _steps;
    public int StepLimit = 20000000;
    public void ResetSteps() { _steps = 0; }

    // completion value of the last expression statement (eval/repl semantics)
    private object _lastValue = JsUndefined.Instance;

    // browser event loop: timers, Promise microtasks and the fetch queue
    public JsEventLoop Loop;

    public JsEventLoop EnsureLoop()
    {
        if (Loop == null)
        {
            Loop = new JsEventLoop();
            Loop.Interp = this;
        }
        return Loop;
    }

    /// <summary>Run queued Promise reactions. The host calls this whenever the
    /// JS stack is empty (after scripts, events, timers, network results).</summary>
    public void DrainJobs()
    {
        JsValueOps.Active = this;
        EnsureLoop().DrainJobs();
    }

    // Labels are attached by a Labeled statement to its body only; a loop/block
    // that owns a label passes this empty list to its nested children so an inner
    // loop never mistakes an outer label for its own (continue outer; etc).
    private static readonly List<string> NoLabels = new List<string>();

    // ---- setup ------------------------------------------------------------

    public JsInterpreter()
    {
        GlobalScope = null;
    }

    public void InitGlobal()
    {
        JsObject global = new JsObject(null);
        Realm = new JsRealm();
        Realm.Interp = this;
        // Global must exist BEFORE builtins run (they populate realm.Global)
        Realm.Global = global;
        JsBuiltins.Install(Realm);
        GlobalScope = JsScope.CreateGlobal(global);
        JsValueOps.Active = this;
        // install `globalThis` and window alias after builtins populated global
        global.Define("globalThis", JsProperty.Data(global));
        global.Define("window", JsProperty.Data(global));
        global.Define("self", JsProperty.Data(global));
    }

    // ---- public entry points ----------------------------------------------

    public object RunProgram(JsProgram prog)
    {
        _steps = 0;
        _lastValue = JsUndefined.Instance;
        HoistTop(prog.Body, GlobalScope);
        Completion c = ExecStatements(prog.Body, GlobalScope, null);
        if (c.Type == CompType.Return) return c.Value;
        return _lastValue;
    }

    public object Call(object fnObj, object thisVal, object[] args, bool construct)
    {
        JsFunction fn = fnObj as JsFunction;
        if (fn == null) throw JsErr.Type("value is not a function");

        if (fn is JsBoundFunction)
        {
            JsBoundFunction b = (JsBoundFunction)fn;
            object[] merged = new object[(b.BoundArgs != null ? b.BoundArgs.Length : 0) + (args != null ? args.Length : 0)];
            int k = 0;
            if (b.BoundArgs != null) for (int i = 0; i < b.BoundArgs.Length; i++) merged[k++] = b.BoundArgs[i];
            if (args != null) for (int i = 0; i < args.Length; i++) merged[k++] = args[i];
            return Call(b.Target, b.BoundThis, merged, construct);
        }

        if (construct && !fn.Constructable) throw JsErr.Type("not a constructor");

        object prevThis = _this;
        JsObject prevHome = _home;
        JsFunction prevSuper = _superCtor;

        if (fn.IsArrow)
        {
            _this = fn.CapturedThis;
            _home = fn.CapturedHome;
            _superCtor = fn.CapturedSuper;
        }
        else
        {
            _this = thisVal;
            _home = fn.HomeObject;
            _superCtor = fn.SuperConstructor;
        }
        if (construct && fn.IsNative) _this = null;

        try
        {
            if (fn.Native != null)
            {
                JsCallArgs ca = new JsCallArgs { ThisValue = _this, Args = args, IsConstruct = construct, Interp = this };
                object r = fn.Native(ca);
                if (construct) return NewCoerce(r, _this, fn);
                return r;
            }

            if (fn.IsAsync)
            {
                if (construct) throw JsErr.Type(fn.Name + " is not a constructor");
                JsEventLoop loop = EnsureLoop();
                JsPromise retPromise = new JsPromise(Realm.PromisePrototype);

                try
                {
                    JsScope asyncScope = fn.Closure != null ? fn.Closure.PushFunction() : new JsScope { IsFunction = true };
                    if (!fn.IsArrow)
                    {
                        JsArray argsObj = Realm.NewArray(args != null ? new List<object>(args) : new List<object>());
                        argsObj.SetProperty("callee", fn);
                        asyncScope.DeclareVar("arguments", argsObj, true);
                    }
                    BindParams(asyncScope, fn, args);
                    if (fn.Body != null) HoistDeclarations(asyncScope, fn.Body);

                    Completion ac;
                    if (fn.IsExpressionBody)
                    {
                        ac = Completion.Ret(EvalExpr(fn.Body, asyncScope));
                    }
                    else
                    {
                        JsBlock blk = fn.Body as JsBlock;
                        ac = ExecStatements(blk != null ? blk.Body : new List<JsNode>(), asyncScope, null);
                    }
                    object aResult = ac.Type == CompType.Return ? ac.Value : JsUndefined.Instance;
                    JsBrowser.FulfillPromise(loop, retPromise, aResult);
                }
                catch (JsThrown jt)
                {
                    JsBrowser.RejectPromise(loop, retPromise, jt.Value);
                }
                catch (Exception ex)
                {
                    JsBrowser.RejectPromise(loop, retPromise, JsBuiltins.CreateErrorObject(Realm, "Error", ex.Message));
                }

                return retPromise;
            }

            JsScope scope = fn.Closure != null ? fn.Closure.PushFunction() : new JsScope { IsFunction = true };
            if (!fn.IsArrow)
            {
                JsArray argsObj = Realm.NewArray(args != null ? new List<object>(args) : new List<object>());
                argsObj.SetProperty("callee", fn);
                scope.DeclareVar("arguments", argsObj, true);
            }
            BindParams(scope, fn, args);
            if (fn.Body != null) HoistDeclarations(scope, fn.Body);

            Completion c;
            if (fn.IsExpressionBody)
            {
                c = Completion.Ret(EvalExpr(fn.Body, scope));
            }
            else
            {
                JsBlock blk = fn.Body as JsBlock;
                c = ExecStatements(blk != null ? blk.Body : new List<JsNode>(), scope, null);
            }
            object result = c.Type == CompType.Return ? c.Value : JsUndefined.Instance;
            if (construct) return NewCoerce(result, _this, fn);
            return result;
        }
        finally
        {
            _this = prevThis;
            _home = prevHome;
            _superCtor = prevSuper;
        }
    }

    private object NewCoerce(object result, object newThis, JsFunction fn)
    {
        if (result is JsObject) return result;
        return newThis != null ? newThis : result;
    }

    // ---- hoisting ---------------------------------------------------------

    private void HoistTop(List<JsNode> body, JsScope scope)
    {
        HoistScan(body, scope, true);
    }

    private void HoistDeclarations(JsScope scope, JsNode body)
    {
        JsBlock blk = body as JsBlock;
        if (blk != null) HoistScan(blk.Body, scope, false);
    }

    private void HoistScan(List<JsNode> stmts, JsScope scope, bool top)
    {
        if (stmts == null) return;
        for (int i = 0; i < stmts.Count; i++) HoistNode(stmts[i], scope);
    }

    private void HoistNode(JsNode s, JsScope scope)
    {
        if (s == null) return;
        switch (s.Kind)
        {
            case JsKind.VarDecl:
                {
                    JsVarDecl vd = (JsVarDecl)s;
                    if (vd.DeclKind != JsDeclKind.Var) return; // let/const are block-scoped, not hoisted to fn
                    for (int i = 0; i < vd.Decls.Count; i++)
                        HoistPattern(((JsDeclarator)vd.Decls[i]).Target, scope);
                    return;
                }
            case JsKind.FunctionDecl:
                {
                    JsFunctionDecl fd = (JsFunctionDecl)s;
                    JsFunction f = MakeFunctionFromNode(fd.Fn, scope);
                    scope.DeclareVar(fd.Fn.Name, f, true);
                    return;
                }
            case JsKind.Block:
                HoistScan(((JsBlock)s).Body, scope, false);
                return;
            case JsKind.If:
                {
                    JsIf n = (JsIf)s;
                    HoistNode(n.Cons, scope);
                    HoistNode(n.Alt, scope);
                    return;
                }
            case JsKind.While: HoistNode(((JsWhile)s).Body, scope); return;
            case JsKind.DoWhile: HoistNode(((JsDoWhile)s).Body, scope); return;
            case JsKind.For:
                {
                    JsFor n = (JsFor)s;
                    if (n.Init != null && n.Init.Kind == JsKind.VarDecl) HoistNode(n.Init, scope);
                    HoistNode(n.Body, scope);
                    return;
                }
            case JsKind.ForIn:
            case JsKind.ForOf:
                {
                    JsForIn n = (JsForIn)s;
                    if (n.Left != null && n.Left.Kind == JsKind.VarDecl) HoistNode(n.Left, scope);
                    HoistNode(n.Body, scope);
                    return;
                }
            case JsKind.Switch:
                {
                    JsSwitch n = (JsSwitch)s;
                    for (int i = 0; i < n.Cases.Count; i++) HoistScan(((JsSwitchCase)n.Cases[i]).Body, scope, false);
                    return;
                }
            case JsKind.Try:
                {
                    JsTry n = (JsTry)s;
                    HoistNode(n.Body, scope);
                    if (n.Handler != null) HoistNode(((JsCatch)n.Handler).Block, scope);
                    if (n.Finalizer != null) HoistNode(n.Finalizer, scope);
                    return;
                }
            case JsKind.Labeled: HoistNode(((JsLabeled)s).Body, scope); return;
        }
    }

    private void HoistPattern(JsNode pattern, JsScope scope)
    {
        if (pattern == null) return;
        switch (pattern.Kind)
        {
            case JsKind.Identifier:
                scope.HoistVar(((JsIdentifier)pattern).Name);
                return;
            case JsKind.AssignPat:
                HoistPattern(((JsAssignPat)pattern).Target, scope);
                return;
            case JsKind.ObjectPat:
                {
                    JsObjectPat p = (JsObjectPat)pattern;
                    for (int i = 0; i < p.Props.Count; i++) HoistPattern(((JsPatProp)p.Props[i]).Value, scope);
                    if (p.Rest != null) HoistPattern(p.Rest, scope);
                    return;
                }
            case JsKind.ArrayPat:
                {
                    JsArrayPat p = (JsArrayPat)pattern;
                    for (int i = 0; i < p.Elements.Count; i++) if (p.Elements[i] != null) HoistPattern(p.Elements[i], scope);
                    if (p.Rest != null) HoistPattern(p.Rest, scope);
                    return;
                }
        }
    }

    private JsFunction MakeFunctionFromNode(JsFunctionNode node, JsScope closure)
    {
        JsFunction f = new JsFunction(Realm.FunctionPrototype);
        f.Name = node.Name ?? "";
        f.Params = node.Params;
        f.Body = node.Body;
        f.IsExpressionBody = node.IsExpressionBody;
        f.Closure = closure;
        f.IsArrow = node.Kind == JsKind.Arrow;
        f.IsMethod = node.IsMethod;
        f.IsGenerator = node.IsGenerator;
        f.IsAsync = node.IsAsync;
        f.Constructable = !f.IsArrow && !f.IsAsync;
        if (!f.IsArrow)
        {
            // give normal functions a .prototype object
            if (f.Params != null && f.Params.Count > 0 && IsLikelyCtorName(f.Name))
            {
            }
            JsObject proto = new JsObject(Realm.ObjectPrototype);
            f.Define("prototype", JsProperty.Data(proto));
            proto.Define("constructor", JsProperty.DataNonEnum(f));
        }
        if (f.IsArrow)
        {
            f.CapturedThis = _this;
            f.CapturedHome = _home;
            f.CapturedSuper = _superCtor;
        }
        return f;
    }

    private static bool IsLikelyCtorName(string n) { return n != null && n.Length > 0; }

    /// <summary>Public wrapper used by Function() and eval to wrap an AST node in a callable.</summary>
    public JsFunction MakeFunctionPublic(JsFunctionNode node, JsScope closure)
    {
        return MakeFunctionFromNode(node, closure);
    }

    // ---- parameters -------------------------------------------------------

    private void BindParams(JsScope scope, JsFunction fn, object[] args)
    {
        if (fn.Params == null) return;
        int alen = args != null ? args.Length : 0;
        int i = 0;
        for (int pi = 0; pi < fn.Params.Count; pi++)
        {
            JsNode param = fn.Params[pi];
            if (param.Kind == JsKind.RestPat)
            {
                List<object> rest = new List<object>();
                for (int k = i; k < alen; k++) rest.Add(args[k]);
                BindPattern(scope, ((JsRestPat)param).Value, Realm.NewArray(rest), BindMode.Param);
                i = alen;
                break;
            }
            object val = i < alen ? args[i] : JsUndefined.Instance;
            i++;
            BindPattern(scope, param, val, BindMode.Param);
        }
    }

    // ---- pattern binding --------------------------------------------------

    public void BindPattern(JsScope scope, JsNode pattern, object value, BindMode mode)
    {
        switch (pattern.Kind)
        {
            case JsKind.Identifier:
                BindName(scope, ((JsIdentifier)pattern).Name, value, mode);
                return;
            case JsKind.Member:
                if (mode == BindMode.Assign)
                {
                    object recv; bool sc;
                    // compute the base object for the member
                    JsMember m = (JsMember)pattern;
                    object baseVal = EvalExpr(m.Object, scope);
                    object key = m.Computed ? EvalExpr(m.Property, scope) : (object)((JsIdentifier)m.Property).Name;
                    SetMember(baseVal, key, value);
                }
                return;
            case JsKind.AssignPat:
                {
                    JsAssignPat ap = (JsAssignPat)pattern;
                    if (IsUndefined(value)) value = EvalExpr(ap.Default, scope);
                    BindPattern(scope, ap.Target, value, mode);
                    return;
                }
            case JsKind.ObjectPat:
                {
                    JsObjectPat op = (JsObjectPat)pattern;
                    List<string> used = op.Rest != null ? new List<string>() : null;
                    for (int i = 0; i < op.Props.Count; i++)
                    {
                        JsPatProp pp = (JsPatProp)op.Props[i];
                        object key = PropKey(pp.Key, scope);
                        string keyStr = key as string;
                        if (used != null && keyStr != null) used.Add(keyStr);
                        object v;
                        GetMember(value, key, out v);
                        BindPattern(scope, pp.Value, v, mode);
                    }
                    if (op.Rest != null)
                    {
                        JsArray rest = Realm.NewArray();
                        List<string> keys = OwnEnumerableKeys(value);
                        for (int i = 0; i < keys.Count; i++)
                            if (used == null || !used.Contains(keys[i])) rest.Push(GetMemberDirect(value, keys[i]));
                        BindPattern(scope, op.Rest, rest, mode);
                    }
                    return;
                }
            case JsKind.ArrayPat:
                {
                    JsArrayPat ap = (JsArrayPat)pattern;
                    List<object> list = ToList(value);
                    for (int i = 0; i < ap.Elements.Count; i++)
                    {
                        JsNode el = ap.Elements[i];
                        if (el == null) continue;
                        object v = i < list.Count ? list[i] : JsUndefined.Instance;
                        BindPattern(scope, el, v, mode);
                    }
                    if (ap.Rest != null)
                    {
                        JsArray rest = Realm.NewArray();
                        for (int i = ap.Elements.Count; i < list.Count; i++) rest.Push(list[i]);
                        BindPattern(scope, ap.Rest, rest, mode);
                    }
                    return;
                }
            case JsKind.RestPat:
                BindPattern(scope, ((JsRestPat)pattern).Value, value, mode);
                return;
        }
    }

    private void BindName(JsScope scope, string name, object value, BindMode mode)
    {
        switch (mode)
        {
            case BindMode.Var: scope.DeclareVar(name, value, false); return;
            case BindMode.Param: scope.DeclareVar(name, value, false); return;
            case BindMode.Let: scope.DeclareLet(name, value, false); return;
            case BindMode.Const: scope.DeclareLet(name, value, true); return;
            default:
                if (!scope.SetVariable(name, value)) scope.SetOrCreateGlobal(name, value);
                return;
        }
    }

    private object PropKey(JsNode keyNode, JsScope scope)
    {
        if (keyNode == null) return "";
        if (keyNode.Kind == JsKind.Literal)
        {
            JsLiteral lit = (JsLiteral)keyNode;
            if (lit.Value is string) return (string)lit.Value;
            if (lit.Value is double) return JsValueOps.NumberToString((double)lit.Value);
            return JsValueOps.ToStringValue(lit.Value);
        }
        if (keyNode.Kind == JsKind.Identifier) return ((JsIdentifier)keyNode).Name;
        return ToPropertyKey(EvalExpr(keyNode, scope));
    }

    private static bool IsUndefined(object v) { return v is JsUndefined; }

    // ---- statements -------------------------------------------------------

    public Completion ExecStatements(List<JsNode> stmts, JsScope scope, List<string> labels)
    {
        for (int i = 0; i < stmts.Count; i++)
        {
            Completion c = ExecStmt(stmts[i], scope, labels);
            if (c.Type != CompType.Normal) return c;
        }
        return Completion.Norm();
    }

    public Completion ExecStmt(JsNode s, JsScope scope, List<string> labels)
    {
        if (++_steps > StepLimit) throw new Exception("Script exceeded step limit");
        switch (s.Kind)
        {
            case JsKind.Empty:
            case JsKind.Debugger:
                return Completion.Norm();

            case JsKind.ExprStmt:
                _lastValue = EvalExpr(((JsExprStmt)s).Expression, scope);
                return Completion.Norm();

            case JsKind.VarDecl:
                return ExecVarDecl((JsVarDecl)s, scope);

            case JsKind.FunctionDecl:
                {
                    JsFunctionDecl fd = (JsFunctionDecl)s;
                    // already hoisted; re-declare in current scope for correctness
                    JsFunction f = MakeFunctionFromNode(fd.Fn, scope);
                    scope.DeclareVar(fd.Fn.Name, f, true);
                    return Completion.Norm();
                }

            case JsKind.ClassDecl:
                {
                    JsClassDecl cd = (JsClassDecl)s;
                    object cls = BuildClass(cd.Class, scope, ((JsClassDecl)s).Class.Name);
                    scope.DeclareLet(cd.Class.Name, cls, false);
                    return Completion.Norm();
                }

            case JsKind.Block:
                {
                    JsScope inner = scope.PushBlock();
                    HoistLetScan(((JsBlock)s).Body, inner);
                    return ExecStatements(((JsBlock)s).Body, inner, NoLabels);
                }

            case JsKind.If:
                {
                    JsIf n = (JsIf)s;
                    if (JsValueOps.ToBoolean(EvalExpr(n.Test, scope)))
                        return ExecStmt(n.Cons, scope, NoLabels);
                    if (n.Alt != null) return ExecStmt(n.Alt, scope, NoLabels);
                    return Completion.Norm();
                }

            case JsKind.While:
                {
                    JsWhile n = (JsWhile)s;
                    while (JsValueOps.ToBoolean(EvalExpr(n.Test, scope)))
                    {
                        Completion c = RunLoopBody(n.Body, scope, NoLabels);
                        if (c.Type == CompType.Break) { if (c.Label == null || HasLabel(labels, c.Label)) break; return c; }
                        if (c.Type == CompType.Return) return c;
                        if (c.Type == CompType.Continue) { if (c.Label != null && !HasLabel(labels, c.Label)) return c; }
                    }
                    return Completion.Norm();
                }

            case JsKind.DoWhile:
                {
                    JsDoWhile n = (JsDoWhile)s;
                    do
                    {
                        Completion c = RunLoopBody(n.Body, scope, NoLabels);
                        if (c.Type == CompType.Break) { if (c.Label == null || HasLabel(labels, c.Label)) break; return c; }
                        if (c.Type == CompType.Return) return c;
                        if (c.Type == CompType.Continue) { if (c.Label != null && !HasLabel(labels, c.Label)) return c; }
                    } while (JsValueOps.ToBoolean(EvalExpr(n.Test, scope)));
                    return Completion.Norm();
                }

            case JsKind.For:
                return ExecFor((JsFor)s, scope, labels);

            case JsKind.ForIn:
            case JsKind.ForOf:
                return ExecForInOf(s, scope, labels);

            case JsKind.Switch:
                return ExecSwitch((JsSwitch)s, scope, labels);

            case JsKind.Try:
                return ExecTry((JsTry)s, scope, labels);

            case JsKind.Throw:
                {
                    object v = EvalExpr(((JsThrow)s).Argument, scope);
                    throw new JsThrown(v, JsValueOps.ToStringValue(v));
                }

            case JsKind.Return:
                {
                    JsReturn r = (JsReturn)s;
                    object v = r.Argument != null ? EvalExpr(r.Argument, scope) : JsUndefined.Instance;
                    return Completion.Ret(v);
                }

            case JsKind.Break: return Completion.Brk(((JsBreak)s).Label);
            case JsKind.Continue: return Completion.Cont(((JsContinue)s).Label);

            case JsKind.Labeled:
                {
                    JsLabeled l = (JsLabeled)s;
                    List<string> bodyLabels = CloneAppend(labels, l.Label);
                    Completion c = ExecStmt(l.Body, scope, bodyLabels);
                    if (c.Type == CompType.Break && c.Label == l.Label) return Completion.Norm();
                    return c;
                }
        }
        return Completion.Norm();
    }

    private Completion RunLoopBody(JsNode body, JsScope scope, List<string> labels)
    {
        return ExecStmt(body, scope, labels);
    }

    private static bool HasLabel(List<string> labels, string label)
    {
        if (labels == null) return false;
        for (int i = 0; i < labels.Count; i++) if (labels[i] == label) return true;
        return false;
    }

    private static List<string> CloneAppend(List<string> labels, string label)
    {
        List<string> r = new List<string>();
        if (labels != null) for (int i = 0; i < labels.Count; i++) r.Add(labels[i]);
        r.Add(label);
        return r;
    }

    private Completion ExecVarDecl(JsVarDecl vd, JsScope scope)
    {
        BindMode mode = vd.DeclKind == JsDeclKind.Var ? BindMode.Var
            : vd.DeclKind == JsDeclKind.Let ? BindMode.Let : BindMode.Const;
        for (int i = 0; i < vd.Decls.Count; i++)
        {
            JsDeclarator d = (JsDeclarator)vd.Decls[i];
            if (d.Target != null && d.Target.Kind == JsKind.Identifier && d.Init == null)
            {
                if (mode == BindMode.Var)
                {
                    // var with no initializer: ensure the binding exists (from
                    // hoisting) but never reset an existing value (i=5; var i; -> 5)
                    scope.HoistVar(((JsIdentifier)d.Target).Name);
                }
                else
                {
                    // let/const without init -> undefined (TDZ resolved)
                    BindPattern(scope, d.Target, JsUndefined.Instance, mode);
                }
                continue;
            }
            object init = d.Init != null ? EvalExpr(d.Init, scope) : JsUndefined.Instance;
            BindPattern(scope, d.Target, init, mode);
        }
        return Completion.Norm();
    }

    private void HoistLetScan(List<JsNode> stmts, JsScope scope)
    {
        if (stmts == null) return;
        for (int i = 0; i < stmts.Count; i++)
        {
            JsNode s = stmts[i];
            if (s.Kind == JsKind.VarDecl)
            {
                JsVarDecl vd = (JsVarDecl)s;
                if (vd.DeclKind == JsDeclKind.Var) continue;
                for (int k = 0; k < vd.Decls.Count; k++)
                    HoistLetPattern(((JsDeclarator)vd.Decls[k]).Target, scope, vd.DeclKind == JsDeclKind.Const);
            }
        }
    }

    private void HoistLetPattern(JsNode p, JsScope scope, bool isConst)
    {
        if (p == null) return;
        if (p.Kind == JsKind.Identifier) scope.HoistLet(((JsIdentifier)p).Name, isConst);
        else if (p.Kind == JsKind.AssignPat) HoistLetPattern(((JsAssignPat)p).Target, scope, isConst);
    }

    private Completion ExecFor(JsFor n, JsScope scope, List<string> labels)
    {
        JsScope loopScope = scope;
        bool needsBlock = n.Init != null && n.Init.Kind == JsKind.VarDecl &&
            ((JsVarDecl)n.Init).DeclKind != JsDeclKind.Var;
        if (needsBlock)
        {
            loopScope = scope.PushBlock();
            HoistLetScan(new List<JsNode> { n.Init }, loopScope);
        }

        if (n.Init != null)
        {
            if (n.Init.Kind == JsKind.VarDecl) ExecVarDecl((JsVarDecl)n.Init, loopScope);
            else EvalExpr(n.Init, loopScope);
        }

        while (n.Test == null || JsValueOps.ToBoolean(EvalExpr(n.Test, loopScope)))
        {
            Completion c = ExecStmt(n.Body, loopScope, NoLabels);
            if (c.Type == CompType.Break) { if (c.Label == null || HasLabel(labels, c.Label)) break; return c; }
            if (c.Type == CompType.Return) return c;
            if (c.Type == CompType.Continue) { if (c.Label != null && !HasLabel(labels, c.Label)) return c; }
            if (n.Update != null) EvalExpr(n.Update, loopScope);
        }
        return Completion.Norm();
    }

    private Completion ExecForInOf(JsNode s, JsScope scope, List<string> labels)
    {
        JsForIn n = (JsForIn)s;
        object right = EvalExpr(n.Right, scope);

        JsScope loopScope = scope;
        bool decl = n.Left != null && n.Left.Kind == JsKind.VarDecl;
        JsVarDecl vd = decl ? (JsVarDecl)n.Left : null;
        JsNode targetPat = decl ? ((JsDeclarator)vd.Decls[0]).Target : n.Left;
        BindMode mode = BindMode.Assign;
        if (decl)
        {
            mode = vd.DeclKind == JsDeclKind.Var ? BindMode.Var
                : vd.DeclKind == JsDeclKind.Let ? BindMode.Let : BindMode.Const;
            if (mode != BindMode.Var)
            {
                loopScope = scope.PushBlock();
                HoistLetPattern(targetPat, loopScope, mode == BindMode.Const);
            }
        }

        if (n.IsOf)
        {
            List<object> values = ToList(right);
            for (int i = 0; i < values.Count; i++)
            {
                BindPattern(loopScope, targetPat, values[i], mode);
                Completion c = ExecStmt(n.Body, loopScope, NoLabels);
                if (c.Type == CompType.Break) { if (c.Label == null || HasLabel(labels, c.Label)) break; return c; }
                if (c.Type == CompType.Return) return c;
                if (c.Type == CompType.Continue) { if (c.Label != null && !HasLabel(labels, c.Label)) return c; }
            }
        }
        else
        {
            List<string> keys = EnumerateKeys(right);
            for (int i = 0; i < keys.Count; i++)
            {
                BindPattern(loopScope, targetPat, keys[i], mode);
                Completion c = ExecStmt(n.Body, loopScope, NoLabels);
                if (c.Type == CompType.Break) { if (c.Label == null || HasLabel(labels, c.Label)) break; return c; }
                if (c.Type == CompType.Return) return c;
                if (c.Type == CompType.Continue) { if (c.Label != null && !HasLabel(labels, c.Label)) return c; }
            }
        }
        return Completion.Norm();
    }

    private Completion ExecSwitch(JsSwitch n, JsScope scope, List<string> labels)
    {
        object disc = EvalExpr(n.Discriminant, scope);
        JsScope swScope = scope.PushBlock();

        int defaultIdx = -1;
        int matchIdx = -1;
        for (int i = 0; i < n.Cases.Count; i++)
        {
            JsSwitchCase sc = (JsSwitchCase)n.Cases[i];
            if (sc.Test == null) { defaultIdx = i; continue; }
            object t = EvalExpr(sc.Test, swScope);
            if (matchIdx < 0 && JsValueOps.StrictEquals(disc, t)) matchIdx = i;
        }

        List<string> swLabels = labels;
        int start = matchIdx >= 0 ? matchIdx : defaultIdx;
        if (start < 0) return Completion.Norm();

        for (int i = start; i < n.Cases.Count; i++)
        {
            JsSwitchCase sc = (JsSwitchCase)n.Cases[i];
            Completion c = ExecStatements(sc.Body, swScope, NoLabels);
            if (c.Type == CompType.Break)
            {
                if (c.Label == null || HasLabel(swLabels, c.Label)) break;
                return c;
            }
            if (c.Type != CompType.Normal) return c;
        }
        return Completion.Norm();
    }

    private Completion ExecTry(JsTry n, JsScope scope, List<string> labels)
    {
        Completion c = Completion.Norm();
        JsThrown pending = null;
        try
        {
            c = ExecStmt(n.Body, scope, NoLabels);
        }
        catch (JsThrown jt)
        {
            pending = jt;
        }

        if (pending != null && n.Handler != null)
        {
            JsCatch ct = (JsCatch)n.Handler;
            JsScope cs = scope.PushBlock();
            if (ct.Param != null)
                BindPattern(cs, ct.Param, MaterializeError(pending.Value), BindMode.Let);
            try
            {
                c = ExecStatements(((JsBlock)ct.Block).Body, cs, NoLabels);
                pending = null;
            }
            catch (JsThrown jt2)
            {
                pending = jt2;
            }
        }

        if (n.Finalizer != null)
        {
            Completion fc = ExecStmt(n.Finalizer, scope, NoLabels);
            if (fc.Type != CompType.Normal) return fc; // finalizer overrides
        }

        if (pending != null) throw pending;
        return c;
    }

    // ---- classes ----------------------------------------------------------

    private object BuildClass(JsClass cls, JsScope scope, string name)
    {
        JsObject superProto = null;
        JsFunction superCtor = null;
        object superVal = null;
        if (cls.Super != null)
        {
            superVal = EvalExpr(cls.Super, scope);
            superCtor = superVal as JsFunction;
            if (superCtor != null)
            {
                object sp;
                if (superCtor.GetProperty("prototype", out sp)) superProto = sp as JsObject;
            }
            else if (superVal is JsObject) superProto = (JsObject)superVal;
        }

        JsObject proto = new JsObject(superProto != null ? superProto : Realm.ObjectPrototype);

        // find constructor
        JsFunctionNode ctorNode = null;
        for (int i = 0; i < cls.Members.Count; i++)
        {
            JsClassMember m = (JsClassMember)cls.Members[i];
            if (m.MemberKind == 3) { ctorNode = m.Value; break; }
        }

        JsFunction ctor;
        if (ctorNode != null)
        {
            ctor = MakeFunctionFromNode(ctorNode, scope);
        }
        else if (superCtor != null)
        {
            // default derived constructor: super(...args)
            ctor = new JsFunction(Realm.FunctionPrototype);
            ctor.Name = name;
            ctor.Params = new List<JsNode>();
            ctor.Closure = scope;
            ctor.SuperConstructor = superCtor;
            ctor.Body = new JsBlock(0); // empty; super called implicitly by NewDefault
            ctor.IsDefaultDerived = true;
        }
        else
        {
            ctor = new JsFunction(Realm.FunctionPrototype);
            ctor.Name = name;
            ctor.Params = new List<JsNode>();
            ctor.Closure = scope;
            ctor.Body = new JsBlock(0);
        }
        ctor.HomeObject = proto;
        ctor.SuperConstructor = superCtor;
        ctor.Define("prototype", JsProperty.Data(proto));
        proto.Define("constructor", JsProperty.DataNonEnum(ctor));
        ctor.SetWellKnown(JsWellKnown.ToStringTag, name ?? "Function");

        // methods
        for (int i = 0; i < cls.Members.Count; i++)
        {
            JsClassMember m = (JsClassMember)cls.Members[i];
            if (m.MemberKind == 3) continue; // constructor handled
            if (m.MemberKind != 0 && m.MemberKind != 1 && m.MemberKind != 2) continue;

            JsFunction mf = MakeFunctionFromNode(m.Value, scope);
            mf.IsMethod = true;
            mf.HomeObject = m.IsStatic ? (JsObject)ctor : proto;

            string key = m.Key.Kind == JsKind.Literal && ((JsLiteral)m.Key).Value is string
                ? (string)((JsLiteral)m.Key).Value
                : JsValueOps.ToStringValue(m.Key.Kind == JsKind.Identifier ? ((JsIdentifier)m.Key).Name : (object)"");
            if (m.Computed) key = JsValueOps.ToStringValue(EvalExpr(m.Key, scope));

            JsObject holder = m.IsStatic ? (JsObject)ctor : proto;
            if (m.MemberKind == 1)
            {
                JsProperty existing;
                holder.TryGetRaw(key, out existing);
                JsFunction g = mf;
                JsFunction s = existing != null && existing.Kind == JsPropKind.Accessor ? existing.Setter : null;
                holder.Define(key, JsProperty.Accessor(g, s));
            }
            else if (m.MemberKind == 2)
            {
                JsProperty existing;
                holder.TryGetRaw(key, out existing);
                JsFunction s = mf;
                JsFunction g = existing != null && existing.Kind == JsPropKind.Accessor ? existing.Getter : null;
                holder.Define(key, JsProperty.Accessor(g, s));
            }
            else
            {
                holder.Define(key, JsProperty.Data(mf));
            }
        }

        // bind class name inside its own scope (for self-reference)
        return ctor;
    }

    // ---- expressions ------------------------------------------------------

    public object EvalExpr(JsNode e, JsScope scope)
    {
        if (++_steps > StepLimit) throw new Exception("Script exceeded step limit");
        switch (e.Kind)
        {
            case JsKind.Literal:
                {
                    if (e is JsRegexLiteral)
                    {
                        JsRegexLiteral rl = (JsRegexLiteral)e;
                        return JsBuiltins.CreateRegExp(Realm, rl.Pattern, rl.Flags);
                    }
                    return ((JsLiteral)e).Value;
                }
            case JsKind.Identifier:
                {
                    string name = ((JsIdentifier)e).Name;
                    return scope.GetVariable(name);
                }
            case JsKind.This:
                return _this != null ? _this : (object)Realm.Global;
            case JsKind.Super:
                throw JsErr.Syntax("'super' keyword unexpected here");
            case JsKind.ArrayLit:
                return EvalArrayLit((JsArrayLit)e, scope);
            case JsKind.ObjectLit:
                return EvalObjectLit((JsObjectLit)e, scope);
            case JsKind.Spread:
                throw JsErr.Syntax("Unexpected '...' here");
            case JsKind.Function:
            case JsKind.Arrow:
                return MakeFunctionFromNode((JsFunctionNode)e, scope);
            case JsKind.Unary:
                return EvalUnary((JsUnary)e, scope);
            case JsKind.Update:
                return EvalUpdate((JsUpdate)e, scope);
            case JsKind.Binary:
                return EvalBinary((JsBinary)e, scope);
            case JsKind.Logical:
                return EvalLogical((JsLogical)e, scope);
            case JsKind.Assignment:
                return EvalAssignment((JsAssignment)e, scope);
            case JsKind.Conditional:
                {
                    JsConditional c = (JsConditional)e;
                    return JsValueOps.ToBoolean(EvalExpr(c.Test, scope)) ? EvalExpr(c.Cons, scope) : EvalExpr(c.Alt, scope);
                }
            case JsKind.Call:
            case JsKind.Member:
                {
                    object recv; bool sc;
                    object v = EvalChain(e, scope, out recv, out sc);
                    return sc ? JsUndefined.Instance : v;
                }
            case JsKind.New:
                return EvalNew((JsNew)e, scope);
            case JsKind.Sequence:
                {
                    JsSequence sq = (JsSequence)e;
                    object last = JsUndefined.Instance;
                    for (int i = 0; i < sq.List.Count; i++) last = EvalExpr(sq.List[i], scope);
                    return last;
                }
            case JsKind.Template:
                return EvalTemplate((JsTemplate)e, scope);
            case JsKind.TaggedTemplate:
                return EvalTaggedTemplate((JsTaggedTemplate)e, scope);
            case JsKind.Class:
                return BuildClass((JsClass)e, scope, ((JsClass)e).Name);
        }
        throw JsErr.Syntax("Unhandled expression kind " + e.Kind);
    }

    private object EvalArrayLit(JsArrayLit n, JsScope scope)
    {
        JsArray arr = Realm.NewArray();
        for (int i = 0; i < n.Elements.Count; i++)
        {
            JsNode el = n.Elements[i];
            if (el == null) { arr.Push(JsUndefined.Instance); continue; }
            if (el.Kind == JsKind.Spread)
            {
                object v = EvalExpr(((JsSpread)el).Argument, scope);
                List<object> list = ToList(v);
                for (int k = 0; k < list.Count; k++) arr.Push(list[k]);
                continue;
            }
            arr.Push(EvalExpr(el, scope));
        }
        return arr;
    }

    private object EvalObjectLit(JsObjectLit n, JsScope scope)
    {
        JsObject o = Realm.NewObject();
        for (int i = 0; i < n.Props.Count; i++)
        {
            JsNode pn = n.Props[i];
            if (pn.Kind == JsKind.Spread)
            {
                object v = EvalExpr(((JsSpread)pn).Argument, scope);
                if (v is JsObject)
                {
                    List<string> keys = OwnEnumerableKeys(v);
                    for (int k = 0; k < keys.Count; k++)
                        o.Define(keys[k], JsProperty.Data(GetMemberDirect(v, keys[k])));
                }
                continue;
            }
            JsObjectProp pr = (JsObjectProp)pn;
            object key = pr.Computed ? ToPropertyKey(EvalExpr(pr.Key, scope))
                : (pr.Key.Kind == JsKind.Literal ? LiteralKey(pr.Key) : (object)((JsIdentifier)pr.Key).Name);

            if (pr.PropKind == 1 || pr.PropKind == 2)
            {
                JsFunction fn = MakeFunctionFromNode((JsFunctionNode)pr.Value, scope);
                fn.IsMethod = true;
                fn.HomeObject = o;
                JsProperty existing;
                string kstr = key as string;
                if (kstr != null && o.TryGetRaw(kstr, out existing) && existing.Kind == JsPropKind.Accessor)
                    o.Define(kstr, JsProperty.Accessor(pr.PropKind == 1 ? fn : existing.Getter, pr.PropKind == 2 ? fn : existing.Setter));
                else
                    o.Define(key as string ?? "", JsProperty.Accessor(pr.PropKind == 1 ? fn : null, pr.PropKind == 2 ? fn : null));
                continue;
            }

            object value;
            if (pr.Value != null && (pr.Value.Kind == JsKind.Function || pr.Value.Kind == JsKind.Arrow))
            {
                JsFunction fn = MakeFunctionFromNode((JsFunctionNode)pr.Value, scope);
                fn.IsMethod = true;
                fn.HomeObject = o;
                if (fn.Name == "" && key is string) fn.Name = (string)key;
                value = fn;
            }
            else value = EvalExpr(pr.Value, scope);

            string ks = key as string;
            if (ks != null) o.Define(ks, JsProperty.Data(value));
            else o.Define((JsSymbol)key, JsProperty.Data(value));
        }
        return o;
    }

    private object LiteralKey(JsNode keyNode)
    {
        JsLiteral lit = (JsLiteral)keyNode;
        if (lit.Value is string) return (string)lit.Value;
        if (lit.Value is double) return JsValueOps.NumberToString((double)lit.Value);
        return JsValueOps.ToStringValue(lit.Value);
    }

    private object EvalTemplate(JsTemplate n, JsScope scope)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(n.Quasis[0]);
        for (int i = 0; i < n.Exprs.Count; i++)
        {
            sb.Append(JsValueOps.ToStringValue(EvalExpr(n.Exprs[i], scope)));
            sb.Append(n.Quasis[i + 1]);
        }
        return sb.ToString();
    }

    private object EvalTaggedTemplate(JsTaggedTemplate n, JsScope scope)
    {
        object tagVal = EvalExpr(n.Tag, scope);
        // build the template object (array of cooked strings)
        JsArray cooked = Realm.NewArray();
        for (int i = 0; i < n.Template.Quasis.Count; i++) cooked.Push(n.Template.Quasis[i]);
        List<object> args = new List<object>();
        args.Add(cooked);
        for (int i = 0; i < n.Template.Exprs.Count; i++) args.Add(EvalExpr(n.Template.Exprs[i], scope));
        return Invoke(tagVal, JsUndefined.Instance, args.ToArray());
    }

    private object EvalUnary(JsUnary n, JsScope scope)
    {
        string op = n.Op;
        if (op == "typeof")
        {
            // typeof on an undeclared identifier must not throw
            if (n.Operand.Kind == JsKind.Identifier)
            {
                string name = ((JsIdentifier)n.Operand).Name;
                if (!scope.HasVariable(name)) return "undefined";
                return JsValueOps.TypeNameOf(scope.GetVariable(name));
            }
            object v = EvalExpr(n.Operand, scope);
            return JsValueOps.TypeNameOf(v);
        }
        if (op == "void") { EvalExpr(n.Operand, scope); return JsUndefined.Instance; }
        if (op == "delete")
        {
            JsNode target = n.Operand;
            if (target.Kind == JsKind.Member)
            {
                object recv; bool sc;
                // evaluate base and key without invoking as method
                JsMember m = (JsMember)target;
                object baseVal = EvalExpr(m.Object, scope);
                object key = m.Computed ? EvalExpr(m.Property, scope) : (object)((JsIdentifier)m.Property).Name;
                if (baseVal is JsSymbol) return true;
                if (!(baseVal is JsObject)) return true;
                object dkey = ToPropertyKey(key);
                string dks = dkey as string;
                if (dks != null) return ((JsObject)baseVal).DeleteProperty(dks) ? (object)true : (object)false;
                if (dkey is JsSymbol) return ((JsObject)baseVal).DeleteProperty((JsSymbol)dkey) ? (object)true : (object)false;
                return (object)true;
            }
            if (target.Kind == JsKind.Identifier) return true; // sloppy: deleting a variable -> true
            EvalExpr(target, scope);
            return true;
        }

        object val = EvalExpr(n.Operand, scope);
        switch (op)
        {
            case "!": return !JsValueOps.ToBoolean(val);
            case "-": return -JsValueOps.ToNumber(val);
            case "+": return JsValueOps.ToNumber(val);
            case "~": return ~(long)JsValueOps.ToInt32(val);
            case "await":
            {
                JsEventLoop loop = EnsureLoop();
                JsPromise p = JsBrowser.CoerceToPromise(Realm, loop, val);
                return loop.AwaitPromise(p);
            }
        }
        throw JsErr.Syntax("Unknown unary operator " + op);
    }

    private object EvalUpdate(JsUpdate n, JsScope scope)
    {
        object old = ToNumeric(EvalExpr(n.Target, scope));
        double d = JsValueOps.ToNumber(old);
        double next = n.Op == "++" ? d + 1 : d - 1;
        AssignTo(n.Target, JsValueOps.IsNumber(old) ? (object)next : (object)next, scope, false);
        return n.Prefix ? (object)next : (object)d;
    }

    private object ToNumeric(object v)
    {
        if (v is JsSymbol) throw JsErr.Type("Cannot convert a Symbol value to a number");
        return JsValueOps.ToNumber(v);
    }

    private object EvalBinary(JsBinary n, JsScope scope)
    {
        switch (n.Op)
        {
            case "&&": { object l = EvalExpr(n.Left, scope); return JsValueOps.ToBoolean(l) ? EvalExpr(n.Right, scope) : l; }
            case "||": { object l = EvalExpr(n.Left, scope); return JsValueOps.ToBoolean(l) ? l : EvalExpr(n.Right, scope); }
            case "??": { object l = EvalExpr(n.Left, scope); return JsValueOps.IsNullish(l) ? EvalExpr(n.Right, scope) : l; }
        }

        object left = EvalExpr(n.Left, scope);

        if (n.Op == "in")
        {
            object container = EvalExpr(n.Right, scope);
            if (JsValueOps.IsNullish(container)) throw JsErr.Type("Cannot use 'in' operator to search for '" + JsValueOps.ToStringValue(left) + "' in " + (container is JsNull ? "null" : "undefined"));
            return HasProperty(container, ToPropertyKey(left));
        }
        if (n.Op == "instanceof") return InstanceOf(left, EvalExpr(n.Right, scope));

        object right = EvalExpr(n.Right, scope);
        return ApplyBinary(n.Op, left, right);
    }

    private object ApplyBinary(string op, object left, object right)
       {
        switch (op)
        {
            case "+":
                {
                    object lp = JsValueOps.ToPrimitive(left, JsHint.Default);
                    object rp = JsValueOps.ToPrimitive(right, JsHint.Default);
                    if (JsValueOps.IsString(lp) || JsValueOps.IsString(rp))
                        return JsValueOps.ToStringValue(lp) + JsValueOps.ToStringValue(rp);
                    return JsValueOps.ToNumber(lp) + JsValueOps.ToNumber(rp);
                }
            case "-": return JsValueOps.ToNumber(left) - JsValueOps.ToNumber(right);
            case "*": return JsValueOps.ToNumber(left) * JsValueOps.ToNumber(right);
            case "/": return JsValueOps.ToNumber(left) / JsValueOps.ToNumber(right);
            case "%": return JsValueOps.ToNumber(left) % JsValueOps.ToNumber(right);
            case "**": return Math.Pow(JsValueOps.ToNumber(left), JsValueOps.ToNumber(right));
            case "&": return (double)((long)JsValueOps.ToInt32(left) & (long)JsValueOps.ToInt32(right));
            case "|": return (double)((long)JsValueOps.ToInt32(left) | (long)JsValueOps.ToInt32(right));
            case "^": return (double)((long)JsValueOps.ToInt32(left) ^ (long)JsValueOps.ToInt32(right));
            case "<<": { int s = (int)((long)JsValueOps.ToUint32(right) & 31); return (double)((long)JsValueOps.ToInt32(left) << s); }
            case ">>": { int s = (int)((long)JsValueOps.ToUint32(right) & 31); return (double)((long)JsValueOps.ToInt32(left) >> s); }
            case ">>>": { int s = (int)((long)JsValueOps.ToUint32(right) & 31); return (double)((long)JsValueOps.ToUint32(left) >> s); }
            case "==": return JsValueOps.LooseEquals(left, right);
            case "!=": return !JsValueOps.LooseEquals(left, right);
            case "===": return JsValueOps.StrictEquals(left, right);
            case "!==": return !JsValueOps.StrictEquals(left, right);
            case "<": case ">": case "<=": case ">=": return Compare(op, left, right);
        }
        throw JsErr.Syntax("Unknown binary operator " + op);
    }

    private object Compare(string op, object left, object right)
    {
        object lp = JsValueOps.ToPrimitive(left, JsHint.Number);
        object rp = JsValueOps.ToPrimitive(right, JsHint.Number);

        if (JsValueOps.IsString(lp) && JsValueOps.IsString(rp))
        {
            string a = (string)lp, b = (string)rp;
            int c = string.CompareOrdinal(a, b);
            return EvalCmpOp(op, c);
        }
        double x = JsValueOps.ToNumber(lp), y = JsValueOps.ToNumber(rp);
        if (double.IsNaN(x) || double.IsNaN(y)) return false;
        if (op == "<") return x < y;
        if (op == ">") return x > y;
        if (op == "<=") return x <= y;
        return x >= y;
    }

    private object EvalCmpOp(string op, int c)
    {
        if (op == "<") return c < 0;
        if (op == ">") return c > 0;
        if (op == "<=") return c <= 0;
        return c >= 0;
    }

    private object EvalLogical(JsLogical n, JsScope scope)
    {
        object l = EvalExpr(n.Left, scope);
        if (n.Op == "&&") return JsValueOps.ToBoolean(l) ? EvalExpr(n.Right, scope) : l;
        if (n.Op == "||") return JsValueOps.ToBoolean(l) ? l : EvalExpr(n.Right, scope);
        return JsValueOps.IsNullish(l) ? EvalExpr(n.Right, scope) : l;
    }

    private object EvalAssignment(JsAssignment n, JsScope scope)
    {
        string op = n.Op;

        if (n.Target.Kind == JsKind.Member)
        {
            JsMember m = (JsMember)n.Target;
            object baseVal = EvalExpr(m.Object, scope);
            object key = m.Computed ? EvalExpr(m.Property, scope) : (object)((JsIdentifier)m.Property).Name;

            if (op == "=")
            {
                object v = EvalExpr(n.Value, scope);
                SetMember(baseVal, key, v);
                return v;
            }

            // compound assignment on member
            object mCur;
            GetMember(baseVal, key, out mCur);
            object mRes;
            switch (op)
            {
                case "??=": mRes = JsValueOps.IsNullish(mCur) ? EvalExpr(n.Value, scope) : mCur; break;
                case "&&=": mRes = JsValueOps.ToBoolean(mCur) ? EvalExpr(n.Value, scope) : mCur; break;
                case "||=": mRes = JsValueOps.ToBoolean(mCur) ? mCur : EvalExpr(n.Value, scope); break;
                default:
                    {
                        string binop = op.Substring(0, op.Length - 1);
                        object rhs = EvalExpr(n.Value, scope);
                        mRes = ApplyBinary(binop, mCur, rhs);
                        break;
                    }
            }
            SetMember(baseVal, key, mRes);
            return mRes;
        }

        // destructuring assignment (target is a pattern)
        if (op == "=" && (n.Target.Kind == JsKind.ObjectPat || n.Target.Kind == JsKind.ArrayPat))
        {
            object v = EvalExpr(n.Value, scope);
            BindPattern(scope, n.Target, v, BindMode.Assign);
            return v;
        }

        if (op == "=")
        {
            object v = EvalExpr(n.Value, scope);
            if (n.Target.Kind == JsKind.Identifier && v is JsFunction fn && string.IsNullOrEmpty(fn.Name))
                fn.Name = ((JsIdentifier)n.Target).Name;
            BindPattern(scope, n.Target, v, BindMode.Assign);
            return v;
        }

        // compound assignment
        object cur = EvalExpr(n.Target, scope);
        object result;
        switch (op)
        {
            case "??=": result = JsValueOps.IsNullish(cur) ? EvalExpr(n.Value, scope) : cur; break;
            case "&&=": result = JsValueOps.ToBoolean(cur) ? EvalExpr(n.Value, scope) : cur; break;
            case "||=": result = JsValueOps.ToBoolean(cur) ? cur : EvalExpr(n.Value, scope); break;
            default:
                {
                    string binop = op.Substring(0, op.Length - 1);
                    object rhs = EvalExpr(n.Value, scope);
                    result = ApplyBinary(binop, cur, rhs);
                    break;
                }
        }
        BindPattern(scope, n.Target, result, BindMode.Assign);
        return result;
    }

    private void AssignTo(JsNode target, object value, JsScope scope, bool incdec)
    {
        BindPattern(scope, target, value, BindMode.Assign);
    }

    // ---- member / call chain ---------------------------------------------

    private object EvalChain(JsNode node, JsScope scope, out object receiver, out bool shortCircuit)
    {
        receiver = JsUndefined.Instance;
        shortCircuit = false;

        if (node.Kind == JsKind.Member)
        {
            JsMember m = (JsMember)node;
            object baseVal; object recv; bool sc;

            if (m.Object != null && m.Object.Kind == JsKind.Super)
            {
                // super.x -> start the lookup at [[HomeObject]].[[Prototype]],
                // but keep `this` as the receiver.
                if (_home == null) throw JsErr.Syntax("'super' keyword unexpected here");
                baseVal = _home.Prototype != null ? (object)_home.Prototype : (object)Realm.ObjectPrototype;
                recv = _this != null ? _this : (object)Realm.Global;
                sc = false;
            }
            else if (IsChainSpine(m.Object))
            {
                baseVal = EvalChain(m.Object, scope, out recv, out sc);
                // the method receiver is the object we just looked the property up on
                recv = baseVal;
            }
            else
            {
                baseVal = EvalExpr(m.Object, scope);
                recv = baseVal;
                sc = false;
            }
            if (sc) { shortCircuit = true; return JsUndefined.Instance; }

            if (m.Optional && JsValueOps.IsNullish(baseVal)) { shortCircuit = true; return JsUndefined.Instance; }

            object key = m.Computed ? ToPropertyKey(EvalExpr(m.Property, scope)) : (object)((JsIdentifier)m.Property).Name;
            object v;
            GetMember(baseVal, key, out v);
            receiver = recv;
            return v;
        }

        if (node.Kind == JsKind.Call)
        {
            JsCall c = (JsCall)node;

            if (c.Callee != null && c.Callee.Kind == JsKind.Super)
            {
                // super(...) -> construct the base instance on the current `this`
                if (_home == null || _superCtor == null) throw JsErr.Syntax("'super' call unexpected here");
                List<object> sargs = EvalArgs(c.Args, scope);
                object baseThis = _this != null ? _this : (object)Realm.Global;
                Call(_superCtor, baseThis, sargs.ToArray(), true);
                return baseThis;
            }

            object callee; object recv; bool sc;
            if (IsChainSpine(c.Callee))
            {
                callee = EvalChain(c.Callee, scope, out recv, out sc);
            }
            else
            {
                callee = EvalExpr(c.Callee, scope);
                recv = JsUndefined.Instance;
                sc = false;
            }
            if (sc) { shortCircuit = true; return JsUndefined.Instance; }

            object thisArg = recv;
            List<object> args = EvalArgs(c.Args, scope);

            if (c.Optional && JsValueOps.IsNullish(callee)) { shortCircuit = true; return JsUndefined.Instance; }

            return Invoke(callee, thisArg, args.ToArray(), c.Callee);
        }

        // spine root
        return EvalExpr(node, scope);
    }

    private static bool IsChainSpine(JsNode n)
    {
        return n.Kind == JsKind.Member || n.Kind == JsKind.Call;
    }

    private static string DescribeCallee(JsNode n)
    {
        if (n == null) return "callee";
        if (n is JsIdentifier id) return id.Name;
        if (n is JsMember m)
        {
            string prop = m.Property is JsIdentifier pid ? pid.Name : (m.Property is JsLiteral lit ? lit.Value?.ToString() : "?");
            return DescribeCallee(m.Object) + "." + prop;
        }
        return n.Kind.ToString();
    }

    private object Invoke(object callee, object thisArg, object[] args, JsNode calleeNode = null)
    {
        JsFunction fn = callee as JsFunction;
        if (fn == null)
        {
            throw JsErr.Type(DescribeCallee(calleeNode) + " is not a function (was " + JsValueOps.TypeNameOf(callee) + ")");
        }
        if (fn == Realm.FunctionCallMethod && thisArg is JsFunction targetFn)
        {
            object realThis = (args != null && args.Length > 0) ? args[0] : Realm.Global;
            object[] realArgs = null;
            if (args != null && args.Length > 1)
            {
                realArgs = new object[args.Length - 1];
                for (int i = 1; i < args.Length; i++) realArgs[i - 1] = args[i];
            }
            if (JsValueOps.IsNullish(realThis)) realThis = Realm.Global;
            return Call(targetFn, realThis, realArgs, false);
        }
        if (fn == Realm.FunctionApplyMethod && thisArg is JsFunction applyTargetFn)
        {
            object realThis = (args != null && args.Length > 0) ? args[0] : Realm.Global;
            List<object> list = (args != null && args.Length > 1) ? ToList(args[1]) : new List<object>();
            if (JsValueOps.IsNullish(realThis)) realThis = Realm.Global;
            return Call(applyTargetFn, realThis, list.ToArray(), false);
        }
        if (JsValueOps.IsNullish(thisArg)) thisArg = Realm.Global;
        return Call(fn, thisArg, args, false);
    }

    private object EvalNew(JsNew n, JsScope scope)
    {
        object callee = EvalExpr(n.Callee, scope);
        JsFunction ctor = callee as JsFunction;
        if (ctor == null) throw JsErr.Type(JsValueOps.TypeNameOf(callee) + " is not a constructor");
        List<object> args = EvalArgs(n.Args, scope);

        if (ctor.IsDefaultDerived && ctor.SuperConstructor != null)
        {
            // derived default: create the instance from THIS class's prototype
            // (implicit super() only runs the base constructor on the same this)
            object pval;
            JsObject ownProto = null;
            if (ctor.GetProperty("prototype", out pval)) ownProto = pval as JsObject;
            JsObject obj = new JsObject(ownProto != null ? ownProto : Realm.ObjectPrototype);
            Call(ctor.SuperConstructor, obj, args.ToArray(), true);
            return obj;
        }

        object protoVal;
        JsObject proto = null;
        if (ctor.GetProperty("prototype", out protoVal)) proto = protoVal as JsObject;
        JsObject newThis = new JsObject(proto != null ? proto : Realm.ObjectPrototype);
        object result = Call(ctor, newThis, args.ToArray(), true);
        return result is JsObject ? result : newThis;
    }

    private List<object> EvalArgs(List<JsNode> argNodes, JsScope scope)
    {
        List<object> args = new List<object>();
        if (argNodes == null) return args;
        for (int i = 0; i < argNodes.Count; i++)
        {
            JsNode a = argNodes[i];
            if (a.Kind == JsKind.Spread)
            {
                object v = EvalExpr(((JsSpread)a).Argument, scope);
                List<object> list = ToList(v);
                for (int k = 0; k < list.Count; k++) args.Add(list[k]);
            }
            else args.Add(EvalExpr(a, scope));
        }
        return args;
    }

    // ---- property access --------------------------------------------------

    public void GetMember(object baseVal, object keyObj, out object value)
    {
        value = JsUndefined.Instance;

        if (JsValueOps.IsNullish(baseVal))
            throw JsErr.Type("Cannot read properties of " + (baseVal is JsNull ? "null" : "undefined") + " (reading '" + KeyText(keyObj) + "')");

        if (keyObj is JsSymbol sym)
        {
            if (baseVal is JsSymbol)
            {
                value = JsUndefined.Instance;
                return;
            }
            JsObject symProto = PrototypeOfValue(baseVal);
            if (symProto == null) { value = JsUndefined.Instance; return; }
            symProto.GetProperty(sym, out value);
            return;
        }

        string keyStr = keyObj as string ?? JsValueOps.ToStringValue(keyObj);
        if (keyStr == null) keyStr = "undefined";

        if (baseVal is JsSymbol)
        {
            if (keyStr == "description") { value = ((JsSymbol)baseVal).Desc; }
            else if (keyStr == "toString") { value = Realm.MakeMethod("toString", SymbolToString, 0); }
            else { value = JsUndefined.Instance; }
            return;
        }

        if (baseVal is string)
        {
            string s = (string)baseVal;
            if (keyStr == "length") { value = (double)s.Length; return; }
            long idx;
            if (IsArrayIndexKey(keyStr, out idx) && idx >= 0 && idx < s.Length)
            { value = s[(int)idx].ToString(); return; }
        }
        if (baseVal is JsArray)
        {
            JsArray a = (JsArray)baseVal;
            if (keyStr == "length") { value = (double)a.Length; return; }
            long idx;
            if (IsArrayIndexKey(keyStr, out idx))
            {
                value = a.GetIndex((int)idx);
                return;
            }
        }

        JsObject proto = PrototypeOfValue(baseVal);
        if (proto == null) { value = JsUndefined.Instance; return; }

        proto.GetProperty(keyStr, out value);
    }

    public object GetMemberDirect(object baseVal, string key)
    {
        object v;
        GetMember(baseVal, key, out v);
        return v;
    }

    public void SetMember(object baseVal, object keyObj, object value)
    {
        if (JsValueOps.IsNullish(baseVal))
            throw JsErr.Type("Cannot set properties of " + (baseVal is JsNull ? "null" : "undefined") + " (setting '" + KeyText(keyObj) + "')");
        if (!(baseVal is JsObject)) return; // sloppy: writing to a primitive is a no-op

        JsObject o = (JsObject)baseVal;
        if (keyObj is JsSymbol sym) { o.SetProperty(sym, value); return; }

        string keyStr = keyObj as string ?? JsValueOps.ToStringValue(keyObj);
        if (keyStr == null) keyStr = "undefined";

        if (o is JsArray)
        {
            if (keyStr == "length")
            {
                ((JsArray)o).SetLength((int)JsValueOps.ToNumber(value));
                return;
            }
            long idx;
            if (IsArrayIndexKey(keyStr, out idx) && idx >= 0)
            {
                JsArray a = (JsArray)o;
                while (a.Length <= idx) a.Push(JsUndefined.Instance);
                a.Items[(int)idx] = value;
                return;
            }
        }
        o.SetProperty(keyStr, value);
    }

    private static string KeyText(object key)
    {
        if (key is JsSymbol) return key.ToString();
        return key as string ?? JsValueOps.ToStringValue(key);
    }

    private static bool IsArrayIndexKey(string key, out long index)
    {
        index = -1;
        if (key == null || key.Length == 0 || key.Length > 10) return false;
        if (key.Length > 1 && key[0] == '0') return false;
        long n = 0;
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            if (c < '0' || c > '9') return false;
            n = n * 10 + (c - '0');
        }
        index = n;
        return true;
    }

    public bool HasProperty(object baseVal, object key)
    {
        if (JsValueOps.IsNullish(baseVal)) return false;
        if (key is JsSymbol sym)
        {
            JsObject protoSym = PrototypeOfValue(baseVal);
            return protoSym != null && protoSym.HasProperty(sym);
        }
        string keyStr = key as string ?? JsValueOps.ToStringValue(key);
        if (keyStr == null) keyStr = "undefined";

        if (baseVal is string && keyStr == "length") return true;
        if (baseVal is JsArray)
        {
            if (keyStr == "length") return true;
            long idx;
            if (IsArrayIndexKey(keyStr, out idx) && idx >= 0 && idx < ((JsArray)baseVal).Length) return true;
        }

        JsObject proto = PrototypeOfValue(baseVal);
        if (proto == null) return false;
        return proto.HasProperty(keyStr);
    }

    public JsObject PrototypeOfValue(object v)
    {
        if (v is JsObject) return (JsObject)v;
        if (v is string) return Realm.StringPrototype;
        if (v is double) return Realm.NumberPrototype;
        if (v is bool) return Realm.BooleanPrototype;
        if (v is JsSymbol) return Realm.SymbolPrototype;
        return null;
    }

    private object SymbolToString(JsCallArgs a)
    {
        object v = a.ThisValue;
        return v is JsSymbol ? "Symbol(" + ((JsSymbol)v).Desc + ")" : "Symbol()";
    }

    public object ToPropertyKey(object v)
    {
        if (v is JsSymbol) return v;
        return JsValueOps.ToStringValue(JsValueOps.ToPrimitive(v, JsHint.String));
    }

    private bool InstanceOf(object left, object right)
    {
        JsFunction ctor = right as JsFunction;
        if (ctor == null) throw JsErr.Type("Right-hand side of 'instanceof' is not callable");
        object protoVal;
        if (!ctor.GetProperty("prototype", out protoVal) || !(protoVal is JsObject)) return false;
        JsObject proto = (JsObject)protoVal;
        JsObject o = left as JsObject;
        if (o == null) return false;
        JsObject p = o.Prototype;
        while (p != null) { if (ReferenceEquals(p, proto)) return true; p = p.Prototype; }
        return false;
    }

    // ---- iteration helpers ------------------------------------------------

    public List<object> ToList(object iterable)
    {
        List<object> list = new List<object>();
        if (iterable is JsArray)
        {
            JsArray a = (JsArray)iterable;
            for (int i = 0; i < a.Length; i++) list.Add(a.Items[i]);
            return list;
        }
        if (iterable is string)
        {
            string s = (string)iterable;
            for (int i = 0; i < s.Length; i++) list.Add(s[i].ToString());
            return list;
        }
        if (JsValueOps.IsNullish(iterable)) return list;

        JsObject it = GetIterator(iterable);
        object value;
        while (IteratorNext(it, out value)) list.Add(value);
        return list;
    }

    public JsObject GetIterator(object iterable)
    {
        if (iterable is JsArray)
        {
            JsArrayIterState it = new JsArrayIterState();
            it.Prototype = Realm.IteratorPrototype;
            it.Items = ((JsArray)iterable).Items;
            it.Define("next", JsProperty.Data(Realm.MakeMethod("next", ArrayIterNext, 0)));
            return it;
        }
        if (iterable is string)
        {
            JsStringIterState it = new JsStringIterState();
            it.Prototype = Realm.IteratorPrototype;
            it.Text = (string)iterable;
            it.Define("next", JsProperty.Data(Realm.MakeMethod("next", StringIterNext, 0)));
            return it;
        }

        object method;
        if (GetWellKnown(iterable, JsWellKnown.Iterator, out method) && method is JsFunction)
        {
            object it = Call(method, iterable, new object[0], false);
            if (it is JsObject) return (JsObject)it;
        }
        throw JsErr.Type(JsValueOps.TypeNameOf(iterable) + " is not iterable");
    }

    public bool IteratorNext(JsObject it, out object value)
    {
        object nextFn;
        if (!it.GetProperty("next", out nextFn) || !(nextFn is JsFunction))
            throw JsErr.Type("Iterator has no 'next' method");
        object r = Call((JsFunction)nextFn, it, new object[0], false);
        value = JsUndefined.Instance;
        if (!(r is JsObject)) throw JsErr.Type("Iterator result must be an object");
        JsObject ro = (JsObject)r;
        object done;
        ro.GetProperty("done", out done);
        if (JsValueOps.ToBoolean(done)) return false;
        ro.GetProperty("value", out value);
        return true;
    }

    private object ArrayIterNext(JsCallArgs a)
    {
        JsArrayIterState it = (JsArrayIterState)a.ThisValue;
        JsObject r = Realm.NewObject();
        if (it.Index < it.Items.Count)
        {
            r.SetProperty("value", it.Items[it.Index]);
            r.SetProperty("done", false);
            it.Index++;
        }
        else r.SetProperty("done", true);
        return r;
    }

    private object StringIterNext(JsCallArgs a)
    {
        JsStringIterState it = (JsStringIterState)a.ThisValue;
        JsObject r = Realm.NewObject();
        if (it.Index < it.Text.Length)
        {
            r.SetProperty("value", it.Text[it.Index].ToString());
            r.SetProperty("done", false);
            it.Index++;
        }
        else r.SetProperty("done", true);
        return r;
    }

    private bool GetWellKnown(object v, string key, out object method)
    {
        method = null;
        JsObject proto = PrototypeOfValue(v);
        if (proto == null) return false;
        return proto.TryGetWellKnown(key, out method);
    }

    public List<string> EnumerateKeys(object v)
    {
        List<string> keys = new List<string>();
        if (JsValueOps.IsNullish(v)) return keys;
        if (v is JsArray)
        {
            List<string> idx = ((JsArray)v).IndexKeys();
            for (int i = 0; i < idx.Count; i++) keys.Add(idx[i]);
            // include inherited enumerable
            JsObject p = ((JsArray)v).Prototype;
            AddInheritedKeys(p, keys, new HashSet<string>(idx));
            return keys;
        }
        if (v is string)
        {
            string s = (string)v;
            for (int i = 0; i < s.Length; i++) keys.Add(i.ToString());
            return keys;
        }
        if (v is JsObject)
        {
            HashSet<string> seen = new HashSet<string>();
            List<string> own = ((JsObject)v).OwnKeys(true);
            for (int i = 0; i < own.Count; i++) { keys.Add(own[i]); seen.Add(own[i]); }
            AddInheritedKeys(((JsObject)v).Prototype, keys, seen);
        }
        return keys;
    }

    private void AddInheritedKeys(JsObject proto, List<string> keys, HashSet<string> seen)
    {
        JsObject o = proto;
        while (o != null)
        {
            List<string> own = o.OwnKeys(true);
            for (int i = 0; i < own.Count; i++)
            {
                if (!seen.Contains(own[i])) { keys.Add(own[i]); seen.Add(own[i]); }
            }
            o = o.Prototype;
        }
    }

    private List<string> OwnEnumerableKeys(object v)
    {
        List<string> keys = new List<string>();
        if (v is JsArray)
        {
            List<string> idx = ((JsArray)v).IndexKeys();
            for (int i = 0; i < idx.Count; i++) keys.Add(idx[i]);
            return keys;
        }
        if (v is JsObject) return ((JsObject)v).OwnKeys(true);
        return keys;
    }

    // ---- error materialisation -------------------------------------------

    public object MaterializeError(object v)
    {
        if (v is JsErrorValue)
        {
            JsErrorValue ev = (JsErrorValue)v;
            return JsBuiltins.CreateErrorObject(Realm, ev.Name, ev.Message);
        }
        return v;
    }
}

// Lightweight iterator backing stores.
public sealed class JsArrayIterState : JsObject
{
    public List<object> Items;
    public int Index;
}

public sealed class JsStringIterState : JsObject
{
    public string Text;
    public int Index;
}
