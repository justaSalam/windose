"""
Theme and styling definitions for Windose Designer / ELF Studio.
Implements the Windows 7 Aero Glass visual language matching System/GUI/Palette/Palette.cs.
"""

# Windose OS Aero Palette Constants (Mirroring System/GUI/Palette/Palette.cs)
WINDOSE_AERO = {
    "desktop_bg": "#3a6ea5",          # Signature Windows 7 Harmony Blue Desktop
    "window_bg": "#f5f6f7",           # Window Client Body Background
    "window_border": "#6482b4",       # Aero Glass Window Outer Border
    "window_border_bright": "#8aaed8", # Aero Bevel Highlight
    "title_active": "#3464a8",        # Active Aero Glass Titlebar
    "title_inactive": "#8397b4",      # Inactive Aero Titlebar
    "title_text": "#ffffff",          # Title text
    "control_face": "#f0f0f0",        # Neutral Control Face
    "control_border": "#acacac",      # Button / Control Outline
    "control_highlight": "#e5f3ff",   # Hover / Focus Tint
    "control_shadow": "#a0a0a0",      # Sunken Shadow
    "input_bg": "#ffffff",            # Text Field Pure White
    "input_border": "#7f9db9",        # Windows Text Field Border
    "input_shadow": "#e2e8ea",        # Inset Top Shadow
    "highlight": "#3399ff",           # Win7 Aero Selection Blue
    "highlight_dark": "#2563eb",      # Saturated Action Blue
    "highlight_text": "#ffffff",      # Text on Highlight
    "taskbar_bg": "#1c2a3a",          # Deep Glass Taskbar / Chrome Slate
    "taskbar_border": "#2c4058",      # Taskbar Border
    "caption_close": "#c31e1e",       # Aero Close Button Crimson Glow
    "caption_close_hover": "#e81123", # Close Button Vibrant Red Hover
    "caption_norm": "#3a6496",        # Min/Max Aero Caption Button
    "success_green": "#10b981",       # Windows Progressbar Green
    "success_green_dark": "#16a34a",
    "text_dark": "#1e293b",           # Primary Text (Light Surface)
    "text_muted": "#64748b",          # Subdued / Secondary Text
    "text_dim": "#94a3b8",            # Placeholder Text
}

# Python Designer IDE Theme (Deep Slate Aero Chrome)
DARK_THEME = {
    "bg_dark": "#131c26",
    "bg_card": "#182433",
    "bg_sidebar": "#16202c",
    "bg_canvas": "#3a6ea5",           # Canvas displays the Windose Desktop
    "border_color": "#25374c",
    "border_highlight": "#38bdf8",
    "primary": "#3464a8",
    "primary_hover": "#2563eb",
    "accent_cyan": "#06b6d4",
    "accent_emerald": "#10b981",
    "accent_amber": "#f59e0b",
    "accent_rose": "#f43f5e",
    "text_main": "#f8fafc",
    "text_muted": "#94a3b8",
    "text_dim": "#64748b",
}

# Windose OS Preset Swatches for Property Inspector
PRESET_COLORS = [
    ("#3464a8", "Aero Blue"),
    ("#3a6ea5", "Desktop Harmony"),
    ("#3399ff", "Win7 Highlight"),
    ("#10b981", "Windows Green"),
    ("#c31e1e", "Crimson Red"),
    ("#f5f6f7", "Window Client"),
    ("#f0f0f0", "Control Face"),
    ("#ffffff", "Pure White"),
    ("#1e293b", "Slate Dark"),
    ("#7f9db9", "Input Border"),
    ("#d1d5db", "Panel Border"),
    ("#1c2a3a", "Aero Slate Glass"),
]

CANVAS_PRESETS = [
    ("800 x 500 (Windose Standard Window)", 800, 500),
    ("680 x 460 (Windose Dialog Box)", 680, 460),
    ("1024 x 600 (Windose Maximize / Netbook)", 1024, 600),
    ("640 x 480 (VGA / Cosmos Default)", 640, 480),
    ("480 x 320 (Embedded / Mobile Display)", 480, 320),
    ("1280 x 720 (720p HD Windose)", 1280, 720),
    ("1920 x 1080 (1080p FHD Windose)", 1920, 1080),
]
