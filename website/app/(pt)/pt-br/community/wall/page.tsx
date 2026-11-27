import { FullCommunityWall } from "@/components/full-community-wall";
import { pageMetadata } from "@/lib/site";

export const metadata = pageMetadata("pt-BR", "wall");

export default function Page({ searchParams }: { searchParams: Promise<{ page?: string | string[] }> }) {
  return <FullCommunityWall locale="pt-BR" searchParams={searchParams} />;
}
