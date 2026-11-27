"use client";

import { useRef, useState, type FormEvent } from "react";
import { Music2 } from "lucide-react";
import { copy, type Locale } from "@/lib/copy";
import { limits, type Fields } from "@/lib/suggestion-fields";

type Feedback = "idle" | "sending" | "success" | "invalid" | "rate" | "error";

export function SongForm({ locale, initial }: { locale: Locale; initial: Fields }) {
  const t = copy[locale].community;
  const [state, setState] = useState<Feedback>("idle");
  const sending = useRef(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (sending.current) return;
    const form = event.currentTarget;
    const values = new FormData(form);
    sending.current = true;
    setState("sending");
    try {
      const response = await fetch("/api/community/suggestions", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(Object.fromEntries(values)),
      });
      const result: unknown = await response.json().catch(() => null);
      if (response.status === 201 && result && typeof result === "object" && "ok" in result && result.ok === true) {
        form.reset();
        for (const key of Object.keys(limits)) {
          const field = form.elements.namedItem(key);
          if (field instanceof HTMLInputElement) field.value = "";
        }
        setState("success");
      } else {
        setState(response.status === 429 ? "rate" : [400, 403, 413, 415].includes(response.status) ? "invalid" : "error");
      }
    } catch {
      setState("error");
    } finally {
      sending.current = false;
    }
  }

  return <form className="song-form" onSubmit={submit} aria-busy={state === "sending"} aria-describedby="form-note">
    <fieldset disabled={state === "sending"}>
      <div className="form-pair">
        <label htmlFor="song">{t.song}
          <input id="song" name="song" required maxLength={limits.song} defaultValue={initial.song}
                 placeholder={t.songPlaceholder} autoComplete="off" />
        </label>
        <label htmlFor="artist">{t.artist}
          <input id="artist" name="artist" required maxLength={limits.artist} defaultValue={initial.artist}
                 placeholder={t.artistPlaceholder} autoComplete="off" />
        </label>
      </div>
      <label htmlFor="name">{t.nickname} <span className="optional">({t.optional})</span>
        <input id="name" name="name" maxLength={limits.name} defaultValue={initial.name}
               placeholder={t.namePlaceholder} autoComplete="nickname" />
      </label>
      <div className="honeypot" aria-hidden="true">
        <label htmlFor="website">Website
          <input id="website" name="website" tabIndex={-1} autoComplete="off" />
        </label>
      </div>
      <p id="form-note" className="form-note">{t.note}</p>
      <button type="submit" className="button button-light">
        <Music2 size={17} aria-hidden="true" /> {state === "sending" ? t.sending : t.send}
      </button>
    </fieldset>
    <p className={`feedback ${state === "success" ? "feedback-success" : ""}`} role="status">
      {state === "idle" || state === "sending" ? "" : t[state]}
    </p>
  </form>;
}
