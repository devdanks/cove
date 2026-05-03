import { useEffect, useState } from "react";
import type { MediaDetailKeyboardShortcut } from "./types";

interface UseMediaDetailLayoutOptions {
  theaterModeSupported?: boolean;
  isTheaterMode?: boolean;
  onTheaterModeToggle?: (value: boolean) => void;
  keyboardShortcuts?: MediaDetailKeyboardShortcut[];
}

export function useMediaDetailLayout({
  theaterModeSupported,
  isTheaterMode,
  onTheaterModeToggle,
  keyboardShortcuts = [],
}: UseMediaDetailLayoutOptions) {
  const [internalTheaterMode, setInternalTheaterMode] = useState(false);
  const theaterMode = isTheaterMode ?? internalTheaterMode;

  useEffect(() => {
    if (!theaterModeSupported) {
      return;
    }

    document.documentElement.classList.toggle("theater-mode", theaterMode);
    return () => document.documentElement.classList.remove("theater-mode");
  }, [theaterMode, theaterModeSupported]);

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

  const setTheaterMode = (value: boolean) => {
    if (isTheaterMode === undefined) {
      setInternalTheaterMode(value);
    }

    onTheaterModeToggle?.(value);
  };

  return {
    theaterMode,
    setTheaterMode,
  };
}