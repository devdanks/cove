import { useCallback, useEffect, useRef, useState, type ImgHTMLAttributes, type KeyboardEvent, type MouseEvent, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { useQuery } from "@tanstack/react-query";
import { scenes, images, performers, galleries, studios, groups, audios, texts, entityImages } from "../api/client";
import type { AffinityHostType, Audio, EntityEngagement, Face, FaceAppearance, Gallery, Group, GroupSummary, Image, PerformerSummary, Scene, SegmentRecord, Studio, Tag as TagType, TextDocument } from "../api/types";
import { formatDuration, formatFileSize, getResolutionLabel } from "./shared";
import { RatingBanner, RatingBadge } from "./Rating";
import { BookOpenText, Building2, FileText, Fingerprint, FolderOpen, GripVertical, Headphones, Layers, Link2, Tag, User, Film, Box, Images as ImagesIcon, Heart, Eye, ThumbsUp, Mic2, MonitorPlay, PlayCircle, Merge } from "lucide-react";
import { createRouteLinkProps, createNestedRouteLinkProps } from "./cardNavigation";
import { CardSelectionToggle, RouteCardLinkOverlay } from "./RouteCardLinkOverlay";
import { getImageDisplayTitle } from "../utils/imageDisplay";
import { getAudioDisplayTitle, getTextDisplayTitle, pickPrimaryTextFile } from "../utils/audioTextDisplay";
import { BookmarkButton } from "./BookmarkButton";
import { useOptionalAppConfig } from "../state/AppConfigContext";

function CoverImage({ className = "", ...props }: ImgHTMLAttributes<HTMLImageElement>) {
  const appConfig = useOptionalAppConfig();
  const fitClass = appConfig?.config?.ui.imageObjectFit === "contain" ? "object-contain" : "object-cover";
  return <img {...props} className={`${className} ${fitClass}`.trim()} />;
}

function createNestedEntityNavigationHandlers<T extends HTMLAnchorElement>(route: { page: string; id: number }, onNavigate?: (route: any) => void) {
  return createNestedRouteLinkProps<T>(route, () => onNavigate?.(route));
}

interface EntityTileDragHandleProps {
  tabIndex: number;
  role: "button";
  "aria-label": string;
  "aria-pressed": boolean;
  onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
}

interface EntityTileFrameProps {
  route: { page: string; id: number };
  label: string;
  onClick: () => void;
  media: ReactNode;
  body: ReactNode;
  footer?: ReactNode;
  children?: ReactNode;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
  mediaClassName?: string;
  bodyClassName?: string;
  extensionClassName?: string;
  className?: string;
  dragHandleProps?: EntityTileDragHandleProps;
  isDragging?: boolean;
  isOver?: boolean;
}

function EntityTileFrame({
  route,
  label,
  onClick,
  media,
  body,
  footer,
  children,
  selected,
  onSelect,
  selecting,
  mediaClassName = "aspect-video bg-gradient-to-br from-surface to-card",
  bodyClassName = "p-2.5",
  extensionClassName = "px-2 py-1.5",
  className = "",
  dragHandleProps,
  isDragging,
  isOver,
}: EntityTileFrameProps) {
  return (
    <div
      onClick={selecting ? onClick : undefined}
      className={`entity-card group relative flex h-full cursor-pointer flex-col overflow-hidden rounded-lg border bg-card text-left transition-colors ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"} ${isDragging ? "opacity-50" : ""} ${isOver ? "outline outline-2 outline-accent" : ""} ${className}`}
    >
      <RouteCardLinkOverlay route={route} onClick={onClick} label={label} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} />
      <div className={`relative flex shrink-0 items-center justify-center overflow-hidden ${mediaClassName}`}>
        {media}
        {(selected !== undefined || selecting) ? <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} /> : null}
      </div>
      <div className={`card-body flex flex-1 flex-col gap-1 border-t border-border/50 ${bodyClassName}`}>{body}</div>
      {footer ? (
        <>
          <hr className="border-border/50 my-0" />
          <div className="relative z-10 flex min-h-[28px] flex-wrap items-center justify-center gap-1 rounded-b px-2 py-1.5 card-popovers">
            {footer}
          </div>
        </>
      ) : null}
      {children ? <div className={`relative z-10 border-t border-border/50 ${extensionClassName}`}>{children}</div> : null}
      {dragHandleProps ? (
        <span
          {...dragHandleProps}
          onClick={(event) => event.stopPropagation()}
          className="absolute bottom-1.5 right-1.5 z-20 inline-flex h-7 w-7 cursor-grab items-center justify-center rounded bg-black/70 text-white opacity-0 transition-opacity hover:bg-black/85 active:cursor-grabbing group-hover:opacity-100 focus:opacity-100"
          title="Drag to reorder"
        >
          <GripVertical className="h-4 w-4" />
        </span>
      ) : null}
    </div>
  );
}

export function LikeCounter({ count }: { count: number }) {
  return (
    <span className="flex items-center gap-1 p-1 text-muted" title={`Likes: ${count}`}>
      <ThumbsUp className="h-3.5 w-3.5 fill-accent text-accent" />
      <span className="text-xs">{count}</span>
    </span>
  );
}

export function CardFavoriteButton(props: { hostType: AffinityHostType; hostId: number; favorite: boolean }) {
  if (!props.favorite) {
    return null;
  }

  return (
    <span className="inline-flex min-h-7 items-center justify-center p-1 text-red-400" title="Favorite" aria-label="Favorite">
      <Heart className="h-4 w-4 fill-current" />
    </span>
  );
}

export function PerformerPreviewGrid({ performers: performerItems, onNavigate }: { performers: Array<{ id: number; name: string; imagePath?: string | null }>; onNavigate?: (route: any) => void }) {
  return (
    <div className="grid grid-cols-2 gap-2">
      {performerItems.map((performer) => {
        const navigationHandlers = createNestedEntityNavigationHandlers<HTMLAnchorElement>({ page: "performer", id: performer.id }, onNavigate);

        return (
          <a
            key={performer.id}
            {...navigationHandlers}
            className="flex flex-col items-center gap-1.5 rounded p-1.5 text-center transition-colors hover:bg-card-hover group/perf"
          >
            <div className="w-20 h-28 rounded overflow-hidden bg-surface flex-shrink-0">
              {performer.imagePath ? (
                <CoverImage src={performer.imagePath} alt="" className="w-full h-full" loading="lazy" />
              ) : (
                <div className="w-full h-full flex items-center justify-center"><User className="w-8 h-8 text-muted" /></div>
              )}
            </div>
            <span className="text-xs text-accent group-hover/perf:underline truncate w-full font-medium">{performer.name}</span>
          </a>
        );
      })}
    </div>
  );
}

export function GalleryPreviewList({ galleries: galleryItems, onNavigate }: { galleries: Array<{ id: number; title?: string | null; date?: string | null; coverPath?: string | null }>; onNavigate?: (route: any) => void }) {
  return (
    <div className="space-y-1">
      {galleryItems.map((gallery) => {
        const navigationHandlers = createNestedEntityNavigationHandlers<HTMLAnchorElement>({ page: "gallery", id: gallery.id }, onNavigate);

        return (
          <a
            key={gallery.id}
            {...navigationHandlers}
            className="flex w-full items-center gap-2 rounded px-1.5 py-1 text-left transition-colors hover:bg-card-hover"
          >
            <div className="h-12 w-12 overflow-hidden rounded bg-surface flex-shrink-0">
              {gallery.coverPath ? (
                <CoverImage src={gallery.coverPath} alt="" className="h-full w-full" loading="lazy" />
              ) : (
                <div className="flex h-full w-full items-center justify-center"><FolderOpen className="w-4 h-4 text-muted" /></div>
              )}
            </div>
            <div className="min-w-0 flex-1">
              <div className="truncate text-xs font-medium text-accent">{gallery.title || `Gallery ${gallery.id}`}</div>
              {gallery.date && <div className="truncate text-[10px] text-muted">{gallery.date}</div>}
            </div>
          </a>
        );
      })}
    </div>
  );
}

function EntityLinkIcon({ page, color }: { page: string; color?: string | null }) {
  if (page === "tag" && color) {
    return <span className="h-3 w-3 rounded-full border border-border" style={{ backgroundColor: color }} />;
  }

  const className = "h-3.5 w-3.5 shrink-0 text-muted";
  switch (page) {
    case "audio":
      return <Headphones className={className} />;
    case "gallery":
      return <ImagesIcon className={className} />;
    case "group":
      return <Layers className={className} />;
    case "image":
      return <ImagesIcon className={className} />;
    case "performer":
      return <User className={className} />;
    case "scene":
      return <Film className={className} />;
    case "studio":
      return <Building2 className={className} />;
    case "tag":
      return <Tag className={className} />;
    case "text":
      return <FileText className={className} />;
    default:
      return <Link2 className={className} />;
  }
}

function EntityLinkList({ items, page, onNavigate }: { items: Array<{ id: number; label: string; color?: string | null }>; page: string; onNavigate?: (route: any) => void }) {
  return (
    <div className="space-y-1">
      {items.map((item) => {
        const navigationHandlers = createNestedEntityNavigationHandlers<HTMLAnchorElement>({ page, id: item.id }, onNavigate);
        return (
          <a key={`${page}-${item.id}`} {...navigationHandlers} className="flex items-center gap-2 rounded px-1.5 py-1 text-xs font-medium text-accent transition-colors hover:bg-card-hover hover:underline">
            <EntityLinkIcon page={page} color={item.color} />
            <span className="min-w-0 truncate">{item.label}</span>
          </a>
        );
      })}
    </div>
  );
}

export function EntityReferencePopovers({
  performers: performerItems = [],
  tags: tagItems = [],
  groups: groupItems = [],
  studio,
  onNavigate,
  className = "",
}: {
  performers?: PerformerSummary[];
  tags?: TagType[];
  groups?: GroupSummary[];
  studio?: { id?: number | null; name?: string | null } | null;
  onNavigate?: (route: any) => void;
  className?: string;
}) {
  const studioName = studio?.name?.trim();
  const studioId = studio?.id ?? null;
  const tagLinks = tagItems.map((tag) => ({ id: tag.id, label: tag.name, color: tag.color ?? tag.tagGroupColor }));
  const groupLinks = groupItems.map((group) => ({ id: group.id, label: group.name }));

  if (!studioName && performerItems.length === 0 && tagLinks.length === 0 && groupLinks.length === 0) {
    return null;
  }

  return (
    <div className={`relative z-[2] flex flex-wrap items-center gap-1 ${className}`} data-entity-reference-popovers>
      {studioName ? (
        <PopoverButton icon={<Building2 className="h-3.5 w-3.5" />} count={1} title="Studio" preferBelow>
          {studioId ? (
            <EntityLinkList items={[{ id: studioId, label: studioName }]} page="studio" onNavigate={onNavigate} />
          ) : (
            <div className="px-1 text-xs text-foreground">{studioName}</div>
          )}
        </PopoverButton>
      ) : null}
      {performerItems.length > 0 ? (
        <PopoverButton icon={<User className="h-3.5 w-3.5" />} count={performerItems.length} title="Performers" wide preferBelow>
          <PerformerPreviewGrid performers={performerItems} onNavigate={onNavigate} />
        </PopoverButton>
      ) : null}
      {tagLinks.length > 0 ? (
        <PopoverButton icon={<Tag className="h-3.5 w-3.5" />} count={tagLinks.length} title="Tags" preferBelow>
          <EntityLinkList items={tagLinks} page="tag" onNavigate={onNavigate} />
        </PopoverButton>
      ) : null}
      {groupLinks.length > 0 ? (
        <PopoverButton icon={<Layers className="h-3.5 w-3.5" />} count={groupLinks.length} title="Groups" preferBelow>
          <EntityLinkList items={groupLinks} page="group" onNavigate={onNavigate} />
        </PopoverButton>
      ) : null}
    </div>
  );
}

// ===== PopoverButton (shared hover popover) =====

export function PopoverButton({ icon, count, title, children, wide, preferBelow }: { icon: React.ReactNode; count: number; title: string; children?: React.ReactNode; wide?: boolean; preferBelow?: boolean }) {
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<HTMLDivElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);
  const enterTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const leaveTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const [popoverStyle, setPopoverStyle] = useState<React.CSSProperties>({});

  const handleMouseEnter = useCallback(() => {
    clearTimeout(leaveTimer.current);
    enterTimer.current = setTimeout(() => {
      if (buttonRef.current) {
        const rect = buttonRef.current.getBoundingClientRect();
        const spaceBelow = window.innerHeight - rect.bottom;
        const showBelow = preferBelow ? (spaceBelow > 100) : (rect.top < 220);
        const style: React.CSSProperties = { position: "fixed", zIndex: 9999 };
        if (showBelow) { style.top = rect.bottom + 4; } else { style.bottom = window.innerHeight - rect.top + 4; }
        const centerX = rect.left + rect.width / 2;
        const popWidth = wide ? 300 : 220;
        let left = centerX - popWidth / 2;
        if (left < 8) left = 8;
        if (left + popWidth > window.innerWidth - 8) left = window.innerWidth - 8 - popWidth;
        style.left = left;
        setPopoverStyle(style);
      }
      setOpen(true);
    }, 200);
  }, [preferBelow, wide]);

  const handleMouseLeave = useCallback(() => {
    clearTimeout(enterTimer.current);
    leaveTimer.current = setTimeout(() => setOpen(false), 200);
  }, []);

  useEffect(() => () => { clearTimeout(enterTimer.current); clearTimeout(leaveTimer.current); }, []);

  return (
    <div className="relative" ref={buttonRef} onMouseEnter={handleMouseEnter} onMouseLeave={handleMouseLeave}>
      <button
        className="flex items-center gap-1 px-1.5 py-1 text-secondary hover:text-accent rounded text-xs transition-colors"
        title={title}
        onClick={(e) => e.stopPropagation()}
        onMouseDown={(e) => e.stopPropagation()}
        onAuxClick={(e) => e.stopPropagation()}
      >
        {icon}
        <span className="font-medium">{count}</span>
      </button>
      {open && children && createPortal(
        <div
          ref={popoverRef}
          style={popoverStyle}
          className={`bg-surface border border-border rounded-lg shadow-2xl shadow-black/40 p-2.5 ${wide ? "min-w-[280px] max-w-[360px]" : "min-w-[180px] max-w-[min(280px,calc(100vw-1rem))]"} max-h-[320px] overflow-y-auto`}
          onClick={(e) => e.stopPropagation()}
          onMouseEnter={() => { clearTimeout(leaveTimer.current); }}
          onMouseLeave={handleMouseLeave}
        >
          <div className="text-xs uppercase tracking-wider text-muted font-semibold mb-1.5 px-1">{title}</div>
          {children}
        </div>,
        document.body
      )}
    </div>
  );
}

// ===== Lazy scene list popover content =====

export function ScenesPopoverContent({ filter }: { filter: Record<string, string | number> }) {
  const { data, isLoading } = useQuery({
    queryKey: ["scenes-popover", filter],
    queryFn: () => scenes.find({ perPage: 10, sort: "date", direction: "desc" }, filter),
  });
  if (isLoading) return <p className="text-[11px] text-muted px-1">LoadingÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦</p>;
  const items = data?.items ?? [];
  if (items.length === 0) return <p className="text-[11px] text-muted px-1">No scenes</p>;
  return (
    <div className="space-y-1">
      {items.map((s) => (
        <div key={s.id} className="flex items-center gap-2 px-1 py-0.5 rounded hover:bg-card">
          <img src={scenes.screenshotUrl(s.id, s.updatedAt)} alt="" className="w-12 h-7 rounded object-cover flex-shrink-0 bg-surface" loading="lazy" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
          <span className="text-[11px] text-foreground truncate">{s.title || "Untitled"}</span>
        </div>
      ))}
      {(data?.totalCount ?? 0) > 10 && (
        <p className="text-[10px] text-muted px-1 pt-0.5">+ {(data!.totalCount) - 10} more</p>
      )}
    </div>
  );
}

// ===== Lazy image list popover content =====

export function ImagesPopoverContent({ filter }: { filter: Record<string, string | number> }) {
  const { data, isLoading } = useQuery({
    queryKey: ["images-popover", filter],
    queryFn: () => images.find({ perPage: 10, sort: "created_at", direction: "desc" }, filter),
  });
  if (isLoading) return <p className="text-[11px] text-muted px-1">LoadingÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦</p>;
  const items = data?.items ?? [];
  if (items.length === 0) return <p className="text-[11px] text-muted px-1">No images</p>;
  return (
    <div className="grid grid-cols-3 gap-1">
      {items.map((img) => (
        <div key={img.id} className="aspect-square rounded overflow-hidden bg-surface">
          <CoverImage src={images.thumbnailUrl(img.id)} alt="" className="w-full h-full" loading="lazy" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
        </div>
      ))}
      {(data?.totalCount ?? 0) > 10 && (
        <p className="col-span-3 text-[10px] text-muted px-1 pt-0.5">+ {(data!.totalCount) - 10} more</p>
      )}
    </div>
  );
}

// ===== Lazy audio list popover content =====

export function AudiosPopoverContent({ filter }: { filter: Record<string, string | number> }) {
  const { data, isLoading } = useQuery({
    queryKey: ["audios-popover", filter],
    queryFn: () => audios.find({ perPage: 10, sort: "created_at", direction: "desc" }, filter),
  });
  if (isLoading) return <p className="text-[11px] text-muted px-1">Loading...</p>;
  const items = data?.items ?? [];
  if (items.length === 0) return <p className="text-[11px] text-muted px-1">No audio</p>;
  return (
    <div className="space-y-1">
      {items.map((audio) => (
        <div key={audio.id} className="flex items-center gap-2 px-1 py-0.5 rounded hover:bg-card">
          <Headphones className="h-3.5 w-3.5 flex-shrink-0 text-muted" />
          <span className="truncate text-[11px] text-foreground">{getAudioDisplayTitle(audio)}</span>
        </div>
      ))}
      {(data?.totalCount ?? 0) > 10 && (
        <p className="text-[10px] text-muted px-1 pt-0.5">+ {(data!.totalCount) - 10} more</p>
      )}
    </div>
  );
}

// ===== Lazy text list popover content =====

export function TextsPopoverContent({ filter }: { filter: Record<string, string | number> }) {
  const { data, isLoading } = useQuery({
    queryKey: ["texts-popover", filter],
    queryFn: () => texts.find({ perPage: 10, sort: "created_at", direction: "desc" }, filter),
  });
  if (isLoading) return <p className="text-[11px] text-muted px-1">Loading...</p>;
  const items = data?.items ?? [];
  if (items.length === 0) return <p className="text-[11px] text-muted px-1">No texts</p>;
  return (
    <div className="space-y-1">
      {items.map((text) => (
        <div key={text.id} className="flex items-center gap-2 px-1 py-0.5 rounded hover:bg-card">
          <FileText className="h-3.5 w-3.5 flex-shrink-0 text-muted" />
          <span className="truncate text-[11px] text-foreground">{getTextDisplayTitle(text)}</span>
        </div>
      ))}
      {(data?.totalCount ?? 0) > 10 && (
        <p className="text-[10px] text-muted px-1 pt-0.5">+ {(data!.totalCount) - 10} more</p>
      )}
    </div>
  );
}

// ===== Lazy performer list popover content =====

export function PerformersPopoverContent({ filter }: { filter: Record<string, string | number> }) {
  const { data, isLoading } = useQuery({
    queryKey: ["performers-popover", filter],
    queryFn: () => performers.find({ perPage: 10, sort: "name", direction: "asc" }, filter),
  });
  if (isLoading) return <p className="text-[11px] text-muted px-1">LoadingÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦</p>;
  const items = data?.items ?? [];
  if (items.length === 0) return <p className="text-[11px] text-muted px-1">No performers</p>;
  return <PerformerPreviewGrid performers={items} />;
}

// ===== Lazy gallery list popover content =====

export function GalleriesPopoverContent({ filter }: { filter: Record<string, string | number> }) {
  const { data, isLoading } = useQuery({
    queryKey: ["galleries-popover", filter],
    queryFn: () => galleries.find({ perPage: 10, sort: "title", direction: "asc" }, filter),
  });
  if (isLoading) return <p className="text-[11px] text-muted px-1">LoadingÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦</p>;
  const items = data?.items ?? [];
  if (items.length === 0) return <p className="text-[11px] text-muted px-1">No galleries</p>;
  return <GalleryPreviewList galleries={items} />;
}

// ===== Lazy studio list popover content =====

export function StudiosPopoverContent({ filter }: { filter: Record<string, string | number> }) {
  const { data, isLoading } = useQuery({
    queryKey: ["studios-popover", filter],
    queryFn: () => studios.find({ perPage: 10, sort: "name", direction: "asc" }, filter),
  });
  if (isLoading) return <p className="text-[11px] text-muted px-1">LoadingÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦</p>;
  const items = data?.items ?? [];
  if (items.length === 0) return <p className="text-[11px] text-muted px-1">No studios</p>;
  return (
    <div className="space-y-1">
      {items.map((s) => {
        const navigationHandlers = createNestedEntityNavigationHandlers<HTMLAnchorElement>({ page: "studio", id: s.id });

        return (
          <a
            key={s.id}
            {...navigationHandlers}
            className="flex items-center gap-2 px-1 py-0.5 rounded hover:bg-card"
          >
            {s.imagePath ? <img src={s.imagePath} alt="" className="w-10 h-7 rounded object-contain flex-shrink-0 bg-surface" loading="lazy" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} /> : <Building2 className="w-4 h-4 text-muted flex-shrink-0" />}
            <span className="text-[11px] text-accent hover:underline truncate">{s.name}</span>
          </a>
        );
      })}
      {(data?.totalCount ?? 0) > 10 && (
        <p className="text-[10px] text-muted px-1 pt-0.5">+ {(data!.totalCount) - 10} more</p>
      )}
    </div>
  );
}

// ===== Lazy group list popover content =====

export function GroupsPopoverContent({ filter }: { filter: Record<string, string | number> }) {
  const { data, isLoading } = useQuery({
    queryKey: ["groups-popover", filter],
    queryFn: () => groups.find({ perPage: 10, sort: "name", direction: "asc" }, filter),
  });
  if (isLoading) return <p className="text-[11px] text-muted px-1">LoadingÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¦</p>;
  const items = data?.items ?? [];
  if (items.length === 0) return <p className="text-[11px] text-muted px-1">No groups</p>;
  return (
    <div className="space-y-1">
      {items.map((g) => (
        <div key={g.id} className="flex items-center gap-2 px-1 py-0.5 rounded hover:bg-card">
          {g.frontImagePath ? <CoverImage src={g.frontImagePath} alt="" className="w-7 h-10 rounded flex-shrink-0 bg-surface" loading="lazy" onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} /> : <Layers className="w-4 h-4 text-muted flex-shrink-0" />}
          <span className="text-[11px] text-foreground truncate">{g.name}</span>
        </div>
      ))}
      {(data?.totalCount ?? 0) > 10 && (
        <p className="text-[10px] text-muted px-1 pt-0.5">+ {(data!.totalCount) - 10} more</p>
      )}
    </div>
  );
}

// ===== SceneCardPopovers =====

export function SceneCardPopovers({ scene, engagement, onNavigate }: { scene: Scene; engagement?: EntityEngagement; onNavigate?: (r: any) => void }) {
  const likeCount = engagement?.likeCount ?? 0;
  const hasFavorite = engagement?.isFavorite === true;
  const hasPopovers =
    scene.tags.length > 0 || scene.performers.length > 0 || scene.groups.length > 0 ||
    scene.galleries.length > 0 || likeCount > 0 || hasFavorite || scene.organized;
  return (
    <>
      <hr className="border-border/50 my-0" />
      <div className="relative z-10 flex flex-wrap items-center justify-center gap-1 px-2 py-1.5 rounded-b card-popovers min-h-[28px]">
        {!hasPopovers && <span className="text-[10px] text-muted/30 select-none">&nbsp;</span>}
        {scene.performers.length > 0 && (
          <PopoverButton icon={<User className="w-3.5 h-3.5" />} count={scene.performers.length} title="Performers" wide preferBelow>
            <PerformerPreviewGrid performers={scene.performers} onNavigate={onNavigate} />
          </PopoverButton>
        )}
        {scene.tags.length > 0 && (
          <PopoverButton icon={<Tag className="w-3.5 h-3.5" />} count={scene.tags.length} title="Tags" preferBelow>
            <EntityLinkList items={scene.tags.map((tag: any) => ({ id: tag.id, label: tag.name, color: tag.color ?? tag.tagGroupColor }))} page="tag" onNavigate={onNavigate} />
          </PopoverButton>
        )}
        {likeCount > 0 && (
          <LikeCounter count={likeCount} />
        )}
        {hasFavorite ? (
          <CardFavoriteButton hostType="scene" hostId={scene.id} favorite={engagement?.isFavorite ?? false} />
        ) : null}
        {scene.groups.length > 0 && (
          <PopoverButton icon={<Layers className="w-3.5 h-3.5" />} count={scene.groups.length} title="Groups" preferBelow>
            <EntityLinkList items={scene.groups.map((group: any) => ({ id: group.id, label: group.name }))} page="group" onNavigate={onNavigate} />
          </PopoverButton>
        )}
        {scene.galleries.length > 0 && (
          <PopoverButton icon={<ImagesIcon className="w-3.5 h-3.5" />} count={scene.galleries.length} title="Galleries" preferBelow>
            <EntityLinkList items={scene.galleries.map((gallery: any) => ({ id: gallery.id, label: gallery.title || "Untitled" }))} page="gallery" onNavigate={onNavigate} />
          </PopoverButton>
        )}
        {scene.organized && (
          <span className="p-1 text-muted" title="Organized"><Box className="w-3.5 h-3.5" /></span>
        )}
      </div>
    </>
  );
}

// ===== PerformerBadge (hover popover with performer image) =====

function PerformerBadge({
  performer,
  navigationHandlers,
}: {
  performer: { id: number; name: string; imagePath?: string | null };
  navigationHandlers: ReturnType<typeof createNestedRouteLinkProps<HTMLAnchorElement>>;
}) {
  const badgeRef = useRef<HTMLAnchorElement>(null);
  const [hover, setHover] = useState(false);
  const [style, setStyle] = useState<React.CSSProperties>({});
  const enterTimer = useRef<ReturnType<typeof setTimeout>>(undefined);
  const leaveTimer = useRef<ReturnType<typeof setTimeout>>(undefined);

  const onEnter = useCallback(() => {
    clearTimeout(leaveTimer.current);
    enterTimer.current = setTimeout(() => {
      if (badgeRef.current) {
        const rect = badgeRef.current.getBoundingClientRect();
        const s: React.CSSProperties = { position: "fixed", zIndex: 9999 };
        const spaceBelow = window.innerHeight - rect.bottom;
        if (spaceBelow > 180) { s.top = rect.bottom + 4; } else { s.bottom = window.innerHeight - rect.top + 4; }
        let left = rect.left + rect.width / 2 - 64;
        if (left < 8) left = 8;
        if (left + 128 > window.innerWidth - 8) left = window.innerWidth - 136;
        s.left = left;
        setStyle(s);
      }
      setHover(true);
    }, 300);
  }, []);

  const onLeave = useCallback(() => {
    clearTimeout(enterTimer.current);
    leaveTimer.current = setTimeout(() => setHover(false), 200);
  }, []);

  useEffect(() => () => { clearTimeout(enterTimer.current); clearTimeout(leaveTimer.current); }, []);

  return (
    <>
      <a ref={badgeRef} {...navigationHandlers} onMouseEnter={onEnter} onMouseLeave={onLeave}
        className="performer-badge flex items-center gap-1 rounded-full border border-border bg-surface px-1.5 py-0.5 min-w-0 hover:border-accent/50 transition-colors">
        {performer.imagePath ? (
          <CoverImage src={performer.imagePath} alt="" className="h-4 w-4 rounded-full flex-shrink-0" loading="lazy" />
        ) : (
          <User className="h-3.5 w-3.5 text-muted flex-shrink-0" />
        )}
        <span className="max-w-[80px] truncate text-[10px] text-secondary hover:text-accent">{performer.name}</span>
      </a>
      {hover && createPortal(
        <div style={style}
          className="bg-surface border border-border rounded-lg shadow-2xl shadow-black/40 p-2 w-[128px]"
          onClick={(e) => e.stopPropagation()}
          onMouseEnter={() => clearTimeout(leaveTimer.current)}
          onMouseLeave={onLeave}
        >
          <div className="w-full aspect-[2/3] rounded overflow-hidden bg-card mb-1.5">
            {performer.imagePath ? (
              <CoverImage src={performer.imagePath} alt="" className="w-full h-full" loading="lazy" />
            ) : (
              <div className="w-full h-full flex items-center justify-center"><User className="w-8 h-8 text-muted" /></div>
            )}
          </div>
          <p className="text-xs text-foreground font-medium text-center truncate">{performer.name}</p>
        </div>,
        document.body
      )}
    </>
  );
}

// ===== SceneCard (redesigned - cleaner, performer badges, 2-line title) =====

export function SceneCard({ scene, engagement, onClick, selected, onSelect, onNavigate, selecting, onQuickView, bookmarkInitiallySaved }: { scene: Scene; engagement?: EntityEngagement; onClick: () => void; selected?: boolean; onSelect?: () => void; selecting?: boolean; onNavigate?: (r: any) => void; onQuickView?: () => void; bookmarkInitiallySaved?: boolean }) {
  const appConfig = useOptionalAppConfig();
  const file = scene.files[0];
  const clipDuration = typeof scene.clipStartSec === "number" && typeof scene.clipEndSec === "number"
    ? Math.max(0, scene.clipEndSec - scene.clipStartSec)
    : undefined;
  const duration = clipDuration ?? file?.duration ?? 0;
  const resLabel = file ? getResolutionLabel(file.width, file.height) : null;
  const coverUrl = entityImages.sceneCoverUrl(scene.id, scene.updatedAt, 1280);
  const previewUrl = scenes.previewUrl(scene.id);
  const videoRef = useRef<HTMLVideoElement>(null);
  const visibleResumeTime = typeof scene.clipStartSec === "number" && typeof engagement?.resumeTime === "number"
    ? Math.max(0, engagement.resumeTime - scene.clipStartSec)
    : engagement?.resumeTime;
  const progressPercent = duration > 0 && visibleResumeTime ? Math.min(100, (visibleResumeTime / duration) * 100) : 0;
  const cardTitle = scene.title || file?.basename || "Untitled";
  const [scrubSeconds, setScrubSeconds] = useState<number | null>(null);
  const scrubPercent = duration > 0 && scrubSeconds != null ? Math.min(100, Math.max(0, ((scrubSeconds - (scene.clipStartSec ?? 0)) / duration) * 100)) : 0;
  const scrubTimestamp = scrubSeconds != null ? formatDuration(scrubSeconds) : null;
  const scrubTimestampPercent = scrubSeconds != null ? Math.min(88, Math.max(12, scrubPercent)) : 0;
  const scrubImageUrl = scrubSeconds != null ? scenes.screenshotUrl(scene.id, scene.updatedAt, scrubSeconds) : null;
  const scenePreviewObjectFit = appConfig?.config?.ui.videoObjectFit === "contain" ? "contain" : "cover";

  const updateScrubPreview = useCallback((event: MouseEvent<HTMLDivElement>) => {
    if (duration <= 0) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const percent = Math.min(1, Math.max(0, (event.clientX - rect.left) / Math.max(1, rect.width)));
    const clipStart = scene.clipStartSec ?? 0;
    const nextSeconds = Math.round(clipStart + percent * duration);
    setScrubSeconds((current) => current === nextSeconds ? current : nextSeconds);
  }, [duration, scene.clipStartSec]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    const observer = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.intersectionRatio > 0) video.play().catch(() => {});
        else video.pause();
      });
    });
    observer.observe(video);
    return () => observer.disconnect();
  }, []);

  return (
    <div onClick={selecting ? onClick : undefined} className={`scene-card relative cursor-pointer group rounded border bg-card overflow-hidden flex flex-col h-full ${selected ? "ring-2 ring-accent border-accent" : "border-border"}`}>
      <RouteCardLinkOverlay route={{ page: "scene", id: scene.id }} onClick={onClick} label={`Open scene ${cardTitle}`} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} />
      <div className="scene-card-preview relative aspect-video bg-black overflow-hidden">
        <img
          src={coverUrl}
          alt={scene.title || ""}
          className="scene-card-preview-image h-full w-full"
          style={{ objectFit: scenePreviewObjectFit }}
          loading="lazy"
        />
        <video ref={videoRef} disableRemotePlayback playsInline muted loop preload="none" src={previewUrl} className="scene-card-preview-video" style={{ objectFit: scenePreviewObjectFit }} />
        {scrubImageUrl ? (
          <img
            src={scrubImageUrl}
            alt=""
            className="absolute inset-0 z-[7] h-full w-full"
            style={{ objectFit: scenePreviewObjectFit }}
            draggable={false}
          />
        ) : null}
        {(selected !== undefined || selecting) && <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />}
        {!selecting && (
          <BookmarkButton
            hostType="scene"
            hostId={scene.id}
            compact
            deferUntilHover
            initialSaved={bookmarkInitiallySaved}
            className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
          />
        )}
        {scene.studioName && scene.studioId && !selecting && (
          <div className="absolute top-0 right-0 p-1 z-[5]">
            <img src={entityImages.studioImageUrl(scene.studioId)} alt={scene.studioName} className="max-h-8 max-w-[120px] object-contain drop-shadow-md"
              onError={(e) => { const el = e.target as HTMLImageElement; el.style.display = "none"; if (el.nextElementSibling) (el.nextElementSibling as HTMLElement).style.display = ""; }} />
            <span className="text-xs font-medium text-white bg-black/60 px-1.5 py-0.5 rounded" style={{ display: "none" }}>{scene.studioName}</span>
          </div>
        )}
        {(duration > 0 || resLabel) && (
          <div className="scene-specs-overlay absolute bottom-0 right-0 flex items-center gap-0.5 px-1.5 py-1 text-xs text-white z-[5] transition-opacity">
            {file && <span className="bg-black/70 px-1 py-0.5 rounded extra-scene-info hidden">{formatFileSize(file.size)}</span>}
            {resLabel && <span className="bg-black/70 px-1 py-0.5 rounded font-black uppercase">{resLabel}</span>}
            {duration > 0 && <span className="bg-black/70 px-1 py-0.5 rounded">{formatDuration(duration)}</span>}
          </div>
        )}
        {onQuickView && (
          <button
            onClick={(e) => { e.stopPropagation(); onQuickView(); }}
            className="absolute bottom-1 left-1 z-10 opacity-0 group-hover:opacity-100 transition-opacity p-1 rounded bg-black/60 text-white hover:bg-black/80"
            title="Quick View"
          >
            <Eye className="w-3.5 h-3.5" />
          </button>
        )}
        {progressPercent > 0 && (
          <div className="absolute bottom-0 left-0 right-0 h-[3px] bg-black/40 z-[6]"><div className="h-full bg-accent" style={{ width: `${progressPercent}%` }} /></div>
        )}
        {duration > 0 && !selecting ? (
          <div
            className="absolute inset-x-0 bottom-0 z-[9] h-10 cursor-ew-resize"
            onMouseEnter={updateScrubPreview}
            onMouseMove={updateScrubPreview}
            onMouseLeave={() => setScrubSeconds(null)}
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
            }}
            aria-hidden="true"
          >
            {scrubTimestamp ? (
              <div
                className="pointer-events-none absolute bottom-4 -translate-x-1/2 whitespace-nowrap rounded bg-black/80 px-1.5 py-0.5 text-[10px] font-medium text-white shadow"
                style={{ left: `${scrubTimestampPercent}%` }}
              >
                {scrubTimestamp}
              </div>
            ) : null}
            <div className={`absolute inset-x-1 bottom-1 h-1 rounded-full bg-black/55 transition-opacity ${scrubSeconds != null ? "opacity-100" : "opacity-0"}`}>
              <div className="h-full rounded-full bg-accent" style={{ width: `${scrubPercent}%` }} />
            </div>
          </div>
        ) : null}
        <RatingBanner rating={engagement?.rating} />
      </div>
      <div className="card-body px-2.5 pt-2 pb-2 border-t border-border/50 flex-1 flex flex-col gap-1.5 min-h-0">
        <div>
          <p className="card-title font-semibold text-foreground line-clamp-2 group-hover:text-accent transition-colors leading-snug" title={cardTitle}>
            {cardTitle}
          </p>
          <div className="mt-1 flex items-center gap-2 text-[11px] text-muted">
            {scene.date && <span>{scene.date}</span>}
            {scene.studioName && <span className="truncate">{scene.studioName}</span>}
          </div>
        </div>
        {scene.performers.length > 0 && (
          <div className="relative z-10 flex items-center gap-1.5 overflow-hidden flex-wrap">
            {scene.performers.slice(0, 4).map((performer) => {
              const navigationHandlers = createNestedEntityNavigationHandlers<HTMLAnchorElement>({ page: "performer", id: performer.id }, onNavigate);

              return <PerformerBadge key={performer.id} performer={performer} navigationHandlers={navigationHandlers} />;
            })}
            {scene.performers.length > 4 && <span className="text-[10px] text-muted">+{scene.performers.length - 4}</span>}
          </div>
        )}
        {scene.details && <p className="text-xs text-secondary line-clamp-2 leading-snug">{scene.details}</p>}
      </div>
      <SceneCardPopovers scene={scene} engagement={engagement} onNavigate={onNavigate} />
    </div>
  );
}

// ===== SceneTile =====

interface SceneTileProps {
  scene: Scene;
  onClick: () => void;
}

export function SceneTile({ scene, onClick }: SceneTileProps) {
  const file = scene.files[0];
  const clipDuration = typeof scene.clipStartSec === "number" && typeof scene.clipEndSec === "number"
    ? Math.max(0, scene.clipEndSec - scene.clipStartSec)
    : undefined;
  const duration = clipDuration ?? file?.duration ?? 0;
  const resLabel = file ? getResolutionLabel(file.width, file.height) : null;
  const coverUrl = entityImages.sceneCoverUrl(scene.id, scene.updatedAt, 960);
  const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "scene", id: scene.id }, onClick);

  return (
    <a {...linkProps} className="group text-left">
      <div className="relative aspect-video overflow-hidden rounded-lg border border-border bg-card shadow-md shadow-black/30">
        <img
          src={coverUrl}
          alt={scene.title || ""}
          className="h-full w-full object-cover"
          loading="lazy"
        />
        {duration > 0 && <span className="absolute bottom-1.5 right-1.5 rounded bg-black/75 px-1.5 py-0.5 text-[11px] text-white">{formatDuration(duration)}</span>}
        {resLabel && <span className="absolute top-1.5 right-1.5 rounded bg-black/75 px-1.5 py-0.5 text-[10px] font-bold uppercase text-accent">{resLabel}</span>}
        <RatingBanner rating={undefined} />
      </div>
      <div className="pt-2">
        <p className="card-title font-medium text-foreground line-clamp-2 group-hover:text-accent">{scene.title || "Untitled"}</p>
        <p className="mt-0.5 truncate text-xs text-secondary">{scene.date || scene.studioName || ""}</p>
      </div>
    </a>
  );
}

// ===== PerformerTile =====

interface PerformerTileEntity {
  id: number;
  name: string;
  imagePath?: string | null;
  country?: string;
  birthdate?: string;
  favorite?: boolean;
  tags?: Array<{ id: number; name: string }>;
  sceneCount?: number;
  imageCount?: number;
  galleryCount?: number;
  audioCount?: number;
  textCount?: number;
  groupCount?: number;
}

interface PerformerTileProps {
  performer: PerformerTileEntity;
  onClick: () => void;
  onNavigate?: (r: any) => void;
  children?: ReactNode;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
}

export function PerformerTile({ performer, engagement, onClick, onNavigate, children, selected, onSelect, selecting }: PerformerTileProps & { engagement?: EntityEngagement }) {
  const sceneCount = performer.sceneCount ?? 0;
  const imageCount = performer.imageCount ?? 0;
  const galleryCount = performer.galleryCount ?? 0;
  const audioCount = performer.audioCount ?? 0;
  const textCount = performer.textCount ?? 0;
  const groupCount = performer.groupCount ?? 0;
  const performerImageUrl = performer.imagePath || null;
  const hasFooter = (performer.tags?.length ?? 0) > 0 || sceneCount > 0 || imageCount > 0 || galleryCount > 0 || audioCount > 0 || textCount > 0 || groupCount > 0;

  return (
    <EntityTileFrame
      route={{ page: "performer", id: performer.id }}
      label={`Open performer ${performer.name}`}
      onClick={onClick}
      selected={selected}
      onSelect={onSelect}
      selecting={selecting}
      mediaClassName="aspect-[2/3] bg-gradient-to-b from-card to-surface"
      bodyClassName="p-2.5"
      media={(
        <>
          {performerImageUrl ? (
            <>
              <CoverImage src={performerImageUrl} alt={performer.name} className="h-full w-full" loading="lazy" onError={(event) => { const image = event.currentTarget; image.style.display = "none"; const fallback = image.nextElementSibling as HTMLElement | null; if (fallback) fallback.style.display = "flex"; }} />
              <div className="hidden h-full w-full items-center justify-center"><User className="h-12 w-12 text-muted" /></div>
            </>
          ) : (
            <div className="flex h-full w-full items-center justify-center"><User className="h-12 w-12 text-muted" /></div>
          )}
          <RatingBanner rating={engagement?.rating} />
          {performer.favorite ? <Heart className="absolute right-1.5 top-1.5 z-[5] h-4 w-4 fill-red-500 text-red-500 drop-shadow-md" /> : null}
        </>
      )}
      body={(
        <>
          <p className="card-title line-clamp-2 font-semibold text-foreground group-hover:text-accent">{performer.name}</p>
          {(performer.country || performer.birthdate) ? (
            <div className="flex items-center gap-2 text-[11px] text-muted">
              {performer.country ? <span>{performer.country}</span> : null}
              {performer.birthdate ? <span>{performer.birthdate}</span> : null}
            </div>
          ) : null}
        </>
      )}
      footer={hasFooter ? (
        <>
            {performer.tags && performer.tags.length > 0 && (
              <PopoverButton icon={<Tag className="w-3.5 h-3.5" />} count={performer.tags.length} title="Tags" preferBelow>
                <div className="flex flex-wrap gap-1">
                  {performer.tags.map((t: any) => {
                    const navigationHandlers = createNestedEntityNavigationHandlers<HTMLAnchorElement>({ page: "tag", id: t.id }, onNavigate);

                    return (
                    <a key={t.id} {...navigationHandlers}
                      className="text-[11px] text-accent hover:underline cursor-pointer px-1.5 py-0.5 rounded bg-card border border-border hover:border-accent/40 transition-colors whitespace-nowrap">
                      {t.name}
                    </a>
                  );})}
                </div>
              </PopoverButton>
            )}
            {sceneCount > 0 && (
              <PopoverButton icon={<Film className="w-3.5 h-3.5" />} count={sceneCount} title="Scenes" wide preferBelow>
                <ScenesPopoverContent filter={{ performerIds: String(performer.id) }} />
              </PopoverButton>
            )}
            {imageCount > 0 && (
              <PopoverButton icon={<ImagesIcon className="w-3.5 h-3.5" />} count={imageCount} title="Images" wide preferBelow>
                <ImagesPopoverContent filter={{ performerIds: String(performer.id) }} />
              </PopoverButton>
            )}
            {galleryCount > 0 && (
              <PopoverButton icon={<FolderOpen className="w-3.5 h-3.5" />} count={galleryCount} title="Galleries" wide preferBelow>
                <GalleriesPopoverContent filter={{ performerIds: String(performer.id) }} />
              </PopoverButton>
            )}
            {audioCount > 0 && (
              <PopoverButton icon={<Headphones className="w-3.5 h-3.5" />} count={audioCount} title="Audio" wide preferBelow>
                <AudiosPopoverContent filter={{ performerIds: String(performer.id) }} />
              </PopoverButton>
            )}
            {textCount > 0 && (
              <PopoverButton icon={<FileText className="w-3.5 h-3.5" />} count={textCount} title="Texts" wide preferBelow>
                <TextsPopoverContent filter={{ performerIds: String(performer.id) }} />
              </PopoverButton>
            )}
            {groupCount > 0 ? <span className="flex items-center gap-0.5 text-xs text-muted px-1" title="Groups"><Layers className="w-3 h-3" /> {groupCount}</span> : null}
        </>
      ) : null}
      extensionClassName="px-2 py-2"
    >
      {children}
    </EntityTileFrame>
  );
}

// ===== StudioTile =====

interface StudioTileProps {
  studio: Studio;
  onClick: () => void;
  onNavigate?: (r: any) => void;
  children?: ReactNode;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
}

export function StudioTile({ studio, engagement, onClick, onNavigate, children, selected, onSelect, selecting }: StudioTileProps & { engagement?: EntityEngagement }) {
  const hasFooter = studio.tags.length > 0 || studio.sceneCount > 0 || studio.performerCount > 0 || studio.imageCount > 0 || studio.galleryCount > 0 || studio.groupCount > 0 || studio.childStudioCount > 0;

  return (
    <EntityTileFrame
      route={{ page: "studio", id: studio.id }}
      label={`Open studio ${studio.name}`}
      onClick={onClick}
      selected={selected}
      onSelect={onSelect}
      selecting={selecting}
      media={(
        <>
          {studio.imagePath ? (
            <>
              <img
                src={studio.imagePath}
                alt={studio.name}
                className="box-border h-full w-full object-contain p-4"
                loading="lazy"
                onError={(event) => {
                  const image = event.currentTarget;
                  image.style.display = "none";
                  const fallback = image.nextElementSibling as HTMLElement | null;
                  if (fallback) fallback.style.display = "flex";
                }}
              />
              <div className="hidden h-full w-full items-center justify-center">
                <Building2 className="h-10 w-10 text-muted" />
              </div>
            </>
          ) : (
            <Building2 className="h-10 w-10 text-muted" />
          )}
          <RatingBanner rating={engagement?.rating} />
          {studio.favorite ? <Heart className="absolute right-1.5 top-1.5 z-[5] h-4 w-4 fill-red-500 text-red-500 drop-shadow-md" /> : null}
        </>
      )}
      body={(
        <>
          <p className="card-title line-clamp-2 font-semibold text-foreground group-hover:text-accent">{studio.name}</p>
          {studio.parentName ? <p className="truncate text-xs text-secondary">{studio.parentName}</p> : null}
        </>
      )}
      footer={hasFooter ? (
        <>
            {studio.tags.length > 0 ? (
              <PopoverButton icon={<Tag className="w-3.5 h-3.5" />} count={studio.tags.length} title="Tags" preferBelow>
                <EntityLinkList items={studio.tags.map((tag) => ({ id: tag.id, label: tag.name, color: tag.color ?? tag.tagGroupColor }))} page="tag" onNavigate={onNavigate} />
              </PopoverButton>
            ) : null}
            {studio.sceneCount > 0 && (
              <PopoverButton icon={<Film className="w-3.5 h-3.5" />} count={studio.sceneCount} title="Scenes" wide preferBelow>
                <ScenesPopoverContent filter={{ studioId: studio.id }} />
              </PopoverButton>
            )}
            {studio.performerCount > 0 && (
              <PopoverButton icon={<User className="w-3.5 h-3.5" />} count={studio.performerCount} title="Performers" wide preferBelow>
                <PerformersPopoverContent filter={{ studioId: studio.id }} />
              </PopoverButton>
            )}
            {studio.imageCount > 0 && (
              <PopoverButton icon={<ImagesIcon className="w-3.5 h-3.5" />} count={studio.imageCount} title="Images" wide preferBelow>
                <ImagesPopoverContent filter={{ studioId: studio.id }} />
              </PopoverButton>
            )}
            {studio.galleryCount > 0 && (
              <PopoverButton icon={<FolderOpen className="w-3.5 h-3.5" />} count={studio.galleryCount} title="Galleries" wide preferBelow>
                <GalleriesPopoverContent filter={{ studioId: studio.id }} />
              </PopoverButton>
            )}
            {studio.groupCount > 0 && (
              <PopoverButton icon={<Layers className="w-3.5 h-3.5" />} count={studio.groupCount} title="Groups" wide preferBelow>
                <GroupsPopoverContent filter={{ studioId: studio.id }} />
              </PopoverButton>
            )}
            {studio.childStudioCount > 0 && (
              <PopoverButton icon={<Building2 className="w-3.5 h-3.5" />} count={studio.childStudioCount} title="Sub-studios" wide preferBelow>
                <StudiosPopoverContent filter={{ parentId: studio.id }} />
              </PopoverButton>
            )}
        </>
      ) : null}
    >
      {children}
    </EntityTileFrame>
  );
}

// ===== ImageTile =====

interface ImageTileProps {
  image: Image;
  onClick: () => void;
  onPreview?: () => void;
  onDetails?: () => void;
  onNavigate?: (r: any) => void;
  onQuickView?: () => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
  bookmarkInitiallySaved?: boolean;
}

export function ImageTile({ image, engagement, onClick, onPreview, onDetails, onNavigate, onQuickView, selected, onSelect, selecting, bookmarkInitiallySaved }: ImageTileProps & { engagement?: EntityEngagement }) {
  const likeCount = engagement?.likeCount ?? 0;
  const hasFavorite = engagement?.isFavorite === true;
  const imageGroups = image.groups ?? [];
  const hasFooter = (image.tags?.length ?? 0) > 0 || (image.performers?.length ?? 0) > 0 || (image.galleries?.length ?? 0) > 0 || imageGroups.length > 0 || likeCount > 0 || hasFavorite || image.organized;
  const displayTitle = getImageDisplayTitle(image);
  const detailsClick = onDetails ?? onClick;
  const previewClick = onPreview ?? detailsClick;
  return (
    <div onClick={selecting ? onClick : undefined} className={`entity-card group relative cursor-pointer overflow-hidden rounded-lg border bg-card text-left shadow-md shadow-black/20 flex flex-col h-full transition-colors ${selected ? "ring-2 ring-accent border-accent" : "border-border hover:border-accent/60"}`}>
      {!onPreview ? <RouteCardLinkOverlay route={{ page: "image", id: image.id }} onClick={detailsClick} label={`Open image ${displayTitle}`} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} /> : null}
      <div className="aspect-square overflow-hidden bg-surface relative" onClick={selecting ? undefined : previewClick}>
        <CoverImage src={images.thumbnailUrl(image.id)} alt={displayTitle} className="h-full w-full" loading="lazy" />
        <RatingBanner rating={engagement?.rating} />
        {(selected !== undefined || selecting) && <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />}
        {!selecting && (
          <BookmarkButton
            hostType="image"
            hostId={image.id}
            compact
            deferUntilHover
            initialSaved={bookmarkInitiallySaved}
            className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
          />
        )}
        {image.studioName && (
          <div className="absolute top-1 right-1 text-[10px] bg-black/70 px-1 py-0.5 rounded text-white truncate max-w-[80%]">{image.studioName}</div>
        )}
        {!selecting && onQuickView && (
          <button
            onClick={(e) => { e.stopPropagation(); onQuickView(); }}
            className="absolute bottom-1 left-1 z-10 opacity-0 group-hover:opacity-100 transition-opacity p-1 rounded bg-black/60 text-white hover:bg-black/80"
            title="Quick View"
          >
            <Eye className="w-3.5 h-3.5" />
          </button>
        )}
      </div>
      <div className="card-body border-t border-border/50 p-2 flex-1 flex flex-col gap-1">
        {!selecting && onPreview ? (
          <a {...createRouteLinkProps<HTMLAnchorElement>({ page: "image", id: image.id }, detailsClick)} className="relative z-10 card-title font-semibold text-foreground line-clamp-2 group-hover:text-accent">
            {displayTitle}
          </a>
        ) : (
          <p className="card-title font-semibold text-foreground line-clamp-2 group-hover:text-accent">{displayTitle}</p>
        )}
      </div>
      {hasFooter && (
        <>
          <hr className="border-border/50 my-0" />
          <div className="relative z-10 flex flex-wrap items-center justify-center gap-1 px-2 py-1.5 rounded-b card-popovers min-h-[28px]">
            {(image.tags?.length ?? 0) > 0 && (
              <PopoverButton icon={<Tag className="w-3.5 h-3.5" />} count={image.tags.length} title="Tags" preferBelow>
                <div className="flex flex-wrap gap-1">
                  {image.tags.map((t: any) => {
                    const navigationHandlers = createNestedEntityNavigationHandlers<HTMLAnchorElement>({ page: "tag", id: t.id }, onNavigate);

                    return (
                    <a key={t.id} {...navigationHandlers}
                      className="text-[11px] text-accent hover:underline cursor-pointer px-1.5 py-0.5 rounded bg-card border border-border hover:border-accent/40 transition-colors whitespace-nowrap">
                      {t.name}
                    </a>
                  );})}
                </div>
              </PopoverButton>
            )}
            {(image.performers?.length ?? 0) > 0 && (
              <PopoverButton icon={<User className="w-3.5 h-3.5" />} count={image.performers.length} title="Performers" wide preferBelow>
                <PerformerPreviewGrid performers={image.performers} onNavigate={onNavigate} />
              </PopoverButton>
            )}
            {image.galleryCount > 0 && (
              <PopoverButton icon={<FolderOpen className="w-3.5 h-3.5" />} count={image.galleryCount} title="Galleries" wide preferBelow>
                <GalleriesPopoverContent filter={{ imageId: image.id }} />
              </PopoverButton>
            )}
            {imageGroups.length > 0 ? (
              <PopoverButton icon={<Layers className="w-3.5 h-3.5" />} count={imageGroups.length} title="Groups" preferBelow>
                <EntityLinkList items={imageGroups.map((group) => ({ id: group.id, label: group.name }))} page="group" onNavigate={onNavigate} />
              </PopoverButton>
            ) : null}
            {likeCount > 0 && (
              <LikeCounter count={likeCount} />
            )}
            {hasFavorite ? (
              <CardFavoriteButton hostType="image" hostId={image.id} favorite={engagement?.isFavorite ?? false} />
            ) : null}
            {image.organized && (
              <span className="p-1 text-muted" title="Organized"><Box className="w-3.5 h-3.5" /></span>
            )}
          </div>
        </>
      )}
    </div>
  );
}

// ===== GalleryTile =====

interface GalleryTileProps {
  gallery: Gallery;
  onClick: () => void;
  onNavigate?: (r: any) => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
  bookmarkInitiallySaved?: boolean;
}

export function GalleryTile({ gallery, engagement, onClick, onNavigate, selected, onSelect, selecting, bookmarkInitiallySaved }: GalleryTileProps & { engagement?: EntityEngagement }) {
  const hasFooter = gallery.imageCount > 0 || gallery.sceneCount > 0 || gallery.tags.length > 0 || gallery.performers.length > 0 || gallery.organized;
  const title = gallery.title || "Untitled";

  return (
    <EntityTileFrame
      route={{ page: "gallery", id: gallery.id }}
      label={`Open gallery ${title}`}
      onClick={onClick}
      selected={selected}
      onSelect={onSelect}
      selecting={selecting}
      media={(
        <>
          {gallery.coverPath ? (
            <>
              <CoverImage src={gallery.coverPath} alt={title} className="h-full w-full" loading="lazy" onError={(event) => { const image = event.currentTarget; image.style.display = "none"; const fallback = image.nextElementSibling as HTMLElement | null; if (fallback) fallback.style.display = "flex"; }} />
              <div className="hidden h-full w-full items-center justify-center"><FolderOpen className="h-10 w-10 text-muted" /></div>
            </>
          ) : (
            <FolderOpen className="h-10 w-10 text-muted" />
          )}
          <RatingBanner rating={engagement?.rating} />
          {!selecting ? (
            <BookmarkButton
              hostType="gallery"
              hostId={gallery.id}
              compact
              deferUntilHover
              initialSaved={bookmarkInitiallySaved}
              className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
            />
          ) : null}
        </>
      )}
      body={(
        <>
          <p className="card-title line-clamp-2 font-semibold text-foreground group-hover:text-accent">{title}</p>
          {(gallery.date || gallery.studioName) ? <p className="truncate text-xs text-secondary">{gallery.date || gallery.studioName}</p> : null}
        </>
      )}
      footer={hasFooter ? (
        <>
            {gallery.imageCount > 0 ? (
              <PopoverButton icon={<ImagesIcon className="w-3.5 h-3.5" />} count={gallery.imageCount} title="Images" wide preferBelow>
                <ImagesPopoverContent filter={{ galleryId: gallery.id }} />
              </PopoverButton>
            ) : null}
            {gallery.sceneCount > 0 ? (
              <PopoverButton icon={<Film className="w-3.5 h-3.5" />} count={gallery.sceneCount} title="Scenes" wide preferBelow>
                <ScenesPopoverContent filter={{ galleryId: gallery.id }} />
              </PopoverButton>
            ) : null}
            {gallery.tags.length > 0 ? (
              <PopoverButton icon={<Tag className="w-3.5 h-3.5" />} count={gallery.tags.length} title="Tags" preferBelow>
                <EntityLinkList items={gallery.tags.map((tag) => ({ id: tag.id, label: tag.name, color: tag.color ?? tag.tagGroupColor }))} page="tag" onNavigate={onNavigate} />
              </PopoverButton>
            ) : null}
            {gallery.performers.length > 0 ? (
              <PopoverButton icon={<User className="w-3.5 h-3.5" />} count={gallery.performers.length} title="Performers" wide preferBelow>
                <PerformerPreviewGrid performers={gallery.performers} onNavigate={onNavigate} />
              </PopoverButton>
            ) : null}
            {gallery.organized ? <span className="p-1 text-muted" title="Organized"><Box className="w-3.5 h-3.5" /></span> : null}
        </>
      ) : null}
    />
  );
}

// ===== GroupTile =====

interface GroupTileProps {
  group: Group;
  onClick: () => void;
  onNavigate?: (r: any) => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
  bookmarkInitiallySaved?: boolean;
  dragHandleProps?: EntityTileDragHandleProps;
  isDragging?: boolean;
  isOver?: boolean;
}

export function GroupTile({ group, engagement, onClick, onNavigate, selected, onSelect, selecting, bookmarkInitiallySaved, dragHandleProps, isDragging, isOver }: GroupTileProps & { engagement?: EntityEngagement }) {
  const hasFooter = (group.tags?.length ?? 0) > 0 || group.sceneCount > 0;

  return (
    <EntityTileFrame
      route={{ page: "group", id: group.id }}
      label={`Open group ${group.name}`}
      onClick={onClick}
      selected={selected}
      onSelect={onSelect}
      selecting={selecting}
      dragHandleProps={dragHandleProps}
      isDragging={isDragging}
      isOver={isOver}
      media={(
        <>
          {group.frontImagePath ? (
            <>
              <CoverImage src={group.frontImagePath} alt={group.name} className="h-full w-full" loading="lazy" onError={(event) => { const image = event.currentTarget; image.style.display = "none"; const fallback = image.nextElementSibling as HTMLElement | null; if (fallback) fallback.style.display = "flex"; }} />
              <div className="hidden h-full w-full items-center justify-center"><Layers className="h-10 w-10 text-muted" /></div>
            </>
          ) : (
            <Layers className="h-10 w-10 text-muted" />
          )}
          <RatingBanner rating={engagement?.rating} />
          {!selecting ? (
            <BookmarkButton
              hostType="group"
              hostId={group.id}
              compact
              deferUntilHover
              initialSaved={bookmarkInitiallySaved}
              className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100"
            />
          ) : null}
          {group.kind === "dynamic" ? <span className="absolute bottom-1 left-1 rounded bg-accent/90 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-white">Dynamic</span> : null}
        </>
      )}
      body={(
        <>
          <p className="card-title line-clamp-2 font-semibold text-foreground group-hover:text-accent">{group.name}</p>
          {(group.date || group.studioName) ? <p className="truncate text-xs text-secondary">{group.date || group.studioName}</p> : null}
        </>
      )}
      footer={hasFooter ? (
        <>
            {group.sceneCount > 0 ? (
              <PopoverButton icon={<Film className="w-3.5 h-3.5" />} count={group.sceneCount} title="Scenes" wide preferBelow>
                <ScenesPopoverContent filter={{ groupId: group.id }} />
              </PopoverButton>
            ) : null}
            {(group.tags?.length ?? 0) > 0 ? (
              <PopoverButton icon={<Tag className="w-3.5 h-3.5" />} count={group.tags.length} title="Tags" preferBelow>
                <EntityLinkList items={group.tags.map((tag) => ({ id: tag.id, label: tag.name, color: tag.color ?? tag.tagGroupColor }))} page="tag" onNavigate={onNavigate} />
              </PopoverButton>
            ) : null}
        </>
      ) : null}
    />
  );
}

interface AudioTileProps {
  audio: Audio;
  engagement?: EntityEngagement;
  onClick: () => void;
  onNavigate?: (route: any) => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
}

export function AudioTile({ audio, engagement, selected, onSelect, selecting, onClick, onNavigate }: AudioTileProps) {
  const title = getAudioDisplayTitle(audio);
  const duration = audio.maxDuration > 0 ? formatDuration(audio.maxDuration) : null;
  const audioRef = useRef<HTMLAudioElement>(null);
  const hoverTimerRef = useRef<number | null>(null);
  const canPreview = !selecting && !selected;

  const stopPreview = useCallback(() => {
    if (hoverTimerRef.current !== null) {
      window.clearTimeout(hoverTimerRef.current);
      hoverTimerRef.current = null;
    }
    const element = audioRef.current;
    if (!element) return;
    element.pause();
    element.currentTime = 0;
  }, []);

  const schedulePreview = (event: MouseEvent<HTMLElement>) => {
    if (!canPreview || (event.target as HTMLElement).closest("[data-audio-preview-ignore]")) return;
    if (hoverTimerRef.current !== null) window.clearTimeout(hoverTimerRef.current);
    hoverTimerRef.current = window.setTimeout(() => {
      hoverTimerRef.current = null;
      const element = audioRef.current;
      if (!element) return;
      element.currentTime = 0;
      element.volume = 0.35;
      element.play().catch(() => {});
    }, 1000);
  };

  useEffect(() => {
    if (!canPreview) stopPreview();
    return () => {
      if (hoverTimerRef.current !== null) window.clearTimeout(hoverTimerRef.current);
    };
  }, [canPreview, stopPreview]);

  return (
    <article onClick={selecting ? onClick : undefined} onMouseEnter={schedulePreview} onMouseLeave={stopPreview} className={`entity-card group relative flex h-full cursor-pointer flex-col overflow-hidden rounded-lg border bg-card text-left transition-colors ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}>
      <RouteCardLinkOverlay route={{ page: "audio", id: audio.id }} onClick={onClick} label={`Open ${title}`} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} />
      <div className="relative flex aspect-video items-center justify-center overflow-hidden bg-surface">
        {audio.imagePath ? (
          <CoverImage src={audio.imagePath} alt={title} className="h-full w-full" loading="lazy" />
        ) : (
          <Headphones className="h-12 w-12 text-muted opacity-50" />
        )}
        <audio ref={audioRef} src={audios.streamUrl(audio.id)} preload="none" />
        {(selected !== undefined || selecting) ? <div data-audio-preview-ignore onMouseEnter={stopPreview}><CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} /></div> : null}
        {!selecting ? <BookmarkButton hostType="audio" hostId={audio.id} compact deferUntilHover className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100" /> : null}
        {audio.hasVideoFiles ? (
          <span className="absolute right-1 top-1 z-[5] inline-flex items-center gap-1 rounded bg-black/70 px-1.5 py-0.5 text-[10px] font-medium text-white"><MonitorPlay className="h-3 w-3" />Video</span>
        ) : null}
        {duration ? <span className="absolute bottom-1 right-1 z-[5] rounded bg-black/70 px-1.5 py-0.5 text-xs text-white">{duration}</span> : null}
      </div>
      <div className="card-body flex flex-1 flex-col gap-2 border-t border-border/50 p-2.5">
        <div className="flex min-h-0 flex-1 flex-col">
          <h2 className="card-title line-clamp-2 font-semibold text-foreground transition-colors group-hover:text-accent">{title}</h2>
          {audio.details ? <p className="mt-1 line-clamp-2 text-xs leading-snug text-muted">{audio.details}</p> : null}
          <div data-audio-preview-ignore className="mt-auto pt-2">
            <EntityReferencePopovers studio={{ id: audio.studioId, name: audio.studioName }} performers={audio.performers} tags={audio.tags} groups={audio.groups} onNavigate={onNavigate} className="w-full justify-center" />
          </div>
        </div>
        <div className="flex flex-wrap gap-1.5 text-[11px] text-muted">
          {engagement?.playCount ? <span className="inline-flex items-center gap-1 rounded border border-border/80 px-1.5 py-0.5"><PlayCircle className="h-3 w-3" />{engagement.playCount} play{engagement.playCount === 1 ? "" : "s"}</span> : null}
          {audio.tracks.length > 0 ? <span className="inline-flex items-center gap-1 rounded border border-border/80 px-1.5 py-0.5"><Mic2 className="h-3 w-3" />{audio.tracks.length} track{audio.tracks.length === 1 ? "" : "s"}</span> : null}
        </div>
      </div>
    </article>
  );
}

interface TextTileProps {
  text: TextDocument;
  onClick: () => void;
  onNavigate?: (route: any) => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
}

export function TextTile({ text, selected, onSelect, selecting, onClick, onNavigate }: TextTileProps) {
  const title = getTextDisplayTitle(text);
  const primaryFile = pickPrimaryTextFile(text);
  const preview = primaryFile?.excerptText?.trim() || text.details?.trim() || "Open the document to read the extracted content and file details.";

  return (
    <article onClick={selecting ? onClick : undefined} className={`entity-card group relative flex h-full cursor-pointer flex-col overflow-hidden rounded-lg border bg-card text-left transition-colors ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}>
      <RouteCardLinkOverlay route={{ page: "text", id: text.id }} onClick={onClick} label={`Open ${title}`} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} />
      <div className="relative flex aspect-video items-center justify-center overflow-hidden bg-surface">
        {text.imagePath ? <CoverImage src={text.imagePath} alt={title} className="h-full w-full" loading="lazy" /> : <FileText className="h-12 w-12 text-muted opacity-50" />}
        {(selected !== undefined || selecting) ? <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} /> : null}
        {!selecting ? <BookmarkButton hostType="text" hostId={text.id} compact deferUntilHover className="absolute left-9 top-1 z-10 border-white/20 bg-black/60 text-white opacity-0 shadow transition-opacity hover:bg-black/80 group-hover:opacity-100 focus:opacity-100" /> : null}
        {text.maxWordCount ? <span className="absolute bottom-1 right-1 z-[5] rounded bg-black/70 px-1.5 py-0.5 text-xs text-white">{Intl.NumberFormat().format(text.maxWordCount)} words</span> : null}
      </div>
      <div className="card-body flex flex-1 flex-col gap-2 border-t border-border/50 p-2.5">
        <div className="flex min-h-0 flex-1 flex-col">
          <h2 className="card-title line-clamp-2 font-semibold text-foreground transition-colors group-hover:text-accent">{title}</h2>
          <p className="mt-1 line-clamp-3 text-xs leading-snug text-muted">{preview}</p>
          <div className="mt-auto pt-2">
            <EntityReferencePopovers studio={{ id: text.studioId, name: text.studioName }} performers={text.performers} tags={text.tags} groups={text.groups} onNavigate={onNavigate} className="w-full justify-center" />
          </div>
        </div>
        {text.maxPageCount ? <div className="flex flex-wrap gap-1.5 text-[11px] text-muted"><span className="inline-flex items-center gap-1 rounded border border-border/80 px-1.5 py-0.5"><BookOpenText className="h-3 w-3" />{text.maxPageCount} page{text.maxPageCount === 1 ? "" : "s"}</span></div> : null}
      </div>
    </article>
  );
}

export function TagTile({ tag, engagement, onClick, onNavigate, children, selected, onSelect, selecting }: { tag: TagType; engagement?: EntityEngagement; onClick: () => void; onNavigate?: (r: any) => void; children?: ReactNode; selected?: boolean; onSelect?: () => void; selecting?: boolean }) {
  const favorite = engagement?.isFavorite ?? tag.favorite;
  const hasFooter = Boolean(tag.sceneCount || tag.segmentCount || tag.imageCount || tag.galleryCount || tag.groupCount || tag.performerCount || tag.studioCount);

  return (
    <EntityTileFrame
      route={{ page: "tag", id: tag.id }}
      label={`Open tag ${tag.name}`}
      onClick={onClick}
      selected={selected}
      onSelect={onSelect}
      selecting={selecting}
      media={(
        <>
          {favorite ? <Heart className="absolute right-2 top-2 z-10 h-4 w-4 fill-red-500 text-red-500 drop-shadow" /> : null}
          {tag.imagePath ? (
            <>
              <CoverImage src={tag.imagePath} alt={tag.name} className="h-full w-full" loading="lazy" onError={(event) => { const image = event.currentTarget; image.style.display = "none"; const fallback = image.nextElementSibling as HTMLElement | null; if (fallback) fallback.style.display = "flex"; }} />
              <div className="hidden h-full w-full items-center justify-center"><Tag className="h-10 w-10 text-muted" /></div>
            </>
          ) : (
            <Tag className="h-10 w-10 text-muted" />
          )}
        </>
      )}
      body={(
        <>
          <h3 className="card-title truncate text-sm font-semibold text-foreground group-hover:text-accent">{tag.name}</h3>
          {tag.tagGroupName ? <div className="inline-flex max-w-full items-center gap-1.5 rounded-full border border-border bg-surface px-2 py-0.5 text-[10px] text-secondary"><span className="h-2 w-2 rounded-full border border-border" style={{ backgroundColor: tag.tagGroupColor ?? "transparent" }} /><span className="truncate">{tag.tagGroupName}</span></div> : null}
          {tag.description ? <p className="line-clamp-1 text-xs text-secondary">{tag.description}</p> : null}
        </>
      )}
      footer={hasFooter ? (
        <>
          {tag.sceneCount != null && tag.sceneCount > 0 ? <PopoverButton icon={<Film className="w-3 h-3" />} count={tag.sceneCount} title="Scenes" wide preferBelow><ScenesPopoverContent filter={{ tagIds: String(tag.id) }} /></PopoverButton> : null}
          {tag.imageCount != null && tag.imageCount > 0 ? <PopoverButton icon={<ImagesIcon className="w-3 h-3" />} count={tag.imageCount} title="Images" wide preferBelow><ImagesPopoverContent filter={{ tagIds: String(tag.id) }} /></PopoverButton> : null}
          {tag.galleryCount != null && tag.galleryCount > 0 ? <PopoverButton icon={<FolderOpen className="w-3 h-3" />} count={tag.galleryCount} title="Galleries" wide preferBelow><GalleriesPopoverContent filter={{ tagIds: String(tag.id) }} /></PopoverButton> : null}
          {tag.groupCount != null && tag.groupCount > 0 ? <PopoverButton icon={<Layers className="w-3 h-3" />} count={tag.groupCount} title="Groups" wide preferBelow><GroupsPopoverContent filter={{ tagIds: String(tag.id) }} /></PopoverButton> : null}
          {tag.segmentCount != null && tag.segmentCount > 0 ? <span className="flex items-center gap-0.5 text-xs text-muted" title="Segments"><Layers className="w-3 h-3" /> {tag.segmentCount}</span> : null}
          {tag.performerCount != null && tag.performerCount > 0 ? <PopoverButton icon={<User className="w-3 h-3" />} count={tag.performerCount} title="Performers" wide preferBelow><PerformersPopoverContent filter={{ tagIds: String(tag.id) }} /></PopoverButton> : null}
          {tag.studioCount != null && tag.studioCount > 0 ? <PopoverButton icon={<Building2 className="w-3 h-3" />} count={tag.studioCount} title="Studios" wide preferBelow><StudiosPopoverContent filter={{ tagIds: String(tag.id) }} /></PopoverButton> : null}
        </>
      ) : null}
    >
      {children}
    </EntityTileFrame>
  );
}

export function FaceTile({ face, onClick, selected, onSelect, selecting, children }: { face: Face; onClick: () => void; selected?: boolean; onSelect?: () => void; selecting?: boolean; children?: React.ReactNode }) {
  const title = face.label?.trim() || face.performerName || `Face #${face.id}`;

  return (
    <article className={`entity-card group relative overflow-hidden rounded-2xl border bg-card/80 shadow-sm transition-colors ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/50"}`}>
      <RouteCardLinkOverlay route={{ page: "face", id: face.id }} onClick={onClick} label={`Open face ${title}`} disabled={selecting} selectionSafeZone />
      <div onClick={selecting ? onSelect : onClick} className="relative block aspect-square max-h-[22rem] w-full bg-surface/70 text-left">
        {(selected !== undefined || selecting) ? <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} /> : null}
        {face.coverImageUrl ? <img src={face.coverImageUrl} alt={title} className="h-full w-full bg-surface/85 object-contain p-2" loading="lazy" /> : <div className="flex h-full items-center justify-center bg-surface text-muted"><Fingerprint className="h-12 w-12" /></div>}
        <div className="absolute inset-x-0 bottom-0 flex flex-wrap gap-1 bg-gradient-to-t from-black/80 via-black/35 to-transparent p-3">
          {face.mergedIntoFaceId ? <span className="inline-flex items-center gap-1 rounded-full bg-black/65 px-2 py-0.5 text-[11px] text-white"><Merge className="h-3 w-3" />Merged</span> : null}
          {face.performerId ? <span className="inline-flex items-center gap-1 rounded-full bg-black/65 px-2 py-0.5 text-[11px] text-white"><Link2 className="h-3 w-3" />Linked</span> : null}
        </div>
      </div>
      <div className="relative z-10 space-y-3 p-4">
        <div>
          <button type="button" onClick={onClick} className="relative z-10 text-left text-sm font-semibold text-foreground hover:text-accent">{title}</button>
          <div className="mt-1 text-xs text-secondary">Updated {new Date(face.updatedAt).toLocaleDateString()}</div>
        </div>
        <div className="grid grid-cols-3 gap-2 text-center text-xs">
          <MetricPill label="Detections" value={face.detectionCount} />
          <MetricPill label="Scenes" value={face.sceneCount} />
          <MetricPill label="Images" value={face.imageCount} />
        </div>
        {children ? <div className="relative z-20 rounded-xl border border-border bg-surface/50 p-3">{children}</div> : null}
        <div className="flex items-center justify-between gap-2 text-xs text-secondary"><span>Source: {face.primarySourceKey || "unknown"}</span><span>{face.frameSampleCount ?? 0} samples</span></div>
      </div>
    </article>
  );
}

export function FaceAppearanceTile({ appearance, onClick }: { appearance: FaceAppearance; onClick: () => void }) {
  const hostLabel = appearance.title || `${appearance.hostType === "image" ? "Image" : "Scene"} #${appearance.hostId}`;
  const Icon = appearance.hostType === "image" ? ImagesIcon : Film;

  return (
    <article className="entity-card group relative overflow-hidden rounded-2xl border border-border bg-card/80 shadow-sm transition-colors hover:border-accent/50">
      <RouteCardLinkOverlay route={{ page: appearance.hostType, id: appearance.hostId }} onClick={onClick} label={`Open ${hostLabel}`} />
      <div className={`relative w-full overflow-hidden bg-surface/80 ${appearance.hostType === "image" ? "aspect-square" : "aspect-video"}`}>
        <div className="absolute inset-0 flex items-center justify-center text-muted">
          <Icon className="h-10 w-10" />
        </div>
        <img
          src={appearance.thumbnailUrl}
          alt={hostLabel}
          className="relative h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.02]"
          loading="lazy"
          onError={(event) => { (event.currentTarget as HTMLImageElement).style.display = "none"; }}
        />
      </div>
      <div className="space-y-3 p-4">
        <div className="flex flex-wrap items-center gap-2 text-[11px] font-medium uppercase tracking-wide text-muted">
          <span className="inline-flex items-center gap-1 rounded-full border border-border bg-surface/70 px-2 py-0.5">
            <Icon className="h-3 w-3" />
            {appearance.hostType}
          </span>
          <span>{appearance.frameSampleCount} frames</span>
          {appearance.topConfidence != null ? <span>{Math.round(appearance.topConfidence * 100)}% confidence</span> : null}
        </div>
        <button type="button" onClick={onClick} className="relative z-10 block text-left text-sm font-semibold text-foreground hover:text-accent">
          {hostLabel}
        </button>
        <div className="grid grid-cols-3 gap-2 text-center text-xs">
          <MetricPill label="Frames" value={appearance.frameSampleCount} />
          <MetricPill label="Samples" value={appearance.retainedSpatialSampleCount} />
          <MetricPill label="Segments" value={appearance.segmentCount} />
        </div>
        <div className="text-xs text-secondary">
          {appearance.hostType === "scene" ? formatFaceAppearanceTimeRange(appearance) : "Image appearance"}
        </div>
      </div>
    </article>
  );
}

function formatFaceAppearanceTimeRange(appearance: FaceAppearance) {
  const start = appearance.firstSeenAtSec == null ? null : formatFaceAppearanceTime(appearance.firstSeenAtSec);
  const end = appearance.lastSeenAtSec == null ? null : formatFaceAppearanceTime(appearance.lastSeenAtSec);
  return start && end && start !== end ? `${start} - ${end}` : start ?? end ?? "Scene appearance";
}

function formatFaceAppearanceTime(totalSeconds: number) {
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = Math.floor(totalSeconds % 60);
  return hours > 0
    ? `${hours}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`
    : `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

function MetricPill({ label, value }: { label: string; value: number }) {
  return <div className="rounded-lg border border-border bg-surface/40 px-2 py-1"><div className="text-sm font-semibold text-foreground">{value}</div><div className="text-[10px] uppercase tracking-wide text-muted">{label}</div></div>;
}

interface SegmentTileItem {
  id: number | string;
  hostType: string;
  hostId: number;
  startSec: number;
  endSec?: number;
  tagName?: string;
  kind?: string;
  sourceKey?: string;
  sourceRunId?: string;
  confidence?: number;
  title?: string;
  updatedAt?: string;
  hostTitle?: string;
}

export function SegmentTile({ segment, route, label, eyebrow, footer, onClick, selected, onSelect, selecting }: { segment: SegmentTileItem; route?: any; label?: string; eyebrow?: string; footer?: ReactNode; onClick: () => void; selected?: boolean; onSelect?: () => void; selecting?: boolean }) {
  const title = segment.title || segment.kind || `Segment ${segment.id}`;
  const cardRoute = route ?? { page: "segment", id: segment.id };

  return (
    <article onClick={selecting ? onClick : undefined} className={`entity-card group relative overflow-hidden rounded border bg-card transition-all ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}>
      <RouteCardLinkOverlay route={cardRoute} onClick={onClick} label={label ?? `Open segment ${title}`} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} />
      <div className="relative aspect-video w-full overflow-hidden bg-surface/70">
        {(selected !== undefined || selecting) ? <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} /> : null}
        {segment.hostType === "scene" ? <img src={scenes.screenshotUrl(segment.hostId, segment.updatedAt, segment.startSec)} alt={title} className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105" loading="lazy" /> : <div className="flex h-full w-full items-center justify-center bg-surface text-muted"><Layers className="h-10 w-10" /></div>}
        <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/35 to-transparent p-3 text-white">
          <div className="text-xs font-medium uppercase tracking-wide text-white/75">{eyebrow ?? formatDuration(segment.startSec)}</div>
          <div className="mt-1 line-clamp-2 text-sm font-semibold">{title}</div>
        </div>
      </div>
      <div className="border-t border-border bg-card p-3">
        <div className="line-clamp-2 text-sm font-medium text-foreground">{title}</div>
        <div className="truncate text-xs text-secondary">{segment.hostTitle || `${segment.hostType} #${segment.hostId}`}</div>
      </div>
      <div className="relative z-10 flex flex-wrap items-center gap-1.5 border-t border-border px-3 py-2 text-[11px] text-secondary">
        {segment.tagName ? <span className="rounded border border-border px-1.5 py-0.5">{segment.tagName}</span> : null}
        {segment.kind ? <span className="rounded border border-border px-1.5 py-0.5">{segment.kind}</span> : null}
        {segment.sourceKey ? <span className="rounded border border-border px-1.5 py-0.5">{segment.sourceKey}</span> : null}
        {segment.confidence != null ? <span className="rounded border border-border px-1.5 py-0.5">{segment.confidence.toFixed(2)} conf</span> : null}
        {segment.sourceRunId ? <span className="rounded border border-border px-1.5 py-0.5">{segment.sourceRunId}</span> : null}
      </div>
      {footer ? <div className="relative z-10 border-t border-border px-3 py-2 text-xs text-secondary">{footer}</div> : null}
    </article>
  );
}
