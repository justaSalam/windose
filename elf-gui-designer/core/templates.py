"""
Built-in templates for Windose Designer / ELF Studio.
Implements Windows 7 Aero styled layouts matching native Windose OS standards.
"""

from core.models import Project, Widget, WidgetType


def create_windose_aero_explorer() -> Project:
    """Flagship Windows 7 Aero File Explorer & Control Panel Template."""
    proj = Project("AeroExplorer", canvas_width=800, canvas_height=500, bg_color="#f5f6f7")
    proj.widgets = [
        # 1. Navigation Toolbar (Back, Forward, Address Bar, Search)
        Widget(
            WidgetType.BUTTON, name="btn_back", x=12, y=10, width=32, height=30,
            text="◀", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_nav_back"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_fwd", x=48, y=10, width=32, height=30,
            text="▶", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_nav_forward"
        ),
        Widget(
            WidgetType.ENTRY, name="inp_address", x=88, y=10, width=470, height=30,
            text="🪐 Windose C: > System32 > Applications", bg_color="#ffffff",
            text_color="#1e293b", border_color="#7f9db9"
        ),
        Widget(
            WidgetType.ENTRY, name="inp_search", x=568, y=10, width=220, height=30,
            placeholder="Search System (Ctrl+E)...", text="", bg_color="#ffffff",
            text_color="#1e293b", border_color="#7f9db9"
        ),

        # 2. Command Bar (Organize, New Folder, Properties, Run)
        Widget(
            WidgetType.PANEL, name="command_bar", x=12, y=48, width=776, height=36,
            bg_color="#ffffff", border_color="#d1d5db", border_width=1, corner_radius=3
        ),
        Widget(
            WidgetType.BUTTON, name="btn_organize", x=20, y=52, width=90, height=28,
            text="Organize ▾", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_organize_click"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_newfolder", x=118, y=52, width=105, height=28,
            text="📁 New folder", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_new_folder"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_props", x=230, y=52, width=95, height=28,
            text="Properties", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_properties_click"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_launch", x=674, y=52, width=106, height=28,
            text="⚡ Launch ELF", bg_color="#3464a8", text_color="#ffffff", corner_radius=3,
            event_handler="on_launch_app"
        ),

        # 3. Left Navigation Pane (Favorites, Libraries, Drives)
        Widget(
            WidgetType.PANEL, name="nav_panel", x=12, y=92, width=190, height=370,
            bg_color="#ffffff", border_color="#d1d5db", border_width=1, corner_radius=3
        ),
        Widget(
            WidgetType.LABEL, name="lbl_favorites", x=24, y=102, width=160, height=20,
            text="⭐ Favorites", text_color="#3464a8", font_size=11, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.LISTBOX, name="nav_list", x=20, y=126, width=174, height=326,
            items=["Desktop", "Downloads", "Documents", "System C:", "Apps (ELF)", "Network Shares"],
            text="Shortcuts", bg_color="#ffffff"
        ),

        # 4. Center Files DataGrid
        Widget(
            WidgetType.DATAGRID, name="grid_files", x=210, y=92, width=578, height=370,
            columns=["Name", "Date Modified", "Type", "Size"], bg_color="#ffffff"
        ),

        # 5. Bottom Aero Status Bar
        Widget(
            WidgetType.STATUSBAR, name="status_bar", x=0, y=472, width=800, height=28,
            text="12 items | 1 item selected (24.5 KB) | Windose Aero Glass File Manager",
            bg_color="#f0f0f0", text_color="#334155", font_size=10
        )
    ]
    return proj


def create_system_monitor_template() -> Project:
    """Windose Task Manager & Kernel Performance Monitor."""
    proj = Project("WindoseSysMon", canvas_width=800, canvas_height=500, bg_color="#f5f6f7")
    proj.widgets = [
        # Top Header Bar
        Widget(
            WidgetType.PANEL, name="header_panel", x=12, y=10, width=776, height=48,
            bg_color="#ffffff", border_color="#d1d5db", border_width=1, corner_radius=3
        ),
        Widget(
            WidgetType.LABEL, name="title_lbl", x=28, y=22, width=380, height=24,
            text="⚡ Windose OS - Kernel Resource & Hardware Monitor", text_color="#3464a8",
            font_size=14, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_refresh", x=674, y=18, width=105, height=32,
            text="⟳ Refresh", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_refresh_clicked"
        ),

        # CPU Card
        Widget(
            WidgetType.PANEL, name="cpu_card", x=12, y=68, width=382, height=175,
            bg_color="#ffffff", border_color="#d1d5db", border_width=1, corner_radius=4
        ),
        Widget(
            WidgetType.LABEL, name="cpu_lbl", x=28, y=82, width=220, height=24,
            text="CPU Utilization (x86_64)", text_color="#1e293b", font_size=12, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.PROGRESSBAR, name="cpu_bar", x=28, y=114, width=350, height=24,
            value=65.0, border_color="#bcbfc4"
        ),
        Widget(
            WidgetType.LABEL, name="cpu_stat", x=28, y=150, width=350, height=20,
            text="Core 0: 65% | Core 1: 52% | Temperature: 42°C", text_color="#64748b", font_size=10, alignment="left"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_boost", x=28, y=184, width=130, height=32,
            text="Turbo Boost", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_turbo_boost"
        ),

        # Memory Card
        Widget(
            WidgetType.PANEL, name="mem_card", x=406, y=68, width=382, height=175,
            bg_color="#ffffff", border_color="#d1d5db", border_width=1, corner_radius=4
        ),
        Widget(
            WidgetType.LABEL, name="mem_lbl", x=422, y=82, width=220, height=24,
            text="Physical Memory (RAM)", text_color="#1e293b", font_size=12, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.PROGRESSBAR, name="mem_bar", x=422, y=114, width=350, height=24,
            value=48.0, border_color="#bcbfc4"
        ),
        Widget(
            WidgetType.LABEL, name="mem_stat", x=422, y=150, width=350, height=20,
            text="Used: 3.8 GB / 8.0 GB (Cached: 1.4 GB)", text_color="#64748b", font_size=10, alignment="left"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_purge", x=422, y=184, width=130, height=32,
            text="Purge Cache", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_purge_cache"
        ),

        # Process & Daemon Controls Card
        Widget(
            WidgetType.PANEL, name="proc_card", x=12, y=254, width=776, height=196,
            bg_color="#ffffff", border_color="#d1d5db", border_width=1, corner_radius=4
        ),
        Widget(
            WidgetType.LABEL, name="proc_title", x=28, y=268, width=240, height=24,
            text="Kernel Services & Processes", text_color="#1e293b", font_size=12, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.SWITCH, name="sw_firewall", x=28, y=308, width=160, height=30,
            text="Kernel Firewall", value=True, event_handler="on_toggle_firewall"
        ),
        Widget(
            WidgetType.SWITCH, name="sw_logging", x=210, y=308, width=160, height=30,
            text="Debug Syslog", value=False, event_handler="on_toggle_logging"
        ),
        Widget(
            WidgetType.SLIDER, name="slider_limit", x=28, y=360, width=340, height=36,
            text="Fan Speed Governor", min_val=0, max_val=100, value=75, event_handler="on_throttle_change"
        ),
        Widget(
            WidgetType.ENTRY, name="inp_kill_pid", x=430, y=308, width=180, height=34,
            placeholder="Process PID to terminate", text="", bg_color="#ffffff"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_kill", x=622, y=308, width=150, height=34,
            text="Terminate Process", bg_color="#c31e1e", text_color="#ffffff", corner_radius=3,
            event_handler="on_kill_process"
        ),

        # Status Bar
        Widget(
            WidgetType.STATUSBAR, name="status_bar", x=0, y=472, width=800, height=28,
            text="Processes: 42 | Threads: 128 | CPU: 65% | Windose Kernel x86_64",
            bg_color="#f0f0f0", text_color="#334155", font_size=10
        )
    ]
    return proj


def create_system_config_template() -> Project:
    """Windose System Preferences & Personalization Dialog."""
    proj = Project("SysConfig", canvas_width=680, canvas_height=460, bg_color="#f5f6f7")
    proj.widgets = [
        Widget(
            WidgetType.LABEL, name="lbl_title", x=20, y=14, width=340, height=28,
            text="⚙️ Windose System Preferences", text_color="#3464a8", font_size=15, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.TABS, name="tabs_config", x=20, y=50, width=640, height=340,
            items=["Display & Aero", "Network", "Users & Security"], value="Display & Aero"
        ),
        Widget(
            WidgetType.CHECKBOX, name="chk_glass", x=44, y=100, width=240, height=28,
            text="Enable Aero Glass Blur Compositor", value=True
        ),
        Widget(
            WidgetType.CHECKBOX, name="chk_shadows", x=44, y=134, width=240, height=28,
            text="Window Multi-Layer Drop Shadows", value=True
        ),
        Widget(
            WidgetType.LABEL, name="lbl_radio_grp", x=44, y=175, width=200, height=22,
            text="Compositor Performance:", text_color="#475569", font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.RADIO, name="rad_quality", x=44, y=204, width=170, height=24,
            text="Ultra Quality (60 FPS)", group="compositor", value=True
        ),
        Widget(
            WidgetType.RADIO, name="rad_balanced", x=230, y=204, width=160, height=24,
            text="Balanced Mode", group="compositor", value=False
        ),
        Widget(
            WidgetType.RADIO, name="rad_power", x=410, y=204, width=170, height=24,
            text="Power Saver (30 FPS)", group="compositor", value=False
        ),
        Widget(
            WidgetType.LABEL, name="lbl_scale", x=44, y=245, width=160, height=22,
            text="Display Scaling:", text_color="#475569", alignment="left"
        ),
        Widget(
            WidgetType.SLIDER, name="slider_scale", x=44, y=274, width=320, height=32,
            text="Scale Factor (%)", min_val=100, max_val=200, value=100
        ),
        Widget(
            WidgetType.NUMERIC, name="num_cache", x=44, y=320, width=120, height=32,
            min_val=16, max_val=512, step=16, value=128
        ),
        Widget(
            WidgetType.LABEL, name="lbl_cache", x=176, y=325, width=200, height=22,
            text="MB Video Buffer Cache", text_color="#64748b", alignment="left"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_apply", x=450, y=405, width=100, height=34,
            text="Apply", bg_color="#3464a8", text_color="#ffffff", corner_radius=3,
            event_handler="on_apply_config"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_cancel", x=560, y=405, width=100, height=34,
            text="Cancel", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_cancel_config"
        )
    ]
    return proj


def create_text_editor_template() -> Project:
    """Windose Notepad & C99 Code Editor."""
    proj = Project("AeroEditor", canvas_width=800, canvas_height=500, bg_color="#f5f6f7")
    proj.widgets = [
        # Toolbar
        Widget(
            WidgetType.PANEL, name="toolbar", x=0, y=0, width=800, height=44,
            bg_color="#ffffff", border_color="#d1d5db", border_width=1, corner_radius=0
        ),
        Widget(
            WidgetType.BUTTON, name="btn_new", x=12, y=8, width=72, height=28,
            text="📄 New", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_new_file"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_open", x=90, y=8, width=72, height=28,
            text="📂 Open", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_open_file"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_save", x=168, y=8, width=72, height=28,
            text="💾 Save", bg_color="#3464a8", text_color="#ffffff", corner_radius=3,
            event_handler="on_save_file"
        ),
        Widget(
            WidgetType.SEPARATOR, name="sep1", x=248, y=8, width=4, height=28,
            orientation="vertical"
        ),
        Widget(
            WidgetType.LABEL, name="lbl_filename", x=265, y=12, width=320, height=20,
            text="C:\\Apps\\source_code.c", text_color="#475569", font_size=11, alignment="left"
        ),
        # Multiline Text Area
        Widget(
            WidgetType.TEXTAREA, name="text_buffer", x=12, y=54, width=776, height=406,
            text="/* Windose OS v10 Aero Kernel Source File */\n#include <stdio.h>\n#include \"kernel.h\"\n\nint main(int argc, char **argv) {\n    printf(\"Hello from native Windose ELF application!\\n\");\n    return 0;\n}",
            bg_color="#ffffff", text_color="#1e293b", border_color="#7f9db9"
        ),
        # Status Bar
        Widget(
            WidgetType.STATUSBAR, name="status_bar", x=0, y=472, width=800, height=28,
            text="Ln 7, Col 1 | UTF-8 | Windows (CRLF) | Windose C99 Native", bg_color="#f0f0f0", text_color="#334155"
        )
    ]
    return proj


def create_database_browser_template() -> Project:
    """Windose Data Browser & Registry Viewer."""
    proj = Project("DataBrowser", canvas_width=800, canvas_height=500, bg_color="#f5f6f7")
    proj.widgets = [
        # Search & Action Bar
        Widget(
            WidgetType.ENTRY, name="inp_search", x=16, y=14, width=280, height=34,
            placeholder="Search records / registry...", text="", bg_color="#ffffff"
        ),
        Widget(
            WidgetType.DROPDOWN, name="combo_filter", x=306, y=14, width=160, height=34,
            options=["All Statuses", "Active", "Pending", "Archived"], value="All Statuses"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_filter", x=476, y=14, width=90, height=34,
            text="🔍 Filter", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_apply_filter"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_export", x=674, y=14, width=110, height=34,
            text="📥 Export CSV", bg_color="#3464a8", text_color="#ffffff", corner_radius=3,
            event_handler="on_export_data"
        ),
        # Table Grid
        Widget(
            WidgetType.DATAGRID, name="grid_records", x=16, y=60, width=768, height=396,
            columns=["UID", "Module", "Priority", "Memory", "State"], bg_color="#ffffff"
        ),
        # Status
        Widget(
            WidgetType.STATUSBAR, name="status_bar", x=0, y=472, width=800, height=28,
            text="Total Records: 1,420 | Showing: 1-50 | Page 1 of 29 | Windose OS", bg_color="#f0f0f0", text_color="#334155"
        )
    ]
    return proj


def create_iot_device_template() -> Project:
    """Embedded IoT Touchscreen Node."""
    proj = Project("IoTDevice", canvas_width=480, canvas_height=320, bg_color="#f5f6f7")
    proj.widgets = [
        Widget(
            WidgetType.PANEL, name="card_bg", x=14, y=14, width=452, height=292,
            bg_color="#ffffff", border_color="#d1d5db", border_width=1, corner_radius=6
        ),
        Widget(
            WidgetType.LABEL, name="lbl_title", x=32, y=26, width=320, height=26,
            text="🌡️ Embedded Environment Node", text_color="#3464a8", font_size=14, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.LABEL, name="lbl_temp", x=32, y=65, width=180, height=44,
            text="23.5 °C", text_color="#1e293b", font_size=26, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.LABEL, name="lbl_hum", x=240, y=65, width=180, height=44,
            text="48 % RH", text_color="#10b981", font_size=26, font_weight="bold", alignment="left"
        ),
        Widget(
            WidgetType.SLIDER, name="slider_target", x=32, y=130, width=416, height=36,
            text="Target Temperature", min_val=15, max_val=35, value=22, event_handler="on_target_temp_change"
        ),
        Widget(
            WidgetType.CHECKBOX, name="chk_ac", x=32, y=180, width=180, height=30,
            text="HVAC Auto-Mode", value=True, event_handler="on_ac_toggled"
        ),
        Widget(
            WidgetType.SWITCH, name="sw_power", x=240, y=180, width=160, height=30,
            text="Main Relay", value=True, event_handler="on_power_toggled"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_sync", x=32, y=232, width=190, height=40,
            text="📡 Sync Telemetry", bg_color="#3464a8", text_color="#ffffff", corner_radius=3,
            event_handler="on_sync_telemetry"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_sleep", x=240, y=232, width=190, height=40,
            text="🌙 Standby Mode", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_deep_sleep"
        )
    ]
    return proj


def create_os_login_template() -> Project:
    """Windose 7 Logon & Bootloader Dialog."""
    proj = Project("WindoseLogon", canvas_width=640, canvas_height=480, bg_color="#3a6ea5")
    proj.widgets = [
        # Center Login Box
        Widget(
            WidgetType.PANEL, name="login_card", x=140, y=80, width=360, height=320,
            bg_color="#ffffff", border_color="#6482b4", border_width=2, corner_radius=8
        ),
        Widget(
            WidgetType.LABEL, name="lbl_logo", x=160, y=105, width=320, height=32,
            text="🪐 WINDOSE 7 AERO", text_color="#3464a8", font_size=18, font_weight="bold"
        ),
        Widget(
            WidgetType.LABEL, name="lbl_sub", x=160, y=140, width=320, height=20,
            text="Please authenticate to initialize desktop session", text_color="#64748b", font_size=10
        ),
        Widget(
            WidgetType.ENTRY, name="inp_user", x=160, y=175, width=320, height=40,
            text="Administrator", bg_color="#ffffff", text_color="#1e293b", border_color="#7f9db9"
        ),
        Widget(
            WidgetType.ENTRY, name="inp_pass", x=160, y=230, width=320, height=40,
            text="••••••••••••", bg_color="#ffffff", text_color="#1e293b", border_color="#7f9db9"
        ),
        Widget(
            WidgetType.CHECKBOX, name="chk_remember", x=160, y=280, width=200, height=26,
            text="Remember Credentials", value=True
        ),
        Widget(
            WidgetType.BUTTON, name="btn_login", x=160, y=325, width=150, height=42,
            text="Log On ➔", bg_color="#3464a8", text_color="#ffffff", corner_radius=3,
            event_handler="on_login_click"
        ),
        Widget(
            WidgetType.BUTTON, name="btn_shutdown", x=330, y=325, width=150, height=42,
            text="Shut Down", bg_color="#f0f0f0", text_color="#1e293b", corner_radius=3,
            event_handler="on_shutdown_click"
        ),
        Widget(
            WidgetType.STATUSBAR, name="status_bar", x=0, y=456, width=640, height=24,
            text="Windose 7 Aero Edition | Cosmos Protected Kernel | ACPI Ready", bg_color="#1c2a3a", text_color="#94a3b8"
        )
    ]
    return proj


TEMPLATES = {
    "Windose Aero Explorer (800x500)": create_windose_aero_explorer,
    "Windose Kernel Monitor (800x500)": create_system_monitor_template,
    "System Preferences Dialog (680x460)": create_system_config_template,
    "Windose Notepad & IDE (800x500)": create_text_editor_template,
    "Registry & Data Browser (800x500)": create_database_browser_template,
    "Embedded Touchscreen (480x320)": create_iot_device_template,
    "Windose Logon Dialog (640x480)": create_os_login_template,
}
