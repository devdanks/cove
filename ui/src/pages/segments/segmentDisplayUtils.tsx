import { type ReactNode } from "react";
import { scenes } from "../../api/client";
import type { ResolvedSpan } from "../../api/types";
import { formatDate } from "../../components/shared";
import { formatOperatorLabel } from "./derivedQueryCriterion";
import type { DerivedSpanItem, RawSegmentItem } from "./types";

export function Pill({ children }: { children: ReactNode }) {
  return (
    <span className="inline-flex items-center rounded-full bg-surface px-2 py-1 text-secondary">
      {children}
    </span>
  );
}

export function SegmentScenePreview({
  hostId,
  updatedAt,
  startSec,
  title,
  imgClassName,
}: {
  hostId: number;
  updatedAt?: string;
  startSec?: number;
  title: string;
  imgClassName: string;
}) {
  return (
    <img
      src={scenes.screenshotUrl(hostId, updatedAt, startSec)}
      alt={title}
      className={imgClassName}
      loading="lazy"
    />
  );
}

export function buildSpanTitle(span: ResolvedSpan, sceneTitle?: string) {
  return span.tagName || span.kind || span.sourceKey || sceneTitle || `Span ${span.spanKey}`;
}

export function buildRawSegmentTitle(segment: RawSegmentItem) {
  return segment.title?.trim() || segment.tagName || segment.kind || `${segment.sourceKey} #${segment.id}`;
}

export function formatSpanItemKindLabel(item: DerivedSpanItem) {
  return item.kind === "derivedQuery"
    ? `Derived ${formatOperatorLabel(item.derivedQueryDescriptor?.operator ?? "intersection")}`
    : "Profile";
}

export function formatSegmentRange(startSec: number, endSec?: number) {
  const start = formatSegmentTime(startSec);
  return endSec == null ? start : `${start} - ${formatSegmentTime(endSec)}`;
}

export function formatSegmentDuration(startSec: number, endSec?: number) {
  if (endSec == null) {
    return "Instant";
  }

  const duration = Math.max(0, endSec - startSec);
  return duration > 0 ? `${formatSegmentTime(duration)} long` : "Instant";
}

export function formatSegmentTime(value: number) {
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

export { formatDate };