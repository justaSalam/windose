using Cosmos.Kernel.Core.IO;
using Cosmos.Kernel.Core.CPU;
using Cosmos.Kernel.Core.X64.Bridge;
using Cosmos.Kernel.HAL;
using Cosmos.Kernel.System;

public enum SystemPowerRequest
{
    None,
    Shutdown,
    Reboot,
}

public static class SystemPowerManager
{
    private static volatile SystemPowerRequest pendingRequest;

    public static void RequestShutdown() => pendingRequest = SystemPowerRequest.Shutdown;
    public static void RequestReboot() => pendingRequest = SystemPowerRequest.Reboot;

    public static void ExecutePending()
    {
        SystemPowerRequest request = pendingRequest;
        if (request == SystemPowerRequest.None) return;
        pendingRequest = SystemPowerRequest.None;

        if (PlatformHAL.PowerOps == null)
        {
            Serial.WriteString("Power request failed: PlatformHAL.PowerOps is unavailable\n");
            BreezeHost.ShowError("Power operations are unavailable on this platform");
            return;
        }

        Serial.WriteString(request == SystemPowerRequest.Shutdown
            ? "Executing system shutdown\n"
            : "Executing system reboot\n");

        if (PlatformHAL.PlatformName == "x86-64")
        {
            InternalCpu.DisableInterrupts();
            if (request == SystemPowerRequest.Shutdown) TryEmulatorShutdown();
            else TryKeyboardControllerReboot();
        }

        if (request == SystemPowerRequest.Shutdown) Power.Shutdown();
        else Power.Reboot();
    }

    private static void TryEmulatorShutdown()
    {
        Serial.WriteString("Trying emulator shutdown ports\n");
        PlatformHAL.PortIO.WriteWord(0x604, 0x2000);
        PlatformHAL.PortIO.WriteWord(0xB004, 0x2000);
        PlatformHAL.PortIO.WriteWord(0x4004, 0x3400);
    }

    private static void TryKeyboardControllerReboot()
    {
        Serial.WriteString("Trying 8042 keyboard-controller reset\n");
        const ushort statusPort = 0x64;
        const byte inputBufferFull = 0x02;

        for (int i = 0; i < 0x100; i++)
        {
            if ((PlatformHAL.PortIO.ReadByte(statusPort) & inputBufferFull) == 0)
                break;
        }

        PlatformHAL.PortIO.WriteByte(statusPort, 0xFE);
        for (int i = 0; i < 0x1000; i++)
            PlatformHAL.PortIO.ReadByte(statusPort);

        Serial.WriteString("8042 reset returned; forcing x64 triple-fault reset\n");
        X64PowerNative.TripleFault();
        while (true) InternalCpu.Halt();
    }
}
