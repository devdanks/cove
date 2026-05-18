import { createContext, useContext } from "react";

export interface ListPageCardSizeContextValue {
  cardMinWidthPx: number;
  zoomLevel: number;
}

export const ListPageCardSizeContext = createContext<ListPageCardSizeContextValue | null>(null);

export function useListPageCardSizeContext() {
  return useContext(ListPageCardSizeContext);
}

export function useListPageCardMinWidthPx() {
  return useListPageCardSizeContext()?.cardMinWidthPx;
}
