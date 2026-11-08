using System;
using System.Runtime.InteropServices;

namespace Windose.System.Resources
{
    public enum PeResourceType : int
    {
        Cursor = 1,
        Bitmap = 2,
        Icon = 3,           // RT_ICON
        Menu = 4,
        Dialog = 5,
        String = 6,
        FontDir = 7,
        Font = 8,
        Accelerator = 9,
        RcData = 10,
        MessageTable = 11,
        GroupCursor = 12,
        GroupIcon = 14,     // RT_GROUP_ICON
        Version = 16,
        Manifest = 24
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PeSectionHeader
    {
        public string Name;
        public uint VirtualSize;
        public uint VirtualAddress;
        public uint SizeOfRawData;
        public uint PointerToRawData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct GrpIconDirEntry
    {
        public byte Width;          // 0 means 256
        public byte Height;         // 0 means 256
        public byte ColorCount;
        public byte Reserved;
        public ushort Planes;
        public ushort BitCount;
        public uint BytesInRes;
        public ushort Id;           // Corresponds to RT_ICON resource ID
    }

    public struct ResourceDirectoryEntry
    {
        public uint NameOrId;
        public bool IsNamed;
        public string? Name;
        public bool IsSubdirectory;
        public uint Offset; // Relative to resource root
    }

    public struct ResourceDataEntry
    {
        public uint DataRva;
        public uint Size;
        public uint CodePage;
        public uint Reserved;
    }
}
