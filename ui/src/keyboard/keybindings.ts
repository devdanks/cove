export type KeybindingDefinition = {
  id: string;
  group: string;
  label: string;
  keys: string;
};

export const KEYBINDING_DEFAULTS: KeybindingDefinition[] = [
  { id: "global.home", group: "Global Navigation", label: "Home", keys: "g h" },
  { id: "global.scenes", group: "Global Navigation", label: "Scenes", keys: "g s" },
  { id: "global.audios", group: "Global Navigation", label: "Audios", keys: "g a" },
  { id: "global.texts", group: "Global Navigation", label: "Texts", keys: "g x" },
  { id: "global.segments", group: "Global Navigation", label: "Segments", keys: "g m" },
  { id: "global.faces", group: "Global Navigation", label: "Faces", keys: "g f" },
  { id: "global.images", group: "Global Navigation", label: "Images", keys: "g i" },
  { id: "global.groups", group: "Global Navigation", label: "Groups", keys: "g v" },
  { id: "global.galleries", group: "Global Navigation", label: "Galleries", keys: "g l" },
  { id: "global.performers", group: "Global Navigation", label: "Performers", keys: "g p" },
  { id: "global.studios", group: "Global Navigation", label: "Studios", keys: "g u" },
  { id: "global.tags", group: "Global Navigation", label: "Tags", keys: "g t" },
  { id: "global.settings", group: "Global Navigation", label: "Settings", keys: "g z" },
  { id: "global.stats", group: "Global Navigation", label: "Stats", keys: "g d" },
  { id: "global.shortcuts", group: "Global Navigation", label: "Show shortcuts", keys: "?" },
  { id: "list.search", group: "List Pages", label: "Focus search", keys: "/" },
  { id: "list.view.grid", group: "List Pages", label: "Grid view", keys: "v g" },
  { id: "list.view.list", group: "List Pages", label: "List view", keys: "v l" },
  { id: "list.view.wall", group: "List Pages", label: "Wall view", keys: "v w" },
  { id: "list.view.tagger", group: "List Pages", label: "Tagger view", keys: "v t" },
  { id: "list.view.graph", group: "List Pages", label: "Graph view", keys: "v h" },
  { id: "list.view.group", group: "List Pages", label: "Group view", keys: "v b" },
  { id: "list.select.all", group: "List Pages", label: "Select all", keys: "s a" },
  { id: "list.select.none", group: "List Pages", label: "Select none", keys: "s n" },
  { id: "list.select.invert", group: "List Pages", label: "Invert selection", keys: "s i" },
  { id: "list.page.previous", group: "List Pages", label: "Previous page", keys: "ArrowLeft" },
  { id: "list.page.next", group: "List Pages", label: "Next page", keys: "ArrowRight" },
  { id: "list.page.back10", group: "List Pages", label: "Back 10 pages", keys: "Shift+ArrowLeft" },
  { id: "list.page.forward10", group: "List Pages", label: "Forward 10 pages", keys: "Shift+ArrowRight" },
  { id: "list.page.first", group: "List Pages", label: "First page", keys: "Ctrl+Home" },
  { id: "list.page.last", group: "List Pages", label: "Last page", keys: "Ctrl+End" },
  { id: "list.filters", group: "List Pages", label: "Filters", keys: "f" },
  { id: "list.zoom.in", group: "List Pages", label: "Zoom in", keys: "+" },
  { id: "list.zoom.out", group: "List Pages", label: "Zoom out", keys: "-" },
];

export const KEYBINDING_GROUPS = Array.from(
  KEYBINDING_DEFAULTS.reduce((groups, definition) => {
    const existing = groups.get(definition.group) ?? [];
    existing.push(definition);
    groups.set(definition.group, existing);
    return groups;
  }, new Map<string, KeybindingDefinition[]>()).entries()
).map(([group, definitions]) => ({ group, definitions }));

export function resolveKeybinding(overrides: Record<string, string> | undefined, id: string, fallback: string) {
  const override = overrides?.[id]?.trim();
  return override || fallback;
}

export function keybindingDefault(id: string) {
  return KEYBINDING_DEFAULTS.find((definition) => definition.id === id)?.keys ?? "";
}