import { useEffect, useRef, useState } from "react";
import { entityImages, scenes } from "../api/client";
import { WallMediaCard } from "./WallMediaCard";

export function SegmentPreviewMedia({
  hostId,
  segmentId,
  updatedAt,
  startSec,
  endSec,
  title,
  className = "h-full w-full",
}: {
  hostId: number;
  segmentId?: number;
  updatedAt?: string;
  startSec?: number;
  endSec?: number;
  title: string;
  className?: string;
}) {
  const rootRef = useRef<HTMLDivElement | null>(null);
  const [previewActive, setPreviewActive] = useState(false);
  const posterUrl = segmentId != null
    ? entityImages.segmentCoverUrl(segmentId, updatedAt)
    : scenes.screenshotUrl(hostId, updatedAt, startSec);
  const videoUrl = startSec != null ? scenes.streamUrl(hostId) : undefined;
  const mediaClassName = className.includes("object-") ? className : `${className} object-cover`;

  useEffect(() => {
    const hoverRoot = rootRef.current?.closest(".scene-card") ?? rootRef.current;
    if (!hoverRoot) return;

    const activate = () => setPreviewActive(true);
    const deactivate = () => setPreviewActive(false);
    hoverRoot.addEventListener("pointerenter", activate);
    hoverRoot.addEventListener("pointerleave", deactivate);
    hoverRoot.addEventListener("focusin", activate);
    hoverRoot.addEventListener("focusout", deactivate);
    return () => {
      hoverRoot.removeEventListener("pointerenter", activate);
      hoverRoot.removeEventListener("pointerleave", deactivate);
      hoverRoot.removeEventListener("focusin", activate);
      hoverRoot.removeEventListener("focusout", deactivate);
    };
  }, []);

  return (
    <div
      ref={rootRef}
      className="h-full w-full"
      onMouseEnter={() => setPreviewActive(true)}
      onMouseLeave={() => setPreviewActive(false)}
      onFocus={() => setPreviewActive(true)}
      onBlur={() => setPreviewActive(false)}
    >
      <WallMediaCard
        title={title}
        imageSrc={posterUrl}
        videoSrc={videoUrl}
        useVideo={previewActive && !!videoUrl}
        muted
        videoStartTimeSec={startSec ?? 0}
        videoLoadRootMargin="0px"
        videoPlayThreshold={0.1}
        trackingEnabled={false}
        fillMedia
        chromeless
        imageClassName={mediaClassName}
        videoClassName={mediaClassName}
        className="h-full w-full bg-black"
      />
    </div>
  );
}