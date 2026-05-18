import { useCallback, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { FaceBatchOperationResult, FindFilter, FaceTopSuggestion } from "../api/types";
import { aiFaces, faces } from "../api/client";
import type { Face } from "../api/types";
import { Fingerprint, Link2, Merge, Trash2 } from "lucide-react";
import { ListPage, type DisplayMode } from "../components/ListPage";
import type { FilterDialogCustomSection } from "../components/FilterDialog";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../components/RouteCardLinkOverlay";
import { createNestedRouteLinkProps } from "../components/cardNavigation";
import { FaceCompareDialog } from "../components/FaceCompareDialog";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { FaceTile } from "../components/EntityCards";
import { formatDate } from "../components/shared";
import { useListUrlState } from "../hooks/useListUrlState";
import { useInfiniteListData } from "../hooks/useInfiniteListData";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { VirtualizedEntityGrid } from "../components/VirtualizedEntityLayouts";

interface Props {
  onNavigate: (r: any) => void;
}

type TriState = "all" | "yes" | "no";
type MergeFilter = "all" | "merged";
type FaceSort = "suggestion_confidence" | "updated_desc" | "created_desc" | "appearance_desc" | "scene_count_desc" | "image_count_desc";

const defaultFaceSort: FaceSort = "suggestion_confidence";
const FACE_SORT_OPTIONS = [
  { value: "suggestion_confidence", label: "Suggested match confidence" },
  { value: "updated_desc", label: "Recently updated" },
  { value: "created_desc", label: "Recently created" },
  { value: "appearance_desc", label: "Most appearances" },
  { value: "scene_count_desc", label: "Most scenes" },
  { value: "image_count_desc", label: "Most images" },
];

function readTriState(value: unknown): TriState {
  return value === "yes" || value === "no" ? value : "all";
}

function readFaceSort(value: unknown): FaceSort {
  return value === "updated_desc"
    || value === "created_desc"
    || value === "appearance_desc"
    || value === "scene_count_desc"
    || value === "image_count_desc"
    || value === "suggestion_confidence"
    ? value
    : defaultFaceSort;
}

function readMergeFilter(value: unknown): MergeFilter {
  return value === "merged" ? value : "all";
}

function sanitizeFaceFilters(filter: Record<string, unknown>) {
  const next: Record<string, unknown> = {};
  const linked = readTriState(filter.linked);
  const ignored = readTriState(filter.ignored);
  const merged = readMergeFilter(filter.merged);

  if (linked !== "all") {
    next.linked = linked;
  }

  if (ignored !== "all") {
    next.ignored = ignored;
  }

  if (merged !== "all") {
    next.merged = merged;
  }

  return next;
}

function formatTriState(value: unknown, yesLabel: string, noLabel: string) {
  const resolved = readTriState(value);
  return resolved === "yes" ? yesLabel : resolved === "no" ? noLabel : "All";
}

function formatMergeFilter(value: unknown) {
  const resolved = readMergeFilter(value);
  return resolved === "merged" ? "Merged only" : "Primary and merged";
}

function renderSelectFilter(
  value: unknown,
  onChange: (value: unknown) => void,
  options: { value: string; label: string }[],
  label: string,
) {
  return (
    <label className="grid gap-1 text-xs text-secondary">
      <span>{label}</span>
      <select
        value={String(value ?? options[0]?.value ?? "")}
        onChange={(event) => onChange(event.target.value)}
        className="min-h-[32px] rounded border border-border bg-input px-2 py-1 text-xs text-foreground outline-none focus:border-accent"
      >
        {options.map((option) => (
          <option key={option.value} value={option.value}>{option.label}</option>
        ))}
      </select>
    </label>
  );
}

export function FacesPage({ onNavigate }: Props) {
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const canReadPerformers = canReadEntity("performer", hasPermission);
  const canWriteFaces = canWriteEntity("face", hasPermission);
  const canDeleteFaces = canDeleteEntity("face", hasPermission);
  const defaultState = useMemo(() => ({
    filter: { page: 1, perPage: 36, sort: defaultFaceSort } as FindFilter,
    objectFilter: { linked: "no" },
    displayMode: "grid" as DisplayMode,
  }), []);
  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode } = useListUrlState({
    resetKey: "faces",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list"] as const,
    allowInfinitePageSize: true,
  });
  const linked = readTriState(objectFilter.linked);
  const ignored = readTriState(objectFilter.ignored);
  const merged = readMergeFilter(objectFilter.merged);
  const sort = readFaceSort(filter.sort);
  const faceFilterSections = useMemo<FilterDialogCustomSection[]>(() => [
    {
      id: "linked",
      label: "Linked state",
      filterKey: "linked",
      defaultValue: "all",
      isActive: (value) => readTriState(value) !== "all",
      summarize: (value) => formatTriState(value, "Linked", "Unlinked"),
      renderEditor: (value, onChange) => renderSelectFilter(value, onChange, [
        { value: "all", label: "Linked and unlinked" },
        { value: "no", label: "Unlinked" },
        { value: "yes", label: "Linked" },
      ], "Linked state"),
    },
    {
      id: "ignored",
      label: "Ignored state",
      filterKey: "ignored",
      defaultValue: "all",
      isActive: (value) => readTriState(value) !== "all",
      summarize: (value) => formatTriState(value, "Ignored", "Not ignored"),
      renderEditor: (value, onChange) => renderSelectFilter(value, onChange, [
        { value: "all", label: "Ignored and visible" },
        { value: "no", label: "Not ignored" },
        { value: "yes", label: "Ignored" },
      ], "Ignored state"),
    },
    {
      id: "merged",
      label: "Merge state",
      filterKey: "merged",
      defaultValue: "all",
      isActive: (value) => readMergeFilter(value) !== "all",
      summarize: formatMergeFilter,
      renderEditor: (value, onChange) => renderSelectFilter(value, onChange, [
        { value: "all", label: "Primary and merged" },
        { value: "merged", label: "Merged only" },
      ], "Merge state"),
    },
  ], []);
  const [comparison, setComparison] = useState<{ face: Face; suggestion: FaceTopSuggestion } | null>(null);
  const [batchMinConfidence, setBatchMinConfidence] = useState(60);
  const [batchResult, setBatchResult] = useState<FaceBatchOperationResult | null>(null);
  const [confirmBatchDelete, setConfirmBatchDelete] = useState(false);
  const [selectAllMatchingPending, setSelectAllMatchingPending] = useState(false);

  const query = useMemo(() => ({
    q: filter.q?.trim() || undefined,
    linked: linked === "all" ? undefined : linked === "yes",
    ignored: ignored === "all" ? undefined : ignored === "yes",
    merged: merged === "all" ? undefined : merged === "merged",
    sort,
    page: filter.page ?? 1,
    perPage: filter.perPage ?? 36,
  }), [filter.page, filter.perPage, filter.q, ignored, linked, merged, sort]);

  const listData = useInfiniteListData<Face>({
    queryKey: ["faces", query],
    filter,
    chunkSize: defaultState.filter.perPage ?? 36,
    queryPage: (nextFilter) => faces.list({
      ...query,
      page: nextFilter.page ?? 1,
      perPage: nextFilter.perPage ?? defaultState.filter.perPage ?? 36,
    }),
  });

  const items = listData.items;
  const totalCount = listData.totalCount;
  const isLoading = listData.isLoading;
  const selectionResetKey = useMemo(() => JSON.stringify({ filter: listData.infiniteFilterKey, objectFilter }), [listData.infiniteFilterKey, objectFilter]);
  const { selectedIds, toggle, selectAll, selectIds, selectNone, invertSelection } = useMultiSelect(items, { preserveOnAppend: listData.infinitePageSize, resetKey: selectionResetKey });
  const selecting = selectedIds.size > 0;
  const selectedFaceIds = useMemo(() => Array.from(selectedIds).map((value) => Number(value)), [selectedIds]);
  const handleSelectAllMatching = async () => {
    setSelectAllMatchingPending(true);
    try {
      selectIds(await listData.fetchAllIds());
    } finally {
      setSelectAllMatchingPending(false);
    }
  };

  const invalidateFace = useCallback((faceId: number) => {
    queryClient.invalidateQueries({ queryKey: ["faces"] });
    queryClient.invalidateQueries({ queryKey: ["face", faceId] });
    queryClient.invalidateQueries({ queryKey: ["face", faceId, "suggestions"] });
  }, [queryClient]);

  const linkMutation = useMutation({
    mutationFn: (data: { faceId: number; performerId: number; setPerformerImage?: boolean }) =>
      faces.link(data.faceId, { performerId: data.performerId, setPerformerImage: data.setPerformerImage }),
    onSuccess: (_, variables) => {
      invalidateFace(variables.faceId);
    },
  });

  const batchLinkTopSuggestionMutation = useMutation({
    mutationFn: () => faces.batchLinkTopSuggestion({ faceIds: selectedFaceIds, minConfidence: batchMinConfidence }),
    onSuccess: (result) => {
      setBatchResult(result);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["faces"] });
    },
  });

  const batchDeleteMutation = useMutation({
    mutationFn: () => faces.batchDelete({ faceIds: selectedFaceIds }),
    onSuccess: (result) => {
      setBatchResult(result);
      selectNone();
      queryClient.invalidateQueries({ queryKey: ["faces"] });
    },
  });

  const rejectSuggestionMutation = useMutation({
    mutationFn: (data: { faceId: number; performerId: number }) =>
      faces.recordSuggestionDecision(data.faceId, { performerId: data.performerId, decision: "reject" }),
    onSuccess: (_, variables) => {
      invalidateFace(variables.faceId);
    },
  });

  const referenceSuggestionMutation = useMutation({
    mutationFn: (data: { faceId: number; referenceSuggestionId: number; action: "import" | "reject" }) =>
      data.action === "import"
        ? aiFaces.importReferencePerformer(data.faceId, { referenceSuggestionId: data.referenceSuggestionId })
        : aiFaces.rejectReferenceSuggestion(data.faceId, { referenceSuggestionId: data.referenceSuggestionId }),
    onSuccess: (_, variables) => {
      invalidateFace(variables.faceId);
      queryClient.invalidateQueries({ queryKey: ["ai-faces", "reference", "status"] });
    },
  });

  const handleFilterChange = useCallback((next: FindFilter) => {
    setFilter({ ...next, sort: readFaceSort(next.sort) });
  }, [setFilter]);

  const handleObjectFilterChange = useCallback((next: Record<string, unknown>) => {
    setObjectFilter(sanitizeFaceFilters(next));
  }, [setObjectFilter]);

  const compareBusy = linkMutation.isPending || rejectSuggestionMutation.isPending || referenceSuggestionMutation.isPending;

  const handleConfirmSuggestion = useCallback((face: Face, suggestion: FaceTopSuggestion, options?: { setPerformerImage?: boolean }) => {
    const localPerformerId = suggestion.localPerformerId ?? (suggestion.performerId > 0 ? suggestion.performerId : undefined);
    if (localPerformerId != null) {
      linkMutation.mutate({ faceId: face.id, performerId: localPerformerId, setPerformerImage: options?.setPerformerImage });
      setComparison(null);
      return;
    }

    referenceSuggestionMutation.mutate({ faceId: face.id, referenceSuggestionId: suggestion.performerId, action: "import" });
    setComparison(null);
  }, [linkMutation, referenceSuggestionMutation]);

  const handleRejectSuggestion = useCallback((face: Face, suggestion: FaceTopSuggestion) => {
    const localPerformerId = suggestion.localPerformerId ?? (suggestion.performerId > 0 ? suggestion.performerId : undefined);
    if (localPerformerId != null) {
      rejectSuggestionMutation.mutate({ faceId: face.id, performerId: localPerformerId });
      setComparison(null);
      return;
    }

    referenceSuggestionMutation.mutate({ faceId: face.id, referenceSuggestionId: suggestion.performerId, action: "reject" });
    setComparison(null);
  }, [referenceSuggestionMutation, rejectSuggestionMutation]);

  return (
    <>
      <ListPage
        title="Faces"
        pageKey="faces"
        filter={filter}
        onFilterChange={handleFilterChange}
        totalCount={totalCount}
        isLoading={isLoading}
        sortOptions={FACE_SORT_OPTIONS}
        displayMode={displayMode}
        onDisplayModeChange={setDisplayMode}
        showClearAllObjectFilters={false}
        availableDisplayModes={["grid", "list"]}
        allowInfinitePageSize
        showPagingControls={!listData.infinitePageSize}
        selectAllPending={listData.infinitePageSize ? selectAllMatchingPending : false}
        onSelectAllMatching={listData.infinitePageSize ? selectAll : undefined}
        selectAllMatchingLabel="Select shown"
        infiniteScroll={listData.infiniteScroll}
        criteriaDefinitions={[]}
        objectFilter={objectFilter}
        onObjectFilterChange={handleObjectFilterChange}
        customFilterSections={faceFilterSections}
        selectedIds={selectedIds}
        onSelectAll={listData.infinitePageSize ? handleSelectAllMatching : selectAll}
        onSelectNone={selectNone}
        onInvertSelection={invertSelection}
        selectionActions={(
          <div className="flex flex-wrap items-center gap-2">
            {canWriteFaces ? (
              <label className="flex items-center gap-1.5 text-xs text-secondary">
                Min
                <input
                  type="number"
                  min={0}
                  max={100}
                  value={batchMinConfidence}
                  onChange={(event) => setBatchMinConfidence(Math.max(0, Math.min(100, Number(event.target.value) || 0)))}
                  className="h-7 w-16 rounded border border-border bg-input px-2 text-xs text-foreground outline-none focus:border-accent"
                />
              </label>
            ) : null}
            {canWriteFaces ? (
              <button
                type="button"
                onClick={() => batchLinkTopSuggestionMutation.mutate()}
                disabled={selectedFaceIds.length === 0 || batchLinkTopSuggestionMutation.isPending || batchDeleteMutation.isPending}
                className="inline-flex items-center gap-1.5 rounded-lg border border-border px-3 py-1 text-xs text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
              >
                <Link2 className="h-3.5 w-3.5" />
                {batchLinkTopSuggestionMutation.isPending ? "Linking..." : "Link suggested"}
              </button>
            ) : null}
            {canDeleteFaces ? (
              <button
                type="button"
                onClick={() => setConfirmBatchDelete(true)}
                disabled={selectedFaceIds.length === 0 || batchLinkTopSuggestionMutation.isPending || batchDeleteMutation.isPending}
                className="inline-flex items-center gap-1.5 rounded-lg border border-red-500/40 px-3 py-1 text-xs text-red-200 transition-colors hover:border-red-400 disabled:cursor-not-allowed disabled:opacity-50"
              >
                <Trash2 className="h-3.5 w-3.5" />
                {batchDeleteMutation.isPending ? "Deleting..." : "Delete"}
              </button>
            ) : null}
          </div>
        )}
        metadataByline={<span className="hidden text-xs text-muted lg:inline">Review face clusters with suggestions, links, and sample counts.</span>}
      >
        {displayMode === "grid" ? (
          <VirtualizedEntityGrid
            items={items}
            getItemKey={(face) => face.id}
            minCardWidth="var(--card-min-width, 200px)"
            estimateRowHeight={340}
            gap={16}
            gapClassName="gap-4"
            infinitePageSize={listData.infinitePageSize}
            hasNextPage={listData.infiniteQuery.hasNextPage}
            isFetchingNextPage={listData.infiniteQuery.isFetchingNextPage}
            loadMore={listData.loadMore}
            renderItem={(face) => (
              <FaceTile
                face={face}
                onClick={() => onNavigate({ page: "face", id: face.id })}
                selected={selectedIds.has(face.id)}
                onSelect={() => toggle(face.id)}
                selecting={selecting}
              >
                {face.performerId ? (
                  <LinkedPerformerSummary face={face} onNavigate={onNavigate} canReadPerformers={canReadPerformers} />
                ) : (
                  <TopSuggestionFooter
                    face={face}
                    suggestion={face.topSuggestion}
                    onNavigate={onNavigate}
                    canReadPerformers={canReadPerformers}
                    canWriteFaces={canWriteFaces}
                    onOpenCompare={(suggestion) => setComparison({ face, suggestion })}
                  />
                )}
              </FaceTile>
            )}
          />
        ) : (
          <FaceListTable
            faces={items}
            onNavigate={onNavigate}
            canReadPerformers={canReadPerformers}
            canWriteFaces={canWriteFaces}
            onOpenCompare={(face, suggestion) => setComparison({ face, suggestion })}
            selectedIds={selectedIds}
            onToggle={toggle}
            selecting={selecting}
          />
        )}
        {items.length === 0 && !isLoading ? (
          <div className="rounded-xl border border-dashed border-border bg-card/40 px-4 py-12 text-center text-sm text-secondary">
            <Fingerprint className="mx-auto mb-3 h-8 w-8 text-muted" />
            No faces matched the current filters.
          </div>
        ) : null}
      </ListPage>

      {batchResult ? (
        <div className="mx-1 mt-3 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-card/80 px-4 py-3 text-sm text-secondary">
          <span>{describeBatchResult(batchResult)}</span>
          <button type="button" onClick={() => setBatchResult(null)} className="text-xs text-accent hover:underline">Dismiss</button>
        </div>
      ) : null}

      <ConfirmDialog
        open={confirmBatchDelete}
        title="Delete selected faces?"
        message={`Delete ${selectedFaceIds.length} selected face${selectedFaceIds.length === 1 ? "" : "s"} and their AI artifacts. This cannot be undone.`}
        confirmLabel="Delete faces"
        onCancel={() => setConfirmBatchDelete(false)}
        onConfirm={() => {
          setConfirmBatchDelete(false);
          batchDeleteMutation.mutate();
        }}
      />

      <FaceCompareDialog
        open={comparison != null}
        face={comparison?.face ?? null}
        suggestion={comparison?.suggestion ?? null}
        disabled={compareBusy}
        canReadPerformers={canReadPerformers}
        onClose={() => setComparison(null)}
        onConfirm={(suggestion, options) => {
          if (comparison) {
            handleConfirmSuggestion(comparison.face, suggestion as FaceTopSuggestion, options);
          }
        }}
        onReject={(suggestion) => {
          if (comparison) {
            handleRejectSuggestion(comparison.face, suggestion as FaceTopSuggestion);
          }
        }}
        onNavigate={onNavigate}
      />
    </>
  );
}

function describeBatchResult(result: FaceBatchOperationResult) {
  const parts = [
    `${result.succeeded.length} succeeded`,
    result.skipped.length > 0 ? `${result.skipped.length} skipped` : null,
    result.failed.length > 0 ? `${result.failed.length} failed` : null,
  ].filter(Boolean);

  const firstIssue = result.failed[0]?.error ?? result.skipped[0]?.reason;
  return firstIssue ? `${parts.join(", ")}. ${firstIssue}` : `${parts.join(", ")}.`;
}

function FaceListTable({
  faces,
  onNavigate,
  canReadPerformers,
  canWriteFaces,
  onOpenCompare,
  selectedIds,
  onToggle,
  selecting,
}: {
  faces: Face[];
  onNavigate: (r: any) => void;
  canReadPerformers: boolean;
  canWriteFaces: boolean;
  onOpenCompare: (face: Face, suggestion: FaceTopSuggestion) => void;
  selectedIds: Set<number>;
  onToggle: (id: number) => void;
  selecting: boolean;
}) {
  return (
    <div className="overflow-hidden rounded-xl border border-border bg-card">
      <div className="hidden grid-cols-[minmax(0,1.1fr)_110px_130px_minmax(0,1fr)_120px] gap-3 border-b border-border bg-surface/70 px-4 py-2 text-[11px] font-medium uppercase tracking-wide text-muted lg:grid">
        <span>Face</span>
        <span>Detections</span>
        <span>Scenes / Images</span>
        <span>Top suggestion</span>
        <span>Updated</span>
      </div>
      <div className="divide-y divide-border">
        {faces.map((face) => (
          <FaceListRow
            key={face.id}
            face={face}
            onNavigate={onNavigate}
            canReadPerformers={canReadPerformers}
            canWriteFaces={canWriteFaces}
            onOpenCompare={(suggestion) => onOpenCompare(face, suggestion)}
            selected={selectedIds.has(face.id)}
            onToggle={() => onToggle(face.id)}
            selecting={selecting}
          />
        ))}
      </div>
    </div>
  );
}

function FaceListRow({
  face,
  onNavigate,
  canReadPerformers,
  canWriteFaces,
  onOpenCompare,
  selected,
  onToggle,
  selecting,
}: {
  face: Face;
  onNavigate: (r: any) => void;
  canReadPerformers: boolean;
  canWriteFaces: boolean;
  onOpenCompare: (suggestion: FaceTopSuggestion) => void;
  selected: boolean;
  onToggle: () => void;
  selecting: boolean;
}) {
  const title = face.label?.trim() || face.performerName || `Face #${face.id}`;

  return (
    <div
      onClick={selecting ? onToggle : undefined}
      className={`group relative cursor-pointer px-4 py-3 transition-colors ${selected ? "bg-accent/10" : "hover:bg-surface/40"}`}
    >
      <RouteCardLinkOverlay
        route={{ page: "face", id: face.id }}
        onClick={() => onNavigate({ page: "face", id: face.id })}
        label={`Open face ${title}`}
        disabled={selecting}
        selectionSafeZone
      />
      <div className="relative z-10 flex items-start gap-3 lg:grid lg:grid-cols-[minmax(0,1.1fr)_110px_130px_minmax(0,1fr)_120px] lg:items-center">
        <div className="relative min-w-0 pl-8">
          <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onToggle} />
          <div className="flex items-start gap-3">
            <div className="hidden h-16 w-16 shrink-0 overflow-hidden rounded-full bg-surface sm:block">
              {face.coverImageUrl ? (
                      <img src={face.coverImageUrl} alt={title} className="h-full w-full bg-surface/85 object-contain p-1" loading="lazy" />
              ) : (
                <div className="flex h-full w-full items-center justify-center text-muted">
                  <Fingerprint className="h-6 w-6" />
                </div>
              )}
            </div>
            <div className="min-w-0">
              <div className="truncate text-sm font-medium text-foreground">{title}</div>
              <div className="mt-1 flex flex-wrap items-center gap-1.5 text-[11px] text-secondary">
                {face.mergedIntoFaceId ? <Badge icon={<Merge className="h-3 w-3" />} label={`Merged into #${face.mergedIntoFaceId}`} /> : null}
                {face.performerId ? <Badge icon={<Link2 className="h-3 w-3" />} label={face.performerName || `Performer #${face.performerId}`} /> : null}
              </div>
            </div>
          </div>
        </div>
        <div className="hidden text-xs text-secondary lg:block">{face.detectionCount}</div>
        <div className="hidden text-xs text-secondary lg:block">{face.sceneCount} / {face.imageCount}</div>
        <div className="hidden lg:block">
          {face.performerId ? (
            <LinkedPerformerSummary face={face} onNavigate={onNavigate} canReadPerformers={canReadPerformers} compact />
          ) : (
            <TopSuggestionFooter
              face={face}
              suggestion={face.topSuggestion}
              onNavigate={onNavigate}
              canReadPerformers={canReadPerformers}
              canWriteFaces={canWriteFaces}
              onOpenCompare={onOpenCompare}
              compact
            />
          )}
        </div>
        <div className="hidden text-xs text-secondary lg:block">{formatDate(face.updatedAt)}</div>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-3 pl-8 text-[11px] text-secondary lg:hidden">
        <span>{face.detectionCount} detections</span>
        <span>{face.sceneCount} scenes</span>
        <span>{face.imageCount} images</span>
        {face.topSuggestion ? <span>Top match: {face.topSuggestion.performerName}</span> : null}
      </div>
    </div>
  );
}

function TopSuggestionFooter({
  face,
  suggestion,
  onNavigate,
  canReadPerformers,
  canWriteFaces,
  onOpenCompare,
  compact = false,
}: {
  face: Face;
  suggestion?: FaceTopSuggestion;
  onNavigate: (r: any) => void;
  canReadPerformers: boolean;
  canWriteFaces: boolean;
  onOpenCompare: (suggestion: FaceTopSuggestion) => void;
  compact?: boolean;
}) {
  if (!suggestion) {
    return <div className="text-xs text-secondary">No top suggestion yet.</div>;
  }

  const localPerformerId = suggestion.localPerformerId ?? (suggestion.performerId > 0 ? suggestion.performerId : undefined);
  const performerLinkProps = localPerformerId != null && canReadPerformers
    ? createNestedRouteLinkProps<HTMLAnchorElement>({ page: "performer", id: localPerformerId }, () => onNavigate({ page: "performer", id: localPerformerId }))
    : null;

  return (
    <div className={`relative z-20 flex items-center gap-3 ${compact ? "min-w-0" : ""}`}>
      <div className={`${compact ? "h-10 w-10" : "h-12 w-12"} shrink-0 overflow-hidden rounded-xl bg-surface/80`}>
        {suggestion.coverImageUrl ? (
          <img src={suggestion.coverImageUrl} alt={suggestion.performerName} className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <div className="flex h-full w-full items-center justify-center text-muted">
            <Fingerprint className="h-5 w-5" />
          </div>
        )}
      </div>
      <div className="min-w-0 flex-1 space-y-1">
        <div className="text-[11px] font-semibold uppercase tracking-wide text-muted">Top suggestion</div>
        {performerLinkProps ? (
          <a {...performerLinkProps} className="block truncate text-sm font-medium text-accent hover:underline">
            {suggestion.performerName}
          </a>
        ) : suggestion.externalUrl ? (
          <a href={suggestion.externalUrl} target="_blank" rel="noopener noreferrer" className="block truncate text-sm font-medium text-accent hover:underline">
            {suggestion.performerName}
          </a>
        ) : (
          <div className="truncate text-sm font-medium text-foreground">{suggestion.performerName}</div>
        )}
        <div className="text-xs text-secondary">{formatPercent(suggestion.confidence)}% confidence</div>
      </div>
      {canWriteFaces ? (
        <button
          type="button"
          onClick={(event) => {
            event.preventDefault();
            event.stopPropagation();
            onOpenCompare(suggestion);
          }}
          className="relative z-20 shrink-0 rounded-lg border border-border px-3 py-1.5 text-xs font-medium text-foreground transition-colors hover:border-accent hover:text-accent"
        >
          Link
        </button>
      ) : null}
    </div>
  );
}

function LinkedPerformerSummary({
  face,
  onNavigate,
  canReadPerformers,
  compact = false,
}: {
  face: Face;
  onNavigate: (r: any) => void;
  canReadPerformers: boolean;
  compact?: boolean;
}) {
  const performerId = face.performerId;
  if (performerId == null) {
    return null;
  }

  const performerLinkProps = canReadPerformers
    ? createNestedRouteLinkProps<HTMLAnchorElement>({ page: "performer", id: performerId }, () => onNavigate({ page: "performer", id: performerId }))
    : null;

  return (
    <div className={`relative z-10 ${compact ? "text-xs" : "space-y-1"}`}>
      <div className="text-[11px] font-semibold uppercase tracking-wide text-muted">Linked performer</div>
      {performerLinkProps ? (
        <a {...performerLinkProps} className="truncate text-sm font-medium text-accent hover:underline">
          {face.performerName || `Performer #${performerId}`}
        </a>
      ) : (
        <div className="truncate text-sm font-medium text-foreground">{face.performerName || `Performer #${performerId}`}</div>
      )}
    </div>
  );
}

function Badge({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full border border-white/15 bg-black/35 px-2 py-0.5 text-[10px] font-medium text-white">
      {icon}
      {label}
    </span>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-lg border border-border bg-surface/50 px-2 py-2">
      <div className="text-sm font-semibold text-foreground">{value}</div>
      <div className="mt-1 text-[10px] uppercase tracking-wide text-muted">{label}</div>
    </div>
  );
}

function formatPercent(value: number) {
  const scaled = value <= 1 ? value * 100 : value;
  return Math.max(0, Math.min(100, Math.round(scaled)));
}
