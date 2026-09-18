"""Fetch the unmodified TimGM6mb 1.3 SoundFont from a pinned Debian source archive."""
import hashlib
import io
from pathlib import Path
import tarfile
import urllib.request

URL = 'https://deb.debian.org/debian/pool/main/t/timgm6mb-soundfont/timgm6mb-soundfont_1.3.orig.tar.gz'
SHA256 = 'af8f3a00e416dfb262bcaa904a1c84df04a51b72bbc1313aed012bc754bdf99b'


def main():
    root = Path(__file__).resolve().parents[1]
    with urllib.request.urlopen(URL, timeout=60) as response:
        archive = response.read(8 * 1024 * 1024)
    if hashlib.sha256(archive).hexdigest() != SHA256:
        raise ValueError('SoundFont source archive checksum mismatch')
    with tarfile.open(fileobj=io.BytesIO(archive), mode='r:gz') as tar:
        data = tar.extractfile('timgm6mb-soundfont_1.3/TimGM6mb.sf2').read()
    destination = root / 'rom' / 'TimGM6mb.sf2'
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(data)
    print(f'Installed TimGM6mb.sf2 ({len(data)} bytes); see release-notices/xbox/TimGM6mb.txt and GPL-2.0.txt.')


if __name__ == '__main__':
    main()
