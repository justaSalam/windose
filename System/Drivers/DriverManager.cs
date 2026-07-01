using System;
using System.Collections.Generic;
using Cosmos.Kernel.Core.IO;

namespace Windose.Drivers;

public static class DriverManager
{
    private static readonly List<IWindoseDriver> drivers = new();

    public static IReadOnlyList<IWindoseDriver> Drivers => drivers;

    public static void Register(IWindoseDriver driver)
    {
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
            driver.Start();
            Serial.WriteString("Driver started: " + driver.Name + "\n");
        }
        catch (Exception exception)
        {
            Serial.WriteString("Driver failed: " + driver.Name + " - " + exception.Message + "\n");
            global::KernelPanic.Show("DRIVER_START_FAILURE", exception);
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
