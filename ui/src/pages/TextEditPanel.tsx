import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { texts } from "../api/client";
import type { SceneGroupInput, TextDocument, TextUpdate } from "../api/types";
import { PerformerContextTagEditor, buildPerformerContextTagIds, syncPerformerContextTags } from "../components/PerformerContextTags";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { EntityReferenceMultiSelector, EntityReferenceValue } from "../components/EntityReferenceSelector";

interface Props {
  text: TextDocument;
  onSaved: () => void;
}

export function TextEditPanel({ text, onSaved }: Props) {
  const queryClient = useQueryClient();
  const inputCls = "w-full rounded-lg border border-border bg-input px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none";

  const [title, setTitle] = useState(text.title ?? "");
  const [code, setCode] = useState(text.code ?? "");
  const [details, setDetails] = useState(text.details ?? "");
  const [date, setDate] = useState(text.date ?? "");
  const [studioId, setStudioId] = useState<number | undefined>(text.studioId ?? undefined);
  const [urls, setUrls] = useState<string[]>(text.urls.length > 0 ? text.urls : [""]);
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(text.customFields ?? {}) });
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(text.tags.map((tag) => tag.id));
  const [selectedPerformerIds, setSelectedPerformerIds] = useState<number[]>(text.performers.map((performer) => performer.id));
  const [contextTagIdsByPerformer, setContextTagIdsByPerformer] = useState<Record<number, number[]>>(() => buildPerformerContextTagIds(text.contextTagApplications));
  const [selectedGroups, setSelectedGroups] = useState<SceneGroupInput[]>(text.groups.map((group) => ({ groupId: group.id, sceneIndex: 0 })));
  useEffect(() => {
    setTitle(text.title ?? "");
    setCode(text.code ?? "");
    setDetails(text.details ?? "");
    setDate(text.date ?? "");
    setStudioId(text.studioId ?? undefined);
    setUrls(text.urls.length > 0 ? text.urls : [""]);
    setCustomFields({ ...(text.customFields ?? {}) });
    setSelectedTagIds(text.tags.map((tag) => tag.id));
    setSelectedPerformerIds(text.performers.map((performer) => performer.id));
    setContextTagIdsByPerformer(buildPerformerContextTagIds(text.contextTagApplications));
    setSelectedGroups(text.groups.map((group) => ({ groupId: group.id, sceneIndex: 0 })));
  }, [text]);

  const mutation = useMutation({
    mutationFn: async (data: TextUpdate) => {
      await texts.update(text.id, data);
      await syncPerformerContextTags("text", text.id, text.contextTagApplications ?? [], contextTagIdsByPerformer, selectedPerformerIds);
      return texts.get(text.id);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["text", text.id] });
      queryClient.invalidateQueries({ queryKey: ["texts"] });
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

      <div className="space-y-1">
        <span className="text-xs text-secondary">URLs</span>
        <StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Custom Fields</span>
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="text" />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Tags</span>
        <EntityReferenceMultiSelector entityType="tag" values={selectedTagIds} onChange={setSelectedTagIds} placeholder="Search tags..." inputClassName={inputCls} />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Performers</span>
        <EntityReferenceMultiSelector entityType="performer" values={selectedPerformerIds} onChange={setSelectedPerformerIds} placeholder="Search performers..." inputClassName={inputCls} />
      </div>

      {selectedPerformerIds.length > 0 ? (
        <div className="space-y-1">
          <span className="text-xs text-secondary">Performer Occurrence Tags</span>
          <PerformerContextTagEditor
            performerIds={selectedPerformerIds}
            contextTagIdsByPerformer={contextTagIdsByPerformer}
            onChange={(performerId, tagIds) => setContextTagIdsByPerformer((current) => ({ ...current, [performerId]: tagIds }))}
            inputClassName={inputCls}
          />
        </div>
      ) : null}

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