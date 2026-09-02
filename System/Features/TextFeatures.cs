
namespace Windose.System.Features
{
    public static class TextFeatures
    {
        public static string Truncate(string text, int maxLength, TruncateMode mode = TruncateMode.Overflow)
        {
            if (text.Length <= maxLength)
            {
                return text;
            }
            switch (mode)
            {
                case TruncateMode.Truncate:
                    return text.Substring(0, maxLength);
                case TruncateMode.Ellipsis:
                    return text.Substring(0, maxLength - 3) + "...";
                default:
                    return text; // Overflow mode, return the original text
            }
        }

        /// <summary>
        /// Fills the text to a specified total length with a specified character. If the text is longer than the total length, it will be truncated based on the specified truncate mode.
        /// </summary>
        public static string Fill(string text, int totalLength, char fillChar = ' ', TruncateMode truncateMode = TruncateMode.Ellipsis)
        {
            if (text.Length >= totalLength)
            {
                return Truncate(text, totalLength, truncateMode);
            }
            return text.PadRight(totalLength, fillChar);
        }
    }


    public enum TruncateMode
    {
        Overflow, // Default behavior, text will overflow the container
        Truncate, // Text will be truncated to fit the container
        Ellipsis, // Text will be truncated and an ellipsis will be added to the end


    }
}
