import { Privacy } from "@/components/privacy";
import { pageMetadata } from "@/lib/site";

export const metadata = pageMetadata("pt-BR", "privacy");
export default function Page() {
  return <Privacy locale="pt-BR" />;
}
