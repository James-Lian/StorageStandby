/** Join two path segments into a clean full path. */
export function joinPath(base: string, relative: string): string {
    return `${base.replace(/[\\/]+$/, "")}/${relative.replace(/^[\\/]+/, "")}`
}

/** Split a semicolon-delimited string into a list of non-empty, trimmed rules. */
export function splitRules(rules: string): string[] {
    return rules
        .split(";")
        .map((rule) => rule.trim())
        .filter(Boolean)
}

/** Heuristic path validity used by the UI table. */
export function isValidRulePath(rule: string): boolean {
    const trimmed = rule.trim()
    if (trimmed.length === 0) return false
    // A glob / pattern character means it isn't a literal filesystem path.
    if (/[*?[\]{}]/.test(trimmed)) return false
    if (trimmed.includes(";")) return false
    return true
}