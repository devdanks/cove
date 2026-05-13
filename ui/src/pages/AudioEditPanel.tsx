import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { audios, entityImages, groups as groupsApi, performers as performersApi, tags as tagsApi } from "../api/client";
import type { Audio, AudioUpdate, SceneGroupInput } from "../api/types";
import { ImageInput } from "../components/ImageInput";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { GroupedTagOptionList, SelectedTagChips, filterTagsForSelector } from "../components/TagSelector";

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
  const [tagSearch, setTagSearch] = useState("");
  const [performerSearch, setPerformerSearch] = useState("");
  const [groupSearch, setGroupSearch] = useState("");

  const { data: allTags } = useQuery({
    queryKey: ["tags-all"],
    queryFn: () => tagsApi.find({ perPage: 500, sort: "name", direction: "asc" }),
  });
  const { data: allPerformers } = useQuery({
    queryKey: ["performers-all"],
    queryFn: () => performersApi.find({ perPage: 500, sort: "name", direction: "asc" }),
  });
  const { data: allGroups } = useQuery({
    queryKey: ["groups-all"],
    queryFn: () => groupsApi.find({ perPage: 500, sort: "name", direction: "asc" }),
  });

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
    setTagSearch("");
    setPerformerSearch("");
    setGroupSearch("");
  }, [audio]);

  const mutation = useMutation({
    mutationFn: (data: AudioUpdate) => audios.update(audio.id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["audio", audio.id] });
      queryClient.invalidateQueries({ queryKey: ["audios"] });
      onSaved();
    },
  });

  const selectedTags = selectedTagIds
    .map((tagId) => allTags?.items.find((tag) => tag.id === tagId) ?? audio.tags.find((tag) => tag.id === tagId))
    .filter((tag): tag is NonNullable<typeof tag> => Boolean(tag));
  const selectedPerformers = selectedPerformerIds
    .map((performerId) => allPerformers?.items.find((performer) => performer.id === performerId) ?? audio.performers.find((performer) => performer.id === performerId))
    .filter((performer): performer is NonNullable<typeof performer> => Boolean(performer));
  const selectedGroupIds = selectedGroups.map((group) => group.groupId);
  const selectedGroupItems = selectedGroups
    .map((groupInput) => allGroups?.items.find((group) => group.id === groupInput.groupId) ?? audio.groups.find((group) => group.id === groupInput.groupId))
    .filter((group): group is NonNullable<typeof group> => Boolean(group));

  const filteredTags = filterTagsForSelector(allTags?.items ?? [], tagSearch, selectedTagIds);
  const filteredPerformers = allPerformers?.items.filter(
    (performer) => !selectedPerformerIds.includes(performer.id) && performer.name.toLowerCase().includes(performerSearch.toLowerCase()),
  ) ?? [];
  const filteredGroups = allGroups?.items.filter(
    (group) => !selectedGroupIds.includes(group.id) && group.name.toLowerCase().includes(groupSearch.toLowerCase()),
  ) ?? [];

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
        <SelectedTagChips tags={selectedTags} onRemove={(tag) => setSelectedTagIds(selectedTagIds.filter((id) => id !== tag.id))} className="mb-1 flex flex-wrap gap-1.5" />
        <input value={tagSearch} onChange={(event) => setTagSearch(event.target.value)} placeholder="Search tags..." className={inputCls} />
        {tagSearch.trim() && filteredTags.length > 0 ? (
          <div className="mt-1">
            <GroupedTagOptionList tags={filteredTags} selectedIds={selectedTagIds} maxItems={20} onSelect={(tag) => {
              setSelectedTagIds([...selectedTagIds, tag.id]);
              setTagSearch("");
            }} />
          </div>
        ) : null}
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Performers</span>
        <div className="mb-1 flex flex-wrap gap-1.5">
          {selectedPerformers.map((performer) => (
            <span key={performer.id} className="inline-flex items-center gap-1 rounded-full bg-accent/10 px-2 py-0.5 text-xs text-accent-hover">
              {performer.name}
              <button type="button" onClick={() => setSelectedPerformerIds(selectedPerformerIds.filter((id) => id !== performer.id))} className="hover:text-foreground">
                x
              </button>
            </span>
          ))}
        </div>
        <input value={performerSearch} onChange={(event) => setPerformerSearch(event.target.value)} placeholder="Search performers..." className={inputCls} />
        {performerSearch.trim() && filteredPerformers.length > 0 ? (
          <div className="mt-1 max-h-28 overflow-y-auto rounded-lg border border-border bg-surface">
            {filteredPerformers.slice(0, 12).map((performer) => (
              <button
                key={performer.id}
                type="button"
                onClick={() => {
                  setSelectedPerformerIds([...selectedPerformerIds, performer.id]);
                  setPerformerSearch("");
                }}
                className="block w-full px-3 py-1.5 text-left text-sm text-foreground transition hover:bg-card"
              >
                {performer.name}{performer.disambiguation ? ` (${performer.disambiguation})` : ""}
              </button>
            ))}
          </div>
        ) : null}
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Groups</span>
        <div className="mb-1 flex flex-wrap gap-1.5">
          {selectedGroupItems.map((group) => (
            <span key={group.id} className="inline-flex items-center gap-1 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs text-emerald-300">
              {group.name}
              <button type="button" onClick={() => setSelectedGroups(selectedGroups.filter((item) => item.groupId !== group.id))} className="hover:text-foreground">
                x
              </button>
            </span>
          ))}
        </div>
        <input value={groupSearch} onChange={(event) => setGroupSearch(event.target.value)} placeholder="Search groups..." className={inputCls} />
        {groupSearch.trim() && filteredGroups.length > 0 ? (
          <div className="mt-1 max-h-28 overflow-y-auto rounded-lg border border-border bg-surface">
            {filteredGroups.slice(0, 12).map((group) => (
              <button
                key={group.id}
                type="button"
                onClick={() => {
                  setSelectedGroups([...selectedGroups, { groupId: group.id, sceneIndex: 0 }]);
                  setGroupSearch("");
                }}
                className="block w-full px-3 py-1.5 text-left text-sm text-foreground transition hover:bg-card"
              >
                {group.name}
              </button>
            ))}
          </div>
        ) : null}
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