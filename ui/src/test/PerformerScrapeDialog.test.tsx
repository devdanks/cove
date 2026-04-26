import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { PerformerScrapeDialog } from "../components/PerformerScrapeDialog";
import type { Performer, ScraperSummary } from "../api/types";

const mocks = vi.hoisted(() => ({
  performersPreviewScrape: vi.fn(),
  performersApplyScraped: vi.fn(),
  systemListScrapers: vi.fn(),
}));

vi.mock("../api/client", () => ({
  performers: {
    previewScrape: mocks.performersPreviewScrape,
    applyScraped: mocks.performersApplyScraped,
  },
  system: { listScrapers: mocks.systemListScrapers },
}));

vi.mock("../state/AppConfigContext", () => ({
  useAppConfig: () => ({
    config: {
      scraping: {
        scraperPreferences: [{ site: "pornhub.com", scraperId: "pornhub-performer" }],
      },
    },
  }),
}));

function renderDialog(performer: Pick<Performer, "id" | "name" | "urls">) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <PerformerScrapeDialog open onClose={vi.fn()} performer={performer} />
    </QueryClientProvider>,
  );
}

describe("PerformerScrapeDialog", () => {
  beforeEach(() => {
    mocks.performersPreviewScrape.mockResolvedValue({
      inputKind: "name",
      sourceValue: "Jane Doe",
      scraped: {
        name: "Jane Doe",
        urls: ["https://www.pornhub.com/pornstar/jane-doe"],
        aliases: ["JD"],
        tagNames: ["Example Tag"],
      },
    });
    mocks.performersApplyScraped.mockResolvedValue({});
    mocks.systemListScrapers.mockResolvedValue([
      {
        id: "pornhub-performer",
        name: "Pornhub",
        entityType: "performer",
        supportedScrapes: ["URL", "Name"],
        urls: ["pornhub.com/pornstar/"],
        sourcePath: "Pornhub.yml",
      } satisfies ScraperSummary,
    ]);
  });

  it("submits performer name scrape requests with the selected scraper", async () => {
    const user = userEvent.setup();
    renderDialog({ id: 9, name: "Jane Doe", urls: ["https://www.pornhub.com/pornstar/jane-doe"] });

    await waitFor(() => expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("pornhub-performer"));

    await user.click(screen.getByRole("button", { name: "name" }));
    const nameInput = screen.getByPlaceholderText("Performer name");
    await user.clear(nameInput);
    await user.type(nameInput, "Jane Doe");
    await user.click(screen.getByRole("button", { name: "Scrape Preview" }));

    await waitFor(() => {
      expect(mocks.performersPreviewScrape).toHaveBeenCalledWith(9, {
        inputKind: "name",
        scraperId: "pornhub-performer",
        name: "Jane Doe",
        url: undefined,
        createMissingTags: true,
      });
    });

    expect(mocks.performersApplyScraped).not.toHaveBeenCalled();
    expect(await screen.findByText("Scrape Preview")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Apply Scraped Metadata" }));

    await waitFor(() => {
      expect(mocks.performersApplyScraped).toHaveBeenCalledWith(9, {
        scraped: expect.objectContaining({
          name: "Jane Doe",
          aliases: ["JD"],
          tagNames: ["Example Tag"],
        }),
        createMissingTags: true,
      });
    });
  });
});