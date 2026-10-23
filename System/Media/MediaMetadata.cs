using System;
using System.IO;

namespace Windose.System.Media
{
    public enum MediaType
    {
        Unknown,
        Video,
        Audio,
        Animation
    }

    /// <summary>
    /// Metadata descriptors for media loaded in Windose Media Player.
    /// </summary>
    public class MediaMetadata
    {
        public string Title { get; set; } = "Unknown Media";
        public string Artist { get; set; } = "Windose";
        public string Album { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string Format { get; set; } = "Unknown";
        public MediaType Type { get; set; } = MediaType.Unknown;

        public int Width { get; set; }
        public int Height { get; set; }
        public int DurationMs { get; set; }
        public int TotalFrames { get; set; }
        public float FrameRate { get; set; } = 30f;

        public bool HasVideo { get; set; }
        public bool HasAudio { get; set; }
        public int AudioSampleRate { get; set; } = 44100;
        public int AudioChannels { get; set; } = 2;
        public int AudioBitsPerSample { get; set; } = 16;

        public string DurationFormatted
        {
            get
            {
                int totalSec = DurationMs / 1000;
                int min = totalSec / 60;
                int sec = totalSec % 60;
                return $"{min:D2}:{sec:D2}";
            }
        }

        public static string FormatTime(int ms)
        {
            if (ms < 0) ms = 0;
            int totalSec = ms / 1000;
            int min = totalSec / 60;
            int sec = totalSec % 60;
            return $"{min:D2}:{sec:D2}";
        }

        public static MediaMetadata FromPath(string path)
        {
            var meta = new MediaMetadata
            {
                SourcePath = path,
                Title = Path.GetFileNameWithoutExtension(path)
            };

            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".avi":
                    meta.Format = "AVI (RIFF Video)";
                    meta.Type = MediaType.Video;
                    meta.HasVideo = true;
                    meta.HasAudio = true;
                    break;
                case ".gif":
                    meta.Format = "Animated GIF";
                    meta.Type = MediaType.Animation;
                    meta.HasVideo = true;
                    meta.HasAudio = false;
                    break;
                case ".wav":
                    meta.Format = "WAV (PCM Audio)";
                    meta.Type = MediaType.Audio;
                    meta.HasAudio = true;
                    meta.HasVideo = false;
                    break;
                case ".mp3":
                    meta.Format = "MP3 Audio";
                    meta.Type = MediaType.Audio;
                    meta.HasAudio = true;
                    meta.HasVideo = false;
                    break;
                case ".mp4":
                    meta.Format = "MP4 Video";
                    meta.Type = MediaType.Video;
                    meta.HasVideo = true;
                    meta.HasAudio = true;
                    break;
                case ".webm":
                    meta.Format = "WebM Video";
                    meta.Type = MediaType.Video;
                    meta.HasVideo = true;
                    meta.HasAudio = true;
                    break;
                case ".mkv":
                    meta.Format = "Matroska Video";
                    meta.Type = MediaType.Video;
                    meta.HasVideo = true;
                    meta.HasAudio = true;
                    break;
                case ".wmv":
                    meta.Format = "Windows Media Video";
                    meta.Type = MediaType.Video;
                    meta.HasVideo = true;
                    meta.HasAudio = true;
                    break;
                case ".mov":
                    meta.Format = "QuickTime Video";
                    meta.Type = MediaType.Video;
                    meta.HasVideo = true;
                    meta.HasAudio = true;
                    break;
                case ".flv":
                    meta.Format = "Flash Video";
                    meta.Type = MediaType.Video;
                    meta.HasVideo = true;
                    meta.HasAudio = true;
                    break;
                case ".ogg":
                    meta.Format = "Ogg Media";
                    meta.Type = MediaType.Audio;
                    meta.HasAudio = true;
                    break;
                case ".wma":
                    meta.Format = "Windows Media Audio";
                    meta.Type = MediaType.Audio;
                    meta.HasAudio = true;
                    break;
                default:
                    meta.Format = "Media Stream";
                    meta.Type = MediaType.Video;
                    meta.HasVideo = true;
                    break;
            }

            return meta;
        }
    }
}
