import { useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ExternalLink, Loader2, Search, X } from "lucide-react";
import { scrapeAttempts, system } from "../api/client";
import { useAppConfig } from "../state/AppConfigContext";
import { VideoScrapeDialog } from "./VideoScrapeDialog";
import type { BatchInputKind, ScrapeApplyPreferences, VideoScrapeVideo } from "./videoScrapeUtils";
import {
  findDefaultKind,
  findPreferredScraperId,
  getVideoLabel,
  getVideoNameSearchInput,
  loadScrapeApplyPreferences,
  saveScrapeApplyPreferences,
  sortScrapersForVideo,
  supportsScrapeKind,
} from "./videoScrapeUtils";

interface Props {
  open: boolean;
  onClose: () => void;
  videos: VideoScrapeVideo[];
}

type BatchStatus = "pending" | "queued" | "scraped" | "applied" | "appliedpartial" | "skipped" | "failure";

interface BatchResult {
  videoId: number;
  label: string;
  status: BatchStatus;
  message: string;
}

function statusTone(status: BatchStatus) {
  switch (status) {
    case "applied":
      return "border-emerald-800/60 bg-emerald-950/30 text-emerald-300";
    case "appliedpartial":
      return "border-amber-800/60 bg-amber-950/30 text-amber-300";
    case "scraped":
    case "queued":
      return "border-cyan-800/60 bg-cyan-950/30 text-cyan-300";
    case "pending":
      return "border-border bg-card/60 text-secondary";
    case "skipped":
      return "border-border bg-card/60 text-muted";
    default:
      return "border-red-800/60 bg-red-950/30 text-red-300";
  }
}

export function VideoBatchScrapeDialog({ open, onClose, videos }: Props) {
  const queryClient = useQueryClient();
  const { config } = useAppConfig();
  const [preferences, setPreferences] = useState<ScrapeApplyPreferences>(() => loadScrapeApplyPreferences());
  const [selectedScraperId, setSelectedScraperId] = useState("");
  const [inputKind, setInputKind] = useState<BatchInputKind>("url");
  const [autoApply, setAutoApply] = useState(true);
  const [results, setResults] = useState<BatchResult[]>([]);
  const [reviewIndex, setReviewIndex] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const reviewAppliedRef = useRef(false);

  const { data: scrapers = [] } = useQuery({
    queryKey: ["system-scrapers"],
    queryFn: system.listScrapers,
    enabled: open,
  });

  const scraperPreferences = config?.scraping.scraperPreferences ?? [];

  const videoScrapers = useMemo(
    () => sortScrapersForVideo(scrapers.filter((scraper) => scraper.entityType.toLowerCase() === "video"), videos[0]?.urls[0], scraperPreferences),
    [videos, scraperPreferences, scrapers],
  );

  const selectedScraper = useMemo(
    () => videoScrapers.find((scraper) => scraper.id === selectedScraperId),
    [videoScrapers, selectedScraperId],
  );
  const videoIdsKey = useMemo(() => videos.map((video) => video.id).join(","), [videos]);

  useEffect(() => {
    if (!open) {
      return;
    }

    setPreferences(loadScrapeApplyPreferences());
  }, [open]);

  useEffect(() => {
    saveScrapeApplyPreferences(preferences);
  }, [preferences]);

  useEffect(() => {
    if (!open) {
      return;
    }

    setResults([]);
    setReviewIndex(null);
    setAutoApply(true);
    setError(null);
  }, [open, videoIdsKey]);

  useEffect(() => {
    if (!open) {
      return;
    }

    if (!selectedScraperId || !videoScrapers.some((scraper) => scraper.id === selectedScraperId)) {
      setSelectedScraperId(findPreferredScraperId(videoScrapers, videos[0]?.urls[0], scraperPreferences));
    }
  }, [open, videoScrapers, videos, scraperPreferences, selectedScraperId]);

  useEffect(() => {
    if (!selectedScraper) {
      return;
    }

    setInputKind((current) => {
      const next = findDefaultKind(selectedScraper, current);
      return next === "fragment" ? "url" : next;
    });
  }, [selectedScraper]);

  const updateResult = (videoId: number, patch: Partial<BatchResult>) => {
    setResults((current) => current.map((result) => (result.videoId === videoId ? { ...result, ...patch } : result)));
  };

  const startReviewFlow = () => {
    if (!selectedScraper) {
      setError("Select a scraper first.");
      return;
    }

    if (videos.length === 0) {
      setError("Select at least one video to batch scrape.");
      return;
    }

    reviewAppliedRef.current = false;
    setResults(videos.map((video, index) => ({
      videoId: video.id,
      label: getVideoLabel(video),
      status: index === 0 ? "queued" : "pending",
      message: index === 0 ? "Opening review dialog." : "Waiting for previous reviews.",
    })));
    setReviewIndex(0);
  };

  const advanceReviewFlow = () => {
    if (reviewIndex == null) {
      return;
    }

    const video = videos[reviewIndex];
    if (video && !reviewAppliedRef.current) {
      updateResult(video.id, { status: "skipped", message: "Review closed without applying changes." });
    }

    reviewAppliedRef.current = false;
    const nextIndex = reviewIndex + 1;
    if (nextIndex < videos.length) {
      const nextVideo = videos[nextIndex];
      updateResult(nextVideo.id, { status: "queued", message: "Opening review dialog." });
      setReviewIndex(nextIndex);
    } else {
      setReviewIndex(null);
    }
  };

  const runMutation = useMutation({
    mutationFn: async () => {
      if (!selectedScraper) {
        throw new Error("Select a scraper first.");
      }

      if (videos.length === 0) {
        throw new Error("Select at least one video to batch scrape.");
      }

      return scrapeAttempts.startVideoBatch({
        scraperId: selectedScraper.id,
        inputKind,
        videoIds: videos.map((video) => video.id),
        autoApply,
        createMissingTags: preferences.createMissingTags,
        createMissingPerformers: preferences.createMissingPerformers,
        createMissingStudio: preferences.createMissingStudio,
        markOrganized: preferences.markOrganized,
        hydratePerformers: preferences.hydratePerformers,
      });
    },
    onSuccess: async ({ jobId }) => {
      setResults(videos.map((video) => ({
        videoId: video.id,
        label: getVideoLabel(video),
        status: "queued",
        message: `Queued in job ${jobId}. Track progress in Jobs.`,
      })));

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["jobs"] }),
        queryClient.invalidateQueries({ queryKey: ["scrape-attempts"] }),
      ]);
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message || "Batch scrape failed.");
    },
  });

  if (!open) {
    return null;
  }

  const canRun = Boolean(selectedScraper) && supportsScrapeKind(selectedScraper, inputKind);
  const completedCount = results.filter((result) => result.status !== "pending").length;
  const reviewVideo = reviewIndex != null ? videos[reviewIndex] : null;
  const reviewAutoRunKey = reviewVideo ? `${selectedScraperId}:${inputKind}:${reviewVideo.id}:${reviewIndex}` : null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
      <div className="flex max-h-[92vh] w-full max-w-6xl flex-col overflow-hidden rounded-[28px] border border-border bg-surface shadow-2xl">
        <div className="flex items-start justify-between border-b border-border px-5 py-4">
          <div>
            <h2 className="flex items-center gap-2 text-lg font-bold text-foreground">
              <Search className="h-5 w-5 text-accent" />
              Batch Video Scrape
            </h2>
            <p className="mt-0.5 text-xs text-secondary">
              Run one scraper across {videos.length} selected video{videos.length === 1 ? "" : "s"} and optionally apply the default review plan automatically.
            </p>
          </div>
          <button onClick={onClose} className="text-muted hover:text-foreground" aria-label="Close batch scrape dialog">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="grid min-h-0 flex-1 gap-0 lg:grid-cols-[340px_minmax(0,1fr)]">
          <div className="overflow-y-auto border-b border-border bg-card/40 p-4 lg:border-b-0 lg:border-r">
            <div className="space-y-4">
              <div className="rounded-2xl border border-border bg-card p-4">
                <div className="text-xs uppercase tracking-[0.18em] text-muted">Selection</div>
                <div className="mt-1 text-2xl font-semibold text-foreground">{videos.length}</div>
                <div className="mt-1 text-xs text-secondary">Selected videos ready for batch scraping.</div>
              </div>

              <div className="space-y-2">
                <label className="block text-sm font-medium text-foreground">Scraper</label>
                <select
                  value={selectedScraperId}
                  onChange={(event) => setSelectedScraperId(event.target.value)}
                  className="w-full rounded-xl border border-border bg-card px-3 py-2 text-sm text-foreground outline-none"
                >
                  {videoScrapers.length === 0 ? <option value="">No video scrapers found</option> : null}
                  {videoScrapers.map((scraper) => (
                    <option key={scraper.id} value={scraper.id}>
                      {scraper.name}
                    </option>
                  ))}
                </select>
                {selectedScraper ? <p className="text-xs text-muted">Supports: {selectedScraper.supportedScrapes.join(", ")}</p> : null}
              </div>

              <div className="space-y-2 rounded-2xl border border-border bg-card p-4">
                <label className="block text-sm font-medium text-foreground">Input</label>
                <div className="grid grid-cols-2 gap-2">
                  {(["url", "name"] as BatchInputKind[]).map((value) => {
                    const supported = supportsScrapeKind(selectedScraper, value);
                    return (
                      <button
                        key={value}
                        onClick={() => setInputKind(value)}
                        disabled={!supported}
                        className={`rounded-xl border px-3 py-2 text-sm capitalize transition-colors ${
                          inputKind === value
                            ? "border-accent bg-accent/10 text-accent"
                            : "border-border bg-surface text-secondary hover:text-foreground"
                        } disabled:cursor-not-allowed disabled:opacity-40`}
                      >
                        {value}
                      </button>
                    );
                  })}
                </div>
                <label className="flex items-start gap-3 rounded-xl border border-border bg-surface px-3 py-3 text-sm text-foreground">
                  <input
                    type="checkbox"
                    checked={autoApply}
                    onChange={(event) => setAutoApply(event.target.checked)}
                    className="mt-0.5 h-4 w-4 rounded border-border bg-card text-accent focus:ring-0"
                  />
                  <span>
                    <span className="block font-medium">Auto-apply default review choices</span>
                    <span className="mt-1 block text-xs text-secondary">
                      Uses the same default field-selection logic as the single-video scrape review.
                    </span>
                  </span>
                </label>
                {inputKind === "name" ? (
                  <div className="rounded-xl border border-border bg-surface px-3 py-3 text-xs text-secondary">
                    Video titles are searched with a cleaned version of each title. Example query: <span className="text-foreground">{videos[0] ? getVideoNameSearchInput(videos[0]) : ""}</span>
                  </div>
                ) : null}
              </div>

              <div className="grid gap-3 rounded-2xl border border-border bg-card p-4">
                <label className="flex items-center gap-2 text-sm text-secondary">
                  <input
                    type="checkbox"
                    checked={preferences.createMissingStudio}
                    onChange={(event) => setPreferences((current) => ({ ...current, createMissingStudio: event.target.checked }))}
                    className="h-4 w-4 rounded border-border bg-card text-accent focus:ring-0"
                  />
                  Create missing studio
                </label>
                <label className="flex items-center gap-2 text-sm text-secondary">
                  <input
                    type="checkbox"
                    checked={preferences.createMissingTags}
                    onChange={(event) => setPreferences((current) => ({ ...current, createMissingTags: event.target.checked }))}
                    className="h-4 w-4 rounded border-border bg-card text-accent focus:ring-0"
                  />
                  Create missing tags
                </label>
                <label className="flex items-center gap-2 text-sm text-secondary">
                  <input
                    type="checkbox"
                    checked={preferences.createMissingPerformers}
                    onChange={(event) => setPreferences((current) => ({ ...current, createMissingPerformers: event.target.checked }))}
                    className="h-4 w-4 rounded border-border bg-card text-accent focus:ring-0"
                  />
                  Create missing performers
                </label>
                <label className="flex items-center gap-2 text-sm text-secondary">
                  <input
                    type="checkbox"
                    checked={preferences.markOrganized}
                    onChange={(event) => setPreferences((current) => ({ ...current, markOrganized: event.target.checked }))}
                    className="h-4 w-4 rounded border-border bg-card text-accent focus:ring-0"
                  />
                  Mark organized after apply
                </label>
                <label className="flex items-center gap-2 text-sm text-secondary">
                  <input
                    type="checkbox"
                    checked={preferences.hydratePerformers}
                    onChange={(event) => setPreferences((current) => ({ ...current, hydratePerformers: event.target.checked }))}
                    className="h-4 w-4 rounded border-border bg-card text-accent focus:ring-0"
                  />
                  Scrape matched performers from performer URLs
                </label>
              </div>
            </div>
          </div>

          <div className="min-h-0 overflow-y-auto p-5">
            {error ? (
              <div className="mb-4 rounded-xl border border-red-800/60 bg-red-950/30 px-3 py-2 text-sm text-red-300">
                {error}
              </div>
            ) : null}

            <div className="space-y-4">
              <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-card/70 px-4 py-3">
                <div>
                  <div className="text-sm font-semibold text-foreground">Run Progress</div>
                  <div className="text-xs text-secondary">{completedCount} of {videos.length} video{videos.length === 1 ? "" : "s"} processed.</div>
                </div>
                <div className="text-xs text-muted">
                  {autoApply ? "Scrapes will be applied with default field choices." : "Each scrape opens in review before the next video starts."}
                </div>
              </div>

              {results.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-border bg-card/40 p-4">
                  <div className="text-sm font-medium text-foreground">Selected Videos</div>
                  <div className="mt-3 space-y-2">
                    {videos.map((video) => (
                      <div key={video.id} className="rounded-xl border border-border bg-card px-3 py-2 text-sm text-secondary">
                        {getVideoLabel(video)}
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="space-y-2">
                  {results.map((result) => (
                    <div key={result.videoId} className="rounded-2xl border border-border bg-card p-4">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="text-sm font-medium text-foreground">{result.label}</div>
                          <div className="mt-1 text-xs text-secondary">{result.message}</div>
                        </div>
                        <span className={`rounded-full border px-2.5 py-1 text-xs capitalize ${statusTone(result.status)}`}>
                          {result.status === "appliedpartial" ? "applied partial" : result.status}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>

        <div className="flex items-center justify-end gap-2 border-t border-border px-5 py-4">
          <button onClick={onClose} className="rounded-xl px-4 py-2 text-sm text-secondary hover:text-foreground">
            Close
          </button>
          <button
            onClick={() => {
              setError(null);
              if (autoApply) {
                runMutation.mutate();
              } else {
                startReviewFlow();
              }
            }}
            disabled={!canRun || runMutation.isPending || videos.length === 0 || reviewIndex != null}
            className="inline-flex items-center gap-2 rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-60"
          >
            {runMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <ExternalLink className="h-4 w-4" />}
            {autoApply ? "Queue Scrape And Apply" : "Scrape And Review"}
          </button>
        </div>
      </div>
      {reviewVideo ? (
        <VideoScrapeDialog
          key={reviewAutoRunKey ?? reviewVideo.id}
          open
          video={reviewVideo}
          initialScraperId={selectedScraperId}
          initialInputKind={inputKind}
          autoRunKey={reviewAutoRunKey ?? undefined}
          onApplied={() => {
            reviewAppliedRef.current = true;
            updateResult(reviewVideo.id, { status: "applied", message: "Reviewed and applied selected fields." });
          }}
          onClose={advanceReviewFlow}
        />
      ) : null}
    </div>
  );
}
