using System;

public static class KernelArguments
{
    public static bool Has(string name)
    {
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg == name)
                return true;
        }

        return false;
    }

    public static string? GetValue(string name)
    {
        string prefix = name + "=";

        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith(prefix, StringComparison.Ordinal))
                return arg.Substring(prefix.Length);
        }

        return null;
    }
}