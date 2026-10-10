using Cosmos.Kernel.System.Graphics;

public class TreeViewItem
{

    public Image image;
    public string text;
    public object tag;
    public TreeViewItem parent;
    public List<TreeViewItem> children = new List<TreeViewItem>();
    public bool expanded = true;
    public bool selected;

    public Action onRightClick;

    public TreeViewItem(string text, object tag = null)
    {
        this.text = text;
        this.tag = tag;
    }

    public TreeViewItem AddChild(string text, object tag = null)
    {
        TreeViewItem item = new TreeViewItem(text, tag)
        {
            parent = this
        };

        children.Add(item);
        return item;
    }

    public TreeViewItem AddChild(string text, string icon, object tag = null)
    {
        TreeViewItem item = new TreeViewItem(text, tag)
        {
            parent = this,
            image = new Png(icon)
        };

        children.Add(item);
        return item;
    }

    public bool HasChildren()
    {
        return children.Count > 0;
    }
}
