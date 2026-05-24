import { describe, expect, it } from "vitest";
import { mergeTaskSelectablePaths, resolveVisibleSettingsTab } from "../pages/SettingsPage";
import { isLimitedPrimarySettingsTabVisible } from "../pages/settings/tabVisibility";

describe("mergeTaskSelectablePaths", () => {
  it("defaults to all selectable paths when there is no stored selection", () => {
    expect(mergeTaskSelectablePaths(undefined, ["/library/a", "/library/b"], [])).toEqual([
      "/library/a",
      "/library/b",
    ]);
  });

  it("auto-selects newly added library roots without re-selecting previously deselected ones", () => {
    expect(
      mergeTaskSelectablePaths(["/library/a"], ["/library/a", "/library/b", "/library/c"], ["/library/a", "/library/b"])
    ).toEqual([
      "/library/a",
      "/library/c",
    ]);
  });

  it("prunes paths that are no longer selectable", () => {
    expect(
      mergeTaskSelectablePaths(["/library/a", "/library/b"], ["/library/b"], ["/library/a", "/library/b"])
    ).toEqual([
      "/library/b",
    ]);
  });
});

describe("resolveVisibleSettingsTab", () => {
  it("falls back to the first visible tab when the requested tab is hidden", () => {
    expect(
      resolveVisibleSettingsTab("library", [
        { key: "my-appearance-theme" },
        { key: "my-theme" },
        { key: "system-info-about" },
      ])
    ).toBe("my-appearance-theme");
  });

  it("keeps the requested tab when it is visible", () => {
    expect(
      resolveVisibleSettingsTab("system-info-about", [
        { key: "my-appearance-theme" },
        { key: "system-info-about" },
      ])
    ).toBe("system-info-about");
  });
});

describe("isLimitedPrimarySettingsTabVisible", () => {
  it("keeps personal and system info tabs visible for limited users", () => {
    expect(isLimitedPrimarySettingsTabVisible("my-account", false)).toBe(true);
    expect(isLimitedPrimarySettingsTabVisible("my-appearance-theme", false)).toBe(true);
    expect(isLimitedPrimarySettingsTabVisible("my-theme", false)).toBe(true);
    expect(isLimitedPrimarySettingsTabVisible("system-info-about", false)).toBe(true);
    expect(isLimitedPrimarySettingsTabVisible("library-paths-storage", false)).toBe(false);
  });

  it("keeps display profiles gated by segment access", () => {
    expect(isLimitedPrimarySettingsTabVisible("library-display-profiles", false)).toBe(false);
    expect(isLimitedPrimarySettingsTabVisible("library-display-profiles", true)).toBe(true);
  });
});