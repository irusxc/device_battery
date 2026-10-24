"""Exercise the actual tray process and single-instance activation, without changing preferences."""
from pathlib import Path
import subprocess, ctypes, time, json
from ctypes import wintypes

ROOT=Path(__file__).resolve().parents[1]
exe=ROOT/'dist'/'v0.2.0'/'PeripheralPower.exe'
user32=ctypes.WinDLL('user32',use_last_error=True)
kernel32=ctypes.WinDLL('kernel32',use_last_error=True)
psapi=ctypes.WinDLL('psapi',use_last_error=True)
user32.GetWindowThreadProcessId.argtypes=[wintypes.HWND,ctypes.POINTER(wintypes.DWORD)]
user32.IsWindowVisible.argtypes=[wintypes.HWND]
user32.IsWindowVisible.restype=wintypes.BOOL
user32.PostMessageW.argtypes=[wintypes.HWND,wintypes.UINT,wintypes.WPARAM,wintypes.LPARAM]
kernel32.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD]
kernel32.OpenProcess.restype=wintypes.HANDLE
kernel32.CloseHandle.argtypes=[wintypes.HANDLE]
class Memory(ctypes.Structure):
    _fields_=[('cb',wintypes.DWORD),('PageFaultCount',wintypes.DWORD)]+[(n,ctypes.c_size_t) for n in ['PeakWorkingSetSize','WorkingSetSize','QuotaPeakPagedPoolUsage','QuotaPagedPoolUsage','QuotaPeakNonPagedPoolUsage','QuotaNonPagedPoolUsage','PagefileUsage','PeakPagefileUsage','PrivateUsage']]
psapi.GetProcessMemoryInfo.argtypes=[wintypes.HANDLE,ctypes.POINTER(Memory),wintypes.DWORD]
callback_type=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HWND,wintypes.LPARAM)
user32.EnumWindows.argtypes=[callback_type,wintypes.LPARAM]
def windows(pid):
    result=[]
    @callback_type
    def callback(hwnd,extra):
        current=wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd,ctypes.byref(current))
        text=ctypes.create_unicode_buffer(256)
        user32.GetWindowTextW(hwnd,text,256)
        if current.value==pid and 'Peripheral Power' in text.value:
            result.append((hwnd,bool(user32.IsWindowVisible(hwnd))))
        return True
    user32.EnumWindows(callback,0)
    return result

process=subprocess.Popen([str(exe),'--background','--runtime-test-instance'],cwd=ROOT,creationflags=subprocess.CREATE_NO_WINDOW)
try:
    time.sleep(2)
    assert process.poll() is None, 'Background app exited unexpectedly'
    initial=windows(process.pid)
    assert initial and not any(v for _,v in initial), 'Background start should keep panel hidden'
    second=subprocess.run([str(exe),'--runtime-test-instance'],cwd=ROOT,timeout=8)
    assert second.returncode==0, 'Second launch failed'
    time.sleep(1)
    activated=windows(process.pid)
    assert any(v for _,v in activated), 'Second launch did not activate original window'
    hwnd=next(h for h,v in activated if v)
    user32.PostMessageW(hwnd,0x0010,0,0)
    time.sleep(.3)
    assert process.poll() is None and not any(v for _,v in windows(process.pid)), 'Closing panel must keep tray alive'
    handle=kernel32.OpenProcess(0x0400|0x0010,False,process.pid)
    memory=Memory(); memory.cb=ctypes.sizeof(memory)
    assert psapi.GetProcessMemoryInfo(handle,ctypes.byref(memory),memory.cb)
    kernel32.CloseHandle(handle)
    result={'background_start':'pass','single_instance_activation':'pass','close_to_tray':'pass','working_set_mib':round(memory.WorkingSetSize/1024**2,1),'private_bytes_mib':round(memory.PrivateUsage/1024**2,1)}
    (ROOT/'build'/'runtime-check.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(result),flush=True)
finally:
    # Test owns this process; production UI exit is separately covered by UiTests.
    if process.poll() is None: process.terminate(); process.wait(timeout=5)
