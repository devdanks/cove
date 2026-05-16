import type { ReactNode } from "react";

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
}

export function FeedCardFrame({ dataAttribute, selected, header, headerActions, media, title, details, metadata, chips }: FeedCardFrameProps) {
  const attributeProps = dataAttribute ?? {};

  return (
    <article
      {...attributeProps}
      className={`group overflow-hidden rounded-xl border bg-card shadow-sm transition-colors ${selected ? "border-accent ring-1 ring-accent/60" : "border-border hover:border-accent/50"}`}
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
      onClick={onClick}
      className="rounded-full border border-border px-2 py-0.5 transition-colors hover:border-accent/50 hover:text-foreground"
    >
      {children}
    </button>
  );
}
