import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ImageDetailPage } from "../pages/ImageDetailPage";

const { mockImages, mockFaces, mockSetFavorite, mockSetRating, mockGoBack } = vi.hoisted(() => ({
  mockImages: {
    get: vi.fn(),
    delete: vi.fn(),
    update: vi.fn(),
    incrementO: vi.fn(),
    thumbnailUrl: vi.fn(() => "/thumb.jpg"),
    imageUrl: vi.fn(() => "/image.jpg"),
    detections: {
      list: vi.fn(),
    },
  },
  mockFaces: {
    get: vi.fn(),
  },
  mockSetFavorite: vi.fn(),
  mockSetRating: vi.fn(),
  mockGoBack: vi.fn(),
}));

vi.mock("../api/client", () => ({
  images: mockImages,
  faces: mockFaces,
}));

vi.mock("../hooks/useEntityEngagement", () => ({
  useEntityEngagement: () => ({
    favorite: false,
    rating: 4,
    setFavorite: mockSetFavorite,
    setRating: mockSetRating,
    favoritePending: false,
  }),
}));

vi.mock("../components/ConfirmDialog", () => ({
  ConfirmDialog: () => null,
}));

vi.mock("../router/RouteRegistry", () => ({
  ExtensionSlot: () => null,
}));

vi.mock("../components/AspectRatingsPanel", () => ({
  AspectRatingsPanel: () => <div>Aspect Ratings</div>,
}));

vi.mock("../components/Rating", () => ({
  InteractiveRating: ({ value }: { value: number }) => <div>Rating {value}</div>,
}));

vi.mock("../components/ExtensionEntityActions", () => ({
  ExtensionEntityActions: () => <div>Extension Actions</div>,
}));

vi.mock("../components/cardNavigation", () => ({
  createRouteLinkProps: (_route: unknown, onClick: () => void) => ({ href: "#", onClick }),
}));

vi.mock("../hooks/useBackNavigation", () => ({
  useBackNavigation: () => ({
    backLabel: "Back to images",
    goBack: mockGoBack,
  }),
}));

vi.mock("../auth/AuthContext", () => ({
  useAuth: () => ({
    user: { kind: "user" },
    hasPermission: () => true,
  }),
}));

vi.mock("../utils/interactionTracking", () => ({
  trackInteraction: vi.fn(),
}));

function buildImage(overrides: Record<string, unknown> = {}) {
  return {
    id: 12,
    title: "Sunset Poster",
    date: "2026-05-01T00:00:00Z",
    studioId: 9,
    studioName: "Sunset Studio",
    photographer: "Riley Smith",
    oCounter: 0,
    rating: 4,
    organized: false,
    details: "A beach sunset still.",
    performers: [{ id: 3, name: "Alex", imagePath: undefined }],
    tags: [{ id: 6, name: "Beach", provenance: undefined }],
    files: [{ id: 1, path: "C:/images/poster.jpg", width: 1920, height: 1080, format: "jpg", size: 1048576 }],
    urls: ["https://example.com/image/12"],
    customFields: {},
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
      <ImageDetailPage id={12} onNavigate={onNavigate} />
    </QueryClientProvider>,
  );

  return { onNavigate };
}

describe("ImageDetailPage", () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it("renders the shared layout with detections and related tabs", async () => {
    mockImages.get.mockResolvedValue(buildImage());
    mockImages.detections.list.mockResolvedValue([
      { id: 1, refId: 33, refKind: "face" },
    ]);
    mockFaces.get.mockResolvedValue({
      id: 33,
      label: "Beach Face",
      performerName: undefined,
      coverImageUrl: undefined,
    });

    renderPage();

    expect(await screen.findByRole("heading", { name: "Sunset Poster" })).toBeInTheDocument();
    expect(screen.getByTestId("media-detail-layout-media")).toBeInTheDocument();

    const tabs = screen.getByRole("tablist", { name: /detail tabs/i });
    fireEvent.click(within(tabs).getByRole("tab", { name: /detections/i }));
    expect(await screen.findByText("Beach Face")).toBeInTheDocument();

    fireEvent.click(within(tabs).getByRole("tab", { name: /related/i }));
    expect(await screen.findByText("Alex")).toBeInTheDocument();
    expect(screen.getByText("Beach")).toBeInTheDocument();
  });

  it("supports keyboard shortcuts for related tab, lightbox, and favorites count", async () => {
    mockImages.get.mockResolvedValue(buildImage());
    mockImages.detections.list.mockResolvedValue([]);
    mockImages.incrementO.mockResolvedValue(undefined);

    renderPage();

    await screen.findByRole("heading", { name: "Sunset Poster" });

    fireEvent.keyDown(window, { key: "r" });
    expect(await screen.findByText("Alex")).toBeInTheDocument();

    fireEvent.keyDown(window, { key: "f" });
    expect(await screen.findByRole("button", { name: "Close (Esc)" })).toBeInTheDocument();

    fireEvent.keyDown(window, { key: "o" });
    await waitFor(() => expect(mockImages.incrementO).toHaveBeenCalledWith(12));
  });
});
