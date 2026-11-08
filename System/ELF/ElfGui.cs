// ElfGui.cs - Windose UI objects owned by ELF programs: event-reporting windows and pixel canvases.
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Graphics;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Windose.System.Elf
{
    public sealed class ChildLayoutInfo
    {
        public int OrigClientX;
        public int OrigClientY;
        public int OrigWidth;
        public int OrigHeight;
        public int AnchorFlags = 1 | 2; // ANCHOR_LEFT (1) | ANCHOR_TOP (2)
    }

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

        public int InitialClientWidth { get; private set; }
        public int InitialClientHeight { get; private set; }

        public int ClientOriginX => (HasTitleBar && drawFrame) ? Palette.BorderSize : (drawFrame ? Palette.BorderSize : 0);
        public int ClientOriginY => HasTitleBar ? Palette.TitleBarHeight : 0;
        public int GetClientWidth() => Math.Max(1, Width - (drawFrame ? Palette.BorderSize * 2 : 0));
        public int GetClientHeight() => Math.Max(1, Height - (HasTitleBar ? Palette.TitleBarHeight : 0) - (drawFrame ? Palette.BorderSize : 0));

        private readonly Dictionary<Component, ChildLayoutInfo> childLayouts = new Dictionary<Component, ChildLayoutInfo>();

        private int lastMouseX = int.MinValue;
        private int lastMouseY = int.MinValue;

        public ElfWindow(int x, int y, int width, int height, string title, Image? icon = null, bool useTitleBar = true, bool showInTaskbar = true)
            : base(x, y, width, height, title, useTitleBar, icon)
        {
            this.showInTaskbar = showInTaskbar;
            this.managesChildLayout = true;
            this.InitialClientWidth = GetClientWidth();
            this.InitialClientHeight = GetClientHeight();
        }

        public void RecordChildInitialLayout(Component child)
        {
            if (child == null || child == titlebar) return;
            lock (childLayouts)
            {
                if (!childLayouts.ContainsKey(child))
                {
                    if (InitialClientWidth <= 0) InitialClientWidth = GetClientWidth();
                    if (InitialClientHeight <= 0) InitialClientHeight = GetClientHeight();

                    int cx = child.X - ClientOriginX;
                    int cy = child.Y - ClientOriginY;
                    childLayouts[child] = new ChildLayoutInfo
                    {
                        OrigClientX = cx,
                        OrigClientY = cy,
                        OrigWidth = child.Width,
                        OrigHeight = child.Height
                    };
                }
            }
        }

        public void UpdateChildLayout(Component child, int clientX, int clientY, int w, int h)
        {
            if (child == null || child == titlebar) return;
            lock (childLayouts)
            {
                if (childLayouts.TryGetValue(child, out var info))
                {
                    info.OrigClientX = clientX;
                    info.OrigClientY = clientY;
                    info.OrigWidth = w;
                    info.OrigHeight = h;
                }
                else
                {
                    childLayouts[child] = new ChildLayoutInfo
                    {
                        OrigClientX = clientX,
                        OrigClientY = clientY,
                        OrigWidth = w,
                        OrigHeight = h
                    };
                }
            }
        }

        public void SetChildAnchors(Component child, int anchorFlags)
        {
            if (child == null) return;
            RecordChildInitialLayout(child);
            lock (childLayouts)
            {
                if (childLayouts.TryGetValue(child, out var info))
                {
                    info.AnchorFlags = anchorFlags;
                }
            }
        }

        public void SetChildBounds(Component child, int x, int y, int w, int h)
        {
            if (child == null) return;
            child.SetBounds(ClientOriginX + x, ClientOriginY + y, w, h);
            UpdateChildLayout(child, x, y, w, h);
            MarkDirty();
        }

        public override Component AddChild(Component child)
        {
            base.AddChild(child);
            if (child != null && child != titlebar)
            {
                RecordChildInitialLayout(child);
            }
            return child;
        }

        public override void RemoveChild(Component child)
        {
            if (child != null)
            {
                lock (childLayouts)
                {
                    childLayouts.Remove(child);
                }
            }
            base.RemoveChild(child);
        }

        protected override void OnWindowResized()
        {
            base.OnWindowResized();
            RecalculateLayout();
            if (Owner != null && Owner.State != ElfState.Exited)
            {
                try
                {
                    Owner.PostEvent(ElfEvent.Resize, ProgramHandle, GetClientWidth(), GetClientHeight());
                }
                catch { }
            }
        }

        public void RecalculateLayout()
        {
            int curW = GetClientWidth();
            int curH = GetClientHeight();
            int initW = InitialClientWidth > 0 ? InitialClientWidth : curW;
            int initH = InitialClientHeight > 0 ? InitialClientHeight : curH;

            if (curW <= 0 || curH <= 0 || initW <= 0 || initH <= 0) return;

            List<KeyValuePair<Component, ChildLayoutInfo>> items;
            lock (childLayouts)
            {
                if (childLayouts.Count == 0) return;
                items = new List<KeyValuePair<Component, ChildLayoutInfo>>(childLayouts);
            }

            int deltaW = curW - initW;
            int deltaH = curH - initH;

            for (int i = 0; i < items.Count; i++)
            {
                Component child = items[i].Key;
                ChildLayoutInfo info = items[i].Value;
                if (child == null || child.Parent != this || child == titlebar) continue;

                int flags = info.AnchorFlags;
                bool anchorLeft = (flags & 1) != 0;
                bool anchorTop = (flags & 2) != 0;
                bool anchorRight = (flags & 4) != 0;
                bool anchorBottom = (flags & 8) != 0;

                // Standard top/bottom bar behavior for MenuBar, Toolbar, and StatusBar if left at defaults
                if (flags == (1 | 2))
                {
                    if (child is MenuBar || child is Toolbar)
                    {
                        anchorLeft = true;
                        anchorTop = true;
                        anchorRight = true;
                        anchorBottom = false;
                    }
                    else if (child is StatusBar)
                    {
                        anchorLeft = true;
                        anchorTop = false;
                        anchorRight = true;
                        anchorBottom = true;
                    }
                }

                int newX = info.OrigClientX;
                int newY = info.OrigClientY;
                int newW = info.OrigWidth;
                int newH = info.OrigHeight;

                // Horizontal
                if (anchorLeft && anchorRight)
                {
                    newW = Math.Max(10, info.OrigWidth + deltaW);
                }
                else if (anchorRight)
                {
                    newX = info.OrigClientX + deltaW;
                }

                // Vertical
                if (anchorTop && anchorBottom)
                {
                    newH = Math.Max(10, info.OrigHeight + deltaH);
                }
                else if (anchorBottom)
                {
                    newY = info.OrigClientY + deltaH;
                }

                child.SetBounds(ClientOriginX + newX, ClientOriginY + newY, newW, newH);
            }

            MarkDirty();
        }

        private static TerminalView? FindTerminal(Component parent)
        {
            if (parent is TerminalView tv && tv.Visible) return tv;
            for (int i = 0; i < parent.children.Count; i++)
            {
                TerminalView? found = FindTerminal(parent.children[i]);
                if (found != null) return found;
            }
            return null;
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
                    case Key.UpArrow: ch = (char)17; break;
                    case Key.DownArrow: ch = (char)18; break;
                    case Key.LeftArrow: ch = (char)19; break;
                    case Key.RightArrow: ch = (char)20; break;
                    case Key.F5: ch = (char)132; break;
                }
                owner.PostEvent(ElfEvent.Key, ProgramHandle, (int)keyEvent.Key,
                    ((int)keyEvent.Modifiers << 16) | (ch & 0xFFFF));
            }
            // Native typing for the focused text field (if any); the program still
            // sees every key as EV_KEY and can read the result with get_text.
            try
            {
                if (focusedComponent is TextField field && field.Visible && field.IsInputEnabled())
                    field.HandleKeyboard(keyEvent);
                else if (focusedComponent is TerminalView term && term.Visible && term.IsInputEnabled())
                    term.HandleKeyboard(keyEvent);
                else
                {
                    TerminalView? tv = FindTerminal(this);
                    if (tv != null && tv.Visible)
                    {
                        tv.HandleKeyboard(keyEvent);
                    }
                }
            }
            catch { }
            // Swallowed otherwise: ELF children (button/label/canvas) take no keyboard input.
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
                lx = mx - win.AbsoluteX - (win.HasTitleBar ? Palette.BorderSize : 0);
                ly = my - win.AbsoluteY - (win.HasTitleBar ? Palette.TitleBarHeight : 0);
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
        private int[] pixels;

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

        public override void Resize(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            int oldW = Width;
            int oldH = Height;
            base.Resize(width, height);
            if (width != oldW || height != oldH)
            {
                int[] newPixels = new int[width * height];
                for (int i = 0; i < newPixels.Length; i++) newPixels[i] = unchecked((int)0xFF000000);
                if (pixels != null && oldW > 0 && oldH > 0)
                {
                    int copyW = Math.Min(oldW, width);
                    int copyH = Math.Min(oldH, height);
                    for (int r = 0; r < copyH; r++)
                    {
                        Array.Copy(pixels, r * oldW, newPixels, r * width, copyW);
                    }
                }
                pixels = newPixels;
            }
            MarkDirty();
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
            int deltaW = width - Width;
            base.Resize(width, height);
            Scroll.Resize(width, height);
            if (List.columns != null)
            {
                if (List.columns.Count == 1)
                {
                    List.columns[0].Width = Math.Max(1, width - 4);
                }
                else if (List.columns.Count > 1 && deltaW != 0)
                {
                    List.columns[0].Width = Math.Max(50, List.columns[0].Width + deltaW);
                }
            }
            Scroll.SetContent(List, width, List.GetContentHeight());
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
