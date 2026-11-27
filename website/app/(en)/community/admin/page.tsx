import type { Metadata } from "next";
import { cookies } from "next/headers";
import { notFound } from "next/navigation";
import { adminCookie, adminKey, adminPath, validSession } from "@/lib/community-admin-session";
import { adminStatuses, getAdminWall } from "@/lib/community-admin-store";
import { limits } from "@/lib/suggestion-fields";
import { save, signIn, signOut } from "./actions";
import styles from "./moderation.module.css";

export const dynamic = "force-dynamic";

export const metadata: Metadata = {
  title: "Moderação do mural · Taskitify",
  robots: { index: false, follow: false },
  referrer: "no-referrer",
};

const labels = {
  suggested: "Sugerida", added: "Adicionada à playlist", hidden: "Oculta",
};
const messages: Record<string, string> = {
  saved: "Alterações salvas.",
  invalid: "Confira o título, o artista e o status.",
  changed: "Essa contribuição mudou. Confira os dados atuais e tente novamente.",
  unavailable: "Não foi possível salvar agora. Tente novamente.",
};

export default async function Page({ searchParams }: { searchParams: Promise<{ cursor?: string; notice?: string }> }) {
  const key = adminKey();
  if (!key) notFound();
  const { cursor: rawCursor, notice } = await searchParams;
  const cursor = typeof rawCursor === "string" && rawCursor.length <= 2048 ? rawCursor : undefined;
  const session = (await cookies()).get(adminCookie)?.value;
  if (!validSession(session, key)) {
    return <main className={`wrap ${styles.moderation}`} lang="pt-BR">
      <h1>Moderação do mural</h1>
      <form className="song-form" action={signIn}>
        <fieldset>
          <label htmlFor="admin-key">Chave de administração
            <input id="admin-key" name="key" type="password" required maxLength={128} autoComplete="current-password" />
          </label>
          <button className="button button-light" type="submit">Entrar</button>
          {notice === "incorrect" && <p role="alert">Chave inválida.</p>}
        </fieldset>
      </form>
    </main>;
  }
  let wall: Awaited<ReturnType<typeof getAdminWall>> | undefined;
  try { wall = await getAdminWall(cursor); } catch { /* Keep private storage errors out of the page. */ }
  const feedback = typeof notice === "string" && Object.hasOwn(messages, notice) ? messages[notice] : undefined;
  return <main className={`wrap ${styles.moderation}`} lang="pt-BR">
    <header className={styles.heading}>
      <h1>Moderação do mural</h1>
      <form action={signOut}><button className="button button-outline" type="submit">Sair</button></form>
    </header>
    {feedback && <p role="status">{feedback}</p>}
    {!wall && <p>Não foi possível carregar as sugestões. Recarregue a página.</p>}
    {wall && wall.entries.length === 0 && <p>Nenhuma sugestão nesta página.</p>}
    {wall?.entries.map((entry) => <form key={entry.pathname} className="song-form" action={save}>
      <fieldset>
        <input type="hidden" name="path" value={entry.pathname} />
        <input type="hidden" name="etag" value={entry.etag} />
        <input type="hidden" name="cursor" value={cursor ?? ""} />
        <div className="form-pair">
          <label>Música<input name="song" required maxLength={limits.song} defaultValue={entry.song} /></label>
          <label>Artista<input name="artist" required maxLength={limits.artist} defaultValue={entry.artist} /></label>
        </div>
        <div className={styles.statusRow}>
          <label>Status<select name="status" defaultValue={entry.status}>
            {adminStatuses.map((status) => <option value={status} key={status}>{labels[status]}</option>)}
          </select></label>
          <button className="button button-light" type="submit">Salvar</button>
        </div>
      </fieldset>
    </form>)}
    <nav className={styles.navigation} aria-label="Páginas das sugestões">
      {cursor && <a className="text-link" href={adminPath}>Primeira página</a>}
      {wall?.next && <a className="text-link" href={`${adminPath}?cursor=${encodeURIComponent(wall.next)}`}>Mais antigas</a>}
    </nav>
  </main>;
}
