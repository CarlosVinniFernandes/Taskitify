import { About } from "@/components/about";
import { pageMetadata } from "@/lib/site";

export const metadata = pageMetadata("en", "about");

export default function Page() {
  return <About locale="en" />;
}
