import { useQuery } from "@tanstack/react-query";
import { segmentSpans } from "../../api/client";
import type { SegmentDerivedQueryDescriptor } from "../../api/types";
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