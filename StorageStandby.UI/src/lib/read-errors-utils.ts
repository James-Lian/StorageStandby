import { logError } from "./notifications"

type ProblemDetails = {
    type?: string
    title?: string
    status?: number
    detail?: string
    instance?: string
    [key: string]: unknown
}

type ParsedErrorDetails = {
    problem: ProblemDetails | null
    rawBody: unknown
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
    typeof value === "object" && value !== null

const toProblemDetails = (value: unknown): ProblemDetails | null => {
    if (!isRecord(value)) return null

    const status = typeof value.status === "number" ? value.status : undefined
    const title = typeof value.title === "string" ? value.title : undefined
    const detail = typeof value.detail === "string" ? value.detail : undefined
    const instance = typeof value.instance === "string" ? value.instance : undefined
    const type = typeof value.type === "string" ? value.type : undefined

    const hasProblemShape = status !== undefined || title !== undefined || detail !== undefined || instance !== undefined || type !== undefined
    if (!hasProblemShape) return null

    return {
        ...value,
        status,
        title,
        detail,
        instance,
        type,
    }
}

export async function readErrorDetails(response: Response): Promise<ParsedErrorDetails> {
    const contentType = response.headers.get("content-type")?.toLowerCase() ?? ""
    const isJsonLike = contentType.includes("application/problem+json") || contentType.includes("application/json")

    if (isJsonLike) {
        try {
            const payload = await response.json()
            return {
                problem: toProblemDetails(payload),
                rawBody: payload,
            }
        } catch {
            return {
                problem: null,
                rawBody: "Failed to parse JSON error response.",
            }
        }
    }

    try {
        const text = await response.text()
        return {
            problem: null,
            rawBody: text || "No error response body",
        }
    } catch {
        return {
            problem: null,
            rawBody: "Failed to read error response body.",
        }
    }
}

export function logApiProblemDetailsError(params: {
    fallbackTitle: string
    responseStatus?: number
    details: ParsedErrorDetails
}) {
    const { fallbackTitle, responseStatus, details } = params
    const problem = details.problem

    const title = problem?.title ?? fallbackTitle
    const statusCode = problem?.status ?? responseStatus ?? "No status code"
    const summary = problem?.detail

    const fullDetails = problem
        ? {
            type: problem.type,
            title: problem.title,
            status: problem.status,
            detail: problem.detail,
            instance: problem.instance,
            extensions: Object.fromEntries(
                Object.entries(problem).filter(([key]) => !["type", "title", "status", "detail", "instance"].includes(key))
            ),
        }
        : details.rawBody

    logError({
        title,
        statusCode,
        summary,
        details: fullDetails,
    })
}

export function logProviderProblemDetailsError(params: {
    fallbackTitle: string
    providerDisplayName: string
    responseStatus?: number
    details: ParsedErrorDetails
}) {
    const { fallbackTitle, providerDisplayName, responseStatus, details } = params
    const problem = details.problem

    const title = problem?.title ?? fallbackTitle
    const statusCode = problem?.status ?? responseStatus ?? "No status code"
    const summary = problem?.detail ?? `Provider: ${providerDisplayName}`

    const fullDetails = problem
        ? {
            type: problem.type,
            title: problem.title,
            status: problem.status,
            detail: problem.detail,
            instance: problem.instance,
            extensions: Object.fromEntries(
                Object.entries(problem).filter(([key]) => !["type", "title", "status", "detail", "instance"].includes(key))
            ),
        }
        : details.rawBody

    logError({
        title,
        statusCode,
        summary,
        details: fullDetails,
    })
}