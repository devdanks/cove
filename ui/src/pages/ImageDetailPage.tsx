import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { faces, images, playback, fileOps } from "../api/client";
import { formatDate, TagBadge, CustomFieldsDisplay } from "../components/shared";
import { Check, Download, Eye, FolderOpen, FolderPlus, ImageOff, Link as LinkIcon, Maximize, MoreVertical, Pencil, ThumbsUp, Trash2, UserRound, X } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState, lazy, Suspense } from "react";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { ExtensionSlot } from "../router/RouteRegistry";
import { AspectRatingsPanel } from "../components/AspectRatingsPanel";
import { InteractiveRating } from "../components/Rating";
import { createRouteLinkProps } from "../components/cardNavigation";
import { ExtensionEntityActions } from "../components/ExtensionEntityActions";
import { MediaDetailLayout } from "../components/MediaDetailLayout/MediaDetailLayout";
import { getImageDisplayTitle } from "../utils/imageDisplay";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import type { FaceHostFace } from "../api/types";
import { createPlaybackSessionId, trackInteraction } from "../utils/interactionTracking";
import { ImageVisualSimilarityPanel } from "../components/VisualSimilarityPanel";
import { BookmarkButton } from "../components/BookmarkButton";
import { AddToGroupDialog } from "../components/AddToGroupDialog";

const ImageEditModal = lazy(() => import("./ImageEditModal").then((module) => ({ default: module.ImageEditModal })));
const ImageDownloadDialog = lazy(() => import("../components/ImageDownloadDialog").then((module) => ({ default: module.ImageDownloadDialog })));

interface Props {
  id: number;
  onNavigate: (r: any) => void;
}

type ImageTab = "details" | "file-info" | "similar" | "detections" | "related";

export function ImageDetailPage({ id, onNavigate }: Props) {
  const { data: image, isLoading } = useQuery({
    queryKey: ["image", id],
    queryFn: () => images.get(id),
  });
  const { hasPermission, user } = useAuth();
  const [editing, setEditing] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [lightboxOpen, setLightboxOpen] = useState(false);
  const [imageLoadFailed, setImageLoadFailed] = useState(false);
  const [showDownloadDialog, setShowDownloadDialog] = useState(false);
  const [showAddToGroup, setShowAddToGroup] = useState(false);
  const [showOpsMenu, setShowOpsMenu] = useState(false);
  const [activeTab, setActiveTab] = useState<ImageTab>("details");
  const queryClient = useQueryClient();
  const opsMenuRef = useRef<HTMLDivElement>(null);
  const { backLabel, goBack } = useBackNavigation({ page: "images" }, onNavigate);
  const canWriteImage = canWriteEntity("image", hasPermission);
  const canWriteGroups = canWriteEntity("group", hasPermission);
  const canDeleteImage = canDeleteEntity("image", hasPermission);
  const canDownloadImage = hasPermission("jobs.run") && canWriteImage;
  const canEngageImage = canReadEntity("image", hasPermission) && (user?.kind === "user" || user?.kind === "system");
  const canReadFaces = canReadEntity("face", hasPermission);
  const canReadFiles = hasPermission("files.read");
  const canReadStudios = canReadEntity("studio", hasPermission);
  const canReadPerformers = canReadEntity("performer", hasPermission);
  const canReadTags = canReadEntity("tag", hasPermission);
  const trackingEnabled = user?.uiPreferences?.tracking?.enabled ?? true;
  const trackImageActivity = canEngageImage && trackingEnabled;
  const {
    engagement: imageEngagement,
    rating: imageRating,
    setRating: setImageRating,
  } = useEntityEngagement("image", id, {
    enabled: !!image && canEngageImage,
    fallbackRating: undefined,
  });
  const { data: imageFaces = [] } = useQuery({
    queryKey: ["image", id, "faces"],
    queryFn: () => faces.imageFaces(id),
    enabled: canReadFaces,
  });  const deleteMut = useMutation({
    mutationFn: () => images.delete(id),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["images"] }); goBack(); },
  });
  const updateMut = useMutation({
    mutationFn: (data: { organized?: boolean }) => images.update(id, data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["image", id] }),
  });
  const incrementLikeMut = useMutation({
    mutationFn: () => images.incrementLike(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["image", id] });
      queryClient.invalidateQueries({ queryKey: ["engagement", "image", id] });
    },
  });
  const revealFileMutation = useMutation({ mutationFn: (fileId: number) => fileOps.reveal(fileId) });
  const canRevealFiles = typeof window !== "undefined" && ["localhost", "127.0.0.1", "::1"].includes(window.location.hostname);
  const imageLikeCount = imageEngagement?.likeCount ?? 0;
  const imageDerivedLikeCount = imageEngagement?.derivedLikeCount ?? 0;
  const imagePageVisitCount = imageEngagement?.pageVisitCount ?? 0;
  const displayTitle = image ? getImageDisplayTitle(image) : `Image ${id}`;
  const tabs = useMemo(() => {
    const nextTabs = [
      { key: "details", label: "Details" },
      ...(canReadFiles ? [{ key: "file-info", label: "File Info", count: image?.files.length ?? 0 }] : []),
      { key: "similar", label: "Similar" },
      { key: "detections", label: "Faces", count: imageFaces.length },
      {
        key: "related",
        label: "Related",
        count: (image?.performers.length ?? 0) + (image?.tags.length ?? 0) + (image?.studioId ? 1 : 0),
      },
    ];
    return nextTabs;
  }, [canReadFiles, image?.files.length, image?.performers.length, image?.studioId, image?.tags.length, imageFaces.length]);

  useEffect(() => {
    if (!tabs.some((tab) => tab.key === activeTab)) {
      setActiveTab("details");
    }
  }, [activeTab, tabs]);

  useEffect(() => {
    if (image) document.title = `${displayTitle} | Cove`;
    return () => { document.title = "Cove"; };
  }, [displayTitle, image]);

  useEffect(() => {
    if (!image || !trackImageActivity) return;

    const imageId = image.id;
    const startedAt = performance.now();
    const sessionId = createPlaybackSessionId();
    trackInteraction({
      hostType: "image",
      hostId: imageId,
      kind: "pageVisit",
      meta: { source: "imageDetailPage" },
    });
    queryClient.invalidateQueries({ queryKey: ["engagement", "image", imageId] });

    let flushed = false;
    const flushDwell = (state: "ended" | "abandoned") => {
      if (flushed) return;
      flushed = true;
      const durationSec = Math.max(0.001, (performance.now() - startedAt) / 1000);
      void playback.recordIntervals({
        hostType: "image",
        hostId: imageId,
        sessionId,
        mediaDurationSec: durationSec,
        currentPositionSec: durationSec,
        state,
        intervals: [{ startSec: 0, endSec: durationSec }],
      }).catch(() => {});
      queryClient.invalidateQueries({ queryKey: ["engagement", "image", imageId] });
    };
    const handlePageHide = () => flushDwell("abandoned");
    window.addEventListener("pagehide", handlePageHide);
    return () => {
      window.removeEventListener("pagehide", handlePageHide);
      flushDwell("ended");
    };
  }, [image?.id, queryClient, trackImageActivity]);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (opsMenuRef.current && !opsMenuRef.current.contains(event.target as Node)) {
        setShowOpsMenu(false);
      }
    };

    if (showOpsMenu) {
      document.addEventListener("mousedown", handleClickOutside);
    }

    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [showOpsMenu]);

  useEffect(() => {
    setImageLoadFailed(false);
  }, [id]);

  const openLightbox = useCallback(() => {
    if (imageLoadFailed) {
      return;
    }

    if (trackImageActivity && !lightboxOpen) {
      trackInteraction({
        hostType: "image",
        hostId: id,
        kind: "openLightbox",
        meta: { source: "imageDetailPage" },
      });
    }

    setLightboxOpen(true);
  }, [id, imageLoadFailed, lightboxOpen, trackImageActivity]);

  const closeLightbox = useCallback(() => {
    if (trackImageActivity && lightboxOpen) {
      trackInteraction({
        hostType: "image",
        hostId: id,
        kind: "closeLightbox",
        meta: { source: "imageDetailPage" },
      });
    }

    setLightboxOpen(false);
  }, [id, lightboxOpen, trackImageActivity]);
  const imageKeyboardShortcuts = useMemo(() => ([
    {
      key: "e",
      description: "Edit image",
      handler: () => {
        if (canWriteImage) {
          setEditing(true);
        }
      },
    },
    {
      key: "l",
      description: "Add like",
      handler: () => {
        if (canEngageImage) {
          incrementLikeMut.mutate();
        }
      },
    },
    {
      key: "f",
      description: "Toggle fullscreen lightbox",
      handler: () => {
        if (lightboxOpen) {
          closeLightbox();
        } else {
          openLightbox();
        }
      },
    },
    {
      key: "d",
      description: "Open detections tab",
      handler: () => setActiveTab("detections"),
    },
    {
      key: "r",
      description: "Open related tab",
      handler: () => setActiveTab("related"),
    },
    {
      key: "Escape",
      description: "Close lightbox",
      handler: () => closeLightbox(),
    },
  ]), [canEngageImage, canWriteImage, closeLightbox, incrementLikeMut, lightboxOpen, openLightbox]);

  if (isLoading) {
    return (
      <div className="px-1 py-2">
        <DetailSkeleton />
      </div>
    );
  }

  if (!image) return <div className="text-center text-secondary py-16">Image not found</div>;

  const detailsContent = (
    <div className="space-y-5">
      {image.details ? (
        <section>
          <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">Description</h2>
          <p className="mt-2 whitespace-pre-wrap text-sm leading-relaxed text-secondary">{image.details}</p>
        </section>
      ) : null}

      <section>
        <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">Details</h2>
        <dl className="mt-3 grid gap-2 md:grid-cols-2 xl:grid-cols-4">
          <DetailField label="Organized" value={image.organized ? "Yes" : "No"} />
          <DetailField label="Created" value={formatDate(image.createdAt)} />
          <DetailField label="Updated" value={formatDate(image.updatedAt)} />
        </dl>
      </section>

      <AspectRatingsPanel hostType="image" hostId={id} canRate={canEngageImage} />

      {image.urls.length > 0 ? (
        <section>
          <h2 className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-muted">
            <LinkIcon className="h-3.5 w-3.5" /> URLs
          </h2>
          <div className="space-y-1">
            {image.urls.map((url, index) => (
              <a key={index} href={url} target="_blank" rel="noopener noreferrer" className="block truncate text-sm text-accent hover:underline">
                {url}
              </a>
            ))}
          </div>
        </section>
      ) : null}

      <CustomFieldsDisplay customFields={image.customFields} entityType="image" />
      <ExtensionSlot slot="image-detail-sidebar-bottom" context={{ image, onNavigate }} />
    </div>
  );

  const fileInfoContent = canReadFiles ? (
    image.files.length > 0 ? (
      <section>
        <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">File Info</h2>
        <div className="mt-3 space-y-3">
          {image.files.map((file) => (
            <div key={file.id} className="space-y-2 rounded-xl border border-border/60 bg-card/60 p-3">
              {canRevealFiles && file.id ? (
                <div className="flex justify-end">
                  <button
                    type="button"
                    onClick={() => revealFileMutation.mutate(file.id)}
                    className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-secondary hover:border-accent hover:text-foreground"
                  >
                    <FolderOpen className="h-3.5 w-3.5" />
                    Reveal
                  </button>
                </div>
              ) : null}
              <dl className="grid gap-2 md:grid-cols-2">
                <DetailField label="Path" value={<span className="break-all font-mono text-[11px]">{file.path}</span>} />
                <DetailField label="Dimensions" value={`${file.width} x ${file.height}`} />
                <DetailField label="Format" value={file.format} />
                <DetailField label="Size" value={`${(file.size / 1024 / 1024).toFixed(2)} MB`} />
              </dl>
            </div>
          ))}
        </div>
      </section>
    ) : (
      <EmptyPanel icon={<ImageOff className="h-10 w-10" />} message="No image file metadata is available yet." />
    )
  ) : (
    <EmptyPanel icon={<ImageOff className="h-10 w-10" />} message="File metadata is unavailable with your current permissions." />
  );

  const detectionsContent = (
    <section>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">Faces</h2>
          <p className="mt-1 text-sm text-secondary">Face clusters attached to this image.</p>
        </div>
        <div className="text-xs text-muted">{imageFaces.length} face{imageFaces.length === 1 ? "" : "s"}</div>
      </div>

      {canReadFaces ? (
        imageFaces.length > 0 ? (
          <div className="mt-4 grid gap-2 sm:grid-cols-2 xl:grid-cols-3">
            {imageFaces.map((face) => {
              const title = face.performerName?.trim() || face.label?.trim() || `Face #${face.id}`;
              const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "face", id: face.id }, () => onNavigate({ page: "face", id: face.id }));

              return (
                <a
                  key={face.id}
                  {...linkProps}
                  className="flex items-center gap-2 rounded-lg border border-border bg-surface/35 px-2 py-2 transition-colors hover:border-accent"
                >
                  <div className="flex h-8 w-8 shrink-0 items-center justify-center overflow-hidden rounded-md bg-surface text-[10px] text-muted">
                    {face.coverImageUrl ? (
                      <img src={face.coverImageUrl} alt={title} className="h-full w-full object-cover" loading="lazy" />
                    ) : (
                      title.slice(0, 2).toUpperCase()
                    )}
                  </div>
                  <div className="min-w-0">
                    <div className="truncate text-sm text-foreground">{title}</div>
                    <div className="text-[11px] text-secondary">{formatImageFaceSummary(face)}</div>
                  </div>
                </a>
              );
            })}
          </div>
        ) : (
          <EmptyPanel icon={<Maximize className="h-10 w-10" />} message="No face detections are attached to this image yet." />
        )
      ) : (
        <EmptyPanel icon={<Maximize className="h-10 w-10" />} message="Face detections are unavailable with your current permissions." />
      )}
    </section>
  );

  const relatedContent = (
    <div className="space-y-5">
      {(canReadStudios && image.studioName && image.studioId) || (canReadPerformers && image.performers.length > 0) ? (
        <section>
          <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">People and Studio</h2>
          {canReadStudios && image.studioName && image.studioId ? (
            <div className="mt-3">
              <button onClick={() => onNavigate({ page: "studio", id: image.studioId })} className="text-sm text-accent hover:underline">
                {image.studioName}
              </button>
            </div>
          ) : null}
          {canReadPerformers && image.performers.length > 0 ? (
            <div className="mt-3 flex flex-wrap gap-2">
              {image.performers.map((performer) => {
                const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "performer", id: performer.id }, () => onNavigate({ page: "performer", id: performer.id }));

                return (
                  <a
                    key={performer.id}
                    {...linkProps}
                    className="flex items-center gap-2 rounded-lg border border-border bg-surface/40 px-3 py-2 transition-colors hover:border-accent"
                  >
                    <div className="flex h-7 w-7 items-center justify-center overflow-hidden rounded-full bg-surface text-xs text-muted">
                      {performer.imagePath ? <img src={performer.imagePath} alt="" className="h-full w-full object-cover" /> : performer.name[0]}
                    </div>
                    <span className="text-sm text-foreground">{performer.name}</span>
                  </a>
                );
              })}
            </div>
          ) : null}
        </section>
      ) : null}

      {canReadTags && image.tags.length > 0 ? (
        <section>
          <h2 className="text-xs font-semibold uppercase tracking-wide text-muted">Tags</h2>
          <div className="mt-3 flex flex-wrap gap-1.5">
            {image.tags.map((tag) => (
              <TagBadge key={tag.id} name={tag.name} tag={tag} provenance={tag.provenance} onClick={() => onNavigate({ page: "tag", id: tag.id })} />
            ))}
          </div>
        </section>
      ) : null}

      {(!canReadPerformers || image.performers.length === 0) && (!canReadTags || image.tags.length === 0) && !(canReadStudios && image.studioName && image.studioId) ? (
        <EmptyPanel icon={<UserRound className="h-10 w-10" />} message="No related performers, studio, or tags are linked to this image yet." />
      ) : null}
    </div>
  );

  const activeContent = activeTab === "file-info"
    ? fileInfoContent
    : activeTab === "similar"
      ? <ImageVisualSimilarityPanel imageId={image.id} onNavigate={onNavigate} />
    : activeTab === "detections"
      ? detectionsContent
      : activeTab === "related"
        ? relatedContent
        : detailsContent;

  return (
    <>
      <Suspense fallback={null}>
        {editing ? <ImageEditModal image={image} open={editing} onClose={() => setEditing(false)} /> : null}
        {showDownloadDialog ? (
          <ImageDownloadDialog
            open={showDownloadDialog}
            image={image}
            onClose={() => setShowDownloadDialog(false)}
            onNavigate={onNavigate}
          />
        ) : null}
      </Suspense>
      <AddToGroupDialog
        open={showAddToGroup}
        onClose={() => setShowAddToGroup(false)}
        items={[{ key: `image-${image.id}`, kind: "image", hostType: "image", hostId: image.id, title: displayTitle }]}
      />
      <ConfirmDialog open={confirmDelete} title="Delete Image" message={`Delete "${displayTitle}"? This cannot be undone.`} onConfirm={() => deleteMut.mutate()} onCancel={() => setConfirmDelete(false)} />

      {/* Lightbox overlay */}
      {lightboxOpen && (
        <div className="fixed inset-0 z-50 bg-black flex items-center justify-center" onClick={closeLightbox} onKeyDown={(e) => { if (e.key === "Escape") closeLightbox(); }} tabIndex={0} ref={(el) => el?.focus()}>
          <img
            src={images.imageUrl(id)}
            alt={image.title || "Image"}
            className="w-[95vw] h-[95vh] object-contain"
          />
          <button
            onClick={(e) => { e.stopPropagation(); closeLightbox(); }}
            className="absolute top-4 right-4 p-2 bg-black/60 text-white rounded hover:bg-black/80"
            title="Close (Esc)"
          >
            <X className="w-6 h-6" />
          </button>
        </div>
      )}
      <MediaDetailLayout
        title={displayTitle}
        subtitle={
          <div className="flex flex-wrap items-center gap-3 text-sm text-secondary">
            {image.date ? <span>{formatDate(image.date)}</span> : null}
            {image.studioName && image.studioId ? (
              canReadStudios ? (
                <button onClick={() => onNavigate({ page: "studio", id: image.studioId })} className="text-accent hover:underline">
                  {image.studioName}
                </button>
              ) : (
                <span>{image.studioName}</span>
              )
            ) : null}
            {image.photographer ? <span>Photo: {image.photographer}</span> : null}
          </div>
        }
        backLabel={backLabel}
        onGoBack={goBack}
        media={
          <div className="relative flex h-full min-h-[40vh] flex-1 items-center justify-center bg-black/90 group">
            {imageLoadFailed ? (
              <div className="flex w-full flex-col items-center justify-center gap-3 px-6 text-center text-secondary">
                <ImageOff className="h-10 w-10 text-muted" />
                <div>
                  <div className="text-sm font-medium text-foreground">Image file unavailable</div>
                  {image.files[0]?.path ? <div className="mt-2 max-w-xl break-all text-xs text-muted">{image.files[0].path}</div> : null}
                </div>
              </div>
            ) : null}
            <img
              src={images.imageUrl(id)}
              alt={displayTitle}
              className={["h-full max-h-full w-full select-none object-contain", imageLoadFailed ? "hidden" : "cursor-zoom-in"].join(" ")}
              onError={() => setImageLoadFailed(true)}
              onLoad={(event) => setImageLoadFailed(event.currentTarget.naturalWidth === 0)}
              onClick={openLightbox}
            />
            {!imageLoadFailed ? (
              <button
                type="button"
                onClick={(event) => { event.stopPropagation(); openLightbox(); }}
                className="absolute top-3 right-3 rounded bg-black/60 p-2 text-white opacity-0 transition-opacity group-hover:opacity-100 hover:bg-black/80"
                title="View fullscreen (F)"
              >
                <Maximize className="h-5 w-5" />
              </button>
            ) : null}
          </div>
        }
        mediaAspectRatio="auto"
        tabs={tabs}
        activeTab={activeTab}
        onTabChange={(key) => setActiveTab(key as ImageTab)}
        engagement={{
          primaryContent: (
            <div className="flex flex-wrap items-center gap-3">
              <InteractiveRating value={imageRating} onChange={(value) => setImageRating(value)} readOnly={!canEngageImage} />
            </div>
          ),
          additionalMetrics: [
            {
              label: "Likes",
              value: imageLikeCount,
              icon: <ThumbsUp className={["h-4 w-4", imageLikeCount > 0 ? "fill-accent text-accent" : ""].join(" ")} />,
              title: "Add like",
              onClick: canEngageImage ? () => incrementLikeMut.mutate() : undefined,
              active: imageLikeCount > 0,
            },
            {
              label: "Derived Likes",
              value: imageDerivedLikeCount,
              icon: <ThumbsUp className={["h-4 w-4", imageDerivedLikeCount > 0 ? "text-accent" : ""].join(" ")} />,
              title: "Derived likes",
              active: imageDerivedLikeCount > 0,
            },
            {
              label: "Page Visits",
              value: imagePageVisitCount,
              icon: <Eye className="h-4 w-4" />,
              title: "Page visits",
            },
          ],
        }}
        actions={
          <>
            <ExtensionSlot slot="image-detail-actions" context={{ image, onNavigate }} />
            <BookmarkButton hostType="image" hostId={image.id} compact />
            {canWriteGroups ? (
              <button
                type="button"
                onClick={() => setShowAddToGroup(true)}
                className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
                title="Add to group"
              >
                <FolderPlus className="h-4 w-4" />
              </button>
            ) : null}
            {canWriteImage ? (
              <button
                type="button"
                onClick={() => updateMut.mutate({ organized: !image.organized })}
                className={`inline-flex items-center justify-center rounded p-1 transition ${image.organized ? "bg-green-600 text-white" : "text-secondary hover:bg-card hover:text-foreground"}`}
                title={image.organized ? "Organized" : "Mark organized"}
              >
                <Check className="h-4 w-4" />
              </button>
            ) : null}
            {canWriteImage ? (
              <button
                type="button"
                onClick={() => setEditing(true)}
                className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
                title="Edit"
              >
                <Pencil className="h-4 w-4" />
              </button>
            ) : null}
            {canDeleteImage ? (
              <button
                type="button"
                onClick={() => setConfirmDelete(true)}
                className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-red-300"
                title="Delete"
              >
                <Trash2 className="h-4 w-4" />
              </button>
            ) : null}
            <div className="relative" ref={opsMenuRef}>
              <button
                type="button"
                onClick={() => setShowOpsMenu((current) => !current)}
                className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
                title="More actions"
              >
                <MoreVertical className="h-4 w-4" />
              </button>
              {showOpsMenu ? (
                <div className="absolute right-0 top-full z-50 mt-1 min-w-[220px] rounded border border-border bg-card py-1 shadow-lg">
                  <ExtensionEntityActions entityType="image" entityId={image.id} renderMode="menu" onInvoked={() => setShowOpsMenu(false)} />
                  {image.files.length === 0 && canDownloadImage ? (
                    <button
                      type="button"
                      onClick={() => { setShowDownloadDialog(true); setShowOpsMenu(false); }}
                      className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground transition-colors hover:bg-surface"
                    >
                      <Download className="h-3.5 w-3.5" /> Download Media...
                    </button>
                  ) : null}
                </div>
              ) : null}
            </div>
          </>
        }
        keyboardShortcuts={imageKeyboardShortcuts}
      >
        <MediaDetailLayout.Content>
          {activeContent}
          <ExtensionSlot slot="image-detail-main-bottom" context={{ image, onNavigate }} />
        </MediaDetailLayout.Content>
      </MediaDetailLayout>
    </>
  );
}

function formatImageFaceSummary(face: FaceHostFace) {
  const confidence = face.topConfidence != null ? `${Math.round(face.topConfidence <= 1 ? face.topConfidence * 100 : face.topConfidence)}%` : null;
  return confidence || "AI face";
}
function DetailField({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="rounded-xl border border-border bg-surface/40 px-4 py-3">
      <div className="text-xs font-semibold uppercase tracking-wide text-muted">{label}</div>
      <div className="mt-1 text-sm text-foreground">{value}</div>
    </div>
  );
}

function EmptyPanel({ icon, message }: { icon: React.ReactNode; message: string }) {
  return (
    <div className="mt-4 flex flex-col items-center justify-center rounded-xl border border-dashed border-border bg-surface/40 px-4 py-8 text-center text-sm text-secondary">
      <div className="mb-3 opacity-60 text-muted">{icon}</div>
      <p>{message}</p>
    </div>
  );
}
