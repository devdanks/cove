import { FolderOpen } from "lucide-react";
import { EntityCardGrid } from "../../components/EntityCardGrid";
import type { DisplayMode } from "../../components/ListPage";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../../components/RouteCardLinkOverlay";
import {
  buildRawSegmentTitle,
  formatDate,
  formatSegmentRange,
  Pill,
  SegmentScenePreview,
} from "./segmentDisplayUtils";
import type { RawSegmentItem } from "./types";

interface Props {
  displayMode: DisplayMode;
  items: RawSegmentItem[];
  canReadScenes: boolean;
  onNavigate: (route: any) => void;
  selectedIds: Set<string | number>;
  onToggle: (id: string | number) => void;
  selecting: boolean;
}

export function RawSegmentResults({
  displayMode,
  items,
  canReadScenes,
  onNavigate,
  selectedIds,
  onToggle,
  selecting,
}: Props) {
  if (displayMode === "grid") {
    return (
      <EntityCardGrid minCardWidth="var(--card-min-width, 220px)">
        {items.map((item) => (
          <RawSegmentCard
            key={item.key}
            item={item}
            canReadScenes={canReadScenes}
            onNavigate={onNavigate}
            onClick={() => (selecting ? onToggle(item.id) : onNavigate({ page: "segment", id: item.id }))}
            selected={selectedIds.has(item.id)}
            onSelect={() => onToggle(item.id)}
            selecting={selecting}
          />
        ))}
      </EntityCardGrid>
    );
  }

  return (
    <div className="overflow-hidden rounded-xl border border-border bg-card">
      <div className="hidden grid-cols-[minmax(0,1.3fr)_140px_minmax(0,1fr)_120px_120px] gap-3 border-b border-border bg-surface/70 px-4 py-2 text-[11px] font-medium uppercase tracking-wide text-muted lg:grid">
        <span>Segment</span>
        <span>Range</span>
        <span>Scene</span>
        <span>Source</span>
        <span>Updated</span>
      </div>
      <div className="divide-y divide-border">
        {items.map((item) => (
          <RawSegmentListRow
            key={item.key}
            item={item}
            canReadScenes={canReadScenes}
            onNavigate={onNavigate}
            selected={selectedIds.has(item.id)}
            onToggle={() => onToggle(item.id)}
            selecting={selecting}
          />
        ))}
      </div>
    </div>
  );
}

function RawSegmentCard({
  item,
  canReadScenes,
  onNavigate,
  onClick,
  selected,
  onSelect,
  selecting,
}: {
  item: RawSegmentItem;
  canReadScenes: boolean;
  onNavigate: (route: any) => void;
  onClick: () => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
}) {
  const title = buildRawSegmentTitle(item);

  return (
    <div onClick={selecting ? onClick : undefined} className={`entity-card group relative overflow-hidden rounded border bg-card transition-all ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}>
      <RouteCardLinkOverlay route={{ page: "segment", id: item.id }} onClick={onClick} label={`Open raw segment ${title}`} disabled={selecting} selectionSafeZone={selected !== undefined || selecting} />
      <div className="relative aspect-video w-full overflow-hidden bg-surface/70">
        {(selected !== undefined || selecting) && <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />}
        <SegmentScenePreview hostId={item.hostId} updatedAt={item.updatedAt} startSec={item.startSec} title={title} imgClassName="h-full w-full object-cover" fallbackClassName="flex h-full w-full items-center justify-center bg-surface text-muted" iconClassName="h-12 w-12" />
        <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/35 to-transparent p-3 text-white">
          <div className="text-xs font-medium uppercase tracking-wide text-white/75">Raw segment #{item.id}</div>
          <div className="mt-1 line-clamp-2 text-sm font-semibold">{title}</div>
        </div>
      </div>

      <div className="border-t border-border bg-card p-3">
        <div className="space-y-1">
          <div className="line-clamp-2 text-sm font-medium text-foreground">{title}</div>
          <div className="truncate text-xs text-secondary">{item.sceneTitle}</div>
        </div>
      </div>

      <div className="relative z-10 flex flex-wrap items-center gap-1.5 border-t border-border px-3 py-2 text-[11px]">
        {item.tagName ? <Pill>{item.tagName}</Pill> : null}
        {item.kind ? <Pill>{item.kind}</Pill> : null}
        <Pill>{formatSegmentRange(item.startSec, item.endSec)}</Pill>
        <Pill>{item.sourceKey}</Pill>
        {item.confidence != null ? <Pill>{item.confidence.toFixed(2)} conf</Pill> : null}
        {item.sourceRunId ? <Pill>{item.sourceRunId}</Pill> : null}
      </div>

      <div className="relative z-10 flex items-center justify-between gap-2 border-t border-border px-3 py-2 text-xs text-secondary">
        <span>Updated {formatDate(item.updatedAt)}</span>
        {canReadScenes ? (
          <button
            type="button"
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
              onNavigate({ page: "scene", id: item.hostId, seekTo: item.startSec });
            }}
            className="inline-flex items-center gap-1 text-accent hover:underline"
          >
            <FolderOpen className="h-3.5 w-3.5" />
            Open scene
          </button>
        ) : null}
      </div>
    </div>
  );
}

function RawSegmentListRow({
  item,
  canReadScenes,
  onNavigate,
  selected,
  onToggle,
  selecting,
}: {
  item: RawSegmentItem;
  canReadScenes: boolean;
  onNavigate: (route: any) => void;
  selected: boolean;
  onToggle: () => void;
  selecting: boolean;
}) {
  const title = buildRawSegmentTitle(item);

  return (
    <div onClick={selecting ? onToggle : undefined} className={`group relative cursor-pointer px-4 py-3 transition-colors ${selected ? "bg-accent/10" : "hover:bg-surface/40"}`}>
      <RouteCardLinkOverlay route={{ page: "segment", id: item.id }} onClick={() => onNavigate({ page: "segment", id: item.id })} label={`Open raw segment ${title}`} disabled={selecting} selectionSafeZone />
      <div className="flex items-start gap-3 lg:grid lg:grid-cols-[minmax(0,1.3fr)_140px_minmax(0,1fr)_120px_120px] lg:items-center">
        <div className="relative min-w-0 pl-8">
          <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onToggle} />
          <div className="flex items-start gap-3">
            <div className="hidden h-16 w-24 shrink-0 overflow-hidden rounded-lg bg-surface sm:block">
              <SegmentScenePreview hostId={item.hostId} updatedAt={item.updatedAt} startSec={item.startSec} title={title} imgClassName="h-full w-full object-cover" fallbackClassName="flex h-full w-full items-center justify-center bg-surface text-muted" iconClassName="h-6 w-6" />
            </div>
            <div className="min-w-0">
              <div className="truncate text-sm font-medium text-foreground">{title}</div>
              <div className="mt-1 flex flex-wrap items-center gap-1.5 text-[11px] text-secondary">
                <Pill>#{item.id}</Pill>
                {item.tagName ? <Pill>{item.tagName}</Pill> : null}
                {item.kind ? <Pill>{item.kind}</Pill> : null}
                {item.confidence != null ? <Pill>{item.confidence.toFixed(2)} conf</Pill> : null}
                {item.sourceRunId ? <Pill>{item.sourceRunId}</Pill> : null}
              </div>
            </div>
          </div>
        </div>
        <div className="hidden text-xs text-secondary lg:block">{formatSegmentRange(item.startSec, item.endSec)}</div>
        <div className="min-w-0 text-xs text-secondary lg:text-sm">
          <div className="truncate text-foreground">{item.sceneTitle}</div>
          {canReadScenes ? (
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <button
                type="button"
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onNavigate({ page: "scene", id: item.hostId, seekTo: item.startSec });
                }}
                className="relative z-10 inline-flex items-center gap-1 text-accent hover:underline"
              >
                <FolderOpen className="h-3.5 w-3.5" />
                Open scene
              </button>
            </div>
          ) : null}
        </div>
        <div className="hidden text-xs text-secondary lg:block">{item.sourceKey}</div>
        <div className="hidden text-xs text-secondary lg:block">{formatDate(item.updatedAt)}</div>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-3 pl-8 text-[11px] text-secondary lg:hidden">
        <span>{formatSegmentRange(item.startSec, item.endSec)}</span>
        <span>{item.sourceKey}</span>
        <span>{formatDate(item.updatedAt)}</span>
      </div>
    </div>
  );
}