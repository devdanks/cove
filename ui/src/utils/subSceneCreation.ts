import type { Scene, SceneCreate } from "../api/types";

interface SubSceneRange {
  startSec: number;
  endSec?: number;
}

interface SubSceneOverrides {
  title?: string;
  tagIds?: number[];
}

function mergeUniqueIds(primary: number[], extra?: number[]) {
  return Array.from(new Set([...(primary ?? []), ...(extra ?? [])]));
}

export function buildSubSceneCreate(scene: Scene, range: SubSceneRange, overrides: SubSceneOverrides = {}): SceneCreate {
  const mergedTagIds = mergeUniqueIds(scene.tags.map((tag) => tag.id), overrides.tagIds);
  const performerIds = scene.performers.map((performer) => performer.id);
  const galleryIds = scene.galleries.map((gallery) => gallery.id);
  const groups = scene.groups.map((group) => ({ groupId: group.id, sceneIndex: group.sceneIndex }));
  const urls = scene.urls.filter((url) => url.trim().length > 0);
  const title = overrides.title?.trim() || scene.title;

  return {
    title,
    code: scene.code,
    details: scene.details,
    director: scene.director,
    date: scene.date,
    organized: scene.organized,
    isVr: scene.isVr ?? false,
    studioId: scene.studioId,
    urls: urls.length > 0 ? [...urls] : undefined,
    tagIds: mergedTagIds.length > 0 ? mergedTagIds : undefined,
    performerIds: performerIds.length > 0 ? performerIds : undefined,
    galleryIds: galleryIds.length > 0 ? galleryIds : undefined,
    groups: groups.length > 0 ? groups : undefined,
    customFields: scene.customFields ? { ...scene.customFields } : undefined,
    parentSceneId: scene.id,
    clipStartSec: range.startSec,
    clipEndSec: range.endSec,
  };
}