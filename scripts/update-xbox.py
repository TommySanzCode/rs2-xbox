"""Verify and replace the installed LAN XBE, preserving the boot-tested executable."""
from ftplib import FTP
import argparse
from getpass import getpass
import hashlib
import os
from pathlib import Path
import re

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--host', required=True, help='Xbox dashboard hostname or LAN address')
parser.add_argument('--folder', default='RS2-2004-LAN')
parser.add_argument('--previous-package', default='RS2-2004-LAN')
parser.add_argument('--backup-name', default='default-before-fit.xbe')
parser.add_argument('--staged-name', default='default-fit-upload.xbe')
args = parser.parse_args()
if not re.fullmatch(r'[A-Za-z0-9_-]{1,42}', args.folder):
    parser.error('Use a simple FATX destination folder name.')
if not re.fullmatch(r'[A-Za-z0-9_-]+', args.previous_package):
    parser.error('Invalid previous package folder.')
for filename in (args.backup_name, args.staged_name):
    if not re.fullmatch(r'[A-Za-z0-9_-]+\.xbe', filename) or len(filename) > 42 or filename.lower() == 'default.xbe':
        parser.error('Use a distinct FATX-safe backup/staging XBE filename.')
if args.backup_name.lower() == args.staged_name.lower():
    parser.error('Backup and staging filenames must differ.')

root = Path(__file__).resolve().parents[1]
client = root / 'Client3' if (root / 'Client3').is_dir() else root
new_xbe = client / 'rom' / 'default.xbe'
old_xbe = client / 'dist' / args.previous_package / 'default.xbe'
folder = '/E/' + args.folder
backup_name = args.backup_name
staged_name = args.staged_name

def remote_digest(ftp, filename):
    digest = hashlib.sha256()
    ftp.retrbinary('RETR ' + filename, digest.update, blocksize=65536)
    return digest.digest()

payload = new_xbe.read_bytes()
if payload[:4] != b'XBEH':
    raise SystemExit('New executable is not an XBE.')
password = os.environ.get('XBOX_FTP_PASSWORD') or getpass('Xbox FTP password: ')
with FTP(args.host, timeout=20) as ftp:
    ftp.login(os.environ.get('XBOX_FTP_USER', 'xbox'), password)
    ftp.cwd(folder)
    entries = {name.casefold() for name in ftp.nlst()}
    if backup_name.casefold() in entries or staged_name.casefold() in entries:
        raise SystemExit('Backup/staged executable already exists; inspect before another update.')
    if remote_digest(ftp, 'default.xbe') != hashlib.sha256(old_xbe.read_bytes()).digest():
        raise SystemExit('Installed XBE differs from the known LAN build; no files changed.')
    with new_xbe.open('rb') as stream:
        ftp.storbinary('STOR ' + staged_name, stream, blocksize=65536)
    if remote_digest(ftp, staged_name) != hashlib.sha256(payload).digest():
        raise SystemExit('Uploaded XBE failed read-back verification; original remains active.')
    ftp.rename('default.xbe', backup_name)
    try:
        ftp.rename(staged_name, 'default.xbe')
    except Exception:
        ftp.rename(backup_name, 'default.xbe')
        raise
    print('Updated ' + folder + '/default.xbe; upload verified by SHA-256.')
    print('Previous executable preserved as ' + backup_name)
