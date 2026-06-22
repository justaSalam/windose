using System.Drawing;

public class ProcessPerformanceList : Component
{
    public int fontSize = 16;
    public Color backgroundColor = Color.White;
    public Color textColor = Palette.ControlBlack;
    public bool programsOnly;
    private readonly MenuPopup contextMenu;
    private readonly MenuItem endTaskItem;
    private readonly MenuItem restartTaskItem;
    private readonly MenuItem openLocationItem;
    private Process selectedProcess;

    public ProcessPerformanceList(int x, int y, int width, int height) : base(x, y, width, height)
    {
        clampSize = false;
        contextMenu = new MenuPopup(200, 28 * 4);
        endTaskItem = contextMenu.AddItem("End Task", EndSelectedProcess);
        restartTaskItem = contextMenu.AddItem("Restart Task", RestartSelectedProcess);
        openLocationItem = contextMenu.AddItem("Open File Location", OpenProcessFileLocation);
        contextMenu.AddItem("Properties", OpenProcessProperties);
    }

    public override void DrawLocal()
    {
        DrawFilledRectangle(backgroundColor, 0, 0, Width, Height);
        DrawSunkenRectangle(0, 0, Width, Height);

        DrawString(programsOnly ? "Application" : "Process", textColor, 6, 4, fontSize);
        DrawString("ID", textColor, 240, 4, fontSize);
        DrawString(programsOnly ? "Started" : "Type", textColor, 290, 4, fontSize);
        DrawString("Last ms", textColor, 390, 4, fontSize);
        DrawString("Average", textColor, 490, 4, fontSize);
        DrawString("Peak", textColor, 600, 4, fontSize);
        DrawLine(Palette.ControlShadow, 2, 23, Width - 3, 23);

        int y = 28;
        for (int i = 0; i < ProcessManger.ProcessCount; i++)
        {
            Process process = ProcessManger.GetProcessAt(i);
            if (programsOnly && process.processType != ProcessType.Program) continue;
            if (y + fontSize > Height) break;

            bool selected = process == selectedProcess;
            Color rowText = selected ? Palette.HighlightText : textColor;
            if (selected)
                DrawFilledRectangle(Palette.Highlight, 2, y - 2, Math.Max(1, Width - 4), fontSize + 4);

            DrawString(TrimName(process.name, 28), rowText, 6, y, fontSize);
            DrawString(process.id.ToString(), rowText, 240, y, fontSize);
            DrawString(programsOnly ? process.startTime : process.processType.ToString(), rowText, 290, y, fontSize);
            DrawString(process.lastUpdateMs.ToString("0.00"), rowText, 390, y, fontSize);
            DrawString(process.averageUpdateMs.ToString("0.00"), rowText, 490, y, fontSize);
            DrawString(process.peakUpdateMs.ToString("0.00"), rowText, 600, y, fontSize);
            y += fontSize + 4;
        }
    }

    public override bool HandleInput(int mouseX, int mouseY, MouseState mouse)
    {
        if (!IsInsideAbsolute(mouseX, mouseY)) return false;
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
                openLocationItem.enabled = selectedProcess.startInfo?.HasExecutablePath == true;
                int x = Math.Min(mouseX, Math.Max(0, Global.screenWidth - contextMenu.Width));
                int y = Math.Min(mouseY, Math.Max(0, Global.screenHeight - contextMenu.Height));
                contextMenu.ShowAt(x, y);
            }
            return true;
        }
        return true;
    }

    private Process GetProcessAt(int localY)
    {
        if (localY < 28) return null;
        int row = (localY - 28) / (fontSize + 4);
        int visibleRows = Math.Max(0, (Height - 28) / (fontSize + 4));
        if (row < 0 || row >= visibleRows) return null;
        int visibleRow = 0;
        for (int i = 0; i < ProcessManger.ProcessCount; i++)
        {
            Process process = ProcessManger.GetProcessAt(i);
            if (programsOnly && process.processType != ProcessType.Program) continue;
            if (visibleRow++ == row) return process;
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

    private void OpenProcessFileLocation()
    {
        string path = selectedProcess?.startInfo?.ExecutablePath;
        if (string.IsNullOrEmpty(path)) return;
        string directory = FileSystemManager.Current?.DirectoryExists(path) == true
            ? FileSystemManager.NormalizePath(path)
            : FileSystemManager.GetParent(path);
        if (string.IsNullOrEmpty(directory)) return;

        FileExplorer explorer = new FileExplorer(100, 100, 800, 500, "File Location", true);
        explorer.NavigateToPath(directory);
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

    public override void Dispose()
    {
        contextMenu.Hide();
        contextMenu.Dispose();
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
