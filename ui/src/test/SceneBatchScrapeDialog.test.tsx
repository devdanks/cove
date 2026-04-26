import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SceneBatchScrapeDialog } from "../components/SceneBatchScrapeDialog";
import type { SceneScrapeScene } from "../components/sceneScrapeUtils";
import type { ScraperSummary } from "../api/types";

const mocks = vi.hoisted(() => ({
  scrapeAttemptsStartSceneBatch: vi.fn(),
  systemListScrapers: vi.fn(),
  savePreferences: vi.fn(),
}));

vi.mock("../api/client", () => ({
  scrapeAttempts: {
    startSceneBatch: mocks.scrapeAttemptsStartSceneBatch,
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

vi.mock("../components/sceneScrapeUtils", () => ({
  findDefaultKind: () => "url",
  findPreferredScraperId: (scrapers: ScraperSummary[]) => scrapers[0]?.id ?? "",
  getSceneLabel: (scene: SceneScrapeScene) => scene.title,
  getSceneNameSearchInput: (scene: SceneScrapeScene) => scene.title,
  loadScrapeApplyPreferences: () => ({
    createMissingStudio: true,
    createMissingTags: true,
    createMissingPerformers: true,
    markOrganized: false,
    hydratePerformers: false,
  }),
  saveScrapeApplyPreferences: mocks.savePreferences,
  sortScrapersForScene: (scrapers: ScraperSummary[]) => scrapers,
  supportsScrapeKind: () => true,
}));

function renderDialog(scenes: SceneScrapeScene[]) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <SceneBatchScrapeDialog open onClose={vi.fn()} scenes={scenes} />
    </QueryClientProvider>,
  );
}

describe("SceneBatchScrapeDialog", () => {
  beforeEach(() => {
    mocks.systemListScrapers.mockResolvedValue([
      {
        id: "scene-scraper",
        name: "Scene Scraper",
        entityType: "scene",
        supportedScrapes: ["URL"],
        urls: ["example.com/scenes/"],
        sourcePath: "Example.yml",
      } satisfies ScraperSummary,
    ]);
    mocks.scrapeAttemptsStartSceneBatch.mockResolvedValue({ jobId: "job-1", queuedCount: 1 });
  });

  it("passes performer scraping through the batch apply payload when enabled", async () => {
    const user = userEvent.setup();
    renderDialog([
      {
        id: 4,
        title: "Scene Title",
        code: undefined,
        details: undefined,
        director: undefined,
        date: undefined,
        organized: false,
        studioName: undefined,
        urls: ["https://example.com/scenes/scene-title"],
        tags: [],
        performers: [],
        files: [],
        updatedAt: "2024-01-01T00:00:00Z",
      },
    ]);

    await waitFor(() => expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("scene-scraper"));

    await user.click(screen.getByRole("checkbox", { name: "Scrape matched performers from performer URLs" }));
    await user.click(screen.getByRole("button", { name: "Queue Scrape And Apply" }));

    await waitFor(() => {
      expect(mocks.scrapeAttemptsStartSceneBatch).toHaveBeenCalledWith(expect.objectContaining({
        hydratePerformers: true,
        autoApply: true,
        sceneIds: [4],
      }));
    });
  });
});