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
        { key: "interface" },
        { key: "changelog" },
        { key: "about" },
      ])
    ).toBe("interface");
  });

  it("keeps the requested tab when it is visible", () => {
    expect(
      resolveVisibleSettingsTab("about", [
        { key: "interface" },
        { key: "about" },
      ])
    ).toBe("about");
  });
});

describe("isLimitedPrimarySettingsTabVisible", () => {
  it("keeps User Settings visible for limited users", () => {
    expect(isLimitedPrimarySettingsTabVisible("user-settings", false)).toBe(true);
    expect(isLimitedPrimarySettingsTabVisible("library", false)).toBe(false);
  });

  it("keeps display profiles gated by segment access", () => {
    expect(isLimitedPrimarySettingsTabVisible("display-profiles", false)).toBe(false);
    expect(isLimitedPrimarySettingsTabVisible("display-profiles", true)).toBe(true);
  });
});