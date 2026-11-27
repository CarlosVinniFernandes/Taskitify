import { notFound } from "next/navigation";
import { ShareViewer } from "@/components/lyrics-share/viewer";
import { shareViewerEnabled } from "@/lib/lyrics-share-experiment";

export default function Page() {
  if (!shareViewerEnabled()) notFound();
  return <ShareViewer />;
}
