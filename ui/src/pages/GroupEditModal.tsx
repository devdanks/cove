import { useState, useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { groups, tags as tagsApi, entityImages } from "../api/client";
import type { Group, GroupUpdate } from "../api/types";
import { EditModal, Field, TextInput, TextArea, NumberInput, SaveButton } from "../components/EditModal";
import { ImageInput } from "../components/ImageInput";
import { RatingField } from "../components/Rating";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { GroupedTagOptionList, SelectedTagChips, filterTagsForSelector } from "../components/TagSelector";
import { DynamicGroupFilterEditor, FILTER_DYNAMIC_SOURCE_KEY, defaultDynamicGroupFilterQueryJson } from "../components/DynamicGroupFilterEditor";

interface Props {
  group: Group;
  open: boolean;
  onClose: () => void;
}

export function GroupEditModal({ group, open, onClose }: Props) {
  const queryClient = useQueryClient();

  const [name, setName] = useState(group.name);
  const [aliases, setAliases] = useState(group.aliases ?? "");
  const [director, setDirector] = useState(group.director ?? "");
  const [date, setDate] = useState(group.date ?? "");
  const [duration, setDuration] = useState<number | undefined>(group.duration ?? undefined);
  const [rating, setRating] = useState<number | undefined>(undefined);
  const [studioId, setStudioId] = useState<number | undefined>(group.studioId ?? undefined);
  const [synopsis, setSynopsis] = useState(group.synopsis ?? "");
  const [urls, setUrls] = useState(group.urls.length > 0 ? group.urls : [""]);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(group.tags.map((t) => t.id));
  const [kind, setKind] = useState<"static" | "dynamic">(group.kind ?? "static");
  const [querySourceKey, setQuerySourceKey] = useState(group.querySourceKey ?? FILTER_DYNAMIC_SOURCE_KEY);
  const [queryJson, setQueryJson] = useState(group.queryJson ?? defaultDynamicGroupFilterQueryJson());
  const [cacheTtlSec, setCacheTtlSec] = useState(group.cacheTtlSec ?? 60);
  const [showInSceneLists, setShowInSceneLists] = useState(group.showInSceneLists ?? false);

  // Tag search
  const [tagSearch, setTagSearch] = useState("");
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(group.customFields ?? {}) });
  const { data: allTags } = useQuery({
    queryKey: ["tags-all"],
    queryFn: () => tagsApi.find({ perPage: 500, sort: "name", direction: "asc" }),
  });
  const { data: dynamicSources = [] } = useQuery({
    queryKey: ["group-dynamic-sources"],
    queryFn: () => groups.dynamicSources(),
    enabled: open,
  });

  useEffect(() => {
    setName(group.name);
    setAliases(group.aliases ?? "");
    setDirector(group.director ?? "");
    setDate(group.date ?? "");
    setDuration(group.duration ?? undefined);
    setRating(undefined);
    setStudioId(group.studioId ?? undefined);
    setSynopsis(group.synopsis ?? "");
    setUrls(group.urls.length > 0 ? group.urls : [""]);
    setSelectedTagIds(group.tags.map((t) => t.id));
    setCustomFields({ ...(group.customFields ?? {}) });
    setKind(group.kind ?? "static");
    setQuerySourceKey(group.querySourceKey ?? dynamicSources.find((source) => source.key === FILTER_DYNAMIC_SOURCE_KEY)?.key ?? dynamicSources[0]?.key ?? FILTER_DYNAMIC_SOURCE_KEY);
    setQueryJson(group.queryJson ?? defaultDynamicGroupFilterQueryJson());
    setCacheTtlSec(group.cacheTtlSec ?? 60);
    setShowInSceneLists(group.showInSceneLists ?? false);
  }, [dynamicSources, group]);

  const mutation = useMutation({
    mutationFn: (data: GroupUpdate) => groups.update(group.id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["group", group.id] });
      queryClient.invalidateQueries({ queryKey: ["groups"] });
      onClose();
    },
  });

  const handleSave = () => {
    const urlList = urls.map((url) => url.trim()).filter(Boolean);
    mutation.mutate({
      name,
      aliases: aliases || undefined,
      director: director || undefined,
      date: date || undefined,
      duration,
      rating,
      studioId,
      synopsis: synopsis || undefined,
      urls: urlList,
      tagIds: selectedTagIds,
      customFields,
      kind,
      querySourceKey: kind === "dynamic" ? querySourceKey : undefined,
      queryJson: kind === "dynamic" && querySourceKey === FILTER_DYNAMIC_SOURCE_KEY ? queryJson : undefined,
      cacheTtlSec: kind === "dynamic" ? cacheTtlSec : undefined,
      showInSceneLists,
    });
  };

  const filteredTags = filterTagsForSelector(allTags?.items ?? [], tagSearch, selectedTagIds);
  const selectedTags = allTags?.items.filter((t) => selectedTagIds.includes(t.id)) ?? group.tags;

  return (
    <EditModal title={`Edit Group: ${group.name}`} open={open} onClose={onClose}>
      <div className="grid grid-cols-2 gap-4 mb-4">
        <ImageInput
          currentImageUrl={entityImages.groupFrontImageUrl(group.id, group.updatedAt)}
          onUpload={(file) => entityImages.uploadGroupFrontImage(group.id, file)}
          onDelete={() => entityImages.deleteGroupFrontImage(group.id)}
          onSuccess={() => queryClient.invalidateQueries({ queryKey: ["group", group.id] })}
          label="Front Cover"
          aspectRatio="2/3"
        />
        <ImageInput
          currentImageUrl={entityImages.groupBackImageUrl(group.id, group.updatedAt)}
          onUpload={(file) => entityImages.uploadGroupBackImage(group.id, file)}
          onDelete={() => entityImages.deleteGroupBackImage(group.id)}
          onSuccess={() => queryClient.invalidateQueries({ queryKey: ["group", group.id] })}
          label="Back Cover"
          aspectRatio="2/3"
        />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Field label="Name *">
          <TextInput value={name} onChange={setName} placeholder="Group name" />
        </Field>
        <Field label="Aliases">
          <TextInput value={aliases} onChange={setAliases} placeholder="Alternative names" />
        </Field>
      </div>

      <div className="grid grid-cols-2 gap-4">
        <Field label="Kind">
          <div className="inline-flex rounded-lg border border-border bg-card p-1">
            {(["static", "dynamic"] as const).map((nextKind) => (
              <button
                key={nextKind}
                type="button"
                onClick={() => setKind(nextKind)}
                className={`rounded-md px-3 py-1.5 text-sm capitalize transition-colors ${kind === nextKind ? "bg-accent text-white" : "text-secondary hover:text-foreground"}`}
              >
                {nextKind}
              </button>
            ))}
          </div>
        </Field>
        <Field label="Scenes list">
          <label className="inline-flex items-center gap-2 rounded-lg border border-border bg-card px-3 py-2 text-sm text-foreground">
            <input type="checkbox" checked={showInSceneLists} onChange={(event) => setShowInSceneLists(event.target.checked)} className="h-4 w-4 accent-accent" />
            Show in Scenes list
          </label>
        </Field>
      </div>

      {kind === "dynamic" ? (
        <div className="grid grid-cols-2 gap-4">
          <Field label="Dynamic source">
            <select
              value={querySourceKey}
              onChange={(event) => {
                setQuerySourceKey(event.target.value);
                if (event.target.value === FILTER_DYNAMIC_SOURCE_KEY && !queryJson) {
                  setQueryJson(defaultDynamicGroupFilterQueryJson());
                }
              }}
              className="w-full rounded border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
            >
              {dynamicSources.map((source) => (
                <option key={source.key} value={source.key}>{source.displayName}</option>
              ))}
            </select>
          </Field>
          <Field label="Cache TTL (seconds)">
            <input
              type="number"
              min={0}
              value={cacheTtlSec}
              onChange={(event) => setCacheTtlSec(Math.max(0, Number(event.target.value) || 0))}
              className="w-full rounded border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
            />
          </Field>
        </div>
      ) : null}

      {kind === "dynamic" && querySourceKey === FILTER_DYNAMIC_SOURCE_KEY ? (
        <DynamicGroupFilterEditor queryJson={queryJson} onChange={setQueryJson} />
      ) : null}

      <div className="grid grid-cols-2 gap-4">
        <Field label="Director">
          <TextInput value={director} onChange={setDirector} placeholder="Director name" />
        </Field>
        <Field label="Date">
          <input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="w-full bg-card border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          />
        </Field>
      </div>

      <div className="grid grid-cols-3 gap-4">
        <Field label="Duration (seconds)">
          <NumberInput value={duration} onChange={setDuration} min={0} />
        </Field>
        <RatingField value={rating} onChange={setRating} />
        <Field label="Studio">
          <StudioSelector value={studioId} onChange={setStudioId} />
        </Field>
      </div>

      <Field label="Synopsis">
        <TextArea value={synopsis} onChange={setSynopsis} placeholder="Group synopsis / description" rows={4} />
      </Field>

      <Field label="URLs">
        <StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" />
      </Field>

      {/* Tags */}
      <Field label="Tags">
        <SelectedTagChips tags={selectedTags} onRemove={(tag) => setSelectedTagIds(selectedTagIds.filter((id) => id !== tag.id))} className="mb-2 flex flex-wrap gap-1.5" />
        <input
          type="text"
          value={tagSearch}
          onChange={(e) => setTagSearch(e.target.value)}
          placeholder="Search tags..."
          className="w-full bg-card border border-border rounded px-3 py-1.5 text-sm text-foreground focus:outline-none focus:border-accent mb-1"
        />
        {tagSearch && filteredTags.length > 0 && (
          <GroupedTagOptionList tags={filteredTags} maxItems={20} onSelect={(tag) => { setSelectedTagIds([...selectedTagIds, tag.id]); setTagSearch(""); }} />
        )}
      </Field>

      <Field label="Custom Fields">
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="group" />
      </Field>

      <div className="flex justify-end gap-3 mt-4">
        <button onClick={onClose} className="px-4 py-2 text-sm text-secondary hover:text-white">Cancel</button>
        <SaveButton loading={mutation.isPending} onClick={handleSave} />
      </div>
    </EditModal>
  );
}
