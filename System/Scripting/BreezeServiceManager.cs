public sealed class BreezeServiceHandle
{
    internal BreezeRuntime Runtime;
    internal Process Process;
    internal readonly List<string> Dependencies = new List<string>();

    public string Name { get; internal set; }
    public bool RestartOnFailure { get; internal set; }
    public bool Protected { get; internal set; }
    public string State => Runtime == null || Runtime.IsTerminated ? "stopped" : "running";
    internal long StartedAt;
}

public static class BreezeServiceManager
{
    private static readonly Dictionary<string, BreezeServiceHandle> services =
        new Dictionary<string, BreezeServiceHandle>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> restartAttempts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static readonly object sync = new object();

    public static BreezeServiceHandle Register(BreezeRuntime runtime, Process process, string name,
        bool restartOnFailure, bool protectedService)
    {
        if (runtime == null || process == null || string.IsNullOrWhiteSpace(name)) return null;
        lock (sync)
        {
            if (services.TryGetValue(name, out BreezeServiceHandle existing) && existing.Runtime != runtime && existing.State == "running")
                return null;

            BreezeServiceHandle service = new BreezeServiceHandle
            {
                Runtime = runtime,
                Process = process,
                Name = name,
                RestartOnFailure = restartOnFailure,
                Protected = protectedService,
                StartedAt = DateTime.UtcNow.Ticks,
            };
            services[name] = service;
            process.canTerminate = !protectedService;
            return service;
        }
    }

    public static bool AddDependency(BreezeServiceHandle service, string dependency)
    {
        if (service == null || string.IsNullOrWhiteSpace(dependency)) return false;
        lock (sync)
        {
            if (!services.TryGetValue(service.Name, out BreezeServiceHandle registered) || registered != service) return false;
            for (int i = 0; i < service.Dependencies.Count; i++)
                if (string.Equals(service.Dependencies[i], dependency, StringComparison.OrdinalIgnoreCase)) return true;
            service.Dependencies.Add(dependency);
            return true;
        }
    }

    public static bool DependenciesReady(BreezeServiceHandle service)
    {
        if (service == null) return false;
        lock (sync)
        {
            for (int i = 0; i < service.Dependencies.Count; i++)
            {
                if (!services.TryGetValue(service.Dependencies[i], out BreezeServiceHandle dependency) || dependency.State != "running")
                    return false;
            }
            return true;
        }
    }

    public static BreezeServiceHandle Find(string name)
    {
        lock (sync)
        {
            if (services.TryGetValue(name ?? "", out BreezeServiceHandle service) && service.State == "running") return service;
            return null;
        }
    }

    public static string GetState(string name)
    {
        lock (sync)
        {
            return services.TryGetValue(name ?? "", out BreezeServiceHandle service) ? service.State : "missing";
        }
    }

    public static bool Stop(string name)
    {
        BreezeServiceHandle service = Find(name);
        if (service == null || service.Protected) return false;
        ProcessManger.QueueStop(service.Process);
        return true;
    }

    public static bool Restart(string name)
    {
        BreezeServiceHandle service = Find(name);
        if (service == null || service.Process == null || !service.Process.CanRestart) return false;
        lock (sync) restartAttempts.Remove(service.Name);
        ProcessManger.QueueRestart(service.Process, true);
        return true;
    }

    public static bool StartFile(string path)
    {
        IWindoseFileSystem fileSystem = FileSystemManager.Current;
        if (fileSystem == null || !fileSystem.TryReadAllText(path, out string source)) return false;
        ProcessManger.QueueStart(new BreezeScheduledApplicationProcess(source, path));
        return true;
    }

    public static void NotifyStopped(BreezeRuntime runtime, bool failed)
    {
        BreezeServiceHandle stopped = null;
        lock (sync)
        {
            foreach (BreezeServiceHandle service in services.Values)
            {
                if (service.Runtime != runtime) continue;
                stopped = service;
                break;
            }
            if (stopped != null) services.Remove(stopped.Name);
        }

        if (stopped != null && failed && stopped.RestartOnFailure && stopped.Process?.startInfo?.RestartFactory != null)
        {
            int attempts;
            lock (sync)
            {
                if (DateTime.UtcNow.Ticks - stopped.StartedAt > 30 * TimeSpan.TicksPerSecond)
                    restartAttempts.Remove(stopped.Name);
                restartAttempts.TryGetValue(stopped.Name, out attempts);
                attempts++;
                restartAttempts[stopped.Name] = attempts;
            }
            if (attempts > 3) return;

            try
            {
                Process replacement = stopped.Process.startInfo.RestartFactory();
                if (replacement != null) ProcessManger.QueueStart(replacement);
            }
            catch (Exception exception)
            {
                Cosmos.Kernel.Core.IO.Serial.WriteString("Could not restart service " + stopped.Name + ": " + exception.Message + "\n");
            }
        }
    }
}
