import { Community, type SearchParameters } from "@/components/community";
import { pageMetadata } from "@/lib/site";

export const metadata = pageMetadata("pt-BR", "community");
export default function Page({ searchParams }: { searchParams: SearchParameters }) {
  return <Community locale="pt-BR" searchParams={searchParams} />;
}
