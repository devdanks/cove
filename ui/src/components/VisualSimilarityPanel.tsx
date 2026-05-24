import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { Film, Image as ImageIcon, Sparkles } from "lucide-react";
import { useVisualSimilarityApi } from "../hooks/useVisualSimilarityApi";
import type { AiVisualSimilarImage, AiVisualSimilarScene } from "../api/types";
import { formatDuration } from "./shared";
import { EntityCardGrid } from "./EntityCardGrid";
import { ImageTile, SceneCard } from "./EntityCards";

const SIMILAR_PER_PAGE = 8;

interface PanelProps {
  onNavigate: (route: any) => void;
}

export function SceneVisualSimilarityPanel({ sceneId, onNavigate }: PanelProps & { sceneId: number }) {
  const visualSimilarity = useVisualSimilarityApi();
  const similarScenes = useQuery({
    queryKey: ["visual-similarity", "scene", sceneId, "similar-scenes"],
    queryFn: () => visualSimilarity!.similarScenesForScene(sceneId, { perPage: SIMILAR_PER_PAGE }),
    enabled: visualSimilarity != null,
    retry: false,
  });

  if (!visualSimilarity) {
    return <UnavailablePanel />;
  }

  if (similarScenes.isError) {
    return <UnavailablePanel />;
  }

  return (
    <div className="space-y-6">
      <SimilarityHeader />
      <SimilarSceneSection title="Similar Scenes" items={similarScenes.data?.items ?? []} loading={similarScenes.isLoading} error={similarScenes.isError} onNavigate={onNavigate} />
    </div>
  );
}

export function ImageVisualSimilarityPanel({ imageId, onNavigate }: PanelProps & { imageId: number }) {
  const visualSimilarity = useVisualSimilarityApi();
  const similarScenes = useQuery({
    queryKey: ["visual-similarity", "image", imageId, "similar-scenes"],
    queryFn: () => visualSimilarity!.similarScenesForImage(imageId, { perPage: SIMILAR_PER_PAGE }),
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
    return <UnavailablePanel />;
  }

  if (similarScenes.isError && similarImages.isError) {
    return <UnavailablePanel />;
  }

  return (
    <div className="space-y-6">
      <SimilarityHeader />
      <SimilarSceneSection title="Similar Scenes" items={similarScenes.data?.items ?? []} loading={similarScenes.isLoading} error={similarScenes.isError} onNavigate={onNavigate} />
      <SimilarImageSection title="Similar Images" items={similarImages.data?.items ?? []} loading={similarImages.isLoading} error={similarImages.isError} onNavigate={onNavigate} />
    </div>
  );
}

type SegmentSimilarityInterval = { startSec: number; endSec?: number };

export function SegmentVisualSimilarityPanel({ sceneId, startSec, endSec, intervals, onNavigate }: PanelProps & { sceneId: number; startSec?: number; endSec?: number; intervals?: SegmentSimilarityInterval[] }) {
  const visualSimilarity = useVisualSimilarityApi();
  const queryIntervals = normalizeIntervals(intervals, startSec, endSec);
  const intervalKey = queryIntervals.map((interval) => `${interval.startSec}:${interval.endSec ?? ""}`).join("|");
  const similarScenes = useQuery({
    queryKey: ["visual-similarity", "scene", sceneId, "segment-similar-scenes", intervalKey],
    queryFn: () => visualSimilarity!.similarScenesForSceneSegment(sceneId, { intervals: queryIntervals, perPage: SIMILAR_PER_PAGE }),
    retry: false,
    enabled: visualSimilarity != null && queryIntervals.length > 0,
  });

  if (!visualSimilarity) {
    return <UnavailablePanel />;
  }

  if (similarScenes.isError) {
    return <UnavailablePanel />;
  }

  return (
    <div className="space-y-6">
      <SimilarityHeader />
      <SimilarSceneSection title="Similar Scenes" items={similarScenes.data?.items ?? []} loading={similarScenes.isLoading} error={similarScenes.isError} onNavigate={onNavigate} />
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

function SimilarSceneSection({ title, items, loading, error, onNavigate }: { title: string; items: AiVisualSimilarScene[]; loading: boolean; error: boolean; onNavigate: (route: any) => void }) {
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
            <SimilarSceneCard key={item.scene.id} item={item} onNavigate={onNavigate} />
          ))}
        </EntityCardGrid>
      )}
    </section>
  );
}

function SimilarImageSection({ title, items, loading, error, onNavigate }: { title: string; items: AiVisualSimilarImage[]; loading: boolean; error: boolean; onNavigate: (route: any) => void }) {
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

function SimilarSceneCard({ item, onNavigate }: { item: AiVisualSimilarScene; onNavigate: (route: any) => void }) {
  const scene = item.scene;
  const matchStart = item.sectionIndex > 0 ? item.startSec : undefined;

  return (
    <div className="relative h-full">
      <SceneCard scene={scene} onClick={() => onNavigate(matchStart != null ? { page: "scene", id: scene.id, seekTo: matchStart } : { page: "scene", id: scene.id })} onNavigate={onNavigate} />
      <SimilarityOverlay distance={item.distance} label={getSceneMeta(item)} />
    </div>
  );
}

function SimilarImageCard({ item, onNavigate }: { item: AiVisualSimilarImage; onNavigate: (route: any) => void }) {
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

function UnavailablePanel() {
  return <EmptyPanel icon={<Sparkles className="h-10 w-10" />} message="Visual similarity is unavailable." />;
}

function getSceneMeta(item: AiVisualSimilarScene) {
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