import { About } from "@/components/about";
import { pageMetadata } from "@/lib/site";

export const metadata = pageMetadata("pt-BR", "about");

export default function Page() {
  return <About locale="pt-BR" />;
}
