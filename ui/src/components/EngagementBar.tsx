import { Heart, Star } from "lucide-react";
import type { EngagementBarProps } from "./MediaDetailLayout/types";

export function EngagementBar({
  primaryContent,
  rating,
  favorite,
  favoritePending,
  ratingReadOnly,
  onFavoriteChange,
  onRatingChange,
  additionalMetrics = [],
  className,
}: EngagementBarProps) {
  return (
    <div
      className={[
        "flex flex-wrap items-center gap-2 text-sm text-secondary",
        className,
      ].filter(Boolean).join(" ")}
    >
      {primaryContent ? <div className="shrink-0">{primaryContent}</div> : null}

      {typeof favorite === "boolean" ? (
        onFavoriteChange ? (
          <button
            type="button"
            aria-pressed={favorite}
            disabled={favoritePending}
            onClick={() => onFavoriteChange(!favorite)}
            className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-1 text-xs font-medium text-foreground transition hover:border-accent disabled:cursor-not-allowed disabled:opacity-60"
          >
            <Heart className={["h-3.5 w-3.5", favorite ? "fill-current text-red-300" : "text-muted"].join(" ")} />
            {favorite ? "Favorite" : "Add Favorite"}
          </button>
        ) : (
          <span className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-1 text-xs font-medium text-foreground">
            <Heart className={["h-3.5 w-3.5", favorite ? "fill-current text-red-300" : "text-muted"].join(" ")} />
            {favorite ? "Favorite" : "Not Favorite"}
          </span>
        )
      ) : null}

      {!primaryContent && typeof rating === "number" ? (
        <span className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-1 text-xs font-medium text-foreground">
          <Star className="h-3.5 w-3.5 text-amber-300" />
          Rating {rating}
        </span>
      ) : null}

      {!primaryContent && typeof rating === "number" && onRatingChange && !ratingReadOnly ? (
        <button
          type="button"
          onClick={() => onRatingChange(rating)}
          className="rounded-full border border-border px-3 py-1 text-xs font-medium text-secondary transition hover:border-accent hover:text-foreground"
        >
          Keep {rating}
        </button>
      ) : null}

      {additionalMetrics.map((metric) => {
        const baseClass = [
          "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs font-medium transition",
          metric.active
            ? "border-accent/60 bg-accent/10 text-foreground"
            : "border-border text-secondary",
          metric.onClick ? "cursor-pointer hover:border-accent hover:text-foreground" : "",
        ].filter(Boolean).join(" ");
        const showLabel = !metric.icon;
        const tooltip = metric.title ?? metric.label;
        const content = (
          <>
            {metric.icon ? <span className="inline-flex items-center text-muted">{metric.icon}</span> : null}
            {showLabel ? <span className="text-muted">{metric.label}</span> : null}
            <span className="text-foreground">{metric.value}</span>
          </>
        );
        return metric.onClick ? (
          <button
            key={metric.label}
            type="button"
            onClick={metric.onClick}
            title={tooltip}
            aria-label={metric.label}
            className={baseClass}
          >
            {content}
          </button>
        ) : (
          <span key={metric.label} title={tooltip} aria-label={metric.label} className={baseClass}>
            {content}
          </span>
        );
      })}
    </div>
  );
}