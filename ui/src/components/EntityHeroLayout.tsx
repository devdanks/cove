import { ArrowLeft, Heart } from "lucide-react";
import type { ReactNode } from "react";

export interface EntityHeroCount {
  key: string;
  label: string;
  value: ReactNode;
  icon?: ReactNode;
}

export interface EntityHeroLayoutProps {
  backLabel: string;
  onGoBack: () => void;
  imageUrl?: string | null;
  imageAlt?: string;
  imageFallback?: ReactNode;
  title: ReactNode;
  sortName?: ReactNode;
  aliases?: ReactNode;
  description?: ReactNode;
  counts?: EntityHeroCount[];
  metaRow?: ReactNode;
  favorite?: boolean;
  onFavoriteToggle?: () => void;
  actions?: ReactNode;
  children?: ReactNode;
}

// Shared hero layout used by entity-style detail pages (Tags, Studios, Performers,
// Galleries, Faces). Mirrors the existing Tag/Studio/Performer detail page header
// (cover image left, title + counts top, scrollable content area below).
export function EntityHeroLayout({
  backLabel,
  onGoBack,
  imageUrl,
  imageAlt,
  imageFallback,
  title,
  sortName,
  aliases,
  description,
  counts = [],
  metaRow,
  favorite,
  onFavoriteToggle,
  actions,
  children,
}: EntityHeroLayoutProps) {
  return (
    <div className="min-h-screen">
      <div className="relative overflow-hidden border-b border-border detail-hero-gradient">
        <div className="mx-auto max-w-7xl px-4 py-8">
          <div className="mb-5 flex items-center justify-between gap-4">
            <button
              type="button"
              onClick={onGoBack}
              className="flex items-center gap-1 text-sm text-secondary hover:text-foreground"
            >
              <ArrowLeft className="h-4 w-4" /> {backLabel}
            </button>
            {actions ? <div className="flex items-center gap-2">{actions}</div> : null}
          </div>

          <div className="flex flex-col gap-6 md:flex-row md:items-end">
            <div className="relative flex h-32 w-32 flex-shrink-0 items-center justify-center overflow-hidden rounded-2xl border border-border bg-card shadow-xl shadow-black/35 md:h-36 md:w-36">
              {imageUrl ? (
                <img
                  src={imageUrl}
                  alt={imageAlt ?? ""}
                  className="h-full w-full object-cover"
                  onError={(e) => {
                    (e.target as HTMLImageElement).style.display = "none";
                    const fallback = (e.target as HTMLImageElement).nextElementSibling as HTMLElement | null;
                    if (fallback) fallback.style.display = "flex";
                  }}
                />
              ) : null}
              <div
                className={[
                  "h-full w-full items-center justify-center bg-card text-muted",
                  imageUrl ? "hidden" : "flex",
                ].join(" ")}
              >
                {imageFallback}
              </div>
            </div>

            <div className="min-w-0 flex-1">
              <div className="mb-2 flex items-start gap-4">
                <div className="min-w-0 flex-1">
                  <h1 className="truncate text-2xl font-bold text-foreground sm:text-3xl md:text-4xl">{title}</h1>
                  {sortName ? <p className="mt-1 text-sm text-muted">Sort name: {sortName}</p> : null}
                  {aliases ? <p className="mt-1 text-sm text-secondary">Also known as: {aliases}</p> : null}
                </div>
                {typeof favorite === "boolean" ? (
                  onFavoriteToggle ? (
                    <button
                      type="button"
                      onClick={onFavoriteToggle}
                      className={`rounded-full p-2 transition-colors ${
                        favorite
                          ? "bg-red-500/15 text-red-500"
                          : "bg-card text-muted hover:text-red-400"
                      }`}
                      title={favorite ? "Remove from favorites" : "Add to favorites"}
                    >
                      <Heart className={`h-6 w-6 ${favorite ? "fill-current" : ""}`} />
                    </button>
                  ) : favorite ? (
                    <span className="rounded-full bg-red-500/15 p-2 text-red-500" title="Favorite">
                      <Heart className="h-6 w-6 fill-current" />
                    </span>
                  ) : null
                ) : null}
              </div>

              {description ? (
                <p className="max-w-4xl whitespace-pre-wrap text-sm leading-6 text-secondary">{description}</p>
              ) : null}

              {counts.length > 0 ? (
                <div className="mt-4 flex flex-wrap gap-3">
                  {counts.map((c) => (
                    <div key={c.key} className="flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-2">
                      {c.icon ? <span className="text-accent">{c.icon}</span> : null}
                      <div>
                        <div className="text-lg font-semibold text-foreground">{c.value}</div>
                        <div className="text-xs text-muted">{c.label}</div>
                      </div>
                    </div>
                  ))}
                </div>
              ) : null}

              {metaRow ? <div className="mt-3 flex flex-wrap items-center gap-3 text-xs text-muted">{metaRow}</div> : null}
            </div>
          </div>
        </div>
      </div>

      <div className="w-full px-4 py-6">
        {children}
      </div>
    </div>
  );
}
