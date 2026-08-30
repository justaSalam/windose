using System.Drawing;
using Cosmos.Kernel.System.Mouse;
using Windose;

public class Tooltip : Component
{
    private sealed class TooltipTarget
    {
        public Component Component;
        public string Text;
    }

    private readonly List<TooltipTarget> targets = new List<TooltipTarget>();
    private Component trackedComponent;
    private string tooltipText = "";
    private int displayDelay = 500; // ms before showing
    private double hoverStartTime;
    private bool isShowing;
    private int tooltipWidth = 200;
    private int tooltipHeight = 20;
    private int padding = 4;
    private int mouseOffset = 4; // gap between cursor tip and tooltip box

    public Color backColor = Color.FromArgb(255, 255, 225);
    public Color borderColor = Palette.ControlShadow;
    public Color textColor = Palette.ControlBlack;
    public int fontSize = 14;

    public string TooltipText
    {
        get => tooltipText;
        set
        {
            tooltipText = value ?? "";
            RecalculateSize();
        }
    }

    public int DisplayDelay
    {
        get => displayDelay;
        set => displayDelay = Math.Max(0, value);
    }

    public Tooltip() : base(0, 0, 200, 20)
    {
        Visible = false;
        zLayer = DrawLayer.Overlay;
    }

    public void AttachTo(Component component, string text)
    {
        if (component == null) return;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i].Component != component) continue;
            targets[i].Text = text ?? "";
            RecalculateSize();
            return;
        }

        targets.Add(new TooltipTarget { Component = component, Text = text ?? "" });
    }

    public void Detach()
    {
        targets.Clear();
        trackedComponent = null;
        isShowing = false;
        Visible = false;
        hoverStartTime = 0;
    }

    public override void Update()
    {
        base.Update();

        TooltipTarget hoveredTarget = null;
        for (int i = 0; i < targets.Count; i++)
        {
            Component component = targets[i].Component;
            if (component != null && component.Visible && component.IsInsideAbsolute(MouseManager.X, MouseManager.Y))
            {
                hoveredTarget = targets[i];
                break;
            }
        }

        if (hoveredTarget == null || string.IsNullOrEmpty(hoveredTarget.Text))
        {
            if (isShowing)
            {
                isShowing = false;
                Visible = false;
            }
            trackedComponent = null;
            hoverStartTime = 0;
            return;
        }

        if (trackedComponent != hoveredTarget.Component)
        {
            trackedComponent = hoveredTarget.Component;
            tooltipText = hoveredTarget.Text;
            RecalculateSize();
            hoverStartTime = DateTime.UtcNow.Ticks / 10000.0;
            isShowing = false;
            Visible = false;
        }

        if (!isShowing)
        {
            if (hoverStartTime == 0)
                hoverStartTime = DateTime.UtcNow.Ticks / 10000.0;

            double now = DateTime.UtcNow.Ticks / 10000.0;
            if (now - hoverStartTime >= displayDelay)
            {
                isShowing = true;
                Visible = true;

                // Position above the mouse cursor
                int tipX = MouseManager.X;
                int tipY = MouseManager.Y - tooltipHeight - mouseOffset;

                // Keep on screen
                if (tipX + tooltipWidth > Global.screenWidth)
                    tipX = Global.screenWidth - tooltipWidth - 4;
                if (tipX < 0)
                    tipX = 4;

                // If there's no room above the cursor, fall back to below it
                if (tipY < 0)
                    tipY = MouseManager.Y + mouseOffset + 16; // 16 ~= cursor height, keeps tooltip clear of the pointer

                if (tipY + tooltipHeight > Global.screenHeight)
                    tipY = Global.screenHeight - tooltipHeight - 4;

                X = tipX;
                Y = tipY;
                Resize(tooltipWidth, tooltipHeight);
                MarkDirty();
            }
        }
    }

    public override void DrawLocal()
    {
        if (!Visible) return;

        DrawFilledRectangle(backColor, 0, 0, Width, Height);
        DrawRectangle(borderColor, 0, 0, Width, Height);

        if (tooltipText != "")
        {
            int textY = Math.Max(0, (Height - MeasureStringHeight(fontSize)) / 2);
            DrawString(tooltipText, textColor, padding, textY, fontSize);
        }
    }

    private void RecalculateSize()
    {
        if (string.IsNullOrEmpty(tooltipText))
        {
            tooltipWidth = 50;
            tooltipHeight = 20;
            return;
        }

        int maxLineWidth = 0;
        int lineCount = 1;
        int currentLineWidth = 0;

        for (int i = 0; i < tooltipText.Length; i++)
        {
            if (tooltipText[i] == '\n')
            {
                if (currentLineWidth > maxLineWidth) maxLineWidth = currentLineWidth;
                currentLineWidth = 0;
                lineCount++;
            }
            else
            {
                currentLineWidth++;
            }
        }
        if (currentLineWidth > maxLineWidth) maxLineWidth = currentLineWidth;

        tooltipWidth = Math.Min(400, maxLineWidth * (fontSize / 2) + padding * 2);
        tooltipWidth = Math.Max(30, tooltipWidth);
        tooltipHeight = lineCount * (fontSize + 2) + padding;
    }

    public override string GetComponentName() => "Tooltip";
}