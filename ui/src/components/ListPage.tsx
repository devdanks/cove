import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode, type RefObject } from "react";
import { useQuery } from "@tanstack/react-query";
import { Search, ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight, ArrowDown, ArrowUp, LayoutGrid, List, Columns3, Grid3X3, Share2, FolderTree, ZoomIn, ZoomOut, SlidersHorizontal, Plus, X, Rows3, MonitorPlay, Play, Pause, Shuffle } from "lucide-react";
import type { CriterionModifier, CustomFieldCriterion, CustomFieldDefinition, CustomFieldEntityType, CustomFieldType, FindFilter } from "../api/types";
import { tags as tagsApi, performers as performersApi, studios as studiosApi, groups as groupsApi, tagGroups as tagGroupsApi } from "../api/client";
import { ExtensionSlot } from "../router/RouteRegistry";
import { SavedFilterMenu } from "./SavedFilterMenu";
import { InfiniteScrollSentinel } from "./InfiniteScrollSentinel";
import { FilterDialog, FilterButton, type CriterionDefinition, type FilterDialogCustomSection } from "./FilterDialog";
import { EntityReferenceSelector, getEntityReferenceLabel, isEntityReferenceType, parseEntityReferenceId } from "./EntityReferenceSelector";
import { useResolvedKeybindingOverrides } from "../hooks/useResolvedKeybindingOverrides";
import { useKeySequence } from "../hooks/useKeySequence";
import { resolveKeybinding } from "../keyboard/keybindings";
import { useAppConfig } from "../state/AppConfigContext";
import { useCustomFieldDefinitions } from "../hooks/useCustomFieldDefinitions";
import { clampCardSizeLevel, useEntityCardSize } from "../hooks/useEntityCardSize";
import { reshuffleRandomSort, withSeededRandomSort } from "../utils/seededRandomSort";
import { trackInteraction } from "../utils/interactionTracking";
import { LIST_PER_PAGE_OPTIONS, toolbarIconButtonClass, toolbarSegmentClass, toolbarSelectClass } from "./listToolbarStyles";
import { ListPageCardSizeContext } from "./ListPageCardSizeContext";

export type DisplayMode = "grid" | "list" | "wall" | "tagger" | "graph" | "byGroup" | "feed" | "vertical";

interface ListPageProps {
  title: string;
  pageKey?: string;
  filter: FindFilter;
  onFilterChange: (f: FindFilter) => void;
  totalCount: number;
  isLoading: boolean;
  children: ReactNode;
  sortOptions?: { value: string; label: string }[];
  displayMode?: DisplayMode;
  onDisplayModeChange?: (mode: DisplayMode) => void;
  availableDisplayModes?: DisplayMode[];
  allowInfinitePageSize?: boolean;
  infinitePageSizeOnly?: boolean;
  selectedIds?: Set<string | number>;
  onSelectAll?: () => void;
  onSelectAllMatching?: () => void;
  onSelectNone?: () => void;
  onInvertSelection?: () => void;
  selectionActions?: ReactNode;
  selectAllLabel?: string;
  selectAllPending?: boolean;
  selectAllMatchingLabel?: string;
  selectAllMatchingPending?: boolean;
  metadataByline?: ReactNode;
  onNew?: () => void;
  renderOperations?: () => ReactNode;
  filterMode?: string;
  searchMode?: string;
  searchModes?: { value: string; label: string; title?: string }[];
  searchPlaceholder?: string;
  onSearchModeChange?: (mode: string) => void;
  // Advanced filtering
  criteriaDefinitions?: CriterionDefinition[];
  objectFilter?: Record<string, unknown>;
  onObjectFilterChange?: (filter: Record<string, unknown>) => void;
  wallColumnCount?: number;
  onWallColumnCountChange?: (count: number) => void;
  autoScrollContainerRef?: RefObject<HTMLElement | null>;
  infiniteScroll?: {
    hasNextPage?: boolean;
    isFetchingNextPage?: boolean;
    onLoadMore: () => void;
    loadedCount: number;
    totalCount: number;
  };
  showAutoScrollControls?: boolean;
  showPagingControls?: boolean;
  customFilterSections?: FilterDialogCustomSection[];
  showClearAllObjectFilters?: boolean;
}

const PER_PAGE_OPTIONS = LIST_PER_PAGE_OPTIONS;
const DEFAULT_ZOOM_LEVEL = 1;
const LIST_SEARCH_DEBOUNCE_MS = 350;

const CUSTOM_FIELD_ENTITY_BY_FILTER_MODE: Record<string, CustomFieldEntityType> = {
  scenes: "scene",
  audios: "audio",
  texts: "text",
  performers: "performer",
  tags: "tag",
  studios: "studio",
  galleries: "gallery",
  images: "image",
  groups: "group",
};

const CUSTOM_FIELD_MODIFIER_LABELS: Record<CriterionModifier, string> = {
  EQUALS: "Equals",
  NOT_EQUALS: "Does Not Equal",
  GREATER_THAN: ">",
  LESS_THAN: "<",
  INCLUDES: "Includes",
  EXCLUDES: "Excludes",
  INCLUDES_ALL: "Includes All",
  EXCLUDES_ALL: "Excludes All",
  IS_NULL: "Is Null",
  NOT_NULL: "Not Null",
  BETWEEN: "Between",
  NOT_BETWEEN: "Not Between",
  MATCHES_REGEX: "Regex",
  NOT_MATCHES_REGEX: "Not Regex",
};

const TEXT_CUSTOM_FIELD_MODIFIERS: CriterionModifier[] = ["EQUALS", "NOT_EQUALS", "INCLUDES", "EXCLUDES", "IS_NULL", "NOT_NULL"];
const ORDERED_CUSTOM_FIELD_MODIFIERS: CriterionModifier[] = ["EQUALS", "NOT_EQUALS", "GREATER_THAN", "LESS_THAN", "BETWEEN", "NOT_BETWEEN", "IS_NULL", "NOT_NULL"];
const BOOLEAN_CUSTOM_FIELD_MODIFIERS: CriterionModifier[] = ["EQUALS", "NOT_EQUALS", "IS_NULL", "NOT_NULL"];
const REFERENCE_CUSTOM_FIELD_MODIFIERS: CriterionModifier[] = ["INCLUDES", "EXCLUDES", "IS_NULL", "NOT_NULL"];

function getDefaultCustomFieldModifier(type: CustomFieldType): CriterionModifier {
  return isEntityReferenceType(type) ? "INCLUDES" : "EQUALS";
}

function normalizeCustomFieldCriteria(value: unknown): CustomFieldCriterion[] {
  return Array.isArray(value) ? value.filter((item): item is CustomFieldCriterion => Boolean(item && typeof item === "object")) : [];
}

function isCustomFieldCriterionActive(value: CustomFieldCriterion | undefined) {
  if (!value?.key) return false;
  const modifier = value.modifier ?? "EQUALS";
  if (modifier === "IS_NULL" || modifier === "NOT_NULL") return true;
  if (modifier === "BETWEEN" || modifier === "NOT_BETWEEN") {
    return String(value.value ?? "").trim() !== "" && String(value.value2 ?? "").trim() !== "";
  }
  return String(value.value ?? "").trim() !== "";
}

function getCustomFieldModifiers(type: CustomFieldType) {
  switch (type) {
    case "number":
    case "date":
    case "timestamp":
    case "duration":
    case "percent":
      return ORDERED_CUSTOM_FIELD_MODIFIERS;
    case "boolean":
      return BOOLEAN_CUSTOM_FIELD_MODIFIERS;
    case "tag":
    case "performer":
    case "studio":
    case "scene":
    case "gallery":
    case "image":
    case "group":
      return REFERENCE_CUSTOM_FIELD_MODIFIERS;
    default:
      return TEXT_CUSTOM_FIELD_MODIFIERS;
  }
}

function formatCustomFieldCriterionValue(
  definition: CustomFieldDefinition | undefined,
  criterion: CustomFieldCriterion,
  valueKey: "value" | "value2",
) {
  const rawValue = criterion[valueKey];
  if (String(rawValue ?? "").trim() === "") {
    return "";
  }

  if (definition && isEntityReferenceType(definition.type)) {
    const displayValue = valueKey === "value2" ? criterion.displayValue2 : criterion.displayValue;
    return displayValue || `Selected ${getEntityReferenceLabel(definition.type).singular}`;
  }

  return String(rawValue);
}

function createCustomFieldFilterSection(definitions: CustomFieldDefinition[]): FilterDialogCustomSection {
  const normalizeCriterion = (criterion: CustomFieldCriterion): CustomFieldCriterion => {
    const definition = definitions.find((candidate) => candidate.key === criterion.key);
    if (!definition) return criterion;
    const availableModifiers = getCustomFieldModifiers(definition.type);
    const defaultModifier = getDefaultCustomFieldModifier(definition.type);
    const modifier = availableModifiers.includes(criterion.modifier ?? defaultModifier) ? (criterion.modifier ?? defaultModifier) : defaultModifier;
    return { ...criterion, type: definition.type, modifier };
  };

  return {
    id: "custom-fields",
    label: "Custom Fields",
    filterKey: "customFieldCriteria",
    defaultValue: [] satisfies CustomFieldCriterion[],
    isActive: (value) => normalizeCustomFieldCriteria(value).map(normalizeCriterion).some(isCustomFieldCriterionActive),
    shouldKeepDraft: (value) => normalizeCustomFieldCriteria(value).some((criterion) => Boolean(criterion.key)),
    sanitize: (value) => normalizeCustomFieldCriteria(value).map(normalizeCriterion).filter(isCustomFieldCriterionActive),
    summarize: (value) => {
      const activeCriteria = normalizeCustomFieldCriteria(value).map(normalizeCriterion).filter(isCustomFieldCriterionActive);
      if (activeCriteria.length === 0) return "";
      return activeCriteria.map((criterion) => {
        const definition = definitions.find((candidate) => candidate.key === criterion.key);
        const label = definition?.label || criterion.key;
        const modifier = CUSTOM_FIELD_MODIFIER_LABELS[criterion.modifier ?? "EQUALS"];
        if (criterion.modifier === "IS_NULL" || criterion.modifier === "NOT_NULL") {
          return `${label} ${modifier}`;
        }

        if (criterion.modifier === "BETWEEN" || criterion.modifier === "NOT_BETWEEN") {
          return `${label} ${modifier} ${formatCustomFieldCriterionValue(definition, criterion, "value")} and ${formatCustomFieldCriterionValue(definition, criterion, "value2")}`;
        }

        return `${label} ${modifier} ${formatCustomFieldCriterionValue(definition, criterion, "value")}`;
      }).join(", ");
    },
    renderEditor: (value, onChange) => (
      <CustomFieldCriteriaEditor
        definitions={definitions}
        value={normalizeCustomFieldCriteria(value)}
        onChange={onChange}
      />
    ),
  };
}

function CustomFieldCriteriaEditor({
  definitions,
  value,
  onChange,
}: {
  definitions: CustomFieldDefinition[];
  value: CustomFieldCriterion[];
  onChange: (value: CustomFieldCriterion[]) => void;
}) {
  const firstDefinition = definitions[0];
  const rows = value.length > 0 ? value : [];
  const setRow = (index: number, nextCriterion: CustomFieldCriterion) => {
    onChange(rows.map((criterion, candidateIndex) => candidateIndex === index ? nextCriterion : criterion));
  };
  const removeRow = (index: number) => onChange(rows.filter((_, candidateIndex) => candidateIndex !== index));
  const addRow = () => {
    if (!firstDefinition) return;
    onChange([...rows, { key: firstDefinition.key, type: firstDefinition.type, value: "", modifier: getDefaultCustomFieldModifier(firstDefinition.type) }]);
  };

  return (
    <div className="space-y-2">
      {rows.map((criterion, index) => {
        const definition = definitions.find((candidate) => candidate.key === criterion.key) ?? firstDefinition;
        if (!definition) return null;
        const availableModifiers = getCustomFieldModifiers(definition.type);
        const defaultModifier = getDefaultCustomFieldModifier(definition.type);
        const modifier = availableModifiers.includes(criterion.modifier ?? defaultModifier) ? (criterion.modifier ?? defaultModifier) : defaultModifier;
        const valueDisabled = modifier === "IS_NULL" || modifier === "NOT_NULL";

        return (
          <div key={`${criterion.key}-${index}`} className="min-w-0 rounded border border-border bg-background p-3">
            <div className="grid min-w-0 gap-3 md:grid-cols-[minmax(10rem,1fr)_minmax(9rem,0.75fr)] xl:grid-cols-[minmax(12rem,1.1fr)_minmax(9rem,0.6fr)_minmax(18rem,2fr)_auto] xl:items-start">
              <label className="block min-w-0 text-xs text-muted">
                Field
                <select
                  value={criterion.key}
                  onChange={(event) => {
                    const nextDefinition = definitions.find((candidate) => candidate.key === event.target.value) ?? definition;
                    setRow(index, { key: nextDefinition.key, type: nextDefinition.type, value: "", modifier: getDefaultCustomFieldModifier(nextDefinition.type) });
                  }}
                  className="mt-1 w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                >
                  {definitions.map((option) => (
                    <option key={option.key} value={option.key}>{option.label || option.key}</option>
                  ))}
                </select>
              </label>
              <label className="block min-w-0 text-xs text-muted">
                Match
                <select
                  value={modifier}
                  onChange={(event) => setRow(index, { ...criterion, modifier: event.target.value as CriterionModifier })}
                  className="mt-1 w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                >
                  {availableModifiers.map((option) => (
                    <option key={option} value={option}>{CUSTOM_FIELD_MODIFIER_LABELS[option]}</option>
                  ))}
                </select>
              </label>
              <div className={`min-w-0 ${modifier === "BETWEEN" || modifier === "NOT_BETWEEN" ? "grid gap-2 sm:grid-cols-2" : ""}`}>
                <CustomFieldValueInput
                  definition={definition}
                  disabled={valueDisabled}
                  value={criterion.value ?? ""}
                  onChange={(nextValue, displayValue) => setRow(index, { ...criterion, modifier, type: definition.type, value: nextValue, displayValue })}
                />
                {modifier === "BETWEEN" || modifier === "NOT_BETWEEN" ? (
                  <CustomFieldValueInput
                    definition={definition}
                    disabled={valueDisabled}
                    label="And"
                    value={criterion.value2 ?? ""}
                    onChange={(nextValue, displayValue) => setRow(index, { ...criterion, modifier, type: definition.type, value2: nextValue, displayValue2: displayValue })}
                  />
                ) : null}
              </div>
              <button
                type="button"
                onClick={() => removeRow(index)}
                aria-label="Remove custom field filter"
                className="justify-self-start rounded border border-border p-2 text-muted hover:border-red-400 hover:text-red-300 xl:mt-6"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        );
      })}
      <button
        type="button"
        onClick={addRow}
        className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-secondary hover:border-accent hover:text-foreground"
      >
        <Plus className="h-3.5 w-3.5" />
        Add custom field filter
      </button>
    </div>
  );
}

function CustomFieldValueInput({
  definition,
  disabled,
  label = "Value",
  value,
  onChange,
}: {
  definition: CustomFieldDefinition;
  disabled: boolean;
  label?: string;
  value: string;
  onChange: (value: string, displayValue?: string) => void;
}) {
  if (isEntityReferenceType(definition.type)) {
    const selectedId = parseEntityReferenceId(value);
    const labels = getEntityReferenceLabel(definition.type);
    return (
      <label className="block min-w-0 text-xs text-muted">
        {label}
        <div className="mt-1 min-w-0">
          <EntityReferenceSelector
            entityType={definition.type}
            value={selectedId}
            disabled={disabled}
            placeholder={`Search ${labels.plural}...`}
            inputClassName="w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground placeholder:text-muted disabled:opacity-50 focus:border-accent focus:outline-none"
            onChange={(nextId, option) => onChange(nextId == null ? "" : String(nextId), option?.label)}
          />
        </div>
      </label>
    );
  }

  if (definition.type === "boolean") {
    return (
      <label className="block text-xs text-muted">
        {label}
        <select
          disabled={disabled}
          value={value || "true"}
          onChange={(event) => onChange(event.target.value)}
          className="mt-1 w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground disabled:opacity-50 focus:border-accent focus:outline-none"
        >
          <option value="true">True</option>
          <option value="false">False</option>
        </select>
      </label>
    );
  }

  if (definition.type === "enum" && definition.options.length > 0) {
    return (
      <label className="block text-xs text-muted">
        {label}
        <select
          disabled={disabled}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          className="mt-1 w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground disabled:opacity-50 focus:border-accent focus:outline-none"
        >
          <option value="">Select</option>
          {definition.options.map((option) => (
            <option key={option} value={option}>{option}</option>
          ))}
        </select>
      </label>
    );
  }

  const inputType: Partial<Record<CustomFieldType, string>> = {
    text: "text",
    longText: "text",
    number: "number",
    boolean: "text",
    date: "date",
    timestamp: "datetime-local",
    url: "url",
    enum: "text",
    duration: "number",
    percent: "number",
  };

  return (
    <label className="block text-xs text-muted">
      {label}
      <input
        type={inputType[definition.type] ?? "text"}
        disabled={disabled}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="mt-1 w-full rounded border border-border bg-input px-3 py-2 text-sm text-foreground disabled:opacity-50 focus:border-accent focus:outline-none"
      />
    </label>
  );
}

const CHIP_MODIFIER_LABELS: Record<string, string> = {
  EQUALS: "=",
  NOT_EQUALS: "≠",
  GREATER_THAN: ">",
  LESS_THAN: "<",
  INCLUDES: "Includes",
  EXCLUDES: "Excludes",
  INCLUDES_ALL: "Includes All",
  EXCLUDES_ALL: "Excludes All",
  IS_NULL: "Is Null",
  NOT_NULL: "Not Null",
  BETWEEN: "Between",
  NOT_BETWEEN: "Not Between",
  MATCHES_REGEX: "Regex",
  NOT_MATCHES_REGEX: "Not Regex",
};

function formatChipScalar(value: unknown): string {
  if (typeof value === "boolean") {
    return value ? "Yes" : "No";
  }

  if (value == null) {
    return "";
  }

  if (typeof value === "object") {
    const candidate = value as { label?: string; name?: string; title?: string; value?: string | number };
    return candidate.label ?? candidate.name ?? candidate.title ?? String(candidate.value ?? "");
  }

  return String(value);
}

function formatChipEntityId(value: unknown, nameMap?: Map<number, string>): string {
  if (typeof value === "number") {
    return nameMap?.get(value) ?? "Unavailable item";
  }

  if (value && typeof value === "object") {
    const candidate = value as { id?: number | string; label?: string; name?: string; title?: string };
    if (candidate.label ?? candidate.name ?? candidate.title) {
      return (candidate.label ?? candidate.name ?? candidate.title)!;
    }
    if (candidate.id != null && typeof candidate.id === "number") {
      return nameMap?.get(candidate.id) ?? "Unavailable item";
    }
    return candidate.id != null ? "Unavailable item" : "";
  }

  return String(value ?? "");
}

function formatDurationChipSeconds(value: unknown): string {
  if (typeof value !== "number" || Number.isNaN(value)) {
    return "";
  }

  const totalSeconds = Math.max(0, Math.round(value));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`
    : `${minutes}:${String(seconds).padStart(2, "0")}`;
}

function formatPercentChipValue(value: unknown): string {
  return typeof value === "number" && Number.isFinite(value) ? `${Number(value.toFixed(1))}%` : "";
}

function formatFilterChipValue(def: CriterionDefinition | undefined, value: unknown, nameMap?: Map<number, string>): string {
  if (Array.isArray(value)) {
    return value.map((item) => formatChipScalar(item)).join(", ");
  }

  if (!value || typeof value !== "object") {
    return String(value ?? "");
  }

  const criterion = value as {
    value?: unknown;
    value2?: unknown;
    tagId?: number;
    unit?: string;
    clauses?: Array<{ tagId?: number; value?: unknown; value2?: unknown; modifier?: string; unit?: string }>;
    excludes?: unknown[];
    modifier?: string;
    depth?: number;
    _names?: Record<string, string>;
  };

  const modifier = criterion.modifier ? CHIP_MODIFIER_LABELS[criterion.modifier] ?? criterion.modifier : "";

  // Merge _names (embedded by filter editor) with nameMap (from entity queries) for best coverage
  const embeddedNames = criterion._names;
  const resolveEntityName = (id: unknown): string => {
    if (typeof id === "number") {
      // First check embedded names (always available), then nameMap (from queries)
      const name = embeddedNames?.[String(id)] ?? nameMap?.get(id);
      return name ?? "Unavailable item";
    }
    return formatChipEntityId(id, nameMap);
  };

  if (def?.type === "tagDuration") {
    const clauses = Array.isArray(criterion.clauses) && criterion.clauses.length > 0 ? criterion.clauses : [criterion];
    const parts = clauses.map((clause) => {
      if (!clause.tagId || typeof clause.value !== "number") {
        return "";
      }

      const tagName = resolveEntityName(clause.tagId);
      const clauseModifier = clause.modifier ? CHIP_MODIFIER_LABELS[clause.modifier] ?? clause.modifier : "";
      const formatDurationValue = (clause.unit ?? "seconds") === "percent" ? formatPercentChipValue : formatDurationChipSeconds;
      const valueText = formatDurationValue(clause.value);
      const value2Text = formatDurationValue(clause.value2);

      if (clause.modifier === "BETWEEN" || clause.modifier === "NOT_BETWEEN") {
        return `${tagName} ${clauseModifier} ${valueText} and ${value2Text}`.trim();
      }

      return `${tagName} ${clauseModifier} ${valueText}`.trim();
    }).filter(Boolean);

    return parts.join(" · ") || JSON.stringify(value);
  }

  if (def?.type === "multiId") {
    const included = Array.isArray(criterion.value)
      ? criterion.value.map((item) => resolveEntityName(item)).filter(Boolean).join(", ")
      : "";
    const excluded = Array.isArray(criterion.excludes)
      ? criterion.excludes.map((item) => resolveEntityName(item)).filter(Boolean).join(", ")
      : "";

    const parts = [
      included ? `${modifier} ${included}`.trim() : "",
      excluded ? `Except ${excluded}` : "",
      criterion.depth === -1 ? "with sub-tags" : "",
    ].filter(Boolean);

    return parts.join(" · ");
  }

  if (criterion.modifier === "IS_NULL" || criterion.modifier === "NOT_NULL") {
    return modifier;
  }

  const valueText = formatChipScalar(criterion.value);
  const value2Text = formatChipScalar(criterion.value2);

  if (criterion.modifier === "BETWEEN" || criterion.modifier === "NOT_BETWEEN") {
    return `${modifier} ${valueText} and ${value2Text}`.trim();
  }

  if (valueText) {
    return `${modifier} ${valueText}`.trim();
  }

  return JSON.stringify(value);
}

export function ListPage({
  title,
  pageKey,
  filter,
  onFilterChange,
  totalCount,
  isLoading,
  children,
  sortOptions,
  displayMode,
  onDisplayModeChange,
  availableDisplayModes,
  allowInfinitePageSize = false,
  infinitePageSizeOnly = false,
  selectedIds,
  onSelectAll,
  onSelectAllMatching,
  onSelectNone,
  onInvertSelection,
  selectionActions,
  selectAllLabel = "Select all",
  selectAllPending = false,
  selectAllMatchingLabel = "Select all matching",
  selectAllMatchingPending = false,
  metadataByline,
  onNew,
  renderOperations,
  filterMode,
  searchMode,
  searchModes,
  searchPlaceholder,
  onSearchModeChange,
  criteriaDefinitions,
  objectFilter,
  onObjectFilterChange,
  wallColumnCount,
  onWallColumnCountChange,
  autoScrollContainerRef,
  infiniteScroll,
  showAutoScrollControls = true,
  showPagingControls = true,
  customFilterSections,
  showClearAllObjectFilters = true,
}: ListPageProps) {
  const [searchText, setSearchText] = useState(filter.q ?? "");
  const [filterDialogOpen, setFilterDialogOpen] = useState(false);
  const [filterDialogPreselect, setFilterDialogPreselect] = useState<string | undefined>();
  const cardSizeEntityType = filterMode ?? pageKey;
  const [zoomLevel, setZoomLevel] = useEntityCardSize(cardSizeEntityType, pageKey, DEFAULT_ZOOM_LEVEL); // 0-5 range: 0=smallest (240px), 5=largest (540px)
  const cardMinWidthPx = Math.round(240 + zoomLevel * 60);
  const [autoScrollEnabled, setAutoScrollEnabled] = useState(false);
  const [autoScrollSpeed, setAutoScrollSpeed] = useState(120);
  const [autoScrollControlsAwake, setAutoScrollControlsAwake] = useState(true);
  const restoredPrefsRef = useRef(false);
  const { config } = useAppConfig();
  const keybindingOverrides = useResolvedKeybindingOverrides();
  const customFieldEntityType = filterMode ? CUSTOM_FIELD_ENTITY_BY_FILTER_MODE[filterMode] : undefined;
  const { data: customFieldDefinitions = [] } = useCustomFieldDefinitions(customFieldEntityType, Boolean(customFieldEntityType));
  const generatedCustomFieldSection = useMemo(() => {
    const definitions = customFieldDefinitions.filter((definition) => definition.filterable);

    return definitions.length > 0 ? createCustomFieldFilterSection(definitions) : undefined;
  }, [customFieldDefinitions]);
  const mergedCustomFilterSections = useMemo(
    () => generatedCustomFieldSection ? [...(customFilterSections ?? []), generatedCustomFieldSection] : customFilterSections,
    [customFilterSections, generatedCustomFieldSection]
  );

  // Determine which entity types are used in active filters for name resolution
  const activeEntityTypes = useMemo(() => {
    if (!objectFilter || !criteriaDefinitions) return new Set<string>();
    const types = new Set<string>();
    for (const key of Object.keys(objectFilter)) {
      const def = criteriaDefinitions.find((d) => d.id === key || d.filterKey === key);
      if ((def?.type === "multiId" || def?.type === "tagDuration") && def.entityType) types.add(def.entityType);
    }
    return types;
  }, [objectFilter, criteriaDefinitions]);

  // Fetch entity names for active multiId filters (uses same cache key as FilterDialog)
  const { data: tagEntities } = useQuery({
    queryKey: ["tags", "all"],
    queryFn: async () => (await tagsApi.find({ perPage: 5000, sort: "name", direction: "asc" })).items,
    staleTime: 60000,
    enabled: activeEntityTypes.has("tags"),
  });
  const { data: performerEntities } = useQuery({
    queryKey: ["performers", "all"],
    queryFn: async () => (await performersApi.find({ perPage: 5000, sort: "name", direction: "asc" })).items,
    staleTime: 60000,
    enabled: activeEntityTypes.has("performers"),
  });
  const { data: studioEntities } = useQuery({
    queryKey: ["studios", "all"],
    queryFn: async () => (await studiosApi.find({ perPage: 5000, sort: "name", direction: "asc" })).items,
    staleTime: 60000,
    enabled: activeEntityTypes.has("studios"),
  });
  const { data: groupEntities } = useQuery({
    queryKey: ["groups", "all"],
    queryFn: async () => (await groupsApi.find({ perPage: 5000, sort: "name", direction: "asc" })).items,
    staleTime: 60000,
    enabled: activeEntityTypes.has("groups"),
  });
  const { data: tagGroupEntities } = useQuery({
    queryKey: ["tag-groups"],
    queryFn: tagGroupsApi.list,
    staleTime: 60000,
    enabled: activeEntityTypes.has("tagGroups"),
  });

  // Build name maps per entity type
  const entityNameMaps = useMemo(() => {
    const maps: Record<string, Map<number, string>> = {};
    const buildMap = (entities: any[] | undefined) => {
      const m = new Map<number, string>();
      if (entities) for (const e of entities) m.set(e.id, e.name || e.title || "Untitled item");
      return m;
    };
    if (tagEntities) maps.tags = buildMap(tagEntities);
    if (performerEntities) maps.performers = buildMap(performerEntities);
    if (studioEntities) maps.studios = buildMap(studioEntities);
    if (groupEntities) maps.groups = buildMap(groupEntities);
    if (tagGroupEntities) maps.tagGroups = buildMap(tagGroupEntities);
    return maps;
  }, [tagEntities, performerEntities, studioEntities, groupEntities, tagGroupEntities]);
  const perPage = filter.perPage ?? 25;
  const infinitePageSize = allowInfinitePageSize && (perPage === 0 || infinitePageSizeOnly);
  const perPageOptions = useMemo(() => {
    if (infinitePageSizeOnly) {
      return [];
    }

    if (infinitePageSize || PER_PAGE_OPTIONS.includes(perPage)) {
      return PER_PAGE_OPTIONS;
    }

    return [...PER_PAGE_OPTIONS, perPage].sort((left, right) => left - right);
  }, [infinitePageSize, infinitePageSizeOnly, perPage]);
  const page = filter.page ?? 1;
  const effectivePerPage = infinitePageSize ? Math.max(totalCount, 1) : perPage;
  const totalPages = Math.max(1, Math.ceil(totalCount / effectivePerPage));
  const start = totalCount > 0 ? (infinitePageSize ? 1 : (page - 1) * effectivePerPage + 1) : 0;
  const end = infinitePageSize ? totalCount : Math.min(page * effectivePerPage, totalCount);
  const sortedSortOptions = useMemo(() => {
    const customSortOptions = customFieldDefinitions
      .filter((definition) => definition.sortable)
      .map((definition) => ({ value: `custom:${definition.type}:${definition.key}`, label: `Custom: ${definition.label || definition.key}` }));
    const mergedOptions = [...(sortOptions ?? []), ...customSortOptions];
    return mergedOptions.length > 0 ? mergedOptions.sort((left, right) => left.label.localeCompare(right.label)) : undefined;
  }, [customFieldDefinitions, sortOptions]);
  const slotContext = { pageKey, title, filter, onFilterChange, totalCount, isLoading };
  const selecting = selectedIds && selectedIds.size > 0;
  const showSelectionBar = Boolean(selectedIds && selecting);
  const showInfiniteAutoScrollControls = infinitePageSize && showAutoScrollControls;
  const contentOwnsInfiniteLoading = infinitePageSize && (displayMode === "grid" || displayMode === "wall" || displayMode === "feed" || displayMode === "vertical");
  const wakeAutoScrollControls = useCallback(() => setAutoScrollControlsAwake(true), []);

  useEffect(() => {
    if ((!infinitePageSize || !showAutoScrollControls) && autoScrollEnabled) {
      setAutoScrollEnabled(false);
    }
  }, [autoScrollEnabled, infinitePageSize, showAutoScrollControls]);

  useEffect(() => {
    if (!showInfiniteAutoScrollControls || !autoScrollControlsAwake) {
      return;
    }

    const timeoutId = window.setTimeout(() => setAutoScrollControlsAwake(false), autoScrollEnabled ? 2600 : 3600);
    return () => window.clearTimeout(timeoutId);
  }, [autoScrollControlsAwake, autoScrollEnabled, autoScrollSpeed, showInfiniteAutoScrollControls]);

  useEffect(() => {
    if (!showInfiniteAutoScrollControls || !autoScrollEnabled) {
      return;
    }

    const container = autoScrollContainerRef?.current;
    const previousSnapType = container?.style.scrollSnapType;
    let previousTime = performance.now();
    let pendingDistance = 0;
    let frameId = 0;

    if (container) {
      container.style.scrollSnapType = "none";
    }

    const step = (currentTime: number) => {
      const deltaSeconds = Math.min(0.05, Math.max(0, (currentTime - previousTime) / 1000));
      previousTime = currentTime;
      pendingDistance += autoScrollSpeed * deltaSeconds;
      const distance = Math.floor(pendingDistance);

      if (distance < 1) {
        frameId = window.requestAnimationFrame(step);
        return;
      }

      pendingDistance -= distance;

      if (container) {
        const maxScrollTop = Math.max(0, container.scrollHeight - container.clientHeight);
        if (container.scrollTop < maxScrollTop - 1) {
          container.scrollTop = Math.min(maxScrollTop, container.scrollTop + distance);
        }
      } else {
        const scrollingElement = document.scrollingElement ?? document.documentElement;
        const maxScrollTop = Math.max(0, scrollingElement.scrollHeight - window.innerHeight);
        if (window.scrollY < maxScrollTop - 1) {
          window.scrollTo({ top: Math.min(maxScrollTop, window.scrollY + distance), behavior: "auto" });
        }
      }

      frameId = window.requestAnimationFrame(step);
    };

    frameId = window.requestAnimationFrame(step);

    return () => {
      window.cancelAnimationFrame(frameId);
      if (container) {
        container.style.scrollSnapType = previousSnapType ?? "";
      }
    };
  }, [autoScrollContainerRef, autoScrollEnabled, autoScrollSpeed, showInfiniteAutoScrollControls]);

  useEffect(() => {
    if (!pageKey || restoredPrefsRef.current) {
      return;
    }

    restoredPrefsRef.current = true;

    try {
      const raw = localStorage.getItem(`cove-list-prefs-${pageKey}`);
      if (!raw) {
        return;
      }

      const parsed = JSON.parse(raw) as { perPage?: number; wallColumnCount?: number };

      if (typeof parsed.wallColumnCount === "number" && onWallColumnCountChange) {
        onWallColumnCountChange(Math.min(12, Math.max(2, parsed.wallColumnCount)));
      }

      const hasPerPageOverride = new URLSearchParams(window.location.search).has("perPage");
      const persistedPerPageAllowed = typeof parsed.perPage === "number" && (parsed.perPage > 0 || (allowInfinitePageSize && parsed.perPage === 0));
      if (!hasPerPageOverride && persistedPerPageAllowed && parsed.perPage !== perPage) {
        onFilterChange({ ...filter, perPage: parsed.perPage, page: 1 });
      }
    } catch {
      // Ignore invalid persisted list preferences.
    }
  }, [allowInfinitePageSize, filter, onFilterChange, onWallColumnCountChange, pageKey, perPage]);

  useEffect(() => {
    if (!pageKey) {
      return;
    }

    localStorage.setItem(
      `cove-list-prefs-${pageKey}`,
      JSON.stringify({ perPage, wallColumnCount })
    );
  }, [pageKey, perPage, wallColumnCount]);

  useEffect(() => {
    setSearchText(filter.q ?? "");
  }, [filter.q]);

  const commitSearch = useCallback((rawSearchText: string, source: "debounce" | "submit" | "clear") => {
    const normalizedSearch = rawSearchText.trim();
    const currentSearch = (filter.q ?? "").trim();
    if (normalizedSearch === currentSearch) {
      return;
    }

    if (pageKey && normalizedSearch.length > 0 && source !== "clear") {
      trackInteraction({
        hostType: "collection",
        kind: "searchQuery",
        meta: {
          pageKey,
          query: normalizedSearch,
          source: "listPageToolbar",
          activeFilterCount: Object.keys(objectFilter ?? {}).length,
        },
      });
    }

    onFilterChange({ ...filter, q: normalizedSearch || undefined, page: 1 });
  }, [filter, objectFilter, onFilterChange, pageKey]);

  useEffect(() => {
    if (searchText.trim() === (filter.q ?? "").trim()) {
      return;
    }

    const timeout = window.setTimeout(() => commitSearch(searchText, "debounce"), LIST_SEARCH_DEBOUNCE_MS);
    return () => window.clearTimeout(timeout);
  }, [commitSearch, filter.q, searchText]);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    commitSearch(searchText, "submit");
  };

  const goTo = useCallback(
    (p: number) => onFilterChange({ ...filter, page: Math.max(1, Math.min(totalPages, p)) }),
    [filter, onFilterChange, totalPages]
  );

  // List-page keyboard shortcuts
  const listBindings = useMemo(() => [
    // "/" focuses search
    { keys: resolveKeybinding(keybindingOverrides, "list.search", "/"), action: () => { document.querySelector<HTMLInputElement>("input[data-list-search='true']")?.focus(); } },
    // View switching
    ...(onDisplayModeChange && availableDisplayModes ? [
      ...(availableDisplayModes.includes("grid") ? [{ keys: resolveKeybinding(keybindingOverrides, "list.view.grid", "v g"), action: () => onDisplayModeChange("grid") }] : []),
      ...(availableDisplayModes.includes("list") ? [{ keys: resolveKeybinding(keybindingOverrides, "list.view.list", "v l"), action: () => onDisplayModeChange("list") }] : []),
      ...(availableDisplayModes.includes("wall") ? [{ keys: resolveKeybinding(keybindingOverrides, "list.view.wall", "v w"), action: () => onDisplayModeChange("wall") }] : []),
      ...(availableDisplayModes.includes("tagger") ? [{ keys: resolveKeybinding(keybindingOverrides, "list.view.tagger", "v t"), action: () => onDisplayModeChange("tagger") }] : []),
      ...(availableDisplayModes.includes("graph") ? [{ keys: resolveKeybinding(keybindingOverrides, "list.view.graph", "v h"), action: () => onDisplayModeChange("graph") }] : []),
      ...(availableDisplayModes.includes("byGroup") ? [{ keys: resolveKeybinding(keybindingOverrides, "list.view.group", "v b"), action: () => onDisplayModeChange("byGroup") }] : []),
      ...(availableDisplayModes.includes("feed") ? [{ keys: resolveKeybinding(keybindingOverrides, "list.view.feed", "v f"), action: () => onDisplayModeChange("feed") }] : []),
      ...(availableDisplayModes.includes("vertical") ? [{ keys: resolveKeybinding(keybindingOverrides, "list.view.vertical", "v k"), action: () => onDisplayModeChange("vertical") }] : []),
    ] : []),
    // Selection
    ...(onSelectAll ? [{ keys: resolveKeybinding(keybindingOverrides, "list.select.all", "s a"), action: onSelectAll }] : []),
    ...(onSelectNone ? [{ keys: resolveKeybinding(keybindingOverrides, "list.select.none", "s n"), action: onSelectNone }] : []),
    ...(onInvertSelection ? [{ keys: resolveKeybinding(keybindingOverrides, "list.select.invert", "s i"), action: onInvertSelection }] : []),
    // Pagination
    ...(showPagingControls ? [
      { keys: resolveKeybinding(keybindingOverrides, "list.page.previous", "ArrowLeft"), action: () => goTo(page - 1) },
      { keys: resolveKeybinding(keybindingOverrides, "list.page.next", "ArrowRight"), action: () => goTo(page + 1) },
      { keys: resolveKeybinding(keybindingOverrides, "list.page.back10", "Shift+ArrowLeft"), action: () => goTo(page - 10) },
      { keys: resolveKeybinding(keybindingOverrides, "list.page.forward10", "Shift+ArrowRight"), action: () => goTo(page + 10) },
      { keys: resolveKeybinding(keybindingOverrides, "list.page.first", "Ctrl+Home"), action: () => goTo(1) },
      { keys: resolveKeybinding(keybindingOverrides, "list.page.last", "Ctrl+End"), action: () => goTo(totalPages) },
    ] : []),
    // Filter dialog
    ...(criteriaDefinitions && onObjectFilterChange ? [{ keys: resolveKeybinding(keybindingOverrides, "list.filters", "f"), action: () => setFilterDialogOpen(true) }] : []),
    // Zoom
    { keys: resolveKeybinding(keybindingOverrides, "list.zoom.in", "+"), action: () => setZoomLevel((v) => clampCardSizeLevel(v + 0.25)) },
    { keys: resolveKeybinding(keybindingOverrides, "list.zoom.out", "-"), action: () => setZoomLevel((v) => clampCardSizeLevel(v - 0.25)) },
  ], [showPagingControls, onDisplayModeChange, availableDisplayModes, onSelectAll, onSelectNone, onInvertSelection, goTo, page, totalPages, criteriaDefinitions, onObjectFilterChange, keybindingOverrides]);

  useKeySequence(listBindings);

  // Set page title (e.g., "Scenes | Cove")
  useEffect(() => {
    document.title = `${title} | Cove`;
    return () => { document.title = "Cove"; };
  }, [title]);

  return (
    <div className="space-y-0">
      {/* Toolbar - matches standard FilteredListToolbar */}
      <div className="mx-1 mt-1 flex flex-wrap items-center gap-2 rounded-xl border border-border bg-surface/90 px-2.5 py-2 shadow-sm shadow-black/20">
        {/* Title + count + byline */}
        <div className="mr-auto flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5 pr-2">
          <h1 className="text-sm font-semibold text-foreground whitespace-nowrap">{title}</h1>
          <span className="text-xs text-muted hidden sm:inline">
            {totalCount > 0 ? `${start}-${end} of ${totalCount.toLocaleString()}` : "0 items"}
          </span>
          <span className="text-xs text-muted sm:hidden">
            {totalCount > 0 ? totalCount.toLocaleString() : "0"}
          </span>
          {metadataByline}
        </div>

        {/* Search */}
        <form onSubmit={handleSearch} className="flex shrink-0 items-center gap-1" style={{ width: searchModes && searchModes.length > 1 ? "22rem" : "18rem", maxWidth: "100%" }}>
          {searchModes && searchModes.length > 1 && onSearchModeChange && (
            <select
              value={searchMode ?? searchModes[0]?.value ?? "text"}
              onChange={(e) => {
                onSearchModeChange(e.target.value);
              }}
              className="min-h-[30px] max-w-[5.75rem] rounded-lg border border-border bg-card/70 px-2 py-1.5 text-xs text-foreground focus:outline-none focus:border-accent"
              aria-label="Search mode"
              title="Search mode"
            >
              {searchModes.map((mode) => (
                <option key={mode.value} value={mode.value} title={mode.title}>{mode.label}</option>
              ))}
            </select>
          )}
          <div className="relative min-w-0 flex-1">
            <Search className="absolute left-2 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-muted" />
            <input
              type="text"
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              placeholder={searchPlaceholder ?? "Search names, titles, tags..."}
              aria-label="Search list"
              data-list-search="true"
              className="w-full rounded-lg border border-border bg-card/70 pl-7 pr-7 py-1.5 text-xs text-foreground focus:outline-none focus:border-accent placeholder:text-muted"
            />
            {searchText.trim().length > 0 ? (
              <button
                type="button"
                onClick={() => {
                  setSearchText("");
                  commitSearch("", "clear");
                }}
                className="absolute right-1.5 top-1/2 -translate-y-1/2 rounded p-0.5 text-muted hover:bg-card/80 hover:text-foreground focus:outline-none focus:ring-1 focus:ring-accent"
                aria-label="Clear search"
                title="Clear search"
              >
                <X className="h-3.5 w-3.5" />
              </button>
            ) : null}
          </div>
        </form>

        {/* Sort */}
        {sortedSortOptions && (
          <div className={toolbarSegmentClass}>
            <select
              value={filter.sort ?? ""}
              onChange={(e) => onFilterChange(withSeededRandomSort(filter, { ...filter, sort: e.target.value || undefined, page: 1 }))}
              className={`${toolbarSelectClass} min-w-[8.5rem] max-w-[10rem]`}
            >
              {sortedSortOptions.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>

            {filter.sort === "random" && (
              <button
                type="button"
                onClick={() => onFilterChange(reshuffleRandomSort(filter))}
                className={toolbarIconButtonClass}
                title="Shuffle"
                aria-label="Shuffle"
              >
                <Shuffle className="w-3.5 h-3.5" />
              </button>
            )}

            {/* Direction toggle */}
            <button
              type="button"
              onClick={() => onFilterChange(withSeededRandomSort(filter, { ...filter, direction: filter.direction === "desc" ? "asc" : "desc" }))}
              className={toolbarIconButtonClass}
              title={filter.direction === "desc" ? "Sort descending" : "Sort ascending"}
            >
              {filter.direction === "desc" ? <ArrowDown className="w-3.5 h-3.5" /> : <ArrowUp className="w-3.5 h-3.5" />}
            </button>
          </div>
        )}

        {/* Saved filters */}
        {filterMode && (
          <SavedFilterMenu
            mode={filterMode}
            currentFilter={filter}
            currentObjectFilter={objectFilter}
            currentUIOptions={{ displayMode }}
            onApplyFilter={(nextFilter) => onFilterChange(withSeededRandomSort(filter, nextFilter))}
            onApplyObjectFilter={onObjectFilterChange}
            onApplyUIOptions={(options) => {
              const mode = typeof options.displayMode === "string" ? options.displayMode : undefined;
              if (mode && onDisplayModeChange) onDisplayModeChange(mode as DisplayMode);
            }}
          />
        )}

        {/* Advanced filter button */}
        {criteriaDefinitions && onObjectFilterChange && (
          <FilterButton
            activeCount={Object.keys(objectFilter ?? {}).length}
            onClick={() => setFilterDialogOpen(true)}
          />
        )}

        {/* Display mode */}
        {onDisplayModeChange && availableDisplayModes && (
          <div className={`${toolbarSegmentClass} gap-0.5`}>
            {availableDisplayModes.includes("grid") && (
              <button
                onClick={() => onDisplayModeChange("grid")}
                className={`rounded-md p-1.5 ${displayMode === "grid" ? "bg-background/60 text-accent shadow-sm" : "text-secondary hover:bg-card/80 hover:text-foreground"}`}
                title="Grid"
              >
                <LayoutGrid className="w-3.5 h-3.5" />
              </button>
            )}
            {availableDisplayModes.includes("list") && (
              <button
                onClick={() => onDisplayModeChange("list")}
                className={`rounded-md p-1.5 ${displayMode === "list" ? "bg-background/60 text-accent shadow-sm" : "text-secondary hover:bg-card/80 hover:text-foreground"}`}
                title="List"
              >
                <List className="w-3.5 h-3.5" />
              </button>
            )}
            {availableDisplayModes.includes("wall") && (
              <button
                onClick={() => onDisplayModeChange("wall")}
                className={`rounded-md p-1.5 ${displayMode === "wall" ? "bg-background/60 text-accent shadow-sm" : "text-secondary hover:bg-card/80 hover:text-foreground"}`}
                title="Wall"
              >
                <Grid3X3 className="w-3.5 h-3.5" />
              </button>
            )}
            {availableDisplayModes.includes("tagger") && (
              <button
                onClick={() => onDisplayModeChange("tagger")}
                className={`rounded-md p-1.5 ${displayMode === "tagger" ? "bg-background/60 text-accent shadow-sm" : "text-secondary hover:bg-card/80 hover:text-foreground"}`}
                title="Tagger"
              >
                <Columns3 className="w-3.5 h-3.5" />
              </button>
            )}
            {availableDisplayModes.includes("graph") && (
              <button
                onClick={() => onDisplayModeChange("graph")}
                className={`rounded-md p-1.5 ${displayMode === "graph" ? "bg-background/60 text-accent shadow-sm" : "text-secondary hover:bg-card/80 hover:text-foreground"}`}
                title="Graph/Tree"
              >
                <Share2 className="w-3.5 h-3.5" />
              </button>
            )}
            {availableDisplayModes.includes("byGroup") && (
              <button
                onClick={() => onDisplayModeChange("byGroup")}
                className={`rounded-md p-1.5 ${displayMode === "byGroup" ? "bg-background/60 text-accent shadow-sm" : "text-secondary hover:bg-card/80 hover:text-foreground"}`}
                title="By Group"
              >
                <FolderTree className="w-3.5 h-3.5" />
              </button>
            )}
            {availableDisplayModes.includes("feed") && (
              <button
                onClick={() => onDisplayModeChange("feed")}
                className={`rounded-md p-1.5 ${displayMode === "feed" ? "bg-background/60 text-accent shadow-sm" : "text-secondary hover:bg-card/80 hover:text-foreground"}`}
                title="Feed"
              >
                <Rows3 className="w-3.5 h-3.5" />
              </button>
            )}
            {availableDisplayModes.includes("vertical") && (
              <button
                onClick={() => onDisplayModeChange("vertical")}
                className={`rounded-md p-1.5 ${displayMode === "vertical" ? "bg-background/60 text-accent shadow-sm" : "text-secondary hover:bg-card/80 hover:text-foreground"}`}
                title="Vertical Viewer"
              >
                <MonitorPlay className="w-3.5 h-3.5" />
              </button>
            )}
          </div>
        )}

        {/* Per page */}
        <div className={toolbarSegmentClass}>
          <select
            value={infinitePageSize ? "infinite" : String(perPage)}
            onChange={(e) => onFilterChange({ ...filter, perPage: e.target.value === "infinite" ? 0 : Number(e.target.value), page: 1 })}
            className={`${toolbarSelectClass} min-w-[4.75rem]`}
            title="Items per page"
            disabled={infinitePageSizeOnly}
          >
            {allowInfinitePageSize ? <option value="infinite">Infinite</option> : null}
            {perPageOptions.map((n) => (
              <option key={n} value={n}>{n}</option>
            ))}
          </select>

          {/* Zoom slider (standard card size slider) */}
          {displayMode === "grid" && (
            <div className="flex items-center gap-1 pl-1">
              <ZoomOut className="w-3 h-3 text-muted" />
              <input
                type="range"
                min={0}
                max={5}
                step={0.25}
                value={zoomLevel}
                onChange={(e) => setZoomLevel(clampCardSizeLevel(Number(e.target.value)))}
                className="w-16 sm:w-20 h-1 accent-accent cursor-pointer"
                title={`Card size: ${Math.round(240 + zoomLevel * 60)}px`}
              />
              <ZoomIn className="w-3 h-3 text-muted" />
            </div>
          )}

          {displayMode === "wall" && wallColumnCount != null && onWallColumnCountChange && (
            <div className="flex items-center gap-1 pl-1">
              <input
                type="range"
                min={2}
                max={8}
                step={1}
                value={wallColumnCount}
                onChange={(e) => onWallColumnCountChange(Number(e.target.value))}
                className="w-16 sm:w-20 h-1 accent-accent cursor-pointer"
                title={`Wall columns: ${wallColumnCount}`}
              />
              <span className="min-w-[1rem] text-[10px] text-muted">{wallColumnCount}</span>
            </div>
          )}
        </div>

        {/* Operations */}
        <div className="ml-auto flex flex-wrap items-center justify-end gap-2">
          {renderOperations?.()}
          <ExtensionSlot slot="list-page-toolbar-end" context={slotContext} />
          {pageKey && <ExtensionSlot slot={`${pageKey}-list-toolbar-end`} context={slotContext} />}
          {onNew && (
            <button
              onClick={onNew}
              className="rounded-lg bg-accent px-3 py-1 text-xs font-medium text-white hover:bg-accent-hover"
            >
              + New
            </button>
          )}
        </div>
      </div>

      {showInfiniteAutoScrollControls && (
        <div className="pointer-events-none fixed right-3 top-[24%] z-[90] -translate-y-1/2 sm:right-5">
          <div
            className="pointer-events-auto relative flex min-h-36 w-12 items-center justify-end"
            onPointerEnter={wakeAutoScrollControls}
            onPointerMove={wakeAutoScrollControls}
            onFocusCapture={wakeAutoScrollControls}
          >
            {!autoScrollControlsAwake && <div className="absolute right-0 h-12 w-1.5 rounded-l-full bg-accent/70 shadow-lg" aria-hidden="true" />}
            <div className={`flex flex-col items-center gap-2 rounded-xl border border-border bg-card/95 px-2 py-2 shadow-2xl backdrop-blur transition-all duration-300 ${autoScrollControlsAwake ? "translate-x-0 opacity-100" : "pointer-events-none translate-x-2 opacity-0"}`}>
            <button
              type="button"
              onClick={() => {
                wakeAutoScrollControls();
                setAutoScrollEnabled((current) => !current);
              }}
              className={`${toolbarIconButtonClass} ${autoScrollEnabled ? "bg-background/60 text-accent shadow-sm" : ""}`}
              aria-label={autoScrollEnabled ? "Pause auto-scroll" : "Start auto-scroll"}
              title={autoScrollEnabled ? "Pause auto-scroll" : "Start auto-scroll"}
            >
              {autoScrollEnabled ? <Pause className="h-4 w-4" /> : <Play className="h-4 w-4" />}
            </button>
            <input
              type="range"
              min={10}
              max={360}
              step={10}
              value={autoScrollSpeed}
              onChange={(event) => {
                wakeAutoScrollControls();
                setAutoScrollSpeed(Number(event.target.value));
              }}
              className="h-24 w-1 accent-accent [writing-mode:vertical-lr]"
              aria-label="Floating auto-scroll speed"
              title={`Auto-scroll speed: ${autoScrollSpeed}px/s`}
            />
            <span className="text-[10px] text-muted tabular-nums [writing-mode:vertical-lr]">{autoScrollSpeed}px/s</span>
            </div>
          </div>
        </div>
      )}

      {/* Active filter tags (criterion badges) */}
      {objectFilter && onObjectFilterChange && criteriaDefinitions && Object.keys(objectFilter).length > 0 && (
        <div className="flex flex-wrap items-center gap-1.5 bg-surface/50 border border-border rounded-lg px-3 py-1.5 mx-1 mt-1">
          {Object.entries(objectFilter).map(([key, value]) => {
            const customSection = mergedCustomFilterSections?.find((section) => section.filterKey === key);
            const def = criteriaDefinitions.find((d) => d.id === key || d.filterKey === key);
            const label = customSection?.label ?? def?.label ?? key;
            const nameMap = def?.entityType ? entityNameMaps[def.entityType] : undefined;
            const displayValue = customSection?.summarize?.(value) ?? formatFilterChipValue(def, value, nameMap);
            return (
              <button
                key={key}
                onClick={() => {
                  if (pageKey) {
                    trackInteraction({
                      hostType: "collection",
                      kind: "filterClear",
                      meta: {
                        pageKey,
                        source: "filterChip",
                        criteriaKeys: [key],
                      },
                    });
                  }
                  const next = { ...objectFilter };
                  delete next[key];
                  onObjectFilterChange(next);
                  onFilterChange({ ...filter, page: 1 });
                }}
                className="group flex items-center gap-1 rounded-full bg-card border border-border px-2.5 py-0.5 text-xs text-foreground hover:border-red-400 hover:text-red-300 transition-colors"
                title={`Remove filter: ${label}`}
              >
                <span className="text-muted">{label}:</span>
                <span className="max-w-[200px] truncate">{displayValue}</span>
                <X className="w-3 h-3 opacity-50 group-hover:opacity-100" />
              </button>
            );
          })}
          {showClearAllObjectFilters && (
            <button
              onClick={() => {
                if (pageKey) {
                  trackInteraction({
                    hostType: "collection",
                    kind: "filterClear",
                    meta: {
                      pageKey,
                      source: "filterChip",
                      clearedAll: true,
                    },
                  });
                }
                onObjectFilterChange({});
                onFilterChange({ ...filter, page: 1 });
              }}
              className="text-xs text-muted hover:text-red-300"
            >
              Clear all
            </button>
          )}
        </div>
      )}

      {/* Selection bar */}
      {showSelectionBar && (
        <div className="flex items-center gap-3 bg-card/80 border border-border rounded-lg px-3 py-1.5 mx-1 mt-1">
          <span className="text-xs text-secondary">
            {selectedIds!.size} selected
          </span>
          {onSelectAll && <button onClick={onSelectAll} disabled={selectAllPending} className="text-xs text-accent hover:underline disabled:cursor-not-allowed disabled:opacity-60">{selectAllPending ? "Selecting..." : selectAllLabel}</button>}
          {onSelectAllMatching && (
            <button onClick={onSelectAllMatching} disabled={selectAllMatchingPending} className="text-xs text-accent hover:underline disabled:cursor-not-allowed disabled:opacity-60">
              {selectAllMatchingPending ? "Selecting..." : selectAllMatchingLabel}
            </button>
          )}
          {onInvertSelection && <button onClick={onInvertSelection} className="text-xs text-secondary hover:text-foreground">Invert</button>}
          {onSelectNone && <button onClick={onSelectNone} className="text-xs text-secondary hover:text-foreground">Deselect all</button>}
          {selectionActions}
        </div>
      )}

      {/* Pagination top */}
      {showPagingControls && totalPages > 1 && (
        <div className="flex items-center justify-center gap-1 py-1 mx-1 mt-1">
          <PaginationControls page={page} totalPages={totalPages} goTo={goTo} />
        </div>
      )}

      {/* Content */}
      {isLoading ? (
        <div className="flex items-center justify-center h-64">
          <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-accent" />
        </div>
      ) : (
        <ListPageCardSizeContext.Provider value={{ cardMinWidthPx, zoomLevel }}>
          <div className="pt-3" style={{ "--card-min-width": `${cardMinWidthPx}px` } as React.CSSProperties}>
            {children}
            {infinitePageSize && infiniteScroll && !contentOwnsInfiniteLoading && (
              <InfiniteScrollSentinel
                hasMore={Boolean(infiniteScroll.hasNextPage)}
                isLoading={Boolean(infiniteScroll.isFetchingNextPage)}
                onLoadMore={infiniteScroll.onLoadMore}
                loadedCount={infiniteScroll.loadedCount}
                totalCount={infiniteScroll.totalCount}
              />
            )}
          </div>
        </ListPageCardSizeContext.Provider>
      )}

      {/* Pagination bottom */}
      {showPagingControls && totalPages > 1 && (
        <div className="flex items-center justify-center gap-1 py-4">
          <PaginationControls page={page} totalPages={totalPages} goTo={goTo} />
        </div>
      )}

      {/* Filter Dialog */}
      {criteriaDefinitions && onObjectFilterChange && (
        <FilterDialog
          open={filterDialogOpen}
          onClose={() => { setFilterDialogOpen(false); setFilterDialogPreselect(undefined); }}
          criteria={criteriaDefinitions}
          activeFilter={objectFilter ?? {}}
          customSections={mergedCustomFilterSections}
          onApply={(f) => {
            if (pageKey) {
              trackInteraction({
                hostType: "collection",
                kind: Object.keys(f).length === 0 ? "filterClear" : "filterApply",
                meta: {
                  pageKey,
                  source: "filterDialog",
                  criteriaKeys: Object.keys(f),
                },
              });
            }
            onObjectFilterChange(f);
            onFilterChange({ ...filter, page: 1 });
          }}
          preselectCriterion={filterDialogPreselect}
        />
      )}
    </div>
  );
}

function PaginationControls({ page, totalPages, goTo }: { page: number; totalPages: number; goTo: (p: number) => void }) {
  const [editing, setEditing] = useState(false);
  const [inputValue, setInputValue] = useState(String(page));

  const handleSubmit = () => {
    const p = parseInt(inputValue, 10);
    if (!isNaN(p) && p >= 1 && p <= totalPages) goTo(p);
    setEditing(false);
  };

  return (
    <>
      <button onClick={() => goTo(1)} disabled={page <= 1} className="p-1 rounded hover:bg-card disabled:opacity-30 disabled:cursor-not-allowed text-secondary hover:text-foreground">
        <ChevronsLeft className="w-3.5 h-3.5" />
      </button>
      <button onClick={() => goTo(page - 1)} disabled={page <= 1} className="p-1 rounded hover:bg-card disabled:opacity-30 disabled:cursor-not-allowed text-secondary hover:text-foreground">
        <ChevronLeft className="w-3.5 h-3.5" />
      </button>
      {getPageNumbers(page, totalPages).map((p, i) =>
        p === -1 ? (
          <span key={`ellipsis-${i}`} className="px-1 text-muted text-xs">…</span>
        ) : (
          <button
            key={p}
            onClick={() => goTo(p)}
            className={`min-w-[28px] h-7 rounded text-xs font-medium ${
              p === page ? "bg-accent text-white" : "text-secondary hover:bg-card hover:text-foreground"
            }`}
          >
            {p}
          </button>
        )
      )}
      <button onClick={() => goTo(page + 1)} disabled={page >= totalPages} className="p-1 rounded hover:bg-card disabled:opacity-30 disabled:cursor-not-allowed text-secondary hover:text-foreground">
        <ChevronRight className="w-3.5 h-3.5" />
      </button>
      <button onClick={() => goTo(totalPages)} disabled={page >= totalPages} className="p-1 rounded hover:bg-card disabled:opacity-30 disabled:cursor-not-allowed text-secondary hover:text-foreground">
        <ChevronsRight className="w-3.5 h-3.5" />
      </button>
      {totalPages > 7 && (
        editing ? (
          <form onSubmit={(e) => { e.preventDefault(); handleSubmit(); }} className="ml-1 flex items-center gap-1">
            <input
              type="text"
              autoFocus
              value={inputValue}
              onChange={(e) => setInputValue(e.target.value)}
              onBlur={handleSubmit}
              className="w-12 h-7 rounded border border-border bg-input text-center text-xs text-foreground focus:outline-none focus:border-accent"
            />
          </form>
        ) : (
          <button onClick={() => { setInputValue(String(page)); setEditing(true); }} className="ml-1 h-7 px-2 rounded text-xs text-muted hover:text-foreground hover:bg-card border border-border" title="Go to page…">
            Go to…
          </button>
        )
      )}
    </>
  );
}

function getPageNumbers(current: number, total: number): number[] {
  if (total <= 7) return Array.from({ length: total }, (_, i) => i + 1);
  const pages: number[] = [1];
  if (current > 3) pages.push(-1);
  for (let i = Math.max(2, current - 1); i <= Math.min(total - 1, current + 1); i++) pages.push(i);
  if (current < total - 2) pages.push(-1);
  pages.push(total);
  return pages;
}
