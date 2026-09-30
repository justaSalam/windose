
using System.Reflection;
using Windose.System.Kernel.Attributes;
using static System.Net.Mime.MediaTypeNames;

namespace Windose.System.Kernel.Subsystem
{
    public static class FileAssociations
    {
        private static readonly Dictionary<string, Type> associations = new();


        public static void Register(string extension, Type applicationType)
        {
            extension = Normalize(extension);

            associations[extension] = applicationType;
        }

        public static bool TryGetApplication(string extension,out Type applicationType)
        {
            return associations.TryGetValue(Normalize(extension),out applicationType!);
        }

        private static string Normalize(string extension)
        {
            if (!extension.StartsWith("."))
            {
                extension = "." + extension;
            }

            return extension.ToLowerInvariant();
        }
        public static void DiscoverPrograms(Assembly assembly)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (!typeof(Application).IsAssignableFrom(type))
                {
                    continue;
                }

                IEnumerable<FileAssociationAttribute> associations =type.GetCustomAttributes<FileAssociationAttribute>();

                foreach (var association in associations)
                {
                    Register(association.Extension,type);
                }
            }
        }
    }
}
