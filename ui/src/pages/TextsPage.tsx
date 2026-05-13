import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookOpenText, FileText } from "lucide-react";
import { system, texts } from "../api/client";
import type { DownloaderMatch, TextCreate, TextDocument, TextFilterCriteria } from "../api/types";
import { BookmarkButton } from "../components/BookmarkButton";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { CreateModalActions, EditModal, Field, TextArea, TextInput } from "../components/EditModal";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../components/RouteCardLinkOverlay";
import { CustomFieldsEditor } from "../components/shared";
import { EntityReferencePopovers } from "../components/EntityCards";
import { useAuth } from "../auth/AuthContext";
import { canWriteEntity } from "../auth/visibility";
import { useListUrlState } from "../hooks/useListUrlState";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { getDefaultFilter } from "../components/SavedFilterMenu";
import { getTextDisplayTitle, pickPrimaryTextFile } from "../utils/audioTextDisplay";
import { FileBackedCreateSource, type CreateSourceMode } from "../components/FileBackedCreateSource";
import { StudioSelector } from "../components/StudioSelector";
import { StringListEditor } from "../components/StringListEditor";
import { BulkSelectionActions } from "../components/BulkSelectionActions";
import { createFromUrlWithOptionalDownload, mergeUrlLists, NoDownloaderFoundError, type UrlDownloadMode } from "../utils/createFromUrlDownload";
import { useFileBackedCreatePreferences } from "../hooks/useFileBackedCreatePreferences";
import { SourceDownloadDialog } from "../components/SourceDownloadDialog";
import { TEXT_CRITERIA } from "../components/FilterDialog";

const SORT_OPTIONS = [
  { value: "updatedAt", label: "Updated At" },
  { value: "createdAt", label: "Created At" },
  { value: "date", label: "Date" },
  { value: "words", label: "Words" },
  { value: "pages", label: "Pages" },
  { value: "title", label: "Title" },
];

interface Props {
  onNavigate: (route: any) => void;
}

export function TextsPage({ onNavigate }: Props) {
  const [showCreate, setShowCreate] = useState(false);
  const defaultState = useMemo(() => {
    const savedFilter = getDefaultFilter("texts");
    return {
      filter: savedFilter?.findFilter ?? { page: 1, perPage: 40, sort: "updatedAt", direction: "desc" },
      objectFilter: savedFilter?.objectFilter ?? {},
      displayMode: "grid" as DisplayMode,
    };
  }, []);

  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode } = useListUrlState({
    resetKey: "texts",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list"] as const,
  });

  const hasObjectFilter = Object.keys(objectFilter).length > 0;

  const { data, isLoading } = useQuery({
    queryKey: ["texts", filter, objectFilter],
    queryFn: () => hasObjectFilter
      ? texts.findFiltered({ findFilter: filter, objectFilter: objectFilter as TextFilterCriteria })
      : texts.find(filter),
  });

  const items = data?.items ?? [];
  const { selectedIds, toggle, selectAll, selectNone, invertSelection } = useMultiSelect(items);
  const selecting = selectedIds.size > 0;
  const { hasPermission } = useAuth();
  const canWriteText = canWriteEntity("text", hasPermission);

  return (
    <>
    {showCreate ? <TextCreateModal open={showCreate} onClose={() => setShowCreate(false)} onCreated={(id) => onNavigate({ page: "text", id })} /> : null}
    <ListPage
      title="Texts"
      pageKey="texts"
      filterMode="texts"
      filter={filter}
      onFilterChange={setFilter}
      totalCount={data?.totalCount ?? 0}
      isLoading={isLoading}
      searchPlaceholder="Search text, tags, performers..."
      sortOptions={SORT_OPTIONS}
      displayMode={displayMode}
      onDisplayModeChange={setDisplayMode}
      availableDisplayModes={["grid", "list"]}
      onNew={canWriteText ? () => setShowCreate(true) : undefined}
      criteriaDefinitions={TEXT_CRITERIA}
      objectFilter={objectFilter}
      onObjectFilterChange={setObjectFilter}
      selectedIds={selectedIds}
      onSelectAll={selectAll}
      onSelectNone={selectNone}
      onInvertSelection={invertSelection}
      selectionActions={<BulkSelectionActions entityType="texts" selectedIds={selectedIds} onDone={selectNone} textItems={items} downloadItems={items} />}
    >
      {items.length === 0 && !isLoading ? (
        <div className="rounded-lg border border-dashed border-border bg-card/70 px-6 py-10 text-sm text-muted">
          No text documents matched the current filter.
        </div>
      ) : (
        displayMode === "list" ? (
          <TextListTable texts={items} selectedIds={selectedIds} selecting={selecting} onToggle={toggle} onNavigate={onNavigate} />
        ) : (
        <EntityCardGrid minCardWidth="300px">
          {items.map((text) => (
            <TextCard
              key={text.id}
              text={text}
              selected={selectedIds.has(text.id)}
              selecting={selecting}
              onSelect={() => toggle(text.id)}
              onClick={() => selecting ? toggle(text.id) : onNavigate({ page: "text", id: text.id })}
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

function TextCreateModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: number) => void }) {
  const queryClient = useQueryClient();
  const [sourceMode, setSourceMode] = useState<CreateSourceMode>("metadata");
  const [filePath, setFilePath] = useState("");
  const [url, setUrl] = useState("");
  const { urlDownloadMode, setUrlDownloadMode, scrapeMetadata, setScrapeMetadata } = useFileBackedCreatePreferences("Text");
  const [noDownloaderFound, setNoDownloaderFound] = useState(false);
  const [sourceDownload, setSourceDownload] = useState<{ sourceUrl: string; data: TextCreate; matches: DownloaderMatch[]; autoApplyMetadata: boolean } | null>(null);
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

  const buildPayload = (extraUrls: string[] = []): TextCreate => ({
    title: title.trim() || undefined,
    code: code.trim() || undefined,
    date: date || undefined,
    details: details.trim() || undefined,
    studioId,
    organized,
    urls: mergeUrlLists(urls, extraUrls),
    customFields: Object.keys(customFields).length > 0 ? customFields : undefined,
  });

  const handleCreated = (created?: TextDocument) => {
    queryClient.invalidateQueries({ queryKey: ["texts"] });
    resetForm();
    if (createAnother) return;
    onClose();
    if (created?.id) onCreated(created.id);
  };

  const createMutation = useMutation({
    mutationFn: (data: TextCreate) => texts.create(data),
    onSuccess: handleCreated,
  });

  const fileMutation = useMutation({
    mutationFn: async ({ path, data }: { path: string; data: TextCreate }) => {
      const created = await texts.createFromFile({ filePath: path });
      return created?.id ? texts.update(created.id, data) : created;
    },
    onSuccess: handleCreated,
  });

  const downloadMutation = useMutation({
    mutationFn: async ({ requestedUrl, data, downloadMode, scrapeMetadata }: { requestedUrl: string; data: TextCreate; downloadMode: UrlDownloadMode; scrapeMetadata: boolean }) => {
      if (downloadMode === "now") {
        const matches = (await system.matchDownloaders({ url: requestedUrl }))
          .filter((match) => match.supportedEntity.toLowerCase() === "text");

        if (matches.length > 1) {
          setSourceDownload({ sourceUrl: requestedUrl, data, matches, autoApplyMetadata: scrapeMetadata });
          return null;
        }

        if (matches.length === 0) {
          throw new NoDownloaderFoundError(requestedUrl);
        }
      }

      return createFromUrlWithOptionalDownload({ requestedUrl, data, entity: "Text", downloadMode, scrapeMetadata, create: texts.create });
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
    <EditModal title="Create Text" open={open} onClose={onClose}>
      <FileBackedCreateSource mode={sourceMode} onModeChange={handleSourceModeChange} filePath={filePath} onFilePathChange={setFilePath} url={url} onUrlChange={handleUrlChange} urlDownloadMode={urlDownloadMode} onUrlDownloadModeChange={setUrlDownloadMode} scrapeMetadata={scrapeMetadata} onScrapeMetadataChange={setScrapeMetadata} noDownloaderFound={noDownloaderFound} onCreateWithoutDownload={handleCreateWithoutDownload} onDismissNoDownloader={() => setNoDownloaderFound(false)} modes={["metadata", "file", "url"]} filePlaceholder="C:\\Media\\document.txt" urlPlaceholder="https://example.com/document.txt" />

      <div className="grid grid-cols-2 gap-4">
        <Field label="Title"><TextInput value={title} onChange={setTitle} placeholder="Text title" /></Field>
        <Field label="Date"><input type="date" value={date} onChange={(event) => setDate(event.target.value)} className="w-full rounded border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none" /></Field>
      </div>
      <Field label="Studio Code"><TextInput value={code} onChange={setCode} placeholder="Text code" /></Field>
      <Field label="Details"><TextArea value={details} onChange={setDetails} placeholder="Text notes" rows={3} /></Field>
      <Field label="Studio"><StudioSelector value={studioId} onChange={setStudioId} /></Field>
      <Field label="URLs"><StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" /></Field>
      <label className="mb-4 flex items-center gap-2 text-sm text-secondary">
        <input type="checkbox" checked={organized} onChange={(event) => setOrganized(event.target.checked)} className="rounded border-border bg-card" />
        Organized
      </label>
      <Field label="Custom Fields"><CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="text" /></Field>
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
        entity="Text"
        sourceUrl={sourceDownload.sourceUrl}
        matches={sourceDownload.matches}
        baseTitle={sourceDownload.data.title}
        metadata={sourceDownload.data}
        autoApplyMetadata={sourceDownload.autoApplyMetadata}
        onClose={() => setSourceDownload(null)}
        onQueued={() => {
          queryClient.invalidateQueries({ queryKey: ["jobs"] });
          queryClient.invalidateQueries({ queryKey: ["texts"] });
          setSourceDownload(null);
          resetForm();
          if (!createAnother) onClose();
        }}
      />
    ) : null}
    </>
  );
}

function TextListTable({ texts: items, selectedIds, selecting, onToggle, onNavigate }: { texts: TextDocument[]; selectedIds: Set<number>; selecting: boolean; onToggle: (id: number) => void; onNavigate: (route: any) => void }) {
  const numberFormat = new Intl.NumberFormat();
  return (
    <div className="overflow-x-auto rounded-lg border border-border bg-card">
      <table className="min-w-full divide-y divide-border text-sm">
        <thead className="bg-surface text-left text-xs uppercase text-muted">
          <tr>
            <th className="w-10 px-3 py-2" />
            <th className="px-3 py-2">Title</th>
            <th className="px-3 py-2">Studio</th>
            <th className="px-3 py-2">Words</th>
            <th className="px-3 py-2">Pages</th>
            <th className="px-3 py-2">Files</th>
            <th className="px-3 py-2">Entities</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {items.map((text) => {
            const title = getTextDisplayTitle(text);
            const primaryFile = pickPrimaryTextFile(text);
            const preview = primaryFile?.excerptText?.trim() || text.details?.trim();
            return (
              <tr key={text.id} onClick={() => selecting ? onToggle(text.id) : onNavigate({ page: "text", id: text.id })} className={`cursor-pointer hover:bg-surface/70 ${selectedIds.has(text.id) ? "bg-accent/10" : ""}`}>
                <td className="px-3 py-2">
                  <input
                    type="checkbox"
                    checked={selectedIds.has(text.id)}
                    onChange={() => onToggle(text.id)}
                    onClick={(event) => event.stopPropagation()}
                    className="rounded border-border bg-card"
                    aria-label={`Select ${title}`}
                  />
                </td>
                <td className="min-w-[18rem] px-3 py-2">
                  <div className="font-medium text-foreground">{title}</div>
                  {preview ? <div className="mt-0.5 line-clamp-1 max-w-xl text-xs text-secondary">{preview}</div> : null}
                  {text.files.length === 0 && text.urls.length > 0 ? <div className="mt-1 text-xs text-cyan-300">Download available</div> : null}
                </td>
                <td className="px-3 py-2 text-secondary">
                  <EntityReferencePopovers studio={{ id: text.studioId, name: text.studioName }} onNavigate={onNavigate} />
                </td>
                <td className="px-3 py-2 text-secondary">{text.maxWordCount ? numberFormat.format(text.maxWordCount) : ""}</td>
                <td className="px-3 py-2 text-secondary">{text.maxPageCount ? numberFormat.format(text.maxPageCount) : ""}</td>
                <td className="px-3 py-2 text-secondary">{text.fileCount}</td>
                <td className="px-3 py-2 text-secondary">
                  <EntityReferencePopovers performers={text.performers} tags={text.tags} groups={text.groups} onNavigate={onNavigate} />
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function TextCard({
  text,
  selected,
  onSelect,
  selecting,
  onClick,
  onNavigate,
}: {
  text: TextDocument;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
  onClick: () => void;
  onNavigate: (route: any) => void;
}) {
  const title = getTextDisplayTitle(text);
  const primaryFile = pickPrimaryTextFile(text);
  const preview = primaryFile?.excerptText?.trim() || text.details?.trim() || "Open the document to read the extracted content and file details.";
  const route = { page: "text", id: text.id };

  return (
    <article onClick={selecting ? onClick : undefined} className={`entity-card group relative flex h-full cursor-pointer flex-col overflow-hidden rounded-lg border bg-card text-left transition-colors ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}>
      <RouteCardLinkOverlay route={route} onClick={onClick} label={`Open ${title}`} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} />
      <div className="relative flex aspect-video items-center justify-center overflow-hidden bg-surface">
        {text.imagePath ? (
          <img src={text.imagePath} alt={title} className="h-full w-full object-cover" loading="lazy" onError={(event) => { (event.currentTarget as HTMLImageElement).style.display = "none"; }} />
        ) : (
          <FileText className="h-12 w-12 text-muted opacity-50" />
        )}
        {(selected !== undefined || selecting) ? <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} /> : null}
        {!selecting ? (
          <BookmarkButton hostType="text" hostId={text.id} compact deferUntilHover className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100" />
        ) : null}
        {text.maxWordCount ? <span className="absolute bottom-1 right-1 z-[5] rounded bg-black/70 px-1.5 py-0.5 text-xs text-white">{Intl.NumberFormat().format(text.maxWordCount)} words</span> : null}
      </div>

      <div className="card-body flex flex-1 flex-col gap-2 border-t border-border/50 p-2.5">
        <div className="flex min-h-0 flex-1 flex-col">
          <h2 className="card-title line-clamp-2 font-semibold text-foreground transition-colors group-hover:text-accent">{title}</h2>
          <p className="mt-1 line-clamp-3 text-xs leading-snug text-muted">{preview}</p>
          <div className="mt-auto pt-2">
            <EntityReferencePopovers
              studio={{ id: text.studioId, name: text.studioName }}
              performers={text.performers}
              tags={text.tags}
              groups={text.groups}
              onNavigate={onNavigate}
              className="w-full justify-center"
            />
          </div>
        </div>

        <div className="flex flex-wrap gap-1.5 text-[11px] text-muted">
          {text.maxPageCount ? (
            <span className="inline-flex items-center gap-1 rounded border border-border/80 px-1.5 py-0.5">
              <BookOpenText className="h-3 w-3" />
              {text.maxPageCount} page{text.maxPageCount === 1 ? "" : "s"}
            </span>
          ) : null}
        </div>
      </div>
    </article>
  );
}
