import { useEffect, useState } from "react";
import { useAuth } from "../auth/AuthContext";
import { useAppConfig } from "../state/AppConfigContext";
import { normalizeShortcutSequence } from "../keyboard/keybindings";
import { supportsServerBackedUiPreferences } from "../utils/userUiPreferences";

const STORAGE_KEY = "cove-keybinding-overrides";
const CHANGE_EVENT = "cove:keybinding-overrides-changed";

export function normalizeKeybindingOverrideMap(overrides?: Record<string, string> | null) {
  return Object.fromEntries(
    Object.entries(overrides ?? {})
      .map(([key, value]) => [key.trim(), normalizeShortcutSequence(value)] as const)
      .filter(([key, value]) => key.length > 0 && value.length > 0),
  );
}

export function readStoredKeybindingOverrides() {
  if (typeof window === "undefined") {
    return {};
  }

  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return {};
    }

    return normalizeKeybindingOverrideMap(JSON.parse(raw) as Record<string, string>);
  } catch {
    return {};
  }
}

export function writeStoredKeybindingOverrides(overrides?: Record<string, string> | null) {
  if (typeof window === "undefined") {
    return;
  }

  const normalized = normalizeKeybindingOverrideMap(overrides);
  if (Object.keys(normalized).length === 0) {
    window.localStorage.removeItem(STORAGE_KEY);
  } else {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(normalized));
  }

  window.dispatchEvent(new CustomEvent(CHANGE_EVENT));
}

export function useResolvedKeybindingOverrides() {
  const { user } = useAuth();
  const { config } = useAppConfig();
  const [storedOverrides, setStoredOverrides] = useState<Record<string, string>>(() => readStoredKeybindingOverrides());

  useEffect(() => {
    if (typeof window === "undefined") {
      return;
    }

    const sync = () => setStoredOverrides(readStoredKeybindingOverrides());
    window.addEventListener(CHANGE_EVENT, sync);
    window.addEventListener("storage", sync);
    return () => {
      window.removeEventListener(CHANGE_EVENT, sync);
      window.removeEventListener("storage", sync);
    };
  }, []);

  const configOverrides = normalizeKeybindingOverrideMap(config?.ui.keybindingOverrides);
  if (supportsServerBackedUiPreferences(user)) {
    const userOverrides = normalizeKeybindingOverrideMap(user.uiPreferences?.keybindingOverrides);
    return Object.keys(userOverrides).length > 0 ? userOverrides : configOverrides;
  }

  return Object.keys(storedOverrides).length > 0 ? storedOverrides : configOverrides;
}
