import { afterEach, describe, expect, it, vi } from "vitest";
import { groups } from "../api/client";

describe("api client", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("treats empty successful responses as void", async () => {
    const fetchMock = vi.fn(async () => new Response("", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(groups.addSubGroup(1, 2)).resolves.toBeUndefined();
    expect(fetchMock).toHaveBeenCalledWith("/api/groups/1/subgroups", expect.objectContaining({ method: "POST" }));
  });
});
