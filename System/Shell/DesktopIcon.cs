using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Input;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Windose.System.Kernel.Subsystem;

namespace Windose.System.Shell
{
    public class DesktopIcon : Component
    {
        public const int DefaultWidth = 96;
        public const int DefaultHeight = 84;
        public static int CurrentIconSize = 32;
        private int _iconSize = 32;
        public int IconSize
        {
            get => _iconSize;
            set
            {
                _iconSize = value;
                Height = Math.Max(DefaultHeight, LabelTop + MaxLabelLines * LabelLineHeight + 4);
            }
        }
        private const int IconTop = 2;
        public int LabelTop => IconTop + IconSize + 4;
        private const int FontSize = 12;
        private const int LabelLineHeight = 14;
        private const int MaxLabelLines = 3;

        public FileEntry fileEntry;
        public Image icon;
        public bool isShortcut;

        /// <summary>System icons (Recycle Bin) can't be renamed, deleted or saved to layout.</summary>
        public bool isSystemIcon;

        public Action leftMousePress;
        public Action leftMouseHold;
        public Action leftMouseRelease;

        private bool isPressed;
        private bool dragging;
        private Point offset;
        private string renameText = "";
        private string renameOriginalText = "";
        private bool renameAllSelected;
        private int renameCaretIndex;

        public bool IsDragging => dragging;
        public bool IsRenaming => renaming;

        public DesktopIcon(int x, int y, FileEntry fileEntry, Image? icon = null)
            : base(x, y, DefaultWidth, DefaultHeight, default, default, HorizontalAlignment.Left, VerticalAlignment.Top)
        {
            this.fileEntry = fileEntry;
            IconSize = CurrentIconSize;

            string ext = Path.GetExtension(fileEntry.FileName ?? "");
            if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                isShortcut = true;
                if (icon != null)
                {
                    this.icon = icon;
                }
                else if (ShortcutFile.TryRead(fileEntry.AbsoluteLocation, out ShortcutInfo sInfo))
                {
                    if (!string.IsNullOrEmpty(sInfo.Icon))
                    {
                        try { this.icon = Windose.System.Resources.IconResource.Load(sInfo.Icon, 48); } catch { }
                    }
                    if (this.icon == null && !string.IsNullOrEmpty(sInfo.Target))
                    {
                        this.icon = FileAssociations.GetIcon(sInfo.Target);
                    }
                }
            }

            if (this.icon == null)
            {
                if (icon != null)
                {
                    this.icon = icon;
                }
                else
                {
                    switch (fileEntry.FileType)
                    {
                        case FileType.Directory:
                            this.icon = Windose.System.Resources.IconResource.Load("imageres.dll,-3", 48);
                            break;

                        case FileType.File:
                            this.icon = FileAssociations.GetIcon(fileEntry.FileName ?? "");
                            break;

                        default:
                            this.icon = Windose.System.Resources.IconResource.Load("imageres.dll,-2", 48);
                            break;
                    }
                }
            }
        }

        public override void DrawLocal()
        {
            int iconX = Math.Max(0, (Width - IconSize) / 2);
            int r = 4;
            bool hovered = state == State.Highlighted;

            if (isPressed)
            {
                // Windows 7 Aero selected icon card: soft translucent cyan-blue rounded card
                DrawOverGradientVerticalRounded(Color.FromArgb(70, 180, 220, 255), Color.FromArgb(110, 100, 165, 240), 0, 0, Width, Height, r);
                DrawRoundedRectangle(Color.FromArgb(180, 70, 145, 230), 0, 0, Width, Height, r);
            }
            else if (hovered)
            {
                // Windows 7 Aero hover icon card: ethereal translucent glow
                DrawOverGradientVerticalRounded(Color.FromArgb(40, 220, 240, 255), Color.FromArgb(70, 140, 200, 250), 0, 0, Width, Height, r);
                DrawRoundedRectangle(Color.FromArgb(130, 120, 180, 240), 0, 0, Width, Height, r);
            }

            if (icon != null)
            {
                if (icon.Width == IconSize && icon.Height == IconSize)
                    DrawImage(icon, iconX, IconTop);
                else
                    DrawImageStretch(icon, new Rectangle(iconX, IconTop, IconSize, IconSize));
            }

            if (isShortcut)
            {
                DrawShortcutBadge(iconX, IconTop + IconSize - 11);
            }

            DrawLabel();
        }

        private void DrawShortcutBadge(int x, int y)
        {
            DrawFilledRectangle(Color.White, x, y, 11, 11);
            DrawRectangle(Color.FromArgb(128, 128, 128), x, y, 11, 11);

            Color arrowColor = Palette.Highlight;
            DrawLine(arrowColor, x + 2, y + 8, x + 6, y + 4);
            DrawLine(arrowColor, x + 3, y + 8, x + 6, y + 5);
            DrawLine(arrowColor, x + 6, y + 4, x + 8, y + 4);
            DrawLine(arrowColor, x + 6, y + 4, x + 6, y + 6);
        }

        public void Set(bool selected)
        {
            isPressed = selected;
            MarkDirty();
        }

        public bool IsInsideLabel(int absX, int absY)
        {
            return absX >= AbsoluteX && absX <= AbsoluteX + Width &&
                   absY >= AbsoluteY + LabelTop && absY <= AbsoluteY + Height;
        }

        public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
        {
            if (renaming)
            {
                if (mouse.left == MouseEvents.Press && IsInsideLabel(mouseX, mouseY))
                {
                    // Click inside rename edit box: position caret
                    int relX = mouseX - AbsoluteX;
                    int textW = font.MeasureString(renameText, FontSize);
                    int startX = Math.Max(4, (Width - textW) / 2);
                    int clickOffset = relX - startX;
                    renameAllSelected = false;

                    if (clickOffset <= 0)
                    {
                        renameCaretIndex = 0;
                    }
                    else
                    {
                        int bestIndex = renameText.Length;
                        int bestDist = int.MaxValue;
                        for (int idx = 0; idx <= renameText.Length; idx++)
                        {
                            int w = font.MeasureString(renameText.Substring(0, idx), FontSize);
                            int dist = Math.Abs(w - clickOffset);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestIndex = idx;
                            }
                        }
                        renameCaretIndex = bestIndex;
                    }
                    MarkDirty();
                    return true;
                }
                return base.HandleInput(mouseX, mouseY, mouse);
            }

            if (mouse.left == MouseEvents.Press)
            {
                dragging = true;
                offset = new Point(mouseX - X, mouseY - Y);
            }
            else if (mouse.left == MouseEvents.Release && dragging)
            {
                dragging = false;
            }
            else if (mouse.left == MouseEvents.None && dragging)
            {
                dragging = false;
            }

            if (dragging && (mouse.left == MouseEvents.Hold || mouse.left == MouseEvents.Press))
            {
                MoveTo(mouseX - offset.X, mouseY - offset.Y);
            }

            return base.HandleInput(mouseX, mouseY, mouse);
        }

        private bool renaming;

        public void BeginRename()
        {
            if (isSystemIcon) return;

            renameOriginalText = isShortcut
                ? Path.GetFileNameWithoutExtension(fileEntry.FileName ?? "")
                : (fileEntry.FileName ?? "");
            renameText = renameOriginalText;
            renameCaretIndex = renameText.Length;
            renameAllSelected = true;
            renaming = true;
            MarkDirty();
        }

        public void CommitRename()
        {
            if (!renaming) return;

            string newName = (renameText ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newName) || newName == "." || newName == ".." ||
                newName.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                CancelRename();
                return;
            }

            if (newName == renameOriginalText)
            {
                CancelRename();
                return;
            }

            string finalFileName = newName;
            if (isShortcut)
            {
                if (finalFileName.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    finalFileName = finalFileName.Substring(0, finalFileName.Length - 4).Trim();
                else if (finalFileName.EndsWith("lnk", StringComparison.OrdinalIgnoreCase) && finalFileName.Length > 3)
                    finalFileName = finalFileName.Substring(0, finalFileName.Length - 3).Trim();

                finalFileName += ".lnk";
            }

            string oldPath = fileEntry.AbsoluteLocation;
            string newPath = ReplacePathName(oldPath, finalFileName);
            if (!PathExists(oldPath) || (oldPath != newPath && PathExists(newPath)))
            {
                CancelRename();
                return;
            }

            try
            {
                if (fileEntry.FileType == FileType.Directory)
                    Directory.Move(oldPath, newPath);
                else
                    File.Move(oldPath, newPath);
            }
            catch
            {
                CancelRename();
                return;
            }

            fileEntry.FileName = finalFileName;
            fileEntry.AbsoluteLocation = newPath;
            Desktop.SaveLayout();

            renameText = "";
            renameOriginalText = "";
            renameAllSelected = false;
            renameCaretIndex = 0;
            renaming = false;
            MarkDirty();
        }

        public void CancelRename()
        {
            if (!renaming) return;

            renameText = "";
            renameOriginalText = "";
            renameAllSelected = false;
            renameCaretIndex = 0;
            renaming = false;
            MarkDirty();
        }

        public override void HandleKeyboard(KeyEvent keyEvent)
        {
            if (keyEvent.Key == Key.F2)
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
                    case Key.Enter:
                        CommitRename();
                        return;

                    case Key.Escape:
                        CancelRename();
                        return;

                    case Key.LeftArrow:
                        if (renameAllSelected)
                        {
                            renameAllSelected = false;
                            renameCaretIndex = 0;
                        }
                        else if (renameCaretIndex > 0)
                        {
                            renameCaretIndex--;
                        }
                        MarkDirty();
                        return;

                    case Key.RightArrow:
                        if (renameAllSelected)
                        {
                            renameAllSelected = false;
                            renameCaretIndex = renameText.Length;
                        }
                        else if (renameCaretIndex < renameText.Length)
                        {
                            renameCaretIndex++;
                        }
                        MarkDirty();
                        return;

                    case Key.Home:
                        renameAllSelected = false;
                        renameCaretIndex = 0;
                        MarkDirty();
                        return;

                    case Key.End:
                        renameAllSelected = false;
                        renameCaretIndex = renameText.Length;
                        MarkDirty();
                        return;

                    case Key.Backspace:
                        if (renameAllSelected)
                        {
                            renameText = "";
                            renameCaretIndex = 0;
                            renameAllSelected = false;
                        }
                        else if (renameCaretIndex > 0 && renameText.Length > 0)
                        {
                            renameText = renameText.Remove(renameCaretIndex - 1, 1);
                            renameCaretIndex--;
                        }
                        MarkDirty();
                        return;

                    case Key.Delete:
                        if (renameAllSelected)
                        {
                            renameText = "";
                            renameCaretIndex = 0;
                            renameAllSelected = false;
                        }
                        else if (renameCaretIndex < renameText.Length)
                        {
                            renameText = renameText.Remove(renameCaretIndex, 1);
                        }
                        MarkDirty();
                        return;

                    default:
                        char printable = GetPrintableCharacter(keyEvent);
                        if (printable != '\0')
                        {
                            if (renameAllSelected)
                            {
                                renameText = printable.ToString();
                                renameCaretIndex = 1;
                                renameAllSelected = false;
                            }
                            else
                            {
                                renameText = renameText.Insert(renameCaretIndex, printable.ToString());
                                renameCaretIndex++;
                            }
                            MarkDirty();
                        }
                        return;
                }
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

        private TrueTypeFont font = SystemFonts.msSansSerif;

        private void DrawLabel()
        {
            string name = renaming ? renameText : (isShortcut ? Path.GetFileNameWithoutExtension(fileEntry.FileName ?? "") : fileEntry.FileName);
            if (string.IsNullOrEmpty(name) && !renaming) return;

            string[] lines = BuildLabelLines(name);
            if (renaming && lines.Length == 0)
                lines = new string[] { "" };

            if (renaming)
            {
                int boxW = Width - 4;
                int boxX = 2;
                int boxH = Math.Max(LabelLineHeight + 4, lines.Length * LabelLineHeight + 4);

                // Edit box background and border
                DrawFilledRectangle(Color.White, boxX, LabelTop - 2, boxW, boxH);
                DrawRectangle(Palette.Highlight, boxX, LabelTop - 2, boxW - 1, boxH - 1);

                if (renameAllSelected && !string.IsNullOrEmpty(renameText))
                {
                    int textW = font.MeasureString(renameText, FontSize);
                    int selX = Math.Max(boxX + 2, (Width - textW) / 2);
                    int selW = Math.Min(boxW - 4, textW + 4);
                    DrawFilledRectangle(Palette.Highlight, selX - 2, LabelTop - 1, selW, LabelLineHeight + 2);
                    DrawString(renameText, font, FontSize, Color.White, selX, LabelTop);
                }
                else
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        int lineWidth = font.MeasureString(lines[i], FontSize);
                        int x = Math.Max(boxX + 3, (Width - lineWidth) / 2);
                        int y = LabelTop + i * LabelLineHeight;
                        DrawString(lines[i], font, FontSize, Color.Black, x, y);
                    }

                    DrawRenameCaret(lines);
                }
            }
            else
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    int lineWidth = font.MeasureString(lines[i], FontSize);
                    int x = Math.Max(2, (Width - lineWidth) / 2);
                    int y = LabelTop + i * LabelLineHeight;

                    // Windows 7 Aero text drop-shadow for readability on wallpapers
                    Color shadow = Color.FromArgb(140, 0, 0, 0);
                    DrawString(lines[i], font, FontSize, shadow, x + 1, y + 1);
                    DrawString(lines[i], font, FontSize, shadow, x, y + 1);

                    DrawString(lines[i], font, FontSize, Color.White, x, y);
                }
            }
        }

        private string[] BuildLabelLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

            int maxLineWidth = Math.Max(20, Width - 14);
            List<string> lines = new List<string>();
            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string currentLine = "";

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                string candidate = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;

                if (font.MeasureString(candidate, FontSize) <= maxLineWidth)
                {
                    currentLine = candidate;
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine);
                        currentLine = "";
                        if (lines.Count >= MaxLabelLines) break;
                    }

                    // Single word exceeds line width: split by characters
                    if (font.MeasureString(word, FontSize) > maxLineWidth)
                    {
                        string partial = "";
                        for (int c = 0; c < word.Length; c++)
                        {
                            string nextPartial = partial + word[c];
                            if (font.MeasureString(nextPartial, FontSize) <= maxLineWidth)
                            {
                                partial = nextPartial;
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(partial))
                                {
                                    lines.Add(partial);
                                    partial = "";
                                    if (lines.Count >= MaxLabelLines) break;
                                }
                                partial = word[c].ToString();
                            }
                        }
                        currentLine = partial;
                    }
                    else
                    {
                        currentLine = word;
                    }

                    if (lines.Count >= MaxLabelLines) break;
                }
            }

            if (lines.Count < MaxLabelLines && !string.IsNullOrEmpty(currentLine))
            {
                lines.Add(currentLine);
            }

            // Ellipsize the final line if content was truncated
            if (lines.Count == MaxLabelLines)
            {
                string combined = string.Join(" ", lines);
                if (combined.Replace(" ", "").Length < text.Replace(" ", "").Length)
                {
                    string last = lines[MaxLabelLines - 1];
                    while (last.Length > 0 && font.MeasureString(last + "...", FontSize) > maxLineWidth)
                    {
                        last = last.Substring(0, last.Length - 1);
                    }
                    lines[MaxLabelLines - 1] = last + "...";
                }
            }

            return lines.ToArray();
        }

        private void DrawRenameCaret(string[] lines)
        {
            if (lines.Length == 0) return;

            int charsCounted = 0;
            int caretLine = 0;
            int caretLineIndex = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                int lineLen = lines[i].Length;
                if (renameCaretIndex <= charsCounted + lineLen)
                {
                    caretLine = i;
                    caretLineIndex = Math.Max(0, renameCaretIndex - charsCounted);
                    break;
                }
                charsCounted += lineLen + 1;
                caretLine = i;
                caretLineIndex = lineLen;
            }

            string lineText = lines[caretLine];
            string sub = caretLineIndex <= lineText.Length ? lineText.Substring(0, caretLineIndex) : lineText;
            int caretOffset = font.MeasureString(sub, FontSize);
            int lineW = font.MeasureString(lineText, FontSize);
            int startX = Math.Max(5, (Width - lineW) / 2);
            int caretX = Math.Min(Width - 4, startX + caretOffset);
            int caretY = LabelTop + caretLine * LabelLineHeight;

            DrawLine(Color.Black, caretX, caretY, caretX, caretY + LabelLineHeight - 1);
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

        public override string GetComponentName() => "DesktopIcon";
    }
}
