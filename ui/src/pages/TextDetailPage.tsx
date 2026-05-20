import { Suspense, lazy, useEffect, useMemo, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BookOpenText, Check, Download, ExternalLink, FileText, Files, FolderOpen, Image, Link2, MoreVertical, Rows3, Trash2 } from "lucide-react";
import { entityImages, fileOps, playback, texts } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canReadEntity, canWriteEntity } from "../auth/visibility";
import { AspectRatingsPanel } from "../components/AspectRatingsPanel";
import { BookmarkButton } from "../components/BookmarkButton";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { DetailSkeleton } from "../components/DetailSkeleton";
import { MediaDetailLayout } from "../components/MediaDetailLayout/MediaDetailLayout";
import { CoverImageDialog } from "../components/CoverImageDialog";
import type { MediaDetailTab } from "../components/MediaDetailLayout/types";
import { InteractiveRating } from "../components/Rating";
import { TextViewer } from "../components/TextViewer";
import { CustomFieldsDisplay, formatDate, formatDuration, formatFileSize } from "../components/shared";
import { EntityReferencePopovers, PerformerTile } from "../components/EntityCards";
import { PerformerContextTagList, getPerformerContextTags } from "../components/PerformerContextTags";
import { useEntityEngagement } from "../hooks/useEntityEngagement";
import { useBackNavigation } from "../hooks/useBackNavigation";
import { createPlaybackSessionId, trackInteraction } from "../utils/interactionTracking";
import { getTextDisplayTitle, pickPrimaryTextFile } from "../utils/audioTextDisplay";
import { TextEditPanel } from "./TextEditPanel";

const MediaScrapeDialog = lazy(() => import("../components/MediaScrapeDialog").then((module) => ({ default: module.MediaScrapeDialog })));
const MediaDownloadDialog = lazy(() => import("../components/MediaDownloadDialog").then((module) => ({ default: module.MediaDownloadDialog })));

type TextTab = "details" | "read" | "file-info" | "history" | "edit";

interface Props {
  id: number;
  onNavigate: (route: any) => void;
}

export function TextDetailPage({ id, onNavigate }: Props) {
  const queryClient = useQueryClient();
  const { data: text, isLoading } = useQuery({
    queryKey: ["text", id],
    queryFn: () => texts.get(id),
  });
  const { data: content, isLoading: contentLoading } = useQuery({
    queryKey: ["text", id, "content"],
    queryFn: () => texts.content(id),
    enabled: !!text,
  });
  const { hasPermission, user } = useAuth();
  const { backLabel, goBack } = useBackNavigation({ page: "texts" }, onNavigate);
  const [activeTab, setActiveTab] = useState<TextTab>("read");
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [showOpsMenu, setShowOpsMenu] = useState(false);
  const [coverOpen, setCoverOpen] = useState(false);
  const [showScrapeDialog, setShowScrapeDialog] = useState(false);
  const [showDownloadDialog, setShowDownloadDialog] = useState(false);
  const opsMenuRef = useRef<HTMLDivElement>(null);
  const canReadText = canReadEntity("text", hasPermission);
  const canWriteText = canWriteEntity("text", hasPermission);
  const canDeleteText = canDeleteEntity("text", hasPermission);
  const canReadTags = canReadEntity("tag", hasPermission);
  const canReadPerformers = canReadEntity("performer", hasPermission);
  const canReadGroups = canReadEntity("group", hasPermission);
  const canReadStudio = canReadEntity("studio", hasPermission);
  const canReadFiles = hasPermission("files.read");
  const trackingEnabled = user?.uiPreferences?.tracking?.enabled ?? true;
  const canEngageText = canReadText && (user?.kind === "user" || user?.kind === "system");
  const trackTextActivity = canEngageText && trackingEnabled;
  const {
    engagement: textEngagement,
    rating: textRating,
    setRating: setTextRating,
  } = useEntityEngagement("text", id, {
    enabled: !!text && canEngageText,
    fallbackFavorite: false,
    fallbackRating: undefined,
  });
  const updateTextMut = useMutation({
    mutationFn: (data: { organized?: boolean }) => texts.update(id, data),
    onSuccess: (updatedText) => {
      queryClient.setQueryData(["text", id], updatedText);
      queryClient.invalidateQueries({ queryKey: ["texts"] });
    },
  });
  const deleteTextMut = useMutation({
    mutationFn: (options?: { deleteFile?: boolean; deleteGenerated?: boolean }) => texts.delete(id, options),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["texts"] });
      goBack();
    },
  });
  const revealFileMutation = useMutation({ mutationFn: (fileId: number) => fileOps.reveal(fileId) });
  const canRevealFiles = typeof window !== "undefined" && ["localhost", "127.0.0.1", "::1"].includes(window.location.hostname);
  const canDownloadTextMedia = canWriteText && hasPermission("jobs.run") && (text?.files.length ?? 0) === 0 && (text?.urls.length ?? 0) > 0;

  useEffect(() => {
    if (!text) {
      return;
    }

    document.title = `${getTextDisplayTitle(text)} | Cove`;
    return () => {
      document.title = "Cove";
    };
  }, [text]);

  useEffect(() => {
    if (!text || !trackTextActivity) {
      return;
    }

    const sessionId = createPlaybackSessionId();
    const startedAt = performance.now();
    let recorded = false;

    trackInteraction({ hostType: "text", hostId: text.id, kind: "pageVisit", meta: { source: "textDetailPage" } });
    queryClient.invalidateQueries({ queryKey: ["engagement", "text", text.id] });

    const recordVisit = (state: "abandoned" | "ended") => {
      if (recorded) {
        return;
      }

      recorded = true;
      const durationSec = Math.max(0.001, (performance.now() - startedAt) / 1000);
      void playback.recordIntervals({
        hostType: "text",
        hostId: text.id,
        sessionId,
        mediaDurationSec: durationSec,
        currentPositionSec: durationSec,
        state,
        intervals: [{ startSec: 0, endSec: durationSec }],
      }).catch(() => {});
      queryClient.invalidateQueries({ queryKey: ["engagement", "text", text.id] });
    };

    const handlePageHide = () => recordVisit("abandoned");
    window.addEventListener("pagehide", handlePageHide);
    return () => {
      window.removeEventListener("pagehide", handlePageHide);
      recordVisit("ended");
    };
  }, [queryClient, text, trackTextActivity]);

  const primaryFile = useMemo(() => pickPrimaryTextFile(text), [text]);
  const displayTitle = text ? getTextDisplayTitle(text) : `Text ${id}`;
  const subtitleText = useMemo(() => {
    if (!text) {
      return undefined;
    }

    return [text.performers.map((performer) => performer.name).filter(Boolean).join(", "), text.studioName, text.date ? formatDate(text.date) : null]
      .filter(Boolean)
      .join(" • ") || undefined;
  }, [text]);
  const detailSubtitle = text && ((canReadPerformers && text.performers.length > 0) || (canReadTags && text.tags.length > 0) || (canReadGroups && text.groups.length > 0) || (canReadStudio && text.studioId && text.studioName) || text.date) ? (
    <div className="flex flex-wrap items-center gap-2">
      <EntityReferencePopovers
        performers={canReadPerformers ? text.performers : []}
        tags={canReadTags ? text.tags : []}
        groups={canReadGroups ? text.groups : []}
        studio={canReadStudio ? { id: text.studioId, name: text.studioName } : null}
        onNavigate={onNavigate}
      />
      {text.date ? <span className="text-sm text-secondary">{formatDate(text.date)}</span> : null}
    </div>
  ) : subtitleText;
  const headerImage = text?.imagePath ? (
    <img src={text.imagePath} alt={`${displayTitle} cover`} className="h-24 w-20 rounded-2xl border border-border object-cover shadow-lg shadow-black/20" />
  ) : undefined;
  const textCoverUrl = text?.imagePath ?? undefined;
  const tabs = useMemo(() => {
    const nextTabs: MediaDetailTab[] = [{ key: "read", label: "Read" }, { key: "details", label: "Details" }];
    if (canReadFiles && (text?.files.length ?? 0) > 0) {
      nextTabs.push({ key: "file-info", label: "File Info", count: text?.files.length ?? 0 });
    }
    nextTabs.push({ key: "history", label: "History" });
    if (canWriteText) {
      nextTabs.push({ key: "edit", label: "Edit" });
    }
    return nextTabs;
  }, [canReadFiles, canReadGroups, canReadPerformers, canReadStudio, canReadTags, canWriteText, text?.files.length, text?.groups.length, text?.performers.length, text?.studioId, text?.tags.length]);

  useEffect(() => {
    if (!tabs.some((tab) => tab.key === activeTab)) {
      setActiveTab("read");
    }
  }, [activeTab, tabs]);

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

  if (isLoading) {
    return <DetailSkeleton showMedia={false} />;
  }

  if (!text) {
    return <div className="rounded-3xl border border-dashed border-border bg-card/70 px-6 py-10 text-sm text-muted">Text document #{id} was not found.</div>;
  }

  return (
    <>
    {text ? (
      <CoverImageDialog
        open={coverOpen}
        title="Set Text Cover"
        currentImageUrl={textCoverUrl}
        onUpload={(file) => entityImages.uploadTextImage(text.id, file)}
        onDelete={() => entityImages.deleteTextImage(text.id)}
        onClose={() => setCoverOpen(false)}
        onSuccess={() => {
          queryClient.invalidateQueries({ queryKey: ["text", text.id] });
          queryClient.invalidateQueries({ queryKey: ["texts"] });
        }}
        aspectRatio="2/3"
      />
    ) : null}
    <MediaDetailLayout
      title={displayTitle}
      subtitle={detailSubtitle}
      backLabel={backLabel}
      onGoBack={goBack}
      headerImage={headerImage}
      media={<TextViewer content={content?.content} renderMode={content?.renderMode} />}
      mediaAspectRatio="auto"
      mediaFullBleed
      mediaSticky={false}
      tabs={tabs}
      activeTab={activeTab}
      onTabChange={(key) => setActiveTab(key as TextTab)}
      engagement={{
        primaryContent: <InteractiveRating value={textRating} onChange={(value) => setTextRating(value)} readOnly={!canEngageText} />,
        additionalMetrics: [
          { label: "Words", value: text.maxWordCount ? Intl.NumberFormat().format(text.maxWordCount) : "-", icon: <BookOpenText className="h-4 w-4" /> },
          { label: "Pages", value: text.maxPageCount ?? "-", icon: <Files className="h-4 w-4" /> },
        ],
      }}
      actions={
        <>
          <BookmarkButton hostType="text" hostId={text.id} compact />
          {canWriteText ? (
            <button
              type="button"
              onClick={() => { if (!updateTextMut.isPending) updateTextMut.mutate({ organized: !text.organized }); }}
              disabled={updateTextMut.isPending}
              className={`inline-flex items-center justify-center rounded p-1 transition ${text.organized ? "bg-green-600 text-white" : "text-secondary hover:bg-card hover:text-foreground"} ${updateTextMut.isPending ? "cursor-not-allowed opacity-60" : ""}`}
              title={text.organized ? "Organized" : "Mark organized"}
            >
              <Check className="h-4 w-4" />
            </button>
          ) : text.organized ? (
            <span className="inline-flex items-center justify-center rounded bg-green-600 p-1 text-white" title="Organized">
              <Check className="h-4 w-4" />
            </span>
          ) : null}
          {canReadFiles && text.files.length > 0 ? (
            <a
              href={texts.fileUrl(text.id)}
              className="inline-flex items-center justify-center rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
              title="Download source file"
            >
              <Download className="h-4 w-4" />
            </a>
          ) : null}
          {canWriteText || canDeleteText ? (
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
                  {canWriteText ? (
                    <button
                      type="button"
                      onClick={() => {
                        setShowScrapeDialog(true);
                        setShowOpsMenu(false);
                      }}
                      className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground transition-colors hover:bg-surface"
                    >
                      <ExternalLink className="h-3.5 w-3.5" /> Scrape...
                    </button>
                  ) : null}
                  {canDownloadTextMedia ? (
                    <button
                      type="button"
                      onClick={() => {
                        setShowDownloadDialog(true);
                        setShowOpsMenu(false);
                      }}
                      className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground transition-colors hover:bg-surface"
                    >
                      <Download className="h-3.5 w-3.5" /> Download Media...
                    </button>
                  ) : null}
                  {canWriteText ? (
                    <button
                      type="button"
                      onClick={() => {
                        setCoverOpen(true);
                        setShowOpsMenu(false);
                      }}
                      className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-foreground transition-colors hover:bg-surface"
                    >
                      <Image className="h-3.5 w-3.5" /> Set Cover...
                    </button>
                  ) : null}
                  {canDeleteText ? (
                    <button
                      type="button"
                      onClick={() => {
                        setConfirmDelete(true);
                        setShowOpsMenu(false);
                      }}
                      className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm text-red-400 transition-colors hover:bg-surface"
                    >
                      <Trash2 className="h-3.5 w-3.5" /> Delete
                    </button>
                  ) : null}
                </div>
              ) : null}
            </div>
          ) : null}
        </>
      }
    >
      <ConfirmDialog
        open={confirmDelete}
        title="Delete Text"
        message={`Delete "${displayTitle}"? This cannot be undone.`}
        confirmLabel={deleteTextMut.isPending ? "Deleting..." : "Delete Text"}
        onConfirm={(options) => deleteTextMut.mutate(options)}
        onCancel={() => setConfirmDelete(false)}
        showDeleteFile
        showDeleteGenerated
      />
      <MediaDetailLayout.Content>
        {activeTab === "read" ? (
          <section className="overflow-hidden rounded-3xl border border-border bg-card/75">
            {contentLoading ? (
              <div className="p-5 text-sm text-muted">Loading extracted text content...</div>
            ) : content?.content ? (
              <TextViewer content={content.content} renderMode={content.renderMode} />
            ) : (
              <div className="p-5 text-sm text-muted">No extracted text content is available yet.</div>
            )}
          </section>
        ) : null}

        {activeTab === "details" ? (
          <div className="space-y-4">
            <MediaDetailLayout.Metadata>
              <DetailGrid
                items={[
                  { label: "Studio", value: text.studioName },
                  { label: "Date", value: text.date ? formatDate(text.date) : undefined },
                  { label: "Words", value: text.maxWordCount ? Intl.NumberFormat().format(text.maxWordCount) : undefined },
                  { label: "Pages", value: text.maxPageCount ? String(text.maxPageCount) : undefined },
                  { label: "Files", value: String(text.fileCount) },
                ]}
              />
            </MediaDetailLayout.Metadata>
            {text.details ? (
              <section className="rounded-3xl border border-border bg-card/75 p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Notes</h3>
                <p className="mt-3 whitespace-pre-wrap text-sm leading-7 text-foreground/92">{text.details}</p>
              </section>
            ) : null}
            {text.urls.length > 0 ? (
              <section className="rounded-3xl border border-border bg-card/75 p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Source URLs</h3>
                <div className="mt-3 flex flex-col gap-2">
                  {text.urls.map((url) => (
                    <a key={url} href={url} target="_blank" rel="noreferrer" className="inline-flex items-center gap-2 text-sm text-accent transition hover:text-accent/80">
                      <Link2 className="h-4 w-4" />
                      <span className="truncate">{url}</span>
                    </a>
                  ))}
                </div>
              </section>
            ) : null}
            {canReadPerformers && text.performers.length > 0 ? (
              <section className="rounded-3xl border border-border bg-card/75 p-5">
                <h3 className="text-sm font-semibold uppercase tracking-[0.18em] text-muted">Performers</h3>
                <div className={text.performers.length > 1 ? "mt-4 grid grid-cols-2 gap-3" : "mt-4 grid max-w-[220px] gap-3"}>
                  {text.performers.map((performer) => {
                    const contextTags = getPerformerContextTags(text.contextTagApplications, performer.id);
                    return (
                      <PerformerTile
                        key={performer.id}
                        performer={performer}
                        onClick={() => onNavigate({ page: "performer", id: performer.id })}
                        onNavigate={onNavigate}
                      >
                        {contextTags.length > 0 ? <div className="space-y-2 text-xs text-secondary"><PerformerContextTagList contextTags={contextTags} onNavigate={onNavigate} /></div> : null}
                      </PerformerTile>
                    );
                  })}
                </div>
              </section>
            ) : null}
            {(canReadTags && text.tags.length > 0) || (canReadGroups && text.groups.length > 0) || (canReadStudio && text.studioId && text.studioName) ? (
              <RelatedSection icon={<Rows3 className="h-4 w-4" />} title="Related Entities">
                <EntityReferencePopovers
                  performers={[]}
                  tags={canReadTags ? text.tags : []}
                  groups={canReadGroups ? text.groups : []}
                  studio={canReadStudio ? { id: text.studioId, name: text.studioName } : null}
                  onNavigate={onNavigate}
                />
              </RelatedSection>
            ) : null}
            {text.customFields && Object.keys(text.customFields).length > 0 ? (
              <MediaDetailLayout.Metadata>
                <CustomFieldsDisplay customFields={text.customFields} entityType="text" />
              </MediaDetailLayout.Metadata>
            ) : null}
            <AspectRatingsPanel hostType="text" hostId={text.id} canRate={canEngageText} />
          </div>
        ) : null}

        {activeTab === "file-info" ? (
          <div className="space-y-4">
            {text.files.map((file) => (
              <MediaDetailLayout.Metadata key={file.id}>
                <div className="flex items-center justify-between gap-3">
                  <div>
                    <h3 className="text-sm font-semibold text-foreground">{file.basename}</h3>
                    <p className="text-xs text-muted">{file.path}</p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    {canRevealFiles && file.id ? (
                      <button
                        type="button"
                        onClick={() => revealFileMutation.mutate(file.id)}
                        className="inline-flex items-center gap-1 rounded border border-border px-2 py-1 text-xs text-secondary hover:border-accent hover:text-foreground"
                      >
                        <FolderOpen className="h-3.5 w-3.5" />
                        Reveal
                      </button>
                    ) : null}
                    <span className="rounded-full border border-border px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.18em] text-muted">{file.format}</span>
                  </div>
                </div>
                <DetailGrid
                  items={[
                    { label: "Pages", value: file.pageCount ? String(file.pageCount) : undefined },
                    { label: "Words", value: file.wordCount ? Intl.NumberFormat().format(file.wordCount) : undefined },
                    { label: "Size", value: formatFileSize(file.size) },
                    { label: "Excerpt", value: file.excerptText?.trim() || undefined },
                  ]}
                />
              </MediaDetailLayout.Metadata>
            ))}
          </div>
        ) : null}

        {activeTab === "history" ? (
          <MediaDetailLayout.Metadata>
            <DetailGrid
              items={[
                { label: "Page Visits", value: String(textEngagement?.pageVisitCount ?? 0) },
                { label: "Time Open", value: formatDuration(textEngagement?.playDuration ?? 0) },
              ]}
            />
          </MediaDetailLayout.Metadata>
        ) : null}

        {activeTab === "edit" ? <TextEditPanel text={text} onSaved={() => setActiveTab("details")} /> : null}
      </MediaDetailLayout.Content>
    </MediaDetailLayout>
    {showScrapeDialog ? (
      <Suspense fallback={null}>
        <MediaScrapeDialog
          open={showScrapeDialog}
          onClose={() => setShowScrapeDialog(false)}
          entityType="text"
          entity={{
            id: text.id,
            title: text.title,
            code: text.code,
            details: text.details,
            date: text.date,
            studioName: text.studioName,
            urls: text.urls,
            tags: text.tags,
            performers: text.performers,
            files: text.files,
            organized: text.organized,
          }}
        />
      </Suspense>
    ) : null}
    {showDownloadDialog ? (
      <Suspense fallback={null}>
        <MediaDownloadDialog
          open={showDownloadDialog}
          entity="Text"
          item={text}
          listQueryKey="texts"
          detailQueryKey="text"
          routePage="text"
          onClose={() => setShowDownloadDialog(false)}
          onNavigate={onNavigate}
        />
      </Suspense>
    ) : null}
    </>
  );
}

function DetailGrid({ items }: { items: { label: string; value?: string }[] }) {
  const visibleItems = items.filter((item) => item.value != null && String(item.value).trim() !== "");
  if (visibleItems.length === 0) {
    return <p className="text-sm text-muted">No metadata available.</p>;
  }

  return (
    <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
      {visibleItems.map((item) => (
        <div key={item.label}>
          <dt className="text-[11px] font-medium uppercase tracking-[0.18em] text-muted">{item.label}</dt>
          <dd className="mt-1 whitespace-pre-wrap text-sm text-foreground">{item.value}</dd>
        </div>
      ))}
    </dl>
  );
}

function RelatedSection({ icon, title, children }: { icon: React.ReactNode; title: string; children: React.ReactNode }) {
  return (
    <section className="rounded-3xl border border-border bg-card/75 p-5">
      <div className="flex items-center gap-2 text-sm font-semibold uppercase tracking-[0.18em] text-muted">
        {icon}
        {title}
      </div>
      <div className="mt-4 flex flex-wrap gap-2">{children}</div>
    </section>
  );
}