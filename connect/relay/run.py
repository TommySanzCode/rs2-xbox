"""Supervise frps and restrict registration to this application's private STCP proxy."""
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import re
import signal
import subprocess
import threading

def allowed(content):
    return (content.get("proxy_type") == "stcp"
            and re.fullmatch(r"rs2\.world-[a-f0-9]{32}", content.get("proxy_name", "")) is not None)

class Policy(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_POST(self):
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size <= 65536 or self.path.split("?")[0] != "/policy":
                raise ValueError("Invalid request")
            request = json.loads(self.rfile.read(size))
            accept = allowed(request.get("content", {}))
        except (ValueError, TypeError, AttributeError):
            accept = False
        payload = json.dumps({"reject": not accept, "reject_reason": "Only RS2 private STCP worlds are permitted" if not accept else "", "unchange": True}).encode()
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

def main():
    server = ThreadingHTTPServer(("127.0.0.1", 10080), Policy)
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, daemon=True).start()
    child = subprocess.Popen(["frps", "-c", "/data/frps.toml"])
    for event in (signal.SIGTERM, signal.SIGINT):
        signal.signal(event, lambda *_: child.terminate())
    try:
        return child.wait()
    finally:
        server.shutdown()

if __name__ == "__main__":
    raise SystemExit(main())
