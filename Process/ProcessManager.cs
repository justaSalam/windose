using System.Diagnostics;
using Windose;

public static class ProcessManger
{
    public static Dictionary<int, Process> processes = new Dictionary<int, Process>();
    private static int processId;

    public static Process Start(Process process)
    {
        try
        {
            process.Start(processId);
            processes.Add(processId, process);
            processId++;
            return process;

        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            return null;
        }
    }

    public static void Update()
    {
        foreach (KeyValuePair<int, Process> process in processes)
        {
            process.Value.Main();
        }
    }

    public static List<Process> GetChildren(int parentPid)
    {
        List<Process> children = new();

        foreach (var process in processes)
        {
            if (process.Key == parentPid)
                children.Add(process.Value);
        }

        return children;
    }

    public static T? GetProcess<T>() where T : Process
    {
        foreach (Process process in processes.Values)
        {
            if (process is T typed)
                return typed;
        }

        return null;
    }



}