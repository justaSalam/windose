public class MenuPage : MenuPopup
{
    public MenuPage(int width = 180) : base(width, 24)
    {
    }

    public MenuItem AddCommand(string text, Action action)
    {
        return AddItem(text, action);
    }

    public override string GetComponentName() => "MenuPage";
}
