import { useSyncExternalStore } from "react";

// Tailwind's `md` breakpoint. Pages that render separate mobile/desktop trees
// use this to actually unmount the inactive one — `md:hidden` is CSS-only, so
// both trees still mount and pay full React cost.
const QUERY = "(min-width: 768px)";

const mql = typeof window !== "undefined" ? window.matchMedia(QUERY) : null;

function subscribe(onChange: () => void) {
  mql?.addEventListener("change", onChange);
  return () => mql?.removeEventListener("change", onChange);
}

export function useIsDesktop() {
  return useSyncExternalStore(
    subscribe,
    () => mql?.matches ?? true,
    () => true,
  );
}
