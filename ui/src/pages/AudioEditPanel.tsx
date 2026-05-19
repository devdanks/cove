import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { audios, entityImages } from "../api/client";
import type { Audio, AudioUpdate, SceneGroupInput } from "../api/types";
import { ImageInput } from "../components/ImageInput";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { EntityReferenceMultiSelector, EntityReferenceValue } from "../components/EntityReferenceSelector";

interface Props {
  audio: Audio;
  onSaved: () => void;
}

export function AudioEditPanel({ audio, onSaved }: Props) {
  const queryClient = useQueryClient();
  const inputCls = "w-full rounded-lg border border-border bg-input px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none";

  const [title, setTitle] = useState(audio.title ?? "");
  const [code, setCode] = useState(audio.code ?? "");
  const [details, setDetails] = useState(audio.details ?? "");
  const [date, setDate] = useState(audio.date ?? "");
  const [studioId, setStudioId] = useState<number | undefined>(audio.studioId ?? undefined);
  const [urls, setUrls] = useState<string[]>(audio.urls.length > 0 ? audio.urls : [""]);
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(audio.customFields ?? {}) });
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(audio.tags.map((tag) => tag.id));
  const [selectedPerformerIds, setSelectedPerformerIds] = useState<number[]>(audio.performers.map((performer) => performer.id));
  const [selectedGroups, setSelectedGroups] = useState<SceneGroupInput[]>(audio.groups.map((group) => ({ groupId: group.id, sceneIndex: 0 })));
  useEffect(() => {
    setTitle(audio.title ?? "");
    setCode(audio.code ?? "");
    setDetails(audio.details ?? "");
    setDate(audio.date ?? "");
    setStudioId(audio.studioId ?? undefined);
    setUrls(audio.urls.length > 0 ? audio.urls : [""]);
    setCustomFields({ ...(audio.customFields ?? {}) });
    setSelectedTagIds(audio.tags.map((tag) => tag.id));
    setSelectedPerformerIds(audio.performers.map((performer) => performer.id));
    setSelectedGroups(audio.groups.map((group) => ({ groupId: group.id, sceneIndex: 0 })));
  }, [audio]);

  const mutation = useMutation({
    mutationFn: (data: AudioUpdate) => audios.update(audio.id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["audio", audio.id] });
      queryClient.invalidateQueries({ queryKey: ["audios"] });
      onSaved();
    },
  });

  const setSelectedGroupIds = (groupIds: number[]) => {
    setSelectedGroups(groupIds.map((groupId) => selectedGroups.find((group) => group.groupId === groupId) ?? { groupId, sceneIndex: 0 }));
  };

  const handleSave = () => {
    mutation.mutate({
      title: title.trim(),
      code: code.trim(),
      details: details.trim(),
      studioId,
      date,
      urls: urls.map((url) => url.trim()).filter(Boolean),
      tagIds: selectedTagIds,
      performerIds: selectedPerformerIds,
      customFields,
      groupIds: selectedGroups,
    });
  };

  return (
    <div className="space-y-4">
      <div className="grid gap-4 lg:grid-cols-[220px_minmax(0,1fr)]">
        <ImageInput
          currentImageUrl={audio.imagePath ?? undefined}
          onUpload={(file) => entityImages.uploadAudioImage(audio.id, file)}
          onDelete={() => entityImages.deleteAudioImage(audio.id)}
          onSuccess={() => {
            queryClient.invalidateQueries({ queryKey: ["audio", audio.id] });
            queryClient.invalidateQueries({ queryKey: ["audios"] });
          }}
          label="Cover"
          aspectRatio="1/1"
        />

        <div className="space-y-4">
          <div className="grid gap-3 md:grid-cols-2">
            <label className="space-y-1">
              <span className="text-xs text-secondary">Title</span>
              <input value={title} onChange={(event) => setTitle(event.target.value)} className={inputCls} />
            </label>
            <label className="space-y-1">
              <span className="text-xs text-secondary">Date</span>
              <input type="date" value={date} onChange={(event) => setDate(event.target.value)} className={inputCls} />
            </label>
          </div>

          <label className="space-y-1">
            <span className="text-xs text-secondary">Code</span>
            <input value={code} onChange={(event) => setCode(event.target.value)} className={inputCls} />
          </label>

          <label className="space-y-1">
            <span className="text-xs text-secondary">Description</span>
            <textarea value={details} onChange={(event) => setDetails(event.target.value)} rows={4} className={inputCls} />
          </label>

          <label className="block space-y-1">
            <span className="text-xs text-secondary">Studio</span>
            <StudioSelector value={studioId} onChange={setStudioId} placeholder="Search studios..." />
          </label>
        </div>
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">URLs</span>
        <StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Custom Fields</span>
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="audio" />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Tags</span>
        <EntityReferenceMultiSelector entityType="tag" values={selectedTagIds} onChange={setSelectedTagIds} placeholder="Search tags..." inputClassName={inputCls} />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Performers</span>
        <EntityReferenceMultiSelector entityType="performer" values={selectedPerformerIds} onChange={setSelectedPerformerIds} placeholder="Search performers..." inputClassName={inputCls} />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Groups</span>
        <div className="mb-1 flex flex-wrap gap-1.5">
          {selectedGroups.map((group) => (
            <span key={group.groupId} className="inline-flex items-center gap-1 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs text-emerald-300">
              <EntityReferenceValue entityType="group" value={group.groupId} />
              <button type="button" onClick={() => setSelectedGroups(selectedGroups.filter((item) => item.groupId !== group.groupId))} className="hover:text-foreground">
                x
              </button>
            </span>
          ))}
        </div>
        <EntityReferenceMultiSelector entityType="group" values={selectedGroups.map((group) => group.groupId)} onChange={setSelectedGroupIds} placeholder="Search groups..." inputClassName={inputCls} />
      </div>

      {mutation.error ? <div className="rounded-lg border border-red-500/40 bg-red-500/10 px-3 py-2 text-sm text-red-200">{(mutation.error as Error).message}</div> : null}

      <div className="flex justify-end gap-3 pt-2">
        <button type="button" onClick={onSaved} className="px-4 py-2 text-sm text-secondary transition hover:text-foreground">Cancel</button>
        <button type="button" onClick={handleSave} disabled={mutation.isPending} className="rounded-lg bg-accent px-4 py-2 text-sm text-white transition hover:bg-accent-hover disabled:opacity-60">
          {mutation.isPending ? "Saving..." : "Save"}
        </button>
      </div>
    </div>
  );
}