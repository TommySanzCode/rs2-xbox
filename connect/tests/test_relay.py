"""Linux integration: actual pinned frps/frpc, TLS, policy and private byte streams.
Run: python3 connect/tests/test_relay.py /path/to/frp_0.68.0_linux_amd64
Only localhost is used. No player credentials, public service, or personal world.
"""
import concurrent.futures
import hashlib
from http.server import ThreadingHTTPServer
import importlib.util
import json
import os
from pathlib import Path
import secrets
import socket
import socketserver
import subprocess
import sys
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parents[1]
BIN = Path(sys.argv[1]).resolve()
TEMP = Path(tempfile.mkdtemp(prefix="rs2-relay-check-"))
children = []
def check(value, label):
    if not value:
        raise AssertionError(label)
    print("PASS: " + label, flush=True)
def free_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]
def launch(name, config):
    log = (TEMP / (name + "-" + secrets.token_hex(4) + ".log")).open("wb")
    p = subprocess.Popen([str(BIN / name), "-c", str(config)], stdout=log, stderr=log)
    log.close()
    children.append(p)
    return p
def terminate(p):
    if p.poll() is None:
        p.terminate()
        p.wait(timeout=8)
def wait_probe(port, expected=True, timeout=12):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=.8) as s:
                s.sendall(b"relay-check")
                ok = s.recv(11) == b"relay-check"
        except OSError:
            ok = False
        if ok == expected:
            return True
        time.sleep(.2)
    return False

class Echo(socketserver.BaseRequestHandler):
    def handle(self):
        try:
            while payload := self.request.recv(16384):
                self.request.sendall(payload)
        except OSError:
            pass
class EchoServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

def main():
    os.umask(0o077)
    relay_port, visitor_port, echo_port, policy_port = [free_port() for _ in range(4)]
    env = dict(os.environ, RS2_RELAY_DATA=str(TEMP))
    subprocess.run([sys.executable, str(ROOT / "relay/admin.py"), "init", "127.0.0.1", str(relay_port)], env=env, check=True)
    profile = json.loads((TEMP / "relay.rs2relay").read_text())
    spec = importlib.util.spec_from_file_location("rs2_policy", ROOT / "relay/run.py")
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    check(module.allowed({"proxy_type":"stcp", "proxy_name":"rs2.world-" + "a"*32}), "policy accepts private world")
    check(not module.allowed({"proxy_type":"tcp", "proxy_name":"rs2.world-" + "a"*32}), "policy rejects public TCP forwarding")
    policy = ThreadingHTTPServer(("127.0.0.1", policy_port), module.Policy)
    threading.Thread(target=policy.serve_forever, daemon=True).start()
    echo = EchoServer(("127.0.0.1", echo_port), Echo)
    threading.Thread(target=echo.serve_forever, daemon=True).start()
    server_path = TEMP / "frps.toml"
    original = server_path.read_text().replace('/data/', str(TEMP) + '/').replace('bindPort = 7000', f'bindPort = {relay_port}').replace(':10080', f':{policy_port}')
    server_path.write_text(original)
    relay = launch("frps", server_path)
    secret = secrets.token_hex(32)
    def common(token=None, ca=None, hostname="127.0.0.1"):
        return f'''serverAddr = "127.0.0.1"
serverPort = {relay_port}
user = "rs2"
loginFailExit = false
auth.token = "{token or profile['token']}"
auth.additionalScopes = ["HeartBeats", "NewWorkConns"]
transport.tls.enable = true
transport.tls.trustedCaFile = "{ca or TEMP / 'ca.crt'}"
transport.tls.serverName = "{hostname}"
log.level = "warn"
'''
    host = TEMP / "host.toml"
    def host_config(key):
        return common() + f'''[[proxies]]
name = "world-{'a'*32}"
type = "stcp"
secretKey = "{key}"
localIP = "127.0.0.1"
localPort = {echo_port}
'''
    def visitor_config(key=secret, prefix=None):
        return (prefix or common()) + f'''[[visitors]]
name = "guest"
type = "stcp"
serverName = "world-{'a'*32}"
secretKey = "{key}"
bindAddr = "127.0.0.1"
bindPort = {visitor_port}
'''
    host.write_text(host_config(secret))
    host_process = launch("frpc", host)
    visitor = TEMP / "visitor.toml"
    visitor.write_text(visitor_config())
    guest = launch("frpc", visitor)
    check(wait_probe(visitor_port), "actual STCP relay with verified TLS and shared authentication")
    def stream(_):
        payload = secrets.token_bytes(262144)
        with socket.create_connection(("127.0.0.1", visitor_port), timeout=10) as s:
            s.settimeout(10)
            s.sendall(payload)
            data = bytearray()
            while len(data) < len(payload):
                chunk = s.recv(733)
                if not chunk: break
                data.extend(chunk)
            return hashlib.sha256(data).digest() == hashlib.sha256(payload).digest()
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        check(all(pool.map(stream, range(8))), "eight simultaneous isolated relay streams")
    terminate(relay)
    check(wait_probe(visitor_port, False), "relay stop interrupts reachability")
    relay = launch("frps", server_path)
    check(wait_probe(visitor_port, timeout=30), "clients reconnect after relay restart")
    terminate(guest)
    for label, config in [
        ("invalid world secret", visitor_config(secrets.token_hex(32))),
        ("invalid relay token", visitor_config(prefix=common(token=secrets.token_hex(32)))),
        ("certificate hostname mismatch", visitor_config(prefix=common(hostname="wrong.example.com")))
    ]:
        visitor.write_text(config)
        bad = launch("frpc", visitor)
        time.sleep(1)
        # Poll throughout the rejection interval, not just before the process starts.
        accepted = wait_probe(visitor_port, True, timeout=3)
        terminate(bad)
        check(not accepted, label + " rejected")
    # Renew using the same CA, proving identities survive certificate replacement.
    terminate(relay)
    before = (TEMP / "ca.crt").read_bytes()
    subprocess.run([sys.executable, str(ROOT / "relay/admin.py"), "renew"], env=env, check=True)
    check(before == (TEMP / "ca.crt").read_bytes(), "renewal preserves trusted CA")
    relay = launch("frps", server_path)
    terminate(host_process)
    rotated = secrets.token_hex(32)
    host.write_text(host_config(rotated)); host_process = launch("frpc", host)
    visitor.write_text(visitor_config()); guest = launch("frpc", visitor)
    check(not wait_probe(visitor_port, True, 4), "old invitation rejected after world-secret rotation")
    terminate(guest)
    visitor.write_text(visitor_config(rotated)); guest = launch("frpc", visitor)
    check(wait_probe(visitor_port), "replacement invitation works after rotation and renewal")
    terminate(relay)
    subprocess.run([sys.executable, str(ROOT / "relay/admin.py"), "rotate-token"], env=env, check=True)
    check(json.loads((TEMP / "relay.rs2relay").read_text())["token"] != profile["token"], "relay token rotation updates private profile")
    echo.shutdown(); policy.shutdown()
    print("All Linux relay checks passed. No internet/NAT or Windows frpc testing is implied.")

try:
    main()
finally:
    for process in children:
        terminate(process)
