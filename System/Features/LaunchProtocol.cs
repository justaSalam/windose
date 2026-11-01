using System;
using System.Collections.Generic;
using System.Text;
using Windose.System.Elf;

namespace Windose.System.Features
{
    public static class LaunchProtocol
    {
        private const string startup = "System/Startup";
        public static async Task Run()
        {
            RegistryEntry[] entries = Registry.GetEntries(startup);

            foreach (RegistryEntry entry in entries)
            {
                string name = entry.Key;
                string[] arguments = Registry.GetString($"{startup}/{name}/Arguments", "").Split(' ');


                if (File.Exists((string)entry.Value) == false)
                {
                    continue;
                }

                byte[] programData = await File.ReadAllBytesAsync((string)entry.Value);
                ElfProgram program = ElfRunner.Spawn(programData, name, arguments, out string error);

                if (program == null)
                {
                    continue;
                }
            }
        }
    }
}
