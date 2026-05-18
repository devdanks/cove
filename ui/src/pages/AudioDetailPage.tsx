import { Suspense, lazy, useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Check, Download, ExternalLink, Eye, FileAudio, Files, FolderOpen, Link2, Mic2, MoreVertical, Rows3, Trash2 } from "lucide-react";
import { audios, fileOps } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { AudioPlayer } from "../components/AudioPlayer";
import { AspectRatingsPanel } from "../components/AspectRatingsPanel";
import { BookmarkButton } from "../components/BookmarkButton";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { MediaDetailLayout } from "../components/MediaDetailLayout/MediaDetailLayout";
import type { MediaDetailTab } from "../components/MediaDetailLayout/types";
import { InteractiveRating } from "../components/Rating";
import { CustomFieldsDisplay, formatDate, formatDuration, formatFileSize } from "../components/shared";
import { EntityReferencePopovers } from "../components/EntityCards";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { VideoPlayer } from "../components/VideoPlayer";
import { trackInteraction } from "../utils/interactionTracking";
import { getAudioDisplayTitle, pickPrimaryAudioFile } from "../utils/audioTextDisplay";
import { AudioEditPanel } from "./AudioEditPanel";

const MediaScrapeDialog = lazy(() => import("../components/MediaScrapeDialog").then((module) => ({ default: module.MediaScrapeDialog })));
const MediaDownloadDialog = lazy(() => import("../components/MediaDownloadDialog").then((module) => ({ default: module.MediaDownloadDialog })));

type AudioTab = "details" | "tracks" | "file-info" | "history" | "edit";

function getMutationErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : error ? String(error) : null;
}

interface Props {
  id: number;
  onNavigate: (route: any) => void;
}

export function AudioDetailPage({ id, onNavigate }: Props) {
  const queryClient = useQueryClient();
  const { data: audio, isLoading } = useQuery({
    queryKey: ["audio", id],
    queryFn: () => audios.get(id),
  });
  const { hasPermission, user } = useAuth();
  const { backLabel, goBack } = useBackNavigation({ page: "audios" }, onNavigate);
  const [activeTab, setActiveTab] = useState<AudioTab>("details");
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [showOpsMenu, setShowOpsMenu] = useState(false);
  const [showScrapeDialog, setShowScrapeDialog] = useState(false);
  const [showDownloadDialog, setShowDownloadDialog] = useState(false);
  const opsMenuRef = useRef<HTMLDivElement>(null);
  const canReadAudio = canReadEntity("audio", hasPermission);
  const canWriteAudio = canWriteEntity("audio", hasPermission);
  const canDeleteAudio = canDeleteEntity("audio", hasPermission);
  const canReadTags = canReadEntity("tag", hasPermission);
  const canReadPerformers = canReadEntity("performer", hasPermission);
  const canReadGroups = canReadEntity("group", hasPermission);
  const canReadStudio = canReadEntity("studio", hasPermission);
  const canStreamAudio = hasPermission("stream.read");
  const canReadFiles = hasPermission("files.read");
  const trackingEnabled = user?.uiPreferences?.tracking?.enabled ?? true;
  const canEngageAudio = canReadAudio && (user?.kind === "user" || user?.kind === "system");
  const trackAudioActivity = canEngageAudio && trackingEnabled;
  const {
    engagement: audioEngagement,
    rating: audioRating,
    setRating: setAudioRating,
  } = useEntityEngagement("audio", id, {
    enabled: !!audio && canEngageAudio,
    fallbackFavorite: false,
    fallbackRating: undefined,
  });
  const updateAudioMut = useMutation({
    mutationFn: (data: { organized?: boolean }) => audios.update(id, data),
    onSuccess: (updatedAudio) => {
      queryClient.setQueryData(["audio", id], updatedAudio);
      queryClient.invalidateQueries({ queryKey: ["audios"] });
    },
  });
  const deleteAudioMut = useMutation({
    mutationFn: (options?: { deleteFile?: boolean; deleteGenerated?: boolean }) => audios.delete(id, options),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["audios"] });
      goBack();
    },
  });
  const revealFileMutation = useMutation({ mutationFn: (fileId: number) => fileOps.reveal(fileId) });
  const canRevealFiles = typeof window !== "undefined" && ["localhost", "127.0.0.1", "::1"].includes(window.location.hostname);
  const canDownloadAudio = canWriteAudio && hasPermission("jobs.run") && (audio?.files.length ?? 0) === 0 && (audio?.urls.length ?? 0) > 0;

  useEffect(() => {
    if (!audio) {
      return;
    }

    document.title = `${getAudioDisplayTitle(audio)} | Cove`;
    return () => {
      document.title = "Cove";
    };
  }, [audio]);

  useEffect(() => {
    if (!audio || !trackAudioActivity) {
      return;
    }

    trackInteraction({ hostType: "audio", hostId: audio.id, kind: "pageVisit", meta: { source: "audioDetailPage" } });
    queryClient.invalidateQueries({ queryKey: ["engagement", "audio", audio.id] });
  }, [audio, queryClient, trackAudioActivity]);

  const primaryFile = useMemo(() => pickPrimaryAudioFile(audio), [audio]);
  const displayTitle = audio ? getAudioDisplayTitle(audio) : `Audio ${id}`;
  const subtitleText = useMemo(() => {
    if (!audio) {
      return undefined;
    }

    return [audio.performers.map((performer) => performer.name).filter(Boolean).join(", "), audio.studioName, audio.date ? formatDate(audio.date) : null]
      .filter(Boolean)
      .join(" • ") || undefined;
  }, [audio]);
  const detailSubtitle = audio && ((canReadPerformers && audio.performers.length > 0) || (canReadTags && audio.tags.length > 0) || (canReadGroups && audio.groups.length > 0) || (canReadStudio && audio.studioId && audio.studioName) || audio.date) ? (
    <div className="flex flex-wrap items-center gap-2">
      <EntityReferencePopovers
        performers={canReadPerformers ? audio.performers : []}
        tags={canReadTags ? audio.tags : []}
        groups={canReadGroups ? audio.groups : []}
        studio={canReadStudio ? { id: audio.studioId, name: audio.studioName } : null}
        onNavigate={onNavigate}
      />
      {audio.date ? <span className="text-sm text-secondary">{formatDate(audio.date)}</span> : null}
    </div>
  ) : subtitleText;
  const headerImage = audio?.imagePath ? (
    <img src={audio.imagePath} alt={`${displayTitle} cover`} className="h-20 w-20 rounded-2xl border border-border object-cover shadow-lg shadow-black/20" />
  ) : undefined;
  const tabs = useMemo(() => {
    const nextTabs: MediaDetailTab[] = [{ key: "details", label: "Details" }];
    if ((audio?.tracks.length ?? 0) > 0) {
      nextTabs.push({ key: "tracks", label: "Tracks", count: audio?.tracks.length ?? 0 });
    }
    if (canReadFiles && (audio?.files.length ?? 0) > 0) {
      nextTabs.push({ key: "file-info", label: "File Info", count: audio?.files.length ?? 0 });
    }
    nextTabs.push({ key: "history", label: "History" });
    if (canWriteAudio) {
      nextTabs.push({ key: "edit", label: "Edit" });
    }
    return nextTabs;
  }, [audio?.files.length, audio?.groups.length, audio?.performers.length, audio?.studioId, audio?.tags.length, audio?.tracks.length, canReadFiles, canReadGroups, canReadPerformers, canReadStudio, canReadTags, canWriteAudio]);

  useEffect(() => {
    if (!tabs.some((tab) => tab.key === activeTab)) {
      setActiveTab("details");
    }
  }, [activeTab, tabs]);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (opsMenuRef.current && !opsMenuRef.current.contains(event.target as Node)) {
        setShowOpsMenu(false);
      }
    };

    if (showOpsMenu) {
      document.addEventListener("mousedown", handleClickOutside);
    }

    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [showOpsMenu]);

  if (isLoading) {
    return <DetailSkeleton />;
  }

  if (!audio) {
    return <div className="rounded-3xl border border-dashed border-border bg-card/70 px-6 py-10 text-sm text-muted">Audio #{id} was not found.</div>;
  }

  const audioPlayCount = audioEngagement?.playCount ?? 0;
  const audioPlayDuration = audioEngagement?.playDuration ?? 0;
  const audioPageVisitCount = audioEngagement?.pageVisitCount ?? 0;

  const audioMedia = !canStreamAudio ? (
    <div className="flex h-full min-h-[20rem] items-center justify-center rounded-[2rem] border border-dashed border-border bg-card/70 text-sm text-muted">
      Playback is unavailable with your current permissions.
    </div>
  ) : primaryFile ? (
    primaryFile.hasVideoTrack ? (
      <div className="flex h-full min-h-0 w-full items-center justify-center bg-black">
        <VideoPlayer
          streamUrl={audios.streamUrl(audio.id)}
          format={primaryFile.format}
          duration={audio.maxDuration || primaryFile.duration}
          resumeTime={audioEngagement?.resumeTime}
          sceneId={audio.id}
          trackingEnabled={trackAudioActivity}
          playbackTracking={{ hostType: "audio", hostId: audio.id, scopeKey: `audio:${audio.id}` }}
          onEnded={() => queryClient.invalidateQueries({ queryKey: ["engagement", "audio", audio.id] })}
        />
      </div>
    ) : (
      <div className="flex h-full min-h-[45vh] w-full flex-col overflow-hidden bg-[radial-gradient(circle_at_22%_28%,rgba(236,72,153,0.26),transparent_32%),radial-gradient(circle_at_78%_18%,rgba(20,184,166,0.22),transparent_28%),linear-gradient(180deg,rgba(17,24,39,0.52),rgba(5,5,14,0.9))]">
        <div className="flex min-h-0 flex-1 items-center justify-center p-6 sm:p-8">
          {audio.imagePath ? (
            <img
              src={audio.imagePath}
              alt={`${displayTitle} cover`}
              className="max-h-[min(52vh,34rem)] max-w-[min(54vw,42rem)] rounded-xl border border-border object-contain shadow-2xl shadow-black/40"
            />
          ) : (
            <div className="flex h-32 w-32 items-center justify-center rounded-xl border border-border bg-card/55 text-accent shadow-xl shadow-black/25">
              <FileAudio className="h-14 w-14" />
            </div>
          )}
        </div>
        <div className="shrink-0 p-4 sm:p-6">
          <AudioPlayer
            streamUrl={audios.streamUrl(audio.id)}
            format={primaryFile.format}
            title={displayTitle}
            subtitle={subtitleText}
            coverUrl={audio.imagePath}
            duration={audio.maxDuration || primaryFile.duration}
            resumeTime={audioEngagement?.resumeTime}
            trackingEnabled={trackAudioActivity}
            playbackTracking={{ hostType: "audio", hostId: audio.id, scopeKey: `audio:${audio.id}` }}
            onEnded={() => queryClient.invalidateQueries({ queryKey: ["engagement", "audio", audio.id] })}
          />
        </div>
      </div>
    )
  ) : (
    <div className="flex h-full min-h-[20rem] items-center justify-center rounded-[2rem] border border-dashed border-border bg-card/70 text-sm text-muted">
      No playable audio file is available.
    </div>
  );

  return (
    <>
    <MediaDetailLayout
      title={displayTitle}
      subtitle={detailSubtitle}
      backLabel={backLabel}
      onGoBack={goBack}
      headerImage={headerImage}
      media={audioMedia}
      mediaAspectRatio={primaryFile?.hasVideoTrack ? "video" : "auto"}
      mediaFullBleed={primaryFile?.hasVideoTrack ?? false}
      mediaSticky={false}
      tabs={tabs}
      activeTab={activeTab}
      onTabChange={(key) => setActiveTab(key as AudioTab)}
      engagement={{
        primaryContent: <InteractiveRating value={audioRating} onChange={(value) => setAudioRating(value)} readOnly={!canEngageAudio} />,
        additionalMetrics: [
          { label: "Plays", value: audioPlayCount, icon: <Eye className="h-4 w-4" /> },
        ],
      }}
      actions={
        <>
          <BookmarkButton hostType="audio" hostId={audio.id} compact />
          {canWriteAudio ? (
            <button
              type="button"
              onClick={() => { if (!updateAudioMut.isPending) updateAudioMut.mutate({ organized: !audio.organized }); }}
              disabled={updateAudioMut.isPending}
              className={`inline-flex items-center justify-center rounded p-1 transition ${audio.organized ? "bg-green-600 text-white" : "text-secondary hover:bg-card hover:text-foreground"} ${updateAudioMut.isPending ? "cursor-not-allowed opacity-60" : ""}`}
              title={audio.organized ? "Organized" : "Mark organized"}
            >
              <Check className="h-4 w-4" />
            </button>
          ) : audio.organized ? (
            <span className="inline-flex items-center justify-center rounded bg-green-600 p-1 text-white" title="Organized">
              <Check className="h-4 w-4" />
            </span>
          ) : null}
          {canStreamAudio ? (
            <a
              href={audios.streamUrl(audio.id)}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
              title="Open in external player"
            >
              <ExternalLink className="h-4 w-4" />
            </a>
          ) : null}
          {canWriteAudio || canDeleteAudio ? (
            <div className="relative" ref={opsMenuRef}>
              <button
                type="button"
                onClick={() => setShowOpsMenu((current) => !current)}
                className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
                title="More actions"
              >
                <MoreVertical className="h-4 w-4" />
              </button>
              {showOpsMenu ? (
                <div className="absolute right-0 top-full z-50 mt-1 min-w-[220px] rounded border border-border bg-card py-1 shadow-lg">
                  {canWriteAudio ? (
                    <button
                      type="button"
                      onClick={() => {
                        setShowScrapeDialog(true);
                        setShowOpsMenu(false);
                      }}
                      className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground transition-colors hover:bg-surface"
                    >
                      <ExternalLink className="h-3.5 w-3.5" /> Scrape...
                    </button>
                  ) : null}
                  {canDownloadAudio ? (
                    <button
                      type="button"
                      onClick={() => {
                        setShowDownloadDialog(true);
                        setShowOpsMenu(false);
                      }}
                      className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground transition-colors hover:bg-surface"
                    >
                      <Download className="h-3.5 w-3.5" /> Download Media...
                    </button>
                  ) : null}
                  {canDeleteAudio ? (
                    <button
                      type="button"
                      onClick={() => {
                        setConfirmDelete(true);
                        setShowOpsMenu(false);
                      }}
                      className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-red-400 transition-colors hover:bg-surface"
                    >
                      <Trash2 className="h-3.5 w-3.5" /> Delete
                    </button>
                  ) : null}
                </div>
              ) : null}
            </div>
          ) : null}
        </>
      }
    >
      <ConfirmDialog
        open={confirmDelete}
        title="Delete Audio"
        message={`Delete "${displayTitle}"? This cannot be undone.`}
        confirmLabel={deleteAudioMut.isPending ? "Deleting..." : "Delete Audio"}
        onConfirm={(options) => deleteAudioMut.mutate(options)}
        onCancel={() => { deleteAudioMut.reset(); setConfirmDelete(false); }}
        isPending={deleteAudioMut.isPending}
        errorMessage={getMutationErrorMessage(deleteAudioMut.error)}
        showDeleteFile
        showDeleteGenerated
      />
      <MediaDetailLayout.Content>
        {activeTab === "details" ? (
          <div className="space-y-4">
            <MediaDetailLayout.Metadata>
              <DetailGrid
                items={[
                  { label: "Studio", value: audio.studioName },
                  { label: "Date", value: audio.date ? formatDate(audio.date) : undefined },
                  { label: "Duration", value: audio.maxDuration > 0 ? formatDuration(audio.maxDuration) : undefined },
                  { label: "Tracks", value: audio.tracks.length > 0 ? String(audio.tracks.length) : undefined },
                  { label: "Files", value: String(audio.fileCount) },
                ]}
              />
            </MediaDetailLayout.Metadata>
            {audio.details ? (
              <section className="rounded-3xl border border-border bg-card/75 p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Notes</h3>
                <p className="mt-3 whitespace-pre-wrap text-sm leading-7 text-foreground/92">{audio.details}</p>
              </section>
            ) : null}
            {audio.urls.length > 0 ? (
              <section className="rounded-3xl border border-border bg-card/75 p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Source URLs</h3>
                <div className="mt-3 flex flex-col gap-2">
                  {audio.urls.map((url) => (
                    <a key={url} href={url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-2 text-sm text-accent transition hover:text-accent/80">
                      <Link2 className="h-4 w-4" />
                      <span className="truncate">{url}</span>
                    </a>
                  ))}
                </div>
              </section>
            ) : null}
            {(canReadPerformers && audio.performers.length > 0) || (canReadTags && audio.tags.length > 0) || (canReadGroups && audio.groups.length > 0) || (canReadStudio && audio.studioId && audio.studioName) ? (
              <RelatedSection icon={<Rows3 className="h-4 w-4" />} title="Related Entities">
                <EntityReferencePopovers
                  performers={canReadPerformers ? audio.performers : []}
                  tags={canReadTags ? audio.tags : []}
                  groups={canReadGroups ? audio.groups : []}
                  studio={canReadStudio ? { id: audio.studioId, name: audio.studioName } : null}
                  onNavigate={onNavigate}
                />
              </RelatedSection>
            ) : null}
            {audio.customFields && Object.keys(audio.customFields).length > 0 ? (
              <MediaDetailLayout.Metadata>
                <CustomFieldsDisplay customFields={audio.customFields} entityType="audio" />
              </MediaDetailLayout.Metadata>
            ) : null}
            <AspectRatingsPanel hostType="audio" hostId={audio.id} canRate={canEngageAudio} />
          </div>
        ) : null}

        {activeTab === "tracks" ? (
          <section className="rounded-3xl border border-border bg-card/75 p-4">
            <div className="space-y-3">
              {audio.tracks.map((track) => (
                <div key={track.id} className="flex items-center justify-between gap-3 rounded-2xl border border-border/80 bg-background/75 px-4 py-3 text-sm">
                  <div className="min-w-0">
                    <div className="font-medium text-foreground">{track.title?.trim() || `Track ${track.orderIndex + 1}`}</div>
                    <div className="text-xs text-muted">Track {track.orderIndex + 1}</div>
                  </div>
                  <div className="shrink-0 text-xs text-muted">
                    {formatDuration(track.startSec)}
                    {track.endSec != null ? ` - ${formatDuration(track.endSec)}` : ""}
                  </div>
                </div>
              ))}
            </div>
          </section>
        ) : null}

        {activeTab === "file-info" ? (
          <div className="space-y-4">
            {audio.files.map((file) => (
              <MediaDetailLayout.Metadata key={file.id}>
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <h3 className="text-sm font-semibold text-foreground">{file.basename}</h3>
                    <p className="text-xs text-muted">{file.path}</p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    {canRevealFiles && file.id ? (
                      <button
                        type="button"
                        onClick={() => revealFileMutation.mutate(file.id)}
                        className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-secondary hover:border-accent hover:text-foreground"
                      >
                        <FolderOpen className="h-3.5 w-3.5" />
                        Reveal
                      </button>
                    ) : null}
                    <span className="rounded-full border border-border px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.18em] text-muted">{file.format}</span>
                  </div>
                </div>
                <DetailGrid
                  items={[
                    { label: "Duration", value: file.duration > 0 ? formatDuration(file.duration) : undefined },
                    { label: "Codec", value: file.audioCodec || undefined },
                    { label: "Bitrate", value: file.bitRate > 0 ? `${Math.round(file.bitRate / 1000)} kbps` : undefined },
                    { label: "Sample Rate", value: file.sampleRate ? `${Intl.NumberFormat().format(file.sampleRate)} Hz` : undefined },
                    { label: "Channels", value: file.channels ? String(file.channels) : undefined },
                    { label: "Size", value: formatFileSize(file.size) },
                    { label: "Video Track", value: file.hasVideoTrack ? "Yes" : "No" },
                  ]}
                />
              </MediaDetailLayout.Metadata>
            ))}
          </div>
        ) : null}

        {activeTab === "history" ? (
          <MediaDetailLayout.Metadata>
            <DetailGrid
              items={[
                { label: "Plays", value: String(audioPlayCount) },
                { label: "Listened", value: formatDuration(audioPlayDuration) },
                { label: "Page Visits", value: String(audioPageVisitCount) },
              ]}
            />
          </MediaDetailLayout.Metadata>
        ) : null}

        {activeTab === "edit" ? <AudioEditPanel audio={audio} onSaved={() => setActiveTab("details")} /> : null}
      </MediaDetailLayout.Content>
    </MediaDetailLayout>
    {showScrapeDialog ? (
      <Suspense fallback={null}>
        <MediaScrapeDialog
          open={showScrapeDialog}
          onClose={() => setShowScrapeDialog(false)}
          entityType="audio"
          entity={{
            id: audio.id,
            title: audio.title,
            code: audio.code,
            details: audio.details,
            date: audio.date,
            studioName: audio.studioName,
            urls: audio.urls,
            tags: audio.tags,
            performers: audio.performers,
            files: audio.files,
            organized: audio.organized,
          }}
        />
      </Suspense>
    ) : null}
    {showDownloadDialog ? (
      <Suspense fallback={null}>
        <MediaDownloadDialog
          open={showDownloadDialog}
          entity="Audio"
          item={audio}
          listQueryKey="audios"
          detailQueryKey="audio"
          routePage="audio"
          onClose={() => setShowDownloadDialog(false)}
          onNavigate={onNavigate}
        />
      </Suspense>
    ) : null}
    </>
  );
}

function DetailGrid({ items }: { items: { label: string; value?: string }[] }) {
  const visibleItems = items.filter((item) => item.value != null && String(item.value).trim() !== "");
  if (visibleItems.length === 0) {
    return <p className="text-sm text-muted">No metadata available.</p>;
  }

  return (
    <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
      {visibleItems.map((item) => (
        <div key={item.label}>
          <dt className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted">{item.label}</dt>
          <dd className="mt-1 text-sm text-foreground">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function RelatedSection({ icon, title, children }: { icon: React.ReactNode; title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-3xl border border-border bg-card/75 p-5">
      <div className="flex items-center gap-2 text-sm font-semibold uppercase tracking-[0.18em] text-muted">
        {icon}
        {title}
      </div>
      <div className="mt-4 flex flex-wrap gap-2">{children}</div>
    </section>
  );
}