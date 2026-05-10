import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ResolvedSpanPlayPage } from "../pages/ResolvedSpanPlayPage";

const { mockScenes, mockSegmentDisplayProfiles, mockSegmentLibrary, mockGoBack } = vi.hoisted(() => ({
  mockScenes: {
    get: vi.fn(),
    createSubScene: vi.fn(),
    streamUrl: vi.fn((id: number) => `/scene-${id}.mp4`),
    screenshotUrl: vi.fn((id: number) => `/scene-${id}.jpg`),
    segments: {
      spanDetail: vi.fn(),
    },
  },
  mockSegmentDisplayProfiles: {
    get: vi.fn(),
  },
  mockSegmentLibrary: {
    list: vi.fn(),
  },
  mockGoBack: vi.fn(),
}));

vi.mock("../api/client", () => ({
  scenes: mockScenes,
  segmentDisplayProfiles: mockSegmentDisplayProfiles,
  segmentLibrary: mockSegmentLibrary,
  faces: { get: vi.fn() },
  performers: { get: vi.fn() },
  tags: { get: vi.fn() },
}));

vi.mock("../hooks/useBackNavigation", () => ({
  useBackNavigation: () => ({
    backLabel: "Back to scene",
    goBack: mockGoBack,
  }),
}));

vi.mock("../auth/AuthContext", () => ({
  useAuth: () => ({
    hasPermission: () => true,
  }),
}));

vi.mock("../components/VideoPlayer", () => ({
  VideoPlayer: ({ clip, resumeTime, onEnded }: { clip: { start: number; end: number }; resumeTime?: number; onEnded?: () => void }) => (
    <div>
      <div data-testid="resolved-span-player">Clip {clip.start}-{clip.end} @ {resumeTime}</div>
      <button type="button" onClick={() => onEnded?.()}>End clip</button>
    </div>
  ),
}));

function buildDetail() {
  return {
    sceneId: 14,
    sceneTitle: "Scene Fourteen",
    profileId: 3,
    span: {
      spanKey: "tag-14",
      startSec: 5,
      endSec: 25,
      tagName: "Action Sequence",
      kind: "tag",
      segmentIds: [71, 72],
      sourceKey: "tag:action",
      colorHint: "amber",
    },
    intervals: [
      { startSec: 5, endSec: 10 },
      { startSec: 20, endSec: 25 },
    ],
  };
}

function buildScene(overrides: Record<string, unknown> = {}) {
  return {
    id: 14,
    title: "Scene Fourteen",
    code: "SC-14",
    details: "Resolved parent scene",
    director: "Director Span",
    date: "2026-05-01",
    organized: true,
    studioId: 22,
    urls: ["https://example.test/scene-14"],
    tags: [{ id: 8, name: "Action" }],
    performers: [{ id: 31, name: "Performer Span" }],
    galleries: [{ id: 45, title: "Gallery Forty Five", date: "2026-04-10" }],
    groups: [{ id: 55, name: "Span Group", sceneIndex: 2 }],
    customFields: { energy: "high" },
    files: [{ id: 1, format: "mp4", duration: 120, captions: [] }],
    ...overrides,
  };
}

function renderPage(props?: Partial<React.ComponentProps<typeof ResolvedSpanPlayPage>>) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  render(
    <QueryClientProvider client={queryClient}>
      <ResolvedSpanPlayPage
        sceneId={14}
        spanKey="tag-14"
        onNavigate={vi.fn()}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe("ResolvedSpanPlayPage", () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it("renders VideoPlayer clip props and seeks within the logical union timeline", async () => {
    mockScenes.segments.spanDetail.mockResolvedValue(buildDetail());
    mockScenes.get.mockResolvedValue(buildScene());
    mockSegmentDisplayProfiles.get.mockResolvedValue({ id: 3, name: "Default Profile" });
    mockSegmentLibrary.list.mockResolvedValue({
      items: [
        { id: 71, startSec: 5, endSec: 10, title: "Segment One" },
        { id: 72, startSec: 20, endSec: 25, title: "Segment Two" },
      ],
    });

    renderPage();

    expect(await screen.findByText("Clip 5-10 @ 5")).toBeInTheDocument();
    expect(screen.queryByTestId("media-detail-layout-media-frame")).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Seek within the resolved span"), { target: { value: "7" } });
    expect(await screen.findByText("Clip 20-25 @ 22")).toBeInTheDocument();
  });

  it("auto-advances between intervals and loops back to the first interval", async () => {
    mockScenes.segments.spanDetail.mockResolvedValue(buildDetail());
    mockScenes.get.mockResolvedValue(buildScene());
    mockSegmentDisplayProfiles.get.mockResolvedValue({ id: 3, name: "Default Profile" });
    mockSegmentLibrary.list.mockResolvedValue({
      items: [
        { id: 71, startSec: 5, endSec: 10, title: "Segment One" },
        { id: 72, startSec: 20, endSec: 25, title: "Segment Two" },
      ],
    });

    renderPage();

    expect(await screen.findByText("Clip 5-10 @ 5")).toBeInTheDocument();

    fireEvent.click(screen.getByText("End clip"));
    expect(await screen.findByText("Clip 20-25 @ 20")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Loop span" }));
    fireEvent.click(screen.getByText("End clip"));
    expect(await screen.findByText("Clip 5-10 @ 5")).toBeInTheDocument();
  });

  it("describes derived intersection spans without union-only copy", async () => {
    const detail = buildDetail();
    detail.span.spanKey = "dq-intersection-5000-25000";
    detail.span.tagName = "Intersection";

    mockScenes.segments.spanDetail.mockResolvedValue(detail);
    mockScenes.get.mockResolvedValue(buildScene());

    renderPage({
      spanKey: detail.span.spanKey,
      profileId: 3,
      derivedQueryDescriptor: {
        operator: "intersection",
        operands: [],
      },
    });

    expect(await screen.findByText("Derived Intersection")).toBeInTheDocument();
    expect(screen.getByText("Playback follows the resolved intersection output intervals and automatically skips the gaps between them.")).toBeInTheDocument();
  });

  it("creates a metadata-preserving scene from the resolved span", async () => {
    mockScenes.segments.spanDetail.mockResolvedValue(buildDetail());
    mockScenes.get.mockResolvedValue(buildScene());
    mockScenes.createSubScene.mockResolvedValue({ id: 777 });
    mockSegmentDisplayProfiles.get.mockResolvedValue({ id: 3, name: "Default Profile" });
    mockSegmentLibrary.list.mockResolvedValue({
      items: [
        { id: 71, startSec: 5, endSec: 10, title: "Segment One" },
        { id: 72, startSec: 20, endSec: 25, title: "Segment Two" },
      ],
    });

    const onNavigate = vi.fn();
    renderPage({ onNavigate });

    fireEvent.click(await screen.findByRole("button", { name: "Make Scene" }));

    await waitFor(() => {
      expect(mockScenes.createSubScene).toHaveBeenCalledWith(14, expect.objectContaining({
        title: "Action Sequence",
        code: "SC-14",
        details: "Resolved parent scene",
        director: "Director Span",
        date: "2026-05-01",
        organized: true,
        studioId: 22,
        urls: ["https://example.test/scene-14"],
        tagIds: [8],
        performerIds: [31],
        galleryIds: [45],
        groups: [{ groupId: 55, sceneIndex: 2 }],
        customFields: { energy: "high" },
        parentSceneId: 14,
        clipStartSec: 5,
        clipEndSec: 25,
      }));
    });
    await waitFor(() => {
      expect(onNavigate).toHaveBeenCalledWith({ page: "scene", id: 777 });
    });
  });
});
