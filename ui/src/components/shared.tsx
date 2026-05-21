import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { createPortal } from "react-dom";
import type { Tag, TagProvenance } from "../api/types";

export { RatingBadge } from "./Rating";
export { CustomFieldsDisplay, CustomFieldsEditor } from "./CustomFields";
import { getResolutionBucketLabel } from "../utils/resolutionBuckets";

export function TagBadge({ name, tag, color, groupColor, onClick, provenance }: { name: string; tag?: Pick<Tag, "color" | "tagGroupColor">; color?: string | null; groupColor?: string | null; onClick?: () => void; provenance?: TagProvenance[] }) {
  const wrapperRef = useRef<HTMLSpanElement>(null);
  const [showProvenance, setShowProvenance] = useState(false);
  const [popupPosition, setPopupPosition] = useState<{ left: number; top: number }>({ left: 0, top: 0 });
  const visibleProvenance = provenance?.filter((entry) => !isUserTagSource(entry.sourceKey)) ?? [];
  const badgeLabel = getTagProvenanceBadgeLabel(visibleProvenance);
  const interactive = Boolean(onClick);
  const resolvedGroupColor = normalizeTagColor(groupColor ?? tag?.tagGroupColor);
  const resolvedColor = normalizeTagColor(color ?? tag?.color ?? groupColor ?? tag?.tagGroupColor);
  const colorStyle = resolvedColor ? getTagColorStyle(resolvedColor) : undefined;

  useLayoutEffect(() => {
    if (!showProvenance || !provenance?.length) {
      return;
    }

    const updatePosition = () => {
      const rect = wrapperRef.current?.getBoundingClientRect();
      if (!rect) {
        return;
      }

      const width = 288;
      const margin = 8;
      const left = Math.min(Math.max(margin, rect.right - width), window.innerWidth - width - margin);
      const preferredTop = rect.bottom + margin;
      const top = preferredTop < window.innerHeight - margin ? preferredTop : Math.max(margin, rect.top - margin);
      setPopupPosition({ left, top });
    };

    updatePosition();
    window.addEventListener("resize", updatePosition);
    window.addEventListener("scroll", updatePosition, true);
    return () => {
      window.removeEventListener("resize", updatePosition);
      window.removeEventListener("scroll", updatePosition, true);
    };
  }, [provenance?.length, showProvenance]);

  const badgeContent = (
    <>
      {resolvedGroupColor ? (
        <span className="inline-flex h-3.5 w-3.5 items-center justify-center rounded-sm border border-current/30" title="Tag group">
          <span className="h-1.5 w-1.5 rounded-full" style={{ backgroundColor: resolvedGroupColor }} />
        </span>
      ) : null}
      <span>{name}</span>
      {badgeLabel ? (
        <span className="rounded-full border border-emerald-400/40 bg-emerald-500/12 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-emerald-200">
          {badgeLabel}
        </span>
      ) : null}
    </>
  );

  return (
    <span
      ref={wrapperRef}
      className="relative inline-flex"
      onMouseEnter={() => setShowProvenance(true)}
      onMouseLeave={() => setShowProvenance(false)}
      onFocus={() => setShowProvenance(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setShowProvenance(false);
        }
      }}
    >
      {interactive ? (
        <button
          type="button"
          onClick={onClick}
          style={colorStyle}
          className="inline-flex items-center gap-1.5 rounded border border-border bg-card px-2 py-0.5 text-xs font-medium text-secondary transition hover:bg-card-hover hover:text-foreground"
        >
          {badgeContent}
        </button>
      ) : (
        <span
          style={colorStyle}
          className="inline-flex items-center gap-1.5 rounded border border-border bg-card px-2 py-0.5 text-xs font-medium text-secondary"
        >
          {badgeContent}
        </span>
      )}
      {provenance?.length ? <span className="sr-only"><TagProvenancePopupContent provenance={provenance} title="Tag Sources" /></span> : null}
      {showProvenance && provenance?.length && typeof document !== "undefined" ? createPortal(
        <span
          className="pointer-events-none fixed z-50 max-h-[min(70vh,24rem)] w-72 overflow-y-auto rounded-xl border border-border bg-surface/95 p-3 text-left shadow-2xl backdrop-blur"
          style={{ left: popupPosition.left, top: popupPosition.top }}
        >
          <TagProvenancePopupContent provenance={provenance} title="Tag Sources" />
        </span>,
        document.body,
      ) : null}
    </span>
  );
}

export function ProvenanceBadge({ name, provenance, onClick, sourceLabel = "Source", children }: { name: string; provenance?: TagProvenance[]; onClick?: () => void; sourceLabel?: string; children?: ReactNode }) {
  const wrapperRef = useRef<HTMLSpanElement>(null);
  const [showProvenance, setShowProvenance] = useState(false);
  const [popupPosition, setPopupPosition] = useState<{ left: number; top: number }>({ left: 0, top: 0 });
  const visibleProvenance = provenance?.filter((entry) => !isUserTagSource(entry.sourceKey)) ?? [];
  const badgeLabel = getTagProvenanceBadgeLabel(visibleProvenance);
  const interactive = Boolean(onClick);

  useLayoutEffect(() => {
    if (!showProvenance || !provenance?.length) {
      return;
    }

    const updatePosition = () => {
      const rect = wrapperRef.current?.getBoundingClientRect();
      if (!rect) {
        return;
      }

      const width = 288;
      const margin = 8;
      const left = Math.min(Math.max(margin, rect.right - width), window.innerWidth - width - margin);
      const preferredTop = rect.bottom + margin;
      const top = preferredTop < window.innerHeight - margin ? preferredTop : Math.max(margin, rect.top - margin);
      setPopupPosition({ left, top });
    };

    updatePosition();
    window.addEventListener("resize", updatePosition);
    window.addEventListener("scroll", updatePosition, true);
    return () => {
      window.removeEventListener("resize", updatePosition);
      window.removeEventListener("scroll", updatePosition, true);
    };
  }, [provenance?.length, showProvenance]);

  const badgeContent = (
    <>
      <span>{children ?? name}</span>
      {badgeLabel ? (
        <span className="rounded-full border border-emerald-400/40 bg-emerald-500/12 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-emerald-200">
          {badgeLabel}
        </span>
      ) : null}
    </>
  );

  return (
    <span
      ref={wrapperRef}
      className="relative inline-flex"
      onMouseEnter={() => setShowProvenance(true)}
      onMouseLeave={() => setShowProvenance(false)}
      onFocus={() => setShowProvenance(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setShowProvenance(false);
        }
      }}
    >
      {interactive ? (
        <button
          type="button"
          onClick={onClick}
          className="inline-flex items-center gap-1.5 rounded border border-border bg-card px-2 py-0.5 text-xs font-medium text-secondary transition hover:bg-card-hover hover:text-foreground"
        >
          {badgeContent}
        </button>
      ) : (
        <span className="inline-flex items-center gap-1.5 rounded border border-border bg-card px-2 py-0.5 text-xs font-medium text-secondary">
          {badgeContent}
        </span>
      )}
      {provenance?.length ? <span className="sr-only"><TagProvenancePopupContent provenance={provenance} title={`${sourceLabel} Sources`} /></span> : null}
      {showProvenance && provenance?.length && typeof document !== "undefined" ? createPortal(
        <span
          className="pointer-events-none fixed z-50 max-h-[min(70vh,24rem)] w-72 overflow-y-auto rounded-xl border border-border bg-surface/95 p-3 text-left shadow-2xl backdrop-blur"
          style={{ left: popupPosition.left, top: popupPosition.top }}
        >
          <TagProvenancePopupContent provenance={provenance} title={`${sourceLabel} Sources`} />
        </span>,
        document.body,
      ) : null}
    </span>
  );
}

function TagProvenancePopupContent({ provenance, title }: { provenance: TagProvenance[]; title: string }) {
  return (
    <>
      <span className="mb-2 block text-[11px] font-semibold uppercase tracking-[0.16em] text-muted">{title}</span>
      <span className="flex flex-col gap-2">
        {provenance
          .slice()
          .sort((left, right) => Date.parse(right.appliedAt) - Date.parse(left.appliedAt))
          .map((entry, index) => (
            <span key={`${entry.sourceKey}-${entry.sourceRunId ?? ""}-${entry.modelKey ?? ""}-${index}`} className="block rounded-lg border border-border/70 bg-card/70 px-2.5 py-2">
              <span className="flex items-center justify-between gap-2 text-xs text-foreground">
                <span className="font-medium">{formatTagProvenanceSource(entry.sourceKey)}</span>
                {entry.confidence != null ? <span className="text-emerald-300">{formatTagConfidence(entry.confidence)}</span> : null}
              </span>
              {entry.modelKey ? <span className="mt-1 block break-all text-[11px] text-secondary">Model {entry.modelKey}</span> : null}
              {entry.sourceRunId ? <span className="mt-1 block break-all text-[11px] text-muted">Run {entry.sourceRunId}</span> : null}
              {entry.contextType && entry.contextId ? <span className="mt-1 block text-[11px] text-muted">Context {formatTagProvenanceSource(entry.contextType)} #{entry.contextId}</span> : null}
              {entry.totalDurationSec != null ? <span className="mt-1 block text-[11px] text-muted">Duration {formatTagDurationProvenance(entry)}</span> : null}
              <span className="mt-1 block text-[11px] text-muted">Applied {formatTagProvenanceDate(entry.appliedAt)}</span>
            </span>
          ))}
      </span>
    </>
  );
}

function normalizeTagColor(value?: string | null) {
  const trimmed = value?.trim();
  if (!trimmed || !/^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$/.test(trimmed)) {
    return null;
  }
  return trimmed;
}

function getTagColorStyle(color: string): CSSProperties {
  return {
    borderColor: hexToRgba(color, 0.58),
    backgroundColor: hexToRgba(color, 0.14),
    color: hexToRgba(color, 0.96),
  };
}

function hexToRgba(hex: string, alpha: number) {
  const value = hex.slice(1, 7);
  const r = Number.parseInt(value.slice(0, 2), 16);
  const g = Number.parseInt(value.slice(2, 4), 16);
  const b = Number.parseInt(value.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function isUserTagSource(sourceKey: string) {
  return sourceKey.trim().toLowerCase() === "user";
}

function getTagProvenanceBadgeLabel(provenance: TagProvenance[]) {
  if (provenance.length === 0) {
    return null;
  }

  const uniqueSources = Array.from(new Set(provenance.map((entry) => entry.sourceKey.trim().toLowerCase())));
  if (uniqueSources.length > 1) {
    return `${uniqueSources.length} src`;
  }

  return formatTagProvenanceBadgeSource(uniqueSources[0]);
}

function formatTagProvenanceBadgeSource(sourceKey: string) {
  switch (sourceKey) {
    case "scraper":
    case "scraper:local":
      return "Scrape";
    case "metadata":
    case "metadata:default":
      return "Meta";
    case "system":
    case "auto-tag":
      return "Auto";
    case "bulk-edit":
      return "Bulk";
    case "stash-import":
      return "Stash";
    default:
      if (sourceKey.startsWith("scraper:")) return "Scrape";
      if (sourceKey.startsWith("metadata:")) return "Meta";
      return sourceKey.startsWith("ext:") ? "AI" : formatTagProvenanceSource(sourceKey);
  }
}

function formatTagProvenanceSource(sourceKey: string) {
  const normalized = sourceKey.trim();
  if (!normalized) {
    return "Unknown";
  }

  if (normalized.toLowerCase() === "user") {
    return "Manual";
  }

  if (normalized.startsWith("ext:")) {
    return normalized.slice(4).split(".").map(capitalizeWord).join(".");
  }

  return normalized.split(/[:._-]+/).map(capitalizeWord).join(" ");
}

function capitalizeWord(value: string) {
  if (!value) {
    return value;
  }

  return value[0].toUpperCase() + value.slice(1);
}

function formatTagConfidence(confidence: number) {
  return `${Math.round(confidence * 100)}%`;
}

function formatTagProvenanceDate(value?: string) {
  if (!value) {
    return "Unknown";
  }

  try {
    return new Date(value).toLocaleString();
  } catch {
    return value;
  }
}

function formatTagDurationProvenance(entry: TagProvenance) {
  const duration = formatDuration(entry.totalDurationSec ?? 0);
  if (entry.hostDurationSec && entry.hostDurationSec > 0) {
    const percent = Math.round(((entry.totalDurationSec ?? 0) / entry.hostDurationSec) * 100);
    return `${duration} (${percent}%)`;
  }

  return duration;
}

export function formatDuration(seconds: number): string {
  if (!seconds || seconds <= 0) return "0:00";
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  if (h > 0) return `${h}:${m.toString().padStart(2, "0")}:${s.toString().padStart(2, "0")}`;
  return `${m}:${s.toString().padStart(2, "0")}`;
}

export function formatFileSize(bytes: number): string {
  if (bytes === 0) return "0 B";
  const k = 1024;
  const sizes = ["B", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + " " + sizes[i];
}

export function formatDate(dateStr?: string): string {
  if (!dateStr) return "";
  try {
    return new Date(dateStr).toLocaleDateString();
  } catch {
    return dateStr;
  }
}

export function getResolutionLabel(width: number, height: number): string | null {
  return getResolutionBucketLabel(width, height);
}

