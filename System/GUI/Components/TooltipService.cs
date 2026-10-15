// TooltipService.cs - one shared hover-tooltip for the whole shell. Attach any component;
// a single hidden Tooltip root serves them all (it already updates while invisible).
public static class TooltipService
{
    private static Tooltip instance;

    public static void Attach(Component component, string text)
    {
        if (component == null || string.IsNullOrEmpty(text)) return;
        if (instance == null) instance = new Tooltip();
        instance.AttachTo(component, text);
    }

    public static void Clear()
    {
        if (instance != null) instance.Detach();
    }
}
