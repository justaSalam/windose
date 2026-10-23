#!/usr/bin/env python3
"""
ELF Studio - Visual GUI Designer for ELF Programs on Windows.
Entry point application launcher.
"""

import sys
import os

# Ensure the root directory of the application is in python path
current_dir = os.path.dirname(os.path.abspath(__file__))
if current_dir not in sys.path:
    sys.path.insert(0, current_dir)

def check_dependencies():
    missing = []
    try:
        import customtkinter
    except ImportError:
        missing.append("customtkinter")
    try:
        import PIL
    except ImportError:
        missing.append("pillow")

    if missing:
        print("Missing required libraries:", ", ".join(missing))
        print("Installing automatically via pip...")
        import subprocess
        subprocess.run([sys.executable, "-m", "pip", "install", *missing], check=True)
        print("Dependencies installed successfully!")

def main():
    check_dependencies()
    from ui.main_window import MainWindow

    app = MainWindow()
    app.mainloop()

if __name__ == "__main__":
    main()
