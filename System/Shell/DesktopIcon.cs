using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Text;
using Windose.Resources.Icons;
using Windose.System.Kernel;

namespace Windose.System.Shell
{
    public class DesktopIcon : Component
    {
        public const int DefaultWidth = 80;
        public const int DefaultHeight = 76;
        private const int IconSize = 32;
        private const int IconTop = 2;
        private const int LabelTop = 40;
        private const int LabelLineHeight = 12;
        private const int MaxLabelLines = 3;

        public FileEntry fileEntry;
        public Image icon;

        public Action leftMousePress;
        public Action leftMouseHold;
        public Action leftMouseRelease;

        private bool isPressed;
        private bool dragging;
        private Point offset;
        private string renameText = "";
        private string renameOriginalText = "";
        private bool renameAllSelected;

        public bool IsDragging => dragging;
        public bool IsRenaming => renaming;

        public DesktopIcon(int x, int y, FileEntry fileEntry, Image ?icon = null) : base(x, y, DefaultWidth, DefaultHeight, default, default, HorizontalAlignment.Left, VerticalAlignment.Top)
        {
            this.fileEntry = fileEntry;

            if (icon != null)
            {
                this.icon = icon;
            }
            else
            {
                //TODO file associations
                switch(fileEntry.FileType)
                {
                    case FileType.Directory:
                        this.icon = new Png("/mnt/System/Icons/directory_closed.png");
                        break;

                    case FileType.File:
                        this.icon = new Png("/mnt/System/Icons/file_lines.png");
                        break;

                    default:
                        this.icon = new Png("/mnt/System/Icons/file_question.png");
                        break;
                }
            }
        }


        public override void DrawLocal()
        {
            int iconX = Math.Max(0, (Width - IconSize) / 2);

            if (isPressed)
                DrawRectangle(Color.Blue, iconX - 2, IconTop - 2, IconSize + 4, IconSize + 4);

            DrawImageAlpha(icon, iconX, IconTop);

            DrawLabel();
        }

        public void Set(bool selected)
        {
            isPressed = selected;
            MarkDirty();
        }




        public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
        {
            if (renaming)
                return base.HandleInput(mouseX, mouseY, mouse);

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
                MoveTo(mouseX - offset.X, mouseY - offset.Y);
              

            }

            return base.HandleInput(mouseX, mouseY, mouse);
        }

        private bool renaming;

        public void BeginRename()
        {
            renameOriginalText = fileEntry.FileName ?? "";
            renameText = renameOriginalText;
            renameAllSelected = true;
            renaming = true;
            MarkDirty();
        }

        public void CommitRename()
        {
            if (!renaming) return;

            string newName = (renameText ?? "").Trim();
            if (newName != "")
            {
                string oldPath = fileEntry.AbsoluteLocation;
                string newPath = ReplacePathName(oldPath, newName);

                if (newName == renameOriginalText)
                {
                    CancelRename();
                    return;
                }

                if (PathExists(newPath))
                {
                    MarkDirty();
                    return;
                }

                if (PathExists(oldPath))
                {
                    if (fileEntry.FileType == FileType.Directory)
                        Directory.Move(oldPath, newPath);
                    else
                        File.Move(oldPath, newPath);
                }

                fileEntry.FileName = newName;
                fileEntry.AbsoluteLocation = newPath;
            }

            renameText = "";
            renameOriginalText = "";
            renameAllSelected = false;
            renaming = false;
            MarkDirty();
        }

        public void CancelRename()
        {
            if (!renaming) return;

            renameText = "";
            renameOriginalText = "";
            renameAllSelected = false;
            renaming = false;
            MarkDirty();
        }

        public override void HandleKeyboard(KeyEvent keyEvent)
        {
            if(keyEvent.Key == ConsoleKeyEx.F2)
            {
                if (renaming)
                    CommitRename();
                else
                    BeginRename();
                return;
            }

            if (renaming)
            {
                switch (keyEvent.Key)
                {
                    case ConsoleKeyEx.Backspace:
                        if (renameAllSelected)
                        {
                            renameText = "";
                            renameAllSelected = false;
                            MarkDirty();
                        }
                        else if (renameText.Length > 0)
                        {
                            renameText = renameText.Substring(0, renameText.Length - 1);
                            MarkDirty();
                        }
                        break;

                    case ConsoleKeyEx.Enter:
                        CommitRename();
                        break;

                    case ConsoleKeyEx.Escape:
                        CancelRename();
                        break;

                    default:
                        char printable = GetPrintableCharacter(keyEvent);
                        if (printable != '\0')
                        {
                            if (renameAllSelected)
                            {
                                renameText = "";
                                renameAllSelected = false;
                            }

                            renameText += printable;
                            MarkDirty();
                        }
                        break;

                }

                return;
            }

            base.HandleKeyboard(keyEvent);
        }

        public void MoveTo(int x, int y)
        {
            if (x == X && y == Y) return;

            Rectangle previousBounds = AbsoluteRectangle;

            X = x;
            Y = y;

            WindowManager.Invalidate(previousBounds);
            WindowManager.Invalidate(AbsoluteRectangle);
            MarkDirty(false);
        }

        private void DrawLabel()
        {
            string name = renaming ? renameText : fileEntry.FileName;
            if (string.IsNullOrEmpty(name) && !renaming) return;

            string[] lines = BuildLabelLines(name);
            if (renaming && lines.Length == 0)
                lines = [""];

            int labelHeight = lines.Length * LabelLineHeight;

            if (isPressed)
                DrawFilledRectangle(Color.Blue, 0, LabelTop, Width, labelHeight);

            if (renaming)
            {
                DrawFilledRectangle(Color.White, 0, LabelTop, Width, labelHeight);
                DrawRectangle(Color.Blue, 0, LabelTop, Width - 1, labelHeight);
            }

            for (int i = 0; i < lines.Length; i++)
            {
                int lineWidth = MeasureStringWidth(lines[i], SystemFonts.spleen6x12);
                int x = Math.Max(0, (Width - lineWidth) / 2);
                DrawString(lines[i], SystemFonts.spleen6x12, renaming ? Color.Black : Color.White, x, LabelTop + i * LabelLineHeight);
            }

            if (renaming)
                DrawRenameCaret(lines);
        }

        private string[] BuildLabelLines(string text)
        {
            int maxCharsPerLine = Math.Max(1, Width / SystemFonts.spleen6x12.Width);
            List<string> lines = new List<string>();
            string current = "";
            bool truncated = false;

            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (lines.Count >= MaxLabelLines)
                {
                    truncated = true;
                    break;
                }

                string word = words[i];
                if (word.Length > maxCharsPerLine)
                {
                    if (!AddLabelLine(lines, current, maxCharsPerLine))
                    {
                        truncated = true;
                        break;
                    }
                    current = "";

                    int index = 0;
                    while (index < word.Length && lines.Count < MaxLabelLines)
                    {
                        int length = Math.Min(maxCharsPerLine, word.Length - index);
                        AddLabelLine(lines, word.Substring(index, length), maxCharsPerLine);
                        index += length;
                    }
                    if (index < word.Length)
                        truncated = true;
                    continue;
                }

                string candidate = current == "" ? word : current + " " + word;
                if (candidate.Length <= maxCharsPerLine)
                {
                    current = candidate;
                }
                else
                {
                    if (!AddLabelLine(lines, current, maxCharsPerLine))
                    {
                        truncated = true;
                        break;
                    }
                    current = word;
                }
            }

            if (!truncated && !AddLabelLine(lines, current, maxCharsPerLine))
                truncated = true;

            if (truncated && lines.Count == MaxLabelLines)
                lines[MaxLabelLines - 1] = Ellipsize(lines[MaxLabelLines - 1], maxCharsPerLine);

            return lines.ToArray();
        }

        private static bool AddLabelLine(List<string> lines, string line, int maxCharsPerLine)
        {
            if (string.IsNullOrEmpty(line)) return true;
            if (lines.Count >= MaxLabelLines) return false;

            lines.Add(line.Length <= maxCharsPerLine ? line : line.Substring(0, maxCharsPerLine));
            return true;
        }

        private static string Ellipsize(string text, int maxCharsPerLine)
        {
            if (maxCharsPerLine <= 3) return text.Substring(0, Math.Min(text.Length, maxCharsPerLine));
            if (text.Length >= maxCharsPerLine) return text.Substring(0, maxCharsPerLine - 3) + "...";
            return text + "...";
        }

        private void DrawRenameCaret(string[] lines)
        {
            if (lines.Length == 0) return;

            string lastLine = lines[lines.Length - 1];
            int lineWidth = MeasureStringWidth(lastLine, SystemFonts.spleen6x12);
            int x = Math.Min(Width - 1, Math.Max(0, (Width - lineWidth) / 2 + lineWidth + 1));
            int y = LabelTop + (lines.Length - 1) * LabelLineHeight;
            DrawLine(Color.Black, x, y + 1, x, y + LabelLineHeight - 2);
        }

        private static string ReplacePathName(string path, string newName)
        {
            if (string.IsNullOrEmpty(path)) return newName;

            int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
            if (slash < 0) return newName;

            return path.Substring(0, slash + 1) + newName;
        }

        private static bool PathExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return File.Exists(path) || Directory.Exists(path);
        }

        public override string GetName() => "DesktopIcon";
    }
}
