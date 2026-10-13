// LuaWindoseLib.cs  —  Lua 5.2 (UniLua / Cosmos.Executable.Lua) API for Windose
//
// Usage:
//     var lua = LuaAPI.NewState();                       // however you create it today
//     lua.L_OpenLibs();
//     new LuaWindoseLib("/mnt/Apps/foo.lua",
//                       new[] { "fs.read", "fs.write", "ui.window", "reg.read" }).Install(lua);
//     lua.L_DoFile("/mnt/Apps/foo.lua");                 // or DoString
//
// Lua side:
//     windose.log("hello")
//     local w, h = windose.screen()
//     windose.fs.write("/mnt/Programs/x.txt", "data")
//     windose.open("explorer")
//
// NOTE (namespace trap): this file lives in Windose.System.Lua. Inside any Windose.System.*
// namespace, the identifiers `System` and `Kernel` resolve to *Windose* namespaces, so:
//   - never write fully-qualified `System.Xxx` here, use the usings below
//   - the kernel class is referenced as WKernel (Windose.Kernel)
//
// ADAPT markers = places where I could not see your source and guessed a signature.

using Cosmos.Executable.Lua;
using System.Text;
using Windose.System.System_Calls;
using WKernel = Windose.Kernel;

namespace Windose.System.Lua;

public sealed class LuaWindoseLib
{
    public const string LibName = "windose";
    public const string Version = "0.1";

    private const string LogTag = "LUA";
    private const int MaxSleepMs = 10_000;
    private const int MaxReadBytes = 1024 * 1024;

    private readonly string _scriptPath;
    private readonly HashSet<string> _caps;

    /// <param name="scriptPath">Script being run (used for log lines).</param>
    /// <param name="capabilities">
    /// Granted capabilities: fs.read, fs.write, ui.window, reg.read, reg.write.
    /// Anything not listed raises a Lua error when called.
    /// </param>
    public LuaWindoseLib(string scriptPath, IEnumerable<string> capabilities)
    {
        _scriptPath = scriptPath;
        _caps = new HashSet<string>();
        if (capabilities != null)
            foreach (string c in capabilities) _caps.Add(c);
    }

    /// <summary>Registers the global `windose` table in the given state.</summary>
    public void Install(ILuaState lua)
    {
        lua.L_RequireF(LibName, Open, true);
        lua.Pop(1);
    }



    // Instance method group => one delegate allocation per Install, not per call.
    private int Open(ILuaState lua)
    {
        NameFuncPair[] core =
        [
            new NameFuncPair("log",      Log),
            new NameFuncPair("ticks",    Ticks),
            new NameFuncPair("fps",      Fps),
            new NameFuncPair("delta_ms", DeltaMs),
            new NameFuncPair("screen",   Screen),
            new NameFuncPair("sleep",    Sleep),
            new NameFuncPair("open",     OpenWindow),
        ];
        lua.L_NewLib(core);

        lua.PushString(Version);
        lua.SetField(-2, "version");

        // windose.fs
        NameFuncPair[] fs =
        [
            new NameFuncPair("read",   FsRead),
            new NameFuncPair("write",  FsWrite),
            new NameFuncPair("append", FsAppend),
            new NameFuncPair("exists", FsExists),
            new NameFuncPair("list",   FsList),
            new NameFuncPair("mkdir",  FsMkdir),
            new NameFuncPair("delete", FsDelete),
        ];
        lua.L_NewLib(fs);
        lua.SetField(-2, "fs");

        // windose.reg
        NameFuncPair[] reg =
        [
            new NameFuncPair("get", RegGet),
            new NameFuncPair("set", RegSet),
        ];
        lua.L_NewLib(reg);
        lua.SetField(-2, "reg");

        return 1;
    }

    // ---------------------------------------------------------------- helpers

    private bool Has(string cap) => _caps.Contains("*") || _caps.Contains(cap);

    private int Denied(ILuaState lua, string cap)
        => lua.L_Error("permission denied: " + cap);

    private int Fail(ILuaState lua, string msg)
    {
        lua.PushNil();
        lua.PushString(msg);
        return 2;
    }

    // Scripts only get /mnt/**. Collapses "." and ".." and rejects anything that escapes /mnt.
    private static bool TryNormalize(string raw, out string path)
    {
        path = "";
        if (string.IsNullOrEmpty(raw)) return false;

        raw = raw.Replace('\\', '/');
        if (!raw.StartsWith("/mnt/")) return false;

        string[] parts = raw.Split('/');
        var stack = new List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            if (p.Length == 0 || p == ".") continue;
            if (p == "..")
            {
                if (stack.Count == 0) return false;
                stack.RemoveAt(stack.Count - 1);
            }
            else stack.Add(p);
        }

        if (stack.Count < 2 || stack[0] != "mnt") return false;
        path = "/" + string.Join("/", stack);
        return true;
    }

    private bool CheckPath(ILuaState lua, int arg, out string path)
    {
        string raw = lua.L_CheckString(arg);
        return TryNormalize(raw, out path);
    }

    // ------------------------------------------------------------------- core

    // windose.log([tag,] message)
    private int Log(ILuaState lua)
    {
        string tag = LogTag;
        string msg;
        if (lua.GetTop() >= 2)
        {
            tag = lua.L_CheckString(1);
            msg = lua.L_CheckString(2);
        }
        else msg = lua.L_CheckString(1);

        // ADAPT: ConsoleMessageType lives in the namespace Kernel.cs imports for SystemLogger.
        SystemLogger.WriteLine(tag, msg, ConsoleMessageType.Log);
        return 0;
    }

    // windose.ticks() -> 100ns ticks since epoch (UTC), as a number
    private int Ticks(ILuaState lua)
    {
        lua.PushNumber((double)DateTime.UtcNow.Ticks);
        return 1;
    }

    // windose.fps() -> number
    private int Fps(ILuaState lua)
    {
        lua.PushNumber(WKernel.Fps);
        return 1;
    }

    // windose.delta_ms() -> number (last frame time)
    private int DeltaMs(ILuaState lua)
    {
        lua.PushNumber(WKernel.DeltaTimeMs);
        return 1;
    }

    // windose.screen() -> width, height
    private int Screen(ILuaState lua)
    {
        // ADAPT: Global.screenWidth/Height as used in Kernel.cs
        lua.PushInteger(Global.screenWidth);
        lua.PushInteger(Global.screenHeight);
        return 2;
    }

    // windose.sleep(ms)   — blocks only the calling thread (threads are preemptive under Cosmos)
    private int Sleep(ILuaState lua)
    {
        int ms = (int)lua.L_CheckInteger(1);
        if (ms < 0) ms = 0;
        if (ms > MaxSleepMs) ms = MaxSleepMs;
        Thread.Sleep(ms);
        return 0;
    }

    // windose.open("explorer" | "perfmon")
    private int OpenWindow(ILuaState lua)
    {
        if (!Has("ui.window")) return Denied(lua, "ui.window");

        string what = lua.L_CheckString(1);
        switch (what)
        {
            case "explorer":
                WindowManager.PostRegister(new FileExplorer(100, 100, 800, 500, "File Explorer"));
                break;
            case "perfmon":
                WindowManager.PostRegister(new PerformanceMonitor(100, 100));
                break;
            default:
                return lua.L_ArgError(1, "unknown window '" + what + "'");
        }
        return 0;
    }

    // --------------------------------------------------------------------- fs

    // windose.fs.read(path) -> string | nil, err
    private int FsRead(ILuaState lua)
    {
        if (!Has("fs.read")) return Denied(lua, "fs.read");
        if (!CheckPath(lua, 1, out string path)) return Fail(lua, "invalid path (must be under /mnt)");

        try
        {
            if (!File.Exists(path)) return Fail(lua, "not found: " + path);
            byte[] data = File.ReadAllBytes(path);
            if (data.Length > MaxReadBytes) return Fail(lua, "file too large");
            lua.PushString(Encoding.UTF8.GetString(data));
            return 1;
        }
        catch (Exception e) { return Fail(lua, e.Message); }
    }

    // windose.fs.write(path, data) -> true | nil, err
    private int FsWrite(ILuaState lua)
    {
        if (!Has("fs.write")) return Denied(lua, "fs.write");
        if (!CheckPath(lua, 1, out string path)) return Fail(lua, "invalid path (must be under /mnt)");
        string data = lua.L_CheckString(2);

        try
        {
            File.WriteAllText(path, data);
            lua.PushBoolean(true);
            return 1;
        }
        catch (Exception e) { return Fail(lua, e.Message); }
    }

    // windose.fs.append(path, data) -> true | nil, err
    private int FsAppend(ILuaState lua)
    {
        if (!Has("fs.write")) return Denied(lua, "fs.write");
        if (!CheckPath(lua, 1, out string path)) return Fail(lua, "invalid path (must be under /mnt)");
        string data = lua.L_CheckString(2);

        try
        {
            // Read-modify-write: avoids relying on FileMode.Append support in the Cosmos FS driver.
            string existing = File.Exists(path) ? File.ReadAllText(path) : "";
            File.WriteAllText(path, existing + data);
            lua.PushBoolean(true);
            return 1;
        }
        catch (Exception e) { return Fail(lua, e.Message); }
    }

    // windose.fs.exists(path) -> boolean
    private int FsExists(ILuaState lua)
    {
        if (!Has("fs.read")) return Denied(lua, "fs.read");
        if (!CheckPath(lua, 1, out string path)) { lua.PushBoolean(false); return 1; }

        try { lua.PushBoolean(File.Exists(path) || Directory.Exists(path)); }
        catch { lua.PushBoolean(false); }
        return 1;
    }

    // windose.fs.list(path) -> { "name", "dir/", ... } | nil, err      (directories get a trailing '/')
    private int FsList(ILuaState lua)
    {
        if (!Has("fs.read")) return Denied(lua, "fs.read");
        if (!CheckPath(lua, 1, out string path)) return Fail(lua, "invalid path (must be under /mnt)");

        try
        {
            if (!Directory.Exists(path)) return Fail(lua, "not a directory: " + path);

            lua.NewTable();
            int n = 1;

            string[] dirs = Directory.GetDirectories(path);
            for (int i = 0; i < dirs.Length; i++)
            {
                lua.PushString(Path.GetFileName(dirs[i]) + "/");
                lua.RawSetI(-2, n++);
            }

            string[] files = Directory.GetFiles(path);
            for (int i = 0; i < files.Length; i++)
            {
                lua.PushString(Path.GetFileName(files[i]));
                lua.RawSetI(-2, n++);
            }
            return 1;
        }
        catch (Exception e) { return Fail(lua, e.Message); }
    }

    // windose.fs.mkdir(path) -> true | nil, err
    private int FsMkdir(ILuaState lua)
    {
        if (!Has("fs.write")) return Denied(lua, "fs.write");
        if (!CheckPath(lua, 1, out string path)) return Fail(lua, "invalid path (must be under /mnt)");

        try
        {
            Directory.CreateDirectory(path);
            lua.PushBoolean(true);
            return 1;
        }
        catch (Exception e) { return Fail(lua, e.Message); }
    }

    // windose.fs.delete(path) -> true | nil, err      (files and empty directories)
    private int FsDelete(ILuaState lua)
    {
        if (!Has("fs.write")) return Denied(lua, "fs.write");
        if (!CheckPath(lua, 1, out string path)) return Fail(lua, "invalid path (must be under /mnt)");

        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path);
            else return Fail(lua, "not found: " + path);

            lua.PushBoolean(true);
            return 1;
        }
        catch (Exception e) { return Fail(lua, e.Message); }
    }

    // -------------------------------------------------------------------- reg

    // windose.reg.get(path) -> string | number | nil
    private int RegGet(ILuaState lua)
    {
        if (!Has("reg.read")) return Denied(lua, "reg.read");
        string key = lua.L_CheckString(1);

        // ADAPT: assumed `object? Registry.Get(string)`. If yours is generic or returns a
        // wrapper, change the next line only.
        object value = Registry.Get(key);

        switch (value)
        {
            case null: lua.PushNil(); break;
            case string s: lua.PushString(s); break;
            case long l: lua.PushNumber((double)l); break;
            case int i: lua.PushNumber((double)i); break;
            case double d: lua.PushNumber(d); break;
            case bool b: lua.PushBoolean(b); break;
            default: lua.PushString(value.ToString()); break;
        }
        return 1;
    }

    // windose.reg.set(path, value) -> true | nil, err
    private int RegSet(ILuaState lua)
    {
        if (!Has("reg.write")) return Denied(lua, "reg.write");
        string key = lua.L_CheckString(1);

        // ADAPT: assumed `Registry.Set(string, object)`.
        try
        {
            if (lua.IsNumber(2)) Registry.Set(key, (long)lua.L_CheckNumber(2));
            else Registry.Set(key, lua.L_CheckString(2));

            lua.PushBoolean(true);
            return 1;
        }
        catch (Exception e) { return Fail(lua, e.Message); }
    }
}