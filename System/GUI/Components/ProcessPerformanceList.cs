using System.Drawing;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Keyboard;

public class ProcessPerformanceList : Component
{
    public int fontSize = 16;
    public Color backgroundColor = Color.White;
    public Color textColor = Palette.ControlBlack;
    public bool programsOnly;
    private readonly MenuPopup contextMenu;
    private readonly MenuItem priorityMenu;
    private readonly MenuItem endTaskItem;
    private readonly MenuItem restartTaskItem;
    private readonly MenuItem openLocationItem;
    private readonly MenuItem propertiesItem;
    private readonly MenuItem setPriorityItem;

    private Process selectedProcess;
    private string filterText = "";
    private bool showFilter;
    private int scrollOffset;
    private int totalVisibleProcesses;

    public ProcessPerformanceList(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        contextMenu = new MenuPopup(220, 28 * 8);
        endTaskItem = contextMenu.AddItem("End Task", EndSelectedProcess);
        restartTaskItem = contextMenu.AddItem("Restart Task", RestartSelectedProcess);
        contextMenu.AddSeparator();
        setPriorityItem = contextMenu.AddItem("Set Priority", null);
        contextMenu.AddSeparator();
        openLocationItem = contextMenu.AddItem("Open File Location", OpenProcessFileLocation);
        propertiesItem = contextMenu.AddItem("Properties", OpenProcessProperties);

        priorityMenu = new MenuItem(160, 28 * 5, 100, 20);
        priorityMenu.AddSubmenuItem("Idle", () => SetPriority(ProcessPriority.Idle));
        priorityMenu.AddSubmenuItem("Low", () => SetPriority(ProcessPriority.Low));
        priorityMenu.AddSubmenuItem("Normal", () => SetPriority(ProcessPriority.Normal));
        priorityMenu.AddSubmenuItem("High", () => SetPriority(ProcessPriority.High));
        priorityMenu.AddSubmenuItem("Critical", () => SetPriority(ProcessPriority.Critical));
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);
        DrawSunkenRectangle(0, 0, Width, Height);

        int colX = 6;
        DrawString(programsOnly ? "Application" : "Process", textColor, colX, 4, fontSize);
        colX += 210;
        DrawString("ID", textColor, colX, 4, fontSize);
        colX += 40;
        DrawString("Priority", textColor, colX, 4, fontSize);
        colX += 50;
        DrawString("State", textColor, colX, 4, fontSize);
        colX += 70;
        DrawString("Last ms", textColor, colX, 4, fontSize);
        colX += 70;
        DrawString("Avg ms", textColor, colX, 4, fontSize);
        colX += 70;
        DrawString("Peak ms", textColor, colX, 4, fontSize);
        colX += 70;
        DrawString("Crashes", textColor, colX, 4, fontSize);
        DrawLine(Palette.ControlShadow, 2, 23, Width - 3, 23);

        int headerHeight = 24;
        int y = headerHeight + 4;
        totalVisibleProcesses = 0;

        for (int i = 0; i < ProcessManger.ProcessCount; i++)
        {
            
            Process process = ProcessManger.GetProcessAt(i);
            if (programsOnly && process.processType != ProcessType.Program)
            {
                continue;
            }

            if (totalVisibleProcesses < scrollOffset)
            {
                totalVisibleProcesses++;
                continue;
            }

            if (y + fontSize > Height)
            {
                totalVisibleProcesses++;
                break;
            }

            bool selected = process == selectedProcess;
            Color rowText = selected ? Palette.HighlightText : textColor;
            if (selected)
                DrawFilledRectangle(Palette.Highlight, 2, y - 2, Math.Max(1, Width - 4), fontSize + 4);

            if (process.IsCrashed)
            {
                DrawFilledRectangle(Color.FromArgb(255, 200, 200), 2, y - 2, Math.Max(1, Width - 4), fontSize + 4);
                rowText = Color.FromArgb(180, 0, 0);
            }
            else if (process.CrashCount > 0)
            {
                DrawFilledRectangle(Color.FromArgb(255, 240, 200), 2, y - 2, Math.Max(1, Width - 4), fontSize + 4);
                rowText = Color.FromArgb(150, 100, 0);
            }

            int cx = 6;
            DrawString(TrimName(process.name, 24), rowText, cx, y, fontSize);
            cx += 210;
            DrawString(process.id.ToString(), rowText, cx, y, fontSize);
            cx += 40;
            DrawString(GetPriorityLabel(process.Priority), rowText, cx, y, fontSize);
            cx += 50;
            DrawString(GetStateLabel(process), rowText, cx, y, fontSize);
            cx += 70;
            DrawString(process.lastUpdateMs.ToString("0.00"), rowText, cx, y, fontSize);
            cx += 70;
            DrawString(process.averageUpdateMs.ToString("0.00"), rowText, cx, y, fontSize);
            cx += 70;
            DrawString(process.peakUpdateMs.ToString("0.00"), rowText, cx, y, fontSize);
            cx += 70;
            DrawString(process.CrashCount + "/" + process.MaxCrashesBeforeTermination, rowText, cx, y, fontSize);

            y += fontSize + 4;
            totalVisibleProcesses++;
        }

        int summaryY = Height - 20;
        DrawFilledRectangle(Palette.ControlFace, 0, summaryY, Width, 20);
        DrawLine(Palette.ControlShadow, 0, summaryY, Width, 1);

        int runningCount = 0;
        int crashedCount = 0;
        for (int i = 0; i < ProcessManger.ProcessCount; i++)
        {
            Process p = ProcessManger.GetProcessAt(i);
            if (p.Running) runningCount++;
            if (p.IsCrashed) crashedCount++;
        }
        string summary = "Processes: " + ProcessManger.ProcessCount + "  Running: " + runningCount;
        if (crashedCount > 0) summary += "  Crashed: " + crashedCount;
        DrawString(summary, Palette.ControlBlack, 6, summaryY + 2, 14);
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;

        if (Mouse.scroll != 0)
        {
            int maxScroll = Math.Max(0, totalVisibleProcesses - (Height - 48) / (fontSize + 4));
            scrollOffset = Math.Clamp(scrollOffset - (int)Mouse.scroll, 0, maxScroll);
            MarkDirty();
            return true;
        }

        if (mouse.left == MouseEvents.Press)
        {
            selectedProcess = GetProcessAt(mouseY - AbsoluteY);
            MarkDirty();
            return true;
        }

        if (mouse.right == MouseEvents.Release)
        {
            selectedProcess = GetProcessAt(mouseY - AbsoluteY);
            MarkDirty();
            if (selectedProcess != null)
            {
                endTaskItem.enabled = selectedProcess.canTerminate;
                restartTaskItem.enabled = selectedProcess.canTerminate && selectedProcess.CanRestart;
                setPriorityItem.enabled = true;
                UpdatePriorityMenuChecks();
                int x = Math.Min(mouseX, Math.Max(0, Global.screenWidth - contextMenu.Width));
                int y = Math.Min(mouseY, Math.Max(0, Global.screenHeight - contextMenu.Height));
                contextMenu.ShowAt(x, y);
            }
            return true;
        }

        return true;
    }

    public override void HandleKeyboard(KeyEvent keyEvent)
    {
        char printable = GetPrintableCharacter(keyEvent);
        if (IsControlPressed(keyEvent) && (printable == 'f' || printable == 'F'))
        {
            showFilter = !showFilter;
            if (!showFilter) filterText = "";
            MarkDirty();
            return;
        }

        if (!showFilter) return;

        if (keyEvent.Key == ConsoleKeyEx.Backspace && filterText.Length > 0)
        {
            filterText = filterText.Substring(0, filterText.Length - 1);
            MarkDirty();
            return;
        }

        char c = printable;
        if (c != 0 && c >= 32)
        {
            filterText += c;
            MarkDirty();
        }
    }

    private Process GetProcessAt(int localY)
    {
        int headerHeight = 24;
        if (localY < headerHeight + 4) return null;
        int row = (localY - headerHeight - 4) / (fontSize + 4);
        if (row < 0) return null;

        int visibleRow = 0;
        for (int i = 0; i < ProcessManger.ProcessCount; i++)
        {
            Process process = ProcessManger.GetProcessAt(i);
            if (programsOnly && process.processType != ProcessType.Program) continue;
            if (visibleRow < scrollOffset) { visibleRow++; continue; }
            if (visibleRow - scrollOffset == row) return process;
            visibleRow++;
        }
        return null;
    }

    private void EndSelectedProcess()
    {
        if (selectedProcess == null || !selectedProcess.canTerminate || !ProcessManger.Contains(selectedProcess)) return;
        ProcessManger.Stop(selectedProcess);
        selectedProcess = null;
        MarkDirty();
    }

    private void RestartSelectedProcess()
    {
        if (selectedProcess == null || !ProcessManger.Contains(selectedProcess)) return;
        ProcessManger.Restart(selectedProcess);
        selectedProcess = null;
        MarkDirty();
    }

    private void SetPriority(ProcessPriority priority)
    {
        if (selectedProcess == null || !ProcessManger.Contains(selectedProcess)) return;
        selectedProcess.Priority = priority;

        switch (priority)
        {
            case ProcessPriority.Idle:
                selectedProcess.UpdateSkipThreshold = 10;
                break;
            case ProcessPriority.Low:
                selectedProcess.UpdateSkipThreshold = 4;
                break;
            default:
                selectedProcess.UpdateSkipThreshold = 0;
                break;
        }

        MarkDirty();
    }


    private void UpdatePriorityMenuChecks()
    {
        if (selectedProcess == null) return;
        ProcessPriority current = selectedProcess.Priority;
    }

    private void OpenProcessFileLocation()
    {
        string path = selectedProcess?.startInfo?.ExecutablePath;
        if (string.IsNullOrEmpty(path)) return;
        FileExplorer explorer = new FileExplorer(100, 100, 800, 500, "Process Location", true);
        WindowManager.Register(explorer);
        selectedProcess = null;
        MarkDirty();
    }

    private void OpenProcessProperties()
    {
        if (selectedProcess == null) return;
        WindowManager.Register(new ProcessProperties(200, 200, selectedProcess));
        selectedProcess = null;
        MarkDirty();
    }

    private static string GetPriorityLabel(ProcessPriority priority)
    {
        switch (priority)
        {
            case ProcessPriority.Idle: return "Idle";
            case ProcessPriority.Low: return "Low";
            case ProcessPriority.Normal: return "Normal";
            case ProcessPriority.High: return "High";
            case ProcessPriority.Critical: return "Critical";
            default: return "Normal";
        }
    }

    private static string GetStateLabel(Process process)
    {
        if (process.IsCrashed) return "Crashed";
        if (!process.Running) return "Stopped";
        if (process.CrashCount > 0) return "Unstable";
        return "Running";
    }

    public override void Dispose()
    {
        if (contextMenu != null)
        {
            contextMenu.Hide();
            contextMenu.Dispose();
        }
        if (priorityMenu != null)
        {
            priorityMenu.Dispose();
        }
        base.Dispose();
    }

    private static string TrimName(string value, int maximumLength)
    {
        if (value == null || value.Length <= maximumLength) return value ?? "";
        return value.Substring(0, maximumLength - 3) + "...";
    }

    public override bool IsOpaqueForCopy() => true;
    public override string GetName() => "ProcessPerformanceList";
}
