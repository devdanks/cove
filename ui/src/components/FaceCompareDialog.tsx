import { useEffect, useMemo, useState } from "react";
import { ChevronLeft, ChevronRight, ExternalLink, Fingerprint, Link2, XCircle } from "lucide-react";
import type { Face, FaceSuggestion, FaceSuggestionEvidence, FaceTopSuggestion } from "../api/types";
import { createRouteLinkProps } from "./cardNavigation";
import { EditModal } from "./EditModal";

type ComparableSuggestion = FaceSuggestion | FaceTopSuggestion;
type ConfirmOptions = { setPerformerImage?: boolean };

interface Props {
  open: boolean;
  face: Face | null;
  suggestion: ComparableSuggestion | null;
  faceImageUrls?: string[];
  disabled?: boolean;
  canReadPerformers: boolean;
  onClose: () => void;
  onConfirm: (suggestion: ComparableSuggestion, options?: ConfirmOptions) => void;
  onReject: (suggestion: ComparableSuggestion) => void;
  onNavigate: (route: any) => void;
}

export function FaceCompareDialog({
  open,
  face,
  suggestion,
  faceImageUrls,
  disabled = false,
  canReadPerformers,
  onClose,
  onConfirm,
  onReject,
  onNavigate,
}: Props) {
  const canSetPerformerImage = useMemo(() => {
    if (!face || !suggestion) {
      return false;
    }

    const localPerformerId = readLocalPerformerId(suggestion);
    return localPerformerId != null
      && !!face.coverImageUrl
      && suggestion.localPerformerHasImage === false
      && suggestion.localPerformerIsLocalOnly === true;
  }, [face, suggestion]);
  const [setPerformerImage, setSetPerformerImage] = useState(false);
  const [faceImageIndex, setFaceImageIndex] = useState(0);
  const [suggestionImageIndex, setSuggestionImageIndex] = useState(0);
  const evidence = useMemo(() => suggestion ? readEvidence(suggestion).slice(0, 5) : [], [suggestion]);
  const faceImages = useMemo(
    () => buildCarouselImageUrls(face?.coverImageUrl, faceImageUrls ?? []),
    [face?.coverImageUrl, faceImageUrls],
  );
  const suggestionImages = useMemo(
    () => buildCarouselImageUrls(suggestion?.coverImageUrl, evidence.map((item) => item.thumbnailUrl), true),
    [evidence, suggestion?.coverImageUrl],
  );

  useEffect(() => {
    setSetPerformerImage(canSetPerformerImage);
  }, [canSetPerformerImage, face?.id, open, suggestion?.performerId]);

  useEffect(() => {
    setFaceImageIndex(0);
    setSuggestionImageIndex(0);
  }, [face?.id, open, suggestion?.performerId]);

  if (!open || !face || !suggestion) {
    return null;
  }

  const faceTitle = face.label?.trim() || face.performerName || "Unidentified face";
  const localPerformerId = readLocalPerformerId(suggestion);
  const referenceOnly = localPerformerId == null && suggestion.performerId < 0;
  const why = readWhy(suggestion);

  const faceLinkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "face", id: face.id }, () => {
    onClose();
    onNavigate({ page: "face", id: face.id });
  });

  const performerLinkProps = localPerformerId != null && canReadPerformers
    ? createRouteLinkProps<HTMLAnchorElement>({ page: "performer", id: localPerformerId }, () => {
      onClose();
      onNavigate({ page: "performer", id: localPerformerId });
    })
    : null;

  return (
    <EditModal title="Compare suggestion" open={open} onClose={onClose}>
      <div className="space-y-5 py-5">
        <div className="grid gap-4 lg:grid-cols-2">
          <ComparePane
            eyebrow="Face in question"
            title={faceTitle}
            imageUrls={faceImages}
            imageIndex={faceImageIndex}
            onImageIndexChange={setFaceImageIndex}
            fallbackLabel="Unidentified face"
            footer={(
              <div className="space-y-2 text-xs text-secondary">
                <div>{face.appearanceCount ?? 0} appearance{(face.appearanceCount ?? 0) === 1 ? "" : "s"}</div>
                <div>{face.videoCount} video{face.videoCount === 1 ? "" : "s"} and {face.imageCount} image{face.imageCount === 1 ? "" : "s"}</div>
                <a {...faceLinkProps} className="inline-flex items-center gap-1 text-accent hover:underline">
                  Open face page
                </a>
              </div>
            )}
          />

          <ComparePane
            eyebrow={referenceOnly ? "Reference suggestion" : "Suggested performer"}
            title={suggestion.performerName}
            imageUrls={suggestionImages}
            imageIndex={suggestionImageIndex}
            onImageIndexChange={setSuggestionImageIndex}
            fallbackLabel={suggestion.performerName}
            footer={(
              <div className="space-y-2 text-xs text-secondary">
                <div>{formatPercent(suggestion.confidence)}% confidence</div>
                {why ? <p>{why}</p> : <p>Review the side-by-side cover images before confirming the link.</p>}
                <div className="flex flex-wrap gap-2 pt-1">
                  {performerLinkProps ? (
                    <a {...performerLinkProps} className="inline-flex items-center gap-1 rounded-lg border border-border px-3 py-1.5 text-foreground transition-colors hover:border-accent hover:text-accent">
                      <Link2 className="h-3.5 w-3.5" />
                      Open performer
                    </a>
                  ) : null}
                  {suggestion.externalUrl ? (
                    <a href={suggestion.externalUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 rounded-lg border border-border px-3 py-1.5 text-foreground transition-colors hover:border-accent hover:text-accent">
                      <ExternalLink className="h-3.5 w-3.5" />
                      Open external
                    </a>
                  ) : null}
                </div>
              </div>
            )}
          />
        </div>

        {evidence.length > 0 ? (
          <section className="space-y-3 rounded-2xl border border-border bg-card/40 p-4">
            <div>
              <div className="text-xs font-semibold uppercase tracking-wide text-muted">Supporting face evidence</div>
              <p className="mt-1 text-sm text-secondary">These nearby face clusters helped produce the current match.</p>
            </div>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
              {evidence.map((item) => {
                const linkProps = createRouteLinkProps<HTMLAnchorElement>({ page: "face", id: item.faceId }, () => {
                  onClose();
                  onNavigate({ page: "face", id: item.faceId });
                });

                return (
                  <a
                    key={`${suggestion.performerId}-${item.faceId}`}
                    {...linkProps}
                    className="group overflow-hidden rounded-2xl border border-border bg-surface/60 transition-colors hover:border-accent/60"
                  >
                    <div className="aspect-square bg-surface/80">
                      {item.thumbnailUrl ? (
                        <img src={item.thumbnailUrl} alt={`Evidence face ${item.faceId}`} className="h-full w-full object-cover" loading="lazy" />
                      ) : (
                        <div className="flex h-full w-full items-center justify-center text-muted">
                          <Fingerprint className="h-8 w-8" />
                        </div>
                      )}
                    </div>
                    <div className="space-y-1 p-3 text-xs text-secondary">
                      <div className="font-medium text-foreground group-hover:text-accent">Face #{item.faceId}</div>
                      <div>{formatPercent(item.similarity)}% similar</div>
                    </div>
                  </a>
                );
              })}
            </div>
          </section>
        ) : null}

        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border pt-4">
          {canSetPerformerImage ? (
            <label className="flex items-center gap-2 rounded-lg border border-border bg-card/40 px-3 py-2 text-sm text-secondary">
              <input
                type="checkbox"
                checked={setPerformerImage}
                onChange={(event) => setSetPerformerImage(event.target.checked)}
                className="rounded border-border bg-surface accent-accent"
              />
              Use face image for this local performer
            </label>
          ) : <span />}
          <div className="flex flex-wrap justify-end gap-2">
            <button
              type="button"
              onClick={() => onReject(suggestion)}
              disabled={disabled}
              className="inline-flex items-center gap-2 rounded-lg border border-border px-4 py-2 text-sm text-foreground transition-colors hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
            >
              <XCircle className="h-4 w-4" />
              Reject
            </button>
            <button
              type="button"
              onClick={() => onConfirm(suggestion, { setPerformerImage: canSetPerformerImage && setPerformerImage })}
              disabled={disabled}
              className="inline-flex items-center gap-2 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-not-allowed disabled:opacity-50"
            >
              {referenceOnly ? "Import performer" : "Confirm link"}
            </button>
          </div>
        </div>
      </div>
    </EditModal>
  );
}

function ComparePane({
  eyebrow,
  title,
  imageUrls,
  imageIndex,
  onImageIndexChange,
  fallbackLabel,
  footer,
}: {
  eyebrow: string;
  title: string;
  imageUrls: string[];
  imageIndex: number;
  onImageIndexChange: (value: number) => void;
  fallbackLabel: string;
  footer: React.ReactNode;
}) {
  const activeIndex = imageUrls.length === 0 ? 0 : Math.min(imageIndex, imageUrls.length - 1);
  const imageUrl = imageUrls[activeIndex];
  const canPage = imageUrls.length > 1;

  return (
    <section className="overflow-hidden rounded-2xl border border-border bg-card/50">
      <div className="relative aspect-square bg-surface/70">
        {imageUrl ? (
          <img src={imageUrl} alt={title} className="h-full w-full object-cover" loading="lazy" />
        ) : (
          <div className="flex h-full w-full items-center justify-center text-muted">
            <Fingerprint className="h-12 w-12" />
          </div>
        )}
        {canPage ? (
          <>
            <button
              type="button"
              onClick={() => onImageIndexChange(activeIndex === 0 ? imageUrls.length - 1 : activeIndex - 1)}
              className="absolute left-2 top-1/2 inline-flex h-9 w-9 -translate-y-1/2 items-center justify-center rounded-full border border-white/20 bg-black/55 text-white transition-colors hover:bg-black/75"
              title="Previous image"
              aria-label="Previous image"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <button
              type="button"
              onClick={() => onImageIndexChange(activeIndex === imageUrls.length - 1 ? 0 : activeIndex + 1)}
              className="absolute right-2 top-1/2 inline-flex h-9 w-9 -translate-y-1/2 items-center justify-center rounded-full border border-white/20 bg-black/55 text-white transition-colors hover:bg-black/75"
              title="Next image"
              aria-label="Next image"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
            <div className="absolute bottom-2 right-2 rounded-full bg-black/60 px-2 py-0.5 text-[11px] font-medium text-white">
              {activeIndex + 1}/{imageUrls.length}
            </div>
          </>
        ) : null}
      </div>
      <div className="space-y-3 p-4">
        <div>
          <div className="text-[11px] font-semibold uppercase tracking-wide text-muted">{eyebrow}</div>
          <div className="mt-1 text-base font-semibold text-foreground">{title || fallbackLabel}</div>
        </div>
        {footer}
      </div>
    </section>
  );
}

function readEvidence(suggestion: ComparableSuggestion): FaceSuggestionEvidence[] {
  return "evidence" in suggestion ? suggestion.evidence : [];
}

function readWhy(suggestion: ComparableSuggestion) {
  return "why" in suggestion ? suggestion.why : undefined;
}

function readLocalPerformerId(suggestion: ComparableSuggestion) {
  return suggestion.localPerformerId ?? (suggestion.performerId > 0 ? suggestion.performerId : undefined);
}

function buildCarouselImageUrls(primaryUrl: string | undefined, samples: Array<string | undefined>, skipFirstSampleWithPrimary = false) {
  const usableSamples = primaryUrl && skipFirstSampleWithPrimary ? samples.slice(1) : samples;
  return uniqueImageUrls([primaryUrl, ...usableSamples]).slice(0, 3);
}

function uniqueImageUrls(values: Array<string | undefined>) {
  return values
    .map((value) => value?.trim())
    .filter((value): value is string => !!value)
    .filter((value, index, all) => all.indexOf(value) === index);
}

function formatPercent(value: number) {
  const scaled = value <= 1 ? value * 100 : value;
  return Math.max(0, Math.min(100, Math.round(scaled)));
}

