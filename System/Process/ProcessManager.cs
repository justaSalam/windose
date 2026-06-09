
public static class ProcessManger
{
    public static List<SingleThreadedProcess> processes = new();
    private static int processId;

    public static SingleThreadedProcess Start(SingleThreadedProcess process)
    {
        process.id = processId;
        process.Start();
        processes.Add(process);

        processId++;
        return process;
    }

    public static void Update()
    {
        foreach (SingleThreadedProcess process in processes)
        {
            process.Main();
        }
    }

    public static T? GetProcess<T>() where T : SingleThreadedProcess
    {
        foreach (SingleThreadedProcess process in processes)
        {
            if (process is T typed)
                return typed;
        }

        return null;
    }

}