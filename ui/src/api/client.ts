import type {
  MeResponse,
  Scene, SceneCreate, SceneUpdate,
  Performer, PerformerCreate, PerformerUpdate,
  Tag, TagDetail, TagCreate, TagUpdate, TagSegmentWall,
  TagGraphNode, TagGraphResponse,
  Studio, StudioCreate, StudioUpdate,
  Gallery, GalleryCreate, GalleryUpdate, GalleryChapter, GalleryChapterCreate, GalleryChapterUpdate,
  Image, ImageCreate, ImageUpdate,
  Group, GroupCreate, GroupUpdate,
  GroupItem, GroupItemCreate, GroupItemsFromSpans, GroupItemsReorder, GroupItemUpdate,
  GroupPlaybackManifest,
  AiDataPurgeRequest,
  AiDataPurgeResult,
  AiDataSelector,
  AiDataSummary,
  AffinityHostType,
  Segment, SegmentCreate, SegmentRecord, SegmentUpdate,
  ResolvedSpanDetail, ResolvedSpanList, SceneResolvedSpans, SegmentDisplayProfile,
  SegmentDisplayProfileCreate, SegmentDisplayProfileUpdate,
  SegmentDisplayRule, SegmentDisplayRuleCreate, SegmentDisplayRuleUpdate,
  SegmentSpanQueryRequest, SegmentSpanSearchRequest, SegmentSpanSearchResponse,
  Detection, DetectionCreate, DetectionUpdate,
  Face, FaceCreate, FaceUpdate, FaceLink, FaceMerge, FaceIgnore, FaceDeleteImpact, FaceSimilar, FaceSuggestion,
  EntityEngagement, EntityFavorite, EntityEngagementBatchRequest, EntityRatings,
  EngagementInteraction, EngagementInteractionWrite,
  SceneHistory,
  PaginatedResponse, Stats, SystemStatus, CoveConfig, JobInfo,
  ScraperSummary,
  DownloaderDescriptor,
  DownloaderBatchStartRequest,
  DownloaderBatchStartResponse,
  DownloaderMatch,
  DownloaderMatchRequest,
  DownloaderStartRequest,
  MetadataServer,
  MetadataServerFindByIdsRequest,
  MetadataServerPerformerBatchTagRequest,
  MetadataServerPerformerImportRequest,
  MetadataServerPerformerMatch,
  MetadataServerSceneImportRequest,
  MetadataServerSceneMatch,
  MetadataServerTagBatchTagRequest,
  MetadataServerTagImportRequest,
  MetadataServerTagMatch,
  MetadataServerStudioBatchTagRequest,
  MetadataServerStudioMatch,
  MetadataServerStudioImportRequest,
  MetadataServerValidationResult,
  FindFilter,
  SavedFilter,
  SavedFilterCreate,
  SavedFilterUpdate,
  PerformerScrapeRequest,
  FilteredQueryRequest,
  SceneFilterCriteria,
  PerformerFilterCriteria,
  TagFilterCriteria,
  StudioFilterCriteria,
  GalleryFilterCriteria,
  ImageFilterCriteria,
  GroupFilterCriteria,
  ScrapeAttempt,
  CreateScrapeAttemptRequest,
  ApplySceneScrapeAttemptRequest,
  BatchSceneScrapeStartRequest,
  BulkSceneUpdate,
  BulkPerformerUpdate,
  BulkTagUpdate,
  BulkStudioUpdate,
  BulkGalleryUpdate,
  BulkImageUpdate,
  BulkGroupUpdate,
  Plugin,
  PluginTask,
  RunPluginTaskRequest,
  PluginSettings,
  Package,
  ExtensionManifest,
  ExtensionInfo,
  DependencyProblem,
  RegistrySearchResult,
  RegistryExtensionDetail,
  RegistryUpdateInfo,
  DependencyInfo,
  DownloaderPreflightRequest,
  DownloaderPreflightResponse,
  UserUiPreferences,
  PlaybackIntervalsRequest,
} from "./types";

const API_BASE = "/api";

// ===== Auth-aware fetch =====
// Lazy import to avoid circular deps; the auth module has no client.ts deps.
import { authStore } from "../auth/authStore";

let refreshInFlight: Promise<boolean> | null = null;

async function tryRefresh(): Promise<boolean> {
  if (refreshInFlight) return refreshInFlight;
  refreshInFlight = (async () => {
    const refresh = authStore.getRefreshToken();
    if (!refresh) return false;
    try {
      const res = await fetch(`${API_BASE}/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken: refresh }),
      });
      if (!res.ok) {
        authStore.clear();
        return false;
      }
      const body = await res.json() as { token?: string; refreshToken?: string };
      if (!body.token) return false;
      authStore.setTokens(body.token, body.refreshToken ?? refresh);
      return true;
    } catch {
      return false;
    } finally {
      // cleared by outer
    }
  })();
  try { return await refreshInFlight; }
  finally { refreshInFlight = null; }
}

async function authedFetch(input: string, init?: RequestInit): Promise<Response> {
  const token = authStore.getAccessToken();
  const shareToken = authStore.getShareToken();
  const sharePassword = authStore.getSharePassword();
  const headers = new Headers(init?.headers ?? {});
  const authMode = shareToken ? "share" : token ? "bearer" : "none";
  if (shareToken) {
    headers.set("X-Share-Token", shareToken);
    if (sharePassword) {
      headers.set("X-Share-Password", sharePassword);
    }
  } else if (token && !headers.has("Authorization")) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  let res = await fetch(input, { ...init, headers });
  if (res.status === 401 && authMode === "bearer" && token && authStore.getRefreshToken()) {
    const ok = await tryRefresh();
    if (ok) {
      const retryToken = authStore.getAccessToken();
      const retryHeaders = new Headers(init?.headers ?? {});
      if (retryToken) retryHeaders.set("Authorization", `Bearer ${retryToken}`);
      res = await fetch(input, { ...init, headers: retryHeaders });
    } else {
      // refresh failed: emit a global event so UI can react
      window.dispatchEvent(new CustomEvent("cove-auth-required"));
    }
  } else if (res.status === 401 && authMode === "none") {
    window.dispatchEvent(new CustomEvent("cove-auth-required"));
  }
  return res;
}

const CRITERION_MODIFIER_MAP: Record<string, string> = {
  EQUALS: "equals",
  NOT_EQUALS: "notEquals",
  GREATER_THAN: "greaterThan",
  LESS_THAN: "lessThan",
  INCLUDES: "includes",
  EXCLUDES: "excludes",
  INCLUDES_ALL: "includesAll",
  EXCLUDES_ALL: "excludesAll",
  IS_NULL: "isNull",
  NOT_NULL: "notNull",
  BETWEEN: "between",
  NOT_BETWEEN: "notBetween",
  MATCHES_REGEX: "matchesRegex",
  NOT_MATCHES_REGEX: "notMatchesRegex",
};

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await authedFetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...options?.headers,
    },
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`API Error ${res.status}: ${text}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

function normalizeApiPath(path: string): string {
  const normalized = path.trim();
  if (normalized.startsWith(`${API_BASE}/`)) {
    return normalized.slice(API_BASE.length);
  }

  return normalized.startsWith("/") ? normalized : `/${normalized}`;
}

async function requestOptional<T>(path: string, options?: RequestInit): Promise<T | null> {
  const res = await authedFetch(`${API_BASE}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...options?.headers,
    },
  });
  if (res.status === 404) {
    return null;
  }
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`API Error ${res.status}: ${text}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

function buildQuery(filter?: FindFilter, extra?: Record<string, string | number | boolean | undefined>): string {
  const params = new URLSearchParams();
  if (filter?.q) params.set("q", filter.q);
  if (filter?.page) params.set("page", String(filter.page));
  if (filter?.perPage) params.set("perPage", String(filter.perPage));
  if (filter?.sort) params.set("sort", filter.sort);
  if (filter?.direction) params.set("direction", filter.direction);
  if (filter?.seed != null) params.set("seed", String(filter.seed));
  if (extra) {
    for (const [k, v] of Object.entries(extra)) {
      if (v !== undefined) params.set(k, String(v));
    }
  }
  const qs = params.toString();
  return qs ? `?${qs}` : "";
}

function buildAiDataQuery(selector?: AiDataSelector): string {
  if (!selector) {
    return "";
  }

  const params = new URLSearchParams();
  if (selector.sourceKey) params.set("sourceKey", selector.sourceKey);
  if (selector.sourceRunId) params.set("sourceRunId", selector.sourceRunId);
  if (selector.model) params.set("model", selector.model);
  if (selector.modality) params.set("modality", selector.modality);
  if (selector.hostType) params.set("hostType", selector.hostType);
  if (selector.hostId != null) params.set("hostId", String(selector.hostId));
  if (selector.kinds && selector.kinds.length > 0) params.set("kinds", selector.kinds.join(","));
  const query = params.toString();
  return query ? `?${query}` : "";
}

function normalizeCriterionPayload<T>(value: T): T {
  if (Array.isArray(value)) {
    return value.map((item) => normalizeCriterionPayload(item)) as T;
  }

  if (value && typeof value === "object") {
    const normalizedEntries = Object.entries(value as Record<string, unknown>).map(([key, entryValue]) => {
      if (key === "modifier" && typeof entryValue === "string") {
        return [key, CRITERION_MODIFIER_MAP[entryValue] ?? entryValue];
      }

      return [key, normalizeCriterionPayload(entryValue)];
    });

    return Object.fromEntries(normalizedEntries) as T;
  }

  return value;
}

function buildMediaUrl(
  path: string,
  version?: string,
  max?: number,
  extra?: Record<string, string | number | undefined>,
): string {
  const params = new URLSearchParams();
  if (typeof max === "number" && max > 0) params.set("max", String(max));
  if (version) params.set("v", version);
  if (extra) {
    for (const [key, value] of Object.entries(extra)) {
      if (value !== undefined && value !== "") {
        params.set(key, String(value));
      }
    }
  }

  const shareToken = authStore.getShareToken();
  const sharePassword = authStore.getSharePassword();
  const accessToken = authStore.getAccessToken();
  if (shareToken) {
    params.set("share_token", shareToken);
    if (sharePassword) {
      params.set("share_password", sharePassword);
    }
  } else if (accessToken) {
    params.set("access_token", accessToken);
  }

  const query = params.toString();
  return `${API_BASE}${path}${query ? `?${query}` : ""}`;
}

// ===== Scenes =====
export const scenes = {
  find: (filter?: FindFilter, extra?: Record<string, string | number | boolean | undefined>) =>
    request<PaginatedResponse<Scene>>(`/scenes${buildQuery(filter, extra)}`),
  findFiltered: (req: FilteredQueryRequest<SceneFilterCriteria>) =>
    request<PaginatedResponse<Scene>>("/scenes/find", { method: "POST", body: JSON.stringify(normalizeCriterionPayload(req)) }),
  get: (id: number) => request<Scene>(`/scenes/${id}`),
  create: (data: SceneCreate) => request<Scene>("/scenes", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: SceneUpdate) => request<Scene>(`/scenes/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  bulkUpdate: (data: BulkSceneUpdate) => request<void>("/scenes/bulk", { method: "POST", body: JSON.stringify(data) }),
  delete: (id: number, deleteFile?: boolean) => request<void>(`/scenes/${id}${deleteFile ? "?deleteFile=true" : ""}`, { method: "DELETE" }),
  bulkDelete: (ids: number[]) => request<{ deleted: number }>("/scenes/destroy", { method: "POST", body: JSON.stringify({ ids }) }),
  merge: (targetId: number, sourceIds: number[]) =>
    request<Scene>("/scenes/merge", { method: "POST", body: JSON.stringify({ targetId, sourceIds }) }),
  recordPlay: (id: number) => request<void>(`/scenes/${id}/play`, { method: "POST" }),
  incrementO: (id: number) => request<number>(`/scenes/${id}/o`, { method: "POST" }),
  decrementO: (id: number) => request<void>(`/scenes/${id}/o`, { method: "DELETE" }),
  resetO: (id: number) => request<void>(`/scenes/${id}/o/reset`, { method: "POST" }),
  deletePlay: (id: number) => request<void>(`/scenes/${id}/play`, { method: "DELETE" }),
  resetPlay: (id: number) => request<void>(`/scenes/${id}/play/reset`, { method: "POST" }),
  getHistory: (id: number) => request<SceneHistory>(`/scenes/${id}/history`),
  searchMetadataServer: (id: number, term?: string, endpoint?: string) =>
    request<MetadataServerSceneMatch[]>(`/scenes/${id}/metadata-server/search${buildQuery(undefined, { term, endpoint })}`),
  importFromMetadataServer: (id: number, data: MetadataServerSceneImportRequest) =>
    request<Scene>(`/scenes/${id}/metadata-server/import`, { method: "POST", body: JSON.stringify(data) }),
  generateScreenshot: (id: number, atSeconds?: number) =>
    request<{ success: boolean }>(`/scenes/${id}/generate-screenshot`, { method: "POST", body: JSON.stringify({ atSeconds }) }),
  setCoverFromFrame: (id: number, atSeconds?: number) =>
    request<{ success: boolean }>(`/scenes/${id}/cover/from-frame`, { method: "POST", body: JSON.stringify({ atSeconds }) }),
  rescan: (id: number) =>
    request<{ jobId: string }>(`/scenes/${id}/rescan`, { method: "POST" }),
  assignFile: (id: number, fileId: number) =>
    request<void>(`/scenes/${id}/assign-file`, { method: "POST", body: JSON.stringify({ fileId }) }),
  streamUrl: (id: number) => buildMediaUrl(`/stream/scene/${id}`),
  screenshotUrl: (id: number, version?: string) => buildMediaUrl(`/stream/scene/${id}/screenshot`, version),
  previewUrl: (id: number) => buildMediaUrl(`/stream/scene/${id}/preview`),
  captionUrl: (sceneId: number, captionId: number) => buildMediaUrl(`/stream/scene/${sceneId}/caption/${captionId}`),
  transcodeUrl: (id: number, resolution?: string) => buildMediaUrl(`/stream/scene/${id}/transcode`, undefined, undefined, { resolution }),
  hlsMasterUrl: (id: number) => buildMediaUrl(`/stream/scene/${id}/hls/master.m3u8`),
  getResolutions: (id: number) => request<string[]>(`/stream/scene/${id}/resolutions`),
  segments: {
    list: (sceneId: number) => request<Segment[]>(`/scenes/${sceneId}/segments`),
    create: (sceneId: number, data: SegmentCreate) =>
      request<Segment>(`/scenes/${sceneId}/segments`, { method: "POST", body: JSON.stringify(data) }),
    update: (sceneId: number, id: number, data: SegmentUpdate) =>
      request<Segment>(`/scenes/${sceneId}/segments/${id}`, { method: "PUT", body: JSON.stringify(data) }),
    delete: (sceneId: number, id: number) =>
      request<void>(`/scenes/${sceneId}/segments/${id}`, { method: "DELETE" }),
    spans: (sceneId: number, profile?: number) =>
      request<SceneResolvedSpans>(`/scenes/${sceneId}/segments/spans${buildQuery(undefined, { profile })}`),
    querySpans: (sceneId: number, data: SegmentSpanQueryRequest) =>
      request<ResolvedSpanList>(`/scenes/${sceneId}/segments/spans/query`, { method: "POST", body: JSON.stringify(data) }),
    spanDetail: (sceneId: number, spanKey: string, profile?: number) =>
      request<ResolvedSpanDetail>(`/scenes/${sceneId}/spans/${encodeURIComponent(spanKey)}${buildQuery(undefined, { profile })}`),
  },
  detections: {
    list: (sceneId: number) => request<Detection[]>(`/scenes/${sceneId}/detections`),
    create: (sceneId: number, data: DetectionCreate) =>
      request<Detection>(`/scenes/${sceneId}/detections`, { method: "POST", body: JSON.stringify(data) }),
    update: (sceneId: number, id: number, data: DetectionUpdate) =>
      request<Detection>(`/scenes/${sceneId}/detections/${id}`, { method: "PUT", body: JSON.stringify(data) }),
    delete: (sceneId: number, id: number) =>
      request<void>(`/scenes/${sceneId}/detections/${id}`, { method: "DELETE" }),
  },
  findDuplicates: (distance = 0, durationDiff?: number) => {
    const params = new URLSearchParams();
    params.set("distance", String(distance));
    if (durationDiff !== undefined) params.set("durationDiff", String(durationDiff));
    return request<Scene[][]>(`/scenes/duplicates?${params.toString()}`);
  },
};

export const playback = {
  recordIntervals: (data: PlaybackIntervalsRequest) =>
    request<void>("/playback/intervals", { method: "POST", body: JSON.stringify(data) }),
};

export const segmentLibrary = {
  list: (opts?: {
    q?: string;
    sceneId?: number;
    sceneIds?: string;
    sceneTitle?: string;
    tagId?: number;
    tagIds?: string;
    kind?: string;
    sourceKey?: string;
    tagged?: boolean;
    minConfidence?: number;
    minDurationSec?: number;
    sort?: string;
    direction?: "asc" | "desc";
    page?: number;
    perPage?: number;
    ids?: string;
    excludeSceneIds?: string;
  }) =>
    request<PaginatedResponse<SegmentRecord>>(`/segments${buildQuery(undefined, opts)}`),
  get: (id: number) => requestOptional<SegmentRecord>(`/segments/${id}`),
  distinctSourceKeys: () => request<{ value: string; count: number }[]>("/segments/source-keys/distinct"),
  distinctKinds: () => request<{ value: string; count: number }[]>("/segments/kinds/distinct"),
};

// ===== Faces =====
export const faces: {
  list: (opts?: { q?: string; performerId?: number; ignored?: boolean; merged?: boolean; page?: number; perPage?: number }) => Promise<PaginatedResponse<Face>>;
  get: (id: number) => Promise<Face>;
  detections: (id: number) => Promise<Detection[]>;
  deleteImpact: (id: number) => Promise<FaceDeleteImpact>;
  create: (data: FaceCreate) => Promise<Face>;
  update: (id: number, data: FaceUpdate) => Promise<Face>;
  delete: (id: number) => Promise<void>;
  link: (id: number, data: FaceLink) => Promise<Face>;
  mergeInto: (id: number, data: FaceMerge) => Promise<Face>;
  setIgnored: (id: number, data: FaceIgnore) => Promise<Face>;
  similar: (id: number, opts?: { kindFamily?: string; k?: number }) => Promise<FaceSimilar[]>;
  suggestions: (id: number, maxResults?: number) => Promise<FaceSuggestion[]>;
  recordSuggestionDecision: (id: number, data: { performerId: number; decision: "accept" | "reject" }) => Promise<void>;
} = {
  list: (opts?: { q?: string; performerId?: number; ignored?: boolean; merged?: boolean; page?: number; perPage?: number }) =>
    request<PaginatedResponse<Face>>(`/faces${buildQuery({ page: opts?.page, perPage: opts?.perPage, q: opts?.q }, {
      performerId: opts?.performerId,
      ignored: opts?.ignored,
      merged: opts?.merged,
    })}`),
  get: (id: number) => request<Face>(`/faces/${id}`),
  detections: (id: number) => request<Detection[]>(`/faces/${id}/detections`),
  deleteImpact: (id: number) => request<FaceDeleteImpact>(`/faces/${id}/delete-impact`),
  create: (data: FaceCreate) => request<Face>("/faces", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: FaceUpdate) => request<Face>(`/faces/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/faces/${id}`, { method: "DELETE" }),
  link: (id: number, data: FaceLink) => request<Face>(`/faces/${id}/link`, { method: "POST", body: JSON.stringify(data) }),
  mergeInto: (id: number, data: FaceMerge) => request<Face>(`/faces/${id}/merge-into`, { method: "POST", body: JSON.stringify(data) }),
  setIgnored: (id: number, data: FaceIgnore) => request<Face>(`/faces/${id}/ignore`, { method: "POST", body: JSON.stringify(data) }),
  similar: (id: number, opts?: { kindFamily?: string; k?: number }) =>
    request<FaceSimilar[]>(`/faces/${id}/similar${buildQuery(undefined, { kindFamily: opts?.kindFamily, k: opts?.k })}`),
  suggestions: (id: number, maxResults?: number) =>
    request<FaceSuggestion[]>(`/faces/${id}/suggestions${buildQuery(undefined, { maxResults })}`),
  recordSuggestionDecision: (id: number, data: { performerId: number; decision: "accept" | "reject" }) =>
    request<void>(`/faces/${id}/suggestions/decision`, { method: "POST", body: JSON.stringify(data) }),
};

export const entityEngagement = {
  get: (hostType: AffinityHostType, hostId: number) => requestOptional<EntityEngagement>(`/engagement/${hostType}/${hostId}`),
  getRatings: (hostType: AffinityHostType, hostId: number) => request<EntityRatings>(`/engagement/${hostType}/${hostId}/ratings`),
  batch: (data: EntityEngagementBatchRequest) =>
    request<EntityEngagement[]>("/engagement/batch", { method: "POST", body: JSON.stringify(data) }),
  setFavorite: (hostType: AffinityHostType, hostId: number, data: EntityFavorite) =>
    request<EntityEngagement>(`/engagement/${hostType}/${hostId}/favorite`, { method: "PUT", body: JSON.stringify(data) }),
  setRating: (hostType: AffinityHostType, hostId: number, data: { value: number | null; aspect?: string }) =>
    request<EntityEngagement>(`/engagement/${hostType}/${hostId}/rating`, { method: "PUT", body: JSON.stringify(data) }),
  recordInteraction: (data: EngagementInteractionWrite) =>
    request<void>("/engagement/interactions", { method: "POST", body: JSON.stringify(data) }),
  getInteractions: (options?: { hostType?: string; hostId?: number; limit?: number }) =>
    request<EngagementInteraction[]>(`/engagement/interactions${buildQuery(undefined, options)}`),
};

// ===== Performers =====
export const performers = {
  find: (filter?: FindFilter, extra?: Record<string, string | number | boolean | undefined>) =>
    request<PaginatedResponse<Performer>>(`/performers${buildQuery(filter, extra)}`),
  findFiltered: (req: FilteredQueryRequest<PerformerFilterCriteria>) =>
    request<PaginatedResponse<Performer>>("/performers/find", { method: "POST", body: JSON.stringify(normalizeCriterionPayload(req)) }),
  get: (id: number) => request<Performer>(`/performers/${id}`),
  create: (data: PerformerCreate) => request<Performer>("/performers", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: PerformerUpdate) => request<Performer>(`/performers/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  scrape: (id: number, data: PerformerScrapeRequest) => request<Performer>(`/performers/${id}/scrape`, { method: "POST", body: JSON.stringify(data) }),
  scrapeUrl: (id: number, data?: { url?: string; createMissingTags?: boolean }) =>
    request<Performer>(`/performers/${id}/scrape-url`, { method: "POST", body: JSON.stringify(data ?? {}) }),
  previewScrape: (id: number, data: PerformerScrapeRequest) => request<import("./types").PerformerScrapePreview>(`/performers/${id}/scrape-preview`, { method: "POST", body: JSON.stringify(data) }),
  applyScraped: (id: number, data: { scraped: import("./types").ScrapedPerformer; createMissingTags?: boolean }) => request<Performer>(`/performers/${id}/apply-scraped`, { method: "POST", body: JSON.stringify(data) }),
  bulkUpdate: (data: BulkPerformerUpdate) => request<void>("/performers/bulk", { method: "POST", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/performers/${id}`, { method: "DELETE" }),
  bulkDelete: (ids: number[]) => request<void>("/performers/bulk", { method: "DELETE", body: JSON.stringify({ ids }) }),
  merge: (targetId: number, sourceIds: number[]) =>
    request<Performer>("/performers/merge", { method: "POST", body: JSON.stringify({ targetId, sourceIds }) }),
  searchMetadataServer: (id: number, term?: string, endpoint?: string) =>
    request<MetadataServerPerformerMatch[]>(`/performers/${id}/metadata-server/search${buildQuery(undefined, { term, endpoint })}`),
  findMetadataServerByIds: (data: MetadataServerFindByIdsRequest) =>
    request<MetadataServerPerformerMatch[]>("/performers/metadata-server/find-by-ids", { method: "POST", body: JSON.stringify(data) }),
  importFromMetadataServer: (id: number, data: MetadataServerPerformerImportRequest) =>
    request<Performer>(`/performers/${id}/metadata-server/import`, { method: "POST", body: JSON.stringify(data) }),
  submitMetadataServerDraft: (id: number, endpoint: string) =>
    request<{ draftId: string | null }>(`/performers/${id}/metadata-server/submit-draft`, { method: "POST", body: JSON.stringify({ endpoint }) }),
  batchTagMetadataServer: (data: MetadataServerPerformerBatchTagRequest) =>
    request<{ jobId: string; itemCount: number }>("/performers/metadata-server/batch-tag", { method: "POST", body: JSON.stringify(data) }),
};

// ===== Tags =====
export const tags = {
  find: (filter?: FindFilter, extra?: Record<string, string | number | boolean | undefined>) =>
    request<PaginatedResponse<Tag>>(`/tags${buildQuery(filter, extra)}`),
  findFiltered: (req: FilteredQueryRequest<TagFilterCriteria>) =>
    request<PaginatedResponse<Tag>>("/tags/find", { method: "POST", body: JSON.stringify(normalizeCriterionPayload(req)) }),
  graph: (req: FilteredQueryRequest<TagFilterCriteria>) =>
    request<TagGraphResponse>("/tags/graph", { method: "POST", body: JSON.stringify(normalizeCriterionPayload(req)) }),
  get: (id: number) => request<TagDetail>(`/tags/${id}`),
  segments: (id: number, count = 100) => request<TagSegmentWall[]>(`/tags/${id}/segments${buildQuery(undefined, { count })}`),
  create: (data: TagCreate) => request<TagDetail>("/tags", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: TagUpdate) => request<TagDetail>(`/tags/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  bulkUpdate: (data: BulkTagUpdate) => request<void>("/tags/bulk", { method: "POST", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/tags/${id}`, { method: "DELETE" }),
  bulkDelete: (ids: number[]) => request<void>("/tags/bulk", { method: "DELETE", body: JSON.stringify({ ids }) }),
  merge: (targetId: number, sourceIds: number[]) =>
    request<TagDetail>("/tags/merge", { method: "POST", body: JSON.stringify({ targetId, sourceIds }) }),
  searchMetadataServer: (id: number, term?: string, endpoint?: string) =>
    request<MetadataServerTagMatch[]>(`/tags/${id}/metadata-server/search${buildQuery(undefined, { term, endpoint })}`),
  findMetadataServerByIds: (data: MetadataServerFindByIdsRequest) =>
    request<MetadataServerTagMatch[]>("/tags/metadata-server/find-by-ids", { method: "POST", body: JSON.stringify(data) }),
  importFromMetadataServer: (id: number, data: MetadataServerTagImportRequest) =>
    request<TagDetail>(`/tags/${id}/metadata-server/import`, { method: "POST", body: JSON.stringify(data) }),
  submitMetadataServerDraft: (id: number, endpoint: string) =>
    request<{ draftId: string | null }>(`/tags/${id}/metadata-server/submit-draft`, { method: "POST", body: JSON.stringify({ endpoint }) }),
  batchTagMetadataServer: (data: MetadataServerTagBatchTagRequest) =>
    request<{ jobId: string; itemCount: number }>("/tags/metadata-server/batch-tag", { method: "POST", body: JSON.stringify(data) }),
};

export const aiData = {
  summary: (selector?: AiDataSelector) => request<AiDataSummary>(`/ai-data/summary${buildAiDataQuery(selector)}`),
  purge: (request_: AiDataPurgeRequest) => request<AiDataPurgeResult>("/ai-data/purge", { method: "POST", body: JSON.stringify(request_) }),
};

// ===== Studios =====
export const studios = {
  find: (filter?: FindFilter, extra?: Record<string, string | number | boolean | undefined>) =>
    request<PaginatedResponse<Studio>>(`/studios${buildQuery(filter, extra)}`),
  findFiltered: (req: FilteredQueryRequest<StudioFilterCriteria>) =>
    request<PaginatedResponse<Studio>>("/studios/find", { method: "POST", body: JSON.stringify(normalizeCriterionPayload(req)) }),
  get: (id: number) => request<Studio>(`/studios/${id}`),
  create: (data: StudioCreate) => request<Studio>("/studios", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: StudioUpdate) => request<Studio>(`/studios/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  bulkUpdate: (data: BulkStudioUpdate) => request<void>("/studios/bulk", { method: "POST", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/studios/${id}`, { method: "DELETE" }),
  bulkDelete: (ids: number[]) => request<void>("/studios/bulk", { method: "DELETE", body: JSON.stringify({ ids }) }),
  merge: (targetId: number, sourceIds: number[]) =>
    request<Studio>("/studios/merge", { method: "POST", body: JSON.stringify({ targetId, sourceIds }) }),
  searchMetadataServer: (id: number, term?: string, endpoint?: string) => {
    const params = new URLSearchParams();
    if (term) params.set("term", term);
    if (endpoint) params.set("endpoint", endpoint);
    const qs = params.toString();
    return request<MetadataServerStudioMatch[]>(`/studios/${id}/metadata-server/search${qs ? `?${qs}` : ""}`);
  },
  findMetadataServerByIds: (data: MetadataServerFindByIdsRequest) =>
    request<MetadataServerStudioMatch[]>("/studios/metadata-server/find-by-ids", { method: "POST", body: JSON.stringify(data) }),
  importFromMetadataServer: (id: number, data: MetadataServerStudioImportRequest) =>
    request<Studio>(`/studios/${id}/metadata-server/import`, { method: "POST", body: JSON.stringify(data) }),
  submitMetadataServerDraft: (id: number, endpoint: string) =>
    request<{ draftId: string | null }>(`/studios/${id}/metadata-server/submit-draft`, { method: "POST", body: JSON.stringify({ endpoint }) }),
  batchTagMetadataServer: (data: MetadataServerStudioBatchTagRequest) =>
    request<{ jobId: string; itemCount: number }>("/studios/metadata-server/batch-tag", { method: "POST", body: JSON.stringify(data) }),
};

// ===== Galleries =====
export const galleries = {
  find: (filter?: FindFilter, extra?: Record<string, string | number | boolean | undefined>) =>
    request<PaginatedResponse<Gallery>>(`/galleries${buildQuery(filter, extra)}`),
  findFiltered: (req: FilteredQueryRequest<GalleryFilterCriteria>) =>
    request<PaginatedResponse<Gallery>>("/galleries/find", { method: "POST", body: JSON.stringify(normalizeCriterionPayload(req)) }),
  get: (id: number) => request<Gallery>(`/galleries/${id}`),
  create: (data: GalleryCreate) => request<Gallery>("/galleries", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: GalleryUpdate) => request<Gallery>(`/galleries/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  bulkUpdate: (data: BulkGalleryUpdate) => request<void>("/galleries/bulk", { method: "POST", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/galleries/${id}`, { method: "DELETE" }),
  bulkDelete: (ids: number[]) => request<void>("/galleries/bulk", { method: "DELETE", body: JSON.stringify({ ids }) }),
  chapters: (id: number) => request<GalleryChapter[]>(`/galleries/${id}/chapters`),
  createChapter: (id: number, data: GalleryChapterCreate) =>
    request<GalleryChapter>(`/galleries/${id}/chapters`, { method: "POST", body: JSON.stringify(data) }),
  updateChapter: (galleryId: number, chapterId: number, data: GalleryChapterUpdate) =>
    request<GalleryChapter>(`/galleries/${galleryId}/chapters/${chapterId}`, { method: "PUT", body: JSON.stringify(data) }),
  deleteChapter: (galleryId: number, chapterId: number) =>
    request<void>(`/galleries/${galleryId}/chapters/${chapterId}`, { method: "DELETE" }),
  addImages: (id: number, imageIds: number[]) =>
    request<{ added: number }>(`/galleries/${id}/images`, { method: "POST", body: JSON.stringify({ imageIds }) }),
  removeImages: (id: number, imageIds: number[]) =>
    request<{ removed: number }>(`/galleries/${id}/images`, { method: "DELETE", body: JSON.stringify({ imageIds }) }),
  uploadCoverImage: (id: number, file: File) => {
    const formData = new FormData();
    formData.append("file", file);
    return request<void>(`/galleries/${id}/image`, { method: "POST", body: formData });
  },
  coverUrl: (id: number, version?: string, max = 640) => buildMediaUrl(`/galleries/${id}/cover`, version, max),
  getCoverImageUrl: (id: number, version?: string, max = 640) => buildMediaUrl(`/galleries/${id}/image`, version, max),
  deleteCoverImage: (id: number) => request<void>(`/galleries/${id}/image`, { method: "DELETE" }),
  setCover: (id: number, imageId: number) =>
    request<void>(`/entity-images/galleries/${id}/cover`, { method: "PUT", body: JSON.stringify({ imageId }) }),
  resetCover: (id: number) => request<void>(`/entity-images/galleries/${id}/cover`, { method: "DELETE" }),
};

// ===== Images =====
export const images = {
  find: (filter?: FindFilter, extra?: Record<string, string | number | boolean | undefined>) =>
    request<PaginatedResponse<Image>>(`/images${buildQuery(filter, extra)}`),
  findFiltered: (req: FilteredQueryRequest<ImageFilterCriteria>) =>
    request<PaginatedResponse<Image>>("/images/find", { method: "POST", body: JSON.stringify(normalizeCriterionPayload(req)) }),
  get: (id: number) => request<Image>(`/images/${id}`),
  create: (data: ImageCreate) => request<Image>("/images", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: ImageUpdate) => request<Image>(`/images/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  bulkUpdate: (data: BulkImageUpdate) => request<void>("/images/bulk", { method: "POST", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/images/${id}`, { method: "DELETE" }),
  bulkDelete: (ids: number[]) => request<void>("/images/bulk", { method: "DELETE", body: JSON.stringify({ ids }) }),
  incrementO: (id: number) => request<number>(`/images/${id}/o`, { method: "POST" }),
  decrementO: (id: number) => request<number>(`/images/${id}/o`, { method: "DELETE" }),
  resetO: (id: number) => request<number>(`/images/${id}/o/reset`, { method: "POST" }),
  detections: {
    list: (imageId: number) => request<Detection[]>(`/images/${imageId}/detections`),
    create: (imageId: number, data: DetectionCreate) =>
      request<Detection>(`/images/${imageId}/detections`, { method: "POST", body: JSON.stringify(data) }),
    update: (imageId: number, id: number, data: DetectionUpdate) =>
      request<Detection>(`/images/${imageId}/detections/${id}`, { method: "PUT", body: JSON.stringify(data) }),
    delete: (imageId: number, id: number) =>
      request<void>(`/images/${imageId}/detections/${id}`, { method: "DELETE" }),
  },
  imageUrl: (id: number) => buildMediaUrl(`/stream/image/${id}`),
  thumbnailUrl: (id: number, max?: number) => buildMediaUrl(`/stream/image/${id}/thumbnail`, undefined, max),
};

// ===== Groups =====
export const groups = {
  find: (filter?: FindFilter, extra?: Record<string, string | number | boolean | undefined>) =>
    request<PaginatedResponse<Group>>(`/groups${buildQuery(filter, extra)}`),
  findFiltered: (req: FilteredQueryRequest<GroupFilterCriteria>) =>
    request<PaginatedResponse<Group>>("/groups/find", { method: "POST", body: JSON.stringify(normalizeCriterionPayload(req)) }),
  get: (id: number) => request<Group>(`/groups/${id}`),
  create: (data: GroupCreate) => request<Group>("/groups", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: GroupUpdate) => request<Group>(`/groups/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  bulkUpdate: (data: BulkGroupUpdate) => request<void>("/groups/bulk", { method: "POST", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/groups/${id}`, { method: "DELETE" }),
  bulkDelete: (ids: number[]) => request<void>("/groups/bulk", { method: "DELETE", body: JSON.stringify({ ids }) }),
  subGroups: (id: number) => request<Group[]>(`/groups/${id}/subgroups`),
  containingGroups: (id: number) => request<Group[]>(`/groups/${id}/containinggroups`),
  addSubGroup: (id: number, subGroupId: number, orderIndex?: number) =>
    request<void>(`/groups/${id}/subgroups`, { method: "POST", body: JSON.stringify({ subGroupId, orderIndex }) }),
  removeSubGroup: (id: number, subGroupId: number) =>
    request<void>(`/groups/${id}/subgroups/${subGroupId}`, { method: "DELETE" }),
  reorderSubGroups: (id: number, subGroupIds: number[]) =>
    request<void>(`/groups/${id}/subgroups/reorder`, { method: "PUT", body: JSON.stringify({ subGroupIds }) }),
  items: {
    list: (groupId: number) => request<GroupItem[]>(`/groups/${groupId}/items`),
    create: (groupId: number, data: GroupItemCreate) =>
      request<GroupItem>(`/groups/${groupId}/items`, { method: "POST", body: JSON.stringify(data) }),
    update: (groupId: number, itemId: number, data: GroupItemUpdate) =>
      request<GroupItem>(`/groups/${groupId}/items/${itemId}`, { method: "PUT", body: JSON.stringify(data) }),
    delete: (groupId: number, itemId: number) =>
      request<void>(`/groups/${groupId}/items/${itemId}`, { method: "DELETE" }),
    reorder: (groupId: number, data: GroupItemsReorder) =>
      request<void>(`/groups/${groupId}/items/reorder`, { method: "PUT", body: JSON.stringify(data) }),
    fromSpans: (groupId: number, data: GroupItemsFromSpans) =>
      request<GroupItem[]>(`/groups/${groupId}/items/from-spans`, { method: "POST", body: JSON.stringify(data) }),
    playbackManifest: (groupId: number) =>
      request<GroupPlaybackManifest>(`/groups/${groupId}/playback-manifest`),
  },
};

export const segmentDisplayProfiles = {
  list: () => request<SegmentDisplayProfile[]>("/segment-display-profiles"),
  get: (id: number) => request<SegmentDisplayProfile>(`/segment-display-profiles/${id}`),
  create: (data: SegmentDisplayProfileCreate) =>
    request<SegmentDisplayProfile>("/segment-display-profiles", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: SegmentDisplayProfileUpdate) =>
    request<SegmentDisplayProfile>(`/segment-display-profiles/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/segment-display-profiles/${id}`, { method: "DELETE" }),
  setDefault: (id: number) => request<SegmentDisplayProfile>(`/segment-display-profiles/${id}/default`, { method: "PUT" }),
  preview: (data: import("./types").SegmentDisplayProfilePreviewRequest) =>
    request<import("./types").ResolvedSpanList>("/segment-display-profiles/preview", { method: "POST", body: JSON.stringify(data) }),
  rules: {
    list: (profileId: number) => request<SegmentDisplayRule[]>(`/segment-display-profiles/${profileId}/rules`),
    create: (profileId: number, data: SegmentDisplayRuleCreate) =>
      request<SegmentDisplayRule>(`/segment-display-profiles/${profileId}/rules`, { method: "POST", body: JSON.stringify(data) }),
    update: (profileId: number, ruleId: number, data: SegmentDisplayRuleUpdate) =>
      request<SegmentDisplayRule>(`/segment-display-profiles/${profileId}/rules/${ruleId}`, { method: "PUT", body: JSON.stringify(data) }),
    delete: (profileId: number, ruleId: number) =>
      request<void>(`/segment-display-profiles/${profileId}/rules/${ruleId}`, { method: "DELETE" }),
  },
};

export const segmentSpans = {
  search: (data: SegmentSpanSearchRequest) =>
    request<SegmentSpanSearchResponse>("/segments/spans/search", { method: "POST", body: JSON.stringify(data) }),
};

// ===== Entity Images =====
async function uploadImage(path: string, file: File): Promise<{ blobId: string }> {
  const formData = new FormData();
  formData.append("file", file);
  const res = await authedFetch(`${API_BASE}${path}`, { method: "POST", body: formData });
  if (!res.ok) throw new Error(`Upload failed: ${res.status}`);
  return res.json();
}

async function deleteImage(path: string): Promise<void> {
  const res = await authedFetch(`${API_BASE}${path}`, { method: "DELETE" });
  if (!res.ok && res.status !== 404) throw new Error(`Delete failed: ${res.status}`);
}

export const entityImages = {
  sceneCoverUrl: (id: number, version?: string, max = 1600) => buildMediaUrl(`/scenes/${id}/image`, version, max),
  uploadSceneCoverImage: (id: number, file: File) => uploadImage(`/scenes/${id}/image`, file),
  deleteSceneCoverImage: (id: number) => deleteImage(`/scenes/${id}/image`),

  performerImageUrl: (id: number, version?: string, max = 640) => buildMediaUrl(`/performers/${id}/image`, version, max),
  uploadPerformerImage: (id: number, file: File) => uploadImage(`/performers/${id}/image`, file),
  deletePerformerImage: (id: number) => deleteImage(`/performers/${id}/image`),

  studioImageUrl: (id: number, version?: string, max = 640) => buildMediaUrl(`/studios/${id}/image`, version, max),
  uploadStudioImage: (id: number, file: File) => uploadImage(`/studios/${id}/image`, file),
  deleteStudioImage: (id: number) => deleteImage(`/studios/${id}/image`),

  tagImageUrl: (id: number, version?: string, max = 640) => buildMediaUrl(`/tags/${id}/image`, version, max),
  uploadTagImage: (id: number, file: File) => uploadImage(`/tags/${id}/image`, file),
  deleteTagImage: (id: number) => deleteImage(`/tags/${id}/image`),

  groupFrontImageUrl: (id: number, version?: string, max = 640) => buildMediaUrl(`/groups/${id}/image/front`, version, max),
  uploadGroupFrontImage: (id: number, file: File) => uploadImage(`/groups/${id}/image/front`, file),
  deleteGroupFrontImage: (id: number) => deleteImage(`/groups/${id}/image/front`),

  groupBackImageUrl: (id: number, version?: string, max = 640) => buildMediaUrl(`/groups/${id}/image/back`, version, max),
  uploadGroupBackImage: (id: number, file: File) => uploadImage(`/groups/${id}/image/back`, file),
  deleteGroupBackImage: (id: number) => deleteImage(`/groups/${id}/image/back`),
};

// ===== System =====
export const system = {
  status: () => request<SystemStatus>("/system/status"),
  stats: () => request<Stats>("/system/stats"),
  getConfig: () => request<CoveConfig>("/system/config"),
  saveConfig: (config: CoveConfig) =>
    request<CoveConfig>("/system/config", { method: "PUT", body: JSON.stringify(config) }),
  listScrapers: () => request<ScraperSummary[]>("/system/scrapers"),
  reloadScrapers: () => request<ScraperSummary[]>("/system/scrapers/reload", { method: "POST" }),
  scrapeUrl: (scraperId: string, entityType: string, url: string) =>
    request<Record<string, unknown>>("/system/scrapers/scrape-url", { method: "POST", body: JSON.stringify({ scraperId, entityType, url }) }),
  scrapeName: (scraperId: string, entityType: string, name: string) =>
    request<Record<string, unknown>[]>("/system/scrapers/scrape-name", { method: "POST", body: JSON.stringify({ scraperId, entityType, name }) }),
  scrapeFragment: (scraperId: string, entityType: string, fragment: Record<string, unknown>) =>
    request<Record<string, unknown>>("/system/scrapers/scrape-fragment", { method: "POST", body: JSON.stringify({ scraperId, entityType, fragment }) }),
  listDownloaders: () => request<DownloaderDescriptor[]>("/system/downloaders"),
  matchDownloaders: (data: DownloaderMatchRequest) => request<DownloaderMatch[]>("/system/downloaders/match", { method: "POST", body: JSON.stringify(data) }),
  startDownload: (data: DownloaderStartRequest) => request<{ jobId: string }>("/system/downloaders/download", { method: "POST", body: JSON.stringify(data) }),
  startBatchDownload: (data: DownloaderBatchStartRequest) => request<DownloaderBatchStartResponse>("/system/downloaders/download-batch", { method: "POST", body: JSON.stringify(data) }),
  preflightDownload: (data: DownloaderPreflightRequest) => request<DownloaderPreflightResponse>("/system/downloaders/preflight", { method: "POST", body: JSON.stringify(data) }),
  validateMetadataServer: (metadataServer: MetadataServer) =>
    request<MetadataServerValidationResult>("/system/metadata-servers/validate", { method: "POST", body: JSON.stringify(metadataServer) }),
  configureUI: (input: Record<string, unknown>) =>
    request<{ success: boolean }>("/system/config/ui", { method: "POST", body: JSON.stringify(input) }),
  configureUISetting: (key: string, value: unknown) =>
    request<{ key: string; value: unknown; success: boolean }>(`/system/config/ui/${encodeURIComponent(key)}`, { method: "PUT", body: JSON.stringify(value) }),
};

export const scrapeAttempts = {
  list: (params?: { entityType?: string; entityId?: number; limit?: number }) => {
    const query = new URLSearchParams();
    if (params?.entityType) query.set("entityType", params.entityType);
    if (params?.entityId != null) query.set("entityId", String(params.entityId));
    if (params?.limit != null) query.set("limit", String(params.limit));
    const suffix = query.toString();
    return request<ScrapeAttempt[]>(`/scrape-attempts${suffix ? `?${suffix}` : ""}`);
  },
  get: (id: string) => request<ScrapeAttempt>(`/scrape-attempts/${id}`),
  create: (data: CreateScrapeAttemptRequest) =>
    request<ScrapeAttempt>("/scrape-attempts", { method: "POST", body: JSON.stringify(data) }),
  applyScene: (id: string, data: ApplySceneScrapeAttemptRequest) =>
    request<ScrapeAttempt>(`/scrape-attempts/${id}/apply`, { method: "POST", body: JSON.stringify(data) }),
  startSceneBatch: (data: BatchSceneScrapeStartRequest) =>
    request<{ jobId: string; queuedCount: number }>("/scrape-attempts/batch-scenes", { method: "POST", body: JSON.stringify(data) }),
};

// ===== Jobs =====
export const jobs = {
  list: () => request<JobInfo[]>("/jobs"),
  history: () => request<JobInfo[]>("/jobs/history"),
  get: (id: string) => request<JobInfo>(`/jobs/${id}`),
  cancel: (id: string) => request<void>(`/jobs/${id}`, { method: "DELETE" }),
};

export const aiFaces = {
  rejectReferenceSuggestion: (faceId: number, data: { referenceSuggestionId: number }) =>
    request<void>(`/ext/ai-faces/reference/faces/${faceId}/reject`, { method: "POST", body: JSON.stringify(data) }),
  importReferencePerformer: (faceId: number, data: { referenceSuggestionId: number }) =>
    request<void>(`/ext/ai-faces/reference/faces/${faceId}/import-performer`, { method: "POST", body: JSON.stringify(data) }),
};

// ===== Metadata Tasks =====
export interface ScanOptions {
  paths?: string[];
  scanGenerateCovers?: boolean;
  scanGeneratePreviews?: boolean;
  scanGenerateSprites?: boolean;
  scanGeneratePhashes?: boolean;
  scanGenerateMd5?: boolean;
  scanGenerateThumbnails?: boolean;
  scanGenerateImagePhashes?: boolean;
  rescan?: boolean;
}

export interface GenerateOptions {
  thumbnails?: boolean;
  previews?: boolean;
  sprites?: boolean;
  markers?: boolean;
  phashes?: boolean;
  md5?: boolean;
  imageThumbnails?: boolean;
  imagePhashes?: boolean;
  overwrite?: boolean;
  sceneIds?: number[];
  paths?: string[];
}

export interface CleanOptions {
  paths?: string[];
  dryRun?: boolean;
}

export interface CleanGeneratedOptions {
  screenshots?: boolean;
  sprites?: boolean;
  transcodes?: boolean;
  markers?: boolean;
  imageThumbnails?: boolean;
  dryRun?: boolean;
}

export interface ExportOptions {
  includeScenes?: boolean;
  includePerformers?: boolean;
  includeStudios?: boolean;
  includeTags?: boolean;
  includeGalleries?: boolean;
  includeGroups?: boolean;
}

export const metadata = {
  scan: (opts?: ScanOptions) =>
    request<{ jobId: string }>("/metadata/scan", { method: "POST", body: JSON.stringify(opts ?? {}) }),
  generate: (opts?: GenerateOptions) =>
    request<{ jobId: string }>("/metadata/generate", { method: "POST", body: JSON.stringify(opts ?? {}) }),
  autoTag: (opts?: { performers?: string[]; studios?: string[]; tags?: string[] }) =>
    request<{ jobId: string }>("/metadata/auto-tag", { method: "POST", body: JSON.stringify(opts ?? {}) }),
  clean: (opts?: CleanOptions) =>
    request<{ jobId: string }>("/metadata/clean", { method: "POST", body: JSON.stringify(opts ?? {}) }),
  cleanGenerated: (opts?: CleanGeneratedOptions) =>
    request<{ jobId: string }>("/metadata/clean-generated", { method: "POST", body: JSON.stringify(opts ?? {}) }),
  export: (opts?: ExportOptions) =>
    request<{ jobId: string }>("/metadata/export", { method: "POST", body: JSON.stringify(opts ?? {}) }),
  identify: (opts?: {
    sources?: string[];
    sceneIds?: number[];
    setCoverImage?: boolean;
    setTags?: boolean;
    setPerformers?: boolean;
    setStudio?: boolean;
    createTags?: boolean;
    createPerformers?: boolean;
    createStudios?: boolean;
    markOrganized?: boolean;
    skipMultipleMatches?: boolean;
    skipSingleNamePerformers?: boolean;
  }) =>
    request<{ jobId: string }>("/metadata/identify", { method: "POST", body: JSON.stringify(opts ?? {}) }),
  import: (opts?: { filePath: string; duplicateHandling?: boolean }) =>
    request<{ jobId: string }>("/metadata/import", { method: "POST", body: JSON.stringify(opts ?? {}) }),
};

// ===== Database =====
export const database = {
  backup: () => request<{ backupPath: string; sizeBytes: number; timestamp: string }>("/database/backup", { method: "POST" }),
  restore: (backupPath: string) =>
    request<{ message: string; backupPath: string }>("/database/restore", {
      method: "POST",
      body: JSON.stringify({ backupPath }),
    }),
  latestBackup: async () => {
    const result = await requestOptional<{ path: string }>("/jobs/backup/latest");
    return result?.path ?? null;
  },
  optimize: () => request<void>("/database/optimize", { method: "POST" }),
  wipe: () =>
    request<{ message: string; backupPath: string; timestamp: string; configBackupPath: string | null }>(
      "/database/wipe",
      { method: "POST" },
    ),
  backupConfig: () =>
    request<{ backupPath: string; sizeBytes: number; timestamp: string }>("/database/config/backup", { method: "POST" }),
  restoreConfig: (backupPath: string) =>
    request<{ message: string; backupPath: string }>("/database/config/restore", {
      method: "POST",
      body: JSON.stringify({ backupPath }),
    }),
  latestConfigBackup: async () => {
    const result = await requestOptional<{ path: string | null }>("/database/config/latest-backup");
    return result?.path ?? null;
  },
};

// ===== Stash Migration =====
export interface StashPreviewResult {
  isValid: boolean;
  error: string | null;
  scenes: number;
  performers: number;
  tags: number;
  studios: number;
  groups: number;
  images: number;
  galleries: number;
}
export interface StashImportResult {
  scenes: number;
  performers: number;
  tags: number;
  studios: number;
  groups: number;
  images: number;
  galleries: number;
}
export interface StashAiImportResult {
  aiRuns: number;
  segments: number;
}
export interface StashImportOptions {
  coveGeneratedPath?: string;
  migrateGeneratedContent?: boolean;
  aiDataSource?: string;
}
export const stashMigration = {
  preview: (stashDbPath: string) =>
    request<StashPreviewResult>("/stash-migration/preview", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ stashDbPath }),
    }),
  startImport: (stashDbPath: string, options?: StashImportOptions) =>
    request<{ jobId: string }>("/stash-migration/import", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        stashDbPath,
        generatedPath: options?.coveGeneratedPath,
        migrateGeneratedContent: options?.migrateGeneratedContent ?? true,
        aiDataSource: options?.aiDataSource,
      }),
    }),
  importResult: (jobId: string) => requestOptional<StashImportResult>(`/stash-migration/import/${jobId}`),
  startAiImport: (stashDbPath: string, aiDataSource: string) =>
    request<{ jobId: string }>("/stash-migration/import-ai", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ stashDbPath, aiDataSource }),
    }),
  aiImportResult: (jobId: string) => requestOptional<StashAiImportResult>(`/stash-migration/import-ai/${jobId}`),
};

// ===== Logs =====
export interface LogEntry {
  timestamp: string;
  level: string;
  message: string;
  exception?: string;
}

export const logs = {
  recent: (level?: string, limit?: number) => {
    const params = new URLSearchParams();
    if (level) params.set("level", level);
    if (limit) params.set("limit", String(limit));
    const qs = params.toString();
    return request<LogEntry[]>(`/logs${qs ? `?${qs}` : ""}`);
  },
};

// ===== Saved Filters =====
export const savedFilters = {
  list: (mode?: string) => request<SavedFilter[]>(`/savedfilters${mode ? `?mode=${mode}` : ""}`),
  get: (id: number) => request<SavedFilter>(`/savedfilters/${id}`),
  create: (data: SavedFilterCreate) => request<SavedFilter>("/savedfilters", { method: "POST", body: JSON.stringify(data) }),
  update: (id: number, data: SavedFilterUpdate) => request<SavedFilter>(`/savedfilters/${id}`, { method: "PUT", body: JSON.stringify(data) }),
  delete: (id: number) => request<void>(`/savedfilters/${id}`, { method: "DELETE" }),
  getDefault: (mode: string) => request<SavedFilter | null>(`/savedfilters/default/${mode}`),
  setDefault: (mode: string, filterId: number | null) =>
    request<void>(`/savedfilters/default/${mode}`, { method: "PUT", body: JSON.stringify({ filterId }) }),
};

// ===== Plugins =====
export const plugins = {
  list: () => request<Plugin[]>("/plugins"),
  getTasks: () => request<PluginTask[]>("/plugins/tasks"),
  runTask: (data: RunPluginTaskRequest) => request<{ jobId: string }>("/plugins/run-task", { method: "POST", body: JSON.stringify(data) }),
  saveSettings: (data: PluginSettings) => request<void>("/plugins/settings", { method: "POST", body: JSON.stringify(data) }),
  reload: () => request<{ message: string }>("/plugins/reload", { method: "POST" }),
  getConfig: (pluginId: string) => request<Record<string, unknown>>(`/plugins/${encodeURIComponent(pluginId)}/config`),
  setConfig: (pluginId: string, values: Record<string, unknown>) =>
    request<void>(`/plugins/${encodeURIComponent(pluginId)}/config`, { method: "POST", body: JSON.stringify(values) }),
  installedPackages: (type?: string) => request<Package[]>(`/plugins/packages/installed${type ? `?type=${type}` : ""}`),
  availablePackages: (type?: string, source?: string) => {
    const params = new URLSearchParams();
    if (type) params.set("type", type);
    if (source) params.set("source", source);
    const qs = params.toString();
    return request<Package[]>(`/plugins/packages/available${qs ? `?${qs}` : ""}`);
  },
  installPackages: (packages: { id: string; sourceUrl: string }[]) =>
    request<{ jobId: string }>("/plugins/packages/install", { method: "POST", body: JSON.stringify({ packages }) }),
  updatePackages: (packages?: { id: string; sourceUrl: string }[]) =>
    request<{ jobId: string }>("/plugins/packages/update", { method: "POST", body: JSON.stringify(packages ? { packages } : {}) }),
  uninstallPackages: (ids: string[]) =>
    request<{ uninstalled: string[] }>("/plugins/packages/uninstall", { method: "POST", body: JSON.stringify(ids) }),
};

// ===== Extensions =====
export const extensions = {
  getManifest: () => request<ExtensionManifest>("/extensions/manifest"),
  invokeAction: <T = unknown>(apiEndpoint: string, payload: unknown) =>
    request<T>(normalizeApiPath(apiEndpoint), {
      method: "POST",
      body: JSON.stringify(payload),
    }),
  list: (category?: string) =>
    request<ExtensionInfo[]>(category ? `/extensions?category=${encodeURIComponent(category)}` : "/extensions"),
  enable: (id: string) => request<void>(`/extensions/${encodeURIComponent(id)}/enable`, { method: "POST" }),
  disable: (id: string) => request<void>(`/extensions/${encodeURIComponent(id)}/disable`, { method: "POST" }),
  getData: (id: string) => request<Record<string, string>>(`/extensions/${encodeURIComponent(id)}/data`),
  setData: (id: string, key: string, value: string) =>
    request<void>(`/extensions/${encodeURIComponent(id)}/data/${encodeURIComponent(key)}`, {
      method: "PUT",
      body: JSON.stringify(value),
    }),
  runJob: (id: string, jobId: string, parameters?: Record<string, string>) =>
    request<{ message: string }>(`/extensions/${encodeURIComponent(id)}/jobs/${encodeURIComponent(jobId)}/run`, {
      method: "POST",
      body: JSON.stringify(parameters ?? null),
    }),
  assetUrl: (extensionId: string, path: string) => `${API_BASE}/extensions/assets/${encodeURIComponent(extensionId)}/${path}`,
  /** Get all available extension categories. */
  getCategories: () => request<string[]>("/extensions/categories"),
  /** Validate all extension dependencies. */
  validateDependencies: () => request<DependencyProblem[]>("/extensions/dependencies/validate"),
  /** Get missing dependencies for a specific extension. */
  getMissingDependencies: (id: string) =>
    request<string[]>(`/extensions/${encodeURIComponent(id)}/dependencies/missing`),
  /** Registry: search for extensions. */
  registrySearch: (params: { q?: string; category?: string; sort?: string; page?: number; pageSize?: number }) => {
    const qs = new URLSearchParams();
    if (params.q) qs.set("q", params.q);
    if (params.category) qs.set("category", params.category);
    if (params.sort) qs.set("sort", params.sort);
    if (params.page) qs.set("page", String(params.page));
    if (params.pageSize) qs.set("pageSize", String(params.pageSize));
    return request<RegistrySearchResult>(`/extensions/registry/search?${qs.toString()}`);
  },
  /** Registry: get extension detail. */
  registryGetExtension: (extensionId: string) =>
    request<RegistryExtensionDetail>(`/extensions/registry/${encodeURIComponent(extensionId)}`),
  /** Registry: check for updates. */
  registryCheckUpdates: () => request<RegistryUpdateInfo[]>("/extensions/registry/updates"),
  /** Registry: get categories. */
  registryGetCategories: () => request<string[]>("/extensions/registry/categories"),
  /** Registry: install an extension. */
  registryInstall: (extensionId: string, version: string, installDependencies = false) =>
    request<{ message: string; path: string; requiresDependencies?: boolean; missingDependencies?: DependencyInfo[]; installedDependencies?: string[] }>("/extensions/registry/install", {
      method: "POST",
      body: JSON.stringify({ extensionId, version, installDependencies }),
    }),
  /** Registry: resolve dependencies for an extension. */
  registryResolveDependencies: (extensionId: string) =>
    request<DependencyInfo[]>(`/extensions/registry/${extensionId}/dependencies`),
  /** Registry: uninstall an extension. */
  registryUninstall: (extensionId: string) =>
    request<{ message: string }>("/extensions/registry/uninstall", {
      method: "POST",
      body: JSON.stringify({ extensionId }),
    }),
};

// ===== Auth / RBAC =====
export interface UserRow {
  id: number;
  username: string;
  displayName?: string | null;
  email?: string | null;
  isActive: boolean;
  isLocked: boolean;
  isSystem: boolean;
  mustChangePassword: boolean;
  lastLoginAt?: string | null;
  lastLoginIp?: string | null;
  createdAt: string;
  roles: string[];
}
export interface RoleRow {
  id: number;
  name: string;
  description?: string | null;
  isBuiltin: boolean;
  isSystem: boolean;
  source: string;
  permissions: string[];
}
export interface PermissionInfo {
  key: string;
  category: string;
  description: string;
  source: string;
  dangerous: boolean;
  implies: string[];
}
export interface AuditEventRow {
  id: number;
  occurredAt: string;
  actorUserId?: number | null;
  actorUsername?: string | null;
  actorKind: string;
  ip?: string | null;
  action: string;
  targetKind?: string | null;
  targetId?: string | null;
  outcome: string;
  detail?: string | null;
}
export interface ContentRuleRow {
  id: number;
  roleId: number;
  roleName: string;
  entityKind: string;
  effect: "allow" | "deny";
  scopeKind: "all" | "tag" | "studio" | "identifier" | "attribute" | "expression";
  scopeValue: string;
  appliesTo: "read" | "write" | "delete" | "all";
  createdAt: string;
  updatedAt: string;
}
export interface EntityOverrideRow {
  id: number;
  roleId: number;
  roleName: string;
  entityKind: string;
  entityId: string;
  effect: "allow" | "deny";
  appliesTo: "read" | "write" | "delete" | "all";
  createdAt: string;
}
export interface ApiTokenRow {
  id: string;
  name: string;
  prefix: string;
  scope: string[] | null;
  createdAt: string;
  lastUsedAt: string | null;
  expiresAt: string | null;
}
export interface ApiTokenIssuedRow extends ApiTokenRow {
  plaintextToken: string;
}
export interface ShareLinkRow {
  id: string;
  createdByUserId: number | null;
  createdByUsername: string | null;
  entityKind: string;
  entityIds: string[];
  createdAt: string;
  expiresAt: string | null;
  viewCount: number;
  hasPassword: boolean;
  revoked: boolean;
}
export interface ShareLinkIssuedRow {
  id: string;
  plaintextToken: string;
  entityKind: string;
  entityIds: string[];
  createdAt: string;
  expiresAt: string | null;
  hasPassword: boolean;
}

export const auth = {
  me: () => request<MeResponse>("/auth/me"),
  login: (username: string, password: string) =>
    request<{ token: string; refreshToken: string; user: unknown; username: string }>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ username, password }),
    }),
  logout: (refreshToken: string) =>
    request<{ message: string }>("/auth/logout", { method: "POST", body: JSON.stringify({ refreshToken }) }),
  changePassword: (currentPassword: string, newPassword: string) =>
    request<{ message: string }>("/auth/change-password", {
      method: "POST",
      body: JSON.stringify({ currentPassword, newPassword }),
    }),
  updateUiPreferences: (preferences: UserUiPreferences | null) =>
    request<UserUiPreferences | null>("/auth/me/ui-preferences", {
      method: "PUT",
      body: JSON.stringify(preferences ?? {}),
    }),
};

export const usersApi = {
  list: () => request<UserRow[]>("/users"),
  get: (id: number) => request<UserRow>(`/users/${id}`),
  create: (req: { username: string; password: string; displayName?: string; email?: string; roles?: string[]; mustChangePassword?: boolean }) =>
    request<UserRow>("/users", { method: "POST", body: JSON.stringify(req) }),
  update: (id: number, req: { displayName?: string; email?: string; isActive?: boolean; mustChangePassword?: boolean }) =>
    request<UserRow>(`/users/${id}`, { method: "PUT", body: JSON.stringify(req) }),
  remove: (id: number) => request<void>(`/users/${id}`, { method: "DELETE" }),
  setRoles: (id: number, roles: string[]) =>
    request<UserRow>(`/users/${id}/roles`, { method: "POST", body: JSON.stringify({ roles }) }),
  adminChangePassword: (id: number, newPassword: string) =>
    request<void>(`/users/${id}/password`, { method: "POST", body: JSON.stringify({ newPassword }) }),
  unlock: (id: number) => request<void>(`/users/${id}/unlock`, { method: "POST" }),
};

export const rolesApi = {
  list: () => request<RoleRow[]>("/roles"),
  get: (id: number) => request<RoleRow>(`/roles/${id}`),
  permissions: () => request<PermissionInfo[]>("/roles/permissions"),
  create: (req: { name: string; description?: string; permissions?: string[] }) =>
    request<RoleRow>("/roles", { method: "POST", body: JSON.stringify(req) }),
  update: (id: number, req: { description?: string; permissions?: string[] }) =>
    request<RoleRow>(`/roles/${id}`, { method: "PUT", body: JSON.stringify(req) }),
  remove: (id: number) => request<void>(`/roles/${id}`, { method: "DELETE" }),
};

export const auditApi = {
  list: (opts?: { action?: string; actor?: string; outcome?: string; page?: number; perPage?: number }) => {
    const params = new URLSearchParams();
    if (opts?.action) params.set("action", opts.action);
    if (opts?.actor) params.set("actor", opts.actor);
    if (opts?.outcome) params.set("outcome", opts.outcome);
    if (opts?.page) params.set("page", String(opts.page));
    if (opts?.perPage) params.set("perPage", String(opts.perPage));
    const qs = params.toString();
    return request<{ items: AuditEventRow[]; totalCount: number; page: number; perPage: number }>(
      `/audit${qs ? "?" + qs : ""}`
    );
  },
};

export const contentRulesApi = {
  list: (roleId?: number) => {
    const qs = roleId ? `?roleId=${roleId}` : "";
    return request<ContentRuleRow[]>(`/content-rules${qs}`);
  },
  create: (req: { roleId: number; entityKind: string; effect: string; scopeKind: string; scopeValue: string; appliesTo: string }) =>
    request<ContentRuleRow>("/content-rules", { method: "POST", body: JSON.stringify(req) }),
  update: (id: number, req: Partial<Pick<ContentRuleRow, "effect" | "scopeKind" | "scopeValue" | "appliesTo">>) =>
    request<ContentRuleRow>(`/content-rules/${id}`, { method: "PUT", body: JSON.stringify(req) }),
  remove: (id: number) => request<void>(`/content-rules/${id}`, { method: "DELETE" }),
  listOverrides: (roleId?: number, entityKind?: string) => {
    const params = new URLSearchParams();
    if (roleId) params.set("roleId", String(roleId));
    if (entityKind) params.set("entityKind", entityKind);
    const qs = params.toString();
    return request<EntityOverrideRow[]>(`/content-rules/overrides${qs ? `?${qs}` : ""}`);
  },
  createOverride: (req: { roleId: number; entityKind: string; entityId: string; effect: string; appliesTo: string }) =>
    request<EntityOverrideRow>("/content-rules/overrides", { method: "POST", body: JSON.stringify(req) }),
  removeOverride: (id: number) => request<void>(`/content-rules/overrides/${id}`, { method: "DELETE" }),
};

export const apiTokensApi = {
  list: () => request<ApiTokenRow[]>("/apitokens"),
  create: (req: { name: string; scope?: string[]; expiresAt?: string }) =>
    request<ApiTokenIssuedRow>("/apitokens", { method: "POST", body: JSON.stringify(req) }),
  revoke: (id: string) => request<void>(`/apitokens/${id}`, { method: "DELETE" }),
};

export const shareLinksApi = {
  list: () => request<ShareLinkRow[]>("/share-links"),
  create: (req: { entityKind: string; entityIds: string[]; expiresAt?: string; password?: string }) =>
    request<ShareLinkIssuedRow>("/share-links", { method: "POST", body: JSON.stringify(req) }),
  revoke: (id: string) => request<void>(`/share-links/${id}`, { method: "DELETE" }),
};
