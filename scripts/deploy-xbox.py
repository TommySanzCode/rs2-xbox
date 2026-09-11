"""Copy a new LAN package to the user's Xbox, then read back and verify every file."""
import argparse
from getpass import getpass
from ftplib import FTP
import hashlib
import os
from pathlib import Path
import re

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--host', required=True)
parser.add_argument('--folder', default='RS2-2004-LAN')
parser.add_argument('--drive', choices=['E', 'F', 'G'], default='E')
args = parser.parse_args()
if not re.fullmatch(r'[A-Za-z0-9_-]{1,42}', args.folder):
    parser.error('Use a simple FATX folder name, at most 42 characters.')
root = Path(__file__).resolve().parents[1]
client = root / 'Client3' if (root / 'Client3').is_dir() else root
source = client / 'dist' / args.folder
if not (source / 'default.xbe').is_file():
    parser.error('Build and package this folder before deploying it.')

files = sorted(p for p in source.rglob('*') if p.is_file())
destination = f'/{args.drive}/{args.folder}'
password = os.environ.get('XBOX_FTP_PASSWORD') or getpass('Xbox FTP password: ')
with FTP() as ftp:
    ftp.connect(args.host, 21, timeout=20)
    ftp.login(os.environ.get('XBOX_FTP_USER', 'xbox'), password)
    ftp.cwd('/' + args.drive)
    if args.folder.casefold() in {name.rstrip('/').split('/')[-1].casefold() for name in ftp.nlst()}:
        raise SystemExit('Destination already exists; use a new package/folder name to preserve it.')
    ftp.mkd(destination)
    for directory in sorted((p for p in source.rglob('*') if p.is_dir()), key=lambda p: len(p.parts)):
        ftp.mkd(destination + '/' + directory.relative_to(source).as_posix())

    for index, local in enumerate(files, 1):
        relative = local.relative_to(source).as_posix()
        remote = destination + '/' + relative
        with local.open('rb') as stream:
            ftp.storbinary('STOR ' + remote, stream, blocksize=65536)
        expected = hashlib.sha256(local.read_bytes()).digest()
        actual = hashlib.sha256()
        ftp.retrbinary('RETR ' + remote, actual.update, blocksize=65536)
        if actual.digest() != expected:
            raise SystemExit('Read-back verification failed for ' + relative)
        if index % 100 == 0 or index == len(files):
            print(f'Uploaded and verified {index}/{len(files)} files.', flush=True)
    print(f'Installed and SHA-256 verified: {args.host}:{destination}/default.xbe', flush=True)
