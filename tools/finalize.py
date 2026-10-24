from pathlib import Path
import hashlib, json, shutil, zipfile

ROOT=Path(__file__).resolve().parents[1]
DIST=ROOT/'dist'/'v0.2.0'
preview=ROOT/'docs'
preview.mkdir(exist_ok=True)
shutil.copy2(ROOT/'build'/'preview-demo.png',preview/'preview-demo.png')
shutil.copy2(ROOT/'build'/'preview-real.png',preview/'preview-real.png')
for name in ['README.md','PROTOCOLS.md','CHANGELOG.md','LICENSE']:
    shutil.copy2(ROOT/name,DIST/name)
(DIST/'docs').mkdir(exist_ok=True)
for name in ['preview-demo.png','PUBLISHING.md','RELEASE-v0.2.0.md']:
    shutil.copy2(ROOT/'docs'/name,DIST/'docs'/name)
archive_path=DIST/'PeripheralPower-0.2.0-win-x64.zip'
with zipfile.ZipFile(archive_path,'w',zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(DIST.iterdir()):
        if path.suffix in ['.exe','.config','.json','.md'] or path.name=='LICENSE':
            archive.write(path,'PeripheralPower/'+path.name)
    for path in sorted((DIST/'docs').iterdir()):
        if path.is_file(): archive.write(path,'PeripheralPower/docs/'+path.name)
    archive.writestr('PeripheralPower/images/','')
with zipfile.ZipFile(archive_path) as archive:
    assert archive.testzip() is None
    for name in ['PeripheralPower.exe','PeripheralPower.exe.config','device-catalog.json','README.md','PROTOCOLS.md','LICENSE','docs/preview-demo.png','docs/PUBLISHING.md','docs/RELEASE-v0.2.0.md']:
        assert 'PeripheralPower/'+name in archive.namelist()
    assert json.loads(archive.read('PeripheralPower/device-catalog.json'))==[]
for path in [DIST/'PeripheralPower.exe',archive_path]:
    print(path.name,path.stat().st_size,'bytes','sha256',hashlib.sha256(path.read_bytes()).hexdigest())
print('Archive verified; final previews saved to docs/.')
