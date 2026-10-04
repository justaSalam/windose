using Cosmos.Kernel.System.Graphics;

public class ListViewItem
{
    public string[] Text { get; }
    public Image? Icon { get; set; }
    public object? Tag { get; set; }

    public ListViewItem(string[] text, Image? icon = null, object? tag = null)
    {
        Text = text ?? Array.Empty<string>();
        Icon = icon;
        Tag = tag;
    }
}
