import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { galleries, groups, images, metadata, performers, scenes, studios, tags, entityImages } from "../api/client";
import type { FindFilter, Gallery, Group, Image, MetadataServer, MetadataServerTagMatch, Performer, Scene, Studio, TagDetail as TagDetailModel, TagSegmentWall } from "../api/types";
import { formatDate, formatDuration, getResolutionLabel, TagBadge, CustomFieldsDisplay } from "../components/shared";
import { Building2, ChevronDown, CloudDownload, Film, FolderOpen, GitMerge, Heart, ImageIcon, Layers, Loader2, Music, Pencil, Search, Tag as TagIcon, Trash2, UserRound, Wand2 } from "lucide-react";
import { useEffect, useState } from "react";
import { TagEditModal } from "./TagEditModal";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { DetailMergeDialog } from "../components/DetailMergeDialog";
import { ExtensionSlot } from "../router/RouteRegistry";
import { SceneCard, PerformerTile, ImageTile, GalleryTile, StudioTile, GroupTile } from "../components/EntityCards";
import { QuickViewDialog } from "../components/QuickViewDialog";
import { useAppConfig } from "../state/AppConfigContext";
import { DetailListToolbar } from "../components/DetailListToolbar";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { BulkSelectionActions } from "../components/BulkSelectionActions";
import { useExtensionTabs } from "../components/useExtensionTabs";
import { EntityDetailTabs } from "../components/EntityDetailTabs";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { EntityHeroLayout } from "../components/EntityHeroLayout";
import { createRouteLinkProps } from "../components/cardNavigation";
import { SCENE_SORT_OPTIONS } from "../components/sceneSortOptions";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { GALLERY_SORT_OPTIONS } from "../components/gallerySortOptions";
import { PERFORMER_SORT_OPTIONS } from "../components/performerSortOptions";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity, filterItemsByPermission } from "../auth/visibility";

const PERFORMER_SORT = PERFORMER_SORT_OPTIONS;
const IMAGE_SORT = [
  { value: "updated_at", label: "Updated At" },
  { value: "created_at", label: "Created At" },
  { value: "title", label: "Title" },
  { value: "rating", label: "Rating" },
  { value: "random", label: "Random" },
];
const GALLERY_SORT = GALLERY_SORT_OPTIONS;
const STUDIO_SORT = [
  { value: "name", label: "Name" },
  { value: "updated_at", label: "Updated At" },
  { value: "created_at", label: "Created At" },
  { value: "random", label: "Random" },
];
const GROUP_SORT = [
  { value: "name", label: "Name" },
  { value: "updated_at", label: "Updated At" },
  { value: "created_at", label: "Created At" },
  { value: "random", label: "Random" },
];

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type TabKey = "scenes" | "performers" | "images" | "galleries" | "segments" | "studios" | "groups" | (string & {});

export function TagDetailPage({ id, onNavigate }: Props) {
  const { config } = useAppConfig();
  const { hasPermission, user } = useAuth();
  const metadataServers = config?.scraping?.metadataServers ?? [];
  const { data: tag, isLoading } = useQuery({
    queryKey: ["tag", id],
    queryFn: () => tags.get(id),
  });
  const [editing, setEditing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [mergeOpen, setMergeOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>("scenes");
  const { allTabs: tagTabs, renderExtensionTab, extensionCounts } = useExtensionTabs("tag", [
    { key: "scenes", label: "Scenes", count: tag?.sceneCount },
    { key: "performers", label: "Performers", count: tag?.performerCount },
    { key: "images", label: "Images", count: tag?.imageCount },
    { key: "galleries", label: "Galleries", count: tag?.galleryCount },
    { key: "segments", label: "Segments", count: tag?.segmentCount },
    { key: "studios", label: "Studios", count: tag?.studioCount },
    { key: "groups", label: "Groups", count: tag?.groupCount },
  ], id);
  const [sceneFilter, setSceneFilter] = useState<FindFilter>({ page: 1, perPage: 24, direction: "desc" });
  const [performerFilter, setPerformerFilter] = useState<FindFilter>({ page: 1, perPage: 18, direction: "asc" });
  const [imageFilter, setImageFilter] = useState<FindFilter>({ page: 1, perPage: 30, direction: "desc" });
  const [galleryFilter, setGalleryFilter] = useState<FindFilter>({ page: 1, perPage: 18, direction: "desc" });
  const [studioFilter, setStudioFilter] = useState<FindFilter>({ page: 1, perPage: 18, direction: "asc" });
  const [groupFilter, setGroupFilter] = useState<FindFilter>({ page: 1, perPage: 18, direction: "asc" });
  const queryClient = useQueryClient();
  const { backLabel, goBack } = useBackNavigation({ page: "tags" }, onNavigate);
  const canWriteTag = canWriteEntity("tag", hasPermission);
  const canEngageTag = canReadEntity("tag", hasPermission) && (user?.kind === "user" || user?.kind === "system");
  const canDeleteTag = canDeleteEntity("tag", hasPermission);
  const canAutoTagTag = hasPermission("library.autotag") && canWriteTag;
  const visibleTagTabs = filterItemsByPermission(tagTabs, {
    scenes: "scenes.read",
    performers: "performers.read",
    images: "images.read",
    galleries: "galleries.read",
    segments: "scenes.read",
    studios: "studios.read",
    groups: "groups.read",
  }, hasPermission);

  const { favorite: tagFavorite, setFavorite: setTagFavorite } = useEntityEngagement("tag", id, {
    fallbackFavorite: tag?.favorite,
  });

  useEffect(() => {
    if (tag) document.title = `${tag.name} | Cove`;
    return () => { document.title = "Cove"; };
  }, [tag]);

  // Keyboard shortcuts
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const el = (e.target as HTMLElement).tagName;
      if (el === "INPUT" || el === "TEXTAREA" || el === "SELECT") return;
      switch (e.key) {
        case "e": if (canWriteTag) setEditing((v) => !v); break;
        case "f": if (tag && canEngageTag) setTagFavorite(!tagFavorite); break;
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [canEngageTag, canWriteTag, tag, tagFavorite, setTagFavorite]);

  useEffect(() => {
    if (visibleTagTabs.length > 0 && !visibleTagTabs.some((tab) => tab.key === activeTab)) {
      setActiveTab(visibleTagTabs[0].key as TabKey);
    }
  }, [activeTab, visibleTagTabs]);

  const deleteMut = useMutation({
    mutationFn: () => tags.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tags"] });
      goBack();
    },
  });

  const autoTagMut = useMutation({
    mutationFn: () => {
      if (!tag) throw new Error("Tag not loaded");
      return metadata.autoTag({ tags: [tag.name] });
    },
  });

  if (isLoading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-accent" />
      </div>
    );
  }

  if (!tag) {
    return <div className="py-16 text-center text-secondary">Tag not found</div>;
  }

  const tagImageUrl = tag.imagePath || entityImages.tagImageUrl(tag.id, tag.updatedAt);

  return (
    <>
      <EntityHeroLayout
        backLabel={backLabel}
        onGoBack={goBack}
        imageUrl={tagImageUrl}
        imageAlt={tag.name}
        imageClassName="h-full w-full object-contain p-3"
        imageFallback={<TagIcon className="h-14 w-14 text-accent" />}
        title={tag.name}
        sortName={tag.sortName && tag.sortName !== tag.name ? tag.sortName : undefined}
        aliases={tag.aliases.length > 0 ? tag.aliases.join(", ") : undefined}
        description={tag.description}
        favorite={tagFavorite}
        onFavoriteToggle={canEngageTag ? () => setTagFavorite(!tagFavorite) : undefined}
        counts={[
          { key: "scenes", label: "Scenes", value: tag.sceneCount, icon: <Film className="h-4 w-4" /> },
          { key: "performers", label: "Performers", value: tag.performerCount, icon: <UserRound className="h-4 w-4" /> },
          { key: "images", label: "Images", value: tag.imageCount, icon: <ImageIcon className="h-4 w-4" /> },
          { key: "galleries", label: "Galleries", value: tag.galleryCount, icon: <FolderOpen className="h-4 w-4" /> },
          { key: "segments", label: "Segments", value: tag.segmentCount, icon: <Layers className="h-4 w-4" /> },
          { key: "studios", label: "Studios", value: tag.studioCount, icon: <Building2 className="h-4 w-4" /> },
          { key: "groups", label: "Groups", value: tag.groupCount, icon: <Layers className="h-4 w-4" /> },
          ...extensionCounts.map((ec) => ({
            key: ec.key,
            label: ec.label,
            value: ec.count,
            icon: ec.icon === "music" ? <Music className="h-4 w-4" /> : undefined,
          })),
        ]}
        metaRow={(
          <>
            {tag.ignoreAutoTag ? <span className="rounded bg-yellow-500/15 px-1.5 py-0.5 text-yellow-400">Ignores Auto-Tag</span> : null}
            <span title={`Created ${formatDate(tag.createdAt)}`}>Updated {formatDate(tag.updatedAt)}</span>
          </>
        )}
        heroContent={(
          <>
            {autoTagMut.isSuccess ? <p className="text-sm text-emerald-300">Auto-tag job queued.</p> : null}
            <CustomFieldsDisplay customFields={tag.customFields} entityType="tag" />
            <TagMetadataServerPanel tag={tag} metadataServers={metadataServers} onNavigate={onNavigate} />
          </>
        )}
        actions={(
          <>
            <ExtensionSlot slot="tag-detail-actions" context={{ tag, onNavigate }} />
            {canWriteTag ? <button onClick={() => setEditing(true)} className="flex items-center gap-1.5 rounded bg-accent px-3 py-1.5 text-sm text-white hover:bg-accent-hover"><Pencil className="h-3.5 w-3.5" /> Edit</button> : null}
            {canAutoTagTag ? <button onClick={() => autoTagMut.mutate()} className="flex items-center gap-1.5 rounded border border-border bg-card px-3 py-1.5 text-sm text-secondary hover:text-foreground" disabled={tag.ignoreAutoTag || autoTagMut.isPending}>{autoTagMut.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Wand2 className="h-3.5 w-3.5" />} Auto Tag</button> : null}
            {canWriteTag ? <button onClick={() => setMergeOpen(true)} className="flex items-center gap-1.5 rounded border border-border bg-card px-3 py-1.5 text-sm text-secondary hover:text-foreground"><GitMerge className="h-3.5 w-3.5" /> Merge...</button> : null}
            {canDeleteTag ? <button onClick={() => setConfirmDelete(true)} className="flex items-center gap-1.5 rounded border border-border bg-card px-3 py-1.5 text-sm text-secondary hover:border-red-500 hover:text-red-300"><Trash2 className="h-3.5 w-3.5" /> Delete</button> : null}
          </>
        )}
      >
        <ExtensionSlot slot="tag-detail-sidebar-bottom" context={{ tag, onNavigate }} />

        <TagHierarchyLinks tag={tag} onNavigate={onNavigate} className="mx-auto mb-4 max-w-7xl" />
        <EntityDetailTabs tabs={visibleTagTabs} activeTab={activeTab} onTabChange={(key) => setActiveTab(key as TabKey)} className="mx-auto max-w-7xl" />

        <div className="py-6">
          {activeTab === "scenes" && (
            <TagScenesPanel tagId={id} filter={sceneFilter} setFilter={setSceneFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "performers" && (
            <TagPerformersPanel tagId={id} filter={performerFilter} setFilter={setPerformerFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "images" && (
            <TagImagesPanel tagId={id} filter={imageFilter} setFilter={setImageFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "galleries" && (
            <TagGalleriesPanel tagId={id} filter={galleryFilter} setFilter={setGalleryFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "segments" && (
            <TagSegmentsPanel tagId={id} onNavigate={onNavigate} />
          )}
          {activeTab === "studios" && (
            <TagStudiosPanel tagId={id} filter={studioFilter} setFilter={setStudioFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "groups" && (
            <TagGroupsPanel tagId={id} filter={groupFilter} setFilter={setGroupFilter} onNavigate={onNavigate} />
          )}
          {renderExtensionTab(activeTab, id, onNavigate)}
        </div>

        <ExtensionSlot slot="tag-detail-bottom" context={{ tag, onNavigate }} />
      </EntityHeroLayout>

      <TagEditModal tag={tag} open={editing} onClose={() => setEditing(false)} />
      <ConfirmDialog
        open={confirmDelete}
        title="Delete Tag"
        message={`Delete "${tag.name}"? This cannot be undone.`}
        onConfirm={() => deleteMut.mutate()}
        onCancel={() => setConfirmDelete(false)}
      />
      <DetailMergeDialog
        open={mergeOpen}
        onClose={() => setMergeOpen(false)}
        entityType="tag"
        targetItem={{ id: tag.id, name: tag.name, imagePath: tagImageUrl, subtitle: tag.sortName && tag.sortName !== tag.name ? tag.sortName : undefined }}
        searchItems={async (term) => {
          const response = await tags.find({ page: 1, perPage: 20, sort: "name", direction: "asc", q: term || undefined });
          return response.items.map((item) => ({
            id: item.id,
            name: item.name,
            imagePath: item.imagePath,
          }));
        }}
        onMerge={(targetId, sourceIds) => tags.merge(targetId, sourceIds)}
        invalidateQueryKeys={[["tag", id], ["tags"]]}
      />

    </>
  );
}

function TagMetadataServerPanel({ tag, metadataServers, onNavigate }: { tag: TagDetailModel; metadataServers: MetadataServer[]; onNavigate: (r: any) => void }) {
  const queryClient = useQueryClient();
  const [term, setTerm] = useState(tag.name);
  const [selectedEndpoint, setSelectedEndpoint] = useState("");
  const [expanded, setExpanded] = useState(false);

  useEffect(() => {
    setTerm(tag.name);
  }, [tag.id, tag.name]);

  useEffect(() => {
    if (selectedEndpoint && !metadataServers.some((box) => box.endpoint === selectedEndpoint)) {
      setSelectedEndpoint("");
    }
  }, [selectedEndpoint, metadataServers]);

  const searchMutation = useMutation({
    mutationFn: (variables: { term?: string; endpoint?: string }) => tags.searchMetadataServer(tag.id, variables.term, variables.endpoint),
  });

  const importMutation = useMutation({
    mutationFn: (match: MetadataServerTagMatch) =>
      tags.importFromMetadataServer(tag.id, { endpoint: match.endpoint, tagId: match.id }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["tag", tag.id] });
      queryClient.invalidateQueries({ queryKey: ["tags"] });
    },
  });

  const draftMutation = useMutation({
    mutationFn: (endpoint: string) => tags.submitMetadataServerDraft(tag.id, endpoint),
  });

  const draftEndpoint = selectedEndpoint || metadataServers[0]?.endpoint;

  return (
    <div className="mt-6 rounded-xl border border-border bg-card p-4">
      <button onClick={() => setExpanded(!expanded)} className="flex w-full items-center justify-between text-left">
        <h2 className="text-base font-semibold text-foreground">MetadataServer</h2>
        <ChevronDown className={`h-4 w-4 text-muted transition-transform ${expanded ? "rotate-180" : ""}`} />
      </button>

      {expanded && (
        <div className="mt-4">
          {metadataServers.length === 0 ? (
            <div className="rounded-xl border border-dashed border-border p-4 text-sm text-secondary">
              No MetadataServer endpoints are configured yet. Use Settings and open Metadata Providers to add one.
              <button onClick={() => onNavigate({ page: "settings" })} className="ml-2 text-accent hover:text-accent-hover">
                Open settings
              </button>
            </div>
          ) : (
            <>
              <div className="grid gap-3 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_auto]">
                <label className="block text-sm">
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Search term</span>
                  <input
                    value={term}
                    onChange={(event) => setTerm(event.target.value)}
                    placeholder={tag.name}
                    className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                  />
                </label>
                <label className="block text-sm">
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Endpoint</span>
                  <select
                    value={selectedEndpoint}
                    onChange={(event) => setSelectedEndpoint(event.target.value)}
                    className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                  >
                    <option value="">All configured endpoints</option>
                    {metadataServers.map((box) => (
                      <option key={box.endpoint} value={box.endpoint}>
                        {box.name || box.endpoint}
                      </option>
                    ))}
                  </select>
                </label>
                <div className="flex flex-wrap items-end gap-2">
                  <button
                    onClick={() => searchMutation.mutate({ term: term.trim() || undefined, endpoint: selectedEndpoint || undefined })}
                    disabled={searchMutation.isPending}
                    className="inline-flex items-center gap-2 rounded-xl border border-border px-4 py-2 text-sm text-foreground hover:border-accent hover:text-accent disabled:opacity-60"
                  >
                    {searchMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
                    Search MetadataServer
                  </button>
                  <button
                    onClick={() => draftEndpoint && draftMutation.mutate(draftEndpoint)}
                    disabled={!draftEndpoint || draftMutation.isPending}
                    className="inline-flex items-center gap-2 rounded-xl border border-border px-4 py-2 text-sm text-foreground hover:border-accent hover:text-accent disabled:opacity-60"
                  >
                    {draftMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <CloudDownload className="h-4 w-4" />}
                    Submit Draft
                  </button>
                </div>
              </div>

              {searchMutation.error && <p className="mt-3 text-sm text-red-300">{searchMutation.error.message}</p>}
              {draftMutation.error && <p className="mt-3 text-sm text-red-300">{draftMutation.error.message}</p>}
              {draftMutation.isSuccess && (
                <p className="mt-3 text-sm text-emerald-300">
                  Tag draft submitted to MetadataServer{draftMutation.data.draftId ? ` (${draftMutation.data.draftId})` : ""}.
                </p>
              )}
              {importMutation.isSuccess && <p className="mt-3 text-sm text-emerald-300">Tag metadata imported from MetadataServer.</p>}

              {searchMutation.data && (
                <div className="mt-4 space-y-3">
                  {searchMutation.data.length === 0 ? (
                    <div className="rounded-xl border border-dashed border-border p-4 text-sm text-secondary">
                      No MetadataServer tag matches were found.
                    </div>
                  ) : (
                    searchMutation.data.map((match) => (
                      <button
                        key={`${match.endpoint}:${match.id}`}
                        onClick={() => importMutation.mutate(match)}
                        disabled={importMutation.isPending}
                        className="flex w-full flex-col gap-4 rounded-xl border border-border bg-surface p-4 text-left transition-colors hover:border-accent/60 disabled:opacity-60 md:flex-row md:items-center"
                      >
                        <div className="flex h-14 w-14 flex-shrink-0 items-center justify-center rounded-lg border border-border bg-card">
                          <TagIcon className="h-7 w-7 text-accent" />
                        </div>

                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="text-base font-semibold text-foreground">{match.name}</span>
                            <span className="rounded-full border border-border px-2 py-0.5 text-xs text-secondary">{match.metadataServerName}</span>
                          </div>
                          <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
                            <span>ID {match.id}</span>
                          </div>
                          {match.description && <p className="mt-2 text-sm text-secondary">{match.description}</p>}
                          {match.aliases.length > 0 && <p className="mt-2 text-xs text-secondary">Aliases: {match.aliases.join(", ")}</p>}
                        </div>

                        <div className="flex items-end">
                          <span className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white">
                            {importMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <CloudDownload className="h-4 w-4" />}
                            Import
                          </span>
                        </div>
                      </button>
                    ))
                  )}
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}

function TagHierarchyLinks({
  tag,
  onNavigate,
  className = "",
}: {
  tag: TagDetailModel;
  onNavigate: (r: any) => void;
  className?: string;
}) {
  if (tag.parents.length === 0 && tag.children.length === 0) {
    return null;
  }

  return (
    <section className={["rounded-xl border border-border bg-card/70 px-4 py-3", className].filter(Boolean).join(" ")}>
      <div className="flex flex-wrap gap-x-6 gap-y-3 text-sm text-secondary">
        {tag.parents.length > 0 ? (
          <div className="flex min-w-0 flex-wrap items-center gap-1.5">
            <span className="inline-flex items-center gap-1 text-[11px] font-semibold uppercase tracking-wide text-muted">
              <TagIcon className="h-3 w-3" /> Parents
            </span>
            {tag.parents.map((parent) => (
              <TagBadge key={parent.id} name={parent.name} tag={parent} onClick={() => onNavigate({ page: "tag", id: parent.id })} />
            ))}
          </div>
        ) : null}
        {tag.children.length > 0 ? (
          <div className="flex min-w-0 flex-wrap items-center gap-1.5">
            <span className="inline-flex items-center gap-1 text-[11px] font-semibold uppercase tracking-wide text-muted">
              <TagIcon className="h-3 w-3" /> Sub Tags
            </span>
            {tag.children.map((child) => (
              <TagBadge key={child.id} name={child.name} tag={child} onClick={() => onNavigate({ page: "tag", id: child.id })} />
            ))}
          </div>
        ) : null}
      </div>
    </section>
  );
}

function TagScenesPanel({ tagId, filter, setFilter, onNavigate }: {
  tagId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const { data, isLoading } = useQuery({
    queryKey: ["tag-scenes", tagId, filter],
    queryFn: () => scenes.find(filter, { tagIds: String(tagId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<Film className="h-10 w-10" />} message="Loading scenes..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<Film className="h-12 w-12" />} message="No scenes with this tag" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={SCENE_SORT_OPTIONS} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="scenes" selectedIds={selectedIds} onDone={selectNone} sceneItems={data.items} onNavigate={onNavigate} />} />
      <EntityCardGrid minCardWidth={`${220 + zoomLevel * 50}px`}>
        {data.items.map((scene) => (
          <SceneCard key={scene.id} scene={scene} onClick={() => selecting ? toggle(scene.id) : onNavigate({ page: "scene", id: scene.id })} onNavigate={onNavigate} onQuickView={() => setQuickViewId(scene.id)} selected={selectedIds.has(scene.id)} onSelect={() => toggle(scene.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
      {quickViewId !== null && (
        <QuickViewDialog type="scene" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      )}
    </>
  );
}

function TagPerformersPanel({ tagId, filter, setFilter, onNavigate }: {
  tagId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const { data, isLoading } = useQuery({
    queryKey: ["tag-performers", tagId, filter],
    queryFn: () => performers.find(filter, { tagIds: String(tagId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<UserRound className="h-10 w-10" />} message="Loading performers..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<UserRound className="h-12 w-12" />} message="No performers with this tag" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={PERFORMER_SORT} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="performers" selectedIds={selectedIds} onDone={selectNone} />} />
      <EntityCardGrid minCardWidth={`${180 + zoomLevel * 50}px`}>
        {data.items.map((performer) => (
          <PerformerTile key={performer.id} performer={performer} onClick={() => selecting ? toggle(performer.id) : onNavigate({ page: "performer", id: performer.id })} selected={selectedIds.has(performer.id)} onSelect={() => toggle(performer.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
    </>
  );
}

function TagImagesPanel({ tagId, filter, setFilter, onNavigate }: {
  tagId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const { data, isLoading } = useQuery({
    queryKey: ["tag-images", tagId, filter],
    queryFn: () => images.find(filter, { tagIds: String(tagId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<ImageIcon className="h-10 w-10" />} message="Loading images..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<ImageIcon className="h-12 w-12" />} message="No images with this tag" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={IMAGE_SORT} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="images" selectedIds={selectedIds} onDone={selectNone} downloadItems={data.items} />} />
      <EntityCardGrid minCardWidth={`${160 + zoomLevel * 50}px`}>
        {data.items.map((image) => (
          <ImageTile key={image.id} image={image} onClick={() => selecting ? toggle(image.id) : onNavigate({ page: "image", id: image.id })} onNavigate={onNavigate} onQuickView={() => setQuickViewId(image.id)} selected={selectedIds.has(image.id)} onSelect={() => toggle(image.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
      {quickViewId !== null && (
        <QuickViewDialog type="image" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      )}
    </>
  );
}

function TagGalleriesPanel({ tagId, filter, setFilter, onNavigate }: {
  tagId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const { data, isLoading } = useQuery({
    queryKey: ["tag-galleries", tagId, filter],
    queryFn: () => galleries.find(filter, { tagIds: String(tagId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<FolderOpen className="h-10 w-10" />} message="Loading galleries..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<FolderOpen className="h-12 w-12" />} message="No galleries with this tag" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={GALLERY_SORT} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="galleries" selectedIds={selectedIds} onDone={selectNone} downloadItems={data.items} />} />
      <EntityCardGrid minCardWidth={`${220 + zoomLevel * 50}px`}>
        {data.items.map((gallery) => (
          <GalleryTile key={gallery.id} gallery={gallery} onClick={() => selecting ? toggle(gallery.id) : onNavigate({ page: "gallery", id: gallery.id })} selected={selectedIds.has(gallery.id)} onSelect={() => toggle(gallery.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
    </>
  );
}

function TagSegmentsPanel({ tagId, onNavigate }: { tagId: number; onNavigate: (r: any) => void }) {
  const { data, isLoading } = useQuery({
    queryKey: ["tag-segments", tagId],
    queryFn: () => tags.segments(tagId, 100),
  });

  if (isLoading) return <LoadingPanel icon={<Layers className="h-10 w-10" />} message="Loading segments..." />;
  if (!data || data.length === 0) return <EmptyPanel icon={<Layers className="h-12 w-12" />} message="No segments with this tag" />;

  return (
    <EntityCardGrid minCardWidth="220px">
      {data.map((segment: TagSegmentWall) => {
        const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "scene", id: segment.sceneId }, () => onNavigate({ page: "scene", id: segment.sceneId }));

        return (
          <a
            key={segment.id}
            {...linkProps}
            className="group text-left"
          >
            <div className="relative aspect-video overflow-hidden rounded-lg border border-border bg-card shadow-md shadow-black/30">
              <img
                src={scenes.screenshotUrl(segment.sceneId)}
                alt={segment.title || segment.kind}
                className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105"
                loading="lazy"
              />
              <span className="absolute bottom-1.5 right-1.5 rounded bg-black/75 px-1.5 py-0.5 text-[11px] text-white">
                {formatDuration(segment.startSec)}
              </span>
            </div>
            <div className="pt-2">
              <p className="truncate text-sm font-medium text-foreground group-hover:text-accent">{segment.title || segment.kind}</p>
              <p className="mt-0.5 truncate text-xs text-secondary">{segment.sceneTitle || "Untitled Scene"}</p>
            </div>
          </a>
        );
      })}
    </EntityCardGrid>
  );
}

function TagStudiosPanel({ tagId, filter, setFilter, onNavigate }: {
  tagId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const { data, isLoading } = useQuery({
    queryKey: ["tag-studios", tagId, filter],
    queryFn: () => studios.find(filter, { tagIds: String(tagId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<Building2 className="h-10 w-10" />} message="Loading studios..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<Building2 className="h-12 w-12" />} message="No studios with this tag" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={STUDIO_SORT} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="studios" selectedIds={selectedIds} onDone={selectNone} />} />
      <EntityCardGrid minCardWidth={`${200 + zoomLevel * 50}px`}>
        {data.items.map((studio) => (
          <StudioTile key={studio.id} studio={studio} onClick={() => selecting ? toggle(studio.id) : onNavigate({ page: "studio", id: studio.id })} selected={selectedIds.has(studio.id)} onSelect={() => toggle(studio.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
    </>
  );
}

function TagGroupsPanel({ tagId, filter, setFilter, onNavigate }: {
  tagId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const { data, isLoading } = useQuery({
    queryKey: ["tag-groups", tagId, filter],
    queryFn: () => groups.find(filter, { tagIds: String(tagId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<Layers className="h-10 w-10" />} message="Loading groups..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<Layers className="h-12 w-12" />} message="No groups with this tag" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={GROUP_SORT} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="groups" selectedIds={selectedIds} onDone={selectNone} />} />
      <EntityCardGrid minCardWidth={`${200 + zoomLevel * 50}px`}>
        {data.items.map((group) => (
          <GroupTile key={group.id} group={group} onClick={() => selecting ? toggle(group.id) : onNavigate({ page: "group", id: group.id })} selected={selectedIds.has(group.id)} onSelect={() => toggle(group.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
    </>
  );
}

function Pager({ filter, setFilter, totalCount }: {
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  totalCount: number;
}) {
  const perPage = filter.perPage ?? 1;
  const page = filter.page ?? 1;
  const totalPages = Math.max(1, Math.ceil(totalCount / perPage));

  if (totalPages <= 1) return null;

  return (
    <div className="mx-auto max-w-7xl mt-6 flex items-center justify-center gap-4">
      <button
        disabled={page <= 1}
        onClick={() => setFilter({ ...filter, page: page - 1 })}
        className="rounded border border-border bg-card px-4 py-2 text-sm text-secondary hover:bg-card-hover disabled:cursor-not-allowed disabled:opacity-50"
      >
        Previous
      </button>
      <span className="text-sm text-secondary">Page {page} of {totalPages}</span>
      <button
        disabled={page >= totalPages}
        onClick={() => setFilter({ ...filter, page: page + 1 })}
        className="rounded border border-border bg-card px-4 py-2 text-sm text-secondary hover:bg-card-hover disabled:cursor-not-allowed disabled:opacity-50"
      >
        Next
      </button>
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
    <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-card/40 py-12 text-muted">
      <div className="mb-3 opacity-60">{icon}</div>
      <p>{message}</p>
    </div>
  );
}
