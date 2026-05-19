import type { CSSProperties, ReactNode } from "react";

export interface FeedMediaDimensions {
  width?: number | null;
  height?: number | null;
}

export function getFeedMediaStyle(media: FeedMediaDimensions | undefined): CSSProperties | undefined {
  if (!media?.width || !media.height || media.height <= media.width) {
    return undefined;
  }

  const ratio = media.width / media.height;
  return { maxWidth: `min(100%, ${56 * ratio}vh, ${34 * ratio}rem)` };
}

interface FeedPortraitMediaFrameProps {
  title: string;
  backgroundSrc?: string | null;
  media: ReactNode;
  children?: ReactNode;
  className?: string;
}

export function FeedPortraitMediaFrame({ title, backgroundSrc, media, children, className }: FeedPortraitMediaFrameProps) {
  return (
    <div
      className={`relative h-[min(72vh,46rem)] overflow-hidden border-y border-border/60 bg-surface ${className ?? ""}`.trim()}
      title={title}
    >
      {backgroundSrc ? (
        <>
          <img src={backgroundSrc} alt="" aria-hidden="true" className="absolute inset-0 h-full w-full scale-110 object-cover opacity-55 blur-2xl" loading="lazy" />
          <div className="absolute inset-0 bg-black/15" />
        </>
      ) : null}
      <div className="relative z-0 h-full w-full">{media}</div>
      {children}
    </div>
  );
}

interface FeedCardFrameProps {
  dataAttribute?: Record<string, string | number>;
  selected?: boolean;
  header: ReactNode;
  headerActions?: ReactNode;
  media: ReactNode;
  title: ReactNode;
  details?: ReactNode;
  metadata?: ReactNode;
  chips?: ReactNode;
  onClick?: () => void;
}

export function FeedCardFrame({ dataAttribute, selected, header, headerActions, media, title, details, metadata, chips, onClick }: FeedCardFrameProps) {
  const attributeProps = dataAttribute ?? {};

  return (
    <article
      {...attributeProps}
      onClick={onClick}
      className={`group overflow-hidden rounded-xl border bg-card shadow-sm transition-colors ${onClick ? "cursor-pointer" : ""} ${selected ? "border-accent ring-1 ring-accent/60" : "border-border hover:border-accent/50"}`}
    >
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 py-3 text-xs text-muted">
        <div className="flex min-w-0 flex-wrap items-center gap-2">{header}</div>
        {headerActions ? <div className="flex shrink-0 items-center gap-2">{headerActions}</div> : null}
      </div>
      {media}
      <div className="space-y-2 p-4">
        {title}
        {details ? <div className="text-sm text-secondary">{details}</div> : null}
        {metadata ? <div className="flex flex-wrap gap-1.5 text-xs text-muted">{metadata}</div> : null}
        {chips ? <div className="flex flex-wrap gap-1.5 text-xs text-muted">{chips}</div> : null}
      </div>
    </article>
  );
}

export function FeedMetadataPill({ children }: { children: ReactNode }) {
  return <span className="rounded border border-border px-2 py-0.5">{children}</span>;
}

export function FeedChipButton({ children, onClick }: { children: ReactNode; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={(event) => {
        event.stopPropagation();
        onClick();
      }}
      className="rounded-full border border-border px-2 py-0.5 transition-colors hover:border-accent/50 hover:text-foreground"
    >
      {children}
    </button>
  );
}
