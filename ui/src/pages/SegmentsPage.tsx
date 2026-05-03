import { useCallback, useEffect, useMemo, useState } from "react";
import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { FolderOpen, Loader2, Trash2 } from "lucide-react";
import { faces, scenes, segmentDisplayProfiles } from "../api/client";
import type { FindFilter } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { AddToGroupDialog, type AddToGroupEntry } from "../components/AddToGroupDialog";
import { ListPage, type DisplayMode } from "../components/ListPage";
import { SCENE_SORT_OPTIONS } from "../components/sceneSortOptions";
import { getDefaultFilter } from "../components/SavedFilterMenu";
import { useListUrlState } from "../hooks/useListUrlState";
import { useMultiSelect } from "../hooks/useMultiSelect";
import {
  buildAppliedDerivedQuery,
  buildDerivedQueryDescriptor,
  createDerivedSpanCustomFilterSection,
  formatOperatorLabel,
  isDerivedSpanQueryFilterActive,
  readDerivedSpanQueryFilter,
} from "./segments/derivedQueryCriterion";
import {
  readRawSegmentIdsFromUrl,
  readSceneSelectionCriterion,
  readSegmentsPageContentView,
  readStringCriterion,
  SEGMENT_CRITERIA,
} from "./segments/segmentCriteriaDefinitions";
import { buildSpanTitle } from "./segments/segmentDisplayUtils";
import { SegmentsPageList } from "./segments/SegmentsPageList";
import { useDerivedSpansQuery } from "./segments/useDerivedSpansQuery";
import { useRawSegmentsQuery } from "./segments/useRawSegmentsQuery";
import type {
  AppliedDerivedQuery,
  DerivedSpanItem,
  DerivedSpanOperandFilterValue,
  DerivedSpanQueryFilterValue,
  RawSegmentItem,
  SegmentsPageContentView,
} from "./segments/types";
import { LOCATION_CHANGE_EVENT, buildCurrentUrl, navigateToUrl } from "../router/location";

interface Props {
  onNavigate: (r: any) => void;
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

  const segmentsWindowQuery = useDerivedSpansQuery({
    activeProfileId,
    pageNumber,
    perPage,
    q,
    sceneTitle,
    sort,
    direction,
    includeSceneIds: sceneSelection.includeIds,
    excludeSceneIds: sceneSelection.excludeIds,
    appliedQuery,
    derivedQueryDescriptor: appliedQuery != null ? derivedQueryDescriptor : undefined,
    enabled: !isRawView && activeProfileId != null && (!derivedSpanQueryActive || !performerFaceQueriesLoading) && (sceneSelection.includeIds.length === 0 || !selectedScenesLoading),
  });

  const rawSegmentsQuery = useRawSegmentsQuery({
    pageNumber,
    perPage,
    q,
    sceneTitle,
    sort,
    direction,
    includeSceneIds: sceneSelection.includeIds,
    excludeSceneIds: sceneSelection.excludeIds,
    rawSegmentIds,
    enabled: isRawView,
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

  const customFilterSections = useMemo(
    () => (isRawView ? [] : [createDerivedSpanCustomFilterSection(sceneSelection.includeIds)]),
    [isRawView, sceneSelection.includeIds],
  );

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
        <SegmentsPageList
          displayMode={displayMode}
          isRawView={isRawView}
          rawItems={rawItems}
          spanItems={spanItems}
          rawSegmentIds={rawSegmentIds}
          appliedQuery={appliedQuery}
          isLoading={isLoading}
          canReadScenes={canReadScenes}
          onNavigate={onNavigate}
          onViewRawSegments={(segmentIds) => switchContentView("raw", segmentIds)}
          selectedIds={selectedIds}
          onToggle={toggle}
          selecting={selecting}
        />
      </ListPage>
    </>
  );
}
