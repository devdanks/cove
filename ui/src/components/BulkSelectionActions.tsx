import { useEffect, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, Edit, Loader2, Trash2, Search, Play } from "lucide-react";
import { scenes as scenesApi, images, galleries, performers, groups, studios, tags, audios, texts } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { canDeleteEntity, canWriteEntity } from "../auth/visibility";
import type { Audio, DeleteEntityOptions, Scene } from "../api/types";
import type { TextDocument } from "../api/types";
import { BulkEditDialog, SCENE_BULK_FIELDS, IMAGE_BULK_FIELDS, GALLERY_BULK_FIELDS, PERFORMER_BULK_FIELDS, GROUP_BULK_FIELDS, STUDIO_BULK_FIELDS, TAG_BULK_FIELDS, AUDIO_BULK_FIELDS, TEXT_BULK_FIELDS } from "./BulkEditDialog";
import { BatchDownloadOptionsDialog } from "./BatchDownloadOptionsDialog";
import { ConfirmDialog } from "./ConfirmDialog";
import { IdentifyDialog } from "./IdentifyDialog";
import { SceneQueue } from "./SceneQueue";
import { ExtensionSelectionActions } from "./ExtensionSelectionActions";
import { MediaScrapeDialog } from "./MediaScrapeDialog";
import {
  DEFAULT_BATCH_DOWNLOAD_OPTIONS,
  formatBatchDownloadSummary,
  getBatchDownloadOptionsStorageKey,
  getUndownloadedSelectionItems,
  loadStoredBatchDownloadOptions,
  queueBatchDownloads,
  saveStoredBatchDownloadOptions,
  type BatchDownloadOptions,
  type DownloadSelectionEntity,
  type DownloadSelectionItem,
} from "../utils/batchDownloads";

const FIELDS_MAP = {
  scenes: SCENE_BULK_FIELDS,
  images: IMAGE_BULK_FIELDS,
  galleries: GALLERY_BULK_FIELDS,
  performers: PERFORMER_BULK_FIELDS,
  groups: GROUP_BULK_FIELDS,
  studios: STUDIO_BULK_FIELDS,
  tags: TAG_BULK_FIELDS,
  audios: AUDIO_BULK_FIELDS,
  texts: TEXT_BULK_FIELDS,
} as const;

const API_MAP = { scenes: scenesApi, images, galleries, performers, groups, studios, tags, audios, texts } as const;

const ENTITY_RESOURCE_MAP = {
  scenes: "scene",
  images: "image",
  galleries: "gallery",
  performers: "performer",
  groups: "group",
  studios: "studio",
  tags: "tag",
  audios: "audio",
  texts: "text",
} as const;

function getMutationErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : error ? String(error) : null;
}

interface Props {
  entityType: keyof typeof FIELDS_MAP;
  selectedIds: Set<number>;
  onDone: () => void;
  /** Raw scene items for Play/Identify (only needed when entityType is "scenes") */
  sceneItems?: Pick<Scene, "id" | "title" | "updatedAt" | "urls" | "files">[];
  audioItems?: Audio[];
  textItems?: TextDocument[];
  downloadItems?: DownloadSelectionItem[];
  /** Navigate callback for the scene queue player */
  onNavigate?: (route: any) => void;
}

export function BulkSelectionActions({ entityType, selectedIds, onDone, sceneItems, audioItems, textItems, downloadItems, onNavigate }: Props) {
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [showIdentify, setShowIdentify] = useState(false);
  const [showQueue, setShowQueue] = useState(false);
  const [showScrape, setShowScrape] = useState(false);
  const [showBatchDownloadOptions, setShowBatchDownloadOptions] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const { hasPermission } = useAuth();
  const queryClient = useQueryClient();
  const api = API_MAP[entityType];
  const fields = FIELDS_MAP[entityType];
  const resource = ENTITY_RESOURCE_MAP[entityType];
  const canWrite = canWriteEntity(resource, hasPermission);
  const canDelete = canDeleteEntity(resource, hasPermission);
  const supportsDeleteOptions = entityType === "scenes" || entityType === "images" || entityType === "audios" || entityType === "texts";

  const bulkDeleteMut = useMutation<void, Error, DeleteEntityOptions | undefined>({
    mutationFn: async (options) => {
      await api.bulkDelete([...selectedIds], options);
    },
    onSuccess: () => { queryClient.invalidateQueries(); setShowDeleteConfirm(false); onDone(); },
  });

  const bulkEditMut = useMutation<void, Error, Record<string, unknown>>({
    mutationFn: async (values) => {
      await api.bulkUpdate({ ids: [...selectedIds], ...values } as any);
    },
    onSuccess: () => { queryClient.invalidateQueries(); setShowBulkEdit(false); onDone(); },
  });

  const isScenes = entityType === "scenes";
  const isAudios = entityType === "audios";
  const isTexts = entityType === "texts";
  const canIdentify = isScenes && hasPermission("library.autotag") && canWrite;
  const downloadEntity: DownloadSelectionEntity | null = entityType === "scenes"
    ? "Scene"
    : entityType === "images"
      ? "Image"
      : entityType === "galleries"
        ? "Gallery"
        : entityType === "audios"
          ? "Audio"
          : entityType === "texts"
            ? "Text"
            : null;
  const resolvedDownloadItems = useMemo(
    () => downloadItems ?? (downloadEntity === "Scene" ? sceneItems ?? [] : []),
    [downloadEntity, downloadItems, sceneItems],
  );
  const selectedDownloadItems = useMemo(
    () => (downloadEntity ? getUndownloadedSelectionItems(resolvedDownloadItems, selectedIds) : []),
    [downloadEntity, resolvedDownloadItems, selectedIds],
  );
  const batchDownloadStorageKey = useMemo(
    () => (downloadEntity ? getBatchDownloadOptionsStorageKey(`bulk-${downloadEntity.toLowerCase()}`) : null),
    [downloadEntity],
  );
  const [batchDownloadOptions, setBatchDownloadOptions] = useState<BatchDownloadOptions>(() =>
    batchDownloadStorageKey ? loadStoredBatchDownloadOptions(batchDownloadStorageKey) : DEFAULT_BATCH_DOWNLOAD_OPTIONS,
  );
  const canDownload = !!downloadEntity && hasPermission("jobs.run") && canWrite;
  const selectedMediaItem = useMemo(() => {
    if (selectedIds.size !== 1) return undefined;
    const [selectedId] = [...selectedIds];
    return isAudios
      ? audioItems?.find((item) => item.id === selectedId)
      : isTexts
        ? textItems?.find((item) => item.id === selectedId)
        : undefined;
  }, [audioItems, isAudios, isTexts, selectedIds, textItems]);
  const mediaScrapeType = isAudios ? "audio" : isTexts ? "text" : null;
  const canScrapeMedia = canWrite && !!mediaScrapeType && !!selectedMediaItem;

  useEffect(() => {
    if (batchDownloadStorageKey) {
      setBatchDownloadOptions(loadStoredBatchDownloadOptions(batchDownloadStorageKey));
    }
  }, [batchDownloadStorageKey]);

  const batchDownloadMut = useMutation({
    mutationFn: async (options: BatchDownloadOptions) => {
      if (!downloadEntity) {
        throw new Error("Bulk download is not available for this entity type.");
      }

      return queueBatchDownloads(downloadEntity, selectedDownloadItems, options);
    },
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ["jobs"] });
      queryClient.invalidateQueries({ queryKey: ["jobs-active"] });
      queryClient.invalidateQueries({ queryKey: ["jobs-history"] });
      queryClient.invalidateQueries();
      window.alert(formatBatchDownloadSummary(downloadEntity!.toLowerCase(), result));
      onDone();
    },
    onError: (error: Error) => {
      window.alert(error.message || "Failed to queue the selected downloads.");
    },
  });

  return (
    <>
      {canDownload && downloadEntity && selectedDownloadItems.length > 0 && (
        <button
          onClick={() => setShowBatchDownloadOptions(true)}
          disabled={batchDownloadMut.isPending}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-cyan-400 hover:text-cyan-300 hover:bg-cyan-900/20 disabled:opacity-60"
        >
          {batchDownloadMut.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Download className="w-3 h-3" />}
          Download
        </button>
      )}
      {canWrite && fields.length > 0 && (
        <button
          onClick={() => setShowBulkEdit(true)}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
        >
          <Edit className="w-3 h-3" />
          Edit
        </button>
      )}
      {canIdentify && (
        <button
          onClick={() => setShowIdentify(true)}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
        >
          <Search className="w-3 h-3" />
          Identify
        </button>
      )}
      {canScrapeMedia && mediaScrapeType && selectedMediaItem && (
        <button
          onClick={() => setShowScrape(true)}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
        >
          <Search className="w-3 h-3" />
          Scrape
        </button>
      )}
      {isScenes && sceneItems && onNavigate && (
        <button
          onClick={() => setShowQueue(true)}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-green-400 hover:text-green-300 hover:bg-green-900/20"
        >
          <Play className="w-3 h-3" />
          Play
        </button>
      )}
      {isAudios && audioItems && onNavigate && (
        <button
          onClick={() => {
            const selectedAudio = audioItems.find((item) => selectedIds.has(item.id));
            if (selectedAudio) {
              onNavigate({ page: "audio", id: selectedAudio.id });
              onDone();
            }
          }}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-green-400 hover:text-green-300 hover:bg-green-900/20"
        >
          <Play className="w-3 h-3" />
          Play
        </button>
      )}
      <ExtensionSelectionActions entityType={entityType} selectedIds={selectedIds} />
      {canDelete && (
        <button
          onClick={() => setShowDeleteConfirm(true)}
          disabled={bulkDeleteMut.isPending}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-red-400 hover:text-red-300 hover:bg-red-900/20"
        >
          {bulkDeleteMut.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Trash2 className="w-3 h-3" />}
          Delete
        </button>
      )}
      {showBulkEdit && (
        <BulkEditDialog
          open
          onClose={() => setShowBulkEdit(false)}
          title={`Bulk Edit ${selectedIds.size} ${entityType}`}
          selectedCount={selectedIds.size}
          fields={fields as any}
          onApply={(values) => bulkEditMut.mutate(values)}
          isPending={bulkEditMut.isPending}
        />
      )}
      {showIdentify && canIdentify && (
        <IdentifyDialog open onClose={() => setShowIdentify(false)} sceneIds={[...selectedIds]} />
      )}
      {showQueue && isScenes && sceneItems && onNavigate && (
        <SceneQueue
          scenes={sceneItems.filter(s => selectedIds.has(s.id)).map(s => ({
            id: s.id,
            title: s.title || s.files[0]?.basename,
            duration: s.files[0]?.duration,
            screenshotUrl: scenesApi.screenshotUrl(s.id, s.updatedAt),
          }))}
          onClose={() => setShowQueue(false)}
          onNavigate={onNavigate}
        />
      )}
      {showScrape && mediaScrapeType && selectedMediaItem ? (
        <MediaScrapeDialog
          open
          entityType={mediaScrapeType}
          entity={selectedMediaItem}
          onClose={() => setShowScrape(false)}
        />
      ) : null}
      {canDownload && downloadEntity && (
        <BatchDownloadOptionsDialog
          open={showBatchDownloadOptions}
          entity={downloadEntity}
          itemCount={selectedDownloadItems.length}
          initialOptions={batchDownloadOptions}
          isPending={batchDownloadMut.isPending}
          onClose={() => setShowBatchDownloadOptions(false)}
          onConfirm={(options) => {
            setBatchDownloadOptions(options);
            if (batchDownloadStorageKey) {
              saveStoredBatchDownloadOptions(batchDownloadStorageKey, options);
            }
            setShowBatchDownloadOptions(false);
            batchDownloadMut.mutate(options);
          }}
        />
      )}
      <ConfirmDialog
        open={showDeleteConfirm}
        title={`Delete ${selectedIds.size} ${resource}${selectedIds.size === 1 ? "" : "s"}`}
        message={`Delete ${selectedIds.size} selected ${resource}${selectedIds.size === 1 ? "" : "s"}? This cannot be undone.`}
        confirmLabel={bulkDeleteMut.isPending ? "Deleting..." : "Delete"}
        onConfirm={(options) => bulkDeleteMut.mutate(supportsDeleteOptions ? options : undefined)}
        onCancel={() => { bulkDeleteMut.reset(); setShowDeleteConfirm(false); }}
        isPending={bulkDeleteMut.isPending}
        errorMessage={getMutationErrorMessage(bulkDeleteMut.error)}
        showDeleteFile={supportsDeleteOptions}
        showDeleteGenerated={supportsDeleteOptions}
      />
    </>
  );
}
