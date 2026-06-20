public class MenuPage : MenuPopup
{
    public MenuPage(int width = 180) : base(width, 24)
    {
    }

    public MenuItem AddCommand(string text, string command, Component target = null, object data = null)
    {
        return AddItem(text, () => WindowManager.PostCommand(command, target: target, data: data));
    }

    public override string GetName() => "MenuPage";
}
