import type {
  ResolvedSpan,
  SegmentDerivedQueryDescriptor,
  SegmentRecord,
  SegmentSpanDerivedQuery,
  SegmentSpanOperand,
  SegmentSpanOperator,
} from "../../api/types";

export interface DerivedSpanItem {
  id: string;
  key: string;
  kind: "profile" | "derivedQuery";
  sceneId: number;
  sceneTitle: string;
  sceneUpdatedAt?: string;
  span: ResolvedSpan;
  profileId?: number;
  derivedQuery?: SegmentSpanDerivedQuery;
  derivedQueryDescriptor?: SegmentDerivedQueryDescriptor;
}

export interface RawSegmentItem extends SegmentRecord {
  key: string;
  sceneId: number;
  sceneTitle: string;
}

export type SegmentsPageContentView = "spans" | "raw";

export interface AppliedDerivedQuery {
  operator: SegmentSpanOperator;
  operands: SegmentSpanOperand[];
  mergeGapSec?: number;
  minDurationSec?: number;
}

export interface DerivedSpanOperandFilterValue {
  sourceKey?: string;
  kind?: string;
  tagIds: number[];
  performerIds: number[];
  faceIds: number[];
  minConfidence?: number;
}

export interface DerivedSpanQueryFilterValue {
  operator: SegmentSpanOperator;
  operands: DerivedSpanOperandFilterValue[];
  mergeGapSec?: number;
  minDurationSec?: number;
}