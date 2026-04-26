import { scenes } from "../api/client";
import type { DownloaderMatch, Scene, ScrapeAttempt, ScraperSummary } from "../api/types";

export type InputKind = "url" | "name" | "fragment";
export type BatchInputKind = Exclude<InputKind, "fragment">;
export type CollectionMode = "skip" | "merge" | "replace";

export type SceneScrapeScene = Pick<
  Scene,
  "id" | "title" | "code" | "details" | "director" | "date" | "organized" | "studioName" | "urls" | "tags" | "performers" | "files" | "updatedAt"
>;

export interface ScraperPreference {
  site: string;
  scraperId: string;
}

export interface SceneReviewData {
  title?: string;
  code?: string;
  details?: string;
  director?: string;
  date?: string;
  image?: string;
  studio?: string;
  urls: string[];
  tags: string[];
  performers: string[];
  raw: Record<string, unknown> | null;
}

export interface ScrapeApplyPreferences {
  createMissingTags: boolean;
  createMissingPerformers: boolean;
  createMissingStudio: boolean;
  markOrganized: boolean;
  hydratePerformers: boolean;
}

export interface SceneApplyPlan {
  currentData: SceneReviewData;
  scrapedData: SceneReviewData | null;
  replaceFields: string[];
  collectionModes: Record<string, CollectionMode>;
}

export const DEFAULT_COLLECTION_MODES: Record<string, CollectionMode> = {
  studio: "skip",
  urls: "skip",
  tags: "skip",
  performers: "skip",
};

export const DEFAULT_SCRAPE_APPLY_PREFERENCES: ScrapeApplyPreferences = {
  createMissingStudio: true,
  createMissingTags: true,
  createMissingPerformers: true,
  markOrganized: false,
  hydratePerformers: false,
};

const SCRAPE_PREFERENCES_STORAGE_KEY = "cove.sceneScrapePreferences";

export function loadScrapeApplyPreferences(): ScrapeApplyPreferences {
  if (typeof window === "undefined") {
    return DEFAULT_SCRAPE_APPLY_PREFERENCES;
  }

  try {
    const raw = window.localStorage.getItem(SCRAPE_PREFERENCES_STORAGE_KEY);
    if (!raw) {
      return DEFAULT_SCRAPE_APPLY_PREFERENCES;
    }

    const parsed = JSON.parse(raw) as Partial<ScrapeApplyPreferences>;
    return {
      createMissingStudio: parsed.createMissingStudio ?? DEFAULT_SCRAPE_APPLY_PREFERENCES.createMissingStudio,
      createMissingTags: parsed.createMissingTags ?? DEFAULT_SCRAPE_APPLY_PREFERENCES.createMissingTags,
      createMissingPerformers: parsed.createMissingPerformers ?? DEFAULT_SCRAPE_APPLY_PREFERENCES.createMissingPerformers,
      markOrganized: parsed.markOrganized ?? DEFAULT_SCRAPE_APPLY_PREFERENCES.markOrganized,
      hydratePerformers: parsed.hydratePerformers ?? DEFAULT_SCRAPE_APPLY_PREFERENCES.hydratePerformers,
    };
  } catch {
    return DEFAULT_SCRAPE_APPLY_PREFERENCES;
  }
}

export function saveScrapeApplyPreferences(preferences: ScrapeApplyPreferences) {
  if (typeof window === "undefined") {
    return;
  }

  try {
    window.localStorage.setItem(SCRAPE_PREFERENCES_STORAGE_KEY, JSON.stringify(preferences));
  } catch {
    // Ignore localStorage failures.
  }
}

export function parseJsonObject(json?: string | null): Record<string, unknown> | null {
  if (!json) {
    return null;
  }

  try {
    const parsed = JSON.parse(json);
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

export function parseJsonObjectArray(json?: string | null): Record<string, unknown>[] {
  if (!json) {
    return [];
  }

  try {
    const parsed = JSON.parse(json);
    return Array.isArray(parsed)
      ? parsed.filter((item): item is Record<string, unknown> => Boolean(item && typeof item === "object" && !Array.isArray(item)))
      : [];
  } catch {
    return [];
  }
}

export function getValue(object: Record<string, unknown> | null, ...names: string[]) {
  if (!object) {
    return undefined;
  }

  const normalized = names.map((name) => name.toLowerCase());
  for (const [key, value] of Object.entries(object)) {
    if (normalized.includes(key.toLowerCase())) {
      return value;
    }
  }

  return undefined;
}

export function getString(object: Record<string, unknown> | null, ...names: string[]) {
  const value = getValue(object, ...names);
  if (typeof value === "string") {
    const trimmed = value.trim();
    return trimmed ? trimmed : undefined;
  }
  if (typeof value === "number" || typeof value === "boolean") {
    return String(value);
  }
  return undefined;
}

export function getStringList(object: Record<string, unknown> | null, ...names: string[]) {
  const value = getValue(object, ...names);
  if (Array.isArray(value)) {
    return value
      .map((item) => (typeof item === "string" ? item : typeof item === "number" || typeof item === "boolean" ? String(item) : undefined))
      .filter((item): item is string => Boolean(item?.trim()))
      .map((item) => item.trim())
      .filter((item, index, items) => items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
  }
  if (typeof value === "string") {
    return value
      .split(",")
      .map((item) => item.trim())
      .filter(Boolean)
      .filter((item, index, items) => items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
  }
  return [];
}

export function getNamedList(object: Record<string, unknown> | null, ...names: string[]) {
  const value = getValue(object, ...names);
  if (typeof value === "string") {
    return value
      .split(",")
      .map((item) => item.trim())
      .filter(Boolean)
      .filter((item, index, items) => items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
  }
  if (!Array.isArray(value)) {
    return [];
  }
  const items = value
    .map((item) => {
      if (typeof item === "string") {
        return item.trim();
      }
      if (item && typeof item === "object") {
        const candidate = getString(item as Record<string, unknown>, "Name", "name", "Title", "title");
        return candidate?.trim();
      }
      return undefined;
    })
    .filter((item): item is string => Boolean(item));
  return items.filter((item, index) => items.findIndex((candidate) => candidate.toLowerCase() === item.toLowerCase()) === index);
}

export function normalizeSceneDate(value?: string | null) {
  const trimmed = value?.trim();
  if (!trimmed) {
    return undefined;
  }

  const compact = trimmed.match(/^(\d{4})(\d{2})(\d{2})$/);
  if (compact) {
    return `${compact[1]}-${compact[2]}-${compact[3]}`;
  }

  const iso = trimmed.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (iso) {
    return `${iso[1]}-${iso[2]}-${iso[3]}`;
  }

  const parsed = new Date(trimmed);
  if (Number.isNaN(parsed.getTime())) {
    return trimmed;
  }

  const year = parsed.getFullYear();
  const month = String(parsed.getMonth() + 1).padStart(2, "0");
  const day = String(parsed.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function normalizeAttemptData(attempt?: ScrapeAttempt | null, rawOverride?: Record<string, unknown> | null): SceneReviewData | null {
  const raw = rawOverride ?? parseJsonObject(attempt?.resultJson);
  if (!raw) {
    return null;
  }
  return {
    title: getString(raw, "Title", "Name"),
    code: getString(raw, "Code"),
    details: getString(raw, "Details", "Description", "Synopsis"),
    director: getString(raw, "Director"),
    date: normalizeSceneDate(getString(raw, "Date", "ReleaseDate")),
    image: getString(raw, "Image", "ImageUrl", "ImageURL"),
    studio: getNamedList(raw, "Studio", "StudioName")[0] ?? getString(raw, "Studio", "StudioName"),
    urls: getStringList(raw, "URLs", "Url", "URL"),
    tags: getNamedList(raw, "Tags", "Tag", "TagNames"),
    performers: getNamedList(raw, "Performers", "Performer", "PerformerNames"),
    raw,
  };
}

export function getAttemptCandidates(attempt?: ScrapeAttempt | null): SceneReviewData[] {
  const candidatePayloads = parseJsonObjectArray(attempt?.candidateResultsJson);
  if (candidatePayloads.length > 0) {
    return candidatePayloads
      .map((payload) => normalizeAttemptData(undefined, payload))
      .filter((candidate): candidate is SceneReviewData => candidate !== null);
  }

  const single = normalizeAttemptData(attempt);
  return single ? [single] : [];
}

export function normalizeSceneSnapshot(scene: SceneScrapeScene, attempt?: ScrapeAttempt | null): SceneReviewData {
  const snapshot = parseJsonObject(attempt?.entitySnapshotJson);
  return {
    title: getString(snapshot, "title") ?? scene.title,
    code: getString(snapshot, "code") ?? scene.code,
    details: getString(snapshot, "details") ?? scene.details,
    director: getString(snapshot, "director") ?? scene.director,
    date: normalizeSceneDate(getString(snapshot, "date") ?? scene.date),
    image: getString(snapshot, "image", "imageUrl", "imageURL") ?? scenes.screenshotUrl(scene.id, scene.updatedAt),
    studio: getString(snapshot, "studio") ?? scene.studioName,
    urls: getStringList(snapshot, "urls").length > 0 ? getStringList(snapshot, "urls") : scene.urls,
    tags: getNamedList(snapshot, "tags").length > 0 ? getNamedList(snapshot, "tags") : scene.tags.map((tag) => tag.name),
    performers:
      getNamedList(snapshot, "performers").length > 0
        ? getNamedList(snapshot, "performers")
        : scene.performers.map((performer) => performer.name),
    raw: snapshot,
  };
}

export function buildFragmentDraft(scene: SceneScrapeScene) {
  return JSON.stringify(
    {
      title: scene.title ?? "",
      name: scene.title ?? scene.files[0]?.basename ?? "",
      filename: scene.files[0]?.basename ?? "",
      path: scene.files[0]?.path ?? "",
      code: scene.code ?? "",
      details: scene.details ?? "",
      director: scene.director ?? "",
      date: scene.date ?? "",
      url: scene.urls[0] ?? "",
      urls: scene.urls,
      studio: scene.studioName ?? "",
    },
    null,
    2,
  );
}

export function supportsScrapeKind(scraper: ScraperSummary | undefined, kind: InputKind) {
  if (!scraper) {
    return false;
  }
  const required = kind === "url" ? "url" : kind === "name" ? "name" : "fragment";
  return scraper.supportedScrapes.some((value) => value.toLowerCase() === required);
}

export function matchesUrlPattern(scraper: ScraperSummary, url: string) {
  const normalizedUrl = url.trim().toLowerCase();
  if (!normalizedUrl) {
    return false;
  }

  return scraper.urls.some((pattern) => {
    const normalizedPattern = pattern.trim().toLowerCase();
    if (!normalizedPattern) {
      return false;
    }

    const fragments = normalizedPattern.split("*").filter(Boolean);
    return fragments.length > 0 && fragments.every((fragment) => normalizedUrl.includes(fragment));
  });
}

export function getScraperSiteKey(value: string | undefined) {
  const trimmed = value?.trim().toLowerCase();
  if (!trimmed) {
    return "";
  }

  try {
    const parsed = new URL(trimmed.startsWith("http://") || trimmed.startsWith("https://") ? trimmed : `https://${trimmed}`);
    return parsed.hostname.replace(/^www\./, "");
  } catch {
    return trimmed
      .replace(/^https?:\/\//, "")
      .replace(/^www\./, "")
      .split(/[/?#*]/)[0] ?? "";
  }
}

export function listsEqual(left: string[], right: string[]) {
  if (left.length !== right.length) {
    return false;
  }
  const normalizedLeft = [...left].map((item) => item.toLowerCase()).sort();
  const normalizedRight = [...right].map((item) => item.toLowerCase()).sort();
  return normalizedLeft.every((item, index) => item === normalizedRight[index]);
}

export function buildDefaultSceneApplyPlan(
  scene: SceneScrapeScene,
  attempt?: ScrapeAttempt | null,
  selectedPayload?: Record<string, unknown> | null,
): SceneApplyPlan {
  const currentData = normalizeSceneSnapshot(scene, attempt);
  const scrapedData = normalizeAttemptData(attempt, selectedPayload);

  if (!scrapedData) {
    return {
      currentData,
      scrapedData: null,
      replaceFields: [],
      collectionModes: { ...DEFAULT_COLLECTION_MODES },
    };
  }

  const replaceFields: string[] = [];
  if (scrapedData.title && scrapedData.title !== currentData.title) replaceFields.push("title");
  if (scrapedData.code && scrapedData.code !== currentData.code) replaceFields.push("code");
  if (scrapedData.details && scrapedData.details !== currentData.details) replaceFields.push("details");
  if (scrapedData.director && scrapedData.director !== currentData.director) replaceFields.push("director");
  if (scrapedData.date && scrapedData.date !== currentData.date) replaceFields.push("date");
  if (scrapedData.image) replaceFields.push("image");

  return {
    currentData,
    scrapedData,
    replaceFields,
    collectionModes: {
      studio: scrapedData.studio && scrapedData.studio !== currentData.studio ? "replace" : "skip",
      urls: scrapedData.urls.length > 0 && !listsEqual(scrapedData.urls, currentData.urls) ? "merge" : "skip",
      tags: scrapedData.tags.length > 0 && !listsEqual(scrapedData.tags, currentData.tags) ? "merge" : "skip",
      performers: scrapedData.performers.length > 0 && !listsEqual(scrapedData.performers, currentData.performers) ? "merge" : "skip",
    },
  };
}

function getScraperSpecificity(scraper: ScraperSummary, sceneUrl?: string) {
  const normalizedUrl = sceneUrl?.trim().toLowerCase();
  if (!normalizedUrl) {
    return 0;
  }

  return scraper.urls.reduce((bestScore, pattern) => {
    const normalizedPattern = pattern.trim().toLowerCase();
    if (!normalizedPattern) {
      return bestScore;
    }

    const fragments = normalizedPattern.split("*").filter(Boolean);
    if (fragments.length === 0 || !fragments.every((fragment) => normalizedUrl.includes(fragment))) {
      return bestScore;
    }

    const score = fragments.length * 1000 + fragments.reduce((sum, fragment) => sum + fragment.length, 0);
    return Math.max(bestScore, score);
  }, 0);
}

function getConfiguredScraperId(scrapers: ScraperSummary[], sceneUrl: string | undefined, scraperPreferences: ScraperPreference[]) {
  const site = getScraperSiteKey(sceneUrl);
  if (!site) {
    return "";
  }

  const configuredScraperId = scraperPreferences.find((preference) => preference.site === site)?.scraperId;
  return configuredScraperId && scrapers.some((scraper) => scraper.id === configuredScraperId) ? configuredScraperId : "";
}

export function sortScrapersForScene(scrapers: ScraperSummary[], sceneUrl: string | undefined, scraperPreferences: ScraperPreference[] = []) {
  const configuredScraperId = getConfiguredScraperId(scrapers, sceneUrl, scraperPreferences);

  return [...scrapers].sort((left, right) => {
    const leftConfigured = configuredScraperId !== "" && left.id === configuredScraperId;
    const rightConfigured = configuredScraperId !== "" && right.id === configuredScraperId;
    if (leftConfigured !== rightConfigured) {
      return leftConfigured ? -1 : 1;
    }

    const specificityDelta = getScraperSpecificity(right, sceneUrl) - getScraperSpecificity(left, sceneUrl);
    if (specificityDelta !== 0) {
      return specificityDelta;
    }

    return left.name.localeCompare(right.name);
  });
}

export function findPreferredScraperId(scrapers: ScraperSummary[], sceneUrl: string | undefined, scraperPreferences: ScraperPreference[] = []) {
  if (scrapers.length === 0) {
    return "";
  }

  return sortScrapersForScene(scrapers, sceneUrl, scraperPreferences)[0]?.id ?? "";
}

export function findDefaultKind(scraper: ScraperSummary | undefined, preferred: InputKind): InputKind {
  if (!scraper) {
    return preferred;
  }
  if (supportsScrapeKind(scraper, preferred)) {
    return preferred;
  }
  if (supportsScrapeKind(scraper, "url")) {
    return "url";
  }
  if (supportsScrapeKind(scraper, "name")) {
    return "name";
  }
  return "fragment";
}

export function sortDownloaderMatches(matches: DownloaderMatch[]) {
  return [...matches].sort((left, right) => {
    const leftLabel = (left.label || left.downloaderName).toLowerCase();
    const rightLabel = (right.label || right.downloaderName).toLowerCase();
    const labelDelta = leftLabel.localeCompare(rightLabel);
    if (labelDelta !== 0) {
      return labelDelta;
    }

    return left.downloaderId.localeCompare(right.downloaderId);
  });
}

export function getSceneScrapeInput(scene: SceneScrapeScene, inputKind: BatchInputKind) {
  if (inputKind === "url") {
    return scene.urls[0]?.trim() ?? "";
  }

  return getSceneNameSearchInput(scene);
}

export function getSceneNameSearchInput(scene: SceneScrapeScene) {
  const raw = scene.title?.trim() || scene.files[0]?.basename?.trim() || "";
  if (!raw) {
    return "";
  }

  const sanitized = raw
    .replace(/[\\/_:|]+/g, " ")
    .replace(/\s+/g, " ")
    .trim();

  return sanitized || raw;
}

export function getSceneLabel(scene: SceneScrapeScene) {
  return scene.title || scene.files[0]?.basename || `Scene ${scene.id}`;
}