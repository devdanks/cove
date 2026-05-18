import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Fingerprint, Link2, Merge, Pencil, Save, Search, Trash2, UserPlus } from "lucide-react";
import { faces, performers } from "../api/client";
import type { Face, FaceAppearance, FaceDeleteImpact, FaceSimilar, FaceSuggestion, FindFilter, PaginatedResponse, Performer } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { DetailListToolbar } from "../components/DetailListToolbar";
import { FaceSuggestionsPanel } from "../components/FaceSuggestionsPanel";
import { FaceCompareDialog } from "../components/FaceCompareDialog";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { EditModal } from "../components/EditModal";
import { EntityHeroLayout } from "../components/EntityHeroLayout";
import { EntityDetailTabs } from "../components/EntityDetailTabs";
import { FaceAppearanceTile, FaceTile } from "../components/EntityCards";
import { MetadataPanel } from "../components/MetadataPanel";
import { formatDate } from "../components/shared";
import { useDetailListQuery } from "../hooks/useDetailListQuery";
import { VirtualizedEntityGrid } from "../components/VirtualizedEntityLayouts";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type FaceTab = "overview" | "appearances" | "similar";
type FaceAppearanceListItem = FaceAppearance & { id: string | number };

const EMPTY_APPEARANCES_PAGE: PaginatedResponse<FaceAppearanceListItem> = { items: [], totalCount: 0, page: 1, perPage: 24 };
const EMPTY_SIMILAR_PAGE: PaginatedResponse<FaceSimilar> = { items: [], totalCount: 0, page: 1, perPage: 18 };
const APPEARANCE_SORT_OPTIONS = [
  { value: "last_seen", label: "Last Seen" },
  { value: "first_seen", label: "First Seen" },
  { value: "sample_count", label: "Frame Samples" },
  { value: "confidence", label: "Confidence" },
  { value: "host_type", label: "Host Type" },
  { value: "title", label: "Title" },
];
const SIMILAR_SORT_OPTIONS = [
  { value: "distance", label: "Closest Match" },
  { value: "appearance_count", label: "Most Appearances" },
  { value: "scene_count", label: "Most Scenes" },
  { value: "image_count", label: "Most Images" },
  { value: "updated_at", label: "Recently Updated" },
  { value: "label", label: "Name" },
];

function readSuggestionPerformerId(value: number | FaceSuggestion) {
  return typeof value === "number" ? value : value.performerId;
}

function canPromptForPerformerImage(face: Face, suggestion: FaceSuggestion) {
  const localPerformerId = suggestion.localPerformerId ?? (suggestion.performerId > 0 ? suggestion.performerId : undefined);
  return localPerformerId != null
    && !!face.coverImageUrl
    && suggestion.localPerformerHasImage === false
    && suggestion.localPerformerIsLocalOnly === true;
}

export function FaceDetailPage({ id, onNavigate }: Props) {
  const queryClient = useQueryClient();
  const { hasPermission, user } = useAuth();
  const canWriteFace = canWriteEntity("face", hasPermission);
  const canDeleteFace = canDeleteEntity("face", hasPermission);
  const canReadPerformers = canReadEntity("performer", hasPermission);
  const canEngageFace = canReadEntity("face", hasPermission) && (user?.kind === "user" || user?.kind === "system");
  const { backLabel, goBack } = useBackNavigation({ page: "faces" }, onNavigate);
  const [appearanceFilter, setAppearanceFilter] = useState<FindFilter>({ page: 1, perPage: 24, sort: "last_seen", direction: "desc" });
  const [similarFilter, setSimilarFilter] = useState<FindFilter>({ page: 1, perPage: 18, sort: "distance", direction: "asc" });
  const [appearanceZoomLevel, setAppearanceZoomLevel] = useState(0);
  const [similarZoomLevel, setSimilarZoomLevel] = useState(0);

  const { data: face, isLoading } = useQuery({
    queryKey: ["face", id],
    queryFn: () => faces.get(id),
  });

  const { data: similarFacesPage = EMPTY_SIMILAR_PAGE, isLoading: similarLoading, infinitePageSize: similarInfinitePageSize, infiniteQuery: similarInfiniteQuery, loadMore: loadMoreSimilar } = useDetailListQuery<FaceSimilar>({
    queryKey: ["face", id, "similar"],
    filter: similarFilter,
    queryFn: (nextFilter) => faces.similar(id, {
      q: nextFilter.q?.trim() || undefined,
      sort: nextFilter.sort,
      direction: nextFilter.direction,
      page: nextFilter.page ?? 1,
      perPage: nextFilter.perPage ?? 18,
      k: Math.max(80, (nextFilter.page ?? 1) * (nextFilter.perPage ?? 18) * 4),
    }),
  });

  const { data: faceAppearancesPage = EMPTY_APPEARANCES_PAGE, isLoading: appearancesLoading, infinitePageSize: appearancesInfinitePageSize, infiniteQuery: appearancesInfiniteQuery, loadMore: loadMoreAppearances } = useDetailListQuery<FaceAppearanceListItem>({
    queryKey: ["face", id, "appearances"],
    filter: appearanceFilter,
    queryFn: async (nextFilter) => {
      const page = await faces.appearances(id, {
        q: nextFilter.q?.trim() || undefined,
        sort: nextFilter.sort,
        direction: nextFilter.direction,
        page: nextFilter.page ?? 1,
        perPage: nextFilter.perPage ?? 24,
      });

      return { ...page, items: page.items.map((appearance) => ({ ...appearance, id: appearance.appearanceId })) };
    },
  });

  const { data: deleteImpact, isLoading: deleteImpactLoading } = useQuery({
    queryKey: ["face", id, "delete-impact"],
    queryFn: () => faces.deleteImpact(id),
    enabled: canDeleteFace,
  });

  const { data: faceSuggestions = [], isLoading: suggestionsLoading } = useQuery({
    queryKey: ["face", id, "suggestions"],
    queryFn: () => faces.suggestions(id),
    enabled: canWriteFace && face != null && face.performerId == null,
  });
  const { data: faceNavigationPage } = useQuery({
    queryKey: ["faces", "navigation"],
    queryFn: () => faces.list({ page: 1, perPage: 500, merged: false }),
  });

  const [label, setLabel] = useState("");
  const [primarySourceKey, setPrimarySourceKey] = useState("");
  const [performerSearch, setPerformerSearch] = useState("");
  const [mergeSearch, setMergeSearch] = useState("");
  const [activeTab, setActiveTab] = useState<FaceTab>("overview");
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isMergeModalOpen, setIsMergeModalOpen] = useState(false);
  const [isCreatePerformerModalOpen, setIsCreatePerformerModalOpen] = useState(false);
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);
  const [comparingSuggestion, setComparingSuggestion] = useState<FaceSuggestion | null>(null);
  const [newPerformerName, setNewPerformerName] = useState("");
  const [setNewPerformerImage, setSetNewPerformerImage] = useState(true);
  const labelInputRef = useRef<HTMLInputElement | null>(null);
  const mergeInputRef = useRef<HTMLInputElement | null>(null);

  const {
    favorite: faceFavorite,
    setFavorite: setFaceFavorite,
    favoritePending: faceFavoritePending,
  } = useEntityEngagement("face", id, {
    enabled: canEngageFace,
  });

  useEffect(() => {
    if (!face) {
      return;
    }

    setLabel(face.label ?? "");
    setPrimarySourceKey(face.primarySourceKey ?? "");
  }, [face]);

  useEffect(() => {
    if (!isEditModalOpen) {
      return;
    }

    window.setTimeout(() => labelInputRef.current?.focus(), 0);
  }, [isEditModalOpen]);

  useEffect(() => {
    if (!isMergeModalOpen) {
      return;
    }

    window.setTimeout(() => mergeInputRef.current?.focus(), 0);
  }, [isMergeModalOpen]);

  useEffect(() => {
    if (!isCreatePerformerModalOpen) {
      return;
    }

    setNewPerformerName((current) => current.trim() || face?.label?.trim() || "");
  }, [face?.label, isCreatePerformerModalOpen]);

  const performerSearchTerm = performerSearch.trim();
  const mergeSearchTerm = mergeSearch.trim();
  const normalizedNewPerformerName = newPerformerName.trim();

  const performerMatchesQuery = useQuery({
    queryKey: ["face", id, "performer-search", performerSearchTerm],
    queryFn: () => performers.find({ q: performerSearchTerm, page: 1, perPage: 6 }),
    enabled: canWriteFace && performerSearchTerm.length >= 2,
  });

  const mergeMatchesQuery = useQuery({
    queryKey: ["face", id, "merge-search", mergeSearchTerm],
    queryFn: () => faces.list({ q: mergeSearchTerm, merged: false, page: 1, perPage: 6 }),
    enabled: canWriteFace && mergeSearchTerm.length >= 2,
  });

  const invalidateFace = (updated?: Face) => {
    queryClient.invalidateQueries({ queryKey: ["face", id] });
    queryClient.invalidateQueries({ queryKey: ["face", id, "appearances"] });
    queryClient.invalidateQueries({ queryKey: ["face", id, "suggestions"] });
    queryClient.invalidateQueries({ queryKey: ["face", id, "similar"] });
    queryClient.invalidateQueries({ queryKey: ["faces"] });
    if (updated?.performerId != null) {
      queryClient.invalidateQueries({ queryKey: ["performer", updated.performerId] });
    }
    if (face?.performerId != null) {
      queryClient.invalidateQueries({ queryKey: ["performer", face.performerId] });
    }
  };

  const updateMutation = useMutation({
    mutationFn: (data: { label?: string; primarySourceKey?: string }) =>
      faces.update(id, {
        label: data.label,
        performerId: face?.performerId,
        primarySourceKey: data.primarySourceKey,
        ignored: face?.ignored ?? false,
      }),
    onSuccess: (updated) => {
      setIsEditModalOpen(false);
      invalidateFace(updated);
    },
  });

  const linkMutation = useMutation({
    mutationFn: (performerId?: number) => faces.link(id, { performerId }),
    onSuccess: (updated) => {
      setPerformerSearch("");
      invalidateFace(updated);
    },
  });

  const mergeMutation = useMutation({
    mutationFn: (targetFaceId: number) => faces.mergeInto(id, { targetFaceId }),
    onSuccess: (updated) => {
      setIsMergeModalOpen(false);
      invalidateFace(updated);
      if (updated.mergedIntoFaceId != null) {
        onNavigate({ page: "face", id: updated.mergedIntoFaceId });
      }
    },
  });

  const deleteMutation = useMutation({
    mutationFn: () => faces.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["faces"] });
      goBack();
    },
  });

  const suggestionDecisionMutation = useMutation({
    mutationFn: (data: { performerId: number; decision: "accept" | "reject"; setPerformerImage?: boolean }) => faces.recordSuggestionDecision(id, data),
    onSuccess: () => {
      invalidateFace();
    },
  });

  const createPerformerMutation = useMutation({
    mutationFn: () => faces.createPerformer(id, { name: normalizedNewPerformerName, setPerformerImage: setNewPerformerImage }),
    onSuccess: (updated) => {
      setIsCreatePerformerModalOpen(false);
      setNewPerformerName("");
      setSetNewPerformerImage(true);
      invalidateFace(updated);
      queryClient.invalidateQueries({ queryKey: ["performers"] });
      if (updated.performerId != null) {
        queryClient.invalidateQueries({ queryKey: ["performer", updated.performerId] });
      }
    },
  });

  const normalizedLabel = label.trim() || undefined;
  const normalizedPrimarySourceKey = primarySourceKey.trim() || undefined;
  const hasMetadataChanges = useMemo(() => {
    if (!face) {
      return false;
    }

    return (face.label ?? "") !== (normalizedLabel ?? "") || (face.primarySourceKey ?? "") !== (normalizedPrimarySourceKey ?? "");
  }, [face, normalizedLabel, normalizedPrimarySourceKey]);

  const mergeCandidates = (mergeMatchesQuery.data?.items ?? []).filter((candidate) => candidate.id !== id);
  const performerMatches = performerMatchesQuery.data?.items ?? [];
  const orderedNavigationFaces = useMemo(
    () => [...(faceNavigationPage?.items ?? [])].sort((left, right) => left.id - right.id),
    [faceNavigationPage?.items],
  );
  const navigationIndex = orderedNavigationFaces.findIndex((candidate) => candidate.id === id);
  const previousFace = navigationIndex > 0 ? orderedNavigationFaces[navigationIndex - 1] : undefined;
  const nextFace = navigationIndex >= 0 ? orderedNavigationFaces[navigationIndex + 1] : undefined;
  const title = face?.label?.trim() || face?.performerName || `Face #${id}`;
  const tabs = useMemo(() => [
    { key: "overview", label: "Overview" },
    { key: "appearances", label: "Appears In", count: face?.appearanceCount || faceAppearancesPage.totalCount },
    { key: "similar", label: "Similar Faces", count: similarFacesPage.totalCount },
  ], [face?.appearanceCount, faceAppearancesPage.totalCount, similarFacesPage.totalCount]);

  const faceKeyboardShortcuts = useMemo(() => [
    {
      key: "e",
      description: "Edit face metadata",
      handler: () => {
        if (!canWriteFace) {
          return;
        }

        setIsEditModalOpen(true);
      },
    },
    {
      key: "m",
      description: "Open merge face dialog",
      handler: () => {
        if (!canWriteFace) {
          return;
        }

        setIsMergeModalOpen(true);
      },
    },
    {
      key: "o",
      description: "Toggle favorite",
      handler: () => {
        if (!canEngageFace) {
          return;
        }

        setFaceFavorite(!faceFavorite);
      },
    },
    {
      key: "[",
      description: "Open previous face",
      handler: () => {
        if (previousFace) {
          onNavigate({ page: "face", id: previousFace.id });
        }
      },
    },
    {
      key: "]",
      description: "Open next face",
      handler: () => {
        if (nextFace) {
          onNavigate({ page: "face", id: nextFace.id });
        }
      },
    },
  ], [canEngageFace, canWriteFace, faceFavorite, nextFace, onNavigate, previousFace, setFaceFavorite]);

  useEffect(() => {
    if (!face) {
      return;
    }

    document.title = `${title} | Cove`;
    return () => {
      document.title = "Cove";
    };
  }, [face, title]);

  if (isLoading) {
    return (
      <div className="px-1 py-2">
        <DetailSkeleton />
      </div>
    );
  }

  if (!face) {
    return <div className="py-16 text-center text-secondary">Face not found</div>;
  }

  const deleteImpactSummary = deleteImpactLoading
    ? "Loading delete impact..."
    : deleteImpact
      ? describeFaceDeleteImpact(deleteImpact)
      : "Delete this face cluster and remove the AI artifacts it owns.";

  const overviewContent = (
    <div className="space-y-6">
      {face.mergedIntoFaceId != null ? (
        <section className="rounded-2xl border border-border bg-card/70 p-5">
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Merged State</h2>
          <p className="mt-2 text-sm text-secondary">This face has already been merged into another primary face cluster.</p>
          <button
            type="button"
            onClick={() => onNavigate({ page: "face", id: face.mergedIntoFaceId })}
            className="mt-3 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
          >
            Open face #{face.mergedIntoFaceId}
          </button>
        </section>
      ) : null}

      <section className="grid gap-6 xl:grid-cols-[minmax(0,1.15fr),minmax(18rem,0.85fr)]">
        <div className="space-y-6">
          <section className="rounded-2xl border border-border bg-card/70 p-5">
            <div className="flex items-start justify-between gap-3">
              <div>
                <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Linked Performer</h2>
                <p className="mt-1 text-sm text-secondary">Keep the cluster linked to a performer and review the best candidate matches here.</p>
              </div>
              {face.performerId ? <StatusPill icon={<Link2 className="h-3 w-3" />} label="Linked" tone="accent" /> : null}
            </div>

            {face.performerId ? (
              <div className="mt-4 space-y-3">
                <button
                  type="button"
                  onClick={() => canReadPerformers && onNavigate({ page: "performer", id: face.performerId })}
                  className={`text-left text-base font-medium ${canReadPerformers ? "text-accent hover:underline" : "text-foreground"}`}
                >
                  {face.performerName || `Performer #${face.performerId}`}
                </button>
                {canWriteFace ? (
                  <button
                    type="button"
                    onClick={() => linkMutation.mutate(undefined)}
                    disabled={linkMutation.isPending}
                    className="rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {linkMutation.isPending ? "Saving..." : "Unlink performer"}
                  </button>
                ) : null}
              </div>
            ) : (
              <div className="mt-4 flex flex-wrap items-center gap-3">
                <p className="text-sm text-secondary">No performer is linked to this face cluster yet.</p>
                {canWriteFace ? (
                  <button
                    type="button"
                    onClick={() => setIsCreatePerformerModalOpen(true)}
                    className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
                  >
                    <UserPlus className="h-4 w-4" />
                    Create Performer
                  </button>
                ) : null}
              </div>
            )}

            {canWriteFace ? (
              <div className="mt-5 space-y-3 border-t border-border pt-4">
                <label className="block text-xs font-semibold uppercase tracking-wide text-muted">Link to performer</label>
                <div className="relative">
                  <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
                  <input
                    type="text"
                    value={performerSearch}
                    onChange={(event) => setPerformerSearch(event.target.value)}
                    placeholder="Search performers"
                    className="w-full rounded-lg border border-border bg-input py-2 pl-9 pr-3 text-sm text-foreground outline-none focus:border-accent"
                  />
                </div>
                {performerSearchTerm.length < 2 ? (
                  <p className="text-xs text-secondary">Type at least two characters to search performers.</p>
                ) : performerMatchesQuery.isLoading ? (
                  <p className="text-xs text-secondary">Searching performers...</p>
                ) : performerMatches.length === 0 ? (
                  <p className="text-xs text-secondary">No performers matched that search.</p>
                ) : (
                  <div className="space-y-2">
                    {performerMatches.map((performer) => (
                      <PerformerCandidateRow
                        key={performer.id}
                        performer={performer}
                        onSelect={() => linkMutation.mutate(performer.id)}
                        disabled={linkMutation.isPending}
                      />
                    ))}
                  </div>
                )}
              </div>
            ) : null}
          </section>

          {!face.performerId && canWriteFace ? (
            <section className="space-y-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Suggested Matches</h2>
                  <p className="mt-1 text-sm text-secondary">Review suggested performer links before accepting or rejecting them.</p>
                </div>
                <StatusPill icon={<Link2 className="h-3 w-3" />} label={`${faceSuggestions.length} candidates`} tone="muted" />
              </div>
              <div>
                <FaceSuggestionsPanel
                  face={face}
                  suggestions={faceSuggestions}
                  isLoading={suggestionsLoading}
                  disabled={suggestionDecisionMutation.isPending}
                  canReadPerformers={canReadPerformers}
                  onAccept={(value) => {
                    if (canPromptForPerformerImage(face, value)) {
                      setComparingSuggestion(value);
                      return;
                    }

                    suggestionDecisionMutation.mutate({ performerId: readSuggestionPerformerId(value), decision: "accept" });
                  }}
                  onReject={(value) => suggestionDecisionMutation.mutate({ performerId: readSuggestionPerformerId(value), decision: "reject" })}
                  onCompare={(value) => setComparingSuggestion(value)}
                  onNavigate={onNavigate}
                />
              </div>
            </section>
          ) : null}
        </div>

        <div className="space-y-6">
          <MetadataPanel
            title="Cluster Details"
            items={[
              { label: "Face ID", value: `#${face.id}` },
              { label: "Primary source", value: face.primarySourceKey || "Unknown" },
              { label: "Created", value: formatDate(face.createdAt) },
              { label: "Updated", value: formatDate(face.updatedAt) },
              { label: "Appearances", value: face.appearanceCount || faceAppearancesPage.totalCount },
              { label: "Scenes", value: face.sceneCount },
              { label: "Images", value: face.imageCount },
              { label: "Performer", value: face.performerName || "Unlinked" },
            ]}
          />
        </div>
      </section>
    </div>
  );

  const appearancesContent = (
    <section className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Appears In</h2>
          <p className="mt-1 text-sm text-secondary">Scenes and images where this face appears.</p>
        </div>
        <div className="text-xs text-muted">{faceAppearancesPage.totalCount} appearance{faceAppearancesPage.totalCount === 1 ? "" : "s"}</div>
      </div>

      {appearancesLoading ? (
        <div className="text-sm text-secondary">Loading appearances...</div>
      ) : faceAppearancesPage.totalCount === 0 ? (
        <div className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-secondary">
          No appearances currently point to this face cluster.
        </div>
      ) : (
        <>
          <DetailListToolbar
            filter={appearanceFilter}
            onFilterChange={setAppearanceFilter}
            totalCount={faceAppearancesPage.totalCount}
            sortOptions={APPEARANCE_SORT_OPTIONS}
            zoomLevel={appearanceZoomLevel}
            onZoomChange={setAppearanceZoomLevel}
            showSearch
            allowInfinitePageSize
          />
          <FaceAppearancesGrid appearances={faceAppearancesPage.items} onNavigate={onNavigate} zoomLevel={appearanceZoomLevel} infinitePageSize={appearancesInfinitePageSize} hasNextPage={appearancesInfiniteQuery.hasNextPage} isFetchingNextPage={appearancesInfiniteQuery.isFetchingNextPage} loadMore={loadMoreAppearances} />
          {!appearancesInfinitePageSize ? <FaceTabPager filter={appearanceFilter} setFilter={setAppearanceFilter} totalCount={faceAppearancesPage.totalCount} /> : null}
        </>
      )}
    </section>
  );

  const similarFacesContent = (
    <section className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Similar Faces</h2>
          <p className="mt-1 text-sm text-secondary">Nearest neighbors from the face embedding index.</p>
        </div>
        <div className="text-xs text-muted">{similarFacesPage.totalCount} match{similarFacesPage.totalCount === 1 ? "" : "es"}</div>
      </div>

      {similarLoading ? (
        <div className="text-sm text-secondary">Loading similar faces...</div>
      ) : similarFacesPage.totalCount === 0 ? (
        <div className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-secondary">
          No similar faces are available for this cluster yet.
        </div>
      ) : (
        <>
          <DetailListToolbar
            filter={similarFilter}
            onFilterChange={setSimilarFilter}
            totalCount={similarFacesPage.totalCount}
            sortOptions={SIMILAR_SORT_OPTIONS}
            zoomLevel={similarZoomLevel}
            onZoomChange={setSimilarZoomLevel}
            showSearch
            allowInfinitePageSize
          />
          <VirtualizedEntityGrid items={similarFacesPage.items} getItemKey={(candidate) => candidate.id} minCardWidth={`${280 + similarZoomLevel * 50}px`} virtualMinColumnWidth={280 + similarZoomLevel * 50} estimateRowHeight={360} gap={16} gapClassName="gap-4" infinitePageSize={similarInfinitePageSize} hasNextPage={similarInfiniteQuery.hasNextPage} isFetchingNextPage={similarInfiniteQuery.isFetchingNextPage} loadMore={loadMoreSimilar} renderItem={(candidate) => (
            <SimilarFaceTile face={candidate} onNavigate={onNavigate} canReadPerformers={canReadPerformers} />
          )} />
          {!similarInfinitePageSize ? <FaceTabPager filter={similarFilter} setFilter={setSimilarFilter} totalCount={similarFacesPage.totalCount} /> : null}
        </>
      )}
    </section>
  );

  const activeTabContent = activeTab === "overview"
    ? overviewContent
    : activeTab === "appearances"
      ? appearancesContent
      : similarFacesContent;

  const faceActions = (
    <>
      {previousFace ? (
        <button
          type="button"
          onClick={() => onNavigate({ page: "face", id: previousFace.id })}
          className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
        >
          <ChevronLeft className="h-4 w-4" />
          Prev Face
        </button>
      ) : null}
      {nextFace ? (
        <button
          type="button"
          onClick={() => onNavigate({ page: "face", id: nextFace.id })}
          className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
        >
          Next Face
          <ChevronRight className="h-4 w-4" />
        </button>
      ) : null}
      {canWriteFace ? (
        <>
          <button
            type="button"
            onClick={() => setIsEditModalOpen(true)}
            className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
          >
            <Pencil className="h-4 w-4" />
            Edit
          </button>
          <button
            type="button"
            onClick={() => setIsMergeModalOpen(true)}
            className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
          >
            <Merge className="h-4 w-4" />
            Merge
          </button>
          {!face.performerId ? (
            <button
              type="button"
              onClick={() => setIsCreatePerformerModalOpen(true)}
              className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
            >
              <UserPlus className="h-4 w-4" />
              Create Performer
            </button>
          ) : null}
        </>
      ) : null}
      {canDeleteFace ? (
        <button
          type="button"
          onClick={() => setIsDeleteDialogOpen(true)}
          disabled={deleteMutation.isPending}
          className="inline-flex items-center gap-2 rounded-full border border-red-500/40 px-3 py-2 text-xs font-medium text-red-200 transition hover:border-red-400 hover:text-red-100 disabled:cursor-not-allowed disabled:opacity-50"
        >
          <Trash2 className="h-4 w-4" />
          {deleteMutation.isPending ? "Deleting..." : "Delete"}
        </button>
      ) : null}
    </>
  );

  return (
    <>
      <EntityHeroLayout
        backLabel={backLabel}
        onGoBack={goBack}
        imageUrl={face.coverImageUrl}
        imageAlt={title}
        imageFallback={<Fingerprint className="h-14 w-14" />}
        title={title}
        counts={[
          { key: "appearances", label: "Appearances", value: face.appearanceCount || faceAppearancesPage.totalCount },
          { key: "scenes", label: "Scenes", value: face.sceneCount },
          { key: "images", label: "Images", value: face.imageCount },
        ]}
        metaRow={(
          <>
            <span>Face cluster #{face.id}</span>
            <span>Primary source {face.primarySourceKey || "Unknown"}</span>
            <span>Created {formatDate(face.createdAt)}</span>
            <span>Updated {formatDate(face.updatedAt)}</span>
          </>
        )}
        favorite={canEngageFace ? faceFavorite : undefined}
        onFavoriteToggle={canEngageFace && !faceFavoritePending ? () => setFaceFavorite(!faceFavorite) : undefined}
        actions={faceActions}
      >
        <EntityDetailTabs tabs={tabs} activeTab={activeTab} onTabChange={(key) => setActiveTab(key as FaceTab)} className="mx-auto mb-4 max-w-7xl" />
        {activeTabContent}
      </EntityHeroLayout>

      <EditModal open={isEditModalOpen} onClose={() => setIsEditModalOpen(false)} title={`Edit ${title}`}>
        <div className="space-y-4 py-5">
          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Label
            <input
              ref={labelInputRef}
              type="text"
              value={label}
              onChange={(event) => setLabel(event.target.value)}
              placeholder="Optional face label"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>
          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Primary source key
            <input
              type="text"
              value={primarySourceKey}
              onChange={(event) => setPrimarySourceKey(event.target.value)}
              placeholder="detector.source"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>
          <div className="flex justify-end">
            <button
              type="button"
              onClick={() => updateMutation.mutate({ label: normalizedLabel, primarySourceKey: normalizedPrimarySourceKey })}
              disabled={!hasMetadataChanges || updateMutation.isPending}
              className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Save className="h-4 w-4" />
              {updateMutation.isPending ? "Saving..." : "Save metadata"}
            </button>
          </div>
        </div>
      </EditModal>

      <EditModal open={isMergeModalOpen} onClose={() => setIsMergeModalOpen(false)} title={`Merge ${title}`}>
        <div className="space-y-4 py-5">
          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">Merge into another face</label>
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
            <input
              ref={mergeInputRef}
              type="text"
              value={mergeSearch}
              onChange={(event) => setMergeSearch(event.target.value)}
              placeholder="Search primary faces"
              className="w-full rounded-lg border border-border bg-input py-2 pl-9 pr-3 text-sm text-foreground outline-none focus:border-accent"
            />
          </div>
          {mergeSearchTerm.length < 2 ? (
            <p className="text-sm text-secondary">Type at least two characters to search merge targets.</p>
          ) : mergeMatchesQuery.isLoading ? (
            <p className="text-sm text-secondary">Searching faces...</p>
          ) : mergeCandidates.length === 0 ? (
            <p className="text-sm text-secondary">No merge targets matched that search.</p>
          ) : (
            <div className="space-y-2">
              {mergeCandidates.map((candidate) => (
                <FaceCandidateRow
                  key={candidate.id}
                  face={candidate}
                  onSelect={() => mergeMutation.mutate(candidate.id)}
                  disabled={mergeMutation.isPending}
                />
              ))}
            </div>
          )}
        </div>
      </EditModal>

      <EditModal open={isCreatePerformerModalOpen} onClose={() => setIsCreatePerformerModalOpen(false)} title={`Create performer from ${title}`}>
        <div className="space-y-4 py-5">
          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Performer name
            <input
              type="text"
              value={newPerformerName}
              onChange={(event) => setNewPerformerName(event.target.value)}
              placeholder="Name"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
              autoFocus
            />
          </label>
          <label className="flex items-center gap-2 text-sm text-secondary">
            <input
              type="checkbox"
              checked={setNewPerformerImage}
              onChange={(event) => setSetNewPerformerImage(event.target.checked)}
              className="rounded border-border bg-surface accent-accent"
            />
            Use this face as the performer image
          </label>
          {createPerformerMutation.error ? (
            <p className="rounded-lg border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-200">{String(createPerformerMutation.error.message ?? createPerformerMutation.error)}</p>
          ) : null}
          <div className="flex justify-end">
            <button
              type="button"
              onClick={() => createPerformerMutation.mutate()}
              disabled={!normalizedNewPerformerName || createPerformerMutation.isPending}
              className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
            >
              <UserPlus className="h-4 w-4" />
              {createPerformerMutation.isPending ? "Creating..." : "Create performer"}
            </button>
          </div>
        </div>
      </EditModal>

      <ConfirmDialog
        open={isDeleteDialogOpen}
        title={`Delete ${title}?`}
        message={`${deleteImpactSummary} This cannot be undone.`}
        confirmLabel="Delete face"
        onCancel={() => setIsDeleteDialogOpen(false)}
        onConfirm={() => {
          setIsDeleteDialogOpen(false);
          deleteMutation.mutate();
        }}
      />

      <FaceCompareDialog
        open={comparingSuggestion != null}
        face={face ?? null}
        suggestion={comparingSuggestion}
        disabled={suggestionDecisionMutation.isPending}
        canReadPerformers={canReadPerformers}
        onClose={() => setComparingSuggestion(null)}
        onConfirm={(value, options) => {
          if ("performerId" in value) {
            suggestionDecisionMutation.mutate({ performerId: value.performerId, decision: "accept", setPerformerImage: options?.setPerformerImage });
          }
          setComparingSuggestion(null);
        }}
        onReject={(value) => {
          if ("performerId" in value) {
            suggestionDecisionMutation.mutate({ performerId: value.performerId, decision: "reject" });
          }
          setComparingSuggestion(null);
        }}
        onNavigate={onNavigate}
      />
    </>
  );
}

function StatusPill({ icon, label, tone }: { icon: React.ReactNode; label: string; tone: "muted" | "accent" }) {
  const toneClassName = tone === "accent"
    ? "border-accent/30 bg-accent/10 text-accent"
    : "border-border bg-surface/70 text-secondary";

  return (
    <span className={`inline-flex items-center gap-1 rounded-full border px-2.5 py-1 ${toneClassName}`}>
      {icon}
      {label}
    </span>
  );
}

function PerformerCandidateRow({ performer, onSelect, disabled }: { performer: Performer; onSelect: () => void; disabled: boolean }) {
  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled}
      className="flex w-full items-center justify-between gap-3 rounded-xl border border-border bg-surface/60 px-3 py-3 text-left transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
    >
      <div>
        <div className="text-sm font-medium text-foreground">{performer.name}</div>
        <div className="mt-1 text-xs text-secondary">{performer.sceneCount ?? 0} scenes</div>
      </div>
      <span className="text-xs text-accent">Link</span>
    </button>
  );
}

function FaceCandidateRow({ face, onSelect, disabled }: { face: Face; onSelect: () => void; disabled: boolean }) {
  const title = face.label?.trim() || face.performerName || `Face #${face.id}`;

  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled}
      className="flex w-full items-center gap-3 rounded-xl border border-border bg-surface/60 px-3 py-3 text-left transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
    >
      <div className="h-14 w-14 overflow-hidden rounded-xl bg-surface/90">
        {face.coverImageUrl ? (
          <img src={face.coverImageUrl} alt={title} className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <div className="flex h-full w-full items-center justify-center text-muted">
            <Fingerprint className="h-5 w-5" />
          </div>
        )}
      </div>
      <div className="min-w-0 flex-1">
        <div className="truncate text-sm font-medium text-foreground">{title}</div>
        <div className="mt-1 text-xs text-secondary">{face.detectionCount} detections</div>
      </div>
      <span className="text-xs text-accent">Merge</span>
    </button>
  );
}

function FaceAppearancesGrid({ appearances, onNavigate, zoomLevel, infinitePageSize, hasNextPage, isFetchingNextPage, loadMore }: { appearances: FaceAppearanceListItem[]; onNavigate: (r: any) => void; zoomLevel: number; infinitePageSize: boolean; hasNextPage?: boolean; isFetchingNextPage?: boolean; loadMore: () => void }) {
  return (
    <VirtualizedEntityGrid items={appearances} getItemKey={(appearance) => appearance.appearanceId} minCardWidth={`${220 + zoomLevel * 50}px`} virtualMinColumnWidth={220 + zoomLevel * 50} estimateRowHeight={280} gap={16} gapClassName="gap-4" infinitePageSize={infinitePageSize} hasNextPage={hasNextPage} isFetchingNextPage={isFetchingNextPage} loadMore={loadMore} renderItem={(appearance) => (
      <FaceAppearanceTile appearance={appearance} onClick={() => onNavigate({ page: appearance.hostType, id: appearance.hostId })} />
    )} />
  );
}

function describeFaceDeleteImpact(deleteImpact: FaceDeleteImpact) {
  const coverImageSummary = deleteImpact.hasCoverImage ? "1 cover image" : "no cover image";
  const summary = `Deletes ${formatCount(deleteImpact.detectionCount, "detection")}, ${formatCount(deleteImpact.embeddingCount, "embedding")}, ${formatCount(deleteImpact.segmentCount, "timeline segment")}, and ${coverImageSummary}.`;

  if (deleteImpact.releasedMergedFaceCount === 0) {
    return summary;
  }

  return `${summary} Reopens ${formatCount(deleteImpact.releasedMergedFaceCount, "merged face", "merged faces")} as standalone ${deleteImpact.releasedMergedFaceCount === 1 ? "cluster" : "clusters"}.`;
}

function formatCount(count: number, singular: string, plural = `${singular}s`) {
  return `${count} ${count === 1 ? singular : plural}`;
}

function FaceTabPager({ filter, setFilter, totalCount }: { filter: FindFilter; setFilter: (filter: FindFilter) => void; totalCount: number }) {
  const perPage = filter.perPage ?? 1;
  const page = filter.page ?? 1;
  const totalPages = Math.max(1, Math.ceil(totalCount / perPage));

  if (totalPages <= 1) {
    return null;
  }

  return (
    <div className="mx-auto mt-6 flex max-w-7xl items-center justify-center gap-4">
      <button
        type="button"
        disabled={page <= 1}
        onClick={() => setFilter({ ...filter, page: page - 1 })}
        className="rounded-lg border border-border px-3 py-2 text-sm text-secondary transition-colors hover:border-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-40"
      >
        Previous
      </button>
      <span className="text-sm text-secondary">Page {page} of {totalPages}</span>
      <button
        type="button"
        disabled={page >= totalPages}
        onClick={() => setFilter({ ...filter, page: page + 1 })}
        className="rounded-lg border border-border px-3 py-2 text-sm text-secondary transition-colors hover:border-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-40"
      >
        Next
      </button>
    </div>
  );
}

function SimilarFaceTile({ face, onNavigate, canReadPerformers }: { face: FaceSimilar; onNavigate: (r: any) => void; canReadPerformers: boolean }) {
  return (
    <FaceTile face={face} onClick={() => onNavigate({ page: "face", id: face.id })}>
      <div className="rounded-xl border border-border bg-surface/50 p-3">
          <div className="text-[11px] font-semibold uppercase tracking-wide text-muted">Closest match</div>
          <div className="mt-1 text-sm font-medium text-foreground">Distance {face.distance.toFixed(3)}</div>
          {face.performerId ? (
            <button
              type="button"
              onClick={() => canReadPerformers && onNavigate({ page: "performer", id: face.performerId })}
              className={`mt-2 text-left text-xs ${canReadPerformers ? "text-accent hover:underline" : "text-secondary"}`}
            >
              {face.performerName || `Performer #${face.performerId}`}
            </button>
          ) : null}
      </div>
    </FaceTile>
  );
}
