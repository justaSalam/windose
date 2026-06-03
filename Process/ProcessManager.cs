using System.Diagnostics;
using Windose;

public static class ProcessManger
{
    public static List<Process> processes = new();
    private static int processId;

    public static Process Start(Process process)
    {
        try
        {
            process.Start();
            processes.Add(process);
            return process;

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return null;
        }
    }

    public static T? GetProcess<T>() where T : Process
    {
        foreach (Process process in processes)
        {
            if (process is T typed)
                return typed;
        }

        return null;
    }



}