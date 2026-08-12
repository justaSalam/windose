using Cosmos.Kernel.System.Graphics;

public class ListViewItem
{
    public string text;
    public Image icon;
    public object tag;
    public FileEntry fileEntry;
    public bool hasFileEntry;
    public bool selected;

    public string size = "";
    public string type = "";
    public string modified = "";
    public bool isFolder;


    public ListViewItem(string text, Image icon = null, object tag = null)
    {
        this.text = text;
        this.icon = icon;
        this.tag = tag;
    }

    public ListViewItem(string text, object tag = null)
    {
        this.text = text;
        this.tag = tag;
    }

    public ListViewItem(FileEntry fileEntry, Image icon = null)
    {
        this.fileEntry = fileEntry;
        this.icon = icon;
        hasFileEntry = true;

        text = fileEntry.FileName;
        tag = fileEntry.AbsoluteLocation;
        isFolder = fileEntry.FileType == FileType.Directory;

        size = isFolder ? "" : FormatSize(fileEntry.SizeBytes);
        type = isFolder ? "File Folder" : fileEntry.FileType.ToString();

        modified = fileEntry.CreatedAt;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return bytes + " B";

        return bytes / 1024 + " KB";
    }
}
