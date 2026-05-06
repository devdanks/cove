import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { faces, galleries, images, metadata, performers, scenes, entityImages } from "../api/client";
import type { Face, FaceSimilar, FindFilter, Gallery, Image, Performer as PerformerModel, Scene, MetadataServer, MetadataServerPerformerMatch } from "../api/types";
import { formatDate, formatDuration, getResolutionLabel, TagBadge, CustomFieldsDisplay } from "../components/shared";
import { Calendar, ChevronDown, CloudDownload, ExternalLink, Film, FolderOpen, GitMerge, Heart, ImageIcon, Layers, Link2, Loader2, MapPin, MoreVertical, Music, Pencil, Ruler, Scale, Search, Trash2, Users, UserRound, Wand2 } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { PerformerEditModal } from "./PerformerEditModal";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { DetailMergeDialog } from "../components/DetailMergeDialog";
import { ExtensionSlot } from "../router/RouteRegistry";
import { AspectRatingsPanel } from "../components/AspectRatingsPanel";
import { SceneCard, GalleryTile, ImageTile } from "../components/EntityCards";
import { InteractiveRating } from "../components/Rating";
import { QuickViewDialog } from "../components/QuickViewDialog";
import { useAppConfig } from "../state/AppConfigContext";
import { DetailListToolbar } from "../components/DetailListToolbar";
import { useMultiSelect } from "../hooks/useMultiSelect";
import { BulkSelectionActions } from "../components/BulkSelectionActions";
import { useExtensionTabs } from "../components/useExtensionTabs";
import { EntityDetailTabs } from "../components/EntityDetailTabs";
import { EntityCardGrid } from "../components/EntityCardGrid";
import { EntityHeroLayout } from "../components/EntityHeroLayout";
import { createRouteLinkProps } from "../components/cardNavigation";
import { SCENE_SORT_OPTIONS } from "../components/sceneSortOptions";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { GALLERY_SORT_OPTIONS } from "../components/gallerySortOptions";
import { PerformerScrapeDialog } from "../components/PerformerScrapeDialog";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity, filterItemsByPermission, hasAnyPermission } from "../auth/visibility";

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type TabKey = "scenes" | "galleries" | "images" | "groups" | "appearsWith" | (string & {});

const IMAGE_SORT = [
  { value: "updated_at", label: "Updated At" },
  { value: "created_at", label: "Created At" },
  { value: "title", label: "Title" },
  { value: "rating", label: "Rating" },
  { value: "random", label: "Random" },
];
const GALLERY_SORT = GALLERY_SORT_OPTIONS;
const GROUP_SORT = [
  { value: "name", label: "Name" },
  { value: "updated_at", label: "Updated At" },
  { value: "created_at", label: "Created At" },
  { value: "random", label: "Random" },
];

export function PerformerDetailPage({ id, onNavigate }: Props) {
  const { config } = useAppConfig();
  const { hasPermission, user } = useAuth();
  const metadataServers = config?.scraping?.metadataServers ?? [];
  const { data: performer, isLoading } = useQuery({
    queryKey: ["performer", id],
    queryFn: () => performers.get(id),
  });
  const [editing, setEditing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [mergeOpen, setMergeOpen] = useState(false);
  const [scrapeOpen, setScrapeOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<TabKey>("scenes");
  const [sceneFilter, setSceneFilter] = useState<FindFilter>({ page: 1, perPage: 24, direction: "desc" });
  const [galleryFilter, setGalleryFilter] = useState<FindFilter>({ page: 1, perPage: 18, direction: "desc" });
  const [imageFilter, setImageFilter] = useState<FindFilter>({ page: 1, perPage: 30, direction: "desc" });
  const [groupFilter, setGroupFilter] = useState<FindFilter>({ page: 1, perPage: 18, direction: "asc" });
  const [appearsWithFilter, setAppearsWithFilter] = useState<FindFilter>({ page: 1, perPage: 18, direction: "asc" });
  const { allTabs: performerTabs, renderExtensionTab, extensionCounts } = useExtensionTabs("performer", [
    { key: "scenes", label: "Scenes", count: performer?.sceneCount },
    { key: "galleries", label: "Galleries", count: performer?.galleryCount },
    { key: "images", label: "Images", count: performer?.imageCount },
    { key: "groups", label: "Groups", count: performer?.groupCount },
    { key: "appearsWith", label: "Appears With" },
  ], id);
  const [showOpsMenu, setShowOpsMenu] = useState(false);
  const opsMenuRef = useRef<HTMLDivElement>(null);
  const queryClient = useQueryClient();
  const { backLabel, goBack } = useBackNavigation({ page: "performers" }, onNavigate);
  const canWritePerformer = canWriteEntity("performer", hasPermission);
  const canEngagePerformer = canReadEntity("performer", hasPermission) && (user?.kind === "user" || user?.kind === "system");
  const canDeletePerformer = canDeleteEntity("performer", hasPermission);
  const canReadFaces = canReadEntity("face", hasPermission);
  const canReadPerformerScenes = canReadEntity("scene", hasPermission);
  const canReadPerformerGalleries = canReadEntity("gallery", hasPermission);
  const canReadPerformerImages = canReadEntity("image", hasPermission);
  const canReadPerformerGroups = canReadEntity("group", hasPermission);
  const canReadTags = canReadEntity("tag", hasPermission);
  const canAutoTagPerformer = hasPermission("library.autotag") && canWritePerformer;
  const canScrapePerformer = hasAnyPermission(hasPermission, ["performers.scrape", "performers.write"]);
  const showPerformerOpsMenu = canWritePerformer || canAutoTagPerformer || canScrapePerformer || canDeletePerformer;
  const visiblePerformerTabs = filterItemsByPermission(performerTabs, {
    scenes: "scenes.read",
    galleries: "galleries.read",
    images: "images.read",
    groups: "groups.read",
    appearsWith: "performers.read",
  }, hasPermission);

  const deleteMut = useMutation({
    mutationFn: () => performers.delete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["performers"] });
      goBack();
    },
  });

  const {
    favorite: performerFavorite,
    rating: performerRating,
    setFavorite: setPerformerFavorite,
    setRating: setPerformerRating,
  } = useEntityEngagement("performer", id, {
    fallbackFavorite: performer?.favorite,
    fallbackRating: performer?.rating,
  });

  const autoTagMut = useMutation({
    mutationFn: () => {
      if (!performer) throw new Error("Performer not loaded");
      return metadata.autoTag({ performers: [performer.name] });
    },
  });

  useEffect(() => {
    if (performer) document.title = `${performer.name} | Cove`;
    return () => { document.title = "Cove"; };
  }, [performer]);

  // Keyboard shortcuts
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;
      switch (e.key) {
        case "e": if (canWritePerformer) setEditing((v) => !v); break;
        case "f": if (performer && canEngagePerformer) setPerformerFavorite(!performerFavorite); break;
        case "c": if (canReadPerformerScenes) setActiveTab("scenes"); break;
        case "g": if (canReadPerformerGalleries) setActiveTab("galleries"); break;
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [canEngagePerformer, canReadPerformerGalleries, canReadPerformerScenes, canWritePerformer, performer, performerFavorite, setPerformerFavorite]);

  useEffect(() => {
    if (!showOpsMenu) return;
    const handler = (e: MouseEvent) => {
      if (opsMenuRef.current && !opsMenuRef.current.contains(e.target as Node)) setShowOpsMenu(false);
    };
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [showOpsMenu]);

  useEffect(() => {
    if (visiblePerformerTabs.length > 0 && !visiblePerformerTabs.some((tab) => tab.key === activeTab)) {
      setActiveTab(visiblePerformerTabs[0].key as TabKey);
    }
  }, [activeTab, visiblePerformerTabs]);

  if (isLoading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-accent" />
      </div>
    );
  }

  if (!performer) {
    return <div className="py-16 text-center text-secondary">Performer not found</div>;
  }

  const age = performer.birthdate
    ? Math.floor((Date.now() - new Date(performer.birthdate).getTime()) / 31557600000)
    : null;

  return (
    <>
      <EntityHeroLayout
        backLabel={backLabel}
        onGoBack={goBack}
        backgroundImageUrl={entityImages.performerImageUrl(performer.id, performer.updatedAt, 1600)}
        imageUrl={performer.imagePath || entityImages.performerImageUrl(performer.id, performer.updatedAt, 1200)}
        imageAlt={performer.name}
        imageContainerClassName="aspect-[2/3] w-48 flex-shrink-0 overflow-hidden rounded-2xl border border-border bg-card shadow-2xl shadow-black/40 md:w-64"
        imageFallbackClassName="flex h-full w-full items-center justify-center bg-gradient-to-b from-card to-surface"
        imageFallback={<UserRound className="h-20 w-20 text-muted/50" />}
        title={performer.name}
        subtitle={performer.disambiguation || undefined}
        aliases={performer.aliases.length > 0 ? performer.aliases.join(", ") : undefined}
        favorite={performerFavorite}
        onFavoriteToggle={canEngagePerformer ? () => setPerformerFavorite(!performerFavorite) : undefined}
        counts={[
          { key: "scenes", label: "Scenes", value: performer.sceneCount, icon: <Film className="h-4 w-4" /> },
          { key: "galleries", label: "Galleries", value: performer.galleryCount, icon: <FolderOpen className="h-4 w-4" /> },
          { key: "images", label: "Images", value: performer.imageCount, icon: <ImageIcon className="h-4 w-4" /> },
          { key: "groups", label: "Groups", value: performer.groupCount, icon: <Layers className="h-4 w-4" /> },
          ...extensionCounts.map((ec) => ({
            key: ec.key,
            label: ec.label,
            value: ec.count,
            icon: ec.icon === "music" ? <Music className="h-4 w-4" /> : <Layers className="h-4 w-4" />,
          })),
        ]}
        heroContent={(
          <>
            <InteractiveRating value={performerRating} onChange={(value) => setPerformerRating(value)} readOnly={!canEngagePerformer} />
            <AspectRatingsPanel hostType="performer" hostId={id} canRate={canEngagePerformer} className="mt-3" />

            <div className="mt-4 grid grid-cols-2 gap-3 md:grid-cols-4">
              {performer.gender && <InfoItem icon={<UserRound className="h-4 w-4" />} label="Gender" value={performer.gender} />}
              {performer.birthdate && (
                <InfoItem icon={<Calendar className="h-4 w-4" />} label="Born" value={`${formatDate(performer.birthdate)}${age ? ` (${age})` : ""}`} />
              )}
              {performer.deathDate && <InfoItem icon={<Calendar className="h-4 w-4" />} label="Died" value={formatDate(performer.deathDate)} />}
              {performer.country && <InfoItem icon={<MapPin className="h-4 w-4" />} label="Country" value={performer.country} />}
              {performer.ethnicity && <InfoItem label="Ethnicity" value={performer.ethnicity} />}
              {performer.heightCm && <InfoItem icon={<Ruler className="h-4 w-4" />} label="Height" value={`${performer.heightCm} cm`} />}
              {performer.weight && <InfoItem icon={<Scale className="h-4 w-4" />} label="Weight" value={`${performer.weight} kg`} />}
              {performer.measurements && <InfoItem label="Measurements" value={performer.measurements} />}
              {performer.eyeColor && <InfoItem label="Eye Color" value={performer.eyeColor} />}
              {performer.hairColor && <InfoItem label="Hair Color" value={performer.hairColor} />}
              {performer.fakeTits && <InfoItem label="Fake Tits" value={performer.fakeTits} />}
              {performer.penisLength != null && <InfoItem label="Penis Length" value={`${performer.penisLength} cm`} />}
              {performer.circumcised && <InfoItem label="Circumcised" value={performer.circumcised} />}
              {performer.tattoos && <InfoItem label="Tattoos" value={performer.tattoos} />}
              {performer.piercings && <InfoItem label="Piercings" value={performer.piercings} />}
              {performer.careerStart && <InfoItem label="Career" value={`${performer.careerStart}${performer.careerEnd ? ` – ${performer.careerEnd}` : " – present"}`} />}
            </div>

            {performer.urls.length > 0 ? (
              <div className="mt-4 flex flex-wrap gap-2">
                {performer.urls.map((url, i) => (
                  <a key={i} href={url} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1.5 rounded-full border border-border bg-card px-3 py-1 text-xs text-accent hover:border-accent/60 hover:text-accent-hover">
                    <ExternalLink className="h-3 w-3" />
                    {(() => { try { return new URL(url).hostname.replace("www.", ""); } catch { return url; } })()}
                  </a>
                ))}
              </div>
            ) : null}

            {autoTagMut.isSuccess ? <p className="mt-4 text-sm text-emerald-300">Auto-tag job queued.</p> : null}

            {canReadTags && performer.tags.length > 0 ? (
              <div className="mt-4 flex flex-wrap gap-1.5">
                {performer.tags.map((tag) => (
                  <TagBadge key={tag.id} name={tag.name} onClick={() => onNavigate({ page: "tag", id: tag.id })} />
                ))}
              </div>
            ) : null}

            {performer.details ? <p className="mt-4 max-w-4xl whitespace-pre-wrap text-sm leading-6 text-secondary">{performer.details}</p> : null}
            <CustomFieldsDisplay customFields={performer.customFields} />
            <PerformerFaceSimilarityPanel performerId={id} canReadFaces={canReadFaces} onNavigate={onNavigate} />
            <PerformerMetadataServerPanel performer={performer} metadataServers={metadataServers} onNavigate={onNavigate} />
          </>
        )}
        actions={(
          <>
            <ExtensionSlot slot="performer-detail-actions" context={{ performer, onNavigate }} />
            {showPerformerOpsMenu ? (
              <div className="relative" ref={opsMenuRef}>
                <button onClick={() => setShowOpsMenu(!showOpsMenu)} className="rounded border border-border bg-card p-2 text-secondary hover:text-foreground" title="Actions">
                  <MoreVertical className="h-4 w-4" />
                </button>
                {showOpsMenu ? (
                  <div className="absolute right-0 z-50 mt-1 min-w-[160px] rounded-lg border border-border bg-card py-1 shadow-xl">
                    {canWritePerformer ? <button onClick={() => { setEditing(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground hover:bg-surface"><Pencil className="h-3.5 w-3.5" /> Edit</button> : null}
                    {canAutoTagPerformer ? <button onClick={() => { autoTagMut.mutate(); setShowOpsMenu(false); }} disabled={autoTagMut.isPending} className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground hover:bg-surface disabled:opacity-60">{autoTagMut.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Wand2 className="h-3.5 w-3.5" />} Auto Tag</button> : null}
                    {canScrapePerformer ? <button onClick={() => { setScrapeOpen(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground hover:bg-surface"><Search className="h-3.5 w-3.5" /> Scrape...</button> : null}
                    {canWritePerformer ? <button onClick={() => { setMergeOpen(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-2 text-sm text-foreground hover:bg-surface"><GitMerge className="h-3.5 w-3.5" /> Merge...</button> : null}
                    {canDeletePerformer ? <div className="my-1 border-t border-border" /> : null}
                    {canDeletePerformer ? <button onClick={() => { setConfirmDelete(true); setShowOpsMenu(false); }} className="flex w-full items-center gap-2 px-3 py-2 text-sm text-red-400 hover:bg-surface"><Trash2 className="h-3.5 w-3.5" /> Delete</button> : null}
                  </div>
                ) : null}
              </div>
            ) : null}
          </>
        )}
        heroRowClassName="flex flex-col gap-6 md:flex-row md:items-start"
      >
        <EntityDetailTabs tabs={visiblePerformerTabs} activeTab={activeTab} onTabChange={(key) => setActiveTab(key as TabKey)} className="mx-auto max-w-7xl mt-0" />

        <div className="py-6">
          {activeTab === "scenes" && (
            <PerformerScenesPanel performerId={id} filter={sceneFilter} setFilter={setSceneFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "galleries" && (
            <PerformerGalleriesPanel performerId={id} filter={galleryFilter} setFilter={setGalleryFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "images" && (
            <PerformerImagesPanel performerId={id} filter={imageFilter} setFilter={setImageFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "groups" && (
            <PerformerGroupsPanel performerId={id} filter={groupFilter} setFilter={setGroupFilter} onNavigate={onNavigate} />
          )}
          {activeTab === "appearsWith" && (
            <PerformerAppearsWithPanel performerId={id} filter={appearsWithFilter} setFilter={setAppearsWithFilter} onNavigate={onNavigate} />
          )}
          {renderExtensionTab(activeTab, id, onNavigate)}
        </div>

        <ExtensionSlot slot="performer-detail-bottom" context={{ performer, onNavigate }} />
      </EntityHeroLayout>

      <PerformerEditModal performer={performer} open={editing} onClose={() => setEditing(false)} />
      <ConfirmDialog
        open={confirmDelete}
        title="Delete Performer"
        message={`Are you sure you want to delete "${performer.name}"? This cannot be undone.`}
        onConfirm={() => deleteMut.mutate()}
        onCancel={() => setConfirmDelete(false)}
      />
      <DetailMergeDialog
        open={mergeOpen}
        onClose={() => setMergeOpen(false)}
        entityType="performer"
        targetItem={{ id: performer.id, name: performer.name, imagePath: performer.imagePath || entityImages.performerImageUrl(performer.id, performer.updatedAt), subtitle: performer.disambiguation }}
        searchItems={async (term) => {
          const response = await performers.find({ page: 1, perPage: 20, sort: "name", direction: "asc", q: term || undefined });
          return response.items.map((item) => ({
            id: item.id,
            name: item.name,
            imagePath: item.imagePath,
            subtitle: item.disambiguation,
          }));
        }}
        onMerge={(targetId, sourceIds) => performers.merge(targetId, sourceIds)}
        invalidateQueryKeys={[["performer", id], ["performers"]]}
      />

      <PerformerScrapeDialog open={scrapeOpen} onClose={() => setScrapeOpen(false)} performer={performer} />
    </>
  );
}

type PerformerFaceMatch = {
  performerId: number;
  performerName: string;
  coverImageUrl?: string;
  bestDistance: number;
  bestFaceId: number;
  bestFaceLabel?: string;
  matchingFaceIds: number[];
};

function PerformerFaceSimilarityPanel({ performerId, canReadFaces, onNavigate }: { performerId: number; canReadFaces: boolean; onNavigate: (r: any) => void }) {
  const { data: linkedFaces = [], isLoading: linkedFacesLoading } = useQuery({
    queryKey: ["performer", performerId, "linked-faces"],
    queryFn: () => faces.performerFaces(performerId),
    enabled: canReadFaces,
  });
  const similaritySourceFaces = linkedFaces.slice(0, 6);
  const visibleLinkedFaces = linkedFaces.slice(0, 12);
  const similarFaceQueries = useQueries({
    queries: similaritySourceFaces.map((face) => ({
      queryKey: ["performer", performerId, "linked-face", face.id, "similar"],
      queryFn: () => faces.similar(face.id, { k: 12 }),
      enabled: canReadFaces,
    })),
  });

  const similarPerformers = useMemo<PerformerFaceMatch[]>(() => {
    const matches = new Map<number, PerformerFaceMatch & { matchingFaceIdSet: Set<number> }>();

    for (const query of similarFaceQueries) {
      const candidates = query.data?.items ?? [];
      for (const candidate of candidates) {
        if (candidate.performerId == null || candidate.performerId === performerId) {
          continue;
        }

        const existing = matches.get(candidate.performerId);
        if (existing) {
          existing.matchingFaceIdSet.add(candidate.id);
          existing.matchingFaceIds = Array.from(existing.matchingFaceIdSet);
          if (candidate.distance < existing.bestDistance) {
            existing.bestDistance = candidate.distance;
            existing.bestFaceId = candidate.id;
            existing.bestFaceLabel = candidate.label;
            if (candidate.coverImageUrl) {
              existing.coverImageUrl = candidate.coverImageUrl;
            }
          }
          continue;
        }

        matches.set(candidate.performerId, {
          performerId: candidate.performerId,
          performerName: candidate.performerName || `Performer #${candidate.performerId}`,
          coverImageUrl: candidate.coverImageUrl,
          bestDistance: candidate.distance,
          bestFaceId: candidate.id,
          bestFaceLabel: candidate.label,
          matchingFaceIds: [candidate.id],
          matchingFaceIdSet: new Set([candidate.id]),
        });
      }
    }

    return Array.from(matches.values())
      .map(({ matchingFaceIdSet: _matchingFaceIdSet, ...candidate }) => candidate)
      .sort((left, right) => left.bestDistance - right.bestDistance || right.matchingFaceIds.length - left.matchingFaceIds.length)
      .slice(0, 8);
  }, [performerId, similarFaceQueries]);

  if (!canReadFaces) {
    return null;
  }

  const similarFacesLoading = similarFaceQueries.some((query) => query.isLoading);

  return (
    <div className="mt-6 rounded-xl border border-border bg-card p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-foreground">Similar Performers by Face</h2>
          <p className="mt-1 text-sm text-secondary">Visual matches derived from linked face embeddings.</p>
        </div>
        <div className="rounded-full border border-border bg-surface px-3 py-1 text-xs text-muted">
          {linkedFaces.length} linked face{linkedFaces.length === 1 ? "" : "s"}
        </div>
      </div>

      {linkedFacesLoading ? (
        <p className="mt-4 text-sm text-secondary">Loading linked faces...</p>
      ) : linkedFaces.length === 0 ? (
        <div className="mt-4 rounded-xl border border-dashed border-border px-4 py-6 text-sm text-secondary">
          This performer does not have any primary face clusters linked yet.
        </div>
      ) : (
        <>
          <div className="mt-4 flex flex-wrap gap-2">
            {visibleLinkedFaces.map((face) => (
              <button
                key={face.id}
                type="button"
                onClick={() => onNavigate({ page: "face", id: face.id })}
                className="rounded-full border border-border bg-surface px-3 py-1.5 text-xs text-foreground transition-colors hover:border-accent"
              >
                {face.label?.trim() || `Face #${face.id}`}
              </button>
            ))}
            {linkedFaces.length > visibleLinkedFaces.length ? (
              <span className="rounded-full border border-border bg-surface px-3 py-1.5 text-xs text-muted">
                +{linkedFaces.length - visibleLinkedFaces.length} more
              </span>
            ) : null}
          </div>

          {similarFacesLoading ? (
            <p className="mt-4 text-sm text-secondary">Finding visually similar performers...</p>
          ) : similarPerformers.length === 0 ? (
            <div className="mt-4 rounded-xl border border-dashed border-border px-4 py-6 text-sm text-secondary">
              No similar performers were found for the linked faces yet.
            </div>
          ) : (
            <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
              {similarPerformers.map((match) => (
                <SimilarPerformerFaceCard key={match.performerId} match={match} onNavigate={onNavigate} />
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}

function SimilarPerformerFaceCard({ match, onNavigate }: { match: PerformerFaceMatch; onNavigate: (r: any) => void }) {
  return (
    <article className="overflow-hidden rounded-2xl border border-border bg-surface/60">
      <button
        type="button"
        onClick={() => onNavigate({ page: "performer", id: match.performerId })}
        className="flex w-full items-center gap-3 p-4 text-left"
      >
        <div className="h-16 w-16 overflow-hidden rounded-xl bg-surface/90">
          {match.coverImageUrl ? (
            <img src={match.coverImageUrl} alt={match.performerName} className="h-full w-full object-cover" loading="lazy" />
          ) : (
            <div className="flex h-full w-full items-center justify-center text-muted">
              <UserRound className="h-6 w-6" />
            </div>
          )}
        </div>
        <div className="min-w-0 flex-1">
          <div className="truncate text-sm font-semibold text-foreground">{match.performerName}</div>
          <div className="mt-1 text-xs text-secondary">Best face distance {match.bestDistance.toFixed(3)}</div>
          <div className="mt-1 text-xs text-muted">
            Matched via {match.matchingFaceIds.length} face{match.matchingFaceIds.length === 1 ? "" : "s"}
          </div>
        </div>
      </button>
      <div className="border-t border-border px-4 py-3 text-xs text-secondary">
        <button
          type="button"
          onClick={() => onNavigate({ page: "face", id: match.bestFaceId })}
          className="text-accent hover:underline"
        >
          Open best face match{match.bestFaceLabel ? `: ${match.bestFaceLabel}` : ""}
        </button>
      </div>
    </article>
  );
}

function PerformerMetadataServerPanel({ performer, metadataServers, onNavigate }: { performer: PerformerModel; metadataServers: MetadataServer[]; onNavigate: (r: any) => void }) {
  const queryClient = useQueryClient();
  const [term, setTerm] = useState(performer.name);
  const [selectedEndpoint, setSelectedEndpoint] = useState("");
  const [expanded, setExpanded] = useState(false);

  useEffect(() => {
    setTerm(performer.name);
  }, [performer.id, performer.name]);

  useEffect(() => {
    if (selectedEndpoint && !metadataServers.some((box) => box.endpoint === selectedEndpoint)) {
      setSelectedEndpoint("");
    }
  }, [selectedEndpoint, metadataServers]);

  const searchMutation = useMutation({
    mutationFn: (variables: { term?: string; endpoint?: string }) => performers.searchMetadataServer(performer.id, variables.term, variables.endpoint),
  });

  const importMutation = useMutation({
    mutationFn: (match: MetadataServerPerformerMatch) =>
      performers.importFromMetadataServer(performer.id, { endpoint: match.endpoint, performerId: match.id }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["performer", performer.id] });
      queryClient.invalidateQueries({ queryKey: ["performers"] });
    },
  });

  const draftMutation = useMutation({
    mutationFn: (endpoint: string) => performers.submitMetadataServerDraft(performer.id, endpoint),
  });

  const draftEndpoint = selectedEndpoint || metadataServers[0]?.endpoint;
  const linkedNames = new Map(metadataServers.map((box) => [box.endpoint, box.name || box.endpoint]));

  return (
    <div className="mt-6 rounded-xl border border-border bg-card p-4">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center justify-between text-left"
      >
        <div className="flex items-center gap-3">
          <h2 className="text-base font-semibold text-foreground">MetadataServer</h2>
          {performer.remoteIds.length > 0 && (
            <div className="flex flex-wrap gap-2">
              {performer.remoteIds.map((remoteId) => (
                <span key={`${remoteId.endpoint}:${remoteId.remoteId}`} className="inline-flex items-center gap-1 rounded-full border border-border px-3 py-1 text-xs text-secondary">
                  <Link2 className="h-3.5 w-3.5 text-accent" />
                  {linkedNames.get(remoteId.endpoint) ?? remoteId.endpoint}
                </span>
              ))}
            </div>
          )}
        </div>
        <ChevronDown className={`h-4 w-4 text-muted transition-transform ${expanded ? "rotate-180" : ""}`} />
      </button>

      {expanded && (
        <div className="mt-4">
          {metadataServers.length === 0 ? (
            <div className="rounded-xl border border-dashed border-border p-4 text-sm text-secondary">
              No MetadataServer endpoints are configured yet. Use Settings and open Metadata Providers to add one.
              <button
                onClick={() => onNavigate({ page: "settings" })}
                className="ml-2 text-accent hover:text-accent-hover"
              >
                Open settings
              </button>
            </div>
          ) : (
            <>
              <div className="grid gap-3 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_auto]">
                <label className="block text-sm">
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Search term</span>
                  <input
                    value={term}
                    onChange={(event) => setTerm(event.target.value)}
                    placeholder={performer.name}
                    className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                  />
                </label>
                <label className="block text-sm">
                  <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Endpoint</span>
                  <select
                    value={selectedEndpoint}
                    onChange={(event) => setSelectedEndpoint(event.target.value)}
                    className="w-full rounded-xl border border-border bg-surface px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
                  >
                    <option value="">All configured endpoints</option>
                    {metadataServers.map((box) => (
                      <option key={box.endpoint} value={box.endpoint}>
                        {box.name || box.endpoint}
                      </option>
                    ))}
                  </select>
                </label>
                <div className="flex flex-wrap items-end gap-2">
                  <button
                    onClick={() => searchMutation.mutate({ term: term.trim() || undefined, endpoint: selectedEndpoint || undefined })}
                    disabled={searchMutation.isPending}
                    className="inline-flex items-center gap-2 rounded-xl border border-border px-4 py-2 text-sm text-foreground hover:border-accent hover:text-accent disabled:opacity-60"
                  >
                    {searchMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
                    Search MetadataServer
                  </button>
                  <button
                    onClick={() => draftEndpoint && draftMutation.mutate(draftEndpoint)}
                    disabled={!draftEndpoint || draftMutation.isPending}
                    className="inline-flex items-center gap-2 rounded-xl border border-border px-4 py-2 text-sm text-foreground hover:border-accent hover:text-accent disabled:opacity-60"
                  >
                    {draftMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <CloudDownload className="h-4 w-4" />}
                    Submit Draft
                  </button>
                </div>
              </div>

              {searchMutation.error && (
                <p className="mt-3 text-sm text-red-300">{searchMutation.error.message}</p>
              )}

              {draftMutation.error && (
                <p className="mt-3 text-sm text-red-300">{draftMutation.error.message}</p>
              )}

              {draftMutation.isSuccess && (
                <p className="mt-3 text-sm text-emerald-300">
                  Performer draft submitted to MetadataServer{draftMutation.data.draftId ? ` (${draftMutation.data.draftId})` : ""}.
                </p>
              )}

              {importMutation.isSuccess && (
                <p className="mt-3 text-sm text-emerald-300">Performer metadata imported from MetadataServer.</p>
              )}

              {searchMutation.data && (
                <div className="mt-4 space-y-3">
                  {searchMutation.data.length === 0 ? (
                    <div className="rounded-xl border border-dashed border-border p-4 text-sm text-secondary">
                      No MetadataServer performer matches were found.
                    </div>
                  ) : (
                    searchMutation.data.map((match) => (
                      <button
                        key={`${match.endpoint}:${match.id}`}
                        onClick={() => importMutation.mutate(match)}
                        disabled={importMutation.isPending}
                        className="flex w-full flex-col gap-4 rounded-xl border border-border bg-surface p-4 text-left transition-colors hover:border-accent/60 disabled:opacity-60 md:flex-row"
                      >
                        <div className="h-28 w-20 flex-shrink-0 overflow-hidden rounded-lg border border-border bg-black/20">
                          {match.imageUrl ? (
                            <img src={match.imageUrl} alt={match.name} className="h-full w-full object-cover" />
                          ) : (
                            <div className="flex h-full w-full items-center justify-center bg-gradient-to-b from-card to-surface">
                              <UserRound className="h-10 w-10 text-muted/50" />
                            </div>
                          )}
                        </div>

                        <div className="min-w-0 flex-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="text-base font-semibold text-foreground">{match.name}</span>
                            <span className="rounded-full border border-border px-2 py-0.5 text-xs text-secondary">
                              {match.serverName}
                            </span>
                            {match.deleted && <span className="rounded-full bg-red-500/15 px-2 py-0.5 text-xs text-red-300">Deleted</span>}
                          </div>
                          {match.disambiguation && <p className="mt-1 text-sm text-secondary">{match.disambiguation}</p>}
                          <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted">
                            {match.gender && <span>{match.gender}</span>}
                            {match.birthDate && <span>Born {match.birthDate}</span>}
                            {match.country && <span>{match.country}</span>}
                            <span>ID {match.id}</span>
                          </div>
                          {match.aliases.length > 0 && <p className="mt-2 text-xs text-secondary">Aliases: {match.aliases.join(", ")}</p>}
                          {match.urls.length > 0 && <p className="mt-1 truncate text-xs text-muted">{match.urls[0]}</p>}
                        </div>

                        <div className="flex items-end">
                          <span className="inline-flex items-center gap-2 rounded-lg bg-accent px-3 py-2 text-sm font-medium text-white">
                            {importMutation.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <CloudDownload className="h-4 w-4" />}
                            Import
                          </span>
                        </div>
                      </button>
                    ))
                  )}
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  );
}

function InfoItem({ icon, label, value }: { icon?: React.ReactNode; label: string; value: string }) {
  return (
    <div className="flex items-center gap-2 text-sm">
      {icon && <span className="text-muted">{icon}</span>}
      <div>
        <div className="text-xs text-muted">{label}</div>
        <div className="text-foreground">{value}</div>
      </div>
    </div>
  );
}

function PerformerScenesPanel({ performerId, filter, setFilter, onNavigate }: {
  performerId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const { data, isLoading } = useQuery({
    queryKey: ["performer-scenes", performerId, filter],
    queryFn: () => scenes.find(filter, { performerIds: String(performerId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<Film className="h-10 w-10" />} message="Loading scenes..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<Film className="h-12 w-12" />} message="No scenes found for this performer" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={SCENE_SORT_OPTIONS} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="scenes" selectedIds={selectedIds} onDone={selectNone} sceneItems={data.items} onNavigate={onNavigate} />} />
      <EntityCardGrid minCardWidth={`${220 + zoomLevel * 50}px`} gapClassName="gap-4">
        {data.items.map((scene) => (
          <SceneCard key={scene.id} scene={scene} onClick={() => selecting ? toggle(scene.id) : onNavigate({ page: "scene", id: scene.id })} onNavigate={onNavigate} onQuickView={() => setQuickViewId(scene.id)} selected={selectedIds.has(scene.id)} onSelect={() => toggle(scene.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
      {quickViewId !== null && (
        <QuickViewDialog type="scene" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      )}
    </>
  );
}

function PerformerGalleriesPanel({ performerId, filter, setFilter, onNavigate }: {
  performerId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const { data, isLoading } = useQuery({
    queryKey: ["performer-galleries", performerId, filter],
    queryFn: () => galleries.find(filter, { performerIds: String(performerId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<FolderOpen className="h-10 w-10" />} message="Loading galleries..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<FolderOpen className="h-12 w-12" />} message="No galleries found for this performer" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={GALLERY_SORT} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="galleries" selectedIds={selectedIds} onDone={selectNone} downloadItems={data.items} />} />
      <EntityCardGrid minCardWidth={`${220 + zoomLevel * 50}px`} gapClassName="gap-4">
        {data.items.map((gallery) => (
          <GalleryTile key={gallery.id} gallery={gallery} onClick={() => selecting ? toggle(gallery.id) : onNavigate({ page: "gallery", id: gallery.id })} selected={selectedIds.has(gallery.id)} onSelect={() => toggle(gallery.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
    </>
  );
}

function PerformerImagesPanel({ performerId, filter, setFilter, onNavigate }: {
  performerId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  const [zoomLevel, setZoomLevel] = useState(0);
  const [quickViewId, setQuickViewId] = useState<number | null>(null);
  const { data, isLoading } = useQuery({
    queryKey: ["performer-images", performerId, filter],
    queryFn: () => images.find(filter, { performerIds: String(performerId) }),
  });
  const { selectedIds, toggle, selectAll, selectNone } = useMultiSelect(data?.items ?? []);
  const selecting = selectedIds.size > 0;

  if (isLoading) return <LoadingPanel icon={<ImageIcon className="h-10 w-10" />} message="Loading images..." />;
  if (!data || data.items.length === 0) return <EmptyPanel icon={<ImageIcon className="h-12 w-12" />} message="No images found for this performer" />;

  return (
    <>
      <DetailListToolbar filter={filter} onFilterChange={setFilter} totalCount={data.totalCount} sortOptions={IMAGE_SORT} zoomLevel={zoomLevel} onZoomChange={setZoomLevel} showSearch selectedCount={selectedIds.size} onSelectAll={selectAll} onSelectNone={selectNone} selectionActions={<BulkSelectionActions entityType="images" selectedIds={selectedIds} onDone={selectNone} downloadItems={data.items} />} />
      <EntityCardGrid minCardWidth={`${160 + zoomLevel * 50}px`}>
        {data.items.map((image) => (
          <ImageTile key={image.id} image={image} onClick={() => selecting ? toggle(image.id) : onNavigate({ page: "image", id: image.id })} onNavigate={onNavigate} onQuickView={() => setQuickViewId(image.id)} selected={selectedIds.has(image.id)} onSelect={() => toggle(image.id)} selecting={selecting} />
        ))}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={data.totalCount} />
      {quickViewId !== null && (
        <QuickViewDialog type="image" id={quickViewId} onClose={() => setQuickViewId(null)} onNavigate={onNavigate} />
      )}
    </>
  );
}

function PerformerGroupsPanel({ performerId, filter, setFilter, onNavigate }: {
  performerId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  // Fetch all scenes for this performer and extract unique groups
  const { data: scenesData, isLoading } = useQuery({
    queryKey: ["performer-scenes-for-groups", performerId],
    queryFn: () => scenes.find({ page: 1, perPage: 200, direction: "desc" }, { performerIds: String(performerId) }),
  });

  const uniqueGroups = useMemo(() => {
    if (!scenesData) return [];
    const seen = new Map<number, { id: number; name: string; sceneCount: number }>();
    for (const scene of scenesData.items) {
      for (const g of scene.groups ?? []) {
        const existing = seen.get(g.id);
        if (existing) {
          existing.sceneCount++;
        } else {
          seen.set(g.id, { id: g.id, name: g.name, sceneCount: 1 });
        }
      }
    }
    return [...seen.values()].sort((a, b) => b.sceneCount - a.sceneCount);
  }, [scenesData]);

  const page = filter.page ?? 1;
  const perPage = filter.perPage ?? 18;
  const paginated = uniqueGroups.slice((page - 1) * perPage, page * perPage);

  if (isLoading) return <LoadingPanel icon={<Layers className="h-10 w-10" />} message="Loading groups..." />;
  if (uniqueGroups.length === 0) return <EmptyPanel icon={<Layers className="h-12 w-12" />} message="No groups for this performer" />;

  return (
    <>
      <EntityCardGrid minCardWidth="200px" gapClassName="gap-4">
        {paginated.map((group) => {
          const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "group", id: group.id }, () => onNavigate({ page: "group", id: group.id }));

          return (
            <a key={group.id} {...linkProps} className="group overflow-hidden rounded-xl border border-border bg-card text-left transition-colors hover:border-accent/60">
              <div className="flex aspect-video items-center justify-center bg-gradient-to-br from-surface to-card">
                <Layers className="h-10 w-10 text-muted" />
              </div>
              <div className="p-3">
                <p className="truncate text-sm font-medium text-foreground group-hover:text-accent">{group.name}</p>
                <p className="mt-1 text-xs text-secondary">{group.sceneCount} scene{group.sceneCount !== 1 ? "s" : ""}</p>
              </div>
            </a>
          );
        })}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={uniqueGroups.length} />
    </>
  );
}

function PerformerAppearsWithPanel({ performerId, filter, setFilter, onNavigate }: {
  performerId: number;
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  onNavigate: (r: any) => void;
}) {
  // Find all scenes this performer is in, collect co-performers
  const { data: scenesData, isLoading } = useQuery({
    queryKey: ["performer-scenes-for-costars", performerId],
    queryFn: () => scenes.find({ page: 1, perPage: 200, direction: "desc" }, { performerIds: String(performerId) }),
  });

  const coStars = useMemo(() => {
    if (!scenesData) return [];
    const counts = new Map<number, { performer: { id: number; name: string; imagePath?: string }; count: number }>();
    for (const scene of scenesData.items) {
      for (const p of scene.performers ?? []) {
        if (p.id === performerId) continue;
        const existing = counts.get(p.id);
        if (existing) {
          existing.count++;
        } else {
          counts.set(p.id, { performer: { id: p.id, name: p.name, imagePath: p.imagePath }, count: 1 });
        }
      }
    }
    return [...counts.values()].sort((a, b) => b.count - a.count);
  }, [scenesData, performerId]);

  // Paginate client-side
  const page = filter.page ?? 1;
  const perPage = filter.perPage ?? 18;
  const paginated = coStars.slice((page - 1) * perPage, page * perPage);

  if (isLoading) return <LoadingPanel icon={<Users className="h-10 w-10" />} message="Loading co-stars..." />;
  if (coStars.length === 0) return <EmptyPanel icon={<Users className="h-12 w-12" />} message="No co-stars found" />;

  return (
    <>
      <EntityCardGrid minCardWidth="180px" gapClassName="gap-4">
        {paginated.map(({ performer: p, count }) => {
          const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "performer", id: p.id }, () => onNavigate({ page: "performer", id: p.id }));

          return (
            <a key={p.id} {...linkProps} className="group overflow-hidden rounded-xl border border-border bg-card text-left transition-colors hover:border-accent/60">
              <div className="aspect-[2/3] bg-gradient-to-b from-card to-surface overflow-hidden">
                <img
                  src={p.imagePath || entityImages.performerImageUrl(p.id)}
                  alt={p.name}
                  className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-105"
                  loading="lazy"
                  onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }}
                />
              </div>
              <div className="p-3">
                <p className="truncate text-sm font-medium text-foreground group-hover:text-accent">{p.name}</p>
                <p className="mt-1 text-xs text-secondary">{count} scene{count !== 1 ? "s" : ""} together</p>
              </div>
            </a>
          );
        })}
      </EntityCardGrid>
      <Pager filter={filter} setFilter={setFilter} totalCount={coStars.length} />
    </>
  );
}

function Pager({ filter, setFilter, totalCount }: {
  filter: FindFilter;
  setFilter: (filter: FindFilter) => void;
  totalCount: number;
}) {
  const perPage = filter.perPage ?? 1;
  const page = filter.page ?? 1;
  const totalPages = Math.max(1, Math.ceil(totalCount / perPage));

  if (totalPages <= 1) return null;

  return (
    <div className="mx-auto max-w-7xl mt-6 flex items-center justify-center gap-4">
      <button
        disabled={page <= 1}
        onClick={() => setFilter({ ...filter, page: page - 1 })}
        className="rounded border border-border bg-card px-4 py-2 text-sm text-secondary hover:bg-card-hover disabled:cursor-not-allowed disabled:opacity-50"
      >
        Previous
      </button>
      <span className="text-sm text-secondary">Page {page} of {totalPages}</span>
      <button
        disabled={page >= totalPages}
        onClick={() => setFilter({ ...filter, page: page + 1 })}
        className="rounded border border-border bg-card px-4 py-2 text-sm text-secondary hover:bg-card-hover disabled:cursor-not-allowed disabled:opacity-50"
      >
        Next
      </button>
    </div>
  );
}

function LoadingPanel({ icon, message }: { icon: React.ReactNode; message: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-12 text-muted">
      <div className="mb-3 animate-pulse">{icon}</div>
      <p>{message}</p>
    </div>
  );
}

function EmptyPanel({ icon, message }: { icon: React.ReactNode; message: string }) {
  return (
    <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-card/40 py-12 text-muted">
      <div className="mb-3 opacity-60">{icon}</div>
      <p>{message}</p>
    </div>
  );
}
