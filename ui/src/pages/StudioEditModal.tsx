import { useState, useEffect } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { studios, entityImages } from "../api/client";
import type { Studio, StudioUpdate } from "../api/types";
import { EditModal, Field, TextInput, TextArea, SaveButton } from "../components/EditModal";
import { ImageInput } from "../components/ImageInput";
import { InteractiveRatingField } from "../components/Rating";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { RemoteIdsEditor, normalizeRemoteIds, type RemoteIdValue } from "../components/RemoteIdsEditor";
import { EntityReferenceMultiSelector, EntityReferenceSelector } from "../components/EntityReferenceSelector";

interface Props {
  studio: Studio;
  open: boolean;
  onClose: () => void;
}

export function StudioEditModal({ studio, open, onClose }: Props) {
  const queryClient = useQueryClient();

  const [name, setName] = useState(studio.name);
  const [details, setDetails] = useState(studio.details ?? "");
  const [rating, setRating] = useState<number | undefined>(undefined);
  const [ignoreAutoTag, setIgnoreAutoTag] = useState(studio.ignoreAutoTag);
  const [urls, setUrls] = useState(studio.urls.length > 0 ? studio.urls : [""]);
  const [aliases, setAliases] = useState(studio.aliases.length > 0 ? studio.aliases : [""]);
  const [parentId, setParentId] = useState<number | undefined>(studio.parentId ?? undefined);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(studio.tags.map((t) => t.id));

  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(studio.customFields ?? {}) });
  const [remoteIds, setRemoteIds] = useState<RemoteIdValue[]>(studio.remoteIds.map((remoteId) => ({ ...remoteId })));

  useEffect(() => {
    setName(studio.name);
    setDetails(studio.details ?? "");
    setRating(undefined);
    setIgnoreAutoTag(studio.ignoreAutoTag);
    setUrls(studio.urls.length > 0 ? studio.urls : [""]);
    setAliases(studio.aliases.length > 0 ? studio.aliases : [""]);
    setParentId(studio.parentId ?? undefined);
    setSelectedTagIds(studio.tags.map((t) => t.id));
    setCustomFields({ ...(studio.customFields ?? {}) });
    setRemoteIds(studio.remoteIds.map((remoteId) => ({ ...remoteId })));
  }, [studio]);

  const mutation = useMutation({
    mutationFn: (data: StudioUpdate) => studios.update(studio.id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["studio", studio.id] });
      queryClient.invalidateQueries({ queryKey: ["studios"] });
      onClose();
    },
  });

  const handleSave = () => {
    const urlList = urls.map((url) => url.trim()).filter(Boolean);
    const aliasList = aliases.map((alias) => alias.trim()).filter(Boolean);
    mutation.mutate({
      name,
      details: details || undefined,
      rating,
      ignoreAutoTag,
      parentId,
      urls: urlList,
      aliases: aliasList,
      tagIds: selectedTagIds,
      customFields,
      remoteIds: normalizeRemoteIds(remoteIds),
    });
  };

  return (
    <EditModal title={`Edit Studio: ${studio.name}`} open={open} onClose={onClose}>
      <div className="flex gap-6 mb-4">
        <ImageInput
          currentImageUrl={entityImages.studioImageUrl(studio.id, studio.updatedAt)}
          onUpload={(file) => entityImages.uploadStudioImage(studio.id, file)}
          onDelete={() => entityImages.deleteStudioImage(studio.id)}
          onSuccess={() => queryClient.invalidateQueries({ queryKey: ["studio", studio.id] })}
          label="Logo"
          aspectRatio="1/1"
          className="w-40"
          objectFit="contain"
        />
        <div className="flex-1 space-y-4">
      <div className="grid grid-cols-2 gap-4">
        <Field label="Name *">
          <TextInput value={name} onChange={setName} placeholder="Studio name" />
        </Field>
        <Field label="Parent Studio">
          <EntityReferenceSelector entityType="studio" value={parentId} onChange={setParentId} placeholder="Search parent studios..." excludeIds={[studio.id]} />
        </Field>
      </div>

      <Field label="Details">
        <TextArea value={details} onChange={setDetails} placeholder="Studio description" rows={3} />
      </Field>

      <div className="grid grid-cols-2 gap-4">
        <InteractiveRatingField label="Rating" value={rating} onChange={setRating} />
        <div className="flex items-end gap-4 pb-4">
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={ignoreAutoTag} onChange={(e) => setIgnoreAutoTag(e.target.checked)} className="rounded bg-card border-border" />
            Ignore Auto Tag
          </label>
        </div>
      </div>

      <Field label="URLs">
        <StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" />
      </Field>

      <Field label="Aliases">
        <StringListEditor values={aliases} onChange={setAliases} placeholder="Alternate name" addLabel="Add Alias" />
      </Field>

      {/* Tags */}
      <Field label="Tags">
        <EntityReferenceMultiSelector entityType="tag" values={selectedTagIds} onChange={setSelectedTagIds} placeholder="Search tags..." />
      </Field>

      <Field label="Remote IDs">
        <RemoteIdsEditor value={remoteIds} onChange={setRemoteIds} />
      </Field>

      <Field label="Custom Fields">
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="studio" />
      </Field>

      </div></div>
      <div className="flex justify-end gap-3 mt-4">
        <button onClick={onClose} className="px-4 py-2 text-sm text-secondary hover:text-white">Cancel</button>
        <SaveButton loading={mutation.isPending} onClick={handleSave} />
      </div>
    </EditModal>
  );
}
