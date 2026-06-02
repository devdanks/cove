import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { VideoBatchScrapeDialog } from "../components/VideoBatchScrapeDialog";
import type { VideoScrapeVideo } from "../components/videoScrapeUtils";
import type { ScraperSummary } from "../api/types";

const mocks = vi.hoisted(() => ({
  scrapeAttemptsStartVideoBatch: vi.fn(),
  systemListScrapers: vi.fn(),
  savePreferences: vi.fn(),
}));

vi.mock("../api/client", () => ({
  scrapeAttempts: {
    startVideoBatch: mocks.scrapeAttemptsStartVideoBatch,
  },
  system: { listScrapers: mocks.systemListScrapers },
}));

vi.mock("../state/AppConfigContext", () => ({
  useAppConfig: () => ({
    config: {
      scraping: {
        scraperPreferences: [],
      },
    },
  }),
}));

vi.mock("../components/videoScrapeUtils", () => ({
  findDefaultKind: () => "url",
  findPreferredScraperId: (scrapers: ScraperSummary[]) => scrapers[0]?.id ?? "",
  getVideoLabel: (video: VideoScrapeVideo) => video.title,
  getVideoNameSearchInput: (video: VideoScrapeVideo) => video.title,
  loadScrapeApplyPreferences: () => ({
    createMissingStudio: true,
    createMissingTags: true,
    createMissingPerformers: true,
    markOrganized: false,
    hydratePerformers: false,
  }),
  saveScrapeApplyPreferences: mocks.savePreferences,
  sortScrapersForVideo: (scrapers: ScraperSummary[]) => scrapers,
  supportsScrapeKind: () => true,
}));

function renderDialog(videos: VideoScrapeVideo[]) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <VideoBatchScrapeDialog open onClose={vi.fn()} videos={videos} />
    </QueryClientProvider>,
  );
}

describe("VideoBatchScrapeDialog", () => {
  beforeEach(() => {
    mocks.systemListScrapers.mockResolvedValue([
      {
        id: "video-scraper",
        name: "Video Scraper",
        entityType: "video",
        supportedScrapes: ["URL"],
        urls: ["example.com/videos/"],
        sourcePath: "Example.yml",
      } satisfies ScraperSummary,
    ]);
    mocks.scrapeAttemptsStartVideoBatch.mockResolvedValue({ jobId: "job-1", queuedCount: 1 });
  });

  it("passes performer scraping through the batch apply payload when enabled", async () => {
    const user = userEvent.setup();
    renderDialog([
      {
        id: 4,
        title: "Video Title",
        code: undefined,
        details: undefined,
        director: undefined,
        date: undefined,
        organized: false,
        studioName: undefined,
        urls: ["https://example.com/videos/video-title"],
        tags: [],
        performers: [],
        files: [],
        updatedAt: "2024-01-01T00:00:00Z",
      },
    ]);

    await waitFor(() => expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("video-scraper"));

    await user.click(screen.getByRole("checkbox", { name: "Scrape matched performers from performer URLs" }));
    await user.click(screen.getByRole("button", { name: "Queue Scrape And Apply" }));

    await waitFor(() => {
      expect(mocks.scrapeAttemptsStartVideoBatch).toHaveBeenCalledWith(expect.objectContaining({
        hydratePerformers: true,
        autoApply: true,
        videoIds: [4],
      }));
    });
  });
});
