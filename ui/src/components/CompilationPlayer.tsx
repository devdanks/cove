import { useCallback, useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { ExternalLink, Repeat, RotateCcw, SkipBack, SkipForward } from "lucide-react";
import { scenes } from "../api/client";
import type { GroupPlaybackManifestItem } from "../api/types";
import { MediaDetailLayout } from "./MediaDetailLayout/MediaDetailLayout";
import { VideoPlayer } from "./VideoPlayer";

interface Props {
  groupId: number;
  groupName: string;
  items: GroupPlaybackManifestItem[];
  onNavigate: (r: any) => void;
  embedded?: boolean;
  backLabel?: string;
  onGoBack?: () => void;
}

export function CompilationPlayer({
  groupId,
  groupName,
  items,
  onNavigate,
  embedded = false,
  backLabel,
  onGoBack,
}: Props) {
  const seekRef = useRef<((time: number) => void) | null>(null);
  const [currentItemIndex, setCurrentItemIndex] = useState(0);
  const [loopCompilation, setLoopCompilation] = useState(false);
  const [autostart, setAutostart] = useState(false);
  const [autostartToken, setAutostartToken] = useState(0);

  const item = items[currentItemIndex];
  const nextItem = items[currentItemIndex + 1] ?? (loopCompilation ? items[0] : undefined);
  const { data: currentScene, isLoading: currentSceneLoading } = useQuery({
    queryKey: ["scene", item?.sceneId],
    queryFn: () => scenes.get(item!.sceneId),
    enabled: !!item,
  });
  useQuery({
    queryKey: ["scene", nextItem?.sceneId],
    queryFn: () => scenes.get(nextItem!.sceneId),
    enabled: nextItem != null,
    staleTime: 60_000,
  });

  const currentFile = currentScene?.files[0];
  const clipEnd = item ? item.endSec ?? currentFile?.duration ?? item.startSec + (item.durationSec ?? 0) : 0;
  const clipDuration = item ? Math.max(0, clipEnd - item.startSec) : 0;

  useEffect(() => {
    if (!item) {
      return;
    }

    seekRef.current = null;
  }, [item]);

  useEffect(() => {
    if (!autostart || !item || currentSceneLoading || !currentFile) {
      return;
    }

    seekRef.current?.(item.startSec);
  }, [autostart, autostartToken, currentFile, currentSceneLoading, item]);

  const moveToItem = useCallback((nextIndex: number, shouldAutoPlay = false) => {
    const boundedIndex = Math.min(items.length - 1, Math.max(0, nextIndex));
    if (shouldAutoPlay) {
      setAutostart(true);
      setAutostartToken((value) => value + 1);
    } else {
      setAutostart(false);
    }
    setCurrentItemIndex(boundedIndex);
  }, [items.length]);

  const advanceToNextItem = useCallback(() => {
    if (currentItemIndex + 1 < items.length) {
      moveToItem(currentItemIndex + 1, true);
      return;
    }

    if (loopCompilation) {
      moveToItem(0, true);
    }
  }, [currentItemIndex, items.length, loopCompilation, moveToItem]);

  const restartItem = useCallback(() => {
    if (!item) {
      return;
    }

    setAutostart(true);
    setAutostartToken((value) => value + 1);
    seekRef.current?.(item.startSec);
  }, [item]);

  if (!item) {
    return null;
  }

  const playerMedia = (
    <div className="flex min-h-0 min-w-0 max-w-full flex-1 flex-col overflow-hidden bg-black">
      {currentSceneLoading ? (
        <div className="flex flex-1 items-center justify-center text-sm text-secondary">
          Loading clip playback...
        </div>
      ) : currentFile ? (
        <div className="flex min-h-0 min-w-0 max-w-full flex-1 overflow-hidden bg-black">
          <VideoPlayer
            streamUrl={scenes.streamUrl(item.sceneId)}
            posterUrl={item.posterPath ? scenes.screenshotUrl(item.sceneId) : undefined}
            format={currentFile.format}
            duration={currentFile.duration}
            resumeTime={item.startSec}
            sceneId={item.sceneId}
            detections={[]}
            captions={currentFile.captions}
            onPlay={() => setAutostart(false)}
            onSeekRegister={(fn) => {
              seekRef.current = fn;
            }}
            autostart={autostart}
            autostartToken={autostartToken}
            playbackTracking={{
              hostType: "group",
              hostId: groupId,
              scopeKey: `group:${groupId}`,
              groupItemId: item.groupItemId,
            }}
            onEnded={advanceToNextItem}
            clip={{ start: item.startSec, end: item.endSec ?? currentFile.duration, loop: false }}
          />
        </div>
      ) : (
        <div className="flex flex-1 items-center justify-center text-sm text-secondary">
          No playable scene file is available for this group item.
        </div>
      )}
    </div>
  );

  const playbackControls = (
    <div className="flex flex-wrap items-center gap-2">
      <button
        type="button"
        onClick={() => moveToItem(currentItemIndex - 1, true)}
        disabled={currentItemIndex === 0 && !loopCompilation}
        className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
      >
        <SkipBack className="h-4 w-4" />
        Previous item
      </button>
      <button
        type="button"
        onClick={() => moveToItem(currentItemIndex + 1, true)}
        disabled={currentItemIndex >= items.length - 1 && !loopCompilation}
        className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
      >
        <SkipForward className="h-4 w-4" />
        Next item
      </button>
      <button
        type="button"
        onClick={restartItem}
        disabled={!currentFile}
        className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
      >
        <RotateCcw className="h-4 w-4" />
        Restart item
      </button>
      <button
        type="button"
        onClick={() => setLoopCompilation((value) => !value)}
        className={`inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors ${loopCompilation ? "border-accent bg-accent/10 text-accent" : "border-border text-foreground hover:border-accent"}`}
      >
        <Repeat className="h-4 w-4" />
        Loop compilation
      </button>
    </div>
  );

  const playlistAndCurrentItem = (
    <div className="grid gap-4 lg:grid-cols-[minmax(0,1.1fr),minmax(0,0.9fr)]">
      <div className="rounded-2xl border border-border bg-surface/50 p-4">
        <div className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-muted">Playlist</div>
        <div className="flex flex-wrap gap-2">
          {items.map((manifestItem, index) => (
            <button
              key={manifestItem.groupItemId}
              type="button"
              onClick={() => moveToItem(index)}
              className={`rounded-full border px-3 py-2 text-sm transition-colors ${index === currentItemIndex ? "border-accent bg-accent/10 text-accent" : "border-border bg-card/60 text-secondary hover:border-accent hover:text-foreground"}`}
            >
              {index + 1}. {manifestItem.title || manifestItem.sceneTitle || `Scene #${manifestItem.sceneId}`}
            </button>
          ))}
        </div>
      </div>

      <div className="rounded-2xl border border-border bg-surface/50 p-4">
        <div className="mb-2 text-[11px] font-semibold uppercase tracking-wide text-muted">Current Item</div>
        <div className="space-y-3 text-sm text-secondary">
          <div>{item.title || item.sceneTitle || `Scene #${item.sceneId}`}</div>
          <div>{formatTime(item.startSec)} - {formatTime(clipEnd)}</div>
          <div>{clipDuration > 0 ? `${formatTime(clipDuration)} total clip length` : "Instant clip"}</div>
          <div className="flex flex-wrap gap-2 pt-1">
            <button
              type="button"
              onClick={() => onNavigate({ page: "scene", id: item.sceneId, seekTo: item.startSec })}
              className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              <ExternalLink className="h-4 w-4" />
              Open scene
            </button>
            <button
              type="button"
              onClick={() => onNavigate({ page: "group", id: groupId })}
              className="rounded-lg border border-border px-3 py-2 text-sm text-foreground transition-colors hover:border-accent"
            >
              Back to group
            </button>
          </div>
        </div>
      </div>
    </div>
  );

  if (!embedded) {
    return (
      <MediaDetailLayout
        title={groupName}
        subtitle={
          <div className="flex flex-wrap items-center gap-3 text-sm text-secondary">
            <span>{items.length} item{items.length === 1 ? "" : "s"}</span>
            <span>Now playing {currentItemIndex + 1}/{items.length}</span>
            <span>{clipDuration > 0 ? `${formatTime(clipDuration)} clip` : "Instant clip"}</span>
          </div>
        }
        backLabel={backLabel}
        onGoBack={onGoBack}
        media={playerMedia}
        mediaAspectRatio="auto"
        mediaFullBleed
        mediaSticky={false}
      >
        <MediaDetailLayout.Content>
          <div className="space-y-4">
            <section className="rounded-2xl border border-border bg-card/70 p-5">
              <div className="text-xs font-semibold uppercase tracking-wide text-muted">Compilation Playback</div>
              <p className="mt-2 text-sm text-secondary">Playback uses the shared scene player and auto-advances through each snapshotted clip range in sequence.</p>
            </section>
            {playbackControls}
            {playlistAndCurrentItem}
          </div>
        </MediaDetailLayout.Content>
      </MediaDetailLayout>
    );
  }

  return (
    <article className="flex h-full flex-col bg-card/80">
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border px-5 py-4">
        <div>
          <div className="text-xs font-semibold uppercase tracking-wide text-muted">Compilation Playback</div>
          <h2 className="mt-2 text-xl font-semibold text-foreground">{groupName}</h2>
          <p className="mt-2 text-sm text-secondary">Playback uses the shared scene player and auto-advances through each snapshotted clip range in sequence.</p>
        </div>
        <div className="flex flex-wrap gap-2 text-xs text-secondary">
          <span className="rounded-full border border-border bg-surface px-2 py-1">{items.length} item{items.length === 1 ? "" : "s"}</span>
          <span className="rounded-full border border-border bg-surface px-2 py-1">Now playing {currentItemIndex + 1}/{items.length}</span>
        </div>
      </div>

      {playerMedia}

      <div className="space-y-4 p-5">
        {playbackControls}
        {playlistAndCurrentItem}
      </div>
    </article>
  );
}

function formatTime(value: number) {
  const totalHundredths = Math.max(0, Math.round(value * 100));
  const hours = Math.floor(totalHundredths / 360000);
  const minutes = Math.floor((totalHundredths % 360000) / 6000);
  const seconds = Math.floor((totalHundredths % 6000) / 100);
  const hundredths = totalHundredths % 100;

  if (hundredths === 0) {
    if (hours > 0) {
      return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
    }

    return `${minutes}:${String(seconds).padStart(2, "0")}`;
  }

  const fractional = hundredths % 10 === 0
    ? String(Math.floor(hundredths / 10))
    : String(hundredths).padStart(2, "0");

  if (hours > 0) {
    return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}.${fractional}`;
  }

  return `${minutes}:${String(seconds).padStart(2, "0")}.${fractional}`;
}