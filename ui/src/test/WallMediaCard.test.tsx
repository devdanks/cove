import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { WallMediaCard } from "../components/WallMediaCard";

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("WallMediaCard", () => {
  it("uses the image fallback without mounting a missing preview video", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: false }));

    const { container } = render(
      <WallMediaCard title="Missing preview" imageSrc="/image.jpg" videoSrc="/missing.mp4" useVideo />,
    );

    expect(screen.getByAltText("Missing preview")).toBeInTheDocument();
    await waitFor(() => expect(fetch).toHaveBeenCalledWith("/missing.mp4", expect.objectContaining({ method: "HEAD" })));
    expect(container.querySelector("video")).not.toBeInTheDocument();
  });

  it("does not treat a non-video preflight response as an available preview", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, headers: { get: () => "text/html" } }));

    const { container } = render(
      <WallMediaCard title="Fallback preview" imageSrc="/image.jpg" videoSrc="/fallback.html" useVideo />,
    );

    expect(screen.getByAltText("Fallback preview")).toBeInTheDocument();
    await waitFor(() => expect(fetch).toHaveBeenCalledWith("/fallback.html", expect.objectContaining({ method: "HEAD" })));
    expect(container.querySelector("video")).not.toBeInTheDocument();
  });

  it("falls back to the secondary image when the primary image fails", async () => {
    const { rerender } = render(
      <WallMediaCard title="Fallback image" imageSrc="/cover.jpg" imageFallbackSrc="/screenshot.jpg" />,
    );

    const image = screen.getByAltText("Fallback image");
    expect(image).toHaveAttribute("src", "/cover.jpg");

    fireEvent.error(image);

    await waitFor(() => expect(screen.getByAltText("Fallback image")).toHaveAttribute("src", "/screenshot.jpg"));

    rerender(<WallMediaCard title="Fallback image" imageSrc="/next-cover.jpg" imageFallbackSrc="/next-screenshot.jpg" />);

    await waitFor(() => expect(screen.getByAltText("Fallback image")).toHaveAttribute("src", "/next-cover.jpg"));
  });

  it("mounts the preview video after the preview exists", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, headers: { get: () => "video/mp4" } }));
    vi.stubGlobal(
      "IntersectionObserver",
      class {
        observe() {}
        disconnect() {}
      },
    );

    const { container } = render(
      <WallMediaCard title="Available preview" imageSrc="/image.jpg" videoSrc="/preview.mp4" useVideo />,
    );

    await waitFor(() => expect(container.querySelector("video")).toBeInTheDocument());
    expect(container.querySelector("video")).toHaveAttribute("src", "/preview.mp4");
  });

  it("can use a successful preview status endpoint before mounting video", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve({ available: false }) }));

    const { container } = render(
      <WallMediaCard title="Status preview" imageSrc="/image.jpg" videoSrc="/preview.mp4" videoStatusSrc="/preview.mp4/status" useVideo />,
    );

    await waitFor(() => expect(fetch).toHaveBeenCalledWith("/preview.mp4/status", expect.objectContaining({ method: "GET" })));
    expect(container.querySelector("video")).not.toBeInTheDocument();
  });

  it("observes and plays the video after async status mounts it", async () => {
    const play = vi.spyOn(HTMLMediaElement.prototype, "play").mockResolvedValue(undefined);

    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve({ available: true }) }));
    vi.stubGlobal(
      "IntersectionObserver",
      class {
        private readonly callback: IntersectionObserverCallback;

        constructor(callback: IntersectionObserverCallback) {
          this.callback = callback;
        }

        observe(target: Element) {
          this.callback([{ isIntersecting: true, target } as IntersectionObserverEntry], this as unknown as IntersectionObserver);
        }

        disconnect() {}
      },
    );

    const { container } = render(
      <WallMediaCard title="Async preview" imageSrc="/image.jpg" videoSrc="/preview.mp4" videoStatusSrc="/preview.mp4/status" useVideo />,
    );

    await waitFor(() => expect(container.querySelector("video")).toBeInTheDocument());
    await waitFor(() => expect(play).toHaveBeenCalled());
  });
});