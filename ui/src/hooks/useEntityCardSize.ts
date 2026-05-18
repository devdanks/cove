import { useCallback, useEffect, useMemo, useState } from "react";

const MIN_CARD_SIZE_LEVEL = 0;
const MAX_CARD_SIZE_LEVEL = 5;
const DEFAULT_CARD_SIZE_LEVEL = 1;

const ENTITY_ALIASES: Record<string, string> = {
  scenes: "scene",
  scene: "scene",
  images: "image",
  image: "image",
  galleries: "gallery",
  gallery: "gallery",
  performers: "performer",
  performer: "performer",
  studios: "studio",
  studio: "studio",
  tags: "tag",
  tag: "tag",
  groups: "group",
  group: "group",
  audios: "audio",
  audio: "audio",
  texts: "text",
  text: "text",
  faces: "face",
  face: "face",
  segments: "segment",
  segment: "segment",
};

export function clampCardSizeLevel(value: number) {
  return Math.min(MAX_CARD_SIZE_LEVEL, Math.max(MIN_CARD_SIZE_LEVEL, value));
}

function normalizeEntityType(entityType?: string) {
  if (!entityType) return undefined;
  const normalized = entityType.trim().toLowerCase();
  return ENTITY_ALIASES[normalized] ?? normalized.replace(/[^a-z0-9_.-]/g, "-");
}

function readLegacyZoomLevel(legacyPageKey?: string) {
  if (!legacyPageKey) return undefined;
  try {
    const raw = localStorage.getItem(`cove-list-prefs-${legacyPageKey}`);
    if (!raw) return undefined;
    const parsed = JSON.parse(raw) as { zoomLevel?: number };
    return typeof parsed.zoomLevel === "number" ? clampCardSizeLevel(parsed.zoomLevel) : undefined;
  } catch {
    return undefined;
  }
}

function readEntityCardSize(entityType?: string, legacyPageKey?: string, defaultValue = DEFAULT_CARD_SIZE_LEVEL) {
  const normalized = normalizeEntityType(entityType);
  if (typeof window === "undefined" || !normalized) {
    return clampCardSizeLevel(defaultValue);
  }

  try {
    const key = `cove.cardSize.${normalized}`;
    const raw = localStorage.getItem(key);
    if (raw != null) {
      const parsed = Number(raw);
      return Number.isFinite(parsed) ? clampCardSizeLevel(parsed) : clampCardSizeLevel(defaultValue);
    }

    const legacyValue = readLegacyZoomLevel(legacyPageKey);
    if (legacyValue != null) {
      localStorage.setItem(key, String(legacyValue));
      return legacyValue;
    }
  } catch {
    return clampCardSizeLevel(defaultValue);
  }

  return clampCardSizeLevel(defaultValue);
}

export function useEntityCardSize(entityType?: string, legacyPageKey?: string, defaultValue = DEFAULT_CARD_SIZE_LEVEL) {
  const normalizedEntityType = useMemo(() => normalizeEntityType(entityType), [entityType]);
  const [level, setLevelState] = useState(() => readEntityCardSize(normalizedEntityType, legacyPageKey, defaultValue));

  useEffect(() => {
    setLevelState(readEntityCardSize(normalizedEntityType, legacyPageKey, defaultValue));
  }, [defaultValue, legacyPageKey, normalizedEntityType]);

  const setLevel = useCallback((value: number | ((current: number) => number)) => {
    setLevelState((current) => {
      const nextValue = typeof value === "function" ? value(current) : value;
      const next = clampCardSizeLevel(nextValue);
      if (typeof window !== "undefined" && normalizedEntityType) {
        try {
          localStorage.setItem(`cove.cardSize.${normalizedEntityType}`, String(next));
        } catch {
          // Ignore storage write failures.
        }
      }
      return next;
    });
  }, [normalizedEntityType]);

  return [level, setLevel] as const;
}