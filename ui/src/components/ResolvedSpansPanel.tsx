import { useEffect, useMemo, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { Layers, Plus, ExternalLink } from "lucide-react";
import { scenes } from "../api/client";
import type { ResolvedSpan, SegmentDisplayProfile, SegmentSpanOperand, SegmentSpanOperator } from "../api/types";
import { AddToGroupDialog, type AddToGroupEntry } from "./AddToGroupDialog";

interface Props {
  sceneId: number;
  spans: ResolvedSpan[];
  loading: boolean;
  profiles: SegmentDisplayProfile[];
  currentProfileId?: number;
  onProfileChange: (profileId: number) => void;
  onSeek?: (time: number) => void;
  onNavigate: (r: any) => void;
}

export function ResolvedSpansPanel({
  sceneId,
  spans,
  loading,
  profiles,
  currentProfileId,
  onProfileChange,
  onSeek,
  onNavigate,
}: Props) {
  const [selectedSpanKeys, setSelectedSpanKeys] = useState<Set<string>>(new Set());
  const [showAddDialog, setShowAddDialog] = useState(false);
  const [showDerivedQuery, setShowDerivedQuery] = useState(false);
  const [operator, setOperator] = useState<SegmentSpanOperator>("intersection");
  const [mergeGapText, setMergeGapText] = useState("");
  const [minDurationText, setMinDurationText] = useState("");
  const [operands, setOperands] = useState<QueryOperandDraft[]>(createDefaultOperands());
  const [derivedSpans, setDerivedSpans] = useState<ResolvedSpan[]>([]);

  useEffect(() => {
    setSelectedSpanKeys(new Set());
    setDerivedSpans([]);
  }, [currentProfileId, sceneId]);

  const allSelectableSpans = useMemo(() => {
    const byKey = new Map<string, ResolvedSpan>();
    spans.forEach((span) => byKey.set(span.spanKey, span));
    derivedSpans.forEach((span) => byKey.set(span.spanKey, span));
    return Array.from(byKey.values());
  }, [derivedSpans, spans]);

  const selectedEntries = useMemo<AddToGroupEntry[]>(() => {
    return allSelectableSpans
      .filter((span) => selectedSpanKeys.has(span.spanKey))
      .map((span) => ({
        key: span.spanKey,
        sceneId,
        spanKey: span.spanKey,
        title: span.tagName || span.kind || `Span ${span.spanKey}`,
        profileId: currentProfileId,
      }));
  }, [allSelectableSpans, currentProfileId, sceneId, selectedSpanKeys]);

  const orderedProfiles = useMemo(
    () => [...profiles].sort((left, right) => Number(right.isDefault) - Number(left.isDefault) || left.name.localeCompare(right.name)),
    [profiles],
  );

  const activeProfileId = currentProfileId ?? orderedProfiles.find((profile) => profile.isDefault)?.id ?? orderedProfiles[0]?.id;

  const queryMutation = useMutation({
    mutationFn: (request: { profile?: number; operator: SegmentSpanOperator; operands: SegmentSpanOperand[]; mergeGapSec?: number; minDurationSec?: number }) =>
      scenes.segments.querySpans(sceneId, request),
    onSuccess: (result) => {
      setDerivedSpans(result.spans);
      setSelectedSpanKeys(new Set());
    },
  });

  const toggleSelection = (spanKey: string) => {
    setSelectedSpanKeys((current) => {
      const next = new Set(current);
      if (next.has(spanKey)) {
        next.delete(spanKey);
      } else {
        next.add(spanKey);
      }
      return next;
    });
  };

  const updateOperand = (index: number, field: keyof QueryOperandDraft, value: string) => {
    setOperands((current) => current.map((operand, operandIndex) => (
      operandIndex === index ? { ...operand, [field]: value } : operand
    )));
  };

  const removeOperand = (index: number) => {
    setOperands((current) => current.filter((_, operandIndex) => operandIndex !== index));
  };

  const runDerivedQuery = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (activeProfileId == null) {
      return;
    }

    const requestOperands = operands
      .map(toQueryOperand)
      .filter((operand) => operand != null);

    if (requestOperands.length === 0) {
      return;
    }

    queryMutation.mutate({
      profile: activeProfileId,
      operator,
      operands: requestOperands,
      mergeGapSec: parseOptionalNumber(mergeGapText),
      minDurationSec: parseOptionalNumber(minDurationText),
    });
  };

  const clearDerivedResults = () => {
    setDerivedSpans([]);
    setSelectedSpanKeys(new Set());
  };

  return (
    <section className="rounded-2xl border border-border bg-card/70 p-5">
      <AddToGroupDialog open={showAddDialog} onClose={() => setShowAddDialog(false)} items={selectedEntries} onAdded={() => setSelectedSpanKeys(new Set())} />
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-2 text-sm font-semibold uppercase tracking-wide text-muted">
            <Layers className="h-4 w-4" />
            Resolved Spans
          </div>
          <p className="mt-2 text-sm text-secondary">The scene timeline now uses resolved spans from the selected display profile. Raw segment editing remains below.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <label className="text-xs font-semibold uppercase tracking-wide text-muted">
            Profile
            <select
              value={activeProfileId ?? ""}
              onChange={(event) => onProfileChange(Number(event.target.value))}
              className="ml-2 rounded-lg border border-border bg-card px-3 py-2 text-sm font-normal text-foreground focus:border-accent focus:outline-none"
            >
              {orderedProfiles.map((profile) => (
                <option key={profile.id} value={profile.id}>
                  {profile.name}{profile.isDefault ? " (Default)" : ""}{profile.userId == null ? "" : " (Mine)"}
                </option>
              ))}
            </select>
          </label>
          {selectedEntries.length > 0 ? (
            <button
              type="button"
              onClick={() => setShowAddDialog(true)}
              className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              <Plus className="h-4 w-4" />
              Add selected to group
            </button>
          ) : null}
          <button
            type="button"
            onClick={() => setShowDerivedQuery((value) => !value)}
            className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
          >
            <Plus className="h-4 w-4" />
            {showDerivedQuery ? "Hide derived query" : "Derived query"}
          </button>
        </div>
      </div>

      {showDerivedQuery ? (
        <form onSubmit={runDerivedQuery} className="mt-4 rounded-2xl border border-border bg-surface/40 p-4">
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <div className="text-xs font-semibold uppercase tracking-wide text-muted">Interval algebra</div>
              <p className="mt-2 text-sm text-secondary">Build a derived span set by intersecting, unioning, or subtracting raw marker criteria against the active display profile.</p>
            </div>
            {derivedSpans.length > 0 ? (
              <button
                type="button"
                onClick={clearDerivedResults}
                className="rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
              >
                Clear results
              </button>
            ) : null}
          </div>

          <div className="mt-4 grid gap-3 md:grid-cols-3">
            <label className="text-sm text-secondary">
              Operator
              <select
                value={operator}
                onChange={(event) => setOperator(event.target.value as SegmentSpanOperator)}
                className="mt-1 w-full rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
              >
                <option value="intersection">Intersection</option>
                <option value="union">Union</option>
                <option value="difference">Difference</option>
              </select>
            </label>
            <label className="text-sm text-secondary">
              Merge gap (sec)
              <input
                type="number"
                min="0"
                step="0.1"
                value={mergeGapText}
                onChange={(event) => setMergeGapText(event.target.value)}
                className="mt-1 w-full rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                placeholder="Optional"
              />
            </label>
            <label className="text-sm text-secondary">
              Minimum duration (sec)
              <input
                type="number"
                min="0"
                step="0.1"
                value={minDurationText}
                onChange={(event) => setMinDurationText(event.target.value)}
                className="mt-1 w-full rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                placeholder="Optional"
              />
            </label>
          </div>

          <div className="mt-4 space-y-3">
            {operands.map((operand, index) => (
              <div key={index} className="rounded-xl border border-border bg-card/70 p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="text-xs font-semibold uppercase tracking-wide text-muted">Operand {index + 1}</div>
                  {operands.length > 2 ? (
                    <button
                      type="button"
                      onClick={() => removeOperand(index)}
                      className="text-xs text-secondary transition-colors hover:text-foreground"
                    >
                      Remove operand
                    </button>
                  ) : null}
                </div>

                <div className="mt-3 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
                  <label className="text-sm text-secondary">
                    Source key
                    <input
                      type="text"
                      value={operand.sourceKey}
                      onChange={(event) => updateOperand(index, "sourceKey", event.target.value)}
                      className="mt-1 w-full rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                      placeholder="performer, face, seen"
                    />
                  </label>
                  <label className="text-sm text-secondary">
                    Kind
                    <input
                      type="text"
                      value={operand.kind}
                      onChange={(event) => updateOperand(index, "kind", event.target.value)}
                      className="mt-1 w-full rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                      placeholder="performer, face"
                    />
                  </label>
                  <label className="text-sm text-secondary">
                    Tag IDs
                    <input
                      type="text"
                      value={operand.tagIds}
                      onChange={(event) => updateOperand(index, "tagIds", event.target.value)}
                      className="mt-1 w-full rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                      placeholder="1, 2, 3"
                    />
                  </label>
                  <label className="text-sm text-secondary">
                    Ref IDs
                    <input
                      type="text"
                      value={operand.refIds}
                      onChange={(event) => updateOperand(index, "refIds", event.target.value)}
                      className="mt-1 w-full rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                      placeholder="42, 99"
                    />
                  </label>
                  <label className="text-sm text-secondary">
                    Min confidence
                    <input
                      type="number"
                      min="0"
                      max="1"
                      step="0.01"
                      value={operand.minConfidence}
                      onChange={(event) => updateOperand(index, "minConfidence", event.target.value)}
                      className="mt-1 w-full rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                      placeholder="Optional"
                    />
                  </label>
                </div>
              </div>
            ))}
          </div>

          <div className="mt-4 flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => setOperands((current) => [...current, { sourceKey: "", kind: "", tagIds: "", refIds: "", minConfidence: "" }])}
              className="rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              Add operand
            </button>
            <button
              type="submit"
              disabled={queryMutation.isPending}
              className="rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
            >
              {queryMutation.isPending ? "Running query..." : "Run query"}
            </button>
          </div>

          {queryMutation.isError ? (
            <div className="mt-3 rounded-lg border border-danger/40 bg-danger/10 px-3 py-2 text-sm text-danger">
              Derived query failed. Check the operand criteria and try again.
            </div>
          ) : null}
        </form>
      ) : null}

      {loading ? (
        <div className="mt-4 text-sm text-secondary">Loading resolved spans...</div>
      ) : spans.length === 0 ? (
        <div className="mt-4 rounded-xl border border-dashed border-border bg-surface/40 px-4 py-6 text-sm text-secondary">
          This profile did not resolve any spans for the current scene.
        </div>
      ) : (
        <div className="mt-4 space-y-2">
          <div className="text-xs font-semibold uppercase tracking-wide text-muted">Profile spans</div>
          {spans.map((span) => renderSpanCard({
            span,
            sceneId,
            profileId: activeProfileId,
            checked: selectedSpanKeys.has(span.spanKey),
            onToggle: toggleSelection,
            onSeek,
            onNavigate,
          }))}
        </div>
      )}

      {derivedSpans.length > 0 ? (
        <div className="mt-4 space-y-2">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="text-xs font-semibold uppercase tracking-wide text-muted">Derived query results</div>
            <div className="text-xs text-secondary">{derivedSpans.length} span{derivedSpans.length === 1 ? "" : "s"} returned</div>
          </div>
          {derivedSpans.map((span) => renderSpanCard({
            span,
            sceneId,
            profileId: activeProfileId,
            checked: selectedSpanKeys.has(span.spanKey),
            onToggle: toggleSelection,
            onSeek,
            onNavigate,
          }))}
        </div>
      ) : null}
    </section>
  );
}

type QueryOperandDraft = {
  sourceKey: string;
  kind: string;
  tagIds: string;
  refIds: string;
  minConfidence: string;
};

function createDefaultOperands(): QueryOperandDraft[] {
  return [
    { sourceKey: "", kind: "", tagIds: "", refIds: "", minConfidence: "" },
    { sourceKey: "", kind: "", tagIds: "", refIds: "", minConfidence: "" },
  ];
}

function toQueryOperand(operand: QueryOperandDraft): SegmentSpanOperand | null {
  const sourceKey = operand.sourceKey.trim();
  const kind = operand.kind.trim();
  const tagIds = operand.tagIds
    .split(",")
    .map((value) => Number(value.trim()))
    .filter((value) => Number.isFinite(value) && value > 0);
  const refIds = operand.refIds
    .split(",")
    .map((value) => Number(value.trim()))
    .filter((value) => Number.isFinite(value) && value > 0);
  const minConfidence = parseOptionalNumber(operand.minConfidence);

  if (!sourceKey && !kind && tagIds.length === 0 && refIds.length === 0 && minConfidence == null) {
    return null;
  }

  return {
    sourceKey: sourceKey || undefined,
    kind: kind || undefined,
    tagIds: tagIds.length > 0 ? tagIds : undefined,
    refIds: refIds.length > 0 ? refIds : undefined,
    minConfidence,
  };
}

function parseOptionalNumber(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return undefined;
  }

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function renderSpanCard({
  span,
  sceneId,
  profileId,
  checked,
  onToggle,
  onSeek,
  onNavigate,
}: {
  span: ResolvedSpan;
  sceneId: number;
  profileId?: number;
  checked: boolean;
  onToggle: (spanKey: string) => void;
  onSeek?: (time: number) => void;
  onNavigate: (r: any) => void;
}) {
  const label = span.tagName || span.kind || span.sourceKey || `Span ${span.spanKey}`;

  return (
    <div key={span.spanKey} className={`rounded-xl border px-3 py-3 transition-colors ${checked ? "border-accent bg-accent/10" : "border-border bg-surface/40"}`}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-start gap-3">
          <input
            type="checkbox"
            checked={checked}
            onChange={() => onToggle(span.spanKey)}
            className="mt-1 h-4 w-4 rounded border-border accent-accent"
          />
          <button type="button" onClick={() => onSeek?.(span.startSec)} className="min-w-0 text-left hover:text-accent">
            <div className="truncate text-sm font-medium text-foreground">{label}</div>
            <div className="mt-1 flex flex-wrap gap-2 text-xs text-secondary">
              <span>{formatTime(span.startSec)} - {formatTime(span.endSec)}</span>
              <span>{span.segmentIds.length} raw segment{span.segmentIds.length === 1 ? "" : "s"}</span>
              {span.sourceKey ? <span>{span.sourceKey}</span> : null}
            </div>
          </button>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => onNavigate({ page: "scene-span", id: sceneId, spanKey: span.spanKey, profileId })}
            className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
          >
            <ExternalLink className="h-4 w-4" />
            Open span
          </button>
          {span.segmentIds[0] != null ? (
            <button
              type="button"
              onClick={() => onNavigate({ page: "segment", id: span.segmentIds[0] })}
              className="rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              Open raw
            </button>
          ) : null}
        </div>
      </div>
    </div>
  );
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