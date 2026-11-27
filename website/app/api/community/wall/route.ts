import { communityWallEnabled, communityWallPageSizes } from "@/lib/community-wall";
import { getWallPage } from "@/lib/community-wall-store";

const headers = { "Cache-Control": "no-store" };

export async function GET(request: Request) {
  if (!communityWallEnabled()) return Response.json({ error: "disabled" }, { status: 404, headers });
  const params = new URL(request.url).searchParams;
  const requested = params.get("page") ?? "1";
  const size = params.get("size");
  const page = Number(requested);
  if (!/^[1-9]\d*$/.test(requested) || !Number.isSafeInteger(page)) {
    return Response.json({ error: "invalid_page" }, { status: 400, headers });
  }
  const { preview, full } = communityWallPageSizes;
  if (size !== null && size !== String(full) && size !== String(preview)) {
    return Response.json({ error: "invalid_size" }, { status: 400, headers });
  }
  try {
    return Response.json(await getWallPage(page, size === String(full) ? full : preview), { headers });
  } catch {
    return Response.json({ error: "unavailable" }, { status: 503, headers });
  }
}
