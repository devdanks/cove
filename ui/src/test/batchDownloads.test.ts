import { describe, expect, it, vi, beforeEach } from "vitest";
import { formatBatchDownloadSummary, queueBatchDownloads, queueImportedUrlDownloads } from "../utils/batchDownloads";

const mocks = vi.hoisted(() => ({
  systemStartBatchDownload: vi.fn(),
}));

vi.mock("../api/client", () => ({
  system: {
    startBatchDownload: mocks.systemStartBatchDownload,
  },
}));

describe("batchDownloads", () => {
  beforeEach(() => {
    mocks.systemStartBatchDownload.mockReset();
    mocks.systemStartBatchDownload.mockResolvedValue({ jobId: "job-1", queuedCount: 1 });
  });

  it("queues existing items as a raw backend batch job", async () => {
    const result = await queueBatchDownloads(
      "Scene",
      [{ id: 4, title: "Existing Scene", urls: ["https://example.com/watch/4"], files: [] }],
      { scrapeScenes: true, allowDuplicateDownloads: true, generate: { thumbnails: true } },
    );

    expect(result).toEqual({ jobId: "job-1", queuedCount: 1, issues: [] });
    expect(mocks.systemStartBatchDownload).toHaveBeenCalledWith({
      items: [
        {
          url: "https://example.com/watch/4",
          sourceUrl: undefined,
          entity: "Scene",
          entityId: 4,
          label: "Existing Scene",
        },
      ],
      followUp: expect.objectContaining({
        scrapeScenes: true,
        allowDuplicateDownloads: true,
        generate: expect.objectContaining({ thumbnails: true }),
      }),
    });
  });

  it("queues the child URL as primary when an existing item also stores a source URL", async () => {
    await queueBatchDownloads(
      "Audio",
      [{
        id: 9,
        title: "Specific Track",
        urls: [
          "https://reddit.com/r/example/comments/abc/post",
          "https://audio.example.net/track/two",
          "https://audio.example.net/track/two",
        ],
        files: [],
      }],
      {},
    );

    expect(mocks.systemStartBatchDownload).toHaveBeenCalledWith(expect.objectContaining({
      items: [
        expect.objectContaining({
          url: "https://audio.example.net/track/two",
          sourceUrl: "https://reddit.com/r/example/comments/abc/post",
          entity: "Audio",
          entityId: 9,
        }),
      ],
    }));
  });

  it("queues imported urls for server-side placeholder creation", async () => {
    const result = await queueImportedUrlDownloads(
      "Scene",
      ["https://example.com/path/free-nature-images.jpg"],
      { scrapeScenes: true },
    );

    expect(result).toEqual({ jobId: "job-1", queuedCount: 1, issues: [] });
    expect(mocks.systemStartBatchDownload).toHaveBeenCalledWith({
      items: [
        {
          url: "https://example.com/path/free-nature-images.jpg",
          entity: "Scene",
          label: "free nature images jpg",
          title: "free nature images jpg",
          createEntityIfMissing: true,
        },
      ],
      followUp: expect.objectContaining({
        scrapeScenes: true,
        allowDuplicateDownloads: false,
        generate: expect.any(Object),
      }),
    });
  });

  it("keeps duplicate imported URL lines for server-side skip logging", async () => {
    await queueImportedUrlDownloads(
      "Scene",
      ["https://example.com/watch/duplicate", "https://example.com/watch/duplicate"],
      { scrapeScenes: true },
    );

    expect(mocks.systemStartBatchDownload).toHaveBeenCalledWith(expect.objectContaining({
      items: [
        expect.objectContaining({ url: "https://example.com/watch/duplicate" }),
        expect.objectContaining({ url: "https://example.com/watch/duplicate" }),
      ],
    }));
  });

  it("keeps server-side duplicate skips in the imported URL result", async () => {
    mocks.systemStartBatchDownload.mockResolvedValue({
      jobId: "job-1",
      queuedCount: 1,
      issues: [
        {
          kind: "skipped",
          label: "existing",
          reason: "This URL is already downloaded for Existing Scene.",
        },
      ],
    });

    const result = await queueImportedUrlDownloads(
      "Scene",
      ["https://example.com/watch/existing", "https://example.com/watch/new"],
      { scrapeScenes: true },
    );

    expect(result).toEqual({
      jobId: "job-1",
      queuedCount: 1,
      issues: [
        {
          kind: "skipped",
          label: "existing",
          reason: "This URL is already downloaded for Existing Scene.",
        },
      ],
    });
  });

  it("formats all-skipped imported URL results without claiming downloads were queued", () => {
    expect(formatBatchDownloadSummary("scene", {
      queuedCount: 0,
      issues: [
        {
          kind: "skipped",
          label: "existing",
          reason: "This URL is already downloaded for Existing Scene.",
        },
      ],
    })).toContain("No scene downloads queued.");
  });
});