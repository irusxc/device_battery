"""Build the dependency-free Windows desktop app with the Windows .NET Framework compiler."""
from pathlib import Path
import os, subprocess, shutil, struct, zlib, zipfile

ROOT = Path(__file__).resolve().parents[1]
DIST = ROOT / 'dist' / 'v0.2.0'
BUILD = ROOT / 'build'
DIST.mkdir(parents=True,exist_ok=True)
BUILD.mkdir(exist_ok=True)
framework = Path(os.environ.get('WINDIR', r'C:\Windows')) / 'Microsoft.NET' / 'Framework64' / 'v4.0.30319'
if not (framework / 'csc.exe').exists():
    raise SystemExit('The .NET Framework 4.x x64 compiler is required. Target: Windows 10/11 x64 with .NET Framework 4.8.')

# Small original icon, generated locally without image or package dependencies.
size = 32
polygon = [(18,5),(9,18),(15,18),(13,27),(24,13),(17,13)]
def inside(x, y):
    yes = False
    for i, (ax, ay) in enumerate(polygon):
        bx, by = polygon[i-1]
        if (ay > y) != (by > y) and x < (bx-ax)*(y-ay)/(by-ay)+ax:
            yes = not yes
    return yes
pixels = bytearray()
for y in range(size):
    pixels.append(0)
    for x in range(size):
        pixels.extend((225,225,225,255) if inside(x,y) else (45,45,45,255) if (x-15.5)**2+(y-15.5)**2 < 225 else (0,0,0,0))
def chunk(kind,data):
    return struct.pack('>I',len(data))+kind+data+struct.pack('>I',zlib.crc32(kind+data)&0xffffffff)
png = b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',32,32,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(pixels))+chunk(b'IEND',b'')
icon = BUILD/'app.ico'
icon.write_bytes(struct.pack('<HHH',0,1,1)+struct.pack('<BBBBHHII',32,32,0,0,1,32,len(png),22)+png)
refs = [framework/'System.dll', framework/'System.Core.dll', framework/'System.Web.Extensions.dll', framework/'System.Xaml.dll', framework/'System.Drawing.dll', framework/'System.Windows.Forms.dll']
refs += [framework/'WPF'/name for name in ['WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll']]
command = [str(framework/'csc.exe'),'/nologo','/utf8output','/codepage:65001','/target:winexe','/platform:x64','/optimize+', '/warn:4', '/out:'+str(DIST/'PeripheralPower.exe'), '/win32manifest:'+str(ROOT/'src'/'app.manifest'), '/win32icon:'+str(icon), '/resource:'+str(ROOT/'src'/'MainWindow.xaml')+',MainWindow.xaml']
command += ['/reference:'+str(ref) for ref in refs]
command += [str(p) for p in sorted((ROOT/'src').glob('*.cs'))]
result = subprocess.run(command, cwd=ROOT)
if result.returncode:
    raise SystemExit(result.returncode)
shutil.copy2(ROOT/'src'/'App.config', DIST/'PeripheralPower.exe.config')
shutil.copy2(ROOT/'device-catalog.json', DIST/'device-catalog.json')
for name in ['README.md','PROTOCOLS.md','CHANGELOG.md','LICENSE']:
    if (ROOT/name).exists(): shutil.copy2(ROOT/name,DIST/name)
(DIST/'images').mkdir(exist_ok=True)
(DIST/'docs').mkdir(exist_ok=True)
for name in ['preview-demo.png','PUBLISHING.md','RELEASE-v0.2.0.md']:
    if (ROOT/'docs'/name).exists(): shutil.copy2(ROOT/'docs'/name,DIST/'docs'/name)
with zipfile.ZipFile(DIST/'PeripheralPower-0.2.0-win-x64.zip','w',zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(DIST.iterdir()):
        if path.suffix in ['.exe','.config','.json','.md'] or path.name == 'LICENSE': archive.write(path,'PeripheralPower/'+path.name)
    for path in sorted((DIST/'docs').iterdir()):
        if path.is_file(): archive.write(path,'PeripheralPower/docs/'+path.name)
    archive.writestr('PeripheralPower/images/','')
print('Built:',DIST/'PeripheralPower.exe')
print('Executable bytes:',(DIST/'PeripheralPower.exe').stat().st_size)
