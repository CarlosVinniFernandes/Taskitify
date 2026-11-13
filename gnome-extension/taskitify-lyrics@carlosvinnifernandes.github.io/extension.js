// Taskitify Lyrics: the current synced lyric line in the GNOME top bar, left of the clock.
//
// The Taskitify app (src/Taskitify.Linux) follows Spotify over MPRIS, looks the lyrics up and writes
// the line that is current right now to $XDG_RUNTIME_DIR/taskitify/line.json:
//     {"line": "...", "title": "...", "artist": "...", "playing": true}
// This extension only watches that file and shows it, so it needs no network or D-Bus access.

import GObject from 'gi://GObject';
import St from 'gi://St';
import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Pango from 'gi://Pango';

import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';

Gio._promisify(Gio.File.prototype, 'load_contents_async', 'load_contents_finish');

const LINE_FILE_NAME = 'line.json';
const APP_DIR_NAME = 'taskitify';
const RETRY_SECONDS = 2;
// The clock is centred; leave it (and the Activities button) room on a typical panel.
const CLOCK_AND_ACTIVITIES_PX = 260;
const MIN_LABEL_PX = 160;

const LyricsButton = GObject.registerClass(
class TaskitifyLyricsButton extends PanelMenu.Button {
    _init() {
        // dontCreateMenu: this is a read-out, not a menu.
        super._init(0.0, 'Taskitify Lyrics', true);
        this.add_style_class_name('taskitify-lyrics-button');

        this._label = new St.Label({
            style_class: 'taskitify-lyrics-label',
            y_align: Clutter.ActorAlign.CENTER,
            text: '',
        });
        this._label.clutter_text.ellipsize = Pango.EllipsizeMode.END;
        this.add_child(this._label);
        this.hide();
    }

    setLine(line, playing) {
        if (!line) {
            this.hide();
            return;
        }
        const monitor = Main.layoutManager.primaryMonitor;
        const maxWidth = Math.max(MIN_LABEL_PX, (monitor.width / 2) - CLOCK_AND_ACTIVITIES_PX);
        this._label.set_style(`max-width: ${Math.round(maxWidth)}px;`);
        if (this._label.text !== line) {
            this._label.text = line;
            this._label.opacity = 0;
            this._label.ease({opacity: playing ? 255 : 150, duration: 180, mode: Clutter.AnimationMode.EASE_OUT_QUAD});
        } else {
            this._label.opacity = playing ? 255 : 150;
        }
        this.show();
    }
});

export default class TaskitifyLyricsExtension extends Extension {
    enable() {
        this._button = new LyricsButton();
        // Index 1 in the left box: right after the Activities button, before the clock's centre box.
        Main.panel.addToStatusArea(this.uuid, this._button, 1, 'left');

        this._dir = Gio.File.new_for_path(GLib.build_filenamev([GLib.get_user_runtime_dir(), APP_DIR_NAME]));
        this._file = this._dir.get_child(LINE_FILE_NAME);
        this._monitor = null;
        this._retryId = 0;
        this._cancellable = new Gio.Cancellable();

        this._watch();
        this._read();
    }

    disable() {
        this._cancellable?.cancel();
        this._cancellable = null;
        if (this._retryId) {
            GLib.source_remove(this._retryId);
            this._retryId = 0;
        }
        this._monitor?.cancel();
        this._monitor = null;
        this._button?.destroy();
        this._button = null;
        this._file = null;
        this._dir = null;
    }

    // Watch the directory, not the file: the app replaces the file atomically (write + rename).
    _watch() {
        if (!this._dir.query_exists(null)) {
            // The app has not started yet: look again until its runtime directory shows up.
            this._retryId = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT, RETRY_SECONDS, () => {
                this._retryId = 0;
                this._watch();
                this._read();
                return GLib.SOURCE_REMOVE;
            });
            return;
        }

        try {
            this._monitor = this._dir.monitor_directory(Gio.FileMonitorFlags.WATCH_MOVES, null);
            this._monitor.connect('changed', (_monitor, file, otherFile) => {
                const names = [file?.get_basename(), otherFile?.get_basename()];
                if (names.includes(LINE_FILE_NAME))
                    this._read();
            });
        } catch (e) {
            console.error(`Taskitify Lyrics: cannot watch ${this._dir.get_path()}: ${e.message}`);
        }
    }

    async _read() {
        if (!this._file || !this._button)
            return;
        try {
            const [bytes] = await this._file.load_contents_async(this._cancellable);
            const data = JSON.parse(new TextDecoder().decode(bytes));
            this._button?.setLine(typeof data.line === 'string' ? data.line : '', data.playing !== false);
        } catch (e) {
            // No file (app closed or no Spotify) or half-written JSON: show nothing.
            if (!e.matches?.(Gio.IOErrorEnum, Gio.IOErrorEnum.CANCELLED))
                this._button?.setLine('', false);
        }
    }
}
