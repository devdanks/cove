import type { CSSProperties, ReactNode } from "react";

interface EntityCardGridProps {
  children: ReactNode;
  minCardWidth?: string;
  gapClassName?: string;
  className?: string;
}

export function EntityCardGrid({
  children,
  minCardWidth = "var(--card-min-width, 200px)",
  gapClassName = "gap-3",
  className = "",
}: EntityCardGridProps) {
  return (
    <div
      className={["grid", gapClassName, className].filter(Boolean).join(" ")}
      style={{ gridTemplateColumns: `repeat(auto-fill, minmax(${minCardWidth}, 1fr))` } as CSSProperties}
    >
      {children}
    </div>
  );
}
