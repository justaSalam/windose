using Cosmos.Kernel.System.Graphics;

public class ListViewItem
{
    public string text;
    public Bitmap icon;
    public object tag;
    public bool selected;

    public string size = "";
    public string type = "";
    public string modified = "";
    public bool isFolder;

    public ListViewItem(string text, Bitmap icon = null, object tag = null)
    {
        this.text = text;
        this.icon = icon;
        this.tag = tag;
    }
}
