import { copy, type Locale } from "@/lib/copy";
import { ContactLink, SiteShell } from "./site";

export function Privacy({ locale }: { locale: Locale }) {
  const t = copy[locale].privacy;
  return (
    <SiteShell locale={locale} page="privacy">
      <article className="wrap privacy-page">
        <header>
          <h1>{t.title}</h1>
          <p>{t.description}</p>
        </header>
        {t.sections.map((section, i) => (
          <section key={section.title} id={i === t.sections.length - 1 ? "contact" : undefined}>
            <h2>{section.title}</h2>
            <p>{section.text}</p>
          </section>
        ))}
        <ContactLink locale={locale} />
      </article>
    </SiteShell>
  );
}
