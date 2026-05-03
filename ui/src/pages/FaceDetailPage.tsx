import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, ChevronRight, Fingerprint, Image as ImageIcon, Link2, Merge, Pencil, Save, Search, Trash2, Video } from "lucide-react";
import { faces, images, performers, scenes } from "../api/client";
import type { Detection, Face, FaceDeleteImpact, FaceSuggestion, Performer } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { FaceSuggestionsPanel } from "../components/FaceSuggestionsPanel";
import { FaceCompareDialog } from "../components/FaceCompareDialog";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { EditModal } from "../components/EditModal";
import { EntityHeroLayout } from "../components/EntityHeroLayout";
import { MetadataPanel } from "../components/MetadataPanel";
import { formatDate } from "../components/shared";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type FaceTab = "overview" | "detections" | "similar";

function readSuggestionPerformerId(value: number | FaceSuggestion) {
  return typeof value === "number" ? value : value.performerId;
}

export function FaceDetailPage({ id, onNavigate }: Props) {
  const queryClient = useQueryClient();
  const { hasPermission, user } = useAuth();
  const canWriteFace = canWriteEntity("face", hasPermission);
  const canDeleteFace = canDeleteEntity("face", hasPermission);
  const canReadPerformers = canReadEntity("performer", hasPermission);
  const canEngageFace = canReadEntity("face", hasPermission) && (user?.kind === "user" || user?.kind === "system");
  const { backLabel, goBack } = useBackNavigation({ page: "faces" }, onNavigate);

  const { data: face, isLoading } = useQuery({
    queryKey: ["face", id],
    queryFn: () => faces.get(id),
  });

  const { data: similarFaces = [], isLoading: similarLoading } = useQuery({
    queryKey: ["face", id, "similar"],
    queryFn: () => faces.similar(id, { k: 12 }),
  });

  const { data: faceDetections = [], isLoading: detectionsLoading } = useQuery({
    queryKey: ["face", id, "detections"],
    queryFn: () => faces.detections(id),
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
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false);
  const [comparingSuggestion, setComparingSuggestion] = useState<FaceSuggestion | null>(null);
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

  const performerSearchTerm = performerSearch.trim();
  const mergeSearchTerm = mergeSearch.trim();

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
    queryClient.invalidateQueries({ queryKey: ["face", id, "detections"] });
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
    mutationFn: (data: { performerId: number; decision: "accept" | "reject" }) => faces.recordSuggestionDecision(id, data),
    onSuccess: () => {
      invalidateFace();
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
    { key: "detections", label: "Detections", count: faceDetections.length },
    { key: "similar", label: "Similar Faces", count: similarFaces.length },
  ], [faceDetections.length, similarFaces.length]);

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
              <p className="mt-4 text-sm text-secondary">No performer is linked to this face cluster yet.</p>
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
                  onAccept={(value) => suggestionDecisionMutation.mutate({ performerId: readSuggestionPerformerId(value), decision: "accept" })}
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
              { label: "Detections", value: face.detectionCount },
              { label: "Scenes", value: face.sceneCount },
              { label: "Images", value: face.imageCount },
              { label: "Performer", value: face.performerName || "Unlinked" },
            ]}
          />
        </div>
      </section>
    </div>
  );

  const detectionsContent = (
    <section className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Appears In</h2>
          <p className="mt-1 text-sm text-secondary">Scenes and images where this face has been detected.</p>
        </div>
        <div className="text-xs text-muted">{faceDetections.length} detection{faceDetections.length === 1 ? "" : "s"}</div>
      </div>

      {detectionsLoading ? (
        <div className="text-sm text-secondary">Loading detections...</div>
      ) : faceDetections.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-secondary">
          No detections currently point to this face cluster.
        </div>
      ) : (
        <FaceDetectionsByHost detections={faceDetections} onNavigate={onNavigate} />
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
        <div className="text-xs text-muted">{similarFaces.length} match{similarFaces.length === 1 ? "" : "es"}</div>
      </div>

      {similarLoading ? (
        <div className="text-sm text-secondary">Loading similar faces...</div>
      ) : similarFaces.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-secondary">
          No similar faces are available for this cluster yet.
        </div>
      ) : (
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {similarFaces.map((candidate) => (
            <SimilarFaceCard key={candidate.id} face={candidate} onNavigate={onNavigate} canReadPerformers={canReadPerformers} />
          ))}
        </div>
      )}
    </section>
  );

  const activeTabContent = activeTab === "overview"
    ? overviewContent
    : activeTab === "detections"
      ? detectionsContent
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
        aliases={`Face cluster #${face.id} · Primary source ${face.primarySourceKey || "Unknown"}`}
        counts={[
          { key: "detections", label: "Detections", value: face.detectionCount },
          { key: "scenes", label: "Scenes", value: face.sceneCount },
          { key: "images", label: "Images", value: face.imageCount },
        ]}
        metaRow={(
          <>
            <span>Created {formatDate(face.createdAt)}</span>
            <span>Updated {formatDate(face.updatedAt)}</span>
          </>
        )}
        favorite={canEngageFace ? faceFavorite : undefined}
        onFavoriteToggle={canEngageFace && !faceFavoritePending ? () => setFaceFavorite(!faceFavorite) : undefined}
        actions={faceActions}
      >
        <div className="mb-4 border-b border-border">
          <div className="flex flex-wrap gap-1">
            {tabs.map((tab) => (
              <button
                key={tab.key}
                type="button"
                onClick={() => setActiveTab(tab.key as FaceTab)}
                className={`px-2.5 py-2 text-sm transition-colors border-b-2 cursor-pointer ${activeTab === tab.key ? "border-accent text-accent" : "border-transparent text-secondary hover:text-foreground"}`}
              >
                {tab.label}
                {typeof tab.count === "number" ? <span className="ml-1 text-xs text-muted">({tab.count})</span> : null}
              </button>
            ))}
          </div>
        </div>
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
        onConfirm={(value) => {
          if ("performerId" in value) {
            suggestionDecisionMutation.mutate({ performerId: value.performerId, decision: "accept" });
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

function FaceDetectionCard({ detection, onNavigate }: { detection: Detection; onNavigate: (r: any) => void }) {
  const hostLabel = detection.hostType === "image" ? `Image #${detection.hostId}` : `Scene #${detection.hostId}`;
  const previewUrl = detection.hostType === "image"
    ? images.thumbnailUrl(detection.hostId, 480)
    : scenes.screenshotUrl(detection.hostId);
  const openHost = () => onNavigate({ page: detection.hostType, id: detection.hostId });

  return (
    <article className="overflow-hidden rounded-2xl border border-border bg-surface/60">
      <button
        type="button"
        onClick={openHost}
        className="block aspect-[4/3] w-full bg-surface/80 text-left"
        aria-label={`Open ${hostLabel}`}
      >
        <img
          src={previewUrl}
          alt={hostLabel}
          className="h-full w-full object-cover"
          loading="lazy"
          onError={(event) => {
            const target = event.target as HTMLImageElement;
            target.style.display = "none";
            const fallback = target.nextElementSibling as HTMLElement | null;
            if (fallback) {
              fallback.style.display = "flex";
            }
          }}
        />
        <div className="hidden h-full w-full items-center justify-center text-muted">
          <Fingerprint className="h-10 w-10" />
        </div>
      </button>
      <div className="space-y-1.5 p-4 text-sm text-secondary">
        <button type="button" onClick={openHost} className="text-left font-medium text-foreground hover:text-accent">
          {hostLabel}
        </button>
        <div className="text-xs">{Math.round(detection.score * 100)}% confidence</div>
        <div className="text-xs">{formatDetectionSubtitle(detection)}</div>
      </div>
    </article>
  );
}

function FaceDetectionsByHost({ detections, onNavigate }: { detections: Detection[]; onNavigate: (r: any) => void }) {
  const grouped = useMemo(() => {
    const map = new Map<string, { hostType: "scene" | "image"; hostId: number; detections: Detection[] }>();
    for (const d of detections) {
      const key = `${d.hostType}:${d.hostId}`;
      const existing = map.get(key);
      if (existing) {
        existing.detections.push(d);
      } else {
        map.set(key, { hostType: d.hostType as "scene" | "image", hostId: d.hostId, detections: [d] });
      }
    }
    return Array.from(map.values()).sort((a, b) => b.detections.length - a.detections.length);
  }, [detections]);

  return (
    <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
      {grouped.map((group) => {
        const hostKey = `${group.hostType}-${group.hostId}`;
        const previewUrl = group.hostType === "image"
          ? images.thumbnailUrl(group.hostId, 480)
          : scenes.screenshotUrl(group.hostId);
        const open = () => onNavigate({ page: group.hostType, id: group.hostId });
        const Icon = group.hostType === "image" ? ImageIcon : Video;
        const bestScore = Math.max(...group.detections.map((d) => d.score));
        return (
          <article key={hostKey} className="overflow-hidden rounded-2xl border border-border bg-surface/60">
            <button type="button" onClick={open} className="block aspect-video w-full bg-surface/80" aria-label={`Open ${group.hostType} ${group.hostId}`}>
              <img
                src={previewUrl}
                alt=""
                className="h-full w-full object-cover"
                loading="lazy"
                onError={(event) => { (event.target as HTMLImageElement).style.visibility = "hidden"; }}
              />
            </button>
            <div className="flex items-center justify-between gap-2 p-3 text-sm">
              <button type="button" onClick={open} className="flex min-w-0 items-center gap-2 text-left text-foreground hover:text-accent">
                <Icon className="h-4 w-4 shrink-0 text-muted" />
                <span className="truncate">{group.hostType === "image" ? "Image" : "Scene"} #{group.hostId}</span>
              </button>
              <div className="flex shrink-0 items-center gap-2 text-xs text-muted">
                <span>{group.detections.length}x</span>
                <span>{Math.round(bestScore * 100)}%</span>
              </div>
            </div>
          </article>
        );
      })}
    </div>
  );
}

function formatDetectionSubtitle(detection: Detection) {
  if (detection.hostType === "scene" && detection.observedAtSec != null) {
    return `Scene frame at ${formatDetectionTime(detection.observedAtSec)}`;
  }

  return detection.class;
}

function formatDetectionTime(totalSeconds: number) {
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = Math.floor(totalSeconds % 60);
  return hours > 0
    ? `${hours}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`
    : `${minutes}:${seconds.toString().padStart(2, "0")}`;
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

function SimilarFaceCard({ face, onNavigate, canReadPerformers }: { face: import("../api/types").FaceSimilar; onNavigate: (r: any) => void; canReadPerformers: boolean }) {
  const title = face.label?.trim() || face.performerName || `Face #${face.id}`;

  return (
    <article className="overflow-hidden rounded-2xl border border-border bg-surface/60">
      <button
        type="button"
        onClick={() => onNavigate({ page: "face", id: face.id })}
        className="block aspect-square w-full bg-surface/80 text-left"
        aria-label={`Open face ${title}`}
      >
        {face.coverImageUrl ? (
          <img src={face.coverImageUrl} alt={title} className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <div className="flex h-full items-center justify-center text-muted">
            <Fingerprint className="h-10 w-10" />
          </div>
        )}
      </button>
      <div className="space-y-2 p-4">
        <button type="button" onClick={() => onNavigate({ page: "face", id: face.id })} className="text-left text-sm font-semibold text-foreground hover:text-accent">
          {title}
        </button>
        <div className="text-xs text-secondary">Distance {face.distance.toFixed(3)}</div>
        {face.performerId ? (
          <button
            type="button"
            onClick={() => canReadPerformers && onNavigate({ page: "performer", id: face.performerId })}
            className={`text-left text-xs ${canReadPerformers ? "text-accent hover:underline" : "text-secondary"}`}
          >
            {face.performerName || `Performer #${face.performerId}`}
          </button>
        ) : null}
      </div>
    </article>
  );
}
