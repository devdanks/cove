import { useCallback, useState, useRef } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { scenes, scrapeAttempts, system } from "../api/client";
import type { ApplySceneScrapeAttemptRequest, Scene, MetadataServerSceneMatch, MetadataServerSceneImportRequest, ScrapeAttempt, ScraperSummary, ScrapeCollectionItemSelection } from "../api/types";
import { useAppConfig } from "../state/AppConfigContext";
import { formatDuration, getResolutionLabel } from "./shared";
import { createNestedRouteLinkProps } from "./cardNavigation";
import { buildFragmentDraft, findDefaultKind, getSceneNameSearchInput, supportsScrapeKind, type CollectionMode, type InputKind } from "./sceneScrapeUtils";
import { buildRelationSelectionPayload, relationKey, ScrapeRelationChoices, type ScrapeRelationActionMap } from "./ScrapeRelationChoices";
import {
  CompactCollectionDecision,
  CompactListValue,
  CompactScalarDecision,
  DEFAULT_TAGGER_BLACKLIST,
  TaggerSettingsPanel,
  TaggerToolbar,
  cleanTaggerQueryString,
  type TaggerQueryMode,
} from "./TaggerShared";
import {
  Search,
  Loader2,
  Check,
  X,
  Plus,
  Minus,
  AlertCircle,
  CloudDownload,
  Fingerprint,
  Settings2,
  EyeOff,
  Eye,
} from "lucide-react";

interface SceneTaggerProps {
  scenes: Scene[];
  onNavigate?: (sceneId: number) => void;
  selectedIds?: Set<number>;
  selecting?: boolean;
  onSelect?: (sceneId: number) => void;
  mode?: "bulk" | "detail";
}

interface TaggerConfig {
  selectedEndpoint: string;
  showUnmatched: boolean;
  setCoverImage: boolean;
  setTags: boolean;
  tagOperation: "merge" | "overwrite";
  setPerformers: boolean;
  setStudio: boolean;
  onlyExistingTags: boolean;
  onlyExistingPerformers: boolean;
  onlyExistingStudio: boolean;
  markOrganized: boolean;
  preferFingerprints: boolean;
  queryMode: TaggerQueryMode;
  blacklist: string[];
  createParentStudios: boolean;
  createParentTags: boolean;
  showMales: boolean;
  performerGenders: string[];
}

interface SceneSearchState {
  loading: boolean;
  results?: UnifiedSceneMatch[];
  error?: string;
  selectedIndex?: number;
  saved?: boolean;
  excludedPerformers?: Set<string>;
  excludedTags?: Set<string>;
  skipStudio?: boolean;
  forceIncludedPerformers?: Set<string>;
  forceIncludedTags?: Set<string>;
  forceIncludeStudio?: boolean;
  fieldStrategies?: Record<string, SceneFieldStrategy>;
  collectionModes?: Record<string, CollectionMode>;
}

type SceneFieldStrategy = "ignore" | "merge" | "overwrite";

type TaggerSource =
  | { kind: "metadata-server"; value: string; label: string; endpoint: string }
  | { kind: "scraper"; value: string; label: string; scraper: ScraperSummary };

interface UnifiedSceneMatch extends MetadataServerSceneMatch {
  sourceKind: "metadata-server" | "scraper";
  scrapeAttemptId?: string;
  selectedCandidateIndex?: number;
  rawResult?: Record<string, unknown>;
}

const sourceValue = (kind: "metadata-server" | "scraper", id: string) => `${kind}:${id}`;

function resolveSource(value: string, sources: TaggerSource[]): TaggerSource | undefined {
  return sources.find((source) => source.value === value)
    ?? sources.find((source) => source.kind === "metadata-server" && source.endpoint === value)
    ?? sources[0];
}

function asString(value: unknown): string | undefined {
  if (typeof value === "string") return value.trim() || undefined;
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  return undefined;
}

function asStringList(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value.flatMap(asStringList).filter(Boolean);
  }
  const text = asString(value);
  if (!text) return [];
  return text.split(",").map((item) => item.trim()).filter(Boolean);
}

function pickString(result: Record<string, unknown>, ...keys: string[]) {
  const entries = Object.entries(result);
  for (const key of keys) {
    const entry = entries.find(([entryKey]) => entryKey.toLowerCase() === key.toLowerCase());
    if (!entry) continue;
    const value = asString(entry[1]);
    if (value) return value;
  }
  return undefined;
}

function pickStringList(result: Record<string, unknown>, ...keys: string[]) {
  const entries = Object.entries(result);
  for (const key of keys) {
    const entry = entries.find(([entryKey]) => entryKey.toLowerCase() === key.toLowerCase());
    if (!entry) continue;
    const values = asStringList(entry[1]);
    if (values.length > 0) return [...new Set(values)];
  }
  return [];
}

function parseAttemptResults(attempt: ScrapeAttempt): Record<string, unknown>[] {
  try {
    if (attempt.candidateResultsJson) {
      const candidates = JSON.parse(attempt.candidateResultsJson);
      if (Array.isArray(candidates)) return candidates.filter((item): item is Record<string, unknown> => item && typeof item === "object" && !Array.isArray(item));
    }
    if (attempt.resultJson) {
      const result = JSON.parse(attempt.resultJson);
      if (result && typeof result === "object" && !Array.isArray(result)) return [result as Record<string, unknown>];
    }
  } catch {
    return [];
  }
  return [];
}

function toCandidates(names: string[]) {
  return names.map((name) => ({ remoteId: name, name, existsLocally: false }));
}

function toScraperSceneMatch(attempt: ScrapeAttempt, result: Record<string, unknown>, index: number, scraper: ScraperSummary): UnifiedSceneMatch {
  const title = pickString(result, "Title", "Name");
  const imageUrl = pickString(result, "Image", "ImageUrl", "ImageURL");
  const performerNames = pickStringList(result, "Performers", "Performer", "PerformerNames");
  const tagNames = pickStringList(result, "Tags", "Tag", "TagNames");
  const studioName = pickString(result, "Studio", "StudioName");
  return {
    sourceKind: "scraper",
    scrapeAttemptId: attempt.id,
    selectedCandidateIndex: index,
    rawResult: result,
    endpoint: scraper.id,
    serverName: scraper.name,
    id: `${attempt.id}:${index}`,
    title,
    code: pickString(result, "Code"),
    date: pickString(result, "Date", "ReleaseDate"),
    director: pickString(result, "Director"),
    details: pickString(result, "Details", "Description", "Synopsis"),
    studioName,
    imageUrl,
    duration: undefined,
    performerNames,
    tagNames,
    urls: pickStringList(result, "URLs", "Url", "URL"),
    fingerprintAlgorithms: [],
    matchCount: 0,
    fingerprints: [],
    studioCandidate: studioName ? { remoteId: studioName, name: studioName, existsLocally: false } : undefined,
    performerCandidates: toCandidates(performerNames),
    tagCandidates: toCandidates(tagNames),
  };
}

function getSceneTagNames(scene: Scene) {
  return scene.tags.map((tag) => tag.name).filter(Boolean);
}

function getScenePerformerNames(scene: Scene) {
  return scene.performers.map((performer) => performer.name).filter(Boolean);
}

function normalizeDecisionValue(value?: string | null) {
  return value?.trim() ?? "";
}

function buildDefaultSceneFieldStrategies(scene: Scene, result: UnifiedSceneMatch): Record<string, SceneFieldStrategy> {
  const fields = [
    { key: "title", current: scene.title, scraped: result.title },
    { key: "code", current: scene.code, scraped: result.code },
    { key: "details", current: scene.details, scraped: result.details },
    { key: "director", current: scene.director, scraped: result.director },
    { key: "date", current: scene.date, scraped: result.date },
  ];
  const strategies: Record<string, SceneFieldStrategy> = {};
  for (const field of fields) {
    if (!field.scraped) continue;
    strategies[field.key] = normalizeDecisionValue(field.current) === normalizeDecisionValue(field.scraped) ? "ignore" : "overwrite";
  }
  return strategies;
}

function getSceneFieldStrategies(scene: Scene, result: UnifiedSceneMatch, state: SceneSearchState | undefined) {
  return { ...buildDefaultSceneFieldStrategies(scene, result), ...(state?.fieldStrategies ?? {}) };
}

function buildDefaultSceneCollectionModes(result: UnifiedSceneMatch, state: SceneSearchState | undefined, taggerConfig: TaggerConfig): Record<string, CollectionMode> {
  return {
    urls: result.urls.length > 0 ? "merge" : "skip",
    tags: taggerConfig.setTags && result.tagNames.length > 0 ? taggerConfig.tagOperation === "overwrite" ? "replace" : "merge" : "skip",
    performers: taggerConfig.setPerformers && result.performerNames.length > 0 ? "merge" : "skip",
    studio: taggerConfig.setStudio && !state?.skipStudio && result.studioName ? "replace" : "skip",
  };
}

function getSceneCollectionModes(result: UnifiedSceneMatch, state: SceneSearchState | undefined, taggerConfig: TaggerConfig) {
  return { ...buildDefaultSceneCollectionModes(result, state, taggerConfig), ...(state?.collectionModes ?? {}) };
}

function collectionModeToFieldStrategy(mode: CollectionMode): SceneFieldStrategy {
  if (mode === "replace") return "overwrite";
  if (mode === "merge") return "merge";
  return "ignore";
}

function buildSceneFieldStrategies(scene: Scene, result: UnifiedSceneMatch, state: SceneSearchState | undefined, taggerConfig: TaggerConfig) {
  const scalarStrategies = getSceneFieldStrategies(scene, result, state);
  const collectionModes = getSceneCollectionModes(result, state, taggerConfig);
  return {
    ...scalarStrategies,
    urls: collectionModeToFieldStrategy(collectionModes.urls),
    tags: collectionModeToFieldStrategy(collectionModes.tags),
    performers: collectionModeToFieldStrategy(collectionModes.performers),
    studio: collectionModeToFieldStrategy(collectionModes.studio),
  };
}

function buildSceneRelationActionMap(
  names: string[],
  currentNames: string[],
  existingNames: string[],
  excludedNames: Set<string> | undefined,
  forceCreateNames: Set<string> | undefined,
  createMissing: boolean,
): ScrapeRelationActionMap {
  const current = new Set(currentNames.map(relationKey));
  const existing = new Set(existingNames.map(relationKey));
  const excluded = new Set(Array.from(excludedNames ?? []).map(relationKey));
  const forced = new Set(Array.from(forceCreateNames ?? []).map(relationKey));
  const actions: ScrapeRelationActionMap = {};

  for (const name of names) {
    const key = relationKey(name);
    if (!key) continue;
    if (excluded.has(key)) actions[key] = "exclude";
    else if (forced.has(key)) actions[key] = "create";
    else if (current.has(key) || existing.has(key)) actions[key] = "include";
    else actions[key] = createMissing ? "create" : "exclude";
  }

  return actions;
}

function buildSceneRelationSelections(
  names: string[],
  currentNames: string[],
  existingNames: string[],
  excludedNames: Set<string> | undefined,
  forceCreateNames: Set<string> | undefined,
  createMissing: boolean,
): ScrapeCollectionItemSelection[] {
  return buildRelationSelectionPayload(
    names,
    buildSceneRelationActionMap(names, currentNames, existingNames, excludedNames, forceCreateNames, createMissing),
  );
}

function buildScraperSceneApplyRequest(result: UnifiedSceneMatch, scene: Scene, state: SceneSearchState | undefined, taggerConfig: TaggerConfig): ApplySceneScrapeAttemptRequest {
  const fieldStrategies = buildSceneFieldStrategies(scene, result, state, taggerConfig);
  const collectionModes = getSceneCollectionModes(result, state, taggerConfig);
  const replaceFields = Object.entries(fieldStrategies)
    .filter(([field, strategy]) => strategy === "overwrite" && !["urls", "tags", "performers", "studio"].includes(field))
    .map(([field]) => field);
  const raw = result.rawResult ?? {};
  if (taggerConfig.setCoverImage && pickString(raw, "Image", "ImageUrl", "ImageURL")) replaceFields.push("image");

  return {
    replaceFields,
    collectionModes,
    createMissingTags: !taggerConfig.onlyExistingTags,
    createMissingPerformers: !taggerConfig.onlyExistingPerformers,
    createMissingStudio: !taggerConfig.onlyExistingStudio,
    markOrganized: taggerConfig.markOrganized,
    hydratePerformers: taggerConfig.createParentTags,
    selectedCandidateIndex: result.selectedCandidateIndex,
    tagSelections: result.tagNames.length > 0 ? buildSceneRelationSelections(result.tagNames, getSceneTagNames(scene), result.tagCandidates.filter((tag) => tag.existsLocally).map((tag) => tag.name), state?.excludedTags, state?.forceIncludedTags, !taggerConfig.onlyExistingTags) : undefined,
    performerSelections: result.performerNames.length > 0 ? buildSceneRelationSelections(result.performerNames, getScenePerformerNames(scene), result.performerCandidates.filter((performer) => performer.existsLocally).map((performer) => performer.name), state?.excludedPerformers, state?.forceIncludedPerformers, !taggerConfig.onlyExistingPerformers) : undefined,
  };
}

const CONCURRENCY_LIMIT = 5;

async function runWithConcurrency<T>(
  items: T[],
  fn: (item: T) => Promise<void>,
  limit: number,
  signal?: AbortSignal
): Promise<void> {
  let index = 0;
  const workers = Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (index < items.length) {
      if (signal?.aborted) return;
      const i = index++;
      await fn(items[i]);
    }
  });
  await Promise.all(workers);
}

export function SceneTagger({ scenes: sceneList, onNavigate, selectedIds, selecting = false, onSelect, mode = "bulk" }: SceneTaggerProps) {
  const { config } = useAppConfig();
  const metadataServers = config?.scraping?.metadataServers ?? [];
  const { data: scraperList = [] } = useQuery({ queryKey: ["scrapers"], queryFn: system.listScrapers });
  const sceneScrapers = scraperList.filter((scraper) => scraper.entityType.toLowerCase() === "scene");
  const taggerSources: TaggerSource[] = [
    ...metadataServers.map((server) => ({
      kind: "metadata-server" as const,
      value: sourceValue("metadata-server", server.endpoint),
      label: server.name || server.endpoint,
      endpoint: server.endpoint,
    })),
    ...sceneScrapers.map((scraper) => ({
      kind: "scraper" as const,
      value: sourceValue("scraper", scraper.id),
      label: `${scraper.name} (Scraper)`,
      scraper,
    })),
  ];

  const TAGGER_CONFIG_KEY = "cove-tagger-config";

  const DEFAULT_TAGGER_CONFIG: TaggerConfig = {
    selectedEndpoint: metadataServers[0] ? sourceValue("metadata-server", metadataServers[0].endpoint) : "",
    showUnmatched: true,
    setCoverImage: true,
    setTags: true,
    tagOperation: "merge",
    setPerformers: true,
    setStudio: true,
    onlyExistingTags: false,
    onlyExistingPerformers: false,
    onlyExistingStudio: false,
    markOrganized: false,
    preferFingerprints: true,
    queryMode: "auto",
    blacklist: [...DEFAULT_TAGGER_BLACKLIST],
    createParentStudios: true,
    createParentTags: true,
    showMales: true,
    performerGenders: ["Female", "Male", "Transgender Female", "Transgender Male", "Intersex", "Non-Binary"],
  };

  const [taggerConfig, _setTaggerConfig] = useState<TaggerConfig>(() => {
    try {
      const saved = localStorage.getItem(TAGGER_CONFIG_KEY);
      if (saved) {
        const parsed = JSON.parse(saved) as Partial<TaggerConfig>;
        return {
          ...DEFAULT_TAGGER_CONFIG,
          ...parsed,
          selectedEndpoint: parsed.selectedEndpoint ?? DEFAULT_TAGGER_CONFIG.selectedEndpoint,
          blacklist: parsed.blacklist ?? DEFAULT_TAGGER_CONFIG.blacklist,
          performerGenders: parsed.performerGenders ?? DEFAULT_TAGGER_CONFIG.performerGenders,
        };
      }
    } catch { /* ignore */ }
    return DEFAULT_TAGGER_CONFIG;
  });

  const setTaggerConfig = useCallback((updater: TaggerConfig | ((prev: TaggerConfig) => TaggerConfig)) => {
    _setTaggerConfig((prev) => {
      const next = typeof updater === "function" ? updater(prev) : updater;
      try { localStorage.setItem(TAGGER_CONFIG_KEY, JSON.stringify(next)); } catch { /* ignore */ }
      return next;
    });
  }, []);
  const [showConfig, setShowConfig] = useState(false);
  const [searchStates, setSearchStates] = useState<Record<number, SceneSearchState>>({});
  const [queryOverrides, setQueryOverrides] = useState<Record<number, string>>({});
  const [scraperInputKinds, setScraperInputKinds] = useState<Record<number, InputKind>>({});
  const selectedSource = resolveSource(taggerConfig.selectedEndpoint, taggerSources);

  const updateSearchState = useCallback(
    (sceneId: number, update: Partial<SceneSearchState>) => {
      setSearchStates((prev) => ({
        ...prev,
        [sceneId]: { ...prev[sceneId], ...update },
      }));
    },
    []
  );

  // Derive search query from scene (standard prepareQueryString logic)
  const getSearchQuery = useCallback(
    (scene: Scene): string => {
      if (queryOverrides[scene.id] !== undefined) return queryOverrides[scene.id];
      const file = scene.files[0];
      const mode = taggerConfig.queryMode;

      // metadata mode, or auto mode when scene has date+studio — build compound query
      if (mode === "metadata" || (mode === "auto" && scene.date && scene.studioName)) {
        let str = [
          scene.date || "",
          scene.studioName || "",
          (scene.performers || []).map((p: any) => p.name).join(" "),
          scene.title ? scene.title.replace(/[^a-zA-Z0-9 ]+/g, "") : "",
        ].filter((s) => s !== "").join(" ");
        str = cleanTaggerQueryString(str, taggerConfig.blacklist);
        return str;
      }

      // filename/dir/path modes: derive from file path
      if (mode === "filename" && file?.basename) {
        return cleanTaggerQueryString(file.basename.replace(/\.\w{2,4}$/, ""), taggerConfig.blacklist);
      }
      if (mode === "dir" && file?.path) {
        const parts = file.path.replace(/\\/g, "/").split("/");
        return parts.length > 1 ? cleanTaggerQueryString(parts[parts.length - 2], taggerConfig.blacklist) : "";
      }
      if (mode === "path" && file?.path) {
        return cleanTaggerQueryString(file.path, taggerConfig.blacklist);
      }

      // auto mode: try title first, then filename — always apply blacklist
      if (scene.title) return cleanTaggerQueryString(scene.title, taggerConfig.blacklist);
      if (file?.basename) {
        return cleanTaggerQueryString(file.basename.replace(/\.\w{2,4}$/, ""), taggerConfig.blacklist);
      }
      return "";
    },
    [queryOverrides, taggerConfig.queryMode, taggerConfig.blacklist]
  );

  const getScraperInputKind = useCallback((scene: Scene, source: TaggerSource | undefined): InputKind => {
    if (source?.kind !== "scraper") {
      return "name";
    }

    const preferred = scene.urls?.some((url) => url.trim()) ? "url" : "name";
    return scraperInputKinds[scene.id] ?? findDefaultKind(source.scraper, preferred);
  }, [scraperInputKinds]);

  const getSourceQuery = useCallback(
    (scene: Scene, source: TaggerSource | undefined): string => {
      if (source?.kind === "scraper") {
        const inputKind = getScraperInputKind(scene, source);
        if (queryOverrides[scene.id] !== undefined) {
          return queryOverrides[scene.id];
        }

        if (inputKind === "url") {
          return scene.urls?.find((url) => url.trim()) ?? "";
        }

        if (inputKind === "fragment") {
          return buildFragmentDraft(scene);
        }

        return getSceneNameSearchInput(scene) || getSearchQuery(scene);
      }
      return getSearchQuery(scene);
    },
    [getScraperInputKind, getSearchQuery, queryOverrides]
  );

  const handleScraperInputKindChange = useCallback((scene: Scene, source: TaggerSource | undefined, inputKind: InputKind) => {
    setScraperInputKinds((prev) => ({ ...prev, [scene.id]: inputKind }));
    setQueryOverrides((prev) => {
      const nextQuery = inputKind === "url"
        ? scene.urls?.find((url) => url.trim()) ?? ""
        : inputKind === "fragment"
          ? buildFragmentDraft(scene)
          : getSceneNameSearchInput(scene) || getSearchQuery(scene);
      return { ...prev, [scene.id]: nextQuery };
    });
    if (source?.kind === "scraper" && !supportsScrapeKind(source.scraper, inputKind)) {
      updateSearchState(scene.id, { error: `The selected scraper does not support ${inputKind} input.` });
    }
  }, [getSearchQuery, updateSearchState]);

  const searchScene = useCallback(
    async (scene: Scene) => {
      const source = selectedSource;
      const query = getSourceQuery(scene, source);
      updateSearchState(scene.id, { loading: true, error: undefined, results: undefined, saved: false });
      try {
        let results: UnifiedSceneMatch[] = [];
        if (source?.kind === "scraper") {
          const inputKind = getScraperInputKind(scene, source);
          if (!supportsScrapeKind(source.scraper, inputKind)) throw new Error(`This scraper does not support ${inputKind} input.`);
          if (inputKind === "url" && !query.trim()) throw new Error("Enter a URL to scrape.");
          if (inputKind === "name" && !query.trim()) throw new Error("Enter a title or name to scrape.");
          let fragment: Record<string, unknown> | undefined;
          if (inputKind === "fragment") {
            const parsed = JSON.parse(query);
            if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
              throw new Error("Fragment input must be a JSON object.");
            }
            fragment = parsed as Record<string, unknown>;
          }
          const attempt = await scrapeAttempts.create({
            scraperId: source.scraper.id,
            entityType: "scene",
            entityId: scene.id,
            inputKind,
            url: inputKind === "url" ? query : undefined,
            name: inputKind === "name" ? query : undefined,
            fragment,
          });
          if (attempt.status.toLowerCase() === "failure") throw new Error(attempt.error || "Scrape returned no results.");
          results = parseAttemptResults(attempt).map((result, index) => toScraperSceneMatch(attempt, result, index, source.scraper));
        } else {
          const endpoint = source?.endpoint || undefined;
          const shouldTryFingerprints = taggerConfig.preferFingerprints || !query;

          if (shouldTryFingerprints) {
            results = (await scenes.searchMetadataServer(scene.id, undefined, endpoint)).map((match) => ({ ...match, sourceKind: "metadata-server" as const }));
          }

          if (results.length === 0 && query) {
            results = (await scenes.searchMetadataServer(scene.id, query, endpoint)).map((match) => ({ ...match, sourceKind: "metadata-server" as const }));
          }
        }

        updateSearchState(scene.id, {
          loading: false,
          results,
          selectedIndex: results.length > 0 ? 0 : undefined,
        });
      } catch (err) {
        updateSearchState(scene.id, {
          loading: false,
          error: err instanceof Error ? err.message : "Search failed",
        });
      }
    },
    [getScraperInputKind, getSourceQuery, selectedSource, taggerConfig.preferFingerprints, updateSearchState]
  );

  // Fingerprint-only search
  const searchSceneFingerprints = useCallback(
    async (scene: Scene) => {
      updateSearchState(scene.id, { loading: true, error: undefined, results: undefined, saved: false });
      try {
        if (selectedSource?.kind !== "metadata-server") throw new Error("Fingerprint search is only available for metadata-server sources.");
        const results = (await scenes.searchMetadataServer(scene.id, undefined, selectedSource.endpoint || undefined)).map((match) => ({ ...match, sourceKind: "metadata-server" as const }));
        updateSearchState(scene.id, {
          loading: false,
          results,
          selectedIndex: results.length > 0 ? 0 : undefined,
        });
      } catch (err) {
        updateSearchState(scene.id, {
          loading: false,
          error: err instanceof Error ? err.message : "Search failed",
        });
      }
    },
    [selectedSource, updateSearchState]
  );

  // Batch scrape all (concurrent)
  const [batchSearching, setBatchSearching] = useState(false);
  const abortRef = useRef<AbortController | null>(null);
  const searchAll = useCallback(async () => {
    setBatchSearching(true);
    const controller = new AbortController();
    abortRef.current = controller;
    const toSearch = sceneList.filter((s) => !searchStates[s.id]?.saved);
    await runWithConcurrency(toSearch, (scene) => searchScene(scene), CONCURRENCY_LIMIT, controller.signal);
    setBatchSearching(false);
    abortRef.current = null;
  }, [sceneList, searchStates, searchScene]);

  const cancelBatchSearch = useCallback(() => {
    abortRef.current?.abort();
    setBatchSearching(false);
  }, []);

  if (taggerSources.length === 0) {
    return (
      <div className="px-4 py-12 text-center">
        <AlertCircle className="w-12 h-12 mx-auto mb-3 text-muted opacity-50" />
        <p className="text-secondary text-lg">No Metadata Sources Configured</p>
        <p className="text-muted text-sm mt-1">
          Add a metadata server or install a scene scraper to use the tagger.
        </p>
      </div>
    );
  }

  const visibleScenes = taggerConfig.showUnmatched
    ? sceneList
    : sceneList.filter((s) => {
        const state = searchStates[s.id];
        return !state || !state.results || state.results.length > 0;
      });

  return (
    <div className="space-y-0">
      <TaggerToolbar
        sources={taggerSources.map((source) => ({ value: source.value, label: source.label }))}
        selectedSource={selectedSource?.value ?? taggerConfig.selectedEndpoint}
        onSourceChange={(value) => {
          setTaggerConfig((c) => ({ ...c, selectedEndpoint: value }));
          setQueryOverrides({});
          setScraperInputKinds({});
        }}
        showToggle={mode === "bulk" ? {
          value: taggerConfig.showUnmatched,
          onChange: (value) => setTaggerConfig((c) => ({ ...c, showUnmatched: value })),
          enabledLabel: "Hide Unmatched",
          disabledLabel: "Show Unmatched",
        } : undefined}
        batchSearching={batchSearching}
        onCancelBatch={cancelBatchSearch}
        onRunAll={searchAll}
        showRunAll={mode === "bulk"}
        countLabel={`${visibleScenes.length} scene${visibleScenes.length !== 1 ? "s" : ""}`}
        settingsOpen={showConfig}
        onToggleSettings={() => setShowConfig((current) => !current)}
      />

      {showConfig && (
        <TaggerSettingsPanel
          blacklist={taggerConfig.blacklist}
          onBlacklistChange={(items) => setTaggerConfig((c) => ({ ...c, blacklist: items }))}
        >

              {/* Performer genders */}
              <div>
                <p className="text-xs text-muted mb-1.5">Performer genders</p>
                <div className="space-y-1">
                  {["Female", "Male", "Transgender Female", "Transgender Male", "Intersex", "Non-Binary"].map((g) => (
                    <label key={g} className="flex items-center gap-2 text-xs text-foreground">
                      <input type="checkbox" checked={taggerConfig.performerGenders.includes(g)} onChange={(e) => setTaggerConfig((c) => ({ ...c, performerGenders: e.target.checked ? [...c.performerGenders, g] : c.performerGenders.filter((x) => x !== g) }))} className="rounded border-border" />
                      {g}
                    </label>
                  ))}
                </div>
                <p className="text-[10px] text-muted mt-1">Performers with these genders will be shown when tagging scenes.</p>
              </div>

              {/* Set scene cover image */}
              <div>
                <label className="flex items-center gap-2 text-xs text-foreground">
                  <input type="checkbox" checked={taggerConfig.setCoverImage} onChange={(e) => setTaggerConfig((c) => ({ ...c, setCoverImage: e.target.checked }))} className="rounded border-border" />
                  Set scene cover image
                </label>
                <p className="text-[10px] text-muted mt-0.5 ml-5">Replace the scene cover if one is found.</p>
              </div>

              {/* Set performers */}
              <div>
                <label className="flex items-center gap-2 text-xs text-foreground">
                  <input type="checkbox" checked={taggerConfig.setPerformers} onChange={(e) => setTaggerConfig((c) => ({ ...c, setPerformers: e.target.checked }))} className="rounded border-border" />
                  Set performers
                </label>
                {taggerConfig.setPerformers && (
                  <label className="flex items-center gap-2 text-xs text-foreground ml-5 mt-1">
                    <input type="checkbox" checked={!taggerConfig.onlyExistingPerformers} onChange={(e) => setTaggerConfig((c) => ({ ...c, onlyExistingPerformers: !e.target.checked }))} className="rounded border-border" />
                    Create missing performers
                  </label>
                )}
                <p className="text-[10px] text-muted mt-0.5 ml-5">Attach performers to scene. Uncheck "Create missing" to only use performers that already exist.</p>
              </div>

              {/* Set studio */}
              <div>
                <label className="flex items-center gap-2 text-xs text-foreground">
                  <input type="checkbox" checked={taggerConfig.setStudio} onChange={(e) => setTaggerConfig((c) => ({ ...c, setStudio: e.target.checked }))} className="rounded border-border" />
                  Set studio
                </label>
                {taggerConfig.setStudio && (
                  <label className="flex items-center gap-2 text-xs text-foreground ml-5 mt-1">
                    <input type="checkbox" checked={!taggerConfig.onlyExistingStudio} onChange={(e) => setTaggerConfig((c) => ({ ...c, onlyExistingStudio: !e.target.checked }))} className="rounded border-border" />
                    Create missing studios
                  </label>
                )}
                <p className="text-[10px] text-muted mt-0.5 ml-5">Set the scene studio. Uncheck "Create missing" to only use studios that already exist.</p>
              </div>

              {/* Set tags + operation */}
              <div>
                <div className="flex items-center gap-3">
                  <label className="flex items-center gap-2 text-xs text-foreground">
                    <input type="checkbox" checked={taggerConfig.setTags} onChange={(e) => setTaggerConfig((c) => ({ ...c, setTags: e.target.checked }))} className="rounded border-border" />
                    Set tags
                  </label>
                  {taggerConfig.setTags && (
                    <select value={taggerConfig.tagOperation} onChange={(e) => setTaggerConfig((c) => ({ ...c, tagOperation: e.target.value as "merge" | "overwrite" }))} className="bg-input border border-border rounded px-2 py-0.5 text-xs text-foreground focus:outline-none focus:border-accent">
                      <option value="merge">Merge</option>
                      <option value="overwrite">Overwrite</option>
                    </select>
                  )}
                </div>
                {taggerConfig.setTags && (
                  <label className="flex items-center gap-2 text-xs text-foreground ml-5 mt-1">
                    <input type="checkbox" checked={!taggerConfig.onlyExistingTags} onChange={(e) => setTaggerConfig((c) => ({ ...c, onlyExistingTags: !e.target.checked }))} className="rounded border-border" />
                    Create missing tags
                  </label>
                )}
                <p className="text-[10px] text-muted mt-0.5 ml-5">Attach tags to scene. Uncheck "Create missing" to only set tags that already exist.</p>
              </div>

              {/* Query mode */}
              <div>
                <div className="flex items-center gap-2">
                  <span className="text-xs text-muted">Query Mode:</span>
                  <select value={taggerConfig.queryMode} onChange={(e) => setTaggerConfig((c) => ({ ...c, queryMode: e.target.value as TaggerConfig["queryMode"] }))} className="bg-input border border-border rounded px-2 py-1 text-xs text-foreground focus:outline-none focus:border-accent">
                    <option value="auto">Auto</option>
                    <option value="filename">Filename</option>
                    <option value="dir">Directory</option>
                    <option value="path">Full Path</option>
                    <option value="metadata">Metadata</option>
                  </select>
                </div>
                <p className="text-[10px] text-muted mt-0.5">Uses metadata if present, or filename</p>
              </div>

              {/* Mark organized */}
              <div>
                <label className="flex items-center gap-2 text-xs text-foreground">
                  <input type="checkbox" checked={taggerConfig.markOrganized} onChange={(e) => setTaggerConfig((c) => ({ ...c, markOrganized: e.target.checked }))} className="rounded border-border" />
                  Mark as Organized on save
                </label>
                <p className="text-[10px] text-muted mt-0.5 ml-5">Immediately mark the scene as Organized after the Save button is clicked.</p>
              </div>
        </TaggerSettingsPanel>
      )}

      {/* Scene list */}
      <div className="divide-y divide-border">
        {visibleScenes.map((scene) => (
          <TaggerSceneRow
            key={scene.id}
            scene={scene}
            state={searchStates[scene.id]}
            query={getSourceQuery(scene, selectedSource)}
            onQueryChange={(q) => setQueryOverrides((prev) => ({ ...prev, [scene.id]: q }))}
            scraperInputKind={getScraperInputKind(scene, selectedSource)}
            onScraperInputKindChange={(inputKind) => handleScraperInputKindChange(scene, selectedSource, inputKind)}
            onSearch={() => searchScene(scene)}
            onSearchFingerprints={() => searchSceneFingerprints(scene)}
            onUpdateState={(update) => updateSearchState(scene.id, update)}
            source={selectedSource}
            taggerConfig={taggerConfig}
            onNavigate={onNavigate}
            selected={selectedIds?.has(scene.id) ?? false}
            selecting={selecting}
            onSelect={onSelect}
            detailMode={mode === "detail"}
          />
        ))}
      </div>
    </div>
  );
}

/* ── Scene Tagger Row ── */

interface TaggerSceneRowProps {
  scene: Scene;
  state?: SceneSearchState;
  query: string;
  onQueryChange: (q: string) => void;
  scraperInputKind: InputKind;
  onScraperInputKindChange: (inputKind: InputKind) => void;
  onSearch: () => void;
  onSearchFingerprints: () => void;
  onUpdateState: (update: Partial<SceneSearchState>) => void;
  source?: TaggerSource;
  taggerConfig: TaggerConfig;
  onNavigate?: (sceneId: number) => void;
  selected?: boolean;
  selecting?: boolean;
  onSelect?: (sceneId: number) => void;
  detailMode?: boolean;
}

function TaggerSceneRow({
  scene,
  state,
  query,
  onQueryChange,
  scraperInputKind,
  onScraperInputKindChange,
  onSearch,
  onSearchFingerprints,
  onUpdateState,
  source,
  taggerConfig,
  onNavigate,
  selected = false,
  selecting = false,
  onSelect,
  detailMode = false,
}: TaggerSceneRowProps) {
  const file = scene.files[0];
  const screenshotUrl = scenes.screenshotUrl(scene.id, scene.updatedAt);
  const selectedResult = state?.results?.[state.selectedIndex ?? 0];
  const queryClient = useQueryClient();
  const sceneLinkProps = createNestedRouteLinkProps<HTMLAnchorElement>({ page: "scene", id: scene.id }, () => onNavigate?.(scene.id));
  const isScraperSource = source?.kind === "scraper";
  const sceneUrls = (scene.urls ?? []).filter((url) => url.trim());
  const selectedUrlOption = sceneUrls.includes(query) ? query : "__custom";
  const searchPlaceholder = isScraperSource
    ? scraperInputKind === "url"
      ? "Scene URL..."
      : scraperInputKind === "fragment"
        ? "Fragment JSON..."
        : "Title or name..."
    : "Search query...";

  const importMut = useMutation<Scene | ScrapeAttempt, Error>({
    mutationFn: () => {
      if (!selectedResult) throw new Error("No result selected");
      const collectionModes = getSceneCollectionModes(selectedResult, state, taggerConfig);
      const tagActions = buildSceneRelationActionMap(selectedResult.tagNames, getSceneTagNames(scene), selectedResult.tagCandidates.filter((tag) => tag.existsLocally).map((tag) => tag.name), state?.excludedTags, state?.forceIncludedTags, !taggerConfig.onlyExistingTags);
      const performerActions = buildSceneRelationActionMap(selectedResult.performerNames, getScenePerformerNames(scene), selectedResult.performerCandidates.filter((performer) => performer.existsLocally).map((performer) => performer.name), state?.excludedPerformers, state?.forceIncludedPerformers, !taggerConfig.onlyExistingPerformers);
      const excludedTags = collectionModes.tags === "skip" ? selectedResult.tagNames : selectedResult.tagNames.filter((name) => tagActions[relationKey(name)] === "exclude");
      const excludedPerformers = collectionModes.performers === "skip" ? selectedResult.performerNames : selectedResult.performerNames.filter((name) => performerActions[relationKey(name)] === "exclude");
      if (selectedResult?.sourceKind === "scraper") {
        if (!selectedResult.scrapeAttemptId) throw new Error("No scraper attempt selected");
        return scrapeAttempts.apply(selectedResult.scrapeAttemptId, buildScraperSceneApplyRequest(selectedResult, scene, state, taggerConfig));
      }

      // Build overrides for force-included entities (entities that would normally be skipped
      // by onlyExisting* flags but the user explicitly opted to create)
      const performerOverrides = selectedResult.performerCandidates.some((performer) => performerActions[relationKey(performer.name)] === "create")
        ? selectedResult.performerCandidates
            .filter(p => performerActions[relationKey(p.name)] === "create")
            .map(p => ({ remoteId: p.remoteId, name: p.name, action: "create" }))
        : undefined;
      const tagOverrides = selectedResult.tagCandidates.some((tag) => tagActions[relationKey(tag.name)] === "create")
        ? selectedResult.tagCandidates
            .filter(t => tagActions[relationKey(t.name)] === "create")
            .map(t => ({ remoteId: t.remoteId, name: t.name, action: "create" }))
        : undefined;
      const studioOverride = state?.forceIncludeStudio && selectedResult.studioCandidate
        ? { remoteId: selectedResult.studioCandidate.remoteId, name: selectedResult.studioCandidate.name, action: "create" }
        : undefined;

      const importReq: MetadataServerSceneImportRequest = {
        endpoint: source?.kind === "metadata-server" ? source.endpoint : selectedResult?.endpoint ?? "",
        sceneId: selectedResult?.id ?? "",
        setCoverImage: taggerConfig.setCoverImage,
        setTags: taggerConfig.setTags && collectionModes.tags !== "skip",
        setPerformers: taggerConfig.setPerformers && collectionModes.performers !== "skip",
        setStudio: taggerConfig.setStudio && collectionModes.studio !== "skip",
        onlyExistingTags: taggerConfig.onlyExistingTags,
        onlyExistingPerformers: taggerConfig.onlyExistingPerformers,
        onlyExistingStudio: taggerConfig.onlyExistingStudio,
        markOrganized: taggerConfig.markOrganized,
        excludedTagNames: excludedTags.length > 0 ? excludedTags : undefined,
        excludedPerformerNames: excludedPerformers.length > 0 ? excludedPerformers : undefined,
        performerOverrides,
        tagOverrides,
        studioOverride,
        fieldStrategies: buildSceneFieldStrategies(scene, selectedResult, state, taggerConfig),
      };
      return scenes.importFromMetadataServer(scene.id, importReq);
    },
    onSuccess: () => {
      onUpdateState({ saved: true });
      queryClient.invalidateQueries({ queryKey: ["scene", scene.id] });
      queryClient.invalidateQueries({ queryKey: ["scenes"] });
    },
  });

  return (
    <div className={`px-3 py-2 ${state?.saved ? "opacity-50" : ""} ${selected ? "bg-accent/5" : ""}`}>
      <div className="flex gap-3">
        {onSelect && (
          <button
            type="button"
            onClick={() => onSelect(scene.id)}
            className={`mt-1 flex h-5 w-5 shrink-0 items-center justify-center rounded border text-[10px] ${selected ? "border-accent bg-accent text-white" : selecting ? "border-accent/60 text-accent" : "border-border text-transparent hover:border-accent hover:text-accent"}`}
            aria-label={selected ? "Deselect scene" : "Select scene"}
            title={selected ? "Deselect" : "Select"}
          >
            <Check className="h-3 w-3" />
          </button>
        )}
        {/* Scene preview — compact */}
        <a
          {...sceneLinkProps}
          className="flex-shrink-0 w-32 block group/scene"
          title={`Open scene ${scene.title || file?.basename || "Untitled"}`}
        >
          <div className="relative aspect-video bg-card rounded overflow-hidden">
            <img
              src={screenshotUrl}
              alt=""
              className="w-full h-full object-cover"
              loading="lazy"
              onError={(e) => {
                (e.target as HTMLImageElement).style.display = "none";
              }}
            />
            {file && file.duration > 0 && (
              <span className="absolute bottom-0.5 right-0.5 text-[8px] text-white bg-black/70 px-0.5 rounded">
                {formatDuration(file.duration)}
              </span>
            )}
          </div>
          <p className="text-[11px] text-accent mt-0.5 truncate font-medium leading-snug group-hover/scene:underline">
            {scene.title || file?.basename || "Untitled"}
          </p>
          <p className="text-[9px] text-muted truncate leading-snug">
            {[scene.studioName, file && getResolutionLabel(file.width, file.height)].filter(Boolean).join(" · ")}
          </p>
        </a>

        {/* Search + Results */}
        <div className="flex-1 min-w-0">
          {detailMode && isScraperSource && (
            <div className="mb-1.5 flex flex-wrap items-center gap-1.5">
              <select
                value={scraperInputKind}
                onChange={(event) => onScraperInputKindChange(event.target.value as InputKind)}
                className="bg-input border border-border rounded px-2 py-1 text-xs text-foreground focus:outline-none focus:border-accent"
              >
                <option value="url" disabled={!supportsScrapeKind(source.scraper, "url")}>URL</option>
                <option value="name" disabled={!supportsScrapeKind(source.scraper, "name")}>Title</option>
                <option value="fragment" disabled={!supportsScrapeKind(source.scraper, "fragment")}>Fragment</option>
              </select>
              {scraperInputKind === "url" && sceneUrls.length > 0 ? (
                <select
                  value={selectedUrlOption}
                  onChange={(event) => {
                    if (event.target.value !== "__custom") {
                      onQueryChange(event.target.value);
                    }
                  }}
                  className="min-w-0 max-w-full flex-1 bg-input border border-border rounded px-2 py-1 text-xs text-foreground focus:outline-none focus:border-accent"
                >
                  <option value="__custom">Custom URL</option>
                  {sceneUrls.map((url) => (
                    <option key={url} value={url}>{url}</option>
                  ))}
                </select>
              ) : null}
            </div>
          )}
          {/* Search input — inline and compact */}
          <div className="flex gap-1.5 mb-1.5">
            {isScraperSource && scraperInputKind === "fragment" ? (
              <textarea
                value={query}
                onChange={(e) => onQueryChange(e.target.value)}
                rows={detailMode ? 8 : 3}
                placeholder={searchPlaceholder}
                className="flex-1 min-w-0 bg-input border border-border rounded pl-2 pr-2 py-1 font-mono text-xs text-foreground focus:outline-none focus:border-accent placeholder:text-muted"
              />
            ) : (
              <input
                type="text"
                value={query}
                onChange={(e) => onQueryChange(e.target.value)}
                onKeyDown={(e) => e.key === "Enter" && onSearch()}
                placeholder={searchPlaceholder}
                className="flex-1 min-w-0 bg-input border border-border rounded pl-2 pr-2 py-1 text-xs text-foreground focus:outline-none focus:border-accent placeholder:text-muted"
              />
            )}
            <button
              onClick={onSearch}
              disabled={state?.loading}
              className="flex h-fit items-center gap-1 px-2 py-1 rounded text-xs font-medium bg-accent text-white hover:bg-accent-hover disabled:opacity-60"
            >
              {state?.loading ? <Loader2 className="w-3 h-3 animate-spin" /> : <Search className="w-3 h-3" />}
            </button>
            {source?.kind === "metadata-server" && (
              <button
                onClick={onSearchFingerprints}
                disabled={state?.loading}
                className="flex items-center gap-1 px-2 py-1 rounded text-xs bg-surface border border-border text-muted hover:text-foreground disabled:opacity-60"
                title="Search by fingerprint only"
              >
                <Fingerprint className="w-3 h-3" />
              </button>
            )}
          </div>

          {/* Error */}
          {state?.error && (
            <p className="text-xs text-red-400 mb-2">
              <AlertCircle className="w-3 h-3 inline mr-1" />
              {state.error}
            </p>
          )}

          {/* No results */}
          {state?.results && state.results.length === 0 && (
            <p className="text-xs text-muted">No matches found.</p>
          )}

          {/* Results */}
          {state?.results && state.results.length > 0 && (
            <TaggerResults
              scene={scene}
              results={state.results}
              selectedIndex={state.selectedIndex ?? 0}
              onSelect={(i) => onUpdateState(i === (state.selectedIndex ?? 0) ? { selectedIndex: i } : {
                selectedIndex: i,
                fieldStrategies: undefined,
                collectionModes: undefined,
                excludedPerformers: undefined,
                excludedTags: undefined,
                skipStudio: undefined,
                forceIncludedPerformers: undefined,
                forceIncludedTags: undefined,
                forceIncludeStudio: undefined,
              })}
              onSave={() => importMut.mutate()}
              saving={importMut.isPending}
              saved={state.saved}
              localDuration={file?.duration}
              excludedPerformers={state.excludedPerformers ?? new Set()}
              excludedTags={state.excludedTags ?? new Set()}
              skipStudio={state.skipStudio ?? false}
              forceIncludedPerformers={state.forceIncludedPerformers ?? new Set()}
              forceIncludedTags={state.forceIncludedTags ?? new Set()}
              forceIncludeStudio={state.forceIncludeStudio ?? false}
              fieldStrategies={selectedResult ? getSceneFieldStrategies(scene, selectedResult, state) : {}}
              collectionModes={selectedResult ? getSceneCollectionModes(selectedResult, state, taggerConfig) : {}}
              onFieldStrategyChange={(field, strategy) => {
                if (!selectedResult) return;
                onUpdateState({ fieldStrategies: { ...getSceneFieldStrategies(scene, selectedResult, state), [field]: strategy } });
              }}
              onCollectionModeChange={(field, mode) => {
                if (!selectedResult) return;
                onUpdateState({ collectionModes: { ...getSceneCollectionModes(selectedResult, state, taggerConfig), [field]: mode } });
              }}
              onTogglePerformer={(name) => {
                const perf = selectedResult?.performerCandidates.find(p => p.name === name);
                const willSkipByDefault = taggerConfig.onlyExistingPerformers && perf && !perf.existsLocally;
                if (willSkipByDefault) {
                  const current = new Set(state.forceIncludedPerformers ?? []);
                  if (current.has(name)) current.delete(name);
                  else current.add(name);
                  onUpdateState({ forceIncludedPerformers: current });
                } else {
                  const current = new Set(state.excludedPerformers ?? []);
                  if (current.has(name)) current.delete(name);
                  else current.add(name);
                  onUpdateState({ excludedPerformers: current });
                }
              }}
              onToggleTag={(name) => {
                const tag = selectedResult?.tagCandidates.find(t => t.name === name);
                const willSkipByDefault = taggerConfig.onlyExistingTags && tag && !tag.existsLocally;
                if (willSkipByDefault) {
                  const current = new Set(state.forceIncludedTags ?? []);
                  if (current.has(name)) current.delete(name);
                  else current.add(name);
                  onUpdateState({ forceIncludedTags: current });
                } else {
                  const current = new Set(state.excludedTags ?? []);
                  if (current.has(name)) current.delete(name);
                  else current.add(name);
                  onUpdateState({ excludedTags: current });
                }
              }}
              onToggleStudio={() => {
                const willSkipByDefault = taggerConfig.onlyExistingStudio && selectedResult?.studioCandidate && !selectedResult.studioCandidate.existsLocally;
                if (willSkipByDefault) {
                  onUpdateState({ forceIncludeStudio: !state.forceIncludeStudio });
                } else {
                  onUpdateState({ skipStudio: !state.skipStudio });
                }
              }}
              taggerConfig={taggerConfig}
            />
          )}

          {/* Saved indicator */}
          {state?.saved && (
            <div className="flex items-center gap-1 mt-2 text-xs text-green-400">
              <Check className="w-3.5 h-3.5" />
              Saved successfully
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

/* ── Tagger Results ── */

interface TaggerResultsProps {
  scene: Scene;
  results: UnifiedSceneMatch[];
  selectedIndex: number;
  onSelect: (index: number) => void;
  onSave: () => void;
  saving?: boolean;
  saved?: boolean;
  localDuration?: number;
  excludedPerformers: Set<string>;
  excludedTags: Set<string>;
  skipStudio: boolean;
  forceIncludedPerformers: Set<string>;
  forceIncludedTags: Set<string>;
  forceIncludeStudio: boolean;
  fieldStrategies: Record<string, SceneFieldStrategy>;
  collectionModes: Record<string, CollectionMode>;
  onFieldStrategyChange: (field: string, strategy: SceneFieldStrategy) => void;
  onCollectionModeChange: (field: string, mode: CollectionMode) => void;
  onTogglePerformer: (name: string) => void;
  onToggleTag: (name: string) => void;
  onToggleStudio: () => void;
  taggerConfig: TaggerConfig;
}

function TaggerResults({ scene, results, selectedIndex, onSelect, onSave, saving, saved, localDuration, excludedPerformers, excludedTags, skipStudio, forceIncludedPerformers, forceIncludedTags, forceIncludeStudio, fieldStrategies, collectionModes, onFieldStrategyChange, onCollectionModeChange, onTogglePerformer, onToggleTag, onToggleStudio, taggerConfig }: TaggerResultsProps) {
  return (
    <div className="space-y-1">
      {results.map((result, i) => (
        <TaggerResultRow
          key={`${result.endpoint}-${result.id}`}
          scene={scene}
          result={result}
          isSelected={i === selectedIndex}
          onClick={() => onSelect(i)}
          onSave={i === selectedIndex ? onSave : undefined}
          saving={i === selectedIndex ? saving : false}
          saved={saved}
          localDuration={localDuration}
          excludedPerformers={excludedPerformers}
          excludedTags={excludedTags}
          skipStudio={skipStudio}
          forceIncludedPerformers={forceIncludedPerformers}
          forceIncludedTags={forceIncludedTags}
          forceIncludeStudio={forceIncludeStudio}
          fieldStrategies={fieldStrategies}
          collectionModes={collectionModes}
          onFieldStrategyChange={i === selectedIndex ? onFieldStrategyChange : undefined}
          onCollectionModeChange={i === selectedIndex ? onCollectionModeChange : undefined}
          onTogglePerformer={i === selectedIndex ? onTogglePerformer : undefined}
          onToggleTag={i === selectedIndex ? onToggleTag : undefined}
          onToggleStudio={i === selectedIndex ? onToggleStudio : undefined}
          taggerConfig={taggerConfig}
        />
      ))}
    </div>
  );
}

function TaggerResultRow({
  scene,
  result,
  isSelected,
  onClick,
  onSave,
  saving,
  saved,
  localDuration,
  excludedPerformers,
  excludedTags,
  skipStudio,
  forceIncludedPerformers,
  forceIncludedTags,
  forceIncludeStudio,
  fieldStrategies,
  collectionModes,
  onFieldStrategyChange,
  onCollectionModeChange,
  onTogglePerformer,
  onToggleTag,
  onToggleStudio,
  taggerConfig,
}: {
  scene: Scene;
  result: MetadataServerSceneMatch;
  isSelected: boolean;
  onClick: () => void;
  onSave?: () => void;
  saving?: boolean;
  saved?: boolean;
  localDuration?: number;
  excludedPerformers: Set<string>;
  excludedTags: Set<string>;
  skipStudio: boolean;
  forceIncludedPerformers: Set<string>;
  forceIncludedTags: Set<string>;
  forceIncludeStudio: boolean;
  fieldStrategies: Record<string, SceneFieldStrategy>;
  collectionModes: Record<string, CollectionMode>;
  onFieldStrategyChange?: (field: string, strategy: SceneFieldStrategy) => void;
  onCollectionModeChange?: (field: string, mode: CollectionMode) => void;
  onTogglePerformer?: (name: string) => void;
  onToggleTag?: (name: string) => void;
  onToggleStudio?: () => void;
  taggerConfig: TaggerConfig;
}) {
  const durationDiff = localDuration != null && result.duration != null
    ? Math.abs(localDuration - result.duration)
    : undefined;
  const durationMatch = durationDiff != null && durationDiff < 5;
  const scalarRows = [
    { key: "title", label: "Title", current: scene.title, scraped: result.title },
    { key: "code", label: "Code", current: scene.code, scraped: result.code },
    { key: "details", label: "Details", current: scene.details, scraped: result.details, multiline: true },
    { key: "director", label: "Director", current: scene.director, scraped: result.director },
    { key: "date", label: "Date", current: scene.date, scraped: result.date },
  ].filter((row) => Boolean(row.scraped));
  const currentTagNames = getSceneTagNames(scene);
  const currentPerformerNames = getScenePerformerNames(scene);
  const existingTagNames = result.tagCandidates.filter((tag) => tag.existsLocally).map((tag) => tag.name);
  const existingPerformerNames = result.performerCandidates.filter((performer) => performer.existsLocally).map((performer) => performer.name);
  const tagActions = buildSceneRelationActionMap(result.tagNames, currentTagNames, existingTagNames, excludedTags, forceIncludedTags, !taggerConfig.onlyExistingTags);
  const performerActions = buildSceneRelationActionMap(result.performerNames, currentPerformerNames, existingPerformerNames, excludedPerformers, forceIncludedPerformers, !taggerConfig.onlyExistingPerformers);

  return (
    <div
      onClick={onClick}
      className={`rounded border cursor-pointer transition-colors ${
        isSelected
          ? "border-accent bg-card"
          : "border-border bg-surface hover:border-accent/50"
      }`}
    >
      {/* Header row — always visible for all results */}
      <div className="flex items-center gap-3 p-2">
        {/* Radio selector for multiple results */}
        <div className="flex-shrink-0">
          <div className={`w-4 h-4 rounded-full border-2 flex items-center justify-center ${isSelected ? "border-accent" : "border-border"}`}>
            {isSelected && <div className="w-2 h-2 rounded-full bg-accent" />}
          </div>
        </div>

        {/* Cover thumbnail */}
        {result.imageUrl && (
          <img src={result.imageUrl} alt="" className="w-20 h-12 object-cover rounded flex-shrink-0" loading="lazy" />
        )}

        <div className="flex-1 min-w-0">
          <p className="text-xs font-medium text-foreground truncate">
            {result.title || "Untitled"}
            {result.code && <span className="text-muted ml-1">({result.code})</span>}
          </p>
          {result.details && (
            <p className="mt-1 text-[11px] leading-relaxed text-secondary line-clamp-2">
              {result.details}
            </p>
          )}
          <div className="flex items-center gap-3 text-[10px] text-muted mt-0.5">
            {result.date && <span>Date: <span className="text-foreground">{result.date}</span></span>}
            {result.director && <span>Director: <span className="text-foreground">{result.director}</span></span>}
            {result.duration != null && (
              <span>
                Duration: <span className="text-foreground">{formatDuration(result.duration)}</span>
                {durationDiff != null && (
                  <span className={durationMatch ? " text-green-400" : durationDiff < 30 ? " text-yellow-400" : " text-red-400"}>
                    {" "}({durationDiff < 1 ? "exact" : `${Math.round(durationDiff)}s diff`})
                  </span>
                )}
              </span>
            )}
            {result.performerNames.length > 0 && (
              <span className="truncate">{result.performerNames.join(", ")}</span>
            )}
          </div>
        </div>

        {/* Fingerprint indicators — shows which algorithms the remote scene has, with match status */}
        {result.fingerprints.length > 0 && (() => {
          const remoteAlgos = [...new Set(result.fingerprints.map(fp => fp.algorithm.toUpperCase()))];
          const matchedSet = new Set(result.fingerprintAlgorithms.map(a => a.toUpperCase()));
          return (
            <span className="flex items-center gap-1 text-[9px] px-2 py-0.5 rounded bg-surface flex-shrink-0" title={result.matchCount > 0 ? `${result.matchCount} fingerprint match${result.matchCount !== 1 ? "es" : ""}` : "No fingerprint matches"}>
              <Fingerprint className={`w-3 h-3 ${result.matchCount > 0 ? "text-green-400" : "text-muted"}`} />
              {remoteAlgos.map((alg, i) => (
                <span key={alg} className={`font-semibold ${matchedSet.has(alg) ? "text-green-300" : "text-muted"}`}>{i > 0 && " · "}{alg}</span>
              ))}
              {result.matchCount > 0 && (
                <span className="text-green-300 opacity-70 ml-0.5">({result.matchCount})</span>
              )}
            </span>
          );
        })()}

        {/* Save button (inline for selected) */}
        {isSelected && onSave && !saved && (
          <button
            onClick={(e) => { e.stopPropagation(); onSave(); }}
            disabled={saving}
            className="flex items-center gap-1.5 px-4 py-1.5 rounded text-xs font-medium bg-green-600 text-white hover:bg-green-500 disabled:opacity-60 flex-shrink-0"
          >
            {saving ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-3.5 h-3.5" />}
            Save
          </button>
        )}
      </div>

      {/* Expanded details — only for selected result */}
      {isSelected && (
        <div className="border-t border-border px-3 py-3 space-y-3">
          {scalarRows.map((row) => (
            <CompactScalarDecision
              key={row.key}
              label={row.label}
              current={row.current}
              scraped={row.scraped}
              multiline={row.multiline}
              replacing={fieldStrategies[row.key] === "overwrite"}
              onChange={(shouldReplace) => onFieldStrategyChange?.(row.key, shouldReplace ? "overwrite" : "ignore")}
            />
          ))}

          {result.studioName && taggerConfig.setStudio && (
            <CompactScalarDecision
              label="Studio"
              current={scene.studioName}
              scraped={result.studioName}
              replacing={collectionModes.studio === "replace"}
              onChange={(shouldReplace) => onCollectionModeChange?.("studio", shouldReplace ? "replace" : "skip")}
            />
          )}

          {result.urls.length > 0 && (
            <CompactCollectionDecision
              label="URLs"
              current={scene.urls}
              mode={collectionModes.urls}
              onModeChange={(mode) => onCollectionModeChange?.("urls", mode)}
              scraped={<CompactListValue values={result.urls} breakAll />}
            />
          )}

          {result.performerNames.length > 0 && taggerConfig.setPerformers && (
            <CompactCollectionDecision
              label="Performers"
              current={currentPerformerNames}
              mode={collectionModes.performers}
              onModeChange={(mode) => onCollectionModeChange?.("performers", mode)}
              scraped={(
                <div onClick={(event) => event.stopPropagation()}>
                  <ScrapeRelationChoices
                    names={result.performerNames}
                    currentNames={currentPerformerNames}
                    existingNames={existingPerformerNames}
                    actions={performerActions}
                    disabled={collectionModes.performers === "skip"}
                    onActionChange={(name) => onTogglePerformer?.(name)}
                  />
                </div>
              )}
            />
          )}

          {result.tagNames.length > 0 && taggerConfig.setTags && (
            <CompactCollectionDecision
              label="Tags"
              current={currentTagNames}
              mode={collectionModes.tags}
              onModeChange={(mode) => onCollectionModeChange?.("tags", mode)}
              scraped={(
                <div onClick={(event) => event.stopPropagation()}>
                  <ScrapeRelationChoices
                    names={result.tagNames}
                    currentNames={currentTagNames}
                    existingNames={existingTagNames}
                    actions={tagActions}
                    disabled={collectionModes.tags === "skip"}
                    onActionChange={(name) => onToggleTag?.(name)}
                  />
                </div>
              )}
            />
          )}
        </div>
      )}
    </div>
  );
}

