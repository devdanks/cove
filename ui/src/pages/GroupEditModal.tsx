import { useState, useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { groups } from "../api/client";
import type { Group, GroupUpdate } from "../api/types";
import { EditModal, Field, TextInput, TextArea, SaveButton } from "../components/EditModal";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { EntityReferenceMultiSelector } from "../components/EntityReferenceSelector";
import { DynamicGroupFilterEditor, FILTER_DYNAMIC_SOURCE_KEY, defaultDynamicGroupFilterQueryJson } from "../components/DynamicGroupFilterEditor";

interface Props {
  group: Group;
  open: boolean;
  onClose: () => void;
}

export function GroupEditModal({ group, open, onClose }: Props) {
  const queryClient = useQueryClient();

  const [name, setName] = useState(group.name);
  const [aliases, setAliases] = useState<string[]>(() => splitAliases(group.aliases));
  const [director, setDirector] = useState(group.director ?? "");
  const [date, setDate] = useState(group.date ?? "");
  const [studioId, setStudioId] = useState<number | undefined>(group.studioId ?? undefined);
  const [description, setDescription] = useState(group.description ?? "");
  const [urls, setUrls] = useState(group.urls.length > 0 ? group.urls : [""]);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(group.tags.map((t) => t.id));
  const [kind, setKind] = useState<"static" | "dynamic">(group.kind ?? "static");
  const [querySourceKey, setQuerySourceKey] = useState(group.querySourceKey ?? FILTER_DYNAMIC_SOURCE_KEY);
  const [queryJson, setQueryJson] = useState(group.queryJson ?? defaultDynamicGroupFilterQueryJson());
  const [showInSceneLists, setShowInSceneLists] = useState(group.showInSceneLists ?? false);

  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(group.customFields ?? {}) });
  const { data: dynamicSources = [] } = useQuery({
    queryKey: ["group-dynamic-sources"],
    queryFn: () => groups.dynamicSources(),
    enabled: open,
  });

  useEffect(() => {
    setName(group.name);
    setAliases(splitAliases(group.aliases));
    setDirector(group.director ?? "");
    setDate(group.date ?? "");
    setStudioId(group.studioId ?? undefined);
    setDescription(group.description ?? "");
    setUrls(group.urls.length > 0 ? group.urls : [""]);
    setSelectedTagIds(group.tags.map((t) => t.id));
    setCustomFields({ ...(group.customFields ?? {}) });
    setKind(group.kind ?? "static");
    setQuerySourceKey(group.querySourceKey ?? dynamicSources.find((source) => source.key === FILTER_DYNAMIC_SOURCE_KEY)?.key ?? dynamicSources[0]?.key ?? FILTER_DYNAMIC_SOURCE_KEY);
    setQueryJson(group.queryJson ?? defaultDynamicGroupFilterQueryJson());
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
      aliases: joinAliases(aliases) || undefined,
      director: director || undefined,
      date: date || undefined,
      studioId,
      description: description || undefined,
      urls: urlList,
      tagIds: selectedTagIds,
      customFields,
      kind,
      querySourceKey: kind === "dynamic" ? querySourceKey : undefined,
      queryJson: kind === "dynamic" && querySourceKey === FILTER_DYNAMIC_SOURCE_KEY ? queryJson : undefined,
      showInSceneLists,
    });
  };

  return (
    <EditModal title={`Edit Group: ${group.name}`} open={open} onClose={onClose}>
      <div className="grid grid-cols-2 gap-4">
        <Field label="Name *">
          <TextInput value={name} onChange={setName} placeholder="Group name" />
        </Field>
        <Field label="Studio">
          <StudioSelector value={studioId} onChange={setStudioId} />
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
        <div className="flex items-end pb-1">
          <label className="inline-flex items-center gap-2 text-sm text-foreground">
            <input type="checkbox" checked={showInSceneLists} onChange={(event) => setShowInSceneLists(event.target.checked)} className="h-4 w-4 accent-accent" />
            Show in scene browsing
          </label>
        </div>
      </div>

      {kind === "dynamic" ? (
        <div className="grid grid-cols-1 gap-4">
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

      <Field label="Description">
        <TextArea value={description} onChange={setDescription} placeholder="Group description" rows={4} />
      </Field>

      <Field label="Aliases">
        <StringListEditor values={aliases} onChange={setAliases} placeholder="Alias" addLabel="Add Alias" />
      </Field>

      <Field label="URLs">
        <StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" />
      </Field>

      {/* Tags */}
      <Field label="Tags">
        <EntityReferenceMultiSelector entityType="tag" values={selectedTagIds} onChange={setSelectedTagIds} placeholder="Search tags..." />
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

function splitAliases(value?: string) {
  return value
    ?.split(/[\r\n,]+/)
    .map((alias) => alias.trim())
    .filter(Boolean) ?? [];
}

function joinAliases(values: string[]) {
  return values.map((alias) => alias.trim()).filter(Boolean).join(", ");
}
