import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { FeedActionPill, FeedCardFrame, FeedChipButton, FeedChipOverflowMenu, FeedIdentityBadge, FeedMetadataPill } from "../components/FeedCardFrame";

describe("FeedCardFrame", () => {
  it("renders a floating post layout with only media in the card-like frame", () => {
    const { container } = render(
      <FeedCardFrame
        identity={<FeedIdentityBadge>Studio One</FeedIdentityBadge>}
        header={<span>Today</span>}
        title={<button type="button">Post title</button>}
        details={<p>Post details</p>}
        media={<div data-testid="feed-media">Media</div>}
        headerActions={<FeedActionPill>12 votes</FeedActionPill>}
        metadata={<FeedMetadataPill>1920x1080</FeedMetadataPill>}
        chips={<FeedChipButton onClick={vi.fn()}>#tag</FeedChipButton>}
      />,
    );

    const card = container.querySelector('[data-feed-card="true"]');
    const content = container.querySelector('[data-feed-card-content="true"]');
    const media = container.querySelector('[data-feed-card-media="true"]');
    const actions = container.querySelector('[data-feed-card-actions="true"]');

    expect(card).toBeInTheDocument();
    expect(content).toBeInTheDocument();
    expect(media).toBeInTheDocument();
    expect(actions).toBeInTheDocument();
    expect(card).toHaveClass("border-b");
    expect(card).not.toHaveClass("rounded-2xl");
    expect(screen.getByText("Studio One")).toBeInTheDocument();
    expect(screen.queryByText("r/")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Post title" })).toBeInTheDocument();
    expect(screen.getByTestId("feed-media")).toBeInTheDocument();
    expect(screen.getByText("12 votes")).toBeInTheDocument();
    expect(screen.getByText("1920x1080")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "#tag" })).toBeInTheDocument();
    expect(screen.getByText("Studio One").compareDocumentPosition(screen.getByText("Today")) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("renders a compact overflow menu for truncated feed chips", () => {
    render(
      <FeedCardFrame
        header={<span>Today</span>}
        title={<button type="button">Post title</button>}
        media={<div>Media</div>}
        chips={(
          <>
            <FeedChipButton onClick={vi.fn()}>#visible</FeedChipButton>
            <FeedChipOverflowMenu>
              <FeedChipButton onClick={vi.fn()}>#hidden</FeedChipButton>
            </FeedChipOverflowMenu>
          </>
        )}
      />,
    );

    expect(screen.getByRole("button", { name: "#visible" })).toBeInTheDocument();
    expect(screen.getByTitle("Show more tags")).toHaveTextContent("...");
    expect(screen.getByRole("button", { name: "#hidden" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "#hidden" }).parentElement).toHaveClass("overflow-y-scroll");
  });
});
