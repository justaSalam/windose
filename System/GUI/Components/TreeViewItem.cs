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

    public void Expand() => expanded = true;
    public void Collapse() => expanded = false;
    public void Toggle() => expanded = !expanded;

    public int Level
    {
        get
        {
            int depth = 0;
            for (TreeViewItem c = parent; c != null; c = c.parent) depth++;
            return depth;
        }
    }

    public string FullPath(string separator = "/")
    {
        string path = text ?? "";
        for (TreeViewItem c = parent; c != null; c = c.parent)
            path = (c.text ?? "") + separator + path;
        return path;
    }
}
