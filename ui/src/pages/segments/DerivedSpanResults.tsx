import { ExternalLink, FolderOpen } from "lucide-react";
import { EntityCardGrid } from "../../components/EntityCardGrid";
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
}: Props) {
  if (displayMode === "grid") {
    return (
      <EntityCardGrid minCardWidth="var(--card-min-width, 220px)">
        {items.map((item) => (
          <DerivedSpanCard
            key={item.key}
            item={item}
            canReadScenes={canReadScenes}
            onNavigate={onNavigate}
            onClick={() => (selecting ? onToggle(item.id) : onNavigate({ page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor }))}
            onViewRawSegments={() => onViewRawSegments(item.span.segmentIds)}
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

function DerivedSpanCard({
  item,
  canReadScenes,
  onNavigate,
  onClick,
  onViewRawSegments,
  selected,
  onSelect,
  selecting,
}: {
  item: DerivedSpanItem;
  canReadScenes: boolean;
  onNavigate: (route: any) => void;
  onClick: () => void;
  onViewRawSegments: () => void;
  selected?: boolean;
  onSelect?: () => void;
  selecting?: boolean;
}) {
  const title = buildSpanTitle(item.span, item.sceneTitle);
  const primaryRawSegmentId = item.span.segmentIds[0];

  return (
    <div
      onClick={selecting ? onClick : undefined}
      className={`entity-card group relative overflow-hidden rounded border bg-card transition-all ${selected ? "border-accent ring-2 ring-accent" : "border-border hover:border-accent/60"}`}
    >
      <RouteCardLinkOverlay
        route={{ page: "scene-span", id: item.sceneId, spanKey: item.span.spanKey, profileId: item.profileId, derivedQueryDescriptor: item.derivedQueryDescriptor }}
        onClick={onClick}
        label={`Open span ${title}`}
        disabled={selecting}
        selectionSafeZone={selected !== undefined || selecting}
      />
      <div className="relative aspect-video w-full overflow-hidden bg-surface/70">
        {(selected !== undefined || selecting) && <CardSelectionToggle selected={selected} selecting={selecting} onToggle={onSelect} />}
        <SegmentScenePreview hostId={item.sceneId} updatedAt={item.sceneUpdatedAt} title={title} imgClassName="h-full w-full object-cover" fallbackClassName="flex h-full w-full items-center justify-center bg-surface text-muted" iconClassName="h-12 w-12" />
        <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/85 via-black/35 to-transparent p-3 text-white">
          <div className="text-xs font-medium uppercase tracking-wide text-white/75">{formatSegmentRange(item.span.startSec, item.span.endSec)}</div>
          <div className="mt-1 line-clamp-2 text-sm font-semibold">{title}</div>
        </div>
      </div>

      <div className="border-t border-border bg-card p-3">
        <div className="space-y-1">
          <div className="flex flex-wrap gap-2">
            <Pill>{formatSpanItemKindLabel(item)}</Pill>
          </div>
          <div className="line-clamp-2 text-sm font-medium text-foreground">{title}</div>
          <div className="truncate text-xs text-secondary">{item.sceneTitle}</div>
        </div>
      </div>

      <div className="relative z-10 flex flex-wrap items-center gap-1.5 border-t border-border px-3 py-2 text-[11px]">
        {item.span.tagName ? <Pill>{item.span.tagName}</Pill> : null}
        {item.span.kind ? <Pill>{item.span.kind}</Pill> : null}
        <Pill>{formatSegmentDuration(item.span.startSec, item.span.endSec)}</Pill>
        {item.span.sourceKey ? <Pill>{item.span.sourceKey}</Pill> : null}
        <span className="ml-auto text-muted">{item.span.segmentIds.length} raw segment{item.span.segmentIds.length === 1 ? "" : "s"}</span>
      </div>

      <div className="relative z-10 flex items-center justify-between gap-2 border-t border-border px-3 py-2 text-xs text-secondary">
        <span>Updated {formatDate(item.sceneUpdatedAt)}</span>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={(event) => {
              event.preventDefault();
              event.stopPropagation();
              onViewRawSegments();
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
              <SegmentScenePreview hostId={item.sceneId} updatedAt={item.sceneUpdatedAt} title={title} imgClassName="h-full w-full object-cover" fallbackClassName="flex h-full w-full items-center justify-center bg-surface text-muted" iconClassName="h-6 w-6" />
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