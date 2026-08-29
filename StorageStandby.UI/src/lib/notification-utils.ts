export const stringifyDetails = (details: unknown) => {
  if (details === undefined || details === null) return ""
  if (typeof details === "string") return details

  try {
    return JSON.stringify(details, null, 2)
  } catch {
    return String(details)
  }
}
