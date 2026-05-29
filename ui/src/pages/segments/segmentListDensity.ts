import { useListPageCardSizeContext } from "../../components/ListPageCardSizeContext";

export interface SegmentListDensity {
  rowPaddingClassName: string;
  previewHeight: number;
  previewWidth: number;
  showPreview: boolean;
  showSecondaryDetails: boolean;
}

export function useSegmentListDensity(): SegmentListDensity {
  const cardSize = useListPageCardSizeContext();
  const level = Math.max(0, Math.min(8, cardSize?.zoomLevel ?? 1));

  if (level <= 0.25) {
    return {
      rowPaddingClassName: "py-1.5",
      previewHeight: 0,
      previewWidth: 0,
      showPreview: false,
      showSecondaryDetails: false,
    };
  }

  if (level <= 0.75) {
    return {
      rowPaddingClassName: "py-2",
      previewHeight: 0,
      previewWidth: 0,
      showPreview: false,
      showSecondaryDetails: true,
    };
  }

  const previewHeight = Math.round(Math.min(128, 48 + level * 10));

  return {
    rowPaddingClassName: level >= 3 ? "py-4" : "py-3",
    previewHeight,
    previewWidth: Math.round(previewHeight * 1.5),
    showPreview: true,
    showSecondaryDetails: true,
  };
}
