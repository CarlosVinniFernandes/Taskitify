#!/usr/bin/env python3
"""Smoke test for src/Taskitify.Linux/SpotifyWeb/spotify_web.py against a local page that behaves like the
Spotify web player: it fetches a token and sends a GraphQL track search with an Authorization header.

    python3 tests/spotify-web-helper/test_helper.py          # needs a display (xvfb-run in CI)

Needs python3-gi and gir1.2-webkit2-4.1. Exits 0 when every check passes.
"""
import json
import os
import queue
import subprocess
import sys
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, HTTPServer

HELPER = os.path.join(os.path.dirname(__file__), "..", "..", "src", "Taskitify.Linux", "SpotifyWeb", "spotify_web.py")

PAGE = b"""<!doctype html><title>fake web player</title><script>
(async () => {
  const t = await (await fetch('/api/token?reason=init')).json();
  await fetch('/pathfinder/v1/query', {
    method: 'POST',
    headers: {'authorization': 'Bearer ' + t.accessToken, 'client-token': 'ct-1', 'content-type': 'application/json', 'x-extra': 'yes'},
    body: JSON.stringify({operationName: 'searchTracks', variables: {searchTerm: 'song artist'},
                          extensions: {persistedQuery: {version: 1, sha256Hash: 'abc123'}}}),
  });
  const x = new XMLHttpRequest();
  x.open('GET', '/xhr-check');
  x.setRequestHeader('authorization', 'Bearer xhr-token');
  x.send();
})();
</script>"""


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def reply(self, body, kind="application/json"):
        self.send_response(200)
        self.send_header("Content-Type", kind)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/":
            self.reply(PAGE, "text/html")
        elif self.path.startswith("/api/token"):
            self.reply(json.dumps({"accessToken": "tok-123", "accessTokenExpirationTimestampMs": int(time.time() * 1000) + 3600_000}).encode())
        else:
            self.reply(b"{}")

    def do_POST(self):
        self.rfile.read(int(self.headers.get("Content-Length", 0)))
        self.reply(b'{"data":{}}')


def main():
    server = HTTPServer(("127.0.0.1", 0), Handler)
    port = server.server_address[1]
    threading.Thread(target=server.serve_forever, daemon=True).start()

    profile = tempfile.mkdtemp(prefix="taskitify-web-test-")
    env = dict(os.environ, TASKITIFY_PROFILE=profile, TASKITIFY_EXTRA_HOSTS=f"127.0.0.1:{port},127.0.0.1:1")
    proc = subprocess.Popen([sys.executable, HELPER], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, env=env)
    events = queue.Queue()
    threading.Thread(target=lambda: [events.put(json.loads(l)) for l in proc.stdout], daemon=True).start()

    def send(**cmd):
        proc.stdin.write(json.dumps(cmd) + "\n")
        proc.stdin.flush()

    history = []
    cursor = [0]

    def wait_for(predicate, what, timeout=20, fresh=False):
        """First event matching, looking through what already arrived (unless fresh) and then waiting."""
        if fresh:
            cursor[0] = len(history)
        end = time.time() + timeout
        index = cursor[0]
        while time.time() < end:
            while index < len(history):
                e = history[index]
                index += 1
                if predicate(e):
                    return e
            try:
                history.append(events.get(timeout=0.2))
            except queue.Empty:
                pass
        raise AssertionError(f"timed out waiting for {what}; saw {[x.get('event') for x in history]}")

    failures = []

    def check(condition, label):
        print(("PASS " if condition else "FAIL ") + label)
        if not condition:
            failures.append(label)

    try:
        wait_for(lambda e: e["event"] == "ready", "ready")
        send(cmd="navigate", url=f"http://127.0.0.1:{port}/")

        token = wait_for(lambda e: e["event"] == "request" and "/api/token" in e["url"], "the token request")
        check(token["status"] == 200 and json.loads(token["response"])["accessToken"] == "tok-123", "token response body is forwarded")

        search = wait_for(lambda e: e["event"] == "request" and "/pathfinder/" in e["url"], "the search request")
        headers = {k.lower(): v for k, v in search["headers"].items()}
        check(search["method"] == "POST", "search method")
        check(headers.get("authorization") == "Bearer tok-123", "search Authorization header is captured")
        check(headers.get("client-token") == "ct-1" and headers.get("x-extra") == "yes", "other search headers are captured")
        body = json.loads(search["body"])
        check(body["operationName"] == "searchTracks" and body["variables"]["searchTerm"] == "song artist", "search body is captured")

        xhr = wait_for(lambda e: e["event"] == "request" and "/xhr-check" in e["url"], "the XHR request")
        check({k.lower(): v for k, v in xhr["headers"].items()}.get("authorization") == "Bearer xhr-token", "XMLHttpRequest headers are captured")

        loaded = wait_for(lambda e: e["event"] == "load", "page load")
        check(loaded["ok"] is True, "load event reports success")

        send(cmd="cookies")
        cookies = wait_for(lambda e: e["event"] == "cookies", "cookie answer", fresh=True)
        check(cookies["signedIn"] is False, "no sp_dc cookie means signed out")

        send(cmd="navigate", url="http://127.0.0.1:1/")
        failed = wait_for(lambda e: e["event"] == "load" and not e["ok"], "load failure", fresh=True)
        check(failed["ok"] is False, "an unreachable page reports a failed load")

        send(cmd="clear")
        wait_for(lambda e: e["event"] == "cleared", "clear", fresh=True)
        check(True, "clear completes")
    except AssertionError as ex:
        failures.append(str(ex))
        print("FAIL " + str(ex))
    finally:
        try:
            send(cmd="quit")
            proc.wait(timeout=10)
        except Exception:
            proc.kill()
        server.shutdown()

    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
