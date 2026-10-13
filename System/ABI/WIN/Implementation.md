1. Write the method. The signature is always private int Name(ILuaState lua). Read args with L_Check*, push return values, and return how many you pushed.

csharp
windose.fs.size(path) -> number | nil, err
private int FsSize(ILuaState lua)
{
    if (!Has("fs.read")) return Denied(lua, "fs.read");
    if (!CheckPath(lua, 1, out string path)) return Fail(lua, "invalid path (must be under /mnt)");

    try
    {
        if (!File.Exists(path)) return Fail(lua, "not found: " + path);
        lua.PushNumber((double)File.ReadAllBytes(path).Length);
        return 1;
    }
    catch (Exception e) { return Fail(lua, e.Message); }
}

2. Register it in the matching NameFuncPair[] inside Open:
csharp
new NameFuncPair("size", FsSize),   // in the `fs` array

3. Call it from Lua:

lua
local n, err = windose.fs.size("/mnt/Programs/x.txt")

Conventions to keep:

Arguments: Lua args are 1-based (L_CheckString(1)). Use L_OptInteger(n, default) for optional ones.
Permission denied or bad argument: return Denied(lua, cap) or return lua.L_ArgError(n, "msg"). Both raise a Lua error.
Runtime failures such as IO errors: return Fail(lua, msg), which gives nil, msg.
New capability: pick a string like "net.http", check it with Has("net.http"), and add it to the array when constructing the lib. No other registration is needed.
New sub-table (for example windose.net): build another NameFuncPair[], then call lua.L_NewLib(arr); lua.SetField(-2, "net"); in Open, next to the fs and reg blocks.
Cosmos limits: no lock(), and avoid lambdas and LINQ inside hot paths. Use method groups, as the file already does.

Returning a table (not covered by the existing functions):

csharp
lua.NewTable();
lua.PushString("value");  lua.SetField(-2, "key");     // t.key = "value"
lua.PushInteger(5);       lua.RawSetI(-2, 1);          // t[1] = 5
return 1;