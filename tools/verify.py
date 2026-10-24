from pathlib import Path
import subprocess, json, time
ROOT=Path(__file__).resolve().parents[1]
exe=ROOT/'dist'/'PeripheralPower.exe'
build=ROOT/'build'
for args, timeout in [(['--self-test',str(build/'test-results.txt')],15),(['--ui-test',str(build/'ui-test-results.txt')],15),(['--probe',str(build/'hardware-scan.json')],35),(['--capture-demo',str(build/'preview-demo.png')],20),(['--capture-real',str(build/'hardware-scan.json'),str(build/'preview-real.png')],20)]:
    started=time.monotonic()
    try:
        result=subprocess.run([str(exe),*args],cwd=ROOT,timeout=timeout)
        print(args[0], 'exit',result.returncode,'seconds',round(time.monotonic()-started,2),flush=True)
        if result.returncode: raise SystemExit(result.returncode)
    except subprocess.TimeoutExpired:
        print(args[0],'TIMEOUT',flush=True)
        raise SystemExit(1)
print((build/'test-results.txt').read_text(),flush=True)
print((build/'ui-test-results.txt').read_text(),flush=True)
snapshot=json.loads((build/'hardware-scan.json').read_text(encoding='utf-8-sig'))
print(json.dumps(snapshot,ensure_ascii=True,indent=2),flush=True)
