import { useEffect, useMemo, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Download, Edit, Loader2, Trash2, Search, Merge, Play } from "lucide-react";
import { scenes as scenesApi, images, galleries, performers, groups, studios, tags } from "../api/client";
import type { Scene } from "../api/types";
import { BulkEditDialog, SCENE_BULK_FIELDS, IMAGE_BULK_FIELDS, GALLERY_BULK_FIELDS, PERFORMER_BULK_FIELDS, GROUP_BULK_FIELDS, STUDIO_BULK_FIELDS, TAG_BULK_FIELDS } from "./BulkEditDialog";
import { BatchDownloadOptionsDialog } from "./BatchDownloadOptionsDialog";
import { IdentifyDialog } from "./IdentifyDialog";
import { SceneQueue } from "./SceneQueue";
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
} as const;

const API_MAP = { scenes: scenesApi, images, galleries, performers, groups, studios, tags } as const;

interface Props {
  entityType: keyof typeof FIELDS_MAP;
  selectedIds: Set<number>;
  onDone: () => void;
  /** Raw scene items for Play/Identify (only needed when entityType is "scenes") */
  sceneItems?: Pick<Scene, "id" | "title" | "updatedAt" | "urls" | "files">[];
  downloadItems?: DownloadSelectionItem[];
  /** Navigate callback for the scene queue player */
  onNavigate?: (route: any) => void;
}

export function BulkSelectionActions({ entityType, selectedIds, onDone, sceneItems, downloadItems, onNavigate }: Props) {
  const [showBulkEdit, setShowBulkEdit] = useState(false);
  const [showIdentify, setShowIdentify] = useState(false);
  const [showQueue, setShowQueue] = useState(false);
  const [showBatchDownloadOptions, setShowBatchDownloadOptions] = useState(false);
  const queryClient = useQueryClient();
  const api = API_MAP[entityType];
  const fields = FIELDS_MAP[entityType];

  const bulkDeleteMut = useMutation<void, Error, void>({
    mutationFn: async () => {
      await api.bulkDelete([...selectedIds]);
    },
    onSuccess: () => { queryClient.invalidateQueries(); onDone(); },
  });

  const bulkEditMut = useMutation<void, Error, Record<string, unknown>>({
    mutationFn: async (values) => {
      await api.bulkUpdate({ ids: [...selectedIds], ...values } as any);
    },
    onSuccess: () => { queryClient.invalidateQueries(); setShowBulkEdit(false); onDone(); },
  });

  const isScenes = entityType === "scenes";
  const downloadEntity: DownloadSelectionEntity | null = entityType === "scenes"
    ? "Scene"
    : entityType === "images"
      ? "Image"
      : entityType === "galleries"
        ? "Gallery"
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
      {downloadEntity && selectedDownloadItems.length > 0 && (
        <button
          onClick={() => setShowBatchDownloadOptions(true)}
          disabled={batchDownloadMut.isPending}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-cyan-400 hover:text-cyan-300 hover:bg-cyan-900/20 disabled:opacity-60"
        >
          {batchDownloadMut.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Download className="w-3 h-3" />}
          Download
        </button>
      )}
      {fields.length > 0 && (
        <button
          onClick={() => setShowBulkEdit(true)}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
        >
          <Edit className="w-3 h-3" />
          Edit
        </button>
      )}
      {isScenes && (
        <button
          onClick={() => setShowIdentify(true)}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-accent hover:text-accent-hover hover:bg-accent/10"
        >
          <Search className="w-3 h-3" />
          Identify
        </button>
      )}
      {isScenes && selectedIds.size >= 2 && (
        <button
          onClick={() => {/* TODO: merge dialog */}}
          className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-yellow-400 hover:text-yellow-300 hover:bg-yellow-900/20"
        >
          <Merge className="w-3 h-3" />
          Merge
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
      <button
        onClick={() => { if (confirm(`Delete ${selectedIds.size} item(s)?`)) bulkDeleteMut.mutate(); }}
        disabled={bulkDeleteMut.isPending}
        className="flex items-center gap-1 px-2 py-0.5 rounded text-xs text-red-400 hover:text-red-300 hover:bg-red-900/20"
      >
        {bulkDeleteMut.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Trash2 className="w-3 h-3" />}
        Delete
      </button>
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
      {showIdentify && isScenes && (
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
      {downloadEntity && (
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
    </>
  );
}
