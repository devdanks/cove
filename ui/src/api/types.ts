// ===== Entity Types =====

export interface Scene {
  id: number;
  title?: string;
  code?: string;
  details?: string;
  director?: string;
  date?: string;
  rating?: number;
  organized: boolean;
  studioId?: number;
  studioName?: string;
  resumeTime: number;
  playDuration: number;
  playCount: number;
  lastPlayedAt?: string;
  oCounter: number;
  urls: string[];
  tags: Tag[];
  performers: PerformerSummary[];
  files: VideoFile[];
  groups: GroupSummary[];
  galleries: GallerySummary[];
  remoteIds: SceneRemoteId[];
  customFields?: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface SceneRemoteId {
  endpoint: string;
  remoteId: string;
}

export interface SceneCreate {
  title?: string;
  code?: string;
  details?: string;
  director?: string;
  date?: string;
  rating?: number;
  organized?: boolean;
  studioId?: number;
  urls?: string[];
  tagIds?: number[];
  performerIds?: number[];
  galleryIds?: number[];
  groups?: { groupId: number; sceneIndex: number }[];
  customFields?: Record<string, unknown>;
}

export interface SceneUpdate extends Partial<SceneCreate> {}

export interface Performer {
  id: number;
  name: string;
  imagePath?: string;
  disambiguation?: string;
  gender?: string;
  birthdate?: string;
  deathDate?: string;
  ethnicity?: string;
  country?: string;
  eyeColor?: string;
  hairColor?: string;
  heightCm?: number;
  weight?: number;
  measurements?: string;
  fakeTits?: string;
  penisLength?: number;
  circumcised?: string;
  careerStart?: string;
  careerEnd?: string;
  tattoos?: string;
  piercings?: string;
  favorite: boolean;
  rating?: number;
  details?: string;
  ignoreAutoTag: boolean;
  urls: string[];
  aliases: string[];
  tags: Tag[];
  remoteIds: PerformerRemoteId[];
  sceneCount: number;
  imageCount: number;
  galleryCount: number;
  groupCount: number;
  customFields?: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface PerformerRemoteId {
  endpoint: string;
  remoteId: string;
}

export interface PerformerSummary {
  id: number;
  name: string;
  disambiguation?: string;
  gender?: string;
  birthdate?: string;
  favorite: boolean;
  imagePath?: string;
}

export interface PerformerCreate {
  name: string;
  disambiguation?: string;
  gender?: string;
  birthdate?: string;
  deathDate?: string;
  ethnicity?: string;
  country?: string;
  eyeColor?: string;
  hairColor?: string;
  heightCm?: number;
  weight?: number;
  measurements?: string;
  fakeTits?: string;
  penisLength?: number;
  circumcised?: string;
  careerStart?: string;
  careerEnd?: string;
  tattoos?: string;
  piercings?: string;
  favorite?: boolean;
  rating?: number;
  details?: string;
  ignoreAutoTag?: boolean;
  urls?: string[];
  aliases?: string[];
  tagIds?: number[];
  customFields?: Record<string, unknown>;
}

export interface PerformerUpdate extends Partial<PerformerCreate> {}

export interface PerformerScrapeRequest {
  inputKind?: "url" | "name";
  scraperId?: string;
  url?: string;
  name?: string;
  createMissingTags?: boolean;
}

export interface ScrapedPerformer {
  name?: string;
  disambiguation?: string;
  gender?: string;
  birthdate?: string;
  country?: string;
  ethnicity?: string;
  eyeColor?: string;
  hairColor?: string;
  heightCm?: number;
  weight?: number;
  measurements?: string;
  tattoos?: string;
  piercings?: string;
  details?: string;
  imageUrl?: string;
  urls: string[];
  aliases: string[];
  tagNames: string[];
}

export interface PerformerScrapePreview {
  scraped: ScrapedPerformer;
  inputKind: "url" | "name";
  sourceValue?: string;
}

export interface Tag {
  id: number;
  name: string;
  description?: string;
  imagePath?: string;
  favorite: boolean;
  ignoreAutoTag: boolean;
  showAsSegment?: boolean | null;
  segmentColorOverride?: string | null;
  segmentLaneOverride?: number | null;
  aliases: string[];
  sceneCount?: number;
  segmentCount?: number;
  imageCount?: number;
  galleryCount?: number;
  groupCount?: number;
  performerCount?: number;
  studioCount?: number;
  provenance?: TagProvenance[];
}

export interface TagProvenance {
  sourceKey: string;
  sourceRunId?: string;
  modelKey?: string;
  confidence?: number;
  appliedAt: string;
}

export interface TagDetail extends Tag {
  sortName?: string;
  parents: Tag[];
  children: Tag[];
  sceneCount: number;
  performerCount: number;
  imageCount: number;
  galleryCount: number;
  studioCount: number;
  groupCount: number;
  segmentCount: number;
  customFields?: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface TagGraphNode {
  id: number;
  name: string;
  favorite: boolean;
  description?: string;
  imagePath?: string;
  parentIds: number[];
  childIds: number[];
  totalUsageCount: number;
  sceneCount: number;
  segmentCount: number;
  imageCount: number;
  galleryCount: number;
  groupCount: number;
  performerCount: number;
  studioCount: number;
}

export interface TagGraphLink {
  sourceId: number;
  targetId: number;
}

export interface TagGraphResponse {
  items: TagGraphNode[];
  links: TagGraphLink[];
  totalCount: number;
}

export interface TagCreate {
  name: string;
  sortName?: string;
  description?: string;
  favorite?: boolean;
  ignoreAutoTag?: boolean;
  showAsSegment?: boolean | null;
  segmentColorOverride?: string | null;
  segmentLaneOverride?: number | null;
  aliases?: string[];
  parentIds?: number[];
  childIds?: number[];
  customFields?: Record<string, unknown>;
}

export interface TagUpdate extends Partial<TagCreate> {}

export interface Studio {
  id: number;
  name: string;
  imagePath?: string;
  parentId?: number;
  parentName?: string;
  rating?: number;
  favorite: boolean;
  details?: string;
  ignoreAutoTag: boolean;
  organized: boolean;
  urls: string[];
  aliases: string[];
  tags: Tag[];
  remoteIds: StudioRemoteId[];
  sceneCount: number;
  imageCount: number;
  galleryCount: number;
  groupCount: number;
  performerCount: number;
  childStudioCount: number;
  customFields?: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface StudioRemoteId {
  endpoint: string;
  remoteId: string;
}

export interface StudioCreate {
  name: string;
  parentId?: number;
  rating?: number;
  favorite?: boolean;
  details?: string;
  ignoreAutoTag?: boolean;
  organized?: boolean;
  urls?: string[];
  aliases?: string[];
  tagIds?: number[];
  customFields?: Record<string, unknown>;
}

export interface StudioUpdate extends Partial<StudioCreate> {}

export interface Gallery {
  id: number;
  title?: string;
  code?: string;
  date?: string;
  details?: string;
  photographer?: string;
  rating?: number;
  organized: boolean;
  coverPath?: string;
  coverImageId?: number;
  studioId?: number;
  studioName?: string;
  urls: string[];
  tags: Tag[];
  performers: PerformerSummary[];
  imageCount: number;
  sceneCount: number;
  sceneIds: number[];
  folderPath?: string;
  files: GalleryFileInfo[];
  customFields?: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface GalleryFileInfo {
  id: number;
  path: string;
  size: number;
  modTime: string;
  fingerprints: { type: string; value: string }[];
}

export interface GalleryChapter {
  id: number;
  title: string;
  imageIndex: number;
  galleryId: number;
  createdAt: string;
  updatedAt: string;
}

export interface GalleryChapterCreate {
  title: string;
  imageIndex: number;
}

export interface GalleryChapterUpdate {
  title?: string;
  imageIndex?: number;
}

export interface GalleryCreate {
  title?: string;
  code?: string;
  date?: string;
  details?: string;
  photographer?: string;
  rating?: number;
  organized?: boolean;
  studioId?: number;
  urls?: string[];
  tagIds?: number[];
  performerIds?: number[];
  sceneIds?: number[];
  customFields?: Record<string, unknown>;
}

export interface GalleryUpdate extends Partial<GalleryCreate> {}

export interface ImageFile {
  id: number;
  path: string;
  basename: string;
  format: string;
  width: number;
  height: number;
  size: number;
}

export interface Image {
  id: number;
  title?: string;
  code?: string;
  details?: string;
  photographer?: string;
  rating?: number;
  organized: boolean;
  oCounter: number;
  studioId?: number;
  studioName?: string;
  date?: string;
  urls: string[];
  tags: Tag[];
  performers: PerformerSummary[];
  galleryCount: number;
  galleryIds: number[];
  galleries: GallerySummary[];
  files: ImageFile[];
  customFields?: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface ImageCreate {
  title?: string;
  code?: string;
  details?: string;
  photographer?: string;
  rating?: number;
  organized?: boolean;
  studioId?: number;
  date?: string;
  urls?: string[];
  tagIds?: number[];
  performerIds?: number[];
  galleryIds?: number[];
  customFields?: Record<string, unknown>;
}

export interface ImageUpdate {
  title?: string;
  code?: string;
  details?: string;
  photographer?: string;
  rating?: number;
  organized?: boolean;
  studioId?: number;
  date?: string;
  urls?: string[];
  tagIds?: number[];
  performerIds?: number[];
  galleryIds?: number[];
  customFields?: Record<string, unknown>;
}

export interface Group {
  id: number;
  name: string;
  aliases?: string;
  duration?: number;
  date?: string;
  rating?: number;
  studioId?: number;
  studioName?: string;
  director?: string;
  synopsis?: string;
  frontImagePath?: string;
  backImagePath?: string;
  urls: string[];
  tags: Tag[];
  sceneCount: number;
  isCompilation?: boolean;
  subGroupCount: number;
  containingGroupCount: number;
  customFields?: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
}

export interface GroupSummary {
  id: number;
  name: string;
  sceneIndex: number;
}

export type GroupItemKind = "scene" | "sceneRange";

export interface GroupItem {
  id: number;
  groupId: number;
  orderIndex: number;
  kind: GroupItemKind;
  sceneId: number;
  sceneTitle?: string;
  startSec?: number;
  endSec?: number;
  title?: string;
  notes?: string;
  sourceSpanKey?: string;
  sourceProfileId?: number;
  sourceQueryJson?: string;
  snapshotAt?: string;
  createdAt: string;
  updatedAt: string;
}

export interface GroupItemCreate {
  orderIndex: number;
  kind: GroupItemKind;
  sceneId: number;
  startSec?: number;
  endSec?: number;
  title?: string;
  notes?: string;
  sourceSpanKey?: string;
  sourceProfileId?: number;
  sourceQueryJson?: string;
}

export interface GroupItemUpdate {
  orderIndex: number;
  kind: GroupItemKind;
  startSec?: number;
  endSec?: number;
  title?: string;
  notes?: string;
}

export interface GroupItemsReorder {
  ids: number[];
}

export interface GroupItemSpanInput {
  spanKey?: string;
  sceneId?: number;
  startSec?: number;
  endSec?: number;
  title?: string;
  profileId?: number;
  derivedQuery?: SegmentSpanDerivedQuery;
}

export interface GroupItemsFromSpans {
  spans: GroupItemSpanInput[];
}

export interface GroupPlaybackManifestItem {
  groupItemId: number;
  sceneId: number;
  sceneTitle?: string;
  src: string;
  startSec: number;
  endSec?: number;
  durationSec?: number;
  posterPath?: string;
  title?: string;
}

export interface GroupPlaybackManifest {
  items: GroupPlaybackManifestItem[];
}

export interface GallerySummary {
  id: number;
  title?: string;
  date?: string;
}

export interface GroupCreate {
  name: string;
  aliases?: string;
  duration?: number;
  date?: string;
  rating?: number;
  studioId?: number;
  director?: string;
  synopsis?: string;
  urls?: string[];
  tagIds?: number[];
  customFields?: Record<string, unknown>;
}

export interface GroupUpdate extends Partial<GroupCreate> {}

export interface VideoFile {
  id: number;
  path: string;
  basename: string;
  format: string;
  width: number;
  height: number;
  duration: number;
  videoCodec: string;
  audioCodec: string;
  frameRate: number;
  bitRate: number;
  size: number;
  fingerprints: Fingerprint[];
  captions?: Caption[];
}

export interface Caption {
  id: number;
  languageCode: string;
  captionType: string;
  filename: string;
}

export interface Fingerprint {
  type: string;
  value: string;
}

export interface TagSegmentWall {
  id: number;
  title?: string;
  startSec: number;
  endSec?: number;
  kind: string;
  sourceKey: string;
  confidence?: number;
  sceneId: number;
  sceneTitle: string;
}

export type SegmentHostType = "scene" | "image" | "audio";
export type DetectionHostType = "scene" | "image";
export type AffinityHostType = "scene" | "image" | "performer" | "face" | "tag" | "studio" | "gallery" | "group";
export type InteractionHostType = AffinityHostType | "segment" | "search" | "collection";

export interface Segment {
  id: number;
  hostType: SegmentHostType;
  hostId: number;
  startSec: number;
  endSec?: number;
  tagId?: number;
  tagName?: string;
  kind?: string;
  refId?: number;
  payload?: unknown;
  sourceKey: string;
  sourceRunId?: string;
  confidence?: number;
  title?: string;
  colorHint?: string;
  createdAt: string;
  updatedAt: string;
}

export interface SegmentRecord extends Segment {
  hostTitle?: string;
}

export interface SegmentCreate {
  startSec: number;
  endSec?: number;
  tagId?: number;
  kind?: string;
  refId?: number;
  payload?: unknown;
  sourceKey?: string;
  sourceRunId?: string;
  confidence?: number;
  title?: string;
  colorHint?: string;
}

export interface SegmentUpdate extends SegmentCreate {
  sourceKey: string;
}

export interface ResolvedSpan {
  spanKey: string;
  hostType: SegmentHostType;
  hostId: number;
  startSec: number;
  endSec: number;
  sourceKey?: string;
  kind?: string;
  tagId?: number;
  tagName?: string;
  colorHint?: string;
  lane?: number;
  collapsedToInstant: boolean;
  segmentIds: number[];
}

export interface ResolvedSpanInterval {
  startSec: number;
  endSec: number;
}

export interface ResolvedSpanDetail {
  span: ResolvedSpan;
  sceneId: number;
  sceneTitle?: string;
  intervals: ResolvedSpanInterval[];
  profileId: number;
  profileVersion: number;
}

export interface SceneResolvedSpans {
  spans: ResolvedSpan[];
  profileId: number;
  profileVersion: number;
}

export interface ResolvedSpanList {
  spans: ResolvedSpan[];
}

export interface SegmentDerivedQueryOperandDescriptor {
  sourceKey?: string;
  kind?: string;
  tagIds?: number[];
  performerIds?: number[];
  faceIds?: number[];
  minConfidence?: number;
}

export interface SegmentDerivedQueryDescriptor {
  operator: SegmentSpanOperator;
  operands: SegmentDerivedQueryOperandDescriptor[];
  mergeGapSec?: number;
  minDurationSec?: number;
}

export type SegmentSpanOperator = "union" | "intersection" | "difference";

export interface SegmentSpanOperand {
  sourceKey?: string;
  kind?: string;
  tagIds?: number[];
  refIds?: number[];
  minConfidence?: number;
}

export interface SegmentSpanQueryRequest {
  profile?: number;
  operator: SegmentSpanOperator;
  operands: SegmentSpanOperand[];
  mergeGapSec?: number;
  minDurationSec?: number;
}

export interface SegmentDisplayProfile {
  id: number;
  name: string;
  description?: string;
  userId?: number;
  isSystem: boolean;
  isDefault: boolean;
  version: number;
  createdAt: string;
  updatedAt: string;
}

export interface SegmentDisplayProfileCreate {
  name: string;
  description?: string;
  isDefault: boolean;
}

export interface SegmentDisplayProfileUpdate {
  name: string;
  description?: string;
}

export interface SegmentSpanDerivedQuery {
  operator: SegmentSpanOperator;
  operands: SegmentSpanOperand[];
  mergeGapSec?: number;
  minDurationSec?: number;
}

export interface SegmentSpanSearchRequest {
  profile?: number;
  derivedQuery?: SegmentSpanDerivedQuery;
  page?: number;
  perPage?: number;
  sort?: string;
  direction?: "asc" | "desc";
  q?: string;
  sceneTitle?: string;
  sceneIds?: number[];
  excludeSceneIds?: number[];
}

export interface SegmentSpanSearchResultItem {
  span: ResolvedSpan;
  sceneId: number;
  sceneTitle?: string;
  sceneUpdatedAt?: string;
  profileId: number;
}

export interface SegmentSpanSearchResponse {
  items: SegmentSpanSearchResultItem[];
  totalCount: number;
  page: number;
  perPage: number;
}

export interface SegmentDistinctValue {
  value: string;
  count: number;
}

export interface SegmentDisplayRule {
  id: number;
  sourceKey?: string;
  kind?: string;
  tagId?: number;
  tagName?: string;
  tagCategory?: string;
  hostType?: SegmentHostType;
  visible: boolean;
  minConfidence?: number;
  minDurationSec?: number;
  mergeGapSec?: number;
  collapseToInstant: boolean;
  colorOverride?: string;
  lane?: number;
  priority?: number;
  userId?: number;
  createdAt: string;
  updatedAt: string;
}

export interface SegmentDisplayRuleCreate {
  sourceKey?: string;
  kind?: string;
  tagId?: number;
  tagCategory?: string;
  hostType?: SegmentHostType;
  visible: boolean;
  minConfidence?: number;
  minDurationSec?: number;
  mergeGapSec?: number;
  collapseToInstant: boolean;
  colorOverride?: string;
  lane?: number;
  priority?: number;
}

export interface SegmentDisplayRuleUpdate extends SegmentDisplayRuleCreate {}

export interface SegmentDisplayProfilePreviewRequest {
  sceneId: number;
  rules: SegmentDisplayRuleCreate[];
}

export interface Detection {
  id: number;
  hostType: DetectionHostType;
  hostId: number;
  observedAtSec?: number;
  frameWidth: number;
  frameHeight: number;
  class: string;
  score: number;
  x: number;
  y: number;
  w: number;
  h: number;
  extra?: unknown;
  refKind?: string;
  refId?: number;
  groupKey?: string;
  sourceKey: string;
  sourceRunId?: string;
  createdAt: string;
  updatedAt: string;
}

export interface DetectionCreate {
  observedAtSec?: number;
  frameWidth: number;
  frameHeight: number;
  class: string;
  score: number;
  x: number;
  y: number;
  w: number;
  h: number;
  extra?: unknown;
  refKind?: string;
  refId?: number;
  groupKey?: string;
  sourceKey?: string;
  sourceRunId?: string;
}

export interface DetectionUpdate extends DetectionCreate {
  sourceKey: string;
}

export interface Face {
  id: number;
  label?: string;
  performerId?: number;
  performerName?: string;
  coverImageUrl?: string;
  mergedIntoFaceId?: number;
  detectionCount: number;
  sceneCount: number;
  imageCount: number;
  primarySourceKey?: string;
  createdAt: string;
  updatedAt: string;
}

export interface FaceCreate {
  label?: string;
  performerId?: number;
  primarySourceKey?: string;
}

export interface FaceUpdate {
  label?: string;
  performerId?: number;
  primarySourceKey?: string;
}

export interface FaceLink {
  performerId?: number;
}

export interface FaceMerge {
  targetFaceId: number;
}


export interface FaceDeleteImpact {
  detectionCount: number;
  embeddingCount: number;
  segmentCount: number;
  hasCoverImage: boolean;
  releasedMergedFaceCount: number;
}

export interface FaceSimilar {
  id: number;
  label?: string;
  performerId?: number;
  performerName?: string;
  coverImageUrl?: string;
  distance: number;
}

export type AiDataKind = "embedding" | "detection" | "segment" | "tagApplication" | "face";

export interface AiDataSelector {
  sourceKey?: string;
  sourceRunId?: string;
  model?: string;
  modality?: string;
  hostType?: string;
  hostId?: number;
  kinds?: AiDataKind[];
}

export interface AiDataSummaryItem {
  kind: string;
  detail?: string;
  sourceKey: string;
  sourceRunId?: string;
  model?: string;
  hostType: string;
  count: number;
}

export interface AiDataSummary {
  items: AiDataSummaryItem[];
  totals: Record<string, number>;
  totalCount: number;
}

export interface AiDataPurgeResult {
  removedCounts: Record<string, number>;
}

export interface EntityEngagement {
  hostId: number;
  isFavorite: boolean;
  rating?: number;
  resumeTime: number;
  playDuration: number;
  playCount: number;
  lastPlayedAt?: string;
  oCount: number;
  completeCount: number;
}

export interface EntityRatings {
  hostId: number;
  ratings: Record<string, number>;
}

export interface EntityFavorite {
  isFavorite: boolean;
}

export interface EngagementInteractionWrite {
  hostType: InteractionHostType;
  hostId?: number;
  kind: string;
  positionSec?: number;
  durationSec?: number;
  sessionId?: string;
  meta?: Record<string, unknown>;
}

export interface EngagementInteraction {
  id: number;
  hostType: InteractionHostType;
  hostId?: number;
  kind: string;
  at: string;
  positionSec?: number;
  durationSec?: number;
  sessionId?: string;
  meta?: Record<string, unknown>;
}

export interface SceneInteractionEvent {
  kind: string;
  at: string;
  meta?: unknown;
}

export interface PlaybackIntervalInput {
  startSec: number;
  endSec: number;
}

export interface PlaybackIntervalsRequest {
  hostType: string;
  hostId: number;
  sessionId: string;
  mediaDurationSec: number;
  currentPositionSec: number;
  state: string;
  intervals: PlaybackIntervalInput[];
}

export interface PlaybackInterval {
  startSec: number;
  endSec: number;
  recordedAt: string;
}

export interface ScenePlaybackSession {
  sessionId: string;
  startedAt: string;
  lastSeenAt: string;
  endedAt?: string | null;
  state: string;
  mediaDurationSec: number;
  totalWatchedSec: number;
  lastPositionSec?: number | null;
  isCompleted: boolean;
  intervals: PlaybackInterval[];
}

export interface SceneHistory {
  playHistory: string[];
  oHistory: string[];
  events?: SceneInteractionEvent[];
  allTimeWatchedIntervals?: PlaybackInterval[];
  totalDistinctWatchedSec?: number;
  sessions?: ScenePlaybackSession[];
}

export interface EntityEngagementBatchRequest {
  hostType: AffinityHostType;
  hostIds: number[];
}

export interface PaginatedResponse<T> {
  items: T[];
  totalCount: number;
  page: number;
  perPage: number;
}

export interface Stats {
  sceneCount: number;
  imageCount: number;
  galleryCount: number;
  performerCount: number;
  studioCount: number;
  tagCount: number;
  groupCount: number;
  totalFileSize: number;
  totalPlayDuration: number;
}

export interface SystemStatus {
  version: string;
  appDir: string | null;
  configFile: string | null;
  databasePath: string;
  migrationRequired: boolean;
  pendingMigrations: string[] | null;
  authEnabled?: boolean;
}

export type RatingSystemType = "stars" | "decimal";
export type RatingStarPrecision = "full" | "half" | "quarter" | "tenth";

export interface RatingSystemOptions {
  type: RatingSystemType;
  starPrecision: RatingStarPrecision;
}

export type AuthUserKind = "user" | "shareLink" | "apiToken" | "system" | "anonymous";

export interface UserThemePreferences {
  activeThemeId?: string | null;
  activeComponentStyles?: string[] | null;
  activeLayoutStyle?: string | null;
  customThemeColors?: Record<string, string> | null;
  styleOptions?: Record<string, Record<string, string>> | null;
}

export interface UserUiPreferences {
  theme?: UserThemePreferences | null;
  ratingSystemOptions?: RatingSystemOptions | null;
  recordPlaybackHistory?: boolean | null;
}

export interface MeResponse {
  user: {
    id: string;
    username: string;
    roles?: string[];
    kind?: AuthUserKind;
    uiPreferences?: UserUiPreferences | null;
  };
  permissions: string[];
  readGrantedEntityKinds?: string[];
}

export interface InterfaceConfig {
  language?: string;
  menuItems: string[];
  handyConnectionEnabled: boolean;
  handyKey?: string;
  defaultDurationForImages?: number;
  disableDropdownCreatePerformer: boolean;
  disableDropdownCreateStudio: boolean;
  disableDropdownCreateTag: boolean;
}

export interface UiConfig {
  title?: string;
  abbreviateCounters: boolean;
  ratingSystemOptions: RatingSystemOptions;
  showStudioAsText: boolean;
  customCss?: string;
  customJs?: string;
  enableCSSCustomization: boolean;
  enableJSCustomization: boolean;
  customLocalesPath?: string;
  autostartVideo: boolean;
  autostartVideoOnPlaySelected: boolean;
  continuePlaylistDefault: boolean;
  showAbLoopControls: boolean;
  trackActivity: boolean;
  soundOnPreview: boolean;
  previewSegmentDuration: number;
  previewSegments: number;
  previewExcludeStart: string;
  previewExcludeEnd: string;
  wallShowTitle: boolean;
  wallPlayback: number;
  deleteFileDefault: boolean;
  slideshowDelay: number;
  noBrowser: boolean;
  notificationsEnabled: boolean;
}

export interface SecurityConfig {
  enabled: boolean;
  username?: string;
  maxSessionAgeMinutes: number;
  newPassword?: string;
}

export interface PackageSource {
  name: string;
  url: string;
}

export interface MetadataServer {
  endpoint: string;
  apiKey: string;
  name: string;
  maxRequestsPerMinute: number;
}

export interface IdentifyDefaultsConfig {
  createTags: boolean;
  createPerformers: boolean;
  createStudios: boolean;
  autoApplyMaxDurationDifferenceSeconds?: number;
  autoApplyMaxPhashDistance?: number;
}

export interface MetadataBatchDefaultsConfig {
  refreshAlreadyTagged: boolean;
  createParentStudios: boolean;
  excludeFields: string[];
}

export interface ScraperPreference {
  site: string;
  scraperId: string;
}

export interface ScrapingConfig {
  scraperDirectories: string[];
  scraperPackageSources: PackageSource[];
  metadataServers: MetadataServer[];
  scraperPreferences: ScraperPreference[];
  identifyDefaults: IdentifyDefaultsConfig;
  metadataBatchDefaults: MetadataBatchDefaultsConfig;
}

export interface CoveConfig {
  covePaths: CovePathConfig[];
  downloaderPathOverrides: DownloaderPathOverrideConfig[];
  generatedPath?: string;
  cachePath?: string;
  host: string;
  port: number;
  maxParallelTasks: number;
  maxConcurrentDownloads: number;
  calculateMd5: boolean;
  enableFfmpegHwAccel: boolean;
  videoExtensions: string[];
  imageExtensions: string[];
  galleryExtensions: string[];
  excludePatterns: string[];
  excludeImagePatterns: string[];
  excludeGalleryPatterns: string[];
  createGalleriesFromFolders: boolean;
  writeImageThumbnails: boolean;
  createImageClipsFromVideos: boolean;
  galleryCoverRegex: string;
  deleteGeneratedDefault: boolean;
  maxTranscodeSize: number;
  maxStreamingTranscodeSize: number;
  transcodeHardwareAcceleration: string;
  transcodeInputArgs?: string;
  transcodeOutputArgs?: string;
  liveTranscodeInputArgs?: string;
  liveTranscodeOutputArgs?: string;
  drawFunscriptHeatmapRange: boolean;
  previewPreset: string;
  previewAudio: string;
  logLevel: string;
  logFile?: string;
  logOut: boolean;
  logAccess: boolean;
  ffmpegPath?: string;
  ffprobePath?: string;
  interface: InterfaceConfig;
  ui: UiConfig;
  security: SecurityConfig;
  scraping: ScrapingConfig;
}

export interface CovePathConfig {
  path: string;
  excludeVideo: boolean;
  excludeImage: boolean;
  excludeAudio: boolean;
}

export interface DownloaderPathOverrideConfig {
  downloaderId: string;
  site?: string;
  path: string;
}

export interface JobInfo {
  id: string;
  type: string;
  description: string;
  status: "pending" | "running" | "completed" | "failed" | "cancelled";
  progress: number;
  subTask?: string;
  startedAt: string;
  completedAt?: string;
  error?: string;
}

export interface FindFilter {
  q?: string;
  page?: number;
  perPage?: number;
  sort?: string;
  direction?: "asc" | "desc";
  seed?: number;
}

export interface SavedFilter {
  id: number;
  mode: string;
  name: string;
  findFilter?: string;
  objectFilter?: string;
  uiOptions?: string;
}

export interface SavedFilterCreate {
  mode: string;
  name: string;
  findFilter?: string;
  objectFilter?: string;
  uiOptions?: string;
}

export interface SavedFilterUpdate {
  mode?: string;
  name?: string;
  findFilter?: string;
  objectFilter?: string;
  uiOptions?: string;
}

export interface ScraperSummary {
  id: string;
  name: string;
  entityType: string;
  supportedScrapes: string[];
  urls: string[];
  sourcePath: string;
}

export interface ScrapeAttempt {
  id: string;
  scraperId: string;
  entityType: string;
  entityId?: number | null;
  inputKind: string;
  inputJson?: string | null;
  resultJson?: string | null;
  candidateResultsJson?: string | null;
  entitySnapshotJson?: string | null;
  status: string;
  error?: string | null;
  createdAt: string;
  appliedAt?: string | null;
}

export interface CreateScrapeAttemptRequest {
  scraperId: string;
  entityType: string;
  entityId?: number;
  inputKind: string;
  url?: string;
  name?: string;
  fragment?: Record<string, unknown>;
}

export interface ApplySceneScrapeAttemptRequest {
  replaceFields?: string[];
  collectionModes?: Record<string, string>;
  createMissingTags?: boolean;
  createMissingPerformers?: boolean;
  createMissingStudio?: boolean;
  markOrganized?: boolean;
  hydratePerformers?: boolean;
  selectedCandidateIndex?: number;
}

export interface BatchSceneScrapeStartRequest {
  scraperId: string;
  inputKind: "url" | "name";
  sceneIds: number[];
  autoApply?: boolean;
  createMissingTags?: boolean;
  createMissingPerformers?: boolean;
  createMissingStudio?: boolean;
  markOrganized?: boolean;
  hydratePerformers?: boolean;
}

export interface DownloaderDescriptor {
  id: string;
  name: string;
  supportedEntity: string;
  supportedUrlPatterns: string[];
  capabilities: string[];
}

export interface DownloaderQualityOption {
  id: string;
  label: string;
  description?: string;
}

export interface DownloaderMatch {
  downloaderId: string;
  downloaderName: string;
  supportedEntity: string;
  normalizedUrl: string;
  label?: string;
  qualityOptions: DownloaderQualityOption[];
}

export interface DownloaderMatchRequest {
  url: string;
}

export interface DownloaderPreflightRequest {
  url: string;
  entity: string;
  entityId?: number;
}

export interface DownloaderPreflightResponse {
  isDuplicate: boolean;
  duplicateReason?: string;
}

export interface DownloaderStartRequest {
  downloaderId: string;
  url: string;
  entity: string;
  entityId?: number;
  qualityId?: string;
  autoApplyMetadata?: boolean;
  allowDuplicateDownload?: boolean;
}

export interface DownloaderBatchGenerateOptions {
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

export interface DownloaderBatchItem {
  downloaderId?: string;
  url: string;
  entity: string;
  entityId?: number;
  qualityId?: string;
  label?: string;
  title?: string;
  createEntityIfMissing?: boolean;
  autoApplyMetadata?: boolean;
}

export interface DownloaderBatchFollowUp {
  scrapeScenes?: boolean;
  allowDuplicateDownloads?: boolean;
  generate?: DownloaderBatchGenerateOptions;
}

export interface DownloaderBatchStartRequest {
  items: DownloaderBatchItem[];
  followUp?: DownloaderBatchFollowUp;
}

export interface DownloaderBatchStartResponse {
  jobId: string;
  queuedCount: number;
}

export interface MetadataServerValidationResult {
  valid: boolean;
  status: string;
  username?: string;
}

export interface MetadataServerPerformerMatch {
  endpoint: string;
  serverName: string;
  id: string;
  name: string;
  disambiguation?: string;
  gender?: string;
  birthDate?: string;
  country?: string;
  imageUrl?: string;
  deleted: boolean;
  mergedIntoId?: string;
  aliases: string[];
  urls: string[];
}

export interface MetadataServerPerformerImportRequest {
  endpoint: string;
  performerId: string;
}

export interface MetadataServerFindByIdsRequest {
  endpoint: string;
  ids: string[];
}

export interface MetadataServerPerformerBatchTagRequest {
  endpoint: string;
  ids?: number[];
  filter?: PerformerFilterCriteria;
  selectAll?: boolean;
  refreshAlreadyTagged?: boolean;
  excludeFields?: string[];
}

export interface MetadataServerStudioMatch {
  endpoint: string;
  serverName: string;
  id: string;
  name: string;
  imageUrl?: string;
  aliases: string[];
  urls: string[];
  parentName?: string;
}

export interface MetadataServerStudioImportRequest {
  endpoint: string;
  studioId: string;
}

export interface MetadataServerStudioBatchTagRequest {
  endpoint: string;
  ids?: number[];
  filter?: StudioFilterCriteria;
  selectAll?: boolean;
  refreshAlreadyTagged?: boolean;
  excludeFields?: string[];
  createParentStudios?: boolean;
}

export interface MetadataServerTagMatch {
  endpoint: string;
  metadataServerName: string;
  id: string;
  name: string;
  description?: string;
  aliases: string[];
}

export interface MetadataServerTagImportRequest {
  endpoint: string;
  tagId: string;
}

export interface MetadataServerTagBatchTagRequest {
  endpoint: string;
  ids?: number[];
  filter?: TagFilterCriteria;
  selectAll?: boolean;
  refreshAlreadyTagged?: boolean;
  excludeFields?: string[];
}

export interface MetadataServerEntityCandidate {
  remoteId: string;
  name: string;
  existsLocally: boolean;
  localId?: number;
}

export interface MetadataServerSceneEntityOverride {
  remoteId: string;
  name: string;
  action: string;
  localId?: number;
}

export interface MetadataServerSceneMatch {
  endpoint: string;
  serverName: string;
  id: string;
  title?: string;
  code?: string;
  date?: string;
  director?: string;
  details?: string;
  studioName?: string;
  imageUrl?: string;
  duration?: number;
  performerNames: string[];
  tagNames: string[];
  urls: string[];
  fingerprintAlgorithms: string[];
  matchCount: number;
  fingerprints: MetadataServerFingerprint[];
  studioCandidate?: MetadataServerEntityCandidate;
  performerCandidates: MetadataServerEntityCandidate[];
  tagCandidates: MetadataServerEntityCandidate[];
}

export interface MetadataServerFingerprint {
  algorithm: string;
  hash: string;
  duration?: number;
}

export interface MetadataServerSceneImportRequest {
  endpoint: string;
  sceneId: string;
  setCoverImage?: boolean;
  setTags?: boolean;
  setPerformers?: boolean;
  setStudio?: boolean;
  onlyExistingTags?: boolean;
  onlyExistingPerformers?: boolean;
  onlyExistingStudio?: boolean;
  markOrganized?: boolean;
  excludedTagNames?: string[];
  excludedPerformerNames?: string[];
  studioOverride?: MetadataServerSceneEntityOverride;
  performerOverrides?: MetadataServerSceneEntityOverride[];
  tagOverrides?: MetadataServerSceneEntityOverride[];
}

// ===== Filter Criteria =====

export type CriterionModifier =
  | "EQUALS" | "NOT_EQUALS" | "GREATER_THAN" | "LESS_THAN"
  | "INCLUDES" | "EXCLUDES" | "INCLUDES_ALL" | "EXCLUDES_ALL"
  | "IS_NULL" | "NOT_NULL" | "BETWEEN" | "NOT_BETWEEN"
  | "MATCHES_REGEX" | "NOT_MATCHES_REGEX";

export interface IntCriterion {
  value: number;
  value2?: number;
  modifier: CriterionModifier;
}

export interface StringCriterion {
  value: string;
  modifier: CriterionModifier;
}

export interface BoolCriterion {
  value: boolean;
}

export interface MultiIdCriterion {
  value: number[];
  modifier: CriterionModifier;
  excludes?: number[];
  depth?: number;
}

export interface DateCriterion {
  value: string;
  value2?: string;
  modifier: CriterionModifier;
}

export interface TimestampCriterion {
  value: string;
  value2?: string;
  modifier: CriterionModifier;
}

export type FingerprintAlgorithm = "md5" | "oshash" | "phash";

export interface FingerprintCriterion {
  type: FingerprintAlgorithm;
  value: string;
  modifier: CriterionModifier;
}

export interface SceneFilterCriteria {
  title?: string;
  code?: string;
  path?: string;
  rating?: number;
  organized?: boolean;
  studioId?: number;
  groupId?: number;
  tagIds?: number[];
  performerIds?: number[];
  ratingCriterion?: IntCriterion;
  oCounterCriterion?: IntCriterion;
  durationCriterion?: IntCriterion;
  resolutionCriterion?: IntCriterion;
  playCountCriterion?: IntCriterion;
  performerCountCriterion?: IntCriterion;
  tagsCriterion?: MultiIdCriterion;
  performersCriterion?: MultiIdCriterion;
  studiosCriterion?: MultiIdCriterion;
  groupsCriterion?: MultiIdCriterion;
  organizedCriterion?: BoolCriterion;
  interactiveCriterion?: BoolCriterion;
  pathCriterion?: StringCriterion;
  fingerprintCriterion?: FingerprintCriterion;
  hashCriterion?: StringCriterion;
  checksumCriterion?: StringCriterion;
  duplicatedPhashCriterion?: BoolCriterion;
  urlCriterion?: StringCriterion;
  dateCriterion?: DateCriterion;
  createdAtCriterion?: TimestampCriterion;
  updatedAtCriterion?: TimestampCriterion;
  performerFavoriteCriterion?: BoolCriterion;
  videoCodecCriterion?: StringCriterion;
  audioCodecCriterion?: StringCriterion;
  frameRateCriterion?: IntCriterion;
  bitrateInterval?: IntCriterion;
  fileCountCriterion?: IntCriterion;
  remoteIdCriterion?: StringCriterion;
  isMissingCriterion?: BoolCriterion;
  duplicatedCriterion?: StringCriterion;
  titleCriterion?: StringCriterion;
  codeCriterion?: StringCriterion;
  detailsCriterion?: StringCriterion;
  directorCriterion?: StringCriterion;
  tagCountCriterion?: IntCriterion;
  resumeTimeCriterion?: IntCriterion;
  playDurationCriterion?: IntCriterion;
  lastPlayedAtCriterion?: TimestampCriterion;
  galleriesCriterion?: MultiIdCriterion;
  performerTagsCriterion?: MultiIdCriterion;
  performerAgeCriterion?: IntCriterion;
  captionsCriterion?: StringCriterion;
  interactiveSpeedCriterion?: IntCriterion;
  orientationCriterion?: StringCriterion;
}

export interface PerformerFilterCriteria {
  name?: string;
  favorite?: boolean;
  rating?: number;
  tagIds?: number[];
  nameCriterion?: StringCriterion;
  ratingCriterion?: IntCriterion;
  ageCriterion?: IntCriterion;
  genderCriterion?: StringCriterion;
  ethnicityCriterion?: StringCriterion;
  countryCriterion?: StringCriterion;
  favoriteCriterion?: BoolCriterion;
  tagsCriterion?: MultiIdCriterion;
  studiosCriterion?: MultiIdCriterion;
  sceneCountCriterion?: IntCriterion;
  studioCountCriterion?: IntCriterion;
  imageCountCriterion?: IntCriterion;
  galleryCountCriterion?: IntCriterion;
  birthdateCriterion?: DateCriterion;
  createdAtCriterion?: TimestampCriterion;
  updatedAtCriterion?: TimestampCriterion;
  pathCriterion?: StringCriterion;
  urlCriterion?: StringCriterion;
  weightCriterion?: IntCriterion;
  heightCriterion?: IntCriterion;
  isMissingCriterion?: BoolCriterion;
  remoteIdCriterion?: StringCriterion;
  remoteIdValueCriterion?: StringCriterion;
  disambiguationCriterion?: StringCriterion;
  detailsCriterion?: StringCriterion;
  eyeColorCriterion?: StringCriterion;
  hairColorCriterion?: StringCriterion;
  measurementsCriterion?: StringCriterion;
  fakeTitsCriterion?: StringCriterion;
  penisLengthCriterion?: IntCriterion;
  circumcisedCriterion?: StringCriterion;
  careerStartCriterion?: DateCriterion;
  careerEndCriterion?: DateCriterion;
  careerLengthCriterion?: IntCriterion;
  tattooCriterion?: StringCriterion;
  piercingsCriterion?: StringCriterion;
  aliasesCriterion?: StringCriterion;
  deathDateCriterion?: DateCriterion;
  playCountCriterion?: IntCriterion;
  oCounterCriterion?: IntCriterion;
  groupsCriterion?: MultiIdCriterion;
  ignoreAutoTagCriterion?: BoolCriterion;
  tagCountCriterion?: IntCriterion;
}

export interface TagFilterCriteria {
  name?: string;
  favorite?: boolean;
  favoriteCriterion?: BoolCriterion;
  sceneCountCriterion?: IntCriterion;
  sceneCountIncludesChildren?: boolean;
  performerCountCriterion?: IntCriterion;
  performerCountIncludesChildren?: boolean;
  parentsCriterion?: MultiIdCriterion;
  childrenCriterion?: MultiIdCriterion;
  isMissingCriterion?: BoolCriterion;
  createdAtCriterion?: TimestampCriterion;
  updatedAtCriterion?: TimestampCriterion;
  nameCriterion?: StringCriterion;
  sortNameCriterion?: StringCriterion;
  remoteIdCriterion?: StringCriterion;
  remoteIdValueCriterion?: StringCriterion;
  aliasesCriterion?: StringCriterion;
  descriptionCriterion?: StringCriterion;
  imageCountCriterion?: IntCriterion;
  imageCountIncludesChildren?: boolean;
  galleryCountCriterion?: IntCriterion;
  galleryCountIncludesChildren?: boolean;
  studioCountCriterion?: IntCriterion;
  studioCountIncludesChildren?: boolean;
  groupCountCriterion?: IntCriterion;
  groupCountIncludesChildren?: boolean;
  parentCountCriterion?: IntCriterion;
  childCountCriterion?: IntCriterion;
  ignoreAutoTagCriterion?: BoolCriterion;
}

export interface StudioFilterCriteria {
  name?: string;
  favorite?: boolean;
  parentId?: number;
  tagIds?: number[];
  ratingCriterion?: IntCriterion;
  favoriteCriterion?: BoolCriterion;
  tagsCriterion?: MultiIdCriterion;
  sceneCountCriterion?: IntCriterion;
  urlCriterion?: StringCriterion;
  remoteIdCriterion?: StringCriterion;
  isMissingCriterion?: BoolCriterion;
  createdAtCriterion?: TimestampCriterion;
  updatedAtCriterion?: TimestampCriterion;
  nameCriterion?: StringCriterion;
  detailsCriterion?: StringCriterion;
  aliasesCriterion?: StringCriterion;
  parentsCriterion?: MultiIdCriterion;
  childCountCriterion?: IntCriterion;
  tagCountCriterion?: IntCriterion;
  groupCountCriterion?: IntCriterion;
  ignoreAutoTagCriterion?: BoolCriterion;
  organizedCriterion?: BoolCriterion;
  galleryCountCriterion?: IntCriterion;
  imageCountCriterion?: IntCriterion;
}

export interface GalleryFilterCriteria {
  title?: string;
  rating?: number;
  organized?: boolean;
  studioId?: number;
  tagIds?: number[];
  performerIds?: number[];
  ratingCriterion?: IntCriterion;
  organizedCriterion?: BoolCriterion;
  tagsCriterion?: MultiIdCriterion;
  performersCriterion?: MultiIdCriterion;
  studiosCriterion?: MultiIdCriterion;
  imageCountCriterion?: IntCriterion;
  titleCriterion?: StringCriterion;
  dateCriterion?: DateCriterion;
  pathCriterion?: StringCriterion;
  fingerprintCriterion?: FingerprintCriterion;
  checksumCriterion?: StringCriterion;
  urlCriterion?: StringCriterion;
  createdAtCriterion?: TimestampCriterion;
  updatedAtCriterion?: TimestampCriterion;
  performerFavoriteCriterion?: BoolCriterion;
  isMissingCriterion?: BoolCriterion;
  codeCriterion?: StringCriterion;
  detailsCriterion?: StringCriterion;
  photographerCriterion?: StringCriterion;
  fileCountCriterion?: IntCriterion;
  tagCountCriterion?: IntCriterion;
  performerCountCriterion?: IntCriterion;
  performerAgeCriterion?: IntCriterion;
  typicalResolutionCriterion?: IntCriterion;
  scenesCriterion?: MultiIdCriterion;
  performerTagsCriterion?: MultiIdCriterion;
}

export interface ImageFilterCriteria {
  title?: string;
  rating?: number;
  organized?: boolean;
  studioId?: number;
  galleryId?: number;
  tagIds?: number[];
  performerIds?: number[];
  ratingCriterion?: IntCriterion;
  organizedCriterion?: BoolCriterion;
  tagsCriterion?: MultiIdCriterion;
  performersCriterion?: MultiIdCriterion;
  studiosCriterion?: MultiIdCriterion;
  galleriesCriterion?: MultiIdCriterion;
  titleCriterion?: StringCriterion;
  oCounterCriterion?: IntCriterion;
  resolutionCriterion?: IntCriterion;
  pathCriterion?: StringCriterion;
  fingerprintCriterion?: FingerprintCriterion;
  checksumCriterion?: StringCriterion;
  createdAtCriterion?: TimestampCriterion;
  updatedAtCriterion?: TimestampCriterion;
  performerFavoriteCriterion?: BoolCriterion;
  isMissingCriterion?: BoolCriterion;
  codeCriterion?: StringCriterion;
  detailsCriterion?: StringCriterion;
  photographerCriterion?: StringCriterion;
  urlCriterion?: StringCriterion;
  dateCriterion?: DateCriterion;
  fileCountCriterion?: IntCriterion;
  tagCountCriterion?: IntCriterion;
  performerCountCriterion?: IntCriterion;
  performerAgeCriterion?: IntCriterion;
  orientationCriterion?: StringCriterion;
  performerTagsCriterion?: MultiIdCriterion;
}

export interface GroupFilterCriteria {
  name?: string;
  rating?: number;
  studioId?: number;
  nameCriterion?: StringCriterion;
  ratingCriterion?: IntCriterion;
  durationCriterion?: IntCriterion;
  studiosCriterion?: MultiIdCriterion;
  tagsCriterion?: MultiIdCriterion;
  dateCriterion?: DateCriterion;
  urlCriterion?: StringCriterion;
  createdAtCriterion?: TimestampCriterion;
  updatedAtCriterion?: TimestampCriterion;
  isMissingCriterion?: BoolCriterion;
  directorCriterion?: StringCriterion;
  synopsisCriterion?: StringCriterion;
  performersCriterion?: MultiIdCriterion;
  sceneCountCriterion?: IntCriterion;
  tagCountCriterion?: IntCriterion;
}

export interface FilteredQueryRequest<T = Record<string, unknown>> {
  findFilter?: FindFilter;
  objectFilter?: T;
}

// ===== Bulk Edit Types =====

export type BulkUpdateMode = "SET" | "ADD" | "REMOVE";

export interface SceneGroupInput {
  groupId: number;
  sceneIndex: number;
}

export interface BulkSceneUpdate {
  ids: number[];
  rating?: number;
  organized?: boolean;
  studioId?: number | null;
  date?: string;
  code?: string;
  director?: string;
  tagIds?: number[];
  tagMode?: BulkUpdateMode;
  performerIds?: number[];
  performerMode?: BulkUpdateMode;
  groupIds?: SceneGroupInput[];
  groupMode?: BulkUpdateMode;
}

export interface BulkPerformerUpdate {
  ids: number[];
  rating?: number;
  favorite?: boolean;
  tagIds?: number[];
  tagMode?: BulkUpdateMode;
}

export interface BulkTagUpdate {
  ids: number[];
  description?: string;
  favorite?: boolean;
  ignoreAutoTag?: boolean;
  parentIds?: number[];
  parentMode?: BulkUpdateMode;
  childIds?: number[];
  childMode?: BulkUpdateMode;
}

export interface BulkStudioUpdate {
  ids: number[];
  rating?: number;
  favorite?: boolean;
  tagIds?: number[];
  tagMode?: BulkUpdateMode;
}

export interface BulkGalleryUpdate {
  ids: number[];
  rating?: number;
  organized?: boolean;
  studioId?: number | null;
  tagIds?: number[];
  tagMode?: BulkUpdateMode;
  performerIds?: number[];
  performerMode?: BulkUpdateMode;
}

export interface BulkImageUpdate {
  ids: number[];
  rating?: number;
  organized?: boolean;
  studioId?: number | null;
  tagIds?: number[];
  tagMode?: BulkUpdateMode;
  performerIds?: number[];
  performerMode?: BulkUpdateMode;
  galleryIds?: number[];
  galleryMode?: BulkUpdateMode;
}

export interface BulkGroupUpdate {
  ids: number[];
  rating?: number;
  studioId?: number | null;
  tagIds?: number[];
  tagMode?: BulkUpdateMode;
}

// ===== Plugin Types =====
export interface Plugin {
  id: string;
  name: string;
  description: string;
  version: string;
  enabled: boolean;
  tasks: PluginTask[];
  settings?: PluginSettingSchema[];
  url?: string;
}

export interface PluginSettingSchema {
  name: string;
  type: "STRING" | "NUMBER" | "BOOLEAN";
  displayName?: string;
  description?: string;
}

export interface PluginTask {
  name: string;
  description: string;
}

export interface RunPluginTaskRequest {
  pluginId: string;
  taskName: string;
  args?: Record<string, string>;
}

export interface PluginSettings {
  enabledMap: Record<string, boolean>;
}

export interface Package {
  name: string;
  description: string;
  version: string;
  sourceUrl: string;
  type: string;
  installed: boolean;
  installedVersion?: string;
}

// ===== Extension System Types =====
export interface ExtensionManifest {
  pages: ExtensionPageDef[];
  slots: ExtensionSlotContribution[];
  tabs: ExtensionTabContribution[];
  themes: ExtensionThemeDef[];
  componentStyles: ExtensionComponentStyleDef[];
  layoutStyles: ExtensionLayoutStyleDef[];
  settingsPanels: ExtensionSettingsPanel[];
  pageOverrides: ExtensionPageOverride[];
  dialogOverrides: ExtensionDialogOverride[];
  actions: ExtensionAction[];
  frontendRuntimeVersion?: string;
  jsBundleUrl?: string;
  cssBundleUrl?: string;
}

export interface ExtensionPageDef {
  route: string;
  label: string;
  icon?: string;
  detailRoute?: string;
  showInNav: boolean;
  navOrder: number;
  requiredPermission?: string;
  componentName?: string;
  extensionId?: string;
}

export interface ExtensionSlotContribution {
  id: string;
  slot: string;
  extensionId: string;
  contentType: "component" | "html";
  componentName?: string;
  html?: string;
  order: number;
}

export interface ExtensionTabContribution {
  key: string;
  label: string;
  pageType: string;
  extensionId: string;
  componentName: string;
  order: number;
  countEndpoint?: string;
  icon?: string;
}

export interface ExtensionThemeDef {
  id: string;
  name: string;
  description?: string;
  cssVariables?: Record<string, string>;
  cssUrl?: string;
  componentStyle?: string;
  layoutStyle?: string;
  backgroundAnimation?: string;
  colorScheme?: string;
}

export interface ExtensionComponentStyleDef {
  id: string;
  name: string;
  description?: string;
}

export interface ExtensionLayoutStyleDef {
  id: string;
  name: string;
  description?: string;
}

export interface ExtensionSettingsPanel {
  id: string;
  label: string;
  extensionId: string;
  componentName: string;
  order: number;
  targetTab?: string;
  targetSection?: string;
}

export interface ExtensionPageOverride {
  targetPage: string;
  extensionId: string;
  componentName: string;
  priority: number;
}

export interface ExtensionDialogOverride {
  dialogId: string;
  extensionId: string;
  componentName: string;
  priority: number;
}

export interface ExtensionAction {
  id: string;
  label: string;
  extensionId: string;
  /** "toolbar", "context-menu", "bulk" */
  actionType: string;
  entityTypes: string[];
  icon?: string;
  apiEndpoint?: string;
  handlerName?: string;
  order: number;
  pages?: string[];
}

export interface ExtensionInfo {
  id: string;
  name: string;
  version: string;
  description?: string;
  author?: string;
  url?: string;
  iconUrl?: string;
  enabled: boolean;
  hasUI: boolean;
  hasApi: boolean;
  hasState: boolean;
  hasJobs: boolean;
  hasEvents: boolean;
  hasData: boolean;
  hasMiddleware: boolean;
  hasActions: boolean;
  categories: string[];
  minCoveVersion?: string;
  dependencies: Record<string, string>;
  kind: string;
  source: string;
  installedAt?: string;
  jobs: { id: string; name: string; description?: string }[];
}

// ===== Registry Types =====
export interface RegistrySearchResult {
  items: RegistryExtensionSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface RegistryExtensionSummary {
  id: string;
  name: string;
  version: string;
  description?: string;
  author?: string;
  iconUrl?: string;
  kind?: string;
  categories: string[];
  updatedAt?: string;
  minCoveVersion?: string;
}

export interface RegistryExtensionDetail extends RegistryExtensionSummary {
  url?: string;
  readme?: string;
  changelog?: string;
  screenshots: string[];
  dependencies: Record<string, string>;
  versions: RegistryVersionInfo[];
}

export interface RegistryVersionInfo {
  version: string;
  releasedAt: string;
  changelog?: string;
  minCoveVersion?: string;
  checksum?: string;
}

export interface RegistryUpdateInfo {
  extensionId: string;
  currentVersion: string;
  latestVersion: string;
  changelog?: string;
}

export interface DependencyInfo {
  id: string;
  versionConstraint: string;
  name?: string;
  resolvedVersion?: string;
  available: boolean;
  installed: boolean;
}

export interface DependencyProblem {
  extensionId: string;
  dependencyId?: string;
  message: string;
}
