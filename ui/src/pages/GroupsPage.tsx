import { useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { groups } from "../api/client";
import type { EntityEngagement, FindFilter, Group, GroupCreate, GroupFilterCriteria, PaginatedResponse } from "../api/types";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { SortableList, type DragHandleProps } from "../components/SortableList";
import { RatingField } from "../components/Rating";
import { CreateModalActions, EditModal, Field, TextInput, TextArea } from "../components/EditModal";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { useEntityEngagementBatch } from "../hooks/useEntityEngagementBatch";
import { formatDate } from "../components/shared";
import { Layers, Trash2, Loader2, Edit, GripVertical } from "lucide-react";
import { GroupTile } from "../components/EntityCards";
import { GROUP_CRITERIA } from "../components/FilterDialog";
import { BulkEditDialog, GROUP_BULK_FIELDS } from "../components/BulkEditDialog";
import { getDefaultFilter } from "../components/SavedFilterMenu";
import { useListUrlState } from "../hooks/useListUrlState";
import { useInfiniteListData } from "../hooks/useInfiniteListData";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canWriteEntity } from "../auth/visibility";
import { CustomFieldsEditor } from "../components/shared";
import { DynamicGroupFilterEditor, FILTER_DYNAMIC_SOURCE_KEY, defaultDynamicGroupFilterQueryJson } from "../components/DynamicGroupFilterEditor";

const SORT_OPTIONS = [
  { value: "sort_order", label: "Manual Order" },
  { value: "name", label: "Name" },
  { value: "date", label: "Date" },
  { value: "rating", label: "Rating" },
  { value: "random", label: "Random" },
  { value: "created_at", label: "Created At" },
];

function getGroupItemCount(group: Group) {
  return group.itemCount ?? (group.kind === "dynamic" ? group.cachedItemCount ?? group.sceneCount : group.sceneCount);
}

interface Props {
  onNavigate: (r: any) => void;
}

export function GroupsPage({ onNavigate }: Props) {
  const defaultState = useMemo(() => {
    const savedFilter = getDefaultFilter("groups");
    return {
      filter: savedFilter?.findFilter ?? { page: 1, perPage: 40, sort: "sort_order", direction: "asc" },
      objectFilter: savedFilter?.objectFilter ?? {},
      displayMode: "grid" as DisplayMode,
    };
  }, []);
  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode } = useListUrlState({
    resetKey: "groups",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list"] as const,
    allowInfinitePageSize: true,
  });
  const [showCreate, setShowCreate] = useState(false);
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [selectAllMatchingPending, setSelectAllMatchingPending] = useState(false);
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const canWriteGroup = canWriteEntity("group", hasPermission);
  const canDeleteGroup = canDeleteEntity("group", hasPermission);

  const hasObjectFilter = Object.keys(objectFilter).length > 0;
  const listData = useInfiniteListData<Group>({
    queryKey: ["groups", filter, objectFilter],
    filter,
    chunkSize: defaultState.filter.perPage ?? 40,
    queryPage: (nextFilter) =>
      hasObjectFilter
        ? groups.findFiltered({ findFilter: nextFilter, objectFilter: objectFilter as GroupFilterCriteria })
        : groups.find(nextFilter),
  });

  const items = listData.items;
  const totalCount = listData.totalCount;
  const isLoading = listData.isLoading;
  const manualOrderingEnabled = !listData.infinitePageSize && displayMode === "grid" && !hasObjectFilter && !filter.q && (filter.sort ?? "sort_order") === "sort_order" && (filter.direction ?? "asc") !== "desc";
  const { engagementById } = useEntityEngagementBatch("group", items.map((item) => item.id));
  const selectionResetKey = useMemo(() => JSON.stringify({ filter: listData.infiniteFilterKey, objectFilter }), [listData.infiniteFilterKey, objectFilter]);
  const { selectedIds, toggle, selectAll, selectIds, selectNone, invertSelection } = useMultiSelect(items, { preserveOnAppend: listData.infinitePageSize, resetKey: selectionResetKey });
  const selecting = selectedIds.size > 0;
  const handleSelectAllMatching = async () => {
    setSelectAllMatchingPending(true);
    try {
      selectIds(await listData.fetchAllIds());
    } finally {
      setSelectAllMatchingPending(false);
    }
  };

  const bulkDeleteMut = useMutation({
    mutationFn: () => groups.bulkDelete([...selectedIds]),
    onSuccess: () => { selectNone(); queryClient.invalidateQueries({ queryKey: ["groups"] }); },
  });

  const bulkEditMut = useMutation({
    mutationFn: (values: Record<string, unknown>) =>
      groups.bulkUpdate({ ids: [...selectedIds], ...values } as any),
    onSuccess: () => {
      setShowBulkEdit(false);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["groups"] });
    },
  });

  const reorderMut = useMutation({
    mutationFn: (nextItems: Group[]) => groups.reorder({ ids: nextItems.map((item) => item.id), startIndex: ((filter.page ?? 1) - 1) * (filter.perPage ?? 40) }),
    onMutate: async (nextItems) => {
      await queryClient.cancelQueries({ queryKey: ["groups", filter, objectFilter] });
      const previousData = queryClient.getQueryData<PaginatedResponse<Group>>(["groups", filter, objectFilter]);
      if (previousData) {
        queryClient.setQueryData<PaginatedResponse<Group>>(["groups", filter, objectFilter], { ...previousData, items: nextItems });
      }
      return { previousData };
    },
    onError: (_error, _nextItems, context) => {
      if (context?.previousData) {
        queryClient.setQueryData(["groups", filter, objectFilter], context.previousData);
      }
    },
    onSettled: () => queryClient.invalidateQueries({ queryKey: ["groups"] }),
  });

  return (
    <>
      <GroupCreateModal open={showCreate} onClose={() => setShowCreate(false)} onCreated={(id) => onNavigate({ page: "group", id })} />
      <ListPage
        title="Groups"
        pageKey="groups"
        filterMode="groups"
        filter={filter}
        onFilterChange={setFilter}
        totalCount={totalCount}
        isLoading={isLoading}
        sortOptions={SORT_OPTIONS}
        displayMode={displayMode}
        onDisplayModeChange={setDisplayMode}
        availableDisplayModes={["grid", "list"]}
        allowInfinitePageSize
        showPagingControls={!listData.infinitePageSize}
        selectAllLabel={listData.infinitePageSize ? "Select loaded" : undefined}
        onSelectAllMatching={listData.infinitePageSize ? handleSelectAllMatching : undefined}
        selectAllMatchingLabel={`Select all ${totalCount} matching`}
        selectAllMatchingPending={selectAllMatchingPending}
        infiniteScroll={listData.infinitePageSize ? {
          hasNextPage: listData.infiniteQuery.hasNextPage,
          hasPreviousPage: listData.infiniteQuery.hasPreviousPage,
          isFetchingNextPage: listData.infiniteQuery.isFetchingNextPage,
          isFetchingPreviousPage: listData.infiniteQuery.isFetchingPreviousPage,
          onLoadMore: listData.loadMore,
          onLoadPrevious: listData.loadPrevious,
          loadedCount: listData.infiniteQuery.loadedThroughCount,
          previousLoadedCount: listData.infiniteQuery.firstLoadedIndex,
          totalCount,
        } : undefined}
        criteriaDefinitions={GROUP_CRITERIA}
        objectFilter={objectFilter}
        onObjectFilterChange={setObjectFilter}
        onNew={canWriteGroup ? () => setShowCreate(true) : undefined}
        selectedIds={selectedIds}
        onSelectAll={selectAll}
        onSelectNone={selectNone}
        onInvertSelection={invertSelection}
        selectionActions={
          <>
            {canWriteGroup && (
              <button
                onClick={() => setShowBulkEdit(true)}
                className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
              >
                <Edit className="w-3 h-3" />
                Edit
              </button>
            )}
            {canDeleteGroup && (
              <button
                onClick={() => { if (confirm(`Delete ${selectedIds.size} group(s)?`)) bulkDeleteMut.mutate(); }}
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
        manualOrderingEnabled ? (
          <SortableList
            items={items}
            getKey={(group) => group.id}
            onReorder={(nextItems) => reorderMut.mutate(nextItems)}
            disabled={!canWriteGroup || selecting || reorderMut.isPending}
            className="grid gap-3"
            style={{ gridTemplateColumns: "repeat(auto-fill, minmax(var(--card-min-width, 160px), 1fr))" }}
            renderItem={(g, { dragHandleProps, isDragging, isOver }) => (
              <GroupCard
                group={g}
                engagement={engagementById.get(g.id)}
                onClick={() => onNavigate({ page: "group", id: g.id })}
                onNavigate={onNavigate}
                selected={selectedIds.has(g.id)}
                onSelect={() => toggle(g.id)}
                selecting={selecting}
                dragHandleProps={canWriteGroup ? dragHandleProps : undefined}
                isDragging={isDragging}
                isOver={isOver}
              />
            )}
          />
        ) : (
          <EntityCardGrid minCardWidth="var(--card-min-width, 160px)">
            {items.map((g) => (
              <GroupCard
                key={g.id}
                group={g}
                engagement={engagementById.get(g.id)}
                onClick={() => selecting ? toggle(g.id) : onNavigate({ page: "group", id: g.id })}
                onNavigate={onNavigate}
                selected={selectedIds.has(g.id)}
                onSelect={() => toggle(g.id)}
                selecting={selecting}
              />
            ))}
          </EntityCardGrid>
        )
      ) : (
        <GroupListTable groups={items} engagementById={engagementById} onNavigate={onNavigate} selectedIds={selectedIds} onToggle={toggle} selecting={selecting} />
      )}
      {items.length === 0 && (
        <div className="text-center text-secondary py-16">
          <Layers className="w-12 h-12 mx-auto mb-3 opacity-50" />
          <p>No groups found</p>
        </div>
      )}
      </ListPage>
      <BulkEditDialog
        open={showBulkEdit}
        onClose={() => setShowBulkEdit(false)}
        title="Edit Groups"
        selectedCount={selectedIds.size}
        fields={GROUP_BULK_FIELDS}
        onApply={(values) => bulkEditMut.mutate(values)}
        isPending={bulkEditMut.isPending}
      />
    </>
  );
}

function GroupCard({ group, engagement, onClick, selected, onSelect, selecting, dragHandleProps, isDragging, isOver }: { group: Group; engagement?: EntityEngagement; onClick: () => void; onNavigate?: (r: any) => void; selected?: boolean; onSelect?: () => void; selecting?: boolean; dragHandleProps?: DragHandleProps; isDragging?: boolean; isOver?: boolean }) {
  return (
    <div className={`relative h-full transition-opacity ${isDragging ? "opacity-50" : ""} ${isOver ? "rounded-lg outline outline-2 outline-accent" : ""}`}>
      <GroupTile group={group} engagement={engagement} onClick={onClick} selected={selected} onSelect={onSelect} selecting={selecting} />
      {dragHandleProps ? (
        <span
          {...dragHandleProps}
          onClick={(event) => event.stopPropagation()}
          className="absolute bottom-1.5 right-1.5 z-20 inline-flex h-7 w-7 cursor-grab items-center justify-center rounded bg-black/70 text-white opacity-0 transition-opacity hover:bg-black/85 active:cursor-grabbing group-hover:opacity-100 focus:opacity-100"
          title="Drag to reorder"
        >
          <GripVertical className="h-4 w-4" />
        </span>
      ) : null}
    </div>
  );
}

function GroupListTable({ groups: items, engagementById, onNavigate, selectedIds, onToggle, selecting }: { groups: Group[]; engagementById: ReadonlyMap<number, EntityEngagement>; onNavigate: (r: any) => void; selectedIds?: Set<number>; onToggle?: (id: number) => void; selecting?: boolean }) {
  return (
    <table className="w-full text-sm">
      <thead>
        <tr className="border-b border-border text-left text-muted text-xs">
          {selectedIds && <th className="w-8 py-2 px-3"></th>}
          <th className="py-2 px-3">Name</th>
          <th className="py-2 px-3">Studio</th>
          <th className="py-2 px-3">Director</th>
          <th className="py-2 px-3">Date</th>
          <th className="py-2 px-3 text-right">Items</th>
          <th className="py-2 px-3 text-right">Rating</th>
        </tr>
      </thead>
      <tbody>
        {items.map((g) => (
          <tr
            key={g.id}
            onClick={() => selecting ? onToggle?.(g.id) : onNavigate({ page: "group", id: g.id })}
            className={`border-b border-border hover:bg-card cursor-pointer ${selectedIds?.has(g.id) ? "bg-accent/10" : ""}`}
          >
            {selectedIds && <td className="py-2 px-3"><input type="checkbox" checked={selectedIds.has(g.id)} onChange={() => onToggle?.(g.id)} onClick={(e) => e.stopPropagation()} className="w-3.5 h-3.5 rounded border-border cursor-pointer accent-accent" /></td>}
            <td className="py-2 px-3 text-foreground">{g.name}</td>
            <td className="py-2 px-3 text-secondary">{g.studioName ?? ""}</td>
            <td className="py-2 px-3 text-secondary">{g.director ?? ""}</td>
            <td className="py-2 px-3 text-secondary">{g.date ? formatDate(g.date) : ""}</td>
            <td className="py-2 px-3 text-secondary text-right">{getGroupItemCount(g)}</td>
            <td className="py-2 px-3 text-secondary text-right">{engagementById.get(g.id)?.rating ?? ""}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/* â”€â”€ Group Create Modal â”€â”€ */
function GroupCreateModal({ open, onClose, onCreated }: { open: boolean; onClose: () => void; onCreated: (id: number) => void }) {
  const qc = useQueryClient();
  const { data: dynamicSources = [] } = useQuery({
    queryKey: ["group-dynamic-sources"],
    queryFn: () => groups.dynamicSources(),
    enabled: open,
  });
  const dynamicSourceOptions = useMemo(() => {
    const filterSource = dynamicSources.find((source) => source.key === FILTER_DYNAMIC_SOURCE_KEY);
    return filterSource ? [filterSource] : dynamicSources;
  }, [dynamicSources]);
  const defaultDynamicSourceKey = dynamicSourceOptions[0]?.key ?? FILTER_DYNAMIC_SOURCE_KEY;
  const [form, setForm] = useState({
    name: "",
    date: "",
    director: "",
    synopsis: "",
    rating: undefined as number | undefined,
    kind: "static" as "static" | "dynamic",
    querySourceKey: FILTER_DYNAMIC_SOURCE_KEY,
    queryJson: defaultDynamicGroupFilterQueryJson(),
    cacheTtlSec: 60,
  });
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({});
  const [createAnother, setCreateAnother] = useState(false);

  const resetForm = () => {
    setForm({ name: "", date: "", director: "", synopsis: "", rating: undefined, kind: "static", querySourceKey: defaultDynamicSourceKey, queryJson: defaultDynamicGroupFilterQueryJson(), cacheTtlSec: 60 });
    setCustomFields({});
  };

  const mutation = useMutation({
    mutationFn: (data: GroupCreate) => groups.create(data),
    onSuccess: (created) => {
      qc.invalidateQueries({ queryKey: ["groups"] });
      resetForm();
      if (createAnother) return;
      onClose();
      if (created?.id) onCreated(created.id);
    },
  });

  const save = () => {
    const name = form.name.trim();
    if (!name) return;
    mutation.mutate({
      name,
      date: form.date || undefined,
      director: form.director || undefined,
      synopsis: form.synopsis || undefined,
      rating: form.rating,
      customFields: Object.keys(customFields).length > 0 ? customFields : undefined,
      kind: form.kind,
      querySourceKey: form.kind === "dynamic" ? form.querySourceKey : undefined,
      queryJson: form.kind === "dynamic" && form.querySourceKey === FILTER_DYNAMIC_SOURCE_KEY ? form.queryJson : undefined,
      cacheTtlSec: form.kind === "dynamic" ? form.cacheTtlSec : undefined,
    });
  };

  return (
    <EditModal title="Create Group" open={open} onClose={onClose}>
      <Field label="Name">
        <TextInput value={form.name} onChange={(v) => setForm({ ...form, name: v })} />
      </Field>
      <Field label="Kind">
        <div className="inline-flex rounded-lg border border-border bg-card p-1">
          {(["static", "dynamic"] as const).map((kind) => (
            <button
              key={kind}
              type="button"
              onClick={() => setForm({ ...form, kind, querySourceKey: kind === "dynamic" ? (form.querySourceKey || defaultDynamicSourceKey) : form.querySourceKey, queryJson: form.queryJson || defaultDynamicGroupFilterQueryJson() })}
              className={`rounded-md px-3 py-1.5 text-sm capitalize transition-colors ${form.kind === kind ? "bg-accent text-white" : "text-secondary hover:text-foreground"}`}
            >
              {kind}
            </button>
          ))}
        </div>
      </Field>
      {form.kind === "dynamic" ? (
        <div className="grid grid-cols-2 gap-4">
          <Field label="Source">
            <select
              value={form.querySourceKey}
              onChange={(event) => setForm({ ...form, querySourceKey: event.target.value, queryJson: event.target.value === FILTER_DYNAMIC_SOURCE_KEY ? (form.queryJson || defaultDynamicGroupFilterQueryJson()) : form.queryJson })}
              className="w-full rounded border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
            >
              {dynamicSourceOptions.map((source) => (
                <option key={source.key} value={source.key}>{source.displayName}</option>
              ))}
            </select>
          </Field>
          <Field label="Cache TTL (seconds)">
            <input
              type="number"
              min={0}
              value={form.cacheTtlSec}
              onChange={(event) => setForm({ ...form, cacheTtlSec: Math.max(0, Number(event.target.value) || 0) })}
              className="w-full rounded border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
            />
          </Field>
        </div>
      ) : null}
      {form.kind === "dynamic" && form.querySourceKey === FILTER_DYNAMIC_SOURCE_KEY ? (
        <DynamicGroupFilterEditor queryJson={form.queryJson} onChange={(queryJson) => setForm({ ...form, queryJson })} />
      ) : null}
      <Field label="Date">
        <TextInput value={form.date} onChange={(v) => setForm({ ...form, date: v })} placeholder="YYYY-MM-DD" />
      </Field>
      <Field label="Director">
        <TextInput value={form.director} onChange={(v) => setForm({ ...form, director: v })} />
      </Field>
      <Field label="Synopsis">
        <TextArea value={form.synopsis} onChange={(v) => setForm({ ...form, synopsis: v })} rows={3} />
      </Field>
      <RatingField value={form.rating} onChange={(value) => setForm({ ...form, rating: value })} />
      <Field label="Custom Fields">
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="group" />
      </Field>
      <CreateModalActions loading={mutation.isPending} onSave={save} createAnother={createAnother} onCreateAnotherChange={setCreateAnother} />
    </EditModal>
  );
}
