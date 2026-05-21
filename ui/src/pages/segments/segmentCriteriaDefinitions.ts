import type { CriterionDefinition } from "../../components/FilterDialog";
import type { CriterionModifier, IntCriterion, MultiIdCriterion } from "../../api/types";
import type { SegmentsPageContentView } from "./types";

const SEGMENT_NUMBER_MODIFIERS: CriterionModifier[] = ["EQUALS", "NOT_EQUALS", "GREATER_THAN", "LESS_THAN", "BETWEEN", "NOT_BETWEEN"];

export interface SegmentCriteriaOptions {
  kindOptions?: { value: string; label: string }[];
  sourceOptions?: { value: string; label: string }[];
}

export const SEGMENT_CRITERIA: CriterionDefinition[] = [
  { id: "sceneTitle", label: "Scene Title", type: "string", filterKey: "sceneTitleCriterion" },
  { id: "scenes", label: "Scenes", type: "multiId", entityType: "scenes", filterKey: "scenesCriterion" },
];

export function createSegmentCriteria(options: SegmentCriteriaOptions = {}): CriterionDefinition[] {
  return [
  ...SEGMENT_CRITERIA,
  { id: "tags", label: "Tags", type: "multiId", entityType: "tags", filterKey: "rawTagsCriterion" },
  { id: "performers", label: "Performers", type: "multiId", entityType: "performers", filterKey: "rawPerformersCriterion" },
  { id: "faces", label: "Faces", type: "multiId", entityType: "faces", filterKey: "rawFacesCriterion" },
  { id: "kind", label: "Segment Type", type: "enum", filterKey: "rawKindCriterion", modifiers: ["EQUALS"], options: options.kindOptions ?? [] },
  { id: "source", label: "Source", type: "enum", filterKey: "rawSourceCriterion", modifiers: ["EQUALS"], options: options.sourceOptions ?? [] },
  { id: "confidence", label: "Confidence", type: "number", filterKey: "rawConfidenceCriterion", modifiers: SEGMENT_NUMBER_MODIFIERS },
  { id: "duration", label: "Duration", type: "duration", filterKey: "rawDurationCriterion", modifiers: SEGMENT_NUMBER_MODIFIERS },
  ];
}

export const RAW_SEGMENT_CRITERIA: CriterionDefinition[] = createSegmentCriteria();

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

export function readMultiIdCriterionIds(value: unknown) {
  if (!value || typeof value !== "object") {
    return [] as number[];
  }

  const criterion = value as Partial<MultiIdCriterion>;
  return Array.isArray(criterion.value)
    ? criterion.value.filter((item): item is number => typeof item === "number" && Number.isFinite(item) && item > 0)
    : [];
}

export function readMinimumNumberCriterion(value: unknown) {
  if (!value || typeof value !== "object") {
    return undefined;
  }

  const criterion = value as Partial<IntCriterion>;
  if (typeof criterion.value !== "number" || !Number.isFinite(criterion.value)) {
    return undefined;
  }

  if (criterion.modifier === "BETWEEN" && typeof criterion.value2 === "number" && Number.isFinite(criterion.value2)) {
    return Math.min(criterion.value, criterion.value2);
  }

  return criterion.value;
}

export interface SegmentNumberCriterionValue {
  modifier?: CriterionModifier;
  value?: number;
  value2?: number;
}

export function readNumberCriterion(value: unknown): SegmentNumberCriterionValue | undefined {
  if (!value || typeof value !== "object") {
    return undefined;
  }

  const criterion = value as Partial<IntCriterion>;
  if (typeof criterion.value !== "number" || !Number.isFinite(criterion.value)) {
    return undefined;
  }

  return {
    modifier: criterion.modifier,
    value: criterion.value,
    value2: typeof criterion.value2 === "number" && Number.isFinite(criterion.value2) ? criterion.value2 : undefined,
  };
}