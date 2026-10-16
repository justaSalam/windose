using Cosmos.Kernel.System.Input;
using System.Drawing;
using Windose.System.Kernel.Subsystem;

/// <summary>
/// Fullscreen lock/logon overlay. Logged-out mode asks for username + password,
/// locked-session mode only for the current user's password.
/// </summary>
public class LockOverlay : Component
{
    private readonly Label titleLabel;
    private readonly Label userLabel;
    private readonly TextField userField;
    private readonly TextField passwordField;
    private readonly Button unlockButton;
    private readonly Label statusLabel;
    private bool loggedOut;

    public LockOverlay() : base(0, 0, Global.screenWidth, Global.screenHeight)
    {
        zLayer = DrawLayer.Overlay;
        Visible = false;
        isRoot = true;

        int cx = Width / 2;
        titleLabel = new Label(cx - 150, Height / 2 - 110, 300, 28)
        {
            text = "Windose",
            fontSize = 24,
            textColor = Color.White,
            useBackground = false,
            horizontalTextAlignment = HorizontalAlignment.Center,
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
        };
        userLabel = new Label(cx - 150, Height / 2 - 70, 300, 22)
        {
            fontSize = 14,
            textColor = Color.White,
            useBackground = false,
            horizontalTextAlignment = HorizontalAlignment.Center,
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
        };
        userField = new TextField(cx - 110, Height / 2 - 40, 220, 26)
        {
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
            PlaceholderText = "Username",
        };
        passwordField = new TextField(cx - 110, Height / 2 - 40, 220, 26)
        {
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
            PlaceholderText = "Password",
        };
        passwordField.PasswordChar = '*';
        unlockButton = new Button("Unlock", cx - 110, Height / 2 - 8, 220, 28)
        {
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
        };
        statusLabel = new Label(cx - 150, Height / 2 + 28, 300, 22)
        {
            fontSize = 14,
            textColor = Color.Yellow,
            useBackground = false,
            horizontalTextAlignment = HorizontalAlignment.Center,
            clampSize = false,
            Margin = new Thickness(0),
            horizontalAlignment = HorizontalAlignment.Left,
            verticalAlignment = VerticalAlignment.Top,
        };
        unlockButton.leftClickAction = TryUnlock;

        AddChild(titleLabel);
        AddChild(userLabel);
        AddChild(userField);
        AddChild(passwordField);
        AddChild(unlockButton);
        AddChild(statusLabel);
    }

    public void Show(bool loggedOut)
    {
        this.loggedOut = loggedOut;
        userField.text = "";
        userField.Visible = loggedOut;
        passwordField.text = "";
        statusLabel.text = "";
        if (loggedOut)
        {
            userLabel.text = "Log on to Windose";
            passwordField.Y = Height / 2 - 8;
            unlockButton.Y = Height / 2 + 24;
            statusLabel.Y = Height / 2 + 60;
        }
        else
        {
            userLabel.text = Session.CurrentUser != null ? Session.CurrentUser.username : "Locked";
        }
        Resize(Global.screenWidth, Global.screenHeight);
        Visible = true;
        MarkDirty();
        WindowManager.Invalidate(new Rectangle(0, 0, Width, Height));
    }

    public void HideOverlay()
    {
        Visible = false;
        WindowManager.Invalidate(new Rectangle(0, 0, Width, Height));
    }

    private void TryUnlock()
    {
        if (loggedOut)
        {
            if (Session.LogIn(userField.text.Trim(), passwordField.text))
            {
                LockManager.Unlock();
            }
            else
            {
                statusLabel.text = "Wrong username or password";
                statusLabel.MarkDirty();
            }
            return;
        }

        if (Session.CurrentUser == null)
        {
            LockManager.Unlock();
            return;
        }

        if (Session.CurrentUser.VerifyPassword(passwordField.text))
        {
            LockManager.Unlock();
        }
        else
        {
            statusLabel.text = "Wrong password";
            statusLabel.MarkDirty();
        }
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(Color.FromArgb(255, 20, 30, 60), 0, 0, Width, Height);
        foreach (Component child in children)
        {
            if (!child.Visible) continue;
            DrawChild(child);
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!Visible) return false;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            Component child = children[i];
            if (child == null || !child.Visible) continue;
            if (!child.IsInsideAbsolute(mouseX, mouseY)) continue;
            if (child.HandleInput(mouseX, mouseY, mouse)) return true;
        }
        if (mouse.left == MouseEvents.Press && userField.Visible)
        {
            userField.RequestFocus();
            return true;
        }
        if (mouse.left == MouseEvents.Press)
        {
            passwordField.RequestFocus();
            return true;
        }
        return true;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        if (keyEvent.Key == Key.Enter) { TryUnlock(); return; }
        if (keyEvent.Key == Key.Escape) return;
        if (keyEvent.Key == Key.Tab)
        {
            if (Component.FocusedComponent == userField || !userField.Visible)
                passwordField.RequestFocus();
            else
                userField.RequestFocus();
            return;
        }
        Component target = null;
        if (userField.Visible && Component.FocusedComponent == userField) target = userField;
        else if (Component.FocusedComponent == passwordField) target = passwordField;
        else target = userField.Visible ? (Component)userField : passwordField;
        target.HandleKeyboard(keyEvent);
    }

    public override string GetComponentName() => "LockOverlay";
}
