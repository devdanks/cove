import { useCallback, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { FindFilter, FaceTopSuggestion } from "../api/types";
import { aiFaces, faces } from "../api/client";
import type { Face } from "../api/types";
import { Fingerprint, Link2, Merge } from "lucide-react";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../components/RouteCardLinkOverlay";
import { createNestedRouteLinkProps } from "../components/cardNavigation";
import { FaceCompareDialog } from "../components/FaceCompareDialog";
import { formatDate } from "../components/shared";
import { useListUrlState } from "../hooks/useListUrlState";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { useAuth } from "../auth/AuthContext";
import { canReadEntity, canWriteEntity } from "../auth/visibility";

interface Props {
  onNavigate: (r: any) => void;
}

type TriState = "all" | "yes" | "no";

function readTriState(value: unknown): TriState {
  return value === "yes" || value === "no" ? value : "all";
}

function sanitizeFaceFilters(filter: Record<string, unknown>) {
  const next: Record<string, unknown> = {};
  const merged = readTriState(filter.merged);

  if (merged !== "all") {
    next.merged = merged;
  }

  return next;
}

export function FacesPage({ onNavigate }: Props) {
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const canReadPerformers = canReadEntity("performer", hasPermission);
  const canWriteFaces = canWriteEntity("face", hasPermission);
  const defaultState = useMemo(() => ({
    filter: { page: 1, perPage: 36 } as FindFilter,
    objectFilter: {},
    displayMode: "grid" as DisplayMode,
  }), []);
  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode } = useListUrlState({
    resetKey: "faces",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list"] as const,
  });
  const merged = readTriState(objectFilter.merged);
  const [comparison, setComparison] = useState<{ face: Face; suggestion: FaceTopSuggestion } | null>(null);

  const query = useMemo(() => ({
    q: filter.q?.trim() || undefined,
    merged: merged === "all" ? undefined : merged === "yes",
    page: filter.page ?? 1,
    perPage: filter.perPage ?? 36,
  }), [filter.page, filter.perPage, filter.q, merged]);

  const { data, isLoading } = useQuery({
    queryKey: ["faces", query],
    queryFn: () => faces.list(query),
  });

  const items = data?.items ?? [];
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(items);
  const selecting = selectedIds.size > 0;

  const invalidateFace = useCallback((faceId: number) => {
    queryClient.invalidateQueries({ queryKey: ["faces"] });
    queryClient.invalidateQueries({ queryKey: ["face", faceId] });
    queryClient.invalidateQueries({ queryKey: ["face", faceId, "suggestions"] });
  }, [queryClient]);

  const linkMutation = useMutation({
    mutationFn: (data: { faceId: number; performerId: number }) => faces.link(data.faceId, { performerId: data.performerId }),
    onSuccess: (_, variables) => {
      invalidateFace(variables.faceId);
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
    setFilter(next);
  }, [setFilter]);

  const updateFaceFilter = useCallback((patch: Record<string, unknown>) => {
    setObjectFilter(sanitizeFaceFilters({ ...objectFilter, ...patch }));
    setFilter({ ...filter, page: 1 });
  }, [filter, objectFilter, setFilter, setObjectFilter]);

  const hasExtraFilters = Object.keys(objectFilter).length > 0;
  const compareBusy = linkMutation.isPending || rejectSuggestionMutation.isPending || referenceSuggestionMutation.isPending;

  const handleConfirmSuggestion = useCallback((face: Face, suggestion: FaceTopSuggestion) => {
    const localPerformerId = suggestion.localPerformerId ?? (suggestion.performerId > 0 ? suggestion.performerId : undefined);
    if (localPerformerId != null) {
      linkMutation.mutate({ faceId: face.id, performerId: localPerformerId });
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
        totalCount={data?.totalCount ?? 0}
        isLoading={isLoading}
        displayMode={displayMode}
        onDisplayModeChange={setDisplayMode}
        availableDisplayModes={["grid", "list"]}
        selectedIds={selectedIds}
        onSelectAll={selectAll}
        onSelectNone={selectNone}
        metadataByline={<span className="hidden text-xs text-muted lg:inline">Browse unlinked clusters with the strongest suggestion visible directly on each card.</span>}
        renderOperations={() => (
          <div className="flex flex-wrap items-center gap-2">
            <select
              value={merged}
              onChange={(event) => updateFaceFilter({ merged: event.target.value })}
              className="min-h-[30px] rounded-lg border border-border bg-card/70 px-3 py-1.5 text-xs text-foreground focus:border-accent focus:outline-none"
              aria-label="Filter faces by merge state"
            >
              <option value="all">Merged + primary</option>
              <option value="no">Primary only</option>
              <option value="yes">Merged only</option>
            </select>
            {hasExtraFilters ? (
              <button
                type="button"
                onClick={() => {
                  setObjectFilter({});
                  setFilter({ ...filter, page: 1 });
                }}
                className="rounded-lg border border-border px-3 py-1 text-xs text-secondary hover:border-accent hover:text-foreground"
              >
                Clear
              </button>
            ) : null}
          </div>
        )}
      >
        {displayMode === "grid" ? (
          <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(18rem,1fr))]">
            {items.map((face) => (
              <FaceCard
                key={face.id}
                face={face}
                onNavigate={onNavigate}
                canReadPerformers={canReadPerformers}
                canWriteFaces={canWriteFaces}
                onOpenCompare={(suggestion) => setComparison({ face, suggestion })}
                selected={selectedIds.has(face.id)}
                onSelect={() => toggle(face.id)}
                selecting={selecting}
              />
            ))}
          </div>
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

      <FaceCompareDialog
        open={comparison != null}
        face={comparison?.face ?? null}
        suggestion={comparison?.suggestion ?? null}
        disabled={compareBusy}
        canReadPerformers={canReadPerformers}
        onClose={() => setComparison(null)}
        onConfirm={(suggestion) => {
          if (comparison) {
            handleConfirmSuggestion(comparison.face, suggestion as FaceTopSuggestion);
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

function FaceCard({
  face,
  onNavigate,
  canReadPerformers,
  canWriteFaces,
  onOpenCompare,
  selected,
  onSelect,
  selecting,
}: {
  face: Face;
  onNavigate: (r: any) => void;
  canReadPerformers: boolean;
  canWriteFaces: boolean;
  onOpenCompare: (suggestion: FaceTopSuggestion) => void;
  selected: boolean;
  onSelect: () => void;
  selecting: boolean;
}) {
  const title = face.label?.trim() || face.performerName || `Face #${face.id}`;
  const openFace = () => onNavigate({ page: "face", id: face.id });

  return (
    <article className={`entity-card group relative overflow-hidden rounded-2xl border bg-card/80 shadow-sm transition-colors ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/50"}`}>
      <RouteCardLinkOverlay
        route={{ page: "face", id: face.id }}
        onClick={openFace}
        label={`Open face ${title}`}
        disabled={selecting}
        selectionSafeZone
      />
      <div
        onClick={selecting ? onSelect : openFace}
        className="relative block aspect-square max-h-[22rem] w-full bg-surface/70 text-left"
      >
        <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
        {face.coverImageUrl ? (
          <img src={face.coverImageUrl} alt={title} className="h-full w-full bg-surface/85 object-contain p-2" loading="lazy" />
        ) : (
          <div className="flex h-full items-center justify-center bg-surface text-muted">
            <Fingerprint className="h-12 w-12" />
          </div>
        )}
        <div className="absolute inset-x-0 bottom-0 flex flex-wrap gap-1 bg-gradient-to-t from-black/80 via-black/35 to-transparent p-3">
          {face.mergedIntoFaceId && <Badge icon={<Merge className="h-3 w-3" />} label={`Merged into #${face.mergedIntoFaceId}`} />}
          {face.performerId && <Badge icon={<Link2 className="h-3 w-3" />} label="Linked" />}
        </div>
      </div>
      <div className="relative z-10 space-y-3 p-4">
        <div>
          <button type="button" onClick={openFace} className="relative z-10 text-left text-sm font-semibold text-foreground hover:text-accent">
            {title}
          </button>
          <div className="mt-1 text-xs text-secondary">Updated {formatDate(face.updatedAt)}</div>
        </div>
        <div className="grid grid-cols-3 gap-2 text-center text-xs">
          <Metric label="Detections" value={face.detectionCount} />
          <Metric label="Scenes" value={face.sceneCount} />
          <Metric label="Images" value={face.imageCount} />
        </div>
        <div className="relative z-20 rounded-xl border border-border bg-surface/50 p-3">
          {face.performerId ? (
            <LinkedPerformerSummary face={face} onNavigate={onNavigate} canReadPerformers={canReadPerformers} />
          ) : (
            <TopSuggestionFooter
              face={face}
              suggestion={face.topSuggestion}
              onNavigate={onNavigate}
              canReadPerformers={canReadPerformers}
              canWriteFaces={canWriteFaces}
              onOpenCompare={onOpenCompare}
            />
          )}
        </div>
        <div className="flex items-center justify-between gap-2 text-xs text-secondary">
          <span>Source: {face.primarySourceKey || "unknown"}</span>
          <span>{face.frameSampleCount ?? 0} samples</span>
        </div>
      </div>
    </article>
  );
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
