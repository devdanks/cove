import { useQuery } from "@tanstack/react-query";
import { segmentLibrary } from "../../api/client";
import type { RawSegmentItem } from "./types";

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
    ],
    queryFn: async (): Promise<{ items: RawSegmentItem[]; totalCount: number }> => {
      const response = await segmentLibrary.list({
        q: q || undefined,
        ids: rawSegmentIds.length > 0 ? rawSegmentIds.join(",") : undefined,
        sceneIds: includeSceneIds.length > 0 ? includeSceneIds.join(",") : undefined,
        excludeSceneIds: excludeSceneIds.length > 0 ? excludeSceneIds.join(",") : undefined,
        sceneTitle: sceneTitle || undefined,
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