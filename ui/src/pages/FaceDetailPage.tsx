import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Fingerprint, Link2, Merge, Save, Search, Trash2 } from "lucide-react";
import { faces, images, performers, scenes } from "../api/client";
import type { Detection, Face, FaceDeleteImpact, Performer } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { formatDate } from "../components/shared";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

export function FaceDetailPage({ id, onNavigate }: Props) {
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const canWriteFace = canWriteEntity("face", hasPermission);
  const canDeleteFace = canDeleteEntity("face", hasPermission);
  const canReadPerformers = canReadEntity("performer", hasPermission);
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

  const [label, setLabel] = useState("");
  const [primarySourceKey, setPrimarySourceKey] = useState("");
  const [performerSearch, setPerformerSearch] = useState("");
  const [mergeSearch, setMergeSearch] = useState("");

  useEffect(() => {
    if (!face) {
      return;
    }

    setLabel(face.label ?? "");
    setPrimarySourceKey(face.primarySourceKey ?? "");
  }, [face]);

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
      }),
    onSuccess: (updated) => {
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
  const title = face?.label?.trim() || face?.performerName || `Face #${id}`;

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
      <div className="flex h-64 items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-accent" />
      </div>
    );
  }

  if (!face) {
    return <div className="py-16 text-center text-secondary">Face not found</div>;
  }

  return (
    <div className="space-y-6">
      <button
        type="button"
        onClick={goBack}
        className="inline-flex items-center gap-2 rounded-lg border border-border bg-card/70 px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
      >
        <ArrowLeft className="h-4 w-4" />
        {backLabel}
      </button>

      <section className="grid gap-6 xl:grid-cols-[minmax(280px,360px),1fr]">
        <div className="overflow-hidden rounded-3xl border border-border bg-card/80 shadow-sm">
          <div className="aspect-square bg-surface/70">
            {face.coverImageUrl ? (
              <img src={face.coverImageUrl} alt={title} className="h-full w-full object-cover" loading="lazy" />
            ) : (
              <div className="flex h-full items-center justify-center text-muted">
                <Fingerprint className="h-16 w-16" />
              </div>
            )}
          </div>
          <div className="space-y-4 p-5">
            <div>
              <h1 className="text-2xl font-semibold text-foreground">{title}</h1>
              <p className="mt-1 text-sm text-secondary">Face cluster #{face.id}</p>
            </div>

            <div className="flex flex-wrap gap-2 text-xs">
              {face.mergedIntoFaceId && <StatusPill icon={<Merge className="h-3 w-3" />} label={`Merged into #${face.mergedIntoFaceId}`} tone="muted" />}
              {face.performerId && <StatusPill icon={<Link2 className="h-3 w-3" />} label="Linked performer" tone="accent" />}
            </div>

            <div className="grid grid-cols-3 gap-3 text-center text-xs">
              <MetricCard label="Detections" value={face.detectionCount} />
              <MetricCard label="Scenes" value={face.sceneCount} />
              <MetricCard label="Images" value={face.imageCount} />
            </div>

            <dl className="space-y-2 text-sm text-secondary">
              <div className="flex items-start justify-between gap-3">
                <dt className="text-muted">Primary source</dt>
                <dd className="text-right text-foreground">{face.primarySourceKey || "Unknown"}</dd>
              </div>
              <div className="flex items-start justify-between gap-3">
                <dt className="text-muted">Created</dt>
                <dd className="text-right text-foreground">{formatDate(face.createdAt)}</dd>
              </div>
              <div className="flex items-start justify-between gap-3">
                <dt className="text-muted">Updated</dt>
                <dd className="text-right text-foreground">{formatDate(face.updatedAt)}</dd>
              </div>
            </dl>
          </div>
        </div>

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

          <section className="grid gap-6 lg:grid-cols-2">
            <div className="rounded-2xl border border-border bg-card/70 p-5">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Linked Performer</h2>
              {face.performerId ? (
                <div className="mt-3 space-y-3">
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
                <p className="mt-3 text-sm text-secondary">No performer is linked to this face cluster yet.</p>
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
            </div>

            <div className="rounded-2xl border border-border bg-card/70 p-5">
              <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Face Controls</h2>
              {canWriteFace ? (
                <div className="mt-3 space-y-4">
                  <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
                    Label
                    <input
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
                  <button
                    type="button"
                    onClick={() => updateMutation.mutate({ label: normalizedLabel, primarySourceKey: normalizedPrimarySourceKey })}
                    disabled={!hasMetadataChanges || updateMutation.isPending}
                    className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    <Save className="h-4 w-4" />
                    {updateMutation.isPending ? "Saving..." : "Save metadata"}
                  </button>

                  <div className="border-t border-border pt-4">
                    <label className="block text-xs font-semibold uppercase tracking-wide text-muted">Merge into another face</label>
                    <div className="relative mt-2">
                      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
                      <input
                        type="text"
                        value={mergeSearch}
                        onChange={(event) => setMergeSearch(event.target.value)}
                        placeholder="Search primary faces"
                        className="w-full rounded-lg border border-border bg-input py-2 pl-9 pr-3 text-sm text-foreground outline-none focus:border-accent"
                      />
                    </div>
                    {mergeSearchTerm.length < 2 ? (
                      <p className="mt-2 text-xs text-secondary">Type at least two characters to search merge targets.</p>
                    ) : mergeMatchesQuery.isLoading ? (
                      <p className="mt-2 text-xs text-secondary">Searching faces...</p>
                    ) : mergeCandidates.length === 0 ? (
                      <p className="mt-2 text-xs text-secondary">No merge targets matched that search.</p>
                    ) : (
                      <div className="mt-3 space-y-2">
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

                  {canDeleteFace ? (
                    <div className="space-y-3 border-t border-border pt-4">
                      <div className="space-y-1 text-xs text-secondary">
                        <div className="font-semibold uppercase tracking-wide text-muted">Delete face cluster</div>
                        <p>
                          {deleteImpactLoading
                            ? "Loading delete impact..."
                            : deleteImpact
                              ? describeFaceDeleteImpact(deleteImpact)
                              : "Delete this face cluster and remove the AI artifacts it owns."}
                        </p>
                      </div>
                      <button
                        type="button"
                        onClick={() => {
                          if (window.confirm(buildFaceDeleteConfirmation(title, deleteImpact))) {
                            deleteMutation.mutate();
                          }
                        }}
                        disabled={deleteMutation.isPending}
                        className="inline-flex items-center gap-2 rounded-lg border border-red-500/40 px-3 py-2 text-sm text-red-200 transition-colors hover:border-red-400 hover:text-red-100 disabled:cursor-not-allowed disabled:opacity-50"
                      >
                        <Trash2 className="h-4 w-4" />
                        {deleteMutation.isPending ? "Deleting..." : "Delete face"}
                      </button>
                    </div>
                  ) : null}
                </div>
              ) : (
                <p className="mt-3 text-sm text-secondary">You have read access to this face cluster, but not write access.</p>
              )}
            </div>
          </section>

          <section className="rounded-2xl border border-border bg-card/70 p-5">
            <div className="flex items-center justify-between gap-3">
              <div>
                <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Detections</h2>
                <p className="mt-1 text-sm text-secondary">Detections that currently resolve to this face cluster.</p>
              </div>
              <div className="text-xs text-muted">{faceDetections.length} item{faceDetections.length === 1 ? "" : "s"}</div>
            </div>

            {detectionsLoading ? (
              <div className="mt-4 text-sm text-secondary">Loading detections...</div>
            ) : faceDetections.length === 0 ? (
              <div className="mt-4 rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-secondary">
                No detections currently point to this face cluster.
              </div>
            ) : (
              <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {faceDetections.map((detection) => (
                  <FaceDetectionCard key={detection.id} detection={detection} onNavigate={onNavigate} />
                ))}
              </div>
            )}
          </section>

          <section className="rounded-2xl border border-border bg-card/70 p-5">
            <div className="flex items-center justify-between gap-3">
              <div>
                <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Similar Faces</h2>
                <p className="mt-1 text-sm text-secondary">Nearest neighbors from the face embedding index.</p>
              </div>
              <div className="text-xs text-muted">{similarFaces.length} match{similarFaces.length === 1 ? "" : "es"}</div>
            </div>

            {similarLoading ? (
              <div className="mt-4 text-sm text-secondary">Loading similar faces...</div>
            ) : similarFaces.length === 0 ? (
              <div className="mt-4 rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-secondary">
                No similar faces are available for this cluster yet.
              </div>
            ) : (
              <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {similarFaces.map((candidate) => (
                  <SimilarFaceCard key={candidate.id} face={candidate} onNavigate={onNavigate} canReadPerformers={canReadPerformers} />
                ))}
              </div>
            )}
          </section>
        </div>
      </section>
    </div>
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

function MetricCard({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border bg-surface/60 px-3 py-3">
      <div className="text-lg font-semibold text-foreground">{value}</div>
      <div className="mt-1 text-[11px] uppercase tracking-wide text-muted">{label}</div>
    </div>
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

function buildFaceDeleteConfirmation(title: string, deleteImpact?: FaceDeleteImpact) {
  return [
    `Delete ${title}?`,
    "",
    deleteImpact
      ? describeFaceDeleteImpact(deleteImpact)
      : "Delete this face cluster and remove the AI artifacts it owns.",
    "",
    "This cannot be undone.",
  ].join("\n");
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
