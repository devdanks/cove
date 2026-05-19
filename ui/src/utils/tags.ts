import type { Tag } from "../api/types";

export function getEditableTagIds(tags: Pick<Tag, "id" | "canRemove">[]): number[] {
  return tags.filter((tag) => tag.canRemove !== false).map((tag) => tag.id);
}

export function getLockedTagIds(tags: Pick<Tag, "id" | "canRemove">[]): number[] {
  return tags.filter((tag) => tag.canRemove === false).map((tag) => tag.id);
}

export function mergeTagIds(...groups: number[][]): number[] {
  return Array.from(new Set(groups.flat().filter((id) => Number.isInteger(id) && id > 0)));
}
