import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { galleries as galleriesApi, groups as groupsApi, images, performers as performersApi, tags as tagsApi } from "../api/client";
import type { Image, ImageUpdate, SceneGroupInput } from "../api/types";
import { InteractiveRatingField } from "../components/Rating";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { GroupedTagOptionList, SelectedTagChips, filterTagsForSelector } from "../components/TagSelector";

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
  const [tagSearch, setTagSearch] = useState("");
  const [performerSearch, setPerformerSearch] = useState("");
  const [gallerySearch, setGallerySearch] = useState("");
  const [groupSearch, setGroupSearch] = useState("");

  const { data: allTags } = useQuery({
    queryKey: ["tags-all"],
    queryFn: () => tagsApi.find({ perPage: 500, sort: "name", direction: "asc" }),
  });
  const { data: allPerformers } = useQuery({
    queryKey: ["performers-all"],
    queryFn: () => performersApi.find({ perPage: 500, sort: "name", direction: "asc" }),
  });
  const { data: allGalleries } = useQuery({
    queryKey: ["galleries-all"],
    queryFn: () => galleriesApi.find({ perPage: 500, sort: "title", direction: "asc" }),
  });
  const { data: allGroups } = useQuery({
    queryKey: ["groups-all"],
    queryFn: () => groupsApi.find({ perPage: 500, sort: "name", direction: "asc" }),
  });

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
    setTagSearch("");
    setPerformerSearch("");
    setGallerySearch("");
    setGroupSearch("");
  }, [image]);

  const mutation = useMutation({
    mutationFn: (data: ImageUpdate) => images.update(image.id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["image", image.id] });
      queryClient.invalidateQueries({ queryKey: ["images"] });
      onSaved();
    },
  });

  const selectedTags = selectedTagIds
    .map((tagId) => allTags?.items.find((tag) => tag.id === tagId) ?? image.tags.find((tag) => tag.id === tagId))
    .filter((tag): tag is NonNullable<typeof tag> => Boolean(tag));
  const selectedPerformers = selectedPerformerIds
    .map((performerId) => allPerformers?.items.find((performer) => performer.id === performerId) ?? image.performers.find((performer) => performer.id === performerId))
    .filter((performer): performer is NonNullable<typeof performer> => Boolean(performer));
  const selectedGalleries = selectedGalleryIds
    .map((galleryId) => allGalleries?.items.find((gallery) => gallery.id === galleryId) ?? image.galleries.find((gallery) => gallery.id === galleryId))
    .filter((gallery): gallery is NonNullable<typeof gallery> => Boolean(gallery));
  const selectedGroupIds = selectedGroups.map((group) => group.groupId);
  const selectedGroupItems = selectedGroups
    .map((groupInput) => allGroups?.items.find((group) => group.id === groupInput.groupId) ?? image.groups?.find((group) => group.id === groupInput.groupId))
    .filter((group): group is NonNullable<typeof group> => Boolean(group));

  const filteredTags = filterTagsForSelector(allTags?.items ?? [], tagSearch, selectedTagIds);
  const filteredPerformers = allPerformers?.items.filter(
    (performer) => !selectedPerformerIds.includes(performer.id) && performer.name.toLowerCase().includes(performerSearch.toLowerCase()),
  ) ?? [];
  const filteredGalleries = allGalleries?.items.filter(
    (gallery) => !selectedGalleryIds.includes(gallery.id) && (gallery.title ?? "").toLowerCase().includes(gallerySearch.toLowerCase()),
  ) ?? [];
  const filteredGroups = allGroups?.items.filter(
    (group) => !selectedGroupIds.includes(group.id) && group.name.toLowerCase().includes(groupSearch.toLowerCase()),
  ) ?? [];

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
        <span className="text-xs text-secondary">Galleries</span>
        <div className="mb-1 flex flex-wrap gap-1.5">
          {selectedGalleries.map((gallery) => (
            <span key={gallery.id} className="inline-flex items-center gap-1 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs text-emerald-300">
              {gallery.title || "Untitled gallery"}
              <button type="button" onClick={() => setSelectedGalleryIds(selectedGalleryIds.filter((id) => id !== gallery.id))} className="hover:text-foreground">
                x
              </button>
            </span>
          ))}
        </div>
        <input value={gallerySearch} onChange={(event) => setGallerySearch(event.target.value)} placeholder="Search galleries..." className={inputCls} />
        {gallerySearch.trim() && filteredGalleries.length > 0 ? (
          <div className="mt-1 max-h-28 overflow-y-auto rounded-lg border border-border bg-surface">
            {filteredGalleries.slice(0, 12).map((gallery) => (
              <button
                key={gallery.id}
                type="button"
                onClick={() => {
                  setSelectedGalleryIds([...selectedGalleryIds, gallery.id]);
                  setGallerySearch("");
                }}
                className="block w-full px-3 py-1.5 text-left text-sm text-foreground transition hover:bg-card"
              >
                {gallery.title || "Untitled gallery"}
              </button>
            ))}
          </div>
        ) : null}
      </div>

      <div className="space-y-1">
        <span className="text-xs text-secondary">Groups</span>
        <div className="mb-1 flex flex-wrap gap-1.5">
          {selectedGroupItems.map((group) => (
            <span key={group.id} className="inline-flex items-center gap-1 rounded-full bg-orange-500/10 px-2 py-0.5 text-xs text-orange-300">
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