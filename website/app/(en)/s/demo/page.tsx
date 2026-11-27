import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { gzipSync } from "node:zlib";
import styles from "@/components/lyrics-share/viewer.module.css";

export const metadata: Metadata = { title: "Send a Song · Demonstrações locais", robots: { index: false, follow: false } };

const demos = [
  {
    v: 1, t: "Letter", s: { t: "Quando a rua se aquieta", a: "Clara do Norte" },
    l: [{ x: "Deixei a luz da cozinha acesa" }, { x: "Para quando a saudade voltar" }],
    x: { to: "Ana", hello: "Ana,", bye: "Com carinho,", sign: "Carlos", ps: "Ainda guardo aquele ingresso." },
    d: { msg: "Pensei em você quando ouvi essa música. Faz tempo que a gente não senta pra conversar." },
    o: { mark: "none" }, c: "2026-11-27", lang: "pt-BR",
  },
  {
    v: 1, t: "CassetteTape", f: "landscape",
    s: { t: "A estrada de volta", a: "Fita de Domingo", dur: 244000 },
    l: [{ x: "A gente errou a saída" }, { x: "E deixou a fita tocar" }],
    x: { model: "Red", sticker: "Rainbow", title: "Domingo, lado B", side: "B", to: "Pra próxima viagem" },
    d: { from: "Carlos", to: "Bia", msg: "Pra ouvir com a janela aberta, igual daquela vez." },
    o: { mark: "none" }, lang: "pt-BR",
  },
  {
    v: 1, t: "Retro", f: "portrait",
    s: { t: "Antes de desligar", a: "Sinal de Ocupado" },
    l: [{ x: "Se a noite apagar tudo", ms: 83000 }, { x: "Deixa a música ligada" }],
    x: { title: "lembrei_de_voce.txt" }, o: { color: "#426C69", time: true, mark: "none" },
    d: { from: "Carlos", msg: "Achei essa hoje. Lembrei das nossas madrugadas no computador." },
    lang: "pt-BR",
  },
];

export default function DemoPage() {
  if (process.env.NODE_ENV !== "development" || process.env.VERCEL_ENV === "production") notFound();
  return <main className={styles.scene} lang="pt-BR">
    <section className={styles.demoList}>
      <h1>Send a Song</h1>
      {demos.map((demo) => <section key={demo.t} id={demo.t.toLowerCase()}>
        <h2>{demo.t}</h2>
        <div>{(["surprise", "direct"] as const).map((r) => {
          const code = gzipSync(JSON.stringify({ ...demo, r })).toString("base64url");
          return <a className={styles.about} key={r} href={`/s#${code}`}>
            {r === "surprise" ? "Abrir surpresa" : "Ver direto"}
          </a>;
        })}</div>
      </section>)}
    </section>
  </main>;
}
