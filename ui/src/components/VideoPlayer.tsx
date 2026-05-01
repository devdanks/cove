import {
  Maximize,
  Minimize,
  Pause,
  PictureInPicture2,
  Play,
  Repeat,
  Repeat1,
  SkipBack,
import { scenes } from "../api/client";
import type { Detection } from "../api/types";
import { createPlaybackTracker, type PlaybackTrackingTarget } from "../utils/interactionTracking";
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }

  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (character) => {
    const random = Math.random() * 16 | 0;
    const value = character === "x" ? random : (random & 0x3) | 0x8;
    return value.toString(16);
  });
}

function roundPlaybackTime(value: number) {
  return Math.round(value * 1000) / 1000;
}

export function VideoPlayer({
  streamUrl,
  posterUrl,
  format,
  duration,
  resumeTime,
  sceneId,
  detections = [],
  captions,
  onPlay,
  onSeekRegister,
  onTimeUpdate: onTimeUpdateProp,
  autostart,
  autostartToken,
  showAbLoop,
  trackActivity = true,
  playbackTracking,
  onEnded: onEndedProp,
  clip,
  onPrev,
  onNext,
}: {
  streamUrl: string;
  posterUrl?: string;
  format: string;
  duration: number;
  resumeTime?: number;
  sceneId: number;
  detections?: Detection[];
  captions?: { id: number; languageCode: string; captionType: string; filename: string }[];
  onPlay: () => void;
  onSeekRegister?: (fn: (time: number) => void) => void;
  onTimeUpdate?: (time: number) => void;
  autostart?: boolean;
  autostartToken?: number;
  showAbLoop?: boolean;
  trackActivity?: boolean;
  playbackTracking?: PlaybackTrackingTarget;
  onEnded?: () => void;
  clip?: { start: number; end?: number; loop?: boolean };
  onPrev?: () => void;
  onNext?: () => void;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [playing, setPlaying] = useState(false);
  const [currentTime, setCurTime] = useState(0);
  const [buffered, setBuffered] = useState(0);
  const [vol, setVol] = useState(() => {
    const saved = localStorage.getItem(VOLUME_KEY);
    return saved ? Number(saved) : 1;
  });
  const [muted, setMuted] = useState(() => localStorage.getItem(MUTED_KEY) === "true");
  const [fullscreen, setFullscreen] = useState(false);
  const [showControls, setShowControls] = useState(true);
  const [showSpeed, setShowSpeed] = useState(false);
  const [rate, setRate] = useState(1);
  const [pip, setPip] = useState(false);
  const [loop, setLoop] = useState(false);
  const [abLoop, setAbLoop] = useState<{ a: number | null; b: number | null }>({ a: null, b: null });
  const [showCaptions, setShowCaptions] = useState(false);
  const [showQuality, setShowQuality] = useState(false);
  const [selectedQuality, setSelectedQuality] = useState<string>("Direct");
  const [availableQualities, setAvailableQualities] = useState<string[]>([]);
  const playbackTracker = useRef(createPlaybackTracker());
  const hideTimer = useRef<ReturnType<typeof setTimeout>>(null);
  const playTriggered = useRef(false);
  const sourceRestoreRef = useRef<{ time: number; shouldPlay: boolean } | null>(null);
  const lastLoadedSourceRef = useRef<string | null>(null);
  const pendingAutostartRef = useRef(false);
  const intervalStart = useRef<number | null>(null);
  const lastSeenTime = useRef<number>(0);
  const lastKeepaliveSentAt = useRef<number>(0);
  const journalFlushed = useRef(false);
  const lastHideInteractionAt = useRef(0);
  const clipEndedHandled = useRef(false);
  const [videoBox, setVideoBox] = useState({ left: 0, top: 0, width: 0, height: 0 });
  const clipStart = clip?.start ?? 0;
  const clipEnd = Math.max(clipStart, clip?.end ?? duration);
  const timelineStart = clip ? clipStart : 0;
  const timelineDuration = clip ? Math.max(clipEnd - clipStart, 0.001) : Math.max(duration, 0.001);
  const visibleCurrentTime = clip ? Math.max(0, currentTime - clipStart) : currentTime;
  const visibleBuffered = clip ? Math.max(0, Math.min(buffered, clipEnd) - clipStart) : buffered;
  const playbackTrackingTarget = useMemo<PlaybackTrackingTarget | null>(() => {
    if (!trackActivity) {
      return null;
    }

    return playbackTracking ?? { hostType: "scene", hostId: sceneId, scopeKey: `scene:${sceneId}` };
  }, [playbackTracking, sceneId, trackActivity]);

  useEffect(() => {
    intervalStart.current = null;
    lastSeenTime.current = 0;
    lastKeepaliveSentAt.current = 0;
    lastHideInteractionAt.current = 0;
    playTriggered.current = false;
    pendingAutostartRef.current = false;
  }, [sceneId]);

  useEffect(() => {
    void playbackTracker.current.setTarget(playbackTrackingTarget);
  }, [playbackTrackingTarget?.groupItemId, playbackTrackingTarget?.hostId, playbackTrackingTarget?.hostType, playbackTrackingTarget?.scopeKey]);

  useEffect(() => {
    clipEndedHandled.current = false;
  }, [clip?.end, clip?.loop, clip?.start, sceneId, streamUrl]);

  useEffect(() => {
    const v = videoRef.current;
    if (!v) return;
    v.volume = vol;
    v.muted = muted;
  }, []);

  useEffect(() => {
    if (onSeekRegister) {
      onSeekRegister((time: number) => {
        const v = videoRef.current;
        if (v) {
          v.currentTime = time;
          v.play().catch(() => {});
        }
      });
    }
  }, [onSeekRegister]);

  const updateVideoBox = useCallback(() => {
    const video = videoRef.current;
    const container = containerRef.current;
    if (!video || !container) {
      return;
    }

    const intrinsicWidth = video.videoWidth || video.clientWidth;
    const intrinsicHeight = video.videoHeight || video.clientHeight;
    const containerWidth = container.clientWidth;
    const containerHeight = container.clientHeight;

    if (!intrinsicWidth || !intrinsicHeight || !containerWidth || !containerHeight) {
      return;
    }

    const scale = Math.min(containerWidth / intrinsicWidth, containerHeight / intrinsicHeight);
    const width = intrinsicWidth * scale;
    const height = intrinsicHeight * scale;
    const left = (containerWidth - width) / 2;
    const top = (containerHeight - height) / 2;

    setVideoBox((current) => {
      if (
        Math.abs(current.left - left) < 0.5
        && Math.abs(current.top - top) < 0.5
        && Math.abs(current.width - width) < 0.5
        && Math.abs(current.height - height) < 0.5
      ) {
        return current;
      }

      return { left, top, width, height };
    });
  }, []);

  useEffect(() => {
    const container = containerRef.current;
    const video = videoRef.current;
    if (!container || !video) {
      return;
    }

    updateVideoBox();
    const resizeObserver = new ResizeObserver(() => updateVideoBox());
    resizeObserver.observe(container);
    resizeObserver.observe(video);
    window.addEventListener("resize", updateVideoBox);
    return () => {
      resizeObserver.disconnect();
      window.removeEventListener("resize", updateVideoBox);
    };
  }, [sceneId, selectedQuality, streamUrl, updateVideoBox]);

  const activeDetections = useMemo(() => {
    if (!detections.length) {
      return [];
    }

    const toleranceSec = 0.5;
    const byKey = new Map<string, Detection>();

    for (const detection of detections) {
      const observedAt = detection.observedAtSec;
      if (observedAt != null && Math.abs(observedAt - currentTime) > toleranceSec) {
        continue;
      }

      const key = detection.groupKey
        ?? `${detection.refKind ?? detection.class}:${detection.refId ?? detection.id}:${detection.class}`;
      const existing = byKey.get(key);
      if (!existing) {
        byKey.set(key, detection);
        continue;
      }

      const existingDelta = Math.abs((existing.observedAtSec ?? currentTime) - currentTime);
      const candidateDelta = Math.abs((detection.observedAtSec ?? currentTime) - currentTime);
      if (candidateDelta < existingDelta) {
        byKey.set(key, detection);
      }
    }

    return Array.from(byKey.values());
  }, [currentTime, detections]);

  const effectiveStreamUrl = selectedQuality === "Direct" ? streamUrl : scenes.transcodeUrl(sceneId, selectedQuality);

  useEffect(() => {
    const v = videoRef.current;
    const nextTime = clip ? clip.start : resumeTime;
    if (v && nextTime != null) {
      v.currentTime = nextTime;
      setCurTime(roundPlaybackTime(nextTime));
    }

    if (clip?.loop && clip.end != null) {
      setAbLoop({ a: clip.start, b: clip.end });
    } else if (clip) {
      setAbLoop({ a: null, b: null });
    }
  }, [clip?.end, clip?.loop, clip?.start, resumeTime, sceneId, streamUrl]);

  useEffect(() => {
    if (!autostart) {
      return;
    }

    pendingAutostartRef.current = true;
    const video = videoRef.current;
    const sourceSignature = `${effectiveStreamUrl}|${format || "mp4"}`;
    if (!video || lastLoadedSourceRef.current !== sourceSignature) {
      return;
    }

    video.play().catch(() => {});
  }, [autostart, autostartToken, effectiveStreamUrl, format]);

  useEffect(() => {
    const handler = () => setPip(document.pictureInPictureElement === videoRef.current);
    document.addEventListener("enterpictureinpicture", handler);
    document.addEventListener("leavepictureinpicture", handler);
    return () => {
      document.removeEventListener("enterpictureinpicture", handler);
      document.removeEventListener("leavepictureinpicture", handler);
    };
  }, []);

  useEffect(() => {
    const v = videoRef.current as (HTMLVideoElement & { webkitShowPlaybackTargetPicker?: () => void }) | null;
    if (!v) return;
    const onTargetChanged = () => {
      const savedTime = v.currentTime;
      setTimeout(() => {
        if (v.currentTime < savedTime - 1) v.currentTime = savedTime;
      }, 500);
    };
    v.addEventListener("webkitcurrentplaybacktargetchanged" as never, onTargetChanged as EventListener);
    return () => v.removeEventListener("webkitcurrentplaybacktargetchanged" as never, onTargetChanged as EventListener);
  }, []);

  useEffect(() => {
    if (abLoop.a == null || abLoop.b == null) return;
    const v = videoRef.current;
    if (!v) return;
    const handler = () => {
      if (v.currentTime >= abLoop.b!) {
        v.currentTime = abLoop.a!;
      }
    };
    v.addEventListener("timeupdate", handler);
    return () => v.removeEventListener("timeupdate", handler);
  }, [abLoop]);

  useEffect(() => {
    if (journalFlushed.current) {
      return;
    }

    journalFlushed.current = true;
    window.localStorage.removeItem("cove-scene-activity-journal");
  }, []);

  const flushInterval = useCallback((state: string, mode: "default" | "keepalive" = "default") => {
    const video = videoRef.current;
    if (!playbackTrackingTarget || !video || intervalStart.current === null) return;
    const startSec = intervalStart.current;
    const endSec = roundPlaybackTime(lastSeenTime.current);
    if (endSec <= startSec) return;
    playbackTracker.current.recordInterval({
      startSec,
      endSec,
      mediaDurationSec: video.duration || 0,
      currentPositionSec: endSec,
      state,
      mode,
    });
  }, [playbackTrackingTarget]);

  const flushIntervalKeepalive = useCallback((state: string) => {
    flushInterval(state, "keepalive");
  }, [flushInterval]);

  useEffect(() => {
    if (!clip) {
      return;
    }

    const video = videoRef.current;
    if (!video) {
      return;
    }

    const handleClipBoundary = () => {
      if (video.currentTime < clipStart) {
        video.currentTime = clipStart;
        setCurTime(roundPlaybackTime(clipStart));
        return;
      }

      if (video.currentTime < clipEnd - 0.05) {
        clipEndedHandled.current = false;
        return;
      }

      if (clip.loop) {
        video.currentTime = clipStart;
        setCurTime(roundPlaybackTime(clipStart));
        lastSeenTime.current = roundPlaybackTime(clipStart);
        if (intervalStart.current !== null) {
          flushInterval("active");
          intervalStart.current = clipStart;
        }
        return;
      }

      if (clipEndedHandled.current) {
        return;
      }

      clipEndedHandled.current = true;
      video.pause();
      video.currentTime = clipEnd;
      lastSeenTime.current = roundPlaybackTime(clipEnd);
      setCurTime(roundPlaybackTime(clipEnd));
      flushInterval("ended");
      intervalStart.current = null;
      setPlaying(false);
      onEndedProp?.();
    };

    video.addEventListener("timeupdate", handleClipBoundary);
    return () => {
      video.removeEventListener("timeupdate", handleClipBoundary);
    };
  }, [clip, clipEnd, clipStart, flushInterval, onEndedProp]);

  useEffect(() => {
    if (!playbackTrackingTarget) {
      return;
    }

    const handleVisibilityChange = () => {
      if (document.visibilityState === "hidden") {
        flushIntervalKeepalive("paused");
      }
    };
    const handlePageHide = () => flushIntervalKeepalive("paused");

    window.addEventListener("pagehide", handlePageHide);
    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      window.removeEventListener("pagehide", handlePageHide);
      document.removeEventListener("visibilitychange", handleVisibilityChange);
      flushIntervalKeepalive("paused");
    };
  }, [flushIntervalKeepalive, playbackTrackingTarget]);

  useEffect(() => {
    const handler = () => setFullscreen(!!document.fullscreenElement);
    document.addEventListener("fullscreenchange", handler);
    return () => document.removeEventListener("fullscreenchange", handler);
  }, []);

  const resetHideTimer = useCallback(() => {
    setShowControls(true);
    if (hideTimer.current) clearTimeout(hideTimer.current);
    hideTimer.current = setTimeout(() => {
      if (videoRef.current && !videoRef.current.paused) setShowControls(false);
    }, 3000);
  }, []);

  useEffect(() => {
    const v = videoRef.current;
    if (!v) return;
    for (let i = 0; i < v.textTracks.length; i++) {
      v.textTracks[i].mode = showCaptions ? "showing" : "hidden";
    }
  }, [showCaptions]);

  useEffect(() => {
    scenes.getResolutions(sceneId).then((res) => setAvailableQualities(res ?? [])).catch(() => {});
  }, [sceneId]);

  useEffect(() => {
    const handler = (event: KeyboardEvent) => {
      const v = videoRef.current;
      if (!v) return;
      const tag = (event.target as HTMLElement).tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;

      switch (event.key) {
        case " ":
        case "k":
          event.preventDefault();
          v.paused ? v.play() : v.pause();
          break;
        case "ArrowLeft":
          event.preventDefault();
          v.currentTime = Math.max(0, v.currentTime - (event.shiftKey ? 10 : 5));
          break;
        case "ArrowRight":
          event.preventDefault();
          v.currentTime = Math.min(v.duration, v.currentTime + (event.shiftKey ? 10 : 5));
          break;
        case "ArrowUp":
          event.preventDefault();
          v.volume = Math.min(1, v.volume + 0.1);
          setVol(v.volume);
          localStorage.setItem(VOLUME_KEY, String(v.volume));
          break;
        case "ArrowDown":
          event.preventDefault();
          v.volume = Math.max(0, v.volume - 0.1);
          setVol(v.volume);
          localStorage.setItem(VOLUME_KEY, String(v.volume));
          break;
        case "m":
          v.muted = !v.muted;
          setMuted(v.muted);
          localStorage.setItem(MUTED_KEY, String(v.muted));
          break;
        case "f":
          if (document.fullscreenElement) document.exitFullscreen();
          else containerRef.current?.requestFullscreen();
          break;
        case "0": case "1": case "2": case "3": case "4":
        case "5": case "6": case "7": case "8": case "9":
          event.preventDefault();
          v.currentTime = v.duration * (Number(event.key) / 10);
          break;
      }
      resetHideTimer();
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [resetHideTimer]);

  const togglePlay = () => {
    const v = videoRef.current;
    if (!v) return;
    v.paused ? v.play() : v.pause();
  };

  const seekTo = (event: React.MouseEvent<HTMLDivElement>) => {
    const v = videoRef.current;
    if (!v) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const pct = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
    v.currentTime = timelineStart + pct * timelineDuration;
  };

  const changeVolume = (event: React.MouseEvent<HTMLDivElement>) => {
    const v = videoRef.current;
    if (!v) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const pct = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
    v.volume = pct;
    v.muted = false;
    setVol(pct);
    setMuted(false);
    localStorage.setItem(VOLUME_KEY, String(pct));
    localStorage.setItem(MUTED_KEY, "false");
  };

  const toggleFullscreen = () => {
    if (document.fullscreenElement) document.exitFullscreen();
    else containerRef.current?.requestFullscreen();
  };

  const changeRate = (nextRate: number) => {
    const v = videoRef.current;
    if (v) v.playbackRate = nextRate;
    setRate(nextRate);
    setShowSpeed(false);
  };

  const changeQuality = (quality: string) => {
    const v = videoRef.current;
    const curTime = v?.currentTime ?? 0;
    const wasPlaying = v ? !v.paused : false;
    sourceRestoreRef.current = { time: curTime, shouldPlay: wasPlaying };
    setSelectedQuality(quality);
    setShowQuality(false);
  };

  useEffect(() => {
    const video = videoRef.current;
    if (!video) {
      return;
    }

    const sourceSignature = `${effectiveStreamUrl}|${format || "mp4"}`;
    if (lastLoadedSourceRef.current === sourceSignature) {
      return;
    }
    lastLoadedSourceRef.current = sourceSignature;

    const pendingRestore = sourceRestoreRef.current;
    sourceRestoreRef.current = null;
    const shouldAutoplayAfterLoad = pendingRestore?.shouldPlay || pendingAutostartRef.current;

    const handleLoadedMetadata = () => {
      const targetTime = pendingRestore?.time ?? (clip ? clip.start : resumeTime);
      if (targetTime != null && Number.isFinite(targetTime)) {
        video.currentTime = targetTime;
        setCurTime(roundPlaybackTime(targetTime));
      }

      if (shouldAutoplayAfterLoad) {
        pendingAutostartRef.current = false;
        video.play().catch(() => {});
      }
    };

    video.addEventListener("loadedmetadata", handleLoadedMetadata, { once: true });
    video.load();
    return () => {
      video.removeEventListener("loadedmetadata", handleLoadedMetadata);
    };
  }, [clip, effectiveStreamUrl, format, resumeTime]);

  const togglePip = async () => {
    const v = videoRef.current;
    if (!v) return;
    try {
      if (document.pictureInPictureElement) {
        await document.exitPictureInPicture();
      } else {
        await v.requestPictureInPicture();
      }
    } catch {
      // PiP not supported or denied.
    }
  };

  const cycleAbLoop = () => {
    const v = videoRef.current;
    if (!v) return;
    if (abLoop.a == null) {
      setAbLoop({ a: v.currentTime, b: null });
    } else if (abLoop.b == null) {
      setAbLoop({ a: abLoop.a, b: v.currentTime });
    } else {
      setAbLoop({ a: null, b: null });
    }
  };

  const fmtTime = (value: number) => {
    if (!isFinite(value)) return "0:00";
    const h = Math.floor(value / 3600);
    const m = Math.floor((value % 3600) / 60);
    const sec = Math.floor(value % 60);
    return h > 0 ? `${h}:${m.toString().padStart(2, "0")}:${sec.toString().padStart(2, "0")}` : `${m}:${sec.toString().padStart(2, "0")}`;
  };

  return (
    <div
      ref={containerRef}
      className="relative group w-full h-full flex items-center justify-center bg-black"
      onMouseMove={resetHideTimer}
      onMouseLeave={() => playing && setShowControls(false)}
    >
      <video
        ref={videoRef}
        className="w-full h-full object-contain cursor-pointer"
        preload="metadata"
        poster={posterUrl}
        {...({ "x-webkit-airplay": "allow" } as Record<string, string>)}
        onLoadedMetadata={updateVideoBox}
        onLoadedData={updateVideoBox}
        onClick={togglePlay}
        onDoubleClick={toggleFullscreen}
        onPlay={() => {
          setPlaying(true);
          pendingAutostartRef.current = false;
          const currentPos = roundPlaybackTime(videoRef.current?.currentTime ?? currentTime);
          intervalStart.current = currentPos;
          lastSeenTime.current = currentPos;
          if (!playTriggered.current) { playTriggered.current = true; onPlay(); }
        }}
        onPause={() => {
          setPlaying(false);
          flushInterval("paused");
          intervalStart.current = null;
        }}
        onSeeking={() => {
          if (intervalStart.current !== null) {
            flushInterval("active");
            intervalStart.current = null;
          }
        }}
        onSeeked={() => {
          const video = videoRef.current;
          if (video && !video.paused) {
            const time = roundPlaybackTime(video.currentTime);
            intervalStart.current = time;
            lastSeenTime.current = time;
          }
        }}
        onTimeUpdate={() => {
          const v = videoRef.current;
          const time = roundPlaybackTime(v?.currentTime ?? 0);
          setCurTime(time);
          onTimeUpdateProp?.(time);
          lastSeenTime.current = time;
          if (trackActivity && intervalStart.current !== null) {
            const now = Date.now();
            if (now - lastKeepaliveSentAt.current >= 10000) {
              lastKeepaliveSentAt.current = now;
              flushInterval("active");
              intervalStart.current = time;
            }
          }
        }}
        onProgress={() => {
          const v = videoRef.current;
          if (v && v.buffered.length > 0) setBuffered(v.buffered.end(v.buffered.length - 1));
        }}
        onEnded={() => {
          if (loop) {
            flushInterval("active");
            intervalStart.current = null;
            const v = videoRef.current;
            if (v) { v.currentTime = 0; v.play().catch(() => {}); }
            return;
          }
          setPlaying(false);
          flushInterval("ended");
          intervalStart.current = null;
          onEndedProp?.();
        }}
      >
        <source src={effectiveStreamUrl} type={`video/${format || "mp4"}`} />
        {captions?.map((cap, idx) => (
          <track
            key={cap.id}
            kind="captions"
            src={scenes.captionUrl(sceneId, cap.id)}
            srcLang={cap.languageCode === "00" ? "en" : cap.languageCode}
            label={cap.languageCode === "00" ? cap.filename : cap.languageCode.toUpperCase()}
            default={idx === 0 && showCaptions}
          />
        ))}
      </video>

      {activeDetections.length > 0 && videoBox.width > 0 && videoBox.height > 0 ? (
        <div className="pointer-events-none absolute inset-0 z-[2]">
          {activeDetections.map((detection) => {
            const left = videoBox.left + (detection.x / Math.max(detection.frameWidth, 1)) * videoBox.width;
            const top = videoBox.top + (detection.y / Math.max(detection.frameHeight, 1)) * videoBox.height;
            const width = (detection.w / Math.max(detection.frameWidth, 1)) * videoBox.width;
            const height = (detection.h / Math.max(detection.frameHeight, 1)) * videoBox.height;
            const color = detectionColor(detection.class);

            return (
              <div
                key={detection.id}
                className="absolute rounded-md border shadow-[0_0_0_1px_rgba(0,0,0,0.25)]"
                style={{
                  left,
                  top,
                  width,
                  height,
                  borderColor: color,
                  boxShadow: `0 0 0 1px ${color}55 inset`,
                  background: `${color}14`,
                }}
              >
                <span
                  className="absolute left-0 top-0 -translate-y-full rounded-sm px-1.5 py-0.5 text-[10px] font-medium uppercase tracking-wide text-white"
                  style={{ backgroundColor: color }}
                >
                  {formatDetectionBadge(detection)}
                </span>
              </div>
            );
          })}
        </div>
      ) : null}

      <div
        className={`absolute bottom-0 left-0 right-0 bg-gradient-to-t from-black/90 via-black/50 to-transparent transition-opacity ${
          showControls ? "opacity-100" : "opacity-0 pointer-events-none"
        }`}
        style={{ padding: "40px 0 0 0" }}
      >
        <div className="px-3">
          <div className="relative h-4 flex items-center cursor-pointer group/seek" onClick={seekTo}>
            <div className="w-full h-1 bg-white/20 rounded-full group-hover/seek:h-1.5 transition-all relative">
              <div className="absolute top-0 left-0 h-full bg-white/30 rounded-full" style={{ width: `${(visibleBuffered / timelineDuration) * 100}%` }} />
              <div className="absolute top-0 left-0 h-full bg-accent rounded-full" style={{ width: `${(visibleCurrentTime / timelineDuration) * 100}%` }} />
              {abLoop.a != null && (
                <div
                  className="absolute top-0 h-full bg-accent/25 pointer-events-none"
                  style={{
                    left: `${((abLoop.a - timelineStart) / timelineDuration) * 100}%`,
                    width: abLoop.b != null ? `${((abLoop.b - abLoop.a) / timelineDuration) * 100}%` : "2px",
                  }}
                />
              )}
            </div>
            <div
              className="absolute top-1/2 -translate-y-1/2 w-3 h-3 bg-accent rounded-full opacity-0 group-hover/seek:opacity-100 transition-opacity"
              style={{ left: `${(visibleCurrentTime / timelineDuration) * 100}%`, transform: "translate(-50%, -50%)" }}
            />
          </div>
        </div>

        <div className="flex items-center gap-2 px-3 py-2 text-white">
          {onPrev && (
            <button onClick={onPrev} className="hover:text-accent p-1" title="Previous scene">
              <SkipBack className="w-4 h-4 fill-current" />
            </button>
          )}

          <button onClick={togglePlay} className="hover:text-accent p-1">
            {playing ? <Pause className="w-5 h-5" /> : <Play className="w-5 h-5" />}
          </button>

          {onNext && (
            <button onClick={onNext} className="hover:text-accent p-1" title="Next scene">
              <SkipForward className="w-4 h-4 fill-current" />
            </button>
          )}

          <button onClick={() => { const v = videoRef.current; if (v) v.currentTime = Math.max(0, v.currentTime - 10); }} className="hover:text-accent p-1" title="Back 10s">
            <SkipBack className="w-4 h-4" />
          </button>
          <button onClick={() => { const v = videoRef.current; if (v) v.currentTime = Math.min(v.duration, v.currentTime + 10); }} className="hover:text-accent p-1" title="Forward 10s">
            <SkipForward className="w-4 h-4" />
          </button>

          <button onClick={() => {
            const v = videoRef.current;
            if (!v) return;
            v.muted = !v.muted;
            setMuted(v.muted);
            localStorage.setItem(MUTED_KEY, String(v.muted));
          }} className="hover:text-accent p-1">
            {muted || vol === 0 ? <VolumeX className="w-4 h-4" /> : <Volume2 className="w-4 h-4" />}
          </button>
          <div className="w-20 h-3 flex items-center cursor-pointer group/vol" onClick={changeVolume}>
            <div className="w-full h-1 bg-white/20 rounded-full relative">
              <div className="absolute top-0 left-0 h-full bg-white rounded-full" style={{ width: `${(muted ? 0 : vol) * 100}%` }} />
            </div>
          </div>

          <span className="text-xs text-white/70 ml-1 select-none tabular-nums">
            {fmtTime(visibleCurrentTime)} / {fmtTime(clip ? clipEnd - clipStart : duration)}
          </span>

          <div className="ml-auto flex items-center gap-2">
            <div className="relative">
              <button
                onClick={() => setShowSpeed(!showSpeed)}
                className={`hover:text-accent p-1 text-xs font-medium flex items-center gap-1 ${rate !== 1 ? "text-accent" : ""}`}
              >
                {rate}x
              </button>
              {showSpeed && (
                <div className="absolute bottom-full right-0 mb-2 bg-surface border border-border rounded shadow-lg py-1 z-10">
                  {PLAYBACK_RATES.map((playbackRate) => (
                    <button
                      key={playbackRate}
                      onClick={() => changeRate(playbackRate)}
                      className={`block w-full text-left px-4 py-1 text-sm hover:bg-card ${playbackRate === rate ? "text-accent" : "text-white"}`}
                    >
                      {playbackRate}x
                    </button>
                  ))}
                </div>
              )}
            </div>

            {showAbLoop && (
              <button
                onClick={cycleAbLoop}
                className={`hover:text-accent p-1 text-xs font-medium flex items-center gap-1 ${abLoop.a != null ? "text-accent" : ""}`}
                title={abLoop.a == null ? "Set loop start (A)" : abLoop.b == null ? "Set loop end (B)" : "Clear A-B loop"}
              >
                <Repeat className="w-4 h-4" />
                {abLoop.a != null && abLoop.b == null && "A"}
                {abLoop.a != null && abLoop.b != null && "A-B"}
              </button>
            )}

            {availableQualities.length > 0 && (
              <div className="relative">
                <button
                  onClick={() => setShowQuality(!showQuality)}
                  className={`hover:text-accent p-1 text-xs font-medium ${selectedQuality !== "Direct" ? "text-accent" : ""}`}
                  title="Video quality"
                >
                  {selectedQuality === "Direct" ? "Direct" : selectedQuality}
                </button>
                {showQuality && (
                  <div className="absolute bottom-full right-0 mb-2 bg-surface border border-border rounded shadow-lg py-1 z-10">
                    <button
                      onClick={() => changeQuality("Direct")}
                      className={`block w-full text-left px-4 py-1 text-sm hover:bg-card ${selectedQuality === "Direct" ? "text-accent" : "text-white"}`}
                    >
                      Direct
                    </button>
                    {availableQualities.map((quality) => (
                      <button
                        key={quality}
                        onClick={() => changeQuality(quality)}
                        className={`block w-full text-left px-4 py-1 text-sm hover:bg-card ${quality === selectedQuality ? "text-accent" : "text-white"}`}
                      >
                        {quality}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            )}

            <button
              onClick={() => setLoop(!loop)}
              className={`hover:text-accent p-1 ${loop ? "text-accent" : ""}`}
              title={loop ? "Disable loop" : "Loop video"}
            >
              <Repeat1 className="w-4 h-4" />
            </button>

            <button onClick={togglePip} className={`hover:text-accent p-1 ${pip ? "text-accent" : ""}`} title="Picture-in-Picture">
              <PictureInPicture2 className="w-4 h-4" />
            </button>

            {captions && captions.length > 0 && (
              <button
                onClick={() => setShowCaptions((prev) => !prev)}
                className={`hover:text-accent p-1 ${showCaptions ? "text-accent" : ""}`}
                title={showCaptions ? "Hide captions" : "Show captions"}
              >
                <Subtitles className="w-4 h-4" />
              </button>
            )}

            <button onClick={toggleFullscreen} className="hover:text-accent p-1">
              {fullscreen ? <Minimize className="w-4 h-4" /> : <Maximize className="w-4 h-4" />}
            </button>
          </div>
        </div>
      </div>

      {!playing && (
        <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
          <div className="bg-black/40 rounded-full p-4">
            <Play className="w-12 h-12 text-white" />
          </div>
        </div>
      )}
    </div>
  );
}

function detectionColor(className: string) {
  const normalized = className.trim().toLowerCase();
  if (normalized === "face") return "#22c55e";
  if (normalized === "person" || normalized === "body") return "#38bdf8";
  if (normalized === "hand") return "#f59e0b";
  if (normalized === "text") return "#a855f7";

  let hash = 0;
  for (let index = 0; index < normalized.length; index += 1) {
    hash = ((hash << 5) - hash) + normalized.charCodeAt(index);
    hash |= 0;
  }

  const hue = Math.abs(hash) % 360;
  return `hsl(${hue} 80% 55%)`;
}

function formatDetectionBadge(detection: Detection) {
  const confidence = Math.round(detection.score * 100);
  const refText = detection.refKind && detection.refId != null
    ? ` · ${detection.refKind} #${detection.refId}`
    : "";
  return `${detection.class} ${confidence}%${refText}`;
}