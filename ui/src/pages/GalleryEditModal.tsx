import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { galleries } from "../api/client";
import type { Gallery, GalleryUpdate } from "../api/types";
import { EditModal, Field, TextInput, TextArea, SaveButton } from "../components/EditModal";
import { InteractiveRatingField } from "../components/Rating";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { StudioSelector } from "../components/StudioSelector";
import { EntityReferenceMultiSelector } from "../components/EntityReferenceSelector";

interface Props {
  gallery: Gallery;
  open: boolean;
  onClose: () => void;
}

export function GalleryEditModal({ gallery, open, onClose }: Props) {
  const qc = useQueryClient();
  const [form, setForm] = useState({
    title: gallery.title ?? "",
    code: gallery.code ?? "",
    date: gallery.date ?? "",
    details: gallery.details ?? "",
    photographer: gallery.photographer ?? "",
    rating: undefined as number | undefined,
    organized: gallery.organized,
    studioId: gallery.studioId,
    urls: gallery.urls.length > 0 ? gallery.urls : [""],
    tagIds: gallery.tags.map((t) => t.id),
    performerIds: gallery.performers.map((p) => p.id),
    sceneIds: gallery.sceneIds ?? [],
  });
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(gallery.customFields ?? {}) });

  const mutation = useMutation({
    mutationFn: (data: GalleryUpdate) => galleries.update(gallery.id, data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["gallery", gallery.id] });
      qc.invalidateQueries({ queryKey: ["galleries"] });
      onClose();
    },
  });

  const save = () => {
    mutation.mutate({
      title: form.title || undefined,
      code: form.code || undefined,
      date: form.date || undefined,
      details: form.details || undefined,
      photographer: form.photographer || undefined,
      rating: form.rating,
      organized: form.organized,
      studioId: form.studioId,
      urls: form.urls.map((url) => url.trim()).filter(Boolean),
      tagIds: form.tagIds,
      performerIds: form.performerIds,
      sceneIds: form.sceneIds,
      customFields,
    });
  };

  return (
    <EditModal title={`Edit Gallery: ${gallery.title || "Untitled"}`} open={open} onClose={onClose}>
      <div className="grid grid-cols-2 gap-4">
        <div className="col-span-2">
          <Field label="Title">
            <TextInput value={form.title} onChange={(v) => setForm({ ...form, title: v })} />
          </Field>
        </div>
        <Field label="Studio Code">
          <TextInput value={form.code} onChange={(v) => setForm({ ...form, code: v })} />
        </Field>
        <Field label="Date">
          <TextInput value={form.date} onChange={(v) => setForm({ ...form, date: v })} placeholder="YYYY-MM-DD" />
        </Field>
        <Field label="Photographer">
          <TextInput value={form.photographer} onChange={(v) => setForm({ ...form, photographer: v })} />
        </Field>
        <InteractiveRatingField label="Rating" value={form.rating} onChange={(v) => setForm({ ...form, rating: v })} />
        <Field label="Studio">
          <StudioSelector value={form.studioId} onChange={(studioId) => setForm({ ...form, studioId })} />
        </Field>
        <div className="flex items-end pb-4">
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={form.organized} onChange={(e) => setForm({ ...form, organized: e.target.checked })} className="rounded bg-card border-border" />
            Organized
          </label>
        </div>
      </div>
      <Field label="Details">
        <TextArea value={form.details} onChange={(v) => setForm({ ...form, details: v })} rows={3} />
      </Field>
      <Field label="URLs">
        <StringListEditor values={form.urls} onChange={(value) => setForm({ ...form, urls: value })} placeholder="https://..." addLabel="Add URL" inputType="url" />
      </Field>

      {/* Tags picker */}
      <Field label="Tags">
        <EntityReferenceMultiSelector entityType="tag" values={form.tagIds} onChange={(tagIds) => setForm({ ...form, tagIds })} placeholder="Search tags..." />
      </Field>

      {/* Performers picker */}
      <Field label="Performers">
        <EntityReferenceMultiSelector entityType="performer" values={form.performerIds} onChange={(performerIds) => setForm({ ...form, performerIds })} placeholder="Search performers..." />
      </Field>

      {/* Scenes */}
      <Field label="Scenes">
        <EntityReferenceMultiSelector entityType="scene" values={form.sceneIds} onChange={(sceneIds) => setForm({ ...form, sceneIds })} placeholder="Search scenes..." />
      </Field>

      <Field label="Custom Fields">
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="gallery" />
      </Field>

      <div className="flex justify-end mt-4">
        <SaveButton loading={mutation.isPending} onClick={save} />
      </div>
    </EditModal>
  );
}
