"""Check exactly the Git index before publishing; never print matched secrets."""
import ipaddress
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
ALLOWED_BINARY = {'rom/Roboto/Roboto-Bold.ttf', 'tests/fixtures/client225-crc.bin'}
ALLOWED_INI = {'xbox-config.example.ini', 'xbox-128-config.example.ini', 'server-package/options.ini'}
# Preserve the pinned upstream notice byte-for-byte (copyright symbol 0xA9).
# This exception changes text decoding only; all privacy checks still apply.
TEXT_ENCODINGS = {'release-notices/xbox/FreeType-FTL.txt': 'latin-1'}
FORBIDDEN_PARTS = {'.deps', 'build', 'dist', 'node_modules', 'runtime', 'players', 'saves', '.git'}
FORBIDDEN_SUFFIXES = {'.xbe', '.iso', '.zip', '.exe', '.dll', '.obj', '.o', '.d', '.rdi', '.log', '.pem', '.key', '.pfx', '.sqlite', '.sqlite3', '.db', '.pyc'}
IPV4 = re.compile(r'(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])')
PRIVATE_PATH = re.compile(r'(?:[A-Za-z]:[\\/]+Users[\\/]+[A-Za-z0-9][^\\/\s]*|/(?:Users|home)/[A-Za-z0-9][A-Za-z0-9_.-]*)', re.I)
SECRET = re.compile(r'-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}')

def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], stderr=subprocess.PIPE)

def index_files():
    entries = []
    for record in git('ls-files', '--stage', '-z').split(b'\0'):
        if not record:
            continue
        metadata, raw_name = record.split(b'\t', 1)
        mode, object_id, stage = metadata.split()
        if stage != b'0' or mode not in (b'100644', b'100755'):
            raise ValueError('Index contains a conflict, symlink, or nested repository.')
        entries.append((raw_name.decode('utf-8'), object_id.decode('ascii')))
    if not entries:
        raise ValueError('No staged/tracked files. Add the intended source files to Git first.')
    # Read index blobs, so unstaged edits cannot hide an already-staged secret.
    data = subprocess.check_output(['git', '-C', str(ROOT), 'cat-file', '--batch'],
        input=''.join(object_id + '\n' for _, object_id in entries).encode('ascii'))
    offset = 0
    result = []
    for name, _ in entries:
        end = data.index(b'\n', offset)
        size = int(data[offset:end].split()[2])
        payload = data[end + 1:end + 1 + size]
        result.append((name, payload))
        offset = end + 1 + size + 1
    return result

def check(files):
    issues = []
    for name, payload in files:
        path = PurePosixPath(name)
        if (set(path.parts) & FORBIDDEN_PARTS or path.suffix.lower() in FORBIDDEN_SUFFIXES or
            name.startswith('rom/cache/') or path.name == 'config.ini' or path.name.startswith('.env') or
            (path.suffix.lower() == '.ini' and name not in ALLOWED_INI)):
            issues.append((name, 'private or generated file'))
            continue
        if name in ALLOWED_BINARY:
            continue
        try:
            text = payload.decode(TEXT_ENCODINGS.get(name, 'utf-8'))
        except UnicodeDecodeError:
            issues.append((name, 'unexpected binary file'))
            continue
        if PRIVATE_PATH.search(text):
            issues.append((name, 'personal absolute path'))
        if SECRET.search(text):
            issues.append((name, 'credential or private-key pattern'))
        for match in IPV4.finditer(text):
            try:
                address = ipaddress.ip_address(match.group())
            except ValueError:
                continue
            if not address.is_loopback and not address.is_unspecified:
                issues.append((name, 'non-loopback IP address'))
                break
        if path.suffix.lower() == '.ini':
            if re.search(r'^[ \t]*(?:(?:account_)?(?:username|password)|rsa_modulus|rsa_exponent)[ \t]*=[ \t]*\S', text, re.M | re.I):
                issues.append((name, 'nonempty private configuration field'))
        if name == 'server-package/options.ini':
            for field in ('account_username', 'account_password'):
                values = re.findall(r'^[ \t]*' + field + r'[ \t]*=[ \t]*(.*)$', text, re.M | re.I)
                if len(values) != 1 or values[0].strip():
                    issues.append((name, 'server options require one blank value for each account field'))
                    break
    return issues

def main():
    files = index_files()
    issues = check(files)
    for name, reason in issues:
        print(f'FAIL: {name}: {reason}')
    if issues:
        return 1
    print(f'PASS: {len(files)} indexed source files checked; no forbidden artifacts, address patterns, private paths, or credential patterns found.')
    print('Pattern checks supplement manual review; they are not a guarantee against every possible secret.')
    return 0

if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print(f'FAIL: publication check could not complete ({type(error).__name__}).')
        sys.exit(1)
