public static class ComponentExtensions
{
    public static T With<T>(this T component, Action<T> configure) where T : Component
    {
        configure(component);
        return component;
    }
}
