using Cosmos.Kernel.Core.IO;

/// <summary>
/// Add a language feature by:
/// <list type="number">
/// <item><description>
/// Defining it in <see cref="CallNative(string, object[])"/>
/// </description></item>
/// <item><description>
/// Declaring its argument count in <see cref="GetExpectedArgumentCount(string)"/>
/// </description></item>
/// <item><description>
/// Adding it into the editors completion list in <see cref="CodeEditor.CompletionItems"/>
/// </description></item>
/// </list>
/// </summary>
public sealed class BreezeRuntime
{
    private sealed class BreezeFileWatch
    {
        public string Path;
        public bool Recursive;
        public Action<FileSystemChange> Handler;
    }

    private const int MaxOperations = 100000;
    private const int MaxBackgroundOperations = 5000;
    private const int MaxLoopIterations = 10000;
    private const int MaxCallDepth = 64;
    private const int MaxEventsPerUpdate = 32;
    private const int MaxBackgroundEventsPerUpdate = 8;
    private const int MaxQueuedMessages = 128;
    private static int nextMessageId;

    private readonly List<Dictionary<string, object>> scopes = new List<Dictionary<string, object>>();
    private readonly Dictionary<string, BreezeFunction> functions = new Dictionary<string, BreezeFunction>();
    private readonly HashSet<string> importedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> grantedCapabilities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Window> applicationWindows = new List<Window>();
    private readonly List<BreezeFileWatch> fileWatches = new List<BreezeFileWatch>();
    private readonly List<BreezeTimerHandle> timers = new List<BreezeTimerHandle>();
    private readonly List<BreezeProcessMessage> messageQueue = new List<BreezeProcessMessage>();
    private readonly object messageQueueLock = new object();
    private static readonly Dictionary<Window, BreezeRuntime> windowOwners = new Dictionary<Window, BreezeRuntime>();
    private static readonly Dictionary<string, BreezeProcessHandle> namedProcesses = new Dictionary<string, BreezeProcessHandle>(StringComparer.OrdinalIgnoreCase);
    private static readonly object processRegistryLock = new object();
    private readonly Action terminatedCallback;
    private readonly Action<string> applicationNameChanged;
    private readonly BreezeProcessHandle processHandle;
    private BreezeServiceHandle serviceHandle;
    private readonly bool backgroundMode;
    private List<BreezeStatement> processUpdateBody;
    private List<BreezeStatement> processMessageBody;
    private int operationCount;
    private int callDepth;
    private int importDepth;
    private string currentModuleDirectory = "";
    private volatile bool terminated;
    private bool keepAliveWithoutWindows;
    private bool hasExplicitProcessName;
    public string LastError { get; private set; }
    public bool IsTerminated => terminated;
    internal Process OwnerProcess => processHandle.OwnerProcess;
    internal Action WorkAvailable;

    public BreezeRuntime(Action terminatedCallback = null, Action<string> applicationNameChanged = null, bool backgroundMode = false)
    {
        this.terminatedCallback = terminatedCallback;
        this.applicationNameChanged = applicationNameChanged;
        this.backgroundMode = backgroundMode;
        processHandle = new BreezeProcessHandle(this);
    }

    internal void AttachProcess(Process process)
    {
        processHandle.OwnerProcess = process;
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
        lock (messageQueueLock) messageQueue.Clear();
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
        importedModules.Clear();
        grantedCapabilities.Clear();
        operationCount = 0;
        callDepth = 0;
        importDepth = 0;
        currentModuleDirectory = FileSystemManager.GetParent(OwnerProcess?.startInfo?.ExecutablePath ?? "");

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

        if (statement is BreezeForEach forEach)
        {
            List<object> values = GetIterationValues(Evaluate(forEach.Collection));
            if (HasError || values == null) return ExecutionResult.None;
            if (values.Count > MaxLoopIterations)
            {
                Fail("Loop limit exceeded (" + MaxLoopIterations + ")");
                return ExecutionResult.None;
            }

            for (int i = 0; i < values.Count; i++)
            {
                Dictionary<string, object> loopScope = new Dictionary<string, object>();
                loopScope[forEach.Name] = values[i];
                scopes.Add(loopScope);
                ExecutionResult result;
                try { result = ExecuteBlock(forEach.Body); }
                finally { scopes.RemoveAt(scopes.Count - 1); }
                if (result.Returned || HasError) return result;
                CountOperation();
            }
            return ExecutionResult.None;
        }

        if (statement is BreezeImport import)
            return ExecuteImport(import.Path);

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

    internal object CallNative(string name, object[] args)
    {
        int expectedCount = GetExpectedArgumentCount(name);
        if (expectedCount < 0) return FailValue<object>("Unknown function '" + name + "'");
        if (args.Length != expectedCount)
            return FailValue<object>(name + " expects " + expectedCount + " argument(s)");
        string requiredCapability = GetRequiredCapability(name);
        if (requiredCapability != "" && !EnsureCapability(requiredCapability))
            return FailValue<object>("Capability denied: " + requiredCapability);
        if (backgroundMode && IsGuiFunction(name))
            return FailValue<object>("GUI function '" + name + "' is not available in a background process");

        switch (name)
        {
            case "process":
                if (backgroundMode)
                    return FailValue<object>("Use scheduledProcess(name) in a background program");
                RegisterProcessName(ToText(args[0]));
                keepAliveWithoutWindows = true;
                hasExplicitProcessName = true;
                applicationNameChanged?.Invoke(processHandle.name);
                return processHandle;

            case "scheduledProcess":
                if (!backgroundMode)
                    return FailValue<object>("scheduledProcess(name) must be started with Run Background");
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
                    lock (processRegistryLock)
                    {
                        if (namedProcesses.TryGetValue(processName, out BreezeProcessHandle found) && found.Running)
                            return found;
                    }
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

            case "broadcast":
                return (double)Broadcast(ToText(args[0]), args[1]);

            case "request":
                {
                    BreezeProcessHandle target = Require<BreezeProcessHandle>(name, args[0]);
                    if (target == null || !target.Running) return false;
                    int id;
                    lock (processRegistryLock) id = ++nextMessageId;
                    return target.Runtime.EnqueueMessage(new BreezeProcessMessage(
                        ToText(args[1]), args[2], processHandle.name, id)) ? (double)id : 0.0;
                }

            case "reply":
                {
                    BreezeProcessMessage request = Require<BreezeProcessMessage>(name, args[0]);
                    if (request == null || request.Id == 0) return false;
                    lock (processRegistryLock)
                    {
                        if (!namedProcesses.TryGetValue(request.Sender, out BreezeProcessHandle target) || !target.Running) return false;
                        return target.Runtime.EnqueueMessage(new BreezeProcessMessage(
                            request.Name + ".reply", args[1], processHandle.name, 0, request.Id));
                    }
                }

            case "tryFindProcess":
                {
                    string processName = ToText(args[0]);
                    lock (processRegistryLock)
                    {
                        if (namedProcesses.TryGetValue(processName, out BreezeProcessHandle found) && found.Running)
                            return found;
                    }
                    return null;
                }

            case "service":
                {
                    if (!backgroundMode) return FailValue<object>("service must run as a background program");
                    string serviceName = ToText(args[0]);
                    RegisterProcessName(serviceName);
                    serviceHandle = BreezeServiceManager.Register(this, OwnerProcess, serviceName, ToBool(args[1]), ToBool(args[2]));
                    if (serviceHandle == null) return FailValue<object>("Service '" + serviceName + "' is already running");
                    keepAliveWithoutWindows = true;
                    hasExplicitProcessName = true;
                    applicationNameChanged?.Invoke(processHandle.name);
                    return serviceHandle;
                }

            case "serviceDependency":
                if (serviceHandle == null) return FailValue<object>("Declare a service before adding dependencies");
                return BreezeServiceManager.AddDependency(serviceHandle, ToText(args[0]));

            case "dependenciesReady":
                return serviceHandle != null && BreezeServiceManager.DependenciesReady(serviceHandle);

            case "startService":
                return BreezeServiceManager.StartFile(ToText(args[0]));

            case "stopService":
                return BreezeServiceManager.Stop(ToText(args[0]));

            case "restartService":
                return BreezeServiceManager.Restart(ToText(args[0]));

            case "serviceState":
                return BreezeServiceManager.GetState(ToText(args[0]));

            case "fileExists":
                return File.Exists(ToText(args[0]));

            case "directoryExists":
                return Directory.Exists(ToText(args[0]));

            case "createDirectory":
                return Directory.CreateDirectory(ToText(args[0]));

            case "deleteFile":
                File.Delete(ToText(args[0]));
                break;

            case "deleteDirectory":
                Directory.Delete(ToText(args[0]), ToBool(args[1]));
                break;

            case "copyFile":
                File.Copy(ToText(args[0]), ToText(args[1]), ToBool(args[2]));
                break;

            case "copyDirectory":
                File.Copy(ToText(args[0]), ToText(args[1]), ToBool(args[2]));
                break;

            case "moveFile":
                File.Move(ToText(args[0]), ToText(args[1]), ToBool(args[2]));
                break;

            case "moveDirectory":
                Directory.Move(ToText(args[0]), ToText(args[1]));
                break;

            case "renamePath":
                File.Copy(ToText(args[0]), ToText(args[1]), ToBool(args[2]));
                break;

            case "readFile":
                {
                    return File.ReadAllText(ToText(args[0]));
                }

            case "tryReadFile":
                {
                    ;
                    return File.ReadAllText(ToText(args[0])); ;
                }

            case "writeFile":
                File.WriteAllText(ToText(args[0]), ToText(args[1]));
                break;

            case "fileInfo":
                {
                    //if (FileSystemManager.Current != null && FileSystemManager.Current.TryGetInfo(ToText(args[0]), out WindoseFileInfo info))
                    //    return info;
                    return FailValue<object>("fileInfo not implemented " + ToText(args[0]));
                }

            case "watchPath":
                return WatchPath(ToText(args[0]), ToBool(args[1]));

            case "registryGet":
                return ToBreezeRegistryValue(SystemRegistry.Get(ToText(args[0])));

            case "registrySet":
                if (!EnsureRegistryWrite(ToText(args[0]))) return false;
                return SystemRegistry.Set(ToText(args[0]), args[1]);

            case "registryDefine":
                if (!EnsureRegistryWrite(ToText(args[0]))) return false;
                SystemRegistry.Define(ToText(args[0]), args[1], ToText(args[2]), ToBool(args[3]));
                return SystemRegistry.Save();

            case "registryDelete":
                if (!EnsureRegistryWrite(ToText(args[0]))) return false;
                return SystemRegistry.Delete(ToText(args[0]));

            case "registryExists":
                return SystemRegistry.Exists(ToText(args[0]));

            case "registryKeys":
                {
                    List<string> keys = SystemRegistry.GetKeys(ToText(args[0]));
                    List<object> result = new List<object>(keys.Count);
                    for (int i = 0; i < keys.Count; i++) result.Add(keys[i]);
                    return result;
                }

            case "registryInfo":
                {
                    RegistryEntry entry = SystemRegistry.GetEntry(ToText(args[0]));
                    if (entry == null) return null;
                    Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    result["key"] = entry.Key;
                    result["value"] = ToBreezeRegistryValue(entry.Value);
                    result["defaultValue"] = ToBreezeRegistryValue(entry.DefaultValue);
                    result["description"] = entry.Description;
                    result["requiresRestart"] = entry.RequiresRestart;
                    result["builtIn"] = entry.IsBuiltIn;
                    return result;
                }

            case "registrySave":
                return SystemRegistry.Save();

            case "registryRestartRequired":
                return SystemRegistry.RestartRequired;

            case "clock":
                return (double)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond);

            case "processCount":
                return (double)ProcessManger.ProcessCount;

            case "log":
                Serial.WriteString("[Breeze:" + processHandle.name + "] " + ToText(args[0]) + "\n");
                return true;

            case "capability":
                return EnsureCapability(ToText(args[0]));

            case "hasCapability":
                return grantedCapabilities.Contains(ToText(args[0]));

            case "capabilities":
                {
                    List<object> result = new List<object>();
                    foreach (string capability in grantedCapabilities) result.Add(capability);
                    return result;
                }

            case "object":
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            case "objectGet":
                {
                    Dictionary<string, object> value = Require<Dictionary<string, object>>(name, args[0]);
                    if (value == null) return null;
                    return value.TryGetValue(ToText(args[1]), out object propertyValue) ? propertyValue : null;
                }

            case "objectSet":
                {
                    Dictionary<string, object> value = Require<Dictionary<string, object>>(name, args[0]);
                    if (value == null) return false;
                    value[ToText(args[1])] = args[2];
                    return args[2];
                }

            case "objectHas":
                {
                    Dictionary<string, object> value = Require<Dictionary<string, object>>(name, args[0]);
                    return value != null && value.ContainsKey(ToText(args[1]));
                }

            case "objectRemove":
                {
                    Dictionary<string, object> value = Require<Dictionary<string, object>>(name, args[0]);
                    return value != null && value.Remove(ToText(args[1]));
                }

            case "objectKeys":
                {
                    Dictionary<string, object> value = Require<Dictionary<string, object>>(name, args[0]);
                    if (value == null) return null;
                    List<object> keys = new List<object>();
                    foreach (string key in value.Keys) keys.Add(key);
                    return keys;
                }

            case "objectCount":
                {
                    Dictionary<string, object> value = Require<Dictionary<string, object>>(name, args[0]);
                    return value == null ? null : (double)value.Count;
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

            case "getDirectories":
                return GetPaths(ToText(args[0]), true);

            case "getFiles":
                return GetPaths(ToText(args[0]), false);

            case "fileName":
                return FileSystemManager.GetName(ToText(args[0]));

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

            // === NEW CONTROLS ===

            case "progressBar":
                RequireCount(name, args, 3);
                {
                    int width = ToInt(args[1]);
                    int height = ToInt(args[2]);
                    if (HasError) return null;
                    return new ProgressBar(0, 0, width, height)
                    {
                        text = ToText(args[0]),
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                }

            case "progressValue":
                RequireCount(name, args, 2);
                {
                    ProgressBar bar = Require<ProgressBar>(name, args[0]);
                    if (bar == null) return null;
                    bar.Value = (float)ToNumber(args[1]);
                    return args[0];
                }

            case "progressIndeterminate":
                RequireCount(name, args, 2);
                {
                    ProgressBar bar = Require<ProgressBar>(name, args[0]);
                    if (bar == null) return null;
                    bar.Indeterminate = ToBool(args[1]);
                    return args[0];
                }

            case "checkbox":
                RequireCount(name, args, 1);
                {
                    Checkbox cb = new Checkbox(0, 0)
                    {
                        text = ToText(args[0]),
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                    return cb;
                }

            case "radioButton":
                RequireCount(name, args, 2);
                {
                    string group = ToText(args[1]);
                    if (HasError) return null;
                    RadioButton rb = new RadioButton(0, 0)
                    {
                        text = ToText(args[0]),
                        Group = group,
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                    return rb;
                }

            case "radioChecked":
                RequireCount(name, args, 1);
                {
                    RadioButton rb = Require<RadioButton>(name, args[0]);
                    if (rb == null) return null;
                    return rb.Checked;
                }

            case "comboBox":
                RequireCount(name, args, 2);
                {
                    int width = ToInt(args[1]);
                    if (HasError) return null;
                    return new ComboBox(0, 0, width)
                    {
                        text = ToText(args[0]),
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                }

            case "comboAdd":
                RequireCount(name, args, 2);
                {
                    ComboBox combo = Require<ComboBox>(name, args[0]);
                    if (combo == null) return null;
                    combo.AddItem(args[1]);
                    return args[0];
                }

            case "comboClear":
                RequireCount(name, args, 1);
                {
                    ComboBox combo = Require<ComboBox>(name, args[0]);
                    if (combo == null) return null;
                    combo.ClearItems();
                    return args[0];
                }

            case "comboSelected":
                RequireCount(name, args, 1);
                {
                    ComboBox combo = Require<ComboBox>(name, args[0]);
                    if (combo == null) return null;
                    return (double)combo.SelectedIndex;
                }

            case "comboText":
                RequireCount(name, args, 1);
                {
                    ComboBox combo = Require<ComboBox>(name, args[0]);
                    if (combo == null) return null;
                    return combo.SelectedText;
                }

            case "tabControl":
                RequireCount(name, args, 3);
                {
                    int width = ToInt(args[1]);
                    int height = ToInt(args[2]);
                    if (HasError) return null;
                    return new TabControl(0, 0, width, height)
                    {
                        text = ToText(args[0]),
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                }

            case "tabAdd":
                RequireCount(name, args, 2);
                {
                    TabControl tabs = Require<TabControl>(name, args[0]);
                    if (tabs == null) return null;
                    return tabs.AddPage(ToText(args[1]));
                }

            case "tabRemove":
                RequireCount(name, args, 2);
                {
                    TabControl tabs = Require<TabControl>(name, args[0]);
                    if (tabs == null) return null;
                    int index = ToInt(args[1]);
                    if (HasError) return null;
                    tabs.RemovePageAt(index);
                    return args[0];
                }

            case "tabSelected":
                RequireCount(name, args, 1);
                {
                    TabControl tabs = Require<TabControl>(name, args[0]);
                    if (tabs == null) return null;
                    return (double)tabs.SelectedIndex;
                }

            case "slider":
                RequireCount(name, args, 3);
                {
                    int width = ToInt(args[1]);
                    int height = ToInt(args[2]);
                    if (HasError) return null;
                    return new Slider(0, 0, width, height, Orientation.Horizontal)
                    {
                        text = ToText(args[0]),
                        clampSize = false,
                        Margin = new Thickness(0),
                    };
                }

            case "sliderValue":
                RequireCount(name, args, 2);
                {
                    Slider slider = Require<Slider>(name, args[0]);
                    if (slider == null) return null;
                    if (args.Length > 1)
                    {
                        slider.Value = (float)ToNumber(args[1]);
                        return args[0];
                    }
                    return (double)slider.Value;
                }

            case "sliderRange":
                RequireCount(name, args, 3);
                {
                    Slider slider = Require<Slider>(name, args[0]);
                    if (slider == null) return null;
                    slider.Minimum = (float)ToNumber(args[1]);
                    slider.Maximum = (float)ToNumber(args[2]);
                    return args[0];
                }

            case "tooltip":
                RequireCount(name, args, 0);
                return new Tooltip();

            case "tooltipAttach":
                RequireCount(name, args, 3);
                {
                    Tooltip tip = Require<Tooltip>(name, args[0]);
                    Component target = Require<Component>(name, args[1]);
                    if (tip == null || target == null) return null;
                    tip.AttachTo(target, ToText(args[2]));
                    return args[0];
                }

            default:
                return FailValue<object>("Unknown function '" + name + "'");
        }

        return null;

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

        if (target is BreezeServiceHandle service)
        {
            if (service.Runtime != this) { Fail("Service belongs to another runtime"); return; }
            if (eventName == "update") { processUpdateBody = body; return; }
            if (eventName == "message") { processMessageBody = body; return; }
            Fail("Event '" + eventName + "' is not supported by a service");
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
        if (target is Checkbox checkbox && eventName == "click") { checkbox.Click = callback; return; }
        if (target is Checkbox checkboxChange && eventName == "change") { checkboxChange.CheckedChanged += _ => callback(); return; }
        if (target is RadioButton radio && eventName == "change") { radio.CheckedChanged += _ => callback(); return; }
        if (target is ComboBox combo && eventName == "change") { combo.SelectedIndexChanged += _ => callback(); return; }
        if (target is Slider slider && eventName == "change") { slider.ValueChanged += _ => callback(); return; }
        if (target is TabControl tabs && eventName == "change") { tabs.SelectedIndexChanged += _ => callback(); return; }
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
        Serial.WriteString("BreezeRuntime.UpdateProcess: owner=" + (OwnerProcess?.startInfo?.Name ?? processHandle.name) + "\n");
        if (terminated) return;
        int delivered = 0;
        int eventLimit = backgroundMode ? MaxBackgroundEventsPerUpdate : MaxEventsPerUpdate;

        if (processUpdateBody != null)
        {
            ExecuteDeferredBody(processUpdateBody);
            if (terminated) return;
        }

        long now = DateTime.UtcNow.Ticks;
        for (int i = 0; i < timers.Count && delivered < eventLimit; i++)
        {
            BreezeTimerHandle timer = timers[i];
            if (!timer.active || timer.TickBody == null || now < timer.NextTick) continue;
            timer.Reset();
            SetGlobal("event", timer);
            ExecuteDeferredBody(timer.TickBody);
            delivered++;
            if (terminated) return;
        }

        while (delivered < eventLimit)
        {
            BreezeProcessMessage message;
            lock (messageQueueLock)
            {
                if (messageQueue.Count == 0) break;
                message = messageQueue[0];
                messageQueue.RemoveAt(0);
            }
            delivered++;
            if (processMessageBody == null) continue;
            SetGlobal("event", message);
            ExecuteDeferredBody(processMessageBody);
            if (terminated) return;
        }
        Serial.WriteString("BreezeRuntime.UpdateProcess: delivered=" + delivered + "\n");
    }

    public int GetRecommendedUpdateIntervalMs()
    {
        if (terminated) return 250;
        if (processUpdateBody != null) return 100;

        lock (messageQueueLock)
            if (messageQueue.Count > 0) return 10;

        long now = DateTime.UtcNow.Ticks;
        long nearest = long.MaxValue;
        for (int i = 0; i < timers.Count; i++)
        {
            BreezeTimerHandle timer = timers[i];
            if (!timer.active || timer.TickBody == null) continue;
            long remaining = timer.NextTick - now;
            if (remaining < nearest) nearest = remaining;
        }

        if (nearest == long.MaxValue) return 250;
        int remainingMs = (int)Math.Max(10, nearest / TimeSpan.TicksPerMillisecond);
        return Math.Min(250, remainingMs);
    }

    private void ExecuteDeferredBody(List<BreezeStatement> body)
    {
        Serial.WriteString("BreezeRuntime.ExecuteDeferredBody: entering\n");
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
            Serial.WriteString("BreezeRuntime.ExecuteDeferredBody: exception - " + exception.Message + "\n");
            Fail(exception.Message);
        }
        if (LastError == null) return;
        TerminateApplication();
        BreezeHost.ShowError(LastError);
    }

    private object GetProperty(object target, string property)
    {
        if (target is Dictionary<string, object> customObject)
            return customObject.TryGetValue(property, out object customValue) ? customValue : null;

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
                case "id": return (double)message.Id;
                case "replyTo": return (double)message.ReplyTo;
                default: return FailValue<object>("Unknown message property '" + property + "'");
            }
        }

        if (target is BreezeServiceHandle service)
        {
            switch (property)
            {
                case "name": return service.Name;
                case "state": return service.State;
                case "running": return service.State == "running";
                case "protected": return service.Protected;
                case "restartOnFailure": return service.RestartOnFailure;
                case "dependenciesReady": return BreezeServiceManager.DependenciesReady(service);
                default: return FailValue<object>("Unknown service property '" + property + "'");
            }
        }

        if (target is WindoseFileInfo fileInfo)
        {
            switch (property)
            {
                case "name": return fileInfo.Name;
                case "path": return fileInfo.FullPath;
                case "isDirectory": return fileInfo.IsDirectory;
                case "size": return (double)fileInfo.Size;
                case "childCount": return (double)fileInfo.ChildCount;
                case "created": return fileInfo.CreatedAt.ToString();
                case "modified": return fileInfo.ModifiedAt.ToString();
                default: return FailValue<object>("Unknown file info property '" + property + "'");
            }
        }

        if (target is FileSystemChange change)
        {
            switch (property)
            {
                case "type": return change.Type.ToString();
                case "path": return change.Path;
                case "previousPath": return change.PreviousPath;
                default: return FailValue<object>("Unknown filesystem change property '" + property + "'");
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
                case "value":
                    if (component is ProgressBar progress) return (double)progress.Value;
                    if (component is Slider slider) return (double)slider.Value;
                    return FailValue<object>("This component has no value property");
                case "checked":
                    if (component is Checkbox checkbox) return checkbox.Checked;
                    if (component is RadioButton radio) return radio.Checked;
                    return FailValue<object>("This component has no checked property");
                case "selectedIndex":
                    if (component is ComboBox combo) return (double)combo.SelectedIndex;
                    if (component is TabControl tabs) return (double)tabs.SelectedIndex;
                    return FailValue<object>("This component has no selectedIndex property");
                case "selectedText":
                    if (component is ComboBox comboText) return comboText.SelectedText;
                    return FailValue<object>("This component has no selectedText property");
                default: return FailValue<object>("Unknown component property '" + property + "'");
            }
        }
        return FailValue<object>("Object does not expose properties");
    }

    private void SetProperty(object target, string property, object value)
    {
        if (target is Dictionary<string, object> customObject)
        {
            customObject[property] = value;
            return;
        }

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
                    else if (component is ProgressBar progress) progress.fontSize = fontSize;
                    else if (component is RadioButton radio) radio.fontSize = fontSize;
                    else if (component is ComboBox combo) combo.fontSize = fontSize;
                    else if (component is Slider slider) slider.fontSize = fontSize;
                    else if (component is TabControl tabs) tabs.fontSize = fontSize;
                    else if (component is Tooltip tooltip) tooltip.fontSize = fontSize;
                    else { Fail("This component has no fontSize property"); return; }
                    break;
                }
            case "value":
                {
                    if (component is ProgressBar progress) progress.Value = (float)ToNumber(value);
                    else if (component is Slider slider) slider.Value = (float)ToNumber(value);
                    else { Fail("This component has no value property"); return; }
                    break;
                }
            case "minimum":
                {
                    if (component is ProgressBar progress) progress.Minimum = (float)ToNumber(value);
                    else if (component is Slider slider) slider.Minimum = (float)ToNumber(value);
                    else { Fail("This component has no minimum property"); return; }
                    break;
                }
            case "maximum":
                {
                    if (component is ProgressBar progress) progress.Maximum = (float)ToNumber(value);
                    else if (component is Slider slider) slider.Maximum = (float)ToNumber(value);
                    else { Fail("This component has no maximum property"); return; }
                    break;
                }
            case "checked":
                {
                    if (component is Checkbox checkbox) checkbox.Checked = ToBool(value);
                    else if (component is RadioButton radio) radio.Checked = ToBool(value);
                    else { Fail("This component has no checked property"); return; }
                    break;
                }
            case "indeterminate":
                {
                    if (component is ProgressBar progress) progress.Indeterminate = ToBool(value);
                    else { Fail("This component has no indeterminate property"); return; }
                    break;
                }
            case "showTicks":
                {
                    if (component is Slider slider) slider.showTicks = ToBool(value);
                    else { Fail("This component has no showTicks property"); return; }
                    break;
                }
            case "showValue":
                {
                    if (component is Slider slider) slider.ShowValue = ToBool(value);
                    else { Fail("This component has no showValue property"); return; }
                    break;
                }
            case "showText":
                {
                    if (component is ProgressBar progress) progress.showText = ToBool(value);
                    else { Fail("This component has no showText property"); return; }
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
        lock (processRegistryLock)
        {
            if (!string.IsNullOrEmpty(processHandle.name) &&
                namedProcesses.TryGetValue(processHandle.name, out BreezeProcessHandle current) &&
                current == processHandle)
                namedProcesses.Remove(processHandle.name);

            processHandle.name = string.IsNullOrEmpty(name) ? "Breeze Application" : name;
            namedProcesses[processHandle.name] = processHandle;
        }
    }

    private bool EnqueueMessage(BreezeProcessMessage message)
    {
        bool queued;
        lock (messageQueueLock)
        {
            if (terminated || messageQueue.Count >= MaxQueuedMessages) return false;
            messageQueue.Add(message);
            queued = true;
        }
        if (queued) WorkAvailable?.Invoke();
        return queued;
    }

    private int Broadcast(string messageName, object data)
    {
        List<BreezeProcessHandle> targets = new List<BreezeProcessHandle>();
        lock (processRegistryLock)
        {
            foreach (BreezeProcessHandle process in namedProcesses.Values)
                if (process != processHandle && process.Running) targets.Add(process);
        }

        int delivered = 0;
        for (int i = 0; i < targets.Count; i++)
            if (targets[i].Runtime.EnqueueMessage(new BreezeProcessMessage(messageName, data, processHandle.name))) delivered++;
        return delivered;
    }

    private bool WatchPath(string path, bool recursive)
    {
        string watchedPath = FileSystemManager.NormalizePath(path);
        BreezeFileWatch watch = new BreezeFileWatch { Path = watchedPath, Recursive = recursive };
        watch.Handler = change =>
        {
            string changedPath = FileSystemManager.NormalizePath(change.Path);
            bool exact = string.Equals(changedPath, watch.Path, StringComparison.OrdinalIgnoreCase);
            bool child = recursive && changedPath.StartsWith(
                watch.Path + (watch.Path.EndsWith("\\") ? "" : "\\"), StringComparison.OrdinalIgnoreCase);
            if (exact || child)
                EnqueueMessage(new BreezeProcessMessage("filesystem.changed", change, "filesystem"));
        };
        fileWatches.Add(watch);
        keepAliveWithoutWindows = true;
        return true;
    }


    private static bool IsGuiFunction(string name)
    {
        switch (name)
        {
            case "window":
            case "windowRoot":
            case "dockPanel":
            case "stackPanel":
            case "panel":
            case "button":
            case "textField":
            case "toolbar":
            case "toolbarButton":
            case "statusBar":
            case "statusPanel":
            case "menuBar":
            case "menu":
            case "menuItem":
            case "treeView":
            case "treeRoot":
            case "treeChild":
            case "listView":
            case "listItem":
            case "listClear":
            case "listMode":
            case "scrollView":
            case "loadDirectory":
            case "dock":
            case "stack":
            case "add":
            case "show":
            case "close":
            // New controls
            case "progressBar":
            case "progressValue":
            case "progressIndeterminate":
            case "checkbox":
            case "radioButton":
            case "radioChecked":
            case "comboBox":
            case "comboAdd":
            case "comboClear":
            case "comboSelected":
            case "comboText":
            case "tabControl":
            case "tabAdd":
            case "tabRemove":
            case "tabSelected":
            case "slider":
            case "sliderValue":
            case "sliderRange":
            case "tooltip":
            case "tooltipAttach":
                return true;
            default:
                return false;
        }
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
        int limit = backgroundMode ? MaxBackgroundOperations : MaxOperations;
        if (++operationCount > limit)
            Fail("Execution limit exceeded (" + limit + " operations)");
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

    private List<object> GetIterationValues(object collection)
    {
        if (collection is List<object> list) return new List<object>(list);
        if (collection is Dictionary<string, object> customObject)
        {
            List<object> keys = new List<object>();
            foreach (string key in customObject.Keys) keys.Add(key);
            return keys;
        }
        Fail("for-in expected a list or object");
        return null;
    }

    private ExecutionResult ExecuteImport(string requestedPath)
    {
        if (!EnsureCapability("filesystem.read"))
        {
            Fail("Capability denied: filesystem.read");
            return ExecutionResult.None;
        }
        if (++importDepth > 32)
        {
            importDepth--;
            Fail("Module import depth exceeded (32)");
            return ExecutionResult.None;
        }

        string path = requestedPath != null && requestedPath.Contains(":")
            ? FileSystemManager.NormalizePath(requestedPath)
            : FileSystemManager.Combine(currentModuleDirectory == "" ? @"0:\" : currentModuleDirectory, requestedPath);
        if (importedModules.Contains(path))
        {
            importDepth--;
            return ExecutionResult.None;
        }

        string source = File.ReadAllText(path);
        if (source == string.Empty)
        {
            importDepth--;
            Fail("Could not import module " + path);
            return ExecutionResult.None;
        }

        importedModules.Add(path);
        BreezeLexer lexer = new BreezeLexer(source);
        List<BreezeToken> tokens = lexer.Tokenize();
        if (lexer.ErrorMessage != null)
        {
            importDepth--;
            Fail(path + ": " + lexer.ErrorMessage);
            return ExecutionResult.None;
        }
        BreezeParser parser = new BreezeParser(tokens);
        List<BreezeStatement> statements = parser.Parse();
        if (parser.ErrorMessage != null)
        {
            importDepth--;
            Fail(path + ": " + parser.ErrorMessage);
            return ExecutionResult.None;
        }

        string previousDirectory = currentModuleDirectory;
        currentModuleDirectory = FileSystemManager.GetParent(path);
        ExecutionResult result;
        try { result = ExecuteBlock(statements); }
        finally
        {
            currentModuleDirectory = previousDirectory;
            importDepth--;
        }
        if (result.Returned) Fail("return can only be used inside a function");
        return ExecutionResult.None;
    }

    private bool EnsureCapability(string capability)
    {
        if (string.IsNullOrWhiteSpace(capability)) return false;
        if (grantedCapabilities.Contains(capability)) return true;
        string executablePath = OwnerProcess?.startInfo?.ExecutablePath ?? "";
        if (!BreezeCapabilityPolicy.IsAllowed(executablePath, capability)) return false;
        grantedCapabilities.Add(capability);
        return true;
    }

    private bool EnsureRegistryWrite(string key)
    {
        string normalized = SystemRegistry.NormalizeKey(key);
        string capability = normalized.StartsWith("System/", StringComparison.OrdinalIgnoreCase)
            ? "registry.write"
            : "registry.custom.write";
        if (EnsureCapability(capability)) return true;
        Fail("Capability denied: " + capability);
        return false;
    }

    private static string GetRequiredCapability(string function)
    {
        if (function == "capability" || function == "hasCapability" || function == "capabilities") return "";
        if (IsGuiFunction(function)) return "ui";
        switch (function)
        {
            case "getDirectories":
            case "getFiles":
            case "fileName":
            case "fileExists":
            case "directoryExists":
            case "readFile":
            case "tryReadFile":
            case "fileInfo":
            case "loadDirectory":
            case "watchPath":
            case "clearWatches": return "filesystem.read";
            case "registryGet":
            case "registryExists":
            case "registryKeys":
            case "registryInfo":
            case "registryRestartRequired": return "registry.read";
            case "createDirectory":
            case "deleteFile":
            case "deleteDirectory":
            case "copyFile":
            case "copyDirectory":
            case "moveFile":
            case "moveDirectory":
            case "renamePath":
            case "writeFile": return "filesystem.write";
            case "registrySave": return "registry.custom.write";
            case "service":
            case "serviceDependency":
            case "dependenciesReady":
            case "startService":
            case "stopService":
            case "restartService":
            case "serviceState": return "service.control";
            case "send":
            case "broadcast":
            case "request":
            case "reply":
            case "findProcess":
            case "tryFindProcess": return "ipc";
            case "stopProcess": return "process.control";
            case "processCount": case "clock": return "process.inspect";
            case "log": case "print": return "logging";
            default: return "";
        }
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
            ListViewItem item = list.AddItem(FileSystemManager.GetName(directory), tag: directory);
            item.isFolder = true;
            item.type = "File Folder";
        }

        string[] files = Directory.GetFiles(path);
        for (int i = 0; i < files.Length; i++)
        {
            string file = files[i];
            ListViewItem item = list.AddItem(FileSystemManager.GetName(file), tag: file);
            item.isFolder = false;
            item.type = string.Equals(FileSystemManager.GetExtension(file), ".breeze", StringComparison.OrdinalIgnoreCase)
                ? "Breeze Script"
                : "File";
        }

        list.MarkDirty();
    }

    private List<object> GetPaths(string path, bool directories)
    {
        if (!Directory.Exists(path))
        {
            Fail("Directory does not exist: " + path);
            return null;
        }

        string[] paths = directories ? Directory.GetDirectories(path) : Directory.GetFiles(path);
        List<object> result = new List<object>(paths.Length);
        for (int i = 0; i < paths.Length; i++) result.Add(paths[i]);
        return result;
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

    private static object ToBreezeRegistryValue(object value)
    {
        if (value is long integer) return (double)integer;
        if (value is float single) return (double)single;
        return value;
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
        lock (messageQueueLock) messageQueue.Clear();
        lock (processRegistryLock)
        {
            if (namedProcesses.TryGetValue(processHandle.name, out BreezeProcessHandle registered) && registered == processHandle)
                namedProcesses.Remove(processHandle.name);
        }
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

    internal static int GetExpectedArgumentCount(string name) => name switch
    {
        "window" => 5,
        "process" => 1,
        "scheduledProcess" => 1,
        "stopProcess" => 1,
        "findProcess" => 1,
        "tryFindProcess" => 1,
        "send" => 3,
        "broadcast" => 2,
        "request" => 3,
        "reply" => 2,
        "service" => 3,
        "serviceDependency" => 1,
        "dependenciesReady" => 0,
        "startService" => 1,
        "stopService" => 1,
        "restartService" => 1,
        "serviceState" => 1,
        "timer" => 1,
        "startTimer" => 1,
        "stopTimer" => 1,
        "getDirectories" => 1,
        "getFiles" => 1,
        "fileName" => 1,
        "fileExists" => 1,
        "directoryExists" => 1,
        "createDirectory" => 1,
        "deleteFile" => 1,
        "deleteDirectory" => 2,
        "copyFile" => 3,
        "copyDirectory" => 2,
        "moveFile" => 3,
        "moveDirectory" => 3,
        "renamePath" => 3,
        "readFile" => 1,
        "tryReadFile" => 1,
        "writeFile" => 3,
        "fileInfo" => 1,
        "watchPath" => 2,
        "clearWatches" => 0,
        "registryGet" => 1,
        "registrySet" => 2,
        "registryDefine" => 4,
        "registryDelete" => 1,
        "registryExists" => 1,
        "registryKeys" => 1,
        "registryInfo" => 1,
        "registrySave" => 0,
        "registryRestartRequired" => 0,
        "clock" => 0,
        "processCount" => 0,
        "log" => 1,
        "capability" => 1,
        "hasCapability" => 1,
        "capabilities" => 0,
        "object" => 0,
        "objectGet" => 2,
        "objectSet" => 3,
        "objectHas" => 2,
        "objectRemove" => 2,
        "objectKeys" => 1,
        "objectCount" => 1,
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
        // New controls
        "progressBar" => 3,
        "progressValue" => 2,
        "progressIndeterminate" => 2,
        "checkbox" => 1,
        "radioButton" => 2,
        "radioChecked" => 1,
        "comboBox" => 2,
        "comboAdd" => 2,
        "comboClear" => 1,
        "comboSelected" => 1,
        "comboText" => 1,
        "tabControl" => 3,
        "tabAdd" => 2,
        "tabRemove" => 2,
        "tabSelected" => 1,
        "slider" => 3,
        "sliderValue" => 2,
        "sliderRange" => 3,
        "tooltip" => 0,
        "tooltipAttach" => 3,
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
