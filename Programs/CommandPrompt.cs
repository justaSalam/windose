using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;

public sealed class CommandPrompt : Window
{
    private readonly TerminalView output;
    private readonly CommandContext context;

    public CommandPrompt(int x = 140, int y = 100, int width = 720, int height = 460): base(x, y, width, height, "Command Prompt", true, new Png("mnt/System/Icons/console_prompt.png"))
    {
        CommandRegistry.EnsureBuiltIns();
        DockPanel root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 2, 2, 2),
            Padding = new Thickness(0),
            clampSize = false,
            useBackground = true,
            backgroundColor = System.Drawing.Color.Black,
        };


        output = new TerminalView(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(0),
            /* 
            Every command call goes through here. Executing directly on this
            callback would run on whatever thread delivers keyboard events —
            fine for now ONLY if nothing here ever calls ReadLineSync (diskpart,
            uac elevate). The moment one of those commands runs, this MUST move
            onto a separate worker thread per invocation, or that command's
            blocking read deadlocks this same input thread. Swap the body below
            for whatever your existing per-window thread spawn call is.
            */
        };

        context = new CommandContext(
     writeLine: output.WriteLine,
     clear: output.Clear,
     close: () => { },
     readLine: output.ReadLineSync);

        output.OnTopLevelSubmit = line =>
        {
            // TODO: replace with whatever Window already uses internally for
            // its own per-window thread (you noted this is implemented and
            // working elsewhere) — I don't have that API, so this is a stand-in.
            new Thread(() => CommandRegistry.Execute(context, line)).Start();
        };
        

        root.AddDockChild(output, Dock.Fill);
        AddChild(root);

        output.WriteLine("Windose Command Prompt");
        output.WriteLine("Type help for available commands.");
        output.WriteLine();
    }

    public override void HandleKeyboard(KeyEvent keyEvent) => output.HandleKeyboard(keyEvent);
    public override string GetComponentName() => "CommandPrompt";
}
