import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { galleries } from "../api/client";
import type { EntityEngagement, FindFilter, Gallery, GalleryCreate, GalleryFilterCriteria } from "../api/types";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { InteractiveRatingField, RatingBanner } from "../components/Rating";
import { EditModal, Field, TextInput, TextArea, SaveButton } from "../components/EditModal";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { useEntityEngagementBatch } from "../hooks/useEntityEngagementBatch";
import { FolderOpen, Image, Users, Tag, Trash2, Loader2, Edit, Box, Film, Check, Search, Download } from "lucide-react";
import { PopoverButton, ScenesPopoverContent, ImagesPopoverContent } from "../components/EntityCards";
import { GALLERY_CRITERIA } from "../components/FilterDialog";
import { BulkEditDialog, GALLERY_BULK_FIELDS } from "../components/BulkEditDialog";
import { getDefaultFilter } from "../components/SavedFilterMenu";
import { useListUrlState } from "../hooks/useListUrlState";
import { createNestedRouteLinkProps, createRouteLinkProps } from "../components/cardNavigation";
import { CardSelectionToggle } from "../components/RouteCardLinkOverlay";
import { useWallColumns } from "../hooks/useWallColumns";
import { GALLERY_SORT_OPTIONS } from "../components/gallerySortOptions";
import { WallMediaCard } from "../components/WallMediaCard";
import { BatchDownloadOptionsDialog } from "../components/BatchDownloadOptionsDialog";
import { GalleryDownloadDialog } from "../components/GalleryDownloadDialog";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canWriteEntity } from "../auth/visibility";
import {
  formatBatchDownloadSummary,
  getBatchDownloadOptionsStorageKey,
  getUndownloadedSelectionItems,
  loadStoredBatchDownloadOptions,
  queueBatchDownloads,
  saveStoredBatchDownloadOptions,
  type BatchDownloadOptions,
} from "../utils/batchDownloads";

interface Props {
  onNavigate: (r: any) => void;
}

export function GalleriesPage({ onNavigate }: Props) {
  const defaultState = useMemo(() => {
    const savedFilter = getDefaultFilter("galleries");
    return {
      filter: savedFilter?.findFilter ?? { page: 1, perPage: 40, direction: "desc" },
      objectFilter: savedFilter?.objectFilter ?? {},
      displayMode: "grid" as DisplayMode,
    };
  }, []);
  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode } = useListUrlState({
    resetKey: "galleries",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list", "wall"] as const,
  });
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [showCreate, setShowCreate] = useState(false);
  const [wallColumnCount, setWallColumnCount] = useState(5);
  const [downloadTarget, setDownloadTarget] = useState<Gallery | "new" | null>(null);
  const [showBatchDownloadOptions, setShowBatchDownloadOptions] = useState(false);
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const canWriteGallery = canWriteEntity("gallery", hasPermission);
  const canDeleteGallery = canDeleteEntity("gallery", hasPermission);
  const canDownloadGallery = hasPermission("jobs.run") && canWriteGallery;

  const hasObjectFilter = Object.keys(objectFilter).length > 0;
  const { data, isLoading } = useQuery({
    queryKey: ["galleries", filter, objectFilter],
    queryFn: () =>
      hasObjectFilter
        ? galleries.findFiltered({ findFilter: filter, objectFilter: objectFilter as GalleryFilterCriteria })
        : galleries.find(filter),
  });

  const items = data?.items ?? [];
  const { engagementById } = useEntityEngagementBatch("gallery", items.map((item) => item.id));
  const wallColumns = useWallColumns(items, wallColumnCount);
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(items);
  const selecting = selectedIds.size > 0;
  const selectedGallery = selectedIds.size === 1 ? items.find((gallery) => selectedIds.has(gallery.id)) : undefined;
  const selectedDownloadTargets = useMemo(() => getUndownloadedSelectionItems(items, selectedIds), [items, selectedIds]);
  const canDownloadSelectedGallery = canDownloadGallery && selectedDownloadTargets.length > 0;
  const batchDownloadStorageKey = getBatchDownloadOptionsStorageKey("page-galleries");
  const [batchDownloadOptions, setBatchDownloadOptions] = useState<BatchDownloadOptions>(() => loadStoredBatchDownloadOptions(batchDownloadStorageKey));

  const bulkDeleteMut = useMutation({
    mutationFn: () => galleries.bulkDelete([...selectedIds]),
    onSuccess: () => { selectNone(); queryClient.invalidateQueries({ queryKey: ["galleries"] }); },
  });

  const bulkEditMut = useMutation({
    mutationFn: (values: Record<string, unknown>) =>
      galleries.bulkUpdate({ ids: [...selectedIds], ...values } as any),
    onSuccess: () => {
      setShowBulkEdit(false);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["galleries"] });
    },
  });

  const batchDownloadMut = useMutation({
    mutationFn: async (options: BatchDownloadOptions) => queueBatchDownloads("Gallery", selectedDownloadTargets, options),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      queryClient.invalidateQueries({ queryKey: ["jobs-active"] });
      queryClient.invalidateQueries({ queryKey: ["jobs-history"] });
      queryClient.invalidateQueries({ queryKey: ["galleries"] });
      window.alert(formatBatchDownloadSummary("gallery", result));
      selectNone();
    },
    onError: (error: Error) => {
      window.alert(error.message || "Failed to queue the selected downloads.");
    },
  });

  return (
    <>
    <GalleryCreateModal open={showCreate} onClose={() => setShowCreate(false)} onCreated={(id) => onNavigate({ page: "gallery", id })} />
    <GalleryDownloadDialog
      open={downloadTarget !== null}
      gallery={downloadTarget && downloadTarget !== "new" ? downloadTarget : undefined}
      onClose={() => setDownloadTarget(null)}
      onNavigate={onNavigate}
    />
    <BatchDownloadOptionsDialog
      open={showBatchDownloadOptions}
      entity="Gallery"
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
    <ListPage
      title="Galleries"
      pageKey="galleries"
      filterMode="galleries"
      filter={filter}
      onFilterChange={setFilter}
      totalCount={data?.totalCount ?? 0}
      isLoading={isLoading}
      sortOptions={GALLERY_SORT_OPTIONS}
      displayMode={displayMode}
      onDisplayModeChange={setDisplayMode}
      availableDisplayModes={["grid", "list", "wall"]}
      renderOperations={canDownloadGallery ? () => (
        <button
          onClick={() => setDownloadTarget("new")}
          className="rounded-lg border border-border px-3 py-1 text-xs font-medium text-foreground hover:border-accent hover:text-accent"
        >
          From URL
        </button>
      ) : undefined}
      criteriaDefinitions={GALLERY_CRITERIA}
      objectFilter={objectFilter}
      onObjectFilterChange={setObjectFilter}
      onNew={canWriteGallery ? () => setShowCreate(true) : undefined}
      wallColumnCount={wallColumnCount}
      onWallColumnCountChange={setWallColumnCount}

      selectedIds={selectedIds}
      onSelectAll={selectAll}
      onSelectNone={selectNone}
      selectionActions={
        <>
          {canDownloadSelectedGallery && (
            <button
              onClick={() => {
                if (selectedDownloadTargets.length > 1 || !selectedGallery) {
                  setShowBatchDownloadOptions(true);
                  return;
                }

                setDownloadTarget(selectedGallery);
              }}
              disabled={batchDownloadMut.isPending}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-cyan-400 hover:text-cyan-300 hover:bg-cyan-900/20 disabled:opacity-60"
            >
              {batchDownloadMut.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Download className="w-3 h-3" />}
              Download
            </button>
          )}
          {canWriteGallery && (
            <button
              onClick={() => setShowBulkEdit(true)}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
            >
              <Edit className="w-3 h-3" />
              Edit
            </button>
          )}
          {canDeleteGallery && (
            <button
              onClick={() => { if (confirm(`Delete ${selectedIds.size} gallery(s)?`)) bulkDeleteMut.mutate(); }}
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
      {displayMode === "grid" ? (
        <EntityCardGrid minCardWidth="var(--card-min-width, 200px)">
          {items.map((g) => (
            <GalleryCard key={g.id} gallery={g} engagement={engagementById.get(g.id)} onClick={() => selecting ? toggle(g.id) : onNavigate({ page: "gallery", id: g.id })} onNavigate={onNavigate} selected={selectedIds.has(g.id)} onSelect={() => toggle(g.id)} selecting={selecting} />
          ))}
        </EntityCardGrid>
      ) : displayMode === "list" ? (
        <GalleryListTable galleries={items} engagementById={engagementById} onNavigate={onNavigate} selectedIds={selectedIds} onToggle={toggle} selecting={selecting} />
      ) : (
        <div className="flex gap-2 px-2">
          {wallColumns.map((column, columnIndex) => (
            <div key={columnIndex} className="flex min-w-0 flex-1 flex-col gap-2">
              {column.map((gallery) => (
                <GalleryWallCard
                  key={gallery.id}
                  gallery={gallery}
                  engagement={engagementById.get(gallery.id)}
                  onClick={() => selecting ? toggle(gallery.id) : onNavigate({ page: "gallery", id: gallery.id })}
                  selected={selectedIds.has(gallery.id)}
                  onSelect={() => toggle(gallery.id)}
                  selecting={selecting}
                />
              ))}
            </div>
          ))}
        </div>
      )}
      {items.length === 0 && (
        <div className="text-center text-secondary py-16">
          <FolderOpen className="w-12 h-12 mx-auto mb-3 opacity-50" />
          <p>No galleries found</p>
        </div>
      )}
    </ListPage>
    <BulkEditDialog
      open={showBulkEdit}
      onClose={() => setShowBulkEdit(false)}
      title="Edit Galleries"
      selectedCount={selectedIds.size}
      fields={GALLERY_BULK_FIELDS}
      onApply={(values) => bulkEditMut.mutate(values)}
      isPending={bulkEditMut.isPending}
    />
    </>
  );
}

function GalleryWallCard({ gallery, engagement, onClick, selected, onSelect, selecting }: { gallery: Gallery; engagement?: EntityEngagement; onClick: () => void; selected?: boolean; onSelect?: () => void; selecting?: boolean }) {
  const rating = engagement?.rating;
  return (
    <WallMediaCard
      onClick={onClick}
      title={gallery.title || "Untitled"}
      imageSrc={gallery.coverPath}
      aspectRatio="16 / 9"
      fallback={<FolderOpen className="w-10 h-10 text-muted opacity-30" />}
      className={`${selected ? "border-accent ring-2 ring-accent" : ""} group`.trim()}
    >
      <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
      <RatingBanner rating={rating} />
      {gallery.studioName && (
        <div className="absolute top-1 right-1 text-xs bg-black/70 px-1.5 py-0.5 rounded text-white truncate max-w-[80%]">
          {gallery.studioName}
        </div>
      )}
    </WallMediaCard>
  );
}

function GalleryCard({ gallery, engagement, onClick, onNavigate, selected, onSelect, selecting }: { gallery: Gallery; engagement?: EntityEngagement; onClick: () => void; onNavigate?: (r: any) => void; selected?: boolean; onSelect?: () => void; selecting?: boolean }) {
  const rating = engagement?.rating;
  const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "gallery", id: gallery.id }, onClick);
  const cardContent = (
    <>
      <div className="aspect-video bg-surface flex items-center justify-center relative overflow-hidden">
        {gallery.coverPath ? (
          <img src={gallery.coverPath} alt={gallery.title || ""} className="w-full h-full object-cover" loading="lazy" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
        ) : (
          <FolderOpen className="w-10 h-10 text-muted opacity-30" />
        )}
        <RatingBanner rating={rating} />
        {gallery.studioName && (
          <div className="absolute top-1 right-1 text-xs bg-black/70 px-1.5 py-0.5 rounded text-white truncate max-w-[80%]">
            {gallery.studioName}
          </div>
        )}
      </div>
      <div className="card-body border-t border-border/50 p-2">
        <h3 className="font-medium text-sm truncate text-foreground">{gallery.title || "Untitled"}</h3>
        {gallery.date && <div className="text-xs text-secondary">{gallery.date}</div>}
      </div>
    </>
  );

  return (
    <div onClick={selecting ? onClick : undefined} className={`entity-card bg-card rounded overflow-hidden border hover:border-accent/60 transition-all cursor-pointer group relative ${selected ? "border-accent ring-2 ring-accent" : "border-border"}`}>
      <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
      {selecting ? (
        cardContent
      ) : (
        <a {...linkProps} aria-label={`Open gallery ${gallery.title || "Untitled"}`} className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-accent">
          {cardContent}
        </a>
      )}
      <GalleryCardPopovers gallery={gallery} onNavigate={onNavigate} />
    </div>
  );
}

function GalleryCardPopovers({ gallery, onNavigate }: { gallery: Gallery; onNavigate?: (r: any) => void }) {
  const hasAny = gallery.imageCount > 0 || gallery.performers.length > 0 || gallery.tags.length > 0 || gallery.sceneCount > 0 || gallery.organized;
  if (!hasAny) return null;

  return (
    <div className="relative z-10 flex items-center justify-center gap-1 px-2 pb-2 border-t border-border/50 pt-1.5">
      {gallery.imageCount > 0 && (
        <PopoverButton icon={<Image className="w-3 h-3" />} count={gallery.imageCount} title="Images" wide preferBelow>
          <ImagesPopoverContent filter={{ galleryId: gallery.id }} />
        </PopoverButton>
      )}
      {gallery.tags.length > 0 && (
        <PopoverButton icon={<Tag className="w-3.5 h-3.5" />} count={gallery.tags.length} title="Tags" preferBelow>
          <div className="flex flex-wrap gap-1">
            {gallery.tags.map((t: any) => {
              const linkProps = createNestedRouteLinkProps<HTMLAnchorElement>({ page: "tag", id: t.id }, () => onNavigate?.({ page: "tag", id: t.id }));

              return <a key={t.id} {...linkProps}
                className="text-[11px] text-accent hover:underline cursor-pointer px-1.5 py-0.5 rounded bg-card border border-border hover:border-accent/40 transition-colors whitespace-nowrap">
                {t.name}
              </a>;
            })}
          </div>
        </PopoverButton>
      )}
      {gallery.performers.length > 0 && (
        <PopoverButton icon={<Users className="w-3.5 h-3.5" />} count={gallery.performers.length} title="Performers" wide preferBelow>
          <div className="grid grid-cols-2 gap-2">
            {gallery.performers.map((p: any) => {
              const linkProps = createNestedRouteLinkProps<HTMLAnchorElement>({ page: "performer", id: p.id }, () => onNavigate?.({ page: "performer", id: p.id }));

              return <a key={p.id} {...linkProps}
                className="flex flex-col items-center gap-1 text-center cursor-pointer rounded hover:bg-card-hover p-1.5 transition-colors">
                <span className="text-xs text-accent hover:underline truncate w-full">{p.name}</span>
              </a>;
            })}
          </div>
        </PopoverButton>
      )}
      {gallery.sceneCount > 0 && (
        <PopoverButton icon={<Film className="w-3 h-3" />} count={gallery.sceneCount} title="Scenes" wide preferBelow>
          <ScenesPopoverContent filter={{ galleryId: gallery.id }} />
        </PopoverButton>
      )}
      {gallery.organized && (
        <span className="text-muted" title="Organized">
          <Box className="w-3 h-3" />
        </span>
      )}
    </div>
  );
}

function GalleryListTable({ galleries: items, engagementById, onNavigate, selectedIds, onToggle, selecting }: { galleries: Gallery[]; engagementById: ReadonlyMap<number, EntityEngagement>; onNavigate: (r: any) => void; selectedIds?: Set<number>; onToggle?: (id: number) => void; selecting?: boolean }) {
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b border-border text-left text-muted text-xs">
          {selectedIds && <th className="w-8 py-2 px-3"></th>}
          <th className="py-2 px-3">Title</th>
          <th className="py-2 px-3">Studio</th>
          <th className="py-2 px-3">Date</th>
          <th className="py-2 px-3 text-right">Images</th>
          <th className="py-2 px-3 text-right">Rating</th>
        </tr>
      </thead>
      <tbody>
        {items.map((g) => (
          <tr key={g.id} onClick={() => selecting ? onToggle?.(g.id) : onNavigate({ page: "gallery", id: g.id })} className={`border-b border-border hover:bg-card cursor-pointer ${selectedIds?.has(g.id) ? "bg-accent/10" : ""}`}>
            {selectedIds && <td className="py-2 px-3"><input type="checkbox" checked={selectedIds.has(g.id)} onChange={() => onToggle?.(g.id)} onClick={(e) => e.stopPropagation()} className="w-3.5 h-3.5 rounded border-border cursor-pointer accent-accent" /></td>}
            <td className="py-2 px-3 text-foreground">{g.title || "Untitled"}</td>
            <td className="py-2 px-3 text-secondary">{g.studioName ?? ""}</td>
            <td className="py-2 px-3 text-secondary">{g.date ?? ""}</td>
            <td className="py-2 px-3 text-secondary text-right">{g.imageCount}</td>
            <td className="py-2 px-3 text-secondary text-right">{engagementById.get(g.id)?.rating ?? ""}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/* ── Gallery Create Modal ── */
function GalleryCreateModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: number) => void }) {
  const qc = useQueryClient();
  const [form, setForm] = useState({
    title: "",
    code: "",
    date: "",
    details: "",
    photographer: "",
    rating: undefined as number | undefined,
  });

  const mutation = useMutation({
    mutationFn: (data: GalleryCreate) => galleries.create(data),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["galleries"] });
      setForm({ title: "", code: "", date: "", details: "", photographer: "", rating: undefined });
      onClose();
      if (created?.id) onCreated(created.id);
    },
  });

  const save = () => {
    const title = form.title.trim();
    if (!title) return;
    mutation.mutate({
      title,
      code: form.code || undefined,
      date: form.date || undefined,
      details: form.details || undefined,
      photographer: form.photographer || undefined,
      rating: form.rating,
    });
  };

  return (
    <EditModal title="Create Gallery" open={open} onClose={onClose}>
      <Field label="Title">
        <TextInput value={form.title} onChange={(v) => setForm({ ...form, title: v })} />
      </Field>
      <Field label="Studio Code">
        <TextInput value={form.code} onChange={(v) => setForm({ ...form, code: v })} />
      </Field>
      <Field label="Date">
        <TextInput value={form.date} onChange={(v) => setForm({ ...form, date: v })} placeholder="YYYY-MM-DD" />
      </Field>
      <Field label="Photographer">
        <TextInput value={form.photographer} onChange={(v) => setForm({ ...form, photographer: v })} />
      </Field>
      <Field label="Details">
        <TextArea value={form.details} onChange={(v) => setForm({ ...form, details: v })} rows={3} />
      </Field>
      <InteractiveRatingField value={form.rating} onChange={(value) => setForm({ ...form, rating: value })} />
      <div className="flex justify-end mt-4">
        <SaveButton loading={mutation.isPending} onClick={save} />
      </div>
    </EditModal>
  );
}
