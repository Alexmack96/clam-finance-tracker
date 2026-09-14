import { cn } from "../lib/utils.js";

export type ChartType = "line" | "bar";

const OPTIONS: { value: ChartType; label: string }[] = [
  { value: "line", label: "Line" },
  { value: "bar", label: "Bar" },
];

/// Segmented Line/Bar switch for a chart card's header.
export function ChartTypeToggle({
  value,
  onChange,
  label,
}: {
  value: ChartType;
  onChange: (value: ChartType) => void;
  label: string;
}) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className="inline-flex shrink-0 rounded-md bg-muted p-0.5"
    >
      {OPTIONS.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(option.value)}
            className={cn(
              "rounded px-3 py-1 text-xs transition-colors",
              active
                ? "bg-background font-semibold text-foreground shadow-sm ring-1 ring-border"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {option.label}
          </button>
        );
      })}
    </div>
  );
}
