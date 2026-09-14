import type { ReactNode } from "react";
import { TriangleAlert } from "lucide-react";

/** Amber strip for "worth a look, not broken". No border; the tint carries it. */
export function WarningBanner({ children }: { children: ReactNode }) {
  return (
    <div
      role="status"
      className="flex items-start gap-3 rounded-md bg-amber-500/10 px-4 py-3 text-sm text-amber-700 dark:bg-amber-400/10 dark:text-amber-400"
    >
      <TriangleAlert className="mt-px size-[18px] shrink-0" strokeWidth={2} aria-hidden="true" />
      <div>{children}</div>
    </div>
  );
}
