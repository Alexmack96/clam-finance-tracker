export type BankSource =
  "Monzo" | "Flex" | "Amex" | "Barclays" | "Santander" | "HSBC" | "SoFi" | "Chase" | "Manual";

export function bankSource(externalId: string | null): BankSource {
  if (!externalId) return "Manual";
  if (externalId.startsWith("monzo:")) return "Monzo";
  if (externalId.startsWith("flex:")) return "Flex";
  if (externalId.startsWith("amex:")) return "Amex";
  if (externalId.startsWith("barclays:")) return "Barclays";
  if (externalId.startsWith("santander:")) return "Santander";
  if (externalId.startsWith("hsbc:")) return "HSBC";
  if (externalId.startsWith("sofi:")) return "SoFi";
  if (externalId.startsWith("chase:")) return "Chase";
  return "Manual";
}

export const SOURCE_STYLES: Record<BankSource, string> = {
  Monzo: "text-orange-500 border-orange-500",
  Flex: "text-cyan-500 border-cyan-500",
  Amex: "text-blue-500 border-blue-500",
  Barclays: "text-sky-500 border-sky-500",
  Santander: "text-red-500 border-red-500",
  HSBC: "text-purple-500 border-purple-500",
  SoFi: "text-green-500 border-green-500",
  Chase: "text-blue-700 border-blue-700",
  Manual: "text-muted-foreground border-muted-foreground/40",
};
