import { useState, useMemo, useCallback, lazy, Suspense } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { aiVisual, images } from "../api/client";
import type { DeleteEntityOptions, EntityEngagement, FindFilter, Image, ImageFilterCriteria } from "../api/types";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { useListUrlState } from "../hooks/useListUrlState";
import { usePaginatedInfiniteQuery } from "../hooks/usePaginatedInfiniteQuery";
import { useEntityEngagementBatch } from "../hooks/useEntityEngagementBatch";
import { ImageIcon, Trash2, Loader2, Edit, Heart, FolderOpen, Search } from "lucide-react";
import { IMAGE_CRITERIA } from "../components/FilterDialog";
import { BulkEditDialog, IMAGE_BULK_FIELDS } from "../components/BulkEditDialog";
import { ImageTile } from "../components/EntityCards";
import type { LightboxImage } from "../components/Lightbox";
import { getDefaultFilter } from "../components/SavedFilterMenu";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../components/RouteCardLinkOverlay";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canWriteEntity } from "../auth/visibility";
import { getImageDisplayTitle } from "../utils/imageDisplay";
import { useWallColumns } from "../hooks/useWallColumns";
import { ExtensionSelectionActions } from "../components/ExtensionSelectionActions";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { withSeededRandomSort } from "../utils/seededRandomSort";
import { fetchAllMatchingIds } from "../utils/selectAllMatching";
import { WallMediaCard } from "../components/WallMediaCard";
import { FeedCardFrame, FeedChipButton, FeedMetadataPill } from "../components/FeedCardFrame";
import { BookmarkButton } from "../components/BookmarkButton";
import { ScraperEntityTagger } from "../components/ScraperEntityTagger";
import { InfiniteScrollSentinel } from "../components/InfiniteScrollSentinel";

const Lightbox = lazy(() => import("../components/Lightbox").then((module) => ({ default: module.Lightbox })));
const ImageCreateModal = lazy(() => import("./ImageEditModal").then((module) => ({ default: module.ImageCreateModal })));
const QuickViewDialog = lazy(() => import("../components/QuickViewDialog").then((module) => ({ default: module.QuickViewDialog })));
const MediaScrapeDialog = lazy(() => import("../components/MediaScrapeDialog").then((module) => ({ default: module.MediaScrapeDialog })));

const SEARCH_MODE_OPTIONS = [
  { value: "text", label: "Text", title: "Text search" },
  { value: "visual", label: "Visual", title: "Visual semantic search" },
];

const VISUAL_MATCH_SORT_OPTION = { value: "visual_match", label: "Visual Match" };

const SORT_OPTIONS = [
  { value: "updated_at", label: "Updated At" },
  { value: "created_at", label: "Created At" },
  { value: "date", label: "Date" },
  { value: "file_mod_time", label: "File Modification Time" },
  { value: "file_size", label: "File Size" },
  { value: "resolution", label: "Resolution" },
  { value: "path", label: "Path" },
  { value: "title", label: "Title" },
  { value: "rating", label: "Rating" },
  { value: "like_counter", label: "Likes" },
  { value: "performer_count", label: "Performer Count" },
  { value: "tag_count", label: "Tag Count" },
  { value: "random", label: "Random" },
];

interface Props {
  onNavigate: (r: any) => void;
}

export function ImagesPage({ onNavigate }: Props) {
  const defaultState = useMemo(() => {
    const savedFilter = getDefaultFilter("images");
    return {
      filter: savedFilter?.findFilter ?? { page: 1, perPage: 40, direction: "desc" },
      objectFilter: savedFilter?.objectFilter ?? {},
      displayMode: "grid" as DisplayMode,
    };
  }, []);
  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode, searchMode, setSearchMode } = useListUrlState({
    resetKey: "images",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "wall", "tagger", "feed"] as const,
    defaultSearchMode: "text",
    allowedSearchModes: ["text", "visual"],
    allowInfinitePageSize: true,
  });
  const [showCreate, setShowCreate] = useState(false);
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [lightboxOpen, setLightboxOpen] = useState(false);
  const [lightboxIndex, setLightboxIndex] = useState(0);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const [showScrapeDialog, setShowScrapeDialog] = useState(false);
  const [wallColumnCount, setWallColumnCount] = useState(6);
  const [selectAllMatchingPending, setSelectAllMatchingPending] = useState(false);
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const canWriteImage = canWriteEntity("image", hasPermission);
  const canDeleteImage = canDeleteEntity("image", hasPermission);

  const hasObjectFilter = Object.keys(objectFilter).length > 0;
  const visualSearchActive = searchMode === "visual" && Boolean(filter.q?.trim());
  const infinitePageSize = filter.perPage === 0;
  const infiniteChunkSize = defaultState.filter.perPage ?? 40;
  const infiniteFilterKey = useMemo(
    () => ({ ...filter, page: 1, perPage: infiniteChunkSize }),
    [filter, infiniteChunkSize],
  );
  const sortOptions = useMemo(
    () => searchMode === "visual" ? [VISUAL_MATCH_SORT_OPTION, ...SORT_OPTIONS] : SORT_OPTIONS,
    [searchMode],
  );

  const handleSearchModeChange = useCallback((mode: string) => {
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
  }, [defaultState.filter.direction, defaultState.filter.sort, filter, setFilter, setSearchMode]);

  const handleDisplayModeChange = useCallback((mode: DisplayMode) => {
    setDisplayMode(mode);
    if (filter.page !== 1) {
      setFilter({ ...filter, page: 1 });
    }
  }, [filter, setDisplayMode, setFilter]);

  const { data, isLoading } = useQuery({
    queryKey: ["images", filter, objectFilter, searchMode],
    queryFn: () => {
      if (visualSearchActive) {
        return aiVisual.searchImages({
          findFilter: filter,
          objectFilter: hasObjectFilter ? objectFilter as ImageFilterCriteria : undefined,
        });
      }

      return hasObjectFilter
        ? images.findFiltered({ findFilter: filter, objectFilter: objectFilter as ImageFilterCriteria })
        : images.find(filter);
    },
    enabled: !infinitePageSize,
  });

  const infiniteImagesQuery = usePaginatedInfiniteQuery<Image>({
    queryKey: ["images", "infinite", infiniteFilterKey, objectFilter, searchMode],
    enabled: infinitePageSize,
    chunkSize: infiniteChunkSize,
    queryFn: (page, perPage) => {
      const nextFilter = { ...filter, page, perPage };
      if (visualSearchActive) {
        return aiVisual.searchImages({
          findFilter: nextFilter,
          objectFilter: hasObjectFilter ? objectFilter as ImageFilterCriteria : undefined,
        });
      }

      return hasObjectFilter
        ? images.findFiltered({ findFilter: nextFilter, objectFilter: objectFilter as ImageFilterCriteria })
        : images.find(nextFilter);
    },
  });

  const items = infinitePageSize ? infiniteImagesQuery.items : (data?.items ?? []);
  const totalCount = infinitePageSize ? infiniteImagesQuery.totalCount : (data?.totalCount ?? 0);
  const loading = infinitePageSize ? infiniteImagesQuery.isPending : isLoading;
  const loadMoreImages = useCallback(() => {
    if (infiniteImagesQuery.hasNextPage && !infiniteImagesQuery.isFetchingNextPage) {
      void infiniteImagesQuery.fetchNextPage();
    }
  }, [infiniteImagesQuery.fetchNextPage, infiniteImagesQuery.hasNextPage, infiniteImagesQuery.isFetchingNextPage]);
  const loadPreviousImages = useCallback(() => {
    if (!infiniteImagesQuery.hasPreviousPage || infiniteImagesQuery.isFetchingPreviousPage) {
      return;
    }

    const beforeHeight = document.documentElement.scrollHeight;
    const beforeTop = window.scrollY;
    void infiniteImagesQuery.fetchPreviousPage().then(() => {
      window.requestAnimationFrame(() => {
        const afterHeight = document.documentElement.scrollHeight;
        window.scrollTo({ top: beforeTop + (afterHeight - beforeHeight) });
      });
    });
  }, [infiniteImagesQuery.fetchPreviousPage, infiniteImagesQuery.hasPreviousPage, infiniteImagesQuery.isFetchingPreviousPage]);
  const { engagementById } = useEntityEngagementBatch("image", items.map((item) => item.id));
  const estimateImageWallHeight = useCallback((image: Image) => {
    const file = image.files[0];
    return file?.width && file.height ? file.height / file.width : 1;
  }, []);
  const wallColumnOptions = useMemo(() => ({ stable: infinitePageSize, getKey: (image: Image) => image.id }), [infinitePageSize]);
  const wallColumns = useWallColumns(items, wallColumnCount, estimateImageWallHeight, wallColumnOptions);
  const selectionResetKey = useMemo(() => JSON.stringify({ filter: infiniteFilterKey, objectFilter, searchMode }), [infiniteFilterKey, objectFilter, searchMode]);
  const { selectedIds, toggle, selectAll, selectIds, selectNone, invertSelection } = useMultiSelect(items, { preserveOnAppend: infinitePageSize, resetKey: selectionResetKey });
  const selecting = selectedIds.size > 0;
  const selectedImage = selectedIds.size === 1 ? items.find((item) => selectedIds.has(item.id)) : undefined;
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);

  const lightboxImages: LightboxImage[] = useMemo(
    () => items.map((img) => ({
      id: img.id,
      src: images.imageUrl(img.id),
      title: getImageDisplayTitle(img),
      interactionSource: "imagesPage",
      interactionMeta: { pageKey: "images" },
    })),
    [items],
  );

  const handleFilterChange = useCallback((next: typeof filter) => {
    setFilter(withSeededRandomSort(filter, next));
  }, [filter, setFilter]);

  const handleSelectAllMatching = useCallback(async () => {
    setSelectAllMatchingPending(true);
    try {
      const ids = await fetchAllMatchingIds<Image>(filter, (nextFilter) => {
        if (visualSearchActive) {
          return aiVisual.searchImages({
            findFilter: nextFilter,
            objectFilter: hasObjectFilter ? objectFilter as ImageFilterCriteria : undefined,
          });
        }

        return hasObjectFilter
          ? images.findFiltered({ findFilter: nextFilter, objectFilter: objectFilter as ImageFilterCriteria })
          : images.find(nextFilter);
      });
      selectIds(ids);
    } finally {
      setSelectAllMatchingPending(false);
    }
  }, [filter, hasObjectFilter, objectFilter, selectIds, visualSearchActive]);

  const bulkDeleteMut = useMutation<void, Error, DeleteEntityOptions | undefined>({
    mutationFn: async (options) => {
      await images.bulkDelete([...selectedIds], options);
    },
    onSuccess: () => { setShowDeleteConfirm(false); selectNone(); queryClient.invalidateQueries({ queryKey: ["images"] }); },
  });

  const bulkEditMut = useMutation({
    mutationFn: (values: Record<string, unknown>) =>
      images.bulkUpdate({ ids: [...selectedIds], ...values } as any),
    onSuccess: () => {
      setShowBulkEdit(false);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["images"] });
    },
  });

  return (
    <>
    <Suspense fallback={null}>
      {showCreate ? <ImageCreateModal open={showCreate} onClose={() => setShowCreate(false)} onCreated={(id) => onNavigate({ page: "image", id })} /> : null}
    </Suspense>
    <ListPage
      title="Images"
      pageKey="images"
      filterMode="images"
      filter={filter}
      onFilterChange={handleFilterChange}
      totalCount={totalCount}
      isLoading={loading}
      searchMode={searchMode}
      searchModes={SEARCH_MODE_OPTIONS}
      searchPlaceholder={searchMode === "visual" ? "Search visuals..." : "Search images, tags, performers..."}
      onSearchModeChange={handleSearchModeChange}
      sortOptions={sortOptions}
      displayMode={displayMode}
      onDisplayModeChange={handleDisplayModeChange}
      availableDisplayModes={["grid", "wall", "tagger", "feed"]}
      allowInfinitePageSize
      onNew={canWriteImage ? () => setShowCreate(true) : undefined}
      criteriaDefinitions={IMAGE_CRITERIA}
      objectFilter={objectFilter}
      onObjectFilterChange={setObjectFilter}
      wallColumnCount={wallColumnCount}
      onWallColumnCountChange={setWallColumnCount}
      showPagingControls={!infinitePageSize}
      selectAllLabel={infinitePageSize ? "Select loaded" : undefined}
      onSelectAllMatching={infinitePageSize ? handleSelectAllMatching : undefined}
      selectAllMatchingLabel={`Select all ${totalCount} matching`}
      selectAllMatchingPending={selectAllMatchingPending}

      selectedIds={selectedIds}
      onSelectAll={selectAll}
      onSelectNone={selectNone}
      onInvertSelection={invertSelection}
      selectionActions={
        <>
          {canWriteImage && selectedImage ? (
            <button
              onClick={() => setShowScrapeDialog(true)}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-cyan-400 hover:text-cyan-300 hover:bg-cyan-900/20"
            >
              <Search className="w-3 h-3" />
              Scrape
            </button>
          ) : null}
          {canWriteImage && (
            <button
              onClick={() => setShowBulkEdit(true)}
              className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
            >
              <Edit className="w-3 h-3" />
              Edit
            </button>
          )}
          <ExtensionSelectionActions entityType="image" selectedIds={selectedIds} />
          {canDeleteImage && (
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
        title={`Delete ${selectedIds.size} image${selectedIds.size === 1 ? "" : "s"}`}
        message={`Delete ${selectedIds.size} selected image${selectedIds.size === 1 ? "" : "s"}? This cannot be undone.`}
        confirmLabel={bulkDeleteMut.isPending ? "Deleting..." : "Delete"}
        onConfirm={(options) => bulkDeleteMut.mutate(options)}
        onCancel={() => { bulkDeleteMut.reset(); setShowDeleteConfirm(false); }}
        isPending={bulkDeleteMut.isPending}
        errorMessage={bulkDeleteMut.error?.message ?? null}
        showDeleteFile
        showDeleteGenerated
      />
      {infinitePageSize && displayMode !== "feed" && items.length > 0 && (infiniteImagesQuery.hasPreviousPage || infiniteImagesQuery.isFetchingPreviousPage) && (
        <InfiniteScrollSentinel
          direction="previous"
          hasMore={Boolean(infiniteImagesQuery.hasPreviousPage)}
          isLoading={infiniteImagesQuery.isFetchingPreviousPage}
          onLoadMore={loadPreviousImages}
          loadedCount={infiniteImagesQuery.firstLoadedIndex}
          totalCount={infiniteImagesQuery.totalCount}
          className="pt-2"
        />
      )}
      {displayMode === "feed" ? (
        <div className="mx-auto flex max-w-5xl flex-col gap-4 px-2">
          {infinitePageSize && items.length > 0 && (infiniteImagesQuery.hasPreviousPage || infiniteImagesQuery.isFetchingPreviousPage) && (
            <InfiniteScrollSentinel
              direction="previous"
              hasMore={Boolean(infiniteImagesQuery.hasPreviousPage)}
              isLoading={infiniteImagesQuery.isFetchingPreviousPage}
              onLoadMore={loadPreviousImages}
              loadedCount={infiniteImagesQuery.firstLoadedIndex}
              totalCount={infiniteImagesQuery.totalCount}
            />
          )}
          {items.map((img) => (
            <ImageFeedCard
              key={img.id}
              image={img}
              engagement={engagementById.get(img.id)}
              onNavigate={onNavigate}
              selected={selectedIds.has(img.id)}
              onSelect={() => toggle(img.id)}
              selecting={selecting}
            />
          ))}
          {infinitePageSize && items.length > 0 && (
            <InfiniteScrollSentinel
              hasMore={Boolean(infiniteImagesQuery.hasNextPage)}
              isLoading={infiniteImagesQuery.isFetchingNextPage}
              onLoadMore={loadMoreImages}
              loadedCount={infiniteImagesQuery.loadedThroughCount}
              totalCount={infiniteImagesQuery.totalCount}
            />
          )}
        </div>
      ) : displayMode === "tagger" ? (
        <ScraperEntityTagger
          entityType="image"
          label="Image"
          items={items}
          selectedIds={selectedIds}
          selecting={selecting}
          onSelect={toggle}
          getTitle={getImageDisplayTitle}
          getImageUrl={(image) => images.thumbnailUrl(image.id, 320)}
          getRoute={(image) => ({ page: "image", id: image.id })}
          queryKey="images"
        />
      ) : displayMode === "grid" ? (
        <EntityCardGrid minCardWidth="var(--card-min-width, 140px)">
          {items.map((img, idx) => (
            <ImageTile
              key={img.id}
              image={img}
              engagement={engagementById.get(img.id)}
              onClick={() => {
                if (selecting) { toggle(img.id); return; }
                onNavigate({ page: "image", id: img.id });
              }}
              onPreview={() => {
                if (selecting) { toggle(img.id); return; }
                setLightboxIndex(idx);
                setLightboxOpen(true);
              }}
              onDetails={() => {
                if (selecting) { toggle(img.id); return; }
                onNavigate({ page: "image", id: img.id });
              }}
              onNavigate={onNavigate}
              selected={selectedIds.has(img.id)}
              onSelect={() => toggle(img.id)}
              selecting={selecting}
              onQuickView={() => setQuickViewId(img.id)}
            />
          ))}
        </EntityCardGrid>
      ) : (
        <div className="flex gap-2 px-2">
          {wallColumns.map((column, columnIndex) => (
            <div key={columnIndex} className="flex min-w-0 flex-1 flex-col gap-2">
              {column.map((img) => (
                <ImageWallCard key={img.id} image={img} onClick={() => selecting ? toggle(img.id) : onNavigate({ page: "image", id: img.id })} selected={selectedIds.has(img.id)} selecting={selecting} onSelect={() => toggle(img.id)} />
              ))}
            </div>
          ))}
        </div>
      )}
      {infinitePageSize && displayMode !== "feed" && items.length > 0 && (
        <InfiniteScrollSentinel
          hasMore={Boolean(infiniteImagesQuery.hasNextPage)}
          isLoading={infiniteImagesQuery.isFetchingNextPage}
          onLoadMore={loadMoreImages}
          loadedCount={infiniteImagesQuery.loadedThroughCount}
          totalCount={infiniteImagesQuery.totalCount}
          className="pb-2"
        />
      )}
      {items.length === 0 && (
        <div className="text-center text-secondary py-16">
          <ImageIcon className="w-12 h-12 mx-auto mb-3 opacity-50" />
          <p>No images found</p>
        </div>
      )}
    </ListPage>
    <BulkEditDialog
      open={showBulkEdit}
      onClose={() => setShowBulkEdit(false)}
      title="Edit Images"
      selectedCount={selectedIds.size}
      fields={IMAGE_BULK_FIELDS}
      onApply={(values) => bulkEditMut.mutate(values)}
      isPending={bulkEditMut.isPending}
    />
    <Suspense fallback={null}>
      {lightboxOpen ? (
        <Lightbox
          images={lightboxImages}
          initialIndex={lightboxIndex}
          open={lightboxOpen}
          onClose={() => setLightboxOpen(false)}
        />
      ) : null}
      {quickViewId !== null ? (
        <QuickViewDialog type="image" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      ) : null}
      {showScrapeDialog && selectedImage ? (
        <MediaScrapeDialog
          open={showScrapeDialog}
          onClose={() => setShowScrapeDialog(false)}
          entityType="image"
          entity={{
            id: selectedImage.id,
            title: selectedImage.title,
            details: selectedImage.details,
            creator: selectedImage.photographer,
            date: selectedImage.date,
            studioName: selectedImage.studioName,
            urls: selectedImage.urls,
            tags: selectedImage.tags,
            performers: selectedImage.performers,
            files: selectedImage.files,
            organized: selectedImage.organized,
          }}
        />
      ) : null}
    </Suspense>
    </>
  );
}

function ImageFeedCard({ image, engagement, onNavigate, selected, onSelect, selecting }: { image: Image; engagement?: EntityEngagement; onNavigate: (route: any) => void; selected?: boolean; onSelect?: () => void; selecting?: boolean }) {
  const displayTitle = getImageDisplayTitle(image);
  const file = image.files[0];
  const aspectRatio = file?.width && file.height ? `${file.width} / ${file.height}` : "1 / 1";
  const likeCount = engagement?.likeCount ?? 0;

  return (
    <FeedCardFrame
      dataAttribute={{ "data-feed-image-id": image.id }}
      selected={selected}
      header={(
        <>
          <span className="font-semibold text-secondary">{image.studioName || "Cove images"}</span>
          {image.date ? <span>{image.date}</span> : null}
          {image.photographer ? <span>{image.photographer}</span> : null}
          {file?.width && file.height ? <span>{file.width}x{file.height}</span> : null}
        </>
      )}
      headerActions={(
        <>
          <span className="inline-flex min-h-7 items-center gap-1 rounded-full border border-border bg-background/70 px-2.5 text-xs font-medium text-secondary">
            <Heart className="h-3.5 w-3.5" />
            {likeCount}
          </span>
          {image.galleryCount > 0 ? (
            <span className="inline-flex min-h-7 items-center gap-1 rounded-full border border-border bg-background/70 px-2.5 text-xs font-medium text-secondary">
              <FolderOpen className="h-3.5 w-3.5" />
              {image.galleryCount}
            </span>
          ) : null}
        </>
      )}
      media={(
        <WallMediaCard
          title={displayTitle}
          imageSrc={images.thumbnailUrl(image.id, 1280)}
          aspectRatio={aspectRatio}
          className="rounded-none border-x-0 border-y border-border/60 hover:border-border/60"
        >
          <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
          <RouteCardLinkOverlay route={{ page: "image", id: image.id }} onClick={() => onNavigate({ page: "image", id: image.id })} label={`Open image ${displayTitle}`} selectionSafeZone />
          {!selecting && (
            <BookmarkButton
              hostType="image"
              hostId={image.id}
              compact
              deferUntilHover
              className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
            />
          )}
        </WallMediaCard>
      )}
      title={(
        <button
          type="button"
          onClick={() => onNavigate({ page: "image", id: image.id })}
          className="text-left text-base font-semibold text-foreground transition-colors hover:text-accent"
        >
          {displayTitle}
        </button>
      )}
      details={image.details ? <p className="line-clamp-3">{image.details}</p> : undefined}
      metadata={(
        <>
          {file ? <FeedMetadataPill>{file.width && file.height ? `${file.width}x${file.height}` : "Image"}</FeedMetadataPill> : null}
          {image.organized ? <FeedMetadataPill>Organized</FeedMetadataPill> : null}
          {image.galleries.length > 0 ? <FeedMetadataPill>{image.galleries.length} galleries</FeedMetadataPill> : null}
        </>
      )}
      chips={(
        <>
          {image.performers.slice(0, 4).map((performer) => (
            <FeedChipButton
              key={performer.id}
              onClick={() => onNavigate({ page: "performer", id: performer.id })}
            >
              {performer.name}
            </FeedChipButton>
          ))}
          {image.tags.slice(0, 4).map((tag) => (
            <FeedChipButton
              key={tag.id}
              onClick={() => onNavigate({ page: "tag", id: tag.id })}
            >
              #{tag.name}
            </FeedChipButton>
          ))}
        </>
      )}
    />
  );
}

function ImageWallCard({ image, onClick, selected, selecting, onSelect }: { image: Image; onClick: () => void; selected?: boolean; selecting?: boolean; onSelect?: () => void }) {
  const displayTitle = getImageDisplayTitle(image);
  const file = image.files[0];
  const aspectRatio = file?.width && file.height ? `${file.width} / ${file.height}` : "1 / 1";

  return (
    <WallMediaCard
      title={displayTitle}
      imageSrc={images.thumbnailUrl(image.id)}
      aspectRatio={aspectRatio}
      className={`group ${selected ? "border-accent ring-1 ring-accent/60" : ""}`.trim()}
    >
      <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
      <RouteCardLinkOverlay route={{ page: "image", id: image.id }} onClick={onClick} label={`Open image ${displayTitle}`} selectionSafeZone />
      {!selecting && (
        <BookmarkButton
          hostType="image"
          hostId={image.id}
          compact
          deferUntilHover
          className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
        />
      )}
    </WallMediaCard>
  );
}
