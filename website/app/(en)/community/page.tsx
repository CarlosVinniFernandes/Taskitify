import { Community, type SearchParameters } from "@/components/community";
import { pageMetadata } from "@/lib/site";

export const metadata = pageMetadata("en", "community");
export default function Page({ searchParams }: { searchParams: SearchParameters }) {
  return <Community locale="en" searchParams={searchParams} />;
}
