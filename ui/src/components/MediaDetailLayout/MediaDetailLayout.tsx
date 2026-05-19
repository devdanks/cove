import {
  ArrowLeft,
  Building2,
  ChevronLeft,
  ChevronRight,
  FileText,
  History,
  Image as ImageIcon,
  Images,
  Info,
  Layers,
  ListVideo,
  Pencil,
  Puzzle,
  SlidersHorizontal,
  Tags,
  UserRound,
  Users,
} from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { DetailSkeleton } from "../DetailSkeleton";
import { EngagementBar } from "../EngagementBar";
import { MediaDetailLayoutContent } from "./Content";
import { MediaDetailLayoutMetadata } from "./Metadata";
import { MediaDetailLayoutSidebar } from "./Sidebar";
import { useMediaDetailLayout } from "./useMediaDetailLayout";
import type { MediaDetailLayoutProps, MediaDetailTab } from "./types";

const SIDEBAR_COLLAPSED_STORAGE_KEY = "cove.detailSidebarCollapsed";

function useSidebarCollapsed(): [boolean, (next: boolean) => void] {
  const [collapsed, setCollapsed] = useState<boolean>(() => {
    if (typeof window === "undefined") return false;
    try {
      return typeof window.localStorage?.getItem === "function"
        ? window.localStorage.getItem(SIDEBAR_COLLAPSED_STORAGE_KEY) === "1"
        : false;
    } catch {
      return false;
    }
  });
  useEffect(() => {
    const handler = (event: StorageEvent) => {
      if (event.key === SIDEBAR_COLLAPSED_STORAGE_KEY) {
        setCollapsed(event.newValue === "1");
      }
    };
    window.addEventListener("storage", handler);
    return () => window.removeEventListener("storage", handler);
  }, []);
  const update = (next: boolean) => {
    setCollapsed(next);
    if (typeof window !== "undefined") {
      try {
        if (typeof window.localStorage?.setItem === "function") {
          window.localStorage.setItem(SIDEBAR_COLLAPSED_STORAGE_KEY, next ? "1" : "0");
        }
      } catch { /* ignore */ }
    }
  };
  return [collapsed, update];
}

function getDefaultTabIcon(tab: MediaDetailTab) {
  const iconClassName = "h-4 w-4";
  const key = tab.key.startsWith("ext:") ? "ext" : tab.key;
  const label = tab.label.toLowerCase();
  if (key === "details" || key === "overview" || label.includes("detail") || label.includes("overview")) return <Info className={iconClassName} />;
  if (key === "segments" || label.includes("segment")) return <ListVideo className={iconClassName} />;
  if (key === "groups" || label.includes("group")) return <Layers className={iconClassName} />;
  if (key === "galleries" || label.includes("galler")) return <Images className={iconClassName} />;
  if (key === "images" || label.includes("image")) return <ImageIcon className={iconClassName} />;
  if (key === "filters" || label.includes("filter")) return <SlidersHorizontal className={iconClassName} />;
  if (key === "file-info" || label.includes("file")) return <FileText className={iconClassName} />;
  if (key === "history" || label.includes("history")) return <History className={iconClassName} />;
  if (key === "edit" || label.includes("edit")) return <Pencil className={iconClassName} />;
  if (key === "performers" || label.includes("performer")) return <UserRound className={iconClassName} />;
  if (key === "studios" || label.includes("studio")) return <Building2 className={iconClassName} />;
  if (key === "tags" || label.includes("tag")) return <Tags className={iconClassName} />;
  if (key === "scenes" || label.includes("scene")) return <ListVideo className={iconClassName} />;
  if (key === "related" || label.includes("related")) return <Layers className={iconClassName} />;
  if (key === "people" || label.includes("people")) return <Users className={iconClassName} />;
  if (key === "ext") return <Puzzle className={iconClassName} />;
  return null;
}

function getTabInitials(label: string) {
  const initials = label
    .replace(/\(.+?\)/g, "")
    .split(/\s+/)
    .filter(Boolean)
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
  return initials || "--";
}

function renderTabIcon(tab: MediaDetailTab) {
  if (tab.icon) {
    return <span className="shrink-0 text-current [&>svg]:h-4 [&>svg]:w-4">{tab.icon}</span>;
  }
  const icon = getDefaultTabIcon(tab);
  return icon ? (
    <span className="shrink-0 text-current" aria-hidden="true">{icon}</span>
  ) : (
    <span className="shrink-0 text-[10px] font-semibold uppercase leading-none tracking-normal" aria-hidden="true">
      {getTabInitials(tab.label)}
    </span>
  );
}

function moveTabFocus(currentTab: HTMLButtonElement, direction: "next" | "previous" | "first" | "last") {
  const tabList = currentTab.closest('[role="tablist"]');
  if (!tabList) return null;
  const enabledTabs = Array.from(tabList.querySelectorAll<HTMLButtonElement>('[role="tab"]')).filter((tab) => !tab.disabled);
  if (enabledTabs.length === 0) return null;
  const currentIndex = enabledTabs.indexOf(currentTab);
  if (currentIndex < 0) return null;
  switch (direction) {
    case "first": return enabledTabs[0] ?? null;
    case "last": return enabledTabs[enabledTabs.length - 1] ?? null;
    case "previous": return enabledTabs[(currentIndex - 1 + enabledTabs.length) % enabledTabs.length] ?? null;
    case "next":
    default: return enabledTabs[(currentIndex + 1) % enabledTabs.length] ?? null;
  }
}

function MediaDetailLayoutRoot({
  title,
  subtitle,
  backLabel = "Back",
  onGoBack,
  media,
  mediaAspectRatio,
  mediaFullBleed,
  mediaSticky = false,
  tabs = [],
  activeTab,
  onTabChange,
  engagement,
  actions,
  keyboardShortcuts = [],
  isLoading,
  error,
  children,
  headerImage,
}: MediaDetailLayoutProps) {
  useMediaDetailLayout({ keyboardShortcuts });
  const [sidebarCollapsed, setSidebarCollapsed] = useSidebarCollapsed();

  const hasTabs = tabs.length > 0;
  const canCollapseSidebar = Boolean(media);

  const handleTabChange = (key: string) => {
    onTabChange?.(key);
    if (canCollapseSidebar && sidebarCollapsed) {
      setSidebarCollapsed(false);
    }
  };

  const tabsNav = hasTabs ? (
    <div className="flex w-11 shrink-0 flex-col border-r border-border bg-background/70 py-2">
      {canCollapseSidebar ? (
        <>
          <button
            type="button"
            aria-label={sidebarCollapsed ? "Expand details sidebar" : "Collapse details sidebar"}
            aria-pressed={sidebarCollapsed}
            onClick={() => setSidebarCollapsed(!sidebarCollapsed)}
            className="group relative mx-1 hidden h-8 items-center justify-center rounded-md text-secondary transition hover:bg-card hover:text-foreground lg:flex"
            title={sidebarCollapsed ? "Expand details sidebar" : "Collapse details sidebar"}
            data-testid="media-detail-layout-sidebar-toggle"
          >
            {sidebarCollapsed ? <ChevronRight className="h-4 w-4" /> : <ChevronLeft className="h-4 w-4" />}
            <span className="pointer-events-none absolute left-full top-1/2 z-20 ml-2 -translate-y-1/2 whitespace-nowrap rounded bg-card px-2 py-1 text-[11px] text-foreground opacity-0 shadow transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100">
              {sidebarCollapsed ? "Expand sidebar" : "Collapse sidebar"}
            </span>
          </button>
          <div className="mx-2 my-1 hidden h-px bg-border/80 lg:block" />
        </>
      ) : null}
      <nav
        className="flex min-h-0 flex-1 flex-col"
        aria-label="Detail tabs"
        role="tablist"
        aria-orientation="vertical"
      >
        {tabs.map((tab) => {
          const isActive = activeTab === tab.key;
          return (
          <button
            key={tab.key}
            type="button"
            role="tab"
            aria-label={tab.label}
            aria-selected={isActive}
            tabIndex={isActive ? 0 : -1}
            disabled={tab.disabled}
            onClick={() => handleTabChange(tab.key)}
            title={tab.label}
            onKeyDown={(event) => {
              let nextTab: HTMLButtonElement | null = null;
              switch (event.key) {
                case "ArrowRight":
                case "ArrowDown":
                  nextTab = moveTabFocus(event.currentTarget, "next"); break;
                case "ArrowLeft":
                case "ArrowUp":
                  nextTab = moveTabFocus(event.currentTarget, "previous"); break;
                case "Home":
                  nextTab = moveTabFocus(event.currentTarget, "first"); break;
                case "End":
                  nextTab = moveTabFocus(event.currentTarget, "last"); break;
                default: return;
              }
              if (!nextTab) return;
              event.preventDefault();
              nextTab.focus();
              if (nextTab.dataset.tabKey) handleTabChange(nextTab.dataset.tabKey);
            }}
            data-tab-key={tab.key}
            className={[
              "group relative mx-1 flex h-8 items-center justify-center rounded-md px-0 text-xs transition-colors",
              isActive
                ? "bg-accent/15 text-accent"
                : "text-secondary hover:bg-card hover:text-foreground",
              tab.disabled ? "cursor-not-allowed opacity-50" : "",
            ].filter(Boolean).join(" ")}
          >
            {renderTabIcon(tab)}
            {typeof tab.count === "number" ? (
              <span
                className="absolute -right-0.5 -top-0.5 min-w-[1rem] rounded-full bg-card px-1 py-0.5 text-center text-[9px] font-semibold text-muted shadow-sm"
              >
                {tab.count}
              </span>
            ) : null}
            <span className="pointer-events-none absolute left-full top-1/2 z-20 ml-2 -translate-y-1/2 whitespace-nowrap rounded bg-card px-2 py-1 text-[11px] text-foreground opacity-0 shadow transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100">
              {tab.label}
            </span>
          </button>
          );
        })}
      </nav>
    </div>
  ) : null;

  const contentNode = isLoading ? (
    <DetailSkeleton showMedia={false} />
  ) : error ? (
    <div className="rounded-md border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-100">{error}</div>
  ) : (
    children
  );

  // Header actions are mounted into the toolbar row at the top of the sidebar.
  const headerActions: ReactNode[] = [];
  if (actions) {
    headerActions.push(<div key="actions" className="flex items-center gap-1">{actions}</div>);
  }

  const isCompactMedia = mediaAspectRatio === "compact";
  const hasFramedMedia = Boolean(media && !mediaFullBleed && (mediaAspectRatio === "video" || mediaAspectRatio === "square"));
  const framedMediaClassName = mediaAspectRatio === "square"
    ? "aspect-square lg:w-[min(100%,calc(100vh-81px))]"
    : "aspect-video lg:w-[min(100%,calc((100vh-81px)*16/9))]";

  // Sidebar: title, back, engagement, action toolbar, vertical tab rail, tab content.
  // Shared detail sidebar: roughly 20% wider than the original desktop widths,
  // internally scrolls, and can collapse to the icon rail on desktop.
  const sidebar = (
    <div
      className={[
        "relative w-full shrink-0 border-b border-border bg-background/40 transition-[width] duration-150",
        media ? (
          sidebarCollapsed
            ? "lg:w-11 lg:min-w-11 lg:max-w-11 lg:border-b-0 lg:border-r lg:max-h-[calc(100vh-49px)]"
            : "lg:w-[456px] xl:w-[480px] 2xl:w-[540px] lg:min-w-[408px] lg:max-w-[600px] lg:border-b-0 lg:border-r lg:max-h-[calc(100vh-49px)]"
        ) : "max-h-[calc(100vh-49px)]",
      ].join(" ")}
      data-testid="media-detail-layout-sidebar"
      data-sidebar-collapsed={sidebarCollapsed ? "true" : "false"}
    >
      <div className={["flex min-h-0 overflow-hidden", media ? "h-full" : "max-h-[calc(100vh-49px)]"].join(" ")}>
        {tabsNav}
        <div className={["min-w-0 flex-1 overflow-y-auto", sidebarCollapsed ? "lg:hidden" : ""].join(" ")}>
          <div className="px-6 pt-4 pb-2 sm:pl-7 lg:pl-8">
            {onGoBack ? (
              <button
                type="button"
                onClick={onGoBack}
                className="mb-3 flex items-center gap-1 text-sm text-secondary transition hover:text-foreground"
              >
                <ArrowLeft className="h-4 w-4" /> {backLabel}
              </button>
            ) : null}
            {headerImage ? <div className="mb-2">{headerImage}</div> : null}
            <h3 className="mt-1 break-words text-[1.5rem] font-semibold leading-snug text-foreground">{title}</h3>
            {subtitle ? <div className="mt-1 text-sm text-secondary">{subtitle}</div> : null}
            {engagement || headerActions.length > 0 ? (
              <div className="mt-3 flex items-center justify-between gap-2">
                <div className="min-w-0 flex-1">
                  {engagement ? <EngagementBar {...engagement} className="" /> : null}
                </div>
                {headerActions.length > 0 ? (
                  <div className="flex shrink-0 items-center gap-1">{headerActions}</div>
                ) : null}
              </div>
            ) : null}
          </div>
          <div className="px-6 py-4 sm:pl-7 lg:pl-8">{contentNode}</div>
        </div>
      </div>
    </div>
  );

  // Right column: media fills available height. Caller's media node should
  // use `flex-1 min-h-0` on its outer container if it wants to fill vertically.
  const rightColumn = media ? (
    <div
      className={[
        "flex min-w-0 min-h-0 flex-1 flex-col",
        isCompactMedia ? "overflow-visible p-4 sm:p-6 lg:p-8" : "overflow-hidden",
        hasFramedMedia ? "items-center justify-center bg-black/95 p-3 sm:p-4" : "",
        isCompactMedia ? "" : mediaAspectRatio === "square" ? "min-h-[70vw]" : "min-h-[45vh]",
        mediaFullBleed ? "bg-black" : "",
        "lg:min-h-0",
        mediaSticky ? "xl:sticky xl:top-0 xl:self-start" : "",
      ].filter(Boolean).join(" ")}
      data-testid="media-detail-layout-media"
    >
      {hasFramedMedia ? (
        <div
          className={[
            "flex h-auto max-h-full w-full max-w-full min-w-0 overflow-hidden bg-black",
            framedMediaClassName,
          ].join(" ")}
          data-testid="media-detail-layout-media-frame"
        >
          {media}
        </div>
      ) : media}
    </div>
  ) : null;

  return (
    <div className="-mx-3 sm:-mx-4 md:-mx-6 -mt-5 -mb-5 overflow-x-hidden">
      <div
        className={
          !media
            ? "flex flex-col"
            : isCompactMedia
              ? "flex flex-col lg:flex-row"
            : "flex flex-col lg:h-[calc(100vh-49px)] lg:overflow-hidden lg:flex-row"
        }
      >
        {sidebar}
        {rightColumn}
      </div>
    </div>
  );
}

type MediaDetailLayoutComponent = typeof MediaDetailLayoutRoot & {
  Content: typeof MediaDetailLayoutContent;
  Metadata: typeof MediaDetailLayoutMetadata;
  Sidebar: typeof MediaDetailLayoutSidebar;
};

export const MediaDetailLayout = MediaDetailLayoutRoot as MediaDetailLayoutComponent;
MediaDetailLayout.Content = MediaDetailLayoutContent;
MediaDetailLayout.Metadata = MediaDetailLayoutMetadata;
MediaDetailLayout.Sidebar = MediaDetailLayoutSidebar;