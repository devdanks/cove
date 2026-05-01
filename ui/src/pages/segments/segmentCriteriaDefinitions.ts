import type { CriterionDefinition } from "../../components/FilterDialog";
import type { SegmentsPageContentView } from "./types";

export const SEGMENT_CRITERIA: CriterionDefinition[] = [
  { id: "sceneTitle", label: "Scene Title", type: "string", filterKey: "sceneTitleCriterion" },
  { id: "scenes", label: "Scenes", type: "multiId", entityType: "scenes", filterKey: "scenesCriterion" },
];

export interface SceneSelectionCriterion {
  includeIds: number[];
  excludeIds: number[];
}

interface SceneSelectionCriterionValue {
  value?: unknown;
  excludes?: unknown;
}

export function readStringCriterion(value: unknown) {
  if (!value || typeof value !== "object") {
    return "";
  }

  const candidate = (value as { value?: unknown }).value;
  return typeof candidate === "string" ? candidate.trim() : "";
}

export function readSceneSelectionCriterion(value: unknown): SceneSelectionCriterion {
  if (!value || typeof value !== "object") {
    return { includeIds: [], excludeIds: [] };
  }

  const criterion = value as SceneSelectionCriterionValue;
  const included = Array.isArray(criterion.value)
    ? criterion.value.filter((item): item is number => typeof item === "number" && Number.isFinite(item))
    : [];
  const excluded = Array.isArray(criterion.excludes)
    ? criterion.excludes.filter((item): item is number => typeof item === "number" && Number.isFinite(item))
    : [];

  return {
    includeIds: included,
    excludeIds: excluded,
  };
}

export function readSegmentsPageContentView(): SegmentsPageContentView {
  const params = new URLSearchParams(window.location.search);
  return params.get("segmentsView") === "raw" ? "raw" : "spans";
}

export function readRawSegmentIdsFromUrl() {
  const params = new URLSearchParams(window.location.search);
  const raw = params.get("rawIds");
  if (!raw) {
    return [] as number[];
  }

  return raw
    .split(",")
    .map((value) => Number(value))
    .filter((value) => Number.isInteger(value) && value > 0);
}