using Cosmos.Kernel.Core.IO;
using System.IO;

public sealed class BreezeRuntime
{
    private const int MaxOperations = 100000;
    private const int MaxLoopIterations = 10000;
    private const int MaxCallDepth = 64;
    private const int MaxEventsPerUpdate = 32;
    private const int MaxQueuedMessages = 128;

    private readonly List<Dictionary<string, object>> scopes = new List<Dictionary<string, object>>();
    private readonly Dictionary<string, BreezeFunction> functions = new Dictionary<string, BreezeFunction>();
    private readonly List<Window> applicationWindows = new List<Window>();
    private readonly List<BreezeTimerHandle> timers = new List<BreezeTimerHandle>();
    private readonly List<BreezeProcessMessage> messageQueue = new List<BreezeProcessMessage>();
    private static readonly Dictionary<Window, BreezeRuntime> windowOwners = new Dictionary<Window, BreezeRuntime>();
    private static readonly Dictionary<string, BreezeProcessHandle> namedProcesses = new Dictionary<string, BreezeProcessHandle>(StringComparer.OrdinalIgnoreCase);
    private readonly Action terminatedCallback;
    private readonly Action<string> applicationNameChanged;
    private readonly BreezeProcessHandle processHandle;
    private List<BreezeStatement> processUpdateBody;
    private List<BreezeStatement> processMessageBody;
    private int operationCount;
    private int callDepth;
    private bool terminated;
    private bool keepAliveWithoutWindows;
    private bool hasExplicitProcessName;
    public string LastError { get; private set; }
    public bool IsTerminated => terminated;

    public BreezeRuntime(Action terminatedCallback = null, Action<string> applicationNameChanged = null)
    {
        this.terminatedCallback = terminatedCallback;
        this.applicationNameChanged = applicationNameChanged;
        processHandle = new BreezeProcessHandle(this);
    }

    public void Execute(string source)
    {
        if (applicationWindows.Count > 0) TerminateApplication();
        applicationWindows.Clear();
        terminated = false;
        keepAliveWithoutWindows = false;
        hasExplicitProcessName = false;
        processUpdateBody = null;
        processMessageBody = null;
        timers.Clear();
        messageQueue.Clear();
        LastError = null;
        BreezeLexer lexer = new BreezeLexer(source);
        List<BreezeToken> tokens = lexer.Tokenize();
        if (lexer.ErrorMessage != null)
        {
            LastError = lexer.ErrorMessage;
            return;
        }

        BreezeParser parser = new BreezeParser(tokens);
        List<BreezeStatement> statements = parser.Parse();
        if (parser.ErrorMessage != null)
        {
            LastError = parser.ErrorMessage;
            return;
        }
        scopes.Clear();
        scopes.Add(new Dictionary<string, object>());
        functions.Clear();
        operationCount = 0;
        callDepth = 0;

        ExecutionResult result = ExecuteBlock(statements);
        if (result.Returned) Fail("return can only be used inside a function");
        if (!HasError && applicationWindows.Count == 0 && !keepAliveWithoutWindows)
            TerminateApplication();
    }

    private ExecutionResult ExecuteBlock(List<BreezeStatement> statements)
    {
        for (int i = 0; i < statements.Count; i++)
        {
            if (terminated) return ExecutionResult.None;
            ExecutionResult result = ExecuteStatement(statements[i]);
            if (result.Returned || HasError) return result;
        }
        return ExecutionResult.None;
    }

    private ExecutionResult ExecuteStatement(BreezeStatement statement)
    {
        CountOperation();
        if (HasError) return ExecutionResult.None;

        if (statement is BreezeLet declaration)
        {
            scopes[scopes.Count - 1][declaration.Name] = Evaluate(declaration.Value);
            return ExecutionResult.None;
        }

        if (statement is BreezeAssign variableAssignment)
        {
            SetVariable(variableAssignment.Name, Evaluate(variableAssignment.Value));
            return ExecutionResult.None;
        }

        if (statement is BreezeSet assignment)
        {
            SetProperty(GetVariable(assignment.Target), assignment.Property, Evaluate(assignment.Value));
            return ExecutionResult.None;
        }

        if (statement is BreezeOn eventStatement)
        {
            BindEvent(GetVariable(eventStatement.Target), eventStatement.Event, eventStatement.Body);
            return ExecutionResult.None;
        }

        if (statement is BreezeIf conditional)
        {
            object condition = Evaluate(conditional.Condition);
            if (HasError) return ExecutionResult.None;
            if (ToBool(condition)) return ExecuteBlock(conditional.ThenBody);
            if (conditional.ElseBody != null) return ExecuteBlock(conditional.ElseBody);
            return ExecutionResult.None;
        }

        if (statement is BreezeWhile loop)
        {
            int iterations = 0;
            while (true)
            {
                object condition = Evaluate(loop.Condition);
                if (HasError || !ToBool(condition)) break;
                if (++iterations > MaxLoopIterations)
                {
                    Fail("Loop limit exceeded (" + MaxLoopIterations + ")");
                    return ExecutionResult.None;
                }
                ExecutionResult result = ExecuteBlock(loop.Body);
                if (result.Returned || HasError) return result;
                CountOperation();
            }
            return ExecutionResult.None;
        }

        if (statement is BreezeFunction function)
        {
            functions[function.Name] = function;
            return ExecutionResult.None;
        }

        if (statement is BreezeReturn returnStatement)
        {
            return new ExecutionResult(true, returnStatement.Value == null ? null : Evaluate(returnStatement.Value));
        }

        if (statement is BreezeExpressionStatement expressionStatement)
            Evaluate(expressionStatement.Expression);

        return ExecutionResult.None;
    }

    private object Evaluate(BreezeExpression expression)
    {
        if (expression is BreezeLiteral literal) return literal.Value;
        if (expression is BreezeVariable variable) return GetVariable(variable.Name);

        if (expression is BreezeUnary unary)
        {
            object value = Evaluate(unary.Value);
            if (HasError) return null;
            if (unary.Operator == BreezeTokenType.Bang) return !ToBool(value);
            if (unary.Operator == BreezeTokenType.Minus) return -ToNumber(value);
            return FailValue<object>("Unsupported unary operator");
        }

        if (expression is BreezeBinary binary)
        {
            object left = Evaluate(binary.Left);
            if (HasError) return null;
            if (binary.Operator == BreezeTokenType.AndAnd && !ToBool(left)) return false;
            if (binary.Operator == BreezeTokenType.OrOr && ToBool(left)) return true;

            object right = Evaluate(binary.Right);
            if (HasError) return null;
            switch (binary.Operator)
            {
                case BreezeTokenType.Plus:
                    if (left is string || right is string) return ToText(left) + ToText(right);
                    return ToNumber(left) + ToNumber(right);
                case BreezeTokenType.Minus: return ToNumber(left) - ToNumber(right);
                case BreezeTokenType.Star: return ToNumber(left) * ToNumber(right);
                case BreezeTokenType.Slash:
                    double divisor = ToNumber(right);
                    if (divisor == 0) return FailValue<object>("Cannot divide by zero");
                    return ToNumber(left) / divisor;
                case BreezeTokenType.EqualEqual: return ValuesEqual(left, right);
                case BreezeTokenType.BangEqual: return !ValuesEqual(left, right);
                case BreezeTokenType.Less: return ToNumber(left) < ToNumber(right);
                case BreezeTokenType.LessEqual: return ToNumber(left) <= ToNumber(right);
                case BreezeTokenType.Greater: return ToNumber(left) > ToNumber(right);
                case BreezeTokenType.GreaterEqual: return ToNumber(left) >= ToNumber(right);
                case BreezeTokenType.AndAnd: return ToBool(right);
                case BreezeTokenType.OrOr: return ToBool(right);
                default: return FailValue<object>("Unsupported binary operator");
            }
        }

        if (expression is BreezeCall call)
        {
            object[] arguments = new object[call.Arguments.Count];
            for (int i = 0; i < arguments.Length; i++)
            {
                arguments[i] = Evaluate(call.Arguments[i]);
                if (HasError) return null;
            }
            if (functions.TryGetValue(call.Name, out BreezeFunction function))
                return CallFunction(function, arguments);
            return CallNative(call.Name, arguments);
        }

        return FailValue<object>("Unsupported expression");
    }

    private object CallFunction(BreezeFunction function, object[] args)
    {
        if (args.Length != function.Parameters.Count)
            return FailValue<object>(function.Name + " expects " + function.Parameters.Count + " argument(s)");
        if (++callDepth > MaxCallDepth)
        {
            callDepth--;
            return FailValue<object>("Function call depth limit exceeded (" + MaxCallDepth + ")");
        }

        Dictionary<string, object> localScope = new Dictionary<string, object>();
        for (int i = 0; i < args.Length; i++) localScope[function.Parameters[i]] = args[i];
        scopes.Add(localScope);
        try
        {
            ExecutionResult result = ExecuteBlock(function.Body);
            return result.Returned ? result.Value : null;
        }
        finally
        {
            scopes.RemoveAt(scopes.Count - 1);
            callDepth--;
        }
    }

    private object CallNative(string name, object[] args)
    {
        int expectedCount = GetExpectedArgumentCount(name);
        if (expectedCount < 0) return FailValue<object>("Unknown function '" + name + "'");
        if (args.Length != expectedCount)
            return FailValue<object>(name + " expects " + expectedCount + " argument(s)");

        switch (name)
        {
            case "process":
                RegisterProcessName(ToText(args[0]));
                keepAliveWithoutWindows = true;
                hasExplicitProcessName = true;
                applicationNameChanged?.Invoke(processHandle.name);
                return processHandle;

            case "stopProcess":
            {
                BreezeProcessHandle handle = Require<BreezeProcessHandle>(name, args[0]);
                if (handle == null) return false;
                if (handle.Runtime != this) return FailValue<object>("Cannot stop a process owned by another runtime");
                TerminateApplication();
                return true;
            }

            case "findProcess":
            {
                string processName = ToText(args[0]);
                if (namedProcesses.TryGetValue(processName, out BreezeProcessHandle found) && found.Running)
                    return found;
                return FailValue<object>("Process '" + processName + "' was not found");
            }

            case "send":
            {
                BreezeProcessHandle target = Require<BreezeProcessHandle>(name, args[0]);
                if (target == null) return false;
                if (!target.Running) return FailValue<object>("Cannot send to a stopped process");
                return target.Runtime.EnqueueMessage(new BreezeProcessMessage(
                    ToText(args[1]), args[2], processHandle.name));
            }

            case "timer":
            {
                int interval = ToInt(args[0]);
                if (HasError) return null;
                if (interval < 1) return FailValue<object>("Timer interval must be at least 1 ms");
                BreezeTimerHandle timer = new BreezeTimerHandle(this, interval);
                timers.Add(timer);
                keepAliveWithoutWindows = true;
                return timer;
            }

            case "startTimer":
            {
                BreezeTimerHandle timer = RequireOwnedTimer(name, args[0]);
                if (timer == null) return false;
                timer.active = true;
                timer.Reset();
                return true;
            }

            case "stopTimer":
            {
                BreezeTimerHandle timer = RequireOwnedTimer(name, args[0]);
                if (timer == null) return false;
                timer.active = false;
                return true;
            }

            case "window":
                {
                    RequireCount(name, args, 5);
                    int x = ToInt(args[1]);
                    int y = ToInt(args[2]);
                    int width = ToInt(args[3]);
                    int height = ToInt(args[4]);
                    if (HasError) return null;
                    Window window = new Window(x, y, width, height, ToText(args[0]), true);
                    applicationWindows.Add(window);
                    windowOwners[window] = this;
                    if (applicationWindows.Count == 1 && !hasExplicitProcessName)
                        applicationNameChanged?.Invoke(window.text);
                    return window;
                }

            case "windowRoot":
                {
                    RequireCount(name, args, 1);
                    Window window = Require<Window>(name, args[0]);
                    if (window == null) return null;
                    DockPanel root = new DockPanel(0, 0, window.Width, window.Height)
                    {
                        horizontalAlignment = HorizontalAlignment.Stretch,
                        verticalAlignment = VerticalAlignment.Stretch,
                        Margin = new Thickness(28, 2, 2, 2),
                        Padding = new Thickness(4),
                        useBackground = true,
                        backgroundColor = Palette.ControlFace,
                    };
                    window.AddChild(root);
                    return root;
                }

            case "dockPanel":
                RequireCount(name, args, 0);
                return new DockPanel(0, 0, 100, 100)
                {
                    clampSize = false,
                    Padding = new Thickness(0),
                    useBackground = true,
                    backgroundColor = Palette.ControlFace,
                };

            case "stackPanel":
                {
                    RequireCount(name, args, 1);
                    StackOrientation orientation = ParseOrientation(ToText(args[0]));
                    if (HasError) return null;
                    return new StackPanel(Palette.ControlFace, 0, 0, 100, 100)
                    {
                        clampSize = false,
                        useBackground = true,
                        Padding = new Thickness(4),
                        Margin = new Thickness(0),
                        orientation = orientation,
                    };
                }

            case "panel":
                {
                    RequireCount(name, args, 2);
                    int height = ToInt(args[1]);
                    if (HasError) return null;
                    return new Panel(Palette.ControlFace, 0, 0, 100, height)
                    {
                        text = ToText(args[0]),
                        fontSize = 16,
                        textColor = Palette.ControlBlack,
                        useBackground = true,
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                }

            case "button":
                {
                    RequireCount(name, args, 3);
                    int width = ToInt(args[1]);
                    int height = ToInt(args[2]);
                    if (HasError) return null;
                    return new Button(0, 0, width, height)
                    {
                        text = ToText(args[0]),
                        fontSize = 16,
                        useBorders = true,
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                }

            case "textField":
                {
                    RequireCount(name, args, 2);
                    int height = ToInt(args[1]);
                    if (HasError) return null;
                    return new TextField(0, 0, 100, height)
                    {
                        text = ToText(args[0]),
                        fontSize = 16,
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                }

            case "toolbar":
                RequireCount(name, args, 0);
                return new Toolbar(0, 0, 100);

            case "toolbarButton":
                RequireCount(name, args, 3);
                {
                    Toolbar toolbar = Require<Toolbar>(name, args[0]);
                    if (toolbar == null) return null;
                    int width = ToInt(args[2]);
                    if (HasError) return null;
                    return toolbar.AddButton(ToText(args[1]), width: width);
                }

            case "statusBar":
                RequireCount(name, args, 0);
                return new StatusBar(0, 0, 100);

            case "statusPanel":
                RequireCount(name, args, 3);
                {
                    StatusBar status = Require<StatusBar>(name, args[0]);
                    if (status == null) return null;
                    int width = ToInt(args[2]);
                    if (HasError) return null;
                    return status.AddPanel(ToText(args[1]), width);
                }

            case "menuBar":
                RequireCount(name, args, 0);
                return new MenuBar(0, 0, 100);

            case "menu":
                RequireCount(name, args, 2);
                {
                    MenuBar menuBar = Require<MenuBar>(name, args[0]);
                    if (menuBar == null) return null;
                    return menuBar.AddMenuPage(ToText(args[1]));
                }

            case "menuItem":
                RequireCount(name, args, 2);
                {
                    MenuPage menu = Require<MenuPage>(name, args[0]);
                    if (menu == null) return null;
                    return menu.AddItem(ToText(args[1]));
                }

            case "treeView":
                RequireCount(name, args, 0);
                return new TreeView(0, 0, 100, 100)
                {
                    useBackground = true,
                    backgroundColor = Palette.ControlWhite,
                    clampSize = false,
                };

            case "treeRoot":
                RequireCount(name, args, 3);
                {
                    TreeView tree = Require<TreeView>(name, args[0]);
                    if (tree == null) return null;
                    return tree.AddRoot(ToText(args[1]), args[2]);
                }

            case "treeChild":
                RequireCount(name, args, 3);
                {
                    TreeViewItem parent = Require<TreeViewItem>(name, args[0]);
                    if (parent == null) return null;
                    return parent.AddChild(ToText(args[1]), args[2]);
                }

            case "listView":
                {
                    RequireCount(name, args, 1);
                    ListViewMode mode = ParseListViewMode(ToText(args[0]));
                    if (HasError) return null;
                    return new ListView(0, 0, 100, 100)
                    {
                        viewMode = mode,
                        useBackground = true,
                        backgroundColor = Palette.ControlWhite,
                        clampSize = false,
                    };
                }

            case "listItem":
                {
                    RequireCount(name, args, 4);
                    ListView list = Require<ListView>(name, args[0]);
                    if (list == null) return null;
                    ListViewItem item = list.AddItem(ToText(args[1]), tag: args[2]);
                    item.isFolder = ToBool(args[3]);
                    item.type = item.isFolder ? "File Folder" : "File";
                    return item;
                }

            case "listClear":
                RequireCount(name, args, 1);
                {
                    ListView list = Require<ListView>(name, args[0]);
                    if (list == null) return null;
                    list.ClearItems();
                    return args[0];
                }

            case "listMode":
                RequireCount(name, args, 2);
                {
                    ListView list = Require<ListView>(name, args[0]);
                    if (list == null) return null;
                    ListViewMode mode = ParseListViewMode(ToText(args[1]));
                    if (HasError) return null;
                    list.SetViewMode(mode);
                    return args[0];
                }

            case "scrollView":
                {
                    RequireCount(name, args, 1);
                    Component content = Require<Component>(name, args[0]);
                    if (content == null) return null;
                    ScrollView scroll = new ScrollView(0, 0, 100, 100) { clampSize = false, Margin = new Thickness(0) };
                    int contentHeight = content is TreeView tree ? tree.GetContentHeight()
                        : content is ListView list ? list.GetContentHeight()
                        : content.Height;
                    scroll.SetContent(content, Math.Max(100, content.Width), Math.Max(1, contentHeight));
                    return scroll;
                }

            case "loadDirectory":
                RequireCount(name, args, 2);
                {
                    ListView list = Require<ListView>(name, args[0]);
                    if (list == null) return null;
                    LoadDirectory(list, ToText(args[1]));
                    return args[0];
                }

            case "list":
                RequireCount(name, args, 0);
                return new List<object>();

            case "listAdd":
                {
                    RequireCount(name, args, 2);
                    List<object> values = Require<List<object>>(name, args[0]);
                    if (values == null) return null;
                    values.Add(args[1]);
                    return args[1];
                }

            case "listGet":
                {
                    RequireCount(name, args, 2);
                    List<object> values = Require<List<object>>(name, args[0]);
                    if (values == null) return null;
                    int index = RequireListIndex(name, values, args[1]);
                    return index < 0 ? null : values[index];
                }

            case "listSet":
                {
                    RequireCount(name, args, 3);
                    List<object> values = Require<List<object>>(name, args[0]);
                    if (values == null) return null;
                    int index = RequireListIndex(name, values, args[1]);
                    if (index < 0) return null;
                    values[index] = args[2];
                    return args[2];
                }

            case "listRemove":
                {
                    RequireCount(name, args, 2);
                    List<object> values = Require<List<object>>(name, args[0]);
                    if (values == null) return null;
                    int index = RequireListIndex(name, values, args[1]);
                    if (index < 0) return null;
                    object removed = values[index];
                    values.RemoveAt(index);
                    return removed;
                }

            case "listCount":
                RequireCount(name, args, 1);
                {
                    List<object> values = Require<List<object>>(name, args[0]);
                    return values == null ? null : (double)values.Count;
                }

            case "dock":
                RequireCount(name, args, 3);
                {
                    DockPanel parent = Require<DockPanel>(name, args[0]);
                    Component child = Require<Component>(name, args[1]);
                    if (parent == null || child == null) return null;
                    Dock position = ParseDock(ToText(args[2]));
                    if (HasError) return null;
                    parent.AddDockChild(child, position);
                    return args[1];
                }

            case "stack":
                RequireCount(name, args, 2);
                {
                    StackPanel parent = Require<StackPanel>(name, args[0]);
                    Component child = Require<Component>(name, args[1]);
                    if (parent == null || child == null) return null;
                    parent.AddStackChild(child);
                    return args[1];
                }

            case "add":
                RequireCount(name, args, 2);
                {
                    Component parent = Require<Component>(name, args[0]);
                    Component child = Require<Component>(name, args[1]);
                    if (parent == null || child == null) return null;
                    parent.AddChild(child);
                    return args[1];
                }

            case "show":
                RequireCount(name, args, 1);
                {
                    Window window = Require<Window>(name, args[0]);
                    if (window == null) return null;
                    WindowManager.Register(window);
                    return args[0];
                }

            case "close":
                RequireCount(name, args, 1);
                {
                    Window window = Require<Window>(name, args[0]);
                    if (window == null) return null;
                    WindowManager.PostClose(window);
                    return true;
                }

            case "value":
                RequireCount(name, args, 2);
                return GetProperty(args[0], ToText(args[1]));

            case "print":
                RequireCount(name, args, 1);
                Serial.WriteString(ToText(args[0]) + "\n");
                return args[0];

            default:
                return FailValue<object>("Unknown function '" + name + "'");
        }
    }

    private void BindEvent(object target, string eventName, List<BreezeStatement> body)
    {
        Action callback = () => ExecuteDeferredBody(body);

        if (target is BreezeProcessHandle process)
        {
            if (process.Runtime != this) { Fail("Process belongs to another runtime"); return; }
            if (eventName == "update") { processUpdateBody = body; return; }
            if (eventName == "message") { processMessageBody = body; return; }
            Fail("Event '" + eventName + "' is not supported by a process");
            return;
        }

        if (target is BreezeTimerHandle timer)
        {
            if (timer.Runtime != this) { Fail("Timer belongs to another runtime"); return; }
            if (eventName == "tick") { timer.TickBody = body; return; }
            Fail("Event '" + eventName + "' is not supported by a timer");
            return;
        }

        if (target is Button button && eventName == "click") { button.leftMouseRelease = callback; return; }
        if (target is MenuItem menuItem && eventName == "click") { menuItem.click = callback; return; }
        if (target is TreeView tree)
        {
            if (eventName == "select")
            {
                tree.selectedChanged = item => { SetGlobal("event", item); callback(); };
                return;
            }
            if (eventName == "doubleClick")
            {
                tree.itemDoubleClick = item => { SetGlobal("event", item); callback(); };
                return;
            }
        }
        if (target is ListView list)
        {
            if (eventName == "select")
            {
                list.selectedChanged = item => { SetGlobal("event", item); callback(); };
                return;
            }
            if (eventName == "doubleClick")
            {
                list.itemDoubleClick = item => { SetGlobal("event", item); callback(); };
                return;
            }
        }
        Fail("Event '" + eventName + "' is not supported by this object");
    }

    public void UpdateProcess()
    {
        if (terminated) return;
        int delivered = 0;

        if (processUpdateBody != null)
        {
            ExecuteDeferredBody(processUpdateBody);
            if (terminated) return;
        }

        long now = DateTime.UtcNow.Ticks;
        for (int i = 0; i < timers.Count && delivered < MaxEventsPerUpdate; i++)
        {
            BreezeTimerHandle timer = timers[i];
            if (!timer.active || timer.TickBody == null || now < timer.NextTick) continue;
            timer.Reset();
            SetGlobal("event", timer);
            ExecuteDeferredBody(timer.TickBody);
            delivered++;
            if (terminated) return;
        }

        while (messageQueue.Count > 0 && delivered < MaxEventsPerUpdate)
        {
            BreezeProcessMessage message = messageQueue[0];
            messageQueue.RemoveAt(0);
            delivered++;
            if (processMessageBody == null) continue;
            SetGlobal("event", message);
            ExecuteDeferredBody(processMessageBody);
            if (terminated) return;
        }
    }

    private void ExecuteDeferredBody(List<BreezeStatement> body)
    {
        if (terminated) return;
        LastError = null;
        operationCount = 0;
        try
        {
            ExecutionResult result = ExecuteBlock(body);
            if (result.Returned) Fail("return can only be used inside a function");
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
        if (LastError == null) return;
        TerminateApplication();
        BreezeHost.ShowError(LastError);
    }

    private object GetProperty(object target, string property)
    {
        if (target is BreezeProcessHandle process)
        {
            switch (property)
            {
                case "name": return process.Name;
                case "running": return process.Running;
                default: return FailValue<object>("Unknown process property '" + property + "'");
            }
        }

        if (target is BreezeTimerHandle timer)
        {
            switch (property)
            {
                case "interval": return (double)timer.Interval;
                case "active": return timer.Active;
                default: return FailValue<object>("Unknown timer property '" + property + "'");
            }
        }

        if (target is BreezeProcessMessage message)
        {
            switch (property)
            {
                case "name": return message.Name;
                case "data": return message.Data;
                case "sender": return message.Sender;
                default: return FailValue<object>("Unknown message property '" + property + "'");
            }
        }

        if (target is ListViewItem listItem)
        {
            switch (property)
            {
                case "text": return listItem.text;
                case "path": return listItem.tag ?? "";
                case "isFolder": return listItem.isFolder;
                case "type": return listItem.type;
                case "size": return listItem.size;
                default: return FailValue<object>("Unknown list item property '" + property + "'");
            }
        }

        if (target is TreeViewItem treeItem)
        {
            switch (property)
            {
                case "text": return treeItem.text;
                case "path": return treeItem.tag ?? "";
                case "expanded": return treeItem.expanded;
                default: return FailValue<object>("Unknown tree item property '" + property + "'");
            }
        }

        if (target is Component component)
        {
            switch (property)
            {
                case "text": return component.text;
                case "width": return (double)component.Width;
                case "height": return (double)component.Height;
                case "visible": return component.Visible;
                default: return FailValue<object>("Unknown component property '" + property + "'");
            }
        }
        return FailValue<object>("Object does not expose properties");
    }

    private void SetProperty(object target, string property, object value)
    {
        if (target is BreezeProcessHandle process)
        {
            if (process.Runtime != this) { Fail("Process belongs to another runtime"); return; }
            if (property != "name") { Fail("Unknown process property '" + property + "'"); return; }
            RegisterProcessName(ToText(value));
            hasExplicitProcessName = true;
            applicationNameChanged?.Invoke(process.name);
            return;
        }

        if (target is not Component component)
        {
            Fail("Only GUI component properties can be assigned");
            return;
        }

        switch (property)
        {
            case "text": component.text = ToText(value); break;
            case "visible": component.Visible = ToBool(value); break;
            case "width":
                {
                    int width = ToInt(value);
                    if (HasError) return;
                    component.Resize(width, component.Height);
                    break;
                }
            case "height":
                {
                    int height = ToInt(value);
                    if (HasError) return;
                    component.Resize(component.Width, height);
                    break;
                }
            case "fontSize":
                {
                    int fontSize = ToInt(value);
                    if (HasError) return;
                    if (component is Button button) button.fontSize = fontSize;
                    else if (component is Panel panel) panel.fontSize = fontSize;
                    else if (component is TextField field) field.fontSize = fontSize;
                    else { Fail("This component has no fontSize property"); return; }
                    break;
                }
            case "canResize":
            case "canMaximize":
            case "canMinimize":
                {
                    Window window = Require<Window>("set", target);
                    if (window == null) return;
                    if (property == "canResize") window.canResize = ToBool(value);
                    else if (property == "canMaximize") window.canMaximize = ToBool(value);
                    else window.canMinimize = ToBool(value);
                    break;
                }
            default: Fail("Unknown property '" + property + "'"); return;
        }

        if (!HasError) component.MarkDirty();
    }

    private object GetVariable(string name)
    {
        for (int i = scopes.Count - 1; i >= 0; i--)
            if (scopes[i].TryGetValue(name, out object value)) return value;
        return FailValue<object>("Unknown variable '" + name + "'");
    }

    private void SetVariable(string name, object value)
    {
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            if (!scopes[i].ContainsKey(name)) continue;
            scopes[i][name] = value;
            return;
        }
        Fail("Unknown variable '" + name + "'");
    }

    private void SetGlobal(string name, object value) => scopes[0][name] = value;

    private void RegisterProcessName(string name)
    {
        if (!string.IsNullOrEmpty(processHandle.name) &&
            namedProcesses.TryGetValue(processHandle.name, out BreezeProcessHandle current) &&
            current == processHandle)
            namedProcesses.Remove(processHandle.name);

        processHandle.name = string.IsNullOrEmpty(name) ? "Breeze Application" : name;
        namedProcesses[processHandle.name] = processHandle;
    }

    private bool EnqueueMessage(BreezeProcessMessage message)
    {
        if (terminated || messageQueue.Count >= MaxQueuedMessages) return false;
        messageQueue.Add(message);
        return true;
    }

    private BreezeTimerHandle RequireOwnedTimer(string function, object value)
    {
        BreezeTimerHandle timer = Require<BreezeTimerHandle>(function, value);
        if (timer == null) return null;
        if (timer.Runtime == this) return timer;
        Fail("Timer belongs to another runtime");
        return null;
    }

    private void CountOperation()
    {
        if (++operationCount > MaxOperations)
            Fail("Execution limit exceeded (" + MaxOperations + " operations)");
    }

    private bool ValuesEqual(object left, object right)
    {
        if (left == null || right == null) return left == right;
        if (left is double || left is int || right is double || right is int)
            return ToNumber(left) == ToNumber(right);
        return left.Equals(right);
    }

    private int RequireListIndex(string function, List<object> values, object value)
    {
        int index = ToInt(value);
        if (HasError) return -1;
        if (index < 0 || index >= values.Count)
        {
            Fail(function + " index " + index + " is outside the list");
            return -1;
        }
        return index;
    }

    private Dock ParseDock(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "top": return Dock.Top;
            case "bottom": return Dock.Bottom;
            case "left": return Dock.Left;
            case "right": return Dock.Right;
            case "fill": return Dock.Fill;
            default: Fail("Unknown dock position '" + value + "'"); return Dock.Fill;
        }
    }

    private StackOrientation ParseOrientation(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "vertical": return StackOrientation.Vertical;
            case "horizontal": return StackOrientation.Horizontal;
            default: Fail("Unknown stack orientation '" + value + "'"); return StackOrientation.Vertical;
        }
    }

    private ListViewMode ParseListViewMode(string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "icons": return ListViewMode.LargeIcon;
            case "smallicons": return ListViewMode.SmallIcon;
            case "list": return ListViewMode.List;
            case "details": return ListViewMode.Details;
            default: Fail("Unknown list mode '" + value + "'"); return ListViewMode.Details;
        }
    }

    private void LoadDirectory(ListView list, string path)
    {
        if (!Directory.Exists(path))
        {
            Fail("Directory does not exist: " + path);
            return;
        }
        list.ClearItems();

        string[] directories = Directory.GetDirectories(path);
        for (int i = 0; i < directories.Length; i++)
        {
            string directory = directories[i];
            ListViewItem item = list.AddItem(Path.GetFileName(directory), tag: directory);
            item.isFolder = true;
            item.type = "File Folder";
        }

        string[] files = Directory.GetFiles(path);
        for (int i = 0; i < files.Length; i++)
        {
            string file = files[i];
            ListViewItem item = list.AddItem(Path.GetFileName(file), tag: file);
            item.isFolder = false;
            item.type = "File";
        }

        list.MarkDirty();
    }

    private T Require<T>(string function, object value) where T : class
    {
        if (value is T typed) return typed;
        Fail(function + " expected " + typeof(T).Name);
        return null;
    }

    private void RequireCount(string function, object[] arguments, int count)
    {
        if (arguments.Length != count)
            Fail(function + " expects " + count + " argument(s)");
    }

    private int ToInt(object value) => (int)ToNumber(value);
    private double ToNumber(object value)
    {
        if (value is double number) return number;
        if (value is int integer) return integer;
        if (double.TryParse(ToText(value), out double parsed)) return parsed;
        Fail("Expected a number");
        return 0;
    }

    private static bool ToBool(object value)
    {
        if (value == null) return false;
        if (value is bool boolean) return boolean;
        if (value is double number) return number != 0;
        if (value is int integer) return integer != 0;
        if (bool.TryParse(ToText(value), out bool parsed)) return parsed;
        if (value is string text) return text.Length > 0;
        return true;
    }

    private static string ToText(object value)
    {
        if (value == null) return "";
        if (value is double number && number == (int)number) return ((int)number).ToString();
        return value.ToString();
    }

    private bool HasError => LastError != null;

    private void Fail(string message)
    {
        if (LastError != null) return;
        LastError = message ?? "Unknown Breeze error";
        TerminateApplication();
    }

    public void TerminateApplication()
    {
        if (terminated) return;
        terminated = true;
        processUpdateBody = null;
        processMessageBody = null;
        timers.Clear();
        messageQueue.Clear();
        if (namedProcesses.TryGetValue(processHandle.name, out BreezeProcessHandle registered) && registered == processHandle)
            namedProcesses.Remove(processHandle.name);
        terminatedCallback?.Invoke();
        for (int i = applicationWindows.Count - 1; i >= 0; i--)
        {
            Window window = applicationWindows[i];
            if (window != null) WindowManager.PostClose(window);
        }
        applicationWindows.Clear();
    }

    public static void NotifyWindowClosed(Window window)
    {
        if (window == null || !windowOwners.TryGetValue(window, out BreezeRuntime owner)) return;
        windowOwners.Remove(window);
        owner.applicationWindows.Remove(window);
        if (owner.applicationWindows.Count != 0 || owner.terminated || owner.keepAliveWithoutWindows) return;
        owner.TerminateApplication();
    }

    private T FailValue<T>(string message)
    {
        Fail(message);
        return default;
    }

    private static int GetExpectedArgumentCount(string name) => name switch
    {
        "window" => 5,
        "process" => 1,
        "stopProcess" => 1,
        "findProcess" => 1,
        "send" => 3,
        "timer" => 1,
        "startTimer" => 1,
        "stopTimer" => 1,
        "windowRoot" => 1,
        "dockPanel" => 0,
        "stackPanel" => 1,
        "panel" => 2,
        "button" => 3,
        "textField" => 2,
        "toolbar" => 0,
        "toolbarButton" => 3,
        "statusBar" => 0,
        "statusPanel" => 3,
        "menuBar" => 0,
        "menu" => 2,
        "menuItem" => 2,
        "treeView" => 0,
        "treeRoot" => 3,
        "treeChild" => 3,
        "listView" => 1,
        "listItem" => 4,
        "listClear" => 1,
        "listMode" => 2,
        "scrollView" => 1,
        "loadDirectory" => 2,
        "list" => 0,
        "listAdd" => 2,
        "listGet" => 2,
        "listSet" => 3,
        "listRemove" => 2,
        "listCount" => 1,
        "dock" => 3,
        "stack" => 2,
        "add" => 2,
        "show" => 1,
        "close" => 1,
        "value" => 2,
        "print" => 1,
        _ => -1,
    };

    private readonly struct ExecutionResult
    {
        public static readonly ExecutionResult None = new ExecutionResult(false, null);

        public readonly bool Returned;
        public readonly object Value;

        public ExecutionResult(bool returned, object value)
        {
            Returned = returned;
            Value = value;
        }
    }
}
