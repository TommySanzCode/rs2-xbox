"""Write only privacy-checked Git index contents to a source ZIP outside the repo."""
import argparse
import hashlib
import importlib.util
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('public_check', Path(__file__).with_name('check-public.py'))
checker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(checker)
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('output', type=Path)
args = parser.parse_args()
output = args.output.resolve()
if output == root or root in output.parents:
    parser.error('Choose an output path outside this source repository.')
files = checker.index_files()
issues = checker.check(files)
if issues:
    raise SystemExit('Privacy check failed; run scripts/check-public.py for filenames and categories.')
output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(output, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
    for name, payload in files:
        archive.writestr('rs2-xbox/' + name, payload)
with zipfile.ZipFile(output) as archive:
    assert len(archive.infolist()) == len(files)
    for name, payload in files:
        assert archive.read('rs2-xbox/' + name) == payload
print(f'Exported and read-back verified {len(files)} source files.')
print('SHA256: ' + hashlib.sha256(output.read_bytes()).hexdigest())
