import { useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { groups } from "../api/client";
import { CompilationPlayer } from "../components/CompilationPlayer";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { useBackNavigation } from "../hooks/useBackNavigation";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

export function CompilationPlayerPage({ id, onNavigate }: Props) {
  const { backLabel, goBack } = useBackNavigation({ page: "group", id }, onNavigate);
  const { data: group, isLoading: groupLoading } = useQuery({
    queryKey: ["group", id],
    queryFn: () => groups.get(id),
  });
  const { data: manifest, isLoading: manifestLoading } = useQuery({
    queryKey: ["group", id, "playback-manifest"],
    queryFn: () => groups.items.playbackManifest(id),
  });

  useEffect(() => {
    if (!group) {
      return;
    }

    document.title = `${group.name} | Cove`;
    return () => {
      document.title = "Cove";
    };
  }, [group]);

  if (groupLoading || manifestLoading) {
    return (
      <div className="px-1 py-2">
        <DetailSkeleton />
      </div>
    );
  }

  if (!group || !manifest || manifest.items.length === 0) {
    return <div className="py-16 text-center text-secondary">Compilation playback is unavailable for this group yet.</div>;
  }

  return (
    <CompilationPlayer
      groupId={id}
      groupName={group.name}
      items={manifest.items}
      onNavigate={onNavigate}
      backLabel={backLabel}
      onGoBack={goBack}
    />
  );
}
