import { ArrowDown, ArrowUp, ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight, Grid3X3, List, Search, Shuffle, ZoomIn, ZoomOut } from "lucide-react";
import type { FindFilter } from "../api/types";
import { isValidElement, useEffect, useMemo, useState } from "react";
import { useEntityCardSize } from "../hooks/useEntityCardSize";
import { reshuffleRandomSort, withSeededRandomSort } from "../utils/seededRandomSort";
import { LIST_PER_PAGE_OPTIONS, toolbarIconButtonClass, toolbarSegmentClass, toolbarSelectClass } from "./listToolbarStyles";
import { FilterButton, FilterDialog, type CriterionDefinition } from "./FilterDialog";

const PER_PAGE_OPTIONS = LIST_PER_PAGE_OPTIONS;

interface DetailListToolbarProps {
  filter: FindFilter;
  onFilterChange: (f: FindFilter) => void;
  totalCount: number;
  sortOptions: { value: string; label: string }[];
  zoomLevel?: number;
  onZoomChange?: (level: number) => void;
  cardSizeEntityType?: string;
  showSearch?: boolean;
  showSort?: boolean;
  selectedCount?: number;
  onSelectAll?: () => void;
  onSelectAllMatching?: () => void;
  onSelectNone?: () => void;
  selectAllLabel?: string;
  selectAllPending?: boolean;
  selectAllMatchingLabel?: string;
  selectAllMatchingPending?: boolean;
  selectionActions?: React.ReactNode;
  displayMode?: "grid" | "list";
  onDisplayModeChange?: (mode: "grid" | "list") => void;
  criteriaDefinitions?: CriterionDefinition[];
  objectFilter?: Record<string, unknown>;
  onObjectFilterChange?: (filter: Record<string, unknown>) => void;
  allowInfinitePageSize?: boolean;
  infinitePageSizeOnly?: boolean;
  showPagingControls?: boolean;
}

export function DetailListToolbar({ filter, onFilterChange, totalCount, sortOptions, zoomLevel, onZoomChange, cardSizeEntityType, showSearch, showSort = true, selectedCount, onSelectAll, onSelectAllMatching, onSelectNone, selectAllLabel = "Select all", selectAllPending = false, selectAllMatchingLabel = "Select all matching", selectAllMatchingPending, selectionActions, displayMode, onDisplayModeChange, criteriaDefinitions, objectFilter, onObjectFilterChange, allowInfinitePageSize = false, infinitePageSizeOnly = false, showPagingControls = true }: DetailListToolbarProps) {
  const page = filter.page ?? 1;
  const perPage = filter.perPage ?? 24;
  const infinitePageSize = allowInfinitePageSize && (perPage === 0 || infinitePageSizeOnly);
  const pageSizeOptions = useMemo(() => {
    if (infinitePageSizeOnly) return [];
    if (infinitePageSize || PER_PAGE_OPTIONS.includes(perPage)) return PER_PAGE_OPTIONS;
    return [...PER_PAGE_OPTIONS, perPage].sort((left, right) => left - right);
  }, [infinitePageSize, infinitePageSizeOnly, perPage]);
  const effectivePerPage = infinitePageSize ? Math.max(totalCount, 1) : perPage;
  const totalPages = Math.max(1, Math.ceil(totalCount / effectivePerPage));
  const start = totalCount > 0 ? (infinitePageSize ? 1 : (page - 1) * effectivePerPage + 1) : 0;
  const end = infinitePageSize ? totalCount : Math.min(page * effectivePerPage, totalCount);
  const [searchText, setSearchText] = useState(filter.q ?? "");
  const [filterDialogOpen, setFilterDialogOpen] = useState(false);
  const sortedSortOptions = useMemo(
    () => [...sortOptions].sort((left, right) => left.label.localeCompare(right.label)),
    [sortOptions]
  );
  const selectionActionEntityType = useMemo(() => {
    if (!isValidElement<{ entityType?: string }>(selectionActions)) return undefined;
    return selectionActions.props.entityType;
  }, [selectionActions]);
  const inferredCardSizeEntityType = useMemo(() => cardSizeEntityType ?? selectionActionEntityType ?? inferCardSizeEntityType(sortOptions), [cardSizeEntityType, selectionActionEntityType, sortOptions]);
  const [storedZoomLevel, setStoredZoomLevel] = useEntityCardSize(inferredCardSizeEntityType, undefined, zoomLevel ?? 0);
  const effectiveZoomLevel = inferredCardSizeEntityType ? storedZoomLevel : zoomLevel;

  useEffect(() => {
    if (!inferredCardSizeEntityType || zoomLevel == null || !onZoomChange) return;
    if (Math.abs(storedZoomLevel - zoomLevel) > 0.001) onZoomChange(storedZoomLevel);
  }, [inferredCardSizeEntityType, onZoomChange, storedZoomLevel, zoomLevel]);

  const handleZoomChange = (level: number) => {
    if (inferredCardSizeEntityType) setStoredZoomLevel(level);
    onZoomChange?.(level);
  };

  const goTo = (nextPage: number) => onFilterChange({ ...filter, page: Math.max(1, Math.min(totalPages, nextPage)) });
  const activeObjectFilter = objectFilter ?? {};

  return (
    <>
      <div className="mx-auto mb-2 flex max-w-7xl flex-wrap items-center gap-2 rounded-xl border border-border bg-surface/90 px-2.5 py-2 text-sm shadow-sm shadow-black/20">
        <div className="mr-auto flex min-w-0 items-center gap-2 pr-2">
          <span className="text-xs text-muted">
            {totalCount > 0 ? `${start}–${end} of ${totalCount.toLocaleString()}` : "0 items"}
          </span>
        </div>

        {showSearch && (
          <form onSubmit={(e) => { e.preventDefault(); onFilterChange({ ...filter, q: searchText || undefined, page: 1 }); }} className="flex w-full max-w-[18rem] shrink-0 items-center gap-1">
            <div className="relative min-w-0 flex-1">
              <Search className="absolute left-2 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted" />
              <input
                type="text"
                value={searchText}
                onChange={(e) => setSearchText(e.target.value)}
                onBlur={() => { if (searchText !== (filter.q ?? "")) onFilterChange({ ...filter, q: searchText || undefined, page: 1 }); }}
                placeholder="Search…"
                className="min-h-[30px] w-full rounded-lg border border-border bg-card/70 py-1.5 pl-7 pr-2 text-xs text-foreground placeholder:text-muted focus:border-accent focus:outline-none"
              />
            </div>
          </form>
        )}

        {showSort && (
          <div className={toolbarSegmentClass}>
            <select
              value={filter.sort ?? sortedSortOptions[0]?.value ?? ""}
              onChange={(e) => onFilterChange(withSeededRandomSort(filter, { ...filter, sort: e.target.value, page: 1 }))}
              className={`${toolbarSelectClass} min-w-[8.5rem] max-w-[10rem]`}
            >
              {sortedSortOptions.map((opt) => (
                <option key={opt.value} value={opt.value}>{opt.label}</option>
              ))}
            </select>
            {filter.sort === "random" ? (
              <button
                type="button"
                onClick={() => onFilterChange(reshuffleRandomSort(filter))}
                className={toolbarIconButtonClass}
                title="Shuffle"
                aria-label="Shuffle"
              >
                <Shuffle className="w-3.5 h-3.5" />
              </button>
            ) : null}
            <button
              type="button"
              onClick={() => onFilterChange(withSeededRandomSort(filter, { ...filter, direction: filter.direction === "asc" ? "desc" : "asc", page: 1 }))}
              className={toolbarIconButtonClass}
              title={filter.direction === "asc" ? "Ascending" : "Descending"}
            >
              {filter.direction === "desc" ? <ArrowDown className="w-3.5 h-3.5" /> : <ArrowUp className="w-3.5 h-3.5" />}
            </button>
          </div>
        )}

        {criteriaDefinitions && onObjectFilterChange ? (
          <FilterButton activeCount={Object.keys(activeObjectFilter).length} onClick={() => setFilterDialogOpen(true)} />
        ) : null}

        {displayMode && onDisplayModeChange ? (
          <div className={toolbarSegmentClass}>
            <button type="button" onClick={() => onDisplayModeChange("grid")} className={`${toolbarIconButtonClass} ${displayMode === "grid" ? "bg-background/60 text-accent shadow-sm" : ""}`} title="Grid view">
              <Grid3X3 className="h-3.5 w-3.5" />
            </button>
            <button type="button" onClick={() => onDisplayModeChange("list")} className={`${toolbarIconButtonClass} ${displayMode === "list" ? "bg-background/60 text-accent shadow-sm" : ""}`} title="List view">
              <List className="h-3.5 w-3.5" />
            </button>
          </div>
        ) : null}

        <div className={toolbarSegmentClass}>
          <select
            value={infinitePageSize ? "infinite" : String(perPage)}
            onChange={(e) => onFilterChange({ ...filter, perPage: e.target.value === "infinite" ? 0 : Number(e.target.value), page: 1 })}
            className={`${toolbarSelectClass} min-w-[4.75rem]`}
            title="Items per page"
            disabled={infinitePageSizeOnly}
          >
            {allowInfinitePageSize ? <option value="infinite">Infinite</option> : null}
            {pageSizeOptions.map((n) => (
              <option key={n} value={n}>{n}</option>
            ))}
          </select>

          {effectiveZoomLevel !== undefined && onZoomChange && (
            <div className="hidden items-center gap-1 pl-1 md:flex">
              <ZoomOut className="w-3 h-3 text-muted" />
              <input
                type="range"
                min={0} max={5} step={0.25}
                value={effectiveZoomLevel}
                onChange={(e) => handleZoomChange(Number(e.target.value))}
                className="h-1 w-16 cursor-pointer accent-accent sm:w-20"
                title={`Card size: ${Math.round(240 + effectiveZoomLevel * 60)}px`}
              />
              <ZoomIn className="w-3 h-3 text-muted" />
            </div>
          )}
        </div>
      </div>

      {selectedCount !== undefined && selectedCount > 0 && (
        <div className="mx-auto mb-2 flex max-w-7xl flex-wrap items-center gap-3 rounded-lg border border-border bg-card/80 px-3 py-1.5">
          <span className="text-xs text-secondary">{selectedCount} selected</span>
          {onSelectAll && <button onClick={onSelectAll} disabled={selectAllPending} className="text-xs text-accent hover:underline disabled:cursor-not-allowed disabled:opacity-60">{selectAllPending ? "Selecting..." : selectAllLabel}</button>}
          {onSelectAllMatching && (
            <button onClick={onSelectAllMatching} disabled={selectAllMatchingPending} className="text-xs text-accent hover:underline disabled:cursor-not-allowed disabled:opacity-60">
              {selectAllMatchingPending ? "Selecting..." : selectAllMatchingLabel}
            </button>
          )}
          {onSelectNone && <button onClick={onSelectNone} className="text-xs text-secondary hover:text-foreground">Deselect all</button>}
          {selectionActions}
        </div>
      )}

      {showPagingControls && !infinitePageSize && totalPages > 1 && (
        <div className="mx-auto mb-4 flex max-w-7xl items-center justify-center gap-1 py-1">
          <button disabled={page <= 1} onClick={() => goTo(1)}
            className={`${toolbarIconButtonClass} disabled:cursor-not-allowed disabled:opacity-30`}>
            <ChevronsLeft className="w-3.5 h-3.5" />
          </button>
          <button disabled={page <= 1} onClick={() => goTo(page - 1)}
            className={`${toolbarIconButtonClass} disabled:cursor-not-allowed disabled:opacity-30`}>
            <ChevronLeft className="w-3.5 h-3.5" />
          </button>
          <span className="px-2 text-xs text-muted">{page} / {totalPages}</span>
          <button disabled={page >= totalPages} onClick={() => goTo(page + 1)}
            className={`${toolbarIconButtonClass} disabled:cursor-not-allowed disabled:opacity-30`}>
            <ChevronRight className="w-3.5 h-3.5" />
          </button>
          <button disabled={page >= totalPages} onClick={() => goTo(totalPages)}
            className={`${toolbarIconButtonClass} disabled:cursor-not-allowed disabled:opacity-30`}>
            <ChevronsRight className="w-3.5 h-3.5" />
          </button>
        </div>
      )}
      {criteriaDefinitions && onObjectFilterChange ? (
        <FilterDialog
          open={filterDialogOpen}
          onClose={() => setFilterDialogOpen(false)}
          criteria={criteriaDefinitions}
          activeFilter={activeObjectFilter}
          onApply={(nextFilter) => {
            onObjectFilterChange(nextFilter);
            onFilterChange({ ...filter, page: 1 });
          }}
        />
      ) : null}
    </>
  );
}

function inferCardSizeEntityType(sortOptions?: { value: string; label: string }[]) {
  const values = new Set((sortOptions ?? []).map((option) => option.value));
  if (values.has("framerate") || values.has("bitrate") || values.has("play_duration") || values.has("performer_age")) return "scenes";
  if (values.has("measurements") || values.has("birthdate") || values.has("career_length")) return "performers";
  if (values.has("image_count") && values.has("path")) return "galleries";
  return undefined;
}
