import { notFound } from "next/navigation";
import { ShareViewer } from "@/components/lyrics-share/viewer";
import { shortSharesEnabled } from "@/lib/lyrics-share-experiment";

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  if (!shortSharesEnabled()) notFound();
  const { id } = await params;
  return <ShareViewer shortId={id} />;
}
