import { useMemo } from "react";
import { InteractiveRating } from "./Rating";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { useEntityRatings } from "../hooks/useEntityRatings";
import type { AffinityHostType } from "../api/types";

interface Props {
  hostType: AffinityHostType;
  hostId: number;
  canRate: boolean;
  className?: string;
}

interface AspectDefinition {
  key: string;
  label: string;
}

const DEFAULT_ASPECTS: Partial<Record<AffinityHostType, AspectDefinition[]>> = {
  scene: [
    { key: "audio", label: "Audio" },
    { key: "video_quality", label: "Video Quality" },
    { key: "content", label: "Content" },
    { key: "performers", label: "Performers" },
  ],
  image: [
    { key: "content", label: "Content" },
    { key: "performers", label: "Performers" },
    { key: "quality", label: "Quality" },
  ],
  performer: [
    { key: "face", label: "Face" },
    { key: "body", label: "Body" },
  ],
};

export function AspectRatingsPanel({ hostType, hostId, canRate, className }: Props) {
  const { ratings, isLoading } = useEntityRatings(hostType, hostId, { enabled: hostId > 0 });
  const { setRating } = useEntityEngagement(hostType, hostId, { enabled: false });

  const aspects = useMemo(() => {
    const defaults = DEFAULT_ASPECTS[hostType] ?? [];
    const defaultKeys = new Set(defaults.map((aspect) => aspect.key));
    const extras = Object.keys(ratings)
      .filter((key) => key !== "overall" && !defaultKeys.has(key))
      .sort((left, right) => left.localeCompare(right))
      .map((key) => ({ key, label: formatAspectLabel(key) }));
    return [...defaults, ...extras];
  }, [hostType, ratings]);

  if (aspects.length === 0) {
    return null;
  }

  return (
    <section className={className}>
      <div className="mb-2 flex items-center justify-between gap-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-muted">Rating Breakdown</h3>
        {isLoading ? <span className="text-xs text-muted">Loading...</span> : null}
      </div>
      <div className="grid gap-x-4 gap-y-2 sm:grid-cols-2">
        {aspects.map((aspect) => (
          <div key={aspect.key} className="flex items-center justify-between gap-3 py-1">
            <div className="text-xs uppercase tracking-wide text-muted">{aspect.label}</div>
            <InteractiveRating
              value={ratings[aspect.key]}
              onChange={(value) => setRating(value, aspect.key)}
              readOnly={!canRate}
            />
          </div>
        ))}
      </div>
    </section>
  );
}

function formatAspectLabel(value: string) {
  return value
    .split(/[_-]/g)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}