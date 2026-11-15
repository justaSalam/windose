using System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;

public sealed class CommandPrompt : Window
{
    private readonly TerminalView output;
    private readonly CommandContext context;

    public CommandPrompt(int x = 140, int y = 100, int width = 720, int height = 460, string? initialCommand = null, string title = "Command Prompt")
        : base(x, y, width, height, title, true, Windose.System.Resources.IconResource.Load("shell32.dll,-284", 16))
    {
        CommandRegistry.EnsureBuiltIns();

        int bx = Palette.BorderSize;
        int by = Palette.TitleBarHeight;
        int bw = Math.Max(1, Width - Palette.BorderSize * 2);
        int bh = Math.Max(1, Height - Palette.TitleBarHeight - Palette.BorderSize);

        output = new TerminalView(bx, by, bw, bh)
        {
            Margin = new Thickness(0),
        };

        context = new CommandContext(
            writeLine: output.WriteLine,
            clear: output.Clear,
            close: () => RequestClose(),
            readLine: output.ReadLineSync)
        {
            CurrentDirectory = "/mnt"
        };

        output.OnTopLevelSubmit = line =>
        {
            new Thread(() => CommandRegistry.Execute(context, line)).Start();
        };

        output.PromptProvider = () => $"windose@user: {context.CurrentDirectory} ";

        AddChild(output);
        SetFocusedComponent(output);
        output.RequestFocus();

        output.WriteLine("Windose Command Prompt");
        output.WriteLine("Type help for available commands.");
        output.WriteLine();

        if (!string.IsNullOrEmpty(initialCommand))
        {
            new Thread(() =>
            {
                Thread.Sleep(60);
                CommandRegistry.Execute(context, initialCommand);
            }).Start();
        }
    }

    protected override void OnWindowResized()
    {
        base.OnWindowResized();
        if (output != null)
        {
            int bx = Palette.BorderSize;
            int by = Palette.TitleBarHeight;
            int bw = Math.Max(1, Width - Palette.BorderSize * 2);
            int bh = Math.Max(1, Height - Palette.TitleBarHeight - Palette.BorderSize);
            output.SetBounds(bx, by, bw, bh);
        }
    }

    public override void HandleKeyboard(KeyEvent keyEvent) => output.HandleKeyboard(keyEvent);
    public override string GetComponentName() => "CommandPrompt";
}
