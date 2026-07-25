using Cosmos.Kernel.System.Graphics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;

namespace Windose.System.Shell
{
    public class DesktopIcon : Component
    {
        public FileEntry fileEntry;
        public Image icon;

        public Action leftMousePress;
        public Action leftMouseHold;
        public Action leftMouseRelease;

        private bool isPressed;
        private bool dragging;
        private Point offset;

        public DesktopIcon(int x, int y, FileEntry fileEntry, Image icon = null) : base(x, y, 64, 64, default, default, HorizontalAlignment.Left, VerticalAlignment.Top)
        {
            this.fileEntry = fileEntry;
            this.icon = icon;
        }


        public override void DrawLocal()
        {

            DrawFilledRectangle(Color.Gray, 8, 0, 48, 48);


            if (fileEntry.FileName != "")
            {
                if (isPressed) DrawFilledRectangle(Color.Blue, 8, 48, MeasureStringWidth(fileEntry.FileName, SystemFonts.spleen6x12), SystemFonts.spleen6x12.Height);
                DrawString(fileEntry.FileName, SystemFonts.spleen6x12, Color.White, 8, 48);
            }
        }

        public void Set(bool selected)
        {
            isPressed = selected;
            MarkDirty();
        }
        



        public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
        {
            if (mouse.left == MouseEvents.Press)
            {
                dragging = true;
                offset = new Point(mouseX - X, mouseY - Y);

            }
            else if (mouse.left == MouseEvents.Release && dragging) //On mouse release
            {
                dragging = false;
            }
            else if (mouse.left == MouseEvents.None && dragging)//If the mouse is released outside of the window, cancel the drag operation
            {
                dragging = false;

            }

            if (dragging && (mouse.left == MouseEvents.Hold || mouse.left == MouseEvents.Press))//Dragging
            {
                X = mouseX - offset.X;
                Y = mouseY - offset.Y;
                MarkDirty();
                

            }

            return base.HandleInput(mouseX, mouseY, mouse);
        }

        public override string GetName() => "Button";
    }
}
