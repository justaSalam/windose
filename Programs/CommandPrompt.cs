using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Vfs;

public sealed class CommandPrompt : Window
{
    private readonly TerminalView output;
    private readonly CommandLineInput input;
    private readonly CommandContext context;

    public CommandPrompt(int x = 140, int y = 100, int width = 720, int height = 460)
        : base(x, y, width, height, "Command Prompt", true)
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
        };

        input = new CommandLineInput(0, 0, Width, 25)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0),
        };

        context = new CommandContext(output.WriteLine, output.Clear, () => WindowManager.PostClose(this))
        {
            CurrentDirectory = "/mnt"
        };
        input.prompt = () => context.CurrentDirectory + ">";
        input.submitted = Execute;

        root.AddDockChild(input, Dock.Bottom);
        root.AddDockChild(output, Dock.Fill);
        AddChild(root);

        output.WriteLine("Windose Command Prompt");
        output.WriteLine("Type help for available commands.");
        output.WriteLine();
    }

    private void Execute(string commandLine)
    {
        output.WriteLine(context.CurrentDirectory + ">" + commandLine);
        CommandRegistry.Execute(context, commandLine);
        input.MarkDirty();
    }

    public override void HandleKeyboard(KeyEvent keyEvent) => input.HandleKeyboard(keyEvent);
    public override string GetName() => "CommandPrompt";
}
