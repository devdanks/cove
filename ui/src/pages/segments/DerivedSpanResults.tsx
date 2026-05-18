import { ExternalLink, FolderOpen } from "lucide-react";
import { VirtualizedEntityGrid } from "../../components/VirtualizedEntityLayouts";
import { SegmentTile } from "../../components/EntityCards";
import type { DisplayMode } from "../../components/ListPage";
import { CardSelectionToggle, RouteCardLinkOverlay } from "../../components/RouteCardLinkOverlay";
import {
  buildSpanTitle,
  formatDate,
  formatSegmentDuration,
  formatSegmentRange,
  formatSpanItemKindLabel,
  Pill,
  SegmentScenePreview,
} from "./segmentDisplayUtils";
import type { DerivedSpanItem } from "./types";

interface Props {
  displayMode: DisplayMode;
  items: DerivedSpanItem[];
  canReadScenes: boolean;
  onNavigate: (route: any) => void;
  onViewRawSegments: (segmentIds: number[]) => void;
  selectedIds: Set<string | number>;
  onToggle: (id: string | number) => void;
  selecting: boolean;
  infinitePageSize: boolean;
  hasNextPage?: boolean;
  isFetchingNextPage?: boolean;
  loadMore: () => void;
}

export function DerivedSpanResults({
  displayMode,
  items,
  canReadScenes,
  onNavigate,
  onViewRawSegments,
  selectedIds,
  onToggle,
  selecting,
  infinitePageSize,
  hasNextPage,
  isFetchingNextPage,
  loadMore,
}: Props) {
  if (displayMode === "grid") {
    return (
      <VirtualizedEntityGrid
        items={items}
        getItemKey={(item) => item.key}
        minCardWidth="var(--card-min-width, 220px)"
        estimateRowHeight={320}
        infinitePageSize={infinitePageSize}
        hasNextPage={hasNextPage}
        isFetchingNextPage={isFetchingNextPage}
        loadMore={loadMore}
        renderItem={(item) => {
          const title = buildSpanTitle(item.span, item.sceneTitle);
          const route = { page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor };
          const primaryRawSegmentId = item.span.segmentIds[0];

          return (
            <SegmentTile
              segment={{
                id: item.id,
                hostType: "scene",
                hostId: item.sceneId,
                startSec: item.span.startSec,
                endSec: item.span.endSec,
                tagName: item.span.tagName,
                kind: item.span.kind,
                sourceKey: item.span.sourceKey,
                title,
                updatedAt: item.sceneUpdatedAt,
                hostTitle: item.sceneTitle,
              }}
              route={route}
              label={`Open span ${title}`}
              eyebrow={formatSegmentRange(item.span.startSec, item.span.endSec)}
              onClick={() => (selecting ? onToggle(item.id) : onNavigate(route))}
              selected={selectedIds.has(item.id)}
              onSelect={() => onToggle(item.id)}
              selecting={selecting}
              footer={(
                <div className="flex items-center justify-between gap-2">
                  <span>Updated {formatDate(item.sceneUpdatedAt)}</span>
                  <div className="flex items-center gap-2">
                    <button
                      type="button"
                      onClick={(event) => {
                        event.preventDefault();
                        event.stopPropagation();
                        onViewRawSegments(item.span.segmentIds);
                      }}
                      className="inline-flex items-center gap-1 text-accent hover:underline"
                    >
                      View raw segments ({item.span.segmentIds.length})
                    </button>
                    {primaryRawSegmentId != null ? (
                      <button
                        type="button"
                        onClick={(event) => {
                          event.preventDefault();
                          event.stopPropagation();
                          onNavigate({ page: "segment", id: primaryRawSegmentId });
                        }}
                        className="inline-flex items-center gap-1 text-accent hover:underline"
                      >
                        <ExternalLink className="h-3.5 w-3.5" />
                        Open raw
                      </button>
                    ) : null}
                    {canReadScenes ? (
                      <button
                        type="button"
                        onClick={(event) => {
                          event.preventDefault();
                          event.stopPropagation();
                          onNavigate({ page: "scene", id: item.sceneId, seekTo: item.span.startSec });
                        }}
                        className="inline-flex items-center gap-1 text-accent hover:underline"
                      >
                        <FolderOpen className="h-3.5 w-3.5" />
                        Open scene
                      </button>
                    ) : null}
                  </div>
                </div>
              )}
            />
          );
        }}
      />
    );
  }

  return (
    <div className="overflow-hidden rounded-xl border border-border bg-card">
      <div className="hidden grid-cols-[minmax(0,1.4fr)_140px_minmax(0,1.1fr)_120px_120px] gap-3 border-b border-border bg-surface/70 px-4 py-2 text-[11px] font-medium uppercase tracking-wide text-muted lg:grid">
        <span>Span</span>
        <span>Range</span>
        <span>Scene</span>
        <span>Source</span>
        <span>Updated</span>
      </div>
      <div className="divide-y divide-border">
        {items.map((item) => (
          <DerivedSpanListRow
            key={item.key}
            item={item}
            canReadScenes={canReadScenes}
            onNavigate={onNavigate}
            onViewRawSegments={onViewRawSegments}
            selected={selectedIds.has(item.id)}
            onToggle={() => onToggle(item.id)}
            selecting={selecting}
          />
        ))}
      </div>
    </div>
  );
}

function DerivedSpanListRow({
  item,
  canReadScenes,
  onNavigate,
  onViewRawSegments,
  selected,
  onToggle,
  selecting,
}: {
  item: DerivedSpanItem;
  canReadScenes: boolean;
  onNavigate: (route: any) => void;
  onViewRawSegments: (segmentIds: number[]) => void;
  selected: boolean;
  onToggle: () => void;
  selecting: boolean;
}) {
  const title = buildSpanTitle(item.span, item.sceneTitle);
  const primaryRawSegmentId = item.span.segmentIds[0];

  return (
    <div onClick={selecting ? onToggle : undefined} className={`group relative cursor-pointer px-4 py-3 transition-colors ${selected ? "bg-accent/10" : "hover:bg-surface/40"}`}>
      <RouteCardLinkOverlay route={{ page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor }} onClick={() => onNavigate({ page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor })} label={`Open span ${title}`} disabled={selecting} selectionSafeZone />
      <div className="flex items-start gap-3 lg:grid lg:grid-cols-[minmax(0,1.4fr)_140px_minmax(0,1.1fr)_120px_120px] lg:items-center">
        <div className="relative min-w-0 pl-8">
          <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onToggle} />
          <div className="flex items-start gap-3">
            <div className="hidden h-16 w-24 shrink-0 overflow-hidden rounded-lg bg-surface sm:block">
              <SegmentScenePreview hostId={item.sceneId} updatedAt={item.sceneUpdatedAt} startSec={item.span.startSec} title={title} imgClassName="h-full w-full object-cover" />
            </div>
            <div className="min-w-0">
              <div className="truncate text-sm font-medium text-foreground">{title}</div>
              <div className="mt-1 flex flex-wrap items-center gap-1.5 text-[11px] text-secondary">
                <Pill>{formatSpanItemKindLabel(item)}</Pill>
                {item.span.tagName ? <Pill>{item.span.tagName}</Pill> : null}
                {item.span.kind ? <Pill>{item.span.kind}</Pill> : null}
                <Pill>{formatSegmentDuration(item.span.startSec, item.span.endSec)}</Pill>
                <span>{item.span.segmentIds.length} raw segment{item.span.segmentIds.length === 1 ? "" : "s"}</span>
              </div>
            </div>
          </div>
        </div>
        <div className="hidden text-xs text-secondary lg:block">{formatSegmentRange(item.span.startSec, item.span.endSec)}</div>
        <div className="min-w-0 text-xs text-secondary lg:text-sm">
          <div className="truncate text-foreground">{item.sceneTitle}</div>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={(event) => {
                event.preventDefault();
                event.stopPropagation();
                onViewRawSegments(item.span.segmentIds);
              }}
              className="relative z-10 inline-flex items-center gap-1 text-accent hover:underline"
            >
              View raw segments ({item.span.segmentIds.length})
            </button>
            {primaryRawSegmentId != null ? (
              <button
                type="button"
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onNavigate({ page: "segment", id: primaryRawSegmentId });
                }}
                className="relative z-10 inline-flex items-center gap-1 text-accent hover:underline"
              >
                <ExternalLink className="h-3.5 w-3.5" />
                Open raw
              </button>
            ) : null}
            {canReadScenes ? (
              <button
                type="button"
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onNavigate({ page: "scene", id: item.sceneId, seekTo: item.span.startSec });
                }}
                className="relative z-10 mt-1 inline-flex items-center gap-1 text-accent hover:underline"
              >
                <FolderOpen className="h-3.5 w-3.5" />
                Open scene
              </button>
            ) : null}
          </div>
        </div>
        <div className="hidden text-xs text-secondary lg:block">{item.span.sourceKey || "Derived"}</div>
        <div className="hidden text-xs text-secondary lg:block">{formatDate(item.sceneUpdatedAt)}</div>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-3 pl-8 text-[11px] text-secondary lg:hidden">
        <span>{formatSegmentRange(item.span.startSec, item.span.endSec)}</span>
        <span>{item.span.sourceKey || "Derived"}</span>
        <span>{formatDate(item.sceneUpdatedAt)}</span>
      </div>
    </div>
  );
}