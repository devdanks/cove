import { FolderOpen } from "lucide-react";
import { EntityCardGrid } from "../../components/EntityCardGrid";
import { SegmentTile } from "../../components/EntityCards";
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
          <SegmentTile
            key={item.key}
            segment={item}
            label={`Open raw segment ${buildRawSegmentTitle(item)}`}
            onClick={() => (selecting ? onToggle(item.id) : onNavigate({ page: "segment", id: item.id }))}
            selected={selectedIds.has(item.id)}
            onSelect={() => onToggle(item.id)}
            selecting={selecting}
            footer={(
              <div className="flex items-center justify-between gap-2">
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
            )}
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
              <SegmentScenePreview hostId={item.hostId} updatedAt={item.updatedAt} startSec={item.startSec} title={title} imgClassName="h-full w-full object-cover" />
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