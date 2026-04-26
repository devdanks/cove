import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Loader2, Search, X } from "lucide-react";
import { performers, system } from "../api/client";
import type { Performer, PerformerScrapePreview, ScraperSummary } from "../api/types";
import { useAppConfig } from "../state/AppConfigContext";
import { findPreferredScraperId, sortScrapersForScene } from "./sceneScrapeUtils";

interface Props {
  open: boolean;
  onClose: () => void;
  performer: Pick<Performer, "id" | "name" | "urls">;
}

type InputKind = "url" | "name";

export function PerformerScrapeDialog({ open, onClose, performer }: Props) {
  const queryClient = useQueryClient();
  const { config } = useAppConfig();
  const [selectedScraperId, setSelectedScraperId] = useState("");
  const [inputKind, setInputKind] = useState<InputKind>(performer.urls.length > 0 ? "url" : "name");
  const [url, setUrl] = useState("");
  const [name, setName] = useState("");
  const [createMissingTags, setCreateMissingTags] = useState(true);
  const [preview, setPreview] = useState<PerformerScrapePreview | null>(null);
  const [error, setError] = useState<string | null>(null);

  const { data: scrapers = [] } = useQuery({
    queryKey: ["system-scrapers"],
    queryFn: system.listScrapers,
    enabled: open,
  });

  const scraperPreferences = config?.scraping.scraperPreferences ?? [];
  const performerScrapers = useMemo(
    () => sortScrapersForScene(scrapers.filter((scraper) => scraper.entityType.toLowerCase() === "performer"), performer.urls[0], scraperPreferences),
    [performer.urls, scraperPreferences, scrapers],
  );

  const selectedScraper = useMemo(
    () => performerScrapers.find((scraper) => scraper.id === selectedScraperId),
    [performerScrapers, selectedScraperId],
  );

  useEffect(() => {
    if (!open) {
      return;
    }

    setUrl(performer.urls[0] ?? "");
    setName(performer.name ?? "");
    setCreateMissingTags(true);
    setPreview(null);
    setError(null);
  }, [open, performer.name, performer.urls]);

  useEffect(() => {
    if (!open) {
      return;
    }

    if (!selectedScraperId || !performerScrapers.some((scraper) => scraper.id === selectedScraperId)) {
      setSelectedScraperId(findPreferredScraperId(performerScrapers, performer.urls[0], scraperPreferences));
    }
  }, [open, performer.urls, performerScrapers, scraperPreferences, selectedScraperId]);

  const previewMutation = useMutation({
    mutationFn: async () => {
      const trimmedUrl = url.trim();
      const trimmedName = name.trim();

      if (inputKind === "url" && !trimmedUrl) {
        throw new Error("Enter a performer URL to scrape.");
      }

      if (inputKind === "name" && !trimmedName) {
        throw new Error("Enter a performer name to scrape.");
      }

      return performers.previewScrape(performer.id, {
        inputKind,
        scraperId: selectedScraperId || undefined,
        url: inputKind === "url" ? trimmedUrl : undefined,
        name: inputKind === "name" ? trimmedName : undefined,
        createMissingTags,
      });
    },
    onSuccess: (result) => {
      setPreview(result);
      setError(null);
    },
    onError: (mutationError: Error) => {
      setPreview(null);
      setError(mutationError.message || "Failed to scrape performer metadata.");
    },
  });

  const applyMutation = useMutation({
    mutationFn: async () => {
      if (!preview) {
        throw new Error("Scrape a performer preview before applying it.");
      }

      return performers.applyScraped(performer.id, {
        scraped: preview.scraped,
        createMissingTags,
      });
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ["performer", performer.id] }),
        queryClient.invalidateQueries({ queryKey: ["performers"] }),
      ]);
      onClose();
    },
    onError: (mutationError: Error) => {
      setError(mutationError.message || "Failed to apply performer metadata.");
    },
  });

  if (!open) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-4">
      <div className="flex w-full max-w-2xl flex-col overflow-hidden rounded-[28px] border border-border bg-surface shadow-2xl">
        <div className="flex items-start justify-between border-b border-border px-5 py-4">
          <div>
            <h2 className="flex items-center gap-2 text-lg font-bold text-foreground">
              <Search className="h-5 w-5 text-accent" />
              Scrape Performer
            </h2>
            <p className="mt-0.5 text-xs text-secondary">
              Scrape performer metadata by URL or name, review the result, then choose whether to apply it to {performer.name}.
            </p>
          </div>
          <button onClick={onClose} className="text-muted hover:text-foreground" aria-label="Close performer scrape dialog">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="space-y-4 p-5">
          {error ? (
            <div className="rounded-xl border border-red-800/60 bg-red-950/30 px-3 py-2 text-sm text-red-300">
              {error}
            </div>
          ) : null}

          <div className="space-y-2">
            <label className="block text-sm font-medium text-foreground">Scraper</label>
            <select
              value={selectedScraperId}
              onChange={(event) => setSelectedScraperId(event.target.value)}
              className="w-full rounded-xl border border-border bg-card px-3 py-2 text-sm text-foreground outline-none"
            >
              <option value="">Auto / Best match</option>
              {performerScrapers.map((scraper) => (
                <option key={scraper.id} value={scraper.id}>
                  {scraper.name}
                </option>
              ))}
            </select>
            <div className="text-xs text-secondary">
              {selectedScraper ? `Supports: ${selectedScraper.supportedScrapes.join(", ")}` : "Automatic mode tries the best matching performer scraper first."}
            </div>
          </div>

          <div className="space-y-2 rounded-2xl border border-border bg-card p-4">
            <label className="block text-sm font-medium text-foreground">Input</label>
            <div className="grid grid-cols-2 gap-2">
              {(["url", "name"] as InputKind[]).map((value) => (
                <button
                  key={value}
                  onClick={() => setInputKind(value)}
                  className={`rounded-xl border px-3 py-2 text-sm capitalize transition-colors ${
                    inputKind === value
                      ? "border-accent bg-accent/10 text-accent"
                      : "border-border bg-surface text-secondary hover:text-foreground"
                  }`}
                >
                  {value}
                </button>
              ))}
            </div>

            {inputKind === "url" ? (
              <input
                value={url}
                onChange={(event) => setUrl(event.target.value)}
                placeholder="https://example.com/pornstar/..."
                className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm text-foreground outline-none"
              />
            ) : (
              <div className="space-y-2">
                <input
                  value={name}
                  onChange={(event) => setName(event.target.value)}
                  placeholder="Performer name"
                  className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm text-foreground outline-none"
                />
                <div className="text-xs text-secondary">
                  Name scraping uses the selected performer scraper when it supports name search, then falls back to known profile URL patterns when possible.
                </div>
              </div>
            )}
          </div>

          <label className="flex items-start gap-3 rounded-xl border border-border bg-card px-4 py-3 text-sm text-foreground">
            <input
              type="checkbox"
              checked={createMissingTags}
              onChange={(event) => setCreateMissingTags(event.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-border bg-card text-accent focus:ring-0"
            />
            <span>
              <span className="block font-medium">Create missing tags</span>
              <span className="mt-1 block text-xs text-secondary">When the scraped performer includes tags that do not exist locally, create them during apply.</span>
            </span>
          </label>

          {preview ? (
            <div className="space-y-3 rounded-2xl border border-border bg-card p-4">
              <div>
                <p className="text-sm font-medium text-foreground">Scrape Preview</p>
                <p className="mt-1 text-xs text-secondary">
                  Previewed from {preview.inputKind === "url" ? "URL" : "name"}
                  {preview.sourceValue ? `: ${preview.sourceValue}` : ""}. Nothing is applied until you choose Apply scraped metadata.
                </p>
              </div>
              <PreviewField label="Name" value={preview.scraped.name} />
              <PreviewField label="Disambiguation" value={preview.scraped.disambiguation} />
              <PreviewField label="Gender" value={preview.scraped.gender} />
              <PreviewField label="Birth date" value={preview.scraped.birthdate} />
              <PreviewField label="Country" value={preview.scraped.country} />
              <PreviewField label="Ethnicity" value={preview.scraped.ethnicity} />
              <PreviewField label="Measurements" value={preview.scraped.measurements} />
              <PreviewField label="Details" value={preview.scraped.details} multiline />
              <PreviewList label="Aliases" values={preview.scraped.aliases} />
              <PreviewList label="URLs" values={preview.scraped.urls} />
              <PreviewList label="Tags" values={preview.scraped.tagNames} />
              {preview.scraped.imageUrl ? (
                <div className="rounded-xl border border-border/60 bg-surface/60 px-3 py-2 text-xs text-secondary">
                  Image to apply: {preview.scraped.imageUrl}
                </div>
              ) : null}
            </div>
          ) : null}

          <div className="flex items-center justify-end gap-2 border-t border-border pt-2">
            <button onClick={onClose} className="rounded-xl px-4 py-2 text-sm text-secondary hover:text-foreground">
              Cancel
            </button>
            {preview ? (
              <button
                onClick={() => setPreview(null)}
                disabled={applyMutation.isPending}
                className="rounded-xl border border-border bg-card px-4 py-2 text-sm font-medium text-foreground hover:border-accent disabled:opacity-60"
              >
                Clear Preview
              </button>
            ) : null}
            <button
              onClick={() => {
                setError(null);
                previewMutation.mutate();
              }}
              disabled={previewMutation.isPending || applyMutation.isPending}
              className="inline-flex items-center gap-2 rounded-xl border border-border bg-card px-4 py-2 text-sm font-medium text-foreground hover:border-accent disabled:opacity-60"
            >
              {previewMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
              {preview ? "Scrape Again" : "Scrape Preview"}
            </button>
            <button
              onClick={() => {
                setError(null);
                applyMutation.mutate();
              }}
              disabled={!preview || previewMutation.isPending || applyMutation.isPending}
              className="inline-flex items-center gap-2 rounded-xl bg-accent px-4 py-2 text-sm font-medium text-white hover:bg-accent-hover disabled:opacity-60"
            >
              {applyMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
              Apply Scraped Metadata
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

function PreviewField({ label, value, multiline = false }: { label: string; value?: string; multiline?: boolean }) {
  if (!value?.trim()) {
    return null;
  }

  return (
    <div className="rounded-xl border border-border/60 bg-surface/60 px-3 py-2 text-sm text-foreground">
      <div className="text-[11px] font-medium uppercase tracking-wide text-muted">{label}</div>
      <div className={multiline ? "mt-1 whitespace-pre-wrap text-secondary" : "mt-1 text-secondary"}>{value}</div>
    </div>
  );
}

function PreviewList({ label, values }: { label: string; values: string[] }) {
  if (values.length === 0) {
    return null;
  }

  return (
    <div className="rounded-xl border border-border/60 bg-surface/60 px-3 py-2 text-sm text-foreground">
      <div className="text-[11px] font-medium uppercase tracking-wide text-muted">{label}</div>
      <div className="mt-1 text-secondary">{values.join(", ")}</div>
    </div>
  );
}