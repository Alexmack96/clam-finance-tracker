// Identify the monthly salary by signature rather than an exact description: the Santander
// statement text carries reference/spacing noise that breaks byte-for-byte matching. A
// "Hudson Bay" reference plus an amount over £5k is a safe bet for the Throgmorton salary.
export function findLatestSalary(
  txs: { description: string; amount: string; date: string }[],
): number | null {
  const salary = txs
    .filter((t) => /hudson bay/i.test(t.description) && parseFloat(t.amount) > 5000)
    .sort((a, b) => new Date(b.date).getTime() - new Date(a.date).getTime())[0];
  return salary ? parseFloat(salary.amount) : null;
}
