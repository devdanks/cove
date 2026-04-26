import { describe, expect, it, vi, beforeEach } from "vitest";
import { queueBatchDownloads, queueImportedUrlDownloads } from "../utils/batchDownloads";

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
          entity: "Scene",
          entityId: 4,
          label: "Existing Scene",
        },
      ],
      followUp: {
        scrapeScenes: true,
        allowDuplicateDownloads: true,
        generate: expect.objectContaining({ thumbnails: true }),
      },
    });
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
      followUp: {
        scrapeScenes: true,
        allowDuplicateDownloads: false,
        generate: expect.any(Object),
      },
    });
  });
});