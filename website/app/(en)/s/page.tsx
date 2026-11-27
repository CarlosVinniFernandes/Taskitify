import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { ShareViewer } from "@/components/lyrics-share/viewer";
import { shareCopy } from "@/lib/lyrics-share-copy";
import { siteOrigin } from "@/lib/site";

const { sent: title, feelings: description } = shareCopy.en;
const origin = siteOrigin();
const images = origin ? [{
  url: new URL("/send-a-song-social.png", origin),
  width: 1200,
  height: 630,
  alt: "A sealed envelope with a music note",
}] : undefined;

export const metadata: Metadata = {
  title,
  description,
  robots: { index: false, follow: false },
  referrer: "no-referrer",
  openGraph: { type: "website", siteName: "Taskitify", title, description, images },
  twitter: { card: "summary_large_image", title, description, images },
};

export default function Page() {
  if (process.env.VERCEL_ENV === "production"
      || (process.env.NODE_ENV !== "development" && process.env.LYRICS_SHARE_EXPERIMENT !== "1")) {
    notFound();
  }
  return <ShareViewer />;
}
