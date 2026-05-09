import { useState, useEffect, useCallback, useMemo, lazy, Suspense } from "react";
import { QueryClient, QueryClientProvider, useQuery } from "@tanstack/react-query";
import { Navbar } from "./components/Navbar";
import { KeyboardShortcutsDialog } from "./components/KeyboardShortcutsDialog";
import { ErrorBoundary } from "./components/ErrorBoundary";
import { RouteRegistryProvider, useRouteRegistry } from "./router/RouteRegistry";
import { AppConfigProvider, useAppConfig } from "./state/AppConfigContext";
import { ExtensionLoaderProvider, useExtensions } from "./extensions/ExtensionLoader";
import { SceneQueueProvider } from "./state/SceneQueueContext";
import { SetupWizardPage } from "./pages/SetupWizardPage";
import { LoginPage } from "./pages/LoginPage";
import { AuthBootstrapPage } from "./pages/AuthBootstrapPage";
import { RedeemInvitePage } from "./pages/RedeemInvitePage";
import { AuthProvider, useAuth } from "./auth/AuthContext";
import { useKeySequence } from "./hooks/useKeySequence";
import { useResolvedKeybindingOverrides } from "./hooks/useResolvedKeybindingOverrides";
import { resolveKeybinding } from "./keyboard/keybindings";
import { LOCATION_CHANGE_EVENT, Route, buildCurrentUrl, buildRoutePath, buildRouteUrl, navigateToUrl, parseCurrentRoute, parseLegacyHashRoute, readStoredRoute, resolveCurrentRoute, syncRouteHistory } from "./router/location";

function normalizeRoute(route: Route): Route {
  if (route.page === "markers") {
    return route.id != null ? { page: "segment", id: route.id } : { page: "segments" };
  }

  return route;
}

const BUILTIN_ROUTE_PERMISSIONS: Partial<Record<Route["page"], string>> = {
  scenes: "scenes.read",
  scene: "scenes.read",
  "scene-span": "segments.read",
  segments: "segments.read",
  segment: "segments.read",
  face: "faces.read",
  performers: "performers.read",
  performer: "performers.read",
  studios: "studios.read",
  studio: "studios.read",
  tags: "tags.read",
  tag: "tags.read",
  galleries: "galleries.read",
  gallery: "galleries.read",
  groups: "groups.read",
  group: "groups.read",
  compilation: "groups.read",
  images: "images.read",
  image: "images.read",
  faces: "faces.read",
  sceneparser: "scenes.read",
  logs: "system.read",
  duplicates: "scenes.read",
  stats: "system.read",
};

// Lazy-loaded page components for code splitting
const ScenesPage = lazy(() => import("./pages/ScenesPage").then(m => ({ default: m.ScenesPage })));
const SegmentsPage = lazy(() => import("./pages/SegmentsPage").then(m => ({ default: m.SegmentsPage })));
const PerformersPage = lazy(() => import("./pages/PerformersPage").then(m => ({ default: m.PerformersPage })));
const StudiosPage = lazy(() => import("./pages/StudiosPage").then(m => ({ default: m.StudiosPage })));
const TagsPage = lazy(() => import("./pages/TagsPage").then(m => ({ default: m.TagsPage })));
const GalleriesPage = lazy(() => import("./pages/GalleriesPage").then(m => ({ default: m.GalleriesPage })));
const GroupsPage = lazy(() => import("./pages/GroupsPage").then(m => ({ default: m.GroupsPage })));
const ImagesPage = lazy(() => import("./pages/ImagesPage").then(m => ({ default: m.ImagesPage })));
const SettingsPage = lazy(() => import("./pages/SettingsPage").then(m => ({ default: m.SettingsPage })));
const StatsPage = lazy(() => import("./pages/StatsPage").then(m => ({ default: m.StatsPage })));
const SceneDetailPage = lazy(() => import("./pages/SceneDetailPage").then(m => ({ default: m.SceneDetailPage })));
const SegmentDetailPage = lazy(() => import("./pages/SegmentDetailPage").then(m => ({ default: m.SegmentDetailPage })));
const ResolvedSpanPlayPage = lazy(() => import("./pages/ResolvedSpanPlayPage").then(m => ({ default: m.ResolvedSpanPlayPage })));
const PerformerDetailPage = lazy(() => import("./pages/PerformerDetailPage").then(m => ({ default: m.PerformerDetailPage })));
const StudioDetailPage = lazy(() => import("./pages/StudioDetailPage").then(m => ({ default: m.StudioDetailPage })));
const TagDetailPage = lazy(() => import("./pages/TagDetailPage").then(m => ({ default: m.TagDetailPage })));
const GalleryDetailPage = lazy(() => import("./pages/GalleryDetailPage").then(m => ({ default: m.GalleryDetailPage })));
const GroupDetailPage = lazy(() => import("./pages/GroupDetailPage").then(m => ({ default: m.GroupDetailPage })));
const CompilationPlayerPage = lazy(() => import("./pages/CompilationPlayerPage").then(m => ({ default: m.CompilationPlayerPage })));
const ImageDetailPage = lazy(() => import("./pages/ImageDetailPage").then(m => ({ default: m.ImageDetailPage })));
const FacesPage = lazy(() => import("./pages/FacesPage").then(m => ({ default: m.FacesPage })));
const FaceDetailPage = lazy(() => import("./pages/FaceDetailPage").then(m => ({ default: m.FaceDetailPage })));
const LogsPage = lazy(() => import("./pages/LogsPage").then(m => ({ default: m.LogsPage })));
const DuplicateFinderPage = lazy(() => import("./pages/DuplicateFinderPage").then(m => ({ default: m.DuplicateFinderPage })));

const SceneFilenameParserPage = lazy(() => import("./pages/SceneFilenameParserPage").then(m => ({ default: m.SceneFilenameParserPage })));
const HomePage = lazy(() => import("./pages/HomePage").then(m => ({ default: m.HomePage })));

export default function App() {
  const [route, setRoute] = useState<Route>(() => {
    const legacyRoute = parseLegacyHashRoute(window.location.hash);
    return normalizeRoute(legacyRoute ?? resolveCurrentRoute());
  });

  useEffect(() => {
    const legacyRoute = parseLegacyHashRoute(window.location.hash);
    if (legacyRoute) {
      const normalizedLegacyRoute = normalizeRoute(legacyRoute);
      navigateToUrl(buildCurrentUrl(buildRoutePath(normalizedLegacyRoute), window.location.search), { replace: true, state: normalizedLegacyRoute });
      setRoute(normalizedLegacyRoute);
    } else {
      const currentRoute = resolveCurrentRoute();
      const normalizedCurrentRoute = normalizeRoute(currentRoute);
      if (normalizedCurrentRoute.page !== currentRoute.page || normalizedCurrentRoute.id !== currentRoute.id) {
        navigateToUrl(buildCurrentUrl(buildRoutePath(normalizedCurrentRoute), window.location.search), { replace: true, state: normalizedCurrentRoute });
        setRoute(normalizedCurrentRoute);
      }
    }
    // Redirect /home to / (canonical home URL)
    if (window.location.pathname === "/home") {
      navigateToUrl(buildCurrentUrl("/", window.location.search), { replace: true });
    }

    syncRouteHistory("push");
  }, []);

  useEffect(() => {
    const handleLocationChange = (event: Event) => {
      syncRouteHistory(event.type === "popstate" ? "history" : "push");
      const currentUrl = buildCurrentUrl(window.location.pathname, window.location.search);
      // Recover route from history.state first, then from session-scoped route history.
      // This keeps derived-query provenance available even if a navigation path only preserved the URL.
      const rawState = event instanceof PopStateEvent ? event.state : window.history.state;
      const stateRoute = rawState && typeof rawState === "object" && typeof (rawState as Route).page === "string"
        ? rawState as Route
        : undefined;
      setRoute(normalizeRoute(stateRoute ?? readStoredRoute(currentUrl) ?? parseCurrentRoute()));
    };
    window.addEventListener("popstate", handleLocationChange);
    window.addEventListener(LOCATION_CHANGE_EVENT, handleLocationChange);
    return () => {
      window.removeEventListener("popstate", handleLocationChange);
      window.removeEventListener(LOCATION_CHANGE_EVENT, handleLocationChange);
    };
  }, []);

  const navigate = useCallback((r: Route) => {
    const currentUrl = buildCurrentUrl(window.location.pathname, window.location.search);
    const nextUrl = buildRouteUrl(r);
    if (currentUrl === nextUrl) {
      window.dispatchEvent(new CustomEvent("cove-page-reset", { detail: r.page }));
    } else {
      // Store the full route (including non-URL-serializable fields) in history.state
      // so the location change handler can recover it without URL round-tripping.
      navigateToUrl(nextUrl, { state: r });
      setRoute(r);
    }
  }, []);

  // Keyboard shortcut: "/" focuses search
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === "/" && !["INPUT", "TEXTAREA", "SELECT"].includes((e.target as HTMLElement)?.tagName)) {
        e.preventDefault();
        const searchInput = document.querySelector<HTMLInputElement>("input[placeholder='Filter...']")
          ?? document.querySelector<HTMLInputElement>("input[placeholder='Search all...']");
        searchInput?.focus();
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, []);

  const [showShortcuts, setShowShortcuts] = useState(false);

  return (
    <RouteRegistryProvider>
      <AppConfigProvider>
        <AuthGate>
          <ExtensionLoaderProvider>
            <SceneQueueProvider>
              <AppKeyboardShortcuts navigate={navigate} onShowShortcuts={() => setShowShortcuts(true)} />
              <AppShell route={route} navigate={navigate} />
              <KeyboardShortcutsDialog open={showShortcuts} onClose={() => setShowShortcuts(false)} />
            </SceneQueueProvider>
          </ExtensionLoaderProvider>
        </AuthGate>
      </AppConfigProvider>
    </RouteRegistryProvider>
  );
}

function AppKeyboardShortcuts({ navigate, onShowShortcuts }: { navigate: (route: Route) => void; onShowShortcuts: () => void }) {
  const overrides = useResolvedKeybindingOverrides();

  const globalBindings = useMemo(() => [
    { keys: resolveKeybinding(overrides, "global.home", "g h"), action: () => navigate({ page: "home" }) },
    { keys: resolveKeybinding(overrides, "global.scenes", "g s"), action: () => navigate({ page: "scenes" }) },
    { keys: resolveKeybinding(overrides, "global.segments", "g m"), action: () => navigate({ page: "segments" }) },
    { keys: resolveKeybinding(overrides, "global.faces", "g f"), action: () => navigate({ page: "faces" }) },
    { keys: resolveKeybinding(overrides, "global.images", "g i"), action: () => navigate({ page: "images" }) },
    { keys: resolveKeybinding(overrides, "global.groups", "g v"), action: () => navigate({ page: "groups" }) },
    { keys: resolveKeybinding(overrides, "global.galleries", "g l"), action: () => navigate({ page: "galleries" }) },
    { keys: resolveKeybinding(overrides, "global.performers", "g p"), action: () => navigate({ page: "performers" }) },
    { keys: resolveKeybinding(overrides, "global.studios", "g u"), action: () => navigate({ page: "studios" }) },
    { keys: resolveKeybinding(overrides, "global.tags", "g t"), action: () => navigate({ page: "tags" }) },
    { keys: resolveKeybinding(overrides, "global.settings", "g z"), action: () => navigate({ page: "settings" }) },
    { keys: resolveKeybinding(overrides, "global.stats", "g d"), action: () => navigate({ page: "stats" }) },
    { keys: resolveKeybinding(overrides, "global.shortcuts", "?"), action: onShowShortcuts },
  ], [navigate, onShowShortcuts, overrides]);

  useKeySequence(globalBindings);
  return null;
}

/**
 * Wraps the app with AuthProvider once we know whether auth is enabled (from /api/system/status),
 * and renders the LoginPage when auth is required but the user is not yet signed in.
 */
function AuthGate({ children }: { children: React.ReactNode }) {
  const { status, statusLoading } = useAppConfig();
  const authEnabled = !!status?.authEnabled;

  if (statusLoading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-accent" />
      </div>
    );
  }

  return (
    <AuthProvider authEnabled={authEnabled}>
      <AuthGateInner>{children}</AuthGateInner>
    </AuthProvider>
  );
}

function getPostLoginRedirectUrl(): string {
  const redirect = new URLSearchParams(window.location.search).get("redirect");
  if (!redirect || !redirect.startsWith("/") || redirect.startsWith("//")) {
    return "/";
  }

  try {
    const url = new URL(redirect, window.location.origin);
    if (url.origin !== window.location.origin || url.pathname === "/login") {
      return "/";
    }

    return `${url.pathname}${url.search}${url.hash}`;
  } catch {
    return "/";
  }
}

function AuthGateInner({ children }: { children: React.ReactNode }) {
  const { authEnabled, user, loading } = useAuth();

  useEffect(() => {
    if (!authEnabled || !user || window.location.pathname !== "/login") {
      return;
    }

    navigateToUrl(getPostLoginRedirectUrl(), { replace: true });
  }, [authEnabled, user]);

  if (window.location.pathname === "/auth/bootstrap") {
    return <AuthBootstrapPage />;
  }
  if (window.location.pathname === "/auth/redeem-invite") {
    return <RedeemInvitePage />;
  }
  if (authEnabled && loading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-accent" />
      </div>
    );
  }
  if (authEnabled && !user) {
    return <LoginPage />;
  }
  return <>{children}</>;
}

function AppShell({ route, navigate }: { route: Route; navigate: (r: Route) => void }) {
  const { config, configLoading, status, statusLoading } = useAppConfig();
  const [setupDismissed, setSetupDismissed] = useState(() => sessionStorage.getItem("cove-setup-dismissed") === "true");

  // Show setup wizard if config has no library paths and user hasn't dismissed it
  const needsSetup = config && config.covePaths.filter(p => p.path.trim() !== "").length === 0 && !setupDismissed;

  if (configLoading || statusLoading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-accent" />
      </div>
    );
  }

  // Migration gate: block the app until migrations are applied (they run on next restart)
  if (status?.migrationRequired) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center">
        <div className="max-w-md text-center space-y-4 p-8">
          <div className="text-4xl">⚙️</div>
          <h1 className="text-xl font-semibold text-foreground">Database Update Required</h1>
          <p className="text-sm text-muted-foreground">
            Cove needs to update the database schema. This will happen automatically — please restart the server.
          </p>
          {status.pendingMigrations && (
            <div className="text-xs text-muted-foreground bg-surface rounded p-3 text-left">
              <div className="font-medium mb-1">Pending migrations:</div>
              {status.pendingMigrations.map(m => (
                <div key={m} className="font-mono">{m}</div>
              ))}
            </div>
          )}
          <p className="text-xs text-muted-foreground">
            A backup will be created automatically before applying changes.
          </p>
        </div>
      </div>
    );
  }

  if (needsSetup && config) {
    return (
      <SetupWizardPage
        config={config}
        onComplete={() => {
          setSetupDismissed(true);
          sessionStorage.setItem("cove-setup-dismissed", "true");
        }}
      />
    );
  }

  return (
    <div className="min-h-screen bg-background text-foreground">
      <Navbar currentPage={route.page} navigate={navigate} />
      <main className="w-full px-3 sm:px-4 md:px-6 py-3 sm:py-5">
        <ErrorBoundary>
          <Suspense fallback={<div className="flex items-center justify-center h-64"><div className="animate-spin rounded-full h-8 w-8 border-b-2 border-accent"></div></div>}>
            <AppRoutes route={route} navigate={navigate} />
          </Suspense>
        </ErrorBoundary>
      </main>
    </div>
  );
}

function AppRoutes({ route, navigate }: { route: Route; navigate: (r: Route) => void }) {
  const { routes } = useRouteRegistry();
  const { getPageOverride, resolveComponent, manifest } = useExtensions();
  const { hasPermission } = useAuth();

  const requiredPermission = BUILTIN_ROUTE_PERMISSIONS[route.page];
  if (requiredPermission && !hasPermission(requiredPermission)) {
    return <AccessDeniedPage navigate={navigate} />;
  }

  // 1. Check for page overrides (extension replaces a built-in page)
  const override = getPageOverride(route.page);
  if (override) {
    const Component = resolveComponent(override.componentName);
    if (Component) {
      return <Component onNavigate={navigate} />;
    }
  }

  // 2. Check extension-contributed pages (new pages via UIPageDefinition)
  const extPage = manifest?.pages.find((p) => p.route === route.page);
  if (extPage?.componentName) {
    const Component = resolveComponent(extPage.componentName);
    if (Component) {
      // Pass id if this is a detail page route
      const props: Record<string, unknown> = { onNavigate: navigate };
      if ("id" in route && route.id !== undefined) {
        props.id = route.id;
      }
      return <Component {...props} />;
    }
  }

  // 3. Check route registry (legacy extension routes)
  const extRoute = routes.find((r) => r.page === route.page);
  if (extRoute?.component) {
    const Comp = extRoute.component;
    return <Comp onNavigate={navigate} />;
  }
  if ("id" in route && route.id !== undefined) {
    const extDetail = routes.find((r) => r.page === route.page);
    if (extDetail?.detailComponent) {
      const Comp = extDetail.detailComponent;
      return <Comp id={(route as any).id} onNavigate={navigate} />;
    }
  }

  // 4. Built-in pages
  return (
    <>
      {route.page === "home" && <HomePage onNavigate={navigate} />}
      {route.page === "scenes" && <ScenesPage onNavigate={navigate} />}
      {route.page === "scene" && route.id !== undefined && <SceneDetailPage id={route.id} initialSeekTo={route.seekTo} onNavigate={navigate} />}
      {route.page === "scene-span" && route.id !== undefined && route.spanKey !== undefined && (
        <ResolvedSpanPlayPage sceneId={route.id} spanKey={route.spanKey} profileId={route.profileId} derivedQueryDescriptor={route.derivedQueryDescriptor} onNavigate={navigate} />
      )}
      {route.page === "segments" && <SegmentsPage onNavigate={navigate} />}
      {route.page === "segment" && route.id !== undefined && <SegmentDetailPage id={route.id} onNavigate={navigate} />}
      {route.page === "faces" && <FacesPage onNavigate={navigate} />}
      {route.page === "face" && route.id !== undefined && <FaceDetailPage id={route.id} onNavigate={navigate} />}
      {route.page === "performers" && <PerformersPage onNavigate={navigate} />}
      {route.page === "performer" && route.id !== undefined && <PerformerDetailPage id={route.id} onNavigate={navigate} />}
      {route.page === "studios" && <StudiosPage onNavigate={navigate} />}
      {route.page === "studio" && route.id !== undefined && <StudioDetailPage id={route.id} onNavigate={navigate} />}
      {route.page === "tags" && <TagsPage onNavigate={navigate} />}
      {route.page === "tag" && route.id !== undefined && <TagDetailPage id={route.id} onNavigate={navigate} />}
      {route.page === "galleries" && <GalleriesPage onNavigate={navigate} />}
      {route.page === "gallery" && route.id !== undefined && <GalleryDetailPage id={route.id} onNavigate={navigate} />}
      {route.page === "groups" && <GroupsPage onNavigate={navigate} />}
      {route.page === "group" && route.id !== undefined && <GroupDetailPage id={route.id} onNavigate={navigate} />}
      {route.page === "compilation" && route.id !== undefined && <CompilationPlayerPage id={route.id} onNavigate={navigate} />}
      {route.page === "images" && <ImagesPage onNavigate={navigate} />}
      {route.page === "image" && route.id !== undefined && <ImageDetailPage id={route.id} onNavigate={navigate} />}
      {route.page === "settings" && <SettingsPage />}
      {route.page === "stats" && <StatsPage onNavigate={navigate} />}
      {route.page === "logs" && <LogsPage />}
      {route.page === "duplicates" && <DuplicateFinderPage onNavigate={navigate} />}
      {route.page === "sceneparser" && <SceneFilenameParserPage onNavigate={navigate} />}
    </>
  );
}

function AccessDeniedPage({ navigate }: { navigate: (r: Route) => void }) {
  return (
    <div className="mx-auto flex min-h-[40vh] max-w-xl flex-col items-center justify-center gap-4 text-center">
      <h1 className="text-2xl font-semibold text-foreground">Access denied</h1>
      <p className="text-sm text-secondary">Your account does not have permission to view this page.</p>
      <button
        onClick={() => navigate({ page: "home" })}
        className="rounded-lg border border-border px-4 py-2 text-sm font-medium text-foreground hover:border-accent hover:text-accent"
      >
        Go home
      </button>
    </div>
  );
}
