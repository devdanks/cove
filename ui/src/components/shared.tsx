import type { CSSProperties, ReactNode } from "react";
import type { FieldProvenance, Tag, TagProvenance } from "../api/types";
import { getFieldProvenanceEntries } from "./FieldProvenanceHover";
import { TagProvenanceHover } from "./TagProvenanceHover";

export { RatingBadge } from "./Rating";
export { CustomFieldsDisplay, CustomFieldsEditor } from "./CustomFields";
export { FieldProvenanceHover } from "./FieldProvenanceHover";
export { TagProvenanceHover } from "./TagProvenanceHover";
import { getResolutionBucketLabel } from "../utils/resolutionBuckets";

export function TagBadge({ name, tag, color, groupColor, onClick, provenance }: { name: string; tag?: Pick<Tag, "color" | "tagGroupColor">; color?: string | null; groupColor?: string | null; onClick?: () => void; provenance?: TagProvenance[] }) {
  const interactive = Boolean(onClick);
  const resolvedGroupColor = normalizeTagColor(groupColor ?? tag?.tagGroupColor);
  const resolvedColor = normalizeTagColor(color ?? tag?.color ?? groupColor ?? tag?.tagGroupColor);
  const colorStyle = resolvedColor ? getTagColorStyle(resolvedColor) : undefined;

  const badgeContent = (
    <>
      {resolvedGroupColor ? (
        <span className="inline-flex h-3.5 w-3.5 items-center justify-center rounded-sm border border-current/30" title="Tag group">
          <span className="h-1.5 w-1.5 rounded-full" style={{ backgroundColor: resolvedGroupColor }} />
        </span>
      ) : null}
      <span>{name}</span>
    </>
  );

  const badge = interactive ? (
    <button
      type="button"
      onClick={onClick}
      style={colorStyle}
      className="inline-flex min-h-9 items-center gap-1.5 rounded border border-border bg-card px-2.5 py-1 text-xs font-medium text-secondary transition hover:bg-card-hover hover:text-foreground sm:min-h-0 sm:px-2 sm:py-0.5"
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
  );

  return <TagProvenanceHover provenance={provenance}>{badge}</TagProvenanceHover>;
}

function fieldProvenanceValueContainsTag(value: unknown, tagName: string) {
  if (typeof value === "string") {
    return value === tagName || value.includes(tagName);
  }

  if (Array.isArray(value)) {
    return value.some((item) => {
      if (typeof item === "string") {
        return item === tagName || item.includes(tagName);
      }
      if (item && typeof item === "object") {
        return ["name", "tagName", "value", "label"].some((key) => {
          const candidate = (item as Record<string, unknown>)[key];
          return typeof candidate === "string" && (candidate === tagName || candidate.includes(tagName));
        });
      }
      return false;
    });
  }

  if (value && typeof value === "object") {
    return ["name", "tagName", "value", "label"].some((key) => {
      const candidate = (value as Record<string, unknown>)[key];
      return typeof candidate === "string" && (candidate === tagName || candidate.includes(tagName));
    });
  }

  return false;
}

export function resolveTagProvenance(tag: Pick<Tag, "name" | "provenance">, fieldProvenance?: FieldProvenance[], fieldKey: string | string[] = "tags") {
  if (tag.provenance?.length) {
    return tag.provenance;
  }

  const fallback = getFieldProvenanceEntries(fieldProvenance, fieldKey)
    .filter((entry) => fieldProvenanceValueContainsTag(entry.value, tag.name))
    .map((entry) => ({
      sourceKey: entry.sourceKey,
      sourceRunId: entry.sourceRunId,
      modelKey: entry.modelKey,
      confidence: entry.confidence,
      appliedAt: entry.createdAt,
    } satisfies TagProvenance));

  return fallback.length > 0 ? fallback : tag.provenance;
}

export function buildTagProvenanceById(tags: Array<Pick<Tag, "id" | "name" | "provenance">>, fieldProvenance?: FieldProvenance[], fieldKey: string | string[] = "tags") {
  return Object.fromEntries(tags.map((tag) => [tag.id, resolveTagProvenance(tag, fieldProvenance, fieldKey)])) as Record<number, TagProvenance[] | undefined>;
}

export function ProvenanceBadge({ name, provenance, onClick, sourceLabel = "Source", children }: { name: string; provenance?: TagProvenance[]; onClick?: () => void; sourceLabel?: string; children?: ReactNode }) {
  const interactive = Boolean(onClick);

  const badgeContent = (
    <>
      <span>{children ?? name}</span>
    </>
  );

  const badge = interactive ? (
    <button
      type="button"
      onClick={onClick}
      className="inline-flex min-h-9 items-center gap-1.5 rounded border border-border bg-card px-2.5 py-1 text-xs font-medium text-secondary transition hover:bg-card-hover hover:text-foreground sm:min-h-0 sm:px-2 sm:py-0.5"
    >
      {badgeContent}
    </button>
  ) : (
    <span className="inline-flex items-center gap-1.5 rounded border border-border bg-card px-2 py-0.5 text-xs font-medium text-secondary">
      {badgeContent}
    </span>
  );

  return <TagProvenanceHover provenance={provenance} sourceLabel={sourceLabel}>{badge}</TagProvenanceHover>;
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

