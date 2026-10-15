using Cosmos.Kernel.System.Input;
using System.Drawing;

/// <summary>
/// Simple modal-style dialog: message text plus a row of buttons.
/// Non-blocking: the result arrives via onResult (button index) when a button is pressed.
/// Closing the window reports -1.
/// </summary>
public class MessageBox : Window
{
    public int Result { get; private set; } = -1;

    private readonly Action<int> onResult;
    private bool resolved;

    private MessageBox(int x, int y, int width, int height, string title, Action<int> onResult)
        : base(x, y, width, height, title, true)
    {
        this.onResult = onResult;
        canResize = false;
        canMaximize = false;
    }

    public static MessageBox Show(string text, string title, Action<int> onResult = null, params string[] buttons)
    {
        if (buttons == null || buttons.Length == 0)
            buttons = new string[] { "OK" };

        const int width = 420;
        int height = 150;
        int x = (Global.screenWidth - width) / 2;
        int y = (Global.screenHeight - height) / 2;

        MessageBox box = new MessageBox(x, y, width, height, title ?? "", onResult);

        Label message = new Label(12, 12, width - 24, 70)
        {
            text = text ?? "",
            useBackground = false,
            clampSize = false,
        };
        box.AddChild(message);

        int count = Math.Min(buttons.Length, 4);
        int btnW = 90;
        int gap = 8;
        int totalW = count * btnW + (count - 1) * gap;
        int bx = (width - totalW) / 2;
        int by = height - 44;
        for (int i = 0; i < count; i++)
        {
            int index = i;
            Button b = new Button(buttons[i] ?? "", bx + i * (btnW + gap), by, btnW, 28);
            b.leftClickAction = () => box.Resolve(index);
            box.AddChild(b);
        }

        box.onClosed = () => box.Resolve(-1);
        WindowManager.Register(box);
        return box;
    }

    private void Resolve(int index)
    {
        if (resolved) return;
        resolved = true;
        Result = index;
        try { onResult?.Invoke(index); } catch { }
        WindowManager.Close(this);
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.Enter) { Resolve(0); return; }
        if (keyEvent.Key == Key.Escape) { Resolve(-1); return; }
        base.HandleKeyboard(keyEvent);
    }

    public override string GetComponentName() => "MessageBox";
}
