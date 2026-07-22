// Robust clipboard copy + a tiny transient toast, shared by the grid
// "Copy with headers" actions. The async Clipboard API can reject with
// "Document is not focused" when a menu/popover closes as we write, so we fall
// back to a hidden-textarea execCommand copy, which only needs the surrounding
// user gesture.
export async function copyToClipboard(text: string): Promise<boolean> {
  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // fall through to the execCommand fallback
    }
  }
  try {
    const ta = document.createElement("textarea");
    ta.value = text;
    ta.style.position = "fixed";
    ta.style.top = "-9999px";
    document.body.appendChild(ta);
    ta.focus();
    ta.select();
    const ok = document.execCommand("copy");
    document.body.removeChild(ta);
    return ok;
  } catch {
    return false;
  }
}

let toastTimer: number | undefined;

// Minimal dependency-free toast — the app has no toast system, and a copy needs
// a "it worked" signal. Single reused node, auto-fades after ~1.6s.
export function flashToast(message: string) {
  let el = document.getElementById("clam-toast");
  if (!el) {
    el = document.createElement("div");
    el.id = "clam-toast";
    el.className =
      "fixed bottom-5 left-1/2 -translate-x-1/2 z-[100000] rounded-full border border-border bg-popover px-4 py-2 text-sm text-popover-foreground shadow-lg transition-opacity duration-200 pointer-events-none";
    document.body.appendChild(el);
  }
  el.textContent = message;
  el.style.opacity = "1";
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => {
    if (el) el.style.opacity = "0";
  }, 1600);
}
