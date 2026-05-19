import { useMemo, useState } from "react";
import { useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Plus, X } from "lucide-react";
import { galleries, groups, images, performers, scenes, studios, tags } from "../api/client";
import type { CustomFieldType, Gallery, Group, Image, Performer, Scene, Studio, Tag } from "../api/types";

export type EntityReferenceType = Extract<CustomFieldType, "tag" | "performer" | "studio" | "scene" | "gallery" | "image" | "group">;

export interface EntityReferenceOption {
  id: number;
  label: string;
  secondaryLabel?: string;
}

const REFERENCE_TYPES = new Set<string>(["tag", "performer", "studio", "scene", "gallery", "image", "group"]);

const ENTITY_LABELS: Record<EntityReferenceType, { singular: string; plural: string; sort: string }> = {
  tag: { singular: "tag", plural: "tags", sort: "name" },
  performer: { singular: "performer", plural: "performers", sort: "name" },
  studio: { singular: "studio", plural: "studios", sort: "name" },
  scene: { singular: "scene", plural: "scenes", sort: "title" },
  gallery: { singular: "gallery", plural: "galleries", sort: "title" },
  image: { singular: "image", plural: "images", sort: "title" },
  group: { singular: "group", plural: "groups", sort: "name" },
};

export function isEntityReferenceType(type: string | undefined): type is EntityReferenceType {
  return Boolean(type && REFERENCE_TYPES.has(type));
}

export function getEntityReferenceLabel(type: EntityReferenceType) {
  return ENTITY_LABELS[type];
}

export function parseEntityReferenceIds(value: unknown): number[] {
  const values = Array.isArray(value) ? value : value == null || value === "" ? [] : [value];
  const ids: number[] = [];

  for (const entry of values) {
    const id = parseEntityReferenceId(entry);
    if (id != null && !ids.includes(id)) {
      ids.push(id);
    }
  }

  return ids;
}

export function parseEntityReferenceId(value: unknown): number | undefined {
  if (typeof value === "number" && Number.isInteger(value)) {
    return value;
  }

  if (typeof value === "string" && value.trim() !== "") {
    const parsed = Number(value);
    return Number.isInteger(parsed) ? parsed : undefined;
  }

  if (value && typeof value === "object") {
    const candidate = value as { id?: unknown; value?: unknown };
    return parseEntityReferenceId(candidate.id ?? candidate.value);
  }

  return undefined;
}

export function EntityReferenceSelector({
  entityType,
  value,
  onChange,
  placeholder,
  disabled = false,
  inputClassName,
  excludeIds,
}: {
  entityType: EntityReferenceType;
  value?: number;
  onChange: (value: number | undefined, option?: EntityReferenceOption) => void;
  placeholder?: string;
  disabled?: boolean;
  inputClassName?: string;
  excludeIds?: Iterable<number>;
}) {
  const [searchText, setSearchText] = useState("");
  const trimmedSearch = searchText.trim();
  const labels = getEntityReferenceLabel(entityType);
  const queryClient = useQueryClient();

  const cachedOptions = useMemo(
    () => getCachedEntityReferenceOptions(queryClient, entityType),
    [entityType, queryClient],
  );
  const cachedSearchOptions = useMemo(() => {
    if (!trimmedSearch || cachedOptions == null) return undefined;

    const needle = trimmedSearch.toLowerCase();
    return cachedOptions
      .filter((option) => option.label.toLowerCase().includes(needle))
      .slice(0, 25);
  }, [cachedOptions, trimmedSearch]);

  const { data: searchResults, isLoading } = useQuery({
    queryKey: ["entity-reference-selector", entityType, trimmedSearch],
    queryFn: () => searchEntityReferences(entityType, trimmedSearch),
    enabled: !disabled && trimmedSearch.length >= 1 && cachedSearchOptions == null,
    staleTime: 60_000,
  });

  const searchOptions = cachedSearchOptions ?? searchResults ?? [];
  const selectedSearchOption = searchOptions.find((option) => option.id === value);
  const { data: selectedOption, isLoading: selectedLoading } = useQuery({
    queryKey: ["entity-reference-selector", entityType, "selected", value],
    queryFn: () => getEntityReference(entityType, value as number),
    enabled: typeof value === "number" && !selectedSearchOption,
    staleTime: 60_000,
  });

  const selected = selectedSearchOption ?? selectedOption;
  const excluded = useMemo(() => new Set(excludeIds ?? []), [excludeIds]);
  const visibleResults = useMemo(
    () => searchOptions.filter((option) => option.id !== value && !excluded.has(option.id)),
    [excluded, searchOptions, value],
  );

  return (
    <div className="min-w-0 space-y-2">
      {typeof value === "number" ? (
        <div className="flex flex-wrap gap-1">
          <span className="inline-flex max-w-full items-center gap-1 rounded border border-border bg-card px-2 py-0.5 text-[10px] text-foreground">
            <span className="min-w-0 truncate">{selected?.label ?? (selectedLoading ? `Loading ${labels.singular}...` : `Unavailable ${labels.singular}`)}</span>
            {selected?.secondaryLabel ? <span className="text-muted">{selected.secondaryLabel}</span> : null}
            <button
              type="button"
              onClick={() => onChange(undefined)}
              className="hover:text-red-400"
              aria-label={`Clear selected ${labels.singular}`}
              disabled={disabled}
            >
              <X className="h-2.5 w-2.5" />
            </button>
          </span>
        </div>
      ) : null}

      <input
        type="text"
        value={searchText}
        onChange={(event) => setSearchText(event.target.value)}
        placeholder={placeholder ?? `Search ${labels.plural}...`}
        disabled={disabled}
        className={inputClassName ?? "w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground placeholder:text-muted disabled:opacity-50 focus:border-accent focus:outline-none"}
      />

      {trimmedSearch ? (
        <div className="max-h-40 overflow-y-auto overflow-x-hidden rounded border border-border bg-surface">
          {isLoading ? <div className="px-3 py-2 text-sm text-muted">Loading...</div> : null}
          {!isLoading && visibleResults.length === 0 ? (
            <div className="px-3 py-2 text-sm text-muted">No {labels.plural} found</div>
          ) : null}
          {visibleResults.map((option) => (
            <button
              key={option.id}
              type="button"
              onClick={() => {
                onChange(option.id, option);
                setSearchText("");
              }}
              className="flex w-full min-w-0 items-center justify-between gap-2 px-3 py-2 text-left text-sm text-foreground hover:bg-card"
            >
              <span className="inline-flex min-w-0 items-center gap-2">
                <Plus className="h-3 w-3" />
                <span className="truncate">{option.label}</span>
              </span>
              {option.secondaryLabel ? <span className="shrink-0 text-xs text-muted">{option.secondaryLabel}</span> : null}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

export function EntityReferenceMultiSelector({
  entityType,
  values,
  onChange,
  placeholder,
  emptyMessage,
  disabled = false,
  inputClassName,
  resultsClassName,
  containerClassName,
  excludeIds,
  lockedIds,
}: {
  entityType: EntityReferenceType;
  values: number[];
  onChange: (values: number[]) => void;
  placeholder?: string;
  emptyMessage?: string;
  disabled?: boolean;
  inputClassName?: string;
  resultsClassName?: string;
  containerClassName?: string;
  excludeIds?: Iterable<number>;
  lockedIds?: Iterable<number>;
}) {
  const [searchText, setSearchText] = useState("");
  const trimmedSearch = searchText.trim();
  const labels = getEntityReferenceLabel(entityType);
  const queryClient = useQueryClient();

  const cachedOptions = useMemo(
    () => getCachedEntityReferenceOptions(queryClient, entityType),
    [entityType, queryClient],
  );
  const cachedSearchOptions = useMemo(() => {
    if (!trimmedSearch || cachedOptions == null) return undefined;

    const needle = trimmedSearch.toLowerCase();
    return cachedOptions
      .filter((option) => option.label.toLowerCase().includes(needle))
      .slice(0, 25);
  }, [cachedOptions, trimmedSearch]);

  const { data: searchResults, isLoading } = useQuery({
    queryKey: ["entity-reference-selector", entityType, trimmedSearch],
    queryFn: () => searchEntityReferences(entityType, trimmedSearch),
    enabled: !disabled && trimmedSearch.length >= 1 && cachedSearchOptions == null,
    staleTime: 60_000,
  });

  const searchOptions = cachedSearchOptions ?? searchResults ?? [];
  const selectedOptions = useEntityReferenceOptions(entityType, values, searchOptions);
  const excluded = useMemo(() => new Set(excludeIds ?? []), [excludeIds]);
  const locked = useMemo(() => new Set(lockedIds ?? []), [lockedIds]);
  const visibleResults = useMemo(
    () => searchOptions.filter((option) => !values.includes(option.id) && !excluded.has(option.id)),
    [excluded, searchOptions, values],
  );

  return (
    <div className={containerClassName ?? "space-y-2"}>
      {values.length > 0 ? (
        <div className="flex flex-wrap gap-1">
          {values.map((id) => {
            const option = selectedOptions.get(id);
            const lockedValue = locked.has(id);
            return (
              <span key={id} className="inline-flex items-center gap-1 rounded border border-border bg-card px-2 py-0.5 text-[10px] text-foreground" title={lockedValue ? "Derived tag" : undefined}>
                <span>{option?.label ?? `Loading ${labels.singular}...`}</span>
                {option?.secondaryLabel ? <span className="text-muted">{option.secondaryLabel}</span> : null}
                {!lockedValue ? (
                  <button
                    type="button"
                    onClick={() => onChange(values.filter((value) => value !== id))}
                    className="hover:text-red-400"
                    aria-label={`Remove ${option?.label ?? labels.singular}`}
                    disabled={disabled}
                  >
                    <X className="h-2.5 w-2.5" />
                  </button>
                ) : null}
              </span>
            );
          })}
        </div>
      ) : null}

      <input
        type="text"
        value={searchText}
        onChange={(event) => setSearchText(event.target.value)}
        placeholder={placeholder ?? `Search ${labels.plural}...`}
        disabled={disabled}
        className={inputClassName ?? "w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground placeholder:text-muted disabled:opacity-50 focus:border-accent focus:outline-none"}
      />

      {trimmedSearch ? (
        <div className={resultsClassName ?? "max-h-40 overflow-y-auto rounded border border-border bg-surface"}>
          {isLoading ? <div className="px-3 py-2 text-sm text-muted">Loading...</div> : null}
          {!isLoading && visibleResults.length === 0 ? (
            <div className="px-3 py-2 text-sm text-muted">{emptyMessage ?? `No ${labels.plural} found`}</div>
          ) : null}
          {visibleResults.map((option) => (
            <button
              key={option.id}
              type="button"
              onClick={() => {
                onChange([...values, option.id]);
                setSearchText("");
              }}
              className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm text-foreground hover:bg-card"
            >
              <span className="inline-flex items-center gap-2">
                <Plus className="h-3 w-3" />
                <span>{option.label}</span>
              </span>
              {option.secondaryLabel ? <span className="text-xs text-muted">{option.secondaryLabel}</span> : null}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

export function EntityReferenceValue({ entityType, value }: { entityType: EntityReferenceType; value: unknown }) {
  const ids = useMemo(() => parseEntityReferenceIds(value), [value]);
  const options = useEntityReferenceOptions(entityType, ids);
  const labels = getEntityReferenceLabel(entityType);

  if (ids.length === 0) {
    return null;
  }

  const text = ids
    .map((id) => options.get(id)?.label ?? `Loading ${labels.singular}...`)
    .filter(Boolean)
    .join(", ");

  return <>{text || `Unavailable ${labels.singular}`}</>;
}

function useEntityReferenceOptions(entityType: EntityReferenceType, ids: number[], seedOptions: EntityReferenceOption[] = []) {
  const missingIds = useMemo(
    () => ids.filter((id) => !seedOptions.some((option) => option.id === id)),
    [ids, seedOptions],
  );
  const selectedQueries = useQueries({
    queries: missingIds.map((id) => ({
      queryKey: ["entity-reference-selector", entityType, "selected", id],
      queryFn: () => getEntityReference(entityType, id),
      staleTime: 60_000,
    })),
  });

  return useMemo(() => {
    const optionMap = new Map<number, EntityReferenceOption>();
    for (const option of seedOptions) {
      optionMap.set(option.id, option);
    }

    for (const query of selectedQueries) {
      if (query.data) {
        optionMap.set(query.data.id, query.data);
      }
    }

    return optionMap;
  }, [seedOptions, selectedQueries]);
}

function getCachedEntityReferenceOptions(queryClient: ReturnType<typeof useQueryClient>, entityType: EntityReferenceType): EntityReferenceOption[] | undefined {
  const queryKey = [getEntityReferenceLabel(entityType).plural, "all"];
  const cached = queryClient.getQueryData<unknown>(queryKey);
  if (!Array.isArray(cached)) {
    return undefined;
  }

  switch (entityType) {
    case "tag":
      return cached.map((item) => toTagOption(item as Tag));
    case "performer":
      return cached.map((item) => toPerformerOption(item as Performer));
    case "studio":
      return cached.map((item) => toStudioOption(item as Studio));
    case "scene":
      return cached.map((item) => toSceneOption(item as Scene));
    case "gallery":
      return cached.map((item) => toGalleryOption(item as Gallery));
    case "image":
      return cached.map((item) => toImageOption(item as Image));
    case "group":
      return cached.map((item) => toGroupOption(item as Group));
  }
}

async function searchEntityReferences(entityType: EntityReferenceType, searchText: string): Promise<EntityReferenceOption[]> {
  const query = searchText || undefined;
  const labels = getEntityReferenceLabel(entityType);
  const filter = { q: query, perPage: 25, sort: labels.sort, direction: "asc" as const };

  switch (entityType) {
    case "tag": return (await tags.find(filter)).items.map(toTagOption);
    case "performer": return (await performers.find(filter)).items.map(toPerformerOption);
    case "studio": return (await studios.find(filter)).items.map(toStudioOption);
    case "scene": return (await scenes.find(filter)).items.map(toSceneOption);
    case "gallery": return (await galleries.find(filter)).items.map(toGalleryOption);
    case "image": return (await images.find(filter)).items.map(toImageOption);
    case "group": return (await groups.find(filter)).items.map(toGroupOption);
  }
}

async function getEntityReference(entityType: EntityReferenceType, id: number): Promise<EntityReferenceOption> {
  switch (entityType) {
    case "tag": return toTagOption(await tags.get(id));
    case "performer": return toPerformerOption(await performers.get(id));
    case "studio": return toStudioOption(await studios.get(id));
    case "scene": return toSceneOption(await scenes.get(id));
    case "gallery": return toGalleryOption(await galleries.get(id));
    case "image": return toImageOption(await images.get(id));
    case "group": return toGroupOption(await groups.get(id));
  }
}

function toTagOption(tag: Tag): EntityReferenceOption {
  return { id: tag.id, label: tag.name };
}

function toPerformerOption(performer: Performer): EntityReferenceOption {
  return {
    id: performer.id,
    label: performer.name,
    secondaryLabel: performer.disambiguation ? `(${performer.disambiguation})` : undefined,
  };
}

function toStudioOption(studio: Studio): EntityReferenceOption {
  return { id: studio.id, label: studio.name };
}

function toSceneOption(scene: Scene): EntityReferenceOption {
  const fileName = scene.files?.[0]?.basename;
  return { id: scene.id, label: scene.title?.trim() || scene.code?.trim() || fileName || "Untitled scene" };
}

function toGalleryOption(gallery: Gallery): EntityReferenceOption {
  const fileName = gallery.files?.[0]?.path?.split(/[\\/]/).pop();
  return { id: gallery.id, label: gallery.title?.trim() || gallery.code?.trim() || fileName || "Untitled gallery" };
}

function toImageOption(image: Image): EntityReferenceOption {
  const fileName = image.files?.[0]?.basename;
  return { id: image.id, label: image.title?.trim() || image.code?.trim() || fileName || "Untitled image" };
}

function toGroupOption(group: Group): EntityReferenceOption {
  return { id: group.id, label: group.name };
}