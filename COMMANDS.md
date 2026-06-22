# Windose Command Prompt

Open **Start > Programs > Command Prompt**. Type `help` to list commands and
`help <command>` to show usage.

Built-in commands include filesystem navigation and mutation, process listing,
running Breeze applications, and starting Breeze background services. Paths
without a drive are resolved relative to the prompt's current directory.

## Add a command

Register commands through `CommandRegistry`. The window and parser do not need
to be modified:

```csharp
CommandRegistry.Register(
    "hello",
    "Greets a user.",
    "hello [name]",
    (context, arguments) =>
    {
        string name = arguments.Length == 0 ? "World" : arguments[0];
        context.WriteLine("Hello, " + name);
    });
```

Registration returns `false` when the name is empty, already registered, or the
handler is missing. Register an alias after its target command:

```csharp
CommandRegistry.RegisterAlias("hi", "hello");
```

Use `context.ResolvePath(value)` for paths relative to the current directory.
Commands can call `context.WriteLine`, `context.Clear`, and `context.Close`.
Unhandled command exceptions are caught and printed instead of escaping into
the desktop loop.

Quoted arguments are kept together:

```text
hello "Breeze Developer"
```
