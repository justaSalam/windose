// ElfGui.cs - Windose UI objects owned by ELF programs: event-reporting windows and pixel canvases.
using Cosmos.Kernel.System.Input;
using System;
using System.Drawing;

namespace Windose.System.Elf
{
    /// <summary>
    /// Top-level window for an ELF program. Behaves like a normal Window (drag, resize, title buttons)
    /// but also reports keyboard and mouse activity to the owning program as EV_KEY / EV_MOUSE*.
    /// Coordinates in mouse events are relative to the innermost program-owned component under the cursor
    /// (client coordinates for the window itself).
    /// </summary>
    public sealed class ElfWindow : Window
    {
        public ElfProgram Owner;
        public int ProgramHandle;

        private int lastMouseX = int.MinValue;
        private int lastMouseY = int.MinValue;

        public ElfWindow(int x, int y, int width, int height, string title)
            : base(x, y, width, height, title, true)
        {
        }

        public override void HandleKeyboard(KeyEvent keyEvent)
        {
            ElfProgram owner = Owner;
            if (owner != null && owner.State != ElfState.Exited)
            {
                // Cook control keys into chars so programs never need raw key codes:
                // printable chars pass through, the rest arrive as \b \r \t Esc(27) Del(127).
                char ch = keyEvent.KeyChar;
                switch (keyEvent.Key)
                {
                    case Key.Backspace: ch = '\b'; break;
                    case Key.Enter: ch = '\r'; break;
                    case Key.Tab: ch = '\t'; break;
                    case Key.Escape: ch = (char)27; break;
                    case Key.Delete: ch = (char)127; break;
                }
                owner.PostEvent(ElfEvent.Key, ProgramHandle, (int)keyEvent.Key,
                    ((int)keyEvent.Modifiers << 16) | (ch & 0xFFFF));
            }
            // Swallowed: ELF children (button/label/canvas) take no keyboard input.
        }

        public override bool HandleInput(int mouseX, int mouseY, MouseState mouseState)
        {
            bool handled = base.HandleInput(mouseX, mouseY, mouseState);
            ReportMouse(mouseX, mouseY, mouseState);
            return handled;
        }

        private void ReportMouse(int mx, int my, MouseState mouse)
        {
            ElfProgram owner = Owner;
            if (owner == null || owner.State == ElfState.Exited) return;

            object target = this;
            Component c = GetChildAt(mx, my);
            while (c != null && WindoseBridge.TryGetHandle(c, owner, out int _))
            {
                target = c;
                c = c.GetChildAt(mx, my);
            }
            if (!WindoseBridge.TryGetHandle(target, owner, out int handle)) return;

            int lx, ly;
            if (target is Window win)
            {
                lx = mx - win.AbsoluteX - Palette.BorderSize;
                ly = my - win.AbsoluteY - Palette.TitleBarHeight;
            }
            else
            {
                Component tc = (Component)target;
                lx = mx - tc.AbsoluteX;
                ly = my - tc.AbsoluteY;
            }
            if (lx < 0) lx = 0;
            if (ly < 0) ly = 0;
            if (lx > 0xFFFFFFF) lx = 0xFFFFFFF;
            if (ly > 0xFFFFFFF) ly = 0xFFFFFFF;

            if (mouse.left == MouseEvents.Press) owner.PostEvent(ElfEvent.MouseDown, handle, lx | (1 << 28), ly);
            if (mouse.right == MouseEvents.Press) owner.PostEvent(ElfEvent.MouseDown, handle, lx | (2 << 28), ly);
            if (mouse.middle == MouseEvents.Press) owner.PostEvent(ElfEvent.MouseDown, handle, lx | (3 << 28), ly);
            if (mouse.left == MouseEvents.Release) owner.PostEvent(ElfEvent.MouseUp, handle, lx | (1 << 28), ly);
            if (mouse.right == MouseEvents.Release) owner.PostEvent(ElfEvent.MouseUp, handle, lx | (2 << 28), ly);
            if (mouse.middle == MouseEvents.Release) owner.PostEvent(ElfEvent.MouseUp, handle, lx | (3 << 28), ly);

            if ((mx != lastMouseX || my != lastMouseY) && HitTest(mx, my))
            {
                lastMouseX = mx;
                lastMouseY = my;
                owner.PostEvent(ElfEvent.MouseMove, handle, lx, ly);
            }
        }
    }

    /// <summary>
    /// Pixel buffer a program paints with draw_pixels. Redrawn by the compositor like any component;
    /// per-frame animation just calls draw_pixels again (no Panel-per-rect accumulation).
    /// </summary>
    public sealed class ElfCanvasComponent : Component
    {
        private readonly int[] pixels;

        public ElfCanvasComponent(int x, int y, int width, int height)
            : base(x, y, width, height)
        {
            pixels = new int[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = unchecked((int)0xFF000000);
        }

        public override void DrawLocal()
        {
            buffer.DrawArrayAlphaClipped(pixels, Width, 0, 0, 0, 0, Width, Height);
        }

        /// <summary>Copies w*h ARGB pixels from an unmanaged buffer. False if the region is invalid.</summary>
        public unsafe bool SetPixels(int x, int y, int w, int h, IntPtr src, int len)
        {
            if (x < 0 || y < 0 || w <= 0 || h <= 0) return false;
            if ((long)x + w > Width || (long)y + h > Height) return false;
            if (len != w * h * 4) return false;
            if (src == IntPtr.Zero) return false;
            try
            {
                int* s = (int*)src;
                for (int row = 0; row < h; row++)
                {
                    int dst = (y + row) * Width + x;
                    int off = row * w;
                    for (int i = 0; i < w; i++)
                        pixels[dst + i] = s[off + i];
                }
            }
            catch { return false; }
            MarkDirty();
            return true;
        }

        public override string GetComponentName() => "ElfCanvas";
    }

    /// <summary>
    /// Scrollable single-column list for ELF programs: a ScrollView + ListView pair that behaves
    /// as one object (one handle, removed as one). Content height tracks the item count.
    /// </summary>
    public sealed class ElfListBox : Component
    {
        public readonly ScrollView Scroll;
        public readonly ListView List;

        public ElfListBox(int x, int y, int width, int height)
            : base(x, y, width, height)
        {
            Scroll = new ScrollView(0, 0, width, height)
            {
                clampSize = false,
                showHorizontalScrollbar = false,
                Margin = new Thickness(0),
            };
            List = new ListView(0, 0, width, height)
            {
                clampSize = false,
                Margin = new Thickness(0),
            };
            List.AddColumn("", Math.Max(1, width - 4));
            Scroll.SetContent(List, width, List.GetContentHeight());
            AddChild(Scroll);
        }

        public void Refresh()
        {
            Scroll.RefreshContent(false);
            MarkDirty();
        }

        public override void DrawLocal()
        {
            DrawFilledRectangle(Palette.ControlWhite, 0, 0, Width, Height);
            foreach (Component child in children)
            {
                if (!child.Visible) continue;
                DrawChild(child);
            }
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            Scroll.Resize(width, height);
            Scroll.RefreshContent(false);
        }

        public override string GetComponentName() => "ElfListBox";
    }

    /// <summary>
    /// Scroll-less tree for ELF programs. Nodes are individual handles (EV_CLICK on select,
    /// EV_DBLCLICK on activate); selection/expansion behave like the C# TreeView.
    /// </summary>
    public sealed class ElfTree : Component
    {
        public readonly TreeView Tree;
        public ElfProgram Owner;

        public ElfTree(int x, int y, int width, int height)
            : base(x, y, width, height)
        {
            Tree = new TreeView(0, 0, width, height)
            {
                clampSize = false,
                Margin = new Thickness(0),
            };
            AddChild(Tree);
        }

        public override void Resize(int width, int height)
        {
            base.Resize(width, height);
            Tree.Resize(width, height);
        }

        public override void DrawLocal()
        {
            DrawFilledRectangle(Palette.ControlWhite, 0, 0, Width, Height);
            foreach (Component child in children)
            {
                if (!child.Visible) continue;
                DrawChild(child);
            }
        }

        public override string GetComponentName() => "ElfTree";
    }
}
