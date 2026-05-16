import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { EngagementBar } from "../components/EngagementBar";

describe("EngagementBar", () => {
  it("renders the favorite action as an icon-only button with a hover title", async () => {
    const user = userEvent.setup();
    const onFavoriteChange = vi.fn();

    render(<EngagementBar favorite={false} onFavoriteChange={onFavoriteChange} />);

    const favoriteButton = screen.getByRole("button", { name: "Add to favorites" });
    expect(favoriteButton).toHaveAttribute("title", "Add to favorites");
    expect(favoriteButton).not.toHaveTextContent("Favorite");

    await user.click(favoriteButton);

    expect(onFavoriteChange).toHaveBeenCalledWith(true);
  });
});