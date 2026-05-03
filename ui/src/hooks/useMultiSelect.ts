import { useCallback, useEffect, useRef, useState } from "react";

export function useMultiSelect<T extends { id: string | number }>(items: T[]) {
  const [selectedIds, setSelectedIds] = useState<Set<T["id"]>>(new Set());

  // Reset selection when the items list changes (e.g. page navigation, new query results)
  const itemIdsKey = items.map((item) => String(item.id)).join(",");
  const prevKey = useRef(itemIdsKey);
  useEffect(() => {
    if (prevKey.current !== itemIdsKey) {
      prevKey.current = itemIdsKey;
      setSelectedIds(new Set<T["id"]>());
    }
  }, [itemIdsKey]);

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

  const selectNone = useCallback(() => {
    setSelectedIds(new Set<T["id"]>());
  }, []);

  return { selectedIds, toggle, selectAll, selectNone };
}
