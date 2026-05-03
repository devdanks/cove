import type { SegmentDerivedQueryDescriptor } from "../api/types";

export interface Route {
  page: string;
  id?: number;
  seekTo?: number;
  spanKey?: string;
  profileId?: number;
  derivedQueryDescriptor?: SegmentDerivedQueryDescriptor;
}

interface RouteHistoryEntry {
  url: string;
  route: Route;
}

export const LOCATION_CHANGE_EVENT = "cove-locationchange";
const ROUTE_HISTORY_KEY = "cove-route-history";
type RouteHistoryMode = "push" | "history";

function isRouteState(value: unknown): value is Route {
  return value != null && typeof value === "object" && typeof (value as Route).page === "string";
}

function parsePath(pathname: string, search?: string): Route {
  const parts = pathname.split("/").filter(Boolean);
  if (parts.length === 0 || parts[0] === "home") {
    return applyRouteSearch({ page: "home" }, search);
  }

  if (parts[0] === "scene" && parts.length > 3 && parts[2] === "span") {
    const id = Number(parts[1]);
    if (Number.isInteger(id) && id > 0) {
      return applyRouteSearch({ page: "scene-span", id, spanKey: decodeURIComponent(parts[3]) }, search);
    }
  }

  if (parts[0] === "compilation" && parts.length > 2 && parts[2] === "play") {
    const id = Number(parts[1]);
    if (Number.isInteger(id) && id > 0) {
      return applyRouteSearch({ page: "compilation", id }, search);
    }
  }

  const page = parts[0];
  const id = parts.length > 1 ? Number(parts[1]) : undefined;
  if (id != null && Number.isInteger(id) && id > 0) {
    return applyRouteSearch({ page, id }, search);
  }

  return applyRouteSearch({ page }, search);
}

export function parseLegacyHashRoute(hash: string): Route | null {
  if (!hash.startsWith("#/")) {
    return null;
  }

  const [pathname, search = ""] = hash.slice(1).split("?");
  return parsePath(pathname, search ? `?${search}` : undefined);
}

export function parseCurrentRoute(): Route {
  return parsePath(window.location.pathname, window.location.search);
}

function readCurrentStateRoute(): Route | undefined {
  return isRouteState(window.history.state) ? window.history.state : undefined;
}

export function buildRoutePath(route: Route): string {
  if (!route.page || route.page === "home") {
    return "/";
  }

  if (route.page === "scene-span" && route.id != null && route.spanKey) {
    return `/scene/${route.id}/span/${encodeURIComponent(route.spanKey)}`;
  }

  if (route.page === "compilation" && route.id != null) {
    return `/compilation/${route.id}/play`;
  }

  if (route.id != null) {
    return `/${route.page}/${route.id}`;
  }

  return `/${route.page}`;
}

export function buildRouteUrl(route: Route): string {
  const params = new URLSearchParams();
  if (route.seekTo != null && Number.isFinite(route.seekTo) && route.seekTo >= 0) {
    params.set("t", String(route.seekTo));
  }
  if (route.profileId != null && Number.isInteger(route.profileId) && route.profileId > 0) {
    params.set("profile", String(route.profileId));
  }

  return buildCurrentUrl(buildRoutePath(route), params);
}

export function buildCurrentUrl(pathname: string, search?: URLSearchParams | string | null): string {
  if (search == null) {
    return pathname;
  }

  const searchString = search instanceof URLSearchParams ? search.toString() : search.replace(/^\?/, "");
  return searchString ? `${pathname}?${searchString}` : pathname;
}

export function emitLocationChange() {
  window.dispatchEvent(new Event(LOCATION_CHANGE_EVENT));
}

export function navigateToUrl(url: string, options?: { replace?: boolean; state?: unknown }) {
  const currentUrl = `${window.location.pathname}${window.location.search}`;
  if (currentUrl === url) {
    return;
  }

  if (options?.replace) {
    window.history.replaceState(options?.state ?? null, "", url);
  } else {
    window.history.pushState(options?.state ?? null, "", url);
  }

  emitLocationChange();
}

function readRouteHistory(): RouteHistoryEntry[] {
  try {
    const raw = sessionStorage.getItem(ROUTE_HISTORY_KEY);
    if (!raw) {
      return [];
    }

    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return [];
    }

    return parsed.filter((entry): entry is RouteHistoryEntry => {
      return entry != null && typeof entry.url === "string" && entry.route != null && typeof entry.route.page === "string";
    });
  } catch {
    return [];
  }
}

function writeRouteHistory(entries: RouteHistoryEntry[]) {
  try {
    sessionStorage.setItem(ROUTE_HISTORY_KEY, JSON.stringify(entries.slice(-30)));
  } catch {
    // Ignore session storage failures.
  }
}

export function readStoredRoute(url: string = buildCurrentUrl(window.location.pathname, window.location.search)): Route | undefined {
  const history = readRouteHistory();
  for (let index = history.length - 1; index >= 0; index -= 1) {
    if (history[index].url === url && isRouteState(history[index].route)) {
      return history[index].route;
    }
  }

  return undefined;
}

export function resolveCurrentRoute(): Route {
  return readCurrentStateRoute() ?? readStoredRoute() ?? parseCurrentRoute();
}

export function syncRouteHistory(mode: RouteHistoryMode = "push") {
  const currentEntry: RouteHistoryEntry = {
    url: buildCurrentUrl(window.location.pathname, window.location.search),
    route: readCurrentStateRoute() ?? parseCurrentRoute(),
  };

  const history = readRouteHistory();
  if (mode === "history")
  {
    for (let index = history.length - 1; index >= 0; index -= 1)
    {
      if (history[index].url === currentEntry.url)
      {
        writeRouteHistory(history.slice(0, index + 1));
        return;
      }
    }
  }

  const lastEntry = history.length > 0 ? history[history.length - 1] : undefined;
  if (lastEntry?.url === currentEntry.url) {
    return;
  }

  history.push(currentEntry);
  writeRouteHistory(history);
}

function getRouteLabel(route: Route): string {
  switch (route.page) {
    case "home": return "Home";
    case "scene": return "Scene";
    case "scene-span": return "Span";
    case "scenes": return "Scenes";
    case "segment": return "Segment";
    case "segments": return "Segments";
    case "faces": return "Faces";
    case "image": return "Image";
    case "images": return "Images";
    case "gallery": return "Gallery";
    case "galleries": return "Galleries";
    case "group": return "Group";
    case "groups": return "Groups";
    case "compilation": return "Compilation";
    case "performer": return "Performer";
    case "performers": return "Performers";
    case "studio": return "Studio";
    case "studios": return "Studios";
    case "tag": return "Tag";
    case "tags": return "Tags";
    default:
      return route.page ? route.page.charAt(0).toUpperCase() + route.page.slice(1) : "Previous Page";
  }
}

function applyRouteSearch(route: Route, search?: string): Route {
  if (!search) {
    return route;
  }

  const params = new URLSearchParams(search);
  const profileParam = params.get("profile");
  const seekParam = params.get("t");
  let nextRoute = route;

  if (profileParam != null) {
    const profileId = Number(profileParam);
    if (Number.isInteger(profileId) && profileId > 0) {
      nextRoute = {
        ...nextRoute,
        profileId,
      };
    }
  }

  if (seekParam == null) {
    return nextRoute;
  }

  const seekTo = Number(seekParam);
  if (!Number.isFinite(seekTo) || seekTo < 0) {
    return nextRoute;
  }

  return {
    ...nextRoute,
    seekTo,
  };
}

export function getPreviousInternalRoute(fallbackRoute: Route): { route: Route; label: string; hasHistory: boolean } {
  const history = readRouteHistory();
  const currentUrl = buildCurrentUrl(window.location.pathname, window.location.search);

  let currentIndex = -1;
  for (let index = history.length - 1; index >= 0; index -= 1) {
    if (history[index].url === currentUrl) {
      currentIndex = index;
      break;
    }
  }

  const previousEntry = currentIndex > 0 ? history[currentIndex - 1] : undefined;
  const route = previousEntry?.route ?? fallbackRoute;

  return {
    route,
    label: getRouteLabel(route),
    hasHistory: previousEntry != null,
  };
}