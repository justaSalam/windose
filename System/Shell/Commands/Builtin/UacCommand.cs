using Windose.System.Kernel.Subsystem;

public sealed class UacCommand : InteractiveShellCommand
{
    public override string Name => "uac";

    public override string Description => "Interactive User Account Control";

    public override string Usage => "uac";

    protected override string Prompt => "UAC>";

    protected override SubcommandDispatcher BuildDispatcher()
    {
        return new SubcommandDispatcher(Usage)
            .Add("status", "", 0, Status)
            .Add("create", "<username> <password> <privilege>", 3, Create)
            .Add("login", "<username> <password>", 2, Login)
            .Add("override", "<new password>", 1, Override)
            .Add("elevate", "<password>", 0, Elevate)
            .Add("end", "", 0, End);
    }
   
    
   

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
        if(!Session.TryElevate(args[0]))
        {
            context.WriteLine("Failed to elevate.");
            context.WriteLine("Invalid password.");
            return;
        }

        context.WriteLine("Session Elevated!");


    }

    private void End(CommandContext context, string[] args)
    {
        context.WriteLine("Privileges de-elevated.");
    }

}