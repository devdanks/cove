import { useState, useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { performers, tags as tagsApi, entityImages } from "../api/client";
import { ImageInput } from "../components/ImageInput";
import type { Performer, PerformerUpdate } from "../api/types";
import { EditModal, Field, TextInput, TextArea, NumberInput, SaveButton } from "../components/EditModal";
import { InteractiveRatingField } from "../components/Rating";
import { CustomFieldsEditor } from "../components/shared";
import { StringListEditor } from "../components/StringListEditor";
import { RemoteIdsEditor, normalizeRemoteIds, type RemoteIdValue } from "../components/RemoteIdsEditor";
import { GroupedTagOptionList, SelectedTagChips, type SelectableTag } from "../components/TagSelector";

interface Props {
  performer: Performer;
  open: boolean;
  onClose: () => void;
}

const GENDER_OPTIONS = [
  { value: "Male", label: "Male" },
  { value: "Female", label: "Female" },
  { value: "TransMale", label: "Trans Male" },
  { value: "TransFemale", label: "Trans Female" },
  { value: "Intersex", label: "Intersex" },
  { value: "NonBinary", label: "Non-Binary" },
];

const CIRCUMCISED_OPTIONS = [
  { value: "Cut", label: "Cut" },
  { value: "Uncut", label: "Uncut" },
];

type SelectedTagOption = SelectableTag;

function buildSelectedTagLookup(tags: Performer["tags"]): Record<number, SelectedTagOption> {
  return Object.fromEntries(tags.map((tag) => [tag.id, tag])) as Record<number, SelectedTagOption>;
}

export function PerformerEditModal({ performer, open, onClose }: Props) {
  const queryClient = useQueryClient();

  const [name, setName] = useState(performer.name);
  const [disambiguation, setDisambiguation] = useState(performer.disambiguation || "");
  const [gender, setGender] = useState(performer.gender || "");
  const [birthdate, setBirthdate] = useState(performer.birthdate || "");
  const [ethnicity, setEthnicity] = useState(performer.ethnicity || "");
  const [country, setCountry] = useState(performer.country || "");
  const [eyeColor, setEyeColor] = useState(performer.eyeColor || "");
  const [hairColor, setHairColor] = useState(performer.hairColor || "");
  const [heightCm, setHeightCm] = useState<number | undefined>(performer.heightCm ?? undefined);
  const [weight, setWeight] = useState<number | undefined>(performer.weight ?? undefined);
  const [measurements, setMeasurements] = useState(performer.measurements || "");
  const [tattoos, setTattoos] = useState(performer.tattoos || "");
  const [piercings, setPiercings] = useState(performer.piercings || "");
  const [rating, setRating] = useState<number | undefined>(undefined);
  const [details, setDetails] = useState(performer.details || "");
  const [deathDate, setDeathDate] = useState(performer.deathDate || "");
  const [fakeTits, setFakeTits] = useState(performer.fakeTits || "");
  const [penisLength, setPenisLength] = useState<number | undefined>(performer.penisLength ?? undefined);
  const [circumcised, setCircumcised] = useState(performer.circumcised || "");
  const [careerStart, setCareerStart] = useState(performer.careerStart || "");
  const [careerEnd, setCareerEnd] = useState(performer.careerEnd || "");
  const [ignoreAutoTag, setIgnoreAutoTag] = useState(performer.ignoreAutoTag ?? false);
  const [urls, setUrls] = useState(performer.urls.length > 0 ? performer.urls : [""]);
  const [aliases, setAliases] = useState(performer.aliases.length > 0 ? performer.aliases : [""]);
  const [selectedTagIds, setSelectedTagIds] = useState<number[]>(performer.tags.map((t) => t.id));
  const [selectedTagsById, setSelectedTagsById] = useState<Record<number, SelectedTagOption>>(() => buildSelectedTagLookup(performer.tags));
  const [tagSearch, setTagSearch] = useState("");
  const [customFields, setCustomFields] = useState<Record<string, unknown>>({ ...(performer.customFields ?? {}) });
  const [remoteIds, setRemoteIds] = useState<RemoteIdValue[]>(performer.remoteIds.map((remoteId) => ({ ...remoteId })));
  const trimmedTagSearch = tagSearch.trim();

  const { data: tagResults, isLoading: tagResultsLoading } = useQuery({
    queryKey: ["performer-tags-search", trimmedTagSearch],
    queryFn: () => tagsApi.find({ q: trimmedTagSearch, perPage: 20, sort: "name", direction: "asc" }),
    enabled: trimmedTagSearch.length > 0,
    staleTime: 60000,
  });

  useEffect(() => {
    setName(performer.name);
    setDisambiguation(performer.disambiguation || "");
    setGender(performer.gender || "");
    setBirthdate(performer.birthdate || "");
    setEthnicity(performer.ethnicity || "");
    setCountry(performer.country || "");
    setEyeColor(performer.eyeColor || "");
    setHairColor(performer.hairColor || "");
    setHeightCm(performer.heightCm ?? undefined);
    setWeight(performer.weight ?? undefined);
    setMeasurements(performer.measurements || "");
    setTattoos(performer.tattoos || "");
    setPiercings(performer.piercings || "");
    setRating(undefined);
    setDetails(performer.details || "");
    setDeathDate(performer.deathDate || "");
    setFakeTits(performer.fakeTits || "");
    setPenisLength(performer.penisLength ?? undefined);
    setCircumcised(performer.circumcised || "");
    setCareerStart(performer.careerStart || "");
    setCareerEnd(performer.careerEnd || "");
    setIgnoreAutoTag(performer.ignoreAutoTag ?? false);
    setUrls(performer.urls.length > 0 ? performer.urls : [""]);
    setAliases(performer.aliases.length > 0 ? performer.aliases : [""]);
    setSelectedTagIds(performer.tags.map((t) => t.id));
    setSelectedTagsById(buildSelectedTagLookup(performer.tags));
    setTagSearch("");
    setCustomFields({ ...(performer.customFields ?? {}) });
    setRemoteIds(performer.remoteIds.map((remoteId) => ({ ...remoteId })));
  }, [performer]);

  const mutation = useMutation({
    mutationFn: (data: PerformerUpdate) => performers.update(performer.id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["performer", performer.id] });
      queryClient.invalidateQueries({ queryKey: ["performers"] });
      onClose();
    },
  });

  const handleSave = () => {
    const urlList = urls.map((url) => url.trim()).filter(Boolean);
    const aliasList = aliases.map((alias) => alias.trim()).filter(Boolean);
    mutation.mutate({
      name,
      disambiguation: disambiguation || undefined,
      gender: gender || undefined,
      birthdate: birthdate || undefined,
      ethnicity: ethnicity || undefined,
      country: country || undefined,
      eyeColor: eyeColor || undefined,
      hairColor: hairColor || undefined,
      heightCm,
      weight,
      measurements: measurements || undefined,
      tattoos: tattoos || undefined,
      piercings: piercings || undefined,
      deathDate: deathDate || undefined,
      fakeTits: fakeTits || undefined,
      penisLength,
      circumcised: circumcised || undefined,
      careerStart: careerStart || undefined,
      careerEnd: careerEnd || undefined,
      rating,
      details: details || undefined,
      ignoreAutoTag,
      urls: urlList,
      aliases: aliasList,
      tagIds: selectedTagIds,
      customFields,
      remoteIds: normalizeRemoteIds(remoteIds),
    });
  };

  const filteredTags = tagResults?.items.filter((tag) => !selectedTagIds.includes(tag.id)) ?? [];
  const selectedTags = selectedTagIds
    .map((tagId) => selectedTagsById[tagId])
    .filter((tag): tag is SelectedTagOption => Boolean(tag));

  const addTag = (tag: SelectedTagOption) => {
    setSelectedTagIds((current) => current.includes(tag.id) ? current : [...current, tag.id]);
    setSelectedTagsById((current) => ({ ...current, [tag.id]: tag }));
    setTagSearch("");
  };

  return (
    <EditModal title="Edit Performer" open={open} onClose={onClose}>
      <div className="grid grid-cols-[200px_1fr] gap-6">
        <ImageInput
          currentImageUrl={entityImages.performerImageUrl(performer.id, performer.updatedAt)}
          onUpload={(file) => entityImages.uploadPerformerImage(performer.id, file)}
          onDelete={() => entityImages.deletePerformerImage(performer.id)}
          onSuccess={() => queryClient.invalidateQueries({ queryKey: ["performer", performer.id] })}
          label="Photo"
          aspectRatio="2/3"
        />
        <div className="space-y-4">
      <div className="grid grid-cols-2 gap-4">
        <Field label="Name *">
          <TextInput value={name} onChange={setName} placeholder="Performer name" />
        </Field>
        <Field label="Disambiguation">
          <TextInput value={disambiguation} onChange={setDisambiguation} placeholder="e.g. (2020s)" />
        </Field>
      </div>

      <div className="grid grid-cols-4 gap-4">
        <Field label="Gender">
          <select
            value={gender}
            onChange={(e) => setGender(e.target.value)}
            className="w-full bg-card border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          >
            <option value="">—</option>
            {GENDER_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </Field>
        <Field label="Birthdate">
          <input
            type="date"
            value={birthdate}
            onChange={(e) => setBirthdate(e.target.value)}
            className="w-full bg-card border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          />
        </Field>
        <Field label="Death Date">
          <input
            type="date"
            value={deathDate}
            onChange={(e) => setDeathDate(e.target.value)}
            className="w-full bg-card border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          />
        </Field>
        <Field label="Country">
          <TextInput value={country} onChange={setCountry} placeholder="e.g. US" />
        </Field>
      </div>

      <div className="grid grid-cols-3 gap-4">
        <Field label="Ethnicity">
          <TextInput value={ethnicity} onChange={setEthnicity} />
        </Field>
        <Field label="Eye Color">
          <TextInput value={eyeColor} onChange={setEyeColor} />
        </Field>
        <Field label="Hair Color">
          <TextInput value={hairColor} onChange={setHairColor} />
        </Field>
      </div>

      <div className="grid grid-cols-3 gap-4">
        <Field label="Height (cm)">
          <NumberInput value={heightCm} onChange={setHeightCm} min={50} max={250} />
        </Field>
        <Field label="Weight (kg)">
          <NumberInput value={weight} onChange={setWeight} min={20} max={300} />
        </Field>
        <Field label="Measurements">
          <TextInput value={measurements} onChange={setMeasurements} placeholder="34D-24-34" />
        </Field>
      </div>

      <div className="grid grid-cols-2 gap-4">
        <Field label="Tattoos">
          <TextInput value={tattoos} onChange={setTattoos} />
        </Field>
        <Field label="Piercings">
          <TextInput value={piercings} onChange={setPiercings} />
        </Field>
      </div>

      <div className="grid grid-cols-3 gap-4">
        <Field label="Fake Tits">
          <TextInput value={fakeTits} onChange={setFakeTits} placeholder="e.g. Augmented" />
        </Field>
        <Field label="Penis Length (cm)">
          <NumberInput value={penisLength} onChange={setPenisLength} min={0} max={50} />
        </Field>
        <Field label="Circumcised">
          <select
            value={circumcised}
            onChange={(e) => setCircumcised(e.target.value)}
            className="w-full bg-card border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          >
            <option value="">—</option>
            {CIRCUMCISED_OPTIONS.map((o) => (
              <option key={o.value} value={o.value}>{o.label}</option>
            ))}
          </select>
        </Field>
      </div>

      <div className="grid grid-cols-2 gap-4">
        <Field label="Career Start">
          <input
            type="date"
            value={careerStart}
            onChange={(e) => setCareerStart(e.target.value)}
            className="w-full bg-card border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          />
        </Field>
        <Field label="Career End">
          <input
            type="date"
            value={careerEnd}
            onChange={(e) => setCareerEnd(e.target.value)}
            className="w-full bg-card border border-border rounded px-3 py-2 text-sm text-foreground focus:outline-none focus:border-accent"
          />
        </Field>
      </div>

      <Field label="Details">
        <TextArea value={details} onChange={setDetails} placeholder="Bio / notes" rows={2} />
      </Field>

      <div className="grid grid-cols-2 gap-4">
        <InteractiveRatingField value={rating} onChange={setRating} label="Rating" />
        <Field label="Aliases">
          <StringListEditor values={aliases} onChange={setAliases} placeholder="Alias" addLabel="Add Alias" />
        </Field>
      </div>

      <Field label="URLs">
        <StringListEditor values={urls} onChange={setUrls} placeholder="https://..." addLabel="Add URL" inputType="url" />
      </Field>

      {/* Tags */}
      <Field label="Tags">
        <SelectedTagChips tags={selectedTags} onRemove={(tag) => setSelectedTagIds((current) => current.filter((id) => id !== tag.id))} className="mb-2 flex flex-wrap gap-1.5" />
        <input
          type="text"
          value={tagSearch}
          onChange={(e) => setTagSearch(e.target.value)}
          placeholder="Search tags..."
          className="w-full bg-card border border-border rounded px-3 py-1.5 text-sm text-foreground focus:outline-none focus:border-accent mb-1"
        />
        {trimmedTagSearch && (
          <div className="max-h-32 overflow-y-auto bg-card rounded border border-border">
            {tagResultsLoading ? (
              <div className="px-3 py-1.5 text-sm text-secondary">Loading...</div>
            ) : filteredTags.length === 0 ? (
              <div className="px-3 py-1.5 text-sm text-secondary">No tags found</div>
            ) : (
              <GroupedTagOptionList tags={filteredTags} maxItems={20} className="border-0 bg-transparent" onSelect={addTag} />
            )}
          </div>
        )}
      </Field>

      <div className="flex gap-6 mb-4">
        <label className="flex items-center gap-2 text-sm text-secondary cursor-pointer">
          <input type="checkbox" checked={ignoreAutoTag} onChange={(e) => setIgnoreAutoTag(e.target.checked)} className="rounded border-border bg-card" />
          Ignore Auto Tag
        </label>
      </div>

      <Field label="Remote IDs">
        <RemoteIdsEditor value={remoteIds} onChange={setRemoteIds} />
      </Field>
      <Field label="Custom Fields">
        <CustomFieldsEditor value={customFields} onChange={setCustomFields} entityType="performer" />
      </Field>
        </div>{/* end right column */}
      </div>{/* end grid */}

      {mutation.error && (
        <div className="bg-red-900/50 border border-red-700 text-red-300 rounded p-2 mb-4 text-sm">
          {(mutation.error as Error).message}
        </div>
      )}

      <div className="flex justify-end gap-3">
        <button onClick={onClose} className="px-4 py-2 text-sm text-secondary hover:text-white">Cancel</button>
        <SaveButton loading={mutation.isPending} onClick={handleSave} />
      </div>
    </EditModal>
  );
}
