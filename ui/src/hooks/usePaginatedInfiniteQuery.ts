import { useInfiniteQuery, type QueryKey } from "@tanstack/react-query";
import type { PaginatedResponse } from "../api/types";

interface UsePaginatedInfiniteQueryOptions<TItem> {
  queryKey: QueryKey;
  queryFn: (page: number, perPage: number) => Promise<PaginatedResponse<TItem>>;
  enabled?: boolean;
  chunkSize?: number;
  maxPages?: number;
}

export function usePaginatedInfiniteQuery<TItem>({
  queryKey,
  queryFn,
  enabled = true,
  chunkSize = 24,
  maxPages = 5,
}: UsePaginatedInfiniteQueryOptions<TItem>) {
  const query = useInfiniteQuery({
    queryKey,
    enabled,
    initialPageParam: 1,
    queryFn: ({ pageParam }) => queryFn(pageParam, chunkSize),
    getNextPageParam: (lastPage) => {
      const loadedThrough = lastPage.page * lastPage.perPage;
      if (loadedThrough >= lastPage.totalCount || lastPage.items.length === 0) {
        return undefined;
      }

      return lastPage.page + 1;
    },
    getPreviousPageParam: (firstPage) => firstPage.page > 1 ? firstPage.page - 1 : undefined,
    maxPages,
  });

  const pages = query.data?.pages ?? [];
  const totalCount = pages[0]?.totalCount ?? 0;
  const lastPage = pages[pages.length - 1];
  const loadedThroughCount = lastPage
    ? Math.min(totalCount, (lastPage.page - 1) * lastPage.perPage + lastPage.items.length)
    : 0;

  return {
    ...query,
    items: pages.flatMap((page) => page.items),
    firstLoadedIndex: pages[0] ? (pages[0].page - 1) * pages[0].perPage : 0,
    loadedThroughCount,
    totalCount,
  };
}