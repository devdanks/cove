import { useQueries, useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { faces, scenes, segmentDisplayProfiles, tags, entityImages, performers as performersApi, studios as studiosApi, galleries as galleriesApi, groups as groupsApi, metadata, playback } from "../api/client";
import { formatDuration, formatFileSize, formatDate, TagBadge, getResolutionLabel, CustomFieldsDisplay } from "../components/shared";
import { 
  Pencil, Plus, Trash2, Search, Eye, EyeOff, Heart, ArrowLeft,
  Check, ChevronLeft, ChevronRight, MoreVertical, PanelLeftClose, PanelLeft,
  Play, Pause, Volume2, VolumeX, Maximize, Minimize,
  SkipBack, SkipForward, Gauge, Clapperboard, Monitor, FolderOpen, Layers,
  RefreshCw, Camera, Image, Merge, Upload, ExternalLink, Download,
  PictureInPicture2, Repeat, Repeat1, Subtitles
} from "lucide-react";
import { useState, useRef, useEffect, useCallback, Fragment, useMemo, lazy, Suspense } from "react";
import { ConfirmDialog } from "../components/ConfirmDialog";
import type { Detection, Face, ResolvedSpan, Scene, SceneUpdate, Segment } from "../api/types";
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
import { authStore } from "../auth/authStore";
import { canDeleteEntity, canReadEntity, canWriteEntity, filterItemsByPermission, hasAnyPermission } from "../auth/visibility";
import { useEntityEngagement } from "../hooks/useEntityEngagement";

const SceneEditModal = lazy(() => import("./SceneEditModal").then((module) => ({ default: module.SceneEditModal })));
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

type TabKey = "details" | "groups" | "galleries" | "segments" | "filters" | "file-info" | "edit" | "history" | string;

export function SceneDetailPage({ id, initialSeekTo, onNavigate }: Props) {
  const { data: scene, isLoading } = useQuery({
    queryKey: ["scene", id],
    queryFn: () => scenes.get(id),
  });
  const { hasPermission, user } = useAuth();
  const { config } = useAppConfig();
  const { hasPrev, hasNext, prevId, nextId, currentPosition, queueLength } = useSceneQueue();
  const { getTabsForPage, resolveComponent: resolveExtComponent } = useExtensions();
  const [editing, setEditing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [showGenerate, setShowGenerate] = useState(false);
  const [theaterMode, setTheaterMode] = useState(false);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
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
  const canReadMarkers = canReadEntity("marker", hasPermission);
  const canWriteMarkers = hasPermission("markers.write");
  const canReadFiles = hasPermission("files.read");
  const canRunJobs = hasPermission("jobs.run");
  const canLibraryScan = hasPermission("library.scan");
  const canLibraryAutoTag = hasPermission("library.autotag");
  const canScrapeScene = hasAnyPermission(hasPermission, ["scenes.scrape", "scenes.write"]);
  const canEngageScene = canReadScene && (user?.kind === "user" || user?.kind === "system");
  const recordPlaybackHistory = user?.uiPreferences?.recordPlaybackHistory ?? true;
  const trackPlaybackActivity = canEngageScene && config?.ui.trackActivity !== false && recordPlaybackHistory;
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
    fallbackFavorite: (scene?.oCounter ?? 0) > 0,
    fallbackRating: scene?.rating,
  });
  const scenePlayCount = sceneEngagement?.playCount ?? scene?.playCount ?? 0;
  const sceneResumeTime = sceneEngagement?.resumeTime ?? scene?.resumeTime;
  const sceneOCount = sceneEngagement?.oCount ?? scene?.oCounter ?? 0;
  const effectiveResumeTime = initialSeekTo ?? sceneResumeTime;

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

  // Theater mode: hide navbar and expand layout
  useEffect(() => {
    if (theaterMode) {
      document.documentElement.classList.add("theater-mode");
    } else {
      document.documentElement.classList.remove("theater-mode");
    }
    return () => document.documentElement.classList.remove("theater-mode");
  }, [theaterMode]);

  // Keyboard shortcuts: "," for theater mode, a/e/s/i/h for tab navigation, o to toggle favorite
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;
      switch (e.key) {
        case ",": setTheaterMode((prev) => !prev); break;
        case "a": setActiveTab("details"); break;
        case "e": if (canWriteScene) setActiveTab("edit"); break;
        case "s": if (canReadMarkers) setActiveTab("segments"); break;
        case "i": if (canReadFiles) setActiveTab("file-info"); break;
        case "h": setActiveTab("history"); break;
        case "o": if (scene && canEngageScene) setSceneFavorite(!sceneFavorite); break;
        case "[": if (hasPrev && prevId != null) onNavigate({ page: "scene", id: prevId }); break;
        case "]": if (hasNext && nextId != null) onNavigate({ page: "scene", id: nextId }); break;
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [canEngageScene, canReadFiles, canReadMarkers, canWriteScene, hasNext, hasPrev, nextId, onNavigate, prevId, scene, sceneFavorite, setSceneFavorite]);

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

  const incrementPlayMut = useMutation({
    mutationFn: () => scenes.recordPlay(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["scene", id] });
      queryClient.invalidateQueries({ queryKey: ["engagement", "scene", id] });
      queryClient.invalidateQueries({ queryKey: ["scene-history", id] });
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
    enabled: canReadMarkers,
  });

  const { data: displayProfiles = [] } = useQuery({
    queryKey: ["segment-display-profiles"],
    queryFn: () => segmentDisplayProfiles.list(),
    enabled: canReadMarkers,
  });

  const { data: resolvedSpansResponse, isLoading: resolvedSpansLoading } = useQuery({
    queryKey: ["scene", id, "resolved-spans", selectedProfileId],
    queryFn: () => scenes.segments.spans(id, selectedProfileId),
    enabled: canReadMarkers,
  });

  const { data: detections = [], isLoading: detectionsLoading } = useQuery({
    queryKey: ["scene", id, "detections"],
    queryFn: () => scenes.detections.list(id),
    enabled: canReadMarkers,
  });

  const faceTrackWindows = useMemo(() => buildFaceTrackWindowMap(segments), [segments]);

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
      enabled: canReadFaces && canReadMarkers,
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
    ...(scene?.groups.length ? [{ key: "groups" as TabKey, label: "Groups" }] : []),
    ...(scene?.galleries.length ? [{ key: "galleries" as TabKey, label: "Galleries" }] : []),
    { key: "filters", label: "Filters" },
    { key: "file-info", label: `File Info${scene?.files.length && scene.files.length > 1 ? ` (${scene.files.length})` : ""}` },
    { key: "history", label: "History" },
    ...getTabsForPage("scene").map((t) => ({ key: `ext:${t.key}` as TabKey, label: t.label })),
    { key: "edit", label: "Edit" },
  ], {
    groups: "groups.read",
    galleries: "galleries.read",
    segments: "markers.read",
    "file-info": "files.read",
    edit: "scenes.write",
  }, hasPermission);

  useEffect(() => {
    if (!tabs.some((tab) => tab.key === activeTab)) {
      setActiveTab("details");
    }
  }, [activeTab, tabs]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-accent" />
      </div>
    );
  }

  if (!scene) return <div className="text-center text-secondary py-16">Scene not found</div>;

  const file = scene.files[0];
  const streamUrl = scenes.streamUrl(id);
  const resLabel = file ? getResolutionLabel(file.width, file.height) : null;

  const studioImageUrl = scene.studioId ? entityImages.studioImageUrl(scene.studioId) : null;

  return (
    <div className="-mx-6 -mt-5 -mb-5 h-[calc(100vh-49px)] overflow-hidden flex flex-col min-h-0">
      <input
        ref={coverFileInputRef}
        type="file"
        accept="image/jpeg,image/png,image/webp,image/gif"
        className="hidden"
        onChange={handleCoverFileChange}
      />
      <Suspense fallback={null}>
        {scene && editing ? <SceneEditModal scene={scene} open={editing} onClose={() => setEditing(false)} /> : null}
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
      {/* Standard layout: left sidebar + right video */}
      <div className={theaterMode ? "flex flex-col flex-1 min-h-0 overflow-hidden" : "flex flex-1 flex-col xl:flex-row min-h-0 overflow-hidden"}>
        {/* Left sidebar: metadata, tabs, tab content */}
        {!theaterMode && !sidebarCollapsed && (
          <div
            className="w-full xl:w-[400px] 2xl:w-[450px] xl:min-w-[350px] xl:max-w-[500px] xl:border-r border-b xl:border-b-0 border-border overflow-y-auto shrink-0 xl:max-h-[calc(100vh-48px)]"
          >
            <div className="px-6 pt-4 pb-2">
              {/* Studio logo */}
              {studioImageUrl && scene.studioId && (
                <div className="mb-3 flex items-start gap-4">
                  <button
                    onClick={() => onNavigate({ page: "studio", id: scene.studioId })}
                    className="flex-shrink-0"
                  >
                    <img
                      src={studioImageUrl}
                      alt={scene.studioName || "Studio"}
                      className="max-h-[5rem] max-w-full object-contain"
                      onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }}
                    />
                  </button>
                </div>
              )}

              {/* Queue navigation removed - will be replaced later */}

              <button
                onClick={goBack}
                className="mb-3 flex items-center gap-1 text-sm text-secondary hover:text-foreground"
              >
                <ArrowLeft className="h-4 w-4" /> {backLabel}
              </button>

              {/* Title — large like original's h3 */}
              <h3 className="text-[1.5rem] font-semibold text-foreground leading-snug line-clamp-2 mt-1">
                {scene.title || file?.basename || `Scene ${scene.id}`}
              </h3>

              {/* Subheader: date left, resolution+fps right */}
              <div className="flex items-center justify-between mt-2 text-sm text-secondary">
                <span>{scene.date ? new Date(scene.date + "T00:00:00").toLocaleDateString(undefined, { year: "numeric", month: "long", day: "numeric" }) : ""}</span>
                <span className="flex items-center gap-1.5">
                  {file && file.frameRate > 0 && <span>{file.frameRate.toFixed(0)} fps</span>}
                  {file && resLabel && <span className="text-accent font-bold">{resLabel}</span>}
                </span>
              </div>

              {/* Studio name text fallback (when no logo) */}
              {scene.studioName && scene.studioId && !studioImageUrl && (
                <button 
                  onClick={() => onNavigate({ page: "studio", id: scene.studioId })}
                  className="text-accent hover:underline text-sm mt-1 block"
                >
                  {scene.studioName}
                </button>
              )}

              {/* Toolbar: rating left, counters + ops right — single row */}
              <div className="flex items-center justify-between mt-3 gap-2">
                <InteractiveRating value={sceneRating ?? scene.rating} onChange={(value) => setSceneRating(value)} readOnly={!canEngageScene} />
                <div className="flex items-center gap-2">
                  <span className="flex items-center gap-1 text-sm text-secondary" title="Your play count">
                    <Eye className="w-4 h-4" />
                    <span>{scenePlayCount}</span>
                  </span>
                  {canEngageScene ? (
                    <button 
                      onClick={() => setSceneFavorite(!sceneFavorite)}
                      disabled={sceneFavoritePending}
                      className="flex items-center gap-1 text-sm text-secondary hover:text-accent"
                      title={sceneFavorite ? "Remove from favorites" : "Add to favorites"}
                    >
                      <Heart className={`w-4 h-4 ${sceneFavorite ? "fill-accent text-accent" : ""}`} />
                      <span>{sceneFavorite ? "Favorited" : "Favorite"}</span>
                    </button>
                  ) : (
                    <span className="flex items-center gap-1 text-sm text-secondary">
                      <Heart className={`w-4 h-4 ${sceneFavorite ? "fill-accent text-accent" : ""}`} />
                      <span>{sceneFavorite ? "Favorited" : "Favorite"}</span>
                    </span>
                  )}
                  {canWriteScene ? (
                    <button 
                      onClick={() => { if (!updateMut.isPending) updateMut.mutate({ organized: !scene.organized }); }}
                      disabled={updateMut.isPending}
                      className={`p-1 rounded ${scene.organized ? "bg-green-600 text-white" : "bg-card text-muted hover:text-foreground"} ${updateMut.isPending ? "opacity-60 cursor-not-allowed" : ""}`}
                      title={scene.organized ? "Organized" : "Not organized"}
                    >
                      <Check className="w-4 h-4" />
                    </button>
                  ) : scene.organized ? (
                    <span className="p-1 rounded bg-green-600 text-white" title="Organized">
                      <Check className="w-4 h-4" />
                    </span>
                  ) : null}
                  {file && (
                    <a
                      href={streamUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="p-1 rounded text-secondary hover:text-foreground hover:bg-card"
                      title="Open in external player"
                    >
                      <ExternalLink className="w-4 h-4" />
                    </a>
                  )}
                  {/* Operations dropdown */}
                  <div className="relative" ref={opsMenuRef}>
                    <button
                      onClick={() => setShowOpsMenu(!showOpsMenu)}
                      className="p-1 rounded text-secondary hover:text-foreground hover:bg-card"
                      title="Operations"
                    >
                      <MoreVertical className="w-4 h-4" />
                    </button>
                    {showOpsMenu && (
                      <div className="absolute right-0 top-full mt-1 z-50 min-w-[220px] bg-card border border-border rounded shadow-lg py-1">
                        {canWriteScene ? <button onClick={() => { setEditing(true); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface flex items-center gap-2"><Pencil className="w-3.5 h-3.5" /> Edit</button> : null}
                        {!file && canDownloadScene ? (
                          <button onClick={() => { setShowDownloadDialog(true); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface flex items-center gap-2"><Download className="w-3.5 h-3.5" /> Download Media…</button>
                        ) : null}
                        {file && canLibraryScan ? (
                          <button onClick={() => { rescanMut.mutate(); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface flex items-center gap-2"><RefreshCw className="w-3.5 h-3.5" /> Rescan</button>
                        ) : null}
                        {canScrapeScene ? <button onClick={() => { setShowScrapeDialog(true); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface flex items-center gap-2"><ExternalLink className="w-3.5 h-3.5" /> Scrape…</button> : null}
                        {canIdentifyScene ? <button onClick={() => { setShowIdentify(true); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface flex items-center gap-2"><Search className="w-3.5 h-3.5" /> Identify…</button> : null}
                        {canGenerateScene || canWriteScene ? <div className="border-t border-border my-1" /> : null}
                        <ExtensionEntityActions entityType="scene" entityId={scene.id} renderMode="menu" onInvoked={() => setShowOpsMenu(false)} />
                        {canGenerateScene ? <button onClick={() => { setShowGenerate(true); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface flex items-center gap-2"><Clapperboard className="w-3.5 h-3.5" /> Generate…</button> : null}
                        {canWriteScene ? <button onClick={() => { handleSetCoverFromCurrentFrame(); setShowOpsMenu(false); }} disabled={coverActionPending || !file} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface disabled:opacity-60 flex items-center gap-2"><Camera className="w-3.5 h-3.5" /> Set Cover from Current Frame</button> : null}
                        {canWriteScene ? <button onClick={() => { coverFileInputRef.current?.click(); setShowOpsMenu(false); }} disabled={coverActionPending} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface disabled:opacity-60 flex items-center gap-2"><Upload className="w-3.5 h-3.5" /> Upload Cover Image…</button> : null}
                        {canWriteScene ? <button onClick={() => { handleResetCoverToDefault(); setShowOpsMenu(false); }} disabled={coverActionPending} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface disabled:opacity-60 flex items-center gap-2"><Image className="w-3.5 h-3.5" /> Use Default Cover</button> : null}
                        {canWriteScene ? <div className="border-t border-border my-1" /> : null}
                        {canWriteScene ? <button onClick={() => { setShowMerge(true); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface flex items-center gap-2"><Merge className="w-3.5 h-3.5" /> Merge…</button> : null}
                        <button onClick={() => { setTheaterMode(true); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface flex items-center gap-2"><Monitor className="w-3.5 h-3.5" /> Theater Mode</button>
                        {canDeleteScene ? <div className="border-t border-border my-1" /> : null}
                        {canDeleteScene ? <button onClick={() => { setConfirmDelete(true); setShowOpsMenu(false); }} className="w-full px-3 py-1.5 text-left text-sm text-red-400 hover:bg-surface flex items-center gap-2"><Trash2 className="w-3.5 h-3.5" /> Delete</button> : null}
                      </div>
                    )}
                  </div>
                  <ExtensionSlot slot="scene-detail-actions" context={{ scene, onNavigate }} />
                </div>
              </div>
            </div>

            {/* Tab Navigation */}
            <div className="px-6">
              <div className="flex flex-wrap border-b border-border">
                {tabs.map((tab) => (
                  <button
                    key={tab.key}
                    onClick={() => setActiveTab(tab.key)}
                    className={`px-2.5 py-2 text-sm transition-colors border-b-2 cursor-pointer ${
                      activeTab === tab.key 
                        ? "border-accent text-accent" 
                        : "border-transparent text-secondary hover:text-foreground"
                    }`}
                  >
                    {tab.label}
                  </button>
                ))}
              </div>
            </div>

            {/* Tab Content */}
            <div className="px-6 py-4">
              {activeTab === "details" && (
                <>
                  <DetailsTab scene={scene} onNavigate={onNavigate} sceneFaces={sceneFaces} />
                  <AspectRatingsPanel hostType="scene" hostId={id} canRate={canEngageScene} className="mt-4" />
                </>
              )}
              {activeTab === "groups" && (
                <GroupsTab scene={scene} onNavigate={onNavigate} />
              )}
              {activeTab === "galleries" && (
                <GalleriesTab scene={scene} onNavigate={onNavigate} />
              )}
              {activeTab === "segments" && (
                <div className="space-y-4">
                  <ResolvedSpansPanel
                    sceneId={scene.id}
                    spans={resolvedSpans}
                    loading={resolvedSpansLoading}
                    profiles={displayProfiles}
                    currentProfileId={activeProfileId}
                    onProfileChange={setSelectedProfileId}
                    onSeek={(t) => seekRef.current?.(t)}
                    onNavigate={onNavigate}
                  />
                  <SegmentsPanel
                    sceneId={scene.id}
                    segments={segments}
                    loading={segmentsLoading}
                    canEdit={canWriteMarkers}
                    onSeek={(t) => seekRef.current?.(t)}
                  />
                </div>
              )}
              {activeTab === "filters" && (
                <VideoFiltersTab filters={videoFilters} onChange={setVideoFilters} />
              )}
              {activeTab === "file-info" && scene.files.length > 0 && (
                <FileInfoTab files={scene.files} />
              )}
              {activeTab === "history" && (
                <HistoryTab
                  scene={scene}
                  playCount={scenePlayCount}
                  favorite={sceneFavorite}
                  favoritePending={sceneFavoritePending}
                  setFavorite={setSceneFavorite}
                  oCount={sceneOCount}
                  canEngageScene={canEngageScene}
                />
              )}
              {activeTab === "edit" && (
                <SceneEditPanel scene={scene} onSaved={() => setActiveTab("details")} />
              )}
              {/* Extension-contributed tab content */}
              {activeTab.startsWith("ext:") && (() => {
                const extTabKey = activeTab.replace("ext:", "");
                const extTab = getTabsForPage("scene").find((t) => t.key === extTabKey);
                if (!extTab) return null;
                const Component = resolveExtComponent(extTab.componentName);
                if (!Component) return <div className="p-4 text-muted">Extension component not found: {extTab.componentName}</div>;
                return <Component entityId={id} />;
              })()}
            </div>
          </div>
        )}

        {/* Sidebar collapse/expand divider */}
        {!theaterMode && (
          <button
            onClick={() => setSidebarCollapsed(!sidebarCollapsed)}
            className="hidden xl:flex items-center justify-center bg-surface/50 hover:bg-surface border-r border-border transition-colors w-[15px] shrink-0"
            title={sidebarCollapsed ? "Show sidebar" : "Hide sidebar"}
          >
            {sidebarCollapsed ? <ChevronRight className="w-4 h-4 text-muted" /> : <ChevronLeft className="w-4 h-4 text-muted" />}
          </button>
        )}

        {/* Right side: video player + scrubber */}
        <div className="min-w-0 flex flex-col flex-1 min-h-0 overflow-hidden">
          <div className="bg-black flex-1 flex flex-col min-h-0 max-h-[70vh] xl:max-h-none">
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
                faceTrackWindows={faceTrackWindows}
                onSeekRegister={(fn) => { seekRef.current = fn; }}
                onTimeUpdate={setVideoTime}
                autostart={config?.ui.autostartVideo}
                showAbLoop={config?.ui.showAbLoopControls}
                trackActivity={trackPlaybackActivity}
                onEnded={() => { if (hasNext && nextId != null) onNavigate({ page: "scene", id: nextId }); }}
                onPrev={hasPrev && prevId != null ? () => onNavigate({ page: "scene", id: prevId }) : undefined}
                onNext={hasNext && nextId != null ? () => onNavigate({ page: "scene", id: nextId }) : undefined}
                onPlay={canEngageScene ? () => incrementPlayMut.mutate() : () => {}}
              />
            ) : (
              <div className="flex items-center justify-center h-48 text-muted">No video file available</div>
            )}
          </div>
          {/* Scene scrubber */}
          {file && (
            <SceneScrubber
              sceneId={scene.id}
              duration={file.duration}
              spans={resolvedSpans}
              rawSegments={segments}
              detections={detections}
              onSeek={(t) => seekRef.current?.(t)}
              currentTime={videoTime}
              profileName={activeProfileName}
            />
          )}

          {/* Theater mode: show metadata below video */}
          {theaterMode && (
            <div className="px-4 pt-3 max-w-5xl mx-auto">
              <h1 className="text-xl font-bold text-foreground">{scene.title || file?.basename || `Scene ${scene.id}`}</h1>
              <div className="flex items-center gap-3 mt-2 flex-wrap">
                <InteractiveRating value={sceneRating ?? scene.rating} onChange={(value) => setSceneRating(value)} readOnly={!canEngageScene} />
                <span className="flex items-center gap-1 text-sm text-secondary" title="Your play count"><Eye className="w-4 h-4" />{scenePlayCount}</span>
                {canEngageScene ? <button onClick={() => setSceneFavorite(!sceneFavorite)} disabled={sceneFavoritePending} className="flex items-center gap-1 text-sm text-secondary hover:text-accent disabled:cursor-not-allowed disabled:opacity-60"><Heart className={`w-4 h-4 ${sceneFavorite ? "fill-accent text-accent" : ""}`} />{sceneFavorite ? "Favorited" : "Favorite"}</button> : <span className="flex items-center gap-1 text-sm text-secondary"><Heart className={`w-4 h-4 ${sceneFavorite ? "fill-accent text-accent" : ""}`} />{sceneFavorite ? "Favorited" : "Favorite"}</span>}
                <button onClick={() => setTheaterMode(false)} className="flex items-center gap-1 px-2 py-1 text-xs bg-accent text-white rounded"><Monitor className="w-3 h-3" /> Exit Theater</button>
              </div>
            </div>
          )}
        </div>
      </div>
      <ExtensionSlot slot="scene-detail-main-bottom" context={{ scene, onNavigate }} />
    </div>
  );
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
          <div className={scene.performers.length > 1 ? "grid grid-cols-2 gap-3" : "flex flex-wrap gap-3"}>
            {scene.performers.map((performer: any) => (
              <PerformerCard 
                key={performer.id} 
                performer={performer}
                sceneDate={scene.date}
                fullWidth={scene.performers.length > 1}
                onClick={() => onNavigate({ page: "performer", id: performer.id })}
              />
            ))}
          </div>
        </div>
      )}

      {/* Faces */}
      {sceneFaces.length > 0 && (
        <div>
          <h6 className="mb-2 text-sm text-muted">Faces in this scene</h6>
          <div className="flex gap-2 overflow-x-auto pb-1">
            {sceneFaces.map(({ face, detectionCount }) => {
              const title = face.label?.trim() || face.performerName || `Face #${face.id}`;
              return (
                <button
                  key={face.id}
                  type="button"
                  onClick={() => onNavigate({ page: "face", id: face.id })}
                  className="flex min-w-[180px] items-center gap-3 rounded-xl border border-border bg-card/70 px-3 py-2 text-left transition-colors hover:border-accent"
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
      <CustomFieldsDisplay customFields={scene.customFields} />
    </div>
  );
}

function GroupsTab({ scene, onNavigate }: { scene: Scene; onNavigate: (r: any) => void }) {
  if (scene.groups.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-card/40 px-4 py-10 text-center text-sm text-secondary">
        <Layers className="mx-auto mb-3 h-8 w-8 text-muted" />
        No groups linked to this scene.
      </div>
    );
  }

  return (
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
  );
}

function GalleriesTab({ scene, onNavigate }: { scene: Scene; onNavigate: (r: any) => void }) {
  if (scene.galleries.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-border bg-card/40 px-4 py-10 text-center text-sm text-secondary">
        <FolderOpen className="mx-auto mb-3 h-8 w-8 text-muted" />
        No galleries linked to this scene.
      </div>
    );
  }

  return (
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
  );
}

function PerformerCard({ performer, sceneDate, fullWidth = false, onClick }: { performer: any; sceneDate?: string; fullWidth?: boolean; onClick: () => void }) {
  const imageUrl = performer.imagePath;
  const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "performer", id: performer.id }, onClick);
  // Calculate age at scene date
  const ageAtScene = (() => {
    if (!sceneDate || !performer.birthdate) return null;
    const scene = new Date(sceneDate);
    const birth = new Date(performer.birthdate);
    let age = scene.getFullYear() - birth.getFullYear();
    const m = scene.getMonth() - birth.getMonth();
    if (m < 0 || (m === 0 && scene.getDate() < birth.getDate())) age--;
    return age > 0 ? age : null;
  })();

  return (
    <a
      {...linkProps}
      className={`bg-card border border-border rounded overflow-hidden hover:border-accent/60 transition-colors text-left ${fullWidth ? "w-full" : ""}`}
      style={fullWidth ? undefined : { width: "200px" }}
    >
      <div className="aspect-[2/3] bg-surface flex items-center justify-center relative">
        {imageUrl ? (
          <img src={imageUrl} alt={performer.name} className="w-full h-full object-cover" />
        ) : (
          <div className="w-full h-full flex items-center justify-center bg-gradient-to-b from-card to-surface">
            <svg viewBox="0 0 100 150" className="w-2/3 h-2/3 opacity-30">
              <ellipse cx="50" cy="35" rx="25" ry="30" fill="currentColor" className="text-muted"/>
              <ellipse cx="50" cy="120" rx="40" ry="45" fill="currentColor" className="text-muted"/>
            </svg>
          </div>
        )}
      </div>
      <div className="p-2 text-center">
        <div className="text-sm text-foreground font-medium truncate">{performer.name}</div>
        <div className="text-xs text-muted flex items-center justify-center gap-1 mt-0.5">
          {ageAtScene && <span>{ageAtScene} yrs old</span>}
          {ageAtScene && performer.sceneCount !== undefined && <span>·</span>}
          {performer.sceneCount !== undefined && (
            <span className="flex items-center gap-0.5"><Eye className="w-3 h-3" /> {performer.sceneCount}</span>
          )}
        </div>
      </div>
    </a>
  );
}

// File Info Tab — show every underlying scene file rather than only the first one.
export function FileInfoTab({ files }: { files: Scene["files"] }) {
  return (
    <div className="space-y-4 text-sm">
      {files.map((file, index) => {
        const sectionLabel = file.basename || file.path.split(/[\\/]/).pop() || `File ${index + 1}`;

        return (
          <section key={file.id ?? `${file.path}-${index}`} className="rounded-xl border border-border bg-card p-4 space-y-3">
            {files.length > 1 && (
              <div>
                <h6 className="text-sm font-semibold text-foreground">{sectionLabel}</h6>
                <p className="text-xs text-muted">File {index + 1} of {files.length}</p>
              </div>
            )}

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
  favorite,
  favoritePending,
  setFavorite,
  oCount,
  canEngageScene,
}: {
  scene: Scene;
  playCount: number;
  favorite: boolean;
  favoritePending: boolean;
  setFavorite: (isFavorite: boolean) => void;
  oCount: number;
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
      case "oCount": return "O count";
      case "hide": return "Backgrounded";
      case "share": return "Shared";
      default: return kind;
    }
  };
  const timelineEvents = history?.events ?? [];

  return (
    <div className="space-y-4 text-sm">
      {/* Play History */}
      <div className="rounded-xl border border-border bg-card p-4">
        <div className="flex items-center justify-between mb-2">
          <h3 className="text-sm font-semibold text-muted uppercase tracking-wide">Play History</h3>
          <div className="flex gap-1">
            <button onClick={() => deletePlayMut.mutate()} className={btnCls} title="Remove last play">-1</button>
            <button onClick={() => resetPlayMut.mutate()} className={btnCls} title="Reset play count">Reset</button>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-2 mb-2">
          <div><span className="text-muted">Play Count:</span> <span className="text-foreground">{playCount}</span></div>
          <div><span className="text-muted">Duration:</span> <span className="text-foreground">{formatDuration(scene.playDuration)}</span></div>
        </div>
        {history?.playHistory && history.playHistory.length > 0 && (
          <div className="max-h-40 overflow-y-auto space-y-0.5 border-t border-border pt-2">
            {history.playHistory.map((date, i) => (
              <div key={i} className="text-xs text-secondary">{new Date(date).toLocaleString()}</div>
            ))}
          </div>
        )}
      </div>

      {/* Favorites History */}
      <div className="rounded-xl border border-border bg-card p-4">
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
      </div>

      <div className="rounded-xl border border-border bg-card p-4">
        <div className="flex items-center justify-between mb-2">
          <h3 className="text-sm font-semibold text-muted uppercase tracking-wide">O Count</h3>
          <div className="flex items-center gap-1.5 rounded border border-border bg-card px-2.5 py-1 text-xs text-secondary">
            <Heart className={`w-3.5 h-3.5 ${oCount > 0 ? "fill-accent text-accent" : ""}`} />
            <span>{oCount}</span>
          </div>
        </div>
        <div className="mb-2">
          <span className="text-muted">Count:</span> <span className="text-foreground">{oCount}</span>
        </div>
        {history?.oHistory && history.oHistory.length > 0 && (
          <div className="max-h-40 overflow-y-auto space-y-0.5 border-t border-border pt-2">
            {history.oHistory.map((date, i) => (
              <div key={i} className="text-xs text-secondary">{new Date(date).toLocaleString()}</div>
            ))}
          </div>
        )}
      </div>

      <div className="rounded-xl border border-border bg-card p-4">
        <div className="mb-2 flex items-center justify-between">
          <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">Watched Sections</h3>
          <span className="text-xs text-secondary">{history?.allTimeWatchedIntervals?.length ?? 0} intervals</span>
        </div>
        {history?.allTimeWatchedIntervals && history.allTimeWatchedIntervals.length > 0 ? (
          <>
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
      </div>

      {history?.sessions && history.sessions.length > 0 && (
        <div className="rounded-xl border border-border bg-card p-4">
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
        </div>
      )}

      {timelineEvents.length > 0 && (
        <div className="rounded-xl border border-border bg-card p-4">
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
        </div>
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

/* ── Video Player with custom controls ── */
const PLAYBACK_RATES = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];
const VOLUME_KEY = "cove-player-volume";
const MUTED_KEY = "cove-player-muted";

function createPlaybackSessionId() {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (character) => {
    const random = Math.random() * 16 | 0;
    const value = character === "x" ? random : (random & 0x3) | 0x8;
    return value.toString(16);
  });
}

function roundPlaybackTime(value: number) {
  return Math.round(value * 1000) / 1000;
}

function buildKeepaliveHeaders() {
  const headers = new Headers({ "Content-Type": "application/json" });
  const shareToken = authStore.getShareToken();
  const sharePassword = authStore.getSharePassword();
  const accessToken = authStore.getAccessToken();

  if (shareToken) {
    headers.set("X-Share-Token", shareToken);
    if (sharePassword) {
      headers.set("X-Share-Password", sharePassword);
    }
  } else if (accessToken) {
    headers.set("Authorization", `Bearer ${accessToken}`);
  }

  return headers;
}

async function postPlaybackIntervalsKeepalive(data: import("../api/types").PlaybackIntervalsRequest) {
  await fetch("/api/playback/intervals", {
    method: "POST",
    keepalive: true,
    headers: buildKeepaliveHeaders(),
    body: JSON.stringify(data),
  });
}

export function VideoPlayer({
  streamUrl,
  posterUrl,
  format,
  duration,
  resumeTime,
  sceneId,
  detections = [],
  captions,
  onPlay,
  onSeekRegister,
  onTimeUpdate: onTimeUpdateProp,
  autostart,
  autostartToken,
  showAbLoop,
  trackActivity = true,
  onEnded: onEndedProp,
  clip,
  onPrev,
  onNext,
  faceTrackWindows,
}: {
  streamUrl: string;
  posterUrl?: string;
  format: string;
  duration: number;
  resumeTime?: number;
  sceneId: number;
  detections?: Detection[];
  captions?: { id: number; languageCode: string; captionType: string; filename: string }[];
  onPlay: () => void;
  onSeekRegister?: (fn: (time: number) => void) => void;
  onTimeUpdate?: (time: number) => void;
  autostart?: boolean;
  autostartToken?: number;
  showAbLoop?: boolean;
  trackActivity?: boolean;
  onEnded?: () => void;
  clip?: { start: number; end?: number; loop?: boolean };
  onPrev?: () => void;
  onNext?: () => void;
  faceTrackWindows?: Map<string, { startSec: number; endSec: number }>;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [playing, setPlaying] = useState(false);
  const [currentTime, setCurTime] = useState(0);
  const [buffered, setBuffered] = useState(0);
  const [vol, setVol] = useState(() => {
    const saved = localStorage.getItem(VOLUME_KEY);
    return saved ? Number(saved) : 1;
  });
  const [muted, setMuted] = useState(() => localStorage.getItem(MUTED_KEY) === "true");
  const [fullscreen, setFullscreen] = useState(false);
  const [showControls, setShowControls] = useState(true);
  const [showSpeed, setShowSpeed] = useState(false);
  const [rate, setRate] = useState(1);
  const [pip, setPip] = useState(false);
  const [loop, setLoop] = useState(false);
  const [abLoop, setAbLoop] = useState<{ a: number | null; b: number | null }>({ a: null, b: null });
  const [showCaptions, setShowCaptions] = useState(false);
  const [faceOverlayEnabled, setFaceOverlayEnabled] = usePersistedFlag("cove.player.faceOverlay", false);
  const [showQuality, setShowQuality] = useState(false);
  const [selectedQuality, setSelectedQuality] = useState<string>("Direct");
  const [availableQualities, setAvailableQualities] = useState<string[]>([]);
  const hideTimer = useRef<ReturnType<typeof setTimeout>>(null);
  const playTriggered = useRef(false);
  const sourceRestoreRef = useRef<{ time: number; shouldPlay: boolean } | null>(null);
  const lastLoadedSourceRef = useRef<string | null>(null);
  const pendingAutostartRef = useRef(false);
  const activitySessionId = useRef(createPlaybackSessionId());
  const intervalStart = useRef<number | null>(null);
  const lastSeenTime = useRef<number>(0);
  const lastKeepaliveSentAt = useRef<number>(0);
  const journalFlushed = useRef(false);
  const lastHideInteractionAt = useRef(0);
  const clipEndedHandled = useRef(false);
  const [videoBox, setVideoBox] = useState({ left: 0, top: 0, width: 0, height: 0 });
  const clipStart = clip?.start ?? 0;
  const clipEnd = Math.max(clipStart, clip?.end ?? duration);
  const timelineStart = clip ? clipStart : 0;
  const timelineDuration = clip ? Math.max(clipEnd - clipStart, 0.001) : Math.max(duration, 0.001);
  const visibleCurrentTime = clip ? Math.max(0, currentTime - clipStart) : currentTime;
  const visibleBuffered = clip ? Math.max(0, Math.min(buffered, clipEnd) - clipStart) : buffered;

  useEffect(() => {
    activitySessionId.current = createPlaybackSessionId();
    intervalStart.current = null;
    lastSeenTime.current = 0;
    lastKeepaliveSentAt.current = 0;
    lastHideInteractionAt.current = 0;
    playTriggered.current = false;
    pendingAutostartRef.current = false;
  }, [sceneId]);

  useEffect(() => {
    clipEndedHandled.current = false;
  }, [clip?.end, clip?.loop, clip?.start, sceneId, streamUrl]);

  // Restore volume
  useEffect(() => {
    const v = videoRef.current;
    if (!v) return;
    v.volume = vol;
    v.muted = muted;
  }, []);

  // Register seek callback for external timeline components.
  useEffect(() => {
    if (onSeekRegister) {
      onSeekRegister((time: number) => {
        const v = videoRef.current;
        if (v) {
          v.currentTime = time;
          v.play().catch(() => {});
        }
      });
    }
  }, [onSeekRegister]);

  const updateVideoBox = useCallback(() => {
    const video = videoRef.current;
    const container = containerRef.current;
    if (!video || !container) {
      return;
    }

    const intrinsicWidth = video.videoWidth || video.clientWidth;
    const intrinsicHeight = video.videoHeight || video.clientHeight;
    const containerWidth = container.clientWidth;
    const containerHeight = container.clientHeight;

    if (!intrinsicWidth || !intrinsicHeight || !containerWidth || !containerHeight) {
      return;
    }

    const scale = Math.min(containerWidth / intrinsicWidth, containerHeight / intrinsicHeight);
    const width = intrinsicWidth * scale;
    const height = intrinsicHeight * scale;
    const left = (containerWidth - width) / 2;
    const top = (containerHeight - height) / 2;

    setVideoBox((current) => {
      if (
        Math.abs(current.left - left) < 0.5
        && Math.abs(current.top - top) < 0.5
        && Math.abs(current.width - width) < 0.5
        && Math.abs(current.height - height) < 0.5
      ) {
        return current;
      }

      return { left, top, width, height };
    });
  }, []);

  useEffect(() => {
    const container = containerRef.current;
    const video = videoRef.current;
    if (!container || !video) {
      return;
    }

    updateVideoBox();
    const resizeObserver = new ResizeObserver(() => updateVideoBox());
    resizeObserver.observe(container);
    resizeObserver.observe(video);
    window.addEventListener("resize", updateVideoBox);
    return () => {
      resizeObserver.disconnect();
      window.removeEventListener("resize", updateVideoBox);
    };
  }, [sceneId, selectedQuality, streamUrl, updateVideoBox]);

  const activeDetections = useMemo(() => {
    if (!detections.length) {
      return [];
    }

    const byKey = new Map<string, Detection[]>();

    for (const detection of detections) {
      const key = detection.groupKey
        ?? `${detection.refKind ?? detection.class}:${detection.refId ?? detection.id}:${detection.class}`;

      const groupDetections = byKey.get(key);
      if (groupDetections) {
        groupDetections.push(detection);
      }
      else {
        byKey.set(key, [detection]);
      }
    }

    return Array.from(byKey.values())
      .filter((group) => faceOverlayEnabled || !group.some((d) => (d.refKind ?? d.class ?? "").toLowerCase() === "face"))
      .map((groupDetections) => selectActiveDetectionAtTime(groupDetections, currentTime, faceTrackWindows ?? new Map()))
      .filter((detection): detection is Detection => detection != null);
  }, [currentTime, detections, faceTrackWindows, faceOverlayEnabled]);

  const effectiveStreamUrl = selectedQuality === "Direct" ? streamUrl : scenes.transcodeUrl(sceneId, selectedQuality);

  // Resume from saved position
  useEffect(() => {
    const v = videoRef.current;
    const nextTime = clip ? clip.start : resumeTime;
    if (v && nextTime != null) {
      v.currentTime = nextTime;
      setCurTime(roundPlaybackTime(nextTime));
    }

    if (clip?.loop && clip.end != null) {
      setAbLoop({ a: clip.start, b: clip.end });
    } else if (clip) {
      setAbLoop({ a: null, b: null });
    }
  }, [clip?.end, clip?.loop, clip?.start, resumeTime, sceneId, streamUrl]);

  // Autostart video
  useEffect(() => {
    if (!autostart) {
      return;
    }

    pendingAutostartRef.current = true;
    const video = videoRef.current;
    const sourceSignature = `${effectiveStreamUrl}|${format || "mp4"}`;
    if (!video || lastLoadedSourceRef.current !== sourceSignature) {
      return;
    }

    video.play().catch(() => {});
  }, [autostart, autostartToken, effectiveStreamUrl, format]);

  // PiP change listener
  useEffect(() => {
    const handler = () => setPip(document.pictureInPictureElement === videoRef.current);
    document.addEventListener("enterpictureinpicture", handler);
    document.addEventListener("leavepictureinpicture", handler);
    return () => {
      document.removeEventListener("enterpictureinpicture", handler);
      document.removeEventListener("leavepictureinpicture", handler);
    };
  }, []);

  // AirPlay: sync seek position when playback target changes (e.g. Apple TV)
  useEffect(() => {
    const v = videoRef.current as (HTMLVideoElement & { webkitShowPlaybackTargetPicker?: () => void }) | null;
    if (!v) return;
    const onTargetChanged = () => {
      // When switching to AirPlay target, re-apply current time after a brief delay
      const savedTime = v.currentTime;
      setTimeout(() => {
        if (v.currentTime < savedTime - 1) v.currentTime = savedTime;
      }, 500);
    };
    v.addEventListener("webkitcurrentplaybacktargetchanged" as any, onTargetChanged);
    return () => v.removeEventListener("webkitcurrentplaybacktargetchanged" as any, onTargetChanged);
  }, []);

  // A-B loop enforcement
  useEffect(() => {
    if (abLoop.a == null || abLoop.b == null) return;
    const v = videoRef.current;
    if (!v) return;
    const handler = () => {
      if (v.currentTime >= abLoop.b!) {
        v.currentTime = abLoop.a!;
      }
    };
    v.addEventListener("timeupdate", handler);
    return () => v.removeEventListener("timeupdate", handler);
  }, [abLoop]);

  useEffect(() => {
    if (journalFlushed.current) {
      return;
    }

    journalFlushed.current = true;
    // Clear any stale localStorage journal entries from the old system
    window.localStorage.removeItem("cove-scene-activity-journal");
  }, []);

  const flushInterval = useCallback((state: string) => {
    const video = videoRef.current;
    if (!trackActivity || !video || intervalStart.current === null) return;
    const startSec = intervalStart.current;
    const endSec = roundPlaybackTime(lastSeenTime.current);
    if (endSec <= startSec) return;
    const data: import("../api/types").PlaybackIntervalsRequest = {
      hostType: "scene",
      hostId: sceneId,
      sessionId: activitySessionId.current,
      mediaDurationSec: video.duration || 0,
      currentPositionSec: endSec,
      state,
      intervals: [{ startSec, endSec }],
    };
    void playback.recordIntervals(data).catch(() => {});
  }, [sceneId, trackActivity]);

  const flushIntervalKeepalive = useCallback((state: string) => {
    const video = videoRef.current;
    if (!trackActivity || !video || intervalStart.current === null) return;
    const startSec = intervalStart.current;
    const endSec = roundPlaybackTime(lastSeenTime.current);
    if (endSec <= startSec) return;
    const data: import("../api/types").PlaybackIntervalsRequest = {
      hostType: "scene",
      hostId: sceneId,
      sessionId: activitySessionId.current,
      mediaDurationSec: video.duration || 0,
      currentPositionSec: endSec,
      state,
      intervals: [{ startSec, endSec }],
    };
    void postPlaybackIntervalsKeepalive(data).catch(() => {});
  }, [sceneId, trackActivity]);

  useEffect(() => {
    if (!clip) {
      return;
    }

    const video = videoRef.current;
    if (!video) {
      return;
    }

    const handleClipBoundary = () => {
      if (video.currentTime < clipStart) {
        video.currentTime = clipStart;
        setCurTime(roundPlaybackTime(clipStart));
        return;
      }

      if (video.currentTime < clipEnd - 0.05) {
        clipEndedHandled.current = false;
        return;
      }

      if (clip.loop) {
        video.currentTime = clipStart;
        setCurTime(roundPlaybackTime(clipStart));
        lastSeenTime.current = roundPlaybackTime(clipStart);
        if (intervalStart.current !== null) {
          flushInterval("active");
          intervalStart.current = clipStart;
        }
        return;
      }

      if (clipEndedHandled.current) {
        return;
      }

      clipEndedHandled.current = true;
      video.pause();
      video.currentTime = clipEnd;
      lastSeenTime.current = roundPlaybackTime(clipEnd);
      setCurTime(roundPlaybackTime(clipEnd));
      flushInterval("ended");
      intervalStart.current = null;
      setPlaying(false);
      onEndedProp?.();
    };

    video.addEventListener("timeupdate", handleClipBoundary);
    return () => {
      video.removeEventListener("timeupdate", handleClipBoundary);
    };
  }, [clip, clipEnd, clipStart, flushInterval, onEndedProp]);

  // Flush interval when the page is backgrounded or the player unmounts.
  useEffect(() => {
    if (!trackActivity) {
      return;
    }

    const handleVisibilityChange = () => {
      if (document.visibilityState === "hidden") {
        flushIntervalKeepalive("paused");
      }
    };
    const handlePageHide = () => flushIntervalKeepalive("paused");

    window.addEventListener("pagehide", handlePageHide);
    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      window.removeEventListener("pagehide", handlePageHide);
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      flushIntervalKeepalive("paused");
    };
  }, [flushIntervalKeepalive, trackActivity]);

  // Fullscreen change listener
  useEffect(() => {
    const handler = () => setFullscreen(!!document.fullscreenElement);
    document.addEventListener("fullscreenchange", handler);
    return () => document.removeEventListener("fullscreenchange", handler);
  }, []);

  // Auto-hide controls
  const resetHideTimer = useCallback(() => {
    setShowControls(true);
    if (hideTimer.current) clearTimeout(hideTimer.current);
    hideTimer.current = setTimeout(() => {
      if (videoRef.current && !videoRef.current.paused) setShowControls(false);
    }, 3000);
  }, []);

  // Toggle text tracks when showCaptions state changes
  useEffect(() => {
    const v = videoRef.current;
    if (!v) return;
    for (let i = 0; i < v.textTracks.length; i++) {
      v.textTracks[i].mode = showCaptions ? "showing" : "hidden";
    }
  }, [showCaptions]);

  // Fetch available resolutions for quality selector
  useEffect(() => {
    scenes.getResolutions(sceneId).then((res) => setAvailableQualities(res ?? [])).catch(() => {});
  }, [sceneId]);

  // Keyboard shortcuts
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const v = videoRef.current;
      if (!v) return;
      const tag = (e.target as HTMLElement).tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;

      switch (e.key) {
        case " ":
        case "k":
          e.preventDefault();
          v.paused ? v.play() : v.pause();
          break;
        case "ArrowLeft":
          e.preventDefault();
          v.currentTime = Math.max(0, v.currentTime - (e.shiftKey ? 10 : 5));
          break;
        case "ArrowRight":
          e.preventDefault();
          v.currentTime = Math.min(v.duration, v.currentTime + (e.shiftKey ? 10 : 5));
          break;
        case "ArrowUp":
          e.preventDefault();
          v.volume = Math.min(1, v.volume + 0.1);
          setVol(v.volume);
          localStorage.setItem(VOLUME_KEY, String(v.volume));
          break;
        case "ArrowDown":
          e.preventDefault();
          v.volume = Math.max(0, v.volume - 0.1);
          setVol(v.volume);
          localStorage.setItem(VOLUME_KEY, String(v.volume));
          break;
        case "m":
          v.muted = !v.muted;
          setMuted(v.muted);
          localStorage.setItem(MUTED_KEY, String(v.muted));
          break;
        case "f":
          if (document.fullscreenElement) document.exitFullscreen();
          else containerRef.current?.requestFullscreen();
          break;
        case "0": case "1": case "2": case "3": case "4":
        case "5": case "6": case "7": case "8": case "9":
          e.preventDefault();
          v.currentTime = v.duration * (Number(e.key) / 10);
          break;
      }
      resetHideTimer();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [resetHideTimer]);

  const togglePlay = () => {
    const v = videoRef.current;
    if (!v) return;
    v.paused ? v.play() : v.pause();
  };

  const seekTo = (e: React.MouseEvent<HTMLDivElement>) => {
    const v = videoRef.current;
    if (!v) return;
    const rect = e.currentTarget.getBoundingClientRect();
    const pct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    v.currentTime = timelineStart + pct * timelineDuration;
  };

  const changeVolume = (e: React.MouseEvent<HTMLDivElement>) => {
    const v = videoRef.current;
    if (!v) return;
    const rect = e.currentTarget.getBoundingClientRect();
    const pct = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    v.volume = pct;
    v.muted = false;
    setVol(pct);
    setMuted(false);
    localStorage.setItem(VOLUME_KEY, String(pct));
    localStorage.setItem(MUTED_KEY, "false");
  };

  const toggleFullscreen = () => {
    if (document.fullscreenElement) document.exitFullscreen();
    else containerRef.current?.requestFullscreen();
  };

  const changeRate = (r: number) => {
    const v = videoRef.current;
    if (v) v.playbackRate = r;
    setRate(r);
    setShowSpeed(false);
  };

  const changeQuality = (q: string) => {
    const v = videoRef.current;
    const curTime = v?.currentTime ?? 0;
    const wasPlaying = v ? !v.paused : false;
    sourceRestoreRef.current = { time: curTime, shouldPlay: wasPlaying };
    setSelectedQuality(q);
    setShowQuality(false);
  };

  useEffect(() => {
    const video = videoRef.current;
    if (!video) {
      return;
    }

    const sourceSignature = `${effectiveStreamUrl}|${format || "mp4"}`;
    if (lastLoadedSourceRef.current === sourceSignature) {
      return;
    }
    lastLoadedSourceRef.current = sourceSignature;

    const pendingRestore = sourceRestoreRef.current;
    sourceRestoreRef.current = null;
    const shouldAutoplayAfterLoad = pendingRestore?.shouldPlay || pendingAutostartRef.current;

    const handleLoadedMetadata = () => {
      const targetTime = pendingRestore?.time ?? (clip ? clip.start : resumeTime);
      if (targetTime != null && Number.isFinite(targetTime)) {
        video.currentTime = targetTime;
        setCurTime(roundPlaybackTime(targetTime));
      }

      if (shouldAutoplayAfterLoad) {
        pendingAutostartRef.current = false;
        video.play().catch(() => {});
      }
    };

    video.addEventListener("loadedmetadata", handleLoadedMetadata, { once: true });
    video.load();
    return () => {
      video.removeEventListener("loadedmetadata", handleLoadedMetadata);
    };
  }, [clip, effectiveStreamUrl, format, resumeTime]);

  const togglePip = async () => {
    const v = videoRef.current;
    if (!v) return;
    try {
      if (document.pictureInPictureElement) {
        await document.exitPictureInPicture();
      } else {
        await v.requestPictureInPicture();
      }
    } catch { /* PiP not supported or denied */ }
  };

  const cycleAbLoop = () => {
    const v = videoRef.current;
    if (!v) return;
    if (abLoop.a == null) {
      setAbLoop({ a: v.currentTime, b: null });
    } else if (abLoop.b == null) {
      setAbLoop({ a: abLoop.a, b: v.currentTime });
    } else {
      setAbLoop({ a: null, b: null });
    }
  };

  const fmtTime = (s: number) => {
    if (!isFinite(s)) return "0:00";
    const h = Math.floor(s / 3600);
    const m = Math.floor((s % 3600) / 60);
    const sec = Math.floor(s % 60);
    return h > 0 ? `${h}:${m.toString().padStart(2, "0")}:${sec.toString().padStart(2, "0")}` : `${m}:${sec.toString().padStart(2, "0")}`;
  };

  return (
    <div
      ref={containerRef}
      className="relative group w-full h-full flex items-center justify-center bg-black"
      onMouseMove={resetHideTimer}
      onMouseLeave={() => playing && setShowControls(false)}
    >
      <video
        ref={videoRef}
        className="w-full h-full object-contain cursor-pointer"
        preload="metadata"
        poster={posterUrl}
        {...{ "x-webkit-airplay": "allow" } as any}
        onLoadedMetadata={updateVideoBox}
        onLoadedData={updateVideoBox}
        onClick={togglePlay}
        onDoubleClick={toggleFullscreen}
        onPlay={() => {
          setPlaying(true);
          pendingAutostartRef.current = false;
          const currentPos = roundPlaybackTime(videoRef.current?.currentTime ?? currentTime);
          intervalStart.current = currentPos;
          lastSeenTime.current = currentPos;
          if (!playTriggered.current) { playTriggered.current = true; onPlay(); }
        }}
        onPause={() => {
          setPlaying(false);
          flushInterval("paused");
          intervalStart.current = null;
        }}
        onSeeking={() => {
          if (intervalStart.current !== null) {
            flushInterval("active");
            intervalStart.current = null;
          }
        }}
        onSeeked={() => {
          const video = videoRef.current;
          if (video && !video.paused) {
            const time = roundPlaybackTime(video.currentTime);
            intervalStart.current = time;
            lastSeenTime.current = time;
          }
        }}
        onTimeUpdate={() => {
          const v = videoRef.current;
          const time = roundPlaybackTime(v?.currentTime ?? 0);
          setCurTime(time);
          onTimeUpdateProp?.(time);
          lastSeenTime.current = time;
          if (trackActivity && intervalStart.current !== null) {
            const now = Date.now();
            if (now - lastKeepaliveSentAt.current >= 10000) {
              lastKeepaliveSentAt.current = now;
              flushInterval("active");
              intervalStart.current = time;
            }
          }
        }}
        onProgress={() => {
          const v = videoRef.current;
          if (v && v.buffered.length > 0) setBuffered(v.buffered.end(v.buffered.length - 1));
        }}
        onEnded={() => {
          if (loop) {
            flushInterval("active");
            intervalStart.current = null;
            const v = videoRef.current;
            if (v) { v.currentTime = 0; v.play().catch(() => {}); }
            return;
          }
          setPlaying(false);
          flushInterval("ended");
          intervalStart.current = null;
          onEndedProp?.();
        }}
      >
        <source src={effectiveStreamUrl} type={`video/${format || "mp4"}`} />
        {captions?.map((cap, idx) => (
          <track
            key={cap.id}
            kind="captions"
            src={scenes.captionUrl(sceneId, cap.id)}
            srcLang={cap.languageCode === "00" ? "en" : cap.languageCode}
            label={cap.languageCode === "00" ? cap.filename : cap.languageCode.toUpperCase()}
            default={idx === 0 && showCaptions}
          />
        ))}
      </video>

      {activeDetections.length > 0 && videoBox.width > 0 && videoBox.height > 0 ? (
        <div className="pointer-events-none absolute inset-0 z-[2]">
          {activeDetections.map((detection) => {
            const left = videoBox.left + (detection.x / Math.max(detection.frameWidth, 1)) * videoBox.width;
            const top = videoBox.top + (detection.y / Math.max(detection.frameHeight, 1)) * videoBox.height;
            const width = (detection.w / Math.max(detection.frameWidth, 1)) * videoBox.width;
            const height = (detection.h / Math.max(detection.frameHeight, 1)) * videoBox.height;
            const color = detectionColor(detection.class);

            return (
              <div
                key={detection.id}
                className="absolute rounded-md border shadow-[0_0_0_1px_rgba(0,0,0,0.25)]"
                style={{
                  left,
                  top,
                  width,
                  height,
                  borderColor: color,
                  boxShadow: `0 0 0 1px ${color}55 inset`,
                  background: `${color}14`,
                }}
              >
                <span
                  className="absolute left-0 top-0 -translate-y-full rounded-sm px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-white"
                  style={{ backgroundColor: color }}
                >
                  {formatDetectionBadge(detection)}
                </span>
              </div>
            );
          })}
        </div>
      ) : null}

      {/* Custom Controls Overlay */}
      <div
        className={`absolute bottom-0 left-0 right-0 bg-gradient-to-t from-black/90 via-black/50 to-transparent transition-opacity ${
          showControls ? "opacity-100" : "opacity-0 pointer-events-none"
        }`}
        style={{ padding: "40px 0 0 0" }}
      >
        {/* Seek bar */}
        <div className="px-3">
          <div className="relative h-4 flex items-center cursor-pointer group/seek" onClick={seekTo}>
            <div className="w-full h-1 bg-white/20 rounded-full group-hover/seek:h-1.5 transition-all relative">
              {/* Buffered */}
              <div className="absolute top-0 left-0 h-full bg-white/30 rounded-full" style={{ width: `${(visibleBuffered / timelineDuration) * 100}%` }} />
              {/* Progress */}
              <div className="absolute top-0 left-0 h-full bg-accent rounded-full" style={{ width: `${(visibleCurrentTime / timelineDuration) * 100}%` }} />
              {/* A-B loop range indicator */}
              {abLoop.a != null && (
                <div
                  className="absolute top-0 h-full bg-accent/25 pointer-events-none"
                  style={{
                    left: `${((abLoop.a - timelineStart) / timelineDuration) * 100}%`,
                    width: abLoop.b != null ? `${((abLoop.b - abLoop.a) / timelineDuration) * 100}%` : "2px",
                  }}
                />
              )}
            </div>
            {/* Seek thumb */}
            <div
              className="absolute top-1/2 -translate-y-1/2 w-3 h-3 bg-accent rounded-full opacity-0 group-hover/seek:opacity-100 transition-opacity"
              style={{ left: `${(visibleCurrentTime / timelineDuration) * 100}%`, transform: "translate(-50%, -50%)" }}
            />
          </div>
        </div>

        {/* Controls row */}
        <div className="flex items-center gap-2 px-3 py-2 text-white">
          {/* Previous scene */}
          {onPrev && (
            <button onClick={onPrev} className="hover:text-accent p-1" title="Previous scene">
              <SkipBack className="w-4 h-4 fill-current" />
            </button>
          )}

          <button onClick={togglePlay} className="hover:text-accent p-1">
            {playing ? <Pause className="w-5 h-5" /> : <Play className="w-5 h-5" />}
          </button>

          {/* Next scene */}
          {onNext && (
            <button onClick={onNext} className="hover:text-accent p-1" title="Next scene">
              <SkipForward className="w-4 h-4 fill-current" />
            </button>
          )}

          <button onClick={() => { const v = videoRef.current; if (v) v.currentTime = Math.max(0, v.currentTime - 10); }} className="hover:text-accent p-1" title="Back 10s">
            <SkipBack className="w-4 h-4" />
          </button>
          <button onClick={() => { const v = videoRef.current; if (v) v.currentTime = Math.min(v.duration, v.currentTime + 10); }} className="hover:text-accent p-1" title="Forward 10s">
            <SkipForward className="w-4 h-4" />
          </button>

          {/* Volume */}
          <button onClick={() => {
            const v = videoRef.current;
            if (!v) return;
            v.muted = !v.muted;
            setMuted(v.muted);
            localStorage.setItem(MUTED_KEY, String(v.muted));
          }} className="hover:text-accent p-1">
            {muted || vol === 0 ? <VolumeX className="w-4 h-4" /> : <Volume2 className="w-4 h-4" />}
          </button>
          <div className="w-20 h-3 flex items-center cursor-pointer group/vol" onClick={changeVolume}>
            <div className="w-full h-1 bg-white/20 rounded-full relative">
              <div className="absolute top-0 left-0 h-full bg-white rounded-full" style={{ width: `${(muted ? 0 : vol) * 100}%` }} />
            </div>
          </div>

          <span className="text-xs text-white/70 ml-1 select-none tabular-nums">
            {fmtTime(visibleCurrentTime)} / {fmtTime(clip ? clipEnd - clipStart : duration)}
          </span>

          <div className="ml-auto flex items-center gap-2">
            {/* Playback speed */}
            <div className="relative">
              <button
                onClick={() => setShowSpeed(!showSpeed)}
                className={`hover:text-accent p-1 text-xs font-medium flex items-center gap-1 ${rate !== 1 ? "text-accent" : ""}`}
              >
                {rate}x
              </button>
              {showSpeed && (
                <div className="absolute bottom-full right-0 mb-2 bg-surface border border-border rounded shadow-lg py-1 z-10">
                  {PLAYBACK_RATES.map((r) => (
                    <button
                      key={r}
                      onClick={() => changeRate(r)}
                      className={`block w-full text-left px-4 py-1 text-sm hover:bg-card ${r === rate ? "text-accent" : "text-white"}`}
                    >
                      {r}x
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* A-B Loop */}
            {showAbLoop && (
              <button
                onClick={cycleAbLoop}
                className={`hover:text-accent p-1 text-xs font-medium flex items-center gap-1 ${abLoop.a != null ? "text-accent" : ""}`}
                title={abLoop.a == null ? "Set loop start (A)" : abLoop.b == null ? "Set loop end (B)" : "Clear A-B loop"}
              >
                <Repeat className="w-4 h-4" />
                {abLoop.a != null && abLoop.b == null && "A"}
                {abLoop.a != null && abLoop.b != null && "A-B"}
              </button>
            )}

            {/* Quality selector */}
            {availableQualities.length > 0 && (
              <div className="relative">
                <button
                  onClick={() => setShowQuality(!showQuality)}
                  className={`hover:text-accent p-1 text-xs font-medium ${selectedQuality !== "Direct" ? "text-accent" : ""}`}
                  title="Video quality"
                >
                  {selectedQuality === "Direct" ? "Direct" : selectedQuality}
                </button>
                {showQuality && (
                  <div className="absolute bottom-full right-0 mb-2 bg-surface border border-border rounded shadow-lg py-1 z-10">
                    <button
                      onClick={() => changeQuality("Direct")}
                      className={`block w-full text-left px-4 py-1 text-sm hover:bg-card ${selectedQuality === "Direct" ? "text-accent" : "text-white"}`}
                    >
                      Direct
                    </button>
                    {availableQualities.map((q) => (
                      <button
                        key={q}
                        onClick={() => changeQuality(q)}
                        className={`block w-full text-left px-4 py-1 text-sm hover:bg-card ${q === selectedQuality ? "text-accent" : "text-white"}`}
                      >
                        {q}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            )}

            {/* Loop entire video */}
            <button
              onClick={() => setLoop(!loop)}
              className={`hover:text-accent p-1 ${loop ? "text-accent" : ""}`}
              title={loop ? "Disable loop" : "Loop video"}
            >
              <Repeat1 className="w-4 h-4" />
            </button>

            {/* Picture-in-Picture */}
            <button onClick={togglePip} className={`hover:text-accent p-1 ${pip ? "text-accent" : ""}`} title="Picture-in-Picture">
              <PictureInPicture2 className="w-4 h-4" />
            </button>

            {/* Captions toggle */}
            {captions && captions.length > 0 && (
              <button
                onClick={() => setShowCaptions((prev) => !prev)}
                className={`hover:text-accent p-1 ${showCaptions ? "text-accent" : ""}`}
                title={showCaptions ? "Hide captions" : "Show captions"}
              >
                <Subtitles className="w-4 h-4" />
              </button>
            )}

            {/* Face overlay toggle (default off) */}
            <button
              onClick={() => setFaceOverlayEnabled((prev) => !prev)}
              className={`hover:text-accent p-1 ${faceOverlayEnabled ? "text-accent" : ""}`}
              title={faceOverlayEnabled ? "Hide face boxes on video" : "Show face boxes on video"}
            >
              {faceOverlayEnabled ? <Eye className="w-4 h-4" /> : <EyeOff className="w-4 h-4" />}
            </button>

            <button onClick={toggleFullscreen} className="hover:text-accent p-1">
              {fullscreen ? <Minimize className="w-4 h-4" /> : <Maximize className="w-4 h-4" />}
            </button>
          </div>
        </div>
      </div>

      {/* Big play button overlay when paused */}
      {!playing && (
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
          <div className="bg-black/40 rounded-full p-4">
            <Play className="w-12 h-12 text-white" />
          </div>
        </div>
      )}
    </div>
  );
}

function detectionColor(className: string) {
  const normalized = className.trim().toLowerCase();
  if (normalized === "face") return "#22c55e";
  if (normalized === "person" || normalized === "body") return "#38bdf8";
  if (normalized === "hand") return "#f59e0b";
  if (normalized === "text") return "#a855f7";

  let hash = 0;
  for (let index = 0; index < normalized.length; index += 1) {
    hash = ((hash << 5) - hash) + normalized.charCodeAt(index);
    hash |= 0;
  }

  const hue = Math.abs(hash) % 360;
  return `hsl(${hue} 80% 55%)`;
}

function formatDetectionBadge(detection: Detection) {
  const confidence = Math.round(detection.score * 100);
  const refText = detection.refKind && detection.refId != null
    ? ` · ${detection.refKind} #${detection.refId}`
    : "";
  return `${detection.class} ${confidence}%${refText}`;
}

// Scene Scrubber / Timeline Component
function SceneScrubber({ 
  sceneId, 
  duration, 
  spans,
  rawSegments,
  detections,
  onSeek,
  currentTime,
  profileName,
}: { 
  sceneId: number; 
  duration: number; 
  spans: Pick<ResolvedSpan, "spanKey" | "startSec" | "endSec" | "tagName" | "kind" | "colorHint" | "sourceKey" | "lane">[];
  rawSegments: Pick<Segment, "id" | "startSec" | "endSec" | "title" | "kind" | "sourceKey">[];
  detections: Pick<Detection, "id" | "observedAtSec" | "class" | "score" | "refKind" | "refId">[];
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
  const faceLanes = useMemo(() => buildTimelineLanes(
    rawSegments
      .filter((segment) => isFaceTimelineSegment(segment))
      .map((segment) => ({
        key: String(segment.id),
        startSec: segment.startSec,
        endSec: segment.endSec ?? segment.startSec + 0.05,
        label: segment.title?.trim() || segment.kind || "Face",
      })),
  ), [rawSegments]);
  const visibleResolvedLanes = showAllResolvedLanes ? segmentLanes : segmentLanes.slice(0, 4);
  const visibleFaceLanes = showAllFaceLanes ? faceLanes : faceLanes.slice(0, 2);
  const hiddenResolvedLaneCount = Math.max(0, segmentLanes.length - visibleResolvedLanes.length);
  const hiddenFaceLaneCount = Math.max(0, faceLanes.length - visibleFaceLanes.length);

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
          <div className="flex items-center justify-between gap-3 border-b border-black/20 bg-[#211d27] px-2 py-1.5 text-[10px] uppercase tracking-[0.16em] text-white/55">
            <span>Resolved spans · {profileName ?? "Resolved"} · {segmentLanes.length} lane{segmentLanes.length === 1 ? "" : "s"}</span>
            {segmentLanes.length > 4 ? (
              <button
                type="button"
                onClick={() => setShowAllResolvedLanes((value) => !value)}
                className="rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
              >
                {showAllResolvedLanes ? "Collapse" : `Show all ${segmentLanes.length}`}
              </button>
            ) : null}
          </div>
          {segmentLanes.length > 0 ? (
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
          )}
          {hiddenResolvedLaneCount > 0 ? (
            <div className="border-t border-black/20 px-2 py-1 text-[10px] text-white/45">
              {hiddenResolvedLaneCount} additional lane{hiddenResolvedLaneCount === 1 ? "" : "s"} hidden until expanded.
            </div>
          ) : null}
        </div>
      )}
      {faceLanes.length > 0 && (
        <div className="border-b border-black/20 bg-[#1c2f28]">
          <div className="flex items-center justify-between gap-3 border-b border-black/20 bg-[#16261f] px-2 py-1.5 text-[10px] uppercase tracking-[0.16em] text-white/55">
            <span>Faces over time · {faceLanes.length} lane{faceLanes.length === 1 ? "" : "s"}</span>
            {faceLanes.length > 2 ? (
              <button
                type="button"
                onClick={() => setShowAllFaceLanes((value) => !value)}
                className="rounded border border-white/10 px-2 py-0.5 text-[9px] text-white/70 transition-colors hover:border-white/30 hover:text-white"
              >
                {showAllFaceLanes ? "Collapse" : `Show all ${faceLanes.length}`}
              </button>
            ) : null}
          </div>
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

function buildFaceTrackWindowMap(segments: Pick<Segment, "kind" | "payload" | "startSec" | "endSec">[]) {
  const windows = new Map<string, { startSec: number; endSec: number }>();

  for (const segment of segments) {
    if (segment.kind?.trim().toLowerCase() !== "face") {
      continue;
    }

    const trackKey = readPayloadString(segment.payload, "trackKey");
    if (!trackKey) {
      continue;
    }

    const startSec = segment.startSec;
    const endSec = Math.max(segment.endSec ?? segment.startSec, startSec);
    const existing = windows.get(trackKey);
    if (!existing) {
      windows.set(trackKey, { startSec, endSec });
      continue;
    }

    existing.startSec = Math.min(existing.startSec, startSec);
    existing.endSec = Math.max(existing.endSec, endSec);
  }

  return windows;
}

function selectActiveDetectionAtTime(
  detections: Detection[],
  currentTime: number,
  faceTrackWindows: Map<string, { startSec: number; endSec: number }>,
) {
  if (!detections.length) {
    return null;
  }

  const ordered = [...detections].sort((left, right) => (left.observedAtSec ?? Number.NEGATIVE_INFINITY) - (right.observedAtSec ?? Number.NEGATIVE_INFINITY) || left.id - right.id);
  const trackKey = ordered[0].groupKey;
  const isFaceTrack = !!trackKey && ordered.some((detection) => detection.refKind?.trim().toLowerCase() === "face");
  if (isFaceTrack) {
    const trackWindow = faceTrackWindows.get(trackKey);
    if (trackWindow) {
      for (let index = 0; index < ordered.length; index += 1) {
        const detection = ordered[index];
        const startSec = index === 0
          ? Math.min(trackWindow.startSec, detection.observedAtSec ?? trackWindow.startSec)
          : detection.observedAtSec ?? trackWindow.startSec;
        const nextObservedAt = ordered[index + 1]?.observedAtSec;
        const endSec = Math.max(nextObservedAt ?? trackWindow.endSec, startSec);
        const isLast = index === ordered.length - 1;
        if (currentTime < startSec) {
          continue;
        }

        if ((isLast && currentTime <= endSec) || (!isLast && currentTime < endSec)) {
          return detection;
        }
      }

      return null;
    }
  }

  const toleranceSec = 0.5;
  let nearestDetection: Detection | null = null;
  let nearestDelta = Number.POSITIVE_INFINITY;
  for (const detection of ordered) {
    const observedAt = detection.observedAtSec;
    const delta = Math.abs((observedAt ?? currentTime) - currentTime);
    if (observedAt != null && delta > toleranceSec) {
      continue;
    }

    if (delta < nearestDelta) {
      nearestDelta = delta;
      nearestDetection = detection;
    }
  }

  return nearestDetection;
}

function readPayloadString(payload: unknown, key: string) {
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) {
    return null;
  }

  const value = (payload as Record<string, unknown>)[key];
  return typeof value === "string" && value.trim().length > 0 ? value : null;
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
  const [rating, setRating] = useState<number | undefined>(scene.rating ?? undefined);
  const [urls, setUrls] = useState(scene.urls.length > 0 ? scene.urls : [""]);
  const [studioId, setStudioId] = useState<number | undefined>(scene.studioId ?? undefined);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(scene.tags.map((t) => t.id));
  const [selectedPerformerIds, setSelectedPerformerIds] = useState<number[]>(scene.performers.map((p) => p.id));
  const [selectedGalleryIds, setSelectedGalleryIds] = useState<number[]>(scene.galleries.map((g) => g.id));
  const [selectedGroups, setSelectedGroups] = useState<{ groupId: number; sceneIndex: number }[]>(
    scene.groups.map((g) => ({ groupId: g.id, sceneIndex: g.sceneIndex }))
  );
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
    setDirector(scene.director || ""); setDate(scene.date || ""); setRating(scene.rating ?? undefined);
    setUrls(scene.urls.length > 0 ? scene.urls : [""]); setStudioId(scene.studioId ?? undefined);
    setSelectedTagIds(scene.tags.map((t) => t.id)); setSelectedPerformerIds(scene.performers.map((p) => p.id));
    setSelectedGalleryIds(scene.galleries.map((g) => g.id));
    setSelectedGroups(scene.groups.map((g) => ({ groupId: g.id, sceneIndex: g.sceneIndex })));
  }, [scene]);

  const mutation = useMutation({
    mutationFn: (data: SceneUpdate) => scenes.update(scene.id, data),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["scene", scene.id] }); queryClient.invalidateQueries({ queryKey: ["scenes"] }); onSaved(); },
  });

  const handleSave = () => {
    const urlList = urls.map((url) => url.trim()).filter(Boolean);
    mutation.mutate({ title: title || undefined, code: code || undefined, details: details || undefined,
      director: director || undefined, date: date || undefined, rating, studioId,
      urls: urlList, tagIds: selectedTagIds, performerIds: selectedPerformerIds, galleryIds: selectedGalleryIds, groups: selectedGroups });
  };

  const filteredTags = allTags?.items.filter((t) => !selectedTagIds.includes(t.id) && t.name.toLowerCase().includes(tagSearch.toLowerCase())) ?? [];
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
        <div className="flex flex-wrap gap-1 mb-1">
          {selectedTags.map((t) => <span key={t.id} className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-accent/20 text-accent">{t.name}<button onClick={() => setSelectedTagIds(selectedTagIds.filter((id) => id !== t.id))} className="hover:text-white">×</button></span>)}
        </div>
        <input value={tagSearch} onChange={(e) => setTagSearch(e.target.value)} placeholder="Search tags…" className={inputCls} />
        {tagSearch && filteredTags.length > 0 && <div className="max-h-24 overflow-y-auto bg-surface rounded border border-border">{filteredTags.slice(0, 10).map((t) => <button key={t.id} onClick={() => { setSelectedTagIds([...selectedTagIds, t.id]); setTagSearch(""); }} className="block w-full text-left px-3 py-1 text-sm text-foreground hover:bg-card">{t.name}</button>)}</div>}
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
