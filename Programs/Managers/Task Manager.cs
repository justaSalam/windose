using System.Drawing;
using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Windose;

public class PerformanceMonitor : Window
{
    private readonly DockPanel root;
    private readonly DockPanel applicationsPage;
    private readonly DockPanel processesPage;
    private readonly DockPanel performancePage;
    private readonly Panel summary;
    private readonly Panel memorySummary;
    private readonly PerformanceGraph frameGraph;
    private readonly PerformanceGraph pipelineGraph;
    private readonly PerformanceGraph memoryGraph;
    private readonly ProcessPerformanceList applicationsList;
    private readonly ProcessPerformanceList processList;
    private int selectedPage;
    private int lastSampleTick;
    private float memoryGraphMaximum = 16;
    private const int SampleIntervalMs = 250;

    public PerformanceMonitor(int x, int y, int width = 720, int height = 560)
        : base(x, y, width, height, "Task Manager", true)
    {
        root = new DockPanel(0, 0, Width, Height)
        {
            horizontalAlignment = HorizontalAlignment.Stretch,
            verticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(28, 4, 4, 4),
            Padding = new Thickness(4),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };

        StackPanel tabs = new StackPanel(Palette.ControlFace, 0, 0, Width, 28)
        {
            orientation = StackOrientation.Horizontal,
            clampSize = false,
            useBackground = true,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            spacing = 2,
        };
        tabs.AddStackChild(CreateTab("Applications", 112, () => ShowPage(0)));
        tabs.AddStackChild(CreateTab("Processes", 96, () => ShowPage(1)));
        tabs.AddStackChild(CreateTab("Performance", 112, () => ShowPage(2)));

        applicationsPage = CreatePage();
        applicationsList = new ProcessPerformanceList(0, 0, Width, Height)
        {
            programsOnly = true,
            clampSize = false,
            Margin = new Thickness(0),
        };
        applicationsPage.AddDockChild(applicationsList, Dock.Fill);

        processesPage = CreatePage();
        processList = new ProcessPerformanceList(0, 0, Width, Height)
        {
            clampSize = false,
            Margin = new Thickness(0),
        };
        processesPage.AddDockChild(processList, Dock.Fill);

        performancePage = CreatePage();
        summary = new Panel(Palette.ControlFace, 0, 0, Width, 28)
        {
            useBackground = true,
            fontSize = 16,
            textColor = Palette.ControlBlack,
            clampSize = false,
            Margin = new Thickness(0, 0, 4, 0),
        };

        frameGraph = new PerformanceGraph(0, 0, Width, 105)
        {
            maximum = 50,
            clampSize = false,
            Margin = new Thickness(0, 0, 6, 0),
        };
        frameGraph.SetSeries(0, "Frame ms", Color.Lime);
        frameGraph.SetSeries(1, "16.7 target", Color.Yellow);

        pipelineGraph = new PerformanceGraph(0, 0, Width, 115)
        {
            maximum = 20,
            clampSize = false,
            Margin = new Thickness(0),
        };
        pipelineGraph.SetSeries(0, "Upload", Color.Cyan);
        pipelineGraph.SetSeries(1, "Display", Color.Lime);
        pipelineGraph.SetSeries(2, "Overlay", Color.Yellow);
        pipelineGraph.SetSeries(3, "UI", Color.Magenta);
        pipelineGraph.SetSeries(4, "Paint", Color.White);

        memorySummary = new Panel(Palette.ControlFace, 0, 0, Width, 24)
        {
            useBackground = true,
            fontSize = 16,
            textColor = Palette.ControlBlack,
            clampSize = false,
            Margin = new Thickness(0, 0, 3, 0),
        };

        memoryGraph = new PerformanceGraph(0, 0, Width, 150)
        {
            maximum = memoryGraphMaximum,
            clampSize = false,
            Margin = new Thickness(0),
        };
        memoryGraph.SetSeries(0, "Heap MB", Color.Lime);
        memoryGraph.SetSeries(1, "Committed MB", Color.Cyan);
        memoryGraph.SetSeries(2, "Fragmented MB", Color.Yellow);

        performancePage.AddDockChild(summary, Dock.Top);
        performancePage.AddDockChild(frameGraph, Dock.Top);
        performancePage.AddDockChild(pipelineGraph, Dock.Top);
        performancePage.AddDockChild(memorySummary, Dock.Top);
        performancePage.AddDockChild(memoryGraph, Dock.Fill);

        root.AddDockChild(tabs, Dock.Top);
        root.AddDockChild(applicationsPage, Dock.Fill);
        root.AddDockChild(processesPage, Dock.Fill);
        root.AddDockChild(performancePage, Dock.Fill);
        AddChild(root);
        ShowPage(0);
    }

    private static DockPanel CreatePage()
    {
        return new DockPanel(0, 0, 100, 100)
        {
            clampSize = false,
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            useBackground = true,
            backgroundColor = Palette.ControlFace,
        };
    }

    private static Button CreateTab(string text, int width, Action action)
    {
        return new Button(0, 0, width, 26)
        {
            text = text,
            fontSize = 14,
            useBorders = true,
            clampSize = false,
            Margin = new Thickness(0),
            leftMouseRelease = action,
        };
    }

    private void ShowPage(int page)
    {
        selectedPage = page;
        applicationsPage.Visible = page == 0;
        processesPage.Visible = page == 1;
        performancePage.Visible = page == 2;
        root.ResolveDockLayout();
        root.MarkDirty();
    }

    public override void Update()
    {
        int now = Environment.TickCount;
        if (now - lastSampleTick < SampleIntervalMs) return;
        lastSampleTick = now;

        if (selectedPage == 0)
        {
            applicationsList.MarkDirty();
            return;
        }
        if (selectedPage == 1)
        {
            processList.MarkDirty();
            return;
        }

        summary.text = $"FPS {Kernel.Fps}    Frame {Kernel.DeltaTimeMs:0.0} ms    UI {PerformanceMetrics.ProcessMs:0.0} ms    Present {PerformanceMetrics.PresentMs:0.0} ms";
        summary.MarkDirty();
        frameGraph.AddSample((float)Kernel.DeltaTimeMs, 16.7f);
        pipelineGraph.AddSample(
            (float)PerformanceMetrics.UploadMs,
            (float)PerformanceMetrics.DisplayMs,
            (float)PerformanceMetrics.OverlayMs,
            (float)PerformanceMetrics.ProcessMs,
            (float)PerformanceMetrics.ComposeMs);

        GarbageCollector.GetStats(out int collections, out int freed);
        float heapMb = BytesToMb(GarbageCollector.GetHeapSizeBytes());
        float committedMb = BytesToMb(GarbageCollector.GetTotalCommittedBytes());
        float fragmentedMb = BytesToMb(GarbageCollector.GetFragmentedBytes());
        int gcPercent = GarbageCollector.GetLastGCPercentTimeInGC();
        ulong pinnedObjects = GarbageCollector.GetPinnedObjectsCount();

        float largestValue = Math.Max(heapMb, Math.Max(committedMb, fragmentedMb));
        if (largestValue > memoryGraphMaximum)
        {
            memoryGraphMaximum = Math.Max(16, (float)Math.Ceiling(largestValue / 8) * 8);
            memoryGraph.maximum = memoryGraphMaximum;
        }

        memorySummary.text = $"Heap {heapMb:0.0}M  Commit {committedMb:0.0}M  Frag {fragmentedMb:0.0}M  GC {gcPercent}%  Runs {collections}  Freed {freed}  Pinned {pinnedObjects}";
        memorySummary.MarkDirty();
        memoryGraph.AddSample(heapMb, committedMb, fragmentedMb);
    }

    private static float BytesToMb(ulong bytes) => bytes / (1024f * 1024f);

    public override string GetName() => "PerformanceMonitor";
}
