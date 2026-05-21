import { useMemo, useState, useCallback, useEffect, useRef, lazy, Suspense } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { aiVisual, entityEngagement, entityImages, scenes, tags, performers, galleries } from "../api/client";
import type { BoolCriterion, EntityEngagement, FindFilter, Group, Scene, SceneCreate, SceneFilterCriteria, SceneListEntry } from "../api/types";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { useListUrlState } from "../hooks/useListUrlState";
import { usePaginatedInfiniteQuery } from "../hooks/usePaginatedInfiniteQuery";
import { useAiVisualAvailability } from "../hooks/useAiVisualAvailability";
import { SceneTagger } from "../components/SceneTagger";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { useEntityEngagementBatch } from "../hooks/useEntityEngagementBatch";
import { CustomFieldsEditor, formatDuration, formatFileSize, getResolutionLabel, RatingBadge } from "../components/shared";
import { SCENE_CRITERIA, type CriterionDefinition } from "../components/FilterDialog";
import { BulkEditDialog, SCENE_BULK_FIELDS } from "../components/BulkEditDialog";
import { CreateModalActions, EditModal, Field, TextArea, TextInput } from "../components/EditModal";
import { Film, Eye, Trash2, Loader2, Edit, Merge, Search, Play, Pause, Download, Layers, Maximize2, Minimize2, Volume2, VolumeX, ThumbsUp, Heart } from "lucide-react";
import { useSceneQueue } from "../state/SceneQueueContext";
import { SceneCard } from "../components/EntityCards";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../components/RouteCardLinkOverlay";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity, hasAnyPermission } from "../auth/visibility";
import { StringListEditor } from "../components/StringListEditor";
import { SCENE_SORT_OPTIONS } from "../components/sceneSortOptions";
import { useWallColumns } from "../hooks/useWallColumns";
import { useAppConfig } from "../state/AppConfigContext";
import { StudioSelector } from "../components/StudioSelector";
import { ExtensionSelectionActions } from "../components/ExtensionSelectionActions";
import { withSeededRandomSort } from "../utils/seededRandomSort";
import { WallMediaCard, type WallMediaVideoControlsState } from "../components/WallMediaCard";
import { FeedActionPill, FeedCardFrame, FeedChipButton, FeedChipOverflowMenu, FeedIdentityBadge, FeedInlineRating, FeedMetadataPill, FeedPortraitMediaFrame, getFeedMediaStyle } from "../components/FeedCardFrame";
import { BookmarkButton } from "../components/BookmarkButton";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { FileBackedCreateSource, type CreateSourceMode } from "../components/FileBackedCreateSource";
import { createFromUrlWithOptionalDownload, mergeUrlLists, NoDownloaderFoundError, type UrlDownloadMode } from "../utils/createFromUrlDownload";
import { useFileBackedCreatePreferences } from "../hooks/useFileBackedCreatePreferences";
import { VirtualizedInfiniteList } from "../components/VirtualizedInfiniteList";
import { VirtualizedEntityGrid, VirtualizedWallColumns } from "../components/VirtualizedEntityLayouts";
import {
  formatBatchDownloadSummary,
  getBatchDownloadOptionsStorageKey,
  getUndownloadedSelectionItems,
  loadStoredBatchDownloadOptions,
  queueBatchDownloads,
  saveStoredBatchDownloadOptions,
  type BatchDownloadOptions,
} from "../utils/batchDownloads";
import { fetchAllMatchingIds } from "../utils/selectAllMatching";

import { getDefaultFilter } from "../components/SavedFilterMenu";

const SceneDownloadDialog = lazy(() => import("../components/SceneDownloadDialog").then((module) => ({ default: module.SceneDownloadDialog })));
const SceneBatchScrapeDialog = lazy(() => import("../components/SceneBatchScrapeDialog").then((module) => ({ default: module.SceneBatchScrapeDialog })));
const BatchDownloadOptionsDialog = lazy(() => import("../components/BatchDownloadOptionsDialog").then((module) => ({ default: module.BatchDownloadOptionsDialog })));
const MergeDialog = lazy(() => import("../components/MergeDialog").then((module) => ({ default: module.MergeDialog })));
const IdentifyDialog = lazy(() => import("../components/IdentifyDialog").then((module) => ({ default: module.IdentifyDialog })));
const QuickViewDialog = lazy(() => import("../components/QuickViewDialog").then((module) => ({ default: module.QuickViewDialog })));

const SEARCH_MODE_OPTIONS = [
  { value: "text", label: "Text", title: "Text search" },
  { value: "visual", label: "Visual", title: "Visual semantic search" },
];

const VISUAL_MATCH_SORT_OPTION = { value: "visual_match", label: "Visual Match" };
const INCLUDE_COMPILATIONS_FILTER_KEY = "includeCompilationGroups";
const IS_VR_FILTER_KEY = "isVrCriterion";
const VERTICAL_PORTRAIT_FILTER_KEY = "orientationCriterion";
const MOBILE_VIEWER_MEDIA_QUERY = "(max-width: 767px), (hover: none) and (pointer: coarse)";
const SCENE_FILTER_CRITERIA: CriterionDefinition[] = [
  ...SCENE_CRITERIA,
  { id: "includeCompilations", label: "Include Compilations", type: "bool", filterKey: INCLUDE_COMPILATIONS_FILTER_KEY },
];

function isMobileViewerViewport() {
  return typeof window !== "undefined"
    && typeof window.matchMedia === "function"
    && window.matchMedia(MOBILE_VIEWER_MEDIA_QUERY).matches;
}

function getBoolCriterionValue(value: unknown) {
  if (typeof value === "boolean") {
    return value;
  }

  const criterionValue = (value as BoolCriterion | undefined)?.value;
  return typeof criterionValue === "boolean" ? criterionValue : undefined;
}

function isIncludeCompilationGroupsEnabled(value: unknown) {
  return getBoolCriterionValue(value) === true;
}

interface Props {
  onNavigate: (r: any) => void;
}

export function ScenesPage({ onNavigate }: Props) {
  const defaultState = useMemo(() => {
    const savedFilter = getDefaultFilter("scenes");
    return {
      filter: savedFilter?.findFilter ?? { page: 1, perPage: 40, sort: "date", direction: "desc" },
      objectFilter: savedFilter?.objectFilter ?? {},
      displayMode: "grid" as DisplayMode,
    };
  }, []);
  const aiVisualAvailable = useAiVisualAvailability();
  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode, searchMode, setSearchMode } = useListUrlState({
    resetKey: "scenes",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list", "wall", "tagger", "feed", "vertical"] as const,
    defaultSearchMode: "text",
    allowedSearchModes: aiVisualAvailable ? ["text", "visual"] : ["text"],
    allowInfinitePageSize: true,
  });
  const [showCreate, setShowCreate] = useState(false);
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [showMerge, setShowMerge] = useState(false);
  const [showIdentify, setShowIdentify] = useState(false);
  const [showBatchScrape, setShowBatchScrape] = useState(false);
  const [showBatchDownloadOptions, setShowBatchDownloadOptions] = useState(false);
  const [selectAllMatchingPending, setSelectAllMatchingPending] = useState(false);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const [wallColumnCount, setWallColumnCount] = useState(5);
  const [isMobileViewer, setIsMobileViewer] = useState(isMobileViewerViewport);
  const verticalViewerRef = useRef<HTMLDivElement>(null);
  const [verticalFullscreen, setVerticalFullscreen] = useState(false);
  const [verticalFullscreenDismissed, setVerticalFullscreenDismissed] = useState(false);
  const [verticalViewerTop, setVerticalViewerTop] = useState(0);
  const [verticalViewerHeight, setVerticalViewerHeight] = useState<number | null>(null);
  const [verticalSoundEnabled, setVerticalSoundEnabled] = useState(false);
  const [activeVerticalSceneId, setActiveVerticalSceneId] = useState<number | null>(null);
  const [verticalAutoScrollEnabled, setVerticalAutoScrollEnabled] = useState(false);
  const [verticalAutoScrollSeconds, setVerticalAutoScrollSeconds] = useState(8);
  const [verticalAutoScrollAwake, setVerticalAutoScrollAwake] = useState(true);
  const [feedAudioSceneId, setFeedAudioSceneId] = useState<number | null>(null);
  const [downloadTarget, setDownloadTarget] = useState<Scene | "new" | null>(null);
  const queryClient = useQueryClient();
  const { setQueue } = useSceneQueue();
  const { hasPermission, user } = useAuth();
  const { config } = useAppConfig();
  const canWriteScene = canWriteEntity("scene", hasPermission);
  const canDeleteScene = canDeleteEntity("scene", hasPermission);
  const canEngageScene = canReadEntity("scene", hasPermission) && (user?.kind === "user" || user?.kind === "system");
  const canScrapeScene = hasAnyPermission(hasPermission, ["scenes.scrape", "scenes.write"]);
  const canIdentifyScene = hasPermission("library.autotag") && canWriteScene;
  const canDownloadScene = hasPermission("jobs.run") && canWriteScene;
  const feedVideoSource = config?.ui.feedVideoSource ?? "preview";
  const feedVideoSound = config?.ui.feedVideoSound ?? false;
  const defaultFeedVideoSound = feedVideoSound && !isMobileViewer;
  const feedVideoStartPercent = config?.ui.feedVideoStartPercent ?? 0;
  const feedVideoStartMinDuration = config?.ui.feedVideoStartMinDuration ?? 0;
  const infiniteOnlyDisplayMode = displayMode === "feed" || displayMode === "vertical";
  const verticalItemHeight = verticalFullscreen
    ? (typeof window !== "undefined" ? window.innerHeight : 720)
    : (verticalViewerHeight ?? (typeof window !== "undefined" ? window.innerHeight : 720));

  useEffect(() => {
    if (typeof window.matchMedia !== "function") {
      setIsMobileViewer(false);
      return;
    }

    const mediaQuery = window.matchMedia(MOBILE_VIEWER_MEDIA_QUERY);
    const syncMobileViewer = () => setIsMobileViewer(mediaQuery.matches);
    syncMobileViewer();
    if (typeof mediaQuery.addEventListener === "function") {
      mediaQuery.addEventListener("change", syncMobileViewer);
      return () => mediaQuery.removeEventListener("change", syncMobileViewer);
    }

    mediaQuery.addListener(syncMobileViewer);
    return () => mediaQuery.removeListener(syncMobileViewer);
  }, []);

  useEffect(() => {
    if (displayMode !== "vertical") {
      setVerticalFullscreen(false);
      setVerticalFullscreenDismissed(false);
      setVerticalAutoScrollEnabled(false);
      setActiveVerticalSceneId(null);
      return;
    }

    const mediaQuery = window.matchMedia("(max-width: 767px)");
    const syncMobileFullscreen = () => {
      if (mediaQuery.matches && !verticalFullscreenDismissed) {
        setVerticalFullscreen(true);
      }
    };

    syncMobileFullscreen();
    mediaQuery.addEventListener("change", syncMobileFullscreen);
    return () => mediaQuery.removeEventListener("change", syncMobileFullscreen);
  }, [displayMode, verticalFullscreenDismissed]);

  useEffect(() => {
    if (displayMode === "vertical") {
      setVerticalSoundEnabled(defaultFeedVideoSound);
    }
  }, [defaultFeedVideoSound, displayMode]);

  useEffect(() => {
    if (displayMode !== "vertical" || verticalFullscreen) {
      setVerticalViewerTop(0);
      setVerticalViewerHeight(null);
      return;
    }

    const updateVerticalBounds = () => {
      const element = verticalViewerRef.current;
      if (!element) return;
      const top = Math.max(0, element.getBoundingClientRect().top);
      const height = Math.max(120, window.innerHeight - top);
      setVerticalViewerTop((current) => Math.abs(current - top) > 0.5 ? top : current);
      setVerticalViewerHeight((current) => current == null || Math.abs(current - height) > 0.5 ? height : current);
    };

    updateVerticalBounds();
    const frameId = window.requestAnimationFrame(updateVerticalBounds);
    window.addEventListener("resize", updateVerticalBounds);
    const resizeObserver = typeof ResizeObserver !== "undefined" ? new ResizeObserver(updateVerticalBounds) : null;
    if (resizeObserver) {
      resizeObserver.observe(document.body);
      if (verticalViewerRef.current?.parentElement) {
        resizeObserver.observe(verticalViewerRef.current.parentElement);
      }
    }

    return () => {
      window.cancelAnimationFrame(frameId);
      window.removeEventListener("resize", updateVerticalBounds);
      resizeObserver?.disconnect();
    };
  }, [displayMode, verticalFullscreen]);

  useEffect(() => {
    if (!verticalFullscreen) {
      return;
    }

    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.body.style.overflow = previousOverflow;
    };
  }, [verticalFullscreen]);

  useEffect(() => {
    if (displayMode !== "feed") {
      setFeedAudioSceneId(null);
    }
  }, [displayMode]);

  const wakeVerticalAutoScroll = useCallback(() => setVerticalAutoScrollAwake(true), []);

  useEffect(() => {
    if (displayMode !== "vertical" || !verticalAutoScrollAwake) {
      return;
    }

    const timeoutId = window.setTimeout(() => setVerticalAutoScrollAwake(false), verticalAutoScrollEnabled ? 2600 : 3600);
    return () => window.clearTimeout(timeoutId);
  }, [displayMode, verticalAutoScrollAwake, verticalAutoScrollEnabled, verticalAutoScrollSeconds]);

  const normalizedObjectFilter = useMemo(() => {
    const includeValue = objectFilter[INCLUDE_COMPILATIONS_FILTER_KEY];
    if (typeof includeValue !== "boolean") {
      return objectFilter;
    }

    return { ...objectFilter, [INCLUDE_COMPILATIONS_FILTER_KEY]: { value: includeValue } satisfies BoolCriterion };
  }, [objectFilter]);

  const effectiveObjectFilter = useMemo(() => {
    if (user?.uiPreferences?.scenes?.excludeVr !== true || normalizedObjectFilter[IS_VR_FILTER_KEY]) {
      return normalizedObjectFilter;
    }

    return { ...normalizedObjectFilter, [IS_VR_FILTER_KEY]: { value: false } satisfies BoolCriterion };
  }, [normalizedObjectFilter, user?.uiPreferences?.scenes?.excludeVr]);

  const backendObjectFilter = useMemo(() => Object.fromEntries(
    Object.entries(effectiveObjectFilter).filter(([key]) => key !== INCLUDE_COMPILATIONS_FILTER_KEY),
  ), [effectiveObjectFilter]);
  const hasObjectFilter = Object.keys(backendObjectFilter).length > 0;
  const compilationBlockingObjectFilter = useMemo(() => Object.fromEntries(
    Object.entries(backendObjectFilter).filter(([key, value]) => key !== IS_VR_FILTER_KEY || getBoolCriterionValue(value) !== false),
  ), [backendObjectFilter]);
  const hasCompilationBlockingObjectFilter = Object.keys(compilationBlockingObjectFilter).length > 0;
  const sceneVrFilterValue = getBoolCriterionValue(backendObjectFilter[IS_VR_FILTER_KEY]);
  const compilationQueryExtra = useMemo(() => sceneVrFilterValue === false ? { isVr: false } : undefined, [sceneVrFilterValue]);
  const visualSearchActive = aiVisualAvailable && searchMode === "visual" && Boolean(filter.q?.trim());
  const infinitePageSize = filter.perPage === 0 || infiniteOnlyDisplayMode;
  const defaultInfiniteChunkSize = defaultState.filter.perPage && defaultState.filter.perPage > 0 ? defaultState.filter.perPage : 40;
  const infiniteChunkSize = displayMode === "vertical" ? 6 : displayMode === "feed" ? 10 : defaultInfiniteChunkSize;
  const infiniteFilterKey = useMemo(
    () => ({ ...filter, page: 1, perPage: infiniteChunkSize }),
    [filter, infiniteChunkSize],
  );

  useEffect(() => {
    if (infiniteOnlyDisplayMode && filter.perPage !== 0) {
      setFilter({ ...filter, page: 1, perPage: 0 });
    }
  }, [filter, infiniteOnlyDisplayMode, setFilter]);
  const searchModeOptions = useMemo(() => aiVisualAvailable ? SEARCH_MODE_OPTIONS : SEARCH_MODE_OPTIONS.filter((mode) => mode.value === "text"), [aiVisualAvailable]);
  const sortOptions = useMemo(
    () => aiVisualAvailable && searchMode === "visual" ? [VISUAL_MATCH_SORT_OPTION, ...SCENE_SORT_OPTIONS] : SCENE_SORT_OPTIONS,
    [aiVisualAvailable, searchMode],
  );

  useEffect(() => {
    if (!aiVisualAvailable && searchMode === "visual") {
      setSearchMode("text");
      if (filter.sort === "visual_match") {
        setFilter({ ...filter, sort: defaultState.filter.sort, direction: defaultState.filter.direction ?? "desc", page: 1 });
      }
    }
  }, [aiVisualAvailable, defaultState.filter.direction, defaultState.filter.sort, filter, searchMode, setFilter, setSearchMode]);

  const handleSearchModeChange = useCallback((mode: string) => {
    if (mode === "visual" && !aiVisualAvailable) {
      return;
    }

    setSearchMode(mode);

    if (mode === "visual") {
      setFilter({ ...filter, sort: "visual_match", direction: "desc", page: 1 });
      return;
    }

    if (filter.sort === "visual_match") {
      setFilter({
        ...filter,
        sort: defaultState.filter.sort,
        direction: defaultState.filter.direction ?? "desc",
        page: 1,
      });
      return;
    }

    setFilter({ ...filter, page: 1 });
  }, [aiVisualAvailable, defaultState.filter.direction, defaultState.filter.sort, filter, setFilter, setSearchMode]);

  const handleDisplayModeChange = useCallback((mode: DisplayMode) => {
    setDisplayMode(mode);

    if (mode === "vertical" && !objectFilter[VERTICAL_PORTRAIT_FILTER_KEY] && Object.keys(objectFilter).length === 0) {
      setObjectFilter({ [VERTICAL_PORTRAIT_FILTER_KEY]: { value: "portrait" } });
    }

    const requiresInfinite = mode === "feed" || mode === "vertical";
    if (filter.page !== 1 || (requiresInfinite && filter.perPage !== 0)) {
      setFilter({ ...filter, page: 1, perPage: requiresInfinite ? 0 : filter.perPage });
    }
  }, [filter, objectFilter, setDisplayMode, setFilter, setObjectFilter]);

  const includeCompilationGroups = isIncludeCompilationGroupsEnabled(normalizedObjectFilter[INCLUDE_COMPILATIONS_FILTER_KEY]);
  const canShowCompilationGroups = !infinitePageSize && includeCompilationGroups && searchMode === "text" && !hasCompilationBlockingObjectFilter && (displayMode === "grid" || displayMode === "list");

  const { data, isLoading } = useQuery({
    queryKey: ["scenes", filter, backendObjectFilter, searchMode],
    queryFn: () => {
      if (visualSearchActive) {
        return aiVisual.searchScenes({
          findFilter: filter,
          objectFilter: hasObjectFilter ? backendObjectFilter as SceneFilterCriteria : undefined,
        });
      }

      return hasObjectFilter
        ? scenes.findFiltered({ findFilter: filter, objectFilter: backendObjectFilter as SceneFilterCriteria })
        : scenes.find(filter);
    },
    enabled: !infinitePageSize && !canShowCompilationGroups,
  });

  const { data: unifiedData, isLoading: unifiedLoading } = useQuery({
    queryKey: ["scenes", "with-compilations", filter, compilationQueryExtra],
    queryFn: () => scenes.findWithCompilations(filter, compilationQueryExtra),
    enabled: !infinitePageSize && canShowCompilationGroups,
  });

  const infiniteScenesQuery = usePaginatedInfiniteQuery<Scene>({
    queryKey: ["scenes", "infinite", infiniteFilterKey, backendObjectFilter, searchMode],
    enabled: infinitePageSize,
    chunkSize: infiniteChunkSize,
    queryFn: (page, perPage) => {
      const nextFilter = { ...filter, page, perPage };
      if (visualSearchActive) {
        return aiVisual.searchScenes({
          findFilter: nextFilter,
          objectFilter: hasObjectFilter ? backendObjectFilter as SceneFilterCriteria : undefined,
        });
      }

      return hasObjectFilter
        ? scenes.findFiltered({ findFilter: nextFilter, objectFilter: backendObjectFilter as SceneFilterCriteria })
        : scenes.find(nextFilter);
    },
  });

  const defaultListEntries: SceneListEntry[] = canShowCompilationGroups
    ? (unifiedData?.items ?? [])
    : (data?.items ?? []).map((scene) => ({ kind: "scene" as const, id: scene.id, scene }));
  const defaultItems = defaultListEntries.flatMap((entry) => entry.kind === "scene" && entry.scene ? [entry.scene] : []);
  const items = infinitePageSize ? infiniteScenesQuery.items : defaultItems;
  const listEntries = infinitePageSize
    ? items.map((scene) => ({ kind: "scene" as const, id: scene.id, scene }))
    : defaultListEntries;
  const totalCount = infinitePageSize
    ? infiniteScenesQuery.totalCount
    : (canShowCompilationGroups ? unifiedData?.totalCount : data?.totalCount);
  const loading = infinitePageSize
    ? infiniteScenesQuery.isPending
    : (canShowCompilationGroups ? unifiedLoading : isLoading);
  const loadMoreScenes = useCallback(() => {
    if (infiniteScenesQuery.hasNextPage && !infiniteScenesQuery.isFetchingNextPage) {
      void infiniteScenesQuery.fetchNextPage();
    }
  }, [infiniteScenesQuery.fetchNextPage, infiniteScenesQuery.hasNextPage, infiniteScenesQuery.isFetchingNextPage]);

  useEffect(() => {
    if (displayMode !== "feed") {
      setFeedAudioSceneId(null);
      return;
    }
    if (!defaultFeedVideoSound) setFeedAudioSceneId(null);
  }, [defaultFeedVideoSound, displayMode]);

  useEffect(() => {
    if (displayMode !== "vertical") {
      setActiveVerticalSceneId(null);
    }
  }, [displayMode]);

  useEffect(() => {
    if (displayMode !== "vertical" || !verticalAutoScrollEnabled || activeVerticalSceneId == null) {
      return;
    }

    const timeoutId = window.setTimeout(() => {
      const root = verticalViewerRef.current;
      if (!root) return;
      const currentIndex = items.findIndex((scene) => scene.id === activeVerticalSceneId);
      const nextIndex = currentIndex >= 0 ? currentIndex + 1 : 0;
      if (nextIndex >= items.length) {
        setVerticalAutoScrollEnabled(false);
        return;
      }
      root.scrollTo({ top: nextIndex * verticalItemHeight, behavior: "smooth" });
    }, verticalAutoScrollSeconds * 1000);

    return () => window.clearTimeout(timeoutId);
  }, [activeVerticalSceneId, displayMode, items, verticalAutoScrollEnabled, verticalAutoScrollSeconds, verticalItemHeight]);
  const { engagementById } = useEntityEngagementBatch("scene", items.map((item) => item.id));
  const wallColumns = useWallColumns(items, wallColumnCount, (scene) => {
    const file = scene.files[0];
    return file?.width && file.height ? file.height / file.width : 9 / 16;
  });
  const selectionResetKey = useMemo(() => JSON.stringify({ filter: infiniteFilterKey, objectFilter: backendObjectFilter, searchMode }), [backendObjectFilter, infiniteFilterKey, searchMode]);
  const { selectedIds, toggle, selectAll, selectIds, selectNone, invertSelection } = useMultiSelect(items, { preserveOnAppend: infinitePageSize, resetKey: selectionResetKey });
  const selecting = selectedIds.size > 0;
  const selectedScene = selectedIds.size === 1 ? items.find((scene) => selectedIds.has(scene.id)) : undefined;
  const selectedDownloadTargets = useMemo(() => getUndownloadedSelectionItems(items, selectedIds), [items, selectedIds]);
  const canDownloadSelectedScene = canDownloadScene && selectedDownloadTargets.length > 0;
  const batchDownloadStorageKey = getBatchDownloadOptionsStorageKey("page-scenes");
  const [batchDownloadOptions, setBatchDownloadOptions] = useState<BatchDownloadOptions>(() => loadStoredBatchDownloadOptions(batchDownloadStorageKey));
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  useEffect(() => {
    setBatchDownloadOptions(loadStoredBatchDownloadOptions(batchDownloadStorageKey));
  }, [batchDownloadStorageKey]);

  const navigateToScene = useCallback((sceneId: number) => {
    const ids = items.map((s) => s.id);
    if (ids.length > 0) {
      setQueue(ids, sceneId, items.map((scene) => ({
        id: scene.id,
        title: scene.title || scene.files[0]?.basename || `Scene ${scene.id}`,
        subtitle: scene.studioName || scene.date || undefined,
        imagePath: scenes.screenshotUrl(scene.id, scene.updatedAt),
      })));
    }
    onNavigate({ page: "scene", id: sceneId });
  }, [items, setQueue, onNavigate]);

  const handlePlaySelected = useCallback(() => {
    const selectedScenes = items.filter((scene) => selectedIds.has(scene.id));
    const ids = selectedScenes.map((scene) => scene.id);
    if (ids.length === 0) {
      return;
    }

    setQueue(ids, ids[0], selectedScenes.map((scene) => ({
      id: scene.id,
      title: scene.title || scene.files[0]?.basename || `Scene ${scene.id}`,
      subtitle: scene.studioName || scene.date || undefined,
      imagePath: scenes.screenshotUrl(scene.id, scene.updatedAt),
    })));
    selectNone();
    onNavigate({ page: "scene", id: ids[0] });
  }, [items, onNavigate, selectNone, selectedIds, setQueue]);

  const handleSelectAllMatching = useCallback(async () => {
    setSelectAllMatchingPending(true);
    try {
      const ids = await fetchAllMatchingIds<Scene>(filter, (nextFilter) => {
        if (visualSearchActive) {
          return aiVisual.searchScenes({
            findFilter: nextFilter,
            objectFilter: hasObjectFilter ? backendObjectFilter as SceneFilterCriteria : undefined,
          });
        }

        return hasObjectFilter
          ? scenes.findFiltered({ findFilter: nextFilter, objectFilter: backendObjectFilter as SceneFilterCriteria })
          : scenes.find(nextFilter);
      });
      selectIds(ids);
    } finally {
      setSelectAllMatchingPending(false);
    }
  }, [backendObjectFilter, filter, hasObjectFilter, selectIds, visualSearchActive]);

  // When sort changes to random, generate a new seed for reproducibility
  const handleFilterChange = useCallback((next: typeof filter) => {
    setFilter(withSeededRandomSort(filter, next));
  }, [filter, setFilter]);

  // Bulk delete
  const bulkDeleteMut = useMutation({
    mutationFn: (options?: { deleteFile?: boolean; deleteGenerated?: boolean }) => scenes.bulkDelete([...selectedIds], options),
    onSuccess: () => {
      setShowDeleteConfirm(false);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["scenes"] });
    },
  });

  // Bulk edit
  const bulkEditMut = useMutation({
    mutationFn: (values: Record<string, unknown>) =>
      scenes.bulkUpdate({
        ids: [...selectedIds],
        ...values,
      } as any),
    onSuccess: () => {
      setShowBulkEdit(false);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["scenes"] });
    },
  });

  const batchDownloadMut = useMutation({
    mutationFn: async (options: BatchDownloadOptions) => queueBatchDownloads("Scene", selectedDownloadTargets, options),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      queryClient.invalidateQueries({ queryKey: ["jobs-active"] });
      queryClient.invalidateQueries({ queryKey: ["jobs-history"] });
      queryClient.invalidateQueries({ queryKey: ["scenes"] });
      window.alert(formatBatchDownloadSummary("scene", result));
      selectNone();
    },
    onError: (error: Error) => {
      window.alert(error.message || "Failed to queue the selected downloads.");
    },
  });

  const verticalOverlayTop = verticalFullscreen ? 12 : Math.max(12, verticalViewerTop + 12);
  const verticalAutoScrollTop = verticalFullscreen
    ? (isMobileViewer ? "64%" : "50%")
    : verticalOverlayTop + (isMobileViewer ? 96 : 44);
  const verticalViewerStyle = verticalFullscreen ? undefined : { height: verticalViewerHeight != null ? `${verticalViewerHeight}px` : "calc(100dvh - 10rem)" };
  const verticalActiveIndex = useMemo(() => items.findIndex((scene) => scene.id === activeVerticalSceneId), [items, activeVerticalSceneId]);

  return (
    <>
    <SceneCreateModal open={showCreate} onClose={() => setShowCreate(false)} onCreated={(id) => onNavigate({ page: "scene", id })} />
    <Suspense fallback={null}>
      {downloadTarget !== null ? (
        <SceneDownloadDialog
          open={downloadTarget !== null}
          scene={downloadTarget !== "new" ? downloadTarget : undefined}
          onClose={() => setDownloadTarget(null)}
          onNavigate={onNavigate}
        />
      ) : null}
      {showBatchScrape ? (
        <SceneBatchScrapeDialog
          open={showBatchScrape}
          onClose={() => setShowBatchScrape(false)}
          scenes={items.filter((scene) => selectedIds.has(scene.id))}
        />
      ) : null}
      {showBatchDownloadOptions ? (
        <BatchDownloadOptionsDialog
          open={showBatchDownloadOptions}
          entity="Scene"
          itemCount={selectedDownloadTargets.length}
          initialOptions={batchDownloadOptions}
          isPending={batchDownloadMut.isPending}
          onClose={() => setShowBatchDownloadOptions(false)}
          onConfirm={(options) => {
            setBatchDownloadOptions(options);
            saveStoredBatchDownloadOptions(batchDownloadStorageKey, options);
            setShowBatchDownloadOptions(false);
            batchDownloadMut.mutate(options);
          }}
        />
      ) : null}
    </Suspense>
    <ListPage
      title="Scenes"
      pageKey="scenes"
      filterMode="scenes"
      filter={filter}
      onFilterChange={handleFilterChange}
      totalCount={totalCount ?? 0}
      isLoading={loading}
      searchMode={searchMode}
      searchModes={searchModeOptions}
      searchPlaceholder={aiVisualAvailable && searchMode === "visual" ? "Search visuals..." : "Search scenes, tags, performers..."}
      onSearchModeChange={handleSearchModeChange}
      sortOptions={sortOptions}
      displayMode={displayMode}
      onDisplayModeChange={handleDisplayModeChange}
      availableDisplayModes={["grid", "list", "wall", "tagger", "feed", "vertical"]}
      allowInfinitePageSize
      infinitePageSizeOnly={infiniteOnlyDisplayMode}
      criteriaDefinitions={SCENE_FILTER_CRITERIA}
      objectFilter={normalizedObjectFilter}
      onObjectFilterChange={setObjectFilter}
      wallColumnCount={wallColumnCount}
      onWallColumnCountChange={setWallColumnCount}
      infiniteScroll={infinitePageSize ? {
        hasNextPage: Boolean(infiniteScenesQuery.hasNextPage),
        isFetchingNextPage: infiniteScenesQuery.isFetchingNextPage,
        onLoadMore: loadMoreScenes,
        loadedCount: infiniteScenesQuery.loadedThroughCount,
        totalCount: infiniteScenesQuery.totalCount,
      } : undefined}
      autoScrollContainerRef={displayMode === "vertical" ? verticalViewerRef : undefined}
      showAutoScrollControls={displayMode !== "vertical"}
      showPagingControls={!infinitePageSize}
      onSelectAll={infinitePageSize ? handleSelectAllMatching : selectAll}
      selectAllPending={infinitePageSize ? selectAllMatchingPending : false}
      onSelectAllMatching={infinitePageSize ? selectAll : undefined}
      selectAllMatchingLabel="Select shown"
      onNew={canWriteScene ? () => setShowCreate(true) : undefined}
      selectedIds={selectedIds}
      onSelectNone={selectNone}
      onInvertSelection={invertSelection}
      selectionActions={
        <>
          {canDownloadSelectedScene && (
            <button
              onClick={() => {
                if (selectedDownloadTargets.length > 1 || !selectedScene) {
                  setShowBatchDownloadOptions(true);
                  return;
                }

                setDownloadTarget(selectedScene);
              }}
              disabled={batchDownloadMut.isPending}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-cyan-400 hover:text-cyan-300 hover:bg-cyan-900/20 disabled:opacity-60"
            >
              {batchDownloadMut.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Download className="w-3 h-3" />}
              Download
            </button>
          )}
          {canWriteScene && (
            <button
              onClick={() => setShowBulkEdit(true)}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
            >
              <Edit className="w-3 h-3" />
              Edit
            </button>
          )}
          {canIdentifyScene && (
            <button
              onClick={() => setShowIdentify(true)}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
            >
              <Search className="w-3 h-3" />
              Identify
            </button>
          )}
          {canScrapeScene && (
            <button
              onClick={() => setShowBatchScrape(true)}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-cyan-400 hover:text-cyan-300 hover:bg-cyan-900/20"
            >
              <Search className="w-3 h-3" />
              Scrape
            </button>
          )}
          {canWriteScene && selectedIds.size >= 2 && (
            <button
              onClick={() => setShowMerge(true)}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-yellow-400 hover:text-yellow-300 hover:bg-yellow-900/20"
            >
              <Merge className="w-3 h-3" />
              Merge
            </button>
          )}
          <button
            onClick={handlePlaySelected}
            className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-green-400 hover:text-green-300 hover:bg-green-900/20"
          >
            <Play className="w-3 h-3" />
            Play
          </button>
          <ExtensionSelectionActions entityType="scene" selectedIds={selectedIds} />
          {canDeleteScene && (
            <button
              onClick={() => setShowDeleteConfirm(true)}
              disabled={bulkDeleteMut.isPending}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-red-400 hover:text-red-300 hover:bg-red-900/20"
            >
              {bulkDeleteMut.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Trash2 className="w-3 h-3" />}
              Delete
            </button>
          )}
        </>
      }
    >
      <ConfirmDialog
        open={showDeleteConfirm}
        title={`Delete ${selectedIds.size} scene${selectedIds.size === 1 ? "" : "s"}`}
        message={`Delete ${selectedIds.size} selected scene${selectedIds.size === 1 ? "" : "s"}? This cannot be undone.`}
        confirmLabel={bulkDeleteMut.isPending ? "Deleting..." : "Delete"}
        onConfirm={(options) => bulkDeleteMut.mutate(options)}
        onCancel={() => setShowDeleteConfirm(false)}
        showDeleteFile
        showDeleteGenerated
      />
      {displayMode === "vertical" && (
        <>
          <button
            type="button"
            onClick={() => {
              if (verticalFullscreen) {
                setVerticalFullscreen(false);
                setVerticalFullscreenDismissed(true);
              } else {
                setVerticalFullscreen(true);
                setVerticalFullscreenDismissed(false);
              }
            }}
            className={`fixed ${verticalFullscreen ? "left-3" : "right-3"} z-[95] rounded-full border border-white/15 bg-black/55 p-2 text-white shadow-lg backdrop-blur transition-colors hover:bg-black/75`}
            style={{ top: verticalOverlayTop }}
            aria-label={verticalFullscreen ? "Exit full screen" : "Enter full screen"}
            title={verticalFullscreen ? "Exit full screen" : "Enter full screen"}
          >
            {verticalFullscreen ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
          </button>
          {infinitePageSize && (
            <div className="pointer-events-none fixed right-3 z-[94] sm:right-5" style={{ top: verticalAutoScrollTop, transform: verticalFullscreen ? "translateY(-50%)" : undefined }}>
              <div
                className="pointer-events-auto relative flex min-h-36 w-12 items-center justify-end"
                onPointerEnter={wakeVerticalAutoScroll}
                onPointerMove={wakeVerticalAutoScroll}
                onFocusCapture={wakeVerticalAutoScroll}
              >
                {!verticalAutoScrollAwake && <div className="absolute right-0 h-12 w-1.5 rounded-l-full bg-white/70 shadow-lg" aria-hidden="true" />}
                <div className={`flex flex-col items-center gap-2 rounded-xl border border-white/15 bg-black/60 px-2 py-2 text-white shadow-2xl backdrop-blur transition-all duration-300 ${verticalAutoScrollAwake ? "translate-x-0 opacity-100" : "pointer-events-none translate-x-2 opacity-0"}`}>
                  <button
                    type="button"
                    onClick={() => {
                      wakeVerticalAutoScroll();
                      setVerticalAutoScrollEnabled((current) => !current);
                    }}
                    className={`rounded-md border border-transparent p-1.5 transition-colors hover:bg-white/15 focus:outline-none focus:border-white/50 ${verticalAutoScrollEnabled ? "text-accent" : "text-white"}`}
                    aria-label={verticalAutoScrollEnabled ? "Pause vertical auto-scroll" : "Start vertical auto-scroll"}
                    title={verticalAutoScrollEnabled ? "Pause vertical auto-scroll" : "Start vertical auto-scroll"}
                  >
                    {verticalAutoScrollEnabled ? <Pause className="h-4 w-4" /> : <Play className="h-4 w-4" />}
                  </button>
                  <input
                    type="range"
                    min={3}
                    max={30}
                    step={1}
                    value={verticalAutoScrollSeconds}
                    onChange={(event) => {
                      wakeVerticalAutoScroll();
                      setVerticalAutoScrollSeconds(Number(event.target.value));
                    }}
                    className="h-24 w-1 accent-accent [writing-mode:vertical-lr]"
                    aria-label="Seconds before next vertical item"
                    title={`${verticalAutoScrollSeconds}s before next item`}
                  />
                  <span className="text-[10px] text-white/80 tabular-nums [writing-mode:vertical-lr]">{verticalAutoScrollSeconds}s/item</span>
                </div>
              </div>
            </div>
          )}
          <div
            ref={verticalViewerRef}
            style={verticalViewerStyle}
            className={verticalFullscreen
              ? "fixed inset-0 z-[80] h-[100dvh] snap-y snap-mandatory overflow-y-auto bg-black px-0 py-0"
              : "relative -mx-3 -mb-5 snap-y snap-mandatory overflow-y-auto bg-black px-0 py-0 sm:-mx-4 md:-mx-6"}
          >
            <VirtualizedInfiniteList
              items={items}
              getItemKey={(scene) => scene.id}
              estimateSize={verticalItemHeight}
              overscan={2}
              hasNextPage={Boolean(infiniteScenesQuery.hasNextPage)}
              isFetchingNextPage={infiniteScenesQuery.isFetchingNextPage}
              loadMore={loadMoreScenes}
              scrollElementRef={verticalViewerRef}
              onActiveIndexChange={(idx) => setActiveVerticalSceneId(idx == null ? null : items[idx]?.id ?? null)}
              itemClassName="snap-start"
              renderItem={({ item: scene, index }) => (
                <SceneVerticalViewerCard
                  scene={scene}
                  useVideo={verticalActiveIndex < 0 ? index === 0 : Math.abs(index - verticalActiveIndex) <= 1}
                  feedVideoSource={feedVideoSource}
                  soundEnabled={verticalSoundEnabled && scene.id === activeVerticalSceneId}
                  onToggleSound={() => setVerticalSoundEnabled((current) => !current)}
                  feedVideoStartPercent={feedVideoStartPercent}
                  feedVideoStartMinDuration={feedVideoStartMinDuration}
                  fullscreen={verticalFullscreen}
                  viewerHeight={verticalViewerHeight}
                  selected={selectedIds.has(scene.id)}
                  selecting={selecting}
                  onSelect={() => toggle(scene.id)}
                  onNavigate={navigateToScene}
                />
              )}
            />
          </div>
        </>
      )}
      {displayMode === "feed" && (
        <div className="mx-auto w-full max-w-[64rem] px-3 sm:px-4">
          <VirtualizedInfiniteList
            items={items}
            getItemKey={(scene) => scene.id}
            estimateSize={760}
            overscan={2}
            adjustScrollOnItemSizeChange={!isMobileViewer}
            hasNextPage={Boolean(infiniteScenesQuery.hasNextPage)}
            isFetchingNextPage={infiniteScenesQuery.isFetchingNextPage}
            loadMore={loadMoreScenes}
            onActiveIndexChange={defaultFeedVideoSound ? (idx) => setFeedAudioSceneId(idx == null ? null : items[idx]?.id ?? null) : undefined}
            className={isMobileViewer ? "[overflow-anchor:none]" : undefined}
            itemClassName="pb-5 [touch-action:pan-y]"
            renderItem={({ item: scene }) => (
              <SceneFeedCard
                scene={scene}
                useVideo={true}
                engagement={engagementById.get(scene.id)}
                feedVideoSource={feedVideoSource}
                feedVideoStartPercent={feedVideoStartPercent}
                feedVideoStartMinDuration={feedVideoStartMinDuration}
                soundEnabled={feedAudioSceneId === scene.id}
                onToggleSound={() => setFeedAudioSceneId((current) => current === scene.id ? null : scene.id)}
                onNavigate={onNavigate}
                canEngage={canEngageScene}
                selected={selectedIds.has(scene.id)}
                selecting={selecting}
                onSelect={() => toggle(scene.id)}
              />
            )}
          />
        </div>
      )}
      {displayMode === "grid" && (
        infinitePageSize ? (
          <VirtualizedEntityGrid
            items={items}
            getItemKey={(s) => s.id}
            minCardWidth="var(--card-min-width, 200px)"
            gap={12}
            estimateRowHeight={320}
            overscan={3}
            infinitePageSize={infinitePageSize}
            hasNextPage={infiniteScenesQuery.hasNextPage}
            isFetchingNextPage={infiniteScenesQuery.isFetchingNextPage}
            loadMore={loadMoreScenes}
            renderItem={(scene) => (
              <SceneCard
                scene={scene}
                engagement={engagementById.get(scene.id)}
                onClick={() => selecting ? toggle(scene.id) : navigateToScene(scene.id)}
                onNavigate={onNavigate}
                selected={selectedIds.has(scene.id)}
                onSelect={() => toggle(scene.id)}
                selecting={selecting}
                onQuickView={() => setQuickViewId(scene.id)}
              />
            )}
          />
        ) : (
          <EntityCardGrid minCardWidth="var(--card-min-width, 200px)">
            {listEntries.map((entry) => entry.kind === "compilation" && entry.group ? (
              <CompilationGroupCard key={`compilation-${entry.group.id}`} group={entry.group} onNavigate={onNavigate} />
            ) : entry.scene ? (
              <SceneCard
                key={`scene-${entry.scene.id}`}
                scene={entry.scene}
                engagement={engagementById.get(entry.scene.id)}
                onClick={() => selecting ? toggle(entry.scene!.id) : navigateToScene(entry.scene!.id)}
                onNavigate={onNavigate}
                selected={selectedIds.has(entry.scene.id)}
                onSelect={() => toggle(entry.scene!.id)}
                selecting={selecting}
                onQuickView={() => setQuickViewId(entry.scene!.id)}
              />
            ) : null)}
          </EntityCardGrid>
        )
      )}
      {displayMode === "list" && (
        <SceneListTable entries={listEntries} engagementById={engagementById} onNavigate={onNavigate} selectedIds={selectedIds} onToggle={toggle} selecting={selecting} />
      )}
      {displayMode === "wall" && (
        <VirtualizedWallColumns
          columns={wallColumns}
          getItemKey={(scene) => scene.id}
          infinitePageSize={infinitePageSize}
          hasNextPage={infiniteScenesQuery.hasNextPage}
          isFetchingNextPage={infiniteScenesQuery.isFetchingNextPage}
          loadMore={loadMoreScenes}
          estimateItemHeight={260}
          gap={4}
          className="flex gap-1 px-2"
          columnClassName="flex-1 flex flex-col gap-1 min-w-0"
          renderItem={(scene) => (
                <SceneWallCard
                  scene={scene}
                  onClick={() => selecting ? toggle(scene.id) : navigateToScene(scene.id)}
                  selected={selectedIds.has(scene.id)}
                  selecting={selecting}
                  onSelect={() => toggle(scene.id)}
                />
          )}
        />
      )}
      {displayMode === "tagger" && (
        <SceneTagger scenes={items} onNavigate={navigateToScene} selectedIds={selectedIds} selecting={selecting} onSelect={toggle} />
      )}
      {listEntries.length === 0 && !loading && (
        <div className="text-center py-20">
          <Film className="w-16 h-16 mx-auto mb-4 text-muted opacity-50" />
          <p className="text-secondary text-lg">No scenes found</p>
          <p className="text-muted text-sm mt-1">Try scanning your library to discover content</p>
        </div>
      )}
    </ListPage>

    {/* Bulk Edit Dialog */}
    <BulkEditDialog
      open={showBulkEdit}
      onClose={() => setShowBulkEdit(false)}
      title="Edit Scenes"
      selectedCount={selectedIds.size}
      fields={SCENE_BULK_FIELDS}
      onApply={(values) => bulkEditMut.mutate(values)}
      isPending={bulkEditMut.isPending}
    />
    <Suspense fallback={null}>
      {showMerge ? (
        <MergeDialog
          open={showMerge}
          onClose={() => { setShowMerge(false); selectNone(); }}
          entityType="scene"
          items={items.filter((s) => selectedIds.has(s.id)).map((s) => ({ id: s.id, name: s.title || s.files[0]?.basename || `Scene ${s.id}` }))}
          onMerge={scenes.merge}
          queryKey="scenes"
        />
      ) : null}
      {showIdentify ? (
        <IdentifyDialog
          open={showIdentify}
          onClose={() => { setShowIdentify(false); selectNone(); }}
          sceneIds={[...selectedIds]}
        />
      ) : null}
      {quickViewId !== null ? (
        <QuickViewDialog type="scene" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      ) : null}
    </Suspense>
    </>
  );
}

function SceneCreateModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: number) => void }) {
  const qc = useQueryClient();
  const [title, setTitle] = useState("");
  const [code, setCode] = useState("");
  const [date, setDate] = useState("");
  const [details, setDetails] = useState("");
  const [director, setDirector] = useState("");
  const [organized, setOrganized] = useState(false);
  const [isVr, setIsVr] = useState(false);
  const [urls, setUrls] = useState<string[]>([""]);
  const [studioId, setStudioId] = useState<number | undefined>(undefined);
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({});
  const [createAnother, setCreateAnother] = useState(false);
  const [sourceMode, setSourceMode] = useState<CreateSourceMode>("metadata");
  const [filePath, setFilePath] = useState("");
  const [url, setUrl] = useState("");
  const { urlDownloadMode, setUrlDownloadMode, scrapeMetadata, setScrapeMetadata } = useFileBackedCreatePreferences("Scene");
  const [noDownloaderFound, setNoDownloaderFound] = useState(false);

  const [tagSearch, setTagSearch] = useState("");
  const [selectedTags, setSelectedTags] = useState<{ id: number; name: string }[]>([]);
  const [performerSearch, setPerformerSearch] = useState("");
  const [selectedPerformers, setSelectedPerformers] = useState<{ id: number; name: string }[]>([]);
  const [gallerySearch, setGallerySearch] = useState("");
  const [selectedGalleries, setSelectedGalleries] = useState<{ id: number; title: string }[]>([]);

  const { data: tagResults } = useQuery({
    queryKey: ["tags-search", tagSearch],
    queryFn: () => tags.find({ q: tagSearch, perPage: 20, sort: "name", direction: "asc" }),
    enabled: tagSearch.length > 0,
  });

  const { data: performerResults } = useQuery({
    queryKey: ["performers-search", performerSearch],
    queryFn: () => performers.find({ q: performerSearch, perPage: 20, sort: "name", direction: "asc" }),
    enabled: performerSearch.length > 0,
  });

  const { data: galleryResults } = useQuery({
    queryKey: ["galleries-search", gallerySearch],
    queryFn: () => galleries.find({ q: gallerySearch, perPage: 20, sort: "title", direction: "asc" }),
    enabled: gallerySearch.length > 0,
  });

  const resetForm = () => {
    setTitle("");
    setCode("");
    setDate("");
    setDetails("");
    setDirector("");
    setOrganized(false);
    setIsVr(false);
    setUrls([""]);
    setStudioId(undefined);
    setCustomFields({});
    setSourceMode("metadata");
    setFilePath("");
    setUrl("");
    setNoDownloaderFound(false);
    setSelectedTags([]);
    setSelectedPerformers([]);
    setSelectedGalleries([]);
    setTagSearch("");
    setPerformerSearch("");
    setGallerySearch("");
  };

  const createMut = useMutation({
    mutationFn: (data: SceneCreate) => scenes.create(data),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["scenes"] });
      resetForm();
      if (createAnother) return;
      onClose();
      if (created?.id) onCreated(created.id);
    },
  });

  const createFromFileMut = useMutation({
    mutationFn: async ({ path, data }: { path: string; data: SceneCreate }) => {
      const created = await scenes.createFromFile({ filePath: path });
      return created?.id ? scenes.update(created.id, data) : created;
    },
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["scenes"] });
      resetForm();
      if (createAnother) return;
      onClose();
      if (created?.id) onCreated(created.id);
    },
  });

  const createFromUrlMut = useMutation({
    mutationFn: ({ requestedUrl, data, downloadMode, scrapeMetadata }: { requestedUrl: string; data: SceneCreate; downloadMode: UrlDownloadMode; scrapeMetadata: boolean }) =>
      createFromUrlWithOptionalDownload({ requestedUrl, data, entity: "Scene", downloadMode, scrapeMetadata, create: scenes.create }),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["scenes"] });
      qc.invalidateQueries({ queryKey: ["jobs"] });
      resetForm();
      if (createAnother) return;
      onClose();
      if (created?.id) onCreated(created.id);
    },
    onError: (err) => {
      if (err instanceof NoDownloaderFoundError) setNoDownloaderFound(true);
    },
  });

  const buildPayload = (extraUrls: string[] = []): SceneCreate => ({
    title: title || undefined,
    code: code || undefined,
    date: date || undefined,
    details: details || undefined,
    director: director || undefined,
    organized,
    isVr,
    studioId,
    urls: mergeUrlLists(urls, extraUrls),
    tagIds: selectedTags.map((t) => t.id),
    performerIds: selectedPerformers.map((p) => p.id),
    galleryIds: selectedGalleries.map((g) => g.id),
    customFields: Object.keys(customFields).length > 0 ? customFields : undefined,
  });

  const handleSourceModeChange = (mode: CreateSourceMode) => {
    setSourceMode(mode);
    setNoDownloaderFound(false);
  };

  const handleUrlChange = (value: string) => {
    setUrl(value);
    setNoDownloaderFound(false);
  };

  const handleCreateWithoutDownload = () => {
    const requestedUrl = url.trim();
    if (requestedUrl) createMut.mutate(buildPayload([requestedUrl]));
  };

  const handleSave = () => {
    if (sourceMode === "file") {
      const trimmedPath = filePath.trim();
      if (trimmedPath) createFromFileMut.mutate({ path: trimmedPath, data: buildPayload() });
      return;
    }

    if (sourceMode === "url") {
      const requestedUrl = url.trim();
      if (requestedUrl) createFromUrlMut.mutate({ requestedUrl, data: buildPayload(), downloadMode: urlDownloadMode, scrapeMetadata });
      return;
    }

    createMut.mutate(buildPayload());
  };

  const pending = createMut.isPending || createFromFileMut.isPending || createFromUrlMut.isPending;
  const error = (createMut.error ?? createFromFileMut.error ?? createFromUrlMut.error) as Error | null;

  return (
    <EditModal title="Create Scene" open={open} onClose={onClose}>
      <FileBackedCreateSource
        mode={sourceMode}
        onModeChange={handleSourceModeChange}
        filePath={filePath}
        onFilePathChange={setFilePath}
        url={url}
        onUrlChange={handleUrlChange}
        urlDownloadMode={urlDownloadMode}
        onUrlDownloadModeChange={setUrlDownloadMode}
        scrapeMetadata={scrapeMetadata}
        onScrapeMetadataChange={setScrapeMetadata}
        noDownloaderFound={noDownloaderFound}
        onCreateWithoutDownload={handleCreateWithoutDownload}
        onDismissNoDownloader={() => setNoDownloaderFound(false)}
        modes={["metadata", "file", "url"]}
        filePlaceholder="C:\\Media\\scene.mp4"
        urlPlaceholder="https://example.com/scene"
      />

      <>
      <div className="grid grid-cols-2 gap-4">
        <Field label="Title">
          <TextInput value={title} onChange={setTitle} placeholder="Scene title" />
        </Field>
        <Field label="Date">
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="w-full bg-card border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          />
        </Field>
      </div>

      <div className="grid grid-cols-2 gap-4">
        <Field label="Studio Code">
          <TextInput value={code} onChange={setCode} placeholder="Studio code" />
        </Field>
        <Field label="Director">
          <TextInput value={director} onChange={setDirector} placeholder="Director" />
        </Field>
      </div>

      <Field label="Details">
        <TextArea value={details} onChange={setDetails} placeholder="Scene description" rows={3} />
      </Field>

      <Field label="Studio">
        <StudioSelector value={studioId} onChange={setStudioId} />
      </Field>

      <Field label="URLs">
        <StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" />
      </Field>

      <div className="mb-2 flex flex-wrap items-center gap-4 text-sm">
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={organized}
            onChange={(e) => setOrganized(e.target.checked)}
            className="rounded bg-card border-border"
          />
          Organized
        </label>
        <label className="flex items-center gap-2">
          <input
            type="checkbox"
            checked={isVr}
            onChange={(e) => setIsVr(e.target.checked)}
            className="rounded bg-card border-border"
          />
          VR
        </label>
      </div>

      <Field label="Custom Fields">
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="scene" />
      </Field>

      <Field label="Tags">
        <div className="flex flex-wrap gap-1.5 mb-2">
          {selectedTags.map((t) => (
            <span key={t.id} className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-accent/20 text-accent">
              {t.name}
              <button onClick={() => setSelectedTags(selectedTags.filter((x) => x.id !== t.id))} className="hover:text-white">×</button>
            </span>
          ))}
        </div>
        <input
          type="text"
          value={tagSearch}
          onChange={(e) => setTagSearch(e.target.value)}
          placeholder="Search tags..."
          className="w-full bg-card border border-border rounded px-3 py-1.5 text-sm text-foreground focus:outline-none focus:border-accent mb-1"
        />
        {tagSearch && (tagResults?.items?.length ?? 0) > 0 && (
          <div className="max-h-32 overflow-y-auto bg-card rounded border border-border">
            {(tagResults?.items ?? []).filter((t) => !selectedTags.some((x) => x.id === t.id)).slice(0, 10).map((t) => (
              <button
                key={t.id}
                onClick={() => { setSelectedTags([...selectedTags, { id: t.id, name: t.name }]); setTagSearch(""); }}
                className="block w-full text-left px-3 py-1.5 text-sm text-secondary hover:bg-card-hover"
              >
                {t.name}
              </button>
            ))}
          </div>
        )}
      </Field>

      <Field label="Performers">
        <div className="flex flex-wrap gap-1.5 mb-2">
          {selectedPerformers.map((p) => (
            <span key={p.id} className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-accent/10 text-accent-hover">
              {p.name}
              <button onClick={() => setSelectedPerformers(selectedPerformers.filter((x) => x.id !== p.id))} className="hover:text-white">×</button>
            </span>
          ))}
        </div>
        <input
          type="text"
          value={performerSearch}
          onChange={(e) => setPerformerSearch(e.target.value)}
          placeholder="Search performers..."
          className="w-full bg-card border border-border rounded px-3 py-1.5 text-sm text-foreground focus:outline-none focus:border-accent mb-1"
        />
        {performerSearch && (performerResults?.items?.length ?? 0) > 0 && (
          <div className="max-h-32 overflow-y-auto bg-card rounded border border-border">
            {(performerResults?.items ?? []).filter((p) => !selectedPerformers.some((x) => x.id === p.id)).slice(0, 10).map((p) => (
              <button
                key={p.id}
                onClick={() => { setSelectedPerformers([...selectedPerformers, { id: p.id, name: p.name }]); setPerformerSearch(""); }}
                className="block w-full text-left px-3 py-1.5 text-sm text-secondary hover:bg-card-hover"
              >
                {p.name}
              </button>
            ))}
          </div>
        )}
      </Field>

      <Field label="Galleries">
        <div className="flex flex-wrap gap-1.5 mb-2">
          {selectedGalleries.map((g) => (
            <span key={g.id} className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-emerald-900 text-emerald-300">
              {g.title}
              <button onClick={() => setSelectedGalleries(selectedGalleries.filter((x) => x.id !== g.id))} className="hover:text-white">×</button>
            </span>
          ))}
        </div>
        <input
          type="text"
          value={gallerySearch}
          onChange={(e) => setGallerySearch(e.target.value)}
          placeholder="Search galleries..."
          className="w-full bg-card border border-border rounded px-3 py-1.5 text-sm text-foreground focus:outline-none focus:border-accent mb-1"
        />
        {gallerySearch && (galleryResults?.items?.length ?? 0) > 0 && (
          <div className="max-h-32 overflow-y-auto bg-card rounded border border-border">
            {(galleryResults?.items ?? []).filter((g) => !selectedGalleries.some((x) => x.id === g.id)).slice(0, 10).map((g) => (
              <button
                key={g.id}
                onClick={() => { setSelectedGalleries([...selectedGalleries, { id: g.id, title: g.title || "Untitled" }]); setGallerySearch(""); }}
                className="block w-full text-left px-3 py-1.5 text-sm text-secondary hover:bg-card-hover"
              >
                {g.title || "Untitled"}
              </button>
            ))}
          </div>
        )}
      </Field>

      <CreateModalActions
        loading={pending}
        onCancel={onClose}
        onSave={handleSave}
        createAnother={createAnother}
        onCreateAnotherChange={setCreateAnother}
      />
      </>
    </EditModal>
  );
}

function CompilationGroupCard({ group, onNavigate }: { group: Group; onNavigate: (r: any) => void }) {
  return (
    <div className="entity-card relative flex h-full cursor-pointer flex-col overflow-hidden rounded border border-border bg-card transition-colors hover:border-accent/60 group">
      <RouteCardLinkOverlay route={{ page: "compilation", id: group.id }} onClick={() => onNavigate({ page: "compilation", id: group.id })} label={`Play compilation ${group.name}`} selectionSafeZone />
      <div className="relative flex aspect-video items-center justify-center overflow-hidden bg-surface">
        <BookmarkButton
          hostType="group"
          hostId={group.id}
          compact
          deferUntilHover
          className="absolute left-1 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
        />
        {group.frontImagePath ? (
          <img src={group.frontImagePath} alt={group.name} className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <Layers className="h-10 w-10 text-muted opacity-40" />
        )}
        <div className="absolute bottom-1 left-1 rounded bg-black/70 px-1.5 py-0.5 text-xs font-medium text-white">
          Compilation
        </div>
        <div className="absolute bottom-1 right-1 rounded bg-black/70 px-1.5 py-0.5 text-xs text-white">
          {group.sceneCount} scenes
        </div>
      </div>
      <div className="border-t border-border/50 px-2.5 py-2">
        <p className="line-clamp-2 text-sm font-semibold text-foreground group-hover:text-accent">{group.name}</p>
        {group.studioName ? <p className="mt-1 truncate text-xs text-muted">{group.studioName}</p> : null}
      </div>
    </div>
  );
}

function getSceneDisplayDuration(scene: Scene) {
  if (typeof scene.clipStartSec === "number" && typeof scene.clipEndSec === "number") {
    return Math.max(0, scene.clipEndSec - scene.clipStartSec);
  }

  return scene.files[0]?.duration ?? 0;
}

function getSceneFeedMedia(scene: Scene, feedVideoSource: string) {
  const coverUrl = entityImages.sceneCoverUrl(scene.id, scene.updatedAt, 1280);

  if (feedVideoSource === "video") {
    return {
      coverUrl,
      videoSrc: scenes.streamUrl(scene.id),
      videoStatusSrc: undefined,
    };
  }

  return {
    coverUrl,
    videoSrc: scenes.previewUrl(scene.id),
    videoStatusSrc: scenes.previewStatusUrl(scene.id),
  };
}

function getSceneFeedVideoStartTime(scene: Scene, feedVideoSource: string, startPercent: number, minDuration: number) {
  if (feedVideoSource !== "video" || startPercent <= 0) {
    return 0;
  }

  const duration = getSceneDisplayDuration(scene);
  if (duration <= Math.max(0, minDuration)) {
    return 0;
  }

  return duration * (Math.min(95, Math.max(0, startPercent)) / 100);
}

/* ── Scene List Table ── */

function SceneListTable({ entries, engagementById, onNavigate, selectedIds, onToggle, selecting }: { entries: SceneListEntry[]; engagementById: ReadonlyMap<number, EntityEngagement>; onNavigate: (r: any) => void; selectedIds?: Set<number>; onToggle?: (id: number) => void; selecting?: boolean }) {
  return (
    <div className="overflow-x-auto px-2">
      <table className="w-full text-xs text-foreground">
        <thead>
          <tr className="border-b border-border text-muted">
            {selectedIds && <th className="w-8 py-2 px-2"></th>}
            <th className="text-left py-2 px-2 font-medium">Title</th>
            <th className="text-left py-2 px-2 font-medium">Date</th>
            <th className="text-left py-2 px-2 font-medium">Rating</th>
            <th className="text-left py-2 px-2 font-medium">Duration</th>
            <th className="text-left py-2 px-2 font-medium">Size</th>
            <th className="text-left py-2 px-2 font-medium">Resolution</th>
            <th className="text-right py-2 px-2 font-medium">Plays</th>
          </tr>
        </thead>
        <tbody>
          {entries.map((entry) => {
            if (entry.kind === "compilation" && entry.group) {
              const group = entry.group;
              return (
                <tr
                  key={`compilation-${group.id}`}
                  onClick={() => onNavigate({ page: "compilation", id: group.id })}
                  className="border-b border-border hover:bg-card cursor-pointer"
                >
                  {selectedIds && <td className="py-1.5 px-2 text-muted"><Layers className="h-3.5 w-3.5" /></td>}
                  <td className="py-1.5 px-2">
                    <span className="text-foreground hover:text-accent">{group.name}</span>
                    {group.studioName && <span className="text-muted ml-2">— {group.studioName}</span>}
                  </td>
                  <td className="py-1.5 px-2 text-muted">{group.date || ""}</td>
                  <td className="py-1.5 px-2 text-muted">Compilation</td>
                  <td className="py-1.5 px-2 text-muted"></td>
                  <td className="py-1.5 px-2 text-muted"></td>
                  <td className="py-1.5 px-2 text-muted">{group.sceneCount} scenes</td>
                  <td className="py-1.5 px-2 text-right text-muted"></td>
                </tr>
              );
            }
            if (!entry.scene) return null;
            const scene = entry.scene;
            const file = scene.files[0];
            const duration = getSceneDisplayDuration(scene);
            return (
              <tr
                key={`scene-${scene.id}`}
                onClick={() => selecting ? onToggle?.(scene.id) : onNavigate({ page: "scene", id: scene.id })}
                className={`border-b border-border hover:bg-card cursor-pointer ${selectedIds?.has(scene.id) ? "bg-accent/10" : ""}`}
              >
                {selectedIds && (
                  <td className="py-1.5 px-2">
                    <input
                      type="checkbox"
                      checked={selectedIds.has(scene.id)}
                      onChange={() => onToggle?.(scene.id)}
                      onClick={(e) => e.stopPropagation()}
                      className="w-3.5 h-3.5 rounded border-border cursor-pointer accent-accent"
                    />
                  </td>
                )}
                <td className="py-1.5 px-2">
                  <span className="text-foreground hover:text-accent">
                    {scene.title || file?.basename || "Untitled"}
                  </span>
                  {scene.studioName && (
                    <span className="text-muted ml-2">— {scene.studioName}</span>
                  )}
                </td>
                <td className="py-1.5 px-2 text-muted">{scene.date || ""}</td>
                <td className="py-1.5 px-2"><RatingBadge rating={engagementById.get(scene.id)?.rating} /></td>
                <td className="py-1.5 px-2 text-muted">{duration > 0 ? formatDuration(duration) : ""}</td>
                <td className="py-1.5 px-2 text-muted">{file ? formatFileSize(file.size) : ""}</td>
                <td className="py-1.5 px-2 text-muted">{file ? getResolutionLabel(file.width, file.height) : ""}</td>
                <td className="py-1.5 px-2 text-right text-muted">{engagementById.get(scene.id)?.playCount || ""}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

/* ── Scene Wall Card ── */

function SceneWallCard({ scene, onClick, selected, selecting, onSelect }: { scene: Scene; onClick: () => void; selected?: boolean; selecting?: boolean; onSelect?: () => void }) {
  const file = scene.files[0];
  const coverUrl = entityImages.sceneCoverUrl(scene.id, scene.updatedAt, 1280);
  const previewUrl = scenes.previewUrl(scene.id);
  const previewStatusUrl = scenes.previewStatusUrl(scene.id);
  const aspectRatio = file?.width && file.height ? `${file.width} / ${file.height}` : "16 / 9";
  const title = scene.title || file?.basename || "Untitled";
  const duration = getSceneDisplayDuration(scene);
  const { config } = useAppConfig();
  const wallPreviewType = config?.ui.wallPreviewType ?? "video";
  const showTitle = config?.ui.wallShowTitle ?? true;

  return (
    <WallMediaCard
      title={title}
      imageSrc={coverUrl}
      videoSrc={previewUrl}
      videoStatusSrc={previewStatusUrl}
      useVideo={wallPreviewType === "video" || wallPreviewType === "webp"}
      // Browsers generally block autoplay with audio, so wall previews stay muted to animate reliably.
      muted
      aspectRatio={aspectRatio}
      imageClassName="object-cover"
      className="group"
    >
      <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
      <RouteCardLinkOverlay route={{ page: "scene", id: scene.id }} onClick={onClick} label={`Open scene ${title}`} selectionSafeZone />
      <div className={`absolute inset-0 bg-gradient-to-t from-black/60 via-transparent to-transparent transition-opacity ${showTitle ? "opacity-0 group-hover:opacity-100" : "opacity-0"}`} />
      {showTitle ? <div className="absolute bottom-0 left-0 right-0 p-1.5 opacity-0 group-hover:opacity-100 transition-opacity">
          <p className="text-xs text-white font-medium truncate">
            {title}
          </p>
      </div> : null}
      {duration > 0 && (
        <span className="absolute top-1 right-1 text-xs text-white bg-black/70 px-1 rounded">
          {formatDuration(duration)}
        </span>
      )}
    </WallMediaCard>
  );
}

function SceneFeedCard({ scene, engagement, feedVideoSource, useVideo, soundEnabled, onToggleSound, feedVideoStartPercent, feedVideoStartMinDuration, onNavigate, canEngage, selected, selecting, onSelect }: { scene: Scene; engagement?: EntityEngagement; feedVideoSource: string; useVideo: boolean; soundEnabled: boolean; onToggleSound: () => void; feedVideoStartPercent: number; feedVideoStartMinDuration: number; onNavigate: (route: any) => void; canEngage: boolean; selected?: boolean; selecting?: boolean; onSelect?: () => void }) {
  const file = scene.files[0];
  const { coverUrl, videoSrc, videoStatusSrc } = getSceneFeedMedia(scene, feedVideoSource);
  const title = scene.title || file?.basename || `Scene ${scene.id}`;
  const duration = getSceneDisplayDuration(scene);
  const aspectRatio = file?.width && file.height ? `${file.width} / ${file.height}` : "16 / 9";
  const mediaStyle = getFeedMediaStyle(file);
  const mediaIsPortrait = Boolean(mediaStyle);
  const videoStartTimeSec = getSceneFeedVideoStartTime(scene, feedVideoSource, feedVideoStartPercent, feedVideoStartMinDuration);
  const visitCount = engagement?.pageVisitCount ?? 0;
  const likeCount = engagement?.likeCount ?? 0;
  const queryClient = useQueryClient();
  const ratingMut = useMutation({
    mutationFn: (value: number | undefined) => entityEngagement.setRating("scene", scene.id, { value: value ?? null, aspect: "overall" }),
    onSuccess: (nextEngagement) => {
      queryClient.setQueryData(["engagement", "scene", scene.id], nextEngagement);
      queryClient.invalidateQueries({ queryKey: ["engagement", "scene", "batch"] });
    },
  });
  const ratingValue = ratingMut.data?.rating ?? engagement?.rating;
  const visibleTags = scene.tags.slice(0, 4);
  const hiddenTags = scene.tags.slice(4);
  const renderVideoControls = (controls: WallMediaVideoControlsState) => (
    <SceneFeedVideoControls controls={controls} soundEnabled={soundEnabled} onToggleSound={onToggleSound} />
  );

  const mediaOverlay = (
    <>
      <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
      <RouteCardLinkOverlay route={{ page: "scene", id: scene.id }} onClick={() => onNavigate({ page: "scene", id: scene.id })} label={`Open scene ${title}`} selectionSafeZone />
      {!selecting && (
        <BookmarkButton
          hostType="scene"
          hostId={scene.id}
          compact
          deferUntilHover
          className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
        />
      )}
    </>
  );

  return (
    <FeedCardFrame
      dataAttribute={{ "data-feed-scene-id": scene.id }}
      selected={selected}
      identity={scene.studioName ? <FeedIdentityBadge>{scene.studioName}</FeedIdentityBadge> : undefined}
      header={(
        <>
          {scene.date ? <span>{scene.date}</span> : null}
          {duration > 0 ? <span>{formatDuration(duration)}</span> : null}
        </>
      )}
      headerActions={(
        <>
          <FeedInlineRating value={ratingValue} onChange={(value) => ratingMut.mutate(value)} readOnly={!canEngage} pending={ratingMut.isPending} />
          <FeedActionPill>
            <ThumbsUp className={["h-3.5 w-3.5", likeCount > 0 ? "fill-accent text-accent" : ""].join(" ")} />
            {likeCount}
          </FeedActionPill>
          {engagement?.isFavorite ? (
            <FeedActionPill>
              <Heart className="h-3.5 w-3.5 fill-current text-red-400" />
              Favorite
            </FeedActionPill>
          ) : null}
          <FeedActionPill>
            <Eye className="h-3.5 w-3.5" />
            {visitCount}
          </FeedActionPill>
        </>
      )}
      media={(
        mediaIsPortrait ? (
          <FeedPortraitMediaFrame
            title={title}
            backgroundSrc={coverUrl}
            className="cursor-pointer"
            media={(
              <WallMediaCard
                title={title}
                imageSrc={coverUrl}
                videoSrc={videoSrc}
                videoStatusSrc={videoStatusSrc}
                useVideo={useVideo}
                muted={!soundEnabled}
                videoStartTimeSec={videoStartTimeSec}
                videoPlayThreshold={0.65}
                playbackTracking={{ hostType: "scene", hostId: scene.id, scopeKey: `scene-feed:${scene.id}` }}
                fillMedia
                chromeless
                imageClassName="object-contain"
                videoClassName="object-contain"
                className="h-full w-full bg-transparent"
                videoControls={renderVideoControls}
              />
            )}
          >
            {mediaOverlay}
          </FeedPortraitMediaFrame>
        ) : (
          <WallMediaCard
            title={title}
            imageSrc={coverUrl}
            videoSrc={videoSrc}
            videoStatusSrc={videoStatusSrc}
            useVideo={useVideo}
            muted={!soundEnabled}
            videoStartTimeSec={videoStartTimeSec}
            videoPlayThreshold={0.65}
            playbackTracking={{ hostType: "scene", hostId: scene.id, scopeKey: `scene-feed:${scene.id}` }}
            aspectRatio={aspectRatio}
            imageClassName="object-cover"
            style={mediaStyle}
            className="overflow-hidden rounded-2xl border border-border/70 bg-black/95 shadow-[0_18px_40px_rgba(0,0,0,0.35)] hover:border-border/70"
            videoControls={renderVideoControls}
          >
            {mediaOverlay}
          </WallMediaCard>
        )
      )}
      title={(
        <button
          type="button"
          onClick={() => onNavigate({ page: "scene", id: scene.id })}
          className="text-left text-base font-semibold text-foreground transition-colors hover:text-accent"
        >
          {title}
        </button>
      )}
      details={scene.details ? <p className="line-clamp-4">{scene.details}</p> : undefined}
      metadata={(scene.organized || scene.galleries.length > 0) ? (
        <>
          {scene.organized ? <FeedMetadataPill>Organized</FeedMetadataPill> : null}
          {scene.galleries.length > 0 ? <FeedMetadataPill>{scene.galleries.length} galleries</FeedMetadataPill> : null}
        </>
      ) : undefined}
      chips={(
        <>
          {scene.performers.slice(0, 4).map((performer) => (
            <FeedChipButton
              key={performer.id}
              onClick={() => onNavigate({ page: "performer", id: performer.id })}
            >
              {performer.name}
            </FeedChipButton>
          ))}
          {visibleTags.map((tag) => (
            <FeedChipButton
              key={tag.id}
              onClick={() => onNavigate({ page: "tag", id: tag.id })}
            >
              #{tag.name}
            </FeedChipButton>
          ))}
          {hiddenTags.length > 0 ? (
            <FeedChipOverflowMenu>
              {hiddenTags.map((tag) => (
                <FeedChipButton
                  key={tag.id}
                  onClick={() => onNavigate({ page: "tag", id: tag.id })}
                >
                  #{tag.name}
                </FeedChipButton>
              ))}
            </FeedChipOverflowMenu>
          ) : null}
        </>
      )}
    />
  );
}

function SceneFeedVideoControls({ controls, soundEnabled, onToggleSound }: { controls: WallMediaVideoControlsState; soundEnabled: boolean; onToggleSound: () => void }) {
  const seekValue = Math.round(controls.progressPercent * 10);

  return (
    <>
      <button
        type="button"
        onClick={(event) => {
          event.preventDefault();
          event.stopPropagation();
          onToggleSound();
        }}
        className="absolute bottom-14 right-3 z-20 flex h-10 w-10 items-center justify-center rounded-full bg-black/45 text-white shadow transition-colors hover:bg-black/70"
        aria-label={soundEnabled ? "Mute this feed item" : "Unmute this feed item"}
        title={soundEnabled ? "Mute this feed item" : "Unmute this feed item"}
      >
        {soundEnabled ? <Volume2 className="h-5 w-5" /> : <VolumeX className="h-5 w-5" />}
      </button>
      <div className="pointer-events-none absolute inset-x-3 bottom-3 z-20 flex items-center gap-2 rounded-full bg-black/45 px-2.5 py-1.5 text-white shadow-lg">
        <button
          type="button"
          onClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            controls.togglePlayback();
          }}
          className="pointer-events-auto flex h-7 w-7 items-center justify-center rounded-full text-white/90 transition-colors hover:bg-white/15 hover:text-white"
          aria-label={controls.isPlaying ? "Pause feed video" : "Play feed video"}
          title={controls.isPlaying ? "Pause" : "Play"}
        >
          {controls.isPlaying ? <Pause className="h-4 w-4" /> : <Play className="h-4 w-4" />}
        </button>
        <input
          type="range"
          min={0}
          max={1000}
          step={1}
          value={seekValue}
          onChange={(event) => controls.seekToPercent(Number(event.target.value) / 1000)}
          onClick={(event) => event.stopPropagation()}
          onMouseDown={(event) => event.stopPropagation()}
          className="pointer-events-auto h-1 min-w-0 flex-1 cursor-pointer accent-white"
          aria-label="Seek feed video"
          title="Seek"
        />
        <span className="min-w-[2.4rem] text-right text-[11px] tabular-nums text-white/90">
          {formatDuration(controls.currentTime || 0)}
        </span>
        <button
          type="button"
          onClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            controls.toggleFullscreen();
          }}
          className="pointer-events-auto flex h-7 w-7 items-center justify-center rounded-full text-white/90 transition-colors hover:bg-white/15 hover:text-white"
          aria-label={controls.isFullscreen ? "Exit fullscreen" : "Enter fullscreen"}
          title={controls.isFullscreen ? "Exit fullscreen" : "Enter fullscreen"}
        >
          {controls.isFullscreen ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
        </button>
      </div>
    </>
  );
}

function SceneVerticalViewerCard({ scene, feedVideoSource, useVideo, soundEnabled, onToggleSound, feedVideoStartPercent, feedVideoStartMinDuration, fullscreen, viewerHeight, onNavigate, selected, selecting, onSelect }: { scene: Scene; feedVideoSource: string; useVideo: boolean; soundEnabled: boolean; onToggleSound: () => void; feedVideoStartPercent: number; feedVideoStartMinDuration: number; fullscreen: boolean; viewerHeight: number | null; onNavigate: (sceneId: number) => void; selected?: boolean; selecting?: boolean; onSelect?: () => void }) {
  const file = scene.files[0];
  const { coverUrl, videoSrc, videoStatusSrc } = getSceneFeedMedia(scene, feedVideoSource);
  const title = scene.title || file?.basename || `Scene ${scene.id}`;
  const duration = getSceneDisplayDuration(scene);
  const videoStartTimeSec = getSceneFeedVideoStartTime(scene, feedVideoSource, feedVideoStartPercent, feedVideoStartMinDuration);
  const availableViewerHeight = viewerHeight != null ? Math.max(120, viewerHeight - 16) : null;

  return (
    <article data-vertical-scene-id={scene.id} className={`flex min-h-full snap-start snap-always items-center justify-center ${fullscreen ? "px-0 py-0" : "px-2 py-1 sm:px-4"}`}>
      <WallMediaCard
        title={title}
        imageSrc={coverUrl}
        videoSrc={videoSrc}
        videoStatusSrc={videoStatusSrc}
        useVideo={useVideo}
        muted={!soundEnabled}
        videoStartTimeSec={videoStartTimeSec}
        videoPlayThreshold={0.72}
        playbackTracking={{ hostType: "scene", hostId: scene.id, scopeKey: `scene-vertical:${scene.id}` }}
        aspectRatio="9 / 16"
        imageClassName="object-cover"
        fillMedia={fullscreen}
        style={fullscreen
          ? { width: "min(100vw, 56.25dvh)", height: "100dvh" }
          : { width: availableViewerHeight != null ? `min(calc(100vw - 1rem), ${Math.round(availableViewerHeight * 0.5625)}px)` : "min(calc(100vw - 1rem), calc((100dvh - 10rem) * 0.5625))" }}
        className={`group mx-auto overflow-hidden bg-card shadow-2xl transition-colors ${fullscreen ? "rounded-none border-0" : "rounded-[1.5rem] sm:rounded-[1.75rem]"} ${selected ? "border-accent ring-1 ring-accent/60" : "border-border hover:border-accent/50"}`}
      >
        <button
          type="button"
          onClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            onToggleSound();
          }}
          className="absolute right-2 top-2 z-20 rounded-full border border-white/15 bg-black/60 p-2 text-white shadow transition-colors hover:bg-black/80"
          aria-label={soundEnabled ? "Mute Vertical Viewer" : "Unmute Vertical Viewer"}
          title={soundEnabled ? "Mute Vertical Viewer" : "Unmute Vertical Viewer"}
        >
          {soundEnabled ? <Volume2 className="h-4 w-4" /> : <VolumeX className="h-4 w-4" />}
        </button>
        <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
        <RouteCardLinkOverlay route={{ page: "scene", id: scene.id }} onClick={() => onNavigate(scene.id)} label={`Open scene ${title}`} selectionSafeZone />
        {!selecting && (
          <BookmarkButton
            hostType="scene"
            hostId={scene.id}
            compact
            deferUntilHover
            className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
          />
        )}
        {duration > 0 ? <span className="absolute right-2 top-12 rounded bg-black/65 px-2 py-0.5 text-xs text-white">{formatDuration(duration)}</span> : null}
        <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/95 via-black/45 to-transparent p-4 pt-14 text-white">
          <div className="flex flex-wrap items-center gap-2 text-[11px] text-white/75">
            {scene.studioName ? <span>{scene.studioName}</span> : null}
            {scene.date ? <span>{scene.date}</span> : null}
            <span>{feedVideoSource === "video" ? "Full video" : "Preview clip"}</span>
          </div>
          <p className="mt-1 line-clamp-2 text-base font-semibold leading-tight sm:text-lg">{title}</p>
          <div className="mt-2 flex flex-wrap gap-1.5 text-xs text-white/85">
            {scene.performers.slice(0, 3).map((performer) => <span key={performer.id}>@{performer.name}</span>)}
            {scene.tags.slice(0, 3).map((tag) => <span key={tag.id}>#{tag.name}</span>)}
          </div>
        </div>
      </WallMediaCard>
    </article>
  );
}
