import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { BottomNav } from "./BottomNav.js";

// Radix positions the popover with floating-ui, which watches element size.
// jsdom has no ResizeObserver.
vi.stubGlobal(
  "ResizeObserver",
  class {
    observe = vi.fn();
    unobserve = vi.fn();
    disconnect = vi.fn();
  },
);

describe("BottomNav", () => {
  it("lists Recurring under Other", () => {
    render(
      <MemoryRouter initialEntries={["/dashboard"]}>
        <BottomNav />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole("button", { name: "Other" }));

    expect(screen.getByRole("link", { name: "Recurring" })).toHaveAttribute("href", "/recurring");
  });
});
