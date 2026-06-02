import { createContext, useContext, useState, useCallback, type ReactNode } from "react";

interface VideoQueueState {
  videoIds: number[];
  currentIndex: number;
  autoplay: boolean;
  items?: Record<number, VideoQueueItem>;
}

export interface VideoQueueItem {
  id: number;
  title?: string | null;
  subtitle?: string | null;
  imagePath?: string | null;
}

interface VideoQueueContextValue {
  queue: VideoQueueState | null;
  setQueue: (ids: number[], currentId: number, items?: VideoQueueItem[]) => void;
  clearQueue: () => void;
  currentId: number | null;
  prevId: number | null;
  nextId: number | null;
  hasPrev: boolean;
  hasNext: boolean;
  goToIndex: (index: number) => number | null;
  toggleAutoplay: () => void;
  autoplay: boolean;
  queueLength: number;
  currentPosition: number;
  queueItems: VideoQueueItem[];
}

const VideoQueueContext = createContext<VideoQueueContextValue | null>(null);

export function VideoQueueProvider({ children }: { children: ReactNode }) {
  const [queue, setQueueState] = useState<VideoQueueState | null>(null);

  const setQueue = useCallback((ids: number[], currentId: number, items?: VideoQueueItem[]) => {
    const idx = ids.indexOf(currentId);
    const itemMap = items?.reduce<Record<number, VideoQueueItem>>((map, item) => {
      map[item.id] = item;
      return map;
    }, {});
    setQueueState({ videoIds: ids, currentIndex: idx >= 0 ? idx : 0, autoplay: false, items: itemMap });
  }, []);

  const clearQueue = useCallback(() => setQueueState(null), []);

  const currentId = queue ? queue.videoIds[queue.currentIndex] ?? null : null;
  const prevId = queue && queue.currentIndex > 0 ? queue.videoIds[queue.currentIndex - 1] : null;
  const nextId = queue && queue.currentIndex < queue.videoIds.length - 1 ? queue.videoIds[queue.currentIndex + 1] : null;
  const queueItems = queue
    ? queue.videoIds.map((id) => queue.items?.[id] ?? { id })
    : [];

  const goToIndex = useCallback((index: number) => {
    if (!queue || index < 0 || index >= queue.videoIds.length) return null;
    const id = queue.videoIds[index];
    setQueueState({ ...queue, currentIndex: index });
    return id;
  }, [queue]);

  const toggleAutoplay = useCallback(() => {
    setQueueState((prev) => prev ? { ...prev, autoplay: !prev.autoplay } : null);
  }, []);

  return (
    <VideoQueueContext.Provider
      value={{
        queue,
        setQueue,
        clearQueue,
        currentId,
        prevId,
        nextId,
        hasPrev: prevId !== null,
        hasNext: nextId !== null,
        goToIndex,
        toggleAutoplay,
        autoplay: queue?.autoplay ?? false,
        queueLength: queue?.videoIds.length ?? 0,
        currentPosition: queue ? queue.currentIndex + 1 : 0,
        queueItems,
      }}
    >
      {children}
    </VideoQueueContext.Provider>
  );
}

export function useVideoQueue() {
  const ctx = useContext(VideoQueueContext);
  if (!ctx) throw new Error("useVideoQueue must be used within VideoQueueProvider");
  return ctx;
}

