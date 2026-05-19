import { useEffect } from "react";
import type { MediaDetailKeyboardShortcut } from "./types";

interface UseMediaDetailLayoutOptions {
  keyboardShortcuts?: MediaDetailKeyboardShortcut[];
}

export function useMediaDetailLayout({
  keyboardShortcuts = [],
}: UseMediaDetailLayoutOptions) {
  useEffect(() => {
    if (keyboardShortcuts.length === 0) {
      return;
    }

    const handler = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const tagName = target?.tagName;
      if (tagName === "INPUT" || tagName === "TEXTAREA" || tagName === "SELECT" || target?.isContentEditable) {
        return;
      }

      const shortcut = keyboardShortcuts.find((entry) => entry.key === event.key);
      if (!shortcut) {
        return;
      }

      event.preventDefault();
      shortcut.handler();
    };

    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [keyboardShortcuts]);
}