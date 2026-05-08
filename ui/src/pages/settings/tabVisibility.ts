export const LIMITED_PRIMARY_SETTINGS_TAB_KEYS = new Set([
  "interface",
  "user-settings",
  "changelog",
  "about",
]);

export function isLimitedPrimarySettingsTabVisible(tabKey: string, canReadSegments: boolean): boolean {
  return LIMITED_PRIMARY_SETTINGS_TAB_KEYS.has(tabKey) || (tabKey === "display-profiles" && canReadSegments);
}