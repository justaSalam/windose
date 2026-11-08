using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Windose.System.Resources
{
    /// <summary>
    /// Pure managed C# reader for Portable Executable (PE32 / PE32+) resource directories (.rsrc).
    /// Extracts RT_GROUP_ICON and RT_ICON resources without native Win32 dependencies.
    /// </summary>
    public class PeResourceReader : IDisposable
    {
        private Stream stream;
        private BinaryReader reader;
        private bool leaveOpen;

        private uint peHeaderOffset;
        private bool is64Bit;
        private uint resourceRva;
        private uint resourceSize;
        private uint resourceFileOffset;
        private readonly List<PeSectionHeader> sections = new List<PeSectionHeader>();

        public bool IsValid { get; private set; }

        public PeResourceReader(Stream peStream, bool leaveOpen = false)
        {
            this.stream = peStream ?? throw new ArgumentNullException(nameof(peStream));
            this.reader = new BinaryReader(peStream, Encoding.UTF8, leaveOpen);
            this.leaveOpen = leaveOpen;
            this.IsValid = ParseHeaders();
        }

        public static PeResourceReader? Open(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            try
            {
                FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var reader = new PeResourceReader(fs, leaveOpen: false);
                if (reader.IsValid)
                    return reader;

                reader.Dispose();
                return null;
            }
            catch
            {
                return null;
            }
        }

        public static PeResourceReader? Open(byte[] peBytes)
        {
            if (peBytes == null || peBytes.Length < 64)
                return null;

            try
            {
                MemoryStream ms = new MemoryStream(peBytes, writable: false);
                var reader = new PeResourceReader(ms, leaveOpen: false);
                if (reader.IsValid)
                    return reader;

                reader.Dispose();
                return null;
            }
            catch
            {
                return null;
            }
        }

        private bool ParseHeaders()
        {
            try
            {
                if (stream.Length < 64)
                    return false;

                stream.Position = 0;
                ushort dosMagic = reader.ReadUInt16();
                if (dosMagic != 0x5A4D) // "MZ"
                    return false;

                stream.Position = 0x3C;
                peHeaderOffset = reader.ReadUInt32();
                if (peHeaderOffset + 4 + 20 > stream.Length)
                    return false;

                stream.Position = peHeaderOffset;
                uint peSig = reader.ReadUInt32();
                if (peSig != 0x00004550) // "PE\0\0"
                    return false;

                // File Header (20 bytes)
                ushort machine = reader.ReadUInt16();
                ushort numberOfSections = reader.ReadUInt16();
                stream.Position += 12; // Skip TimeDateStamp (4), PointerToSymbolTable (4), NumberOfSymbols (4)
                ushort sizeOfOptionalHeader = reader.ReadUInt16();
                ushort characteristics = reader.ReadUInt16();

                if (sizeOfOptionalHeader == 0)
                    return false;

                long optionalHeaderStart = stream.Position;
                ushort optMagic = reader.ReadUInt16();
                is64Bit = (optMagic == 0x20B);

                // Locate DataDirectory[2] (Resource Table)
                int rsrcDirectoryOffset = is64Bit ? 128 : 112;
                if (sizeOfOptionalHeader < rsrcDirectoryOffset + 8)
                    return false;

                stream.Position = optionalHeaderStart + rsrcDirectoryOffset;
                resourceRva = reader.ReadUInt32();
                resourceSize = reader.ReadUInt32();

                if (resourceRva == 0 || resourceSize == 0)
                    return false;

                // Read Section Headers
                long sectionTableOffset = optionalHeaderStart + sizeOfOptionalHeader;
                stream.Position = sectionTableOffset;

                sections.Clear();
                for (int i = 0; i < numberOfSections; i++)
                {
                    byte[] nameBytes = reader.ReadBytes(8);
                    string secName = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0');

                    uint virtualSize = reader.ReadUInt32();
                    uint virtualAddress = reader.ReadUInt32();
                    uint sizeOfRawData = reader.ReadUInt32();
                    uint pointerToRawData = reader.ReadUInt32();

                    stream.Position += 16; // Skip relocs, line numbers, characteristics

                    sections.Add(new PeSectionHeader
                    {
                        Name = secName,
                        VirtualSize = virtualSize,
                        VirtualAddress = virtualAddress,
                        SizeOfRawData = sizeOfRawData,
                        PointerToRawData = pointerToRawData
                    });
                }

                resourceFileOffset = RvaToFileOffset(resourceRva);
                return (resourceFileOffset > 0);
            }
            catch
            {
                return false;
            }
        }

        public uint RvaToFileOffset(uint rva)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                PeSectionHeader sec = sections[i];
                uint span = Math.Max(sec.VirtualSize, sec.SizeOfRawData);
                if (rva >= sec.VirtualAddress && rva < sec.VirtualAddress + span)
                {
                    return (rva - sec.VirtualAddress) + sec.PointerToRawData;
                }
            }
            return 0;
        }

        public List<int> GetGroupIconIds()
        {
            var result = new List<int>();
            if (!IsValid) return result;

            try
            {
                // Level 1: Find Type 14 (RT_GROUP_ICON)
                List<ResourceDirectoryEntry> rootEntries = ReadDirectory(resourceFileOffset);
                ResourceDirectoryEntry? groupTypeEntry = null;

                for (int i = 0; i < rootEntries.Count; i++)
                {
                    if (!rootEntries[i].IsNamed && rootEntries[i].NameOrId == (uint)PeResourceType.GroupIcon)
                    {
                        groupTypeEntry = rootEntries[i];
                        break;
                    }
                }

                if (groupTypeEntry == null || !groupTypeEntry.Value.IsSubdirectory)
                    return result;

                // Level 2: Group IDs
                uint level2Offset = resourceFileOffset + groupTypeEntry.Value.Offset;
                List<ResourceDirectoryEntry> idEntries = ReadDirectory(level2Offset);

                for (int i = 0; i < idEntries.Count; i++)
                {
                    if (!idEntries[i].IsNamed)
                    {
                        result.Add((int)idEntries[i].NameOrId);
                    }
                }
            }
            catch { }

            return result;
        }

        public List<GrpIconDirEntry> GetGroupIconFrames(int groupId)
        {
            var frames = new List<GrpIconDirEntry>();
            byte[]? raw = GetResourceData((int)PeResourceType.GroupIcon, groupId);
            if (raw == null || raw.Length < 6)
                return frames;

            try
            {
                using var ms = new MemoryStream(raw, writable: false);
                using var br = new BinaryReader(ms);

                ushort reserved = br.ReadUInt16();
                ushort type = br.ReadUInt16();
                ushort count = br.ReadUInt16();

                if (type != 1) return frames;

                for (int i = 0; i < count; i++)
                {
                    if (ms.Position + 14 > ms.Length)
                        break;

                    frames.Add(new GrpIconDirEntry
                    {
                        Width = br.ReadByte(),
                        Height = br.ReadByte(),
                        ColorCount = br.ReadByte(),
                        Reserved = br.ReadByte(),
                        Planes = br.ReadUInt16(),
                        BitCount = br.ReadUInt16(),
                        BytesInRes = br.ReadUInt32(),
                        Id = br.ReadUInt16()
                    });
                }
            }
            catch { }

            return frames;
        }

        public byte[]? GetIconData(int iconId)
        {
            return GetResourceData((int)PeResourceType.Icon, iconId);
        }

        public byte[]? GetResourceData(int typeId, int nameId)
        {
            if (!IsValid) return null;

            try
            {
                // Level 1: Resource Type
                List<ResourceDirectoryEntry> rootEntries = ReadDirectory(resourceFileOffset);
                ResourceDirectoryEntry? typeEntry = null;

                for (int i = 0; i < rootEntries.Count; i++)
                {
                    if (!rootEntries[i].IsNamed && rootEntries[i].NameOrId == (uint)typeId)
                    {
                        typeEntry = rootEntries[i];
                        break;
                    }
                }

                if (typeEntry == null || !typeEntry.Value.IsSubdirectory)
                    return null;

                // Level 2: Resource Name / ID
                uint level2Offset = resourceFileOffset + typeEntry.Value.Offset;
                List<ResourceDirectoryEntry> nameEntries = ReadDirectory(level2Offset);
                ResourceDirectoryEntry? idEntry = null;

                for (int i = 0; i < nameEntries.Count; i++)
                {
                    if (!nameEntries[i].IsNamed && nameEntries[i].NameOrId == (uint)nameId)
                    {
                        idEntry = nameEntries[i];
                        break;
                    }
                }

                if (idEntry == null || !idEntry.Value.IsSubdirectory)
                    return null;

                // Level 3: Language (choose first available language entry)
                uint level3Offset = resourceFileOffset + idEntry.Value.Offset;
                List<ResourceDirectoryEntry> langEntries = ReadDirectory(level3Offset);
                if (langEntries.Count == 0 || langEntries[0].IsSubdirectory)
                    return null;

                // Leaf: IMAGE_RESOURCE_DATA_ENTRY
                uint dataEntryOffset = resourceFileOffset + langEntries[0].Offset;
                stream.Position = dataEntryOffset;

                uint dataRva = reader.ReadUInt32();
                uint dataSize = reader.ReadUInt32();

                if (dataRva == 0 || dataSize == 0 || dataSize > 16 * 1024 * 1024)
                    return null;

                uint fileOffset = RvaToFileOffset(dataRva);
                if (fileOffset == 0 || fileOffset + dataSize > stream.Length)
                    return null;

                stream.Position = fileOffset;
                return reader.ReadBytes((int)dataSize);
            }
            catch
            {
                return null;
            }
        }

        private List<ResourceDirectoryEntry> ReadDirectory(uint directoryOffset)
        {
            var entries = new List<ResourceDirectoryEntry>();
            if (directoryOffset + 16 > stream.Length)
                return entries;

            stream.Position = directoryOffset;
            uint characteristics = reader.ReadUInt32();
            uint timeDateStamp = reader.ReadUInt32();
            ushort majorVer = reader.ReadUInt16();
            ushort minorVer = reader.ReadUInt16();
            ushort numNamed = reader.ReadUInt16();
            ushort numId = reader.ReadUInt16();

            int total = numNamed + numId;
            for (int i = 0; i < total; i++)
            {
                if (stream.Position + 8 > stream.Length)
                    break;

                uint nameOrId = reader.ReadUInt32();
                uint offsetToData = reader.ReadUInt32();

                bool isNamed = (nameOrId & 0x80000000) != 0;
                bool isSubdir = (offsetToData & 0x80000000) != 0;

                entries.Add(new ResourceDirectoryEntry
                {
                    NameOrId = isNamed ? 0 : nameOrId,
                    IsNamed = isNamed,
                    Name = null,
                    IsSubdirectory = isSubdir,
                    Offset = offsetToData & 0x7FFFFFFF
                });
            }

            return entries;
        }

        public void Dispose()
        {
            if (!leaveOpen)
            {
                reader?.Dispose();
                stream?.Dispose();
            }
        }
    }
}
