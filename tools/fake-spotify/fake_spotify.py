#!/usr/bin/env python3
"""A stand-in for Spotify Desktop on Linux: owns org.mpris.MediaPlayer2.spotify on the session bus
and plays a fake playlist, so Taskitify can be developed and tested without a Spotify account.

    python3 tools/fake-spotify/fake_spotify.py            # plays, advances, answers controls
    python3 tools/fake-spotify/fake_spotify.py --paused   # starts paused

Needs python3-dbus and python3-gi (both ship with Ubuntu desktop).
"""
import sys
import time

import dbus
import dbus.mainloop.glib
import dbus.service
from gi.repository import GLib

BUS_NAME = "org.mpris.MediaPlayer2.spotify"
OBJECT_PATH = "/org/mpris/MediaPlayer2"
PLAYER = "org.mpris.MediaPlayer2.Player"
PROPS = "org.freedesktop.DBus.Properties"

# Real songs, so LRCLIB has synced lyrics for them.
PLAYLIST = [
    ("Bohemian Rhapsody", "Queen", "A Night at the Opera", 354),
    ("Billie Jean", "Michael Jackson", "Thriller", 294),
    ("Take On Me", "a-ha", "Hunting High and Low", 225),
]


class FakePlayer(dbus.service.Object):
    def __init__(self, bus, paused):
        super().__init__(dbus.service.BusName(BUS_NAME, bus), OBJECT_PATH)
        self.index = 0
        self.status = "Paused" if paused else "Playing"
        self.shuffle = False
        self.loop = "None"
        self._base = 0.0
        self._base_at = time.monotonic()

    # -- state -----------------------------------------------------------------------------
    def position(self):
        elapsed = time.monotonic() - self._base_at if self.status == "Playing" else 0.0
        return min(self._base + elapsed, PLAYLIST[self.index][3])

    def _set_position(self, seconds):
        self._base = max(0.0, seconds)
        self._base_at = time.monotonic()

    def metadata(self):
        title, artist, album, length = PLAYLIST[self.index]
        return dbus.Dictionary({
            "mpris:trackid": dbus.ObjectPath(f"/com/spotify/track/fake{self.index}"),
            "mpris:length": dbus.Int64(length * 1_000_000),
            "mpris:artUrl": "",
            "xesam:title": title,
            "xesam:artist": dbus.Array([artist], signature="s"),
            "xesam:album": album,
        }, signature="sv")

    def all_props(self):
        return dbus.Dictionary({
            "PlaybackStatus": self.status,
            "LoopStatus": self.loop,
            "Shuffle": dbus.Boolean(self.shuffle),
            "Metadata": self.metadata(),
            "Position": dbus.Int64(int(self.position() * 1_000_000)),
            "CanGoNext": dbus.Boolean(True),
            "CanGoPrevious": dbus.Boolean(True),
            "CanPlay": dbus.Boolean(True),
            "CanPause": dbus.Boolean(True),
            "CanSeek": dbus.Boolean(True),
        }, signature="sv")

    def _changed(self, *names):
        full = self.all_props()
        self.PropertiesChanged(PLAYER, {n: full[n] for n in names}, dbus.Array([], signature="s"))

    def _go(self, index):
        self.index = index % len(PLAYLIST)
        self._set_position(0)
        self._changed("Metadata", "PlaybackStatus", "Position")

    # -- org.freedesktop.DBus.Properties ---------------------------------------------------
    @dbus.service.method(PROPS, in_signature="ss", out_signature="v")
    def Get(self, interface, prop):
        return self.all_props()[prop]

    @dbus.service.method(PROPS, in_signature="s", out_signature="a{sv}")
    def GetAll(self, interface):
        return self.all_props()

    @dbus.service.method(PROPS, in_signature="ssv")
    def Set(self, interface, prop, value):
        if prop == "Shuffle":
            self.shuffle = bool(value)
        elif prop == "LoopStatus":
            self.loop = str(value)
        self._changed(prop)

    @dbus.service.signal(PROPS, signature="sa{sv}as")
    def PropertiesChanged(self, interface, changed, invalidated):
        pass

    # -- org.mpris.MediaPlayer2.Player -----------------------------------------------------
    @dbus.service.method(PLAYER)
    def PlayPause(self):
        self._set_position(self.position())
        self.status = "Paused" if self.status == "Playing" else "Playing"
        self._changed("PlaybackStatus")

    @dbus.service.method(PLAYER)
    def Play(self):
        if self.status != "Playing":
            self.PlayPause()

    @dbus.service.method(PLAYER)
    def Pause(self):
        if self.status == "Playing":
            self.PlayPause()

    @dbus.service.method(PLAYER)
    def Next(self):
        self._go(self.index + 1)

    @dbus.service.method(PLAYER)
    def Previous(self):
        self._go(self.index - 1)

    @dbus.service.method(PLAYER, in_signature="x")
    def Seek(self, offset):
        self.SetPosition(self.metadata()["mpris:trackid"], int(self.position() * 1_000_000) + offset)

    @dbus.service.method(PLAYER, in_signature="ox")
    def SetPosition(self, track_id, position):
        self._set_position(position / 1_000_000)
        self.Seeked(dbus.Int64(position))

    @dbus.service.signal(PLAYER, signature="x")
    def Seeked(self, position):
        pass


def main():
    dbus.mainloop.glib.DBusGMainLoop(set_as_default=True)
    player = FakePlayer(dbus.SessionBus(), paused="--paused" in sys.argv)
    print(f"fake Spotify on {BUS_NAME}: {PLAYLIST[0][0]} — {PLAYLIST[0][1]} ({player.status})", flush=True)

    def advance():
        if player.status == "Playing" and player.position() >= PLAYLIST[player.index][3] - 0.2:
            player._go(player.index + 1)
        return True

    GLib.timeout_add(500, advance)
    GLib.MainLoop().run()


if __name__ == "__main__":
    main()
