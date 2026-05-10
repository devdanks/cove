import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { galleries, images, scenes, entityImages, fileOps } from "../api/client";
import type { FindFilter } from "../api/types";
import { formatDate, formatDuration, formatFileSize, getResolutionLabel, TagBadge, CustomFieldsDisplay } from "../components/shared";
import { Download, Film, FolderOpen, HardDrive, ImageIcon, Link as LinkIcon, Pencil, Plus, Trash2, UserRound, Check, Loader2, MoreVertical, RefreshCw, Star } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { GalleryEditModal } from "./GalleryEditModal";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { ExtensionSlot } from "../router/RouteRegistry";
import { Lightbox, type LightboxImage } from "../components/Lightbox";
import { InteractiveRating } from "../components/Rating";
import { DetailListToolbar } from "../components/DetailListToolbar";
import { SceneCard, ImageTile } from "../components/EntityCards";
import { EntityHeroLayout } from "../components/EntityHeroLayout";
import { EntityDetailTabs } from "../components/EntityDetailTabs";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { QuickViewDialog } from "../components/QuickViewDialog";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { BulkSelectionActions } from "../components/BulkSelectionActions";
import { useExtensionTabs } from "../components/useExtensionTabs";
import { createRouteLinkProps } from "../components/cardNavigation";
import { getImageDisplayTitle } from "../utils/imageDisplay";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { GalleryDownloadDialog } from "../components/GalleryDownloadDialog";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity, filterItemsByPermission } from "../auth/visibility";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { BookmarkButton } from "../components/BookmarkButton";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type TabKey = "images" | "scenes" | "fileinfo" | (string & {});

export function GalleryDetailPage({ id, onNavigate }: Props) {
  const { hasPermission, user } = useAuth();
  const [imageFilter, setImageFilter] = useState<FindFilter>({ page: 1, perPage: 60, direction: "desc" });
  const { data: gallery, isLoading } = useQuery({
    queryKey: ["gallery", id],
    queryFn: () => galleries.get(id),
  });
  const { data: galleryImages } = useQuery({
    queryKey: ["gallery-images", id, imageFilter],
    queryFn: () => images.find(imageFilter, { galleryId: id }),
    enabled: !!gallery,
  });
  const effectiveImageCount = galleryImages?.totalCount ?? gallery?.imageCount ?? 0;
  const [editing, setEditing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>("images");
  const { allTabs: galleryTabs, renderExtensionTab } = useExtensionTabs("gallery", [
    { key: "images", label: "Images", count: effectiveImageCount },
    { key: "scenes", label: "Scenes" },
    { key: "fileinfo", label: "File Info" },
  ]);
  const [lightboxOpen, setLightboxOpen] = useState(false);
  const [lightboxIndex, setLightboxIndex] = useState(0);
  const [imageZoom, setImageZoom] = useState(0);
  const [sceneFilter, setSceneFilter] = useState<FindFilter>({ page: 1, perPage: 24, direction: "desc" });
  const [showAddImages, setShowAddImages] = useState(false);
  const [showOpsMenu, setShowOpsMenu] = useState(false);
  const [showDownloadDialog, setShowDownloadDialog] = useState(false);
  const opsMenuRef = useRef<HTMLDivElement>(null);
  const queryClient = useQueryClient();
  const { backLabel, goBack } = useBackNavigation({ page: "galleries" }, onNavigate);
  const canWriteGallery = canWriteEntity("gallery", hasPermission);
  const canEngageGallery = canReadEntity("gallery", hasPermission) && (user?.kind === "user" || user?.kind === "system");
  const canDeleteGallery = canDeleteEntity("gallery", hasPermission);
  const canDownloadGallery = hasPermission("jobs.run") && canWriteGallery;
  const canReadGalleryImages = canReadEntity("image", hasPermission);
  const canReadPerformers = canReadEntity("performer", hasPermission);
  const canReadStudios = canReadEntity("studio", hasPermission);
  const canReadTags = canReadEntity("tag", hasPermission);
  const {
    favorite: galleryFavorite,
    rating: galleryRating,
    setFavorite: setGalleryFavorite,
    setRating: setGalleryRating,
    favoritePending: galleryFavoritePending,
  } = useEntityEngagement("gallery", id, {
    enabled: !!gallery,
    fallbackRating: undefined,
  });
  const visibleGalleryTabs = filterItemsByPermission(galleryTabs, {
    images: "images.read",
    scenes: "scenes.read",
    fileinfo: "galleries.read",
  }, hasPermission);

  useEffect(() => {
    if (gallery) document.title = `${gallery.title || `Gallery ${id}`} | Cove`;
    return () => { document.title = "Cove"; };
  }, [gallery, id]);

  // Close ops menu on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (opsMenuRef.current && !opsMenuRef.current.contains(e.target as Node)) setShowOpsMenu(false);
    };
    if (showOpsMenu) document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [showOpsMenu]);

  const galleryKeyboardShortcuts = useMemo(() => ([
    {
      key: "e",
      description: "Edit gallery",
      handler: () => {
        if (canWriteGallery) {
          setEditing(true);
        }
      },
    },
    {
      key: "a",
      description: "Open images tab",
      handler: () => {
        if (canReadGalleryImages) {
          setActiveTab("images");
        }
      },
    },
    {
      key: "s",
      description: "Open scenes tab",
      handler: () => setActiveTab("scenes"),
    },
    {
      key: "f",
      description: "Open file info tab",
      handler: () => setActiveTab("fileinfo"),
    },
  ]), [canReadGalleryImages, canWriteGallery]);

  useEffect(() => {
    if (galleryKeyboardShortcuts.length === 0) return;
    const handler = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const tagName = target?.tagName;
      if (tagName === "INPUT" || tagName === "TEXTAREA" || tagName === "SELECT" || target?.isContentEditable) return;
      const shortcut = galleryKeyboardShortcuts.find((entry) => entry.key === event.key);
      if (!shortcut) return;
      event.preventDefault();
      shortcut.handler();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [galleryKeyboardShortcuts]);

  useEffect(() => {
    if (visibleGalleryTabs.length > 0 && !visibleGalleryTabs.some((tab) => tab.key === activeTab)) {
      setActiveTab(visibleGalleryTabs[0].key);
    }
  }, [activeTab, visibleGalleryTabs]);

  const deleteMut = useMutation({
    mutationFn: () => galleries.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["galleries"] });
      goBack();
    },
  });

  const galleryUpdateMut = useMutation({
    mutationFn: (data: { organized?: boolean }) => galleries.update(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["gallery", id] });
      queryClient.invalidateQueries({ queryKey: ["galleries"] });
    },
  });

  const removeImagesMut = useMutation({
    mutationFn: (imageIds: number[]) => galleries.removeImages(id, imageIds),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["gallery-images", id] });
      queryClient.invalidateQueries({ queryKey: ["gallery", id] });
    },
  });

  const addImagesMut = useMutation({
    mutationFn: (imageIds: number[]) => galleries.addImages(id, imageIds),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["gallery-images", id] });
      queryClient.invalidateQueries({ queryKey: ["gallery", id] });
      setShowAddImages(false);
    },
  });

  const lightboxImages: LightboxImage[] = useMemo(
    () => (galleryImages?.items ?? []).map((img) => ({
      id: img.id,
      src: images.imageUrl(img.id),
      title: img.title,
      interactionSource: "galleryDetailPage",
      interactionMeta: { galleryId: id },
    })),
    [galleryImages, id],
  );

  if (isLoading) {
    return (
      <div className="px-1 py-2">
        <DetailSkeleton />
      </div>
    );
  }

  if (!gallery) {
    return <div className="py-16 text-center text-secondary">Gallery not found</div>;
  }

  const activeContent =
    activeTab === "images"
      ? (
          <GalleryImagesPanel
            galleryId={id}
            filter={imageFilter}
            setFilter={setImageFilter}
            onNavigate={onNavigate}
            galleryImages={galleryImages}
            onShowAddImages={() => setShowAddImages(true)}
            onLightbox={(idx) => { setLightboxIndex(idx); setLightboxOpen(true); }}
            removeImagesMut={removeImagesMut}
            imageZoom={imageZoom}
            setImageZoom={setImageZoom}
            canWriteGallery={canWriteGallery}
          />
        )
      : activeTab === "scenes"
        ? <GalleryScenesPanel galleryId={id} filter={sceneFilter} setFilter={setSceneFilter} onNavigate={onNavigate} />
        : activeTab === "fileinfo"
          ? <GalleryFileInfo gallery={gallery} />
          : renderExtensionTab(activeTab, id, onNavigate);

  return (
    <div className="min-h-screen">
      <GalleryDownloadDialog
        open={showDownloadDialog}
        gallery={gallery}
        onClose={() => setShowDownloadDialog(false)}
        onNavigate={onNavigate}
      />
      <GalleryEditModal gallery={gallery} open={editing} onClose={() => setEditing(false)} />
      <ConfirmDialog
        open={confirmDelete}
        title="Delete Gallery"
        message={`Delete "${gallery.title || "Untitled"}"? This cannot be undone.`}
        onConfirm={() => deleteMut.mutate()}
        onCancel={() => setConfirmDelete(false)}
      />

      <EntityHeroLayout
        backLabel={backLabel}
        onGoBack={goBack}
        imageUrl={gallery.coverPath}
        imageAlt={gallery.title || "Gallery cover"}
        imageFallback={<ImageIcon className="h-14 w-14" />}
        title={gallery.title || "Untitled Gallery"}
        aliases={
          <span className="inline-flex flex-wrap items-center gap-x-3 gap-y-1">
            {gallery.date ? <span>{formatDate(gallery.date)}</span> : null}
            {gallery.studioName && gallery.studioId ? (
              canReadStudios ? (
                <button onClick={() => onNavigate({ page: "studio", id: gallery.studioId })} className="text-accent hover:underline">{gallery.studioName}</button>
              ) : (
                <span>{gallery.studioName}</span>
              )
            ) : null}
            {gallery.photographer ? <span>Photographer: {gallery.photographer}</span> : null}
            {gallery.code ? <span>Code: {gallery.code}</span> : null}
          </span>
        }
        description={gallery.details}
        counts={[
          { key: "images", label: "Images", value: effectiveImageCount, icon: <ImageIcon className="h-4 w-4" /> },
          { key: "scenes", label: "Scenes", value: gallery.sceneCount, icon: <Film className="h-4 w-4" /> },
          { key: "files", label: "Files", value: gallery.files.length, icon: <HardDrive className="h-4 w-4" /> },
        ]}
        metaRow={
          <>
            <span title={`Created ${formatDate(gallery.createdAt)}`}>Updated {formatDate(gallery.updatedAt)}</span>
            {gallery.organized ? (
              <span className="inline-flex items-center gap-1 rounded bg-green-500/15 px-1.5 py-0.5 text-green-300">
                <Check className="h-3 w-3" /> Organized
              </span>
            ) : null}
            <InteractiveRating value={galleryRating} onChange={(value) => setGalleryRating(value)} readOnly={!canEngageGallery} />
          </>
        }
        favorite={canEngageGallery ? galleryFavorite : undefined}
        onFavoriteToggle={canEngageGallery && !galleryFavoritePending ? () => setGalleryFavorite(!galleryFavorite) : undefined}
        actions={
          <>
            <ExtensionSlot slot="gallery-detail-actions" context={{ gallery, onNavigate }} />
            <BookmarkButton hostType="gallery" hostId={gallery.id} />
            {canWriteGallery ? (
              <button
                type="button"
                onClick={() => galleryUpdateMut.mutate({ organized: !gallery.organized })}
                className={`flex items-center gap-1.5 rounded border px-3 py-1.5 text-sm transition-colors ${gallery.organized ? "border-green-500/40 bg-green-600 text-white" : "border-border bg-card text-secondary hover:text-foreground"}`}
                title={gallery.organized ? "Organized" : "Mark organized"}
              >
                <Check className="h-3.5 w-3.5" /> {gallery.organized ? "Organized" : "Organize"}
              </button>
            ) : null}
            {canWriteGallery ? (
              <button
                type="button"
                onClick={() => setEditing(true)}
                className="flex items-center gap-1.5 rounded bg-accent px-3 py-1.5 text-sm text-white hover:bg-accent-hover"
              >
                <Pencil className="h-3.5 w-3.5" /> Edit
              </button>
            ) : null}
            <div className="relative" ref={opsMenuRef}>
              <button
                type="button"
                onClick={() => setShowOpsMenu(!showOpsMenu)}
                aria-label="Open gallery operations"
                className="flex items-center gap-1.5 rounded border border-border bg-card px-3 py-1.5 text-sm text-secondary hover:text-foreground"
                title="Operations"
              >
                <MoreVertical className="h-4 w-4" />
              </button>
              {showOpsMenu ? (
                <div className="absolute right-0 top-full mt-1 z-50 min-w-[180px] rounded border border-border bg-card py-1 shadow-lg">
                  {canWriteGallery ? <button onClick={() => { setEditing(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Pencil className="h-3.5 w-3.5" /> Edit</button> : null}
                  {gallery.files.length === 0 && canDownloadGallery ? <button onClick={() => { setShowDownloadDialog(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><Download className="h-3.5 w-3.5" /> Download Media...</button> : null}
                  <button onClick={() => { setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground hover:bg-surface"><RefreshCw className="h-3.5 w-3.5" /> Rescan</button>
                  {canDeleteGallery ? <div className="my-1 border-t border-border" /> : null}
                  {canDeleteGallery ? <button onClick={() => { setConfirmDelete(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-red-400 hover:bg-surface"><Trash2 className="h-3.5 w-3.5" /> Delete</button> : null}
                </div>
              ) : null}
            </div>
          </>
        }
      >
        {(canReadTags && gallery.tags.length > 0) || gallery.urls.length > 0 || gallery.customFields ? (
          <div className="mb-6 space-y-3 text-sm text-secondary">
            {canReadTags && gallery.tags.length > 0 ? (
              <div className="flex flex-wrap gap-1.5">
                {gallery.tags.map((tag) => (
                  <TagBadge key={tag.id} name={tag.name} tag={tag} provenance={tag.provenance} onClick={() => onNavigate({ page: "tag", id: tag.id })} />
                ))}
              </div>
            ) : null}
            {gallery.urls.length > 0 ? (
              <div className="flex flex-wrap gap-x-4 gap-y-1">
                {gallery.urls.map((url, index) => (
                  <a key={index} href={url} target="_blank" rel="noopener noreferrer" className="flex max-w-xs items-center gap-1 truncate text-accent hover:underline">
                    <LinkIcon className="h-3.5 w-3.5 flex-shrink-0" />{new URL(url).hostname}
                  </a>
                ))}
              </div>
            ) : null}
            <CustomFieldsDisplay customFields={gallery.customFields} entityType="gallery" />
          </div>
        ) : null}

        {canReadPerformers && gallery.performers.length > 0 ? (
          <section className="mb-6 rounded-2xl border border-border bg-card/70 p-5">
            <h2 className="mb-3 text-sm font-semibold uppercase tracking-wide text-muted">Performers</h2>
            <div className="flex flex-wrap justify-center gap-3">
              {gallery.performers.map((performer) => (
                <GalleryPerformerCard key={performer.id} performer={performer} onClick={() => onNavigate({ page: "performer", id: performer.id })} />
              ))}
            </div>
          </section>
        ) : null}

        <EntityDetailTabs tabs={visibleGalleryTabs} activeTab={activeTab} onTabChange={(key) => setActiveTab(key as TabKey)} className="mx-auto mb-4 max-w-7xl" />

        {activeContent}
        <ExtensionSlot slot="gallery-detail-main-bottom" context={{ gallery, onNavigate }} />
      </EntityHeroLayout>

      <ExtensionSlot slot="gallery-detail-bottom" context={{ gallery, onNavigate }} />

      {/* Add Images Dialog */}
      {showAddImages && canWriteGallery && (
        <AddImagesDialog
          galleryId={id}
          existingImageIds={new Set(galleryImages?.items.map((i) => i.id) ?? [])}
          onAdd={(ids) => addImagesMut.mutate(ids)}
          onClose={() => setShowAddImages(false)}
          isPending={addImagesMut.isPending}
        />
      )}

      <Lightbox
        images={lightboxImages}
        initialIndex={lightboxIndex}
        open={lightboxOpen}
        onClose={() => setLightboxOpen(false)}
      />
    </div>
  );
}

function GalleryPerformerCard({ performer, onClick }: { performer: { id: number; name: string; disambiguation?: string; imagePath?: string }; onClick: () => void }) {
  const imageUrl = performer.imagePath || entityImages.performerImageUrl(performer.id);
  const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "performer", id: performer.id }, onClick);

  return (
    <a
      {...linkProps}
      className="w-[180px] overflow-hidden rounded-lg border border-border bg-surface text-left transition-colors hover:border-accent/60"
    >
      <div className="aspect-[2/3] overflow-hidden bg-card">
        <img
          src={imageUrl}
          alt={performer.name}
          className="h-full w-full object-cover"
          onError={(e) => {
            (e.target as HTMLImageElement).style.display = "none";
            const fallback = (e.target as HTMLImageElement).nextElementSibling as HTMLElement | null;
            if (fallback) fallback.style.display = "flex";
          }}
        />
        <div className="hidden h-full w-full items-center justify-center bg-gradient-to-b from-card to-surface">
          <UserRound className="h-12 w-12 text-muted/50" />
        </div>
      </div>
      <div className="p-2 text-center">
        <p className="truncate text-sm font-medium text-foreground">{performer.name}</p>
        {performer.disambiguation && <p className="truncate text-xs text-muted">{performer.disambiguation}</p>}
      </div>
    </a>
  );
}

function GalleryScenesPanel({ galleryId, filter, setFilter, onNavigate }: {
  galleryId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const { data, isLoading } = useQuery({
    queryKey: ["gallery-scenes", galleryId, filter],
    queryFn: () => scenes.find(filter, { galleryId: String(galleryId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<Film className="h-10 w-10" />} message="Loading scenes..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<Film className="h-12 w-12" />} message="No scenes for this gallery" />;

  return (
    <>
      <DetailListToolbar
        filter={filter}
        onFilterChange={setFilter}
        totalCount={data.totalCount}
        sortOptions={[
          { value: "title", label: "Title" },
          { value: "date", label: "Date" },
          { value: "rating", label: "Rating" },
          { value: "created_at", label: "Created At" },
        ]}
        zoomLevel={zoomLevel}
        onZoomChange={setZoomLevel}
        showSearch
        selectedCount={selectedIds.size}
        onSelectAll={selectAll}
        onSelectNone={selectNone}
        selectionActions={<BulkSelectionActions entityType="scenes" selectedIds={selectedIds} onDone={selectNone} sceneItems={data.items} onNavigate={onNavigate} />}
      />
      <EntityCardGrid minCardWidth={`${220 + zoomLevel * 50}px`} gapClassName="gap-4">
        {data.items.map((scene) => (
          <SceneCard key={scene.id} scene={scene} onClick={() => selecting ? toggle(scene.id) : onNavigate({ page: "scene", id: scene.id })} onNavigate={onNavigate} onQuickView={() => setQuickViewId(scene.id)} selected={selectedIds.has(scene.id)} onSelect={() => toggle(scene.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      {quickViewId !== null && (
        <QuickViewDialog type="scene" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      )}
    </>
  );
}

const IMAGE_SORT = [
  { label: "Title", value: "title" },
  { label: "Rating", value: "rating" },
  { label: "Created At", value: "created_at" },
];

function GalleryImagesPanel({ galleryId, filter, setFilter, onNavigate, galleryImages, onShowAddImages, onLightbox, removeImagesMut, imageZoom, setImageZoom, canWriteGallery }: {
  galleryId: number;
  filter: FindFilter;
  setFilter: (f: FindFilter) => void;
  onNavigate: (r: any) => void;
  galleryImages: { items: any[]; totalCount: number } | undefined;
  onShowAddImages: () => void;
  onLightbox: (idx: number) => void;
  removeImagesMut: any;
  imageZoom: number;
  setImageZoom: (z: number) => void;
  canWriteGallery: boolean;
}) {
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(galleryImages?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (!galleryImages) return <EmptyPanel icon={<ImageIcon className="h-12 w-12" />} message="No images in this gallery" />;
  if (galleryImages.items.length === 0) return (
    <>
      {canWriteGallery ? <div className="flex justify-end mb-3">
        <button onClick={onShowAddImages} className="flex items-center gap-1 px-2 py-1 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10 border border-border">
          <Plus className="w-3 h-3" /> Add Images
        </button>
      </div> : null}
      <EmptyPanel icon={<ImageIcon className="h-12 w-12" />} message="No images in this gallery" />
    </>
  );

  return (
    <>
      <DetailListToolbar
        filter={filter}
        onFilterChange={setFilter}
        totalCount={galleryImages.totalCount}
        sortOptions={IMAGE_SORT}
        zoomLevel={imageZoom}
        onZoomChange={setImageZoom}
        showSearch
        selectedCount={selectedIds.size}
        onSelectAll={selectAll}
        onSelectNone={selectNone}
        selectionActions={
          <>
            <BulkSelectionActions entityType="images" selectedIds={selectedIds} onDone={selectNone} downloadItems={galleryImages.items} />
            {canWriteGallery ? <button
              onClick={() => { if (confirm(`Remove ${selectedIds.size} image(s) from gallery?`)) removeImagesMut.mutate([...selectedIds]); }}
              disabled={removeImagesMut.isPending}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-orange-400 hover:text-orange-300 hover:bg-orange-900/20"
            >
              {removeImagesMut.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Trash2 className="w-3 h-3" />}
              Remove from Gallery
            </button> : null}
          </>
        }
      />
      {canWriteGallery ? <div className="flex justify-end mb-2">
        <button onClick={onShowAddImages} className="flex items-center gap-1 px-2 py-1 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10 border border-border">
          <Plus className="w-3 h-3" /> Add Images
        </button>
      </div> : null}
      <EntityCardGrid minCardWidth={`${160 + imageZoom * 50}px`}>
        {galleryImages.items.map((image, idx) => (
          <ImageTile
            key={image.id}
            image={image}
            onClick={() => selecting ? toggle(image.id) : onLightbox(idx)}
            onNavigate={onNavigate}
            onQuickView={() => setQuickViewId(image.id)}
            selected={selectedIds.has(image.id)}
            onSelect={() => toggle(image.id)}
            selecting={selecting}
          />
        ))}
      </EntityCardGrid>
      {quickViewId !== null && (
        <QuickViewDialog type="image" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      )}
    </>
  );
}

function AddImagesDialog({ galleryId, existingImageIds, onAdd, onClose, isPending }: {
  galleryId: number;
  existingImageIds: Set<number>;
  onAdd: (ids: number[]) => void;
  onClose: () => void;
  isPending: boolean;
}) {
  const [searchFilter, setSearchFilter] = useState<FindFilter>({ page: 1, perPage: 30, direction: "desc" });
  const [selected, setSelected] = useState<Set<number>>(new Set());

  const { data } = useQuery({
    queryKey: ["images-for-gallery", searchFilter],
    queryFn: () => images.find(searchFilter),
  });

  const allImages = data?.items ?? [];
  const available = allImages.filter((i) => !existingImageIds.has(i.id));

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70" onClick={onClose}>
      <div className="bg-card border border-border rounded-xl shadow-2xl w-full max-w-4xl max-h-[80vh] flex flex-col" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between px-5 py-4 border-b border-border">
          <h2 className="text-lg font-semibold text-foreground">Add Images to Gallery</h2>
          <div className="flex items-center gap-3">
            <span className="text-xs text-muted">{selected.size} selected</span>
            <button
              onClick={() => onAdd([...selected])}
              disabled={selected.size === 0 || isPending}
              className="px-3 py-1.5 rounded text-sm font-medium bg-accent hover:bg-accent-hover text-white disabled:opacity-50 flex items-center gap-2"
            >
              {isPending && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
              Add {selected.size > 0 ? selected.size : ""}
            </button>
          </div>
        </div>

        <div className="px-5 py-3 border-b border-border">
          <input
            type="text"
            placeholder="Search images..."
            value={searchFilter.q ?? ""}
            onChange={(e) => setSearchFilter((f) => ({ ...f, q: e.target.value || undefined, page: 1 }))}
            className="w-full bg-input border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          />
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          {available.length > 0 ? (
            <div className="grid grid-cols-4 sm:grid-cols-5 lg:grid-cols-6 gap-3">
              {available.map((image) => (
                <button
                  key={image.id}
                  onClick={() => setSelected((prev) => { const n = new Set(prev); n.has(image.id) ? n.delete(image.id) : n.add(image.id); return n; })}
                  className={`group overflow-hidden rounded-lg border text-left relative ${selected.has(image.id) ? "border-accent ring-2 ring-accent" : "border-border"}`}
                >
                  {selected.has(image.id) && (
                    <div className="absolute top-1 left-1 z-10">
                      <div className="w-5 h-5 rounded bg-accent flex items-center justify-center">
                        <Check className="w-3 h-3 text-white" />
                      </div>
                    </div>
                  )}
                  <div className="aspect-square overflow-hidden bg-surface">
                    <img src={images.thumbnailUrl(image.id)} alt={getImageDisplayTitle(image)} className="h-full w-full object-cover" loading="lazy" />
                  </div>
                  <div className="p-1.5">
                    <p className="truncate text-xs text-foreground">{getImageDisplayTitle(image)}</p>
                  </div>
                </button>
              ))}
            </div>
          ) : (
            <div className="text-center py-12 text-muted">No images available to add</div>
          )}
        </div>

        <div className="flex items-center justify-between px-5 py-3 border-t border-border">
          <div className="flex items-center gap-2">
            <button onClick={() => setSearchFilter((f) => ({ ...f, page: Math.max(1, (f.page ?? 1) - 1) }))} disabled={(searchFilter.page ?? 1) <= 1} className="px-2 py-1 rounded text-xs text-secondary hover:text-foreground disabled:opacity-30">Prev</button>
            <span className="text-xs text-muted">Page {searchFilter.page ?? 1}</span>
            <button onClick={() => setSearchFilter((f) => ({ ...f, page: (f.page ?? 1) + 1 }))} className="px-2 py-1 rounded text-xs text-secondary hover:text-foreground">Next</button>
          </div>
          <button onClick={onClose} className="px-3 py-1.5 rounded text-sm text-secondary hover:text-foreground">Cancel</button>
        </div>
      </div>
    </div>
  );
}

function LoadingPanel({ icon, message }: { icon: React.ReactNode; message: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-12 text-muted">
      <div className="mb-3 animate-pulse">{icon}</div>
      <p>{message}</p>
    </div>
  );
}

function EmptyPanel({ icon, message }: { icon: React.ReactNode; message: string }) {
  return (
    <div className="rounded-xl border border-dashed border-border bg-card/40 py-12 text-center text-muted">
      <div className="mx-auto mb-3 flex justify-center opacity-60">{icon}</div>
      <p>{message}</p>
    </div>
  );
}

function GalleryFileInfo({ gallery }: { gallery: { folderPath?: string; files: { id: number; path: string; size: number; modTime: string; fingerprints: { type: string; value: string }[] }[] } }) {
  const hasFolder = !!gallery.folderPath;
  const hasFiles = gallery.files.length > 0;
  const revealMutation = useMutation({ mutationFn: (fileId: number) => fileOps.reveal(fileId) });
  const canReveal = typeof window !== "undefined" && ["localhost", "127.0.0.1", "::1"].includes(window.location.hostname);

  if (!hasFolder && !hasFiles) {
    return <EmptyPanel icon={<HardDrive className="h-8 w-8" />} message="No file information available" />;
  }

  return (
    <div className="space-y-4">
      {hasFolder && (
        <div className="rounded-xl border border-border bg-card p-4">
          <h3 className="mb-3 text-sm font-semibold uppercase tracking-wide text-muted">Folder</h3>
          <dl className="space-y-2 text-sm">
            <div>
              <dt className="text-muted">Path</dt>
              <dd className="font-mono text-xs text-foreground break-all">{gallery.folderPath}</dd>
            </div>
          </dl>
        </div>
      )}
      {gallery.files.map((file) => (
        <div key={file.id} className="rounded-xl border border-border bg-card p-4">
          <div className="mb-3 flex items-center justify-between gap-3">
            <h3 className="text-sm font-semibold uppercase tracking-wide text-muted">File</h3>
            {canReveal ? (
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
          <dl className="space-y-2 text-sm">
            <div>
              <dt className="text-muted">Path</dt>
              <dd className="font-mono text-xs text-foreground break-all">{file.path}</dd>
            </div>
            <div>
              <dt className="text-muted">Size</dt>
              <dd className="text-foreground">{formatFileSize(file.size)}</dd>
            </div>
            <div>
              <dt className="text-muted">Modified</dt>
              <dd className="text-foreground">{formatDate(file.modTime)}</dd>
            </div>
            {file.fingerprints.length > 0 && (
              <div>
                <dt className="text-muted mb-1">Fingerprints</dt>
                {file.fingerprints.map((fp, i) => (
                  <dd key={i} className="text-foreground">
                    <span className="text-muted text-xs uppercase">{fp.type}:</span>{" "}
                    <span className="font-mono text-xs break-all">{fp.value}</span>
                  </dd>
                ))}
              </div>
            )}
          </dl>
        </div>
      ))}
    </div>
  );
}
