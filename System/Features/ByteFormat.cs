public static class ByteFormat
{
    public const long kilo = 1024;
    public const long mega = kilo * 1024;
    public const long giga = mega * 1024;


    public static string FormatBytes(long bytes)
    {
        if (bytes < kilo)
            return $"{bytes} bytes";
        else if (bytes < mega)
            return $"{(bytes / (double)kilo):F2} KB";
        else if (bytes < giga)
            return $"{(bytes / (double)mega):F2} MB";
        else
            return $"{(bytes / (double)giga):F2} GB";
    }

    public static string FormatBytes(byte[] bytes)
    {
        return FormatBytes(bytes.Length);
    }

    public static string FormatBytes(ulong bytes)
    {
        if (bytes < kilo)
            return $"{bytes} B";
        else if (bytes < mega)
            return $"{(bytes / (double)kilo):F2} KB";
        else if (bytes < giga)
            return $"{(bytes / (double)mega):F2} MB";
        else
            return $"{(bytes / (double)giga):F2} GB";
    }


}

