import { useCallback, useEffect, useRef, useState } from "react";

interface UseMultiSelectOptions {
  preserveOnAppend?: boolean;
  resetKey?: string;
}

export function useMultiSelect<T extends { id: string | number }>(items: T[], options: UseMultiSelectOptions = {}) {
  const [selectedIds, setSelectedIds] = useState<Set<T["id"]>>(new Set());

  // Infinite lists can unload pages above or below the viewport, so those selections are preserved until the query changes.
  const itemIdsKey = items.map((item) => String(item.id)).join(",");
  const prevKey = useRef(itemIdsKey);
  const prevItemIds = useRef(items.map((item) => String(item.id)));
  const resetKey = options.resetKey ?? "";
  const prevResetKey = useRef(resetKey);

  useEffect(() => {
    if (prevResetKey.current !== resetKey) {
      prevResetKey.current = resetKey;
      prevKey.current = itemIdsKey;
      prevItemIds.current = items.map((item) => String(item.id));
      setSelectedIds(new Set<T["id"]>());
      return;
    }

    if (prevKey.current !== itemIdsKey) {
      const nextItemIds = items.map((item) => String(item.id));

      prevKey.current = itemIdsKey;
      prevItemIds.current = nextItemIds;

      if (options.preserveOnAppend) {
        return;
      }

      setSelectedIds(new Set<T["id"]>());
    }
  }, [itemIdsKey, items, options.preserveOnAppend, resetKey]);

  const toggle = useCallback((id: T["id"]) => {
    setSelectedIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }, []);

  const selectAll = useCallback(() => {
    setSelectedIds(new Set(items.map((i) => i.id)));
  }, [items]);

  const selectIds = useCallback((ids: Array<T["id"]>) => {
    setSelectedIds(new Set(ids));
  }, []);

  const selectNone = useCallback(() => {
    setSelectedIds(new Set<T["id"]>());
  }, []);

  const invertSelection = useCallback(() => {
    setSelectedIds((prev) => {
      const next = new Set<T["id"]>();
      for (const item of items) {
        if (!prev.has(item.id)) {
          next.add(item.id);
        }
      }
      return next;
    });
  }, [items]);

  return { selectedIds, toggle, selectAll, selectIds, selectNone, invertSelection };
}
