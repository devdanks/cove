import { useQuery } from "@tanstack/react-query";
import { segmentLibrary } from "../../api/client";
import type { RawSegmentItem } from "./types";
import type { RawSegmentFilterValue } from "./rawSegmentFilter";

interface UseRawSegmentsQueryOptions {
  pageNumber: number;
  perPage: number;
  q: string;
  sceneTitle: string;
  sort: string;
  direction: "asc" | "desc";
  includeSceneIds: number[];
  excludeSceneIds: number[];
  rawSegmentIds: number[];
  rawFilter: RawSegmentFilterValue;
  enabled: boolean;
}

export function useRawSegmentsQuery({
  pageNumber,
  perPage,
  q,
  sceneTitle,
  sort,
  direction,
  includeSceneIds,
  excludeSceneIds,
  rawSegmentIds,
  rawFilter,
  enabled,
}: UseRawSegmentsQueryOptions) {
  return useQuery({
    queryKey: [
      "segments-page",
      "raw",
      pageNumber,
      perPage,
      q,
      sceneTitle,
      sort,
      direction,
      includeSceneIds.join(","),
      excludeSceneIds.join(","),
      rawSegmentIds.join(","),
      rawFilter,
    ],
    queryFn: async (): Promise<{ items: RawSegmentItem[]; totalCount: number }> => {
      const response = await segmentLibrary.list({
        q: q || undefined,
        ids: rawSegmentIds.length > 0 ? rawSegmentIds.join(",") : undefined,
        sceneIds: includeSceneIds.length > 0 ? includeSceneIds.join(",") : undefined,
        excludeSceneIds: excludeSceneIds.length > 0 ? excludeSceneIds.join(",") : undefined,
        sceneTitle: sceneTitle || undefined,
        tagIds: rawFilter.tagIds.length > 0 ? rawFilter.tagIds.join(",") : undefined,
        kind: rawFilter.kind,
        sourceKey: rawFilter.sourceKey,
        sourceCategory: rawFilter.sourceCategory,
        refIds: rawFilter.faceIds.length > 0 ? rawFilter.faceIds.join(",") : undefined,
        performerIds: rawFilter.performerIds.length > 0 ? rawFilter.performerIds.join(",") : undefined,
        minConfidence: rawFilter.minConfidence,
        minDurationSec: rawFilter.minDurationSec,
        confidence: rawFilter.confidenceCriterion?.value,
        confidence2: rawFilter.confidenceCriterion?.value2,
        confidenceModifier: rawFilter.confidenceCriterion?.modifier,
        durationSec: rawFilter.durationCriterion?.value,
        durationSec2: rawFilter.durationCriterion?.value2,
        durationModifier: rawFilter.durationCriterion?.modifier,
        sort,
        direction,
        page: pageNumber,
        perPage,
      });

      return {
        items: response.items.map((item) => ({
          ...item,
          key: `segment:${item.id}`,
          sceneId: item.hostId,
          sceneTitle: item.hostTitle?.trim() || `Scene #${item.hostId}`,
        })),
        totalCount: response.totalCount,
      };
    },
    enabled,
    staleTime: 15_000,
  });
}