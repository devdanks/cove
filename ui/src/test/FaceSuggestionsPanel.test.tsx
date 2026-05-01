import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { FaceSuggestionsPanel } from "../components/FaceSuggestionsPanel";
import type { FaceSuggestion } from "../api/types";

describe("FaceSuggestionsPanel", () => {
  it("renders the empty state when there are no suggestions", () => {
    render(
      <FaceSuggestionsPanel
        suggestions={[]}
        isLoading={false}
        disabled={false}
        canReadPerformers
        onAccept={vi.fn()}
        onReject={vi.fn()}
        onNavigate={vi.fn()}
      />,
    );

    expect(screen.getByText("Suggested performers")).toBeInTheDocument();
    expect(screen.getByText("No suggestions yet - run AI.Faces.")).toBeInTheDocument();
  });

  it("renders populated suggestions and forwards accept/reject actions", () => {
    const onAccept = vi.fn();
    const onReject = vi.fn();
    const onNavigate = vi.fn();
    const suggestions: FaceSuggestion[] = [
      {
        performerId: 12,
        performerName: "Jane Doe",
        coverImageUrl: "/img/performers/12.jpg",
        confidence: 0.92,
        why: "Two high-similarity face matches from the same source.",
        evidence: [
          { faceId: 51, thumbnailUrl: "/img/faces/51.jpg", similarity: 0.94 },
          { faceId: 52, thumbnailUrl: "/img/faces/52.jpg", similarity: 0.88 },
        ],
      },
    ];

    render(
      <FaceSuggestionsPanel
        suggestions={suggestions}
        isLoading={false}
        disabled={false}
        canReadPerformers
        onAccept={onAccept}
        onReject={onReject}
        onNavigate={onNavigate}
      />,
    );

    expect(screen.getByText("Jane Doe")).toBeInTheDocument();
    expect(screen.getByText("Two high-similarity face matches from the same source.")).toBeInTheDocument();
    expect(screen.getByText("92%")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Accept" }));
    expect(onAccept).toHaveBeenCalledWith(12);

    fireEvent.click(screen.getByRole("button", { name: "Reject" }));
    expect(onReject).toHaveBeenCalledWith(12);

    fireEvent.click(screen.getByRole("button", { name: "Open evidence face 51" }));
    expect(onNavigate).toHaveBeenCalledWith({ page: "face", id: 51 });
  });
});