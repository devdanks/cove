import { useQueries, useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { faces, scenes, segmentDisplayProfiles, tagApplications, entityImages, metadata, fileOps, galleries } from "../api/client";
import { formatDuration, formatFileSize, formatDate, TagBadge, getResolutionLabel, CustomFieldsDisplay, CustomFieldsEditor, FieldProvenanceHover, resolveTagProvenance } from "../components/shared";
import { 
  Pencil, Plus, Trash2, Search, Eye, EyeOff, ArrowLeft, ThumbsUp,
  Check, ChevronLeft, ChevronRight, ChevronDown, MoreVertical,
  Gauge, Clapperboard, FolderOpen, Layers, Clock, List,
  RefreshCw, Camera, Image, Merge, ExternalLink, Download, X, Sparkles, Volume2,
} from "lucide-react";
import { useState, useRef, useEffect, useCallback, Fragment, useMemo, lazy, Suspense } from "react";
import { ConfirmDialog } from "../components/ConfirmDialog";
import type { Detection, Face, PerformerSummary, ResolvedSpan, Scene, SceneUpdate, Segment, TagApplication, TagProvenance } from "../api/types";
import { ExtensionSlot } from "../router/RouteRegistry";
import { AspectRatingsPanel } from "../components/AspectRatingsPanel";
import { InteractiveRating } from "../components/Rating";
import { ResolvedSpansPanel } from "../components/ResolvedSpansPanel";
import { useSceneQueue, type SceneQueueItem } from "../state/SceneQueueContext";
import { useAppConfig } from "../state/AppConfigContext";
import { useExtensions } from "../extensions/ExtensionLoader";
import { createRouteLinkProps } from "../components/cardNavigation";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { ExtensionEntityActions } from "../components/ExtensionEntityActions";
import { ExtensionErrorBoundary } from "../components/ExtensionErrorBoundary";
import { FloatingActionMenu } from "../components/FloatingActionMenu";
import { RemoteIdsEditor, normalizeRemoteIds, type RemoteIdValue } from "../components/RemoteIdsEditor";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity, filterItemsByPermission, hasAnyPermission } from "../auth/visibility";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { VideoPlayer } from "../components/VideoPlayer";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { MediaDetailLayout } from "../components/MediaDetailLayout/MediaDetailLayout";
import { CoverImageDialog } from "../components/CoverImageDialog";
import { PerformerTile, EntityRefBadge } from "../components/EntityCards";
import { trackInteraction } from "../utils/interactionTracking";
import { getEditableTagIds, getLockedTagIds, mergeTagIds } from "../utils/tags";
import { SceneVisualSimilarityPanel, useSceneVisualSimilarityAvailable } from "../components/VisualSimilarityPanel";
import { SceneAudioSimilarityPanel, useSceneAudioSimilarityAvailable } from "../components/AudioSimilarityPanel";
import { EntityReferenceMultiSelector, EntityReferenceSelector, EntityReferenceValue } from "../components/EntityReferenceSelector";
import { useDocumentTitle } from "../hooks/useDocumentTitle";

const GenerateDialog = lazy(() => import("../components/GenerateDialog").then((module) => ({ default: module.GenerateDialog })));
const DetailMergeDialog = lazy(() => import("../components/DetailMergeDialog").then((module) => ({ default: module.DetailMergeDialog })));
const IdentifyDialog = lazy(() => import("../components/IdentifyDialog").then((module) => ({ default: module.IdentifyDialog })));
const SceneDownloadDialog = lazy(() => import("../components/SceneDownloadDialog").then((module) => ({ default: module.SceneDownloadDialog })));
const SceneMetadataTaggerDialog = lazy(() => import("../components/MetadataTaggerDialog").then((module) => ({ default: module.SceneMetadataTaggerDialog })));

interface Props {
  id: number;
  initialSeekTo?: number;
  onNavigate: (r: any) => void;
}

// localStorage-backed boolean flag with safe SSR fallback.
function usePersistedFlag(key: string, defaultValue: boolean): [boolean, (next: boolean | ((prev: boolean) => boolean)) => void] {
  const [value, setValue] = useState<boolean>(() => {
    if (typeof window === "undefined") return defaultValue;
    try {
      const raw = window.localStorage.getItem(key);
      if (raw === "true") return true;
      if (raw === "false") return false;
    } catch { /* ignore */ }
    return defaultValue;
  });
  const set = useCallback((next: boolean | ((prev: boolean) => boolean)) => {
    setValue((prev) => {
      const resolved = typeof next === "function" ? (next as (p: boolean) => boolean)(prev) : next;
      try { window.localStorage.setItem(key, resolved ? "true" : "false"); } catch { /* ignore */ }
      return resolved;
    });
  }, [key]);
  return [value, set];
}

function SceneQueuePanel({
  items,
  currentId,
  autoplay,
  onNavigate,
  onClose,
  onClear,
  onToggleAutoplay,
}: {
  items: SceneQueueItem[];
  currentId: number;
  autoplay: boolean;
  onNavigate: (sceneId: number, index: number) => void;
  onClose: () => void;
  onClear: () => void;
  onToggleAutoplay: () => void;
}) {
  return (
    <div className="max-h-72 flex-shrink-0 overflow-hidden border-t border-border bg-[#161616] text-white shadow-[0_-8px_24px_rgba(0,0,0,0.28)]">
      <div className="flex items-center justify-between border-b border-white/10 px-3 py-2">
        <div>
          <div className="text-sm font-semibold">Play Selected Queue</div>
          <div className="text-xs text-white/50">{items.length} scene{items.length === 1 ? "" : "s"}</div>
        </div>
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={onToggleAutoplay}
            className={["rounded px-2 py-1 text-xs", autoplay ? "bg-accent/20 text-accent" : "text-white/60 hover:bg-white/10 hover:text-white"].join(" ")}
          >
            Auto
          </button>
          <button type="button" onClick={onClear} className="rounded px-2 py-1 text-xs text-white/60 hover:bg-white/10 hover:text-white">
            Clear
          </button>
          <button type="button" onClick={onClose} className="rounded p-1 text-white/60 hover:bg-white/10 hover:text-white" aria-label="Close queue panel">
            <X className="h-4 w-4" />
          </button>
        </div>
      </div>
      <div className="max-h-56 overflow-y-auto p-2">
        <div className="grid gap-1 sm:grid-cols-2 xl:grid-cols-3">
          {items.map((item, index) => {
            const active = item.id === currentId;
            return (
              <button
                key={`${item.id}-${index}`}
                type="button"
                onClick={() => onNavigate(item.id, index)}
                className={["flex min-w-0 items-center gap-2 rounded border p-1.5 text-left transition", active ? "border-accent bg-accent/15" : "border-white/10 bg-white/[0.03] hover:border-accent/50 hover:bg-white/[0.06]"].join(" ")}
              >
                {item.imagePath ? (
                  <img src={item.imagePath} alt="" className="h-10 w-16 shrink-0 rounded object-cover bg-black" loading="lazy" />
                ) : (
                  <div className="flex h-10 w-16 shrink-0 items-center justify-center rounded bg-black/40 text-white/35">
                    <Clapperboard className="h-4 w-4" />
                  </div>
                )}
                <div className="min-w-0 flex-1">
                  <div className="truncate text-xs font-medium text-white">{item.title || `Scene ${item.id}`}</div>
                  <div className="mt-0.5 truncate text-[10px] text-white/45">
                    {index + 1}{active ? " · Now playing" : item.subtitle ? ` · ${item.subtitle}` : ""}
                  </div>
                </div>
              </button>
            );
          })}
        </div>
      </div>
    </div>
  );
}

type TabKey = "details" | "segments" | "filters" | "file-info" | "edit" | "history" | string;

export function SceneDetailPage({ id, initialSeekTo, onNavigate }: Props) {
  const { data: scene, isLoading } = useQuery({
    queryKey: ["scene", id],
    queryFn: () => scenes.get(id),
  });
  const { hasPermission, user } = useAuth();
  const { config } = useAppConfig();
  const { queue, currentId: queueCurrentId, hasPrev, hasNext, prevId, nextId, currentPosition, queueLength, queueItems, goToIndex, clearQueue, autoplay: queueAutoplay, toggleAutoplay } = useSceneQueue();
  const { getTabsForPage, resolveComponent: resolveExtComponent, getFeature } = useExtensions();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [showGenerate, setShowGenerate] = useState(false);
  const [showOpsMenu, setShowOpsMenu] = useState(false);
  const [showQueuePanel, setShowQueuePanel] = useState(false);
  const [showMerge, setShowMerge] = useState(false);
  const [showIdentify, setShowIdentify] = useState(false);
  const [showScrapeDialog, setShowScrapeDialog] = useState(false);
  const [showDownloadDialog, setShowDownloadDialog] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>("details");
  const [selectedProfileId, setSelectedProfileId] = useState<number | undefined>(undefined);
  const queryClient = useQueryClient();
  const { backLabel, goBack } = useBackNavigation({ page: "scenes" }, onNavigate);
  const canWriteScene = canWriteEntity("scene", hasPermission);
  const canReadScene = canReadEntity("scene", hasPermission);
  const canDeleteScene = canDeleteEntity("scene", hasPermission);
  const canReadGroups = canReadEntity("group", hasPermission);
  const canReadGalleries = canReadEntity("gallery", hasPermission);
  const canReadFaces = canReadEntity("face", hasPermission);
  const canReadSegments = canReadEntity("segment", hasPermission);
  const canWriteSegments = hasPermission("segments.write");
  const canReadFiles = hasPermission("files.read");
  const canRunJobs = hasPermission("jobs.run");
  const canLibraryScan = hasPermission("library.scan");
  const canLibraryAutoTag = hasPermission("library.autotag");
  const canScrapeScene = hasAnyPermission(hasPermission, ["scenes.scrape", "scenes.write"]);
  const canEngageScene = canReadScene && (user?.kind === "user" || user?.kind === "system");
  const trackingEnabled = user?.uiPreferences?.tracking?.enabled ?? true;
  const trackPlaybackActivity = canEngageScene && trackingEnabled;
  const canGenerateScene = canRunJobs && canWriteScene;
  const canIdentifyScene = canLibraryAutoTag && canWriteScene;
  const canDownloadScene = canRunJobs && canWriteScene;
  const seekRef = useRef<((time: number) => void) | null>(null);
  const trackedPageVisitSceneIdRef = useRef<number | null>(null);
  const opsMenuRef = useRef<HTMLDivElement>(null);
  const [videoTime, setVideoTime] = useState(0);
  const [coverOpen, setCoverOpen] = useState(false);
  const [videoFilters, setVideoFilters] = useState({ brightness: 100, contrast: 100, gamma: 100, saturation: 100, hue: 0 });
  const {
    engagement: sceneEngagement,
    favorite: sceneFavorite,
    rating: sceneRating,
    setFavorite: setSceneFavorite,
    setRating: setSceneRating,
    favoritePending: sceneFavoritePending,
  } = useEntityEngagement("scene", id, {
    enabled: !!scene && canReadScene,
    fallbackFavorite: false,
    fallbackRating: undefined,
  });
  const scenePlayCount = sceneEngagement?.playCount ?? 0;
  const scenePlayDuration = sceneEngagement?.playDuration ?? 0;
  const sceneResumeTime = sceneEngagement?.resumeTime;
  const sceneLikeCount = sceneEngagement?.likeCount ?? 0;
  const sceneDerivedLikeCount = sceneEngagement?.derivedLikeCount ?? 0;
  const scenePageVisitCount = sceneEngagement?.pageVisitCount ?? 0;
  const effectiveSceneResumeTime = typeof sceneResumeTime === "number" && Number.isFinite(sceneResumeTime) && sceneResumeTime > 0
    ? sceneResumeTime
    : undefined;
  const effectiveResumeTime = initialSeekTo ?? effectiveSceneResumeTime;

  useEffect(() => {
    const sceneId = scene?.id;
    if (!sceneId || !trackPlaybackActivity) return;
    if (trackedPageVisitSceneIdRef.current === sceneId) return;
    trackedPageVisitSceneIdRef.current = sceneId;
    trackInteraction({ hostType: "scene", hostId: sceneId, kind: "pageVisit" });
    queryClient.invalidateQueries({ queryKey: ["engagement", "scene", sceneId] });
  }, [queryClient, scene?.id, trackPlaybackActivity]);

  useDocumentTitle(scene ? scene.title || scene.files?.[0]?.basename || `Scene ${id}` : null);

  // Disable background animations on video player pages for GPU performance
  // Controlled by gradient > "Pause on Scene Player" setting (default: on)
  useEffect(() => {
    try {
      const opts = JSON.parse(localStorage.getItem("cove-style-options") ?? "{}");
      if (opts.gradient?.scenepause === "off") return;
    } catch { /* default to pausing */ }
    document.body.classList.add("has-video-player");
    return () => document.body.classList.remove("has-video-player");
  }, []);

  // Close ops menu on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (opsMenuRef.current && !opsMenuRef.current.contains(e.target as Node)) {
        setShowOpsMenu(false);
      }
    };
    if (showOpsMenu) document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [showOpsMenu]);

  const videoStyle = useMemo(() => {
    const { brightness, contrast, saturation, hue } = videoFilters;
    return { filter: `brightness(${brightness}%) contrast(${contrast}%) saturate(${saturation}%) hue-rotate(${hue}deg)` };
  }, [videoFilters]);

  const deleteMut = useMutation({
    mutationFn: (deleteFile?: boolean) => scenes.delete(id, deleteFile),
    onSuccess: () => { 
      queryClient.invalidateQueries({ queryKey: ["scenes"] }); 
      goBack(); 
    },
  });

  const incrementLikeMut = useMutation({
    mutationFn: () => scenes.incrementLike(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["scene", id] });
      queryClient.invalidateQueries({ queryKey: ["engagement", "scene", id] });
    },
  });

  const updateMut = useMutation({
    mutationFn: (data: { organized?: boolean; rating?: number }) => scenes.update(id, data),
    onSuccess: (updatedScene) => {
      queryClient.setQueryData<Scene>(["scene", id], updatedScene);
    },
  });

  const invalidateSceneCover = useCallback(() => {
    queryClient.invalidateQueries({ queryKey: ["scene", id] });
    queryClient.invalidateQueries({ queryKey: ["scenes"] });
  }, [id, queryClient]);

  const setCoverFromCurrentFrameMut = useMutation({
    mutationFn: (atSeconds?: number) => scenes.setCoverFromFrame(id, atSeconds),
    onSuccess: invalidateSceneCover,
  });

  const coverActionPending = setCoverFromCurrentFrameMut.isPending;

  const handleSetCoverFromCurrentFrame = () => {
    setCoverFromCurrentFrameMut.mutate(videoTime);
  };

  const { data: segments = [], isLoading: segmentsLoading } = useQuery({
    queryKey: ["scene", id, "segments"],
    queryFn: () => scenes.segments.list(id),
    enabled: canReadSegments,
  });

  const { data: displayProfiles = [] } = useQuery({
    queryKey: ["segment-display-profiles"],
    queryFn: () => segmentDisplayProfiles.list(),
    enabled: canReadSegments,
  });

  const { data: resolvedSpansResponse, isLoading: resolvedSpansLoading } = useQuery({
    queryKey: ["scene", id, "resolved-spans", selectedProfileId],
    queryFn: () => scenes.segments.spans(id, selectedProfileId),
    enabled: canReadSegments,
  });

  const { data: detections = [], isLoading: detectionsLoading } = useQuery({
    queryKey: ["scene", id, "detections"],
    queryFn: () => scenes.detections.list(id),
    enabled: canReadSegments,
  });

  const sceneFaceIds = useMemo(() => {
    const ids = new Set<number>();
    for (const detection of detections) {
      if (detection.refId != null && detection.refKind?.toLowerCase() === "face") {
        ids.add(detection.refId);
      }
    }

    for (const segment of segments) {
      if (segment.refId != null && isFaceTimelineSegment(segment)) {
        ids.add(Number(segment.refId));
      }
    }

    return Array.from(ids);
  }, [detections, segments]);

  const sceneFaceQueries = useQueries({
    queries: sceneFaceIds.map((faceId) => ({
      queryKey: ["face", faceId],
      queryFn: () => faces.get(faceId),
      enabled: canReadFaces && canReadSegments,
    })),
  });

  const sceneFaces = useMemo(() => {
    const countsByFaceId = new Map<number, number>();
    for (const detection of detections) {
      if (detection.refId != null && detection.refKind?.toLowerCase() === "face") {
        countsByFaceId.set(detection.refId, (countsByFaceId.get(detection.refId) ?? 0) + 1);
      }
    }

    return sceneFaceQueries
      .map((query) => query.data)
      .filter((face): face is Face => face != null)
      .map((face) => ({ face, detectionCount: countsByFaceId.get(face.id) ?? 0 }))
      .sort((left, right) => right.detectionCount - left.detectionCount || left.face.id - right.face.id);
  }, [detections, sceneFaceQueries]);

  const rescanMut = useMutation({
    mutationFn: () => scenes.rescan(id),
  });

  const resolvedSpans = resolvedSpansResponse?.spans ?? [];
  const activeProfileId = selectedProfileId ?? resolvedSpansResponse?.profileId;
  const activeProfileName = displayProfiles.find((profile) => profile.id === activeProfileId)?.name ?? "Resolved";
  const hasVisualSimilarity = useSceneVisualSimilarityAvailable(id);
  const hasAudioSimilarity = useSceneAudioSimilarityAvailable(id);
  const sceneExtTabs = useMemo(() => getTabsForPage("scene"), [getTabsForPage]);

  const tabs = filterItemsByPermission([
    { key: "details", label: "Details" },
    { key: "segments", label: `Segments${segments.length ? ` (${segments.length})` : ""}` },
    ...(hasVisualSimilarity ? [{ key: "similar", label: "Similar", icon: <Sparkles className="h-4 w-4" /> }] : []),
    ...(hasAudioSimilarity ? [{ key: "audio-similar", label: "Audio Similar", icon: <Volume2 className="h-4 w-4" /> }] : []),
    { key: "filters", label: "Filters" },
    { key: "file-info", label: `File Info${scene?.files.length && scene.files.length > 1 ? ` (${scene.files.length})` : ""}` },
    { key: "history", label: "History" },
    ...sceneExtTabs.map((t) => ({ key: `ext:${t.key}` as TabKey, label: t.label, manualContexts: t.manualContexts })),
    { key: "edit", label: "Edit" },
  ], {
    segments: "segments.read",
    "file-info": "files.read",
    edit: "scenes.write",
  }, hasPermission);

  useEffect(() => {
    if (!tabs.some((tab) => tab.key === activeTab)) {
      setActiveTab("details");
    }
  }, [activeTab, tabs]);

  useEffect(() => {
    if (!queue || queueCurrentId === id) {
      return;
    }

    const nextIndex = queue.sceneIds.indexOf(id);
    if (nextIndex >= 0) {
      goToIndex(nextIndex);
    }
  }, [goToIndex, id, queue, queueCurrentId]);

  const queueSyncedToScene = queueCurrentId === id;

  const sceneKeyboardShortcuts = useMemo(() => [
    { key: "a", description: "Open details tab", handler: () => setActiveTab("details") },
    { key: "e", description: "Open edit tab", handler: () => canWriteScene && setActiveTab("edit") },
    { key: "s", description: "Open segments tab", handler: () => canReadSegments && setActiveTab("segments") },
    { key: "i", description: "Open file info tab", handler: () => canReadFiles && setActiveTab("file-info") },
    { key: "h", description: "Open history tab", handler: () => setActiveTab("history") },
    { key: "o", description: "Toggle favorite", handler: () => scene && canEngageScene && setSceneFavorite(!sceneFavorite) },
    { key: "[", description: "Open previous scene", handler: () => queueSyncedToScene && hasPrev && prevId != null && onNavigate({ page: "scene", id: prevId }) },
    { key: "]", description: "Open next scene", handler: () => queueSyncedToScene && hasNext && nextId != null && onNavigate({ page: "scene", id: nextId }) },
  ], [canEngageScene, canReadFiles, canReadSegments, canWriteScene, hasNext, hasPrev, nextId, onNavigate, prevId, queueSyncedToScene, scene, sceneFavorite, setSceneFavorite]);

  if (isLoading) {
    return (
      <div className="-mx-6 -mt-5 -mb-5 px-6 py-6">
        <DetailSkeleton />
      </div>
    );
  }

  if (!scene) return <div className="text-center text-secondary py-16">Scene not found</div>;

  const file = scene.files[0];
  const streamUrl = scenes.streamUrl(id);
  const resLabel = file ? getResolutionLabel(file.width, file.height) : null;

  const studioImageUrl = scene.studioId ? entityImages.studioImageUrl(scene.studioId) : null;
  const sceneTitle = scene.title || file?.basename || `Scene ${scene.id}`;

  const sceneHeaderImage = studioImageUrl && scene.studioId ? (
    <button
      type="button"
      onClick={() => onNavigate({ page: "studio", id: scene.studioId })}
      className="block"
      title={scene.studioName || "Studio"}
    >
      <img
        src={studioImageUrl}
        alt={scene.studioName || "Studio"}
        className="h-20 w-auto max-w-full object-contain"
        onError={(event) => { (event.target as HTMLImageElement).style.display = "none"; }}
      />
    </button>
  ) : null;

  const sceneSubtitle = (
    <div className="flex flex-wrap items-start gap-4 text-sm text-secondary">
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        {scene.date ? (
          <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="date">
            <span>
              {new Date(`${scene.date}T00:00:00`).toLocaleDateString(undefined, {
                year: "numeric",
                month: "long",
                day: "numeric",
              })}
            </span>
          </FieldProvenanceHover>
        ) : null}

        <div className="flex flex-wrap items-center gap-2">
          {scene.studioName && scene.studioId ? (
            <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="studio">
              <button
                type="button"
                onClick={() => onNavigate({ page: "studio", id: scene.studioId })}
                className="font-medium text-accent hover:underline"
              >
                {scene.studioName}
              </button>
            </FieldProvenanceHover>
          ) : null}
          {file && file.frameRate > 0 ? <span>{file.frameRate.toFixed(0)} fps</span> : null}
          {file && resLabel ? <span className="font-semibold text-accent">{resLabel}</span> : null}
          {scene.code ? <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="code"><span>Code {scene.code}</span></FieldProvenanceHover> : null}
          {scene.director ? (
            <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="director">
              <button
                type="button"
                onClick={() => onNavigate({ page: "scenes", query: scene.director })}
                className="hover:text-foreground"
              >
                Director {scene.director}
              </button>
            </FieldProvenanceHover>
          ) : null}
        </div>
      </div>
    </div>
  );

  const sceneActions = (
    <>
      {canWriteScene ? (
        <button
          type="button"
          onClick={() => { if (!updateMut.isPending) updateMut.mutate({ organized: !scene.organized }); }}
          disabled={updateMut.isPending}
          className={`inline-flex items-center justify-center rounded p-1 transition ${scene.organized ? "bg-green-600 text-white" : "bg-card text-muted hover:text-foreground"} ${updateMut.isPending ? "cursor-not-allowed opacity-60" : ""}`}
          title={scene.organized ? "Organized" : "Mark organized"}
        >
          <Check className="h-4 w-4" />
        </button>
      ) : scene.organized ? (
        <span className="inline-flex items-center justify-center rounded bg-green-600 p-1 text-white" title="Organized">
          <Check className="h-4 w-4" />
        </span>
      ) : null}

      {file ? (
        <a
          href={streamUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
          title="Open in external player"
        >
          <ExternalLink className="h-4 w-4" />
        </a>
      ) : null}

      {queueLength > 1 ? (
        <button
          type="button"
          onClick={() => setShowQueuePanel((value) => !value)}
          className={["inline-flex items-center gap-1 rounded px-1.5 py-1 text-xs transition", showQueuePanel ? "bg-accent/15 text-accent" : "text-secondary hover:bg-card hover:text-foreground"].join(" ")}
          title="Show selected queue"
          aria-pressed={showQueuePanel}
        >
          <List className="h-4 w-4" />
          <span>{currentPosition}/{queueLength}</span>
        </button>
      ) : null}

      <div className="relative" ref={opsMenuRef}>
        <button
          type="button"
          onClick={() => setShowOpsMenu(!showOpsMenu)}
          className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
          title="Operations"
        >
          <MoreVertical className="h-4 w-4" />
        </button>
        <FloatingActionMenu open={showOpsMenu} anchorRef={opsMenuRef} onClose={() => setShowOpsMenu(false)} className="min-w-[220px] py-1">
            {!file && canDownloadScene ? (
              <button onClick={() => { setShowDownloadDialog(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Download className="h-3.5 w-3.5" /> Download Media…</button>
            ) : null}
            {file && canLibraryScan ? (
              <button onClick={() => { rescanMut.mutate(); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><RefreshCw className="h-3.5 w-3.5" /> Rescan</button>
            ) : null}
            {canScrapeScene ? <button onClick={() => { setShowScrapeDialog(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Search className="h-3.5 w-3.5" /> Scrape / Metadata…</button> : null}
            {canIdentifyScene ? <button onClick={() => { setShowIdentify(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Search className="h-3.5 w-3.5" /> Identify…</button> : null}
            {canGenerateScene || canWriteScene ? <div className="my-1 border-t border-border" /> : null}
            <ExtensionEntityActions entityType="scene" entityId={scene.id} renderMode="menu" onInvoked={() => setShowOpsMenu(false)} />
            {canGenerateScene ? <button onClick={() => { setShowGenerate(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Clapperboard className="h-3.5 w-3.5" /> Generate…</button> : null}
            {canWriteScene ? <button onClick={() => { setCoverOpen(true); setShowOpsMenu(false); }} disabled={coverActionPending} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface disabled:opacity-60"><Image className="h-3.5 w-3.5" /> Set Cover…</button> : null}
            {canWriteScene ? <div className="my-1 border-t border-border" /> : null}
            {canWriteScene ? <button onClick={() => { setShowMerge(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Merge className="h-3.5 w-3.5" /> Merge…</button> : null}
            {canDeleteScene ? <div className="my-1 border-t border-border" /> : null}
            {canDeleteScene ? <button onClick={() => { setConfirmDelete(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-red-400 hover:bg-surface"><Trash2 className="h-3.5 w-3.5" /> Delete</button> : null}
        </FloatingActionMenu>
      </div>

      <ExtensionSlot slot="scene-detail-actions" context={{ scene, onNavigate }} />
    </>
  );

  const activeTabContent = activeTab === "details" ? (
    <DetailsTab scene={scene} onNavigate={onNavigate} sceneFaces={sceneFaces} />
  ) : activeTab === "segments" ? (
    <div className="space-y-4">
      <ResolvedSpansPanel
        sceneId={scene.id}
        spans={resolvedSpans}
        loading={resolvedSpansLoading}
        profiles={displayProfiles}
        currentProfileId={activeProfileId}
        onProfileChange={setSelectedProfileId}
        onSeek={(time) => seekRef.current?.(time)}
        onNavigate={onNavigate}
      />
      <SegmentsPanel
        sceneId={scene.id}
        segments={segments}
        loading={segmentsLoading}
        canEdit={canWriteSegments}
        onSeek={(time) => seekRef.current?.(time)}
        currentTime={videoTime}
      />
    </div>
  ) : activeTab === "similar" ? (
    <SceneVisualSimilarityPanel sceneId={scene.id} onNavigate={onNavigate} />
  ) : activeTab === "audio-similar" ? (
    <SceneAudioSimilarityPanel sceneId={scene.id} onNavigate={onNavigate} />
  ) : activeTab === "filters" ? (
    <VideoFiltersTab filters={videoFilters} onChange={setVideoFilters} />
  ) : activeTab === "file-info" && scene.files.length > 0 ? (
    <FileInfoTab files={scene.files} />
  ) : activeTab === "history" ? (
    <HistoryTab
      scene={scene}
      playCount={scenePlayCount}
      playDuration={scenePlayDuration}
    />
  ) : activeTab === "edit" ? (
    <SceneEditPanel scene={scene} onSaved={() => setActiveTab("details")} />
  ) : activeTab.startsWith("ext:") ? (() => {
    const extTabKey = activeTab.replace("ext:", "");
    const extTab = sceneExtTabs.find((tab) => tab.key === extTabKey);
    if (!extTab) return null;
    const Component = resolveExtComponent(extTab.componentName);
    if (!Component) return <div className="p-4 text-muted">Extension component not found: {extTab.componentName}</div>;
    return (
      <ExtensionErrorBoundary extensionId={extTab.extensionId}>
        <Component entityId={id} />
      </ExtensionErrorBoundary>
    );
  })() : null;

  const sceneMedia = (
    <div className="flex min-h-0 min-w-0 max-w-full flex-1 flex-col overflow-hidden bg-black">
      <div className="flex min-h-0 min-w-0 max-w-full flex-1 overflow-hidden bg-black">
        {file ? (
          <VideoPlayer
            streamUrl={streamUrl}
            posterUrl={scenes.screenshotUrl(id, scene.updatedAt)}
            format={file.format}
            duration={file.duration}
            resumeTime={effectiveResumeTime}
            sceneId={id}
            detections={detections}
            segments={segments}
            faces={sceneFaces.map(({ face }) => face)}
            captions={file.captions}
            videoStyle={videoStyle}
            onSeekRegister={(fn) => { seekRef.current = fn; }}
            onTimeUpdate={setVideoTime}
            autostart={config?.ui.autostartVideo}
            showAbLoop={config?.ui.showAbLoopControls}
            trackingEnabled={trackPlaybackActivity}
            onEnded={() => { if (queueAutoplay && queueSyncedToScene && hasNext && nextId != null) onNavigate({ page: "scene", id: nextId }); }}
            onPrev={queueSyncedToScene && hasPrev && prevId != null ? () => onNavigate({ page: "scene", id: prevId }) : undefined}
            onNext={queueSyncedToScene && hasNext && nextId != null ? () => onNavigate({ page: "scene", id: nextId }) : undefined}
          />
        ) : (
          <div className="flex h-48 items-center justify-center text-muted">No video file available</div>
        )}
      </div>
      {file ? (
        <SceneScrubber
          sceneId={scene.id}
          duration={file.duration}
          spans={resolvedSpans}
          rawSegments={segments}
          detections={detections}
          faces={sceneFaces.map(({ face }) => face)}
          performers={scene.performers}
          onSeek={(time) => seekRef.current?.(time)}
          currentTime={videoTime}
          profileName={activeProfileName}
        />
      ) : null}
      {showQueuePanel && queueLength > 0 ? (
        <SceneQueuePanel
          items={queueItems}
          currentId={id}
          autoplay={queueAutoplay}
          onClose={() => setShowQueuePanel(false)}
          onClear={() => { clearQueue(); setShowQueuePanel(false); }}
          onToggleAutoplay={toggleAutoplay}
          onNavigate={(sceneId, index) => {
            goToIndex(index);
            onNavigate({ page: "scene", id: sceneId });
          }}
        />
      ) : null}
    </div>
  );

  return (
    <>
      <CoverImageDialog
        open={coverOpen}
        title="Set Scene Cover"
        currentImageUrl={scenes.screenshotUrl(scene.id, scene.updatedAt)}
        onUpload={(file) => entityImages.uploadSceneCoverImage(scene.id, file)}
        onDelete={() => entityImages.deleteSceneCoverImage(scene.id)}
        onClose={() => setCoverOpen(false)}
        onSuccess={invalidateSceneCover}
        aspectRatio="16/9"
        extraActions={file ? (
          <button
            type="button"
            onClick={() => { handleSetCoverFromCurrentFrame(); setCoverOpen(false); }}
            disabled={coverActionPending}
            className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground hover:border-accent hover:text-accent disabled:opacity-60"
          >
            {coverActionPending ? <span className="h-3.5 w-3.5 animate-spin rounded-full border-b-2 border-accent" /> : <Camera className="h-3.5 w-3.5" />}
            From Current Frame
          </button>
        ) : null}
      />
      <Suspense fallback={null}>
        {showGenerate ? (
          <GenerateDialog
            open={showGenerate}
            onClose={() => setShowGenerate(false)}
            sceneIds={[id]}
            title={`Generate for "${scene.title || "Untitled"}"`}
          />
        ) : null}
        {showDownloadDialog ? (
          <SceneDownloadDialog
            open={showDownloadDialog}
            scene={scene}
            onClose={() => setShowDownloadDialog(false)}
            onNavigate={onNavigate}
          />
        ) : null}
        {showScrapeDialog ? (
          <SceneMetadataTaggerDialog
            open={showScrapeDialog}
            scene={scene}
            onClose={() => setShowScrapeDialog(false)}
            onNavigate={onNavigate}
          />
        ) : null}
        {showMerge ? (
          <DetailMergeDialog
            open={showMerge}
            onClose={() => setShowMerge(false)}
            entityType="scene"
            targetItem={{ id: scene.id, name: scene.title || file?.basename || `Scene ${scene.id}`, imagePath: scenes.screenshotUrl(scene.id, scene.updatedAt), subtitle: scene.studioName }}
            searchItems={async (term) => {
              const response = await scenes.find({ page: 1, perPage: 20, direction: "desc", q: term || undefined });
              return response.items.map((item) => ({
                id: item.id,
                name: item.title || item.files[0]?.basename || `Scene ${item.id}`,
                imagePath: scenes.screenshotUrl(item.id, item.updatedAt),
                subtitle: item.studioName,
              }));
            }}
            onMerge={(targetId, sourceIds) => scenes.merge(targetId, sourceIds)}
            invalidateQueryKeys={[["scene", id], ["scenes"]]}
          />
        ) : null}
        {showIdentify ? (
          <IdentifyDialog
            open={showIdentify}
            onClose={() => setShowIdentify(false)}
            sceneIds={[id]}
          />
        ) : null}
      </Suspense>
      <ConfirmDialog
        open={confirmDelete}
        title="Delete Scene"
        message={`Are you sure you want to delete "${scene.title || "Untitled"}"? This cannot be undone.`}
        onConfirm={(opts) => deleteMut.mutate(opts?.deleteFile)}
        onCancel={() => setConfirmDelete(false)}
        showDeleteFile
      />
      <MediaDetailLayout
        title={<FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="title">{sceneTitle}</FieldProvenanceHover>}
        headerImage={sceneHeaderImage}
        subtitle={sceneSubtitle}
        backLabel={backLabel}
        onGoBack={goBack}
        media={sceneMedia}
        mediaAspectRatio="auto"
        mediaFullBleed
        mediaSticky={false}
        tabs={tabs}
        activeTab={activeTab}
        onTabChange={(key) => setActiveTab(key as TabKey)}
        engagement={{
          primaryContent: <InteractiveRating value={sceneRating} onChange={(value) => setSceneRating(value)} readOnly={!canEngageScene} />,
          favorite: sceneFavorite,
          favoritePending: sceneFavoritePending,
          onFavoriteChange: canEngageScene ? setSceneFavorite : undefined,
          additionalMetrics: [
            {
              label: "Likes",
              value: sceneLikeCount,
              icon: <ThumbsUp className={["h-4 w-4", sceneLikeCount > 0 ? "fill-accent text-accent" : ""].join(" ")} />,
              title: "Likes",
              onClick: canEngageScene ? () => incrementLikeMut.mutate() : undefined,
              active: sceneLikeCount > 0,
            },
            {
              label: "Page Visits",
              value: scenePageVisitCount,
              icon: <Eye className="h-4 w-4" />,
              title: "Page visits",
            },
          ],
        }}
        keyboardShortcuts={sceneKeyboardShortcuts}
        actions={sceneActions}
      >
        <MediaDetailLayout.Content>
          {activeTab === "details" ? (
            <div className="mb-4">
              <AspectRatingsPanel hostType="scene" hostId={id} canRate={canEngageScene} />
            </div>
          ) : null}
          {activeTabContent}
        </MediaDetailLayout.Content>
        <ExtensionSlot slot="scene-detail-main-bottom" context={{ scene, onNavigate }} />
      </MediaDetailLayout>
    </>
  );
}

function buildSceneEditPerformerContextTagIds(scene: Scene): Record<number, number[]> {
  const result: Record<number, number[]> = {};
  for (const application of scene.contextTagApplications ?? []) {
    if (application.contextType !== "performer" || application.contextId == null) {
      continue;
    }

    result[application.contextId] = [...(result[application.contextId] ?? []), application.tag.id];
  }

  return result;
}

async function syncSceneEditPerformerContextTags(sceneId: number, existingApplications: TagApplication[], desiredByPerformer: Record<number, number[]>, selectedPerformerIds: number[]) {
  const selectedPerformers = new Set(selectedPerformerIds);
  const desiredKeys = new Set<string>();

  for (const [performerIdText, tagIds] of Object.entries(desiredByPerformer)) {
    const performerId = Number(performerIdText);
    if (!selectedPerformers.has(performerId)) {
      continue;
    }

    for (const tagId of tagIds) {
      desiredKeys.add(`${performerId}:${tagId}`);
    }
  }

  const existingContextApplications = existingApplications.filter((application) => application.contextType === "performer" && application.contextId != null);

  for (const application of existingContextApplications) {
    const key = `${application.contextId}:${application.tag.id}`;
    if (!desiredKeys.has(key)) {
      await tagApplications.delete(application.id);
    }
  }

  const existingKeys = new Set(existingContextApplications.map((application) => `${application.contextId}:${application.tag.id}`));
  for (const [performerIdText, tagIds] of Object.entries(desiredByPerformer)) {
    const performerId = Number(performerIdText);
    if (!selectedPerformers.has(performerId)) {
      continue;
    }

    for (const tagId of tagIds) {
      const key = `${performerId}:${tagId}`;
      if (existingKeys.has(key)) {
        continue;
      }

      await tagApplications.create({
        hostType: "scene",
        hostId: sceneId,
        contextType: "performer",
        contextId: performerId,
        tagId,
        sourceKey: "user",
      });
    }
  }
}

// Details Tab Content
export function DetailsTab({ scene, onNavigate, sceneFaces = [] }: { scene: Scene; onNavigate: (r: any) => void; sceneFaces?: Array<{ face: Face; detectionCount: number }> }) {
  return (
    <div className="space-y-4">
      {/* Created/Updated + Code/Director at top like original */}
      <dl className="grid gap-y-1.5 text-sm" style={{ gridTemplateColumns: "auto 1fr" }}>
        <dt className="text-muted pr-3">Created</dt>
        <dd className="text-foreground">{formatDate(scene.createdAt)}</dd>
        <dt className="text-muted pr-3">Updated</dt>
        <dd className="text-foreground">{formatDate(scene.updatedAt)}</dd>
        {scene.code && (
          <>
            <dt className="text-muted pr-3">Studio Code</dt>
            <dd className="text-foreground"><FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="code">{scene.code}</FieldProvenanceHover></dd>
          </>
        )}
        {scene.director && (
          <>
            <dt className="text-muted pr-3">Director</dt>
            <dd>
              <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="director">
                <button onClick={() => onNavigate({ page: "scenes", query: scene.director })} className="text-accent hover:underline">
                  {scene.director}
                </button>
              </FieldProvenanceHover>
            </dd>
          </>
        )}
      </dl>

      {/* Details / Description */}
      {scene.details && (
        <div>
          <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="details" block>
            <p className="text-sm text-foreground whitespace-pre-wrap">{scene.details}</p>
          </FieldProvenanceHover>
        </div>
      )}

      {/* Tags */}
      {scene.tags.length > 0 && (
        <div>
          <h6 className="text-sm text-muted mb-2">Tags</h6>
          <div className="flex flex-wrap gap-1.5">
            {scene.tags.map((tag: any) => (
              <TagBadge 
                key={tag.id} 
                name={tag.name} 
                tag={tag}
                provenance={resolveTagProvenance(tag, scene.fieldProvenance)}
                onClick={() => onNavigate({ page: "tag", id: tag.id })} 
              />
            ))}
          </div>
        </div>
      )}

      {/* Performers */}
      {scene.performers.length > 0 && (
        <div>
          <h6 className="text-sm text-muted mb-2">Performer{scene.performers.length > 1 ? "s" : ""}</h6>
          <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="performers" block>
            <div className={scene.performers.length > 1 ? "grid grid-cols-2 gap-3" : "grid max-w-[220px] gap-3"}>
              {scene.performers.map((performer: any) => {
                const contextTags = (scene.contextTagApplications ?? []).filter((application) => application.contextType === "performer" && application.contextId === performer.id);
                const ageAtScene = getAgeAtDate(scene.date, performer.birthdate);
                const footer = ageAtScene || contextTags.length > 0
                  ? <ScenePerformerTileFooter ageAtScene={ageAtScene} contextTags={contextTags} />
                  : null;

                return (
                  <PerformerTile
                    key={performer.id}
                    performer={performer}
                    onClick={() => onNavigate({ page: "performer", id: performer.id })}
                    onNavigate={onNavigate}
                  >
                    {footer}
                  </PerformerTile>
                );
              })}
            </div>
          </FieldProvenanceHover>
        </div>
      )}

      {scene.groups.length > 0 && (
        <div>
          <h6 className="mb-2 text-sm text-muted">Groups</h6>
          <div className="flex flex-wrap gap-2">
            {scene.groups.map((group) => (
              <EntityRefBadge
                key={group.id}
                route={{ page: "group", id: group.id }}
                onNavigate={onNavigate}
                imageUrl={entityImages.groupFrontImageUrl(group.id)}
                icon={<Layers className="h-5 w-5" />}
                label={group.name}
              />
            ))}
          </div>
        </div>
      )}

      {scene.galleries.length > 0 && (
        <div>
          <h6 className="mb-2 text-sm text-muted">Galleries</h6>
          <div className="flex flex-wrap gap-2">
            {scene.galleries.map((gallery) => (
              <EntityRefBadge
                key={gallery.id}
                route={{ page: "gallery", id: gallery.id }}
                onNavigate={onNavigate}
                imageUrl={galleries.coverUrl(gallery.id)}
                icon={<FolderOpen className="h-5 w-5" />}
                label={gallery.title || "Untitled"}
              />
            ))}
          </div>
        </div>
      )}

      {/* Faces */}
      {sceneFaces.length > 0 && (
        <div>
          <h6 className="mb-2 text-sm text-muted">Faces in this scene</h6>
          <div className="flex flex-wrap gap-2">
            {sceneFaces.map(({ face, detectionCount }) => {
              const title = face.label?.trim() || face.performerName || `Face #${face.id}`;
              return (
                <button
                  key={face.id}
                  type="button"
                  onClick={() => onNavigate({ page: "face", id: face.id })}
                  className="flex min-w-[180px] flex-1 items-center gap-3 rounded-xl border border-border bg-card/70 px-3 py-2 text-left transition-colors hover:border-accent sm:flex-none sm:basis-[calc(50%-0.25rem)]"
                >
                  <div className="h-14 w-14 overflow-hidden rounded-lg bg-surface/80">
                    {face.coverImageUrl ? (
                      <img src={face.coverImageUrl} alt={title} className="h-full w-full object-cover" loading="lazy" />
                    ) : (
                      <div className="flex h-full w-full items-center justify-center text-muted">
                        <Image className="h-5 w-5" />
                      </div>
                    )}
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="truncate text-sm font-medium text-foreground">{title}</div>
                    <div className="mt-1 text-xs text-secondary">
                      {detectionCount} detection{detectionCount === 1 ? "" : "s"}
                    </div>
                  </div>
                </button>
              );
            })}
          </div>
        </div>
      )}

      {/* URLs */}
      {scene.urls && scene.urls.length > 0 && (
        <div>
          <h6 className="text-sm text-muted mb-2">URLs</h6>
          <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="urls" block>
            <div className="space-y-1">
              {scene.urls.map((url: string, i: number) => (
                <a
                  key={i}
                  href={url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="text-accent hover:underline text-sm block truncate"
                >
                  {url}
                </a>
              ))}
            </div>
          </FieldProvenanceHover>
        </div>
      )}

      <CustomFieldsDisplay customFields={scene.customFields} entityType="scene" />
    </div>
  );
}

function ScenePerformerTileFooter({ ageAtScene, contextTags = [] }: { ageAtScene: number | null; contextTags?: TagApplication[] }) {
  return <div className="space-y-2 text-xs text-secondary">
    {ageAtScene ? <div className="text-center">{ageAtScene} yrs old</div> : null}
    <PerformerContextTagList contextTags={contextTags} />
  </div>;
}

function getAgeAtDate(sceneDate?: string, birthdate?: string) {
  if (!sceneDate || !birthdate) return null;

  const scene = new Date(sceneDate);
  const birth = new Date(birthdate);
  let age = scene.getFullYear() - birth.getFullYear();
  const monthDelta = scene.getMonth() - birth.getMonth();
  if (monthDelta < 0 || (monthDelta === 0 && scene.getDate() < birth.getDate())) age--;
  return age > 0 ? age : null;
}

function PerformerContextTagList({ contextTags }: { contextTags: TagApplication[] }) {
  return contextTags.length > 0 ? (
    <div className="flex flex-wrap gap-1.5">
      {contextTags.map((application) => (
        <TagBadge key={application.id} name={application.tag.name} tag={application.tag} provenance={[toTagProvenance(application)]} />
      ))}
    </div>
  ) : null;
}

function toTagProvenance(application: TagApplication) {
  return {
    sourceKey: application.sourceKey,
    sourceRunId: application.sourceRunId ?? undefined,
    modelKey: application.modelKey ?? undefined,
    confidence: application.confidence ?? undefined,
    appliedAt: application.appliedAt,
    contextType: application.contextType ?? undefined,
    contextId: application.contextId ?? undefined,
    totalDurationSec: application.totalDurationSec ?? undefined,
    hostDurationSec: application.hostDurationSec ?? undefined,
  };
}

// File Info Tab — show every underlying scene file rather than only the first one.
export function FileInfoTab({ files }: { files: Scene["files"] }) {
  const revealMutation = useMutation({ mutationFn: (fileId: number) => fileOps.reveal(fileId) });
  const canReveal = typeof window !== "undefined" && ["localhost", "127.0.0.1", "::1"].includes(window.location.hostname);

  return (
    <div className="space-y-4 text-sm">
      {files.map((file, index) => {
        const sectionLabel = file.basename || file.path.split(/[\\/]/).pop() || `File ${index + 1}`;

        return (
          <section key={file.id ?? `${file.path}-${index}`} className="rounded-xl border border-border bg-card p-4 space-y-3">
            {files.length > 1 && (
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h6 className="text-sm font-semibold text-foreground">{sectionLabel}</h6>
                  <p className="text-xs text-muted">File {index + 1} of {files.length}</p>
                </div>
                {canReveal && file.id ? (
                  <button
                    type="button"
                    onClick={() => revealMutation.mutate(file.id)}
                    className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-secondary hover:border-accent hover:text-foreground"
                  >
                    <FolderOpen className="h-3.5 w-3.5" />
                    Reveal
                  </button>
                ) : null}
              </div>
            )}

            {files.length <= 1 && canReveal && file.id ? (
              <div className="flex justify-end">
                <button
                  type="button"
                  onClick={() => revealMutation.mutate(file.id)}
                  className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-secondary hover:border-accent hover:text-foreground"
                >
                  <FolderOpen className="h-3.5 w-3.5" />
                  Reveal
                </button>
              </div>
            ) : null}

            <dl className="grid gap-y-1.5" style={{ gridTemplateColumns: "minmax(100px, auto) 1fr" }}>
              <dt className="text-muted">Path</dt>
              <dd className="text-foreground break-all font-mono text-xs">{file.path}</dd>

              <dt className="text-muted">File Size</dt>
              <dd className="text-foreground">{formatFileSize(file.size)}</dd>

              <dt className="text-muted">Duration</dt>
              <dd className="text-foreground">{formatDuration(file.duration)}</dd>

              <dt className="text-muted">Dimensions</dt>
              <dd className="text-foreground">{file.width}×{file.height}</dd>

              <dt className="text-muted">Frame Rate</dt>
              <dd className="text-foreground">{file.frameRate.toFixed(2)} fps</dd>

              <dt className="text-muted">Bitrate</dt>
              <dd className="text-foreground">{Math.round(file.bitRate / 1000)} kbps</dd>

              <dt className="text-muted">Video Codec</dt>
              <dd className="text-foreground">{file.videoCodec}</dd>

              <dt className="text-muted">Audio Codec</dt>
              <dd className="text-foreground">{file.audioCodec}</dd>
            </dl>

            {file.fingerprints && file.fingerprints.length > 0 && (
              <div>
                <h6 className="text-sm text-muted mb-1 font-medium">Fingerprints</h6>
                <dl className="grid gap-y-1" style={{ gridTemplateColumns: "auto 1fr" }}>
                  {file.fingerprints.map((fp: any) => (
                    <Fragment key={`${file.id ?? index}-${fp.type}`}>
                      <dt className="text-muted text-xs pr-3">{fp.type}</dt>
                      <dd className="text-foreground font-mono text-xs break-all">{fp.value}</dd>
                    </Fragment>
                  ))}
                </dl>
              </div>
            )}
          </section>
        );
      })}
    </div>
  );
}

// History Tab
function HistoryTab({
  scene,
  playCount,
  playDuration,
}: {
  scene: Scene;
  playCount: number;
  playDuration: number;
}) {
  const queryClient = useQueryClient();
  const { data: history } = useQuery({
    queryKey: ["scene-history", scene.id],
    queryFn: () => scenes.getHistory(scene.id),
  });
  const resetPlayMut = useMutation({
    mutationFn: () => scenes.resetPlay(scene.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["scene", scene.id] });
      queryClient.invalidateQueries({ queryKey: ["engagement", "scene", scene.id] });
      queryClient.invalidateQueries({ queryKey: ["scene-history", scene.id] });
    },
  });
  const deletePlayMut = useMutation({
    mutationFn: () => scenes.deletePlay(scene.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["scene", scene.id] });
      queryClient.invalidateQueries({ queryKey: ["engagement", "scene", scene.id] });
      queryClient.invalidateQueries({ queryKey: ["scene-history", scene.id] });
    },
  });

  const btnCls = "rounded border border-border bg-card px-2 py-0.5 text-xs text-secondary hover:text-foreground hover:bg-card-hover";
  const recentSessions = history?.sessions?.slice(0, 10) ?? [];

  return (
    <div className="space-y-6 text-sm">
      {/* Play History */}
      <section>
        <div className="flex items-center justify-between mb-2">
          <h3 className="text-sm font-semibold text-muted uppercase tracking-wide">Play History</h3>
          <div className="flex gap-1">
            <button onClick={() => deletePlayMut.mutate()} className={btnCls} title="Remove last play">-1</button>
            <button onClick={() => resetPlayMut.mutate()} className={btnCls} title="Reset play count">Reset</button>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-2 mb-2">
          <div><span className="text-muted">Play Count:</span> <span className="text-foreground">{playCount}</span></div>
          <div><span className="text-muted">Duration:</span> <span className="text-foreground">{formatDuration(playDuration)}</span></div>
        </div>
        {history?.playHistory && history.playHistory.length > 0 && (
          <div className="max-h-40 overflow-y-auto space-y-0.5 border-t border-border pt-2">
            {history.playHistory.map((date, i) => (
              <div key={i} className="text-xs text-secondary">{new Date(date).toLocaleString()}</div>
            ))}
          </div>
        )}
      </section>

      {recentSessions.length > 0 && (
        <section>
          <div className="mb-2 flex items-center justify-between">
            <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Playback Sessions</h3>
            <span className="text-xs text-secondary">{recentSessions.length}{(history?.sessions?.length ?? 0) > recentSessions.length ? ` of ${history?.sessions?.length ?? 0}` : ""} sessions</span>
          </div>
          <div className="space-y-3 border-t border-border pt-3">
            {recentSessions.map((session) => (
              <div key={session.sessionId} className="rounded-lg border border-border/70 bg-surface/35 px-3 py-2">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="text-xs font-medium uppercase tracking-wide text-foreground">
                      {session.isCompleted ? "Completed session" : "Playback session"}
                    </div>
                    <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-xs text-secondary">
                      <span>Watched {formatDuration(session.totalWatchedSec)}</span>
                      {session.lastPositionSec != null ? <span>Last position {formatDuration(session.lastPositionSec)}</span> : null}
                      <span>{session.intervals.length} intervals</span>
                    </div>
                  </div>
                  <div className="shrink-0 text-xs text-secondary">{new Date(session.startedAt).toLocaleString()}</div>
                </div>
                {session.intervals.length > 0 ? (
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {session.intervals.map((range, index) => (
                      <span key={`${session.sessionId}-${range.startSec}-${range.endSec}-${index}`} className="rounded-full border border-border bg-card px-2 py-0.5 text-[11px] text-secondary">
                        {formatDuration(range.startSec)}-{formatDuration(range.endSec)}
                      </span>
                    ))}
                  </div>
                ) : null}
              </div>
            ))}
          </div>
        </section>
      )}

      {/* Timestamps */}
      <div className="grid grid-cols-2 gap-2">
        <div><span className="text-muted">Created:</span> <span className="text-foreground">{formatDate(scene.createdAt)}</span></div>
        <div><span className="text-muted">Updated:</span> <span className="text-foreground">{formatDate(scene.updatedAt)}</span></div>
      </div>
    </div>
  );
}

// Video Filters Tab — matches standard's brightness/contrast/gamma/saturation/hue
interface VideoFilters {
  brightness: number;
  contrast: number;
  gamma: number;
  saturation: number;
  hue: number;
}

function VideoFiltersTab({ filters, onChange }: { filters: VideoFilters; onChange: (f: VideoFilters) => void }) {
  const sliders: { key: keyof VideoFilters; label: string; min: number; max: number; default: number; unit: string; formatValue?: (v: number) => string }[] = [
    { key: "brightness", label: "Brightness", min: 0, max: 200, default: 100, unit: "%" },
    { key: "contrast", label: "Contrast", min: 0, max: 200, default: 100, unit: "%" },
    { key: "gamma", label: "Gamma", min: 0, max: 200, default: 100, unit: "", formatValue: (v) => String(v - 100) },
    { key: "saturation", label: "Saturation", min: 0, max: 200, default: 100, unit: "%" },
    { key: "hue", label: "Hue", min: -180, max: 180, default: 0, unit: "°" },
  ];

  const handleReset = () => onChange({ brightness: 100, contrast: 100, gamma: 100, saturation: 100, hue: 0 });

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h5 className="text-sm font-medium text-foreground">Filters</h5>
        <button onClick={handleReset} className="text-xs text-accent hover:underline">Reset All</button>
      </div>
      {sliders.map(({ key, label, min, max, default: def, unit, formatValue }) => (
        <div key={key} className="flex items-center gap-3">
          <span className="text-sm text-muted w-24 flex-shrink-0">{label}</span>
          <input
            type="range"
            min={min}
            max={max}
            value={filters[key]}
            onChange={(e) => onChange({ ...filters, [key]: Number(e.target.value) })}
            className="flex-1 h-1 accent-accent cursor-pointer"
          />
          <button
            onClick={() => onChange({ ...filters, [key]: def })}
            className="text-xs text-secondary hover:text-foreground w-12 text-right cursor-pointer"
            title="Click to reset"
          >
            {formatValue ? formatValue(filters[key]) : `${filters[key]}${unit}`}
          </button>
        </div>
      ))}
    </div>
  );
}

type TimelineOverlayItem = {
  key: string;
  startSec: number;
  endSec: number;
  label: string;
  colorHint?: string | null;
  colorSeed?: string;
};

const SEGMENT_TIMELINE_COLORS = ["#a87a2d", "#4c6faa", "#7963a1", "#a05f7b", "#3f7f6e", "#4b7f8e", "#748a37", "#a35d4e"];
const FACE_TIMELINE_COLORS = ["#4d8569", "#4a807b", "#5a7ca5", "#7d8842", "#a07a3f", "#93658a"];

function timelineHash(value: string) {
  let hash = 0;
  for (let index = 0; index < value.length; index++) {
    hash = ((hash << 5) - hash + value.charCodeAt(index)) | 0;
  }
  return Math.abs(hash);
}

function getTimelineOverlayColor(item: TimelineOverlayItem, palette: string[]) {
  const hint = item.colorHint?.trim();
  if (hint) return hint;
  const seed = item.colorSeed || item.label || item.key;
  return palette[timelineHash(seed) % palette.length];
}

function timelineLabelFits(widthPercent: number, label: string) {
  return widthPercent >= Math.min(10, Math.max(2.4, label.length * 0.28));
}

function getSegmentTimelineLabel(
  span: Pick<ResolvedSpan, "spanKey" | "tagName" | "kind" | "sourceKey" | "segmentIds">,
  rawSegmentsById: Map<number, Pick<Segment, "id" | "title" | "kind" | "sourceKey" | "refId">>,
  performersById: Map<number, Pick<PerformerSummary, "id" | "name">>,
) {
  const tagName = span.tagName?.trim();
  if (tagName) return tagName;

  for (const segmentId of span.segmentIds ?? []) {
    const segment = rawSegmentsById.get(segmentId);
    if (!segment) continue;

    const kind = segment.kind?.trim().toLowerCase();
    if (segment.refId != null && kind === "performer") {
      const performerName = performersById.get(Number(segment.refId))?.name?.trim();
      if (performerName) return performerName;
    }

    const title = segment.title?.trim();
    if (title && title.toLowerCase() !== "performer") return title;
  }

  return span.kind?.trim() || span.sourceKey?.trim() || "Segment";
}

// Scene Scrubber / Timeline Component
function SceneScrubber({ 
  sceneId, 
  duration, 
  spans,
  rawSegments,
  detections,
  faces,
  performers,
  onSeek,
  currentTime,
  profileName,
}: { 
  sceneId: number; 
  duration: number; 
  spans: Pick<ResolvedSpan, "spanKey" | "startSec" | "endSec" | "tagName" | "kind" | "colorHint" | "sourceKey" | "lane" | "segmentIds">[];
  rawSegments: Pick<Segment, "id" | "startSec" | "endSec" | "title" | "kind" | "sourceKey" | "refId">[];
  detections: Pick<Detection, "id" | "observedAtSec" | "class" | "score" | "refKind" | "refId">[];
  faces?: Pick<Face, "id" | "label" | "performerName" | "performerId">[];
  performers?: Pick<PerformerSummary, "id" | "name">[];
  onSeek?: (time: number) => void;
  currentTime?: number;
  profileName?: string;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const scrollRef = useRef<HTMLDivElement>(null);
  const [spriteData, setSpriteData] = useState<{ entries: { start: number; end: number; x: number; y: number; w: number; h: number }[]; imageUrl: string } | null>(null);
  const [spriteError, setSpriteError] = useState(false);
  const [spriteLoadSettled, setSpriteLoadSettled] = useState(false);
  
  const spriteVttUrl = `/api/stream/scene/${sceneId}/vtt/thumbs`;
  const spriteImageUrl = `/api/stream/scene/${sceneId}/sprite`;
  const [showAllResolvedLanes, setShowAllResolvedLanes] = useState(false);
  const [showAllFaceLanes, setShowAllFaceLanes] = useState(false);
  const [overlaysCollapsed, setOverlaysCollapsed] = usePersistedFlag("cove.timeline.overlaysCollapsed", false);
  const [facesEnabled, setFacesEnabled] = usePersistedFlag("cove.timeline.facesEnabled", false);
  
  const formatTime = (s: number) => {
    const m = Math.floor(s / 60);
    const sec = Math.floor(s % 60);
    return `${m}:${sec.toString().padStart(2, "0")}`;
  };

  // Load and parse VTT sprite data
  useEffect(() => {
    let cancelled = false;

    setSpriteData(null);
    setSpriteError(false);
    setSpriteLoadSettled(false);

    fetch(spriteVttUrl)
      .then(r => { if (!r.ok) throw new Error("VTT not found"); return r.text(); })
      .then(text => {
        if (cancelled) return;
        const entries: typeof spriteData extends null ? never : NonNullable<typeof spriteData>["entries"] = [];
        const blocks = text.split(/\n\n+/);
        for (const block of blocks) {
          const lines = block.trim().split("\n");
          for (let i = 0; i < lines.length; i++) {
            const timeMatch = lines[i].match(/(\d{2}:\d{2}:\d{2}\.\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2}\.\d{3})/);
            if (timeMatch && lines[i + 1]) {
              const xywhMatch = lines[i + 1].match(/#xywh=(\d+),(\d+),(\d+),(\d+)/);
              if (xywhMatch) {
                entries.push({
                  start: parseVttTime(timeMatch[1]),
                  end: parseVttTime(timeMatch[2]),
                  x: parseInt(xywhMatch[1]),
                  y: parseInt(xywhMatch[2]),
                  w: parseInt(xywhMatch[3]),
                  h: parseInt(xywhMatch[4]),
                });
              }
            }
          }
        }
        if (entries.length > 0) {
          setSpriteData({ entries, imageUrl: spriteImageUrl });
        } else {
          setSpriteError(true);
        }
        setSpriteLoadSettled(true);
      })
      .catch(() => {
        if (cancelled) return;
        setSpriteError(true);
        setSpriteLoadSettled(true);
      });

    return () => {
      cancelled = true;
    };
  }, [sceneId, spriteVttUrl, spriteImageUrl]);

  const thumbCount = spriteData ? spriteData.entries.length : 0;
  const thumbWidth = 160;
  const thumbHeight = spriteData?.entries[0] ? Math.round(thumbWidth * (spriteData.entries[0].h / spriteData.entries[0].w)) : 0;
  const rawSegmentsById = useMemo(() => new Map(rawSegments.map((segment) => [segment.id, segment])), [rawSegments]);
  const performersById = useMemo(() => new Map((performers ?? []).map((performer) => [performer.id, performer])), [performers]);
  const nonFaceSpans = useMemo(() => spans.filter((span) => !isFaceResolvedSpan(span, rawSegmentsById)), [rawSegmentsById, spans]);
  const segmentLanes = useMemo(() => buildTimelineLanes<TimelineOverlayItem>(
    nonFaceSpans.map((span) => ({
      key: span.spanKey,
      startSec: span.startSec,
      endSec: span.endSec,
      label: getSegmentTimelineLabel(span, rawSegmentsById, performersById),
      colorHint: span.colorHint,
      colorSeed: `${span.kind ?? "span"}:${span.tagName ?? ""}:${span.sourceKey ?? ""}`,
    })),
  ), [nonFaceSpans, performersById, rawSegmentsById]);
  const faceLanes = useMemo(() => {
    if (!facesEnabled) return [] as ReturnType<typeof buildTimelineLanes<TimelineOverlayItem>>;
    const facesById = new Map<number, Pick<Face, "id" | "label" | "performerName" | "performerId">>();
    for (const face of faces ?? []) facesById.set(face.id, face);

    const items: TimelineOverlayItem[] = [];
    const segmentFaceIds = new Set<number>();

    for (const segment of rawSegments) {
      if (!isFaceTimelineSegment(segment)) continue;
      const faceId = segment.refId != null ? Number(segment.refId) : -Math.abs(segment.id);
      if (faceId > 0) segmentFaceIds.add(faceId);
      const face = facesById.get(faceId);
      const label = face?.performerName?.trim() || face?.label?.trim() || segment.title?.trim() || (faceId > 0 ? `Face #${faceId}` : "Face");
      items.push({
        key: `face-segment-${segment.id}`,
        startSec: segment.startSec,
        endSec: Math.max(segment.endSec ?? segment.startSec, segment.startSec + 0.4),
        label,
        colorSeed: String(faceId),
      });
    }

    const buckets = new Map<number, number[]>();
    for (const det of detections) {
      if (det.refId == null || det.refKind?.toLowerCase() !== "face" || segmentFaceIds.has(det.refId)) continue;
      if (det.observedAtSec == null) continue;
      const arr = buckets.get(det.refId) ?? [];
      arr.push(det.observedAtSec);
      buckets.set(det.refId, arr);
    }

    const MERGE_GAP_SEC = 2.5;
    for (const [faceId, times] of buckets.entries()) {
      times.sort((left, right) => left - right);
      let windowStart = times[0];
      let windowEnd = times[0];
      let runIndex = 0;
      const flush = () => {
        const face = facesById.get(faceId);
        const label = face?.performerName?.trim() || face?.label?.trim() || (faceId > 0 ? `Face #${faceId}` : "Face");
        items.push({
          key: `face-${faceId}-${runIndex++}`,
          startSec: windowStart,
          endSec: Math.max(windowEnd, windowStart + 0.4),
          label,
          colorSeed: String(faceId),
        });
      };
      for (let i = 1; i < times.length; i++) {
        const t = times[i];
        if (t - windowEnd <= MERGE_GAP_SEC) {
          windowEnd = t;
        } else {
          flush();
          windowStart = t;
          windowEnd = t;
        }
      }
      flush();
    }

    return buildTimelineLanes(items);
  }, [detections, rawSegments, faces, facesEnabled]);
  const visibleResolvedLanes = showAllResolvedLanes ? segmentLanes : segmentLanes.slice(0, 4);
  const visibleFaceLanes = showAllFaceLanes ? faceLanes : faceLanes.slice(0, 2);
  const hiddenResolvedLaneCount = Math.max(0, segmentLanes.length - visibleResolvedLanes.length);
  const hiddenFaceLaneCount = Math.max(0, faceLanes.length - visibleFaceLanes.length);
  const hasFaceDetections = useMemo(
    () => detections.some((det) => det.refKind?.toLowerCase() === "face" && det.refId != null)
      || rawSegments.some((segment) => isFaceTimelineSegment(segment)),
    [detections, rawSegments],
  );

  // Determine which thumbnail index is active based on current video time
  const activeIndex = useMemo(() => {
    if (currentTime == null || currentTime <= 0) return -1;
    if (spriteData) {
      for (let i = spriteData.entries.length - 1; i >= 0; i--) {
        if (currentTime >= spriteData.entries[i].start) return i;
      }
      return 0;
    }
    return -1;
  }, [currentTime, spriteData, duration, thumbCount]);

  // Auto-scroll to active thumbnail
  useEffect(() => {
    if (activeIndex >= 0 && scrollRef.current) {
      const targetLeft = activeIndex * thumbWidth;
      const { scrollLeft, clientWidth } = scrollRef.current;
      if (targetLeft < scrollLeft || targetLeft + thumbWidth > scrollLeft + clientWidth) {
        scrollRef.current.scrollTo({ left: Math.max(0, targetLeft - clientWidth / 2 + thumbWidth / 2), behavior: "smooth" });
      }
    }
  }, [activeIndex, thumbWidth]);

  const scroll = (dir: number) => {
    if (scrollRef.current) scrollRef.current.scrollBy({ left: dir * thumbWidth * 4, behavior: "smooth" });
  };
  const clampPercent = (value: number) => Math.min(100, Math.max(0, value));
  const timelineDuration = Math.max(0.001, duration || 0);
  
  return (
    <div className="flex-shrink-0 bg-[#1a1a1a] border-t border-border">
      {(spans.length > 0 || hasFaceDetections) && (
        <div className="border-b border-black/20 bg-[#181a20]">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-white/10 bg-[#20222a] px-2 py-1.5 pr-8 text-[10px] text-white/65">
            <div className="flex min-w-0 flex-wrap items-center gap-1.5">
              <span className="font-semibold uppercase tracking-[0.16em] text-white/70">Timeline overlays</span>
              {nonFaceSpans.length > 0 ? <span className="rounded border border-white/10 bg-white/[0.04] px-1.5 py-0.5">{nonFaceSpans.length} segment{nonFaceSpans.length === 1 ? "" : "s"}</span> : null}
              {hasFaceDetections ? <span className="rounded border border-white/10 bg-white/[0.04] px-1.5 py-0.5">face detections</span> : null}
            </div>
            <div className="flex shrink-0 flex-wrap items-center gap-1">
              <button
                type="button"
                onClick={() => setOverlaysCollapsed((value) => !value)}
                className="inline-flex items-center gap-1 rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
                title={overlaysCollapsed ? "Show timeline overlays" : "Collapse timeline overlays"}
              >
                <ChevronDown className={`h-3 w-3 transition-transform ${overlaysCollapsed ? "-rotate-90" : ""}`} />
                {overlaysCollapsed ? "Show" : "Collapse"}
              </button>
              {!overlaysCollapsed && segmentLanes.length > 4 ? (
                <button
                  type="button"
                  onClick={() => setShowAllResolvedLanes((value) => !value)}
                  className="rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
                >
                  {showAllResolvedLanes ? "Fewer segments" : `All ${segmentLanes.length} segment lanes`}
                </button>
              ) : null}
              {!overlaysCollapsed && hasFaceDetections ? (
                <button
                  type="button"
                  onClick={() => setFacesEnabled((value) => !value)}
                  className="inline-flex items-center gap-1 rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
                  title={facesEnabled ? "Hide face appearance bars" : "Show face appearance bars"}
                >
                  {facesEnabled ? <Eye className="h-3 w-3" /> : <EyeOff className="h-3 w-3" />}
                  {facesEnabled ? "Hide faces" : "Show faces"}
                </button>
              ) : null}
              {!overlaysCollapsed && facesEnabled && faceLanes.length > 2 ? (
                <button
                  type="button"
                  onClick={() => setShowAllFaceLanes((value) => !value)}
                  className="rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
                >
                  {showAllFaceLanes ? "Fewer faces" : `All ${faceLanes.length} face lanes`}
                </button>
              ) : null}
            </div>
          </div>
          {!overlaysCollapsed ? <div className="space-y-2 px-2 py-2">
            {nonFaceSpans.length > 0 ? (
              <div className="space-y-1">
                <div className="flex items-center justify-between text-[10px] uppercase tracking-[0.14em] text-white/45">
                  <span>Segments{profileName ? ` · ${profileName}` : ""}</span>
                  {hiddenResolvedLaneCount > 0 ? <span>{hiddenResolvedLaneCount} hidden</span> : null}
                </div>
                <div className="relative overflow-hidden rounded border border-white/10 bg-black/25" style={{ height: `${Math.max(28, visibleResolvedLanes.length * 24 + 6)}px` }}>
                  {visibleResolvedLanes.map((lane, laneIndex) => lane.map(({ item, endSec }) => {
                    const start = clampPercent((item.startSec / timelineDuration) * 100);
                    const end = clampPercent(((endSec + 0.001) / timelineDuration) * 100);
                    const width = Math.max(0.45, end - start);
                    const color = getTimelineOverlayColor(item, SEGMENT_TIMELINE_COLORS);

                    return (
                      <button
                        key={item.key}
                        className="absolute h-5 overflow-hidden rounded-sm px-1.5 text-left text-[10px] font-semibold leading-5 text-white shadow-sm transition hover:brightness-110 focus:outline-none focus:ring-1 focus:ring-white/70"
                        style={{
                          left: `${start}%`,
                          top: `${laneIndex * 24 + 4}px`,
                          width: `${width}%`,
                          backgroundColor: color,
                          boxShadow: "inset 0 0 0 1px rgba(255,255,255,0.2)",
                        }}
                        title={`${item.label} (${formatTimelineTime(item.startSec)} - ${formatTimelineTime(endSec)})`}
                        onClick={() => onSeek?.(item.startSec)}
                      >
                        {timelineLabelFits(width, item.label) ? <span className="block truncate">{item.label}</span> : null}
                      </button>
                    );
                  }))}
                </div>
              </div>
            ) : null}
            {hasFaceDetections && facesEnabled ? (
              <div className="space-y-1">
                <div className="flex items-center justify-between text-[10px] uppercase tracking-[0.14em] text-white/45">
                  <span>Faces</span>
                  {hiddenFaceLaneCount > 0 ? <span>{hiddenFaceLaneCount} hidden</span> : null}
                </div>
                <div className="relative overflow-hidden rounded border border-white/10 bg-black/25" style={{ height: `${Math.max(28, visibleFaceLanes.length * 24 + 6)}px` }}>
                  {visibleFaceLanes.map((lane, laneIndex) => lane.map(({ item, endSec }) => {
                    const start = clampPercent((item.startSec / timelineDuration) * 100);
                    const end = clampPercent(((endSec + 0.001) / timelineDuration) * 100);
                    const width = Math.max(0.45, end - start);
                    const color = getTimelineOverlayColor(item, FACE_TIMELINE_COLORS);

                    return (
                      <button
                        key={item.key}
                        className="absolute h-5 overflow-hidden rounded-sm px-1.5 text-left text-[10px] font-semibold leading-5 text-white shadow-sm transition hover:brightness-110 focus:outline-none focus:ring-1 focus:ring-white/70"
                        style={{
                          left: `${start}%`,
                          top: `${laneIndex * 24 + 4}px`,
                          width: `${width}%`,
                          backgroundColor: color,
                          boxShadow: "inset 0 0 0 1px rgba(255,255,255,0.2)",
                        }}
                        title={`${item.label} (${formatTimelineTime(item.startSec)} - ${formatTimelineTime(endSec)})`}
                        onClick={() => onSeek?.(item.startSec)}
                      >
                        {timelineLabelFits(width, item.label) ? <span className="block truncate">{item.label}</span> : null}
                      </button>
                    );
                  }))}
                </div>
              </div>
            ) : null}
          </div> : null}
        </div>
      )}
      {detections.length > 0 && (
        <div className="relative h-5 border-b border-black/20 bg-[#1f2c35]">
          {detections.map((detection) => {
            const time = detection.observedAtSec ?? 0;
            return (
              <button
                key={detection.id}
                className="absolute top-1/2 h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full border border-white/30 bg-sky-400/80 hover:bg-sky-300"
                style={{ left: `${clampPercent((time / timelineDuration) * 100)}%` }}
                title={`${detection.class} (${Math.round(detection.score * 100)}%) at ${formatTimelineTime(time)}${detection.refKind && detection.refId != null ? ` • ${detection.refKind} #${detection.refId}` : ""}`}
                onClick={() => onSeek?.(time)}
              />
            );
          })}
        </div>
      )}

      {spriteData && spriteLoadSettled && !spriteError ? (
      <div className="relative flex overflow-hidden" ref={containerRef}>
        <button onClick={() => scroll(-1)} className="flex-shrink-0 w-7 bg-[#222] hover:bg-[#333] text-muted border-r border-border z-10">
          <ChevronLeft className="w-4 h-4 mx-auto" />
        </button>
        
        <div ref={scrollRef} className="flex-1 flex overflow-x-auto scrollbar-thin scrollbar-thumb-border">
          {Array.from({ length: thumbCount }).map((_, i) => {
            const entry = spriteData.entries[i];
            const time = entry?.start ?? 0;
            const isActive = i === activeIndex;
            return (
              <div 
                key={i} 
                className={`flex-shrink-0 relative cursor-pointer hover:ring-2 hover:ring-accent hover:z-10 ${isActive ? "ring-2 ring-accent z-10" : ""}`}
                style={{ width: thumbWidth }}
                onClick={() => onSeek?.(time)}
              >
                <div className="bg-surface" style={{ width: thumbWidth, height: thumbHeight }}>
                  {entry ? (
                    <div
                      style={{
                        width: thumbWidth,
                        height: thumbHeight,
                        backgroundImage: `url(${spriteData!.imageUrl})`,
                        backgroundPosition: `-${entry.x * (thumbWidth / entry.w)}px -${entry.y * (thumbHeight / entry.h)}px`,
                        backgroundSize: `${(spriteData!.entries[0].w * Math.ceil(Math.sqrt(thumbCount))) * (thumbWidth / entry.w)}px auto`,
                      }}
                    />
                  ) : null}
                </div>
                <div className="absolute bottom-0 left-0 right-0 text-center text-[10px] text-white bg-black/70 py-0.5">
                  {formatTime(time)}
                </div>
              </div>
            );
          })}
        </div>
        
        <button onClick={() => scroll(1)} className="flex-shrink-0 w-7 bg-[#222] hover:bg-[#333] text-muted border-l border-border z-10">
          <ChevronRight className="w-4 h-4 mx-auto" />
        </button>
      </div>
      ) : null}
    </div>
  );
}

function buildTimelineLanes<T extends { key: string; startSec: number; endSec: number }>(items: T[]) {
  const ordered = [...items].sort((left, right) => left.startSec - right.startSec || left.endSec - right.endSec || left.key.localeCompare(right.key));
  const lanes: Array<Array<{ item: T; endSec: number }>> = [];
  const laneEnds: number[] = [];

  ordered.forEach((item) => {
    const effectiveEnd = Math.max(item.endSec, item.startSec + 0.05);
    let laneIndex = laneEnds.findIndex((laneEnd) => laneEnd <= item.startSec);
    if (laneIndex === -1) {
      laneIndex = lanes.length;
      lanes.push([]);
      laneEnds.push(effectiveEnd);
    } else {
      laneEnds[laneIndex] = effectiveEnd;
    }

    lanes[laneIndex].push({ item: { ...item, endSec: effectiveEnd }, endSec: effectiveEnd });
  });

  return lanes;
}

function isFaceTimelineSegment(segment: Pick<Segment, "title" | "kind" | "sourceKey">) {
  const normalizedKind = segment.kind?.trim().toLowerCase() ?? "";
  const normalizedSource = segment.sourceKey?.trim().toLowerCase() ?? "";
  const normalizedTitle = segment.title?.trim().toLowerCase() ?? "";
  return normalizedKind === "face" || normalizedSource.includes("face") || normalizedTitle.startsWith("face-");
}

function isFaceResolvedSpan(
  span: Pick<ResolvedSpan, "kind" | "sourceKey" | "tagName" | "segmentIds">,
  rawSegmentsById: Map<number, Pick<Segment, "id" | "title" | "kind" | "sourceKey" | "refId">>,
) {
  if (isFaceTimelineSegment({ title: span.tagName ?? undefined, kind: span.kind, sourceKey: span.sourceKey ?? "" })) {
    return true;
  }

  const segmentIds = span.segmentIds ?? [];
  return segmentIds.length > 0 && segmentIds.every((segmentId) => {
    const segment = rawSegmentsById.get(segmentId);
    return segment ? isFaceTimelineSegment(segment) : false;
  });
}

function parseVttTime(timeStr: string): number {
  const parts = timeStr.split(":");
  return parseInt(parts[0]) * 3600 + parseInt(parts[1]) * 60 + parseFloat(parts[2]);
}

function formatTimelineTime(seconds: number) {
  const mins = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  const fractional = seconds % 1;

  if (fractional > 0) {
    return `${mins}:${secs.toString().padStart(2, "0")}.${Math.round(fractional * 10)}`;
  }

  return `${mins}:${secs.toString().padStart(2, "0")}`;
}

function parseSegmentTimeInput(value: string) {
  const trimmed = value.trim();
  if (!trimmed) return null;

  const parts = trimmed.split(":").map((part) => part.trim());
  if (parts.length > 3 || parts.some((part) => part === "" || Number.isNaN(Number(part)))) return null;

  const numbers = parts.map(Number);
  if (numbers.some((part) => part < 0 || !Number.isFinite(part))) return null;

  if (numbers.length === 1) return numbers[0];
  if (numbers.length === 2) return numbers[0] * 60 + numbers[1];
  return numbers[0] * 3600 + numbers[1] * 60 + numbers[2];
}

function formatSegmentTimeInput(seconds: number) {
  const safeSeconds = Math.max(0, seconds || 0);
  const hours = Math.floor(safeSeconds / 3600);
  const minutes = Math.floor((safeSeconds % 3600) / 60);
  const wholeSeconds = Math.floor(safeSeconds % 60);
  const tenths = Math.round((safeSeconds - Math.floor(safeSeconds)) * 10);
  const normalizedWholeSeconds = tenths === 10 ? wholeSeconds + 1 : wholeSeconds;
  const normalizedTenths = tenths === 10 ? 0 : tenths;
  const secondText = normalizedTenths > 0
    ? `${normalizedWholeSeconds.toString().padStart(2, "0")}.${normalizedTenths}`
    : normalizedWholeSeconds.toString().padStart(2, "0");

  return hours > 0 ? `${hours}:${minutes.toString().padStart(2, "0")}:${secondText}` : `${minutes}:${secondText}`;
}

function SegmentsPanel({
  sceneId,
  segments,
  loading,
  canEdit,
  onSeek,
  currentTime = 0,
}: {
  sceneId: number;
  segments: Segment[];
  loading: boolean;
  canEdit: boolean;
  onSeek?: (time: number) => void;
  currentTime?: number;
}) {
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [title, setTitle] = useState("");
  const [kind, setKind] = useState<"tag" | "performer">("tag");
  const [startSec, setStartSec] = useState(0);
  const [endSec, setEndSec] = useState<number | "">("");
  const [selectedTagId, setSelectedTagId] = useState<number | null>(null);
  const [selectedPerformerId, setSelectedPerformerId] = useState<number | null>(null);
  const [startText, setStartText] = useState("0:00");
  const [endText, setEndText] = useState("");
  const parsedStart = parseSegmentTimeInput(startText);
  const parsedEnd = endText.trim() === "" ? null : parseSegmentTimeInput(endText);
  const hasSelectedEntity = kind === "performer" ? selectedPerformerId != null : selectedTagId != null;
  const canSaveSegment = parsedStart != null && parsedStart >= 0 && (parsedEnd == null || parsedEnd >= parsedStart) && hasSelectedEntity;
  const kindOptions = ["tag", "performer"] as const;

  const createMutation = useMutation({
    mutationFn: (data: { title?: string; kind?: string; startSec: number; endSec?: number; tagId?: number; refId?: number }) =>
      scenes.segments.create(sceneId, {
        startSec: data.startSec,
        endSec: data.endSec,
        tagId: data.tagId,
        refId: data.refId,
        kind: data.kind,
        title: data.title,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["scene", sceneId, "segments"] });
      resetForm();
    },
  });

  const updateMutation = useMutation({
    mutationFn: (data: { segment: Segment; startSec: number; endSec?: number; tagId?: number; refId?: number; kind?: string; title?: string }) =>
      scenes.segments.update(sceneId, data.segment.id, {
        startSec: data.startSec,
        endSec: data.endSec,
        tagId: data.tagId,
        kind: data.kind,
        refId: data.refId ?? (data.kind === data.segment.kind ? data.segment.refId : undefined),
        payload: data.segment.payload,
        sourceKey: data.segment.sourceKey || "user",
        sourceRunId: data.segment.sourceRunId,
        confidence: data.segment.confidence,
        title: data.title,
        colorHint: data.segment.colorHint,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["scene", sceneId, "segments"] });
      resetForm();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (segmentId: number) => scenes.segments.delete(sceneId, segmentId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["scene", sceneId, "segments"] });
    },
  });

  const resetForm = () => {
    setAdding(false);
    setEditingId(null);
    setTitle("");
    setKind("tag");
    setStartTimeFromSeconds(0);
    setEndSec("");
    setEndText("");
    setSelectedTagId(null);
    setSelectedPerformerId(null);
  };

  const setStartTimeFromSeconds = (seconds: number) => {
    const normalized = Math.max(0, seconds);
    setStartSec(normalized);
    setStartText(formatSegmentTimeInput(normalized));
  };

  const setEndTimeFromSeconds = (seconds: number | "") => {
    if (seconds === "") {
      setEndSec("");
      setEndText("");
      return;
    }

    const normalized = Math.max(0, seconds);
    setEndSec(normalized);
    setEndText(formatSegmentTimeInput(normalized));
  };

  const startEdit = (segment: Segment) => {
    setAdding(true);
    setEditingId(segment.id);
    setTitle(segment.title || "");
    setKind(segment.kind?.toLowerCase() === "performer" ? "performer" : "tag");
    setStartTimeFromSeconds(segment.startSec);
    setEndTimeFromSeconds(segment.endSec ?? "");
    setSelectedTagId(segment.kind?.toLowerCase() === "performer" ? null : segment.tagId ?? null);
    setSelectedPerformerId(segment.kind?.toLowerCase() === "performer" && segment.refId != null ? Number(segment.refId) : null);
  };

  const editingSegment = editingId != null ? segments.find((segment) => segment.id === editingId) ?? null : null;

  const saveSegment = () => {
    if (!canSaveSegment || parsedStart == null) {
      return;
    }

    const nextEndSec = parsedEnd == null ? undefined : parsedEnd;
    const nextKind = kind;
    const nextTagId = kind === "tag" ? selectedTagId ?? undefined : undefined;
    const nextRefId = kind === "performer" ? selectedPerformerId ?? undefined : undefined;

    if (editingSegment) {
      updateMutation.mutate({
        segment: editingSegment,
        startSec: parsedStart,
        endSec: nextEndSec,
        tagId: nextTagId,
        refId: nextRefId,
        kind: nextKind,
        title: title || undefined,
      });
      return;
    }

    createMutation.mutate({
      title: title || undefined,
      startSec: parsedStart,
      endSec: nextEndSec,
      tagId: nextTagId,
      refId: nextRefId,
      kind: nextKind,
    });
  };

  return (
    <div>
      <div className="mb-3 flex items-center justify-between">
        <span className="text-sm text-secondary">
          {loading ? "Loading segments..." : `${segments.length} segment${segments.length !== 1 ? "s" : ""}`}
        </span>
        {canEdit && (
          <button onClick={() => adding ? resetForm() : setAdding(true)} className="flex items-center gap-1 text-sm text-accent hover:underline">
            <Plus className="h-3.5 w-3.5" /> {adding ? "Cancel" : "Add"}
          </button>
        )}
      </div>

      {adding && canEdit && (
        <div className="mb-3 space-y-2 rounded border border-border bg-card p-3">
          <div className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_12rem]">
            <input
              type="text"
              placeholder="Segment title"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              className="w-full rounded border border-border bg-input px-3 py-1.5 text-sm text-foreground"
            />
            <select
              value={kind}
              onChange={(event) => {
                const nextKind = event.target.value === "performer" ? "performer" : "tag";
                setKind(nextKind);
                if (nextKind === "performer") {
                  setSelectedTagId(null);
                } else {
                  setSelectedPerformerId(null);
                }
              }}
              className="w-full rounded border border-border bg-input px-3 py-1.5 text-sm text-foreground"
            >
              {kindOptions.map((option) => (
                <option key={option} value={option}>{option === "tag" ? "Tag" : "Performer"}</option>
              ))}
            </select>
          </div>
          <div className="grid gap-2 sm:grid-cols-2 xl:grid-cols-[minmax(7rem,0.75fr)_minmax(7rem,0.75fr)_minmax(10rem,1.8fr)]">
            <label className="space-y-1">
              <span className="text-xs text-secondary">Start</span>
              <div className="flex gap-1">
                <input
                  type="text"
                  inputMode="decimal"
                  placeholder="0:00"
                  value={startText}
                  onChange={(event) => {
                    const next = event.target.value;
                    setStartText(next);
                    const parsed = parseSegmentTimeInput(next);
                    if (parsed != null) setStartSec(parsed);
                  }}
                  onBlur={() => setStartText(formatSegmentTimeInput(startSec))}
                  className="min-w-0 flex-1 rounded border border-border bg-input px-3 py-1.5 font-mono text-sm text-foreground"
                />
                <button type="button" onClick={() => setStartTimeFromSeconds(currentTime)} className="inline-flex items-center justify-center rounded border border-border px-2 text-secondary hover:text-foreground" title="Use current time" aria-label="Use current time for segment start"><Clock className="h-3.5 w-3.5" /></button>
              </div>
            </label>
            <label className="space-y-1">
              <span className="text-xs text-secondary">End</span>
              <div className="flex gap-1">
                <input
                  type="text"
                  inputMode="decimal"
                  placeholder="Optional"
                  value={endText}
                  onChange={(event) => {
                    const next = event.target.value;
                    setEndText(next);
                    if (next.trim() === "") {
                      setEndSec("");
                      return;
                    }
                    const parsed = parseSegmentTimeInput(next);
                    if (parsed != null) setEndSec(parsed);
                  }}
                  onBlur={() => setEndText(endSec === "" ? "" : formatSegmentTimeInput(endSec))}
                  className="min-w-0 flex-1 rounded border border-border bg-input px-3 py-1.5 font-mono text-sm text-foreground"
                />
                <button type="button" onClick={() => setEndTimeFromSeconds(currentTime)} className="inline-flex items-center justify-center rounded border border-border px-2 text-secondary hover:text-foreground" title="Use current time" aria-label="Use current time for segment end"><Clock className="h-3.5 w-3.5" /></button>
              </div>
            </label>
            {kind === "tag" ? (
              <label className="min-w-0 space-y-1 sm:col-span-2 xl:col-span-1">
                <span className="text-xs text-secondary">Tag</span>
                <EntityReferenceSelector
                  entityType="tag"
                  value={selectedTagId ?? undefined}
                  onChange={(tagId) => setSelectedTagId(tagId ?? null)}
                  placeholder="Search tags..."
                  inputClassName="w-full rounded border border-border bg-input px-3 py-1.5 text-sm text-foreground"
                />
              </label>
            ) : (
              <label className="min-w-0 space-y-1 sm:col-span-2 xl:col-span-1">
                <span className="text-xs text-secondary">Performer</span>
                <EntityReferenceSelector
                  entityType="performer"
                  value={selectedPerformerId ?? undefined}
                  onChange={(performerId) => setSelectedPerformerId(performerId ?? null)}
                  placeholder="Search performers..."
                  inputClassName="w-full rounded border border-border bg-input px-3 py-1.5 text-sm text-foreground"
                />
              </label>
            )}
          </div>
          {!canSaveSegment ? <div className="text-xs text-red-300">Use valid times and choose a {kind}.</div> : null}
          <div className="flex justify-end gap-2">
            <button onClick={resetForm} className="px-3 py-1 text-sm text-secondary hover:text-foreground">Cancel</button>
            <button
              onClick={saveSegment}
              disabled={!canSaveSegment || createMutation.isPending || updateMutation.isPending}
              className="rounded bg-accent px-3 py-1 text-sm text-white hover:bg-accent-hover disabled:opacity-50"
            >
              {editingId ? "Update" : "Save"}
            </button>
          </div>
        </div>
      )}

      {!loading && segments.length === 0 && !adding && (
        <p className="text-sm text-muted">No segments yet.</p>
      )}

      <div className="space-y-1">
        {segments.map((segment) => (
          <div key={segment.id} className="group flex items-center justify-between rounded border border-border bg-card px-3 py-2 text-sm">
            <button className="flex min-w-0 items-center gap-3 text-left hover:text-accent" onClick={() => onSeek?.(segment.startSec)}>
              <span className="w-24 font-mono text-xs text-accent">
                {formatTimelineTime(segment.startSec)}{segment.endSec != null ? ` - ${formatTimelineTime(segment.endSec)}` : ""}
              </span>
              <span className="truncate text-foreground group-hover:text-accent">{segment.title || segment.kind || segment.tagName || "Untitled segment"}</span>
              {segment.tagName && <span className="rounded bg-surface px-1.5 py-0.5 text-xs text-secondary">{segment.tagName}</span>}
              {segment.kind && <span className="rounded bg-surface px-1.5 py-0.5 text-xs text-secondary">{segment.kind}</span>}
            </button>
            {canEdit && (
              <div className="flex items-center gap-2 opacity-0 transition-opacity group-hover:opacity-100">
                <button onClick={() => startEdit(segment)} className="text-muted hover:text-accent" title="Edit segment">
                  <Pencil className="h-3.5 w-3.5" />
                </button>
                <button onClick={() => deleteMutation.mutate(segment.id)} className="text-muted hover:text-red-400" title="Delete segment">
                  <Trash2 className="h-3.5 w-3.5" />
                </button>
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function DetectionsPanel({
  detections,
  loading,
  onSeek,
}: {
  detections: Detection[];
  loading: boolean;
  onSeek?: (time: number) => void;
}) {
  const classCounts = useMemo(() => {
    const counts = new Map<string, number>();
    for (const detection of detections) {
      counts.set(detection.class, (counts.get(detection.class) ?? 0) + 1);
    }
    return Array.from(counts.entries()).sort((a, b) => b[1] - a[1]).slice(0, 6);
  }, [detections]);

  if (loading) {
    return <div className="text-sm text-secondary">Loading detections...</div>;
  }

  if (detections.length === 0) {
    return <div className="text-sm text-muted">No detections recorded for this scene.</div>;
  }

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center gap-2 text-xs text-secondary">
        <span>{detections.length} detection{detections.length !== 1 ? "s" : ""}</span>
        {classCounts.map(([name, count]) => (
          <span key={name} className="rounded-full border border-border bg-surface px-2 py-1">
            {name} · {count}
          </span>
        ))}
      </div>
      <div className="space-y-1">
        {detections.map((detection) => (
          <div key={detection.id} className="rounded border border-border bg-card px-3 py-2 text-sm">
            <div className="flex items-center justify-between gap-3">
              <button className="flex items-center gap-3 text-left hover:text-accent" onClick={() => onSeek?.(detection.observedAtSec ?? 0)}>
                <span className="w-20 font-mono text-xs text-accent">{formatTimelineTime(detection.observedAtSec ?? 0)}</span>
                <span className="text-foreground">{detection.class}</span>
                <span className="rounded bg-surface px-1.5 py-0.5 text-xs text-secondary">{Math.round(detection.score * 100)}%</span>
              </button>
              <div className="text-xs text-secondary">{detection.frameWidth}×{detection.frameHeight}</div>
            </div>
            <div className="mt-2 flex flex-wrap gap-2 text-xs text-secondary">
              <span className="rounded bg-surface px-1.5 py-0.5">x {detection.x.toFixed(3)}</span>
              <span className="rounded bg-surface px-1.5 py-0.5">y {detection.y.toFixed(3)}</span>
              <span className="rounded bg-surface px-1.5 py-0.5">w {detection.w.toFixed(3)}</span>
              <span className="rounded bg-surface px-1.5 py-0.5">h {detection.h.toFixed(3)}</span>
              {detection.refKind && detection.refId != null && (
                <span className="rounded bg-surface px-1.5 py-0.5">{detection.refKind} #{detection.refId}</span>
              )}
              {detection.groupKey && <span className="rounded bg-surface px-1.5 py-0.5">group {detection.groupKey}</span>}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ===== Inline Scene Edit Panel =====
function SceneEditPanel({ scene, onSaved }: { scene: Scene; onSaved: () => void }) {
  const queryClient = useQueryClient();
  const { config } = useAppConfig();
  const [title, setTitle] = useState(scene.title || "");
  const [code, setCode] = useState(scene.code || "");
  const [details, setDetails] = useState(scene.details || "");
  const [director, setDirector] = useState(scene.director || "");
  const [date, setDate] = useState(scene.date || "");
  const [isVr, setIsVr] = useState(scene.isVr ?? false);
  const [rating, setRating] = useState<number | undefined>(undefined);
  const [urls, setUrls] = useState(scene.urls.length > 0 ? scene.urls : [""]);
  const [remoteIds, setRemoteIds] = useState<RemoteIdValue[]>(scene.remoteIds?.length ? scene.remoteIds : []);
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(scene.customFields ?? {}) });
  const [studioId, setStudioId] = useState<number | undefined>(scene.studioId ?? undefined);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(getEditableTagIds(scene.tags));
  const [selectedPerformerIds, setSelectedPerformerIds] = useState<number[]>(scene.performers.map((p) => p.id));
  const [selectedGalleryIds, setSelectedGalleryIds] = useState<number[]>(scene.galleries.map((g) => g.id));
  const [selectedGroups, setSelectedGroups] = useState<{ groupId: number; sceneIndex: number }[]>(
    scene.groups.map((g) => ({ groupId: g.id, sceneIndex: g.sceneIndex }))
  );
  const [contextTagIdsByPerformer, setContextTagIdsByPerformer] = useState<Record<number, number[]>>(() => buildSceneEditPerformerContextTagIds(scene));
  const [performerOccurrenceTagsOpen, setPerformerOccurrenceTagsOpen] = useState(false);
  useEffect(() => {
    setTitle(scene.title || ""); setCode(scene.code || ""); setDetails(scene.details || "");
    setDirector(scene.director || ""); setDate(scene.date || ""); setIsVr(scene.isVr ?? false); setRating(undefined);
    setUrls(scene.urls.length > 0 ? scene.urls : [""]); setStudioId(scene.studioId ?? undefined);
    setRemoteIds(scene.remoteIds?.length ? scene.remoteIds : []);
    setCustomFields({ ...(scene.customFields ?? {}) });
    setSelectedTagIds(getEditableTagIds(scene.tags)); setSelectedPerformerIds(scene.performers.map((p) => p.id));
    setSelectedGalleryIds(scene.galleries.map((g) => g.id));
    setSelectedGroups(scene.groups.map((g) => ({ groupId: g.id, sceneIndex: g.sceneIndex })));
    setContextTagIdsByPerformer(buildSceneEditPerformerContextTagIds(scene));
  }, [scene]);

  const mutation = useMutation({
    mutationFn: async (data: SceneUpdate) => {
      const updated = await scenes.update(scene.id, data);
      await syncSceneEditPerformerContextTags(scene.id, scene.contextTagApplications ?? [], contextTagIdsByPerformer, selectedPerformerIds);
      return updated;
    },
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["scene", scene.id] }); queryClient.invalidateQueries({ queryKey: ["tagapplications"] }); queryClient.invalidateQueries({ queryKey: ["scenes"] }); onSaved(); },
  });

  const handleSave = () => {
    const urlList = urls.map((url) => url.trim()).filter(Boolean);
    mutation.mutate({ title: title || undefined, code: code || undefined, details: details || undefined,
      director: director || undefined, date: date || undefined, isVr, rating, studioId,
      urls: urlList, remoteIds: normalizeRemoteIds(remoteIds), customFields,
      tagIds: selectedTagIds, performerIds: selectedPerformerIds, galleryIds: selectedGalleryIds, groups: selectedGroups });
  };

  const setPerformerContextTagIds = (performerId: number, tagIds: number[]) => {
    setContextTagIdsByPerformer((current) => ({ ...current, [performerId]: Array.from(new Set(tagIds)) }));
  };
  const setSelectedGroupIds = (groupIds: number[]) => {
    setSelectedGroups(groupIds.map((groupId) => selectedGroups.find((group) => group.groupId === groupId) ?? { groupId, sceneIndex: 0 }));
  };

  const lockedTagIds = getLockedTagIds(scene.tags);
  const displayedTagIds = mergeTagIds(lockedTagIds, selectedTagIds);
  const tagProvenanceById = useMemo(() => {
    const lookup: Record<number, TagProvenance[] | undefined> = {};
    for (const tag of scene.tags) {
      lookup[tag.id] = resolveTagProvenance(tag, scene.fieldProvenance);
    }
    return lookup;
  }, [scene.fieldProvenance, scene.tags]);
  const updateSelectedTagIds = (tagIds: number[]) => {
    const locked = new Set(lockedTagIds);
    setSelectedTagIds(tagIds.filter((tagId) => !locked.has(tagId)));
  };

  const inputCls = "w-full bg-input border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent";

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="title" block>
          <label className="space-y-1"><span className="text-xs text-secondary">Title</span><input value={title} onChange={(e) => setTitle(e.target.value)} className={inputCls} /></label>
        </FieldProvenanceHover>
        <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="date" block>
          <label className="space-y-1"><span className="text-xs text-secondary">Date</span><input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputCls} /></label>
        </FieldProvenanceHover>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="code" block>
          <label className="space-y-1"><span className="text-xs text-secondary">Studio Code</span><input value={code} onChange={(e) => setCode(e.target.value)} className={inputCls} /></label>
        </FieldProvenanceHover>
        <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="director" block>
          <label className="space-y-1"><span className="text-xs text-secondary">Director</span><input value={director} onChange={(e) => setDirector(e.target.value)} className={inputCls} /></label>
        </FieldProvenanceHover>
      </div>
      <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="details" block>
        <label className="block space-y-1"><span className="text-xs text-secondary">Details</span><textarea value={details} onChange={(e) => setDetails(e.target.value)} rows={3} className={inputCls} /></label>
      </FieldProvenanceHover>
      <label className="inline-flex items-center gap-2 text-sm text-secondary">
        <input type="checkbox" checked={isVr} onChange={(e) => setIsVr(e.target.checked)} className="rounded border-border bg-card" />
        VR
      </label>
      <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="studio" block>
        <div className="space-y-1">
          <span className="text-xs text-secondary">Studio</span>
          <StudioSelector value={studioId} onChange={setStudioId} placeholder="Search studios..." />
        </div>
      </FieldProvenanceHover>
      <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="urls" block>
        <div className="space-y-1"><span className="text-xs text-secondary">URLs</span><StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" /></div>
      </FieldProvenanceHover>
      {/* Tags */}
      <div className="space-y-1">
        <span className="text-xs text-secondary">Tags</span>
        <EntityReferenceMultiSelector entityType="tag" values={displayedTagIds} lockedIds={lockedTagIds} onChange={updateSelectedTagIds} placeholder="Search tags..." inputClassName={inputCls} selectedProvenanceById={tagProvenanceById} />
      </div>

      {/* Performers */}
      <FieldProvenanceHover fieldProvenance={scene.fieldProvenance} fieldKey="performers" block>
        <div className="space-y-1">
          <span className="text-xs text-secondary">Performers</span>
          <EntityReferenceMultiSelector entityType="performer" values={selectedPerformerIds} onChange={setSelectedPerformerIds} placeholder="Search performers..." inputClassName={inputCls} />
        </div>
      </FieldProvenanceHover>

      {selectedPerformerIds.length > 0 ? (
        <div className="space-y-2 rounded-lg border border-border bg-surface/40 p-3">
          <button
            type="button"
            onClick={() => setPerformerOccurrenceTagsOpen((open) => !open)}
            className="flex w-full items-center justify-between gap-3 text-left text-xs font-medium uppercase tracking-wide text-secondary hover:text-foreground"
          >
            <span>Performer Occurrence Tags</span>
            <span className="inline-flex items-center gap-2 normal-case tracking-normal text-muted">
              {selectedPerformerIds.reduce((sum, performerId) => sum + (contextTagIdsByPerformer[performerId]?.length ?? 0), 0)} tag assignments
              {performerOccurrenceTagsOpen ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
            </span>
          </button>
          {performerOccurrenceTagsOpen ? selectedPerformerIds.map((performerId) => {
            const tagIds = contextTagIdsByPerformer[performerId] ?? [];

            return (
              <div key={performerId} className="rounded-lg border border-border bg-card/70 p-3">
                <div className="mb-2 flex items-center justify-between gap-3">
                  <div className="min-w-0 text-sm font-medium text-foreground"><EntityReferenceValue entityType="performer" value={performerId} /></div>
                  <div className="text-xs text-muted">{tagIds.length} tag{tagIds.length === 1 ? "" : "s"}</div>
                </div>
                <EntityReferenceMultiSelector
                  entityType="tag"
                  values={tagIds}
                  onChange={(nextTagIds) => setPerformerContextTagIds(performerId, nextTagIds)}
                  placeholder="Search tags for this occurrence..."
                  emptyMessage="No tags found"
                  inputClassName={inputCls}
                />
              </div>
            );
          }) : null}
        </div>
      ) : null}

      {/* Galleries */}
      <div className="space-y-1">
        <span className="text-xs text-secondary">Galleries</span>
        <EntityReferenceMultiSelector entityType="gallery" values={selectedGalleryIds} onChange={setSelectedGalleryIds} placeholder="Search galleries..." inputClassName={inputCls} />
      </div>

      {/* Groups */}
      <div className="space-y-1">
        <span className="text-xs text-secondary">Groups</span>
        <div className="space-y-1 mb-1">
          {selectedGroups.map((sg) => {
            return (
              <div key={sg.groupId} className="flex items-center gap-2">
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-orange-900 text-orange-300">
                  <EntityReferenceValue entityType="group" value={sg.groupId} />
                  <button onClick={() => setSelectedGroups(selectedGroups.filter((g) => g.groupId !== sg.groupId))} className="hover:text-white">×</button>
                </span>
                <label className="flex items-center gap-1 text-xs text-muted">
                  Scene #
                  <input type="number" min={0} value={sg.sceneIndex}
                    onChange={(e) => setSelectedGroups(selectedGroups.map((g) => g.groupId === sg.groupId ? { ...g, sceneIndex: Number(e.target.value) || 0 } : g))}
                    className="w-16 bg-surface border border-border rounded px-2 py-0.5 text-xs text-foreground focus:outline-none focus:border-accent" />
                </label>
              </div>
            );
          })}
        </div>
        <EntityReferenceMultiSelector entityType="group" values={selectedGroups.map((group) => group.groupId)} onChange={setSelectedGroupIds} placeholder="Search groups..." inputClassName={inputCls} />
      </div>

      <div className="space-y-1"><span className="text-xs text-secondary">Remote IDs</span><RemoteIdsEditor value={remoteIds} onChange={setRemoteIds} metadataServers={config?.scraping?.metadataServers} /></div>
      <div className="space-y-1"><span className="text-xs text-secondary">Custom Fields</span><CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="scene" /></div>

      {mutation.error && <div className="bg-red-900/50 border border-red-700 text-red-300 rounded p-2 text-sm">{(mutation.error as Error).message}</div>}

      <div className="flex justify-end gap-3 pt-2">
        <button onClick={onSaved} className="px-4 py-2 text-sm text-secondary hover:text-foreground">Cancel</button>
        <button onClick={handleSave} disabled={mutation.isPending} className="px-4 py-2 text-sm bg-accent hover:bg-accent-hover text-white rounded disabled:opacity-50">
          {mutation.isPending ? "Saving…" : "Save"}
        </button>
      </div>
    </div>
  );
}
