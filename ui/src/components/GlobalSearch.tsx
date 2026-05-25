import { useDeferredValue, useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { useQuery } from "@tanstack/react-query";
import { Building2, Film, FolderOpen, ImageIcon, Layers, Loader2, Search, Tag, Users } from "lucide-react";
import { galleries, groups, images, performers, scenes, studios, tags } from "../api/client";
import type { InteractionHostType } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { canReadEntity } from "../auth/visibility";
import { getImageDisplayTitle } from "../utils/imageDisplay";
import { trackInteraction } from "../utils/interactionTracking";

interface Props {
  navigate: (r: any) => void;
}

type SearchGroup = {
  key: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  items: { id: number; title: string; subtitle?: string; route: any; hostType: InteractionHostType }[];
};

type SceneSearchItems = Awaited<ReturnType<typeof scenes.find>>["items"];
type PerformerSearchItems = Awaited<ReturnType<typeof performers.find>>["items"];
type StudioSearchItems = Awaited<ReturnType<typeof studios.find>>["items"];
type TagSearchItems = Awaited<ReturnType<typeof tags.find>>["items"];
type GallerySearchItems = Awaited<ReturnType<typeof galleries.find>>["items"];
type ImageSearchItems = Awaited<ReturnType<typeof images.find>>["items"];
type GroupSearchItems = Awaited<ReturnType<typeof groups.find>>["items"];

type SearchDefinition = {
  key: string;
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  hostType: InteractionHostType;
  load: () => Promise<{ items: unknown[] }>;
  mapItems: (items: unknown[]) => SearchGroup["items"];
};

async function mergeSearches<TItem extends { id: number }>(limit: number, searches: Promise<{ items: TItem[] }>[]) {
  const settled = await Promise.allSettled(searches);
  const resultSets = settled
    .filter((result): result is PromiseFulfilledResult<{ items: TItem[] }> => result.status === "fulfilled")
    .map((result) => result.value.items);
  const seen = new Set<number>();
  const items: TItem[] = [];

  for (let index = 0; items.length < limit; index++) {
    let foundItemAtIndex = false;
    for (const resultItems of resultSets) {
      const item = resultItems[index];
      if (!item) continue;
      foundItemAtIndex = true;
      if (seen.has(item.id)) continue;
      seen.add(item.id);
      items.push(item);
      if (items.length >= limit) return { items };
    }
    if (!foundItemAtIndex) break;
  }

  return { items };
}

export function GlobalSearch({ navigate }: Props) {
  const [term, setTerm] = useState("");
  const [open, setOpen] = useState(false);
  const [desktopPanelStyle, setDesktopPanelStyle] = useState<{ left: number; top: number; width: number } | null>(null);
  const deferredTerm = useDeferredValue(term.trim());
  const containerRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const lastTrackedSearchKey = useRef("");
  const { hasPermission, permissions } = useAuth();

  const readableEntities = useMemo(() => ({
    scenes: canReadEntity("scene", hasPermission),
    performers: canReadEntity("performer", hasPermission),
    studios: canReadEntity("studio", hasPermission),
    tags: canReadEntity("tag", hasPermission),
    galleries: canReadEntity("gallery", hasPermission),
    images: canReadEntity("image", hasPermission),
    groups: canReadEntity("group", hasPermission),
  }), [hasPermission, permissions]);

  const searchableLabels = useMemo(() => {
    const labels: string[] = [];
    if (readableEntities.scenes) labels.push("scenes");
    if (readableEntities.performers) labels.push("performers");
    if (readableEntities.studios) labels.push("studios");
    if (readableEntities.tags) labels.push("tags");
    if (readableEntities.galleries) labels.push("galleries");
    if (readableEntities.images) labels.push("images");
    if (readableEntities.groups) labels.push("groups");
    return labels;
  }, [readableEntities]);

  useEffect(() => {
    const onPointerDown = (event: MouseEvent) => {
      const target = event.target as Node;
      const inSearchControl = containerRef.current?.contains(target);
      const inSearchPanel = panelRef.current?.contains(target);

      if (!inSearchControl && !inSearchPanel) {
        setOpen(false);
      }
    };
    document.addEventListener("mousedown", onPointerDown);
    return () => document.removeEventListener("mousedown", onPointerDown);
  }, []);

  useEffect(() => {
    if (!open) {
      setDesktopPanelStyle(null);
      return;
    }

    const updatePanelPosition = () => {
      const trigger = containerRef.current;
      if (!trigger) return;

      const rect = trigger.getBoundingClientRect();
      const viewportWidth = window.innerWidth;
      const width = Math.min(480, Math.max(280, viewportWidth - 32));
      const left = Math.min(Math.max(16, rect.right - width), Math.max(16, viewportWidth - width - 16));
      setDesktopPanelStyle({ left, top: rect.bottom + 8, width });
    };

    updatePanelPosition();
    window.addEventListener("resize", updatePanelPosition);
    window.addEventListener("scroll", updatePanelPosition, true);
    return () => {
      window.removeEventListener("resize", updatePanelPosition);
      window.removeEventListener("scroll", updatePanelPosition, true);
    };
  }, [open]);

  const { data, isFetching } = useQuery({
    queryKey: ["global-search", deferredTerm, searchableLabels.join(",")],
    enabled: deferredTerm.length >= 2 && searchableLabels.length > 0,
    queryFn: async () => {
      const query = { q: deferredTerm, perPage: 8, direction: "desc" as const };
      const aliasCriterion = { value: deferredTerm, modifier: "INCLUDES" as const };
      const aliasFindFilter = { perPage: query.perPage, sort: "name", direction: "asc" as const };
      const searches: SearchDefinition[] = [
        ...(readableEntities.scenes ? [{
          key: "scenes",
          label: "Scenes",
          icon: Film,
          hostType: "scene" as const,
          load: () => scenes.find(query),
          mapItems: (items: unknown[]) => (items as SceneSearchItems).map((item) => ({
            id: item.id,
            title: item.title || item.files[0]?.basename || `Scene ${item.id}`,
            subtitle: item.studioName || item.date || undefined,
            route: { page: "scene", id: item.id },
            hostType: "scene" as const,
          })),
        }] : []),
        ...(readableEntities.performers ? [{
          key: "performers",
          label: "Performers",
          icon: Users,
          hostType: "performer" as const,
          load: () => mergeSearches<PerformerSearchItems[number]>(query.perPage, [
            performers.find({ ...query, sort: "name", direction: "asc" }),
            performers.findFiltered({ findFilter: aliasFindFilter, objectFilter: { aliasesCriterion: aliasCriterion } }),
          ]),
          mapItems: (items: unknown[]) => (items as PerformerSearchItems).map((item) => ({
            id: item.id,
            title: item.name,
            subtitle: item.aliases?.length ? `Aliases: ${item.aliases.slice(0, 3).join(", ")}` : item.disambiguation || undefined,
            route: { page: "performer", id: item.id },
            hostType: "performer" as const,
          })),
        }] : []),
        ...(readableEntities.studios ? [{
          key: "studios",
          label: "Studios",
          icon: Building2,
          hostType: "studio" as const,
          load: () => mergeSearches<StudioSearchItems[number]>(query.perPage, [
            studios.find({ ...query, sort: "name", direction: "asc" }),
            studios.findFiltered({ findFilter: aliasFindFilter, objectFilter: { aliasesCriterion: aliasCriterion } }),
          ]),
          mapItems: (items: unknown[]) => (items as StudioSearchItems).map((item) => ({
            id: item.id,
            title: item.name,
            subtitle: item.aliases?.length ? `Aliases: ${item.aliases.slice(0, 3).join(", ")}` : item.parentName || undefined,
            route: { page: "studio", id: item.id },
            hostType: "studio" as const,
          })),
        }] : []),
        ...(readableEntities.tags ? [{
          key: "tags",
          label: "Tags",
          icon: Tag,
          hostType: "tag" as const,
          load: () => mergeSearches<TagSearchItems[number]>(query.perPage, [
            tags.find({ ...query, sort: "name", direction: "asc" }),
            tags.findFiltered({ findFilter: aliasFindFilter, objectFilter: { aliasesCriterion: aliasCriterion } }),
          ]),
          mapItems: (items: unknown[]) => (items as TagSearchItems).map((item) => ({
            id: item.id,
            title: item.name,
            subtitle: item.aliases?.length ? `Aliases: ${item.aliases.slice(0, 3).join(", ")}` : item.description || undefined,
            route: { page: "tag", id: item.id },
            hostType: "tag" as const,
          })),
        }] : []),
        ...(readableEntities.galleries ? [{
          key: "galleries",
          label: "Galleries",
          icon: FolderOpen,
          hostType: "gallery" as const,
          load: () => galleries.find({ ...query, sort: "title", direction: "asc" }),
          mapItems: (items: unknown[]) => (items as GallerySearchItems).map((item) => ({
            id: item.id,
            title: item.title || `Gallery ${item.id}`,
            subtitle: item.studioName || item.date || undefined,
            route: { page: "gallery", id: item.id },
            hostType: "gallery" as const,
          })),
        }] : []),
        ...(readableEntities.images ? [{
          key: "images",
          label: "Images",
          icon: ImageIcon,
          hostType: "image" as const,
          load: () => images.find({ ...query, sort: "title", direction: "asc" }),
          mapItems: (items: unknown[]) => (items as ImageSearchItems).map((item) => ({
            id: item.id,
            title: getImageDisplayTitle(item),
            subtitle: item.studioName || item.date || undefined,
            route: { page: "image", id: item.id },
            hostType: "image" as const,
          })),
        }] : []),
        ...(readableEntities.groups ? [{
          key: "groups",
          label: "Groups",
          icon: Layers,
          hostType: "group" as const,
          load: () => groups.find({ ...query, sort: "name", direction: "asc" }),
          mapItems: (items: unknown[]) => (items as GroupSearchItems).map((item) => ({
            id: item.id,
            title: item.name,
            subtitle: item.studioName || item.date || undefined,
            route: { page: "group", id: item.id },
            hostType: "group" as const,
          })),
        }] : []),
      ];

      const results = await Promise.allSettled(searches.map((search) => search.load()));
      return searches.flatMap((search, index) => {
        const result = results[index];
        if (result?.status !== "fulfilled") {
          return [];
        }

        const items = search.mapItems(result.value.items);
        return items.length > 0
          ? [{ key: search.key, label: search.label, icon: search.icon, items }]
          : [];
      });
    },
  });

  const flatResults = useMemo(() => (data ?? []).flatMap((group) => group.items), [data]);

  useEffect(() => {
    if (!open || deferredTerm.length < 2 || isFetching || searchableLabels.length === 0) {
      return;
    }

    const resultCount = flatResults.length;
    const searchKey = `${deferredTerm}|${searchableLabels.join(",")}|${resultCount}`;
    if (lastTrackedSearchKey.current === searchKey) {
      return;
    }

    lastTrackedSearchKey.current = searchKey;
    trackInteraction({
      hostType: "search",
      kind: "searchQuery",
      meta: {
        query: deferredTerm,
        resultCount,
        scopes: searchableLabels,
        source: "globalSearch",
      },
    });
  }, [deferredTerm, flatResults.length, isFetching, open, searchableLabels]);

  const handleSelect = (item: SearchGroup["items"][number], rank: number) => {
    trackInteraction({
      hostType: item.hostType,
      hostId: item.id,
      kind: "searchSelect",
      meta: {
        query: deferredTerm,
        rank,
        source: "globalSearch",
      },
    });
    navigate(item.route);
    setOpen(false);
    setTerm("");
  };

  const renderResults = () => (
    <>
      <div className="border-b border-border px-3 py-2 text-[11px] uppercase tracking-wider text-muted">
        Global Search
      </div>
      {searchableLabels.length === 0 ? (
        <div className="px-4 py-6 text-sm text-secondary">No searchable libraries are available for this account.</div>
      ) : deferredTerm.length < 2 ? (
        <div className="px-4 py-6 text-sm text-secondary">Type at least 2 characters to search {searchableLabels.join(", ")}.</div>
      ) : isFetching ? (
        <div className="flex items-center gap-2 px-4 py-6 text-sm text-secondary">
          <Loader2 className="h-4 w-4 animate-spin" /> Searching...
        </div>
      ) : !data || data.length === 0 ? (
        <div className="px-4 py-6 text-sm text-secondary">No results found for &ldquo;{deferredTerm}&rdquo;.</div>
      ) : (
        <div className="max-h-[28rem] overflow-y-auto">
          {data.map((group) => {
            const Icon = group.icon;
            return (
              <div key={group.key} className="border-b border-border last:border-b-0">
                <div className="flex items-center gap-2 px-3 py-2 text-[11px] font-semibold uppercase tracking-wider text-muted">
                  <Icon className="h-3.5 w-3.5" />
                  {group.label}
                </div>
                <div className="pb-2">
                  {group.items.map((item) => (
                    <button
                      key={`${group.key}-${item.id}`}
                      onClick={() => handleSelect(item, flatResults.findIndex((result) => result.hostType === item.hostType && result.id === item.id) + 1)}
                      className="flex w-full items-start gap-3 px-3 py-2 text-left hover:bg-surface"
                    >
                      <Icon className="mt-0.5 h-4 w-4 shrink-0 text-accent" />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate text-sm text-foreground">{item.title}</span>
                        {item.subtitle && <span className="block truncate text-xs text-secondary">{item.subtitle}</span>}
                      </span>
                    </button>
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </>
  );

  const renderedPanel = open && typeof document !== "undefined" ? createPortal(
    <>
      <div className="pointer-events-none fixed inset-0 z-40 bg-black/60" />
      <div ref={panelRef}>
        {/* Mobile: full-width search input dropdown */}
        <div className="md:hidden fixed left-4 right-4 top-14 z-[60]">
          <div className="overflow-hidden rounded-lg border border-border bg-surface shadow-xl">
            <div className="p-2 border-b border-border">
              <div className="relative">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
                <input
                  value={term}
                  onChange={(event) => {
                    setTerm(event.target.value);
                  }}
                  onKeyDown={(event) => {
                    if (event.key === "Escape") {
                      setOpen(false);
                      return;
                    }
                    if (event.key === "Enter" && flatResults.length > 0) {
                      event.preventDefault();
                      handleSelect(flatResults[0], 1);
                    }
                  }}
                  placeholder="Search all..."
                  className="w-full rounded-lg border border-border bg-input py-1.5 pl-9 pr-3 text-sm text-foreground placeholder:text-muted focus:border-accent focus:outline-none"
                />
              </div>
            </div>
            {renderResults()}
          </div>
        </div>

        {/* Desktop: results dropdown */}
        {desktopPanelStyle ? (
          <div
            className="hidden md:block fixed z-[60] overflow-hidden rounded-lg border border-border bg-surface shadow-xl"
            style={desktopPanelStyle}
          >
            {renderResults()}
          </div>
        ) : null}
      </div>
    </>,
    document.body,
  ) : null;

  return (
    <div ref={containerRef} className="relative">
      {/* Mobile: icon button that opens the search */}
      <button
        onClick={() => setOpen(!open)}
        className="md:hidden p-1.5 rounded border border-border bg-input text-secondary hover:text-foreground hover:border-accent"
        title="Search"
      >
        <Search className="h-4 w-4" />
      </button>

      {/* Desktop: always-visible search input */}
      <div className="relative z-[60] hidden md:block">
        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
        <input
          value={term}
          onChange={(event) => {
            setTerm(event.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={(event) => {
            if (event.key === "Escape") {
              setOpen(false);
              return;
            }
            if (event.key === "Enter" && flatResults.length > 0) {
              event.preventDefault();
              handleSelect(flatResults[0], 1);
            }
          }}
          placeholder="Search all..."
          className="w-72 rounded-lg border border-border bg-input py-1.5 pl-9 pr-3 text-sm text-foreground placeholder:text-muted focus:border-accent focus:outline-none"
        />
      </div>

      {open && <div className="pointer-events-none fixed inset-0 z-50 bg-black/60" />}
      {renderedPanel}
    </div>
  );
}