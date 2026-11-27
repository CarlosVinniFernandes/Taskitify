import type { CSSProperties } from "react";

const papers = ["#efebe0", "#e7e8df", "#efe6da", "#ede8e3", "#e9ece7", "#f0ece4"] as const;

export function noteStyle(seed: number) {
  return {
    mount: (seed >>> 23) % 3,
    style: {
      "--tint": papers[(seed >>> 8) % papers.length],
      "--tilt": (((seed >>> 17) % 7 - 3) * .48) + "deg",
      "--note-top": (18 + (seed % 3) * 3) + "px",
    } as CSSProperties,
  };
}
