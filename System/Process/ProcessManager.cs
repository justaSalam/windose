
public static class ProcessManger
{
    public static List<Process> processes = new();
    private static int processId;

    public static Process Start(Process process)
    {
        process.id = processId;
        process.Start();
        processes.Add(process);

        processId++;
        return process;
    }

    public static void Update()
    {
        foreach (Process process in processes)
        {
            process.Main();
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