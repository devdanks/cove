import { useMemo, useState } from "react";
import { ArrowDown, ArrowUp, SlidersHorizontal } from "lucide-react";
import type { FindFilter, SceneFilterCriteria } from "../api/types";
import { FilterDialog, SCENE_CRITERIA } from "./FilterDialog";
import { Field } from "./EditModal";

export const FILTER_DYNAMIC_SOURCE_KEY = "filter";

const DEFAULT_FIND_FILTER: FindFilter = {
  page: 1,
  perPage: 40,
  sort: "updated_at",
  direction: "desc",
};

const SCENE_SORT_OPTIONS = [
  { value: "updated_at", label: "Recently Updated" },
  { value: "created_at", label: "Recently Added" },
  { value: "date", label: "Date" },
  { value: "title", label: "Title" },
  { value: "rating", label: "Rating" },
  { value: "duration", label: "Duration" },
  { value: "path", label: "Path" },
];

interface DynamicGroupFilterQuery {
  entityType?: string;
  findFilter?: FindFilter;
  objectFilter?: SceneFilterCriteria;
}

interface DynamicGroupFilterEditorProps {
  queryJson?: string | null;
  onChange: (queryJson: string) => void;
}

export function defaultDynamicGroupFilterQueryJson() {
  return serializeDynamicGroupFilterQuery(DEFAULT_FIND_FILTER, {});
}

export function parseDynamicGroupFilterQuery(queryJson?: string | null): Required<Pick<DynamicGroupFilterQuery, "findFilter" | "objectFilter">> {
  if (!queryJson) {
    return { findFilter: DEFAULT_FIND_FILTER, objectFilter: {} };
  }

  try {
    const parsed = JSON.parse(queryJson) as DynamicGroupFilterQuery;
    return {
      findFilter: { ...DEFAULT_FIND_FILTER, ...(parsed.findFilter ?? {}), page: 1 },
      objectFilter: parsed.objectFilter ?? {},
    };
  } catch {
    return { findFilter: DEFAULT_FIND_FILTER, objectFilter: {} };
  }
}

export function serializeDynamicGroupFilterQuery(findFilter: FindFilter, objectFilter: SceneFilterCriteria) {
  const cleanedFindFilter: FindFilter = {
    ...findFilter,
    page: 1,
    perPage: findFilter.perPage ?? DEFAULT_FIND_FILTER.perPage,
    sort: findFilter.sort ?? DEFAULT_FIND_FILTER.sort,
    direction: findFilter.direction ?? DEFAULT_FIND_FILTER.direction,
  };

  if (!cleanedFindFilter.q) {
    delete cleanedFindFilter.q;
  }

  return JSON.stringify({
    entityType: "scene",
    findFilter: cleanedFindFilter,
    objectFilter: Object.keys(objectFilter).length > 0 ? objectFilter : undefined,
  });
}

export function DynamicGroupFilterEditor({ queryJson, onChange }: DynamicGroupFilterEditorProps) {
  const [filterOpen, setFilterOpen] = useState(false);
  const query = useMemo(() => parseDynamicGroupFilterQuery(queryJson), [queryJson]);
  const findFilter = query.findFilter;
  const objectFilter = query.objectFilter;
  const activeCriteriaCount = Object.keys(objectFilter).length;

  const updateFindFilter = (next: FindFilter) => onChange(serializeDynamicGroupFilterQuery(next, objectFilter));
  const updateObjectFilter = (next: Record<string, unknown>) => onChange(serializeDynamicGroupFilterQuery(findFilter, next as SceneFilterCriteria));

  return (
    <div className="rounded-lg border border-border bg-card/60 p-3">
      <div className="grid gap-3 md:grid-cols-[1fr_auto_auto] md:items-end">
        <Field label="Scene search">
          <input
            type="text"
            value={findFilter.q ?? ""}
            onChange={(event) => updateFindFilter({ ...findFilter, q: event.target.value || undefined, page: 1 })}
            placeholder="Optional keyword search"
            className="w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground placeholder:text-muted focus:border-accent focus:outline-none"
          />
        </Field>
        <Field label="Sort">
          <select
            value={findFilter.sort ?? DEFAULT_FIND_FILTER.sort}
            onChange={(event) => updateFindFilter({ ...findFilter, sort: event.target.value, page: 1 })}
            className="w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
          >
            {SCENE_SORT_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </Field>
        <button
          type="button"
          onClick={() => updateFindFilter({ ...findFilter, direction: findFilter.direction === "asc" ? "desc" : "asc", page: 1 })}
          className="inline-flex h-10 items-center justify-center rounded border border-border bg-input px-3 text-secondary transition-colors hover:text-foreground"
          title={findFilter.direction === "asc" ? "Ascending" : "Descending"}
        >
          {findFilter.direction === "desc" ? <ArrowDown className="h-4 w-4" /> : <ArrowUp className="h-4 w-4" />}
        </button>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={() => setFilterOpen(true)}
          className="inline-flex items-center gap-2 rounded border border-border bg-input px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
        >
          <SlidersHorizontal className="h-4 w-4" />
          Scene filters
          {activeCriteriaCount > 0 ? <span className="rounded-full bg-accent px-1.5 py-0.5 text-[10px] font-bold text-white">{activeCriteriaCount}</span> : null}
        </button>
      </div>
      <FilterDialog
        open={filterOpen}
        onClose={() => setFilterOpen(false)}
        criteria={SCENE_CRITERIA}
        activeFilter={objectFilter as Record<string, unknown>}
        onApply={updateObjectFilter}
      />
    </div>
  );
}