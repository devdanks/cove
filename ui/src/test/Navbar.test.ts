import { describe, expect, it } from "vitest";
import { createManualOpenRequest, registerManualContext } from "../components/ManualContext";

describe("createManualOpenRequest", () => {
  it("describes extension settings routes without selecting a topic", () => {
    const request = createManualOpenRequest("settings", "settings", "/settings/extensions/ai");

    expect(request.page).toBe("settings");
    expect(request.topicId).toBeUndefined();
    expect(request.contexts).toEqual(expect.arrayContaining([
      "page:settings",
      "route:/settings/extensions/ai",
      "settings-tab:extensions/ai",
      "settings-tab:extensions",
    ]));
  });

  it("orders the most specific settings context before parent settings contexts", () => {
    expect(createManualOpenRequest("settings", "settings", "/settings/extensions/ai/tagging").contexts).toEqual(expect.arrayContaining([
      "settings-tab:extensions/ai/tagging",
      "settings-tab:extensions/ai",
    ]));

    const contexts = createManualOpenRequest("settings", "settings", "/settings/extensions/ai/tagging").contexts ?? [];
    expect(contexts.indexOf("settings-tab:extensions/ai/tagging")).toBeLessThan(contexts.indexOf("settings-tab:extensions/ai"));
  });

  it("puts active pane contexts before page contexts", () => {
    const unregister = registerManualContext("pane:ai.run");
    try {
      expect(createManualOpenRequest("scene", "scenes", "/scenes/1").contexts?.[0]).toBe("pane:ai.run");
    } finally {
      unregister();
    }
  });
});
