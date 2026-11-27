"use server";

import { BlobPreconditionFailedError } from "@vercel/blob";
import { revalidateTag } from "next/cache";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { adminCookie, adminKey, adminPath, issueSession, matchesKey, sessionSeconds, validSession } from "@/lib/community-admin-session";
import { updateAdminWall } from "@/lib/community-admin-store";
import { wallEntryTag } from "@/lib/community-wall";

const cookieOptions = {
  httpOnly: true,
  sameSite: "strict" as const,
  secure: process.env.VERCEL === "1",
  path: adminPath,
};

export async function signIn(data: FormData) {
  const key = adminKey();
  if (!key) redirect(adminPath);
  if (!matchesKey(data.get("key"), key)) redirect(adminPath + "?notice=incorrect");
  (await cookies()).set(adminCookie, issueSession(key), { ...cookieOptions, maxAge: sessionSeconds });
  redirect(adminPath);
}

export async function signOut() {
  (await cookies()).set(adminCookie, "", { ...cookieOptions, maxAge: 0 });
  redirect(adminPath);
}

export async function save(data: FormData) {
  const key = adminKey();
  if (!key || !validSession((await cookies()).get(adminCookie)?.value, key)) redirect(adminPath);
  let notice: string;
  try {
    notice = await updateAdminWall(data);
    if (notice === "saved") revalidateTag(wallEntryTag, { expire: 0 });
  } catch (error) {
    notice = error instanceof BlobPreconditionFailedError ? "changed" : "unavailable";
  }
  const cursor = data.get("cursor");
  const params = new URLSearchParams({ notice });
  if (typeof cursor === "string" && cursor.length <= 2048 && cursor) params.set("cursor", cursor);
  redirect(`${adminPath}?${params}`);
}
