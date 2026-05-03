import { beforeEach, describe, expect, it } from "vitest";
import { buildRouteUrl, getPreviousInternalRoute, parseCurrentRoute, syncRouteHistory } from "../router/location";

const sessionEntries = new Map<string, string>();

beforeEach(() => {
  sessionEntries.clear();
  Object.defineProperty(window, "sessionStorage", {
    configurable: true,
    value: {
      getItem: (key: string) => sessionEntries.get(key) ?? null,
      setItem: (key: string, value: string) => {
        sessionEntries.set(key, value);
      },
      removeItem: (key: string) => {
        sessionEntries.delete(key);
      },
    },
  });

  window.history.replaceState(null, "", "/");
});

describe("route history", () => {
  it("parses and rebuilds scene seek timestamps", () => {
    window.history.replaceState(null, "", "/scene/42?t=91.5");

    expect(parseCurrentRoute()).toEqual({
      page: "scene",
      id: 42,
      seekTo: 91.5,
    });

    expect(buildRouteUrl({ page: "scene", id: 42, seekTo: 91.5 })).toBe("/scene/42?t=91.5");
  });

  it("keeps back labels aligned with browser back navigation after a popstate-style move", () => {
    window.history.replaceState(null, "", "/performers");
    syncRouteHistory("push");

    window.history.pushState(null, "", "/performer/1");
    syncRouteHistory("push");

    window.history.pushState(null, "", "/scene/2");
    syncRouteHistory("push");

    window.history.replaceState(null, "", "/performer/1");
    syncRouteHistory("history");

    expect(getPreviousInternalRoute({ page: "performers" })).toEqual(
      expect.objectContaining({
        route: { page: "performers" },
        label: "Performers",
        hasHistory: true,
      }),
    );
  });
});