public enum BreezeTokenType
{
    End, Identifier, String, Number,
    Let, Set, On, If, Else, While, For, In, Import, Function, Return, True, False, Null,
    Equals, EqualEqual, Bang, BangEqual,
    Less, LessEqual, Greater, GreaterEqual,
    Plus, Minus, Star, Slash, AndAnd, OrOr,
    Dot, Comma, Semicolon,
    LeftParenthesis, RightParenthesis, LeftBrace, RightBrace,
}

public readonly struct BreezeToken
{
    public readonly BreezeTokenType Type;
    public readonly string Text;
    public readonly int Line;

    public BreezeToken(BreezeTokenType type, string text, int line)
    {
        Type = type;
        Text = text;
        Line = line;
    }
}

public sealed class BreezeLexer
{
    private readonly string source;
    private readonly List<BreezeToken> tokens = new List<BreezeToken>();
    private int position;
    private int line = 1;
    public string ErrorMessage { get; private set; }

    public BreezeLexer(string source) => this.source = source ?? "";

    public List<BreezeToken> Tokenize()
    {
        while (!AtEnd() && ErrorMessage == null)
        {
            char current = Peek();
            if (current == ' ' || current == '\t' || current == '\r') { position++; continue; }
            if (current == '\n') { line++; position++; continue; }
            if (current == '/' && PeekNext() == '/')
            {
                while (!AtEnd() && Peek() != '\n') position++;
                continue;
            }
            if (IsIdentifierStart(current)) { ReadIdentifier(); continue; }
            if (char.IsDigit(current)) { ReadNumber(); continue; }
            if (current == '"') { ReadString(); continue; }

            position++;
            switch (current)
            {
                case '=': AddMatched('=', BreezeTokenType.Equals, BreezeTokenType.EqualEqual); break;
                case '!': AddMatched('=', BreezeTokenType.Bang, BreezeTokenType.BangEqual); break;
                case '<': AddMatched('=', BreezeTokenType.Less, BreezeTokenType.LessEqual); break;
                case '>': AddMatched('=', BreezeTokenType.Greater, BreezeTokenType.GreaterEqual); break;
                case '&':
                    if (!MatchNext('&')) { SetError("Expected '&' after '&'"); break; }
                    Add(BreezeTokenType.AndAnd, "&&");
                    break;
                case '|':
                    if (!MatchNext('|')) { SetError("Expected '|' after '|'"); break; }
                    Add(BreezeTokenType.OrOr, "||");
                    break;
                case '+': Add(BreezeTokenType.Plus, "+"); break;
                case '-': Add(BreezeTokenType.Minus, "-"); break;
                case '*': Add(BreezeTokenType.Star, "*"); break;
                case '/': Add(BreezeTokenType.Slash, "/"); break;
                case '.': Add(BreezeTokenType.Dot, "."); break;
                case ',': Add(BreezeTokenType.Comma, ","); break;
                case ';': Add(BreezeTokenType.Semicolon, ";"); break;
                case '(': Add(BreezeTokenType.LeftParenthesis, "("); break;
                case ')': Add(BreezeTokenType.RightParenthesis, ")"); break;
                case '{': Add(BreezeTokenType.LeftBrace, "{"); break;
                case '}': Add(BreezeTokenType.RightBrace, "}"); break;
                default: SetError("Unexpected character '" + current + "'"); break;
            }
        }

        tokens.Add(new BreezeToken(BreezeTokenType.End, "", line));
        return tokens;
    }

    private void ReadIdentifier()
    {
        int start = position;
        while (!AtEnd() && IsIdentifierPart(Peek())) position++;
        string text = source.Substring(start, position - start);
        BreezeTokenType type = text switch
        {
            "let" => BreezeTokenType.Let,
            "set" => BreezeTokenType.Set,
            "on" => BreezeTokenType.On,
            "if" => BreezeTokenType.If,
            "else" => BreezeTokenType.Else,
            "while" => BreezeTokenType.While,
            "for" => BreezeTokenType.For,
            "in" => BreezeTokenType.In,
            "import" => BreezeTokenType.Import,
            "function" => BreezeTokenType.Function,
            "return" => BreezeTokenType.Return,
            "true" => BreezeTokenType.True,
            "false" => BreezeTokenType.False,
            "null" => BreezeTokenType.Null,
            _ => BreezeTokenType.Identifier,
        };
        Add(type, text);
    }

    private void ReadNumber()
    {
        int start = position;
        while (!AtEnd() && char.IsDigit(Peek())) position++;
        if (!AtEnd() && Peek() == '.' && char.IsDigit(PeekNext()))
        {
            position++;
            while (!AtEnd() && char.IsDigit(Peek())) position++;
        }
        Add(BreezeTokenType.Number, source.Substring(start, position - start));
    }

    private void ReadString()
    {
        position++;
        string value = "";
        while (!AtEnd() && Peek() != '"')
        {
            char current = source[position++];
            if (current == '\n') line++;
            if (current == '\\' && !AtEnd())
            {
                char escaped = source[position++];
                value += escaped switch
                {
                    'n' => "\n",
                    't' => "\t",
                    '"' => "\"",
                    '\\' => "\\",
                    _ => escaped.ToString(),
                };
            }
            else value += current;
        }
        if (AtEnd())
        {
            SetError("Unterminated string");
            return;
        }
        position++;
        Add(BreezeTokenType.String, value);
    }

    private bool MatchNext(char expected)
    {
        if (AtEnd() || Peek() != expected) return false;
        position++;
        return true;
    }

    private void AddMatched(char second, BreezeTokenType single, BreezeTokenType paired)
    {
        bool matched = MatchNext(second);
        Add(matched ? paired : single, matched ? source.Substring(position - 2, 2) : source[position - 1].ToString());
    }
    private void Add(BreezeTokenType type, string text) => tokens.Add(new BreezeToken(type, text, line));
    private bool AtEnd() => position >= source.Length;
    private char Peek() => AtEnd() ? '\0' : source[position];
    private char PeekNext() => position + 1 >= source.Length ? '\0' : source[position + 1];
    private static bool IsIdentifierStart(char value) => char.IsLetter(value) || value == '_';
    private static bool IsIdentifierPart(char value) => char.IsLetterOrDigit(value) || value == '_';
    private void SetError(string message)
    {
        if (ErrorMessage == null) ErrorMessage = "Line " + line + ": " + message;
    }
}

public abstract class BreezeStatement { }
public abstract class BreezeExpression { }
public sealed class BreezeLet : BreezeStatement { public string Name; public BreezeExpression Value; }
public sealed class BreezeAssign : BreezeStatement { public string Name; public BreezeExpression Value; }
public sealed class BreezeSet : BreezeStatement { public string Target; public string Property; public BreezeExpression Value; }
public sealed class BreezeOn : BreezeStatement { public string Target; public string Event; public List<BreezeStatement> Body; }
public sealed class BreezeIf : BreezeStatement { public BreezeExpression Condition; public List<BreezeStatement> ThenBody; public List<BreezeStatement> ElseBody; }
public sealed class BreezeWhile : BreezeStatement { public BreezeExpression Condition; public List<BreezeStatement> Body; }
public sealed class BreezeForEach : BreezeStatement { public string Name; public BreezeExpression Collection; public List<BreezeStatement> Body; }
public sealed class BreezeImport : BreezeStatement { public string Path; }
public sealed class BreezeFunction : BreezeStatement { public string Name; public List<string> Parameters; public List<BreezeStatement> Body; }
public sealed class BreezeReturn : BreezeStatement { public BreezeExpression Value; }
public sealed class BreezeExpressionStatement : BreezeStatement { public BreezeExpression Expression; }
public sealed class BreezeLiteral : BreezeExpression { public object Value; }
public sealed class BreezeVariable : BreezeExpression { public string Name; }
public sealed class BreezeCall : BreezeExpression { public string Name; public List<BreezeExpression> Arguments; }
public sealed class BreezeUnary : BreezeExpression { public BreezeTokenType Operator; public BreezeExpression Value; }
public sealed class BreezeBinary : BreezeExpression { public BreezeExpression Left; public BreezeTokenType Operator; public BreezeExpression Right; }

public sealed class BreezeParser
{
    private readonly List<BreezeToken> tokens;
    private int position;
    public string ErrorMessage { get; private set; }

    public BreezeParser(List<BreezeToken> tokens) => this.tokens = tokens;

    public List<BreezeStatement> Parse()
    {
        List<BreezeStatement> statements = new List<BreezeStatement>();
        while (!Check(BreezeTokenType.End) && ErrorMessage == null) statements.Add(ParseStatement());
        return statements;
    }

    private BreezeStatement ParseStatement()
    {
        if (Match(BreezeTokenType.Let)) return ParseLet();
        if (Match(BreezeTokenType.Set)) return ParseSet();
        if (Match(BreezeTokenType.On)) return ParseOn();
        if (Match(BreezeTokenType.If)) return ParseIf();
        if (Match(BreezeTokenType.While)) return ParseWhile();
        if (Match(BreezeTokenType.For)) return ParseForEach();
        if (Match(BreezeTokenType.Import)) return ParseImport();
        if (Match(BreezeTokenType.Function)) return ParseFunction();
        if (Match(BreezeTokenType.Return)) return ParseReturn();
        if (Check(BreezeTokenType.Identifier) && CheckNext(BreezeTokenType.Equals)) return ParseAssignment();

        BreezeExpression expression = ParseExpression();
        ConsumeStatementEnd("expression");
        return new BreezeExpressionStatement { Expression = expression };
    }

    private BreezeStatement ParseLet()
    {
        string name = Consume(BreezeTokenType.Identifier, "Expected variable name").Text;
        Consume(BreezeTokenType.Equals, "Expected '=' after variable name");
        BreezeExpression value = ParseExpression();
        ConsumeStatementEnd("declaration");
        return new BreezeLet { Name = name, Value = value };
    }

    private BreezeStatement ParseAssignment()
    {
        string name = Consume(BreezeTokenType.Identifier, "Expected variable name").Text;
        Consume(BreezeTokenType.Equals, "Expected '=' after variable name");
        BreezeExpression value = ParseExpression();
        ConsumeStatementEnd("assignment");
        return new BreezeAssign { Name = name, Value = value };
    }

    private BreezeStatement ParseSet()
    {
        string target = Consume(BreezeTokenType.Identifier, "Expected target name").Text;
        Consume(BreezeTokenType.Dot, "Expected '.' after target");
        string property = Consume(BreezeTokenType.Identifier, "Expected property name").Text;
        Consume(BreezeTokenType.Equals, "Expected '=' after property");
        BreezeExpression value = ParseExpression();
        ConsumeStatementEnd("assignment");
        return new BreezeSet { Target = target, Property = property, Value = value };
    }

    private BreezeStatement ParseOn()
    {
        string target = Consume(BreezeTokenType.Identifier, "Expected event target").Text;
        Consume(BreezeTokenType.Dot, "Expected '.' after event target");
        string eventName = Consume(BreezeTokenType.Identifier, "Expected event name").Text;
        return new BreezeOn { Target = target, Event = eventName, Body = ParseRequiredBlock("event") };
    }

    private BreezeStatement ParseIf()
    {
        Consume(BreezeTokenType.LeftParenthesis, "Expected '(' after if");
        BreezeExpression condition = ParseExpression();
        Consume(BreezeTokenType.RightParenthesis, "Expected ')' after condition");
        List<BreezeStatement> thenBody = ParseRequiredBlock("if");
        List<BreezeStatement> elseBody = null;
        if (Match(BreezeTokenType.Else))
        {
            if (Match(BreezeTokenType.If)) elseBody = new List<BreezeStatement> { ParseIf() };
            else elseBody = ParseRequiredBlock("else");
        }
        return new BreezeIf { Condition = condition, ThenBody = thenBody, ElseBody = elseBody };
    }

    private BreezeStatement ParseWhile()
    {
        Consume(BreezeTokenType.LeftParenthesis, "Expected '(' after while");
        BreezeExpression condition = ParseExpression();
        Consume(BreezeTokenType.RightParenthesis, "Expected ')' after condition");
        return new BreezeWhile { Condition = condition, Body = ParseRequiredBlock("while") };
    }

    private BreezeStatement ParseForEach()
    {
        Consume(BreezeTokenType.LeftParenthesis, "Expected '(' after for");
        string name = Consume(BreezeTokenType.Identifier, "Expected loop variable").Text;
        Consume(BreezeTokenType.In, "Expected 'in' after loop variable");
        BreezeExpression collection = ParseExpression();
        Consume(BreezeTokenType.RightParenthesis, "Expected ')' after collection");
        return new BreezeForEach { Name = name, Collection = collection, Body = ParseRequiredBlock("for") };
    }

    private BreezeStatement ParseImport()
    {
        string path = Consume(BreezeTokenType.String, "Expected module path after import").Text;
        ConsumeStatementEnd("import");
        return new BreezeImport { Path = path };
    }

    private BreezeStatement ParseFunction()
    {
        string name = Consume(BreezeTokenType.Identifier, "Expected function name").Text;
        Consume(BreezeTokenType.LeftParenthesis, "Expected '(' after function name");
        List<string> parameters = new List<string>();
        if (!Check(BreezeTokenType.RightParenthesis))
        {
            do parameters.Add(Consume(BreezeTokenType.Identifier, "Expected parameter name").Text);
            while (Match(BreezeTokenType.Comma));
        }
        Consume(BreezeTokenType.RightParenthesis, "Expected ')' after parameters");
        return new BreezeFunction { Name = name, Parameters = parameters, Body = ParseRequiredBlock("function") };
    }

    private BreezeStatement ParseReturn()
    {
        BreezeExpression value = IsStatementEnd() ? new BreezeLiteral { Value = null } : ParseExpression();
        ConsumeStatementEnd("return value");
        return new BreezeReturn { Value = value };
    }

    private List<BreezeStatement> ParseRequiredBlock(string owner)
    {
        Consume(BreezeTokenType.LeftBrace, "Expected '{' before " + owner + " body");
        List<BreezeStatement> body = new List<BreezeStatement>();
        while (!Check(BreezeTokenType.RightBrace) && !Check(BreezeTokenType.End) && ErrorMessage == null) body.Add(ParseStatement());
        Consume(BreezeTokenType.RightBrace, "Expected '}' after " + owner + " body");
        return body;
    }

    private BreezeExpression ParseExpression() => ParseOr();

    private BreezeExpression ParseOr()
    {
        BreezeExpression expression = ParseAnd();
        while (Match(BreezeTokenType.OrOr)) expression = Binary(expression, Previous().Type, ParseAnd());
        return expression;
    }

    private BreezeExpression ParseAnd()
    {
        BreezeExpression expression = ParseEquality();
        while (Match(BreezeTokenType.AndAnd)) expression = Binary(expression, Previous().Type, ParseEquality());
        return expression;
    }

    private BreezeExpression ParseEquality()
    {
        BreezeExpression expression = ParseComparison();
        while (Match(BreezeTokenType.EqualEqual, BreezeTokenType.BangEqual)) expression = Binary(expression, Previous().Type, ParseComparison());
        return expression;
    }

    private BreezeExpression ParseComparison()
    {
        BreezeExpression expression = ParseTerm();
        while (Match(BreezeTokenType.Less, BreezeTokenType.LessEqual, BreezeTokenType.Greater, BreezeTokenType.GreaterEqual))
            expression = Binary(expression, Previous().Type, ParseTerm());
        return expression;
    }

    private BreezeExpression ParseTerm()
    {
        BreezeExpression expression = ParseFactor();
        while (Match(BreezeTokenType.Plus, BreezeTokenType.Minus)) expression = Binary(expression, Previous().Type, ParseFactor());
        return expression;
    }

    private BreezeExpression ParseFactor()
    {
        BreezeExpression expression = ParseUnary();
        while (Match(BreezeTokenType.Star, BreezeTokenType.Slash)) expression = Binary(expression, Previous().Type, ParseUnary());
        return expression;
    }

    private BreezeExpression ParseUnary()
    {
        if (Match(BreezeTokenType.Bang, BreezeTokenType.Minus))
            return new BreezeUnary { Operator = Previous().Type, Value = ParseUnary() };
        return ParsePrimary();
    }

    private BreezeExpression ParsePrimary()
    {
        if (Match(BreezeTokenType.String)) return new BreezeLiteral { Value = Previous().Text };
        if (Match(BreezeTokenType.Number)) return new BreezeLiteral { Value = double.Parse(Previous().Text) };
        if (Match(BreezeTokenType.True)) return new BreezeLiteral { Value = true };
        if (Match(BreezeTokenType.False)) return new BreezeLiteral { Value = false };
        if (Match(BreezeTokenType.Null)) return new BreezeLiteral { Value = null };
        if (Match(BreezeTokenType.LeftParenthesis))
        {
            BreezeExpression expression = ParseExpression();
            Consume(BreezeTokenType.RightParenthesis, "Expected ')' after expression");
            return expression;
        }

        BreezeToken identifier = Consume(BreezeTokenType.Identifier, "Expected expression");
        if (!Match(BreezeTokenType.LeftParenthesis)) return new BreezeVariable { Name = identifier.Text };
        List<BreezeExpression> arguments = new List<BreezeExpression>();
        if (!Check(BreezeTokenType.RightParenthesis))
        {
            do arguments.Add(ParseExpression()); while (Match(BreezeTokenType.Comma));
        }
        Consume(BreezeTokenType.RightParenthesis, "Expected ')' after arguments");
        return new BreezeCall { Name = identifier.Text, Arguments = arguments };
    }

    private static BreezeExpression Binary(BreezeExpression left, BreezeTokenType op, BreezeExpression right)
        => new BreezeBinary { Left = left, Operator = op, Right = right };

    private bool Match(params BreezeTokenType[] types)
    {
        for (int i = 0; i < types.Length; i++)
        {
            if (!Check(types[i])) continue;
            position++;
            return true;
        }
        return false;
    }

    private bool Check(BreezeTokenType type) => tokens[position].Type == type;
    private bool CheckNext(BreezeTokenType type) => position + 1 < tokens.Count && tokens[position + 1].Type == type;
    private BreezeToken Previous() => tokens[position - 1];
    private bool IsStatementEnd()
    {
        return Check(BreezeTokenType.Semicolon);
    }

    private void ConsumeStatementEnd(string owner)
    {
        if (Match(BreezeTokenType.Semicolon)) return;
        SetError("Expected ';' after " + owner);
    }

    private BreezeToken Consume(BreezeTokenType type, string message)
    {
        if (Check(type)) return tokens[position++];
        SetError(message);
        return new BreezeToken(type, "", tokens[position].Line);
    }

    private void SetError(string message)
    {
        if (ErrorMessage == null) ErrorMessage = "Line " + tokens[position].Line + ": " + message;
    }
}
