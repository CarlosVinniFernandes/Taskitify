#!/usr/bin/env python3
"""Spotify web player host for Taskitify on Linux (the WebView2 counterpart of the Windows app).

Runs WebKitGTK with its own persistent profile (cookies and login live only there; Spotify Desktop's and
the browser's profiles are never read). It watches the web player's own API traffic and reports it to
Taskitify as JSON lines, so the app can use the player's token and its track-search query:

  stdin  (one JSON object per line)
    {"cmd": "navigate", "url": "https://open.spotify.com/"}     load a page in the hidden player
    {"cmd": "login", "url": "https://accounts.spotify.com/..."} show the window so the user can sign in
    {"cmd": "hide"}                                              hide the window
    {"cmd": "cookies"}                                           answer whether the sign-in cookie exists
    {"cmd": "clear"}                                             delete all web data (disconnect)
    {"cmd": "quit"}
  stdout (one JSON object per line)
    {"event": "ready"}
    {"event": "load", "uri": "...", "ok": true}
    {"event": "request", "method", "url", "headers", "body", "status", "response"}
    {"event": "cookies", "signedIn": true}
    {"event": "cleared"}  {"event": "window_closed"}

Needs python3-gi and gir1.2-webkit2-4.1. Tokens are never logged; stderr carries diagnostics only.
"""
import json
import os
import sys
import threading
from urllib.parse import urlparse

import gi

gi.require_version("Gtk", "3.0")
gi.require_version("WebKit2", "4.1")
from gi.repository import GLib, Gtk, WebKit2  # noqa: E402

API_HOSTS = {"api.spotify.com", "api-partner.spotify.com", "spclient.wg.spotify.com", "open.spotify.com"}
# Tests point the helper at a local server; real use never sets this.
EXTRA_HOSTS = {h for h in os.environ.get("TASKITIFY_EXTRA_HOSTS", "").split(",") if h}
PROFILE = os.environ.get(
    "TASKITIFY_PROFILE",
    os.path.join(os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share"), "taskitify", "spotify-web-session"),
)
MAX_RESPONSE = 2 * 1024 * 1024

# Wraps fetch and XMLHttpRequest in the page so the app sees the web player's API calls with the headers
# and body it sent. Responses are forwarded only for the token endpoint and the GraphQL search.
INJECTED = r"""
(function () {
  if (window.__taskitify) return;
  window.__taskitify = true;
  const HOSTS = %HOSTS%;
  const post = (m) => { try { window.webkit.messageHandlers.taskitify.postMessage(JSON.stringify(m)); } catch (e) {} };
  const absolute = (u) => { try { return new URL(u, location.href); } catch (e) { return null; } };
  const watched = (u) => { const x = absolute(u); return x && HOSTS.indexOf(x.host) >= 0; };
  const wantsResponse = (u, method) => {
    const x = absolute(u);
    return x && ((x.pathname === '/api/token') || (x.host.indexOf('api-partner') === 0 && method === 'POST'));
  };
  const plainHeaders = (h) => {
    const out = {};
    if (!h) return out;
    if (typeof Headers !== 'undefined' && h instanceof Headers) h.forEach((v, k) => { out[k] = v; });
    else if (Array.isArray(h)) h.forEach((p) => { out[p[0]] = p[1]; });
    else Object.keys(h).forEach((k) => { out[k] = h[k]; });
    return out;
  };

  const realFetch = window.fetch;
  window.fetch = async function (input, init) {
    const url = typeof input === 'string' ? input : (input && input.url) || String(input);
    const method = ((init && init.method) || (input && input.method) || 'GET').toUpperCase();
    const response = await realFetch.apply(this, arguments);
    if (watched(url)) {
      const msg = {
        type: 'request', method: method, url: absolute(url).href,
        headers: Object.assign({}, plainHeaders(input && input.headers), plainHeaders(init && init.headers)),
        body: init && typeof init.body === 'string' ? init.body : null,
        status: response.status,
      };
      if (wantsResponse(url, method)) { try { msg.response = await response.clone().text(); } catch (e) {} }
      post(msg);
    }
    return response;
  };

  const realOpen = XMLHttpRequest.prototype.open;
  const realSend = XMLHttpRequest.prototype.send;
  const realSetHeader = XMLHttpRequest.prototype.setRequestHeader;
  XMLHttpRequest.prototype.open = function (method, url) {
    this.__t = { method: String(method).toUpperCase(), url: url, headers: {} };
    return realOpen.apply(this, arguments);
  };
  XMLHttpRequest.prototype.setRequestHeader = function (k, v) {
    if (this.__t) this.__t.headers[k] = v;
    return realSetHeader.apply(this, arguments);
  };
  XMLHttpRequest.prototype.send = function (body) {
    const t = this.__t;
    if (t && watched(t.url)) {
      this.addEventListener('loadend', () => {
        const msg = { type: 'request', method: t.method, url: absolute(t.url).href, headers: t.headers,
                      body: typeof body === 'string' ? body : null, status: this.status };
        if (wantsResponse(t.url, t.method)) { try { msg.response = this.responseText; } catch (e) {} }
        post(msg);
      });
    }
    return realSend.apply(this, arguments);
  };
})();
"""


def emit(obj):
    sys.stdout.write(json.dumps(obj, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def is_spotify_host(host):
    return host == "spotify.com" or host.endswith(".spotify.com")


class Host:
    def __init__(self):
        os.makedirs(PROFILE, mode=0o700, exist_ok=True)
        data = WebKit2.WebsiteDataManager(base_data_directory=PROFILE, base_cache_directory=os.path.join(PROFILE, "cache"))
        data.get_cookie_manager().set_persistent_storage(os.path.join(PROFILE, "cookies.sqlite"), WebKit2.CookiePersistentStorage.SQLITE)
        data.get_cookie_manager().set_accept_policy(WebKit2.CookieAcceptPolicy.ALWAYS)
        self.data = data
        context = WebKit2.WebContext.new_with_website_data_manager(data)

        content = WebKit2.UserContentManager()
        content.register_script_message_handler("taskitify")
        content.connect("script-message-received::taskitify", self.on_message)
        hosts = sorted(API_HOSTS | EXTRA_HOSTS)
        content.add_script(WebKit2.UserScript(
            INJECTED.replace("%HOSTS%", json.dumps(hosts)),
            WebKit2.UserContentInjectedFrames.TOP_FRAME, WebKit2.UserScriptInjectionTime.START, None, None))

        self.view = WebKit2.WebView(web_context=context, user_content_manager=content)
        self.view.connect("load-changed", self.on_load_changed)
        self.view.connect("load-failed", self.on_load_failed)
        self.view.connect("decide-policy", self.on_decide_policy)
        self.window = Gtk.Window(title="Taskitify: Spotify")
        self.window.set_default_size(480, 720)
        self.window.add(self.view)
        self.window.connect("delete-event", self.on_delete)
        self.interactive = False
        self.apply_settings()
        self.requested = None
        self.reported = True

    def apply_settings(self):
        s = self.view.get_settings()
        # Hidden player: only the page logic is needed. The login window gets images and the rest.
        s.set_auto_load_images(self.interactive)
        s.set_enable_media(self.interactive)
        s.set_enable_webaudio(False)
        s.set_enable_webgl(False)
        s.set_enable_media_stream(False)
        s.set_enable_encrypted_media(False)
        s.set_enable_developer_extras(False)

    # -- commands -----------------------------------------------------------------------------
    def handle(self, cmd):
        name = cmd.get("cmd")
        if name == "navigate":
            self.set_interactive(False)
            self.load(cmd["url"])
        elif name == "login":
            self.set_interactive(True)
            self.load(cmd["url"])
            self.window.show_all()
            self.window.present()
        elif name == "hide":
            self.window.hide()
        elif name == "cookies":
            self.data.get_cookie_manager().get_cookies("https://open.spotify.com", None, self.on_cookies)
        elif name == "clear":
            self.load("about:blank")
            self.data.clear(WebKit2.WebsiteDataTypes.ALL, 0, None, self.on_cleared)
        elif name == "quit":
            Gtk.main_quit()
        return False

    def load(self, url):
        self.requested = url
        self.reported = url == "about:blank"
        self.view.load_uri(url)

    def set_interactive(self, value):
        if self.interactive != value:
            self.interactive = value
            self.apply_settings()

    # -- callbacks ----------------------------------------------------------------------------
    def on_message(self, _manager, result):
        try:
            text = result.get_js_value().to_string()
            msg = json.loads(text)
        except Exception:
            return
        if msg.get("type") != "request":
            return
        response = msg.get("response")
        if response is not None and len(response) > MAX_RESPONSE:
            msg["response"] = None
        msg["event"] = "request"
        del msg["type"]
        emit(msg)

    def report_load(self, ok):
        # One answer per navigation. WebKitGTK does not always raise load-failed for an unreachable
        # page; it finishes on about:blank instead, which counts as a failed load too.
        if not self.reported:
            self.reported = True
            emit({"event": "load", "uri": self.requested, "ok": ok})

    def on_load_changed(self, _view, event):
        if event == WebKit2.LoadEvent.FINISHED:
            self.report_load(self.view.get_uri() != "about:blank")

    def on_load_failed(self, _view, _event, _uri, _error):
        self.report_load(False)
        return False

    def on_decide_policy(self, _view, decision, kind):
        if kind not in (WebKit2.PolicyDecisionType.NAVIGATION_ACTION, WebKit2.PolicyDecisionType.NEW_WINDOW_ACTION):
            return False
        uri = decision.get_navigation_action().get_request().get_uri()
        parsed = urlparse(uri)
        allowed = uri == "about:blank" or parsed.netloc in EXTRA_HOSTS or (
            parsed.scheme == "https" and (self.interactive or is_spotify_host(parsed.hostname or "")))
        if not allowed:
            decision.ignore()
            return True
        if kind == WebKit2.PolicyDecisionType.NEW_WINDOW_ACTION:
            # Pop-ups (social sign-in) stay in the one window.
            decision.ignore()
            self.view.load_uri(uri)
            return True
        return False

    def on_cookies(self, manager, result):
        try:
            cookies = manager.get_cookies_finish(result)
            emit({"event": "cookies", "signedIn": any(c.get_name() == "sp_dc" for c in cookies)})
        except Exception:
            emit({"event": "cookies", "signedIn": False})

    def on_cleared(self, data, result):
        try:
            data.clear_finish(result)
        finally:
            self.window.hide()
            emit({"event": "cleared"})

    def on_delete(self, _window, _event):
        self.window.hide()
        emit({"event": "window_closed"})
        return True


def read_commands(host):
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            cmd = json.loads(line)
        except ValueError:
            continue
        GLib.idle_add(host.handle, cmd)
    # stdin closed: the app is gone.
    GLib.idle_add(Gtk.main_quit)


def main():
    host = Host()
    threading.Thread(target=read_commands, args=(host,), daemon=True).start()
    emit({"event": "ready"})
    Gtk.main()


if __name__ == "__main__":
    main()
