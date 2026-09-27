using Windose.System.Kernel.Subsystem;

public sealed class UacCommand : IShellCommand
{
    public string Name => "uac";
    public string Description => "User Access Control Settings";
    public string Usage => "uac [command]";

    private readonly SubcommandDispatcher dispatcher;

    public UacCommand()
    {
        dispatcher = new SubcommandDispatcher(Usage)
            .Add("status", "", 0, Status)
            .Add("create", "<username> <password> <privilege>", 3, Create)
            .Add("login", "<username> <password>", 2, Login)
            .Add("override", "<new password>", 1, Override)
            .Add("elevate", "", 0, Elevate)
            .Add("end", "", 0, End);
    }

    public void Execute(CommandContext context, string[] args) => dispatcher.Dispatch(context, args);

    private void Status(CommandContext context, string[] args)
    {
        context.WriteLine($"Current User: {Session.CurrentUser?.username ?? "None"}");
        context.WriteLine($"Privilege Level: {Session.CurrentUser?.privilege.ToString() ?? "None"}");
        context.WriteLine($"Elevated: {Session.isElevated}");
    }

    private void Create(CommandContext context, string[] args)
    {
        UserAccount ?user = UserAccount.CreateAccount(args[0], args[1], Session.ParsePrivilege(args[2]));

        context.WriteLine($"Created user: {user.username} with privilege level: {user.privilege}");
        context.WriteLine($"Password: {user.password}");
    }

    private void Login(CommandContext context, string[] args)
    {
        context.WriteLine(Session.LogIn(args[0], args[1])
            ? $"Logged in as {args[0]}"
            : "Failed to log in");
    }

    private void Override(CommandContext context, string[] args)
    {
        UserAccount? current = Session.CurrentUser;
        if (current == null)
        {
            context.WriteLine("No user is currently logged in.");
            return;
        }
        current.ChangePassword(args[0]);
    }

    private void Elevate(CommandContext context, string[] args)
    {
        if (Session.CurrentUser == null)
        {
            context.WriteLine("No user is currently logged in.");
            return;
        }
        context.WriteLine("Elevate not implemented yet.");
        // TODO: context.ReadLine() for password prompt
    }

    private void End(CommandContext context, string[] args)
    {
        context.WriteLine("Privileges de-elevated.");
    }
}