using Cosmos.Kernel.System.Graphics;

public class FileListViewItem
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


    public FileListViewItem(string text, Image icon = null, object tag = null)
    {
        this.text = text;
        this.icon = icon;
        this.tag = tag;
    }

    public FileListViewItem(string text, object tag = null)
    {
        this.text = text;
        this.tag = tag;
    }

    public FileListViewItem(FileEntry fileEntry, Image icon = null)
    {
        this.fileEntry = fileEntry;
        this.icon = icon;
        hasFileEntry = true;

        text = fileEntry.FileName;
        tag = fileEntry.AbsoluteLocation;
        isFolder = fileEntry.FileType == FileType.Directory;

        size = isFolder ? "" : ByteSize.Format(fileEntry.SizeBytes);
        type = isFolder ? "File Folder" : fileEntry.FileType.ToString();

        modified = fileEntry.CreatedAt;
    }
}
