import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { createPlaybackTracker } from "../utils/interactionTracking";

describe("interactionTracking playback batching", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.runOnlyPendingTimers();
    vi.useRealTimers();
  });

  it("batches multiple intervals into one timed flush", async () => {
    const sendBatch = vi.fn(() => Promise.resolve());
    const tracker = createPlaybackTracker({ sendBatch, flushIntervalMs: 5000, maxBatchSize: 20 });

    await tracker.setTarget({ hostType: "scene", hostId: 42, scopeKey: "scene:42" });
    tracker.recordInterval({ startSec: 0, endSec: 10, mediaDurationSec: 120, currentPositionSec: 10, state: "active" });
    tracker.recordInterval({ startSec: 10, endSec: 18, mediaDurationSec: 120, currentPositionSec: 18, state: "paused" });

    expect(sendBatch).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(5000);

    expect(sendBatch).toHaveBeenCalledTimes(1);
    expect(sendBatch).toHaveBeenCalledWith(
      {
        hostType: "scene",
        hostId: 42,
        sessionId: tracker.getSessionId(),
        mediaDurationSec: 120,
        currentPositionSec: 18,
        state: "paused",
        intervals: [
          { startSec: 0, endSec: 10 },
          { startSec: 10, endSec: 18 },
        ],
      },
      "default",
    );
  });

  it("flushes on compilation item changes without creating a new compilation session", async () => {
    const sendBatch = vi.fn(() => Promise.resolve());
    const tracker = createPlaybackTracker({ sendBatch, flushIntervalMs: 5000, maxBatchSize: 20 });

    await tracker.setTarget({ hostType: "group", hostId: 7, scopeKey: "group:7", groupItemId: 101 });
    const compilationSessionId = tracker.getSessionId();

    tracker.recordInterval({ startSec: 3, endSec: 9, mediaDurationSec: 60, currentPositionSec: 9, state: "active" });
    await tracker.setTarget({ hostType: "group", hostId: 7, scopeKey: "group:7", groupItemId: 102 });

    expect(sendBatch).toHaveBeenCalledTimes(1);
    expect(sendBatch).toHaveBeenNthCalledWith(
      1,
      {
        hostType: "group",
        hostId: 7,
        sessionId: compilationSessionId,
        mediaDurationSec: 60,
        currentPositionSec: 9,
        state: "active",
        intervals: [{ startSec: 3, endSec: 9 }],
        groupItemId: 101,
      },
      "default",
    );
    expect(tracker.getSessionId()).toBe(compilationSessionId);
  });
});