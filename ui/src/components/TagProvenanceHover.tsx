import { useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import type { TagProvenance } from "../api/types";

export function TagProvenanceHover({ provenance, sourceLabel = "Tag", children, className }: { provenance?: TagProvenance[]; sourceLabel?: string; children: ReactNode; className?: string }) {
  const wrapperRef = useRef<HTMLSpanElement>(null);
  const [showProvenance, setShowProvenance] = useState(false);
  const [popupPosition, setPopupPosition] = useState<{ left: number; top: number }>({ left: 0, top: 0 });

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

  if (!provenance?.length) {
    return <>{children}</>;
  }

  return (
    <span
      ref={wrapperRef}
      className={["relative inline-flex cursor-help", className ?? ""].filter(Boolean).join(" ")}
      onMouseEnter={() => setShowProvenance(true)}
      onMouseLeave={() => setShowProvenance(false)}
      onFocus={() => setShowProvenance(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setShowProvenance(false);
        }
      }}
    >
      {children}
      <span className="sr-only"><TagProvenancePopupContent provenance={provenance} title={`${sourceLabel} Sources`} /></span>
      {showProvenance && typeof document !== "undefined" ? createPortal(
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

  if (normalized.startsWith("scraper:")) {
    return `Scraper: ${formatProviderIdentifier(normalized.slice("scraper:".length))}`;
  }

  if (normalized.startsWith("metadata:")) {
    return `Metadata: ${formatProviderIdentifier(normalized.slice("metadata:".length))}`;
  }

  return normalized.split(/[:._-]+/).map(capitalizeWord).join(" ");
}

function formatProviderIdentifier(value: string) {
  const trimmed = value.trim();
  if (!trimmed) {
    return "Default";
  }

  try {
    const url = new URL(trimmed);
    return url.host || trimmed;
  } catch {
    return trimmed;
  }
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

function formatDuration(seconds: number): string {
  if (!seconds || seconds <= 0) return "0:00";
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = Math.floor(seconds % 60);
  if (h > 0) return `${h}:${m.toString().padStart(2, "0")}:${s.toString().padStart(2, "0")}`;
  return `${m}:${s.toString().padStart(2, "0")}`;
}