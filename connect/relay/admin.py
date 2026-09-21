"""Local administration only. Never serves profiles or private keys over HTTP."""
import ipaddress
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys

DATA = Path(os.environ.get("RS2_RELAY_DATA", "/data"))

def openssl(*args):
    subprocess.run(["openssl", *args], check=True, cwd=DATA, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

def write(name, value):
    tmp = DATA / (name + ".tmp")
    tmp.write_text(value, encoding="utf-8")
    tmp.chmod(0o600)
    tmp.replace(DATA / name)

def certificate(host):
    try:
        ipaddress.IPv4Address(host)
        san = "IP:" + host
    except ValueError:
        san = "DNS:" + host
    write("server.ext", "subjectAltName=" + san + "\nextendedKeyUsage=serverAuth\nbasicConstraints=CA:FALSE\n")
    openssl("req", "-new", "-newkey", "rsa:3072", "-nodes", "-keyout", "server.key", "-out", "server.csr", "-subj", "/CN=" + host)
    openssl("x509", "-req", "-in", "server.csr", "-CA", "ca.crt", "-CAkey", "ca.key", "-CAcreateserial", "-out", "server.crt", "-days", "365", "-sha256", "-extfile", "server.ext")

def configuration(profile):
    write("frps.toml", f'''bindAddr = "0.0.0.0"
bindPort = 7000
proxyBindAddr = "127.0.0.1"
auth.method = "token"
auth.token = {json.dumps(profile["token"])}
auth.additionalScopes = ["HeartBeats", "NewWorkConns"]
transport.tls.force = true
transport.tls.certFile = "/data/server.crt"
transport.tls.keyFile = "/data/server.key"
maxPortsPerClient = 1
log.to = "console"
log.level = "warn"
[[httpPlugins]]
name = "rs2-stcp-only"
addr = "127.0.0.1:10080"
path = "/policy"
ops = ["NewProxy"]
''')
    write("relay.rs2relay", json.dumps(profile, indent=2) + "\n")

def main():
    os.umask(0o077)
    DATA.mkdir(parents=True, exist_ok=True)
    action = sys.argv[1] if len(sys.argv) > 1 else ""
    if action == "init":
        if (DATA / "relay.rs2relay").exists() or (DATA / "ca.key").exists():
            raise ValueError("Already initialized; never replace existing relay keys implicitly.")
        host, port = sys.argv[2], int(sys.argv[3])
        if len(host) > 253 or not re.fullmatch(r"[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?", host) or not 1024 <= port <= 65535:
            raise ValueError("Use a DNS hostname or public IPv4 address, and port 1024–65535.")
        openssl("req", "-x509", "-newkey", "rsa:3072", "-nodes", "-keyout", "ca.key", "-out", "ca.crt", "-days", "3650", "-sha256", "-subj", "/CN=RS2 Xbox Connect private relay CA", "-addext", "basicConstraints=critical,CA:TRUE", "-addext", "keyUsage=critical,keyCertSign,cRLSign")
        certificate(host)
        configuration(dict(version=1, host=host, port=port, token=secrets.token_hex(32), caPem=(DATA / "ca.crt").read_text()))
    elif action in ("renew", "rotate-token"):
        profile = json.loads((DATA / "relay.rs2relay").read_text())
        if action == "renew":
            certificate(profile["host"])
        else:
            profile["token"] = secrets.token_hex(32)
            configuration(profile)
    else:
        raise ValueError("Expected init, renew, or rotate-token.")
    print("Relay configuration updated. Secrets were written only to the private data directory.")

if __name__ == "__main__":
    main()
