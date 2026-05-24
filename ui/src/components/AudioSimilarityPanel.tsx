import { useQuery } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { Film, Volume2 } from "lucide-react";
import { useAudioSimilarityApi } from "../hooks/useAudioSimilarityApi";
import type { AiAudioSimilarScene } from "../api/types";
import { formatDuration } from "./shared";
import { EntityCardGrid } from "./EntityCardGrid";
import { SceneCard } from "./EntityCards";

const SIMILAR_PER_PAGE = 8;

interface PanelProps {
  onNavigate: (route: any) => void;
}

export function SceneAudioSimilarityPanel({ sceneId, onNavigate }: PanelProps & { sceneId: number }) {
  const audioSimilarity = useAudioSimilarityApi();
  const similarScenes = useQuery({
    queryKey: ["audio-similarity", "scene", sceneId, "similar-scenes"],
    queryFn: () => audioSimilarity!.similarScenesForScene(sceneId, { perPage: SIMILAR_PER_PAGE }),
    enabled: audioSimilarity != null,
    retry: false,
  });

  if (!audioSimilarity) {
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
      <Volume2 className="h-3.5 w-3.5" />
      Audio Similarity
    </div>
  );
}

function SimilarSceneSection({ title, items, loading, error, onNavigate }: { title: string; items: AiAudioSimilarScene[]; loading: boolean; error: boolean; onNavigate: (route: any) => void }) {
  if (error) {
    return null;
  }

  return (
    <section>
      <SectionTitle title={title} count={items.length} />
      {loading ? (
        <LoadingPanel />
      ) : items.length === 0 ? (
        <EmptyPanel icon={<Film className="h-10 w-10" />} message="No audio matches yet." />
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

function SectionTitle({ title, count }: { title: string; count: number }) {
  return (
    <div className="flex items-center justify-between gap-3">
      <h2 className="text-sm font-semibold text-foreground">{title}</h2>
      {count > 0 ? <span className="text-xs text-muted">{count}</span> : null}
    </div>
  );
}

function SimilarSceneCard({ item, onNavigate }: { item: AiAudioSimilarScene; onNavigate: (route: any) => void }) {
  const scene = item.scene;
  const matchStart = item.sectionIndex > 0 ? item.startSec : undefined;

  return (
    <div className="relative h-full">
      <SceneCard scene={scene} onClick={() => onNavigate(matchStart != null ? { page: "scene", id: scene.id, seekTo: matchStart } : { page: "scene", id: scene.id })} onNavigate={onNavigate} />
      <SimilarityOverlay distance={item.distance} label={getSceneMeta(item)} />
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
  return <EmptyPanel icon={<Volume2 className="h-10 w-10" />} message="Audio similarity is unavailable." />;
}

function getSceneMeta(item: AiAudioSimilarScene) {
  if (item.sectionIndex > 0 && item.startSec != null) {
    return item.endSec != null ? `${formatDuration(item.startSec)} - ${formatDuration(item.endSec)}` : formatDuration(item.startSec);
  }

  return undefined;
}