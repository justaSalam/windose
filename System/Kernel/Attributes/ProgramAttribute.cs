
namespace Windose.System.Kernel.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class ProgramAttribute : Attribute
    {
        public string Id { get; }
        public string Name { get; }

        public ProgramAttribute(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
