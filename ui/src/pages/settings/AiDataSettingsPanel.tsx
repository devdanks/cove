import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Database, RefreshCw, Search, Trash2 } from "lucide-react";

import { aiData, aiFaces, jobs } from "../../api/client";
import type { AiDataKind, AiDataSelector, AiDataSummaryItem } from "../../api/types";
import { ConfirmDialog } from "../../components/ConfirmDialog";

const KIND_OPTIONS: Array<{ value: AiDataKind; label: string }> = [
  { value: "embedding", label: "Embeddings" },
  { value: "detection", label: "Detections" },
  { value: "segment", label: "Segments" },
  { value: "tagApplication", label: "Tag Provenance" },
  { value: "face", label: "Faces" },
];

const MODALITY_OPTIONS = ["visual", "audio", "face", "text", "other"];
const HOST_TYPE_OPTIONS = ["scene", "image", "performer", "face", "segment", "audio"];

interface FilterDraft {
  sourceKey: string;
  sourceRunId: string;
  model: string;
  modality: string;
  hostType: string;
  hostId: string;
  kinds: AiDataKind[];
}

const EMPTY_FILTERS: FilterDraft = {
  sourceKey: "",
  sourceRunId: "",
  model: "",
  modality: "",
  hostType: "",
  hostId: "",
  kinds: [],
};

export function AiDataSettingsPanel() {
  const queryClient = useQueryClient();
  const [filters, setFilters] = useState<FilterDraft>(EMPTY_FILTERS);
  const [previewSelector, setPreviewSelector] = useState<AiDataSelector | null>(null);
  const [previewKey, setPreviewKey] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [referenceImportJobId, setReferenceImportJobId] = useState<string | null>(null);

  const selector = useMemo(() => buildSelector(filters), [filters]);
  const selectorKey = JSON.stringify(selector);

  const summaryQuery = useQuery({
    queryKey: ["ai-data", "summary", "overall"],
    queryFn: () => aiData.summary(),
  });

  const previewQuery = useQuery({
    queryKey: ["ai-data", "summary", "preview", previewKey],
    queryFn: () => aiData.summary(previewSelector ?? undefined),
    enabled: previewSelector !== null,
  });

  const referenceStatusQuery = useQuery({
    queryKey: ["ai-faces", "reference", "status"],
    queryFn: () => aiFaces.referenceStatus(),
  });

  const referenceImportJobQuery = useQuery({
    queryKey: ["jobs", "ai-faces-reference", referenceImportJobId],
    queryFn: () => jobs.get(referenceImportJobId!),
    enabled: referenceImportJobId !== null,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === "completed" || status === "failed" || status === "cancelled" ? false : 1500;
    },
  });

  const purgeMutation = useMutation({
    mutationFn: (payload: AiDataSelector) => aiData.purge(payload),
    onSuccess: async () => {
      setConfirmOpen(false);
      await queryClient.invalidateQueries({ queryKey: ["ai-data", "summary"] });
      setPreviewSelector(selector);
      setPreviewKey(selectorKey);
    },
  });

  const repairFaceCoversMutation = useMutation({
    mutationFn: () => aiFaces.repairMissingCovers({ force: true }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["face"] });
      await queryClient.invalidateQueries({ queryKey: ["faces"] });
    },
  });

  const importReferencePackMutation = useMutation({
    mutationFn: (file: File) => aiFaces.importReferencePack(file),
    onSuccess: async (result) => {
      setReferenceImportJobId(result.jobId);
      await queryClient.invalidateQueries({ queryKey: ["jobs"] });
    },
  });

  const clearReferencePackMutation = useMutation({
    mutationFn: () => aiFaces.clearReferencePack(),
    onSuccess: async () => {
      setReferenceImportJobId(null);
      await queryClient.invalidateQueries({ queryKey: ["ai-faces", "reference", "status"] });
    },
  });

  useEffect(() => {
    const status = referenceImportJobQuery.data?.status;
    if (status === "completed" || status === "failed" || status === "cancelled") {
      queryClient.invalidateQueries({ queryKey: ["ai-faces", "reference", "status"] });
    }
  }, [referenceImportJobQuery.data?.status, queryClient]);

  const overallSummary = summaryQuery.data;
  const previewSummary = previewQuery.data;
  const previewMatchesCurrent = previewKey === selectorKey;
  const activeSummary = previewSummary && previewMatchesCurrent ? previewSummary : overallSummary;
  const hasActiveFilters = selectorKey !== JSON.stringify({});
  const canPurge = Boolean(previewSummary && previewMatchesCurrent && previewSummary.totalCount > 0 && !purgeMutation.isPending);

  return (
    <div className="space-y-5">
      <ConfirmDialog
        open={confirmOpen}
        title="Purge AI Data"
        message={previewSummary
          ? `Remove ${previewSummary.totalCount} AI artifact row(s) that match the current preview? This also removes AI-only tag links when their provenance is deleted.`
          : "Preview the current selector before purging."}
        confirmLabel="Purge"
        onConfirm={() => {
          if (previewSelector) {
            purgeMutation.mutate(previewSelector);
          }
        }}
        onCancel={() => setConfirmOpen(false)}
      />

      <section className="rounded-2xl border border-border bg-surface p-5 shadow-[0_12px_30px_-20px_rgba(0,0,0,0.7)]">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h3 className="text-base font-semibold text-foreground">AI Artifact Totals</h3>
            <p className="mt-1 text-sm text-secondary">Current counts across embeddings, detections, timeline segments, tag provenance, and face-owned AI state.</p>
          </div>
          <button
            type="button"
            onClick={() => {
              queryClient.invalidateQueries({ queryKey: ["ai-data", "summary"] });
            }}
            className="inline-flex items-center gap-2 rounded-xl border border-border bg-card px-3 py-2 text-sm text-secondary transition hover:text-foreground"
          >
            <RefreshCw className={`h-4 w-4 ${summaryQuery.isFetching ? "animate-spin" : ""}`} />
            Refresh
          </button>
        </div>

        <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
          {KIND_OPTIONS.map((option) => (
            <SummaryCard key={option.value} label={option.label} value={overallSummary?.totals?.[option.value] ?? 0} />
          ))}
        </div>
      </section>

      <section className="rounded-2xl border border-border bg-surface p-5 shadow-[0_12px_30px_-20px_rgba(0,0,0,0.7)]">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <h3 className="text-base font-semibold text-foreground">Reference Face DB</h3>
            <p className="mt-1 text-sm text-secondary">Import a `.saie` archive so AI.Faces can suggest performers from an external face reference database.</p>
          </div>
          <label className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-border bg-card px-3 py-2 text-sm font-medium text-foreground transition hover:border-accent disabled:cursor-not-allowed disabled:opacity-50">
            <Database className="h-4 w-4" />
            {importReferencePackMutation.isPending ? "Uploading..." : "Upload .saie pack"}
            <input
              type="file"
              accept=".saie"
              className="hidden"
              disabled={importReferencePackMutation.isPending}
              onChange={(event) => {
                const file = event.target.files?.[0];
                if (file) {
                  importReferencePackMutation.mutate(file);
                }

                event.target.value = "";
              }}
            />
          </label>
        </div>

        {referenceImportJobQuery.data ? (
          <div className="mt-4 rounded-xl border border-border bg-card p-4 text-sm text-secondary">
            Import job {referenceImportJobQuery.data.id} is {referenceImportJobQuery.data.status.toLowerCase()} at {Math.round(referenceImportJobQuery.data.progress * 100)}%.
            {referenceImportJobQuery.data.subTask ? ` ${referenceImportJobQuery.data.subTask}` : ""}
          </div>
        ) : null}

        {referenceStatusQuery.data ? (
          <div className="mt-4 grid gap-3 md:grid-cols-2">
            <div className="rounded-xl border border-border bg-card p-4 text-sm text-secondary">
              <div className="text-[11px] font-semibold uppercase tracking-wide text-muted">Pack</div>
              <div className="mt-2 text-base font-semibold text-foreground">{referenceStatusQuery.data.packId}</div>
              <div className="mt-1">{referenceStatusQuery.data.performerCount.toLocaleString()} identities â€¢ {referenceStatusQuery.data.embeddingDim} dims</div>
            </div>
            <div className="rounded-xl border border-border bg-card p-4 text-sm text-secondary">
              <div className="text-[11px] font-semibold uppercase tracking-wide text-muted">Source</div>
              <div className="mt-2 break-all text-foreground">{referenceStatusQuery.data.sourceEndpoint ?? "Unknown source"}</div>
              <div className="mt-1">Imported {new Date(referenceStatusQuery.data.importedAt).toLocaleString()}</div>
            </div>
          </div>
        ) : referenceStatusQuery.isLoading ? (
          <div className="mt-4 rounded-xl border border-border bg-card p-4 text-sm text-secondary">Loading reference pack status...</div>
        ) : (
          <div className="mt-4 rounded-xl border border-dashed border-border px-4 py-6 text-sm text-secondary">No `.saie` reference pack is currently imported.</div>
        )}

        {referenceStatusQuery.data ? (
          <div className="mt-4 flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => {
                if (window.confirm("Remove the imported AI.Faces reference pack and clear its cached suggestion source?")) {
                  clearReferencePackMutation.mutate();
                }
              }}
              disabled={clearReferencePackMutation.isPending}
              className="inline-flex items-center gap-2 rounded-xl border border-border bg-card px-3 py-2 text-sm font-medium text-foreground transition hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Trash2 className="h-4 w-4" />
              {clearReferencePackMutation.isPending ? "Clearing..." : "Clear imported reference pack"}
            </button>
          </div>
        ) : null}
      </section>

      <section className="rounded-2xl border border-border bg-surface p-5 shadow-[0_12px_30px_-20px_rgba(0,0,0,0.7)]">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <h3 className="text-base font-semibold text-foreground">Face Cover Actions</h3>
            <p className="mt-1 text-sm text-secondary">Recompute AI face covers using the strongest AI.Faces exemplar so face detail pages pick sharper, larger crops.</p>
          </div>
          <button
            type="button"
            onClick={() => {
              if (window.confirm("Regenerate face covers for all faces with stored AI.Faces cover metadata? Existing face covers will be replaced when a better exemplar is available.")) {
                repairFaceCoversMutation.mutate();
              }
            }}
            disabled={repairFaceCoversMutation.isPending}
            className="inline-flex items-center gap-2 rounded-xl border border-border bg-card px-3 py-2 text-sm font-medium text-foreground transition hover:border-accent disabled:cursor-not-allowed disabled:opacity-50"
          >
            <RefreshCw className={`h-4 w-4 ${repairFaceCoversMutation.isPending ? "animate-spin" : ""}`} />
            {repairFaceCoversMutation.isPending ? "Regenerating..." : "Regenerate face covers"}
          </button>
        </div>

        {repairFaceCoversMutation.data ? (
          <div className="mt-4 space-y-3">
            <div className="rounded-xl border border-border bg-card p-4 text-sm text-secondary">
              Scanned {repairFaceCoversMutation.data.scannedCount} faces, repaired {repairFaceCoversMutation.data.repairedCount}, skipped {repairFaceCoversMutation.data.skippedCount}, failed {repairFaceCoversMutation.data.failedCount}.
            </div>
            {repairFaceCoversMutation.data.errors.length > 0 ? (
              <div className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 text-sm text-amber-100">
                {repairFaceCoversMutation.data.errors.slice(0, 3).map((error) => (
                  <div key={error}>{error}</div>
                ))}
              </div>
            ) : null}
          </div>
        ) : null}
      </section>

      <section className="rounded-2xl border border-border bg-surface p-5 shadow-[0_12px_30px_-20px_rgba(0,0,0,0.7)]">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <h3 className="text-base font-semibold text-foreground">Selector</h3>
            <p className="mt-1 text-sm text-secondary">Preview a selector before running a destructive purge. Leaving fields empty broadens the match.</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => {
                setPreviewSelector(selector);
                setPreviewKey(selectorKey);
              }}
              className="inline-flex items-center gap-2 rounded-xl bg-accent px-3 py-2 text-sm font-medium text-white transition hover:bg-accent-hover"
            >
              <Search className="h-4 w-4" />
              Preview
            </button>
            <button
              type="button"
              onClick={() => {
                setFilters(EMPTY_FILTERS);
                setPreviewSelector(null);
                setPreviewKey(null);
              }}
              className="inline-flex items-center gap-2 rounded-xl border border-border bg-card px-3 py-2 text-sm text-secondary transition hover:text-foreground"
            >
              <RefreshCw className="h-4 w-4" />
              Reset
            </button>
            <button
              type="button"
              onClick={() => setConfirmOpen(true)}
              disabled={!canPurge}
              className="inline-flex items-center gap-2 rounded-xl bg-red-600 px-3 py-2 text-sm font-medium text-white transition enabled:hover:bg-red-500 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Trash2 className="h-4 w-4" />
              Purge
            </button>
          </div>
        </div>

        <div className="mt-4 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          <LabeledInput label="Source key" value={filters.sourceKey} onChange={(value) => setFilters((current) => ({ ...current, sourceKey: value }))} placeholder="ext:ai.tagging" />
          <LabeledInput label="Source run id" value={filters.sourceRunId} onChange={(value) => setFilters((current) => ({ ...current, sourceRunId: value }))} placeholder="run-1234" />
          <LabeledInput label="Model" value={filters.model} onChange={(value) => setFilters((current) => ({ ...current, model: value }))} placeholder="tagger-v1" />
          <LabeledSelect label="Modality" value={filters.modality} onChange={(value) => setFilters((current) => ({ ...current, modality: value }))} options={MODALITY_OPTIONS} />
          <LabeledSelect label="Host type" value={filters.hostType} onChange={(value) => setFilters((current) => ({ ...current, hostType: value }))} options={HOST_TYPE_OPTIONS} />
          <LabeledInput label="Host id" value={filters.hostId} onChange={(value) => setFilters((current) => ({ ...current, hostId: value }))} placeholder="42" />
        </div>

        <div className="mt-4">
          <div className="mb-2 text-xs font-semibold uppercase tracking-[0.16em] text-muted">Kinds</div>
          <div className="flex flex-wrap gap-2">
            {KIND_OPTIONS.map((option) => {
              const selected = filters.kinds.includes(option.value);
              return (
                <button
                  key={option.value}
                  type="button"
                  onClick={() => {
                    setFilters((current) => ({
                      ...current,
                      kinds: selected
                        ? current.kinds.filter((kind) => kind !== option.value)
                        : [...current.kinds, option.value],
                    }));
                  }}
                  className={`rounded-full border px-3 py-1.5 text-sm transition ${selected ? "border-accent bg-accent/15 text-foreground" : "border-border bg-card text-secondary hover:text-foreground"}`}
                >
                  {option.label}
                </button>
              );
            })}
          </div>
        </div>

        {!previewMatchesCurrent && previewSummary ? (
          <div className="mt-4 rounded-xl border border-amber-500/40 bg-amber-500/10 px-4 py-3 text-sm text-amber-100">
            Filters changed after the last preview. Run Preview again before purging.
          </div>
        ) : null}

        {previewSummary && previewMatchesCurrent ? (
          <div className="mt-4 rounded-xl border border-border bg-card p-4">
            <div className="flex items-center gap-2 text-sm font-medium text-foreground">
              <Database className="h-4 w-4 text-accent" />
              Preview matches {previewSummary.totalCount} row(s)
            </div>
            <p className="mt-1 text-sm text-secondary">
              {hasActiveFilters ? "This preview is scoped to the current selector." : "No filters are set, so the preview spans all AI-managed artifacts."}
            </p>
          </div>
        ) : null}
      </section>

      <section className="rounded-2xl border border-border bg-surface p-5 shadow-[0_12px_30px_-20px_rgba(0,0,0,0.7)]">
        <div>
          <h3 className="text-base font-semibold text-foreground">{previewSummary && previewMatchesCurrent ? "Preview Results" : "Summary Table"}</h3>
          <p className="mt-1 text-sm text-secondary">Grouped by artifact kind, detail, provenance source, model, and host type.</p>
        </div>

        {summaryQuery.isLoading || previewQuery.isFetching ? (
          <div className="mt-6 flex items-center justify-center py-10 text-secondary">
            <RefreshCw className="mr-2 h-4 w-4 animate-spin" />
            Loading AI data summary...
          </div>
        ) : activeSummary && activeSummary.items.length > 0 ? (
          <div className="mt-4 overflow-x-auto">
            <table className="min-w-full text-left text-sm">
              <thead className="text-xs uppercase tracking-[0.12em] text-muted">
                <tr>
                  <th className="px-3 py-2">Kind</th>
                  <th className="px-3 py-2">Detail</th>
                  <th className="px-3 py-2">Source</th>
                  <th className="px-3 py-2">Run</th>
                  <th className="px-3 py-2">Model</th>
                  <th className="px-3 py-2">Host</th>
                  <th className="px-3 py-2 text-right">Count</th>
                </tr>
              </thead>
              <tbody>
                {activeSummary.items.map((item: AiDataSummaryItem) => (
                  <tr key={buildRowKey(item)} className="border-t border-border/70 text-secondary">
                    <td className="px-3 py-2 font-medium text-foreground">{formatKind(item.kind)}</td>
                    <td className="px-3 py-2">{item.detail ?? "-"}</td>
                    <td className="px-3 py-2 break-all">{item.sourceKey}</td>
                    <td className="px-3 py-2 break-all">{item.sourceRunId ?? "-"}</td>
                    <td className="px-3 py-2 break-all">{item.model ?? "-"}</td>
                    <td className="px-3 py-2">{formatKind(item.hostType)}</td>
                    <td className="px-3 py-2 text-right font-medium text-foreground">{item.count.toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <div className="mt-6 rounded-xl border border-dashed border-border px-4 py-10 text-center text-secondary">
            No AI artifacts matched the current selector.
          </div>
        )}
      </section>
    </div>
  );
}

function buildSelector(filters: FilterDraft): AiDataSelector {
  const sourceKey = filters.sourceKey.trim();
  const sourceRunId = filters.sourceRunId.trim();
  const model = filters.model.trim();
  const modality = filters.modality.trim();
  const hostType = filters.hostType.trim();
  const hostId = Number.parseInt(filters.hostId, 10);

  return {
    sourceKey: sourceKey || undefined,
    sourceRunId: sourceRunId || undefined,
    model: model || undefined,
    modality: modality || undefined,
    hostType: hostType || undefined,
    hostId: Number.isFinite(hostId) ? hostId : undefined,
    kinds: filters.kinds.length > 0 ? filters.kinds : undefined,
  };
}

function SummaryCard({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border bg-card p-4">
      <div className="text-xs uppercase tracking-[0.16em] text-muted">{label}</div>
      <div className="mt-2 text-2xl font-semibold text-foreground">{value.toLocaleString()}</div>
    </div>
  );
}

function LabeledInput({ label, value, onChange, placeholder }: { label: string; value: string; onChange: (value: string) => void; placeholder?: string }) {
  return (
    <label className="flex flex-col gap-2 text-sm text-secondary">
      <span>{label}</span>
      <input
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        className="rounded-xl border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
      />
    </label>
  );
}

function LabeledSelect({ label, value, onChange, options }: { label: string; value: string; onChange: (value: string) => void; options: string[] }) {
  return (
    <label className="flex flex-col gap-2 text-sm text-secondary">
      <span>{label}</span>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className="rounded-xl border border-border bg-card px-3 py-2 text-sm text-foreground focus:border-accent focus:outline-none"
      >
        <option value="">Any</option>
        {options.map((option) => (
          <option key={option} value={option}>{formatKind(option)}</option>
        ))}
      </select>
    </label>
  );
}

function formatKind(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .split(/[^a-zA-Z0-9]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function buildRowKey(item: AiDataSummaryItem) {
  return [item.kind, item.detail ?? "", item.sourceKey, item.sourceRunId ?? "", item.model ?? "", item.hostType].join("::");
}