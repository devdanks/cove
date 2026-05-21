import { useQuery } from "@tanstack/react-query";
import { segmentSpans } from "../../api/client";
import type { SegmentDerivedQueryDescriptor } from "../../api/types";
import type { RawSegmentFilterValue } from "./rawSegmentFilter";
import type { AppliedDerivedQuery, DerivedSpanItem } from "./types";

interface UseDerivedSpansQueryOptions {
  activeProfileId?: number;
  pageNumber: number;
  perPage: number;
  q: string;
  sceneTitle: string;
  sort: string;
  direction: "asc" | "desc";
  includeSceneIds: number[];
  excludeSceneIds: number[];
  appliedQuery: AppliedDerivedQuery | null;
  derivedQueryDescriptor?: SegmentDerivedQueryDescriptor;
  rawFilter: RawSegmentFilterValue;
  enabled: boolean;
}

export function useDerivedSpansQuery({
  activeProfileId,
  pageNumber,
  perPage,
  q,
  sceneTitle,
  sort,
  direction,
  includeSceneIds,
  excludeSceneIds,
  appliedQuery,
  derivedQueryDescriptor,
  rawFilter,
  enabled,
}: UseDerivedSpansQueryOptions) {
  return useQuery({
    queryKey: [
      "segments-page",
      "search",
      activeProfileId,
      pageNumber,
      perPage,
      q,
      sceneTitle,
      sort,
      direction,
      includeSceneIds.join(","),
      excludeSceneIds.join(","),
      appliedQuery ?? null,
      rawFilter,
    ],
    queryFn: async (): Promise<{ items: DerivedSpanItem[]; totalCount: number }> => {
      if (activeProfileId == null) {
        return { items: [], totalCount: 0 };
      }

      const response = await segmentSpans.search({
        profile: activeProfileId,
        derivedQuery: appliedQuery != null ? {
          operator: appliedQuery.operator,
          operands: appliedQuery.operands,
          mergeGapSec: appliedQuery.mergeGapSec,
          minDurationSec: appliedQuery.minDurationSec,
        } : undefined,
        page: pageNumber,
        perPage,
        sort,
        direction,
        q: q || undefined,
        sceneTitle: sceneTitle || undefined,
        sceneIds: includeSceneIds.length > 0 ? includeSceneIds : undefined,
        excludeSceneIds: excludeSceneIds.length > 0 ? excludeSceneIds : undefined,
        tagIds: rawFilter.tagIds.length > 0 ? rawFilter.tagIds : undefined,
        kind: rawFilter.kind,
        sourceKey: rawFilter.sourceKey,
        refIds: rawFilter.faceIds.length > 0 ? rawFilter.faceIds : undefined,
        performerIds: rawFilter.performerIds.length > 0 ? rawFilter.performerIds : undefined,
        confidence: rawFilter.confidenceCriterion?.value,
        confidence2: rawFilter.confidenceCriterion?.value2,
        confidenceModifier: rawFilter.confidenceCriterion?.modifier,
        durationSec: rawFilter.durationCriterion?.value,
        durationSec2: rawFilter.durationCriterion?.value2,
        durationModifier: rawFilter.durationCriterion?.modifier,
      });

      return {
        items: response.items.map((item) => ({
          id: `${item.sceneId}:${item.span.spanKey}`,
          key: `${item.sceneId}:${item.span.spanKey}`,
          kind: derivedQueryDescriptor ? "derivedQuery" : "profile",
          sceneId: item.sceneId,
          sceneTitle: item.sceneTitle ?? `Scene #${item.sceneId}`,
          sceneUpdatedAt: item.sceneUpdatedAt,
          span: item.span,
          profileId: item.profileId,
          derivedQuery: appliedQuery != null ? {
            operator: appliedQuery.operator,
            operands: appliedQuery.operands,
            mergeGapSec: appliedQuery.mergeGapSec,
            minDurationSec: appliedQuery.minDurationSec,
          } : undefined,
          derivedQueryDescriptor,
        })),
        totalCount: response.totalCount,
      };
    },
    enabled,
    staleTime: 15_000,
  });
}