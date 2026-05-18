import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { DetailListToolbar } from "../components/DetailListToolbar";

afterEach(() => {
  vi.restoreAllMocks();
});

describe("DetailListToolbar", () => {
  it("preserves the random seed when toggling sort direction", async () => {
    const user = userEvent.setup();
    const onFilterChange = vi.fn();

    render(
      <DetailListToolbar
        filter={{ page: 1, perPage: 24, sort: "random", direction: "asc", seed: 2468 }}
        onFilterChange={onFilterChange}
        totalCount={10}
        sortOptions={[{ value: "random", label: "Random" }]}
      />,
    );

    await user.click(screen.getByTitle("Ascending"));

    expect(onFilterChange).toHaveBeenCalledWith(expect.objectContaining({ sort: "random", direction: "desc", seed: 2468 }));
  });

  it("shows a shuffle button for random sort and replaces the seed", async () => {
    const user = userEvent.setup();
    const onFilterChange = vi.fn();
    vi.spyOn(Math, "random").mockReturnValue(0.5);

    render(
      <DetailListToolbar
        filter={{ page: 3, perPage: 24, sort: "random", direction: "asc", seed: 2468 }}
        onFilterChange={onFilterChange}
        totalCount={10}
        sortOptions={[{ value: "random", label: "Random" }]}
      />,
    );

    await user.click(screen.getByTitle("Shuffle"));

    expect(onFilterChange).toHaveBeenCalledWith(expect.objectContaining({ sort: "random", page: 1, seed: 1073741823 }));
  });
});