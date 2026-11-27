import { communityWallEnabled } from "@/lib/community-wall";
import { getWallPage } from "@/lib/community-wall-store";

const headers = { "Cache-Control": "no-store" };

export async function GET(request: Request) {
  if (!communityWallEnabled()) return Response.json({ error: "disabled" }, { status: 404, headers });
  const requested = new URL(request.url).searchParams.get("page") ?? "1";
  const page = Number(requested);
  if (!/^[1-9]\d*$/.test(requested) || !Number.isSafeInteger(page)) {
    return Response.json({ error: "invalid_page" }, { status: 400, headers });
  }
  try {
    return Response.json(await getWallPage(page), { headers });
  } catch {
    return Response.json({ error: "unavailable" }, { status: 503, headers });
  }
}
