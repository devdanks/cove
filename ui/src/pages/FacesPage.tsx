import { useCallback, useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import type { FindFilter } from "../api/types";
import { faces } from "../api/client";
import type { Face } from "../api/types";
import { Fingerprint, Link2, Merge } from "lucide-react";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../components/RouteCardLinkOverlay";
import { formatDate } from "../components/shared";
import { useListUrlState } from "../hooks/useListUrlState";
import { useMultiSelect } from "../hooks/useMultiSelect";

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

  const handleFilterChange = useCallback((next: FindFilter) => {
    setFilter(next);
  }, [setFilter]);

  const updateFaceFilter = useCallback((patch: Record<string, unknown>) => {
    setObjectFilter(sanitizeFaceFilters({ ...objectFilter, ...patch }));
    setFilter({ ...filter, page: 1 });
  }, [filter, objectFilter, setFilter, setObjectFilter]);

  const hasExtraFilters = Object.keys(objectFilter).length > 0;

  return (
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
        metadataByline={<span className="hidden text-xs text-muted lg:inline">Browse linked and merged face clusters using the shared entity browser.</span>}
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
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
            {items.map((face) => (
              <FaceCard
                key={face.id}
                face={face}
                onNavigate={onNavigate}
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
  );
}

function FaceCard({
  face,
  onNavigate,
  selected,
  onSelect,
  selecting,
}: {
  face: Face;
  onNavigate: (r: any) => void;
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
      <button
        type="button"
        onClick={selecting ? onSelect : openFace}
        className="relative block aspect-square w-full bg-surface/70 text-left"
        aria-label={`Open face ${title}`}
      >
        <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />
        {face.coverImageUrl ? (
          <img src={face.coverImageUrl} alt={title} className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <div className="flex h-full items-center justify-center bg-surface text-muted">
            <Fingerprint className="h-12 w-12" />
          </div>
        )}
        <div className="absolute inset-x-0 bottom-0 flex flex-wrap gap-1 bg-gradient-to-t from-black/80 via-black/35 to-transparent p-3">
          {face.mergedIntoFaceId && <Badge icon={<Merge className="h-3 w-3" />} label={`Merged into #${face.mergedIntoFaceId}`} />}
          {face.performerId && <Badge icon={<Link2 className="h-3 w-3" />} label="Linked" />}
        </div>
      </button>
      <div className="space-y-3 p-4">
        <div>
          <button type="button" onClick={openFace} className="text-left text-sm font-semibold text-foreground hover:text-accent">
            {title}
          </button>
          <div className="mt-1 text-xs text-secondary">Updated {formatDate(face.updatedAt)}</div>
        </div>
        <div className="grid grid-cols-3 gap-2 text-center text-xs">
          <Metric label="Detections" value={face.detectionCount} />
          <Metric label="Scenes" value={face.sceneCount} />
          <Metric label="Images" value={face.imageCount} />
        </div>
        <div className="flex items-center justify-between gap-2 text-xs text-secondary">
          <span>Source: {face.primarySourceKey || "unknown"}</span>
          {face.performerId && (
            <button
              onClick={() => onNavigate({ page: "performer", id: face.performerId })}
              className="text-accent hover:underline"
            >
              {face.performerName || `Performer #${face.performerId}`}
            </button>
          )}
        </div>
      </div>
    </article>
  );
}

function FaceListTable({
  faces,
  onNavigate,
  selectedIds,
  onToggle,
  selecting,
}: {
  faces: Face[];
  onNavigate: (r: any) => void;
  selectedIds: Set<number>;
  onToggle: (id: number) => void;
  selecting: boolean;
}) {
  return (
    <div className="overflow-hidden rounded-xl border border-border bg-card">
      <div className="hidden grid-cols-[minmax(0,1.3fr)_120px_120px_150px_120px] gap-3 border-b border-border bg-surface/70 px-4 py-2 text-[11px] font-medium uppercase tracking-wide text-muted lg:grid">
        <span>Face</span>
        <span>Detections</span>
        <span>Scenes / Images</span>
        <span>Source</span>
        <span>Updated</span>
      </div>
      <div className="divide-y divide-border">
        {faces.map((face) => (
          <FaceListRow
            key={face.id}
            face={face}
            onNavigate={onNavigate}
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
  selected,
  onToggle,
  selecting,
}: {
  face: Face;
  onNavigate: (r: any) => void;
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
      <div className="flex items-start gap-3 lg:grid lg:grid-cols-[minmax(0,1.3fr)_120px_120px_150px_120px] lg:items-center">
        <div className="relative min-w-0 pl-8">
          <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onToggle} />
          <div className="flex items-start gap-3">
            <div className="hidden h-16 w-16 shrink-0 overflow-hidden rounded-full bg-surface sm:block">
              {face.coverImageUrl ? (
                <img src={face.coverImageUrl} alt={title} className="h-full w-full object-cover" loading="lazy" />
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
        <div className="hidden text-xs text-secondary lg:block">{face.primarySourceKey || "unknown"}</div>
        <div className="hidden text-xs text-secondary lg:block">{formatDate(face.updatedAt)}</div>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-3 pl-8 text-[11px] text-secondary lg:hidden">
        <span>{face.detectionCount} detections</span>
        <span>{face.sceneCount} scenes</span>
        <span>{face.imageCount} images</span>
      </div>
    </div>
  );
}

function Badge({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-black/55 px-2 py-1 text-[11px] font-medium text-white backdrop-blur-sm">
      {icon}
      {label}
    </span>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border bg-surface/60 px-2 py-2">
      <div className="text-base font-semibold text-foreground">{value}</div>
      <div className="mt-1 text-[11px] uppercase tracking-wide text-muted">{label}</div>
    </div>
  );
}