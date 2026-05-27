import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { TutorialStoryboardDialog } from "../components/TutorialStoryboardDialog";
import type { ExtensionTutorialTopic } from "../api/types";

describe("TutorialStoryboardDialog", () => {
  it("renders extension manual subtopics under their parent topic", async () => {
    const user = userEvent.setup();
    const extensionTopics: ExtensionTutorialTopic[] = [
      {
        id: "cove.ai",
        title: "AI",
        description: "AI overview.",
        extensionId: "cove.ai.core",
        order: 80,
        slides: [
          {
            id: "overview",
            title: "AI overview",
            caption: "Start with the AI stack.",
            points: ["Connect the AI server"],
          },
        ],
      },
      {
        id: "cove.ai.tagging",
        title: "AI Tagging",
        description: "Generated tag workflows.",
        extensionId: "cove.ai.tagging",
        parentTopicId: "cove.ai",
        order: 81,
        slides: [
          {
            id: "settings",
            title: "Configure tag generation",
            bodyMarkdown: "Tune **generated tag names** and review the results in Cove.\n\n- Use the AI settings tab\n- Review before broad cleanup",
            imageSrc: "docs/tagging.png",
            imageAlt: "AI Tagging screenshot",
            links: [{ label: "AI Extensions README", url: "https://github.com/yourcove/AI.Extensions" }],
          },
        ],
      },
    ];

    render(
      <TutorialStoryboardDialog
        open
        onClose={vi.fn()}
        request={{ topicId: "cove.ai" }}
        extensionTopics={extensionTopics}
      />,
    );

    expect(screen.getByRole("button", { name: /AI overview/i })).toHaveAttribute("data-topic-depth", "0");
    const childButton = screen.getByRole("button", { name: /AI Tagging/i });
    expect(childButton).toHaveAttribute("data-topic-depth", "1");

    await user.click(childButton);

    expect(screen.getByRole("heading", { name: "AI Tagging" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Configure tag generation" })).toBeInTheDocument();
    expect(screen.getByText("generated tag names")).toBeInTheDocument();
    expect(screen.getByAltText("AI Tagging screenshot")).toHaveAttribute("src", "/api/extensions/assets/cove.ai.tagging/docs/tagging.png");
    expect(screen.getByRole("link", { name: /AI Extensions README/i })).toHaveAttribute("href", "https://github.com/yourcove/AI.Extensions");
  });

  it("opens the topic whose manual contexts match the current UI context", () => {
    const extensionTopics: ExtensionTutorialTopic[] = [
      {
        id: "cove.ai.visual",
        title: "AI Visual",
        description: "Visual similarity workflows.",
        contexts: ["settings-tab:extensions/ai/visual", "panel:visual-similarity"],
        pages: ["settings"],
        extensionId: "cove.ai.visual",
        order: 80,
        slides: [
          {
            id: "visual-search",
            title: "Find visually related media",
            bodyMarkdown: "Use the **Similar** tab after visual embeddings are ready.",
          },
        ],
      },
    ];

    render(
      <TutorialStoryboardDialog
        open
        onClose={vi.fn()}
        request={{ page: "settings", contexts: ["panel:visual-similarity", "page:settings"] }}
        currentPage="settings"
        extensionTopics={extensionTopics}
      />,
    );

    expect(screen.getByRole("heading", { name: "AI Visual" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Find visually related media" })).toBeInTheDocument();
    expect(screen.getByText("Similar")).toBeInTheDocument();
  });
});