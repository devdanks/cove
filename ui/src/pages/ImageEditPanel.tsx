import { useEffect, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { images } from "../api/client";
import type { Image, ImageUpdate, SceneGroupInput } from "../api/types";
import { InteractiveRatingField } from "../components/Rating";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { EntityReferenceMultiSelector, EntityReferenceValue } from "../components/EntityReferenceSelector";

interface Props {
  image: Image;
  onSaved: () => void;
}

export function ImageEditPanel({ image, onSaved }: Props) {
  const queryClient = useQueryClient();
  const inputCls = "w-full rounded-lg border border-border bg-input px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none";

  const [title, setTitle] = useState(image.title ?? "");
  const [code, setCode] = useState(image.code ?? "");
  const [details, setDetails] = useState(image.details ?? "");
  const [photographer, setPhotographer] = useState(image.photographer ?? "");
  const [date, setDate] = useState(image.date ?? "");
  const [rating, setRating] = useState<number | undefined>(undefined);
  const [studioId, setStudioId] = useState<number | undefined>(image.studioId ?? undefined);
  const [urls, setUrls] = useState<string[]>(image.urls.length > 0 ? image.urls : [""]);
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(image.customFields ?? {}) });
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(image.tags.map((tag) => tag.id));
  const [selectedPerformerIds, setSelectedPerformerIds] = useState<number[]>(image.performers.map((performer) => performer.id));
  const [selectedGalleryIds, setSelectedGalleryIds] = useState<number[]>(image.galleryIds ?? []);
  const [selectedGroups, setSelectedGroups] = useState<SceneGroupInput[]>((image.groups ?? []).map((group) => ({ groupId: group.id, sceneIndex: group.sceneIndex ?? 0 })));
  useEffect(() => {
    setTitle(image.title ?? "");
    setCode(image.code ?? "");
    setDetails(image.details ?? "");
    setPhotographer(image.photographer ?? "");
    setDate(image.date ?? "");
    setRating(undefined);
    setStudioId(image.studioId ?? undefined);
    setUrls(image.urls.length > 0 ? image.urls : [""]);
    setCustomFields({ ...(image.customFields ?? {}) });
    setSelectedTagIds(image.tags.map((tag) => tag.id));
    setSelectedPerformerIds(image.performers.map((performer) => performer.id));
    setSelectedGalleryIds(image.galleryIds ?? []);
    setSelectedGroups((image.groups ?? []).map((group) => ({ groupId: group.id, sceneIndex: group.sceneIndex ?? 0 })));
  }, [image]);

  const mutation = useMutation({
    mutationFn: (data: ImageUpdate) => images.update(image.id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["image", image.id] });
      queryClient.invalidateQueries({ queryKey: ["images"] });
      onSaved();
    },
  });

  const setSelectedGroupIds = (groupIds: number[]) => {
    setSelectedGroups(groupIds.map((groupId) => selectedGroups.find((group) => group.groupId === groupId) ?? { groupId, sceneIndex: 0 }));
  };

  const handleSave = () => {
    mutation.mutate({
      title: title.trim() || undefined,
      code: code.trim() || undefined,
      details: details.trim() || undefined,
      photographer: photographer.trim() || undefined,
      date: date || undefined,
      rating,
      studioId,
      urls: urls.map((url) => url.trim()).filter(Boolean),
      tagIds: selectedTagIds,
      performerIds: selectedPerformerIds,
      galleryIds: selectedGalleryIds,
      groupIds: selectedGroups,
      customFields,
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

      <div className="grid gap-3 md:grid-cols-2">
        <label className="space-y-1">
          <span className="text-xs text-secondary">Studio Code</span>
          <input value={code} onChange={(event) => setCode(event.target.value)} className={inputCls} />
        </label>
        <label className="space-y-1">
          <span className="text-xs text-secondary">Photographer</span>
          <input value={photographer} onChange={(event) => setPhotographer(event.target.value)} className={inputCls} />
        </label>
      </div>

      <label className="space-y-1">
        <span className="text-xs text-secondary">Details</span>
        <textarea value={details} onChange={(event) => setDetails(event.target.value)} rows={4} className={inputCls} />
      </label>

      <div className="grid gap-3 md:grid-cols-2">
        <InteractiveRatingField label="Rating" value={rating} onChange={setRating} />
        <label className="block space-y-1">
          <span className="text-xs text-secondary">Studio</span>
          <StudioSelector value={studioId} onChange={setStudioId} placeholder="Search studios..." />
        </label>
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">URLs</span>
        <StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" />
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
        <span className="text-xs text-secondary">Galleries</span>
        <EntityReferenceMultiSelector entityType="gallery" values={selectedGalleryIds} onChange={setSelectedGalleryIds} placeholder="Search galleries..." inputClassName={inputCls} />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Groups</span>
        <div className="mb-1 flex flex-wrap gap-1.5">
          {selectedGroups.map((group) => (
            <span key={group.groupId} className="inline-flex items-center gap-1 rounded-full bg-orange-500/10 px-2 py-0.5 text-xs text-orange-300">
              <EntityReferenceValue entityType="group" value={group.groupId} />
              <button type="button" onClick={() => setSelectedGroups(selectedGroups.filter((item) => item.groupId !== group.groupId))} className="hover:text-foreground">
                x
              </button>
            </span>
          ))}
        </div>
        <EntityReferenceMultiSelector entityType="group" values={selectedGroups.map((group) => group.groupId)} onChange={setSelectedGroupIds} placeholder="Search groups..." inputClassName={inputCls} />
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Custom Fields</span>
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="image" />
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