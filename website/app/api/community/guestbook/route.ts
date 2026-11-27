import { communityWallEnabled } from "@/lib/community-wall";
import { getGuestbookPage } from "@/lib/community-wall-store";

export const maxDuration = 25;

const headers = { "Cache-Control": "no-store" };

export async function GET(request: Request) {
  if (!communityWallEnabled()) return Response.json({ error: "disabled" }, { status: 404, headers });
  const selected = new URL(request.url).searchParams.get("offset") ?? "0";
  const offset = Number(selected);
  if (!/^\d+$/.test(selected) || !Number.isSafeInteger(offset)) {
    return Response.json({ error: "invalid_offset" }, { status: 400, headers });
  }
  try {
    return Response.json(await getGuestbookPage(offset), { headers });
  } catch {
    return Response.json({ error: "unavailable" }, { status: 503, headers });
  }
}
