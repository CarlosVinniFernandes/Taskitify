import { Home } from "@/components/home";
import { pageMetadata } from "@/lib/site";

export const revalidate = 300;

export const metadata = pageMetadata("en", "home");
export default function Page() {
  return <Home locale="en" />;
}
