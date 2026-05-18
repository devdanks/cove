import { act, fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../components/Rating", () => ({
  RatingBanner: () => null,
  RatingBadge: () => null,
}));

import { SceneCard, SceneCardPopovers } from "../components/EntityCards";
import { DetailsTab, FileInfoTab } from "../pages/SceneDetailPage";

const sceneFile = {
  id: 10,
  basename: "alpha.mp4",
  path: "C:\\library\\alpha.mp4",
  size: 1_048_576,
  duration: 120,
  width: 1920,
  height: 1080,
  frameRate: 29.97,
  bitRate: 2_400_000,
  videoCodec: "H264",
  audioCodec: "AAC",
  fingerprints: [],
};

const baseScene = {
  id: 42,
  title: "Sample Scene",
  updatedAt: "2024-01-12T00:00:00Z",
  files: [sceneFile],
  performers: [],
  groups: [],
  galleries: [],
  tags: [],
  studioName: null,
  studioId: null,
  resumeTime: 0,
  rating: null,
  likeCounter: 1,
  organized: false,
  details: null,
  date: null,
  playCount: 0,
};

function renderWithQueryClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

beforeEach(() => {
  vi.restoreAllMocks();
  vi.stubGlobal(
    "IntersectionObserver",
    class {
      observe() {}
      disconnect() {}
      unobserve() {}
    }
  );
});

afterEach(() => {
  vi.useRealTimers();
});

describe("SceneCard navigation", () => {
  it("renders the main scene surface as a real link", () => {
    const onClick = vi.fn();
    render(<SceneCard scene={baseScene as any} onClick={onClick} />);

    expect(screen.getByRole("link", { name: /Open scene Sample Scene/i })).toHaveAttribute("href", "/scene/42");
    expect(onClick).not.toHaveBeenCalled();
  });

  it("navigates in-place on a plain left click through the scene link", () => {
    const onClick = vi.fn();
    render(<SceneCard scene={baseScene as any} onClick={onClick} />);

    fireEvent.click(screen.getByRole("link", { name: /Open scene Sample Scene/i }));

    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("lets modified clicks fall through to normal browser link behavior", () => {
    const onClick = vi.fn();
    render(<SceneCard scene={baseScene as any} onClick={onClick} />);

    fireEvent.click(screen.getByRole("link", { name: /Open scene Sample Scene/i }), { ctrlKey: true });

    expect(onClick).not.toHaveBeenCalled();
  });

  it("renders performer badges as real links without triggering scene navigation on modified clicks", () => {
    const onClick = vi.fn();
    const onNavigate = vi.fn();

    render(
      <SceneCard
        scene={{
          ...baseScene,
          performers: [{ id: 7, name: "Alice Example", imagePath: null }],
        } as any}
        onClick={onClick}
        onNavigate={onNavigate}
      />
    );

    const performerLink = screen.getByRole("link", { name: /Alice Example/i });
    fireEvent.click(performerLink, { ctrlKey: true });

    expect(performerLink).toHaveAttribute("href", "/performer/7");
    expect(onClick).not.toHaveBeenCalled();
    expect(onNavigate).not.toHaveBeenCalled();
  });

  it("renders performer popover items as real links", () => {
    vi.useFakeTimers();

    render(
      <SceneCardPopovers
        scene={{
          ...baseScene,
          performers: [{ id: 9, name: "Popover Performer", imagePath: null }],
        } as any}
      />
    );

    fireEvent.mouseEnter(screen.getByTitle("Performers"));
    act(() => {
      vi.advanceTimersByTime(250);
    });

    expect(screen.getByRole("link", { name: /Popover Performer/i })).toHaveAttribute("href", "/performer/9");
  });

  it("renders a likes counter instead of the legacy O badge", () => {
    render(<SceneCard scene={baseScene as any} engagement={{ hostId: 42, isFavorite: false, resumeTime: 0, playDuration: 0, playCount: 0, likeCount: 1, derivedLikeCount: 0, pageVisitCount: 0, completeCount: 0 }} onClick={vi.fn()} />);

    expect(screen.getByTitle("Likes: 1")).toBeInTheDocument();
    expect(screen.queryByText(/^O$/)).not.toBeInTheDocument();
  });

  it("does not render a favorite card control when the card is not favorited", () => {
    render(<SceneCard scene={baseScene as any} engagement={{ hostId: 42, isFavorite: false, resumeTime: 0, playDuration: 0, playCount: 0, likeCount: 0, derivedLikeCount: 0, pageVisitCount: 0, completeCount: 0 }} onClick={vi.fn()} />);

    expect(screen.queryByTitle("Favorite")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Favorite" })).not.toBeInTheDocument();
  });

  it("renders a non-interactive favorite indicator when the card is favorited", () => {
    render(<SceneCard scene={baseScene as any} engagement={{ hostId: 42, isFavorite: true, resumeTime: 0, playDuration: 0, playCount: 0, likeCount: 0, derivedLikeCount: 0, pageVisitCount: 0, completeCount: 0 }} onClick={vi.fn()} />);

    expect(screen.getByTitle("Favorite")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Favorite" })).not.toBeInTheDocument();
  });
});

describe("FileInfoTab", () => {
  it("renders every underlying scene file", () => {
    renderWithQueryClient(
      <FileInfoTab
        files={[
          sceneFile,
          {
            ...sceneFile,
            id: 11,
            basename: "beta.mp4",
            path: "D:\\archive\\beta.mp4",
          },
        ] as any}
      />
    );

    expect(screen.getByText("C:\\library\\alpha.mp4")).toBeInTheDocument();
    expect(screen.getByText("D:\\archive\\beta.mp4")).toBeInTheDocument();
    expect(screen.getByText("File 1 of 2")).toBeInTheDocument();
    expect(screen.getByText("File 2 of 2")).toBeInTheDocument();
  });
});

describe("DetailsTab performers", () => {
  it("shows performer age at scene date and uses a paired grid for multiple performers", () => {
    const scene = {
      ...baseScene,
      date: "2024-01-12",
      remoteIds: [],
      urls: [],
      customFields: undefined,
      performers: [
        { id: 7, name: "Alice Example", birthdate: "2000-01-10", imagePath: null },
        { id: 8, name: "Beth Example", birthdate: "1998-03-01", imagePath: null },
      ],
    };

    renderWithQueryClient(<DetailsTab scene={scene as any} onNavigate={vi.fn()} />);

    expect(screen.getByText("24 yrs old")).toBeInTheDocument();

    const performerGrid = screen.getByText("Performers").nextElementSibling as HTMLElement;
    expect(performerGrid.className).toContain("grid");
    expect(performerGrid.className).toContain("grid-cols-2");

    expect(screen.getByRole("link", { name: /Alice Example/i }).className).toContain("absolute inset-0");
    expect(screen.getByRole("link", { name: /Beth Example/i }).className).toContain("absolute inset-0");
  });

});
