import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bookmark, Camera, ChevronLeft, ChevronRight, Clapperboard, ExternalLink, Film, Image, Layers, MoreVertical, Pencil, Save, Search, Trash2 } from "lucide-react";
import { entityImages, scenes, segmentLibrary, tags } from "../api/client";
import type { Route } from "../router/location";
import type { Scene, SegmentRecord } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { VideoPlayer } from "../components/VideoPlayer";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { MediaDetailLayout } from "../components/MediaDetailLayout/MediaDetailLayout";
import { CoverImageDialog } from "../components/CoverImageDialog";
import { formatDate } from "../components/shared";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { SegmentVisualSimilarityPanel } from "../components/VisualSimilarityPanel";
import { buildSubSceneCreate } from "../utils/subSceneCreation";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type SegmentTab = "overview" | "metadata" | "context" | "similar" | "spans" | "payload";

export function SegmentDetailPage({ id, onNavigate }: Props) {
  const queryClient = useQueryClient();
  const { hasPermission } = useAuth();
  const canWriteSegments = canWriteEntity("segment", hasPermission);
  const canDeleteSegments = canDeleteEntity("segment", hasPermission);
  const canReadScenes = canReadEntity("scene", hasPermission);
  const canWriteScenes = canWriteEntity("scene", hasPermission);
  const canReadTags = canReadEntity("tag", hasPermission);
  const { backLabel, goBack } = useBackNavigation({ page: "segments" }, onNavigate);

  const { data: segment, isLoading } = useQuery({
    queryKey: ["segment", id],
    queryFn: () => segmentLibrary.get(id),
  });

  const [title, setTitle] = useState("");
  const [kind, setKind] = useState("");
  const [sourceKey, setSourceKey] = useState("");
  const [sourceRunId, setSourceRunId] = useState("");
  const [colorHint, setColorHint] = useState("");
  const [startSec, setStartSec] = useState(0);
  const [endSec, setEndSec] = useState<number | "">("");
  const [confidenceText, setConfidenceText] = useState("");
  const [tagSearch, setTagSearch] = useState("");
  const [selectedTagId, setSelectedTagId] = useState<number | null>(null);
  const [selectedTagName, setSelectedTagName] = useState("");
  const [activeTab, setActiveTab] = useState<SegmentTab>("overview");
  const [coverOpen, setCoverOpen] = useState(false);
  const [showOpsMenu, setShowOpsMenu] = useState(false);
  const [segmentVideoTime, setSegmentVideoTime] = useState(0);
  const titleInputRef = useRef<HTMLInputElement | null>(null);
  const opsMenuRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    if (!segment) {
      return;
    }

    setTitle(segment.title ?? "");
    setKind(segment.kind ?? "");
    setSourceKey(segment.sourceKey ?? "");
    setSourceRunId(segment.sourceRunId ?? "");
    setColorHint(segment.colorHint ?? "");
    setStartSec(segment.startSec);
    setEndSec(segment.endSec ?? "");
    setConfidenceText(segment.confidence != null ? String(segment.confidence) : "");
    setTagSearch("");
    setSelectedTagId(segment.tagId ?? null);
    setSelectedTagName(segment.tagName ?? "");
  }, [segment]);

  const tagSearchTerm = tagSearch.trim();
  const tagResultsQuery = useQuery({
    queryKey: ["segment", id, "tags-search", tagSearchTerm],
    queryFn: () => tags.find({ q: tagSearchTerm, perPage: 8 }),
    enabled: canWriteSegments && canReadTags && tagSearchTerm.length >= 1,
  });
  const { data: siblingSegments = [], isLoading: siblingSegmentsLoading } = useQuery({
    queryKey: ["segment", id, "scene-context", segment?.hostId],
    queryFn: () => scenes.segments.list(segment!.hostId),
    enabled: !!segment,
  });
  const { data: containingSpans, isLoading: containingSpansLoading } = useQuery({
    queryKey: ["segment", id, "resolved-span-lookup", segment?.hostId],
    queryFn: () => scenes.segments.spans(segment!.hostId),
    enabled: !!segment && segment.hostType === "scene",
  });
  const { data: playbackScene, isLoading: playbackSceneLoading } = useQuery({
    queryKey: ["scene", segment?.hostId],
    queryFn: () => scenes.get(segment!.hostId),
    enabled: !!segment && segment.hostType === "scene" && canReadScenes,
  });

  const normalizedTitle = title.trim() || undefined;
  const normalizedKind = kind.trim() || undefined;
  const normalizedSourceKey = sourceKey.trim();
  const normalizedSourceRunId = sourceRunId.trim() || undefined;
  const normalizedColorHint = colorHint.trim() || undefined;
  const normalizedEndSec = endSec === "" ? undefined : endSec;
  const normalizedConfidence = confidenceText.trim() === "" ? undefined : Number(confidenceText);
  const canSave =
    canWriteSegments &&
    normalizedSourceKey.length > 0 &&
    Number.isFinite(startSec) &&
    (normalizedEndSec == null || normalizedEndSec >= startSec) &&
    (normalizedConfidence == null || Number.isFinite(normalizedConfidence));

  const invalidateSegmentQueries = (current?: SegmentRecord | null, nextTagId?: number | null) => {
    queryClient.invalidateQueries({ queryKey: ["segments"] });
    queryClient.invalidateQueries({ queryKey: ["segment", id] });

    if (current?.hostId != null) {
      queryClient.invalidateQueries({ queryKey: ["scene", current.hostId, "segments"] });
      queryClient.invalidateQueries({ queryKey: ["scene", current.hostId] });
    }

    if (current?.tagId != null) {
      queryClient.invalidateQueries({ queryKey: ["tag", current.tagId] });
    }

    if (nextTagId != null) {
      queryClient.invalidateQueries({ queryKey: ["tag", nextTagId] });
    }
  };

  const updateMutation = useMutation({
    mutationFn: async () => {
      if (!segment) {
        throw new Error("Segment not loaded");
      }

      return scenes.segments.update(segment.hostId, segment.id, {
        startSec,
        endSec: normalizedEndSec,
        tagId: selectedTagId ?? undefined,
        kind: normalizedKind,
        refId: segment.refId,
        payload: segment.payload,
        sourceKey: normalizedSourceKey,
        sourceRunId: normalizedSourceRunId,
        confidence: normalizedConfidence,
        title: normalizedTitle,
        colorHint: normalizedColorHint,
      });
    },
    onSuccess: () => {
      invalidateSegmentQueries(segment, selectedTagId);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: async () => {
      if (!segment) {
        throw new Error("Segment not loaded");
      }

      return scenes.segments.delete(segment.hostId, segment.id);
    },
    onSuccess: () => {
      invalidateSegmentQueries(segment);
      goBack();
    },
  });

  const createSubSceneMutation = useMutation({
    mutationFn: async () => {
      if (!segment || segment.hostType !== "scene" || !playbackScene) {
        throw new Error("Segment is not scene-backed");
      }

      const clipEndSec = segment.endSec ?? playbackScene?.files[0]?.duration;
      if (clipEndSec == null || clipEndSec <= segment.startSec) {
        throw new Error("Segment needs an end time before it can become a scene");
      }

      return scenes.createSubScene(
        segment.hostId,
        buildSubSceneCreate(playbackScene, {
          startSec: segment.startSec,
          endSec: clipEndSec,
        }, {
          title: displayTitle,
          tagIds: segment.tagId ? [segment.tagId] : undefined,
        }),
      );
    },
    onSuccess: (newScene) => {
      queryClient.invalidateQueries({ queryKey: ["scenes"] });
      queryClient.invalidateQueries({ queryKey: ["scene", segment?.hostId] });
      onNavigate({ page: "scene", id: newScene.id });
    },
  });

  const invalidateSceneCover = () => {
    if (!segment || segment.hostType !== "scene") {
      return;
    }

    queryClient.invalidateQueries({ queryKey: ["scene", segment.hostId] });
    queryClient.invalidateQueries({ queryKey: ["scenes"] });
    queryClient.invalidateQueries({ queryKey: ["segment", id] });
    queryClient.invalidateQueries({ queryKey: ["segments"] });
  };

  const setCoverFromCurrentFrameMutation = useMutation({
    mutationFn: async () => {
      if (!segment || segment.hostType !== "scene") {
        throw new Error("Segment is not scene-backed");
      }

      return scenes.setCoverFromFrame(segment.hostId, segmentVideoTime || segment.startSec);
    },
    onSuccess: invalidateSceneCover,
  });

  const payloadText = useMemo(() => formatPayload(segment?.payload), [segment?.payload]);
  const displayTitle = segment?.title?.trim() || segment?.kind || segment?.tagName || `Segment #${id}`;
  const orderedSiblingSegments = useMemo(
    () => [...siblingSegments].sort((left, right) => left.startSec - right.startSec || (left.endSec ?? left.startSec) - (right.endSec ?? right.startSec) || left.id - right.id),
    [siblingSegments],
  );
  const sceneContext = useMemo(() => {
    if (!segment) {
      return {
        currentIndex: -1,
        previous: [] as SegmentRecord[],
        next: [] as SegmentRecord[],
        sameSource: [] as SegmentRecord[],
        sameKind: [] as SegmentRecord[],
      };
    }

    const currentIndex = orderedSiblingSegments.findIndex((item) => item.id === segment.id);
    const previous = currentIndex > 0 ? orderedSiblingSegments.slice(Math.max(0, currentIndex - 2), currentIndex) : [];
    const next = currentIndex >= 0 ? orderedSiblingSegments.slice(currentIndex + 1, currentIndex + 3) : [];
    const sameSource = orderedSiblingSegments.filter((item) => item.id !== segment.id && item.sourceKey === segment.sourceKey).slice(0, 4);
    const sameKind = segment.kind
      ? orderedSiblingSegments.filter((item) => item.id !== segment.id && item.kind === segment.kind).slice(0, 4)
      : [];

    return {
      currentIndex,
      previous,
      next,
      sameSource,
      sameKind,
    };
  }, [orderedSiblingSegments, segment]);
  const resolvedSpanRoute = useMemo<Route | null>(() => {
    if (!segment || segment.hostType !== "scene" || !containingSpans) {
      return null;
    }

    const containingSpan = containingSpans.spans.find((span) => span.segmentIds.includes(segment.id));
    if (!containingSpan) {
      return null;
    }

    return {
      page: "scene-span",
      id: segment.hostId,
      spanKey: containingSpan.spanKey,
      profileId: containingSpans.profileId,
    };
  }, [containingSpans, segment]);
  const previousSegment = sceneContext.previous.at(-1);
  const nextSegment = sceneContext.next[0];
  const hasResolvedSpanPreview = segment?.hostType === "scene" && (containingSpansLoading || (containingSpans?.spans.length ?? 0) > 0);
  const canCreateSubScene = !!segment
    && segment.hostType === "scene"
    && !!playbackScene
    && canReadScenes
    && canWriteScenes
    && (segment.endSec != null || (playbackScene?.files[0]?.duration ?? 0) > segment.startSec);
  const tabs = useMemo(() => {
    const baseTabs = [
      { key: "overview", label: "Overview" },
      { key: "similar", label: "Similar" },
      { key: "metadata", label: canWriteSegments ? "Edit" : "Metadata" },
      { key: "context", label: "Context", count: Math.max(0, orderedSiblingSegments.length - 1) },
    ];

    if (hasResolvedSpanPreview) {
      baseTabs.push({ key: "spans", label: "Resolved Spans", count: containingSpans?.spans.length ?? 0 });
    }

    baseTabs.push({ key: "payload", label: "Payload" });

    return baseTabs;
  }, [canWriteSegments, containingSpans?.spans.length, hasResolvedSpanPreview, orderedSiblingSegments.length]);
  const segmentKeyboardShortcuts = useMemo(() => {
    if (!segment) {
      return [];
    }

    return [
      {
        key: "e",
        description: canWriteSegments ? "Edit segment" : "Open segment details",
        handler: () => {
          setActiveTab("metadata");
          if (canWriteSegments) {
            window.setTimeout(() => titleInputRef.current?.focus(), 0);
          }
        },
      },
      {
        key: "s",
        description: "Open parent scene",
        handler: () => {
          if (segment.hostType === "scene" && canReadScenes) {
            onNavigate(buildSceneRouteForSegment(segment));
          }
        },
      },
      {
        key: "[",
        description: "Open previous segment",
        handler: () => {
          if (previousSegment) {
            onNavigate({ page: "segment", id: previousSegment.id });
          }
        },
      },
      {
        key: "]",
        description: "Open next segment",
        handler: () => {
          if (nextSegment) {
            onNavigate({ page: "segment", id: nextSegment.id });
          }
        },
      },
    ];
  }, [canReadScenes, canWriteSegments, nextSegment, onNavigate, previousSegment, segment]);
  useEffect(() => {
    if (!segment) {
      return;
    }

    document.title = `${displayTitle} | Cove`;
    return () => {
      document.title = "Cove";
    };
  }, [displayTitle, segment]);

  useEffect(() => {
    const handler = (event: MouseEvent) => {
      if (opsMenuRef.current && !opsMenuRef.current.contains(event.target as Node)) {
        setShowOpsMenu(false);
      }
    };

    if (showOpsMenu) {
      document.addEventListener("mousedown", handler);
    }

    return () => document.removeEventListener("mousedown", handler);
  }, [showOpsMenu]);

  if (isLoading) {
    return (
      <div className="px-1 py-2">
        <DetailSkeleton />
      </div>
    );
  }

  if (!segment) {
    return <div className="py-16 text-center text-secondary">Segment not found</div>;
  }

  const overviewContent = (
    <div className="space-y-6">
      <SegmentSummaryCard
        segment={segment}
        canReadScenes={canReadScenes}
        canReadTags={canReadTags}
        resolvedSpanRoute={resolvedSpanRoute}
        onNavigate={onNavigate}
        showHeading={false}
      />
    </div>
  );

  const editContent = (
    <section className="rounded-2xl border border-border bg-card/70 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">{canWriteSegments ? "Edit Segment" : "Segment Details"}</h2>
          <p className="mt-1 text-sm text-secondary">
            {canWriteSegments
              ? "Update the timing, metadata, and tag assignment for this segment."
              : "You have read access to this segment, but not write access."}
          </p>
        </div>
      </div>

      {canWriteSegments ? (
        <div className="mt-4 grid gap-4 lg:grid-cols-2">
          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Title
            <input
              ref={titleInputRef}
              type="text"
              value={title}
              onChange={(event) => setTitle(event.target.value)}
              placeholder="Optional segment title"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>

          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Kind
            <input
              type="text"
              value={kind}
              onChange={(event) => setKind(event.target.value)}
              placeholder="intro, highlight, action..."
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>

          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Start seconds
            <input
              type="number"
              min={0}
              step="0.1"
              value={startSec}
              onChange={(event) => setStartSec(Number(event.target.value))}
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>

          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            End seconds
            <input
              type="number"
              min={0}
              step="0.1"
              value={endSec}
              onChange={(event) => setEndSec(event.target.value === "" ? "" : Number(event.target.value))}
              placeholder="Optional"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>

          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Source key
            <input
              type="text"
              value={sourceKey}
              onChange={(event) => setSourceKey(event.target.value)}
              placeholder="user or detector.source"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>

          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Source run id
            <input
              type="text"
              value={sourceRunId}
              onChange={(event) => setSourceRunId(event.target.value)}
              placeholder="Optional run identifier"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>

          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Confidence
            <input
              type="number"
              min={0}
              step="0.01"
              value={confidenceText}
              onChange={(event) => setConfidenceText(event.target.value)}
              placeholder="Optional"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>

          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">
            Color hint
            <input
              type="text"
              value={colorHint}
              onChange={(event) => setColorHint(event.target.value)}
              placeholder="#ffaa00"
              className="mt-2 w-full rounded-lg border border-border bg-input px-3 py-2 text-sm font-normal text-foreground outline-none focus:border-accent"
            />
          </label>
        </div>
      ) : (
        <div className="mt-4 grid gap-3 md:grid-cols-2">
          <ReadOnlyField label="Title" value={segment.title} />
          <ReadOnlyField label="Kind" value={segment.kind} />
          <ReadOnlyField label="Source key" value={segment.sourceKey} />
          <ReadOnlyField label="Source run id" value={segment.sourceRunId} />
          <ReadOnlyField label="Color hint" value={segment.colorHint} />
          <ReadOnlyField label="Ref id" value={segment.refId?.toString()} />
        </div>
      )}

      {canWriteSegments ? (
        <div className="mt-5 space-y-3 border-t border-border pt-4">
          <label className="block text-xs font-semibold uppercase tracking-wide text-muted">Tag</label>
          <div className="relative">
            <div className="flex items-center rounded-lg border border-border bg-input px-3 py-2 text-sm text-foreground">
              <Search className="mr-2 h-4 w-4 flex-shrink-0 text-muted" />
              <input
                type="text"
                value={tagSearch}
                onChange={(event) => {
                  setTagSearch(event.target.value);
                  setSelectedTagId(null);
                  setSelectedTagName("");
                }}
                placeholder={selectedTagName || (canReadTags ? "Search tag..." : "Tag lookup unavailable")}
                disabled={!canReadTags}
                className="w-full bg-transparent outline-none disabled:cursor-not-allowed disabled:text-muted"
              />
            </div>
            {tagSearchTerm && tagResultsQuery.data && tagResultsQuery.data.items.length > 0 ? (
              <div className="absolute z-10 mt-1 max-h-48 w-full overflow-y-auto rounded-lg border border-border bg-card shadow-lg">
                {tagResultsQuery.data.items.map((tag) => (
                  <button
                    key={tag.id}
                    type="button"
                    onClick={() => {
                      setSelectedTagId(tag.id);
                      setSelectedTagName(tag.name);
                      setTagSearch("");
                    }}
                    className="block w-full px-3 py-2 text-left text-sm text-secondary hover:bg-card-hover hover:text-foreground"
                  >
                    {tag.name}
                  </button>
                ))}
              </div>
            ) : null}
          </div>
          {selectedTagId != null || selectedTagName ? (
            <div className="flex items-center justify-between rounded-lg border border-border bg-surface/60 px-3 py-2 text-sm text-secondary">
              <span className="text-foreground">{selectedTagName || `Tag #${selectedTagId}`}</span>
              <button
                type="button"
                onClick={() => {
                  setSelectedTagId(null);
                  setSelectedTagName("");
                  setTagSearch("");
                }}
                className="text-accent hover:underline"
              >
                Clear tag
              </button>
            </div>
          ) : (
            <p className="text-xs text-secondary">This segment is currently untagged.</p>
          )}
        </div>
      ) : null}

      {canWriteSegments ? (
        <div className="mt-5 flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
          <div className="text-xs text-secondary">
            {normalizedEndSec != null && normalizedEndSec < startSec ? "End time must be after the start time." : "Changes are written back through the owning scene segment API."}
          </div>
          <div className="flex items-center gap-2">
            {canDeleteSegments ? (
              <button
                type="button"
                onClick={() => {
                  if (window.confirm(`Delete segment #${segment.id}?`)) {
                    deleteMutation.mutate();
                  }
                }}
                disabled={deleteMutation.isPending}
                className="inline-flex items-center gap-2 rounded-lg border border-red-500/40 px-3 py-2 text-sm text-red-200 transition-colors hover:border-red-400 disabled:cursor-not-allowed disabled:opacity-50"
              >
                <Trash2 className="h-4 w-4" />
                {deleteMutation.isPending ? "Deleting..." : "Delete"}
              </button>
            ) : null}
            <button
              type="button"
              onClick={() => updateMutation.mutate()}
              disabled={!canSave || updateMutation.isPending}
              className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Save className="h-4 w-4" />
              {updateMutation.isPending ? "Saving..." : "Save changes"}
            </button>
          </div>
        </div>
      ) : null}
    </section>
  );

  const relatedContent = (
    <section className="rounded-2xl border border-border bg-card/70 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Scene Context</h2>
          <p className="mt-1 text-sm text-secondary">
            {sceneContext.currentIndex >= 0
              ? `Segment ${sceneContext.currentIndex + 1} of ${orderedSiblingSegments.length} by timeline order in this scene.`
              : "See nearby segments from the same scene to understand context."}
          </p>
        </div>
        <div className="text-xs text-muted">{orderedSiblingSegments.length} segment{orderedSiblingSegments.length === 1 ? "" : "s"} in scene</div>
      </div>

      {siblingSegmentsLoading ? (
        <div className="mt-4 text-sm text-secondary">Loading scene context...</div>
      ) : orderedSiblingSegments.length <= 1 ? (
        <EmptyPanel icon={<Clapperboard className="h-10 w-10" />} message="No additional segments exist in this scene yet." />
      ) : (
        <div className="mt-4 space-y-4">
          <SegmentContextSection title="Previous Segments" items={sceneContext.previous} onNavigate={onNavigate} emptyMessage="This is the first segment in the scene." />
          <SegmentContextSection title="Next Segments" items={sceneContext.next} onNavigate={onNavigate} emptyMessage="This is the last segment in the scene." />
          <SegmentContextSection title="Same Source" items={sceneContext.sameSource} onNavigate={onNavigate} emptyMessage="No other segments in this scene share the same source." compact />
          {segment.kind ? (
            <SegmentContextSection title="Same Kind" items={sceneContext.sameKind} onNavigate={onNavigate} emptyMessage={`No other ${segment.kind} segments in this scene.`} compact />
          ) : null}
        </div>
      )}
    </section>
  );

  const spansContent = hasResolvedSpanPreview ? (
    <section className="rounded-2xl border border-border bg-card/70 p-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Resolved Spans</h2>
          <p className="mt-1 text-sm text-secondary">Preview the resolved spans from the current display profile that include or neighbor this segment.</p>
        </div>
        {resolvedSpanRoute ? (
          <button
            type="button"
            onClick={() => onNavigate(resolvedSpanRoute)}
            className="rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
          >
            Open containing span
          </button>
        ) : null}
      </div>

      {containingSpansLoading ? (
        <div className="mt-4 text-sm text-secondary">Loading resolved spans...</div>
      ) : containingSpans?.spans.length ? (
        <div className="mt-4 grid gap-3 xl:grid-cols-2">
          {containingSpans.spans.map((span) => {
            const spanRoute = {
              page: "scene-span" as const,
              id: segment.hostId,
              spanKey: span.spanKey,
              profileId: containingSpans.profileId,
            };
            const includesCurrentSegment = span.segmentIds.includes(segment.id);
            return (
              <article key={span.spanKey} className={`rounded-xl border px-4 py-4 ${includesCurrentSegment ? "border-accent/40 bg-accent/5" : "border-border bg-surface/40"}`}>
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <div className="text-sm font-medium text-foreground">{span.tagName || span.kind || `Span ${span.spanKey}`}</div>
                    <div className="mt-1 text-xs text-secondary">{formatSegmentRange(span.startSec, span.endSec)} • {span.segmentIds.length} segment{span.segmentIds.length === 1 ? "" : "s"}</div>
                  </div>
                  {includesCurrentSegment ? <StatusPill label="Contains current segment" tone="accent" /> : null}
                </div>
                <button
                  type="button"
                  onClick={() => onNavigate(spanRoute)}
                  className="mt-3 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
                >
                  Open resolved span
                </button>
              </article>
            );
          })}
        </div>
      ) : (
        <EmptyPanel icon={<Layers className="h-10 w-10" />} message="No resolved spans currently include this segment." />
      )}
    </section>
  ) : null;

  const payloadContent = (
    <section className="rounded-2xl border border-border bg-card/70 p-5">
      <div>
        <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">Payload</h2>
        <p className="mt-1 text-sm text-secondary">Raw segment payload as stored by the source that created or updated this record.</p>
      </div>
      {payloadText ? (
        <pre className="mt-4 overflow-x-auto rounded-xl border border-border bg-surface/70 p-4 text-xs text-secondary">{payloadText}</pre>
      ) : (
        <EmptyPanel icon={<Bookmark className="h-10 w-10" />} message="No payload is stored on this segment." />
      )}
    </section>
  );

  const activeContent =
    activeTab === "metadata"
      ? editContent
      : activeTab === "context"
        ? relatedContent
        : activeTab === "similar"
          ? segment.hostType === "scene"
            ? <SegmentVisualSimilarityPanel sceneId={segment.hostId} startSec={segment.startSec} endSec={segment.endSec} onNavigate={onNavigate} />
            : <EmptyPanel icon={<Film className="h-10 w-10" />} message="Visual similarity is only available for scene-backed segments." />
        : activeTab === "spans"
          ? spansContent
          : activeTab === "payload"
            ? payloadContent
            : overviewContent;

  return (
    <MediaDetailLayout
      title={displayTitle}
      subtitle={`Segment #${segment.id} • ${formatSegmentRange(segment.startSec, segment.endSec)}`}
      backLabel={backLabel}
      onGoBack={goBack}
      media={
        <SegmentPlaybackPanel
          segment={segment}
          scene={playbackScene}
          sceneLoading={playbackSceneLoading}
          canReadScenes={canReadScenes}
          onNavigate={onNavigate}
          onTimeUpdate={setSegmentVideoTime}
          embedded
        />
      }
      mediaAspectRatio="auto"
      mediaFullBleed
      tabs={tabs}
      activeTab={activeTab}
      onTabChange={(key) => setActiveTab(key as SegmentTab)}
      keyboardShortcuts={segmentKeyboardShortcuts}
      actions={
        <>
          {previousSegment ? (
            <button
              type="button"
              aria-label="Open previous segment"
              onClick={() => onNavigate({ page: "segment", id: previousSegment.id })}
              className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
            >
              <ChevronLeft className="h-4 w-4" />
              Prev
            </button>
          ) : null}
          {nextSegment ? (
            <button
              type="button"
              aria-label="Open next segment"
              onClick={() => onNavigate({ page: "segment", id: nextSegment.id })}
              className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
            >
              Next
              <ChevronRight className="h-4 w-4" />
            </button>
          ) : null}
          <button
            type="button"
            onClick={() => setActiveTab("metadata")}
            className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
          >
            <Pencil className="h-4 w-4" />
            {canWriteSegments ? "Edit" : "Details"}
          </button>
          {segment.hostType === "scene" && canReadScenes ? (
            <button
              type="button"
              onClick={() => createSubSceneMutation.mutate()}
              disabled={!canCreateSubScene || createSubSceneMutation.isPending}
              className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Clapperboard className="h-4 w-4" />
              {createSubSceneMutation.isPending ? "Creating..." : "Make Scene"}
            </button>
          ) : null}
          {segment.hostType === "scene" && canReadScenes ? (
            <button
              type="button"
              onClick={() => onNavigate(buildSceneRouteForSegment(segment))}
              className="inline-flex items-center gap-2 rounded-full border border-border px-3 py-2 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
            >
              <ExternalLink className="h-4 w-4" />
              Open Scene
            </button>
          ) : null}
          {segment.hostType === "scene" && canWriteScenes ? (
            <div className="relative" ref={opsMenuRef}>
              <button
                type="button"
                onClick={() => setShowOpsMenu((current) => !current)}
                className="inline-flex items-center justify-center rounded-full border border-border p-2 text-secondary transition hover:border-accent hover:text-foreground"
                title="More actions"
              >
                <MoreVertical className="h-4 w-4" />
              </button>
              {showOpsMenu ? (
                <div className="absolute right-0 top-full z-50 mt-1 min-w-[190px] rounded border border-border bg-card py-1 shadow-lg">
                  <button
                    type="button"
                    onClick={() => {
                      setCoverOpen(true);
                      setShowOpsMenu(false);
                    }}
                    className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground transition-colors hover:bg-surface"
                  >
                    <Image className="h-3.5 w-3.5" /> Set Cover...
                  </button>
                </div>
              ) : null}
            </div>
          ) : null}
        </>
      }
    >
      <CoverImageDialog
        open={coverOpen}
        title="Set Scene Cover"
        currentImageUrl={playbackScene ? scenes.screenshotUrl(segment.hostId, playbackScene.updatedAt) : undefined}
        onUpload={(file) => entityImages.uploadSceneCoverImage(segment.hostId, file)}
        onDelete={() => entityImages.deleteSceneCoverImage(segment.hostId)}
        onClose={() => setCoverOpen(false)}
        onSuccess={invalidateSceneCover}
        aspectRatio="16/9"
        extraActions={(
          <button
            type="button"
            onClick={() => { setCoverFromCurrentFrameMutation.mutate(); setCoverOpen(false); }}
            disabled={setCoverFromCurrentFrameMutation.isPending}
            className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground hover:border-accent hover:text-accent disabled:opacity-60"
          >
            {setCoverFromCurrentFrameMutation.isPending ? <span className="h-3.5 w-3.5 animate-spin rounded-full border-b-2 border-accent" /> : <Camera className="h-3.5 w-3.5" />}
            From Current Frame
          </button>
        )}
      />
      {/* segments do not support engagement (see ui/src/api/types.ts AffinityHostType) */}
      <MediaDetailLayout.Content>{activeContent}</MediaDetailLayout.Content>
    </MediaDetailLayout>
  );
}

function ReadOnlyField({ label, value }: { label: string; value?: string }) {
  return (
    <div className="rounded-xl border border-border bg-surface/60 px-4 py-3">
      <div className="text-xs font-semibold uppercase tracking-wide text-muted">{label}</div>
      <div className="mt-2 text-sm text-foreground">{value || "Not set"}</div>
    </div>
  );
}

function SegmentSummaryCard({
  segment,
  canReadScenes,
  canReadTags,
  resolvedSpanRoute,
  onNavigate,
  showHeading = true,
}: {
  segment: SegmentRecord;
  canReadScenes: boolean;
  canReadTags: boolean;
  resolvedSpanRoute: Route | null;
  onNavigate: (r: any) => void;
  showHeading?: boolean;
}) {
  const displayTitle = segment.title?.trim() || segment.kind || segment.tagName || `Segment #${segment.id}`;

  return (
    <section className="rounded-2xl border border-border bg-card/70 p-5">
      {showHeading ? (
        <div>
          <h1 className="text-xl font-semibold text-foreground">{displayTitle}</h1>
          <p className="mt-1 text-sm text-secondary">Segment #{segment.id}</p>
        </div>
      ) : null}

      <div className={`${showHeading ? "mt-4" : ""} flex flex-wrap gap-2 text-xs`}>
        {segment.tagName ? <StatusPill label={segment.tagName} tone="accent" /> : null}
        {segment.kind ? <StatusPill label={segment.kind} tone="muted" /> : null}
        <StatusPill label={segment.sourceKey} tone="muted" />
      </div>

      <div className="mt-4 grid grid-cols-2 gap-2">
        <InfoMetric label="Duration" value={formatSegmentDuration(segment.startSec, segment.endSec)} />
        <InfoMetric label="Confidence" value={formatConfidence(segment.confidence)} />
        <InfoMetric label="Tag" value={segment.tagName || "Untagged"} />
        <InfoMetric label="Source Run" value={segment.sourceRunId || "Not set"} />
      </div>

      <dl className="mt-4 space-y-2 text-sm text-secondary">
        <div className="flex items-start justify-between gap-3">
          <dt className="text-muted">Range</dt>
          <dd className="text-right text-foreground">{formatSegmentRange(segment.startSec, segment.endSec)}</dd>
        </div>
        <div className="flex items-start justify-between gap-3">
          <dt className="text-muted">Host</dt>
          <dd className="text-right text-foreground">
            {segment.hostType === "scene" && canReadScenes ? (
              <button
                type="button"
                onClick={() => onNavigate(buildSceneRouteForSegment(segment))}
                className="text-accent hover:underline"
              >
                {segment.hostTitle || `Scene #${segment.hostId}`}
              </button>
            ) : (
              segment.hostTitle || `Scene #${segment.hostId}`
            )}
          </dd>
        </div>
        <div className="flex items-start justify-between gap-3">
          <dt className="text-muted">Created</dt>
          <dd className="text-right text-foreground">{formatDate(segment.createdAt)}</dd>
        </div>
        <div className="flex items-start justify-between gap-3">
          <dt className="text-muted">Updated</dt>
          <dd className="text-right text-foreground">{formatDate(segment.updatedAt)}</dd>
        </div>
      </dl>

      {(canReadScenes || resolvedSpanRoute || (segment.tagId && canReadTags)) ? (
        <div className="mt-5 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
          {segment.hostType === "scene" && canReadScenes ? (
            <button
              type="button"
              onClick={() => onNavigate(buildSceneRouteForSegment(segment))}
              className="w-full rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              Open scene at clip start
            </button>
          ) : null}
          {resolvedSpanRoute ? (
            <button
              type="button"
              onClick={() => onNavigate(resolvedSpanRoute)}
              className="w-full rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              Open resolved span
            </button>
          ) : null}
          {segment.tagId && canReadTags ? (
            <button
              type="button"
              onClick={() => onNavigate({ page: "tag", id: segment.tagId })}
              className="w-full rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              Open tag
            </button>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function SegmentPlaybackPanel({
  segment,
  scene,
  sceneLoading,
  canReadScenes,
  onNavigate,
  onTimeUpdate,
  embedded = false,
}: {
  segment: SegmentRecord;
  scene?: Scene;
  sceneLoading: boolean;
  canReadScenes: boolean;
  onNavigate: (r: any) => void;
  onTimeUpdate?: (time: number) => void;
  embedded?: boolean;
}) {
  const file = scene?.files[0];
  const clipDuration = getSegmentDuration(segment.startSec, segment.endSec);
  const containerClassName = embedded
    ? "flex min-h-0 min-w-0 max-w-full flex-1 flex-col overflow-hidden bg-black"
    : "self-start overflow-hidden rounded-3xl border border-border bg-card/80 shadow-sm xl:sticky xl:top-4";

  if (segment.hostType !== "scene") {
    return (
      <article className={containerClassName}>
        <div className="flex aspect-video items-center justify-center bg-surface/70 text-muted">
          <Film className="h-16 w-16" />
        </div>
        <div className="space-y-2 p-5">
          <h2 className="text-lg font-semibold text-foreground">Segment Playback</h2>
          <p className="text-sm text-secondary">Inline playback is only available for scene-backed segments right now.</p>
        </div>
      </article>
    );
  }

  if (!canReadScenes) {
    return (
      <article className={containerClassName}>
        <div className="flex aspect-video items-center justify-center bg-surface/70 text-muted">
          <Film className="h-16 w-16" />
        </div>
        <div className="space-y-2 p-5">
          <h2 className="text-lg font-semibold text-foreground">Segment Playback</h2>
          <p className="text-sm text-secondary">The shared scene player is unavailable because your current permissions do not allow scene playback.</p>
        </div>
      </article>
    );
  }

  return (
    <article className={containerClassName}>
      {!embedded ? (
        <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border px-5 py-4">
          <div>
            <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-muted">
              <Clapperboard className="h-3.5 w-3.5" />
              Segment Playback
            </div>
            <p className="mt-2 text-sm text-secondary">
              This now uses the same scene player surface as the main scene page, starting at the clip's time range.
            </p>
          </div>
          <div className="flex flex-wrap gap-2 text-xs">
            <StatusPill label={formatSegmentRange(segment.startSec, segment.endSec)} tone="accent" />
            {clipDuration > 0 ? <StatusPill label={formatSegmentDuration(segment.startSec, segment.endSec)} tone="muted" /> : null}
            <StatusPill label={segment.hostTitle || `Scene #${segment.hostId}`} tone="muted" />
          </div>
        </div>
      ) : null}

      <div className={embedded ? "flex min-h-0 min-w-0 max-w-full flex-1 flex-col overflow-hidden bg-black" : "bg-black px-3 py-3 sm:px-4"}>
        {sceneLoading ? (
          <div className={embedded ? "flex flex-1 items-center justify-center bg-black text-sm text-secondary" : "mx-auto flex aspect-video max-w-5xl items-center justify-center rounded-2xl bg-black text-sm text-secondary"}>
            Loading scene player...
          </div>
        ) : file ? (
          <div className={embedded ? "flex min-h-0 min-w-0 max-w-full flex-1 overflow-hidden bg-black" : "mx-auto aspect-video max-w-5xl overflow-hidden rounded-2xl bg-black"}>
            <VideoPlayer
              streamUrl={scenes.streamUrl(segment.hostId)}
              posterUrl={scenes.screenshotUrl(segment.hostId, segment.updatedAt)}
              format={file.format}
              duration={file.duration}
              resumeTime={segment.startSec}
              sceneId={segment.hostId}
              detections={[]}
              captions={file.captions}
              onPlay={() => {}}
              onTimeUpdate={onTimeUpdate}
              showAbLoop
              trackingEnabled={false}
              clip={{ start: segment.startSec, end: segment.endSec ?? file.duration, loop: true }}
            />
          </div>
        ) : (
          <div className={embedded ? "flex flex-1 items-center justify-center bg-black text-sm text-secondary" : "mx-auto flex aspect-video max-w-5xl items-center justify-center rounded-2xl bg-black text-sm text-secondary"}>
            No playable scene file is available for this segment.
          </div>
        )}
      </div>

      {!embedded ? (
      <div className="space-y-4 p-5">
        <div className="grid gap-3 sm:grid-cols-3">
          <InfoMetric label="Clip Start" value={formatSegmentTime(segment.startSec)} />
          <InfoMetric label="Clip End" value={segment.endSec != null ? formatSegmentTime(segment.endSec) : "Scene end"} />
          <InfoMetric label="Duration" value={clipDuration > 0 ? formatSegmentTime(clipDuration) : "Instant"} />
        </div>

        <div className="rounded-2xl border border-border bg-surface/50 p-4">
          <div className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-muted">Clip-focused playback</div>
          <p className="text-sm text-secondary">
            The full scene player opens at this segment's start time so you get the normal controls, captions, quality selection, A/B looping, and detection overlays from the main scene page.
          </p>
          <div className="mt-3 flex flex-wrap gap-2 text-xs">
            <StatusPill label={segment.sourceKey} tone="muted" />
            {segment.kind ? <StatusPill label={segment.kind} tone="muted" /> : null}
            {segment.tagName ? <StatusPill label={segment.tagName} tone="muted" /> : null}
            <StatusPill label={formatConfidence(segment.confidence)} tone="muted" />
          </div>
        </div>

        <div className="rounded-2xl border border-border bg-surface/50 p-4">
          <div className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-muted">Scene handoff</div>
          <p className="text-sm text-secondary">Open the parent scene exactly where this segment begins, or jump straight to the segment end for manual review.</p>
          <div className="mt-4 flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => onNavigate(buildSceneRouteForSegment(segment))}
              className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              <ExternalLink className="h-4 w-4" />
              Open at clip start
            </button>
            {segment.endSec != null ? (
              <button
                type="button"
                onClick={() => onNavigate(buildSceneRouteForSegment(segment, segment.endSec))}
                className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
              >
                <ExternalLink className="h-4 w-4" />
                Open at clip end
              </button>
            ) : null}
          </div>
        </div>
      </div>
      ) : null}
    </article>
  );
}

function InfoMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-border bg-surface/60 px-3 py-3">
      <div className="text-[11px] font-semibold uppercase tracking-wide text-muted">{label}</div>
      <div className="mt-1 text-sm font-medium text-foreground">{value}</div>
    </div>
  );
}

function SegmentContextSection({
  title,
  items,
  onNavigate,
  emptyMessage,
  compact = false,
}: {
  title: string;
  items: SegmentRecord[];
  onNavigate: (r: any) => void;
  emptyMessage: string;
  compact?: boolean;
}) {
  return (
    <div>
      <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">{title}</div>
      {items.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border bg-surface/30 px-3 py-3 text-sm text-secondary">
          {emptyMessage}
        </div>
      ) : (
        <div className="grid gap-2">
          {items.map((item) => {
            const titleText = item.title?.trim() || item.kind || item.tagName || `Segment #${item.id}`;
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => onNavigate({ page: "segment", id: item.id })}
                className="flex items-center justify-between gap-3 rounded-xl border border-border bg-surface/50 px-3 py-3 text-left transition-colors hover:border-accent"
              >
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium text-foreground">{titleText}</div>
                  <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-secondary">
                    <span>{formatSegmentRange(item.startSec, item.endSec)}</span>
                    {item.tagName ? <span>{item.tagName}</span> : null}
                    {!compact && item.kind ? <span>{item.kind}</span> : null}
                  </div>
                </div>
                <div className="shrink-0 text-xs text-muted">{compact ? item.sourceKey : formatSegmentDuration(item.startSec, item.endSec)}</div>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

function StatusPill({ label, tone }: { label: string; tone: "accent" | "muted" }) {
  const toneClass = tone === "accent"
    ? "border-accent/30 bg-accent/10 text-accent"
    : "border-border bg-surface text-secondary";

  return (
    <span className={`inline-flex items-center rounded-full border px-2 py-1 ${toneClass}`}>
      <Bookmark className="mr-1 h-3 w-3" />
      {label}
    </span>
  );
}

function EmptyPanel({ icon, message }: { icon: React.ReactNode; message: string }) {
  return (
    <div className="mt-4 flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-surface/40 px-4 py-8 text-center text-sm text-secondary">
      <div className="mb-3 opacity-60 text-muted">{icon}</div>
      <p>{message}</p>
    </div>
  );
}

function buildSceneRouteForSegment(segment: Pick<SegmentRecord, "hostId" | "startSec">, seekTo = segment.startSec) {
  return {
    page: "scene",
    id: segment.hostId,
    seekTo,
  };
}

function formatSegmentRange(startSec: number, endSec?: number) {
  const start = formatSegmentTime(startSec);
  return endSec == null ? start : `${start} - ${formatSegmentTime(endSec)}`;
}

function formatSegmentDuration(startSec: number, endSec?: number) {
  const duration = getSegmentDuration(startSec, endSec);
  return duration > 0 ? `${formatSegmentTime(duration)} long` : "Instant";
}

function getSegmentDuration(startSec?: number, endSec?: number) {
  if (startSec == null || endSec == null) {
    return 0;
  }

  return Math.max(0, endSec - startSec);
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

function formatConfidence(confidence?: number) {
  return confidence == null ? "Not set" : `${(confidence * 100).toFixed(0)}%`;
}

function clampNumber(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max);
}

function formatPayload(payload: unknown) {
  if (payload == null) {
    return "";
  }

  try {
    return JSON.stringify(payload, null, 2);
  } catch {
    return String(payload);
  }
}