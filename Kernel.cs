using Cosmos.Kernel.Core.Memory.GarbageCollector;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Network;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4.UDP.DHCP;
using System.Drawing;
using Windose.Drivers;
using Windose.Installer;
using Windose.Programs.Breeze;
using Windose.System.Features;
using Windose.System.System_Calls;
using Sys = Cosmos.Kernel.System;


namespace Windose;

/// <summary>
/// Main kernel class - inherits from Cosmos.Kernel.System.Kernel.
/// </summary>
public class Kernel : Sys.Kernel
{
    public static Color Gray = Color.FromArgb(123, 126, 121);
    public static Color Blue = Color.FromArgb(0, 0, 128);

    public static Kernel Instance = null!;
    public static DirectBitmap mainBuffer;
    public static SVGAII3DCanvas canvas;
    
    
    

    private WindowManager windowManager = null!;
    public CosmosDisplayDriver displayDriver = null!;
    private CosmosMouseDriver mouseDriver = null!;
    private int tick = 0;

    protected override void BeforeRun()
    {
        
        KernelConsole.Default.Font = SystemFonts.lucida;
        KernelPanic.Install();
        try
        {
            InitializeKernel();
        }
        catch (Exception exception)
        {
            KernelPanic.Show("KERNEL_INITIALIZATION_FAILURE", exception);
        }
    }

    private async void InitializeKernel()
    {
        Instance = this;
        GarbageCollector.Initialize();
        Palette.Initialize();


        //TODO: 
        //main partition, copy files, general setup
        Setup.Run();

        displayDriver = new CosmosDisplayDriver();

        DriverManager.Register(displayDriver);
        DriverManager.Start(displayDriver);

        canvas = displayDriver.Canvas;
        mainBuffer = displayDriver.BackBuffer;



        mouseDriver = new CosmosMouseDriver(displayDriver.Width, displayDriver.Height);
        DriverManager.Register(mouseDriver);
        DriverManager.StartAll();

        Global.screenHeight = displayDriver.Height;
        Global.screenWidth = displayDriver.Width;
        Registry.SetRuntimeValue("System/Display/CurrentWidth", (long)displayDriver.Width);
        Registry.SetRuntimeValue("System/Display/CurrentHeight", (long)displayDriver.Height);

        SystemLogger.WriteLine("Kernel", "Boot completed successfully", ConsoleMessageType.Log);

        Background.Load();
        Explorer explorer = new Explorer(canvas);
        windowManager = new WindowManager();




        ProcessManger.Start(explorer);
        ProcessManger.Start(windowManager);
        ProcessManger.Start(new HotkeyManager());


        Directory.CreateDirectory("/mnt/Programs");
        Directory.CreateDirectory("/mnt/Apps");
        File.WriteAllText("/mnt/Programs/ControlTest.breeze", ControlTest.data);
        File.WriteAllText("/mnt/Programs/CLAUDE.breeze", breezeScript);

        BreezeCapabilityPolicy.Grant("/mnt/Apps/main.breeze","service.control");

            
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = ConsoleKeyEx.Tab, Modifiers = ConsoleModifiers.Alt}, Power.Reboot);
        HotkeyManager.RegisterHotkey(new KeyEvent { Key = ConsoleKeyEx.S, Modifiers = ConsoleModifiers.Control & ConsoleModifiers.Shift }, Power.Reboot);



        File.WriteAllBytes("/mnt/System/kbReadTest.bin", new byte[1024]);
        File.WriteAllBytes("/mnt/System/mbReadTest.bin", new byte[1024 * 1024]);
    }

    private long lastFrameTicks;
    public static double DeltaTimeMs;
    public static double DeltaTimeSeconds;
    public static int Fps;
    protected override void Run()
    {
        try
        {
            mouseDriver.Update();
            Tick();

            PerformanceMetrics.BeginFrame();

            long processStartedAt = PerformanceMetrics.Now;

            ProcessManger.Update();

            PerformanceMetrics.ProcessTicks = PerformanceMetrics.Now - processStartedAt;

            displayDriver.Present(mainBuffer, mouseDriver.X, mouseDriver.Y);

            tick++;

        }
        catch (Exception ex)
        {
            KernelPanic.Show("KERNEL_FRAME_FAILURE", ex);
        }

    }

    private void Tick()
    {
        long now = DateTime.UtcNow.Ticks;

        if (lastFrameTicks != 0)
        {
            long deltaTicks = now - lastFrameTicks;

            DeltaTimeMs = deltaTicks / 10000.0;
            DeltaTimeSeconds = deltaTicks / 10_000_000.0;

            if (DeltaTimeSeconds > 0)
                Fps = (int)(1.0 / DeltaTimeSeconds);
        }

        lastFrameTicks = now;
    }
    private const string breezeScript = "// DisplaySettings.breeze\r\n// Breeze rewrite of the C# DisplaySettings window.\r\n// Notes on translation choices are at the bottom of this file.\r\n\r\ncapability(\"ui\");\r\ncapability(\"registry.read\");\r\ncapability(\"registry.write\");\r\n\r\n// --- Window & root layout ---\r\n\r\nlet win = window(\"Display Properties\", 150, 110, 520, 390);\r\nset win.canResize = false;\r\nset win.canMaximize = false;\r\n\r\nlet root = windowRoot(win);\r\n\r\nlet tabs = tabControl(\"\", 480, 290);\r\ndock(root, tabs, \"fill\");\r\n\r\nlet buttonsRow = stackPanel(\"horizontal\");\r\ndock(root, buttonsRow, \"bottom\");\r\n\r\nlet okButton = button(\"OK\", 78, 26);\r\nlet applyButton = button(\"Apply\", 78, 26);\r\nlet cancelButton = button(\"Cancel\", 78, 26);\r\nstack(buttonsRow, okButton);\r\nstack(buttonsRow, applyButton);\r\nstack(buttonsRow, cancelButton);\r\n\r\n// --- Settings tab ---\r\n\r\nlet settingsPage = tabAdd(tabs, \"Settings\");\r\nlet settingsStack = stackPanel(\"vertical\");\r\nadd(settingsPage, settingsStack);\r\n\r\nstack(settingsStack, panel(\"Monitor\", 22));\r\n\r\nlet currentModeLabel = panel(\"\", 22);\r\nstack(settingsStack, currentModeLabel);\r\n\r\nlet restartNotice = panel(\"\", 34);\r\nstack(settingsStack, restartNotice);\r\n\r\nstack(settingsStack, panel(\"Display mode\", 22));\r\nstack(settingsStack, panel(\"Resolution\", 20));\r\n\r\nlet resolutionCombo = comboBox(\"Select a resolution\", 210);\r\ncomboAdd(resolutionCombo, \"640 x 480\");\r\ncomboAdd(resolutionCombo, \"800 x 600\");\r\ncomboAdd(resolutionCombo, \"1024 x 768\");\r\ncomboAdd(resolutionCombo, \"1280 x 720\");\r\ncomboAdd(resolutionCombo, \"1920 x 1080\");\r\nstack(settingsStack, resolutionCombo);\r\n\r\nstack(settingsStack, panel(\"Colors\", 20));\r\n\r\nlet colorDepthCombo = comboBox(\"Select color depth\", 210);\r\ncomboAdd(colorDepthCombo, \"4-bit\");\r\ncomboAdd(colorDepthCombo, \"16-bit\");\r\ncomboAdd(colorDepthCombo, \"24-bit\");\r\ncomboAdd(colorDepthCombo, \"32-bit\");\r\nstack(settingsStack, colorDepthCombo);\r\n\r\n// --- Desktop tab ---\r\n\r\nlet desktopPage = tabAdd(tabs, \"Desktop\");\r\nlet desktopStack = stackPanel(\"vertical\");\r\nadd(desktopPage, desktopStack);\r\n\r\nstack(desktopStack, panel(\"Icon grid\", 22));\r\n\r\nlet snapToGrid = checkbox(\"Snap icons to grid\");\r\nstack(desktopStack, snapToGrid);\r\n\r\nlet showGrid = checkbox(\"Show icon grid\");\r\nstack(desktopStack, showGrid);\r\n\r\nlet gridWidthValue = panel(\"\", 20);\r\nstack(desktopStack, gridWidthValue);\r\n\r\nlet gridWidthSlider = slider(\"\", 260, 28);\r\nset gridWidthSlider.showTicks = true;\r\nsliderRange(gridWidthSlider, 48, 160);\r\nstack(desktopStack, gridWidthSlider);\r\n\r\nlet gridHeightValue = panel(\"\", 20);\r\nstack(desktopStack, gridHeightValue);\r\n\r\nlet gridHeightSlider = slider(\"\", 260, 28);\r\nset gridHeightSlider.showTicks = true;\r\nsliderRange(gridHeightSlider, 48, 160);\r\nstack(desktopStack, gridHeightSlider);\r\n\r\nlet presetRow = stackPanel(\"horizontal\");\r\nstack(desktopStack, presetRow);\r\n\r\nlet compactButton = button(\"Compact\", 78, 26);\r\nlet comfortableButton = button(\"Comfortable\", 96, 26);\r\nlet wideButton = button(\"Wide\", 78, 26);\r\nstack(presetRow, compactButton);\r\nstack(presetRow, comfortableButton);\r\nstack(presetRow, wideButton);\r\n\r\n// --- Helpers ---\r\n\r\nfunction registryGetOrDefault(key, fallback) {\r\n    let value = registryGet(key);\r\n    if (value == null) {\r\n        return fallback;\r\n    }\r\n    return value;\r\n}\r\n\r\nfunction selectedResolution() {\r\n    let text = comboText(resolutionCombo);\r\n    let result = object();\r\n    if (text == \"640 x 480\") { objectSet(result, \"width\", 640); objectSet(result, \"height\", 480); return result; }\r\n    if (text == \"800 x 600\") { objectSet(result, \"width\", 800); objectSet(result, \"height\", 600); return result; }\r\n    if (text == \"1024 x 768\") { objectSet(result, \"width\", 1024); objectSet(result, \"height\", 768); return result; }\r\n    if (text == \"1280 x 720\") { objectSet(result, \"width\", 1280); objectSet(result, \"height\", 720); return result; }\r\n    objectSet(result, \"width\", 1920);\r\n    objectSet(result, \"height\", 1080);\r\n    return result;\r\n}\r\n\r\nfunction selectedDepth() {\r\n    let text = comboText(colorDepthCombo);\r\n    if (text == \"4-bit\") { return 4; }\r\n    if (text == \"16-bit\") { return 16; }\r\n    if (text == \"24-bit\") { return 24; }\r\n    return 32;\r\n}\r\n\r\nfunction updateDisplayLabels() {\r\n    let currentWidth = registryGetOrDefault(\"System/Display/CurrentWidth\", 1920);\r\n    let currentHeight = registryGetOrDefault(\"System/Display/CurrentHeight\", 1080);\r\n    let resolution = selectedResolution();\r\n    let requestedWidth = objectGet(resolution, \"width\");\r\n    let requestedHeight = objectGet(resolution, \"height\");\r\n\r\n    set currentModeLabel.text = \"Current display: \" + currentWidth + \" x \" + currentHeight + \", \" + selectedDepth() + \"-bit color\";\r\n\r\n    if (requestedWidth == currentWidth && requestedHeight == currentHeight) {\r\n        set restartNotice.text = \"These settings match the current display.\";\r\n    } else {\r\n        set restartNotice.text = \"Resolution changes will apply after restart.\";\r\n    }\r\n}\r\n\r\nfunction updateGridLabels() {\r\n    set gridWidthValue.text = \"Grid width: \" + value(gridWidthSlider, \"value\") + \" px\";\r\n    set gridHeightValue.text = \"Grid height: \" + value(gridHeightSlider, \"value\") + \" px\";\r\n}\r\n\r\nfunction applySettings() {\r\n    let resolution = selectedResolution();\r\n    registrySet(\"System/Display/Width\", objectGet(resolution, \"width\"));\r\n    registrySet(\"System/Display/Height\", objectGet(resolution, \"height\"));\r\n    registrySet(\"System/Display/BitsPerPixel\", selectedDepth());\r\n    registrySet(\"System/Desktop/IconGridEnabled\", value(snapToGrid, \"checked\"));\r\n    registrySet(\"System/Desktop/IconGridVisible\", value(showGrid, \"checked\"));\r\n    registrySet(\"System/Desktop/IconGridWidth\", value(gridWidthSlider, \"value\"));\r\n    registrySet(\"System/Desktop/IconGridHeight\", value(gridHeightSlider, \"value\"));\r\n    registrySave();\r\n    updateDisplayLabels();\r\n}\r\n\r\nfunction setGridPreset(width, height) {\r\n    sliderValue(gridWidthSlider, width);\r\n    sliderValue(gridHeightSlider, height);\r\n    updateGridLabels();\r\n}\r\n\r\n// --- Events ---\r\n\r\non resolutionCombo.change { updateDisplayLabels(); }\r\non colorDepthCombo.change { updateDisplayLabels(); }\r\non gridWidthSlider.change { updateGridLabels(); }\r\non gridHeightSlider.change { updateGridLabels(); }\r\n\r\non okButton.click { applySettings(); close(win); }\r\non applyButton.click { applySettings(); }\r\non cancelButton.click { close(win); }\r\n\r\non compactButton.click { setGridPreset(80, 76); }\r\non comfortableButton.click { setGridPreset(88, 84); }\r\non wideButton.click { setGridPreset(104, 92); }\r\n\r\n// --- Initial state ---\r\n\r\nlet savedWidth = registryGetOrDefault(\"System/Display/Width\", 1920);\r\nlet savedHeight = registryGetOrDefault(\"System/Display/Height\", 1080);\r\nlet savedDepth = registryGetOrDefault(\"System/Display/BitsPerPixel\", 32);\r\n\r\nset resolutionCombo.text = savedWidth + \" x \" + savedHeight;\r\nset colorDepthCombo.text = savedDepth + \"-bit\";\r\n\r\nset snapToGrid.checked = registryGetOrDefault(\"System/Desktop/IconGridEnabled\", true);\r\nset showGrid.checked = registryGetOrDefault(\"System/Desktop/IconGridVisible\", false);\r\nsliderValue(gridWidthSlider, registryGetOrDefault(\"System/Desktop/IconGridWidth\", 80));\r\nsliderValue(gridHeightSlider, registryGetOrDefault(\"System/Desktop/IconGridHeight\", 80));\r\n\r\nupdateGridLabels();\r\nupdateDisplayLabels();\r\n\r\nshow(win);\r\n\r\n// ---------------------------------------------------------------------\r\n// Translation notes:\r\n//\r\n// 1. No absolute x/y positioning primitive exists in Breeze's GUI\r\n//    functions, so the pixel-perfect GroupBox layout from the C#\r\n//    version became stackPanel/dockPanel composition instead. Visual\r\n//    result will differ (no group borders — \"panel(text, height)\" is\r\n//    used as a plain section header).\r\n//\r\n// 2. No window icon parameter on window(); the original's\r\n//    display_properties.png icon is dropped.\r\n//\r\n// 3. RUNTIME BUG: sliderValue's native implementation requires exactly\r\n//    2 args (RequireCount(name, args, 2)), which makes its read branch\r\n//    (args.Length == 1) dead code — you can never call it as a getter.\r\n//    Reads have to go through value(slider, \"value\") instead. Worth\r\n//    fixing in BreezeRuntime.CallNative if sliderValue is meant to be\r\n//    a getter/setter like the C# version implies.\r\n//\r\n// 4. No SetProperty case for \"selectedIndex\" on ComboBox — there's no\r\n//    way to select a combo item by index from a script. I worked\r\n//    around it by setting the \"text\" property directly to the target\r\n//    item string instead. This relies on the ComboBox implementation\r\n//    treating a text-property write as equivalent to selecting the\r\n//    matching item; if it doesn't, comboSelected()/comboText() will\r\n//    disagree with what's displayed. Might be worth adding a real\r\n//    \"selectedIndex\" setter case to SetProperty.\r\n// ---------------------------------------------------------------------\r\n";
}
