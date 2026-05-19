import { ArrowLeft, Check, Heart } from "lucide-react";
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
  backgroundImageUrl?: string | null;
  backgroundImageAlt?: string;
  backgroundImageClassName?: string;
  backgroundOverlayClassName?: string;
  imageUrl?: string | null;
  imageAlt?: string;
  imageContainerClassName?: string;
  imageClassName?: string;
  imageFallbackClassName?: string;
  imageFallback?: ReactNode;
  title: ReactNode;
  subtitle?: ReactNode;
  sortName?: ReactNode;
  aliases?: ReactNode;
  description?: ReactNode;
  counts?: EntityHeroCount[];
  metaRow?: ReactNode;
  favorite?: boolean;
  favoritePending?: boolean;
  onFavoriteToggle?: () => void;
  organized?: boolean;
  organizedPending?: boolean;
  onOrganizedToggle?: () => void;
  titleActions?: ReactNode;
  heroContent?: ReactNode;
  actions?: ReactNode;
  heroRowClassName?: string;
  contentClassName?: string;
  children?: ReactNode;
}

// Shared hero layout used by entity-style detail pages (Tags, Studios, Performers,
// Galleries, Faces). Mirrors the existing Tag/Studio/Performer detail page header
// (cover image left, title + counts top, scrollable content area below).
export function EntityHeroLayout({
  backLabel,
  onGoBack,
  backgroundImageUrl,
  backgroundImageAlt,
  backgroundImageClassName,
  backgroundOverlayClassName,
  imageUrl,
  imageAlt,
  imageContainerClassName,
  imageClassName,
  imageFallbackClassName,
  imageFallback,
  title,
  subtitle,
  sortName,
  aliases,
  description,
  counts = [],
  metaRow,
  favorite,
  favoritePending = false,
  onFavoriteToggle,
  organized,
  organizedPending = false,
  onOrganizedToggle,
  titleActions,
  heroContent,
  actions,
  heroRowClassName,
  contentClassName,
  children,
}: EntityHeroLayoutProps) {
  const resolvedImageContainerClassName = imageContainerClassName ?? "relative flex h-48 w-48 flex-shrink-0 items-center justify-center overflow-hidden rounded-xl border border-border bg-card shadow-xl shadow-black/35 md:h-56 md:w-56";
  const resolvedImageClassName = imageClassName ?? "h-full w-full object-cover";
  const resolvedFallbackClassName = imageFallbackClassName ?? "h-full w-full items-center justify-center bg-card text-muted";
  const resolvedHeroRowClassName = heroRowClassName ?? "flex flex-col gap-6 md:flex-row md:items-start";
  const resolvedContentClassName = contentClassName ?? "w-full px-4 py-6";
  const favoriteTitle = favorite ? "Remove favorite" : "Favorite";
  const heroActionClassName = "inline-flex h-10 w-10 items-center justify-center rounded-lg border border-border bg-card transition-colors hover:border-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-60";
  const favoriteAction = typeof favorite === "boolean" ? (
    onFavoriteToggle ? (
      <button
        type="button"
        onClick={onFavoriteToggle}
        disabled={favoritePending}
        aria-pressed={favorite}
        title={favoriteTitle}
        className={`${heroActionClassName} ${favorite ? "text-red-400" : "text-accent"}`}
      >
        <Heart className={`h-4 w-4 ${favorite ? "fill-current" : ""}`} />
      </button>
    ) : (
      <span title={favoriteTitle} className={`inline-flex h-10 w-10 items-center justify-center rounded-lg border border-border bg-card ${favorite ? "text-red-400" : "text-accent"}`}>
        <Heart className={`h-4 w-4 ${favorite ? "fill-current" : ""}`} />
      </span>
    )
  ) : null;
  const organizedTitle = organized ? "Mark unorganized" : "Mark organized";
  const organizedAction = typeof organized === "boolean" ? (
    onOrganizedToggle ? (
      <button
        type="button"
        onClick={onOrganizedToggle}
        disabled={organizedPending}
        aria-pressed={organized}
        title={organizedTitle}
        className={`${heroActionClassName} ${organized ? "text-emerald-400" : "text-secondary"}`}
      >
        <Check className="h-4 w-4" />
      </button>
    ) : organized ? (
      <span title="Organized" className="inline-flex h-10 w-10 items-center justify-center rounded-lg border border-border bg-card text-emerald-400">
        <Check className="h-4 w-4" />
      </span>
    ) : null
  ) : null;
  const hasHeaderActions = Boolean(organizedAction || favoriteAction || actions);

  return (
    <div className="min-h-screen">
      <div className="relative overflow-hidden border-b border-border detail-hero-gradient">
        {backgroundImageUrl ? (
          <>
            <img
              src={backgroundImageUrl}
              alt={backgroundImageAlt ?? ""}
              className={backgroundImageClassName ?? "absolute inset-0 h-full w-full scale-110 object-cover opacity-10 blur-md"}
              onError={(event) => {
                (event.target as HTMLImageElement).style.display = "none";
              }}
            />
            <div className={backgroundOverlayClassName ?? "absolute inset-0 bg-gradient-to-t from-background via-background/70 to-transparent"} />
          </>
        ) : null}

        <div className="relative mx-auto max-w-7xl px-4 py-8">
          <div className="mb-5 flex items-center justify-between gap-4">
            <button
              type="button"
              onClick={onGoBack}
              className="flex items-center gap-1 text-sm text-secondary hover:text-foreground"
            >
              <ArrowLeft className="h-4 w-4" /> {backLabel}
            </button>
            {hasHeaderActions ? <div className="flex items-center gap-2">{organizedAction}{favoriteAction}{actions}</div> : null}
          </div>

          <div className={resolvedHeroRowClassName}>
            <div className={resolvedImageContainerClassName}>
              {imageUrl ? (
                <img
                  src={imageUrl}
                  alt={imageAlt ?? ""}
                  className={resolvedImageClassName}
                  onError={(e) => {
                    (e.target as HTMLImageElement).style.display = "none";
                    const fallback = (e.target as HTMLImageElement).nextElementSibling as HTMLElement | null;
                    if (fallback) fallback.style.display = "flex";
                  }}
                />
              ) : null}
              <div
                className={[
                  resolvedFallbackClassName,
                  imageUrl ? "hidden" : "flex",
                ].join(" ")}
              >
                {imageFallback}
              </div>
            </div>

            <div className="min-w-0 flex-1">
              <div className="mb-2 flex items-start gap-4">
                <div className="min-w-0 flex-1">
                  <h1 className="truncate text-2xl font-bold text-foreground sm:text-3xl">{title}</h1>
                  {subtitle ? <div className="mt-1 text-sm text-secondary">{subtitle}</div> : null}
                  {sortName ? <p className="mt-1 text-sm text-muted">Sort name: {sortName}</p> : null}
                  {aliases ? <p className="mt-1 text-sm text-secondary">Also known as: {aliases}</p> : null}
                </div>
                {titleActions ? <div className="flex items-center gap-2">{titleActions}</div> : null}
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
              {heroContent ? <div className="mt-4">{heroContent}</div> : null}
            </div>
          </div>
        </div>
      </div>

      <div className={resolvedContentClassName}>
        {children}
      </div>
    </div>
  );
}
