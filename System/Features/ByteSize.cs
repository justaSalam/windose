public static class ByteSize
{
    public const long Kilobyte = 1024;
    public const long Megabyte = Kilobyte * 1024;
    public const long Gigabyte = Megabyte * 1024;
    public const long Terabyte = Gigabyte * 1024;



    public static string Format(byte[] bytes)
    {
        return Format(bytes.Length);
    }
    public static string Format(long bytes)
    {
        return bytes switch
        {
            < Kilobyte => $"{bytes} bytes",
            < Megabyte => $"{bytes / Kilobyte:F2} KB",
            < Gigabyte => $"{bytes / Megabyte:F2} MB",
            < Terabyte => $"{bytes / Gigabyte:F2} GB",
            _ => $"{bytes / Terabyte:F2} TB",
        };
    }

    public static string Format(ulong bytes)
    {
        return bytes switch
        {
            < Kilobyte => $"{bytes} bytes",
            < Megabyte => $"{bytes / Kilobyte:F2} KB",
            < Gigabyte => $"{bytes / Megabyte:F2} MB",
            < Terabyte => $"{bytes / Gigabyte:F2} GB",
            _ => $"{bytes / Terabyte:F2} TB",
        };
    }


}

