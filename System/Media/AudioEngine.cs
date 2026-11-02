using System;
using System.IO;
using Cosmos.Kernel.System.Audio;
using Windose.System.System_Calls;

namespace Windose.System.Media
{
    /// <summary>
    /// Safe audio engine interfacing with Cosmos Audio subsystem for Windose Media Player.
    /// Fails gracefully if audio hardware is absent or disabled.
    /// </summary>
    public class AudioEngine
    {
        private static AudioEngine? instance;
        public static AudioEngine Instance => instance ??= new AudioEngine();

        private AudioPlayback? currentPlayback;
        private MemoryAudioStream? currentStream;
        private byte[]? currentWaveData;
        private byte[]? currentScaledWaveData;
        private bool isPaused;
        private bool isMuted;
        private float volume = 1.0f;
        private int startTimeTick;
        private int pausedOffsetMs;
        private int audioDurationMs;

        public bool IsHardwareAvailable { get; private set; }
        public bool IsPlaying => currentPlayback != null && currentPlayback.IsPlaying && !isPaused;
        public bool IsMuted
        {
            get => isMuted;
            set => isMuted = value;
        }
        public float Volume
        {
            get => volume;
            set => volume = Math.Clamp(value, 0f, 1f);
        }

        public AudioEngine()
        {
            CheckHardware();
        }

        private void CheckHardware()
        {
            try
            {
                IsHardwareAvailable = AudioManager.IsEnabled && AudioManager.Count > 0;
            }
            catch
            {
                IsHardwareAvailable = false;
            }
        }

        public bool PlayWave(byte[] waveBytes)
        {
            Stop();
            currentWaveData = waveBytes;
            if (waveBytes == null || waveBytes.Length < 44) return false;

            if (isMuted || volume <= 0.001f)
            {
                return true;
            }

            // Approximate duration from WAV header data length
            try
            {
                if (waveBytes.Length >= 44 && waveBytes[0] == 'R' && waveBytes[1] == 'I' && waveBytes[2] == 'F' && waveBytes[3] == 'F')
                {
                    uint sampleRate = BitConverter.ToUInt32(waveBytes, 24);
                    ushort channels = BitConverter.ToUInt16(waveBytes, 22);
                    ushort bitsPerSample = BitConverter.ToUInt16(waveBytes, 34);
                    uint dataSize = BitConverter.ToUInt32(waveBytes, 40);
                    int bytesPerSec = (int)(sampleRate * channels * (bitsPerSample / 8));
                    if (bytesPerSec > 0)
                    {
                        audioDurationMs = (int)((dataSize * 1000L) / bytesPerSec);
                    }
                }
            }
            catch
            {
                audioDurationMs = 0;
            }

            startTimeTick = Environment.TickCount;
            pausedOffsetMs = 0;
            isPaused = false;

            if (!IsHardwareAvailable)
            {
                // Audio hardware not available; visualizer still progresses using time tick
                return true;
            }

            // Apply software volume scaling to 16-bit PCM wave data if volume < 1.0f
            byte[] playBytes = waveBytes;
            if (volume < 0.999f && waveBytes.Length > 44)
            {
                try
                {
                    short bitsPerSample = BitConverter.ToInt16(waveBytes, 34);
                    if (bitsPerSample == 16)
                    {
                        playBytes = (byte[])waveBytes.Clone();
                        for (int i = 44; i + 1 < playBytes.Length; i += 2)
                        {
                            short s = BitConverter.ToInt16(playBytes, i);
                            short scaled = (short)(s * volume);
                            playBytes[i] = (byte)(scaled & 0xFF);
                            playBytes[i + 1] = (byte)((scaled >> 8) & 0xFF);
                        }
                    }
                }
                catch
                {
                    playBytes = waveBytes;
                }
            }
            currentScaledWaveData = playBytes;

            try
            {
                currentStream = MemoryAudioStream.FromWave(playBytes);
                if (currentStream != null)
                {
                    AudioManager.TryStartPlayback(currentStream, null, out currentPlayback);
                }
                return true;
            }
            catch
            {
                // Sound driver error; fall back silently
                currentPlayback = null;
                return false;
            }
        }

        public void PlayStartupSound()
        {
            try
            {
                if (File.Exists("/mnt/System/Media/startup.wav"))
                {
                    PlayFile("/mnt/System/Media/startup.wav");
                    return;
                }
                byte[] chime = CreateChimeWave(1800, 523.25f, 659.25f, 783.99f);
                PlayWave(chime);
            }
            catch { }
        }

        public void PlayAlertSound()
        {
            try
            {
                if (File.Exists("/mnt/System/Media/alert.wav"))
                {
                    PlayFile("/mnt/System/Media/alert.wav");
                    return;
                }
                byte[] chime = CreateChimeWave(600, 880f, 1108.73f, 1318.51f);
                PlayWave(chime);
            }
            catch { }
        }

        public void PlayErrorSound()
        {
            try
            {
                if (File.Exists("/mnt/System/Media/error.wav"))
                {
                    PlayFile("/mnt/System/Media/error.wav");
                    return;
                }
                byte[] chime = CreateChimeWave(800, 220f, 277.18f, 329.63f);
                PlayWave(chime);
            }
            catch { }
        }

        public static bool IsWave(byte[]? data)
        {
            return data != null && data.Length >= 44 &&
                   data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
                   data[8] == 'W' && data[9] == 'A' && data[10] == 'V' && data[11] == 'E';
        }

        /// <summary>
        /// Wraps raw PCM audio bytes into a valid 44-byte RIFF WAVE container for Cosmos MemoryAudioStream playback.
        /// Cosmos only supports uncompressed PCM .wav streams.
        /// </summary>
        public static byte[] WrapPcmInWave(byte[] pcmData, int sampleRate = 44100, ushort channels = 2, ushort bitsPerSample = 16)
        {
            if (pcmData == null) return Array.Empty<byte>();
            if (IsWave(pcmData)) return pcmData;

            int dataBytes = pcmData.Length;
            byte[] wav = new byte[44 + dataBytes];

            // RIFF header
            wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
            int fileSizeMinus8 = 36 + dataBytes;
            Array.Copy(BitConverter.GetBytes(fileSizeMinus8), 0, wav, 4, 4);
            wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';

            // fmt chunk
            wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
            Array.Copy(BitConverter.GetBytes(16), 0, wav, 16, 4); // Subchunk1Size
            Array.Copy(BitConverter.GetBytes((short)1), 0, wav, 20, 2); // PCM
            Array.Copy(BitConverter.GetBytes((short)channels), 0, wav, 22, 2);
            Array.Copy(BitConverter.GetBytes(sampleRate), 0, wav, 24, 4);
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            Array.Copy(BitConverter.GetBytes(byteRate), 0, wav, 28, 4);
            short blockAlign = (short)(channels * (bitsPerSample / 8));
            Array.Copy(BitConverter.GetBytes(blockAlign), 0, wav, 32, 2);
            Array.Copy(BitConverter.GetBytes((short)bitsPerSample), 0, wav, 34, 2);

            // data chunk
            wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
            Array.Copy(BitConverter.GetBytes(dataBytes), 0, wav, 40, 4);

            Array.Copy(pcmData, 0, wav, 44, dataBytes);
            return wav;
        }

        public bool PlayFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;

                string ext = Path.GetExtension(path);
                if (!string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    SystemLogger.WriteLine("AudioEngine", $"Cosmos only supports uncompressed PCM .wav audio. Cannot play '{Path.GetFileName(path)}'.", ConsoleMessageType.Warning);
                    return false;
                }

                byte[] data = File.ReadAllBytes(path);
                if (!IsWave(data))
                {
                    SystemLogger.WriteLine("AudioEngine", $"'{Path.GetFileName(path)}' is not a valid RIFF/WAVE file.", ConsoleMessageType.Warning);
                    return false;
                }

                return PlayWave(data);
            }
            catch (Exception ex)
            {
                SystemLogger.WriteLine("AudioEngine", $"Error playing '{path}': {ex.Message}", ConsoleMessageType.Error);
                return false;
            }
        }

        public void Pause()
        {
            if (isPaused) return;
            isPaused = true;
            pausedOffsetMs = Environment.TickCount - startTimeTick;
            try
            {
                currentPlayback?.Stop();
            }
            catch { }
        }

        public void Resume()
        {
            if (!isPaused) return;
            isPaused = false;
            startTimeTick = Environment.TickCount - pausedOffsetMs;
            byte[]? dataToPlay = currentScaledWaveData ?? currentWaveData;
            if (IsHardwareAvailable && dataToPlay != null)
            {
                try
                {
                    currentStream = MemoryAudioStream.FromWave(dataToPlay);
                    if (currentStream != null)
                    {
                        AudioManager.TryStartPlayback(currentStream, null, out currentPlayback);
                    }
                }
                catch { }
            }
        }

        public void Stop()
        {
            isPaused = false;
            pausedOffsetMs = 0;
            try
            {
                currentPlayback?.Stop();
            }
            catch { }
            currentPlayback = null;
            currentStream = null;
            currentScaledWaveData = null;
        }

        public int GetPositionMs()
        {
            if (!IsPlaying && !isPaused) return 0;
            if (isPaused) return pausedOffsetMs;
            int elapsed = Environment.TickCount - startTimeTick;
            if (audioDurationMs > 0 && elapsed > audioDurationMs) elapsed = audioDurationMs;
            return elapsed;
        }

        /// <summary>
        /// Synthesizes a clean 44.1kHz 16-bit stereo WAV chime for demo media.
        /// </summary>
        public static byte[] CreateChimeWave(int durationMs = 2500, float freq1 = 440f, float freq2 = 554.37f, float freq3 = 659.25f)
        {
            int sampleRate = 44100;
            int totalSamples = (int)(sampleRate * (durationMs / 1000.0f));
            int dataBytes = totalSamples * 4; // 16-bit stereo = 4 bytes per sample
            byte[] wav = new byte[44 + dataBytes];

            // RIFF header
            wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
            int fileSizeMinus8 = 36 + dataBytes;
            Array.Copy(BitConverter.GetBytes(fileSizeMinus8), 0, wav, 4, 4);
            wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';

            // fmt chunk
            wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
            Array.Copy(BitConverter.GetBytes(16), 0, wav, 16, 4); // Subchunk1Size
            Array.Copy(BitConverter.GetBytes((short)1), 0, wav, 20, 2); // PCM
            Array.Copy(BitConverter.GetBytes((short)2), 0, wav, 22, 2); // Stereo
            Array.Copy(BitConverter.GetBytes(sampleRate), 0, wav, 24, 4); // SampleRate
            Array.Copy(BitConverter.GetBytes(sampleRate * 4), 0, wav, 28, 4); // ByteRate
            Array.Copy(BitConverter.GetBytes((short)4), 0, wav, 32, 2); // BlockAlign
            Array.Copy(BitConverter.GetBytes((short)16), 0, wav, 34, 2); // BitsPerSample

            // data chunk
            wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
            Array.Copy(BitConverter.GetBytes(dataBytes), 0, wav, 40, 4);

            // Fill harmonic chord samples with decay envelope
            int p = 44;
            double twoPi = 2.0 * Math.PI;
            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double decay = Math.Exp(-2.2 * t);
                double sample = (Math.Sin(twoPi * freq1 * t) * 0.4 +
                                 Math.Sin(twoPi * freq2 * t) * 0.3 +
                                 Math.Sin(twoPi * freq3 * t) * 0.3) * decay;

                short val = (short)(sample * 30000.0);
                byte lo = (byte)(val & 0xFF);
                byte hi = (byte)((val >> 8) & 0xFF);

                wav[p++] = lo; wav[p++] = hi; // Left
                wav[p++] = lo; wav[p++] = hi; // Right
            }

            return wav;
        }
    }
}
