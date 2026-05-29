using System.Diagnostics;
using Windose;

public static class ProcessManger
{
    public static Dictionary<int, Process> processes = new Dictionary<int, Process>();
    private static int processId;

    public static Process Start(Process process)
    {
        process.id = processId;
        process.startTime = DateTime.Now.ToString("HH:mm:ss");

        process.Start();
        processes.Add(processId, process);
        processId++;
        return process;
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



    public static void Tick()
    {
        foreach (KeyValuePair<int, Process> _process in processes)
        {
            Process process = _process.Value;

            process.Tick();

        }
    }



}