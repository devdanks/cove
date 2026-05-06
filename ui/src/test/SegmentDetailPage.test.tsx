import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SegmentDetailPage } from "../pages/SegmentDetailPage";

const { mockScenes, mockSegmentLibrary, mockTags, mockGoBack } = vi.hoisted(() => ({
  mockScenes: {
    get: vi.fn(),
    streamUrl: vi.fn(() => "/stream/scene.mp4"),
    screenshotUrl: vi.fn(() => "/scene.jpg"),
    segments: {
      list: vi.fn(),
      spans: vi.fn(),
      update: vi.fn(),
      delete: vi.fn(),
    },
  },
  mockSegmentLibrary: {
    get: vi.fn(),
  },
  mockTags: {
    find: vi.fn(),
  },
  mockGoBack: vi.fn(),
}));

vi.mock("../api/client", () => ({
  scenes: mockScenes,
  segmentLibrary: mockSegmentLibrary,
  tags: mockTags,
}));

vi.mock("../components/VideoPlayer", () => ({
  VideoPlayer: () => <div data-testid="segment-video-player">Video Player</div>,
}));

vi.mock("../auth/AuthContext", () => ({
  useAuth: () => ({
    hasPermission: () => true,
  }),
}));

vi.mock("../hooks/useBackNavigation", () => ({
  useBackNavigation: () => ({
    backLabel: "Back to segments",
    goBack: mockGoBack,
  }),
}));

function buildSegment(overrides: Record<string, unknown> = {}) {
  return {
    id: 7,
    hostId: 99,
    hostType: "scene",
    hostTitle: "Scene 99",
    title: "Episode Intro",
    kind: "intro",
    sourceKey: "detector.segment",
    sourceRunId: "run-1",
    colorHint: "#ffaa00",
    startSec: 12,
    endSec: 21,
    confidence: 0.91,
    tagId: 5,
    tagName: "Opening",
    refId: 55,
    payload: { score: 0.91 },
    createdAt: "2026-05-01T12:00:00Z",
    updatedAt: "2026-05-01T13:00:00Z",
    ...overrides,
  };
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const onNavigate = vi.fn();

  render(
    <QueryClientProvider client={queryClient}>
      <SegmentDetailPage id={7} onNavigate={onNavigate} />
    </QueryClientProvider>,
  );

  return { onNavigate };
}

describe("SegmentDetailPage", () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it("renders media layout tabs and resolved span preview", async () => {
    mockSegmentLibrary.get.mockResolvedValue(buildSegment());
    mockScenes.get.mockResolvedValue({
      id: 99,
      title: "Scene 99",
      files: [{ format: "mp4", duration: 120, captions: [] }],
    });
    mockScenes.segments.list.mockResolvedValue([
      buildSegment({ id: 6, title: "Cold Open", startSec: 5, endSec: 10, tagName: "Teaser" }),
      buildSegment(),
      buildSegment({ id: 8, title: "Action Beat", startSec: 22, endSec: 34, tagName: "Action" }),
    ]);
    mockScenes.segments.spans.mockResolvedValue({
      profileId: 3,
      spans: [
        {
          spanKey: "focus",
          startSec: 10,
          endSec: 28,
          tagName: "Opening Stretch",
          kind: "highlight",
          segmentIds: [7, 8],
        },
      ],
    });
    mockTags.find.mockResolvedValue({ items: [] });

    renderPage();

    expect(await screen.findByRole("heading", { name: "Episode Intro" })).toBeInTheDocument();
    expect(screen.getByTestId("media-detail-layout-media")).toBeInTheDocument();
    expect(screen.getByTestId("segment-video-player")).toBeInTheDocument();
    expect(screen.queryByTestId("media-detail-layout-media-frame")).not.toBeInTheDocument();

    const tabs = screen.getByRole("tablist", { name: /detail tabs/i });

    fireEvent.click(within(tabs).getByRole("tab", { name: /context/i }));
    expect(await screen.findByText("Previous Segments")).toBeInTheDocument();
    expect(screen.getAllByText("Cold Open").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Action Beat").length).toBeGreaterThan(0);

    fireEvent.click(within(tabs).getByRole("tab", { name: /resolved spans/i }));
    expect(await screen.findByText("Opening Stretch")).toBeInTheDocument();
    expect(screen.getByText("Contains current segment")).toBeInTheDocument();
  });

  it("supports keyboard shortcuts for edit and adjacent navigation", async () => {
    mockSegmentLibrary.get.mockResolvedValue(buildSegment());
    mockScenes.get.mockResolvedValue({
      id: 99,
      title: "Scene 99",
      files: [{ format: "mp4", duration: 120, captions: [] }],
    });
    mockScenes.segments.list.mockResolvedValue([
      buildSegment({ id: 6, title: "Cold Open", startSec: 5, endSec: 10 }),
      buildSegment(),
      buildSegment({ id: 8, title: "Action Beat", startSec: 22, endSec: 34 }),
    ]);
    mockScenes.segments.spans.mockResolvedValue({ profileId: 3, spans: [] });
    mockTags.find.mockResolvedValue({ items: [] });

    const { onNavigate } = renderPage();

    await screen.findByRole("heading", { name: "Episode Intro" });

    fireEvent.keyDown(window, { key: "e" });
    expect(await screen.findByRole("heading", { name: "Edit Segment" })).toBeInTheDocument();
    expect(screen.getByLabelText("Title")).toHaveFocus();

    fireEvent.keyDown(window, { key: "]" });
    expect(onNavigate).toHaveBeenCalledWith({ page: "segment", id: 8 });

    fireEvent.keyDown(window, { key: "s" });
    expect(onNavigate).toHaveBeenCalledWith({ page: "scene", id: 99, seekTo: 12 });
  });
});
