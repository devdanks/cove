import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ExternalLink, Loader2, Search, X } from "lucide-react";
import { scrapeAttempts, system } from "../api/client";
import type { Image } from "../api/types";
import { useAppConfig } from "../state/AppConfigContext";
import { getImageDisplayTitle } from "../utils/imageDisplay";
import type { BatchInputKind, ScrapeApplyPreferences } from "./sceneScrapeUtils";
import {
  findDefaultKind,
  findPreferredScraperId,
  loadScrapeApplyPreferences,
  saveScrapeApplyPreferences,
  supportsScrapeKind,
} from "./sceneScrapeUtils";

interface Props {
  open: boolean;
  onClose: () => void;
  images: Image[];
}

type BatchStatus = "pending" | "queued" | "skipped" | "failure";

interface BatchResult {
  imageId: number;
  label: string;
  status: BatchStatus;
  message: string;
}

function statusTone(status: BatchStatus) {
  switch (status) {
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

function getImageNameSearchInput(image: Image) {
  return getImageDisplayTitle(image);
}

export function ImageBatchScrapeDialog({ open, onClose, images }: Props) {
  const queryClient = useQueryClient();
  const { config } = useAppConfig();
  const [preferences, setPreferences] = useState<ScrapeApplyPreferences>(() => loadScrapeApplyPreferences());
  const [selectedScraperId, setSelectedScraperId] = useState("");
  const [inputKind, setInputKind] = useState<BatchInputKind>("url");
  const [autoApply, setAutoApply] = useState(true);
  const [results, setResults] = useState<BatchResult[]>([]);
  const [error, setError] = useState<string | null>(null);

  const { data: scrapers = [] } = useQuery({
    queryKey: ["system-scrapers"],
    queryFn: system.listScrapers,
    enabled: open,
  });

  const scraperPreferences = config?.scraping.scraperPreferences ?? [];
  const imageScrapers = useMemo(
    () => scrapers
      .filter((scraper) => scraper.entityType.toLowerCase() === "image")
      .sort((left, right) => left.name.localeCompare(right.name)),
    [scrapers],
  );
  const selectedScraper = useMemo(
    () => imageScrapers.find((scraper) => scraper.id === selectedScraperId),
    [imageScrapers, selectedScraperId],
  );
  const imageIdsKey = useMemo(() => images.map((image) => image.id).join(","), [images]);

  useEffect(() => {
    if (!open) return;
    setPreferences(loadScrapeApplyPreferences());
  }, [open]);

  useEffect(() => {
    saveScrapeApplyPreferences(preferences);
  }, [preferences]);

  useEffect(() => {
    if (!open) return;
    setResults([]);
    setAutoApply(true);
    setError(null);
  }, [open, imageIdsKey]);

  useEffect(() => {
    if (!open) return;

    if (!selectedScraperId || !imageScrapers.some((scraper) => scraper.id === selectedScraperId)) {
      setSelectedScraperId(findPreferredScraperId(imageScrapers, images[0]?.urls[0], scraperPreferences));
    }
  }, [open, imageScrapers, images, scraperPreferences, selectedScraperId]);

  useEffect(() => {
    if (!selectedScraper) return;
    setInputKind((current) => {
      const next = findDefaultKind(selectedScraper, current);
      return next === "fragment" ? "url" : next;
    });
  }, [selectedScraper]);

  const runMutation = useMutation({
    mutationFn: async () => {
      if (!selectedScraper) {
        throw new Error("Select a scraper first.");
      }

      if (images.length === 0) {
        throw new Error("Select at least one image to batch scrape.");
      }

      return scrapeAttempts.startImageBatch({
        scraperId: selectedScraper.id,
        inputKind,
        imageIds: images.map((image) => image.id),
        autoApply,
        createMissingTags: preferences.createMissingTags,
        createMissingPerformers: preferences.createMissingPerformers,
        createMissingStudio: preferences.createMissingStudio,
        markOrganized: preferences.markOrganized,
      });
    },
    onSuccess: async ({ jobId }) => {
      setResults(images.map((image) => ({
        imageId: image.id,
        label: getImageDisplayTitle(image),
        status: "queued",
        message: `Queued in job ${jobId}. Track progress in Jobs.`,
      })));

      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["jobs"] }),
        queryClient.invalidateQueries({ queryKey: ["scrape-attempts"] }),
        queryClient.invalidateQueries({ queryKey: ["images"] }),
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

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
      <div className="flex max-h-[92vh] w-full max-w-6xl flex-col overflow-hidden rounded-[28px] border border-border bg-surface shadow-2xl">
        <div className="flex items-start justify-between border-b border-border px-5 py-4">
          <div>
            <h2 className="flex items-center gap-2 text-lg font-bold text-foreground">
              <Search className="h-5 w-5 text-accent" />
              Batch Image Scrape
            </h2>
            <p className="mt-0.5 text-xs text-secondary">
              Run one scraper across {images.length} selected image{images.length === 1 ? "" : "s"} and optionally apply the default review plan automatically.
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
                <div className="mt-1 text-2xl font-semibold text-foreground">{images.length}</div>
                <div className="mt-1 text-xs text-secondary">Selected images ready for batch scraping.</div>
              </div>

              <div className="space-y-2">
                <label className="block text-sm font-medium text-foreground">Scraper</label>
                <select
                  value={selectedScraperId}
                  onChange={(event) => setSelectedScraperId(event.target.value)}
                  className="w-full rounded-xl border border-border bg-card px-3 py-2 text-sm text-foreground outline-none"
                >
                  {imageScrapers.length === 0 ? <option value="">No image scrapers found</option> : null}
                  {imageScrapers.map((scraper) => (
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
                      Uses the same default field-selection logic as the single-image scrape review.
                    </span>
                  </span>
                </label>
                {inputKind === "name" ? (
                  <div className="rounded-xl border border-border bg-surface px-3 py-3 text-xs text-secondary">
                    Image titles are searched with each image title or file name. Example query: <span className="text-foreground">{images[0] ? getImageNameSearchInput(images[0]) : ""}</span>
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
                  <div className="text-xs text-secondary">{completedCount} of {images.length} image{images.length === 1 ? "" : "s"} processed.</div>
                </div>
                <div className="text-xs text-muted">
                  {autoApply ? "Scrapes will be applied with default field choices." : "Scrapes will be queued for later review."}
                </div>
              </div>

              {results.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-border bg-card/40 p-4">
                  <div className="text-sm font-medium text-foreground">Selected Images</div>
                  <div className="mt-3 space-y-2">
                    {images.map((image) => (
                      <div key={image.id} className="rounded-xl border border-border bg-card px-3 py-2 text-sm text-secondary">
                        {getImageDisplayTitle(image)}
                      </div>
                    ))}
                  </div>
                </div>
              ) : (
                <div className="space-y-2">
                  {results.map((result) => (
                    <div key={result.imageId} className="rounded-2xl border border-border bg-card p-4">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <div className="text-sm font-medium text-foreground">{result.label}</div>
                          <div className="mt-1 text-xs text-secondary">{result.message}</div>
                        </div>
                        <span className={`rounded-full border px-2.5 py-1 text-xs capitalize ${statusTone(result.status)}`}>
                          {result.status}
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
              runMutation.mutate();
            }}
            disabled={!canRun || runMutation.isPending || images.length === 0}
            className="inline-flex items-center gap-2 rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-60"
          >
            {runMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <ExternalLink className="h-4 w-4" />}
            {autoApply ? "Queue Scrape And Apply" : "Queue Scrape"}
          </button>
        </div>
      </div>
    </div>
  );
}