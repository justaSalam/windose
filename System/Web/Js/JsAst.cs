using System;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// Abstract syntax tree. Nodes carry a `Kind` so the interpreter can switch on
// a byte rather than walk a virtual table. Statements and expressions are
// separate hierarchies; binding patterns reuse the expression shape where it
// helps (identifiers) and add dedicated pattern nodes for destructuring.
// ---------------------------------------------------------------------------

public enum JsKind : byte
{
    // expressions
    Literal,
    Identifier,
    This,
    Super,
    ArrayLit,
    ObjectLit,
    ObjectProp,     // child of ObjectLit: Key, Value, Kind(init/get/set), Computed, Spread, Shorthand
    Spread,
    Function,       // function expression / method value (Params, Body, Name, Flags)
    Arrow,
    Unary,
    Update,
    Binary,
    Logical,        // && || ??
    Assignment,     // Target, Value, Op
    Conditional,
    Call,
    New,
    Member,
    Sequence,
    Template,       // parts: alternating string (Quasis) and expressions (List)
    Class,          // Name, Super, List of members
    ClassMember,    // Key, Value(Function), IsStatic, Kind(method/get/set/constructor)
    TaggedTemplate, // Tag(Callee), Template

    // binding patterns
    ObjectPat,      // List of PatProp (Key, Value=pattern, Default), Rest
    ArrayPat,       // List of elements (may contain null elisions), Rest
    AssignPat,      // Target + Default (default value)
    RestPat,        // Value = inner pattern
    PatProp,        // Key, Value(pattern), Computed, Rest=false

    // statements
    Program,
    Block,
    VarDecl,        // Kind via DeclKind, List of Declarator (Target, Init)
    Declarator,     // Target(pattern), Init
    FunctionDecl,
    ExprStmt,
    If,
    While,
    DoWhile,
    For,
    ForIn,
    ForOf,
    Return,
    Break,
    Continue,
    Throw,
    Try,
    Catch,          // Param + Block (parent.Try.Body / .Handler / .Finalizer)
    Switch,
    SwitchCase,     // Test (null = default), List of stmts
    Labeled,
    Empty,
    ClassDecl,
    Debugger,
}

public enum JsDeclKind : byte
{
    Var,
    Let,
    Const,
}

public class JsNode
{
    public JsKind Kind;
    public int Line;

    public JsNode(JsKind kind, int line) { Kind = kind; Line = line; }
}

// ---- expressions ----------------------------------------------------------

public sealed class JsLiteral : JsNode
{
    public object Value;
    public JsLiteral(object v, int line) : base(JsKind.Literal, line) { Value = v; }
}

public sealed class JsIdentifier : JsNode
{
    public string Name;
    public JsIdentifier(string name, int line) : base(JsKind.Identifier, line) { Name = name; }
}

public sealed class JsThis : JsNode
{
    public JsThis(int line) : base(JsKind.This, line) { }
}

public sealed class JsSuper : JsNode
{
    public JsSuper(int line) : base(JsKind.Super, line) { }
}

public sealed class JsArrayLit : JsNode
{
    public List<JsNode> Elements = new List<JsNode>(); // null = elision
    public JsArrayLit(int line) : base(JsKind.ArrayLit, line) { }
}

public sealed class JsObjectLit : JsNode
{
    public List<JsNode> Props = new List<JsNode>(); // JsObjectProp / JsSpread
    public JsObjectLit(int line) : base(JsKind.ObjectLit, line) { }
}

public sealed class JsObjectProp : JsNode
{
    public JsNode Key;          // expression (identifier name, string literal, computed expr)
    public JsNode Value;        // expression or function
    public bool Computed;
    public bool Shorthand;
    public byte PropKind;       // 0=init, 1=get, 2=set
    public JsObjectProp(int line) : base(JsKind.ObjectProp, line) { }
}

public sealed class JsSpread : JsNode
{
    public JsNode Argument;
    public JsSpread(JsNode arg, int line) : base(JsKind.Spread, line) { Argument = arg; }
}

public sealed class JsFunctionNode : JsNode
{
    public string Name;
    public List<JsNode> Params = new List<JsNode>();
    public JsNode Body;              // JsBlock or expression
    public bool IsExpressionBody;    // concise arrow body
    public bool Strict;
    public bool IsGenerator;
    public bool IsAsync;             // parsed but unsupported
    public bool IsMethod;            // shorthand/class method (sets [[HomeObject]])
    public JsFunctionNode(JsKind kind, int line) : base(kind, line) { }
}

public sealed class JsUnary : JsNode
{
    public string Op;
    public JsNode Operand;
    public bool Prefix;
    public JsUnary(string op, JsNode operand, bool prefix, int line)
        : base(JsKind.Unary, line) { Op = op; Operand = operand; Prefix = prefix; }
}

public sealed class JsUpdate : JsNode
{
    public string Op;   // ++ or --
    public JsNode Target;
    public bool Prefix;
    public JsUpdate(string op, JsNode target, bool prefix, int line)
        : base(JsKind.Update, line) { Op = op; Target = target; Prefix = prefix; }
}

public sealed class JsBinary : JsNode
{
    public string Op;
    public JsNode Left, Right;
    public JsBinary(string op, JsNode l, JsNode r, int line)
        : base(JsKind.Binary, line) { Op = op; Left = l; Right = r; }
}

public sealed class JsLogical : JsNode
{
    public string Op;   // && || ??
    public JsNode Left, Right;
    public JsLogical(string op, JsNode l, JsNode r, int line)
        : base(JsKind.Logical, line) { Op = op; Left = l; Right = r; }
}

public sealed class JsAssignment : JsNode
{
    public string Op;   // = += -= etc
    public JsNode Target;
    public JsNode Value;
    public JsAssignment(string op, JsNode target, JsNode value, int line)
        : base(JsKind.Assignment, line) { Op = op; Target = target; Value = value; }
}

public sealed class JsConditional : JsNode
{
    public JsNode Test, Cons, Alt;
    public JsConditional(JsNode t, JsNode c, JsNode a, int line)
        : base(JsKind.Conditional, line) { Test = t; Cons = c; Alt = a; }
}

public sealed class JsCall : JsNode
{
    public JsNode Callee;
    public List<JsNode> Args = new List<JsNode>();
    public bool Optional; // ?.()
    public JsCall(JsNode callee, int line) : base(JsKind.Call, line) { Callee = callee; }
}

public sealed class JsNew : JsNode
{
    public JsNode Callee;
    public List<JsNode> Args = new List<JsNode>();
    public JsNew(JsNode callee, int line) : base(JsKind.New, line) { Callee = callee; }
}

public sealed class JsMember : JsNode
{
    public JsNode Object;
    public JsNode Property;  // expression when Computed, JsIdentifier (name) otherwise
    public bool Computed;
    public bool Optional;    // ?.
    public JsMember(JsNode obj, JsNode prop, bool computed, int line)
        : base(JsKind.Member, line) { Object = obj; Property = prop; Computed = computed; }
}

public sealed class JsSequence : JsNode
{
    public List<JsNode> List = new List<JsNode>();
    public JsSequence(int line) : base(JsKind.Sequence, line) { }
}

public sealed class JsTemplate : JsNode
{
    public List<string> Quasis = new List<string>();   // cooked strings, count = Exprs.Count+1
    public List<JsNode> Exprs = new List<JsNode>();
    public JsTemplate(int line) : base(JsKind.Template, line) { }
}

public sealed class JsTaggedTemplate : JsNode
{
    public JsNode Tag;
    public JsTemplate Template;
    public JsTaggedTemplate(JsNode tag, JsTemplate tpl, int line)
        : base(JsKind.TaggedTemplate, line) { Tag = tag; Template = tpl; }
}

public sealed class JsClass : JsNode
{
    public string Name;
    public JsNode Super;
    public List<JsNode> Members = new List<JsNode>(); // JsClassMember
    public JsClass(int line) : base(JsKind.Class, line) { }
}

public sealed class JsClassMember : JsNode
{
    public JsNode Key;
    public JsFunctionNode Value;   // method / getter / setter / constructor
    public bool Computed;
    public bool IsStatic;
    public byte MemberKind;        // 0=method, 1=get, 2=set, 3=constructor
    public JsClassMember(int line) : base(JsKind.ClassMember, line) { }
}

// ---- binding patterns -----------------------------------------------------

public sealed class JsObjectPat : JsNode
{
    public List<JsNode> Props = new List<JsNode>(); // JsPatProp
    public JsNode Rest;   // may be null
    public JsObjectPat(int line) : base(JsKind.ObjectPat, line) { }
}

public sealed class JsArrayPat : JsNode
{
    public List<JsNode> Elements = new List<JsNode>(); // may contain null (elision) or patterns
    public JsNode Rest;
    public JsArrayPat(int line) : base(JsKind.ArrayPat, line) { }
}

public sealed class JsAssignPat : JsNode
{
    public JsNode Target;
    public JsNode Default;
    public JsAssignPat(JsNode target, JsNode def, int line)
        : base(JsKind.AssignPat, line) { Target = target; Default = def; }
}

public sealed class JsRestPat : JsNode
{
    public JsNode Value;
    public JsRestPat(JsNode value, int line) : base(JsKind.RestPat, line) { Value = value; }
}

public sealed class JsPatProp : JsNode
{
    public JsNode Key;
    public JsNode Value;   // pattern (identifier / objectpat / arraypat / assignpat)
    public bool Computed;
    public JsPatProp(int line) : base(JsKind.PatProp, line) { }
}

// ---- statements -----------------------------------------------------------

public sealed class JsBlock : JsNode
{
    public List<JsNode> Body = new List<JsNode>();
    public JsBlock(int line) : base(JsKind.Block, line) { }
}

public sealed class JsVarDecl : JsNode
{
    public JsDeclKind DeclKind;
    public List<JsNode> Decls = new List<JsNode>(); // JsDeclarator
    public JsVarDecl(JsDeclKind kind, int line) : base(JsKind.VarDecl, line) { DeclKind = kind; }
}

public sealed class JsDeclarator : JsNode
{
    public JsNode Target; // pattern
    public JsNode Init;   // may be null
    public JsDeclarator(int line) : base(JsKind.Declarator, line) { }
}

public sealed class JsFunctionDecl : JsNode
{
    public JsFunctionNode Fn;
    public JsFunctionDecl(JsFunctionNode fn, int line) : base(JsKind.FunctionDecl, line) { Fn = fn; }
}

public sealed class JsExprStmt : JsNode
{
    public JsNode Expression;
    public JsExprStmt(JsNode expr, int line) : base(JsKind.ExprStmt, line) { Expression = expr; }
}

public sealed class JsIf : JsNode
{
    public JsNode Test, Cons, Alt;
    public JsIf(JsNode t, JsNode c, JsNode a, int line)
        : base(JsKind.If, line) { Test = t; Cons = c; Alt = a; }
}

public sealed class JsWhile : JsNode
{
    public JsNode Test; public JsNode Body;
    public JsWhile(JsNode t, JsNode b, int line) : base(JsKind.While, line) { Test = t; Body = b; }
}

public sealed class JsDoWhile : JsNode
{
    public JsNode Body; public JsNode Test;
    public JsDoWhile(JsNode b, JsNode t, int line) : base(JsKind.DoWhile, line) { Body = b; Test = t; }
}

public sealed class JsFor : JsNode
{
    public JsNode Init;   // JsVarDecl or expression or null
    public JsNode Test;   // expression or null
    public JsNode Update; // expression or null
    public JsNode Body;
    public JsFor(int line) : base(JsKind.For, line) { }
}

public sealed class JsForIn : JsNode
{
    public JsNode Left;   // JsVarDecl or pattern/target
    public JsNode Right;
    public JsNode Body;
    public bool IsOf;
    public JsForIn(int line) : base(JsKind.ForIn, line) { }
}

public sealed class JsReturn : JsNode
{
    public JsNode Argument; // may be null
    public JsReturn(JsNode arg, int line) : base(JsKind.Return, line) { Argument = arg; }
}

public sealed class JsBreak : JsNode
{
    public string Label; // may be null
    public JsBreak(string label, int line) : base(JsKind.Break, line) { Label = label; }
}

public sealed class JsContinue : JsNode
{
    public string Label;
    public JsContinue(string label, int line) : base(JsKind.Continue, line) { Label = label; }
}

public sealed class JsThrow : JsNode
{
    public JsNode Argument;
    public JsThrow(JsNode arg, int line) : base(JsKind.Throw, line) { Argument = arg; }
}

public sealed class JsTry : JsNode
{
    public JsNode Body;        // JsBlock
    public JsNode Handler;     // JsCatch or null
    public JsNode Finalizer;   // JsBlock or null
    public JsTry(int line) : base(JsKind.Try, line) { }
}

public sealed class JsCatch : JsNode
{
    public JsNode Param;   // pattern or null (optional catch binding)
    public JsNode Block;
    public JsCatch(int line) : base(JsKind.Catch, line) { }
}

public sealed class JsSwitch : JsNode
{
    public JsNode Discriminant;
    public List<JsNode> Cases = new List<JsNode>(); // JsSwitchCase
    public JsSwitch(int line) : base(JsKind.Switch, line) { }
}

public sealed class JsSwitchCase : JsNode
{
    public JsNode Test; // null = default
    public List<JsNode> Body = new List<JsNode>();
    public JsSwitchCase(int line) : base(JsKind.SwitchCase, line) { }
}

public sealed class JsLabeled : JsNode
{
    public string Label;
    public JsNode Body;
    public JsLabeled(string label, JsNode body, int line)
        : base(JsKind.Labeled, line) { Label = label; Body = body; }
}

public sealed class JsEmpty : JsNode
{
    public JsEmpty(int line) : base(JsKind.Empty, line) { }
}

public sealed class JsClassDecl : JsNode
{
    public JsClass Class;
    public JsClassDecl(JsClass cls, int line) : base(JsKind.ClassDecl, line) { Class = cls; }
}

public sealed class JsProgram : JsNode
{
    public List<JsNode> Body = new List<JsNode>();
    public JsProgram(int line) : base(JsKind.Program, line) { }
}
