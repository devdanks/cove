import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { GroupDetailPage } from "../pages/GroupDetailPage";

const { mockGroups, mockScenes, mockGoBack } = vi.hoisted(() => ({
  mockGroups: {
    get: vi.fn(),
    find: vi.fn(),
    delete: vi.fn(),
    subGroups: vi.fn(),
    addSubGroup: vi.fn(),
    removeSubGroup: vi.fn(),
    reorderSubGroups: vi.fn(),
    containingGroups: vi.fn(),
    items: {
      page: vi.fn(),
      list: vi.fn(),
      delete: vi.fn(),
      reorder: vi.fn(),
      playbackManifest: vi.fn(),
    },
  },
  mockScenes: {
    find: vi.fn(),
  },
  mockGoBack: vi.fn(),
}));

vi.mock("../api/client", () => ({
  groups: mockGroups,
  scenes: mockScenes,
  entityImages: {
    groupFrontImageUrl: vi.fn(() => "/front.jpg"),
    uploadGroupFrontImage: vi.fn(),
    deleteGroupFrontImage: vi.fn(),
    groupBackImageUrl: vi.fn(() => "/back.jpg"),
    uploadGroupBackImage: vi.fn(),
    deleteGroupBackImage: vi.fn(),
  },
}));

vi.mock("../components/CompilationPlayer", () => ({
  CompilationPlayer: () => <div data-testid="compilation-player">Compilation Player</div>,
}));

vi.mock("../components/ConfirmDialog", () => ({
  ConfirmDialog: () => null,
}));

vi.mock("../pages/GroupEditModal", () => ({
  GroupEditModal: () => null,
}));

vi.mock("../router/RouteRegistry", () => ({
  ExtensionSlot: () => null,
}));

vi.mock("../components/EntityCards", () => ({
  GroupTile: ({ group, onClick }: { group: { name: string }; onClick: () => void }) => (
    <button type="button" onClick={onClick}>{group.name}</button>
  ),
  SceneCard: ({ scene, onClick }: { scene: { title?: string; id: number }; onClick: () => void }) => (
    <button type="button" onClick={onClick}>{scene.title || `Scene #${scene.id}`}</button>
  ),
}));

vi.mock("../components/QuickViewDialog", () => ({
  QuickViewDialog: () => null,
}));

vi.mock("../components/DetailListToolbar", () => ({
  DetailListToolbar: () => null,
}));

vi.mock("../components/BulkSelectionActions", () => ({
  BulkSelectionActions: () => null,
}));

vi.mock("../components/useExtensionTabs", () => ({
  useExtensionTabs: (_pageType: string, tabs: Array<{ key: string; label: string }>) => ({
    allTabs: tabs,
    renderExtensionTab: () => null,
  }),
}));

vi.mock("../hooks/useBackNavigation", () => ({
  useBackNavigation: () => ({
    backLabel: "Back to groups",
    goBack: mockGoBack,
  }),
}));

vi.mock("../hooks/useMultiSelect", () => ({
  useMultiSelect: () => ({
    selectedIds: new Set<number>(),
    toggle: vi.fn(),
    selectAll: vi.fn(),
    selectNone: vi.fn(),
  }),
}));

vi.mock("../components/SortableList", () => ({
  SortableList: ({ items, renderItem }: { items: any[]; renderItem: (item: any, state: any) => React.ReactNode }) => (
    <div>
      {items.map((item, index) => (
        <div key={item.id}>{renderItem(item, { dragHandleProps: {}, index, isDragging: false, isOver: false })}</div>
      ))}
    </div>
  ),
}));

vi.mock("../auth/AuthContext", () => ({
  useAuth: () => ({
    hasPermission: () => true,
  }),
}));

function buildGroup(overrides: Record<string, unknown> = {}) {
  return {
    id: 4,
    name: "Summer Compilation",
    aliases: "Summer Mix",
    date: "2026-05-01T00:00:00Z",
    director: "Alex Doe",
    duration: 3600,
    studioId: 11,
    studioName: "Cove Studio",
    synopsis: "A curated compilation.",
    tags: [],
    urls: ["https://example.com/group/4"],
    customFields: {},
    frontImagePath: undefined,
    backImagePath: undefined,
    sceneCount: 2,
    subGroupCount: 1,
    containingGroupCount: 3,
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
      <GroupDetailPage id={4} onNavigate={onNavigate} />
    </QueryClientProvider>,
  );

  return { onNavigate };
}

describe("GroupDetailPage", () => {
  afterEach(() => {
    vi.clearAllMocks();
  });

  it("renders the shared hero layout and switches to the metadata tab", async () => {
    mockGroups.get.mockResolvedValue(buildGroup());
    mockGroups.items.list.mockResolvedValue([
      { id: 21, orderIndex: 0, sceneId: 10, title: "Clip One", kind: "sceneRange", startSec: 1, endSec: 5 },
    ]);
    mockGroups.items.page.mockResolvedValue({
      items: [{ id: 21, orderIndex: 0, sceneId: 10, title: "Clip One", kind: "sceneRange", startSec: 1, endSec: 5 }],
      totalCount: 1,
      page: 1,
      perPage: 40,
    });
    mockGroups.items.playbackManifest.mockResolvedValue({
      items: [{ groupItemId: 21, sceneId: 10, title: "Clip One", startSec: 1, endSec: 5, durationSec: 4 }],
    });
    mockScenes.find.mockResolvedValue({ items: [], totalCount: 0 });
    mockGroups.subGroups.mockResolvedValue([]);
    mockGroups.containingGroups.mockResolvedValue([]);

    renderPage();

    expect(await screen.findByRole("heading", { name: "Summer Compilation" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /^items$/i })).toBeInTheDocument();
    expect(screen.queryByTestId("compilation-player")).not.toBeInTheDocument();
    expect(screen.getByTitle("Standalone Compilation")).toBeInTheDocument();

    const tabs = screen.getByRole("tablist", { name: /detail tabs/i });
    fireEvent.click(within(tabs).getByRole("tab", { name: /metadata/i }));

    expect(await screen.findByText("https://example.com/group/4")).toBeInTheDocument();
    expect(screen.getByText("Sub-Group Count")).toBeInTheDocument();
  });

  it("switches between metadata and edit tabs", async () => {
    mockGroups.get.mockResolvedValue(buildGroup());
    mockGroups.items.list.mockResolvedValue([
      { id: 21, orderIndex: 0, sceneId: 10, title: "Clip One", kind: "sceneRange", startSec: 1, endSec: 5 },
    ]);
    mockGroups.items.page.mockResolvedValue({
      items: [{ id: 21, orderIndex: 0, sceneId: 10, title: "Clip One", kind: "sceneRange", startSec: 1, endSec: 5 }],
      totalCount: 1,
      page: 1,
      perPage: 40,
    });
    mockGroups.items.playbackManifest.mockResolvedValue({
      items: [{ groupItemId: 21, sceneId: 10, title: "Clip One", startSec: 1, endSec: 5, durationSec: 4 }],
    });
    mockScenes.find.mockResolvedValue({ items: [], totalCount: 0 });
    mockGroups.subGroups.mockResolvedValue([]);
    mockGroups.containingGroups.mockResolvedValue([]);

    renderPage();

    await screen.findByRole("heading", { name: "Summer Compilation" });
    const tabs = screen.getByRole("tablist", { name: /detail tabs/i });

    fireEvent.click(within(tabs).getByRole("tab", { name: /metadata/i }));
    expect(await screen.findByText("Sub-Group Count")).toBeInTheDocument();

    fireEvent.click(within(tabs).getByRole("tab", { name: /^edit$/i }));
    expect(await screen.findByRole("heading", { name: "Edit Group" })).toBeInTheDocument();
  });

  it("adds a subgroup from the search results", async () => {
    mockGroups.get.mockResolvedValue(buildGroup({ subGroupCount: 0 }));
    mockGroups.items.list.mockResolvedValue([]);
    mockGroups.items.page.mockResolvedValue({ items: [], totalCount: 0, page: 1, perPage: 40 });
    mockGroups.items.playbackManifest.mockResolvedValue({ items: [] });
    mockScenes.find.mockResolvedValue({ items: [], totalCount: 0 });
    mockGroups.subGroups.mockResolvedValue([]);
    mockGroups.containingGroups.mockResolvedValue([]);
    mockGroups.find.mockResolvedValue({ items: [buildGroup({ id: 8, name: "Nested Group" })], totalCount: 1, page: 1, perPage: 20 });
    mockGroups.addSubGroup.mockResolvedValue(undefined);

    renderPage();

    fireEvent.click(await screen.findByRole("button", { name: /add sub-group/i }));
    fireEvent.change(screen.getByPlaceholderText("Search groups to add..."), { target: { value: "Nested" } });
    fireEvent.click(await screen.findByRole("button", { name: /nested group/i }));

    await waitFor(() => expect(mockGroups.addSubGroup).toHaveBeenCalledWith(4, 8));
  });
});
