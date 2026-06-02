import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { EntityDetailTabs } from "../components/EntityDetailTabs";

describe("EntityDetailTabs", () => {
  it("left aligns the shared entity tab list within its container", () => {
    render(
      <EntityDetailTabs
        tabs={[
          { key: "images", label: "Images", count: 5 },
          { key: "videos", label: "Videos", count: 1 },
          { key: "fileinfo", label: "File Info" },
        ]}
        activeTab="images"
        onTabChange={vi.fn()}
      />,
    );

    expect(screen.getByRole("tablist", { name: /detail tabs/i })).not.toHaveClass("mx-auto");
  });
});

