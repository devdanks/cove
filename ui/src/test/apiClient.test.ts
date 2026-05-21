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

  it("preserves zero page size for group item pages", async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify({ items: [], totalCount: 0, page: 1, perPage: 0 }), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);

    await groups.items.page(4, { page: 1, perPage: 0 });

    expect(fetchMock).toHaveBeenCalledWith("/api/groups/4/items/page?page=1&perPage=0", expect.objectContaining({ headers: expect.any(Headers) }));
  });
});
