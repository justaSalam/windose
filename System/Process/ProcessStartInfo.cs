public sealed class ProcessStartInfo
{
    public string Name = "";
    public string ExecutablePath = "";
    public string Arguments = "";
    public string WorkingDirectory = "";
    public Func<Process> RestartFactory;

    public bool HasExecutablePath => !string.IsNullOrEmpty(ExecutablePath);
}
