public static class WindoseClipboard
{
    private const int MaximumTextLength = 65536;
    private static string text = "";

    public static string Text => text;
    public static bool HasText => text.Length > 0;

    public static void SetText(string value)
    {
        value ??= "";
        text = value.Length > MaximumTextLength
            ? value.Substring(0, MaximumTextLength)
            : value;
    }

    public static void Clear()
    {
        text = "";
    }
}
