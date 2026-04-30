import { type ReactNode, useCallback, useEffect, useMemo, useState } from "react";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bookmark, ExternalLink, Film, FolderOpen, Loader2, Trash2 } from "lucide-react";
import { faces, scenes, segmentDisplayProfiles, segmentLibrary, segmentSpans } from "../api/client";
import type { FindFilter, ResolvedSpan, Scene, SegmentDerivedQueryDescriptor, SegmentRecord, SegmentSpanDerivedQuery, SegmentSpanOperand, SegmentSpanOperator } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { AddToGroupDialog, type AddToGroupEntry } from "../components/AddToGroupDialog";
import { EntityMultiSelector } from "../components/EntityMultiSelector";
import type { CriterionDefinition, FilterDialogCustomSection } from "../components/FilterDialog";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../components/RouteCardLinkOverlay";
import { SCENE_SORT_OPTIONS } from "../components/sceneSortOptions";
import { getDefaultFilter } from "../components/SavedFilterMenu";
import { formatDate } from "../components/shared";
import { useListUrlState } from "../hooks/useListUrlState";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { LOCATION_CHANGE_EVENT, buildCurrentUrl, navigateToUrl } from "../router/location";

interface Props {
  onNavigate: (r: any) => void;
}

const SEGMENT_CRITERIA: CriterionDefinition[] = [
  { id: "sceneTitle", label: "Scene Title", type: "string", filterKey: "sceneTitleCriterion" },
  { id: "scenes", label: "Scenes", type: "multiId", entityType: "scenes", filterKey: "scenesCriterion" },
];

interface SceneSelectionCriterionValue {
  value?: unknown;
  excludes?: unknown;
}

interface DerivedSpanItem {
  id: string;
  key: string;
  kind: "profile" | "derivedQuery";
  sceneId: number;
  sceneTitle: string;
  sceneUpdatedAt?: string;
  span: ResolvedSpan;
  profileId?: number;
  derivedQuery?: SegmentSpanDerivedQuery;
  derivedQueryDescriptor?: SegmentDerivedQueryDescriptor;
}

interface RawSegmentItem extends SegmentRecord {
  key: string;
  sceneId: number;
  sceneTitle: string;
}

type SegmentsPageContentView = "spans" | "raw";

interface AppliedDerivedQuery {
  operator: SegmentSpanOperator;
  operands: SegmentSpanOperand[];
  mergeGapSec?: number;
  minDurationSec?: number;
}

interface DerivedSpanOperandFilterValue {
  sourceKey?: string;
  kind?: string;
  tagIds: number[];
  performerIds: number[];
  faceIds: number[];
  minConfidence?: number;
}

interface DerivedSpanQueryFilterValue {
  operator: SegmentSpanOperator;
  operands: DerivedSpanOperandFilterValue[];
  mergeGapSec?: number;
  minDurationSec?: number;
}

function readStringCriterion(value: unknown) {
  if (!value || typeof value !== "object") {
    return "";
  }

  const candidate = (value as { value?: unknown }).value;
  return typeof candidate === "string" ? candidate.trim() : "";
}

function readSceneSelectionCriterion(value: unknown) {
  if (!value || typeof value !== "object") {
    return { includeIds: [] as number[], excludeIds: [] as number[] };
  }

  const criterion = value as SceneSelectionCriterionValue;
  const included = Array.isArray(criterion.value)
    ? criterion.value.filter((item): item is number => typeof item === "number" && Number.isFinite(item))
    : [];
  const excluded = Array.isArray(criterion.excludes)
    ? criterion.excludes.filter((item): item is number => typeof item === "number" && Number.isFinite(item))
    : [];

  return {
    includeIds: included,
    excludeIds: excluded,
  };
}

function readSegmentsPageContentView(): SegmentsPageContentView {
  const params = new URLSearchParams(window.location.search);
  return params.get("segmentsView") === "raw" ? "raw" : "spans";
}

function readRawSegmentIdsFromUrl() {
  const params = new URLSearchParams(window.location.search);
  const raw = params.get("rawIds");
  if (!raw) {
    return [] as number[];
  }

  return raw
    .split(",")
    .map((value) => Number(value))
    .filter((value) => Number.isInteger(value) && value > 0);
}

export function SegmentsPage({ onNavigate }: Props) {
  const queryClient = useQueryClient();
  const defaultState = useMemo(() => ({
    filter: getDefaultFilter("segments")?.findFilter ?? { page: 1, perPage: 24, sort: "updated_at", direction: "desc" } as FindFilter,
    objectFilter: getDefaultFilter("segments")?.objectFilter ?? {},
    displayMode: "grid" as DisplayMode,
  }), []);
  const { filter, setFilter, objectFilter, setObjectFilter, displayMode, setDisplayMode } = useListUrlState({
    resetKey: "segments",
    defaultFilter: defaultState.filter,
    defaultObjectFilter: defaultState.objectFilter,
    defaultDisplayMode: defaultState.displayMode,
    allowedDisplayModes: ["grid", "list"] as const,
  });
  const { hasPermission } = useAuth();
  const canReadScenes = canReadEntity("scene", hasPermission);
  const canWriteGroups = canWriteEntity("group", hasPermission);
  const canDeleteSegments = canDeleteEntity("marker", hasPermission);
  const [showAddToGroup, setShowAddToGroup] = useState(false);
  const [activeProfileId, setActiveProfileId] = useState<number>();
  const [contentView, setContentView] = useState<SegmentsPageContentView>(() => readSegmentsPageContentView());
  const [rawSegmentIds, setRawSegmentIds] = useState<number[]>(() => readRawSegmentIdsFromUrl());

  const sceneTitle = readStringCriterion(objectFilter.sceneTitleCriterion);
  const sceneSelection = readSceneSelectionCriterion(objectFilter.scenesCriterion);
  const derivedSpanQueryFilter = useMemo(
    () => readDerivedSpanQueryFilter(objectFilter.derivedSpanQuery),
    [objectFilter.derivedSpanQuery],
  );
  const derivedSpanQueryActive = isDerivedSpanQueryFilterActive(objectFilter.derivedSpanQuery);
  const q = filter.q?.trim() ?? "";
  const perPage = filter.perPage ?? 24;
  const pageNumber = filter.page ?? 1;
  const sort = filter.sort ?? "updated_at";
  const direction = filter.direction ?? "desc";
  const isRawView = contentView === "raw";

  useEffect(() => {
    const syncContentView = () => {
      setContentView(readSegmentsPageContentView());
      setRawSegmentIds(readRawSegmentIdsFromUrl());
    };

    window.addEventListener("popstate", syncContentView);
    window.addEventListener(LOCATION_CHANGE_EVENT, syncContentView);

    return () => {
      window.removeEventListener("popstate", syncContentView);
      window.removeEventListener(LOCATION_CHANGE_EVENT, syncContentView);
    };
  }, []);

  const updateContentView = useCallback((nextView: SegmentsPageContentView, nextRawSegmentIds?: number[]) => {
    const params = new URLSearchParams(window.location.search);
    if (nextView === "raw") {
      params.set("segmentsView", "raw");
    } else {
      params.delete("segmentsView");
    }

    const normalizedRawIds = Array.from(new Set((nextRawSegmentIds ?? []).filter((value) => Number.isInteger(value) && value > 0)));
    if (normalizedRawIds.length > 0) {
      params.set("rawIds", normalizedRawIds.join(","));
    } else {
      params.delete("rawIds");
    }

    navigateToUrl(buildCurrentUrl(window.location.pathname, params), { replace: true });
  }, []);

  const switchContentView = useCallback((nextView: SegmentsPageContentView, nextRawSegmentIds?: number[]) => {
    setFilter({
      ...filter,
      page: 1,
    });
    updateContentView(nextView, nextRawSegmentIds);
  }, [filter, setFilter, updateContentView]);

  const profilesQuery = useQuery({
    queryKey: ["segment-display-profiles"],
    queryFn: () => segmentDisplayProfiles.list(),
    enabled: !isRawView,
  });

  const availableProfiles = useMemo(() => {
    const profiles = profilesQuery.data ?? [];
    const nonRawProfiles = profiles.filter((profile) => !(profile.isSystem && profile.name === "Raw"));
    return nonRawProfiles.length > 0 ? nonRawProfiles : profiles;
  }, [profilesQuery.data]);

  useEffect(() => {
    if (availableProfiles.length === 0) {
      return;
    }

    if (activeProfileId != null && availableProfiles.some((profile) => profile.id === activeProfileId)) {
      return;
    }

    setActiveProfileId(availableProfiles.find((profile) => profile.isDefault)?.id ?? availableProfiles[0].id);
  }, [activeProfileId, availableProfiles]);

  const selectedSceneQueries = useQueries({
    queries: !isRawView ? sceneSelection.includeIds.map((sceneId) => ({
      queryKey: ["scene", sceneId],
      queryFn: () => scenes.get(sceneId),
      staleTime: 60_000,
    })) : [],
  });

  const selectedScenesLoading = selectedSceneQueries.some((query) => query.isLoading);

  const selectedPerformerIds = useMemo(
    () => Array.from(new Set(derivedSpanQueryFilter.operands.flatMap((operand) => operand.performerIds))),
    [derivedSpanQueryFilter.operands],
  );

  const performerFaceQueries = useQueries({
    queries: !isRawView ? selectedPerformerIds.map((performerId) => ({
      queryKey: ["segments-page", "performer-faces", performerId],
      queryFn: () => faces.list({ performerId, merged: false, page: 1, perPage: 200 }),
      staleTime: 60_000,
    })) : [],
  });

  const performerFaceIdsByPerformer = useMemo(() => {
    const map = new Map<number, number[]>();
    selectedPerformerIds.forEach((performerId, index) => {
      map.set(performerId, performerFaceQueries[index]?.data?.items.map((face) => face.id) ?? []);
    });
    return map;
  }, [performerFaceQueries, selectedPerformerIds]);

  const appliedQuery = useMemo(
    () => buildAppliedDerivedQuery(derivedSpanQueryFilter, performerFaceIdsByPerformer),
    [derivedSpanQueryFilter, performerFaceIdsByPerformer],
  );
  const derivedQueryDescriptor = useMemo(
    () => buildDerivedQueryDescriptor(derivedSpanQueryFilter),
    [derivedSpanQueryFilter],
  );
  const performerFaceQueriesLoading = performerFaceQueries.some((query) => query.isLoading);

  const segmentsWindowQuery = useQuery({
    queryKey: [
      "segments-page",
      "search",
      activeProfileId,
      pageNumber,
      perPage,
      q,
      sceneTitle,
      sort,
      direction,
      sceneSelection.includeIds.join(","),
      sceneSelection.excludeIds.join(","),
      appliedQuery ?? null,
    ],
    queryFn: async (): Promise<{ items: DerivedSpanItem[]; totalCount: number }> => {
      if (activeProfileId == null) {
        return { items: [], totalCount: 0 };
      }

      const descriptor = appliedQuery != null ? derivedQueryDescriptor : undefined;
      const response = await segmentSpans.search({
        profile: activeProfileId,
        derivedQuery: appliedQuery != null ? {
          operator: appliedQuery.operator,
          operands: appliedQuery.operands,
          mergeGapSec: appliedQuery.mergeGapSec,
          minDurationSec: appliedQuery.minDurationSec,
        } : undefined,
        page: pageNumber,
        perPage,
        sort,
        direction,
        q: q || undefined,
        sceneTitle: sceneTitle || undefined,
        sceneIds: sceneSelection.includeIds.length > 0 ? sceneSelection.includeIds : undefined,
        excludeSceneIds: sceneSelection.excludeIds.length > 0 ? sceneSelection.excludeIds : undefined,
      });

      return {
        items: response.items.map((item, index) => ({
          id: `${item.sceneId}:${item.span.spanKey}`,
          key: `${item.sceneId}:${item.span.spanKey}`,
          kind: descriptor ? "derivedQuery" : "profile",
          sceneId: item.sceneId,
          sceneTitle: item.sceneTitle ?? `Scene #${item.sceneId}`,
          sceneUpdatedAt: item.sceneUpdatedAt,
          span: item.span,
          profileId: item.profileId,
          derivedQuery: appliedQuery != null ? {
            operator: appliedQuery.operator,
            operands: appliedQuery.operands,
            mergeGapSec: appliedQuery.mergeGapSec,
            minDurationSec: appliedQuery.minDurationSec,
          } : undefined,
          derivedQueryDescriptor: descriptor,
        })),
        totalCount: response.totalCount,
      };
    },
    enabled: !isRawView && activeProfileId != null && (!derivedSpanQueryActive || !performerFaceQueriesLoading) && (sceneSelection.includeIds.length === 0 || !selectedScenesLoading),
    staleTime: 15_000,
  });

  const rawSegmentsQuery = useQuery({
    queryKey: [
      "segments-page",
      "raw",
      pageNumber,
      perPage,
      q,
      sceneTitle,
      sort,
      direction,
      sceneSelection.includeIds.join(","),
      sceneSelection.excludeIds.join(","),
      rawSegmentIds.join(","),
    ],
    queryFn: async (): Promise<{ items: RawSegmentItem[]; totalCount: number }> => {
      const response = await segmentLibrary.list({
        q: q || undefined,
        ids: rawSegmentIds.length > 0 ? rawSegmentIds.join(",") : undefined,
        sceneIds: sceneSelection.includeIds.length > 0 ? sceneSelection.includeIds.join(",") : undefined,
        excludeSceneIds: sceneSelection.excludeIds.length > 0 ? sceneSelection.excludeIds.join(",") : undefined,
        sceneTitle: sceneTitle || undefined,
        sort,
        direction,
        page: pageNumber,
        perPage,
      });

      return {
        items: response.items.map((item) => ({
          ...item,
          key: `segment:${item.id}`,
          sceneId: item.hostId,
          sceneTitle: item.hostTitle?.trim() || `Scene #${item.hostId}`,
        })),
        totalCount: response.totalCount,
      };
    },
    enabled: isRawView,
    staleTime: 15_000,
  });

  const spanItems = segmentsWindowQuery.data?.items ?? [];
  const rawItems = rawSegmentsQuery.data?.items ?? [];
  const items = isRawView ? rawItems : spanItems;
  const totalCount = isRawView ? (rawSegmentsQuery.data?.totalCount ?? 0) : (segmentsWindowQuery.data?.totalCount ?? 0);
  const selectionItems: Array<{ id: string | number }> = items;

  const isLoading = (!isRawView && profilesQuery.isLoading)
    || (!isRawView && sceneSelection.includeIds.length > 0
      ? selectedScenesLoading
      : false)
    || (!isRawView && performerFaceQueriesLoading)
    || (isRawView ? rawSegmentsQuery.isLoading : segmentsWindowQuery.isLoading);
  const firstQueryError = isRawView
    ? (rawSegmentsQuery.error instanceof Error ? rawSegmentsQuery.error : undefined)
    : (segmentsWindowQuery.error instanceof Error ? segmentsWindowQuery.error : undefined);

  useEffect(() => {
    if (totalCount === 0) {
      return;
    }

    const totalPages = Math.max(1, Math.ceil(totalCount / perPage));
    if (pageNumber <= totalPages) {
      return;
    }

    setFilter({
      ...filter,
      page: totalPages,
    });
  }, [filter, pageNumber, perPage, setFilter, totalCount]);

  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(selectionItems);
  const selecting = selectedIds.size > 0;
  const appliedQuerySelectionKey = appliedQuery == null ? "" : JSON.stringify(appliedQuery);
  const selectedEntries = useMemo<AddToGroupEntry[]>(() => spanItems
    .filter((item) => selectedIds.has(item.id))
    .map((item) => ({
      key: item.key,
      sceneId: item.sceneId,
      spanKey: item.span.spanKey,
      title: buildSpanTitle(item.span, item.sceneTitle),
      profileId: item.profileId,
      derivedQuery: item.derivedQuery,
    })), [selectedIds, spanItems]);
  const selectedRawSegments = useMemo(() => rawItems.filter((item) => selectedIds.has(item.id)), [rawItems, selectedIds]);

  const rawDeleteMutation = useMutation({
    mutationFn: async (segmentsToDelete: RawSegmentItem[]) => {
      for (const segment of segmentsToDelete) {
        await scenes.segments.delete(segment.hostId, segment.id);
      }
    },
    onSuccess: async (_result, segmentsToDelete) => {
      await queryClient.invalidateQueries({ queryKey: ["segments-page"] });
      for (const segment of segmentsToDelete) {
        await queryClient.invalidateQueries({ queryKey: ["segment", segment.id] });
        await queryClient.invalidateQueries({ queryKey: ["scene", segment.hostId, "segments"] });
        await queryClient.invalidateQueries({ queryKey: ["scene", segment.hostId] });
      }
      selectNone();
    },
  });

  const customFilterSections = useMemo<FilterDialogCustomSection[]>(() => {
    if (isRawView) {
      return [];
    }

    return [
      {
        id: "derivedSpanQuery",
        label: "Derived Spans",
        filterKey: "derivedSpanQuery",
        defaultValue: createDefaultDerivedSpanQueryFilter(),
        isActive: isDerivedSpanQueryFilterActive,
        summarize: summarizeDerivedSpanQuery,
        renderEditor: (value, onChange) => (
          <DerivedSpanQueryEditor
            value={readDerivedSpanQueryFilter(value)}
            onChange={(nextValue) => onChange(nextValue)}
            scopeSceneIds={sceneSelection.includeIds}
          />
        ),
      },
    ];
  }, [isRawView, sceneSelection.includeIds]);

  useEffect(() => {
    selectNone();
  }, [activeProfileId, appliedQuerySelectionKey, contentView, rawSegmentIds.join(","), selectNone]);

  return (
    <>
      <AddToGroupDialog open={showAddToGroup} onClose={() => setShowAddToGroup(false)} items={selectedEntries} onAdded={() => selectNone()} />
      <ListPage
        title="Segments"
        pageKey="segments"
        filter={filter}
        onFilterChange={setFilter}
        totalCount={totalCount}
        isLoading={isLoading}
        sortOptions={SCENE_SORT_OPTIONS}
        displayMode={displayMode}
        onDisplayModeChange={setDisplayMode}
        availableDisplayModes={["grid", "list"]}
        criteriaDefinitions={SEGMENT_CRITERIA}
        objectFilter={objectFilter}
        onObjectFilterChange={setObjectFilter}
        customFilterSections={customFilterSections}
        metadataByline={(
          <span className="hidden text-xs text-muted lg:inline">
            {isRawView
              ? "Browse persisted raw Segment rows. Profiles and derived-query filters are disabled in raw view."
              : "Browse resolved spans from the active profile. Use Filters to build derived intersections, unions, and differences before snapshotting clips into a compilation."}
          </span>
        )}
        renderOperations={() => (
          <div className="flex flex-wrap items-center gap-2">
            <div className="inline-flex rounded-lg border border-border bg-card/70 p-1 text-xs">
              <button
                type="button"
                onClick={() => switchContentView("spans")}
                className={`rounded-md px-2.5 py-1.5 transition-colors ${!isRawView ? "bg-accent text-white" : "text-muted hover:text-foreground"}`}
              >
                Resolved spans
              </button>
              <button
                type="button"
                onClick={() => switchContentView("raw")}
                className={`rounded-md px-2.5 py-1.5 transition-colors ${isRawView ? "bg-accent text-white" : "text-muted hover:text-foreground"}`}
              >
                Raw segments
              </button>
            </div>
            <label
              title={isRawView ? "Raw view shows segments as observed; profiles and derived queries do not apply." : undefined}
              className="flex items-center gap-2 rounded-lg border border-border bg-card/70 px-2.5 py-1.5 text-xs text-muted"
            >
              <span className="font-semibold uppercase tracking-wide">Profile</span>
              <select
                value={activeProfileId ?? ""}
                onChange={(event) => setActiveProfileId(Number(event.target.value))}
                className="min-w-[10rem] bg-transparent text-xs text-foreground focus:outline-none disabled:cursor-not-allowed disabled:opacity-60"
                disabled={isRawView || availableProfiles.length === 0}
              >
                {availableProfiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>
                    {profile.name}{profile.isDefault ? " (Default)" : ""}
                  </option>
                ))}
              </select>
            </label>
          </div>
        )}
        selectedIds={selectedIds}
        onSelectAll={selectAll}
        onSelectNone={selectNone}
        selectionActions={
          <>
            {isRawView && canDeleteSegments && selectedRawSegments.length > 0 ? (
              <button
                type="button"
                onClick={() => {
                  if (window.confirm(`Delete ${selectedRawSegments.length} raw segment(s)?`)) {
                    rawDeleteMutation.mutate(selectedRawSegments);
                  }
                }}
                disabled={rawDeleteMutation.isPending}
                className="flex items-center gap-1 rounded px-2 py-0.5 text-xs text-red-400 hover:bg-red-900/20 hover:text-red-300 disabled:opacity-60"
              >
                {rawDeleteMutation.isPending ? <Loader2 className="h-3 w-3 animate-spin" /> : <Trash2 className="h-3 w-3" />}
                Delete
              </button>
            ) : null}
            {!isRawView && canWriteGroups && selectedEntries.length > 0 ? (
              <button
                type="button"
                onClick={() => setShowAddToGroup(true)}
                className="flex items-center gap-1 rounded px-2 py-0.5 text-xs text-accent hover:bg-accent/10 hover:text-accent-hover"
              >
                <FolderOpen className="h-3 w-3" />
                Add to group
              </button>
            ) : null}
          </>
        }
      >
        {firstQueryError ? (
          <div className="mb-4 rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
            {isRawView ? `Raw segments failed to load: ${firstQueryError.message}` : `Derived query failed: ${firstQueryError.message}`}
          </div>
        ) : null}

        {displayMode === "grid" ? (
          <div className="grid gap-3" style={{ gridTemplateColumns: "repeat(auto-fill, minmax(var(--card-min-width, 220px), 1fr))" }}>
            {isRawView ? rawItems.map((item) => (
              <RawSegmentCard
                key={item.key}
                item={item}
                canReadScenes={canReadScenes}
                onNavigate={onNavigate}
                onClick={() => selecting ? toggle(item.id) : onNavigate({ page: "segment", id: item.id })}
                selected={selectedIds.has(item.id)}
                onSelect={() => toggle(item.id)}
                selecting={selecting}
              />
            )) : spanItems.map((item) => (
              <DerivedSpanCard
                key={item.key}
                item={item}
                canReadScenes={canReadScenes}
                onNavigate={onNavigate}
                onClick={() => selecting ? toggle(item.id) : onNavigate({ page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor })}
                onViewRawSegments={() => switchContentView("raw", item.span.segmentIds)}
                selected={selectedIds.has(item.id)}
                onSelect={() => toggle(item.id)}
                selecting={selecting}
              />
            ))}
          </div>
        ) : (
          isRawView ? (
            <RawSegmentListTable
              items={rawItems}
              canReadScenes={canReadScenes}
              onNavigate={onNavigate}
              selectedIds={selectedIds}
              onToggle={toggle}
              selecting={selecting}
            />
          ) : (
            <DerivedSpanListTable
              items={spanItems}
              canReadScenes={canReadScenes}
              onNavigate={onNavigate}
              onViewRawSegments={(segmentIds) => switchContentView("raw", segmentIds)}
              selectedIds={selectedIds}
              onToggle={toggle}
              selecting={selecting}
            />
          )
        )}
        {items.length === 0 && !isLoading && (
          <div className="py-16 text-center text-secondary">
            <Bookmark className="mx-auto mb-3 h-12 w-12 text-muted opacity-50" />
            <p>
              {isRawView
                ? rawSegmentIds.length > 0
                  ? "No raw segments matched the selected span contents."
                  : "No raw segments found for this scope."
                : appliedQuery != null
                  ? "No derived spans matched the current query."
                  : "No spans found for this profile and scope."}
            </p>
          </div>
        )}
      </ListPage>
    </>
  );
}

function DerivedSpanCard({
  item,
  canReadScenes,
  onNavigate,
  onClick,
  onViewRawSegments,
  selected,
  onSelect,
  selecting,
}: {
  item: DerivedSpanItem;
  canReadScenes: boolean;
  onNavigate: (r: any) => void;
  onClick: () => void;
  onViewRawSegments: () => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
}) {
  const title = buildSpanTitle(item.span, item.sceneTitle);
  const primaryRawSegmentId = item.span.segmentIds[0];

  return (
    <div
      onClick={selecting ? onClick : undefined}
      className={`entity-card group relative overflow-hidden rounded border bg-card transition-all ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}
    >
      <RouteCardLinkOverlay
        route={{ page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor }}
        onClick={onClick}
        label={`Open span ${title}`}
        disabled={selecting}
        selectionSafeZone={selected !== undefined || selecting}
      />
      <div className="relative aspect-video w-full overflow-hidden bg-surface/70">
        {(selected !== undefined || selecting) && <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />}
        <SegmentScenePreview
          hostId={item.sceneId}
          updatedAt={item.sceneUpdatedAt}
          title={title}
          imgClassName="h-full w-full object-cover"
          fallbackClassName="flex h-full w-full items-center justify-center bg-surface text-muted"
          iconClassName="h-12 w-12"
        />
        <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/35 to-transparent p-3 text-white">
          <div className="text-xs font-medium uppercase tracking-wide text-white/75">{formatSegmentRange(item.span.startSec, item.span.endSec)}</div>
          <div className="mt-1 line-clamp-2 text-sm font-semibold">{title}</div>
        </div>
      </div>

      <div className="border-t border-border bg-card p-3">
        <div className="space-y-1">
          <div className="flex flex-wrap gap-2">
            <Pill>{formatSpanItemKindLabel(item)}</Pill>
          </div>
          <div className="line-clamp-2 text-sm font-medium text-foreground">{title}</div>
          <div className="truncate text-xs text-secondary">{item.sceneTitle}</div>
        </div>
      </div>

      <div className="relative z-10 flex flex-wrap items-center gap-1.5 border-t border-border px-3 py-2 text-[11px]">
          {item.span.tagName ? <Pill>{item.span.tagName}</Pill> : null}
          {item.span.kind ? <Pill>{item.span.kind}</Pill> : null}
          <Pill>{formatSegmentDuration(item.span.startSec, item.span.endSec)}</Pill>
          {item.span.sourceKey ? <Pill>{item.span.sourceKey}</Pill> : null}
          <span className="ml-auto text-muted">{item.span.segmentIds.length} raw segment{item.span.segmentIds.length === 1 ? "" : "s"}</span>
        </div>

      <div className="relative z-10 flex items-center justify-between gap-2 border-t border-border px-3 py-2 text-xs text-secondary">
        <span>Updated {formatDate(item.sceneUpdatedAt)}</span>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
              onViewRawSegments();
            }}
            className="inline-flex items-center gap-1 text-accent hover:underline"
          >
            View raw segments ({item.span.segmentIds.length})
          </button>
          {primaryRawSegmentId != null ? (
            <button
              type="button"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onNavigate({ page: "segment", id: primaryRawSegmentId });
              }}
              className="inline-flex items-center gap-1 text-accent hover:underline"
            >
              <ExternalLink className="h-3.5 w-3.5" />
              Open raw
            </button>
          ) : null}
          {canReadScenes ? (
            <button
              type="button"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onNavigate({ page: "scene", id: item.sceneId, seekTo: item.span.startSec });
              }}
              className="inline-flex items-center gap-1 text-accent hover:underline"
            >
              <FolderOpen className="h-3.5 w-3.5" />
              Open scene
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function DerivedSpanListTable({
  items,
  canReadScenes,
  onNavigate,
  onViewRawSegments,
  selectedIds,
  onToggle,
  selecting,
}: {
  items: DerivedSpanItem[];
  canReadScenes: boolean;
  onNavigate: (r: any) => void;
  onViewRawSegments: (segmentIds: number[]) => void;
  selectedIds: Set<string | number>;
  onToggle: (id: string | number) => void;
  selecting: boolean;
}) {
  return (
    <div className="overflow-hidden rounded-xl border border-border bg-card">
      <div className="hidden grid-cols-[minmax(0,1.4fr)_140px_minmax(0,1.1fr)_120px_120px] gap-3 border-b border-border bg-surface/70 px-4 py-2 text-[11px] font-medium uppercase tracking-wide text-muted lg:grid">
        <span>Span</span>
        <span>Range</span>
        <span>Scene</span>
        <span>Source</span>
        <span>Updated</span>
      </div>
      <div className="divide-y divide-border">
        {items.map((item) => (
          <DerivedSpanListRow
            key={item.key}
            item={item}
            canReadScenes={canReadScenes}
            onNavigate={onNavigate}
            onViewRawSegments={onViewRawSegments}
            selected={selectedIds.has(item.id)}
            onToggle={() => onToggle(item.id)}
            selecting={selecting}
          />
        ))}
      </div>
    </div>
  );
}

function DerivedSpanListRow({
  item,
  canReadScenes,
  onNavigate,
  onViewRawSegments,
  selected,
  onToggle,
  selecting,
}: {
  item: DerivedSpanItem;
  canReadScenes: boolean;
  onNavigate: (r: any) => void;
  onViewRawSegments: (segmentIds: number[]) => void;
  selected: boolean;
  onToggle: () => void;
  selecting: boolean;
}) {
  const title = buildSpanTitle(item.span, item.sceneTitle);
  const primaryRawSegmentId = item.span.segmentIds[0];

  return (
    <div
      onClick={selecting ? onToggle : undefined}
      className={`group relative cursor-pointer px-4 py-3 transition-colors ${selected ? "bg-accent/10" : "hover:bg-surface/40"}`}
    >
      <RouteCardLinkOverlay
        route={{ page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor }}
        onClick={() => onNavigate({ page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor })}
        label={`Open span ${title}`}
        disabled={selecting}
        selectionSafeZone
      />
      <div className="flex items-start gap-3 lg:grid lg:grid-cols-[minmax(0,1.4fr)_140px_minmax(0,1.1fr)_120px_120px] lg:items-center">
        <div className="relative min-w-0 pl-8">
          <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onToggle} />
          <div className="flex items-start gap-3">
            <div className="hidden h-16 w-24 shrink-0 overflow-hidden rounded-lg bg-surface sm:block">
              <SegmentScenePreview
                hostId={item.sceneId}
                updatedAt={item.sceneUpdatedAt}
                title={title}
                imgClassName="h-full w-full object-cover"
                fallbackClassName="flex h-full w-full items-center justify-center bg-surface text-muted"
                iconClassName="h-6 w-6"
              />
            </div>
            <div className="min-w-0">
              <div className="truncate text-sm font-medium text-foreground">{title}</div>
              <div className="mt-1 flex flex-wrap items-center gap-1.5 text-[11px] text-secondary">
                <Pill>{formatSpanItemKindLabel(item)}</Pill>
                {item.span.tagName ? <Pill>{item.span.tagName}</Pill> : null}
                {item.span.kind ? <Pill>{item.span.kind}</Pill> : null}
                <Pill>{formatSegmentDuration(item.span.startSec, item.span.endSec)}</Pill>
                <span>{item.span.segmentIds.length} raw segment{item.span.segmentIds.length === 1 ? "" : "s"}</span>
              </div>
            </div>
          </div>
        </div>
        <div className="hidden text-xs text-secondary lg:block">{formatSegmentRange(item.span.startSec, item.span.endSec)}</div>
        <div className="min-w-0 text-xs text-secondary lg:text-sm">
          <div className="truncate text-foreground">{item.sceneTitle}</div>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onViewRawSegments(item.span.segmentIds);
              }}
              className="relative z-10 inline-flex items-center gap-1 text-accent hover:underline"
            >
              View raw segments ({item.span.segmentIds.length})
            </button>
            {primaryRawSegmentId != null ? (
              <button
                type="button"
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onNavigate({ page: "segment", id: primaryRawSegmentId });
                }}
                className="relative z-10 inline-flex items-center gap-1 text-accent hover:underline"
              >
                <ExternalLink className="h-3.5 w-3.5" />
                Open raw
              </button>
            ) : null}
            {canReadScenes ? (
            <button
              type="button"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onNavigate({ page: "scene", id: item.sceneId, seekTo: item.span.startSec });
              }}
              className="relative z-10 mt-1 inline-flex items-center gap-1 text-accent hover:underline"
            >
              <FolderOpen className="h-3.5 w-3.5" />
              Open scene
            </button>
            ) : null}
          </div>
        </div>
        <div className="hidden text-xs text-secondary lg:block">{item.span.sourceKey || "Derived"}</div>
        <div className="hidden text-xs text-secondary lg:block">{formatDate(item.sceneUpdatedAt)}</div>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-3 pl-8 text-[11px] text-secondary lg:hidden">
        <span>{formatSegmentRange(item.span.startSec, item.span.endSec)}</span>
        <span>{item.span.sourceKey || "Derived"}</span>
        <span>{formatDate(item.sceneUpdatedAt)}</span>
      </div>
    </div>
  );
}

function RawSegmentCard({
  item,
  canReadScenes,
  onNavigate,
  onClick,
  selected,
  onSelect,
  selecting,
}: {
  item: RawSegmentItem;
  canReadScenes: boolean;
  onNavigate: (r: any) => void;
  onClick: () => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
}) {
  const title = buildRawSegmentTitle(item);

  return (
    <div
      onClick={selecting ? onClick : undefined}
      className={`entity-card group relative overflow-hidden rounded border bg-card transition-all ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}
    >
      <RouteCardLinkOverlay
        route={{ page: "segment", id: item.id }}
        onClick={onClick}
        label={`Open raw segment ${title}`}
        disabled={selecting}
        selectionSafeZone={selected !== undefined || selecting}
      />
      <div className="relative aspect-video w-full overflow-hidden bg-surface/70">
        {(selected !== undefined || selecting) && <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />}
        <SegmentScenePreview
          hostId={item.hostId}
          updatedAt={item.updatedAt}
          title={title}
          imgClassName="h-full w-full object-cover"
          fallbackClassName="flex h-full w-full items-center justify-center bg-surface text-muted"
          iconClassName="h-12 w-12"
        />
        <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/35 to-transparent p-3 text-white">
          <div className="text-xs font-medium uppercase tracking-wide text-white/75">Raw segment #{item.id}</div>
          <div className="mt-1 line-clamp-2 text-sm font-semibold">{title}</div>
        </div>
      </div>

      <div className="border-t border-border bg-card p-3">
        <div className="space-y-1">
          <div className="line-clamp-2 text-sm font-medium text-foreground">{title}</div>
          <div className="truncate text-xs text-secondary">{item.sceneTitle}</div>
        </div>
      </div>

      <div className="relative z-10 flex flex-wrap items-center gap-1.5 border-t border-border px-3 py-2 text-[11px]">
        {item.tagName ? <Pill>{item.tagName}</Pill> : null}
        {item.kind ? <Pill>{item.kind}</Pill> : null}
        <Pill>{formatSegmentRange(item.startSec, item.endSec)}</Pill>
        <Pill>{item.sourceKey}</Pill>
        {item.confidence != null ? <Pill>{item.confidence.toFixed(2)} conf</Pill> : null}
        {item.sourceRunId ? <Pill>{item.sourceRunId}</Pill> : null}
      </div>

      <div className="relative z-10 flex items-center justify-between gap-2 border-t border-border px-3 py-2 text-xs text-secondary">
        <span>Updated {formatDate(item.updatedAt)}</span>
        <div className="flex items-center gap-2">
          {canReadScenes ? (
            <button
              type="button"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onNavigate({ page: "scene", id: item.hostId, seekTo: item.startSec });
              }}
              className="inline-flex items-center gap-1 text-accent hover:underline"
            >
              <FolderOpen className="h-3.5 w-3.5" />
              Open scene
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function RawSegmentListTable({
  items,
  canReadScenes,
  onNavigate,
  selectedIds,
  onToggle,
  selecting,
}: {
  items: RawSegmentItem[];
  canReadScenes: boolean;
  onNavigate: (r: any) => void;
  selectedIds: Set<string | number>;
  onToggle: (id: string | number) => void;
  selecting: boolean;
}) {
  return (
    <div className="overflow-hidden rounded-xl border border-border bg-card">
      <div className="hidden grid-cols-[minmax(0,1.3fr)_140px_minmax(0,1fr)_120px_120px] gap-3 border-b border-border bg-surface/70 px-4 py-2 text-[11px] font-medium uppercase tracking-wide text-muted lg:grid">
        <span>Segment</span>
        <span>Range</span>
        <span>Scene</span>
        <span>Source</span>
        <span>Updated</span>
      </div>
      <div className="divide-y divide-border">
        {items.map((item) => (
          <RawSegmentListRow
            key={item.key}
            item={item}
            canReadScenes={canReadScenes}
            onNavigate={onNavigate}
            selected={selectedIds.has(item.id)}
            onToggle={() => onToggle(item.id)}
            selecting={selecting}
          />
        ))}
      </div>
    </div>
  );
}

function RawSegmentListRow({
  item,
  canReadScenes,
  onNavigate,
  selected,
  onToggle,
  selecting,
}: {
  item: RawSegmentItem;
  canReadScenes: boolean;
  onNavigate: (r: any) => void;
  selected: boolean;
  onToggle: () => void;
  selecting: boolean;
}) {
  const title = buildRawSegmentTitle(item);

  return (
    <div
      onClick={selecting ? onToggle : undefined}
      className={`group relative cursor-pointer px-4 py-3 transition-colors ${selected ? "bg-accent/10" : "hover:bg-surface/40"}`}
    >
      <RouteCardLinkOverlay
        route={{ page: "segment", id: item.id }}
        onClick={() => onNavigate({ page: "segment", id: item.id })}
        label={`Open raw segment ${title}`}
        disabled={selecting}
        selectionSafeZone
      />
      <div className="flex items-start gap-3 lg:grid lg:grid-cols-[minmax(0,1.3fr)_140px_minmax(0,1fr)_120px_120px] lg:items-center">
        <div className="relative min-w-0 pl-8">
          <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onToggle} />
          <div className="flex items-start gap-3">
            <div className="hidden h-16 w-24 shrink-0 overflow-hidden rounded-lg bg-surface sm:block">
              <SegmentScenePreview
                hostId={item.hostId}
                updatedAt={item.updatedAt}
                title={title}
                imgClassName="h-full w-full object-cover"
                fallbackClassName="flex h-full w-full items-center justify-center bg-surface text-muted"
                iconClassName="h-6 w-6"
              />
            </div>
            <div className="min-w-0">
              <div className="truncate text-sm font-medium text-foreground">{title}</div>
              <div className="mt-1 flex flex-wrap items-center gap-1.5 text-[11px] text-secondary">
                <Pill>#{item.id}</Pill>
                {item.tagName ? <Pill>{item.tagName}</Pill> : null}
                {item.kind ? <Pill>{item.kind}</Pill> : null}
                {item.confidence != null ? <Pill>{item.confidence.toFixed(2)} conf</Pill> : null}
                {item.sourceRunId ? <Pill>{item.sourceRunId}</Pill> : null}
              </div>
            </div>
          </div>
        </div>
        <div className="hidden text-xs text-secondary lg:block">{formatSegmentRange(item.startSec, item.endSec)}</div>
        <div className="min-w-0 text-xs text-secondary lg:text-sm">
          <div className="truncate text-foreground">{item.sceneTitle}</div>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            {canReadScenes ? (
              <button
                type="button"
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onNavigate({ page: "scene", id: item.hostId, seekTo: item.startSec });
                }}
                className="relative z-10 inline-flex items-center gap-1 text-accent hover:underline"
              >
                <FolderOpen className="h-3.5 w-3.5" />
                Open scene
              </button>
            ) : null}
          </div>
        </div>
        <div className="hidden text-xs text-secondary lg:block">{item.sourceKey}</div>
        <div className="hidden text-xs text-secondary lg:block">{formatDate(item.updatedAt)}</div>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-3 pl-8 text-[11px] text-secondary lg:hidden">
        <span>{formatSegmentRange(item.startSec, item.endSec)}</span>
        <span>{item.sourceKey}</span>
        <span>{formatDate(item.updatedAt)}</span>
      </div>
    </div>
  );
}

function Pill({ children }: { children: ReactNode }) {
  return (
    <span className="inline-flex items-center rounded-full bg-surface px-2 py-1 text-secondary">
      {children}
    </span>
  );
}

function SegmentScenePreview({
  hostId,
  updatedAt,
  title,
  imgClassName,
  fallbackClassName,
  iconClassName,
}: {
  hostId: number;
  updatedAt?: string;
  title: string;
  imgClassName: string;
  fallbackClassName: string;
  iconClassName: string;
}) {
  const [failed, setFailed] = useState(false);

  if (failed) {
    return (
      <div className={fallbackClassName}>
        <Film className={iconClassName} />
      </div>
    );
  }

  return (
    <img
      src={scenes.screenshotUrl(hostId, updatedAt)}
      alt={title}
      className={imgClassName}
      loading="lazy"
      onError={() => setFailed(true)}
    />
  );
}

function buildSpanTitle(span: ResolvedSpan, sceneTitle?: string) {
  return span.tagName || span.kind || span.sourceKey || sceneTitle || `Span ${span.spanKey}`;
}

function buildRawSegmentTitle(segment: RawSegmentItem) {
  return segment.title?.trim() || segment.tagName || segment.kind || `${segment.sourceKey} #${segment.id}`;
}

function formatSpanItemKindLabel(item: DerivedSpanItem) {
  return item.kind === "derivedQuery"
    ? `Derived ${formatOperatorLabel(item.derivedQueryDescriptor?.operator ?? "intersection")}`
    : "Profile";
}

function createEmptyDerivedSpanOperand(): DerivedSpanOperandFilterValue {
  return {
    sourceKey: undefined,
    kind: undefined,
    tagIds: [],
    performerIds: [],
    faceIds: [],
    minConfidence: undefined,
  };
}

function createDefaultDerivedSpanQueryFilter(): DerivedSpanQueryFilterValue {
  return {
    operator: "intersection",
    operands: [createEmptyDerivedSpanOperand(), createEmptyDerivedSpanOperand()],
    mergeGapSec: undefined,
    minDurationSec: undefined,
  };
}

function readDerivedSpanOperandFilter(value: unknown): DerivedSpanOperandFilterValue {
  if (!value || typeof value !== "object") {
    return createEmptyDerivedSpanOperand();
  }

  const operand = value as {
    sourceKey?: unknown;
    kind?: unknown;
    tagIds?: unknown;
    performerIds?: unknown;
    faceIds?: unknown;
    minConfidence?: unknown;
  };

  return {
    sourceKey: typeof operand.sourceKey === "string" && operand.sourceKey.trim().length > 0 ? operand.sourceKey.trim() : undefined,
    kind: typeof operand.kind === "string" && operand.kind.trim().length > 0 ? operand.kind.trim() : undefined,
    tagIds: normalizeIdArray(operand.tagIds),
    performerIds: normalizeIdArray(operand.performerIds),
    faceIds: normalizeIdArray(operand.faceIds),
    minConfidence: normalizeFiniteNumber(operand.minConfidence),
  };
}

function readDerivedSpanQueryFilter(value: unknown): DerivedSpanQueryFilterValue {
  const fallback = createDefaultDerivedSpanQueryFilter();
  if (!value || typeof value !== "object") {
    return fallback;
  }

  const candidate = value as {
    operator?: unknown;
    operands?: unknown;
    mergeGapSec?: unknown;
    minDurationSec?: unknown;
  };
  const operator = candidate.operator === "union" || candidate.operator === "difference" || candidate.operator === "intersection"
    ? candidate.operator
    : fallback.operator;
  const operands = Array.isArray(candidate.operands)
    ? candidate.operands.map(readDerivedSpanOperandFilter)
    : fallback.operands;

  return {
    operator,
    operands: operands.length > 0 ? operands : fallback.operands,
    mergeGapSec: normalizeFiniteNumber(candidate.mergeGapSec),
    minDurationSec: normalizeFiniteNumber(candidate.minDurationSec),
  };
}

function isDerivedSpanOperandFilterActive(operand: DerivedSpanOperandFilterValue) {
  return Boolean(
    operand.sourceKey
    || operand.kind
    || operand.tagIds.length > 0
    || operand.performerIds.length > 0
    || operand.faceIds.length > 0
    || operand.minConfidence != null,
  );
}

function isDerivedSpanQueryFilterActive(value: unknown) {
  const filter = readDerivedSpanQueryFilter(value);
  return filter.operands.some(isDerivedSpanOperandFilterActive);
}

function summarizeDerivedSpanQuery(value: unknown) {
  const filter = readDerivedSpanQueryFilter(value);
  const activeOperandCount = filter.operands.filter(isDerivedSpanOperandFilterActive).length;
  if (activeOperandCount === 0) {
    return "Resolved spans";
  }

  return `${formatOperatorLabel(filter.operator)} · ${activeOperandCount} operand${activeOperandCount === 1 ? "" : "s"}`;
}

function buildAppliedDerivedQuery(
  filter: DerivedSpanQueryFilterValue,
  performerFaceIdsByPerformer: Map<number, number[]>,
): AppliedDerivedQuery | null {
  const operands = filter.operands
    .map((operand) => buildAppliedDerivedOperand(operand, performerFaceIdsByPerformer))
    .filter((operand): operand is SegmentSpanOperand => operand != null);

  if (operands.length === 0) {
    return null;
  }

  return {
    operator: filter.operator,
    operands,
    mergeGapSec: filter.mergeGapSec,
    minDurationSec: filter.minDurationSec,
  };
}

function buildAppliedDerivedOperand(
  operand: DerivedSpanOperandFilterValue,
  performerFaceIdsByPerformer: Map<number, number[]>,
): SegmentSpanOperand | null {
  const linkedFaceIds = operand.performerIds.flatMap((performerId) => performerFaceIdsByPerformer.get(performerId) ?? []);
  const refIds = Array.from(new Set([...operand.faceIds, ...linkedFaceIds]));

  if (operand.performerIds.length > 0 && refIds.length === 0) {
    refIds.push(-1);
  }

  if (!operand.sourceKey && !operand.kind && operand.tagIds.length === 0 && refIds.length === 0 && operand.minConfidence == null) {
    return null;
  }

  return {
    sourceKey: operand.sourceKey,
    kind: operand.kind,
    tagIds: operand.tagIds.length > 0 ? operand.tagIds : undefined,
    refIds: refIds.length > 0 ? refIds : undefined,
    minConfidence: operand.minConfidence,
  };
}

function buildDerivedQueryDescriptor(filter: DerivedSpanQueryFilterValue): SegmentDerivedQueryDescriptor | undefined {
  const operands = filter.operands
    .filter(isDerivedSpanOperandFilterActive)
    .map((operand) => ({
      sourceKey: operand.sourceKey,
      kind: operand.kind,
      tagIds: operand.tagIds.length > 0 ? operand.tagIds : undefined,
      performerIds: operand.performerIds.length > 0 ? operand.performerIds : undefined,
      faceIds: operand.faceIds.length > 0 ? operand.faceIds : undefined,
      minConfidence: operand.minConfidence,
    }));

  if (operands.length === 0) {
    return undefined;
  }

  return {
    operator: filter.operator,
    operands,
    mergeGapSec: filter.mergeGapSec,
    minDurationSec: filter.minDurationSec,
  };
}

function normalizeIdArray(value: unknown) {
  return Array.isArray(value)
    ? value.filter((item): item is number => typeof item === "number" && Number.isFinite(item) && item > 0)
    : [];
}

function normalizeFiniteNumber(value: unknown) {
  return typeof value === "number" && Number.isFinite(value) ? value : undefined;
}

function formatOperatorLabel(operator: SegmentSpanOperator) {
  switch (operator) {
    case "union":
      return "Union";
    case "difference":
      return "Difference";
    case "intersection":
    default:
      return "Intersection";
  }
}

function DerivedSpanQueryEditor({
  value,
  onChange,
  scopeSceneIds,
}: {
  value: DerivedSpanQueryFilterValue;
  onChange: (value: DerivedSpanQueryFilterValue) => void;
  scopeSceneIds: number[];
}) {
  // Fetch operand options lazily — this component only mounts when the filter dialog is open,
  // so the query only fires when the user is actively editing the filter.
  const optionsQuery = useQuery({
    queryKey: ["segments-page", "operand-options", scopeSceneIds.join(",")],
    queryFn: async () => {
      const response = await segmentLibrary.list({
        sceneIds: scopeSceneIds.length > 0 ? scopeSceneIds.join(",") : undefined,
        perPage: 5000,
      });

      const sourceKeys = Array.from(new Set(response.items.map((segment) => segment.sourceKey?.trim()).filter((value): value is string => Boolean(value)))).sort((left, right) => left.localeCompare(right));
      const kinds = Array.from(new Set(response.items.map((segment) => segment.kind?.trim()).filter((value): value is string => Boolean(value)))).sort((left, right) => left.localeCompare(right));

      return { sourceKeys, kinds };
    },
    staleTime: 60_000,
  });

  const sourceOptions = optionsQuery.data?.sourceKeys ?? [];
  const kindOptions = optionsQuery.data?.kinds ?? [];
  const optionsLoading = optionsQuery.isLoading;

  const updateOperand = (index: number, patch: Partial<DerivedSpanOperandFilterValue>) => {
    onChange({
      ...value,
      operands: value.operands.map((operand, operandIndex) => (
        operandIndex === index ? { ...operand, ...patch } : operand
      )),
    });
  };

  return (
    <div className="space-y-4">
      <p className="text-xs text-secondary">Build derived span combinations inside Filters so intersections, unions, and performer or face matches stay part of the page’s filter state.</p>

      <div className="grid gap-3 md:grid-cols-3">
        <label className="space-y-1 text-xs text-secondary">
          <span className="font-semibold uppercase tracking-wide text-muted">Operator</span>
          <select
            value={value.operator}
            onChange={(event) => onChange({ ...value, operator: event.target.value as SegmentSpanOperator })}
            className="w-full rounded border border-border bg-input px-2 py-1.5 text-sm text-foreground focus:border-accent focus:outline-none"
          >
            <option value="intersection">Intersection</option>
            <option value="union">Union</option>
            <option value="difference">Difference</option>
          </select>
        </label>
        <label className="space-y-1 text-xs text-secondary">
          <span className="font-semibold uppercase tracking-wide text-muted">Merge gap (sec)</span>
          <input
            type="number"
            min="0"
            step="0.1"
            value={value.mergeGapSec ?? ""}
            onChange={(event) => onChange({ ...value, mergeGapSec: parseOptionalNumber(event.target.value) })}
            className="w-full rounded border border-border bg-input px-2 py-1.5 text-sm text-foreground focus:border-accent focus:outline-none"
            placeholder="Optional"
          />
        </label>
        <label className="space-y-1 text-xs text-secondary">
          <span className="font-semibold uppercase tracking-wide text-muted">Minimum duration (sec)</span>
          <input
            type="number"
            min="0"
            step="0.1"
            value={value.minDurationSec ?? ""}
            onChange={(event) => onChange({ ...value, minDurationSec: parseOptionalNumber(event.target.value) })}
            className="w-full rounded border border-border bg-input px-2 py-1.5 text-sm text-foreground focus:border-accent focus:outline-none"
            placeholder="Optional"
          />
        </label>
      </div>

      <div className="space-y-3">
        {value.operands.map((operand, index) => (
          <div key={index} className="rounded-lg border border-border bg-card/60 p-3">
            <div className="flex items-center justify-between gap-3">
              <div className="text-xs font-semibold uppercase tracking-wide text-muted">Operand {index + 1}</div>
              {value.operands.length > 2 ? (
                <button
                  type="button"
                  onClick={() => onChange({ ...value, operands: value.operands.filter((_, operandIndex) => operandIndex !== index) })}
                  className="text-xs text-secondary hover:text-foreground"
                >
                  Remove operand
                </button>
              ) : null}
            </div>

            <div className="mt-3 grid gap-3 md:grid-cols-3">
              <label className="space-y-1 text-xs text-secondary">
                <span className="font-semibold uppercase tracking-wide text-muted">Source</span>
                <select
                  value={operand.sourceKey ?? ""}
                  onChange={(event) => updateOperand(index, { sourceKey: event.target.value || undefined })}
                  className="w-full rounded border border-border bg-input px-2 py-1.5 text-sm text-foreground focus:border-accent focus:outline-none"
                >
                  <option value="">Any source</option>
                  {optionsLoading && sourceOptions.length === 0 ? <option value="" disabled>Loading sources...</option> : null}
                  {sourceOptions.map((sourceKey) => (
                    <option key={sourceKey} value={sourceKey}>{sourceKey}</option>
                  ))}
                </select>
              </label>
              <label className="space-y-1 text-xs text-secondary">
                <span className="font-semibold uppercase tracking-wide text-muted">Kind</span>
                <select
                  value={operand.kind ?? ""}
                  onChange={(event) => updateOperand(index, { kind: event.target.value || undefined })}
                  className="w-full rounded border border-border bg-input px-2 py-1.5 text-sm text-foreground focus:border-accent focus:outline-none"
                >
                  <option value="">Any kind</option>
                  {optionsLoading && kindOptions.length === 0 ? <option value="" disabled>Loading kinds...</option> : null}
                  {kindOptions.map((kind) => (
                    <option key={kind} value={kind}>{kind}</option>
                  ))}
                </select>
              </label>
              <label className="space-y-1 text-xs text-secondary">
                <span className="font-semibold uppercase tracking-wide text-muted">Minimum confidence</span>
                <input
                  type="number"
                  min="0"
                  max="1"
                  step="0.01"
                  value={operand.minConfidence ?? ""}
                  onChange={(event) => updateOperand(index, { minConfidence: parseOptionalNumber(event.target.value) })}
                  className="w-full rounded border border-border bg-input px-2 py-1.5 text-sm text-foreground focus:border-accent focus:outline-none"
                  placeholder="Optional"
                />
              </label>
            </div>

            <div className="mt-3 grid gap-3 lg:grid-cols-3">
              <div className="space-y-1">
                <div className="text-xs font-semibold uppercase tracking-wide text-muted">Tags</div>
                <EntityMultiSelector
                  entityType="tags"
                  values={operand.tagIds}
                  onChange={(tagIds) => updateOperand(index, { tagIds })}
                  placeholder="Search tags..."
                  emptyMessage="No tags found"
                />
              </div>
              <div className="space-y-1">
                <div className="text-xs font-semibold uppercase tracking-wide text-muted">Performers</div>
                <EntityMultiSelector
                  entityType="performers"
                  values={operand.performerIds}
                  onChange={(performerIds) => updateOperand(index, { performerIds })}
                  placeholder="Search performers..."
                  emptyMessage="No performers found"
                />
                <p className="text-[11px] text-muted">Performer matches use linked faces automatically.</p>
              </div>
              <div className="space-y-1">
                <div className="text-xs font-semibold uppercase tracking-wide text-muted">Faces</div>
                <EntityMultiSelector
                  entityType="faces"
                  values={operand.faceIds}
                  onChange={(faceIds) => updateOperand(index, { faceIds })}
                  placeholder="Search faces..."
                  emptyMessage="No faces found"
                />
              </div>
            </div>
          </div>
        ))}
      </div>

      <button
        type="button"
        onClick={() => onChange({ ...value, operands: [...value.operands, createEmptyDerivedSpanOperand()] })}
        className="rounded border border-border px-3 py-1.5 text-xs text-foreground hover:border-accent"
      >
        Add operand
      </button>
    </div>
  );
}

function parseOptionalNumber(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return undefined;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function formatSegmentRange(startSec: number, endSec?: number) {
  const start = formatSegmentTime(startSec);
  return endSec == null ? start : `${start} - ${formatSegmentTime(endSec)}`;
}

function formatSegmentDuration(startSec: number, endSec?: number) {
  if (endSec == null) {
    return "Instant";
  }

  const duration = Math.max(0, endSec - startSec);
  return duration > 0 ? `${formatSegmentTime(duration)} long` : "Instant";
}

function formatSegmentTime(value: number) {
  const totalHundredths = Math.max(0, Math.round(value * 100));
  const hours = Math.floor(totalHundredths / 360000);
  const minutes = Math.floor((totalHundredths % 360000) / 6000);
  const seconds = Math.floor((totalHundredths % 6000) / 100);
  const hundredths = totalHundredths % 100;

  if (hundredths === 0) {
    if (hours > 0) {
      return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
    }

    return `${minutes}:${String(seconds).padStart(2, "0")}`;
  }

  const fractional = hundredths % 10 === 0
    ? String(Math.floor(hundredths / 10))
    : String(hundredths).padStart(2, "0");

  if (hours > 0) {
    return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}.${fractional}`;
  }

  return `${minutes}:${String(seconds).padStart(2, "0")}.${fractional}`;
}
