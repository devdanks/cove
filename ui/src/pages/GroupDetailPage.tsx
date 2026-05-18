import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { groups, scenes } from "../api/client";
import type { FindFilter, Group, GroupItem, Scene, SceneFilterCriteria, SegmentDerivedQueryDescriptor, SegmentSpanDerivedQuery } from "../api/types";
import { formatDate, formatDuration, getResolutionLabel, TagBadge, CustomFieldsDisplay } from "../components/shared";
import { Clapperboard, ExternalLink, Film, GripVertical, Layers, Link as LinkIcon, Pencil, Play, Plus, Trash2, X } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { GroupEditModal } from "./GroupEditModal";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { ExtensionSlot } from "../router/RouteRegistry";
import { GroupTile, SceneCard } from "../components/EntityCards";
import { CompilationPlayer } from "../components/CompilationPlayer";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { QuickViewDialog } from "../components/QuickViewDialog";
import { DetailListToolbar } from "../components/DetailListToolbar";
import { SCENE_CRITERIA } from "../components/FilterDialog";
import { EntityHeroLayout } from "../components/EntityHeroLayout";
import { EntityDetailTabs } from "../components/EntityDetailTabs";
import { BulkSelectionActions } from "../components/BulkSelectionActions";
import { useExtensionTabs } from "../components/useExtensionTabs";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity, filterItemsByPermission } from "../auth/visibility";
import { SortableList } from "../components/SortableList";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { useDetailListQuery } from "../hooks/useDetailListQuery";
import { useDetailListSelection } from "../hooks/useDetailListSelection";
import { withRequiredMultiId } from "../utils/detailRelationFilters";
import { VirtualizedEntityGrid } from "../components/VirtualizedEntityLayouts";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type TabKey = "items" | "containingGroups" | "metadata" | "edit" | (string & {});

export function GroupDetailPage({ id, onNavigate }: Props) {
  const { data: group, isLoading } = useQuery({
    queryKey: ["group", id],
    queryFn: () => groups.get(id),
  });
  const { hasPermission, user } = useAuth();
  const [editing, setEditing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>("items");
  const { allTabs: groupTabs, renderExtensionTab } = useExtensionTabs("group", [
    { key: "items", label: "Items" },
    { key: "containingGroups", label: "Containing Groups" },
    { key: "metadata", label: "Metadata" },
    { key: "edit", label: "Edit" },
  ], id);
  const [sceneFilter, setSceneFilter] = useState<FindFilter>({ page: 1, perPage: 24, direction: "asc", sort: "date" });
  const queryClient = useQueryClient();
  const { backLabel, goBack } = useBackNavigation({ page: "groups" }, onNavigate);
  const canReadGroups = canReadEntity("group", hasPermission);
  const canReadScenes = canReadEntity("scene", hasPermission);
  const canWriteGroup = canWriteEntity("group", hasPermission);
  const canDeleteGroup = canDeleteEntity("group", hasPermission);
  const canReadStudios = canReadEntity("studio", hasPermission);
  const canReadTags = canReadEntity("tag", hasPermission);
  const canEngageGroup = canReadGroups && (user?.kind === "user" || user?.kind === "system");
  const { data: groupItems = [], isLoading: groupItemsLoading } = useQuery({
    queryKey: ["group-items", id],
    queryFn: () => groups.items.list(id),
    enabled: canReadGroups,
  });
  const { favorite: groupFavorite, setFavorite: setGroupFavorite, favoritePending: groupFavoritePending } = useEntityEngagement("group", id, {
    enabled: !!group,
  });
  const { data: playbackManifest, isLoading: playbackManifestLoading } = useQuery({
    queryKey: ["group", id, "playback-manifest"],
    queryFn: () => groups.items.playbackManifest(id),
    enabled: canReadScenes,
  });
  const hasPlaybackItems = (playbackManifest?.items.length ?? 0) > 0;
  const hasCompilationItems = groupItems.some((item) => item.kind === "sceneRange");

  useEffect(() => {
    if (group) document.title = `${group.name} | Cove`;
    return () => { document.title = "Cove"; };
  }, [group]);

  const deleteMut = useMutation({
    mutationFn: () => groups.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["groups"] });
      goBack();
    },
  });

  const tabs = useMemo(() => {
    const countedTabs = groupTabs.map((tab) => ({
      ...tab,
      count:
        tab.key === "items"
          ? groupItems.length + (group?.subGroupCount ?? 0)
          : tab.key === "containingGroups"
            ? group?.containingGroupCount
            : undefined,
    }));

    return filterItemsByPermission(countedTabs, {
      items: canReadScenes || canReadGroups ? "groups.read" : "__denied__",
      containingGroups: "groups.read",
      metadata: "groups.read",
      edit: "groups.read",
    }, hasPermission).filter((tab) => tab.key !== "items" || canReadScenes || canReadGroups);
  }, [canReadGroups, canReadScenes, group?.containingGroupCount, group?.subGroupCount, groupItems.length, groupTabs, hasPermission]);

  useEffect(() => {
    if (tabs.length > 0 && !tabs.some((tab) => tab.key === activeTab)) {
      setActiveTab(tabs[0].key as TabKey);
    }
  }, [activeTab, tabs]);

  if (isLoading) {
    return (
      <div className="px-1 py-2">
        <DetailSkeleton />
      </div>
    );
  }

  if (!group) {
    return <div className="py-16 text-center text-secondary">Group not found</div>;
  }

  const itemsContent = (
    <div className="space-y-6">
      {canReadScenes ? (
        <GroupScenesPanel
          groupId={id}
          filter={sceneFilter}
          setFilter={setSceneFilter}
          onNavigate={onNavigate}
          groupItems={groupItems}
          groupItemsLoading={groupItemsLoading}
          canWriteGroup={canWriteGroup}
        />
      ) : (
        <EmptyPanel icon={<Film className="h-12 w-12" />} message="Scene playback and scene list access are unavailable for this group." />
      )}

      {canReadGroups ? (
        <section className="rounded-2xl border border-border bg-card/70 p-5">
          <GroupSubGroupsPanel groupId={id} onNavigate={onNavigate} canWriteGroup={canWriteGroup} />
        </section>
      ) : null}
    </div>
  );

  const containingGroupsContent = (
    <section className="rounded-2xl border border-border bg-card/70 p-5">
      <div className="mb-4">
        <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Containing Groups</h2>
        <p className="mt-1 text-sm text-secondary">Browse the parent collections that already include this group.</p>
      </div>
      <GroupContainingGroupsPanel groupId={id} onNavigate={onNavigate} />
    </section>
  );

  const metadataContent = (
    <div className="space-y-6">
      <section className="rounded-2xl border border-border bg-card/70 p-5">
        <h2 className="mb-4 text-sm font-semibold uppercase tracking-wide text-muted">Metadata</h2>
        <dl className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          <MetadataField label="Scene Count" value={group.sceneCount} />
          <MetadataField label="Sub-Group Count" value={group.subGroupCount} />
          <MetadataField label="Containing Groups" value={group.containingGroupCount} />
          <MetadataField label="Created" value={formatDate(group.createdAt)} />
          <MetadataField label="Updated" value={formatDate(group.updatedAt)} />
          <MetadataField label="Aliases" value={group.aliases || "Not set"} />
        </dl>
      </section>

      {group.urls.length > 0 ? (
        <section className="rounded-2xl border border-border bg-card/70 p-5">
          <h2 className="mb-3 flex items-center gap-1.5 text-sm font-semibold uppercase tracking-wide text-muted">
            <LinkIcon className="h-4 w-4" /> URLs
          </h2>
          <div className="space-y-1 text-sm">
            {group.urls.map((url, index) => (
              <a key={index} href={url} target="_blank" rel="noopener noreferrer" className="block truncate text-accent hover:underline">
                {url}
              </a>
            ))}
          </div>
        </section>
      ) : null}

      <CustomFieldsDisplay customFields={group.customFields} entityType="group" />
      <ExtensionSlot slot="group-detail-sidebar-bottom" context={{ group, onNavigate }} />
    </div>
  );

  const editContent = (
    <section className="rounded-2xl border border-border bg-card/70 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Edit Group</h2>
          <p className="mt-1 text-sm text-secondary">
            {canWriteGroup
              ? "Open the group editor modal, adjust collection metadata, or remove the group entirely."
              : "You have read access to this group, but not write access."}
          </p>
        </div>
      </div>

      <div className="mt-5 flex flex-wrap gap-2">
        {canWriteGroup ? (
          <button
            type="button"
            onClick={() => setEditing(true)}
            className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
          >
            <Pencil className="h-4 w-4" />
            Edit metadata
          </button>
        ) : null}
        {canDeleteGroup ? (
          <button
            type="button"
            onClick={() => setConfirmDelete(true)}
            className="inline-flex items-center gap-2 rounded-lg border border-red-500/40 px-3 py-2 text-sm text-red-200 transition-colors hover:border-red-400"
          >
            <Trash2 className="h-4 w-4" />
            Delete group
          </button>
        ) : null}
        {hasPlaybackItems ? (
          <button
            type="button"
            onClick={() => onNavigate({ page: "compilation", id })}
            className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
          >
            <Play className="h-4 w-4" />
            {hasCompilationItems ? "Open standalone compilation" : "Open standalone group player"}
          </button>
        ) : null}
      </div>
    </section>
  );

  const activeContent =
    activeTab === "items"
      ? itemsContent
      : activeTab === "containingGroups"
        ? containingGroupsContent
        : activeTab === "metadata"
          ? metadataContent
          : activeTab === "edit"
            ? editContent
            : renderExtensionTab(activeTab, id, onNavigate);

  return (
    <div>
      <GroupEditModal group={group} open={editing} onClose={() => setEditing(false)} />
      <ConfirmDialog
        open={confirmDelete}
        title="Delete Group"
        message={`Delete "${group.name}"? This cannot be undone.`}
        onConfirm={() => deleteMut.mutate()}
        onCancel={() => setConfirmDelete(false)}
      />

      <EntityHeroLayout
        title={group.name}
        favorite={groupFavorite}
        onFavoriteToggle={canEngageGroup && !groupFavoritePending ? () => setGroupFavorite(!groupFavorite) : undefined}
        aliases={group.aliases || undefined}
        metaRow={
          <div className="flex flex-wrap items-center gap-3 text-sm text-secondary">
            {group.date ? <span>{formatDate(group.date)}</span> : null}
            {group.director ? <span>Director: {group.director}</span> : null}
            {group.duration ? <span className="inline-flex items-center gap-1"><Clapperboard className="h-4 w-4" /> {formatDuration(group.duration)}</span> : null}
            {group.studioName && group.studioId ? (
              canReadStudios ? (
                <button onClick={() => onNavigate({ page: "studio", id: group.studioId })} className="text-accent hover:underline">
                  {group.studioName}
                </button>
              ) : (
                <span>{group.studioName}</span>
              )
            ) : null}
          </div>
        }
        backLabel={backLabel}
        onGoBack={goBack}
        imageUrl={group.frontImagePath}
        imageAlt={group.name}
        imageFallback={<Layers className="h-14 w-14" />}
        counts={[
          { key: "scenes", label: "Scenes", value: group.sceneCount, icon: <Film className="h-4 w-4" /> },
          { key: "subgroups", label: "Sub-groups", value: group.subGroupCount, icon: <Layers className="h-4 w-4" /> },
          { key: "containing", label: "Containing", value: group.containingGroupCount, icon: <LinkIcon className="h-4 w-4" /> },
        ]}
        actions={
          <>
            <ExtensionSlot slot="group-detail-actions" context={{ group, onNavigate }} />
            {hasPlaybackItems ? (
              <button
                type="button"
                onClick={() => onNavigate({ page: "compilation", id })}
                className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
                title={hasCompilationItems ? "Standalone Compilation" : "Standalone Player"}
              >
                <Play className="h-4 w-4" />
              </button>
            ) : null}
            {canWriteGroup ? (
              <button
                type="button"
                onClick={() => setEditing(true)}
                className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
                title="Edit"
              >
                <Pencil className="h-4 w-4" />
              </button>
            ) : null}
            {canDeleteGroup ? (
              <button
                type="button"
                onClick={() => setConfirmDelete(true)}
                className="inline-flex items-center justify-center rounded p-1 text-red-300 transition hover:bg-red-500/10 hover:text-red-200"
                title="Delete"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            ) : null}
          </>
        }
      >
        <div className="mx-auto max-w-7xl">
          {(group.synopsis || (canReadTags && group.tags.length > 0)) ? (
            <section className="mb-4 space-y-4 rounded-2xl border border-border bg-card/70 p-5">
              {group.synopsis ? (
                <div>
                  <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">Synopsis</h2>
                  <p className="mt-3 whitespace-pre-wrap text-sm leading-7 text-foreground/92">{group.synopsis}</p>
                </div>
              ) : null}
              {canReadTags && group.tags.length > 0 ? (
                <div>
                  <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">Tags</h2>
                  <div className="mt-3 flex flex-wrap gap-1.5">
                    {group.tags.map((tag) => (
                      <TagBadge key={tag.id} name={tag.name} tag={tag} onClick={() => onNavigate({ page: "tag", id: tag.id })} />
                    ))}
                  </div>
                </div>
              ) : null}
            </section>
          ) : null}
          <EntityDetailTabs tabs={tabs} activeTab={activeTab} onTabChange={(key) => setActiveTab(key as TabKey)} />
          <div className="py-6">
            {activeContent}
            <ExtensionSlot slot="group-detail-main-bottom" context={{ group, onNavigate }} />
          </div>
        </div>
      </EntityHeroLayout>

      <ExtensionSlot slot="group-detail-bottom" context={{ group, onNavigate }} />
    </div>
  );
}

function MetadataField({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-border bg-surface/50 px-4 py-3">
      <dt className="text-xs font-semibold uppercase tracking-wide text-muted">{label}</dt>
      <dd className="mt-1 text-sm text-foreground">{value}</dd>
    </div>
  );
}

function GroupScenesPanel({ groupId, filter, setFilter, onNavigate, groupItems, groupItemsLoading, canWriteGroup }: {
  groupId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
  groupItems?: GroupItem[];
  groupItemsLoading?: boolean;
  canWriteGroup?: boolean;
}) {
  const queryClient = useQueryClient();
  const [zoomLevel, setZoomLevel] = useState(0);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const [objectFilter, setObjectFilter] = useState<Record<string, unknown>>({});
  const hasObjectFilter = Object.keys(objectFilter).length > 0;
  const { data: groupScenes, isLoading, infinitePageSize, infiniteQuery, infiniteFilterKey, fetchAllIds, loadMore } = useDetailListQuery<Scene>({
    queryKey: ["group-scenes", groupId, objectFilter],
    filter,
    queryFn: (nextFilter) => hasObjectFilter
      ? scenes.findFiltered({
          findFilter: nextFilter,
          objectFilter: withRequiredMultiId(objectFilter as SceneFilterCriteria, "groupsCriterion", groupId),
        })
      : scenes.find(nextFilter, { groupId: String(groupId) }),
  });
  const deleteItemMutation = useMutation({
    mutationFn: (itemId: number) => groups.items.delete(groupId, itemId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["group-items", groupId] });
      queryClient.invalidateQueries({ queryKey: ["group", groupId] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    },
  });
  const reorderItemMutation = useMutation({
    mutationFn: (ids: number[]) => groups.items.reorder(groupId, { ids }),
    onMutate: async (ids) => {
      await queryClient.cancelQueries({ queryKey: ["group-items", groupId] });
      const previousItems = queryClient.getQueryData<GroupItem[]>(["group-items", groupId]) ?? [];
      const itemsById = new Map(previousItems.map((item) => [item.id, item]));
      const nextItems = ids
        .map((itemId, index) => {
          const item = itemsById.get(itemId);
          return item ? { ...item, orderIndex: index } : undefined;
        })
        .filter((item): item is GroupItem => item != null);

      queryClient.setQueryData(["group-items", groupId], nextItems);
      return { previousItems };
    },
    onError: (_error, _ids, context) => {
      if (context?.previousItems) {
        queryClient.setQueryData(["group-items", groupId], context.previousItems);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ["group-items", groupId] });
    },
  });
  const items = groupScenes?.items ?? [];
  const { selectedIds, toggle, selectAll, selectAllPending, selectShown, selectNone } = useDetailListSelection({ items, infinitePageSize, infiniteFilterKey, fetchAllIds, resetKeyParts: [objectFilter] });
  const selecting = selectedIds.size > 0;
  const toolbar = (
    <DetailListToolbar
      filter={filter}
      onFilterChange={setFilter}
      totalCount={groupScenes?.totalCount ?? 0}
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
      selectAllPending={selectAllPending}
      onSelectAllMatching={selectShown}
      selectAllMatchingLabel="Select shown"
      onSelectNone={selectNone}
      selectionActions={<BulkSelectionActions entityType="scenes" selectedIds={selectedIds} onDone={selectNone} sceneItems={items} onNavigate={onNavigate} />}
      criteriaDefinitions={SCENE_CRITERIA}
      objectFilter={objectFilter}
      onObjectFilterChange={setObjectFilter}
      allowInfinitePageSize
    />
  );

  if (groupItemsLoading) {
    return <LoadingPanel icon={<Film className="h-10 w-10" />} message="Loading group items..." />;
  }

  if (groupItems && groupItems.length > 0) {
    const orderedItems = [...groupItems].sort((left, right) => left.orderIndex - right.orderIndex || left.id - right.id);

    return (
      <div className="space-y-4">
        <div className="flex items-center justify-between rounded-xl border border-border bg-card p-4">
          <div>
            <div className="text-sm font-semibold text-foreground">Group Items</div>
            <div className="mt-1 text-sm text-secondary">This tab now reads the ordered playback items directly from the new group item API.</div>
          </div>
          <div className="text-xs text-muted">{orderedItems.length} item{orderedItems.length === 1 ? "" : "s"}</div>
        </div>

        <SortableList
          items={orderedItems}
          getKey={(item) => item.id}
          onReorder={(nextItems) => reorderItemMutation.mutate(nextItems.map((item) => item.id))}
          disabled={!canWriteGroup || reorderItemMutation.isPending}
          className="space-y-2"
          renderItem={(item, { dragHandleProps, isDragging, isOver }) => {
            const label = item.title || item.sceneTitle || `Scene #${item.sceneId}`;
            return (
              <div className={`rounded-xl border bg-card/80 p-4 transition-colors ${isDragging ? "border-accent opacity-40" : isOver ? "border-accent bg-accent/5" : "border-border"}`}>
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div className="flex items-start gap-3">
                    {canWriteGroup ? (
                      <span {...dragHandleProps} className="mt-0.5 inline-flex shrink-0 cursor-grab items-center text-muted active:cursor-grabbing">
                        <GripVertical className="h-4 w-4" />
                      </span>
                    ) : null}
                    <div>
                      <div className="text-sm font-medium text-foreground">{label}</div>
                      <div className="mt-1 flex flex-wrap gap-2 text-xs text-secondary">
                        <span>#{item.orderIndex + 1}</span>
                        <span>{item.kind === "sceneRange" ? formatDurationRange(item.startSec, item.endSec) : "Full scene"}</span>
                        {item.sourceSpanKey ? <span>Span snapshot</span> : null}
                      </div>
                    </div>
                  </div>
                  <div className="flex flex-wrap gap-2">
                    <button
                      type="button"
                      onClick={() => onNavigate({ page: "scene", id: item.sceneId, seekTo: item.startSec ?? 0 })}
                      className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
                    >
                      <ExternalLink className="h-4 w-4" />
                      Open scene
                    </button>
                    {item.sourceSpanKey ? (
                      <button
                        type="button"
                        onClick={() => onNavigate({
                          page: "scene-span",
                          id: item.sceneId,
                          spanKey: item.sourceSpanKey,
                          profileId: item.sourceProfileId,
                          derivedQueryDescriptor: parseGroupItemDerivedQueryDescriptor(item.sourceQueryJson),
                        })}
                        className="rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
                      >
                        Open span
                      </button>
                    ) : null}
                    {canWriteGroup ? (
                      <>
                        <button
                          type="button"
                          onClick={() => deleteItemMutation.mutate(item.id)}
                          disabled={deleteItemMutation.isPending}
                          className="rounded-lg border border-red-400/30 px-3 py-2 text-sm text-red-200 transition-colors hover:border-red-400 disabled:cursor-not-allowed disabled:opacity-50"
                        >
                          Remove
                        </button>
                      </>
                    ) : null}
                  </div>
                </div>
              </div>
            );
          }}
        />
      </div>
    );
  }

  if (isLoading) return <LoadingPanel icon={<Film className="h-10 w-10" />} message="Loading scenes..." />;
  if (!groupScenes || items.length === 0) return <>{toolbar}<EmptyPanel icon={<Film className="h-12 w-12" />} message="No scenes in this group" /></>;

  return (
    <>
      {toolbar}
      <VirtualizedEntityGrid
        items={items}
        getItemKey={(scene) => scene.id}
        minCardWidth={`${220 + zoomLevel * 50}px`}
        virtualMinColumnWidth={220 + zoomLevel * 50}
        estimateRowHeight={320}
        gap={16}
        gapClassName="gap-4"
        infinitePageSize={infinitePageSize}
        hasNextPage={infiniteQuery.hasNextPage}
        isFetchingNextPage={infiniteQuery.isFetchingNextPage}
        loadMore={loadMore}
        renderItem={(scene) => (
          <SceneCard scene={scene} onClick={() => selecting ? toggle(scene.id) : onNavigate({ page: "scene", id: scene.id })} onNavigate={onNavigate} onQuickView={() => setQuickViewId(scene.id)} selected={selectedIds.has(scene.id)} onSelect={() => toggle(scene.id)} selecting={selecting} />
        )}
      />
      {quickViewId !== null && (
        <QuickViewDialog type="scene" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      )}
    </>
  );
}

function parseGroupItemDerivedQueryDescriptor(sourceQueryJson?: string): SegmentDerivedQueryDescriptor | undefined {
  if (!sourceQueryJson) {
    return undefined;
  }

  try {
    const parsed = JSON.parse(sourceQueryJson) as SegmentSpanDerivedQuery;
    if (!parsed || typeof parsed !== "object" || !Array.isArray(parsed.operands) || typeof parsed.operator !== "string") {
      return undefined;
    }

    return {
      operator: parsed.operator,
      mergeGapSec: typeof parsed.mergeGapSec === "number" ? parsed.mergeGapSec : undefined,
      minDurationSec: typeof parsed.minDurationSec === "number" ? parsed.minDurationSec : undefined,
      operands: parsed.operands
        .filter((operand) => operand != null && typeof operand === "object")
        .map((operand) => ({
          sourceKey: operand.sourceKey,
          kind: operand.kind,
          tagIds: Array.isArray(operand.tagIds) ? operand.tagIds.filter((value): value is number => Number.isInteger(value) && value > 0) : undefined,
          faceIds: Array.isArray(operand.refIds) ? operand.refIds.filter((value): value is number => Number.isInteger(value) && value > 0) : undefined,
          minConfidence: typeof operand.minConfidence === "number" ? operand.minConfidence : undefined,
        })),
    };
  } catch {
    return undefined;
  }
}
function GroupSubGroupsPanel({ groupId, onNavigate, canWriteGroup }: { groupId: number; onNavigate: (r: any) => void; canWriteGroup: boolean }) {
  const queryClient = useQueryClient();
  const { data: subGroups, isLoading } = useQuery({
    queryKey: ["group-subgroups", groupId],
    queryFn: () => groups.subGroups(groupId),
  });
  const [showAddDialog, setShowAddDialog] = useState(false);
  const [searchTerm, setSearchTerm] = useState("");

  const { data: searchResults } = useQuery({
    queryKey: ["groups-search-for-subgroup", searchTerm],
    queryFn: () => groups.find({ page: 1, perPage: 20, q: searchTerm }),
    enabled: showAddDialog && searchTerm.length > 0,
  });

  const addMut = useMutation({
    mutationFn: (subGroupId: number) => groups.addSubGroup(groupId, subGroupId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["group-subgroups", groupId] }),
  });

  const removeMut = useMutation({
    mutationFn: (subGroupId: number) => groups.removeSubGroup(groupId, subGroupId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["group-subgroups", groupId] }),
  });

  const reorderMut = useMutation({
    mutationFn: (ids: number[]) => groups.reorderSubGroups(groupId, ids),
    onMutate: async (ids) => {
      await queryClient.cancelQueries({ queryKey: ["group-subgroups", groupId] });
      const previousGroups = queryClient.getQueryData<Group[]>(["group-subgroups", groupId]) ?? [];
      const groupsById = new Map(previousGroups.map((group) => [group.id, group]));
      const nextGroups = ids
        .map((groupIdToMove) => groupsById.get(groupIdToMove))
        .filter((group): group is Group => group != null);

      queryClient.setQueryData(["group-subgroups", groupId], nextGroups);
      return { previousGroups };
    },
    onError: (_error, _ids, context) => {
      if (context?.previousGroups) {
        queryClient.setQueryData(["group-subgroups", groupId], context.previousGroups);
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ["group-subgroups", groupId] });
    },
  });

  const existingIds = new Set(subGroups?.map((g) => g.id) ?? []);
  const availableResults = (searchResults?.items ?? []).filter((g) => g.id !== groupId && !existingIds.has(g.id));

  if (isLoading) return <LoadingPanel icon={<Layers className="h-10 w-10" />} message="Loading sub-groups..." />;

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold text-muted uppercase tracking-wider">Sub-Groups</h3>
        {canWriteGroup ? <button
          onClick={() => setShowAddDialog(!showAddDialog)}
          className="flex items-center gap-1 px-2 py-1 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10 border border-border"
        >
          <Plus className="w-3 h-3" />
          Add Sub-Group
        </button> : null}
      </div>

      {/* Add sub-group search */}
      {showAddDialog && canWriteGroup && (
        <div className="rounded-xl border border-border bg-card p-4">
          <div className="flex items-center gap-2 mb-3">
            <input
              type="text"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              placeholder="Search groups to add..."
              className="flex-1 bg-input border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
              autoFocus
            />
            <button onClick={() => { setShowAddDialog(false); setSearchTerm(""); }} className="p-1.5 rounded hover:bg-surface text-muted"><X className="w-4 h-4" /></button>
          </div>
          {availableResults.length > 0 ? (
            <div className="space-y-1 max-h-48 overflow-y-auto">
              {availableResults.map((g) => (
                <button
                  key={g.id}
                  onClick={() => addMut.mutate(g.id)}
                  disabled={addMut.isPending}
                  className="w-full flex items-center justify-between px-3 py-2 rounded text-left text-sm hover:bg-surface text-foreground"
                >
                  <span>{g.name}</span>
                  <Plus className="w-3.5 h-3.5 text-muted" />
                </button>
              ))}
            </div>
          ) : searchTerm.length > 0 ? (
            <p className="text-sm text-muted text-center py-4">No groups found</p>
          ) : (
            <p className="text-sm text-muted text-center py-4">Type to search for groups</p>
          )}
        </div>
      )}

      {subGroups && subGroups.length > 0 ? (
        <SortableList
          items={subGroups}
          getKey={(item) => item.id}
          onReorder={(nextGroups) => reorderMut.mutate(nextGroups.map((item) => item.id))}
          disabled={!canWriteGroup || reorderMut.isPending}
          className="space-y-2"
          renderItem={(g, { dragHandleProps, index, isDragging, isOver }) => (
            <div className={`group flex items-center gap-3 rounded-xl border bg-card px-4 py-3 transition-colors ${isDragging ? "border-accent opacity-40" : isOver ? "border-accent bg-accent/5" : "border-border"}`}>
              {canWriteGroup ? (
                <span {...dragHandleProps} className="inline-flex shrink-0 cursor-grab items-center text-muted active:cursor-grabbing">
                  <GripVertical className="h-4 w-4" />
                </span>
              ) : null}
              <span className="w-6 text-center text-xs text-muted">{index + 1}</span>
              <button onClick={() => onNavigate({ page: "group", id: g.id })} className="flex-1 text-left text-sm font-medium text-foreground hover:text-accent">{g.name}</button>
              <span className="text-xs text-muted">{g.sceneCount} scenes</span>
              {canWriteGroup ? <button
                onClick={() => { if (confirm(`Remove "${g.name}" from sub-groups?`)) removeMut.mutate(g.id); }}
                className="opacity-0 group-hover:opacity-100 p-1 rounded hover:bg-red-900/20 text-muted hover:text-red-400"
              >
                <X className="w-3.5 h-3.5" />
              </button> : null}
            </div>
          )}
        />
      ) : (
        <EmptyPanel icon={<Layers className="h-12 w-12" />} message="No sub-groups" />
      )}
    </div>
  );
}

function GroupContainingGroupsPanel({ groupId, onNavigate }: { groupId: number; onNavigate: (r: any) => void }) {
  const { data: containingGroups, isLoading } = useQuery({
    queryKey: ["group-containinggroups", groupId],
    queryFn: () => groups.containingGroups(groupId),
  });

  if (isLoading) return <LoadingPanel icon={<Layers className="h-10 w-10" />} message="Loading containing groups..." />;
  if (!containingGroups || containingGroups.length === 0) return <EmptyPanel icon={<Layers className="h-12 w-12" />} message="No containing groups" />;

  return (
    <div className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
      {containingGroups.map((g) => (
        <GroupTile key={g.id} group={g} onClick={() => onNavigate({ page: "group", id: g.id })} />
      ))}
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

function formatDurationRange(startSec?: number, endSec?: number) {
  if (startSec == null || endSec == null) {
    return "Range unavailable";
  }

  return `${formatDurationValue(startSec)} - ${formatDurationValue(endSec)}`;
}

function formatDurationValue(value: number) {
  const totalSeconds = Math.max(0, Math.floor(value));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
    : `${minutes}:${String(seconds).padStart(2, "0")}`;
}