import { useEffect, useMemo, useState } from "react";
import { BookOpen, Check, ChevronLeft, ChevronRight, Database, ExternalLink, FolderOpen, HelpCircle, ImageIcon, LayoutGrid, Play, RefreshCw, Settings, Tag, X } from "lucide-react";
import ReactMarkdown from "react-markdown";
import type { ExtensionTutorialTopic } from "../api/types";
import { normalizeManualContext, uniqueManualContexts, type TutorialOpenRequest } from "./ManualContext";

export const TUTORIAL_STORYBOARD_STORAGE_KEY = "cove-tutorial-storyboard-complete";
export const TUTORIAL_STORYBOARD_EVENT = "cove:tutorial-storyboard-open";

export type TutorialSlideMockKind = "tasks" | "feed" | "metadata" | "settings" | "scenePlayer" | "tagging" | "images" | "extension";

export type { TutorialOpenRequest } from "./ManualContext";

export interface TutorialStoryboardSlide {
  id: string;
  title: string;
  caption?: string;
  bodyMarkdown?: string;
  imageSrc?: string;
  imageAlt?: string;
  mockKind?: TutorialSlideMockKind;
  points?: string[];
  links?: { label: string; url: string }[];
}

export interface TutorialStoryboardTopic {
  id: string;
  title: string;
  description?: string;
  pages?: string[];
  contexts?: string[];
  extensionId?: string;
  parentTopicId?: string;
  order: number;
  slides: TutorialStoryboardSlide[];
}

interface TutorialTopicEntry {
  topic: TutorialStoryboardTopic;
  depth: number;
}

const builtinTutorialTopics: TutorialStoryboardTopic[] = [
  {
    id: "getting-started",
    title: "Getting Started",
    description: "The first pass through setup, tasks, browsing, and where to return later.",
    pages: ["home", "settings"],
    order: 10,
    slides: [
      {
        id: "tasks",
        title: "Start with Tasks",
        caption: "Scan after adding folders, then generate the media Cove needs for fast browsing.",
        mockKind: "tasks",
        points: ["Scan reads library folders", "Generate creates previews and thumbnails", "Jobs keep running while you browse"],
      },
      {
        id: "browse",
        title: "Pick the right browsing shape",
        caption: "Scenes and images can move between grid, feed, wall, and infinite sessions.",
        mockKind: "feed",
        points: ["Grid is quick scanning", "Feed shows context", "Infinite keeps long sessions smooth"],
      },
      {
        id: "metadata",
        title: "Clean metadata from the item",
        caption: "Use Scrape or Identify from a detail page first, then scale the workflow up when the match looks right.",
        mockKind: "metadata",
        points: ["Review fields before applying", "Tune providers in Settings", "Batch only after a single-item check"],
      },
      {
        id: "return",
        title: "Replay this any time",
        caption: "Open Help from the top bar to jump back into the manual for the page you are on.",
        mockKind: "settings",
        points: ["Topics can target specific pages", "Extensions can add their own topics", "Links can open a specific topic or slide"],
      },
    ],
  },
  {
    id: "scenes",
    title: "Scenes",
    description: "Watching, browsing, resuming, and managing scene metadata.",
    pages: ["scenes", "scene"],
    order: 20,
    slides: [
      {
        id: "watching",
        title: "Watch from the detail page",
        caption: "Scene detail pages center playback, timeline context, and the metadata you need while watching.",
        mockKind: "scenePlayer",
        points: ["Resume applies when a real position exists", "Configured default starts handle long videos", "Timeline and metadata stay close to the player"],
      },
      {
        id: "scene-feed",
        title: "Use Feed for review sessions",
        caption: "Feed mode keeps playback and metadata in one vertical session for browsing many scenes.",
        mockKind: "feed",
        points: ["Infinite mode keeps loading results", "Selection options appear after selecting an item", "The floating auto-scroll control follows the session"],
      },
    ],
  },
  {
    id: "images",
    title: "Images",
    description: "Image browsing modes, wall sessions, lightbox review, and metadata cleanup.",
    pages: ["images", "image", "galleries", "gallery"],
    order: 30,
    slides: [
      {
        id: "image-modes",
        title: "Choose the image view",
        caption: "Grid, wall, tagger, and feed each support a different kind of image workflow.",
        mockKind: "images",
        points: ["Grid is dense and direct", "Wall preserves a visual scan", "Feed carries more context per image"],
      },
      {
        id: "image-metadata",
        title: "Review before batching",
        caption: "Open image details or scrape one item before applying large metadata changes.",
        mockKind: "metadata",
        points: ["Use single-item review for provider quality", "Bulk edit applies only after selection", "Galleries and tags can be cleaned from the same flow"],
      },
    ],
  },
  {
    id: "tagging",
    title: "Tagging",
    description: "Tagger views, segment-derived data, and metadata review loops.",
    pages: ["tags", "tag", "segments", "segment", "faces", "face"],
    order: 40,
    slides: [
      {
        id: "tagger-views",
        title: "Use tagger views for fast cleanup",
        caption: "Tagger views are built for repeated metadata decisions across many results.",
        mockKind: "tagging",
        points: ["Select an item to reveal bulk options", "Use filters before selecting all matching", "Move to detail pages when one item needs care"],
      },
      {
        id: "segments",
        title: "Segments connect metadata to time",
        caption: "Segments and spans help Cove turn scene time ranges into reusable browsing and tagging context.",
        mockKind: "scenePlayer",
        points: ["Resolved spans can open directly in the player", "Raw segments remain available for inspection", "Infinite result windows can restore earlier pages"],
      },
    ],
  },
  {
    id: "settings",
    title: "Settings",
    description: "Configuration areas for navigation, playback, feed behavior, themes, and extensions.",
    pages: ["settings", "stats", "logs"],
    order: 50,
    slides: [
      {
        id: "settings-map",
        title: "Settings is the control room",
        caption: "Use Settings when the browsing experience, playback behavior, extensions, or library paths need adjustment.",
        mockKind: "settings",
        points: ["Navigation controls top-level tabs", "Scene Player controls watching defaults", "Extensions can add panels and tutorial topics"],
      },
    ],
  },
];

export function hasCompletedTutorialStoryboard() {
  return localStorage.getItem(TUTORIAL_STORYBOARD_STORAGE_KEY) === "true";
}

export function openTutorialStoryboard(request?: TutorialOpenRequest | string) {
  const detail = typeof request === "string" ? { topicId: request } : request;
  window.dispatchEvent(new CustomEvent<TutorialOpenRequest | undefined>(TUTORIAL_STORYBOARD_EVENT, { detail }));
}

interface Props {
  open: boolean;
  onClose: () => void;
  request?: TutorialOpenRequest;
  currentPage?: string;
  extensionTopics?: ExtensionTutorialTopic[];
}

export function TutorialStoryboardDialog({ open, onClose, request, currentPage, extensionTopics = [] }: Props) {
  const topics = useMemo(() => mergeTutorialTopics(extensionTopics), [extensionTopics]);
  const topicEntries = useMemo(() => buildTopicEntries(topics), [topics]);
  const [selectedTopicId, setSelectedTopicId] = useState(() => pickInitialTopicId(topics, request, currentPage));
  const [index, setIndex] = useState(0);
  const selectedTopic = topics.find((topic) => topic.id === selectedTopicId) ?? topics[0];
  const slide = selectedTopic.slides[index] ?? selectedTopic.slides[0];
  const isLast = index === selectedTopic.slides.length - 1;
  const progressLabel = `${index + 1} of ${selectedTopic.slides.length}`;

  useEffect(() => {
    if (!open) return;
    const nextTopicId = pickInitialTopicId(topics, request, currentPage);
    const nextTopic = topics.find((topic) => topic.id === nextTopicId) ?? topics[0];
    const nextSlideIndex = request?.slideId ? Math.max(0, nextTopic.slides.findIndex((item) => item.id === request.slideId)) : 0;
    setSelectedTopicId(nextTopic.id);
    setIndex(nextSlideIndex);
  }, [currentPage, open, request, topics]);

  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        markCompleteAndClose();
      } else if (event.key === "ArrowRight") {
        setIndex((current) => Math.min(selectedTopic.slides.length - 1, current + 1));
      } else if (event.key === "ArrowLeft") {
        setIndex((current) => Math.max(0, current - 1));
      }
    };

    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [open, selectedTopic.slides.length]);

  if (!open || !selectedTopic || !slide) return null;

  function markCompleteAndClose() {
    localStorage.setItem(TUTORIAL_STORYBOARD_STORAGE_KEY, "true");
    onClose();
  }

  function chooseTopic(topicId: string) {
    setSelectedTopicId(topicId);
    setIndex(0);
  }

  return (
    <div className="fixed inset-0 z-[80] flex items-center justify-center bg-black/70 px-3 py-4" role="dialog" aria-modal="true" aria-labelledby="tutorial-storyboard-title">
      <div className="flex h-[90vh] max-h-[92vh] w-[96vw] max-w-[96rem] flex-col overflow-hidden rounded-xl border border-border bg-background shadow-2xl">
        <div className="flex items-center justify-between gap-3 border-b border-border px-4 py-3">
          <div className="flex min-w-0 items-center gap-3">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-accent/15 text-accent">
              <BookOpen className="h-5 w-5" />
            </div>
            <div className="min-w-0">
              <div className="text-xs font-semibold uppercase tracking-wide text-muted">Cove manual</div>
              <h2 id="tutorial-storyboard-title" className="truncate text-base font-semibold text-foreground">{selectedTopic.title}</h2>
            </div>
          </div>
          <button type="button" onClick={markCompleteAndClose} className="rounded p-2 text-muted transition-colors hover:bg-surface hover:text-foreground" title="Close manual">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="grid min-h-0 flex-1 overflow-hidden lg:grid-cols-[17rem_minmax(0,1.45fr)_minmax(18rem,0.55fr)]">
          <aside className="hidden min-h-0 border-r border-border bg-nav/40 p-3 lg:block">
            <div className="mb-2 px-2 text-xs font-semibold uppercase tracking-wide text-muted">Topics</div>
            <div className="space-y-1 overflow-y-auto pr-1">
              {topicEntries.map(({ topic, depth }) => (
                <button
                  key={topic.id}
                  type="button"
                  onClick={() => chooseTopic(topic.id)}
                  data-topic-depth={depth}
                  style={{ paddingLeft: `${0.75 + depth * 1.1}rem` }}
                  className={`w-full rounded-lg px-3 py-2 text-left transition-colors ${topic.id === selectedTopic.id ? "bg-accent/15 text-accent" : "text-secondary hover:bg-card hover:text-foreground"}`}
                >
                  <span className="block truncate text-sm font-medium">{topic.title}</span>
                  {topic.description ? <span className="mt-0.5 line-clamp-2 block text-xs text-muted">{topic.description}</span> : null}
                  {topic.extensionId ? <span className="mt-1 block text-[11px] text-muted">Extension</span> : null}
                </button>
              ))}
            </div>
          </aside>

          <div className="min-h-0 overflow-y-auto bg-black/20 p-4 sm:p-6">
            <div className="mb-3 flex gap-1.5 lg:hidden">
              <select
                value={selectedTopic.id}
                onChange={(event) => chooseTopic(event.target.value)}
                className="w-full rounded-lg border border-border bg-input px-3 py-2 text-sm text-foreground"
                aria-label="Tutorial topic"
              >
                {topicEntries.map(({ topic, depth }) => <option key={topic.id} value={topic.id}>{`${"  ".repeat(depth)}${topic.title}`}</option>)}
              </select>
            </div>
            <StoryboardPreview slide={slide} />
          </div>

          <aside className="flex min-h-0 flex-col overflow-y-auto border-t border-border p-4 lg:border-l lg:border-t-0 sm:p-6">
            <div className="text-xs font-semibold uppercase tracking-wide text-muted">{progressLabel}</div>
            <h3 className="mt-2 text-xl font-semibold text-foreground">{slide.title}</h3>
            {slide.caption ? <p className="mt-3 text-sm leading-6 text-secondary">{slide.caption}</p> : null}
            {slide.bodyMarkdown ? <ManualMarkdown markdown={slide.bodyMarkdown} /> : null}
            {(slide.points?.length ?? 0) > 0 ? (
              <div className="mt-5 space-y-2">
                {slide.points!.map((point) => (
                  <div key={point} className="flex items-start gap-2 rounded-lg border border-border bg-card/70 px-3 py-2 text-sm text-secondary">
                    <Check className="mt-0.5 h-4 w-4 shrink-0 text-accent" />
                    <span>{point}</span>
                  </div>
                ))}
              </div>
            ) : null}
            {(slide.links?.length ?? 0) > 0 ? (
              <div className="mt-5 space-y-2">
                {slide.links!.map((link) => (
                  <a
                    key={`${link.label}:${link.url}`}
                    href={link.url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex w-full items-center justify-between gap-3 rounded-lg border border-border bg-card/70 px-3 py-2 text-sm text-accent transition-colors hover:border-accent hover:bg-card"
                  >
                    <span className="truncate">{link.label}</span>
                    <ExternalLink className="h-4 w-4 shrink-0" />
                  </a>
                ))}
              </div>
            ) : null}

            <div className="mt-auto pt-6">
              <div className="mb-4 flex gap-1.5">
                {selectedTopic.slides.map((item, itemIndex) => (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => setIndex(itemIndex)}
                    className={`h-1.5 flex-1 rounded-full transition-colors ${itemIndex === index ? "bg-accent" : "bg-border hover:bg-muted"}`}
                    aria-label={`Go to slide ${itemIndex + 1}`}
                  />
                ))}
              </div>
              <div className="flex flex-wrap items-center justify-between gap-2">
                <button
                  type="button"
                  onClick={() => setIndex((current) => Math.max(0, current - 1))}
                  disabled={index === 0}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-border px-3 py-2 text-sm text-secondary transition-colors hover:border-accent hover:text-foreground disabled:cursor-not-allowed disabled:opacity-45"
                >
                  <ChevronLeft className="h-4 w-4" />
                  Back
                </button>
                <button
                  type="button"
                  onClick={() => {
                    if (isLast) markCompleteAndClose();
                    else setIndex((current) => Math.min(selectedTopic.slides.length - 1, current + 1));
                  }}
                  className="inline-flex items-center gap-1.5 rounded-lg bg-accent px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
                >
                  {isLast ? "Done" : "Next"}
                  {isLast ? <Check className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
                </button>
              </div>
            </div>
          </aside>
        </div>
      </div>
    </div>
  );
}

function mergeTutorialTopics(extensionTopics: ExtensionTutorialTopic[]): TutorialStoryboardTopic[] {
  const normalizedExtensionTopics = extensionTopics
    .filter((topic) => topic.id && topic.title && (topic.slides?.length ?? 0) > 0)
    .map<TutorialStoryboardTopic>((topic) => ({
      id: topic.id,
      title: topic.title,
      description: topic.description,
      pages: topic.pages,
      contexts: normalizeManualContexts(topic.contexts),
      extensionId: topic.extensionId,
      parentTopicId: topic.parentTopicId,
      order: topic.order ?? 100,
      slides: (topic.slides ?? []).map((slide) => ({
        id: slide.id,
        title: slide.title,
        caption: slide.caption,
        bodyMarkdown: slide.bodyMarkdown,
        imageSrc: resolveManualImageSrc(slide.imageSrc, topic.extensionId),
        imageAlt: slide.imageAlt,
        mockKind: normalizeMockKind(slide.mockKind),
        points: slide.points?.length ? slide.points : [],
        links: normalizeManualLinks(slide.links),
      })),
    }));

  return [...builtinTutorialTopics, ...normalizedExtensionTopics].sort((left, right) => left.order - right.order || left.title.localeCompare(right.title));
}

function buildTopicEntries(topics: TutorialStoryboardTopic[]): TutorialTopicEntry[] {
  const sorted = [...topics].sort((left, right) => left.order - right.order || left.title.localeCompare(right.title));
  const byId = new Map(sorted.map((topic) => [topic.id, topic]));
  const childrenByParent = new Map<string, TutorialStoryboardTopic[]>();
  const roots: TutorialStoryboardTopic[] = [];

  for (const topic of sorted) {
    if (topic.parentTopicId && byId.has(topic.parentTopicId)) {
      const children = childrenByParent.get(topic.parentTopicId) ?? [];
      children.push(topic);
      childrenByParent.set(topic.parentTopicId, children);
    } else {
      roots.push(topic);
    }
  }

  const entries: TutorialTopicEntry[] = [];
  const visited = new Set<string>();
  const visit = (topic: TutorialStoryboardTopic, depth: number) => {
    if (visited.has(topic.id)) return;
    visited.add(topic.id);
    entries.push({ topic, depth });
    for (const child of childrenByParent.get(topic.id) ?? []) {
      visit(child, depth + 1);
    }
  };

  for (const topic of roots) {
    visit(topic, 0);
  }

  for (const topic of sorted) {
    visit(topic, 0);
  }

  return entries;
}

function normalizeManualLinks(links?: { label: string; url: string }[]) {
  return (links ?? [])
    .map((link) => ({ label: link.label?.trim(), url: normalizeManualLinkUrl(link.url) }))
    .filter((link): link is { label: string; url: string } => Boolean(link.label && link.url));
}

function normalizeManualLinkUrl(url?: string) {
  if (!url) return undefined;
  try {
    const parsed = new URL(url);
    return parsed.protocol === "http:" || parsed.protocol === "https:" ? parsed.toString() : undefined;
  } catch {
    return undefined;
  }
}

function resolveManualImageSrc(imageSrc: string | undefined, extensionId: string | undefined) {
  const value = imageSrc?.trim();
  if (!value) return undefined;
  if (isAbsoluteManualAssetUrl(value) || value.startsWith("/")) return value;
  if (!extensionId) return value;

  const normalizedPath = value.replace(/^\.\//, "").split("/").filter(Boolean).map(encodeURIComponent).join("/");
  return `/api/extensions/assets/${encodeURIComponent(extensionId)}/${normalizedPath}`;
}

function isAbsoluteManualAssetUrl(value: string) {
  try {
    const parsed = new URL(value);
    return parsed.protocol === "http:" || parsed.protocol === "https:" || parsed.protocol === "data:";
  } catch {
    return false;
  }
}

function ManualMarkdown({ markdown }: { markdown: string }) {
  return (
    <div className="mt-4 text-sm leading-6 text-secondary">
      <ReactMarkdown
        components={{
          p: ({ children }) => <p className="mb-3 last:mb-0">{children}</p>,
          ul: ({ children }) => <ul className="mb-3 list-disc space-y-1 pl-5 last:mb-0">{children}</ul>,
          ol: ({ children }) => <ol className="mb-3 list-decimal space-y-1 pl-5 last:mb-0">{children}</ol>,
          li: ({ children }) => <li>{children}</li>,
          strong: ({ children }) => <strong className="font-semibold text-foreground">{children}</strong>,
          code: ({ children }) => <code className="rounded bg-card px-1 py-0.5 text-xs text-foreground">{children}</code>,
          a: ({ href, children }) => {
            const safeHref = normalizeManualLinkUrl(href);
            return safeHref ? <a href={safeHref} target="_blank" rel="noopener noreferrer" className="text-accent hover:underline">{children}</a> : <span>{children}</span>;
          },
        }}
      >
        {markdown}
      </ReactMarkdown>
    </div>
  );
}

function normalizeMockKind(value?: string): TutorialSlideMockKind | undefined {
  const knownKinds = new Set<TutorialSlideMockKind>(["tasks", "feed", "metadata", "settings", "scenePlayer", "tagging", "images", "extension"]);
  return knownKinds.has(value as TutorialSlideMockKind) ? value as TutorialSlideMockKind : undefined;
}

function normalizeManualContexts(contexts?: string[]) {
  return uniqueManualContexts(contexts ?? []);
}

function pickInitialTopicId(topics: TutorialStoryboardTopic[], request?: TutorialOpenRequest, currentPage?: string) {
  if (request?.topicId && topics.some((topic) => topic.id === request.topicId)) {
    return request.topicId;
  }

  const contextTopicId = pickTopicIdForContexts(topics, request, currentPage);
  if (contextTopicId) {
    return contextTopicId;
  }

  const page = request?.page ?? currentPage;
  if (page) {
    const pageTopic = topics.find((topic) => topic.pages?.includes(page));
    if (pageTopic) return pageTopic.id;
  }

  return topics.find((topic) => topic.id === "getting-started")?.id ?? topics[0]?.id ?? "getting-started";
}

function pickTopicIdForContexts(topics: TutorialStoryboardTopic[], request?: TutorialOpenRequest, currentPage?: string) {
  const contexts = uniqueManualContexts([
    request?.context,
    ...(request?.contexts ?? []),
    currentPage ? `page:${currentPage}` : undefined,
  ]);

  for (const context of contexts) {
    const topic = topics.find((candidate) => topicMatchesContext(candidate, context));
    if (topic) return topic.id;
  }

  return undefined;
}

function topicMatchesContext(topic: TutorialStoryboardTopic, context: string) {
  const normalizedContext = normalizeManualContext(context);
  if (!normalizedContext) return false;

  if (topic.contexts?.some((topicContext) => normalizeManualContext(topicContext) === normalizedContext)) {
    return true;
  }

  if (normalizedContext.startsWith("page:")) {
    const page = normalizedContext.slice("page:".length);
    return topic.pages?.some((topicPage) => topicPage.toLowerCase() === page) ?? false;
  }

  return false;
}

function StoryboardPreview({ slide }: { slide: TutorialStoryboardSlide }) {
  if (slide.imageSrc) {
    return (
      <div className="flex h-full min-h-[34rem] items-center justify-center overflow-hidden rounded-lg border border-border bg-card shadow-xl">
        <img src={slide.imageSrc} alt={slide.imageAlt ?? slide.title} className="block max-h-full w-full object-contain bg-black" />
      </div>
    );
  }

  return (
    <div className="mx-auto h-full min-h-[34rem] max-w-5xl overflow-hidden rounded-lg border border-border bg-card shadow-xl">
      <div className="flex items-center gap-2 border-b border-border bg-nav px-3 py-2">
        <div className="h-2.5 w-2.5 rounded-full bg-red-400/80" />
        <div className="h-2.5 w-2.5 rounded-full bg-amber-300/80" />
        <div className="h-2.5 w-2.5 rounded-full bg-green-400/80" />
        <div className="ml-2 h-6 flex-1 rounded bg-background px-3 text-xs leading-6 text-muted">cove.local</div>
      </div>
      <div className="grid min-h-[calc(100%-2.5rem)] grid-cols-[10rem_minmax(0,1fr)] bg-background">
        <div className="border-r border-border bg-nav/90 p-3">
          <div className="mb-4 h-7 rounded bg-accent/25" />
          {["Scenes", "Images", "Texts", "Settings"].map((item, index) => (
            <div key={item} className={`mb-2 flex items-center gap-2 rounded px-2 py-2 text-xs ${index === 0 ? "bg-accent/20 text-accent" : "text-secondary"}`}>
              <div className="h-3 w-3 rounded bg-current opacity-60" />
              <span>{item}</span>
            </div>
          ))}
        </div>
        <div className="p-4">
          {slide.mockKind === "tasks" ? <TasksMock /> : null}
          {slide.mockKind === "feed" ? <FeedMock /> : null}
          {slide.mockKind === "metadata" ? <MetadataMock /> : null}
          {slide.mockKind === "settings" ? <SettingsMock /> : null}
          {slide.mockKind === "scenePlayer" ? <ScenePlayerMock /> : null}
          {slide.mockKind === "tagging" ? <TaggingMock /> : null}
          {slide.mockKind === "images" ? <ImagesMock /> : null}
          {slide.mockKind === "extension" || !slide.mockKind ? <ExtensionMock /> : null}
        </div>
      </div>
    </div>
  );
}

function TasksMock() {
  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div>
          <div className="text-lg font-semibold text-foreground">Tasks</div>
          <div className="text-xs text-muted">Library maintenance</div>
        </div>
        <RefreshCw className="h-5 w-5 text-accent" />
      </div>
      {["Scan library", "Generate previews", "Build hashes"].map((label, index) => (
        <div key={label} className="rounded-lg border border-border bg-card p-3">
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-3">
              <FolderOpen className="h-5 w-5 text-accent" />
              <div>
                <div className="text-sm font-medium text-foreground">{label}</div>
                <div className="text-xs text-muted">{index === 0 ? "Run first" : "Queue when ready"}</div>
              </div>
            </div>
            <div className="rounded bg-accent px-3 py-1 text-xs font-medium text-white">Run</div>
          </div>
        </div>
      ))}
    </div>
  );
}

function FeedMock() {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2 text-xs">
        {["Grid", "Feed", "Wall", "Infinite"].map((label, index) => (
          <div key={label} className={`rounded px-2.5 py-1 ${index === 1 || index === 3 ? "bg-accent text-white" : "bg-card text-secondary"}`}>{label}</div>
        ))}
      </div>
      <div className="grid gap-3 md:grid-cols-2">
        {[0, 1].map((item) => (
          <div key={item} className="overflow-hidden rounded-lg border border-border bg-card">
            <div className="aspect-video bg-gradient-to-br from-accent/70 via-cyan-500/35 to-rose-400/45" />
            <div className="space-y-2 p-3">
              <div className="h-2.5 w-3/4 rounded bg-foreground/75" />
              <div className="flex gap-1.5">
                <span className="rounded border border-border px-2 py-0.5 text-[11px] text-muted">tag</span>
                <span className="rounded border border-border px-2 py-0.5 text-[11px] text-muted">rating</span>
              </div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function MetadataMock() {
  return (
    <div className="grid gap-3 md:grid-cols-[1.1fr_0.9fr]">
      <div className="overflow-hidden rounded-lg border border-border bg-card">
        <div className="aspect-[4/3] bg-gradient-to-br from-sky-500/50 via-accent/40 to-fuchsia-400/40" />
        <div className="space-y-2 p-3">
          <div className="h-2.5 w-4/5 rounded bg-foreground/80" />
          <div className="h-2.5 w-2/3 rounded bg-foreground/35" />
        </div>
      </div>
      <div className="rounded-lg border border-border bg-card p-3">
        <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-foreground"><Database className="h-4 w-4 text-accent" /> Metadata</div>
        {["Scrape", "Identify", "Apply fields"].map((label, index) => (
          <div key={label} className={`mb-2 rounded px-3 py-2 text-sm ${index === 0 ? "bg-accent text-white" : "bg-background text-secondary"}`}>{label}</div>
        ))}
      </div>
    </div>
  );
}

function SettingsMock() {
  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2 text-lg font-semibold text-foreground"><Settings className="h-5 w-5 text-accent" /> Settings</div>
      <div className="grid gap-3 md:grid-cols-2">
        {["Navigation", "Scene Player", "Feed & Viewer", "Extensions"].map((label, index) => (
          <div key={label} className="rounded-lg border border-border bg-card p-3">
            <div className="mb-3 flex items-center gap-2 text-sm font-medium text-foreground">
              {index === 0 ? <LayoutGrid className="h-4 w-4 text-accent" /> : <Play className="h-4 w-4 text-accent" />}
              {label}
            </div>
            <div className="space-y-2">
              <div className="h-2 rounded bg-foreground/60" />
              <div className="h-2 w-2/3 rounded bg-foreground/30" />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function ScenePlayerMock() {
  return (
    <div className="space-y-3">
      <div className="overflow-hidden rounded-lg border border-border bg-card">
        <div className="flex aspect-video items-center justify-center bg-gradient-to-br from-slate-900 via-indigo-950 to-cyan-950">
          <Play className="h-14 w-14 rounded-full bg-black/40 p-3 text-white" />
        </div>
        <div className="space-y-2 p-3">
          <div className="h-2 rounded bg-accent" />
          <div className="flex justify-between text-xs text-muted"><span>04:12</span><span>38:20</span></div>
        </div>
      </div>
      <div className="grid gap-3 md:grid-cols-3">
        {["Resume", "Segments", "Details"].map((label) => <div key={label} className="rounded-lg border border-border bg-card p-3 text-sm text-secondary">{label}</div>)}
      </div>
    </div>
  );
}

function TaggingMock() {
  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2 text-lg font-semibold text-foreground"><Tag className="h-5 w-5 text-accent" /> Tagger</div>
      <div className="grid gap-2 md:grid-cols-3">
        {Array.from({ length: 9 }, (_, index) => (
          <div key={index} className="rounded-lg border border-border bg-card p-3">
            <div className="mb-3 aspect-video rounded bg-gradient-to-br from-teal-500/35 via-accent/25 to-amber-300/30" />
            <div className="h-2 rounded bg-foreground/60" />
          </div>
        ))}
      </div>
    </div>
  );
}

function ImagesMock() {
  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2 text-lg font-semibold text-foreground"><ImageIcon className="h-5 w-5 text-accent" /> Images</div>
      <div className="columns-3 gap-2">
        {[1.1, 0.75, 1.35, 0.9, 1.25, 0.8, 1.45, 1].map((ratio, index) => (
          <div key={index} className="mb-2 break-inside-avoid overflow-hidden rounded-lg border border-border bg-card">
            <div style={{ aspectRatio: `1 / ${ratio}` }} className="bg-gradient-to-br from-emerald-500/40 via-sky-400/25 to-rose-400/35" />
          </div>
        ))}
      </div>
    </div>
  );
}

function ExtensionMock() {
  return (
    <div className="flex h-full min-h-[28rem] flex-col items-center justify-center rounded-lg border border-border bg-card p-6 text-center">
      <HelpCircle className="mb-4 h-12 w-12 text-accent" />
      <div className="text-lg font-semibold text-foreground">Extension Topic</div>
      <div className="mt-2 max-w-md text-sm leading-6 text-secondary">This slide can be supplied by a Cove extension with its own screenshots, points, and page targeting.</div>
    </div>
  );
}