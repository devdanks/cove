import { useEffect, useRef, useState } from "react";
import type { HTMLAttributes, ReactNode } from "react";

interface WallMediaCardProps extends HTMLAttributes<HTMLDivElement> {
  title: string;
  imageSrc?: string | null;
  imageFallbackSrc?: string | null;
  videoSrc?: string | null;
  videoStatusSrc?: string | null;
  useVideo?: boolean;
  muted?: boolean;
  videoStartTimeSec?: number;
  videoLoadRootMargin?: string;
  videoPlayThreshold?: number;
  aspectRatio?: string;
  fillMedia?: boolean;
  fallback?: ReactNode;
  imageClassName?: string;
}

export function WallMediaCard({
  title,
  imageSrc,
  imageFallbackSrc,
  videoSrc,
  videoStatusSrc,
  useVideo = false,
  muted = true,
  videoStartTimeSec = 0,
  videoLoadRootMargin = "320px 0px",
  videoPlayThreshold = 0.6,
  aspectRatio = "1 / 1",
  fillMedia = false,
  fallback,
  imageClassName = "object-cover",
  className,
  children,
  ...props
}: WallMediaCardProps) {
  const mediaRef = useRef<HTMLDivElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const [videoFailed, setVideoFailed] = useState(false);
  const [videoAvailable, setVideoAvailable] = useState(false);
  const [shouldLoadVideo, setShouldLoadVideo] = useState(false);
  const [shouldPlayVideo, setShouldPlayVideo] = useState(false);
  const [resolvedImageSrc, setResolvedImageSrc] = useState<string | null>(imageSrc ?? imageFallbackSrc ?? null);

  useEffect(() => {
    setVideoFailed(false);
  }, [videoSrc]);

  useEffect(() => {
    setResolvedImageSrc(imageSrc ?? imageFallbackSrc ?? null);
  }, [imageFallbackSrc, imageSrc]);

  useEffect(() => {
    if (!useVideo || !videoSrc) {
      setShouldLoadVideo(false);
      setShouldPlayVideo(false);
      return;
    }

    const element = mediaRef.current;
    if (!element) return;

    if (typeof IntersectionObserver === "undefined") {
      setShouldLoadVideo(true);
      setShouldPlayVideo(true);
      return;
    }

    const loadObserver = new IntersectionObserver(([entry]) => {
      setShouldLoadVideo(entry.isIntersecting);
    }, { rootMargin: videoLoadRootMargin, threshold: 0 });
    const playObserver = new IntersectionObserver(([entry]) => {
      const intersectionRatio = typeof entry.intersectionRatio === "number"
        ? entry.intersectionRatio
        : (entry.isIntersecting ? 1 : 0);
      setShouldPlayVideo(entry.isIntersecting && intersectionRatio >= videoPlayThreshold);
    }, { threshold: [0, Math.min(1, Math.max(0.01, videoPlayThreshold)), 1] });

    loadObserver.observe(element);
    playObserver.observe(element);
    return () => {
      loadObserver.disconnect();
      playObserver.disconnect();
    };
  }, [useVideo, videoLoadRootMargin, videoPlayThreshold, videoSrc]);

  useEffect(() => {
    if (!useVideo || !videoSrc || !shouldLoadVideo) {
      setVideoAvailable(false);
      return;
    }

    if (!videoStatusSrc) {
      setVideoAvailable(true);
      return;
    }

    const controller = new AbortController();
    setVideoAvailable(false);
    fetch(videoStatusSrc, { method: "GET", signal: controller.signal })
      .then((response) => {
        return response.ok ? response.json() as Promise<{ available?: boolean }> : { available: false };
      })
      .then((status) => {
        if (!controller.signal.aborted) setVideoAvailable(status.available === true);
      })
      .catch(() => {
        if (!controller.signal.aborted) setVideoAvailable(false);
      });

    return () => controller.abort();
  }, [shouldLoadVideo, useVideo, videoSrc, videoStatusSrc]);

  const seekToStartTime = () => {
    const video = videoRef.current;
    if (!video || videoStartTimeSec <= 0 || !Number.isFinite(video.duration)) return;
    if (video.duration > videoStartTimeSec + 1) {
      video.currentTime = videoStartTimeSec;
    }
  };

  useEffect(() => {
    seekToStartTime();
  }, [videoSrc, videoStartTimeSec, videoAvailable, shouldLoadVideo]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !useVideo || !videoSrc || !videoAvailable || videoFailed) return;

    if (shouldPlayVideo) {
      const playResult = video.play();
      if (playResult && typeof playResult.catch === "function") {
        playResult.catch(() => {});
      }
    } else {
      video.pause();
    }
  }, [shouldPlayVideo, useVideo, videoSrc, videoAvailable, videoFailed]);

  return (
    <div
      {...props}
      className={`cursor-pointer rounded overflow-hidden border border-border hover:border-accent/60 transition-all ${className ?? ""}`.trim()}
      title={title}
    >
      <div ref={mediaRef} className={`relative w-full bg-surface ${fillMedia ? "h-full" : ""}`} style={fillMedia ? undefined : { aspectRatio }}>
        {useVideo && videoSrc && shouldLoadVideo && videoAvailable && !videoFailed ? (
          <video
            ref={videoRef}
            src={videoSrc}
            poster={resolvedImageSrc ?? undefined}
            className={`absolute inset-0 h-full w-full ${imageClassName}`}
            muted={muted}
            playsInline
            loop
            preload={shouldPlayVideo ? "auto" : "metadata"}
            onLoadedMetadata={seekToStartTime}
            onError={() => setVideoFailed(true)}
          />
        ) : resolvedImageSrc ? (
          <img
            src={resolvedImageSrc}
            alt={title}
            className={`absolute inset-0 h-full w-full ${imageClassName}`}
            loading="lazy"
            onError={() => {
              if (resolvedImageSrc === imageSrc && imageFallbackSrc && imageFallbackSrc !== imageSrc) {
                setResolvedImageSrc(imageFallbackSrc);
                return;
              }

              setResolvedImageSrc(null);
            }}
          />
        ) : (
          <div className="absolute inset-0 flex items-center justify-center">
            {fallback}
          </div>
        )}
        {children}
      </div>
    </div>
  );
}