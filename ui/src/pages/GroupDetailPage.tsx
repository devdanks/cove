import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { groups, scenes } from "../api/client";
import type { FindFilter, Group, GroupItem, Image as ImageEntity, Scene, SegmentDerivedQueryDescriptor, SegmentSpanDerivedQuery } from "../api/types";
import { formatDate, formatDuration, CustomFieldsDisplay } from "../components/shared";
import { Clapperboard, ExternalLink, Film, GripVertical, Image as ImageIcon, Layers, Link as LinkIcon, Pencil, Play, Plus, RefreshCw, Trash2, X } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { GroupEditModal } from "./GroupEditModal";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { ExtensionSlot } from "../router/RouteRegistry";
import { GroupTile, ImageTile, SceneCard } from "../components/EntityCards";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { QuickViewDialog } from "../components/QuickViewDialog";
import { DetailListToolbar } from "../components/DetailListToolbar";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { EntityHeroLayout } from "../components/EntityHeroLayout";
import { EntityDetailTabs } from "../components/EntityDetailTabs";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { BulkSelectionActions } from "../components/BulkSelectionActions";
import { useExtensionTabs } from "../components/useExtensionTabs";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity, filterItemsByPermission } from "../auth/visibility";
import { SortableList, type DragHandleProps } from "../components/SortableList";
import { BookmarkButton } from "../components/BookmarkButton";
import { SCENE_SORT_OPTIONS } from "../components/sceneSortOptions";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type TabKey = "items" | "containingGroups" | "metadata" | "edit" | (string & {});
type GroupItemsDisplayMode = "grid" | "list";

export function GroupDetailPage({ id, onNavigate }: Props) {
  const { data: group, isLoading } = useQuery({
    queryKey: ["group", id],
    queryFn: () => groups.get(id),
  });
  const { hasPermission } = useAuth();
  const [editing, setEditing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>("items");
  const [groupItemsDisplayMode, setGroupItemsDisplayMode] = useState<GroupItemsDisplayMode>("grid");
  const { allTabs: groupTabs, renderExtensionTab } = useExtensionTabs("group", [
    { key: "items", label: "Items" },
    { key: "containingGroups", label: "Containing Groups" },
    { key: "metadata", label: "Metadata" },
    { key: "edit", label: "Edit" },
  ], id);
  const [sceneFilter, setSceneFilter] = useState<FindFilter>({ page: 1, perPage: 24, direction: "asc", sort: "date" });
  const [groupItemFilter, setGroupItemFilter] = useState<FindFilter>({ page: 1, perPage: 40, direction: "asc", sort: "order" });
  const queryClient = useQueryClient();
  const { backLabel, goBack } = useBackNavigation({ page: "groups" }, onNavigate);
  const canReadGroups = canReadEntity("group", hasPermission);
  const canReadScenes = canReadEntity("scene", hasPermission);
  const canWriteGroup = canWriteEntity("group", hasPermission);
  const canDeleteGroup = canDeleteEntity("group", hasPermission);
  const canReadStudios = canReadEntity("studio", hasPermission);
  const canReadTags = canReadEntity("tag", hasPermission);
  const isDynamicGroup = group?.kind === "dynamic";
  const isBuiltInPersonalGroup = isDynamicGroup && isBuiltInPersonalDynamicSource(group?.querySourceKey);
  const canModifyGroup = canWriteGroup && !isBuiltInPersonalGroup;
  const canRemoveGroup = canDeleteGroup && !isBuiltInPersonalGroup;
  const { data: groupItemsPage, isLoading: pagedGroupItemsLoading } = useQuery({
    queryKey: ["group-items-page", id, groupItemFilter],
    queryFn: () => groups.items.page(id, groupItemFilter),
    enabled: canReadGroups && !!group,
  });
  const groupItems = groupItemsPage?.items ?? [];
  const groupItemsLoading = pagedGroupItemsLoading;
  const groupItemsTotalCount = groupItemsPage?.totalCount ?? (isDynamicGroup ? group?.cachedItemCount ?? groupItems.length : groupItems.length);
  const { data: playbackManifest, isLoading: playbackManifestLoading } = useQuery({
    queryKey: ["group", id, "playback-manifest"],
    queryFn: () => groups.items.playbackManifest(id),
    enabled: canReadScenes,
  });
  const hasPlaybackItems = (playbackManifest?.items.length ?? 0) > 0;
  const hasCompilationItems = playbackManifest?.items.some((item) => item.endSec != null) ?? groupItems.some((item) => item.kind === "sceneRange");

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

  const snapshotMut = useMutation({
    mutationFn: () => groups.snapshot(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["group", id] });
      queryClient.invalidateQueries({ queryKey: ["group-items", id] });
      queryClient.invalidateQueries({ queryKey: ["group-items-page", id] });
      queryClient.invalidateQueries({ queryKey: ["group", id, "playback-manifest"] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    },
  });

  const showInSceneListsMut = useMutation({
    mutationFn: (showInSceneLists: boolean) => groups.update(id, { showInSceneLists }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["group", id] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    },
  });

  const tabs = useMemo(() => {
    const countedTabs = groupTabs.map((tab) => ({
      ...tab,
      count:
        tab.key === "items"
          ? groupItemsTotalCount + (group?.subGroupCount ?? 0)
          : tab.key === "containingGroups"
            ? group?.containingGroupCount
            : undefined,
    }));

    return filterItemsByPermission(countedTabs, {
      items: canReadScenes || canReadGroups ? "groups.read" : "__denied__",
      containingGroups: "groups.read",
      metadata: "groups.read",
      edit: "groups.read",
    }, hasPermission)
      .filter((tab) => tab.key !== "items" || canReadScenes || canReadGroups)
      .filter((tab) => !isBuiltInPersonalGroup || tab.key !== "edit");
  }, [canReadGroups, canReadScenes, group?.containingGroupCount, group?.subGroupCount, groupItemsTotalCount, groupTabs, hasPermission, isBuiltInPersonalGroup]);

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
          groupItemsTotalCount={groupItemsTotalCount}
          groupItemFilter={groupItemFilter}
          setGroupItemFilter={setGroupItemFilter}
          groupItemsDisplayMode={groupItemsDisplayMode}
          setGroupItemsDisplayMode={setGroupItemsDisplayMode}
          canWriteGroup={canModifyGroup}
          group={group}
          onRefreshDynamic={() => {
            queryClient.invalidateQueries({ queryKey: ["group-items", id] });
            queryClient.invalidateQueries({ queryKey: ["group-items-page", id] });
            queryClient.invalidateQueries({ queryKey: ["group", id, "playback-manifest"] });
            queryClient.invalidateQueries({ queryKey: ["group", id] });
          }}
          refreshingDynamic={groupItemsLoading || playbackManifestLoading}
          onSnapshotDynamic={() => snapshotMut.mutate()}
          snapshottingDynamic={snapshotMut.isPending}
        />
      ) : (
        <EmptyPanel icon={<Film className="h-12 w-12" />} message="Scene playback and scene list access are unavailable for this group." />
      )}

      {canReadGroups ? (
        <section className="rounded-2xl border border-border bg-card/70 p-5">
          <GroupSubGroupsPanel groupId={id} onNavigate={onNavigate} canWriteGroup={canModifyGroup} />
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
            {canModifyGroup
              ? "Open the group editor modal, adjust collection metadata, or remove the group entirely."
              : isBuiltInPersonalGroup
                ? "This built-in personal group is managed automatically."
                : "You have read access to this group, but not write access."}
          </p>
        </div>
      </div>

      <div className="mt-5 flex flex-wrap gap-2">
        {canModifyGroup ? (
          <button
            type="button"
            onClick={() => setEditing(true)}
            className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
          >
            <Pencil className="h-4 w-4" />
            Edit metadata
          </button>
        ) : null}
        {canModifyGroup ? (
          <button
            type="button"
            onClick={() => showInSceneListsMut.mutate(!(group.showInSceneLists ?? true))}
            disabled={showInSceneListsMut.isPending}
            className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-wait disabled:opacity-60"
          >
            {group.showInSceneLists ?? true ? "Hide from Scenes list" : "Show in Scenes list"}
          </button>
        ) : null}
        {canRemoveGroup ? (
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

  const heroCounts = group.kind === "dynamic"
    ? [
        { key: "items", label: "Items", value: groupItemsTotalCount || group.cachedItemCount || 0, icon: <Layers className="h-4 w-4" /> },
        { key: "source", label: "Source", value: formatDynamicSourceLabel(group.querySourceKey), icon: <RefreshCw className="h-4 w-4" /> },
        { key: "containing", label: "Containing", value: group.containingGroupCount, icon: <LinkIcon className="h-4 w-4" /> },
      ]
    : [
        { key: "scenes", label: "Scenes", value: group.sceneCount, icon: <Film className="h-4 w-4" /> },
        { key: "subgroups", label: "Sub-groups", value: group.subGroupCount, icon: <Layers className="h-4 w-4" /> },
        { key: "containing", label: "Containing", value: group.containingGroupCount, icon: <LinkIcon className="h-4 w-4" /> },
      ];

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
        imageFallback={<Layers className="h-14 w-14" />}
        counts={heroCounts}
        actions={
          <>
            <ExtensionSlot slot="group-detail-actions" context={{ group, onNavigate }} />
            {!isBuiltInPersonalGroup ? <BookmarkButton hostType="group" hostId={group.id} compact /> : null}
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
            {canModifyGroup ? (
              <button
                type="button"
                onClick={() => setEditing(true)}
                className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
                title="Edit"
              >
                <Pencil className="h-4 w-4" />
              </button>
            ) : null}
            {canRemoveGroup ? (
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
        <EntityDetailTabs tabs={tabs} activeTab={activeTab} onTabChange={(key) => setActiveTab(key as TabKey)} className="mx-auto max-w-7xl" />
        <div className="py-6">
          {activeContent}
          <ExtensionSlot slot="group-detail-main-bottom" context={{ group, onNavigate }} />
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

function GroupScenesPanel({ groupId, filter, setFilter, onNavigate, groupItems, groupItemsLoading, groupItemsTotalCount, groupItemFilter, setGroupItemFilter, groupItemsDisplayMode, setGroupItemsDisplayMode, canWriteGroup, group, onRefreshDynamic, refreshingDynamic, onSnapshotDynamic, snapshottingDynamic }: {
  groupId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
  groupItems?: GroupItem[];
  groupItemsLoading?: boolean;
  groupItemsTotalCount?: number;
  groupItemFilter?: FindFilter;
  setGroupItemFilter?: (filter: FindFilter) => void;
  groupItemsDisplayMode: GroupItemsDisplayMode;
  setGroupItemsDisplayMode: (mode: GroupItemsDisplayMode) => void;
  canWriteGroup?: boolean;
  group: Group;
  onRefreshDynamic?: () => void;
  refreshingDynamic?: boolean;
  onSnapshotDynamic?: () => void;
  snapshottingDynamic?: boolean;
}) {
  const queryClient = useQueryClient();
  const [zoomLevel, setZoomLevel] = useState(0);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const isDynamic = group.kind === "dynamic";
  const { data: groupScenes, isLoading } = useQuery({
    queryKey: ["group-scenes", groupId, filter],
    queryFn: () => scenes.find(filter, { groupId: String(groupId) }),
    enabled: !isDynamic,
  });
  const deleteItemMutation = useMutation({
    mutationFn: (itemId: number) => groups.items.delete(groupId, itemId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["group-items", groupId] });
      queryClient.invalidateQueries({ queryKey: ["group-items-page", groupId] });
      queryClient.invalidateQueries({ queryKey: ["group", groupId] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    },
  });
  const reorderItemMutation = useMutation({
    mutationFn: (ids: number[]) => groups.items.reorder(groupId, { ids, startIndex: ((groupItemFilter?.page ?? 1) - 1) * (groupItemFilter?.perPage ?? 40) }),
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ["group-items", groupId] });
      queryClient.invalidateQueries({ queryKey: ["group-items-page", groupId] });
      queryClient.invalidateQueries({ queryKey: ["group", groupId, "playback-manifest"] });
    },
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(groupScenes?.items ?? []);
  const selecting = selectedIds.size > 0;

  useEffect(() => {
    if (isDynamic && !groupItemsLoading && groupItems) {
      queryClient.invalidateQueries({ queryKey: ["group", groupId] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    }
  }, [groupId, groupItems, groupItemsLoading, isDynamic, queryClient]);

  if (groupItemsLoading) {
    return <LoadingPanel icon={<Film className="h-10 w-10" />} message="Loading group items..." />;
  }

  if (isDynamic || (groupItemsTotalCount ?? 0) > 0 || (groupItems && groupItems.length > 0)) {
    const orderedItems = [...(groupItems ?? [])].sort((left, right) => left.orderIndex - right.orderIndex || left.id - right.id);
    const canReorderItems = !isDynamic && !!canWriteGroup && (groupItemFilter?.sort ?? "order") === "order" && (groupItemFilter?.direction ?? "asc") !== "desc";
    const bookmarkInitiallySaved = group.querySourceKey === "save-for-later" ? true : undefined;

    return (
      <div className="space-y-4">
        {isDynamic ? (
          <DynamicGroupBanner
            group={group}
            resolvedCount={groupItemsTotalCount ?? orderedItems.length}
            onRefresh={onRefreshDynamic}
            refreshing={refreshingDynamic}
            onSnapshot={onSnapshotDynamic}
            snapshotting={snapshottingDynamic}
            canWriteGroup={!!canWriteGroup}
          />
        ) : null}

        <div className="flex items-center justify-between rounded-xl border border-border bg-card p-4">
          <div>
            <div className="text-sm font-semibold text-foreground">{isDynamic ? "Resolved Items" : "Group Items"}</div>
          </div>
          <div className="text-xs text-muted">{(isDynamic ? groupItemsTotalCount ?? orderedItems.length : orderedItems.length)} item{(isDynamic ? groupItemsTotalCount ?? orderedItems.length : orderedItems.length) === 1 ? "" : "s"}</div>
        </div>

        {groupItemFilter && setGroupItemFilter ? (
          <DetailListToolbar
            filter={groupItemFilter}
            onFilterChange={setGroupItemFilter}
            totalCount={groupItemsTotalCount ?? orderedItems.length}
            sortOptions={[
              { value: "order", label: "Manual Order" },
              { value: "title", label: "Title" },
              { value: "kind", label: "Kind" },
              { value: "created_at", label: "Created At" },
            ]}
            showSort={!isDynamic}
            showSearch={!isDynamic}
            zoomLevel={zoomLevel}
            onZoomChange={setZoomLevel}
            displayMode={groupItemsDisplayMode}
            onDisplayModeChange={setGroupItemsDisplayMode}
          />
        ) : null}

        {orderedItems.length === 0 ? (
          <EmptyPanel icon={<Layers className="h-12 w-12" />} message={isDynamic ? "No items resolved for this dynamic group" : "No group items"} />
        ) : null}

        {groupItemsDisplayMode === "grid" ? (
          canReorderItems ? (
            <SortableList
              items={orderedItems}
              getKey={(item) => item.id}
              onReorder={(nextItems) => reorderItemMutation.mutate(nextItems.map((item) => item.id))}
              disabled={reorderItemMutation.isPending}
              className="grid gap-4"
              style={{ gridTemplateColumns: `repeat(auto-fill, minmax(${220 + zoomLevel * 50}px, 1fr))` }}
              renderItem={(item, { dragHandleProps, isDragging, isOver }) => (
                <GroupItemCard item={item} onNavigate={onNavigate} onRemove={canWriteGroup ? () => deleteItemMutation.mutate(item.id) : undefined} dragHandleProps={dragHandleProps} isDragging={isDragging} isOver={isOver} bookmarkInitiallySaved={bookmarkInitiallySaved} />
              )}
            />
          ) : (
            <EntityCardGrid minCardWidth={`${220 + zoomLevel * 50}px`} gapClassName="gap-4">
              {orderedItems.map((item) => (
                <GroupItemCard key={item.id} item={item} onNavigate={onNavigate} onRemove={!isDynamic && canWriteGroup ? () => deleteItemMutation.mutate(item.id) : undefined} bookmarkInitiallySaved={bookmarkInitiallySaved} />
              ))}
            </EntityCardGrid>
          )
        ) : canReorderItems ? (
          <SortableList
            items={orderedItems}
            getKey={(item) => item.id}
            onReorder={(nextItems) => reorderItemMutation.mutate(nextItems.map((item) => item.id))}
            disabled={reorderItemMutation.isPending}
            className="space-y-2"
            renderItem={(item, { dragHandleProps, isDragging, isOver }) => (
              <GroupItemRow item={item} onNavigate={onNavigate} onRemove={canWriteGroup ? () => deleteItemMutation.mutate(item.id) : undefined} dragHandleProps={dragHandleProps} isDragging={isDragging} isOver={isOver} />
            )}
          />
        ) : (
          <div className="space-y-2">
            {orderedItems.map((item) => (
              <GroupItemRow key={item.id} item={item} onNavigate={onNavigate} onRemove={!isDynamic && canWriteGroup ? () => deleteItemMutation.mutate(item.id) : undefined} />
            ))}
          </div>
        )}
      </div>
    );
  }

  if (isLoading) return <LoadingPanel icon={<Film className="h-10 w-10" />} message="Loading scenes..." />;
  if (!groupScenes || groupScenes.items.length === 0) return <EmptyPanel icon={<Film className="h-12 w-12" />} message="No scenes in this group" />;

  return (
    <>
      <DetailListToolbar
        filter={filter}
        onFilterChange={setFilter}
        totalCount={groupScenes.totalCount}
        sortOptions={SCENE_SORT_OPTIONS}
        zoomLevel={zoomLevel}
        onZoomChange={setZoomLevel}
        showSearch
        selectedCount={selectedIds.size}
        onSelectAll={selectAll}
        onSelectNone={selectNone}
        selectionActions={<BulkSelectionActions entityType="scenes" selectedIds={selectedIds} onDone={selectNone} sceneItems={groupScenes.items} onNavigate={onNavigate} />}
      />
      <EntityCardGrid minCardWidth={`${220 + zoomLevel * 50}px`} gapClassName="gap-4">
        {groupScenes.items.map((scene) => (
          <SceneCard key={scene.id} scene={scene} onClick={() => selecting ? toggle(scene.id) : onNavigate({ page: "scene", id: scene.id })} onNavigate={onNavigate} onQuickView={() => setQuickViewId(scene.id)} selected={selectedIds.has(scene.id)} onSelect={() => toggle(scene.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      {quickViewId !== null && (
        <QuickViewDialog type="scene" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      )}
    </>
  );
}

function GroupItemCard({ item, onNavigate, onRemove, dragHandleProps, isDragging, isOver, bookmarkInitiallySaved }: { item: GroupItem; onNavigate: (r: any) => void; onRemove?: () => void; dragHandleProps?: DragHandleProps; isDragging?: boolean; isOver?: boolean; bookmarkInitiallySaved?: boolean }) {
  const label = getGroupItemLabel(item);
  const target = getGroupItemRoute(item);
  const actionOverlay = (
    <div className="absolute left-1.5 right-1.5 top-1.5 z-30 flex items-center justify-between gap-2 pointer-events-none">
      {dragHandleProps ? (
        <span
          {...dragHandleProps}
          onClick={(event) => event.stopPropagation()}
          className="pointer-events-auto inline-flex h-7 w-7 cursor-grab items-center justify-center rounded bg-black/70 text-white transition-colors hover:bg-black/85 active:cursor-grabbing"
          title="Drag to reorder"
        >
          <GripVertical className="h-4 w-4" />
        </span>
      ) : <span />}
      {onRemove ? (
        <button
          type="button"
          onClick={(event) => { event.stopPropagation(); onRemove(); }}
          className="pointer-events-auto inline-flex h-7 w-7 items-center justify-center rounded bg-black/70 text-white transition-colors hover:bg-red-600"
          title="Remove from group"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      ) : null}
    </div>
  );

  const wrapperClass = `relative h-full transition ${isDragging ? "opacity-50" : ""} ${isOver ? "rounded-lg outline outline-2 outline-accent" : ""}`;

  if (item.sceneId) {
    const scene = groupItemToScene(item);
    return (
      <div className={wrapperClass}>
        {actionOverlay}
        {item.kind === "sceneRange" ? (
          <span className="absolute bottom-1.5 left-1.5 z-30 rounded bg-black/75 px-1.5 py-0.5 text-[11px] text-white">{formatDurationRange(item.startSec, item.endSec)}</span>
        ) : null}
        <SceneCard scene={scene} onClick={() => onNavigate({ page: "scene", id: scene.id, seekTo: item.startSec ?? 0 })} onNavigate={onNavigate} bookmarkInitiallySaved={bookmarkInitiallySaved} />
      </div>
    );
  }

  if (item.imageId || item.hostType === "image") {
    const image = groupItemToImage(item);
    return (
      <div className={wrapperClass}>
        {actionOverlay}
        <ImageTile image={image} onClick={() => onNavigate({ page: "image", id: image.id })} onNavigate={onNavigate} bookmarkInitiallySaved={bookmarkInitiallySaved} />
      </div>
    );
  }

  if (item.childGroupId || item.hostType === "group") {
    const childGroup = groupItemToGroup(item);
    return (
      <div className={wrapperClass}>
        {actionOverlay}
        <GroupTile group={childGroup} onClick={() => onNavigate({ page: "group", id: childGroup.id })} bookmarkInitiallySaved={bookmarkInitiallySaved} />
      </div>
    );
  }

  return (
    <article className={`${wrapperClass} flex h-full flex-col rounded-lg border border-border bg-card/80 p-4 transition-colors hover:border-accent/60`}>
      {actionOverlay}
      <div className="flex items-start gap-3">
        <GroupItemKindIcon item={item} />
        <div className="min-w-0 flex-1">
          <h3 className="line-clamp-2 text-sm font-semibold text-foreground">{label}</h3>
          <div className="mt-1 flex flex-wrap gap-2 text-xs text-secondary">
            <span>#{item.orderIndex + 1}</span>
            <span>{getGroupItemMeta(item)}</span>
          </div>
        </div>
      </div>
      <div className="mt-auto flex flex-wrap gap-2 pt-4">
        {target ? (
          <button
            type="button"
            onClick={() => onNavigate(target.route)}
            className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
          >
            <ExternalLink className="h-4 w-4" />
            {target.label}
          </button>
        ) : null}
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
      </div>
    </article>
  );
}

function GroupItemRow({ item, onNavigate, onRemove, dragHandleProps, isDragging, isOver }: { item: GroupItem; onNavigate: (r: any) => void; onRemove?: () => void; dragHandleProps?: DragHandleProps; isDragging?: boolean; isOver?: boolean }) {
  const label = getGroupItemLabel(item);
  const target = getGroupItemRoute(item);
  return (
    <div className={`rounded-xl border bg-card/80 p-4 transition-colors ${isDragging ? "border-accent opacity-40" : isOver ? "border-accent bg-accent/5" : "border-border"}`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex items-start gap-3">
          {dragHandleProps ? (
            <span {...dragHandleProps} className="mt-0.5 inline-flex shrink-0 cursor-grab items-center text-muted active:cursor-grabbing">
              <GripVertical className="h-4 w-4" />
            </span>
          ) : null}
          <GroupItemKindIcon item={item} />
          <div>
            <div className="text-sm font-medium text-foreground">{label}</div>
            <div className="mt-1 flex flex-wrap gap-2 text-xs text-secondary">
              <span>#{item.orderIndex + 1}</span>
              <span>{getGroupItemMeta(item)}</span>
              {item.sceneId && item.kind !== "scene" && item.kind !== "sceneRange" ? <span>Playable scene reference</span> : null}
              {item.kind !== "scene" && item.kind !== "sceneRange" ? <span>Skipped by player</span> : null}
              {item.sourceSpanKey ? <span>Span snapshot</span> : null}
            </div>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {target ? (
            <button
              type="button"
              onClick={() => onNavigate(target.route)}
              className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              <ExternalLink className="h-4 w-4" />
              {target.label}
            </button>
          ) : null}
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
          {onRemove ? (
            <button
              type="button"
              onClick={onRemove}
              className="rounded-lg border border-red-400/30 px-3 py-2 text-sm text-red-200 transition-colors hover:border-red-400"
            >
              Remove
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function groupItemToScene(item: GroupItem): Scene {
  return {
    id: item.sceneId ?? item.hostId ?? 0,
    title: getGroupItemLabel(item),
    organized: false,
    urls: [],
    tags: [],
    performers: [],
    files: [],
    groups: [],
    galleries: [],
    remoteIds: [],
    createdAt: item.createdAt,
    updatedAt: item.updatedAt,
  };
}

function groupItemToImage(item: GroupItem): ImageEntity {
  return {
    id: item.imageId ?? item.hostId ?? 0,
    title: getGroupItemLabel(item),
    organized: false,
    urls: [],
    tags: [],
    performers: [],
    galleryCount: 0,
    galleryIds: [],
    galleries: [],
    files: [],
    createdAt: item.createdAt,
    updatedAt: item.updatedAt,
  };
}

function groupItemToGroup(item: GroupItem): Group {
  return {
    id: item.childGroupId ?? item.hostId ?? 0,
    name: item.childGroupName ?? getGroupItemLabel(item),
    urls: [],
    tags: [],
    sceneCount: 0,
    subGroupCount: 0,
    containingGroupCount: 0,
    createdAt: item.createdAt,
    updatedAt: item.updatedAt,
  };
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

function DynamicGroupBanner({ group, resolvedCount, onRefresh, refreshing, onSnapshot, snapshotting, canWriteGroup }: {
  group: Group;
  resolvedCount: number;
  onRefresh?: () => void;
  refreshing?: boolean;
  onSnapshot?: () => void;
  snapshotting?: boolean;
  canWriteGroup: boolean;
}) {
  const sourceLabel = formatDynamicSourceLabel(group.querySourceKey);
  const resolvedAge = group.lastResolvedAt ? formatResolvedAge(group.lastResolvedAt) : resolvedCount > 0 ? "resolved just now" : "not resolved yet";
  return (
    <div className="rounded-xl border border-accent/30 bg-accent/5 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <div className="text-sm font-semibold text-foreground">Dynamic</div>
          <div className="mt-1 text-sm text-secondary">
            {resolvedCount || group.cachedItemCount || 0} items · {sourceLabel} · {resolvedAge}
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={onRefresh}
            disabled={refreshing}
            className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-wait disabled:opacity-60"
          >
            <RefreshCw className={`h-4 w-4 ${refreshing ? "animate-spin" : ""}`} />
            Refresh
          </button>
          {canWriteGroup ? (
            <button
              type="button"
              onClick={onSnapshot}
              disabled={snapshotting}
              className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-wait disabled:opacity-60"
            >
              Snapshot to static
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function formatDynamicSourceLabel(sourceKey?: string | null): string {
  if (sourceKey === "filter") return "filtered scenes";
  return sourceKey ? sourceKey.replaceAll("-", " ") : "dynamic";
}

function isBuiltInPersonalDynamicSource(sourceKey?: string | null): boolean {
  return sourceKey === "save-for-later" || sourceKey === "watch-history" || sourceKey === "continue-watching";
}

function GroupItemKindIcon({ item }: { item: GroupItem }) {
  const className = "mt-0.5 h-4 w-4 shrink-0 text-muted";
  if (item.kind === "image") return <ImageIcon className={className} />;
  if (item.kind === "group") return <Layers className={className} />;
  return <Film className={className} />;
}

function getGroupItemLabel(item: GroupItem): string {
  return item.title || item.sceneTitle || item.imageTitle || item.childGroupName || `${item.hostType || item.kind} #${item.hostId ?? item.sceneId ?? item.imageId ?? item.childGroupId ?? item.id}`;
}

function getGroupItemMeta(item: GroupItem): string {
  if (item.kind === "sceneRange") return formatDurationRange(item.startSec, item.endSec);
  if (item.kind === "scene") return "Full scene";
  if (item.kind === "image") return "Image";
  if (item.kind === "group") return "Group";
  return item.hostType ? item.hostType : item.kind;
}

function getGroupItemRoute(item: GroupItem): { label: string; route: any } | null {
  if (item.sceneId) return { label: "Open scene", route: { page: "scene", id: item.sceneId, seekTo: item.startSec ?? 0 } };
  if (item.imageId) return { label: "Open image", route: { page: "image", id: item.imageId } };
  if (item.childGroupId) return { label: "Open group", route: { page: "group", id: item.childGroupId } };
  if (item.hostType === "image" && item.hostId) return { label: "Open image", route: { page: "image", id: item.hostId } };
  if (item.hostType === "group" && item.hostId) return { label: "Open group", route: { page: "group", id: item.hostId } };
  if (item.hostType === "scene" && item.hostId) return { label: "Open scene", route: { page: "scene", id: item.hostId, seekTo: item.startSec ?? 0 } };
  return null;
}

function formatResolvedAge(value?: string | null): string {
  if (!value) return "not resolved yet";
  const resolvedAt = new Date(value).getTime();
  if (!Number.isFinite(resolvedAt)) return "resolved recently";
  const seconds = Math.max(0, Math.floor((Date.now() - resolvedAt) / 1000));
  if (seconds < 60) return `resolved ${seconds}s ago`;
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `resolved ${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  return `resolved ${hours}h ago`;
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
    onSuccess: (_result, subGroupId) => {
      setShowAddDialog(false);
      setSearchTerm("");
      queryClient.invalidateQueries({ queryKey: ["group-subgroups", groupId] });
      queryClient.invalidateQueries({ queryKey: ["group-containinggroups", subGroupId] });
      queryClient.invalidateQueries({ queryKey: ["group", groupId] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    },
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
          {addMut.isError ? (
            <p className="mt-3 text-xs text-red-300">{addMut.error instanceof Error ? addMut.error.message : "Could not add sub-group"}</p>
          ) : null}
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