using System;
using System.Collections.Generic;

// ---------------------------------------------------------------------------
// Recursive-descent parser (precedence climbing for expressions). Emits the
// JsAst node tree. Handles ES5 plus major ES2015+: arrow functions (with
// backtracking), classes, template literals, let/const, destructuring
// (declaration, assignment and parameters), default/rest parameters, spread,
// computed/shorthand properties, for-of, labeled statements, and the `??`
// nullish and optional `?.` operators.
// ---------------------------------------------------------------------------

public static class JsParser
{
    private sealed class P
    {
        public List<JsToken> Toks;
        public int Pos;
        public bool NoIn; // suppress `in` as a binary operator (for-statement init)
    }

    // ---- entry points -----------------------------------------------------

    public static JsProgram ParseProgram(string src)
    {
        P p = NewParser(src);
        JsProgram prog = new JsProgram(1);
        while (Cur(p).Type != JsTokenType.Eof)
        {
            prog.Body.Add(ParseStatement(p));
        }
        return prog;
    }

    private static P NewParser(string src)
    {
        List<JsToken> toks;
        try { toks = JsLexer.Lex(src); }
        catch (Exception ex) { throw new JsSyntaxError("Lexer error: " + ex.Message); }
        return new P { Toks = toks, Pos = 0, NoIn = false };
    }

    // ---- token helpers ----------------------------------------------------

    private static JsToken Cur(P p) { return p.Toks[p.Pos]; }
    private static JsToken Peek(P p, int n)
    {
        int i = p.Pos + n;
        if (i >= p.Toks.Count) return p.Toks[p.Toks.Count - 1];
        return p.Toks[i];
    }

    private static JsToken Advance(P p)
    {
        JsToken t = p.Toks[p.Pos];
        if (p.Pos < p.Toks.Count - 1) p.Pos++;
        return t;
    }

    private static bool IsPunct(P p, string op)
    {
        JsToken t = Cur(p);
        return t.Type == JsTokenType.Punct && (string)t.Value == op;
    }

    private static bool IsKeyword(P p, string kw)
    {
        JsToken t = Cur(p);
        return t.Type == JsTokenType.Keyword && (string)t.Value == kw;
    }

    private static bool IsIdent(P p, string name)
    {
        JsToken t = Cur(p);
        return t.Type == JsTokenType.Ident && (string)t.Value == name;
    }

    private static void Expect(P p, string op)
    {
        if (!IsPunct(p, op)) Fail(p, "expected '" + op + "' but found " + Describe(Cur(p)));
        Advance(p);
    }

    private static string Describe(JsToken t)
    {
        if (t.Type == JsTokenType.Eof) return "end of input";
        if (t.Value != null) return "'" + t.Value + "'";
        return t.Type.ToString();
    }

    private static void Fail(P p, string msg)
    {
        throw new JsSyntaxError("Line " + Cur(p).Line + ": " + msg);
    }

    private static string IdentName(P p)
    {
        JsToken t = Cur(p);
        if (t.Type != JsTokenType.Ident && t.Type != JsTokenType.Keyword)
            Fail(p, "expected an identifier but found " + Describe(t));
        Advance(p);
        return (string)t.Value;
    }

    // ---- statements -------------------------------------------------------

    private static JsNode ParseStatement(P p)
    {
        JsToken t = Cur(p);
        int line = t.Line;

        if (t.Type == JsTokenType.Punct)
        {
            switch ((string)t.Value)
            {
                case "{": return ParseBlock(p);
                case ";": Advance(p); return new JsEmpty(line);
            }
        }

        if (t.Type == JsTokenType.Keyword)
        {
            switch ((string)t.Value)
            {
                case "var": return ParseVarDecl(p, JsDeclKind.Var);
                case "const": return ParseVarDecl(p, JsDeclKind.Const);
                case "function":
                    {
                        JsFunctionNode fn = ParseFunction(p, true);
                        return new JsFunctionDecl(fn, line);
                    }
                case "class":
                    {
                        JsClass cls = ParseClass(p);
                        return new JsClassDecl(cls, line);
                    }
                case "if": return ParseIf(p);
                case "while": return ParseWhile(p);
                case "do": return ParseDoWhile(p);
                case "for": return ParseFor(p);
                case "switch": return ParseSwitch(p);
                case "try": return ParseTry(p);
                case "throw": return ParseThrow(p);
                case "return": return ParseReturn(p);
                case "break": return ParseBreak(p);
                case "continue": return ParseContinue(p);
                case "debugger": Advance(p); AcceptSemi(p); return new JsDebuggerNode(line);
                case "with": return ParseWith(p);
            }
        }

        // `let` / `const` are contextual keywords
        if (t.Type == JsTokenType.Ident)
        {
            string word = (string)t.Value;
            if (word == "let")
            {
                JsToken n = Peek(p, 1);
                if (n.Type == JsTokenType.Ident || (n.Type == JsTokenType.Punct && ((string)n.Value == "[" || (string)n.Value == "{")))
                    return ParseVarDecl(p, JsDeclKind.Let);
            }
            else if (word == "const")
            {
                // `const` is a reserved word but tolerate as decl
                JsToken n = Peek(p, 1);
                if (n.Type != JsTokenType.Punct || (string)n.Value != ":")
                    return ParseVarDecl(p, JsDeclKind.Const);
            }
            else if (word == "async")
            {
                JsToken n = Peek(p, 1);
                if (n.Type == JsTokenType.Keyword && (string)n.Value == "function")
                {
                    JsFunctionNode fn = ParseFunction(p, true); // async ignored (runs synchronously)
                    return new JsFunctionDecl(fn, line);
                }
            }
            // labeled statement: Ident ':'
            if (Peek(p, 1).Type == JsTokenType.Punct && (string)Peek(p, 1).Value == ":")
            {
                string label = (string)Advance(p).Value;
                Advance(p); // ':'
                JsNode body = Cur(p).Type == JsTokenType.Ident && (string)Cur(p).Value == "function"
                    ? new JsFunctionDecl(ParseFunction(p, true), line)
                    : ParseStatement(p);
                return new JsLabeled(label, body, line);
            }
        }

        // expression statement
        JsNode expr = ParseExpression(p);
        AcceptSemi(p);
        return new JsExprStmt(expr, line);
    }

    private static void AcceptSemi(P p)
    {
        if (IsPunct(p, ";")) { Advance(p); return; }
        // ASI: allow omission before }, EOF or a line break
        JsToken t = Cur(p);
        if (t.Type == JsTokenType.Punct && (string)t.Value == "}") return;
        if (t.Type == JsTokenType.Eof) return;
        Fail(p, "expected ';' but found " + Describe(t));
    }

    private static JsNode ParseBlock(P p)
    {
        int line = Cur(p).Line;
        Expect(p, "{");
        JsBlock block = new JsBlock(line);
        while (!IsPunct(p, "}") && Cur(p).Type != JsTokenType.Eof)
            block.Body.Add(ParseStatement(p));
        Expect(p, "}");
        return block;
    }

    private static JsNode ParseVarDecl(P p, JsDeclKind kind)
    {
        int line = Cur(p).Line;
        Advance(p); // var / let / const
        JsVarDecl decl = new JsVarDecl(kind, line);
        while (true)
        {
            int dline = Cur(p).Line;
            JsDeclarator d = new JsDeclarator(dline);
            d.Target = ParseBindingTarget(p);
            if (IsPunct(p, "="))
            {
                Advance(p);
                d.Init = ParseAssignment(p);
            }
            else if (kind != JsDeclKind.Var && kind != JsDeclKind.Let && d.Target.Kind != JsKind.Identifier)
            {
                // const without initialiser is a syntax error
                Fail(p, "const declaration requires an initialiser");
            }
            decl.Decls.Add(d);
            if (IsPunct(p, ",")) { Advance(p); continue; }
            break;
        }
        AcceptSemi(p);
        return decl;
    }

    private static JsNode ParseIf(P p)
    {
        int line = Advance(p).Line; // if
        Expect(p, "(");
        JsNode test = ParseExpression(p);
        Expect(p, ")");
        JsNode cons = ParseStatement(p);
        JsNode alt = null;
        if (IsKeyword(p, "else")) { Advance(p); alt = ParseStatement(p); }
        return new JsIf(test, cons, alt, line);
    }

    private static JsNode ParseWhile(P p)
    {
        int line = Advance(p).Line;
        Expect(p, "(");
        JsNode test = ParseExpression(p);
        Expect(p, ")");
        JsNode body = ParseStatement(p);
        return new JsWhile(test, body, line);
    }

    private static JsNode ParseDoWhile(P p)
    {
        int line = Advance(p).Line;
        JsNode body = ParseStatement(p);
        if (!IsKeyword(p, "while")) Fail(p, "expected 'while' after do-body");
        Advance(p);
        Expect(p, "(");
        JsNode test = ParseExpression(p);
        Expect(p, ")");
        if (IsPunct(p, ";")) Advance(p);
        return new JsDoWhile(body, test, line);
    }

    private static JsNode ParseFor(P p)
    {
        int line = Advance(p).Line; // for
        bool isAwait = false;
        if (IsIdent(p, "await")) { Advance(p); isAwait = true; } // tolerated, no-op
        Expect(p, "(");

        // empty init
        if (IsPunct(p, ";"))
        {
            Advance(p);
            return ParseForTail(p, null, line);
        }

        bool saveNoIn = p.NoIn;
        p.NoIn = true;

        // var/let/const declaration (possibly for-in/of)
        if (IsKeyword(p, "var") || IsKeyword(p, "const") ||
            (IsIdent(p, "let") && (Peek(p, 1).Type == JsTokenType.Ident)))
        {
            JsDeclKind dk = IsKeyword(p, "var") ? JsDeclKind.Var
                : IsKeyword(p, "const") ? JsDeclKind.Const : JsDeclKind.Let;
            Advance(p);
            JsVarDecl decl = new JsVarDecl(dk, line);
            while (true)
            {
                int dline = Cur(p).Line;
                JsDeclarator d = new JsDeclarator(dline);
                d.Target = ParseBindingTarget(p);
                if (IsPunct(p, "="))
                {
                    Advance(p);
                    d.Init = ParseAssignment(p);
                }
                decl.Decls.Add(d);
                if (IsPunct(p, ",")) { Advance(p); continue; }
                break;
            }
            p.NoIn = saveNoIn;

            if (IsKeyword(p, "in") || IsIdent(p, "of"))
            {
                bool isOf = IsIdent(p, "of");
                Advance(p);
                JsNode right = ParseExpression(p);
                Expect(p, ")");
                JsNode fbody = ParseStatement(p);
                JsForIn fo = new JsForIn(line);
                fo.Left = decl;
                fo.Right = right;
                fo.Body = fbody;
                fo.IsOf = isOf;
                return fo;
            }

            // classic for with declaration init
            return ParseForTail(p, decl, line);
        }

        // expression init (possibly for-in/of)
        JsNode initExpr = IsPunct(p, ";") ? null : ParseExpression(p);
        p.NoIn = saveNoIn;

        if (initExpr != null && (IsKeyword(p, "in") || IsIdent(p, "of")))
        {
            bool isOf = IsIdent(p, "of");
            Advance(p);
            JsNode right = ParseExpression(p);
            Expect(p, ")");
            JsNode fbody = ParseStatement(p);
            JsForIn fo = new JsForIn(line);
            fo.Left = initExpr;
            fo.Right = right;
            fo.Body = fbody;
            fo.IsOf = isOf;
            return fo;
        }

        return ParseForTail(p, initExpr, line);
    }

    private static JsNode ParseForTail(P p, JsNode init, int line)
    {
        // init already consumed up to here; expect ';' (or ')' for `for(;;)` after empty init handled)
        if (IsPunct(p, ";")) Advance(p);
        JsNode test = IsPunct(p, ";") ? null : ParseExpression(p);
        if (IsPunct(p, ";")) Advance(p);
        JsNode update = IsPunct(p, ")") ? null : ParseExpression(p);
        Expect(p, ")");
        JsNode body = ParseStatement(p);
        JsFor f = new JsFor(line);
        f.Init = init;
        f.Test = test;
        f.Update = update;
        f.Body = body;
        return f;
    }

    private static JsNode ParseSwitch(P p)
    {
        int line = Advance(p).Line;
        Expect(p, "(");
        JsNode disc = ParseExpression(p);
        Expect(p, ")");
        Expect(p, "{");
        JsSwitch sw = new JsSwitch(line);
        sw.Discriminant = disc;
        while (!IsPunct(p, "}") && Cur(p).Type != JsTokenType.Eof)
        {
            int cl = Cur(p).Line;
            JsSwitchCase sc = new JsSwitchCase(cl);
            if (IsKeyword(p, "case"))
            {
                Advance(p);
                sc.Test = ParseExpression(p);
                Expect(p, ":");
            }
            else if (IsKeyword(p, "default"))
            {
                Advance(p);
                Expect(p, ":");
                sc.Test = null;
            }
            else Fail(p, "expected 'case' or 'default'");
            while (!IsKeyword(p, "case") && !IsKeyword(p, "default") && !IsPunct(p, "}") && Cur(p).Type != JsTokenType.Eof)
                sc.Body.Add(ParseStatement(p));
            sw.Cases.Add(sc);
        }
        Expect(p, "}");
        return sw;
    }

    private static JsNode ParseTry(P p)
    {
        int line = Advance(p).Line;
        JsTry tr = new JsTry(line);
        tr.Body = ParseBlock(p);
        if (IsKeyword(p, "catch"))
        {
            int cl = Advance(p).Line;
            JsCatch ct = new JsCatch(cl);
            if (IsPunct(p, "("))
            {
                Advance(p);
                ct.Param = ParseBindingTarget(p);
                Expect(p, ")");
            }
            ct.Block = ParseBlock(p);
            tr.Handler = ct;
        }
        if (IsKeyword(p, "finally"))
        {
            Advance(p);
            tr.Finalizer = ParseBlock(p);
        }
        if (tr.Handler == null && tr.Finalizer == null)
            Fail(p, "try requires catch or finally");
        return tr;
    }

    private static JsNode ParseThrow(P p)
    {
        int line = Advance(p).Line;
        JsNode arg = ParseExpression(p);
        AcceptSemi(p);
        return new JsThrow(arg, line);
    }

    private static JsNode ParseReturn(P p)
    {
        int line = Advance(p).Line;
        JsNode arg = null;
        if (!IsPunct(p, ";") && !IsPunct(p, "}") && Cur(p).Type != JsTokenType.Eof)
        {
            // don't allow `return\n expr` (ASI) but we keep it simple
            arg = ParseExpression(p);
        }
        AcceptSemi(p);
        return new JsReturn(arg, line);
    }

    private static JsNode ParseBreak(P p)
    {
        int line = Advance(p).Line;
        string label = null;
        if (Cur(p).Type == JsTokenType.Ident) label = (string)Advance(p).Value;
        AcceptSemi(p);
        return new JsBreak(label, line);
    }

    private static JsNode ParseContinue(P p)
    {
        int line = Advance(p).Line;
        string label = null;
        if (Cur(p).Type == JsTokenType.Ident) label = (string)Advance(p).Value;
        AcceptSemi(p);
        return new JsContinue(label, line);
    }

    private static JsNode ParseWith(P p)
    {
        // `with` is unsupported; parse the header and treat the body in the current scope.
        int line = Advance(p).Line;
        Expect(p, "(");
        ParseExpression(p);
        Expect(p, ")");
        JsNode body = ParseStatement(p);
        return body;
    }

    // ---- functions / classes ---------------------------------------------

    private static JsFunctionNode ParseFunction(P p, bool isDecl)
    {
        int line = Cur(p).Line;
        Advance(p); // function
        bool isGen = false;
        if (IsPunct(p, "*")) { Advance(p); isGen = true; }
        string name = null;
        if (Cur(p).Type == JsTokenType.Ident) name = (string)Advance(p).Value;
        else if (isDecl) name = "";

        JsFunctionNode fn = new JsFunctionNode(JsKind.Function, line);
        fn.Name = name ?? "";
        fn.IsGenerator = isGen;
        ParseParamsInto(p, fn);
        fn.Body = ParseBlock(p);
        return fn;
    }

    private static void ParseParamsInto(P p, JsFunctionNode fn)
    {
        Expect(p, "(");
        while (!IsPunct(p, ")") && Cur(p).Type != JsTokenType.Eof)
        {
            if (IsPunct(p, "..."))
            {
                int rl = Advance(p).Line;
                JsNode rest = ParseBindingTarget(p);
                fn.Params.Add(new JsRestPat(rest, rl));
            }
            else
            {
                fn.Params.Add(ParseBindingElement(p));
            }
            if (IsPunct(p, ",")) Advance(p);
            else break;
        }
        Expect(p, ")");
    }

    private static JsClass ParseClass(P p)
    {
        int line = Advance(p).Line; // class
        JsClass cls = new JsClass(line);
        if (Cur(p).Type == JsTokenType.Ident) cls.Name = (string)Advance(p).Value;
        if (IsKeyword(p, "extends"))
        {
            Advance(p);
            cls.Super = ParseCallChain(p);
        }
        Expect(p, "{");
        while (!IsPunct(p, "}") && Cur(p).Type != JsTokenType.Eof)
            cls.Members.Add(ParseClassMember(p));
        Expect(p, "}");
        return cls;
    }

    private static JsNode ParseClassMember(P p)
    {
        int line = Cur(p).Line;
        bool isStatic = false;
        bool isAsync = false;
        bool isGen = false;

        // `static`
        if (IsIdent(p, "static"))
        {
            JsToken n = Peek(p, 1);
            bool accessorNext = (n.Type == JsTokenType.Ident && ((string)n.Value == "get" || (string)n.Value == "set"));
            bool nameNext = !(n.Type == JsTokenType.Punct && ((string)n.Value == "(" || (string)n.Value == "{"));
            if (!accessorNext && nameNext) { Advance(p); isStatic = true; }
        }
        // `async`
        if (IsIdent(p, "async"))
        {
            JsToken n = Peek(p, 1);
            if (!(n.Type == JsTokenType.Punct && ((string)n.Value == "(" || (string)n.Value == "*")))
            {
                Advance(p); isAsync = true;
                if (IsPunct(p, "*")) { Advance(p); isGen = true; }
            }
        }
        if (IsPunct(p, "*")) { Advance(p); isGen = true; }

        byte memberKind = 0; // 0 method, 1 get, 2 set, 3 constructor
        if ((IsIdent(p, "get") || IsIdent(p, "set")))
        {
            JsToken n = Peek(p, 1);
            bool notCall = !(n.Type == JsTokenType.Punct && ((string)n.Value == "(" || (string)n.Value == "*"));
            bool notAsync = !(n.Type == JsTokenType.Ident && (string)n.Value == "async");
            if (notCall && notAsync)
            {
                memberKind = IsIdent(p, "get") ? (byte)1 : (byte)2;
                Advance(p);
            }
        }

        // property name
        JsNode key = ParsePropName(p, out bool keyComputed);
        bool isCtor = false;
        if (memberKind == 0 && !isStatic && !isAsync && !isGen
            && key.Kind == JsKind.Literal && key is JsLiteral lit && lit.Value is string s && s == "constructor")
        {
            isCtor = true;
        }

        JsFunctionNode method = new JsFunctionNode(JsKind.Function, line);
        method.IsMethod = true;
        method.IsAsync = isAsync;
        method.IsGenerator = isGen;
        ParseParamsInto(p, method);
        method.Body = ParseBlock(p);

        JsClassMember m = new JsClassMember(line);
        m.Key = key;
        m.Value = method;
        m.IsStatic = isStatic;
        m.Computed = keyComputed;
        m.MemberKind = isCtor ? (byte)3 : memberKind;
        return m;
    }

    private static JsNode ParsePropName(P p, out bool computed)
    {
        JsToken t = Cur(p);
        int line = t.Line;
        computed = false;
        if (t.Type == JsTokenType.Punct && (string)t.Value == "[")
        {
            Advance(p);
            JsNode e = ParseExpression(p);
            Expect(p, "]");
            computed = true;
            return e;
        }
        if (t.Type == JsTokenType.String) { Advance(p); return new JsLiteral((string)t.Value, line); }
        if (t.Type == JsTokenType.Number) { Advance(p); return new JsLiteral((double)t.Value, line); }
        if (t.Type == JsTokenType.Ident || t.Type == JsTokenType.Keyword)
        {
            Advance(p);
            return new JsLiteral((string)t.Value, line);
        }
        Fail(p, "expected a property name but found " + Describe(t));
        return null;
    }

    // ---- binding patterns -------------------------------------------------

    // Parse a binding target (no default): identifier, object or array pattern.
    private static JsNode ParseBindingTarget(P p)
    {
        if (IsPunct(p, "{")) return ParseObjectPattern(p);
        if (IsPunct(p, "[")) return ParseArrayPattern(p);
        if (Cur(p).Type == JsTokenType.Ident) return new JsIdentifier((string)Advance(p).Value, Cur(p).Line);
        // allow destructuring assignment targets like `a.b` only in assignment, not declaration
        Fail(p, "expected a binding name or pattern but found " + Describe(Cur(p)));
        return null;
    }

    // Parse a binding element: pattern + optional default.
    private static JsNode ParseBindingElement(P p)
    {
        int line = Cur(p).Line;
        JsNode target = ParseBindingTarget(p);
        if (IsPunct(p, "="))
        {
            Advance(p);
            JsNode def = ParseAssignment(p);
            return new JsAssignPat(target, def, line);
        }
        return target;
    }

    private static JsNode ParseObjectPattern(P p)
    {
        int line = Cur(p).Line;
        Expect(p, "{");
        JsObjectPat pat = new JsObjectPat(line);
        while (!IsPunct(p, "}") && Cur(p).Type != JsTokenType.Eof)
        {
            int pl = Cur(p).Line;
            if (IsPunct(p, "..."))
            {
                Advance(p);
                pat.Rest = ParseBindingTarget(p);
            }
            else
            {
                JsNode key = ParsePropName(p, out bool computed);
                JsNode value;
                if (IsPunct(p, ":"))
                {
                    Advance(p);
                    value = ParseBindingElement(p);
                }
                else
                {
                    // shorthand: {a} or {a = def}
                    JsLiteral klit = key as JsLiteral;
                    if (klit == null || !(klit.Value is string))
                        Fail(p, "shorthand object pattern requires an identifier key");
                    string kname = (string)klit.Value;
                    value = new JsIdentifier(kname, pl);
                    if (IsPunct(p, "="))
                    {
                        Advance(p);
                        JsNode def = ParseAssignment(p);
                        value = new JsAssignPat(value, def, pl);
                    }
                }
                JsPatProp prop = new JsPatProp(pl);
                prop.Key = key;
                prop.Value = value;
                prop.Computed = computed;
                pat.Props.Add(prop);
            }
            if (IsPunct(p, ",")) Advance(p);
            else break;
        }
        Expect(p, "}");
        return pat;
    }

    private static JsNode ParseArrayPattern(P p)
    {
        int line = Cur(p).Line;
        Expect(p, "[");
        JsArrayPat pat = new JsArrayPat(line);
        while (!IsPunct(p, "]") && Cur(p).Type != JsTokenType.Eof)
        {
            if (IsPunct(p, ","))
            {
                Advance(p);
                pat.Elements.Add(null); // elision
                continue;
            }
            if (IsPunct(p, "..."))
            {
                int rl = Advance(p).Line;
                pat.Rest = new JsRestPat(ParseBindingTarget(p), rl);
            }
            else
            {
                pat.Elements.Add(ParseBindingElement(p));
            }
            if (IsPunct(p, ",")) Advance(p);
            else break;
        }
        Expect(p, "]");
        return pat;
    }

    // ---- expressions ------------------------------------------------------

    private static JsNode ParseExpression(P p)
    {
        JsNode first = ParseAssignment(p);
        if (!IsPunct(p, ",")) return first;
        JsSequence seq = new JsSequence(first.Line);
        seq.List.Add(first);
        while (IsPunct(p, ","))
        {
            Advance(p);
            seq.List.Add(ParseAssignment(p));
        }
        return seq;
    }

    private static bool IsAssignOp(string op)
    {
        switch (op)
        {
            case "=": case "+=": case "-=": case "*=": case "/=": case "%=":
            case "&=": case "|=": case "^=": case "<<=": case ">>=": case ">>>=":
            case "**=": case "??=": case "&&=": case "||=":
                return true;
            default:
                return false;
        }
    }

    private static JsNode ParseAssignment(P p)
    {
        // object destructuring with a following `=`
        if (IsPunct(p, "{"))
        {
            int save = p.Pos;
            JsNode pat = TryParsePattern(p, () => ParseObjectPattern(p));
            if (pat != null && IsPunct(p, "="))
            {
                Advance(p);
                JsNode rhs = ParseAssignment(p);
                return new JsAssignment("=", pat, rhs, Cur(p).Line);
            }
            p.Pos = save;
        }

        // arrow functions
        JsNode arrow = TryParseArrow(p);
        if (arrow != null) return arrow;

        JsNode left = ParseConditional(p);

        JsToken t = Cur(p);
        if (t.Type == JsTokenType.Punct && IsAssignOp((string)t.Value))
        {
            string op = (string)t.Value;
            Advance(p);
            JsNode rhs = ParseAssignment(p);
            if (op == "=" && (left.Kind == JsKind.ObjectLit || left.Kind == JsKind.ArrayLit))
                left = ExprToPattern(left);
            return new JsAssignment(op, left, rhs, t.Line);
        }
        return left;
    }

    private static JsNode TryParsePattern(P p, Func<JsNode> parse)
    {
        try { return parse(); }
        catch (JsSyntaxError) { return null; }
    }

    private static JsNode ExprToPattern(JsNode e)
    {
        switch (e.Kind)
        {
            case JsKind.Identifier:
                return e;
            case JsKind.Member:
                return e;
            case JsKind.ObjectLit:
                {
                    JsObjectLit lit = (JsObjectLit)e;
                    JsObjectPat pat = new JsObjectPat(lit.Line);
                    for (int i = 0; i < lit.Props.Count; i++)
                    {
                        JsNode pn = lit.Props[i];
                        if (pn.Kind == JsKind.Spread)
                        {
                            pat.Rest = ExprToPattern(((JsSpread)pn).Argument);
                            continue;
                        }
                        JsObjectProp pr = (JsObjectProp)pn;
                        JsPatProp pp = new JsPatProp(pr.Line);
                        pp.Key = pr.Key;
                        pp.Value = ExprToPattern(pr.Value);
                        pp.Computed = pr.Computed;
                        pat.Props.Add(pp);
                    }
                    return pat;
                }
            case JsKind.ArrayLit:
                {
                    JsArrayLit lit = (JsArrayLit)e;
                    JsArrayPat pat = new JsArrayPat(lit.Line);
                    for (int i = 0; i < lit.Elements.Count; i++)
                    {
                        JsNode el = lit.Elements[i];
                        if (el == null) { pat.Elements.Add(null); continue; }
                        if (el.Kind == JsKind.Spread) { pat.Rest = ExprToPattern(((JsSpread)el).Argument); continue; }
                        pat.Elements.Add(ExprToPattern(el));
                    }
                    return pat;
                }
            case JsKind.Assignment:
                {
                    JsAssignment a = (JsAssignment)e;
                    return new JsAssignPat(ExprToPattern(a.Target), a.Value, a.Line);
                }
            case JsKind.Spread:
                return new JsRestPat(ExprToPattern(((JsSpread)e).Argument), e.Line);
            default:
                return e; // literals etc. (invalid as binding; will error at runtime)
        }
    }

    private static JsNode ParseConditional(P p)
    {
        JsNode test = ParseBinary(p, 3);
        if (IsPunct(p, "?"))
        {
            int line = Advance(p).Line;
            JsNode cons = ParseAssignment(p);
            Expect(p, ":");
            JsNode alt = ParseAssignment(p);
            return new JsConditional(test, cons, alt, line);
        }
        return test;
    }

    private static int Prec(string op)
    {
        switch (op)
        {
            case "||": case "??": return 3;
            case "&&": return 4;
            case "|": return 5;
            case "^": return 6;
            case "&": return 7;
            case "==": case "!=": case "===": case "!==": return 8;
            case "<": case ">": case "<=": case ">=": case "instanceof": case "in": return 9;
            case "<<": case ">>": case ">>>": return 10;
            case "+": case "-": return 11;
            case "*": case "/": case "%": return 12;
            case "**": return 13;
            default: return -1;
        }
    }

    private static JsNode ParseBinary(P p, int minPrec)
    {
        JsNode left = ParseUnary(p);
        while (true)
        {
            JsToken t = Cur(p);
            string op = null;
            if (t.Type == JsTokenType.Punct) op = (string)t.Value;
            else if (t.Type == JsTokenType.Keyword && ((string)t.Value == "instanceof" || (string)t.Value == "in")) op = (string)t.Value;
            if (op == null) break;
            if (op == "in" && p.NoIn) break;
            int prec = Prec(op);
            if (prec < minPrec) break;
            Advance(p);
            JsNode right;
            if (op == "**") right = ParseBinary(p, prec);       // right-assoc
            else right = ParseBinary(p, prec + 1);
            if (op == "&&" || op == "||" || op == "??")
                left = new JsLogical(op, left, right, t.Line);
            else
                left = new JsBinary(op, left, right, t.Line);
        }
        return left;
    }

    private static JsNode ParseUnary(P p)
    {
        JsToken t = Cur(p);
        int line = t.Line;

        if (t.Type == JsTokenType.Punct)
        {
            string op = (string)t.Value;
            if (op == "!" || op == "~" || op == "+" || op == "-")
            {
                Advance(p);
                return new JsUnary(op, ParseUnary(p), true, line);
            }
            if (op == "++" || op == "--")
            {
                Advance(p);
                return new JsUpdate(op, ParseUnary(p), true, line);
            }
        }
        if (t.Type == JsTokenType.Keyword)
        {
            string kw = (string)t.Value;
            if (kw == "typeof" || kw == "void" || kw == "delete")
            {
                Advance(p);
                return new JsUnary(kw, ParseUnary(p), true, line);
            }
        }
        if (t.Type == JsTokenType.Ident && (string)t.Value == "await")
        {
            Advance(p);
            // async/await unsupported: evaluate operand and pass it through
            return new JsUnary("await", ParseUnary(p), true, line);
        }

        return ParsePostfix(p);
    }

    private static JsNode ParsePostfix(P p)
    {
        JsNode expr = ParseLeftHandSide(p);
        JsToken t = Cur(p);
        if (t.Type == JsTokenType.Punct && ((string)t.Value == "++" || (string)t.Value == "--"))
        {
            Advance(p);
            return new JsUpdate((string)t.Value, expr, false, t.Line);
        }
        return expr;
    }

    private static JsNode ParseLeftHandSide(P p)
    {
        if (IsKeyword(p, "new")) return ParseNewExpression(p);
        JsNode expr = ParsePrimary(p);
        return ParseMemberTail(p, expr, true);
    }

    private static JsNode ParseCallChain(P p)
    {
        if (IsKeyword(p, "new")) return ParseNewExpression(p);
        JsNode expr = ParsePrimary(p);
        return ParseMemberTail(p, expr, true);
    }

    private static JsNode ParseNewExpression(P p)
    {
        int line = Advance(p).Line; // new
        if (IsPunct(p, "."))
        {
            Advance(p);
            if (IsIdent(p, "target")) { Advance(p); return ParseMemberTail(p, new JsLiteral(JsUndefined.Instance, line), true); }
            Fail(p, "expected 'target' after 'new.'");
        }

        JsNode callee = ParseNewOrPrimaryNoCall(p);
        List<JsNode> args = new List<JsNode>();
        if (IsPunct(p, "(")) args = ParseArguments(p);
        JsNew node = new JsNew(callee, line);
        node.Args = args;
        return ParseMemberTail(p, node, true);
    }

    // Parses a `new` callee: primary + member access, plus nested `new X(...)`,
    // but stops before a `(` that would become the OUTER new's argument list.
    private static JsNode ParseNewOrPrimaryNoCall(P p)
    {
        if (IsKeyword(p, "new")) return ParseNewExpression(p);
        JsNode expr = ParsePrimary(p);
        return ParseMemberTail(p, expr, false); // no call / template consumption
    }

    private static JsNode ParseMemberTail(P p, JsNode expr, bool allowCalls)
    {
        while (true)
        {
            JsToken t = Cur(p);
            if (t.Type == JsTokenType.Punct)
            {
                string op = (string)t.Value;
                if (op == ".")
                {
                    Advance(p);
                    JsToken name = Cur(p);
                    if (name.Type != JsTokenType.Ident && name.Type != JsTokenType.Keyword)
                        Fail(p, "expected a property name after '.' but found " + Describe(name));
                    Advance(p);
                    expr = new JsMember(expr, new JsIdentifier((string)name.Value, name.Line), false, name.Line);
                    continue;
                }
                if (op == "?.")
                {
                    Advance(p);
                    if (IsPunct(p, "("))
                    {
                        int cl = Cur(p).Line;
                        List<JsNode> args = ParseArguments(p);
                        JsCall call = new JsCall(expr, cl);
                        call.Args = args;
                        call.Optional = true;
                        expr = call;
                    }
                    else if (IsPunct(p, "["))
                    {
                        int cl = Cur(p).Line;
                        Advance(p);
                        JsNode prop = ParseExpression(p);
                        Expect(p, "]");
                        JsMember m = new JsMember(expr, prop, true, cl);
                        m.Optional = true;
                        expr = m;
                    }
                    else
                    {
                        JsToken name = Cur(p);
                        if (name.Type != JsTokenType.Ident && name.Type != JsTokenType.Keyword)
                            Fail(p, "expected a property name after '?.' but found " + Describe(name));
                        Advance(p);
                        JsMember m = new JsMember(expr, new JsIdentifier((string)name.Value, name.Line), false, name.Line);
                        m.Optional = true;
                        expr = m;
                    }
                    continue;
                }
                if (op == "[")
                {
                    int cl = Advance(p).Line;
                    JsNode prop = ParseExpression(p);
                    Expect(p, "]");
                    expr = new JsMember(expr, prop, true, cl);
                    continue;
                }
                if (op == "(" && allowCalls)
                {
                    int cl = Cur(p).Line;
                    List<JsNode> args = ParseArguments(p);
                    JsCall call = new JsCall(expr, cl);
                    call.Args = args;
                    expr = call;
                    continue;
                }
            }

            if (allowCalls && (t.Type == JsTokenType.TmplNoSub || t.Type == JsTokenType.TmplStart))
            {
                JsTemplate tpl = ParseTemplateLiteral(p);
                JsTaggedTemplate tt = new JsTaggedTemplate(expr, tpl, t.Line);
                expr = tt;
                continue;
            }

            break;
        }
        return expr;
    }

    private static List<JsNode> ParseArguments(P p)
    {
        Expect(p, "(");
        List<JsNode> args = new List<JsNode>();
        while (!IsPunct(p, ")") && Cur(p).Type != JsTokenType.Eof)
        {
            if (IsPunct(p, "..."))
            {
                int sl = Advance(p).Line;
                args.Add(new JsSpread(ParseAssignment(p), sl));
            }
            else args.Add(ParseAssignment(p));
            if (IsPunct(p, ",")) Advance(p);
            else break;
        }
        Expect(p, ")");
        return args;
    }

    private static JsNode ParsePrimary(P p)
    {
        JsToken t = Cur(p);
        int line = t.Line;

        switch (t.Type)
        {
            case JsTokenType.Number:
                Advance(p);
                return new JsLiteral((double)t.Value, line);
            case JsTokenType.String:
                Advance(p);
                return new JsLiteral((string)t.Value, line);
            case JsTokenType.Regex:
                Advance(p);
                return new JsRegexLiteral((string)t.Value, t.Flags, line);
            case JsTokenType.TmplNoSub:
            case JsTokenType.TmplStart:
                return ParseTemplateLiteral(p);
        }

        if (t.Type == JsTokenType.Punct)
        {
            switch ((string)t.Value)
            {
                case "(":
                    {
                        Advance(p);
                        JsNode e = ParseExpression(p);
                        Expect(p, ")");
                        return e;
                    }
                case "[":
                    return ParseArrayLiteral(p);
                case "{":
                    return ParseObjectLiteral(p);
                case "...":
                    {
                        Advance(p);
                        return new JsSpread(ParseAssignment(p), line);
                    }
            }
        }

        if (t.Type == JsTokenType.Keyword)
        {
            switch ((string)t.Value)
            {
                case "this": Advance(p); return new JsThis(line);
                case "super": Advance(p); return new JsSuper(line);
                case "true": Advance(p); return new JsLiteral(true, line);
                case "false": Advance(p); return new JsLiteral(false, line);
                case "null": Advance(p); return new JsLiteral(JsNull.Instance, line);
                case "function":
                    {
                        JsFunctionNode fn = ParseFunction(p, false);
                        return fn;
                    }
                case "class":
                    {
                        JsClass cls = ParseClass(p);
                        return cls;
                    }
            }
            Fail(p, "unexpected keyword " + Describe(t));
        }

        if (t.Type == JsTokenType.Ident)
        {
            Advance(p);
            return new JsIdentifier((string)t.Value, line);
        }

        Fail(p, "unexpected token " + Describe(t));
        return null;
    }

    private static JsNode ParseArrayLiteral(P p)
    {
        int line = Advance(p).Line; // [
        JsArrayLit arr = new JsArrayLit(line);
        while (!IsPunct(p, "]") && Cur(p).Type != JsTokenType.Eof)
        {
            if (IsPunct(p, ","))
            {
                Advance(p);
                arr.Elements.Add(null); // elision
                continue;
            }
            if (IsPunct(p, "..."))
            {
                int sl = Advance(p).Line;
                arr.Elements.Add(new JsSpread(ParseAssignment(p), sl));
            }
            else
            {
                arr.Elements.Add(ParseAssignment(p));
            }
            if (IsPunct(p, ",")) Advance(p);
            else break;
        }
        Expect(p, "]");
        return arr;
    }

    private static JsNode ParseObjectLiteral(P p)
    {
        int line = Advance(p).Line; // {
        JsObjectLit obj = new JsObjectLit(line);
        while (!IsPunct(p, "}") && Cur(p).Type != JsTokenType.Eof)
        {
            int pl = Cur(p).Line;

            if (IsPunct(p, "..."))
            {
                Advance(p);
                obj.Props.Add(new JsSpread(ParseAssignment(p), pl));
                if (IsPunct(p, ",")) Advance(p);
                continue;
            }

            byte propKind = 0;
            bool isAsync = false, isGen = false;

            if (IsIdent(p, "get") || IsIdent(p, "set"))
            {
                JsToken n = Peek(p, 1);
                bool notCall = !(n.Type == JsTokenType.Punct && (string)n.Value == "(");
                bool notColon = !(n.Type == JsTokenType.Punct && (string)n.Value == ":");
                if (notCall && notColon)
                {
                    propKind = IsIdent(p, "get") ? (byte)1 : (byte)2;
                    Advance(p);
                }
            }
            if (IsIdent(p, "async"))
            {
                JsToken n = Peek(p, 1);
                bool notCall = !(n.Type == JsTokenType.Punct && (string)n.Value == "(");
                bool notColon = !(n.Type == JsTokenType.Punct && (string)n.Value == ":");
                if (notCall && notColon)
                {
                    isAsync = true;
                    Advance(p);
                    if (IsPunct(p, "*")) { Advance(p); isGen = true; }
                }
            }
            if (IsPunct(p, "*")) { Advance(p); isGen = true; }

            // method shorthand: name ( params ) { body }
            if (propKind == 0 && (IsPunct(p, "(")))
            {
                // `{ () {} }` invalid but tolerate
            }

            JsNode key = ParsePropName(p, out bool computed);

            // method value
            if (IsPunct(p, "(") || (propKind != 0 && IsPunct(p, "(")))
            {
                JsFunctionNode m = new JsFunctionNode(JsKind.Function, pl);
                m.IsMethod = true;
                m.IsAsync = isAsync;
                m.IsGenerator = isGen;
                ParseParamsInto(p, m);
                m.Body = ParseBlock(p);
                JsObjectProp mp = new JsObjectProp(pl);
                mp.Key = key;
                mp.Value = m;
                mp.Computed = computed;
                mp.PropKind = propKind;
                obj.Props.Add(mp);
            }
            else if (IsPunct(p, ":"))
            {
                Advance(p);
                JsNode value = ParseAssignment(p);
                JsObjectProp pr = new JsObjectProp(pl);
                pr.Key = key;
                pr.Value = value;
                pr.Computed = computed;
                pr.PropKind = propKind;
                obj.Props.Add(pr);
            }
            else
            {
                // shorthand {a} or {a = def}
                JsLiteral sklit = key as JsLiteral;
                bool shorthand = !computed && sklit != null && sklit.Value is string
                                 && (Cur(p).Type == JsTokenType.Punct && ((string)Cur(p).Value == "," || (string)Cur(p).Value == "}"));
                if (!shorthand) Fail(p, "expected ':' or '(' in object literal but found " + Describe(Cur(p)));
                string sname = (string)sklit.Value;
                JsObjectProp sh = new JsObjectProp(pl);
                sh.Key = key;
                sh.Value = new JsIdentifier(sname, pl);
                sh.Shorthand = true;
                obj.Props.Add(sh);
            }

            if (IsPunct(p, ",")) Advance(p);
            else break;
        }
        Expect(p, "}");
        return obj;
    }

    private static JsTemplate ParseTemplateLiteral(P p)
    {
        JsToken t = Cur(p);
        JsTemplate tpl = new JsTemplate(t.Line);

        if (t.Type == JsTokenType.TmplNoSub)
        {
            Advance(p);
            tpl.Quasis.Add((string)t.Value);
            return tpl;
        }

        // TmplStart
        if (t.Type != JsTokenType.TmplStart) Fail(p, "expected a template literal");
        tpl.Quasis.Add((string)Advance(p).Value);
        while (true)
        {
            tpl.Exprs.Add(ParseExpression(p));
            JsToken nt = Cur(p);
            if (nt.Type == JsTokenType.TmplMiddle)
            {
                tpl.Quasis.Add((string)Advance(p).Value);
                continue;
            }
            if (nt.Type == JsTokenType.TmplEnd)
            {
                tpl.Quasis.Add((string)Advance(p).Value);
                break;
            }
            Fail(p, "expected '}' to close template substitution but found " + Describe(nt));
        }
        return tpl;
    }

    // ---- arrow functions --------------------------------------------------

    private static JsNode TryParseArrow(P p)
    {
        int save = p.Pos;
        bool async = false;

        if (IsIdent(p, "async"))
        {
            JsToken n1 = Peek(p, 1);
            JsToken n2 = Peek(p, 2);
            bool identArrow = n1.Type == JsTokenType.Ident && n2.Type == JsTokenType.Punct && (string)n2.Value == "=>";
            bool parenStart = n1.Type == JsTokenType.Punct && (string)n1.Value == "(";
            if (!identArrow && !parenStart) return null;
            async = true;
            Advance(p); // consume `async`
        }

        List<JsNode> ps;
        JsToken t = Cur(p);

        if (t.Type == JsTokenType.Ident && Peek(p, 1).Type == JsTokenType.Punct && (string)Peek(p, 1).Value == "=>")
        {
            ps = new List<JsNode>();
            ps.Add(new JsIdentifier((string)t.Value, t.Line));
            Advance(p); // ident
        }
        else if (t.Type == JsTokenType.Punct && (string)t.Value == "(")
        {
            try { ps = ParseParamsForArrow(p); }
            catch (JsSyntaxError) { p.Pos = save; return null; }
        }
        else
        {
            p.Pos = save;
            return null;
        }

        if (!(IsPunct(p, "=>"))) { p.Pos = save; return null; }
        int line = Advance(p).Line; // =>

        JsFunctionNode fn = new JsFunctionNode(JsKind.Arrow, line);
        fn.Params = ps;
        fn.IsAsync = async;
        if (IsPunct(p, "{"))
        {
            fn.Body = ParseBlock(p);
            fn.IsExpressionBody = false;
        }
        else
        {
            fn.Body = ParseAssignment(p);
            fn.IsExpressionBody = true;
        }
        return fn;
    }

    private static List<JsNode> ParseParamsForArrow(P p)
    {
        List<JsNode> ps = new List<JsNode>();
        Expect(p, "(");
        while (!IsPunct(p, ")") && Cur(p).Type != JsTokenType.Eof)
        {
            if (IsPunct(p, "..."))
            {
                int rl = Advance(p).Line;
                ps.Add(new JsRestPat(ParseBindingTarget(p), rl));
            }
            else ps.Add(ParseBindingElement(p));
            if (IsPunct(p, ",")) Advance(p);
            else break;
        }
        Expect(p, ")");
        return ps;
    }
}

// A regex literal node: holds pattern + flags, materialised into a JsRegExp at eval.
public sealed class JsRegexLiteral : JsNode
{
    public string Pattern;
    public string Flags;
    public JsRegexLiteral(string pattern, string flags, int line)
        : base(JsKind.Literal, line) { Pattern = pattern; Flags = flags; }
}

// A debugger statement (no-op).
public sealed class JsDebuggerNode : JsNode
{
    public JsDebuggerNode(int line) : base(JsKind.Debugger, line) { }
}
