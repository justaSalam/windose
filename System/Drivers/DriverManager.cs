using System;
using System.Collections.Generic;
using Cosmos.Kernel.Core.IO;
using Windose.System.System_Calls;

namespace Windose.Drivers;

public static class DriverManager
{
    private static readonly List<IWindoseDriver> drivers = new();

    public static IReadOnlyList<IWindoseDriver> Drivers => drivers;

    public static void Register(IWindoseDriver driver)
    {
        ConsoleMessage.WriteLine("DM", "Registered driver: " + driver.Name, ConsoleMessageType.Log);

        if (driver == null) return;
        drivers.Add(driver);
    }

    public static T Get<T>() where T : class, IWindoseDriver
    {
        for (int i = 0; i < drivers.Count; i++)
        {
            if (drivers[i] is T driver) return driver;
        }

        return null;
    }

    public static void StartAll()
    {
        for (int i = 0; i < drivers.Count; i++)
        {
            Start(drivers[i]);
        }
    }

    public static void Start(IWindoseDriver driver)
    {
        if (driver == null || driver.State == WindoseDriverState.Started) return;

        try
        {
            ConsoleMessage.WriteLine("DM", "Starting driver: " + driver.Name, ConsoleMessageType.Log);


            driver.Start();
        }
        catch (Exception exception)
        {
            ConsoleMessage.WriteLine("DM", "Failed to start driver: " + driver.Name, ConsoleMessageType.Log);

            KernelPanic.Show("DRIVER_START_FAILURE", exception);
        }
    }

    public static void StopAll()
    {
        for (int i = drivers.Count - 1; i >= 0; i--)
        {
            try
            {
                drivers[i].Stop();
            }
            catch
            {
            }
        }
    }
}
