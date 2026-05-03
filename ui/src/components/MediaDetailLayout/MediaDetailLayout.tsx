import { ArrowLeft, MonitorPlay, PanelRightOpen } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { DetailSkeleton } from "../DetailSkeleton";
import { EngagementBar } from "../EngagementBar";
import { MediaDetailLayoutContent } from "./Content";
import { MediaDetailLayoutMetadata } from "./Metadata";
import { MediaDetailLayoutSidebar } from "./Sidebar";
import { useMediaDetailLayout } from "./useMediaDetailLayout";
import type { MediaDetailLayoutProps, MediaDetailTab } from "./types";

const RAIL_STORAGE_KEY = "cove.detailRail";

function useDetailRail(): [boolean, (next: boolean) => void] {
  const [rail, setRail] = useState<boolean>(() => {
    if (typeof window === "undefined") return false;
    return window.localStorage.getItem(RAIL_STORAGE_KEY) === "1";
  });
  useEffect(() => {
    const handler = (event: StorageEvent) => {
      if (event.key === RAIL_STORAGE_KEY) {
        setRail(event.newValue === "1");
      }
    };
    window.addEventListener("storage", handler);
    return () => window.removeEventListener("storage", handler);
  }, []);
  const update = (next: boolean) => {
    setRail(next);
    if (typeof window !== "undefined") {
      window.localStorage.setItem(RAIL_STORAGE_KEY, next ? "1" : "0");
    }
  };
  return [rail, update];
}

function renderTabLabel(tab: MediaDetailTab) {
  return (
    <>
      {tab.icon ? <span className="shrink-0">{tab.icon}</span> : null}
      <span>{tab.label}</span>
      {typeof tab.count === "number" ? (
        <span className="ml-1 rounded-full bg-card px-1.5 py-0.5 text-[10px] font-semibold text-muted">{tab.count}</span>
      ) : null}
    </>
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
  tabs = [],
  activeTab,
  onTabChange,
  engagement,
  actions,
  keyboardShortcuts = [],
  theaterModeSupported,
  isTheaterMode,
  onTheaterModeToggle,
  isLoading,
  error,
  children,
  headerImage,
}: MediaDetailLayoutProps) {
  const { theaterMode, setTheaterMode } = useMediaDetailLayout({
    theaterModeSupported,
    isTheaterMode,
    onTheaterModeToggle,
    keyboardShortcuts,
  });
  const [railEnabled, setRailEnabled] = useDetailRail();

  const hasTabs = tabs.length > 0;

  const tabsNav = hasTabs ? (
    <nav
      className="flex flex-wrap border-b border-border"
      aria-label="Detail tabs"
      role="tablist"
      aria-orientation="horizontal"
    >
      {tabs.map((tab) => (
        <button
          key={tab.key}
          type="button"
          role="tab"
          aria-selected={activeTab === tab.key}
          tabIndex={activeTab === tab.key ? 0 : -1}
          disabled={tab.disabled}
          onClick={() => onTabChange?.(tab.key)}
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
            if (nextTab.dataset.tabKey) onTabChange?.(nextTab.dataset.tabKey);
          }}
          data-tab-key={tab.key}
          className={[
            "inline-flex items-center gap-1.5 px-2.5 py-2 text-sm transition-colors border-b-2 cursor-pointer",
            activeTab === tab.key
              ? "border-accent text-accent"
              : "border-transparent text-secondary hover:text-foreground",
            tab.disabled ? "cursor-not-allowed opacity-50" : "",
          ].filter(Boolean).join(" ")}
        >
          {renderTabLabel(tab)}
        </button>
      ))}
    </nav>
  ) : null;

  const contentNode = isLoading ? (
    <DetailSkeleton showMedia={false} />
  ) : error ? (
    <div className="rounded-md border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-100">{error}</div>
  ) : (
    children
  );

  // Header actions: theater toggle + page-supplied actions, mounted into the
  // toolbar row at the top of the sidebar (right-aligned), not as a separate bar.
  const headerActions: ReactNode[] = [];
  if (theaterModeSupported) {
    headerActions.push(
      <button
        key="theater-toggle"
        type="button"
        aria-label="Toggle theater mode"
        aria-pressed={theaterMode}
        onClick={() => setTheaterMode(!theaterMode)}
        className="inline-flex items-center gap-1 rounded p-1 text-secondary transition hover:bg-card hover:text-foreground"
        title={theaterMode ? "Exit theater" : "Theater mode"}
      >
        <MonitorPlay className="h-4 w-4" />
      </button>,
    );
  }
  if (hasTabs) {
    headerActions.push(
      <button
        key="rail-toggle"
        type="button"
        aria-label="Toggle vertical icon rail"
        aria-pressed={railEnabled}
        onClick={() => setRailEnabled(!railEnabled)}
        className={[
          "inline-flex items-center gap-1 rounded p-1 transition",
          railEnabled ? "bg-accent/15 text-accent" : "text-secondary hover:bg-card hover:text-foreground",
        ].join(" ")}
        title={railEnabled ? "Use horizontal tabs" : "Use vertical icon rail (experimental)"}
      >
        <PanelRightOpen className="h-4 w-4" />
      </button>,
    );
  }
  if (actions) {
    headerActions.push(<div key="actions" className="flex items-center gap-1">{actions}</div>);
  }

  // Sidebar: title, back, engagement, action toolbar, tabs nav, tab content.
  // Mirrors master/stash structure: 400-450px wide, internally scrolls.
  const railWidth = railEnabled && hasTabs ? 48 : 0;
  const sidebar = (
    <div
      className={[
        "relative w-full overflow-y-auto shrink-0 border-b border-border bg-background/40",
        media
          ? "lg:w-[380px] xl:w-[400px] 2xl:w-[450px] lg:min-w-[340px] lg:max-w-[500px] lg:border-b-0 lg:border-r lg:max-h-[calc(100vh-49px)]"
          : "max-h-[calc(100vh-49px)]",
      ].join(" ")}
      data-testid="media-detail-layout-sidebar"
      style={railEnabled && hasTabs ? { paddingRight: railWidth } : undefined}
    >
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
      {hasTabs && !railEnabled ? <div className="px-6 sm:pl-7 lg:pl-8">{tabsNav}</div> : null}
      <div className="px-6 py-4 sm:pl-7 lg:pl-8">{contentNode}</div>
      {hasTabs && railEnabled ? (
        <nav
          className="absolute inset-y-0 right-0 flex w-12 flex-col items-stretch gap-0.5 border-l border-border bg-background/60 py-2"
          aria-label="Detail sections"
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
                aria-selected={isActive}
                disabled={tab.disabled}
                onClick={() => onTabChange?.(tab.key)}
                title={tab.label}
                className={[
                  "group relative mx-1 flex h-10 items-center justify-center rounded-md text-xs transition-colors",
                  isActive ? "bg-accent/15 text-accent" : "text-muted hover:bg-card hover:text-foreground",
                  tab.disabled ? "cursor-not-allowed opacity-50" : "cursor-pointer",
                ].join(" ")}
              >
                {tab.icon ? (
                  <span className="text-current">{tab.icon}</span>
                ) : (
                  <span className="font-semibold tracking-tight">{tab.label.slice(0, 2).toUpperCase()}</span>
                )}
                {typeof tab.count === "number" && tab.count > 0 ? (
                  <span className="absolute -right-0.5 -top-0.5 min-w-[1rem] rounded-full bg-card px-1 text-[9px] font-semibold text-muted shadow">{tab.count}</span>
                ) : null}
                <span className="pointer-events-none absolute right-full top-1/2 z-10 mr-1 -translate-y-1/2 whitespace-nowrap rounded bg-card px-2 py-1 text-[11px] text-foreground opacity-0 shadow transition-opacity group-hover:opacity-100">
                  {tab.label}
                </span>
              </button>
            );
          })}
        </nav>
      ) : null}
    </div>
  );

  // Right column: media fills available height. Caller's media node should
  // use `flex-1 min-h-0` on its outer container if it wants to fill vertically.
  const rightColumn = media ? (
    <div
      className="flex min-w-0 min-h-0 flex-1 flex-col overflow-hidden"
      data-testid="media-detail-layout-media"
    >
      {media}
    </div>
  ) : null;

  return (
    <div className="-mx-3 sm:-mx-4 md:-mx-6 -mt-5 -mb-5 overflow-x-hidden">
      <div
        className={
          theaterMode || !media
            ? "flex flex-col"
            : "flex flex-col h-[calc(100vh-49px)] overflow-hidden lg:flex-row"
        }
      >
        {!theaterMode ? sidebar : null}
        {rightColumn}
        {theaterMode && hasTabs ? (
          <div className="border-t border-border bg-background/40">
            <div className="px-6 pt-3">{tabsNav}</div>
            <div className="px-6 py-4">{contentNode}</div>
          </div>
        ) : null}
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