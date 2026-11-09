using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.Kernel.System.Graphics;

namespace Windose.System.Resources
{
    /// <summary>
    /// Central manager for loading, caching, and decoding Windows PE icon resources (.dll,[-]id).
    /// Transparently aliases legacy XP icon paths to authentic Windows 7 DLL resources.
    /// </summary>
    public static class IconResource
    {
        private const string DefaultDllPath = "/mnt/System/";
        private static readonly Dictionary<string, Image> Cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static Image? fallbackIcon;

        // Fast mapping from legacy XP .png icon names to authentic Windows 7 DLL resources
        private static readonly Dictionary<string, string> LegacyIconAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Core Shell
            { "start_menu_xp.png", "imageres.dll,-24" },
            { "start_menu_shortcuts.png", "imageres.dll,-24" },
            { "computermona.png", "imageres.dll,-109" },
            { "computerwil.png", "imageres.dll,-109" },
            { "desktop.png", "imageres.dll,-109" },
            { "recycle_bin_empty.png", "imageres.dll,-55" },
            { "recycle_bin_full.png", "imageres.dll,-54" },
            { "recycle_bin_file.png", "imageres.dll,-55" },
            { "recycle_bin_directory.png", "imageres.dll,-55" },
            { "recycle_bin_file_directory.png", "imageres.dll,-54" },
            { "hard_disk_drive.png", "imageres.dll,-32" },
            { "hard_disk_drive_pie.png", "imageres.dll,-32" },
            { "hard_disk_drive_tools.png", "imageres.dll,-32" },
            { "clean_drive.png", "imageres.dll,-32" },
            { "ram_drive.png", "imageres.dll,-32" },
            { "chip_ramdrive.png", "imageres.dll,-32" },
            { "removable_disk_drive_alt.png", "imageres.dll,-30" },
            { "directory_closed.png", "imageres.dll,-3" },
            { "directory_explorer.png", "imageres.dll,-4" },
            { "directory_folder_options.png", "imageres.dll,-115" },
            { "directory_open_file_mydocs.png", "imageres.dll,-1002" },
            { "directory_fonts.png", "shell32.dll,-74" },
            { "directory_network_conn.png", "netcenter.dll,-1" },

            // Programs & Accessories
            { "console_prompt.png", "shell32.dll,-284" },
            { "calculator.png", "accessibilitycpl.dll,-327" },
            { "paint_file.png", "imageres.dll,-71" },
            { "paint_old.png", "imageres.dll,-71" },
            { "true_type_paint.png", "imageres.dll,-71" },
            { "write_wordpad.png", "shell32.dll,-70" },
            { "write_file.png", "shell32.dll,-70" },
            { "notepad_file.png", "shell32.dll,-70" },
            { "notepad_file_gear.png", "shell32.dll,-70" },
            { "media_player.png", "wmploc.dll,-102" },
            { "media_player_file.png", "wmploc.dll,-102" },
            { "msie.png", "shell32.dll,-512" },
            { "msie_box.png", "shell32.dll,-512" },
            { "search_file.png", "imageres.dll,-74" },
            { "search_directory.png", "imageres.dll,-4" },
            { "search_computer.png", "imageres.dll,-109" },
            { "search_laptop.png", "imageres.dll,-109" },
            { "computer_taskmgr.png", "shell32.dll,-171" },
            { "display_properties.png", "imageres.dll,-27" },
            { "settings_gear.png", "imageres.dll,-27" },
            { "appwizard.png", "ddores.dll,-2001" },
            { "appwiz_file.png", "ddores.dll,-2001" },
            { "mci_devices.png", "setupapi.dll,-1" },
            { "hardware.png", "setupapi.dll,-1" },
            { "scanregw.png", "imageres.dll,-68" },
            { "regedit_binary.png", "imageres.dll,-68" },
            { "regedit_file.png", "imageres.dll,-68" },
            { "clock.png", "imageres.dll,-105" },
            { "time_and_date.png", "imageres.dll,-105" },
            { "charmap.png", "imageres.dll,-107" },
            { "calendar.png", "imageres.dll,-105" },
            { "system_restore.png", "imageres.dll,-115" },
            { "gears_tweakui_a.png", "imageres.dll,-27" },

            // Tray & Notifications
            { "computer_sound.png", "mmres.dll,-3004" },
            { "sndvol_main.png", "mmres.dll,-3004" },
            { "loudspeaker_wave.png", "mmres.dll,-3004" },
            { "loudspeaker_muted.png", "mmres.dll,-3004" },
            { "network_normal_two_pcs.png", "netcenter.dll,-1" },
            { "network_drive.png", "netcenter.dll,-1" },
            { "conn_cloud.png", "netcenter.dll,-1" },
            { "message_envelope_open.png", "imageres.dll,-111" },
            { "shut_down_normal.png", "shell32.dll,-28" },

            // Documents & Files
            { "file_lines.png", "imageres.dll,-2" },
            { "file_windows.png", "imageres.dll,-24" },
            { "file_question.png", "imageres.dll,-2" },
            { "document.png", "imageres.dll,-2" },
            { "executable.png", "imageres.dll,-2" },
            { "executable_gear.png", "imageres.dll,-2" },
            { "executable_script.png", "imageres.dll,-2" },
            { "application_hammer_grouppol.png", "imageres.dll,-2" },
            { "script_file_blue.png", "shell32.dll,-70" },
            { "html.png", "shell32.dll,-512" },
            { "html_new.png", "shell32.dll,-512" },
            { "font_tt.png", "shell32.dll,-74" },
            { "font_bitmap.png", "shell32.dll,-74" },
            { "font_opentype.png", "shell32.dll,-74" },
            { "accessibility.png", "accessibilitycpl.dll,-1" },
            { "users.png", "imageres.dll,-1001" },
            { "users_key.png", "imageres.dll,-1001" },
            { "user_computer_pair.png", "imageres.dll,-1001" },
            { "user_world.png", "imageres.dll,-1001" },
            { "keyboard.png", "setupapi.dll,-10" },
            { "mouse_ms.png", "setupapi.dll,-11" },
            { "directx.png", "imageres.dll,-27" },
            { "world.png", "netcenter.dll,-1" }
        };

        private static readonly Dictionary<string, string> PeToLegacyAliases;

        static IconResource()
        {
            PeToLegacyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in LegacyIconAliases)
            {
                if (!PeToLegacyAliases.ContainsKey(kvp.Value))
                {
                    PeToLegacyAliases[kvp.Value] = kvp.Key;
                }
            }
            PeToLegacyAliases["shell32.dll,-284"] = "console_prompt.png";
            PeToLegacyAliases["shell32.dll,-25"] = "search_computer.png";
        }

        public static Image? LoadEmbeddedPng(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string clean = Path.GetFileName(name).Trim();
            if (!clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                clean += ".png";

            string resName = "Windose.Resources.Icons." + clean;
            byte[]? data = Windose.System.Kernel.ResourceLoader.FromStream(resName);
            if (data != null && data.Length > 0)
            {
                try
                {
                    return ImageLoader.FromBytes(data, clean);
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Loads an icon from a Windows resource specifier (e.g. "imageres.dll,-1001" or "shell32.dll,4"),
        /// a legacy icon alias path, or a direct file path. Never throws; returns a failsafe fallback if unresolvable.
        /// </summary>
        public static Image Load(string? specifier, int targetSize = 32)
        {
            if (string.IsNullOrWhiteSpace(specifier))
                return GetFallbackIcon(targetSize);

            string key = specifier.Trim();

            // 1. Check in-memory decoded cache
            string cacheKey = $"{key}:{targetSize}";
            lock (Cache)
            {
                if (Cache.TryGetValue(cacheKey, out Image? cached) && cached != null)
                    return cached;
            }

            // 2. Check legacy alias table (e.g. "/mnt/System/Icons/console_prompt.png" or "console_prompt.png")
            string cleanName = Path.GetFileName(key);
            string? mappedPeKey = null;
            if (LegacyIconAliases.TryGetValue(key, out string? aliased) ||
                LegacyIconAliases.TryGetValue(cleanName, out aliased))
            {
                mappedPeKey = aliased;
            }

            string peLookupKey = mappedPeKey ?? key;

            // 3. Check if specifier is a standard PE resource string (e.g. "imageres.dll,-1001" or "imageres.dll,5")
            int commaIdx = peLookupKey.IndexOf(',');
            if (commaIdx > 0 && commaIdx < peLookupKey.Length - 1)
            {
                string dllPart = peLookupKey.Substring(0, commaIdx).Trim();
                string idPart = peLookupKey.Substring(commaIdx + 1).Trim();

                if (int.TryParse(idPart, out int parsedId))
                {
                    bool isIndex = (parsedId >= 0);
                    int resourceId = isIndex ? parsedId : Math.Abs(parsedId);
                    Image? loadedFromPe = LoadFromPe(dllPart, resourceId, isIndex, targetSize);
                    if (loadedFromPe != null)
                    {
                        lock (Cache)
                        {
                            Cache[cacheKey] = loadedFromPe;
                        }
                        return loadedFromPe;
                    }
                }
            }

            // 4. Try loading from embedded resources:
            Image? embedded = LoadEmbeddedPng(cleanName);
            if (embedded == null && PeToLegacyAliases.TryGetValue(peLookupKey, out string? pngAlias))
            {
                embedded = LoadEmbeddedPng(pngAlias);
            }
            if (embedded != null)
            {
                lock (Cache) { Cache[cacheKey] = embedded; }
                return embedded;
            }

            // 5. Check if a physical image file exists on disk
            if (File.Exists(key))
            {
                try
                {
                    Image diskImg = ImageLoader.Load(key);
                    if (diskImg != null)
                    {
                        lock (Cache) { Cache[cacheKey] = diskImg; }
                        return diskImg;
                    }
                }
                catch { }
            }

            // 6. Fallback if not found
            Image fallback = GetFallbackIcon(targetSize);
            lock (Cache)
            {
                Cache[cacheKey] = fallback;
            }
            return fallback;
        }

        private static Image? LoadFromPe(string dllPath, int idOrIndex, bool isIndex, int targetSize)
        {
            string resolvedPath = dllPath;
            if (!File.Exists(resolvedPath))
            {
                string fileName = Path.GetFileName(dllPath);
                resolvedPath = Path.Combine(DefaultDllPath, fileName);
                if (!File.Exists(resolvedPath))
                {
                    resolvedPath = Path.Combine(DefaultDllPath, "Icons", fileName);
                    if (!File.Exists(resolvedPath))
                    {
                        resolvedPath = Path.Combine("/mnt/Icons/", fileName);
                        if (!File.Exists(resolvedPath))
                            return null;
                    }
                }
            }

            using var reader = PeResourceReader.Open(resolvedPath);
            if (reader == null)
                return null;

            int targetGroupId = idOrIndex;
            if (isIndex)
            {
                List<int> groupIds = reader.GetGroupIconIds();
                if (idOrIndex < 0 || idOrIndex >= groupIds.Count)
                    return null;
                targetGroupId = groupIds[idOrIndex];
            }

            List<GrpIconDirEntry> frames = reader.GetGroupIconFrames(targetGroupId);
            if (frames.Count == 0)
                return null;

            GrpIconDirEntry best = SelectBestFrame(frames, targetSize);
            byte[]? iconPayload = reader.GetIconData(best.Id);
            if (iconPayload == null || iconPayload.Length == 0)
                return null;

            return DecodeFrame(best, iconPayload);
        }

        private static GrpIconDirEntry SelectBestFrame(List<GrpIconDirEntry> frames, int targetSize)
        {
            GrpIconDirEntry best = frames[0];
            int bestDiff = int.MaxValue;
            int bestBpp = -1;

            for (int i = 0; i < frames.Count; i++)
            {
                GrpIconDirEntry f = frames[i];
                int realW = (f.Width == 0) ? 256 : f.Width;
                int diff = Math.Abs(realW - targetSize);
                int bpp = f.BitCount;

                if (diff < bestDiff || (diff == bestDiff && bpp > bestBpp))
                {
                    best = f;
                    bestDiff = diff;
                    bestBpp = bpp;
                }
            }

            return best;
        }

        private static Image? DecodeFrame(GrpIconDirEntry entry, byte[] payload)
        {
            try
            {
                // Check if embedded frame is a modern PNG
                if (payload.Length >= 8 &&
                    payload[0] == 0x89 && payload[1] == (byte)'P' &&
                    payload[2] == (byte)'N' && payload[3] == (byte)'G')
                {
                    using var ms = new MemoryStream(payload, writable: false);
                    return new Png(ms);
                }

                // Embedded DIB bitmap: synthesize standard 22-byte single-icon ICO container
                byte[] icoBytes = new byte[22 + payload.Length];
                using (var ms = new MemoryStream(icoBytes))
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write((ushort)0); // idReserved
                    bw.Write((ushort)1); // idType (1 = icon)
                    bw.Write((ushort)1); // idCount (1 frame)
                    bw.Write(entry.Width);
                    bw.Write(entry.Height);
                    bw.Write(entry.ColorCount);
                    bw.Write(entry.Reserved);
                    bw.Write(entry.Planes);
                    bw.Write(entry.BitCount);
                    bw.Write((uint)payload.Length);
                    bw.Write((uint)22); // offset to DIB
                    bw.Write(payload);
                    bw.Flush();
                }

                return new Ico(icoBytes);
            }
            catch
            {
                return null;
            }
        }

        public static Image GetFallback(int size = 32) => GetFallbackIcon(size);

        public static Image GetFallbackIcon(int size = 32)
        {
            if (fallbackIcon != null && (int)fallbackIcon.Width == size)
                return fallbackIcon;

            int w = Math.Max(16, size);
            int h = Math.Max(16, size);
            var bmp = new Bitmap(w, h, ColorDepth.ColorDepth32);
            int[] raw = bmp.RawData;

            int white = unchecked((int)0xFFFFFFFF);
            int border = unchecked((int)0xFF607D8B);
            int fold = unchecked((int)0xFF90A4AE);
            int transparent = 0;

            int pad = Math.Max(2, w / 8);
            int right = w - pad - 1;
            int bottom = h - pad - 1;
            int foldSize = Math.Max(4, w / 4);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    if (x < pad || x > right || y < pad || y > bottom)
                    {
                        raw[idx] = transparent;
                    }
                    else if ((x > right - foldSize) && (y < pad + foldSize))
                    {
                        raw[idx] = (x == right - foldSize || y == pad + foldSize) ? border : fold;
                    }
                    else if (x == pad || x == right || y == pad || y == bottom)
                    {
                        raw[idx] = border;
                    }
                    else
                    {
                        raw[idx] = white;
                    }
                }
            }

            fallbackIcon = bmp;
            return bmp;
        }

        public static void ClearCache()
        {
            lock (Cache)
            {
                Cache.Clear();
            }
        }
    }
}
