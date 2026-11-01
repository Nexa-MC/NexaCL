"""Open and close the real distributed UI through the OS, using an isolated data directory."""
import argparse
import ctypes
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time


def windows_window(pid):
    from ctypes import wintypes
    user = ctypes.WinDLL("user32", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user.EnumWindows.argtypes = [callback_type, wintypes.LPARAM]
    user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user.IsWindowVisible.argtypes = [wintypes.HWND]
    user.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    found = []

    @callback_type
    def visit(window, _):
        owner = wintypes.DWORD()
        user.GetWindowThreadProcessId(window, ctypes.byref(owner))
        if owner.value != pid or not user.IsWindowVisible(window):
            return True
        rect = wintypes.RECT()
        title = ctypes.create_unicode_buffer(512)
        if user.GetWindowRect(window, ctypes.byref(rect)):
            user.GetWindowTextW(window, title, len(title))
            if "NexaCL" in title.value and rect.right - rect.left >= 800 and rect.bottom - rect.top >= 450:
                found.append(window)
        return True

    user.EnumWindows(visit, 0)
    if not found:
        return None
    window = found[0]

    def close():
        if not user.PostMessageW(window, 0x10, 0, 0):  # WM_CLOSE goes through the product close guard.
            raise RuntimeError("Could not request native window closure")
    return close


class X11Windows:
    class Attributes(ctypes.Structure):
        _fields_ = [(name, ctypes.c_int) for name in ("x", "y", "width", "height", "border", "depth")] + [
            ("visual", ctypes.c_void_p), ("root", ctypes.c_ulong), ("kind", ctypes.c_int),
            ("bit_gravity", ctypes.c_int), ("win_gravity", ctypes.c_int), ("backing_store", ctypes.c_int),
            ("backing_planes", ctypes.c_ulong), ("backing_pixel", ctypes.c_ulong), ("save_under", ctypes.c_int),
            ("colormap", ctypes.c_ulong), ("map_installed", ctypes.c_int), ("map_state", ctypes.c_int),
            ("all_events", ctypes.c_long), ("your_events", ctypes.c_long), ("propagation", ctypes.c_long),
            ("override_redirect", ctypes.c_int), ("screen", ctypes.c_void_p)]

    class Message(ctypes.Structure):
        _fields_ = [("type", ctypes.c_int), ("serial", ctypes.c_ulong), ("send_event", ctypes.c_int),
                    ("display", ctypes.c_void_p), ("window", ctypes.c_ulong), ("message_type", ctypes.c_ulong),
                    ("format", ctypes.c_int), ("data", ctypes.c_long * 5)]

    class Event(ctypes.Union):
        _fields_ = [("padding", ctypes.c_long * 24)]

    def __init__(self):
        self.x = ctypes.CDLL("libX11.so.6")
        self.x.XOpenDisplay.argtypes = [ctypes.c_char_p]
        self.x.XOpenDisplay.restype = ctypes.c_void_p
        self.x.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
        self.x.XDefaultRootWindow.restype = ctypes.c_ulong
        self.x.XInternAtom.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
        self.x.XInternAtom.restype = ctypes.c_ulong
        self.x.XQueryTree.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong),
                                     ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.POINTER(ctypes.c_ulong)), ctypes.POINTER(ctypes.c_uint)]
        self.x.XGetWindowAttributes.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(self.Attributes)]
        self.x.XFetchName.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_void_p)]
        self.x.XGetWindowProperty.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_long,
                                            ctypes.c_long, ctypes.c_int, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong),
                                            ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_ulong),
                                            ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_void_p)]
        self.x.XSendEvent.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_long, ctypes.POINTER(self.Event)]
        self.x.XFree.argtypes = [ctypes.c_void_p]
        self.x.XFlush.argtypes = [ctypes.c_void_p]
        self.x.XCloseDisplay.argtypes = [ctypes.c_void_p]
        error_type = ctypes.CFUNCTYPE(ctypes.c_int, ctypes.c_void_p, ctypes.c_void_p)
        self._error = error_type(lambda *_: 0)  # Windows can disappear between a tree read and a property query.
        self.x.XSetErrorHandler.argtypes = [error_type]
        self.x.XSetErrorHandler(self._error)
        self.display = self.x.XOpenDisplay(None)
        if not self.display:
            raise RuntimeError("A real X11 display or Xvfb is required")
        self.pid_atom = self.x.XInternAtom(self.display, b"_NET_WM_PID", 0)

    def find(self, pid):
        root = self.x.XDefaultRootWindow(self.display)
        returned_root, parent = ctypes.c_ulong(), ctypes.c_ulong()
        children = ctypes.POINTER(ctypes.c_ulong)()
        count = ctypes.c_uint()
        if not self.x.XQueryTree(self.display, root, ctypes.byref(returned_root), ctypes.byref(parent),
                                 ctypes.byref(children), ctypes.byref(count)):
            return None
        try:
            windows = list(children[:count.value])
        finally:
            if children:
                self.x.XFree(children)
        for window in windows:
            attributes = self.Attributes()
            if not self.x.XGetWindowAttributes(self.display, window, ctypes.byref(attributes)):
                continue
            if attributes.map_state != 2 or attributes.width < 800 or attributes.height < 450:
                continue
            kind, size, remaining, data = ctypes.c_ulong(), ctypes.c_ulong(), ctypes.c_ulong(), ctypes.c_void_p()
            format_bits = ctypes.c_int()
            status = self.x.XGetWindowProperty(self.display, window, self.pid_atom, 0, 1, 0, 0,
                                              ctypes.byref(kind), ctypes.byref(format_bits), ctypes.byref(size),
                                              ctypes.byref(remaining), ctypes.byref(data))
            try:
                if status != 0 or format_bits.value != 32 or size.value != 1 or not data:
                    continue
                if ctypes.cast(data, ctypes.POINTER(ctypes.c_ulong))[0] != pid:
                    continue
            finally:
                if data:
                    self.x.XFree(data)
            title = ctypes.c_void_p()
            try:
                if not self.x.XFetchName(self.display, window, ctypes.byref(title)) or not title:
                    continue
                if b"NexaCL" not in ctypes.string_at(title):
                    continue
            finally:
                if title:
                    self.x.XFree(title)
            return lambda: self.request_close(window)
        return None

    def request_close(self, window):
        message = self.Message()
        message.type, message.send_event, message.display = 33, 1, self.display
        message.window = window
        message.message_type = self.x.XInternAtom(self.display, b"WM_PROTOCOLS", 0)
        message.format = 32
        message.data[0] = self.x.XInternAtom(self.display, b"WM_DELETE_WINDOW", 0)
        event = self.Event()
        ctypes.memmove(ctypes.byref(event), ctypes.byref(message), ctypes.sizeof(message))
        if not self.x.XSendEvent(self.display, window, 0, 0, ctypes.byref(event)):
            raise RuntimeError("Could not request native window closure")
        self.x.XFlush(self.display)

    def close(self):
        self.x.XCloseDisplay(self.display)


def verify(payload):
    if sys.platform not in ("linux", "win32"):
        raise ValueError("OS window automation is available on Linux/X11 and Windows")
    executable = payload.resolve() / ("Nexa.Desktop.exe" if sys.platform == "win32" else "Nexa.Desktop")
    observer = X11Windows() if sys.platform == "linux" else None
    try:
        with tempfile.TemporaryDirectory(prefix="nexa-real-ui-") as temporary:
            root = Path(temporary)
            with (root / "process.log").open("wb") as log:
                process = subprocess.Popen([str(executable), "--safe-mode"], cwd=root, stdout=log, stderr=subprocess.STDOUT,
                                           env={**os.environ, "NEXA_DATA_DIR": str(root / "data")})
                try:
                    deadline = time.monotonic() + 60
                    while time.monotonic() < deadline:
                        if process.poll() is not None:
                            raise RuntimeError(f"Application exited before a visible product window: {process.returncode}")
                        close = observer.find(process.pid) if observer else windows_window(process.pid)
                        if close:
                            time.sleep(.25)
                            close()
                            if process.wait(timeout=25) != 0:
                                raise RuntimeError(f"Native UI shutdown failed: {process.returncode}")
                            break
                        time.sleep(.05)
                    else:
                        raise TimeoutError("The real product window did not become visible")
                except BaseException:
                    if process.poll() is None:
                        process.terminate()
                        try:
                            process.wait(timeout=5)
                        except subprocess.TimeoutExpired:
                            process.kill()
                            process.wait()
                    log.flush()
                    print((root / "process.log").read_text(encoding="utf-8", errors="replace")[-16384:], file=sys.stderr)
                    raise
    finally:
        if observer:
            observer.close()
    print("Real distributed UI became visible and accepted OS closure; native process exited successfully.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--payload", type=Path, required=True)
    verify(parser.parse_args().payload)
