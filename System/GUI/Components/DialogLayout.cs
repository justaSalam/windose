using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace Windose.System.GUI.Components
{
    /// <summary>
    /// A modular layout container for dialogs and form-based windows.
    /// Handles header prompts, structured form rows, custom controls, status labels,
    /// and bottom button strips with automatic spacing, alignment, and responsiveness.
    /// </summary>
    public class DialogLayout : DockPanel
    {
        private readonly Panel headerPanel;
        private readonly StackPanel bodyStack;
        private readonly DockPanel footerPanel;
        private readonly StackPanel buttonStrip;
        private readonly Separator? footerSeparator;

        public DialogLayout(Window parentWindow, bool showFooterSeparator = true)
            : base(0, 0, parentWindow.ClientWidth, parentWindow.ClientHeight)
        {
            horizontalAlignment = HorizontalAlignment.Stretch;
            verticalAlignment = VerticalAlignment.Stretch;
            Margin = new Thickness(0);
            Padding = new Thickness(12, 12, 14, 14); // 12px top/bottom, 14px left/right
            useBackground = true;
            backgroundColor = Palette.ControlFace;

            // Header (optional prompt & icon)
            headerPanel = new Panel(Palette.ControlFace, 0, 0, Width, 36)
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                useBackground = false,
                Margin = new Thickness(0, 8, 0, 0),
                Visible = false
            };
            AddDockChild(headerPanel, Dock.Top);

            // Footer (bottom button strip)
            footerPanel = new DockPanel(0, 0, Width, 34)
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                useBackground = false,
                Margin = new Thickness(8, 0, 0, 0)
            };

            if (showFooterSeparator)
            {
                footerSeparator = new Separator(0, 0, Width, 2)
                {
                    horizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                footerPanel.AddDockChild(footerSeparator, Dock.Top);
            }

            buttonStrip = new StackPanel(Palette.ControlFace, 0, 0, Width, 28)
            {
                horizontalAlignment = HorizontalAlignment.Right,
                orientation = StackOrientation.Horizontal,
                spacing = 8,
                useBackground = false,
                Margin = new Thickness(0)
            };
            footerPanel.AddDockChild(buttonStrip, Dock.Right);
            AddDockChild(footerPanel, Dock.Bottom);

            // Body (vertically stacked form fields and rows)
            bodyStack = new StackPanel(Palette.ControlFace, 0, 0, Width, Height)
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                verticalAlignment = VerticalAlignment.Stretch,
                orientation = StackOrientation.Vertical,
                spacing = 8,
                useBackground = false,
                Margin = new Thickness(0)
            };
            AddDockChild(bodyStack, Dock.Fill);

            // Attach to window's Content
            parentWindow.Content = this;
        }

        public void SetHeader(string prompt, Image? icon = null, int fontSize = 12)
        {
            headerPanel.children.Clear();
            headerPanel.Visible = true;

            int textLeft = 0;
            if (icon != null)
            {
                ImageView iv = new ImageView(icon, 0, 0, 32, 32)
                {
                    Margin = new Thickness(0, 0, 10, 0)
                };
                headerPanel.AddChild(iv);
                textLeft = 40;
            }

            string[] lines = prompt.Split('\n');
            int labelH = Math.Max(32, lines.Length * (fontSize + 5) + 4);
            Label lbl = new Label(textLeft, 0, Math.Max(100, headerPanel.Width - textLeft), labelH)
            {
                text = prompt,
                fontSize = fontSize,
                textColor = Palette.ControlBlack,
                useBackground = false,
                horizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0)
            };
            headerPanel.AddChild(lbl);
            headerPanel.Height = Math.Max(34, labelH);
            ResolveDockLayout();
        }

        public void AddRow(string labelText, Component field, Component? actionButton = null)
        {
            Panel row = new Panel(Palette.ControlFace, 0, 0, bodyStack.Width, 48)
            {
                horizontalAlignment = HorizontalAlignment.Stretch,
                useBackground = false,
                Margin = new Thickness(0)
            };

            Label lbl = new Label(0, 0, row.Width, 18)
            {
                text = labelText,
                fontSize = 12,
                textColor = Palette.ControlBlack,
                useBackground = false,
                horizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0)
            };
            row.AddChild(lbl);

            int btnWidth = (actionButton != null) ? (actionButton.Width > 0 ? actionButton.Width : 80) : 0;
            int gap = (actionButton != null) ? 6 : 0;
            int fieldWidth = Math.Max(50, row.Width - btnWidth - gap);

            field.X = 0;
            field.Y = 20;
            field.Width = fieldWidth;
            if (field.Height < 24) field.Height = 24;
            field.horizontalAlignment = actionButton != null ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            row.AddChild(field);

            if (actionButton != null)
            {
                actionButton.X = fieldWidth + gap;
                actionButton.Y = 20;
                actionButton.Height = field.Height;
                actionButton.horizontalAlignment = HorizontalAlignment.Right;
                row.AddChild(actionButton);
            }

            row.Height = 20 + field.Height;
            bodyStack.AddStackChild(row);
        }

        public void AddControl(Component control, int height = -1)
        {
            if (height > 0) control.Height = height;
            control.horizontalAlignment = HorizontalAlignment.Stretch;
            bodyStack.AddStackChild(control);
        }

        public void AddStatus(Label statusLabel)
        {
            statusLabel.horizontalAlignment = HorizontalAlignment.Stretch;
            statusLabel.useBackground = false;
            statusLabel.textColor = Color.FromArgb(200, 30, 30);
            statusLabel.fontSize = 11;
            statusLabel.Height = 18;
            bodyStack.AddStackChild(statusLabel);
        }

        public void SetButtons(params Button[] buttons)
        {
            buttonStrip.children.Clear();
            if (buttons == null || buttons.Length == 0) return;

            for (int i = 0; i < buttons.Length; i++)
            {
                Button btn = buttons[i];
                if (btn.Width <= 0 || btn.Width < 76) btn.Width = 78;
                if (btn.Height <= 0 || btn.Height < 24) btn.Height = 26;
                btn.useBorders = true;
                btn.Margin = new Thickness(0);
                btn.horizontalAlignment = HorizontalAlignment.Right;
                buttonStrip.AddStackChild(btn);
            }
            buttonStrip.ResolveStackLayout();
            footerPanel.ResolveDockLayout();
        }
    }
}
