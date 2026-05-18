import { useQueries, useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { faces, scenes, segmentDisplayProfiles, tags, tagApplications, entityImages, performers as performersApi, studios as studiosApi, galleries as galleriesApi, groups as groupsApi, metadata, fileOps } from "../api/client";
import { formatDuration, formatFileSize, formatDate, TagBadge, getResolutionLabel, CustomFieldsDisplay } from "../components/shared";
import { 
  Pencil, Plus, Trash2, Search, Eye, EyeOff, Heart, ArrowLeft, ThumbsUp,
  Check, ChevronLeft, ChevronRight, ChevronDown, MoreVertical,
  Gauge, Clapperboard, Monitor, FolderOpen, Layers,
  RefreshCw, Camera, Image, Merge, Upload, ExternalLink, Download, X,
} from "lucide-react";
import { useState, useRef, useEffect, useCallback, Fragment, useMemo, lazy, Suspense } from "react";
import { ConfirmDialog } from "../components/ConfirmDialog";
import type { Detection, Face, ResolvedSpan, Scene, SceneUpdate, Segment, TagApplication } from "../api/types";
import { ExtensionSlot } from "../router/RouteRegistry";
import { AspectRatingsPanel } from "../components/AspectRatingsPanel";
import { InteractiveRating } from "../components/Rating";
import { ResolvedSpansPanel } from "../components/ResolvedSpansPanel";
import { useSceneQueue } from "../state/SceneQueueContext";
import { useAppConfig } from "../state/AppConfigContext";
import { useExtensions } from "../extensions/ExtensionLoader";
import { createRouteLinkProps } from "../components/cardNavigation";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { ExtensionEntityActions } from "../components/ExtensionEntityActions";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity, filterItemsByPermission, hasAnyPermission } from "../auth/visibility";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { VideoPlayer } from "../components/VideoPlayer";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { MediaDetailLayout } from "../components/MediaDetailLayout/MediaDetailLayout";
import { PerformerTile } from "../components/EntityCards";
import { trackInteraction } from "../utils/interactionTracking";
import { SceneVisualSimilarityPanel } from "../components/VisualSimilarityPanel";
import { GroupedTagOptionList, SelectedTagChips, filterTagsForSelector, type SelectableTag } from "../components/TagSelector";

const GenerateDialog = lazy(() => import("../components/GenerateDialog").then((module) => ({ default: module.GenerateDialog })));
const DetailMergeDialog = lazy(() => import("../components/DetailMergeDialog").then((module) => ({ default: module.DetailMergeDialog })));
const IdentifyDialog = lazy(() => import("../components/IdentifyDialog").then((module) => ({ default: module.IdentifyDialog })));
const SceneDownloadDialog = lazy(() => import("../components/SceneDownloadDialog").then((module) => ({ default: module.SceneDownloadDialog })));
const SceneScrapeDialog = lazy(() => import("../components/SceneScrapeDialog").then((module) => ({ default: module.SceneScrapeDialog })));

interface Props {
  id: number;
  initialSeekTo?: number;
  onNavigate: (r: any) => void;
}

// localStorage-backed boolean flag with safe SSR fallback. Used by the timeline
// to remember collapse and faces-on/off state across reloads.
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

type TabKey = "details" | "segments" | "filters" | "file-info" | "edit" | "history" | string;

export function SceneDetailPage({ id, initialSeekTo, onNavigate }: Props) {
  const { data: scene, isLoading } = useQuery({
    queryKey: ["scene", id],
    queryFn: () => scenes.get(id),
  });
  const { hasPermission, user } = useAuth();
  const { config } = useAppConfig();
  const { hasPrev, hasNext, prevId, nextId, currentPosition, queueLength } = useSceneQueue();
  const { getTabsForPage, resolveComponent: resolveExtComponent } = useExtensions();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [showGenerate, setShowGenerate] = useState(false);
  const [theaterMode, setTheaterMode] = useState(false);
  const [showOpsMenu, setShowOpsMenu] = useState(false);
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
  const opsMenuRef = useRef<HTMLDivElement>(null);
  const coverFileInputRef = useRef<HTMLInputElement>(null);
  const [videoTime, setVideoTime] = useState(0);
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
    if (!scene || !trackPlaybackActivity) return;
    trackInteraction({ hostType: "scene", hostId: id, kind: "pageVisit" });
    queryClient.invalidateQueries({ queryKey: ["engagement", "scene", id] });
  }, [id, queryClient, scene, trackPlaybackActivity]);

  useEffect(() => {
    if (scene) document.title = `${scene.title || scene.files?.[0]?.basename || `Scene ${id}`} | Cove`;
    return () => { document.title = "Cove"; };
  }, [scene, id]);

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

  // Apply CSS filters to video element when videoFilters change
  useEffect(() => {
    const video = document.querySelector('video');
    if (video) {
      const { brightness, contrast, saturation, hue } = videoFilters;
      video.style.filter = `brightness(${brightness}%) contrast(${contrast}%) saturate(${saturation}%) hue-rotate(${hue}deg)`;
    }
    return () => {
      const video = document.querySelector('video');
      if (video) video.style.filter = '';
    };
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

  const uploadCoverImageMut = useMutation({
    mutationFn: (file: File) => entityImages.uploadSceneCoverImage(id, file),
    onSuccess: invalidateSceneCover,
  });

  const resetCoverImageMut = useMutation({
    mutationFn: () => entityImages.deleteSceneCoverImage(id),
    onSuccess: invalidateSceneCover,
  });

  const coverActionPending = setCoverFromCurrentFrameMut.isPending || uploadCoverImageMut.isPending || resetCoverImageMut.isPending;

  const handleSetCoverFromCurrentFrame = () => {
    setCoverFromCurrentFrameMut.mutate(videoTime);
  };

  const handleResetCoverToDefault = () => {
    resetCoverImageMut.mutate();
  };

  const handleCoverFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (file) {
      uploadCoverImageMut.mutate(file);
    }
    event.target.value = "";
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

    return Array.from(ids);
  }, [detections]);

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

  const tabs = filterItemsByPermission([
    { key: "details", label: "Details" },
    { key: "segments", label: `Segments${segments.length ? ` (${segments.length})` : ""}` },
    { key: "similar", label: "Similar" },
    { key: "filters", label: "Filters" },
    { key: "file-info", label: `File Info${scene?.files.length && scene.files.length > 1 ? ` (${scene.files.length})` : ""}` },
    { key: "history", label: "History" },
    ...getTabsForPage("scene").map((t) => ({ key: `ext:${t.key}` as TabKey, label: t.label })),
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

  const sceneKeyboardShortcuts = useMemo(() => [
    { key: ",", description: "Toggle theater mode", handler: () => setTheaterMode(!theaterMode) },
    { key: "a", description: "Open details tab", handler: () => setActiveTab("details") },
    { key: "e", description: "Open edit tab", handler: () => canWriteScene && setActiveTab("edit") },
    { key: "s", description: "Open segments tab", handler: () => canReadSegments && setActiveTab("segments") },
    { key: "i", description: "Open file info tab", handler: () => canReadFiles && setActiveTab("file-info") },
    { key: "h", description: "Open history tab", handler: () => setActiveTab("history") },
    { key: "o", description: "Toggle favorite", handler: () => scene && canEngageScene && setSceneFavorite(!sceneFavorite) },
    { key: "[", description: "Open previous scene", handler: () => hasPrev && prevId != null && onNavigate({ page: "scene", id: prevId }) },
    { key: "]", description: "Open next scene", handler: () => hasNext && nextId != null && onNavigate({ page: "scene", id: nextId }) },
  ], [canEngageScene, canReadFiles, canReadSegments, canWriteScene, hasNext, hasPrev, nextId, onNavigate, prevId, scene, sceneFavorite, theaterMode, setSceneFavorite]);

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
        className="max-h-[5rem] max-w-full object-contain"
        onError={(event) => { (event.target as HTMLImageElement).style.display = "none"; }}
      />
    </button>
  ) : null;

  const sceneSubtitle = (
    <div className="flex flex-wrap items-start gap-4 text-sm text-secondary">
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        {scene.date ? (
          <span>
            {new Date(`${scene.date}T00:00:00`).toLocaleDateString(undefined, {
              year: "numeric",
              month: "long",
              day: "numeric",
            })}
          </span>
        ) : null}

        <div className="flex flex-wrap items-center gap-2">
          {scene.studioName && scene.studioId ? (
            <button
              type="button"
              onClick={() => onNavigate({ page: "studio", id: scene.studioId })}
              className="font-medium text-accent hover:underline"
            >
              {scene.studioName}
            </button>
          ) : null}
          {file && file.frameRate > 0 ? <span>{file.frameRate.toFixed(0)} fps</span> : null}
          {file && resLabel ? <span className="font-semibold text-accent">{resLabel}</span> : null}
          {scene.code ? <span>Code {scene.code}</span> : null}
          {scene.director ? (
            <button
              type="button"
              onClick={() => onNavigate({ page: "scenes", query: scene.director })}
              className="hover:text-foreground"
            >
              Director {scene.director}
            </button>
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

      <div className="relative" ref={opsMenuRef}>
        <button
          type="button"
          onClick={() => setShowOpsMenu(!showOpsMenu)}
          className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
          title="Operations"
        >
          <MoreVertical className="h-4 w-4" />
        </button>
        {showOpsMenu && (
          <div className="absolute right-0 top-full mt-1 z-50 min-w-[220px] rounded-2xl border border-border bg-card py-1 shadow-lg">
            {canWriteScene ? <button onClick={() => { setActiveTab("edit"); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Pencil className="h-3.5 w-3.5" /> Edit</button> : null}
            {!file && canDownloadScene ? (
              <button onClick={() => { setShowDownloadDialog(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Download className="h-3.5 w-3.5" /> Download Media…</button>
            ) : null}
            {file && canLibraryScan ? (
              <button onClick={() => { rescanMut.mutate(); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><RefreshCw className="h-3.5 w-3.5" /> Rescan</button>
            ) : null}
            {canScrapeScene ? <button onClick={() => { setShowScrapeDialog(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><ExternalLink className="h-3.5 w-3.5" /> Scrape…</button> : null}
            {canIdentifyScene ? <button onClick={() => { setShowIdentify(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Search className="h-3.5 w-3.5" /> Identify…</button> : null}
            {canGenerateScene || canWriteScene ? <div className="my-1 border-t border-border" /> : null}
            <ExtensionEntityActions entityType="scene" entityId={scene.id} renderMode="menu" onInvoked={() => setShowOpsMenu(false)} />
            {canGenerateScene ? <button onClick={() => { setShowGenerate(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Clapperboard className="h-3.5 w-3.5" /> Generate…</button> : null}
            {canWriteScene ? <button onClick={() => { handleSetCoverFromCurrentFrame(); setShowOpsMenu(false); }} disabled={coverActionPending || !file} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface disabled:opacity-60"><Camera className="h-3.5 w-3.5" /> Set Cover from Current Frame</button> : null}
            {canWriteScene ? <button onClick={() => { coverFileInputRef.current?.click(); setShowOpsMenu(false); }} disabled={coverActionPending} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface disabled:opacity-60"><Upload className="h-3.5 w-3.5" /> Upload Cover Image…</button> : null}
            {canWriteScene ? <button onClick={() => { handleResetCoverToDefault(); setShowOpsMenu(false); }} disabled={coverActionPending} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface disabled:opacity-60"><Image className="h-3.5 w-3.5" /> Use Default Cover</button> : null}
            {canWriteScene ? <div className="my-1 border-t border-border" /> : null}
            {canWriteScene ? <button onClick={() => { setShowMerge(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Merge className="h-3.5 w-3.5" /> Merge…</button> : null}
            <button onClick={() => { setTheaterMode(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Monitor className="h-3.5 w-3.5" /> Theater Mode</button>
            {canDeleteScene ? <div className="my-1 border-t border-border" /> : null}
            {canDeleteScene ? <button onClick={() => { setConfirmDelete(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-red-400 hover:bg-surface"><Trash2 className="h-3.5 w-3.5" /> Delete</button> : null}
          </div>
        )}
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
      />
    </div>
  ) : activeTab === "similar" ? (
    <SceneVisualSimilarityPanel sceneId={scene.id} onNavigate={onNavigate} />
  ) : activeTab === "filters" ? (
    <VideoFiltersTab filters={videoFilters} onChange={setVideoFilters} />
  ) : activeTab === "file-info" && scene.files.length > 0 ? (
    <FileInfoTab files={scene.files} />
  ) : activeTab === "history" ? (
    <HistoryTab
      scene={scene}
      playCount={scenePlayCount}
      playDuration={scenePlayDuration}
      favorite={sceneFavorite}
      favoritePending={sceneFavoritePending}
      setFavorite={setSceneFavorite}
      likeCount={sceneLikeCount}
      canEngageScene={canEngageScene}
    />
  ) : activeTab === "edit" ? (
    <SceneEditPanel scene={scene} onSaved={() => setActiveTab("details")} />
  ) : activeTab.startsWith("ext:") ? (() => {
    const extTabKey = activeTab.replace("ext:", "");
    const extTab = getTabsForPage("scene").find((tab) => tab.key === extTabKey);
    if (!extTab) return null;
    const Component = resolveExtComponent(extTab.componentName);
    if (!Component) return <div className="p-4 text-muted">Extension component not found: {extTab.componentName}</div>;
    return <Component entityId={id} />;
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
            captions={file.captions}
            onSeekRegister={(fn) => { seekRef.current = fn; }}
            onTimeUpdate={setVideoTime}
            autostart={config?.ui.autostartVideo}
            showAbLoop={config?.ui.showAbLoopControls}
            trackingEnabled={trackPlaybackActivity}
            onEnded={() => { if (hasNext && nextId != null) onNavigate({ page: "scene", id: nextId }); }}
            onPrev={hasPrev && prevId != null ? () => onNavigate({ page: "scene", id: prevId }) : undefined}
            onNext={hasNext && nextId != null ? () => onNavigate({ page: "scene", id: nextId }) : undefined}
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
          onSeek={(time) => seekRef.current?.(time)}
          currentTime={videoTime}
          profileName={activeProfileName}
        />
      ) : null}
    </div>
  );

  return (
    <>
      <input
        ref={coverFileInputRef}
        type="file"
        accept="image/jpeg,image/png,image/webp,image/gif"
        className="hidden"
        onChange={handleCoverFileChange}
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
          <SceneScrapeDialog
            open={showScrapeDialog}
            scene={scene}
            onClose={() => setShowScrapeDialog(false)}
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
        title={sceneTitle}
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
          additionalMetrics: [
            {
              label: "Plays",
              value: scenePlayCount,
              icon: <Eye className="h-4 w-4" />,
            },
            {
              label: "Likes",
              value: sceneLikeCount,
              icon: <ThumbsUp className={["h-4 w-4", sceneLikeCount > 0 ? "fill-accent text-accent" : ""].join(" ")} />,
              title: "Add like",
              onClick: canEngageScene ? () => incrementLikeMut.mutate() : undefined,
              active: sceneLikeCount > 0,
            },
            {
              label: "Derived Likes",
              value: sceneDerivedLikeCount,
              icon: <ThumbsUp className={["h-4 w-4", sceneDerivedLikeCount > 0 ? "text-accent" : ""].join(" ")} />,
              title: "Derived likes",
              active: sceneDerivedLikeCount > 0,
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
        theaterModeSupported
        isTheaterMode={theaterMode}
        onTheaterModeToggle={setTheaterMode}
        actions={sceneActions}
      >
        <MediaDetailLayout.Content>
          {activeTabContent}
          {activeTab === "details" ? (
            <AspectRatingsPanel hostType="scene" hostId={id} canRate={canEngageScene} />
          ) : null}
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
            <dd className="text-foreground">{scene.code}</dd>
          </>
        )}
        {scene.director && (
          <>
            <dt className="text-muted pr-3">Director</dt>
            <dd>
              <button onClick={() => onNavigate({ page: "scenes", query: scene.director })} className="text-accent hover:underline">
                {scene.director}
              </button>
            </dd>
          </>
        )}
      </dl>

      {/* Details / Description */}
      {scene.details && (
        <div>
          <p className="text-sm text-foreground whitespace-pre-wrap">{scene.details}</p>
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
                provenance={tag.provenance}
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
        </div>
      )}

      {scene.groups.length > 0 && (
        <div>
          <h6 className="mb-2 text-sm text-muted">Groups</h6>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {scene.groups.map((group) => {
              const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "group", id: group.id }, () => onNavigate({ page: "group", id: group.id }));

              return (
                <a
                  key={group.id}
                  {...linkProps}
                  className="rounded-xl border border-border bg-card p-4 text-left transition-colors hover:border-accent/60"
                >
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <div className="text-sm font-medium text-foreground">{group.name}</div>
                      <div className="mt-1 text-xs text-secondary">Scene #{group.sceneIndex}</div>
                    </div>
                    <Layers className="h-5 w-5 text-muted" />
                  </div>
                </a>
              );
            })}
          </div>
        </div>
      )}

      {scene.galleries.length > 0 && (
        <div>
          <h6 className="mb-2 text-sm text-muted">Galleries</h6>
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {scene.galleries.map((gallery) => {
              const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "gallery", id: gallery.id }, () => onNavigate({ page: "gallery", id: gallery.id }));

              return (
                <a
                  key={gallery.id}
                  {...linkProps}
                  className="group overflow-hidden rounded-xl border border-border bg-card text-left transition-colors hover:border-accent/60"
                >
                  <div className="flex aspect-video items-center justify-center bg-gradient-to-br from-surface to-card">
                    <FolderOpen className="h-10 w-10 text-muted" />
                  </div>
                  <div className="p-3">
                    <p className="truncate text-sm font-medium text-foreground group-hover:text-accent">
                      {gallery.title || "Untitled"}
                    </p>
                    {gallery.date && (
                      <p className="mt-1 text-xs text-secondary">{formatDate(gallery.date)}</p>
                    )}
                  </div>
                </a>
              );
            })}
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
        </div>
      )}

      {/* Remote IDs */}
      {scene.remoteIds && scene.remoteIds.length > 0 && (
        <div>
          <h6 className="text-sm text-muted mb-2">Remote IDs</h6>
          <dl className="grid gap-y-1 text-sm" style={{ gridTemplateColumns: "auto 1fr" }}>
            {scene.remoteIds.map((sid, i) => (
              <Fragment key={i}>
                <dt className="text-muted pr-3 truncate">{sid.endpoint}</dt>
                <dd className="text-foreground font-mono text-xs break-all">{sid.remoteId}</dd>
              </Fragment>
            ))}
          </dl>
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
  favorite,
  favoritePending,
  setFavorite,
  likeCount,
  canEngageScene,
}: {
  scene: Scene;
  playCount: number;
  playDuration: number;
  favorite: boolean;
  favoritePending: boolean;
  setFavorite: (isFavorite: boolean) => void;
  likeCount: number;
  canEngageScene: boolean;
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
  const interactionLabel = (kind: string) => {
    switch (kind) {
      case "pause": return "Paused";
      case "seek": return "Seeked";
      case "likeCount": return "Liked";
      case "pageVisit": return "Page visit";
      case "derivedLike": return "Derived like";
      case "hide": return "Backgrounded";
      case "share": return "Shared";
      default: return kind;
    }
  };
  const timelineEvents = history?.events ?? [];
  const totalNonDistinctWatchedSec = (history as { totalNonDistinctWatchedSec?: number } | undefined)?.totalNonDistinctWatchedSec;

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

      {/* Favorite (boolean state) */}
      <section>
        <div className="flex items-center justify-between mb-2">
          <h3 className="text-sm font-semibold text-muted uppercase tracking-wide">Favorite</h3>
          {canEngageScene ? (
            <button
              onClick={() => setFavorite(!favorite)}
              disabled={favoritePending}
              className="flex items-center gap-1.5 rounded border border-border bg-card px-2.5 py-1 text-xs text-secondary hover:border-accent/50 hover:text-accent disabled:cursor-not-allowed disabled:opacity-60"
              title={favorite ? "Remove from favorites" : "Add to favorites"}
            >
              <Heart className={`w-3.5 h-3.5 ${favorite ? "fill-accent text-accent" : ""}`} />
              <span>{favorite ? "Favorited" : "Favorite"}</span>
            </button>
          ) : (
            <span className="flex items-center gap-1.5 rounded border border-border bg-card px-2.5 py-1 text-xs text-secondary">
              <Heart className={`w-3.5 h-3.5 ${favorite ? "fill-accent text-accent" : ""}`} />
              <span>{favorite ? "Favorited" : "Favorite"}</span>
            </span>
          )}
        </div>
        <div className="mb-2">
          <span className="text-muted">Current state:</span> <span className="text-foreground">{favorite ? "Favorited" : "Not favorited"}</span>
        </div>
      </section>

      <section>
        <div className="flex items-center justify-between mb-2">
          <h3 className="text-sm font-semibold text-muted uppercase tracking-wide">Likes</h3>
          <div className="flex items-center gap-1.5 rounded border border-border bg-card px-2.5 py-1 text-xs text-secondary">
            <ThumbsUp className={`w-3.5 h-3.5 ${likeCount > 0 ? "fill-accent text-accent" : ""}`} />
            <span>{likeCount}</span>
          </div>
        </div>
        <div className="mb-2">
          <span className="text-muted">Count:</span> <span className="text-foreground">{likeCount}</span>
        </div>
        {history?.likeHistory && history.likeHistory.length > 0 && (
          <div className="max-h-40 overflow-y-auto space-y-0.5 border-t border-border pt-2">
            {history.likeHistory.map((date, i) => (
              <div key={i} className="text-xs text-secondary">{new Date(date).toLocaleString()}</div>
            ))}
          </div>
        )}
      </section>

      <section>
        <div className="mb-2 flex items-center justify-between">
          <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Watched Sections</h3>
          <span className="text-xs text-secondary">{history?.allTimeWatchedIntervals?.length ?? 0} intervals</span>
        </div>
        {history?.allTimeWatchedIntervals && history.allTimeWatchedIntervals.length > 0 ? (
          <>
            {totalNonDistinctWatchedSec != null && (
              <div className="mb-2 text-xs text-secondary">Total watched: {formatDuration(totalNonDistinctWatchedSec)}</div>
            )}
            {history.totalDistinctWatchedSec != null && (
              <div className="mb-2 text-xs text-secondary">Total distinct: {formatDuration(history.totalDistinctWatchedSec)}</div>
            )}
          <div className="space-y-2 border-t border-border pt-3">
            {history.allTimeWatchedIntervals.map((range, index) => (
              <div key={`${range.startSec}-${range.endSec}-${index}`} className="rounded-lg border border-border/70 bg-surface/35 px-3 py-2">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="text-xs font-medium uppercase tracking-wide text-foreground">
                      {formatDuration(range.startSec)} to {formatDuration(range.endSec)}
                    </div>
                    <div className="mt-1 text-xs text-secondary">
                      Span {formatDuration(Math.max(0, range.endSec - range.startSec))}
                    </div>
                  </div>
                  <div className="shrink-0 text-xs text-secondary">{new Date(range.recordedAt).toLocaleString()}</div>
                </div>
              </div>
            ))}
          </div>
          </>
        ) : (
          <div className="border-t border-border pt-3 text-xs text-secondary">No watched intervals recorded yet.</div>
        )}
      </section>

      {history?.sessions && history.sessions.length > 0 && (
        <section>
          <div className="mb-2 flex items-center justify-between">
            <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Playback Sessions</h3>
            <span className="text-xs text-secondary">{history.sessions.length} sessions</span>
          </div>
          <div className="space-y-3 border-t border-border pt-3">
            {history.sessions.map((session) => (
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

      {timelineEvents.length > 0 && (
        <section>
          <div className="mb-2 flex items-center justify-between">
            <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Interaction Timeline</h3>
            <span className="text-xs text-secondary">{timelineEvents.length} events</span>
          </div>
          <div className="max-h-72 space-y-2 overflow-y-auto border-t border-border pt-3">
            {timelineEvents.map((event, index) => (
              <div key={`${event.at}-${event.kind}-${index}`} className="rounded-lg border border-border/70 bg-surface/35 px-3 py-2">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="text-xs font-medium uppercase tracking-wide text-foreground">{interactionLabel(event.kind)}</div>
                  </div>
                  <div className="shrink-0 text-xs text-secondary">{new Date(event.at).toLocaleString()}</div>
                </div>
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

// Scene Scrubber / Timeline Component
function SceneScrubber({ 
  sceneId, 
  duration, 
  spans,
  rawSegments,
  detections,
  faces,
  onSeek,
  currentTime,
  profileName,
}: { 
  sceneId: number; 
  duration: number; 
  spans: Pick<ResolvedSpan, "spanKey" | "startSec" | "endSec" | "tagName" | "kind" | "colorHint" | "sourceKey" | "lane">[];
  rawSegments: Pick<Segment, "id" | "startSec" | "endSec" | "title" | "kind" | "sourceKey">[];
  detections: Pick<Detection, "id" | "observedAtSec" | "class" | "score" | "refKind" | "refId">[];
  faces?: Pick<Face, "id" | "label" | "performerName" | "performerId">[];
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
  const screenshotUrl = `/api/stream/scene/${sceneId}/screenshot`;
  const [showAllResolvedLanes, setShowAllResolvedLanes] = useState(false);
  const [showAllFaceLanes, setShowAllFaceLanes] = useState(false);
  const [resolvedCollapsed, setResolvedCollapsed] = usePersistedFlag("cove.timeline.resolvedCollapsed", false);
  const [facesCollapsed, setFacesCollapsed] = usePersistedFlag("cove.timeline.facesCollapsed", true);
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

  const thumbCount = spriteData ? spriteData.entries.length : Math.min(Math.ceil(duration / 10), 60);
  const thumbWidth = 160;
  const thumbHeight = spriteData?.entries[0] ? Math.round(thumbWidth * (spriteData.entries[0].h / spriteData.entries[0].w)) : 90;
  const segmentLanes = useMemo(() => buildTimelineLanes(
    spans.map((span) => ({
      key: span.spanKey,
      startSec: span.startSec,
      endSec: span.endSec,
      label: span.tagName || span.kind || span.sourceKey || "Segment",
      colorHint: span.colorHint,
    })),
  ), [spans]);
  const faceLanes = useMemo(() => {
    if (!facesEnabled) return [] as ReturnType<typeof buildTimelineLanes<{ key: string; startSec: number; endSec: number; label: string; faceId: number }>>;
    const facesById = new Map<number, Pick<Face, "id" | "label" | "performerName" | "performerId">>();
    for (const face of faces ?? []) facesById.set(face.id, face);

    // Group detection observations per face id and merge close detections into
    // continuous appearance windows.
    const buckets = new Map<number, number[]>();
    for (const det of detections) {
      if (det.refId == null || det.refKind?.toLowerCase() !== "face") continue;
      if (det.observedAtSec == null) continue;
      const arr = buckets.get(det.refId) ?? [];
      arr.push(det.observedAtSec);
      buckets.set(det.refId, arr);
    }

    // Fallback to face-tagged raw segments when no detections are available
    // for that face id (e.g. legacy data).
    if (buckets.size === 0) {
      for (const segment of rawSegments) {
        if (!isFaceTimelineSegment(segment)) continue;
        const fakeId = -Math.abs(segment.id);
        const arr = buckets.get(fakeId) ?? [];
        arr.push(segment.startSec);
        if (segment.endSec != null) arr.push(segment.endSec);
        buckets.set(fakeId, arr);
      }
    }

    const MERGE_GAP_SEC = 2.5;
    const items: { key: string; startSec: number; endSec: number; label: string; faceId: number }[] = [];
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
          faceId,
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
    // Fallback: evenly-spaced thumbs
    const interval = duration / thumbCount;
    return Math.min(Math.floor(currentTime / interval), thumbCount - 1);
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
  
  return (
    <div className="flex-shrink-0 bg-[#1a1a1a] border-t border-border">
      {spans.length > 0 && (
        <div className="border-b border-black/20 bg-[#26222d]">
          <div className="flex items-center justify-between gap-3 border-b border-black/20 bg-[#211d27] px-2 py-1.5 pr-8 text-[10px] uppercase tracking-[0.16em] text-white/55">
            <button
              type="button"
              onClick={() => setResolvedCollapsed((value) => !value)}
              className="flex flex-1 items-center gap-1.5 text-left hover:text-white/80"
              title={resolvedCollapsed ? "Expand resolved spans" : "Collapse resolved spans"}
            >
              <ChevronDown className={`h-3 w-3 transition-transform ${resolvedCollapsed ? "-rotate-90" : ""}`} />
              <span>Resolved spans · {profileName ?? "Resolved"} · {segmentLanes.length} lane{segmentLanes.length === 1 ? "" : "s"}</span>
            </button>
            {!resolvedCollapsed && segmentLanes.length > 4 ? (
              <button
                type="button"
                onClick={() => setShowAllResolvedLanes((value) => !value)}
                className="shrink-0 rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
              >
                {showAllResolvedLanes ? "Collapse" : `Show all ${segmentLanes.length}`}
              </button>
            ) : null}
          </div>
          {!resolvedCollapsed ? (
            segmentLanes.length > 0 ? (
              <div className="relative" style={{ height: `${Math.max(26, visibleResolvedLanes.length * 22 + 6)}px` }}>
                {visibleResolvedLanes.map((lane, laneIndex) => lane.map(({ item, endSec }) => {
                  const start = clampPercent((item.startSec / duration) * 100);
                  const end = clampPercent(((endSec + 0.001) / duration) * 100);
                  const width = Math.max(0.4, end - start);

                  return (
                    <button
                      key={item.key}
                      className="absolute h-[18px] overflow-hidden rounded px-1 text-left text-[10px] font-medium text-white hover:brightness-110"
                      style={{
                        left: `${start}%`,
                        top: `${laneIndex * 22 + 4}px`,
                        width: `${width}%`,
                        backgroundColor: item.colorHint || "rgba(217, 119, 6, 0.8)",
                      }}
                      title={`${item.label} (${formatTimelineTime(item.startSec)} - ${formatTimelineTime(endSec)})`}
                      onClick={() => onSeek?.(item.startSec)}
                    >
                      {width > 10 ? item.label : ""}
                    </button>
                  );
                }))}
              </div>
            ) : (
              <div className="px-2 py-2 text-[11px] text-white/55">No resolved spans are available for the current profile.</div>
            )
          ) : null}
          {!resolvedCollapsed && hiddenResolvedLaneCount > 0 ? (
            <div className="border-t border-black/20 px-2 py-1 text-[10px] text-white/45">
              {hiddenResolvedLaneCount} additional lane{hiddenResolvedLaneCount === 1 ? "" : "s"} hidden until expanded.
            </div>
          ) : null}
        </div>
      )}
      {hasFaceDetections && (
        <div className="border-b border-black/20 bg-[#1c2f28]">
          <div className="flex items-center justify-between gap-3 border-b border-black/20 bg-[#16261f] px-2 py-1.5 pr-8 text-[10px] uppercase tracking-[0.16em] text-white/55">
            <button
              type="button"
              onClick={() => facesEnabled && setFacesCollapsed((value) => !value)}
              className="flex flex-1 items-center gap-1.5 text-left hover:text-white/80"
              title={!facesEnabled ? "Faces over time are hidden" : facesCollapsed ? "Expand faces over time" : "Collapse faces over time"}
            >
              <ChevronDown className={`h-3 w-3 transition-transform ${(!facesEnabled || facesCollapsed) ? "-rotate-90" : ""}`} />
              <span>Faces over time{facesEnabled ? ` · ${faceLanes.length} lane${faceLanes.length === 1 ? "" : "s"}` : " · hidden"}</span>
            </button>
            <button
              type="button"
              onClick={() => { setFacesEnabled((value) => !value); if (!facesEnabled) setFacesCollapsed(false); }}
              className="shrink-0 inline-flex items-center gap-1 rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
              title={facesEnabled ? "Hide face appearance bars" : "Show face appearance bars"}
            >
              {facesEnabled ? <Eye className="h-3 w-3" /> : <EyeOff className="h-3 w-3" />}
              {facesEnabled ? "Hide faces" : "Show faces"}
            </button>
            {facesEnabled && !facesCollapsed && faceLanes.length > 2 ? (
              <button
                type="button"
                onClick={() => setShowAllFaceLanes((value) => !value)}
                className="shrink-0 rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
              >
                {showAllFaceLanes ? "Collapse" : `Show all ${faceLanes.length}`}
              </button>
            ) : null}
          </div>
          {facesEnabled && !facesCollapsed ? (
            <>
              <div className="relative" style={{ height: `${Math.max(26, visibleFaceLanes.length * 22 + 6)}px` }}>
                {visibleFaceLanes.map((lane, laneIndex) => lane.map(({ item, endSec }) => {
                  const start = clampPercent((item.startSec / duration) * 100);
                  const end = clampPercent(((endSec + 0.001) / duration) * 100);
                  const width = Math.max(0.4, end - start);

                  return (
                    <button
                      key={item.key}
                      className="absolute h-[18px] overflow-hidden rounded px-1 text-left text-[10px] font-medium text-white hover:brightness-110"
                      style={{
                        left: `${start}%`,
                        top: `${laneIndex * 22 + 4}px`,
                        width: `${width}%`,
                        backgroundColor: "rgba(34, 197, 94, 0.78)",
                      }}
                      title={`${item.label} (${formatTimelineTime(item.startSec)} - ${formatTimelineTime(endSec)})`}
                      onClick={() => onSeek?.(item.startSec)}
                    >
                      {width > 8 ? item.label : ""}
                    </button>
                  );
                }))}
              </div>
              {hiddenFaceLaneCount > 0 ? (
                <div className="border-t border-black/20 px-2 py-1 text-[10px] text-white/45">
                  {hiddenFaceLaneCount} additional face lane{hiddenFaceLaneCount === 1 ? "" : "s"} hidden until expanded.
                </div>
              ) : null}
            </>
          ) : null}
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
                style={{ left: `${clampPercent((time / duration) * 100)}%` }}
                title={`${detection.class} (${Math.round(detection.score * 100)}%) at ${formatTimelineTime(time)}${detection.refKind && detection.refId != null ? ` • ${detection.refKind} #${detection.refId}` : ""}`}
                onClick={() => onSeek?.(time)}
              />
            );
          })}
        </div>
      )}

      {/* Thumbnails scrubber - uses sprite sheet if available, falls back to individual screenshots */}
      <div className="relative flex overflow-hidden" ref={containerRef}>
        <button onClick={() => scroll(-1)} className="flex-shrink-0 w-7 bg-[#222] hover:bg-[#333] text-muted border-r border-border z-10">
          <ChevronLeft className="w-4 h-4 mx-auto" />
        </button>
        
        <div ref={scrollRef} className="flex-1 flex overflow-x-auto scrollbar-thin scrollbar-thumb-border">
          {Array.from({ length: Math.max(thumbCount, 1) }).map((_, i) => {
            const time = spriteData ? spriteData.entries[i]?.start ?? (i / thumbCount) * duration : (i / thumbCount) * duration;
            const entry = spriteData?.entries[i];
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
                  ) : spriteLoadSettled && spriteError ? (
                    <img 
                      src={`${screenshotUrl}?seconds=${Math.floor(time)}`} 
                      alt="" 
                      className="w-full h-full object-cover"
                      loading="lazy"
                      onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }}
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

function SegmentsPanel({
  sceneId,
  segments,
  loading,
  canEdit,
  onSeek,
}: {
  sceneId: number;
  segments: Segment[];
  loading: boolean;
  canEdit: boolean;
  onSeek?: (time: number) => void;
}) {
  const queryClient = useQueryClient();
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [title, setTitle] = useState("");
  const [kind, setKind] = useState("");
  const [startSec, setStartSec] = useState(0);
  const [endSec, setEndSec] = useState<number | "">("");
  const [tagSearch, setTagSearch] = useState("");
  const [selectedTagId, setSelectedTagId] = useState<number | null>(null);
  const [selectedTagName, setSelectedTagName] = useState("");

  const { data: tagResults } = useQuery({
    queryKey: ["tags-search", tagSearch],
    queryFn: () => tags.find({ q: tagSearch, perPage: 10 }),
    enabled: tagSearch.length >= 1,
  });

  const createMutation = useMutation({
    mutationFn: (data: { title?: string; kind?: string; startSec: number; endSec?: number; tagId?: number }) =>
      scenes.segments.create(sceneId, {
        startSec: data.startSec,
        endSec: data.endSec,
        tagId: data.tagId,
        kind: data.kind,
        title: data.title,
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["scene", sceneId, "segments"] });
      resetForm();
    },
  });

  const updateMutation = useMutation({
    mutationFn: (segment: Segment) =>
      scenes.segments.update(sceneId, segment.id, {
        startSec,
        endSec: endSec === "" ? undefined : endSec,
        tagId: selectedTagId ?? undefined,
        kind: kind || undefined,
        refId: segment.refId,
        payload: segment.payload,
        sourceKey: segment.sourceKey || "user",
        sourceRunId: segment.sourceRunId,
        confidence: segment.confidence,
        title: title || undefined,
        colorHint: segment.colorHint,
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
    setKind("");
    setStartSec(0);
    setEndSec("");
    setTagSearch("");
    setSelectedTagId(null);
    setSelectedTagName("");
  };

  const startEdit = (segment: Segment) => {
    setAdding(true);
    setEditingId(segment.id);
    setTitle(segment.title || "");
    setKind(segment.kind || "");
    setStartSec(segment.startSec);
    setEndSec(segment.endSec ?? "");
    setTagSearch("");
    setSelectedTagId(segment.tagId ?? null);
    setSelectedTagName(segment.tagName || "");
  };

  const editingSegment = editingId != null ? segments.find((segment) => segment.id === editingId) ?? null : null;

  const saveSegment = () => {
    if (editingSegment) {
      updateMutation.mutate(editingSegment);
      return;
    }

    createMutation.mutate({
      title: title || undefined,
      kind: kind || undefined,
      startSec,
      endSec: endSec === "" ? undefined : endSec,
      tagId: selectedTagId ?? undefined,
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
          <div className="grid gap-2 sm:grid-cols-2">
            <input
              type="text"
              placeholder="Segment title"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              className="w-full rounded border border-border bg-input px-3 py-1.5 text-sm text-foreground"
            />
            <input
              type="text"
              placeholder="Kind (intro, pose, action...)"
              value={kind}
              onChange={(event) => setKind(event.target.value)}
              className="w-full rounded border border-border bg-input px-3 py-1.5 text-sm text-foreground"
            />
          </div>
          <div className="grid gap-2 sm:grid-cols-[120px_120px_minmax(0,1fr)]">
            <input
              type="number"
              step="0.1"
              min={0}
              placeholder="Start"
              value={startSec}
              onChange={(event) => setStartSec(Number(event.target.value))}
              className="rounded border border-border bg-input px-3 py-1.5 text-sm text-foreground"
            />
            <input
              type="number"
              step="0.1"
              min={0}
              placeholder="End"
              value={endSec}
              onChange={(event) => setEndSec(event.target.value === "" ? "" : Number(event.target.value))}
              className="rounded border border-border bg-input px-3 py-1.5 text-sm text-foreground"
            />
            <div className="relative">
              <div className="flex items-center rounded border border-border bg-input px-3 py-1.5 text-sm">
                <Search className="mr-2 h-3.5 w-3.5 flex-shrink-0 text-muted" />
                <input
                  type="text"
                  placeholder={selectedTagName || "Search tag..."}
                  value={tagSearch}
                  onChange={(event) => { setTagSearch(event.target.value); setSelectedTagId(null); setSelectedTagName(""); }}
                  className="w-full bg-transparent text-foreground outline-none"
                />
              </div>
              {tagSearch && tagResults && tagResults.items.length > 0 && (
                <div className="absolute z-10 mt-1 max-h-40 w-full overflow-y-auto rounded border border-border bg-card shadow-lg">
                  {tagResults.items.map((tag: { id: number; name: string }) => (
                    <button
                      key={tag.id}
                      onClick={() => { setSelectedTagId(tag.id); setSelectedTagName(tag.name); setTagSearch(""); }}
                      className="block w-full px-3 py-1.5 text-left text-sm text-secondary hover:bg-card-hover hover:text-foreground"
                    >
                      {tag.name}
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
          <div className="flex justify-end gap-2">
            <button onClick={resetForm} className="px-3 py-1 text-sm text-secondary hover:text-foreground">Cancel</button>
            <button
              onClick={saveSegment}
              disabled={createMutation.isPending || updateMutation.isPending}
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
  const [title, setTitle] = useState(scene.title || "");
  const [code, setCode] = useState(scene.code || "");
  const [details, setDetails] = useState(scene.details || "");
  const [director, setDirector] = useState(scene.director || "");
  const [date, setDate] = useState(scene.date || "");
  const [rating, setRating] = useState<number | undefined>(undefined);
  const [urls, setUrls] = useState(scene.urls.length > 0 ? scene.urls : [""]);
  const [studioId, setStudioId] = useState<number | undefined>(scene.studioId ?? undefined);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(scene.tags.map((t) => t.id));
  const [selectedPerformerIds, setSelectedPerformerIds] = useState<number[]>(scene.performers.map((p) => p.id));
  const [selectedGalleryIds, setSelectedGalleryIds] = useState<number[]>(scene.galleries.map((g) => g.id));
  const [selectedGroups, setSelectedGroups] = useState<{ groupId: number; sceneIndex: number }[]>(
    scene.groups.map((g) => ({ groupId: g.id, sceneIndex: g.sceneIndex }))
  );
  const [contextTagIdsByPerformer, setContextTagIdsByPerformer] = useState<Record<number, number[]>>(() => buildSceneEditPerformerContextTagIds(scene));
  const [contextTagSearchByPerformer, setContextTagSearchByPerformer] = useState<Record<number, string>>({});
  const [tagSearch, setTagSearch] = useState("");
  const [perfSearch, setPerfSearch] = useState("");
  const [gallerySearch, setGallerySearch] = useState("");
  const [groupSearch, setGroupSearch] = useState("");

  const { data: allTags } = useQuery({ queryKey: ["tags-all"], queryFn: () => tags.find({ perPage: 500, sort: "name", direction: "asc" }) });
  const { data: allPerformers } = useQuery({ queryKey: ["performers-all"], queryFn: () => performersApi.find({ perPage: 500, sort: "name", direction: "asc" }) });
  const { data: allGalleries } = useQuery({ queryKey: ["galleries-all"], queryFn: () => galleriesApi.find({ perPage: 500, sort: "title", direction: "asc" }) });
  const { data: allGroups } = useQuery({ queryKey: ["groups-all"], queryFn: () => groupsApi.find({ perPage: 500, sort: "name", direction: "asc" }) });

  useEffect(() => {
    setTitle(scene.title || ""); setCode(scene.code || ""); setDetails(scene.details || "");
    setDirector(scene.director || ""); setDate(scene.date || ""); setRating(undefined);
    setUrls(scene.urls.length > 0 ? scene.urls : [""]); setStudioId(scene.studioId ?? undefined);
    setSelectedTagIds(scene.tags.map((t) => t.id)); setSelectedPerformerIds(scene.performers.map((p) => p.id));
    setSelectedGalleryIds(scene.galleries.map((g) => g.id));
    setSelectedGroups(scene.groups.map((g) => ({ groupId: g.id, sceneIndex: g.sceneIndex })));
    setContextTagIdsByPerformer(buildSceneEditPerformerContextTagIds(scene));
    setContextTagSearchByPerformer({});
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
      director: director || undefined, date: date || undefined, rating, studioId,
      urls: urlList, tagIds: selectedTagIds, performerIds: selectedPerformerIds, galleryIds: selectedGalleryIds, groups: selectedGroups });
  };

  const filteredTags = filterTagsForSelector(allTags?.items ?? [], tagSearch, selectedTagIds);
  const filteredPerformers = allPerformers?.items.filter((p) => !selectedPerformerIds.includes(p.id) && p.name.toLowerCase().includes(perfSearch.toLowerCase())) ?? [];
  const filteredGalleries = allGalleries?.items.filter((g) => !selectedGalleryIds.includes(g.id) && (g.title || "").toLowerCase().includes(gallerySearch.toLowerCase())) ?? [];
  const selectedGroupIds = selectedGroups.map((g) => g.groupId);
  const filteredGroupsList = allGroups?.items.filter((g) => !selectedGroupIds.includes(g.id) && g.name.toLowerCase().includes(groupSearch.toLowerCase())) ?? [];
  const selectedTags = selectedTagIds
    .map((id) => allTags?.items.find((tag) => tag.id === id) ?? scene.tags.find((tag) => tag.id === id))
    .filter((tag): tag is NonNullable<typeof tag> => Boolean(tag));
  const selectedPerformers = selectedPerformerIds
    .map((id) => allPerformers?.items.find((performer) => performer.id === id) ?? scene.performers.find((performer) => performer.id === id))
    .filter((performer): performer is NonNullable<typeof performer> => Boolean(performer));
  const selectedGalleries = selectedGalleryIds
    .map((id) => allGalleries?.items.find((gallery) => gallery.id === id) ?? scene.galleries.find((gallery) => gallery.id === id))
    .filter((gallery): gallery is NonNullable<typeof gallery> => Boolean(gallery));
  const knownContextTags = (scene.contextTagApplications ?? []).map((application) => application.tag);
  const knownTags = [...(allTags?.items ?? []), ...scene.tags, ...knownContextTags];
  const tagById = new Map(knownTags.map((tag) => [tag.id, tag]));
  const setPerformerContextTagIds = (performerId: number, tagIds: number[]) => {
    setContextTagIdsByPerformer((current) => ({ ...current, [performerId]: Array.from(new Set(tagIds)) }));
  };

  const inputCls = "w-full bg-input border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent";

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <label className="space-y-1"><span className="text-xs text-secondary">Title</span><input value={title} onChange={(e) => setTitle(e.target.value)} className={inputCls} /></label>
        <label className="space-y-1"><span className="text-xs text-secondary">Date</span><input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inputCls} /></label>
      </div>
      <div className="grid grid-cols-2 gap-3">
        <label className="space-y-1"><span className="text-xs text-secondary">Studio Code</span><input value={code} onChange={(e) => setCode(e.target.value)} className={inputCls} /></label>
        <label className="space-y-1"><span className="text-xs text-secondary">Director</span><input value={director} onChange={(e) => setDirector(e.target.value)} className={inputCls} /></label>
      </div>
      <label className="block space-y-1"><span className="text-xs text-secondary">Details</span><textarea value={details} onChange={(e) => setDetails(e.target.value)} rows={3} className={inputCls} /></label>
      <div className="space-y-1">
        <span className="text-xs text-secondary">Studio</span>
        <StudioSelector value={studioId} onChange={setStudioId} placeholder="Search studios..." />
      </div>
      <div className="space-y-1"><span className="text-xs text-secondary">URLs</span><StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" /></div>

      {/* Tags */}
      <div className="space-y-1">
        <span className="text-xs text-secondary">Tags</span>
        <SelectedTagChips tags={selectedTags} onRemove={(tag) => setSelectedTagIds(selectedTagIds.filter((id) => id !== tag.id))} className="mb-1 flex flex-wrap gap-1" />
        <input value={tagSearch} onChange={(e) => setTagSearch(e.target.value)} placeholder="Search tags…" className={inputCls} />
        {tagSearch && filteredTags.length > 0 && <GroupedTagOptionList tags={filteredTags} maxItems={20} onSelect={(tag) => { setSelectedTagIds([...selectedTagIds, tag.id]); setTagSearch(""); }} />}
      </div>

      {/* Performers */}
      <div className="space-y-1">
        <span className="text-xs text-secondary">Performers</span>
        <div className="flex flex-wrap gap-1 mb-1">
          {selectedPerformers.map((p) => <span key={p.id} className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-accent/10 text-accent-hover">{p.name}<button onClick={() => setSelectedPerformerIds(selectedPerformerIds.filter((id) => id !== p.id))} className="hover:text-white">×</button></span>)}
        </div>
        <input value={perfSearch} onChange={(e) => setPerfSearch(e.target.value)} placeholder="Search performers…" className={inputCls} />
        {perfSearch && filteredPerformers.length > 0 && <div className="max-h-24 overflow-y-auto bg-surface rounded border border-border">{filteredPerformers.slice(0, 10).map((p) => <button key={p.id} onClick={() => { setSelectedPerformerIds([...selectedPerformerIds, p.id]); setPerfSearch(""); }} className="block w-full text-left px-3 py-1 text-sm text-foreground hover:bg-card">{p.name}{p.disambiguation ? ` (${p.disambiguation})` : ""}</button>)}</div>}
      </div>

      {selectedPerformers.length > 0 ? (
        <div className="space-y-2 rounded-lg border border-border bg-surface/40 p-3">
          <div className="text-xs font-medium uppercase tracking-wide text-secondary">Performer Occurrence Tags</div>
          {selectedPerformers.map((performer) => {
            const tagIds = contextTagIdsByPerformer[performer.id] ?? [];
            const search = contextTagSearchByPerformer[performer.id] ?? "";
            const selectedContextTags = tagIds.map((tagId) => tagById.get(tagId)).filter(Boolean) as SelectableTag[];
            const availableTags = filterTagsForSelector(allTags?.items ?? [], search, tagIds);

            return (
              <div key={performer.id} className="rounded-lg border border-border bg-card/70 p-3">
                <div className="mb-2 flex items-center justify-between gap-3">
                  <div className="min-w-0 text-sm font-medium text-foreground">{performer.name}</div>
                  <div className="text-xs text-muted">{tagIds.length} tag{tagIds.length === 1 ? "" : "s"}</div>
                </div>
                <SelectedTagChips
                  tags={selectedContextTags}
                  emptyText="No occurrence tags"
                  onRemove={(tag) => setPerformerContextTagIds(performer.id, tagIds.filter((tagId) => tagId !== tag.id))}
                  className="mb-2 flex flex-wrap gap-1.5"
                />
                <input
                  value={search}
                  onChange={(event) => setContextTagSearchByPerformer((current) => ({ ...current, [performer.id]: event.target.value }))}
                  placeholder="Search tags for this occurrence..."
                  className={inputCls}
                />
                {search.trim() && availableTags.length > 0 ? (
                  <div className="mt-1">
                    <GroupedTagOptionList
                      tags={availableTags}
                      selectedIds={tagIds}
                      maxItems={20}
                      onSelect={(tag) => {
                        setPerformerContextTagIds(performer.id, [...tagIds, tag.id]);
                        setContextTagSearchByPerformer((current) => ({ ...current, [performer.id]: "" }));
                      }}
                    />
                  </div>
                ) : null}
              </div>
            );
          })}
        </div>
      ) : null}

      {/* Galleries */}
      <div className="space-y-1">
        <span className="text-xs text-secondary">Galleries</span>
        <div className="flex flex-wrap gap-1 mb-1">
          {selectedGalleries.map((g) => <span key={g.id} className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-emerald-900 text-emerald-300">{g.title || "Untitled"}<button onClick={() => setSelectedGalleryIds(selectedGalleryIds.filter((id) => id !== g.id))} className="hover:text-white">×</button></span>)}
        </div>
        <input value={gallerySearch} onChange={(e) => setGallerySearch(e.target.value)} placeholder="Search galleries…" className={inputCls} />
        {gallerySearch && filteredGalleries.length > 0 && <div className="max-h-24 overflow-y-auto bg-surface rounded border border-border">{filteredGalleries.slice(0, 10).map((g) => <button key={g.id} onClick={() => { setSelectedGalleryIds([...selectedGalleryIds, g.id]); setGallerySearch(""); }} className="block w-full text-left px-3 py-1 text-sm text-foreground hover:bg-card">{g.title || "Untitled"}</button>)}</div>}
      </div>

      {/* Groups */}
      <div className="space-y-1">
        <span className="text-xs text-secondary">Groups</span>
        <div className="space-y-1 mb-1">
          {selectedGroups.map((sg) => {
            const group = allGroups?.items.find((g) => g.id === sg.groupId);
            return (
              <div key={sg.groupId} className="flex items-center gap-2">
                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-orange-900 text-orange-300">
                  {group?.name || `Group #${sg.groupId}`}
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
        <input value={groupSearch} onChange={(e) => setGroupSearch(e.target.value)} placeholder="Search groups…" className={inputCls} />
        {groupSearch && filteredGroupsList.length > 0 && <div className="max-h-24 overflow-y-auto bg-surface rounded border border-border">{filteredGroupsList.slice(0, 10).map((g) => <button key={g.id} onClick={() => { setSelectedGroups([...selectedGroups, { groupId: g.id, sceneIndex: 0 }]); setGroupSearch(""); }} className="block w-full text-left px-3 py-1 text-sm text-foreground hover:bg-card">{g.name}</button>)}</div>}
      </div>

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
