namespace Windose.System.Kernel.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class FileAssociationAttribute : Attribute
    {
        public string Extension { get; }
        public FileAssociationAttribute(string ext)
        {
            Extension = ext.StartsWith('.') ? ext.ToLowerInvariant() : $".{ext.ToLowerInvariant()}";
        }
    }
}
