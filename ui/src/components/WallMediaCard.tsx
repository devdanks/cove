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
  aspectRatio?: string;
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
  aspectRatio = "1 / 1",
  fallback,
  imageClassName = "object-cover",
  className,
  children,
  ...props
}: WallMediaCardProps) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const [videoFailed, setVideoFailed] = useState(false);
  const [videoAvailable, setVideoAvailable] = useState(false);
  const [resolvedImageSrc, setResolvedImageSrc] = useState<string | null>(imageSrc ?? imageFallbackSrc ?? null);

  useEffect(() => {
    setVideoFailed(false);
  }, [videoSrc]);

  useEffect(() => {
    setResolvedImageSrc(imageSrc ?? imageFallbackSrc ?? null);
  }, [imageFallbackSrc, imageSrc]);

  useEffect(() => {
    if (!useVideo || !videoSrc) {
      setVideoAvailable(false);
      return;
    }

    const controller = new AbortController();
    setVideoAvailable(false);
    fetch(videoStatusSrc ?? videoSrc, { method: videoStatusSrc ? "GET" : "HEAD", signal: controller.signal })
      .then((response) => {
        if (videoStatusSrc) {
          return response.ok ? response.json() as Promise<{ available?: boolean }> : { available: false };
        }

        const contentType = response.headers.get("content-type")?.toLowerCase() ?? "";
        return { available: response.ok && contentType.startsWith("video/") };
      })
      .then((status) => {
        if (!controller.signal.aborted) setVideoAvailable(status.available === true);
      })
      .catch(() => {
        if (!controller.signal.aborted) setVideoAvailable(false);
      });

    return () => controller.abort();
  }, [useVideo, videoSrc, videoStatusSrc]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video || !useVideo || !videoSrc || !videoAvailable || videoFailed) return;

    const observer = new IntersectionObserver((entries) => {
      for (const entry of entries) {
        if (entry.isIntersecting) video.play().catch(() => {});
        else video.pause();
      }
    }, { threshold: 0.15 });

    observer.observe(video);
    return () => observer.disconnect();
  }, [useVideo, videoSrc, videoAvailable, videoFailed]);

  return (
    <div
      {...props}
      className={`cursor-pointer rounded overflow-hidden border border-border hover:border-accent/60 transition-all ${className ?? ""}`.trim()}
      title={title}
    >
      <div className="relative w-full bg-surface" style={{ aspectRatio }}>
        {useVideo && videoSrc && videoAvailable && !videoFailed ? (
          <video
            ref={videoRef}
            src={videoSrc}
            poster={resolvedImageSrc ?? undefined}
            className={`absolute inset-0 h-full w-full ${imageClassName}`}
            muted={muted}
            playsInline
            loop
            preload="metadata"
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