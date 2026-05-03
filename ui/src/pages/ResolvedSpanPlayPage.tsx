import { type ReactNode, useCallback, useEffect, useMemo, useState } from "react";
import { useQueries, useQuery } from "@tanstack/react-query";
import { ExternalLink, Repeat, RotateCcw, SkipBack, SkipForward } from "lucide-react";
import { faces, performers, scenes, segmentDisplayProfiles, segmentLibrary, tags } from "../api/client";
import type { ResolvedSpanDetail, ResolvedSpanInterval, SegmentDerivedQueryDescriptor, SegmentRecord, SegmentSpanOperator } from "../api/types";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { MediaDetailLayout } from "../components/MediaDetailLayout/MediaDetailLayout";
import { VideoPlayer } from "../components/VideoPlayer";
import { useBackNavigation } from "../hooks/useBackNavigation";

interface Props {
  sceneId: number;
  spanKey: string;
  profileId?: number;
  derivedQueryDescriptor?: SegmentDerivedQueryDescriptor;
  onNavigate: (r: any) => void;
}

export function ResolvedSpanPlayPage({ sceneId, spanKey, profileId, derivedQueryDescriptor, onNavigate }: Props) {
  const { backLabel, goBack } = useBackNavigation({ page: "scene", id: sceneId }, onNavigate);
  const { data: detail, isLoading } = useQuery({
    queryKey: ["scene", sceneId, "span", spanKey, profileId],
    queryFn: () => scenes.segments.spanDetail(sceneId, spanKey, profileId),
  });

  useEffect(() => {
    if (!detail) {
      return;
    }

    const title = detail.span.tagName || detail.span.kind || detail.sceneTitle || `Span ${detail.span.spanKey}`;
    document.title = `${title} | Cove`;
    return () => {
      document.title = "Cove";
    };
  }, [detail]);

  if (isLoading) {
    return (
      <div className="px-1 py-2">
        <DetailSkeleton />
      </div>
    );
  }

  if (!detail) {
    return <div className="py-16 text-center text-secondary">Resolved span not found</div>;
  }

  return (
    <ResolvedSpanPlayerCard
      detail={detail}
      derivedQueryDescriptor={derivedQueryDescriptor}
      onNavigate={onNavigate}
      backLabel={backLabel}
      onGoBack={goBack}
    />
  );
}

function ResolvedSpanPlayerCard({
  detail,
  derivedQueryDescriptor,
  backLabel,
  onGoBack,
  onNavigate,
}: {
  detail: ResolvedSpanDetail;
  derivedQueryDescriptor?: SegmentDerivedQueryDescriptor;
  backLabel: string;
  onGoBack: () => void;
  onNavigate: (r: any) => void;
}) {
  const [currentAbsoluteTime, setCurrentAbsoluteTime] = useState(detail.intervals[0]?.startSec ?? detail.span.startSec);
  const [loopSpan, setLoopSpan] = useState(false);
  const [activeIntervalIndex, setActiveIntervalIndex] = useState(0);
  const [resumeTime, setResumeTime] = useState(detail.intervals[0]?.startSec ?? detail.span.startSec);
  const [autostart, setAutostart] = useState(false);
  const [autostartToken, setAutostartToken] = useState(0);

  const intervals = useMemo(
    () => detail.intervals.length > 0 ? detail.intervals : [{ startSec: detail.span.startSec, endSec: detail.span.endSec }],
    [detail.intervals, detail.span.endSec, detail.span.startSec],
  );
  const totalLogicalDuration = useMemo(
    () => intervals.reduce((sum, interval) => sum + Math.max(0, interval.endSec - interval.startSec), 0),
    [intervals],
  );
  const isDerivedQuery = useMemo(() => detail.span.spanKey.startsWith("dq-"), [detail.span.spanKey]);
  const derivedOperator = useMemo(
    () => derivedQueryDescriptor?.operator ?? parseDerivedOperator(detail.span.spanKey),
    [derivedQueryDescriptor?.operator, detail.span.spanKey],
  );
  const tagIds = useMemo(
    () => Array.from(new Set(derivedQueryDescriptor?.operands.flatMap((operand) => operand.tagIds ?? []) ?? [])),
    [derivedQueryDescriptor],
  );
  const performerIds = useMemo(
    () => Array.from(new Set(derivedQueryDescriptor?.operands.flatMap((operand) => operand.performerIds ?? []) ?? [])),
    [derivedQueryDescriptor],
  );
  const faceIds = useMemo(
    () => Array.from(new Set(derivedQueryDescriptor?.operands.flatMap((operand) => operand.faceIds ?? []) ?? [])),
    [derivedQueryDescriptor],
  );

  const profileQuery = useQuery({
    queryKey: ["segment-display-profile", detail.profileId],
    queryFn: () => segmentDisplayProfiles.get(detail.profileId),
    enabled: !isDerivedQuery,
    staleTime: 60_000,
  });

  const rawSegmentsQuery = useQuery({
    queryKey: ["segments", "ids", detail.span.segmentIds.join(",")],
    queryFn: async () => {
      const response = await segmentLibrary.list({
        ids: detail.span.segmentIds.join(","),
        sort: "start_sec",
        direction: "asc",
        page: 1,
        perPage: Math.max(detail.span.segmentIds.length, 1),
      });
      return response.items;
    },
    enabled: !isDerivedQuery && detail.span.segmentIds.length > 0,
    staleTime: 60_000,
  });

  const tagQueries = useQueries({
    queries: tagIds.map((tagId) => ({
      queryKey: ["tag", tagId],
      queryFn: () => tags.get(tagId),
      staleTime: 60_000,
    })),
  });
  const performerQueries = useQueries({
    queries: performerIds.map((performerId) => ({
      queryKey: ["performer", performerId],
      queryFn: () => performers.get(performerId),
      staleTime: 60_000,
    })),
  });
  const faceQueries = useQueries({
    queries: faceIds.map((faceId) => ({
      queryKey: ["face", faceId],
      queryFn: () => faces.get(faceId),
      staleTime: 60_000,
    })),
  });

  const tagNamesById = useMemo(() => {
    const map = new Map<number, string>();
    tagIds.forEach((tagId, index) => {
      const tag = tagQueries[index]?.data;
      if (tag) {
        map.set(tagId, tag.name);
      }
    });
    return map;
  }, [tagIds, tagQueries]);
  const performerNamesById = useMemo(() => {
    const map = new Map<number, string>();
    performerIds.forEach((performerId, index) => {
      const performer = performerQueries[index]?.data;
      if (performer) {
        map.set(performerId, performer.name);
      }
    });
    return map;
  }, [performerIds, performerQueries]);
  const faceLabelsById = useMemo(() => {
    const map = new Map<number, string>();
    faceIds.forEach((faceId, index) => {
      const face = faceQueries[index]?.data;
      if (face) {
        map.set(faceId, face.label?.trim() || face.performerName?.trim() || `Face #${faceId}`);
      }
    });
    return map;
  }, [faceIds, faceQueries]);

  const { data: currentScene, isLoading: currentSceneLoading } = useQuery({
    queryKey: ["scene", detail.sceneId],
    queryFn: () => scenes.get(detail.sceneId),
    staleTime: 60_000,
  });
  const currentFile = currentScene?.files[0];

  useEffect(() => {
    const initialStart = intervals[0]?.startSec ?? detail.span.startSec;
    setCurrentAbsoluteTime(initialStart);
    setResumeTime(initialStart);
    setLoopSpan(false);
    setAutostart(false);
    setAutostartToken(0);
    setActiveIntervalIndex(0);
  }, [detail.span.spanKey, detail.span.startSec, intervals]);

  const currentInterval = intervals[activeIntervalIndex] ?? intervals[0];
  const currentLogicalTime = useMemo(
    () => absoluteToLogicalTime(currentAbsoluteTime, intervals, activeIntervalIndex),
    [activeIntervalIndex, currentAbsoluteTime, intervals],
  );

  const seekAbsolute = useCallback((nextTime: number) => {
    const nextIndex = findIntervalIndex(nextTime, intervals);
    const bounded = clampNumber(nextTime, intervals[nextIndex].startSec, intervals[nextIndex].endSec);
    setActiveIntervalIndex(nextIndex);
    setResumeTime(bounded);
    setCurrentAbsoluteTime(bounded);
  }, [intervals]);

  const seekLogical = useCallback((nextLogicalTime: number) => {
    const absolute = logicalToAbsoluteTime(nextLogicalTime, intervals);
    seekAbsolute(absolute);
  }, [intervals, seekAbsolute]);

  const advanceInterval = useCallback(() => {
    const nextIndex = activeIntervalIndex + 1;
    if (nextIndex < intervals.length) {
      const nextStart = intervals[nextIndex].startSec;
      setActiveIntervalIndex(nextIndex);
      setResumeTime(nextStart);
      setCurrentAbsoluteTime(nextStart);
      setAutostart(true);
      setAutostartToken((value) => value + 1);
      return;
    }

    if (loopSpan && intervals.length > 0) {
      const nextStart = intervals[0].startSec;
      setActiveIntervalIndex(0);
      setResumeTime(nextStart);
      setCurrentAbsoluteTime(nextStart);
      setAutostart(true);
      setAutostartToken((value) => value + 1);
      return;
    }

    setAutostart(false);
    const endTime = intervals[intervals.length - 1]?.endSec ?? detail.span.endSec;
    setResumeTime(endTime);
    setCurrentAbsoluteTime(endTime);
  }, [activeIntervalIndex, detail.span.endSec, intervals, loopSpan]);

  const handlePlayerTimeUpdate = useCallback((nextTime: number) => {
    setCurrentAbsoluteTime(nextTime);
    const nextIndex = findIntervalIndex(nextTime, intervals);
    if (nextIndex !== activeIntervalIndex) {
      setActiveIntervalIndex(nextIndex);
    }
  }, [activeIntervalIndex, intervals]);

  const restartSpan = useCallback(() => {
    if (intervals.length === 0) {
      return;
    }

    const start = intervals[0].startSec;
    setActiveIntervalIndex(0);
    setResumeTime(start);
    setCurrentAbsoluteTime(start);
    setAutostart(true);
    setAutostartToken((value) => value + 1);
  }, [intervals]);

  const spanTitle = detail.span.tagName || detail.span.kind || detail.sceneTitle || `Span ${detail.span.spanKey}`;
  const clipProgress = totalLogicalDuration > 0 ? Math.min(100, (currentLogicalTime / totalLogicalDuration) * 100) : 0;
  const spanSummary = isDerivedQuery && derivedOperator
    ? `Derived ${formatOperatorLabel(derivedOperator)}`
    : detail.span.kind || "Resolved span";
  const playbackDescription = isDerivedQuery
    ? `Playback follows the resolved ${derivedOperator ? formatOperatorLabel(derivedOperator).toLowerCase() : "derived"} output intervals and automatically skips the gaps between them.`
    : "Playback follows the resolved span intervals and automatically skips the gaps between them.";

  const playerMedia = (
    <div className="flex min-h-0 min-w-0 max-w-full flex-1 flex-col overflow-hidden bg-black">
      {currentSceneLoading ? (
        <div className="flex flex-1 items-center justify-center bg-black text-sm text-secondary">
          Loading resolved span playback...
        </div>
      ) : currentFile ? (
        <div className="flex min-h-0 min-w-0 max-w-full flex-1 overflow-hidden bg-black">
          <VideoPlayer
            streamUrl={scenes.streamUrl(detail.sceneId)}
            posterUrl={scenes.screenshotUrl(detail.sceneId)}
            format={currentFile.format}
            duration={currentFile.duration}
            resumeTime={resumeTime}
            sceneId={detail.sceneId}
            detections={[]}
            captions={currentFile.captions}
            onPlay={() => setAutostart(false)}
            onTimeUpdate={handlePlayerTimeUpdate}
            autostart={autostart}
            autostartToken={autostartToken}
            playbackTracking={{
              hostType: "scene",
              hostId: detail.sceneId,
              scopeKey: `scene:${detail.sceneId}:span:${detail.span.spanKey}`,
            }}
            onEnded={advanceInterval}
            clip={{ start: currentInterval.startSec, end: currentInterval.endSec, loop: false }}
          />
        </div>
      ) : (
        <div className="flex flex-1 items-center justify-center bg-black text-sm text-secondary">
          No playable scene file is available for this resolved span.
        </div>
      )}
    </div>
  );

  return (
    <MediaDetailLayout
      title={spanTitle}
      subtitle={
        <div className="flex flex-wrap items-center gap-3 text-sm text-secondary">
          {detail.sceneTitle ? <span>Scene: {detail.sceneTitle}</span> : null}
          <span>{spanSummary}</span>
          <span>{formatTime(detail.span.startSec)} - {formatTime(detail.span.endSec)}</span>
          <span>{intervals.length} interval{intervals.length === 1 ? "" : "s"}</span>
          <span>Profile {detail.profileId}</span>
        </div>
      }
      backLabel={backLabel}
      onGoBack={onGoBack}
      media={playerMedia}
      mediaAspectRatio="auto"
      mediaFullBleed
      mediaSticky={false}
    >
      <MediaDetailLayout.Content>
        <div className="space-y-4">
          <section className="rounded-2xl border border-border bg-card/70 p-5">
            <div className="text-xs font-semibold uppercase tracking-wide text-muted">Resolved Span Playback</div>
            <p className="mt-2 text-sm text-secondary">{playbackDescription}</p>
          </section>

          <div className="rounded-2xl border border-border bg-surface/50 p-4">
          <div className="mb-2 flex items-center justify-between gap-3 text-xs text-muted">
            <span>Union progress</span>
            <span>{formatTime(currentLogicalTime)} / {formatTime(totalLogicalDuration)}</span>
          </div>
          <input
            type="range"
            min={0}
            max={Math.max(totalLogicalDuration, 0.001)}
            step={0.01}
            value={Math.min(currentLogicalTime, Math.max(totalLogicalDuration, 0.001))}
            onChange={(event) => seekLogical(Number(event.target.value))}
            disabled={!currentFile || currentSceneLoading || totalLogicalDuration <= 0}
            className="w-full accent-accent disabled:cursor-not-allowed disabled:opacity-50"
            aria-label="Seek within the resolved span"
          />
          <div className="mt-3 flex items-center justify-between text-xs text-secondary">
            <span>Active interval {activeIntervalIndex + 1} of {intervals.length}</span>
            <span>{clipProgress.toFixed(0)}%</span>
          </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={restartSpan}
              disabled={!currentFile || currentSceneLoading}
              className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
            >
              <RotateCcw className="h-4 w-4" />
              Restart
            </button>
            <button
              type="button"
              onClick={() => seekLogical(Math.max(0, currentLogicalTime - 5))}
              disabled={!currentFile || currentSceneLoading}
              className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
            >
              <SkipBack className="h-4 w-4" />
              -5s
            </button>
            <button
              type="button"
              onClick={() => seekLogical(Math.min(totalLogicalDuration, currentLogicalTime + 5))}
              disabled={!currentFile || currentSceneLoading}
              className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
            >
              <SkipForward className="h-4 w-4" />
              +5s
            </button>
            <button
              type="button"
              onClick={() => setLoopSpan((value) => !value)}
              className={`inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors ${loopSpan ? "border-accent bg-accent/10 text-accent" : "border-border text-foreground hover:border-accent"}`}
            >
              <Repeat className="h-4 w-4" />
              Loop span
            </button>
          </div>

          <div className="grid gap-4 lg:grid-cols-[minmax(0,1.1fr),minmax(0,0.9fr)]">
            <div className="rounded-2xl border border-border bg-surface/50 p-4">
              <div className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-muted">Intervals</div>
              <div className="space-y-2">
                {intervals.map((interval, index) => (
                  <button
                    key={`${interval.startSec}-${interval.endSec}`}
                    type="button"
                    onClick={() => seekAbsolute(interval.startSec)}
                    className={`flex w-full items-center justify-between rounded-xl border px-3 py-2 text-left text-sm transition-colors ${index === activeIntervalIndex ? "border-accent bg-accent/10 text-foreground" : "border-border bg-card/60 text-secondary hover:border-accent"}`}
                  >
                    <span>Interval {index + 1}</span>
                    <span className="font-mono text-xs">{formatTime(interval.startSec)} - {formatTime(interval.endSec)}</span>
                  </button>
                ))}
              </div>
            </div>

            <ResolvedSpanProvenanceCard
              detail={detail}
              derivedOperator={derivedOperator}
              derivedQueryDescriptor={derivedQueryDescriptor}
              profileName={profileQuery.data?.name}
              rawSegments={rawSegmentsQuery.data ?? []}
              rawSegmentsLoading={rawSegmentsQuery.isLoading}
              tagNamesById={tagNamesById}
              performerNamesById={performerNamesById}
              faceLabelsById={faceLabelsById}
              onNavigate={onNavigate}
            />
          </div>
        </div>
      </MediaDetailLayout.Content>
    </MediaDetailLayout>
  );
}

function ResolvedSpanProvenanceCard({
  detail,
  derivedOperator,
  derivedQueryDescriptor,
  profileName,
  rawSegments,
  rawSegmentsLoading,
  tagNamesById,
  performerNamesById,
  faceLabelsById,
  onNavigate,
}: {
  detail: ResolvedSpanDetail;
  derivedOperator?: SegmentSpanOperator;
  derivedQueryDescriptor?: SegmentDerivedQueryDescriptor;
  profileName?: string;
  rawSegments: SegmentRecord[];
  rawSegmentsLoading: boolean;
  tagNamesById: Map<number, string>;
  performerNamesById: Map<number, string>;
  faceLabelsById: Map<number, string>;
  onNavigate: (r: any) => void;
}) {
  const isDerivedQuery = detail.span.spanKey.startsWith("dq-");

  return (
    <div className="rounded-2xl border border-border bg-surface/50 p-4">
      <div className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-muted">Provenance</div>
      <div className="space-y-4 text-sm text-secondary">
        <div>Scene: {detail.sceneTitle || `Scene #${detail.sceneId}`}</div>

        {isDerivedQuery ? (
          derivedQueryDescriptor ? (
            <>
              <div>
                Derived span: <span className="font-medium text-foreground">{formatOperatorLabel(derivedOperator ?? derivedQueryDescriptor.operator)}</span>
              </div>
              <div className="space-y-2">
                {derivedQueryDescriptor.operands.map((operand, index) => (
                  <div key={`${index}-${operand.sourceKey ?? ""}-${operand.kind ?? ""}`} className="rounded-xl border border-border bg-card/70 px-3 py-3">
                    <div className="text-xs font-semibold uppercase tracking-wide text-muted">Operand {index + 1}</div>
                    <div className="mt-2 flex flex-wrap gap-2">
                      {operand.sourceKey ? <ProvenanceChip>{operand.sourceKey}</ProvenanceChip> : null}
                      {operand.kind ? <ProvenanceChip>{operand.kind}</ProvenanceChip> : null}
                      {operand.minConfidence != null ? <ProvenanceChip>Min confidence {operand.minConfidence}</ProvenanceChip> : null}
                      {operand.tagIds?.map((tagId) => (
                        <button
                          key={`tag-${tagId}`}
                          type="button"
                          onClick={() => onNavigate({ page: "tag", id: tagId })}
                          className="rounded-full border border-border bg-card px-2.5 py-1 text-xs text-foreground transition-colors hover:border-accent"
                        >
                          Tag: {tagNamesById.get(tagId) ?? `#${tagId}`}
                        </button>
                      ))}
                      {operand.performerIds?.map((performerId) => (
                        <button
                          key={`performer-${performerId}`}
                          type="button"
                          onClick={() => onNavigate({ page: "performer", id: performerId })}
                          className="rounded-full border border-border bg-card px-2.5 py-1 text-xs text-foreground transition-colors hover:border-accent"
                        >
                          Performer: {performerNamesById.get(performerId) ?? `#${performerId}`}
                        </button>
                      ))}
                      {operand.faceIds?.map((faceId) => (
                        <button
                          key={`face-${faceId}`}
                          type="button"
                          onClick={() => onNavigate({ page: "face", id: faceId })}
                          className="rounded-full border border-border bg-card px-2.5 py-1 text-xs text-foreground transition-colors hover:border-accent"
                        >
                          Face: {faceLabelsById.get(faceId) ?? `#${faceId}`}
                        </button>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
              <div className="flex flex-wrap gap-2">
                {derivedQueryDescriptor.mergeGapSec != null ? <ProvenanceChip>Merge gap {formatTime(derivedQueryDescriptor.mergeGapSec)}</ProvenanceChip> : null}
                {derivedQueryDescriptor.minDurationSec != null ? <ProvenanceChip>Min duration {formatTime(derivedQueryDescriptor.minDurationSec)}</ProvenanceChip> : null}
              </div>
            </>
          ) : (
            <div className="rounded-xl border border-border bg-card/70 px-3 py-3 text-sm text-secondary">
              <div className="font-medium text-foreground">Derived span: {formatOperatorLabel(derivedOperator ?? "intersection")}</div>
              <p className="mt-2">Operands are unavailable for this derived span because it was opened directly from its URL. Reopen it from the Segments page to see the full intersection/union/difference inputs.</p>
            </div>
          )
        ) : (
          <>
            <div>
              Resolved span on profile <span className="font-medium text-foreground">{profileName ?? `Profile ${detail.profileId}`}</span>
            </div>
            <div className="flex flex-wrap gap-2">
              {detail.span.tagName ? <ProvenanceChip>{detail.span.tagName}</ProvenanceChip> : null}
              {detail.span.kind ? <ProvenanceChip>{detail.span.kind}</ProvenanceChip> : null}
              {detail.span.sourceKey ? <ProvenanceChip>{detail.span.sourceKey}</ProvenanceChip> : null}
              {detail.span.colorHint ? <ProvenanceChip>{detail.span.colorHint}</ProvenanceChip> : null}
            </div>
            <div>
              <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">Raw segments</div>
              {rawSegmentsLoading ? (
                <div className="rounded-xl border border-border bg-card/70 px-3 py-3">Loading raw segments...</div>
              ) : rawSegments.length > 0 ? (
                <div className="space-y-2">
                  {rawSegments.map((segment) => (
                    <button
                      key={segment.id}
                      type="button"
                      onClick={() => onNavigate({ page: "segment", id: segment.id })}
                      className="flex w-full items-center justify-between gap-3 rounded-xl border border-border bg-card/70 px-3 py-3 text-left transition-colors hover:border-accent"
                    >
                      <div className="min-w-0">
                        <div className="truncate text-sm font-medium text-foreground">#{segment.id} {segment.title?.trim() || segment.tagName || segment.kind || segment.sourceKey}</div>
                        <div className="mt-1 flex flex-wrap gap-2 text-xs text-secondary">
                          {segment.sourceKey ? <span>{segment.sourceKey}</span> : null}
                          {segment.kind ? <span>{segment.kind}</span> : null}
                          {segment.confidence != null ? <span>{segment.confidence.toFixed(2)} conf</span> : null}
                        </div>
                      </div>
                      <div className="shrink-0 text-xs font-mono text-secondary">{formatTime(segment.startSec)} - {formatTime(segment.endSec ?? segment.startSec)}</div>
                    </button>
                  ))}
                </div>
              ) : (
                <div className="rounded-xl border border-border bg-card/70 px-3 py-3">No raw segments were returned for this resolved span.</div>
              )}
            </div>
          </>
        )}

        <div className="flex flex-wrap gap-2 pt-1">
          <button
            type="button"
            onClick={() => onNavigate({ page: "scene", id: detail.sceneId, seekTo: detail.span.startSec })}
            className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
          >
            <ExternalLink className="h-4 w-4" />
            Open scene at span start
          </button>
        </div>
      </div>
    </div>
  );
}

function ProvenanceChip({ children }: { children: ReactNode }) {
  return (
    <span className="rounded-full border border-border bg-card px-2.5 py-1 text-xs text-foreground">
      {children}
    </span>
  );
}

function findIntervalIndex(time: number, intervals: ResolvedSpanInterval[]) {
  const containingIndex = intervals.findIndex((interval) => time >= interval.startSec && time <= interval.endSec);
  if (containingIndex >= 0) {
    return containingIndex;
  }

  const nextIndex = intervals.findIndex((interval) => time < interval.startSec);
  if (nextIndex >= 0) {
    return nextIndex;
  }

  return Math.max(0, intervals.length - 1);
}

function logicalToAbsoluteTime(logicalTime: number, intervals: ResolvedSpanInterval[]) {
  let remaining = Math.max(0, logicalTime);
  for (const interval of intervals) {
    const duration = Math.max(0, interval.endSec - interval.startSec);
    if (remaining <= duration) {
      return interval.startSec + remaining;
    }
    remaining -= duration;
  }

  const lastInterval = intervals[intervals.length - 1];
  return lastInterval ? lastInterval.endSec : 0;
}

function absoluteToLogicalTime(currentTime: number, intervals: ResolvedSpanInterval[], activeIntervalIndex: number) {
  let elapsed = 0;
  for (let index = 0; index < intervals.length; index += 1) {
    const interval = intervals[index];
    const duration = Math.max(0, interval.endSec - interval.startSec);
    if (index === activeIntervalIndex) {
      return elapsed + clampNumber(currentTime, interval.startSec, interval.endSec) - interval.startSec;
    }
    elapsed += duration;
  }

  return elapsed;
}

function clampNumber(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function parseDerivedOperator(spanKey: string): SegmentSpanOperator | undefined {
  const parts = spanKey.split("-", 4);
  const operator = parts[1];
  return operator === "intersection" || operator === "union" || operator === "difference"
    ? operator
    : undefined;
}

function formatOperatorLabel(operator: SegmentSpanOperator) {
  switch (operator) {
    case "intersection":
      return "Intersection";
    case "union":
      return "Union";
    case "difference":
      return "Difference";
    default:
      return operator;
  }
}

function formatTime(value: number) {
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