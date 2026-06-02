import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { Film, Image as ImageIcon, Sparkles } from "lucide-react";
import { useVisualSimilarityApi } from "../hooks/useVisualSimilarityApi";
import type { VisualSimilarImage, VisualSimilarVideo } from "../api/types";
import { formatDuration } from "./shared";
import { EntityCardGrid } from "./EntityCardGrid";
import { ImageTile, VideoCard } from "./EntityCards";
import { useManualContext } from "./ManualContext";

const SIMILAR_PER_PAGE = 8;
const AVAILABILITY_PER_PAGE = 1;

interface PanelProps {
  onNavigate: (route: any) => void;
}

export function useVideoVisualSimilarityAvailable(videoId?: number) {
  const visualSimilarity = useVisualSimilarityApi();
  const preview = useQuery({
    queryKey: ["visual-similarity", "video", videoId, "similar-videos", "preview"],
    queryFn: () => visualSimilarity!.similarVideosForVideo(videoId!, { perPage: AVAILABILITY_PER_PAGE }),
    enabled: visualSimilarity != null && typeof videoId === "number" && videoId > 0,
    retry: false,
  });

  return visualSimilarity != null && (preview.data?.items.length ?? 0) > 0;
}

export function useImageVisualSimilarityAvailable(imageId?: number) {
  const visualSimilarity = useVisualSimilarityApi();
  const similarVideosPreview = useQuery({
    queryKey: ["visual-similarity", "image", imageId, "similar-videos", "preview"],
    queryFn: () => visualSimilarity!.similarVideosForImage(imageId!, { perPage: AVAILABILITY_PER_PAGE }),
    enabled: visualSimilarity != null && typeof imageId === "number" && imageId > 0,
    retry: false,
  });
  const similarImagesPreview = useQuery({
    queryKey: ["visual-similarity", "image", imageId, "similar-images", "preview"],
    queryFn: () => visualSimilarity!.similarImagesForImage(imageId!, { perPage: AVAILABILITY_PER_PAGE }),
    enabled: visualSimilarity != null && typeof imageId === "number" && imageId > 0,
    retry: false,
  });

  return visualSimilarity != null
    && ((similarVideosPreview.data?.items.length ?? 0) > 0 || (similarImagesPreview.data?.items.length ?? 0) > 0);
}

export function VideoVisualSimilarityPanel({ videoId, onNavigate }: PanelProps & { videoId: number }) {
  useManualContext(["panel:visual-similarity", "feature:visual-similarity"]);
  const visualSimilarity = useVisualSimilarityApi();
  const similarVideos = useQuery({
    queryKey: ["visual-similarity", "video", videoId, "similar-videos"],
    queryFn: () => visualSimilarity!.similarVideosForVideo(videoId, { perPage: SIMILAR_PER_PAGE }),
    enabled: visualSimilarity != null,
    retry: false,
  });

  if (!visualSimilarity) {
    return <UnavailablePanel message="No visual embedding provider is available." />;
  }

  if (similarVideos.isError) {
    return <UnavailablePanel message="Visual similarity could not be loaded." />;
  }

  return (
    <div className="space-y-6">
      <SimilarityHeader />
      <SimilarVideoSection title="Similar Videos" items={similarVideos.data?.items ?? []} loading={similarVideos.isLoading} error={similarVideos.isError} onNavigate={onNavigate} />
    </div>
  );
}

export function ImageVisualSimilarityPanel({ imageId, onNavigate }: PanelProps & { imageId: number }) {
  useManualContext(["panel:visual-similarity", "feature:visual-similarity"]);
  const visualSimilarity = useVisualSimilarityApi();
  const similarVideos = useQuery({
    queryKey: ["visual-similarity", "image", imageId, "similar-videos"],
    queryFn: () => visualSimilarity!.similarVideosForImage(imageId, { perPage: SIMILAR_PER_PAGE }),
    enabled: visualSimilarity != null,
    retry: false,
  });
  const similarImages = useQuery({
    queryKey: ["visual-similarity", "image", imageId, "similar-images"],
    queryFn: () => visualSimilarity!.similarImagesForImage(imageId, { perPage: SIMILAR_PER_PAGE }),
    enabled: visualSimilarity != null,
    retry: false,
  });

  if (!visualSimilarity) {
    return <UnavailablePanel message="No visual embedding provider is available." />;
  }

  if (similarVideos.isError && similarImages.isError) {
    return <UnavailablePanel message="Visual similarity could not be loaded." />;
  }

  return (
    <div className="space-y-6">
      <SimilarityHeader />
      <SimilarVideoSection title="Similar Videos" items={similarVideos.data?.items ?? []} loading={similarVideos.isLoading} error={similarVideos.isError} onNavigate={onNavigate} />
      <SimilarImageSection title="Similar Images" items={similarImages.data?.items ?? []} loading={similarImages.isLoading} error={similarImages.isError} onNavigate={onNavigate} />
    </div>
  );
}

type SegmentSimilarityInterval = { startSec: number; endSec?: number };

export function useSegmentVisualSimilarityAvailable({ videoId, startSec, endSec, intervals }: { videoId?: number; startSec?: number; endSec?: number; intervals?: SegmentSimilarityInterval[] }) {
  const visualSimilarity = useVisualSimilarityApi();
  const queryIntervals = normalizeIntervals(intervals, startSec, endSec);
  const intervalKey = queryIntervals.map((interval) => `${interval.startSec}:${interval.endSec ?? ""}`).join("|");
  const preview = useQuery({
    queryKey: ["visual-similarity", "video", videoId, "segment-similar-videos", "preview", intervalKey],
    queryFn: () => visualSimilarity!.similarVideosForVideoSegment(videoId!, { intervals: queryIntervals, perPage: AVAILABILITY_PER_PAGE }),
    enabled: visualSimilarity != null && typeof videoId === "number" && videoId > 0 && queryIntervals.length > 0,
    retry: false,
  });

  return visualSimilarity != null && (preview.data?.items.length ?? 0) > 0;
}

export function SegmentVisualSimilarityPanel({ videoId, startSec, endSec, intervals, onNavigate }: PanelProps & { videoId: number; startSec?: number; endSec?: number; intervals?: SegmentSimilarityInterval[] }) {
  useManualContext(["panel:visual-similarity", "feature:visual-similarity"]);
  const visualSimilarity = useVisualSimilarityApi();
  const queryIntervals = normalizeIntervals(intervals, startSec, endSec);
  const intervalKey = queryIntervals.map((interval) => `${interval.startSec}:${interval.endSec ?? ""}`).join("|");
  const similarVideos = useQuery({
    queryKey: ["visual-similarity", "video", videoId, "segment-similar-videos", intervalKey],
    queryFn: () => visualSimilarity!.similarVideosForVideoSegment(videoId, { intervals: queryIntervals, perPage: SIMILAR_PER_PAGE }),
    retry: false,
    enabled: visualSimilarity != null && queryIntervals.length > 0,
  });

  if (!visualSimilarity) {
    return <UnavailablePanel message="No visual embedding provider is available." />;
  }

  if (similarVideos.isError) {
    return <UnavailablePanel message="Visual similarity could not be loaded." />;
  }

  return (
    <div className="space-y-6">
      <SimilarityHeader />
      <SimilarVideoSection title="Similar Videos" items={similarVideos.data?.items ?? []} loading={similarVideos.isLoading} error={similarVideos.isError} onNavigate={onNavigate} />
    </div>
  );
}

function SimilarityHeader() {
  return (
    <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-muted">
      <Sparkles className="h-3.5 w-3.5" />
      Visual Similarity
    </div>
  );
}

function SimilarVideoSection({ title, items, loading, error, onNavigate }: { title: string; items: VisualSimilarVideo[]; loading: boolean; error: boolean; onNavigate: (route: any) => void }) {
  if (error) {
    return null;
  }

  return (
    <section>
      <SectionTitle title={title} count={items.length} />
      {loading ? (
        <LoadingPanel />
      ) : items.length === 0 ? (
        <EmptyPanel icon={<Film className="h-10 w-10" />} message="No visual matches yet." />
      ) : (
        <EntityCardGrid minCardWidth="240px" gapClassName="gap-4" className="mt-3">
          {items.map((item) => (
            <SimilarVideoCard key={item.video.id} item={item} onNavigate={onNavigate} />
          ))}
        </EntityCardGrid>
      )}
    </section>
  );
}

function SimilarImageSection({ title, items, loading, error, onNavigate }: { title: string; items: VisualSimilarImage[]; loading: boolean; error: boolean; onNavigate: (route: any) => void }) {
  if (error) {
    return null;
  }

  return (
    <section>
      <SectionTitle title={title} count={items.length} />
      {loading ? (
        <LoadingPanel />
      ) : items.length === 0 ? (
        <EmptyPanel icon={<ImageIcon className="h-10 w-10" />} message="No visual matches yet." />
      ) : (
        <EntityCardGrid minCardWidth="190px" gapClassName="gap-4" className="mt-3">
          {items.map((item) => (
            <SimilarImageCard key={item.image.id} item={item} onNavigate={onNavigate} />
          ))}
        </EntityCardGrid>
      )}
    </section>
  );
}

function SectionTitle({ title, count }: { title: string; count: number }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <h2 className="text-sm font-semibold text-foreground">{title}</h2>
      {count > 0 ? <span className="text-xs text-muted">{count}</span> : null}
    </div>
  );
}

function SimilarVideoCard({ item, onNavigate }: { item: VisualSimilarVideo; onNavigate: (route: any) => void }) {
  const video = item.video;
  const matchStart = item.sectionIndex > 0 ? item.startSec : undefined;

  return (
    <div className="relative h-full">
      <VideoCard video={video} onClick={() => onNavigate(matchStart != null ? { page: "video", id: video.id, seekTo: matchStart } : { page: "video", id: video.id })} onNavigate={onNavigate} />
      <SimilarityOverlay distance={item.distance} label={getVideoMeta(item)} />
    </div>
  );
}

function SimilarImageCard({ item, onNavigate }: { item: VisualSimilarImage; onNavigate: (route: any) => void }) {
  const image = item.image;

  return (
    <div className="relative h-full">
      <ImageTile image={image} onClick={() => onNavigate({ page: "image", id: image.id })} onNavigate={onNavigate} />
      <SimilarityOverlay distance={item.distance} />
    </div>
  );
}

function SimilarityOverlay({ distance, label }: { distance: number; label?: string }) {
  const match = Math.max(0, Math.min(100, Math.round((1 - distance) * 100)));
  return (
    <div className="pointer-events-none absolute left-2 right-2 top-2 z-20 flex items-start justify-between gap-2">
      {label ? <span className="max-w-[70%] truncate rounded bg-black/75 px-2 py-1 text-[11px] font-medium text-white shadow-sm">{label}</span> : <span />}
      <span className="shrink-0 rounded bg-black/75 px-2 py-1 text-[11px] font-medium text-white shadow-sm">{match}%</span>
    </div>
  );
}

function LoadingPanel() {
  return <div className="mt-3 rounded-xl border border-border bg-surface/40 px-4 py-8 text-center text-sm text-secondary">Loading...</div>;
}

function EmptyPanel({ icon, message }: { icon: ReactNode; message: string }) {
  return (
    <div className="mt-3 flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-surface/40 px-4 py-8 text-center text-sm text-secondary">
      <div className="mb-3 text-muted opacity-60">{icon}</div>
      <p>{message}</p>
    </div>
  );
}

function UnavailablePanel({ message }: { message: string }) {
  return <EmptyPanel icon={<Sparkles className="h-10 w-10" />} message={message} />;
}

function getVideoMeta(item: VisualSimilarVideo) {
  if (item.sectionIndex > 0 && item.startSec != null) {
    return item.endSec != null ? `${formatDuration(item.startSec)} - ${formatDuration(item.endSec)}` : formatDuration(item.startSec);
  }

  return undefined;
}

function normalizeIntervals(intervals: SegmentSimilarityInterval[] | undefined, startSec: number | undefined, endSec: number | undefined) {
  const source = intervals && intervals.length > 0 ? intervals : startSec != null ? [{ startSec, endSec }] : [];
  return source
    .filter((interval) => Number.isFinite(interval.startSec))
    .map((interval) => ({
      startSec: interval.startSec,
      endSec: typeof interval.endSec === "number" && Number.isFinite(interval.endSec) ? interval.endSec : undefined,
    }));
}
