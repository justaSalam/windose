public static class ByteFormat
{
    public const long kilo = 1024;
    public const long mega = kilo * 1024;
    public const long giga = mega * 1024;
    public const long tera = giga * 1024;


    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < kilo => $"{bytes} bytes",
            < mega => $"{bytes / kilo:F2} KB",
            < giga => $"{bytes / mega:F2} MB",
            < tera => $"{bytes / giga:F2} GB",
            _ => $"{bytes / tera:F2} TB",
        };
    }

    public static string FormatBytes(byte[] bytes)
    {
        return FormatBytes(bytes.Length);
    }

    public static string FormatBytes(ulong bytes)
    {
        return bytes switch
        {
            < kilo => $"{bytes} bytes",
            < mega => $"{bytes / kilo:F2} KB",
            < giga => $"{bytes / mega:F2} MB",
            < tera => $"{bytes / giga:F2} GB",
            _ => $"{bytes / tera:F2} TB",
        };
    }


}

