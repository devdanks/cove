import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { EntityReferenceMultiSelector } from "../components/EntityReferenceSelector";

describe("EntityReferenceMultiSelector", () => {
  it("does not render a remove button for locked tag chips", async () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    queryClient.setQueryData(["entity-reference-selector", "tag", "selected", 1], { id: 1, label: "Manual" });
    queryClient.setQueryData(["entity-reference-selector", "tag", "selected", 2], { id: 2, label: "Derived" });

    render(
      <QueryClientProvider client={queryClient}>
        <EntityReferenceMultiSelector
          entityType="tag"
          values={[1, 2]}
          lockedIds={[2]}
          onChange={vi.fn()}
        />
      </QueryClientProvider>,
    );

    expect(await screen.findByText("Derived")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Remove Manual" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Derived/i })).not.toBeInTheDocument();
  });
});
