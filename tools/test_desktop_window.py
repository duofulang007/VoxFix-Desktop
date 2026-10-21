"""Verify the actual shipped window is visible on the interactive input desktop."""
import ctypes as c
from ctypes import wintypes as w
import json
from pathlib import Path
import subprocess
import sys
import time

user = c.WinDLL("user32", use_last_error=True)
user.OpenInputDesktop.argtypes = [w.DWORD, w.BOOL, w.DWORD]
user.OpenInputDesktop.restype = w.HANDLE
user.GetThreadDesktop.argtypes = [w.DWORD]
user.GetThreadDesktop.restype = w.HANDLE
user.GetUserObjectInformationW.argtypes = [w.HANDLE, c.c_int, w.LPVOID, w.DWORD, c.POINTER(w.DWORD)]
user.GetWindowThreadProcessId.argtypes = [w.HWND, c.POINTER(w.DWORD)]
user.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, c.c_int]
user.IsWindowVisible.argtypes = [w.HWND]
user.GetWindowRect.argtypes = [w.HWND, c.POINTER(w.RECT)]
user.CloseDesktop.argtypes = [w.HANDLE]
user.MonitorFromRect.argtypes = [c.POINTER(w.RECT), w.DWORD]
user.MonitorFromRect.restype = w.HANDLE
user.SetWindowPos.argtypes = [w.HWND, w.HWND, c.c_int, c.c_int, c.c_int, c.c_int, w.UINT]
user.GetDC.argtypes = [w.HWND]
user.GetDC.restype = w.HDC
user.ReleaseDC.argtypes = [w.HWND, w.HDC]
gdi = c.WinDLL("gdi32", use_last_error=True)
gdi.GetPixel.argtypes = [w.HDC, c.c_int, c.c_int]
gdi.GetPixel.restype = w.DWORD
CALLBACK = c.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
user.EnumDesktopWindows.argtypes = [w.HANDLE, CALLBACK, w.LPARAM]


def desktop_name(handle):
    name = c.create_unicode_buffer(256)
    needed = w.DWORD()
    if not user.GetUserObjectInformationW(handle, 2, name, c.sizeof(name), c.byref(needed)):
        raise c.WinError(c.get_last_error())
    return name.value


def main():
    handle = user.OpenInputDesktop(0, False, 0x41)
    if not handle:
        raise c.WinError(c.get_last_error())
    rows = []

    @CALLBACK
    def visit(window, unused):
        title = c.create_unicode_buffer(256)
        user.GetWindowTextW(window, title, 256)
        if title.value not in ("VoxFix Desktop", "VoxFix 麦克风"):
            return True
        pid = w.DWORD()
        thread = user.GetWindowThreadProcessId(window, c.byref(pid))
        rect = w.RECT()
        user.GetWindowRect(window, c.byref(rect))
        rows.append({"title": title.value, "pid": pid.value,
                     "handle": int(window), "desktop": expected,
                     "visible": bool(user.IsWindowVisible(window)),
                     "onScreen": bool(user.MonitorFromRect(c.byref(rect), 0)),
                     "rect": [rect.left, rect.top, rect.right, rect.bottom]})
        return True

    try:
        expected = desktop_name(handle)
        if not user.EnumDesktopWindows(handle, visit, 0):
            raise c.WinError(c.get_last_error())
    finally:
        user.CloseDesktop(handle)
    print(json.dumps({"inputDesktop": expected, "windows": rows}, ensure_ascii=True))
    assert len(rows) == 1, "Expected exactly one VoxFix Desktop window on interactive desktop"
    window = rows[0]
    assert window["title"] == "VoxFix Desktop", "Product name must distinguish the desktop app"
    assert window["visible"] and window["desktop"] == expected, "Microphone is not visible on interactive desktop"
    assert window["onScreen"], "Microphone is outside all screens"
    assert window["rect"][2] > window["rect"][0] and window["rect"][3] > window["rect"][1], "Window is empty"
    left, top, right, bottom = window["rect"]
    width, height = right - left, bottom - top
    points = ([(32, 48), (96, 48), (64, 40), (64, 56)] if width == 128 else
              [(24, 44), (88, 44), (56, 36), (56, 52)] if width == 112 else
              [(12, 36), (59, 36), (36, 12), (36, 59)] if width == 72 else
              [(8, 18), (90, 18), (50, 5), (50, 30)])
    screen = user.GetDC(None)
    assert screen, "Cannot inspect the actual desktop surface"
    colors = []
    try:
        for x, y in points:
            pixel = gdi.GetPixel(screen, left + x, top + y)
            colors.append((pixel & 255, (pixel >> 8) & 255, (pixel >> 16) & 255))
    finally:
        user.ReleaseDC(None, screen)
    assert sum(max(rgb) - min(rgb) <= 8 and 60 <= min(rgb) <= 240 for rgb in colors) >= 3, \
        "Window exists but its gray surface was not drawn on the desktop: " + str(colors)
    print("PASS: VoxFix Desktop has one visible interactive-desktop window")
    if "--exercise-reopen" in sys.argv:
        root = Path(__file__).resolve().parent.parent
        # Hide only this application's window, then exercise the ordinary second-launch path.
        if not user.SetWindowPos(window["handle"], None, -32000, -32000, width, height, 0x0094):
            raise c.WinError(c.get_last_error())
        subprocess.run([str(root / "bin/VoxFix Desktop.exe")], cwd=root, timeout=10, check=True)
        time.sleep(0.3)
        subprocess.run([sys.executable, str(Path(__file__).resolve())], timeout=10, check=True)
        print("PASS: second launch restores the existing microphone without another instance")


if __name__ == "__main__":
    main()
