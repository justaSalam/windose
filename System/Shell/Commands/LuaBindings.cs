using Cosmos.Executable.Lua;
using System;
using System.Collections.Generic;
using System.Text;

namespace Windose.System.Shell.Commands
{
    public static class LuaBindings
    {
        public static CommandContext? CurrentContext { get; set; }
        public static void RegisterBindings(LuaInterpreter interpreter, CommandContext context)
        {
            CurrentContext = context;

            ILuaState state = interpreter.State;

            state.NewTable();

            state.PushString("write");
            state.PushCSharpFunction((luaState) =>
            {
                string text = luaState.ToString(1);
                CurrentContext?.WriteLine(text);
                return 0;
            });
            state.SetTable(-3);

            state.PushString("readLine");
            state.PushCSharpFunction((luaState) =>
            {
                string prompt = luaState.ToString(1);
                string input = CurrentContext?.ReadLine(prompt) ?? "";
                luaState.PushString(input);
                return 1;
            });
            state.SetTable(-3);

            state.PushString("clear");
            state.PushCSharpFunction((luaState) =>
            {
                CurrentContext?.Clear();
                return 0;
            });
            state.SetTable(-3);

            state.PushString("close");
            state.PushCSharpFunction((luaState) =>
            {
                CurrentContext?.Close();
                return 0;
            });
            state.SetTable(-3);

            state.PushString("resolvePath");
            state.PushCSharpFunction((luaState) =>
            {
                string path = luaState.ToString(1);
                string resolvedPath = CurrentContext?.ResolvePath(path) ?? "";
                luaState.PushString(resolvedPath);
                return 1;
            });
            state.SetTable(-3);

            state.PushString("currentDirectory");
            state.PushCSharpFunction((luaState) =>
            {
                string currentDirectory = CurrentContext?.CurrentDirectory ?? "";
                luaState.PushString(currentDirectory);
                return 1;
            });
            state.SetTable(-3);

            state.PushString("setCurrentDirectory");
            state.PushCSharpFunction((luaState) =>
            {
                string newDirectory = luaState.ToString(1);
                if (CurrentContext != null)
                {
                    CurrentContext.CurrentDirectory = newDirectory;
                }
                return 0;
            });
            state.SetTable(-3);


            state.SetGlobal("terminal");
        }

    }
}
