import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { useState } from "react";
import { useMultiSelect } from "../hooks/useMultiSelect";

function MultiSelectProbe() {
  const [items, setItems] = useState([{ id: 1 }, { id: 2 }]);
  const { selectedIds, toggle } = useMultiSelect(items, { preserveOnAppend: true });

  return (
    <div>
      <div data-testid="selected">{[...selectedIds].join(",")}</div>
      <button type="button" onClick={() => toggle(1)}>Toggle first</button>
      <button type="button" onClick={() => setItems((current) => [...current, { id: 3 }])}>Append</button>
      <button type="button" onClick={() => setItems([{ id: 9 }])}>Replace</button>
    </div>
  );
}

describe("useMultiSelect", () => {
  it("preserves selection when more items append in infinite mode", async () => {
    const user = userEvent.setup();

    render(<MultiSelectProbe />);

    await user.click(screen.getByRole("button", { name: "Toggle first" }));
    expect(screen.getByTestId("selected")).toHaveTextContent("1");

    await user.click(screen.getByRole("button", { name: "Append" }));

    await waitFor(() => {
      expect(screen.getByTestId("selected")).toHaveTextContent("1");
    });

    await user.click(screen.getByRole("button", { name: "Replace" }));

    await waitFor(() => {
      expect(screen.getByTestId("selected")).toHaveTextContent("");
    });
  });
});