import { useEffect, useMemo, useRef, useState, type MouseEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Headphones, Mic2, MonitorPlay, PlayCircle } from "lucide-react";
import { audios, system } from "../api/client";
import { createFromUrlWithOptionalDownload, mergeUrlLists, NoDownloaderFoundError, type UrlDownloadMode } from "../utils/createFromUrlDownload";
import type { Audio, AudioCreate, AudioFilterCriteria, DownloaderMatch, EntityEngagement } from "../api/types";
import { BookmarkButton } from "../components/BookmarkButton";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { CreateModalActions, EditModal, Field, TextArea, TextInput } from "../components/EditModal";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../components/RouteCardLinkOverlay";
import { CustomFieldsEditor, formatDuration } from "../components/shared";
import { EntityReferencePopovers } from "../components/EntityCards";
import { useAuth } from "../auth/AuthContext";
import { canWriteEntity } from "../auth/visibility";
import { useEntityEngagementBatch } from "../hooks/useEntityEngagementBatch";
import { useListUrlState } from "../hooks/useListUrlState";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { getDefaultFilter } from "../components/SavedFilterMenu";
import { getAudioDisplayTitle } from "../utils/audioTextDisplay";
import { FileBackedCreateSource, type CreateSourceMode } from "../components/FileBackedCreateSource";
import { useFileBackedCreatePreferences } from "../hooks/useFileBackedCreatePreferences";
import { StudioSelector } from "../components/StudioSelector";
import { StringListEditor } from "../components/StringListEditor";
import { BulkSelectionActions } from "../components/BulkSelectionActions";
import { SourceDownloadDialog } from "../components/SourceDownloadDialog";
import { AUDIO_CRITERIA } from "../components/FilterDialog";

const SORT_OPTIONS = [
  { value: "updatedAt", label: "Updated At" },
  { value: "createdAt", label: "Created At" },
  { value: "date", label: "Date" },
  { value: "duration", label: "Duration" },
  { value: "title", label: "Title" },
];

interface Props {
  onNavigate: (route: any) => void;
}

export function AudiosPage({ onNavigate }: Props) {
  const [showCreate, setShowCreate] = useState(false);
  const defaultState = useMemo(() => {
    const savedFilter = getDefaultFilter("audios");
    return {
      filter: savedFilter?.findFilter ?? { page: 1, perPage: 40, sort: "updatedAt", direction: "desc" },
      objectFilter: savedFilter?.objectFilter ?? {},
      displayMode: "grid" as DisplayMode,
    };
  }, []);

  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode } = useListUrlState({
    resetKey: "audios",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list"] as const,
  });

  const hasObjectFilter = Object.keys(objectFilter).length > 0;

  const { data, isLoading } = useQuery({
    queryKey: ["audios", filter, objectFilter],
    queryFn: () => hasObjectFilter
      ? audios.findFiltered({ findFilter: filter, objectFilter: objectFilter as AudioFilterCriteria })
      : audios.find(filter),
  });

  const items = data?.items ?? [];
  const { engagementById } = useEntityEngagementBatch("audio", items.map((item) => item.id));
  const { selectedIds, toggle, selectAll, selectNone, invertSelection } = useMultiSelect(items);
  const selecting = selectedIds.size > 0;
  const { hasPermission } = useAuth();
  const canWriteAudio = canWriteEntity("audio", hasPermission);

  return (
    <>
    {showCreate ? <AudioCreateModal open={showCreate} onClose={() => setShowCreate(false)} onCreated={(id) => onNavigate({ page: "audio", id })} /> : null}
    <ListPage
      title="Audios"
      pageKey="audios"
      filterMode="audios"
      filter={filter}
      onFilterChange={setFilter}
      totalCount={data?.totalCount ?? 0}
      isLoading={isLoading}
      searchPlaceholder="Filter audio..."
      sortOptions={SORT_OPTIONS}
      displayMode={displayMode}
      onDisplayModeChange={setDisplayMode}
      availableDisplayModes={["grid", "list"]}
      onNew={canWriteAudio ? () => setShowCreate(true) : undefined}
      criteriaDefinitions={AUDIO_CRITERIA}
      objectFilter={objectFilter}
      onObjectFilterChange={setObjectFilter}
      selectedIds={selectedIds}
      onSelectAll={selectAll}
      onSelectNone={selectNone}
      onInvertSelection={invertSelection}
      selectionActions={<BulkSelectionActions entityType="audios" selectedIds={selectedIds} onDone={selectNone} audioItems={items} downloadItems={items} onNavigate={onNavigate} />}
    >
      {items.length === 0 && !isLoading ? (
        <div className="rounded-lg border border-dashed border-border bg-card/70 px-6 py-10 text-sm text-muted">
          No audio items matched the current filter.
        </div>
      ) : (
        displayMode === "list" ? (
          <AudioListTable audios={items} engagementById={engagementById} selectedIds={selectedIds} selecting={selecting} onToggle={toggle} onNavigate={onNavigate} />
        ) : (
        <EntityCardGrid minCardWidth="280px">
          {items.map((audio) => (
            <AudioCard
              key={audio.id}
              audio={audio}
              engagement={engagementById.get(audio.id)}
              selected={selectedIds.has(audio.id)}
              selecting={selecting}
              onSelect={() => toggle(audio.id)}
              onClick={() => selecting ? toggle(audio.id) : onNavigate({ page: "audio", id: audio.id })}
              onNavigate={onNavigate}
            />
          ))}
        </EntityCardGrid>
        )
      )}
    </ListPage>
    </>
  );
}

function AudioCreateModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: number) => void }) {
  const queryClient = useQueryClient();
  const [sourceMode, setSourceMode] = useState<CreateSourceMode>("metadata");
  const [filePath, setFilePath] = useState("");
  const [url, setUrl] = useState("");
  const { urlDownloadMode, setUrlDownloadMode, scrapeMetadata, setScrapeMetadata } = useFileBackedCreatePreferences("Audio");
  const [noDownloaderFound, setNoDownloaderFound] = useState(false);
  const [sourceDownload, setSourceDownload] = useState<{ sourceUrl: string; data: AudioCreate; matches: DownloaderMatch[]; autoApplyMetadata: boolean } | null>(null);
  const [title, setTitle] = useState("");
  const [code, setCode] = useState("");
  const [date, setDate] = useState("");
  const [details, setDetails] = useState("");
  const [studioId, setStudioId] = useState<number | undefined>(undefined);
  const [urls, setUrls] = useState<string[]>([""]);
  const [organized, setOrganized] = useState(false);
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({});
  const [createAnother, setCreateAnother] = useState(false);

  const resetForm = () => {
    setSourceMode("metadata");
    setFilePath("");
    setUrl("");
    setNoDownloaderFound(false);
    setTitle("");
    setCode("");
    setDate("");
    setDetails("");
    setStudioId(undefined);
    setUrls([""]);
    setOrganized(false);
    setCustomFields({});
  };

  const buildPayload = (extraUrls: string[] = []): AudioCreate => ({
    title: title.trim() || undefined,
    code: code.trim() || undefined,
    date: date || undefined,
    details: details.trim() || undefined,
    studioId,
    organized,
    urls: mergeUrlLists(urls, extraUrls),
    customFields: Object.keys(customFields).length > 0 ? customFields : undefined,
  });

  const handleCreated = (created?: Audio) => {
    queryClient.invalidateQueries({ queryKey: ["audios"] });
    resetForm();
    if (createAnother) return;
    onClose();
    if (created?.id) onCreated(created.id);
  };

  const createMutation = useMutation({
    mutationFn: (data: AudioCreate) => audios.create(data),
    onSuccess: handleCreated,
  });

  const fileMutation = useMutation({
    mutationFn: async ({ path, data }: { path: string; data: AudioCreate }) => {
      const created = await audios.createFromFile({ filePath: path });
      return created?.id ? audios.update(created.id, data) : created;
    },
    onSuccess: handleCreated,
  });

  const downloadMutation = useMutation({
    mutationFn: async ({ requestedUrl, data, downloadMode, scrapeMetadata }: { requestedUrl: string; data: AudioCreate; downloadMode: UrlDownloadMode; scrapeMetadata: boolean }) => {
      if (downloadMode === "now") {
        const matches = (await system.matchDownloaders({ url: requestedUrl }))
          .filter((match) => match.supportedEntity.toLowerCase() === "audio");

        if (matches.length > 1) {
          setSourceDownload({ sourceUrl: requestedUrl, data, matches, autoApplyMetadata: scrapeMetadata });
          return null;
        }

        if (matches.length === 0) {
          throw new NoDownloaderFoundError(requestedUrl);
        }
      }

      return createFromUrlWithOptionalDownload({ requestedUrl, data, entity: "Audio", downloadMode, scrapeMetadata, create: audios.create });
    },
    onSuccess: (created) => {
      if (!created) return;
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      handleCreated(created);
    },
    onError: (err) => {
      if (err instanceof NoDownloaderFoundError) setNoDownloaderFound(true);
    },
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
    if (requestedUrl) createMutation.mutate(buildPayload([requestedUrl]));
  };

  const handleSave = () => {
    if (sourceMode === "file") {
      const path = filePath.trim();
      if (path) fileMutation.mutate({ path, data: buildPayload() });
      return;
    }
    if (sourceMode === "url") {
      const requestedUrl = url.trim();
      if (requestedUrl) downloadMutation.mutate({ requestedUrl, data: buildPayload(), downloadMode: urlDownloadMode, scrapeMetadata });
      return;
    }
    createMutation.mutate(buildPayload());
  };

  const pending = createMutation.isPending || fileMutation.isPending || downloadMutation.isPending;
  const error = (createMutation.error ?? fileMutation.error ?? downloadMutation.error) as Error | null;
  const visibleError = error instanceof NoDownloaderFoundError ? null : error;
  return (
    <>
    <EditModal title="Create Audio" open={open} onClose={onClose}>
      <FileBackedCreateSource mode={sourceMode} onModeChange={handleSourceModeChange} filePath={filePath} onFilePathChange={setFilePath} url={url} onUrlChange={handleUrlChange} urlDownloadMode={urlDownloadMode} onUrlDownloadModeChange={setUrlDownloadMode} scrapeMetadata={scrapeMetadata} onScrapeMetadataChange={setScrapeMetadata} noDownloaderFound={noDownloaderFound} onCreateWithoutDownload={handleCreateWithoutDownload} onDismissNoDownloader={() => setNoDownloaderFound(false)} modes={["metadata", "file", "url"]} filePlaceholder="C:\\Media\\audio.mp3" urlPlaceholder="https://example.com/audio.mp3" />

      <div className="grid grid-cols-2 gap-4">
        <Field label="Title"><TextInput value={title} onChange={setTitle} placeholder="Audio title" /></Field>
        <Field label="Date"><input type="date" value={date} onChange={(event) => setDate(event.target.value)} className="w-full rounded border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none" /></Field>
      </div>
      <Field label="Studio Code"><TextInput value={code} onChange={setCode} placeholder="Audio code" /></Field>
      <Field label="Details"><TextArea value={details} onChange={setDetails} placeholder="Audio notes" rows={3} /></Field>
      <Field label="Studio"><StudioSelector value={studioId} onChange={setStudioId} /></Field>
      <Field label="URLs"><StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" /></Field>
      <label className="mb-4 flex items-center gap-2 text-sm text-secondary">
        <input type="checkbox" checked={organized} onChange={(event) => setOrganized(event.target.checked)} className="rounded border-border bg-card" />
        Organized
      </label>
      <Field label="Custom Fields"><CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="audio" /></Field>
      {visibleError ? (
        <div className="mb-4 rounded border border-red-700 bg-red-900/50 p-2 text-sm text-red-300">
          {visibleError.message}
        </div>
      ) : null}
      <CreateModalActions loading={pending} onCancel={onClose} onSave={handleSave} createAnother={createAnother} onCreateAnotherChange={setCreateAnother} />
    </EditModal>
    {sourceDownload ? (
      <SourceDownloadDialog
        open
        entity="Audio"
        sourceUrl={sourceDownload.sourceUrl}
        matches={sourceDownload.matches}
        baseTitle={sourceDownload.data.title}
        metadata={sourceDownload.data}
        autoApplyMetadata={sourceDownload.autoApplyMetadata}
        onClose={() => setSourceDownload(null)}
        onQueued={() => {
          queryClient.invalidateQueries({ queryKey: ["jobs"] });
          queryClient.invalidateQueries({ queryKey: ["audios"] });
          setSourceDownload(null);
          resetForm();
          if (!createAnother) onClose();
        }}
      />
    ) : null}
    </>
  );
}

function AudioListTable({ audios: items, engagementById, selectedIds, selecting, onToggle, onNavigate }: { audios: Audio[]; engagementById: ReadonlyMap<number, EntityEngagement>; selectedIds: Set<number>; selecting: boolean; onToggle: (id: number) => void; onNavigate: (route: any) => void }) {
  return (
    <div className="overflow-x-auto rounded-lg border border-border bg-card">
      <table className="min-w-full divide-y divide-border text-sm">
        <thead className="bg-surface text-left text-xs uppercase text-muted">
          <tr>
            <th className="w-10 px-3 py-2" />
            <th className="px-3 py-2">Title</th>
            <th className="px-3 py-2">Studio</th>
            <th className="px-3 py-2">Duration</th>
            <th className="px-3 py-2">Files</th>
            <th className="px-3 py-2">Entities</th>
            <th className="px-3 py-2">Listened</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {items.map((audio) => {
            const title = getAudioDisplayTitle(audio);
            const duration = audio.maxDuration > 0 ? formatDuration(audio.maxDuration) : "";
            const engagement = engagementById.get(audio.id);
            return (
              <tr key={audio.id} onClick={() => selecting ? onToggle(audio.id) : onNavigate({ page: "audio", id: audio.id })} className={`cursor-pointer hover:bg-surface/70 ${selectedIds.has(audio.id) ? "bg-accent/10" : ""}`}>
                <td className="px-3 py-2">
                  <input
                    type="checkbox"
                    checked={selectedIds.has(audio.id)}
                    onChange={() => onToggle(audio.id)}
                    onClick={(event) => event.stopPropagation()}
                    className="rounded border-border bg-card"
                    aria-label={`Select ${title}`}
                  />
                </td>
                <td className="min-w-[18rem] px-3 py-2">
                  <div className="font-medium text-foreground">{title}</div>
                  {audio.details ? <div className="mt-0.5 line-clamp-1 max-w-xl text-xs text-secondary">{audio.details}</div> : null}
                  {audio.files.length === 0 && audio.urls.length > 0 ? <div className="mt-1 text-xs text-cyan-300">Download available</div> : null}
                </td>
                <td className="px-3 py-2 text-secondary">
                  <EntityReferencePopovers studio={{ id: audio.studioId, name: audio.studioName }} onNavigate={onNavigate} />
                </td>
                <td className="px-3 py-2 text-secondary">{duration}</td>
                <td className="px-3 py-2 text-secondary">{audio.fileCount}</td>
                <td className="px-3 py-2 text-secondary">
                  <EntityReferencePopovers performers={audio.performers} tags={audio.tags} groups={audio.groups} onNavigate={onNavigate} />
                </td>
                <td className="px-3 py-2 text-secondary">{engagement?.playDuration ? formatDuration(engagement.playDuration) : ""}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function AudioCard({
  audio,
  engagement,
  selected,
  onSelect,
  selecting,
  onClick,
  onNavigate,
}: {
  audio: Audio;
  engagement?: EntityEngagement;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
  onClick: () => void;
  onNavigate: (route: any) => void;
}) {
  const title = getAudioDisplayTitle(audio);
  const duration = audio.maxDuration > 0 ? formatDuration(audio.maxDuration) : null;
  const route = { page: "audio", id: audio.id };
  const audioRef = useRef<HTMLAudioElement>(null);
  const hoverTimerRef = useRef<number | null>(null);
  const canPreview = !selecting && !selected;

  const playPreview = () => {
    const element = audioRef.current;
    if (!element) return;
    element.currentTime = 0;
    element.volume = 0.35;
    element.play().catch(() => {});
  };

  const stopPreview = () => {
    if (hoverTimerRef.current !== null) {
      window.clearTimeout(hoverTimerRef.current);
      hoverTimerRef.current = null;
    }
    const element = audioRef.current;
    if (!element) return;
    element.pause();
    element.currentTime = 0;
  };

  const schedulePreview = (event: MouseEvent<HTMLElement>) => {
    if (!canPreview || (event.target as HTMLElement).closest("[data-audio-preview-ignore]")) return;
    if (hoverTimerRef.current !== null) window.clearTimeout(hoverTimerRef.current);
    hoverTimerRef.current = window.setTimeout(() => {
      hoverTimerRef.current = null;
      playPreview();
    }, 1000);
  };

  useEffect(() => {
    if (!canPreview) stopPreview();
    return () => {
      if (hoverTimerRef.current !== null) window.clearTimeout(hoverTimerRef.current);
    };
  }, [canPreview]);

  return (
    <article onClick={selecting ? onClick : undefined} onMouseEnter={schedulePreview} onMouseLeave={stopPreview} className={`entity-card group relative flex h-full cursor-pointer flex-col overflow-hidden rounded-lg border bg-card text-left transition-colors ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}>
      <RouteCardLinkOverlay route={route} onClick={onClick} label={`Open ${title}`} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} />
      <div className="relative flex aspect-video items-center justify-center overflow-hidden bg-surface">
        {audio.imagePath ? (
          <img src={audio.imagePath} alt={title} className="h-full w-full object-cover" loading="lazy" onError={(event) => { (event.currentTarget as HTMLImageElement).style.display = "none"; }} />
        ) : (
          <Headphones className="h-12 w-12 text-muted opacity-50" />
        )}
        <audio ref={audioRef} src={audios.streamUrl(audio.id)} preload="none" />
        {(selected !== undefined || selecting) ? <div data-audio-preview-ignore onMouseEnter={stopPreview}><CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} /></div> : null}
        {!selecting ? (
          <BookmarkButton hostType="audio" hostId={audio.id} compact deferUntilHover className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100" />
        ) : null}
        {audio.hasVideoFiles ? (
          <span className="absolute right-1 top-1 z-[5] inline-flex items-center gap-1 rounded bg-black/70 px-1.5 py-0.5 text-[10px] font-medium text-white">
            <MonitorPlay className="h-3 w-3" />
            Video
          </span>
        ) : null}
        {duration ? <span className="absolute bottom-1 right-1 z-[5] rounded bg-black/70 px-1.5 py-0.5 text-xs text-white">{duration}</span> : null}
      </div>

      <div className="card-body flex flex-1 flex-col gap-2 border-t border-border/50 p-2.5">
        <div className="flex min-h-0 flex-1 flex-col">
          <h2 className="card-title line-clamp-2 font-semibold text-foreground transition-colors group-hover:text-accent">{title}</h2>
          <div data-audio-preview-ignore className="mt-auto pt-2">
            <EntityReferencePopovers
              studio={{ id: audio.studioId, name: audio.studioName }}
              performers={audio.performers}
              tags={audio.tags}
              groups={audio.groups}
              onNavigate={onNavigate}
            />
          </div>
          {audio.details ? <p className="mt-1 line-clamp-2 text-xs leading-snug text-muted">{audio.details}</p> : null}
        </div>

        <div className="flex flex-wrap gap-1.5 text-[11px] text-muted">
          {engagement?.playCount ? (
            <span className="inline-flex items-center gap-1 rounded border border-border/80 px-1.5 py-0.5">
              <PlayCircle className="h-3 w-3" />
              {engagement.playCount} play{engagement.playCount === 1 ? "" : "s"}
            </span>
          ) : null}
          {audio.tracks.length > 0 ? (
            <span className="inline-flex items-center gap-1 rounded border border-border/80 px-1.5 py-0.5">
              <Mic2 className="h-3 w-3" />
              {audio.tracks.length} track{audio.tracks.length === 1 ? "" : "s"}
            </span>
          ) : null}
        </div>
      </div>
    </article>
  );
}

