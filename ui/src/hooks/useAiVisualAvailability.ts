import { useQuery } from "@tanstack/react-query";
import { extensions } from "../api/client";

function isAiVisualExtension(id: string, name: string) {
  const normalizedId = id.trim().toLowerCase().replace(/[._]/g, "-");
  const normalizedName = name.trim().toLowerCase();

  return normalizedId === "ai-visual" || normalizedId === "ai-visuals" || normalizedName === "ai.visual" || normalizedName === "ai visual";
}

export function useAiVisualAvailability() {
  const { data: installedExtensions } = useQuery({
    queryKey: ["extensions", "ai-visual-availability"],
    queryFn: () => extensions.list(),
    staleTime: 60000,
  });

  return installedExtensions?.some((extension) => extension.enabled && extension.hasApi && isAiVisualExtension(extension.id, extension.name)) ?? false;
}