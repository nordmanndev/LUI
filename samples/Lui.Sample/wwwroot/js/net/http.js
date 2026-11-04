/**
 * The fetch boundary of the page, part one: every request that is not a suggestion goes through here.
 * (Part two is `suggest-transport.js`. `fetch` appears in these two files and nowhere else — a guard.)
 */

/** A response the server refused: RFC 9457 problem details. */
export class ApiError extends Error {
    /**
     * @param {number} status
     * @param {string} detail
     */
    constructor(status, detail) {
        super(detail);
        this.name = 'ApiError';
        this.status = status;
    }
}

/**
 * @param {string} method
 * @param {string} url
 * @param {unknown} [body]
 * @param {{ signal?: AbortSignal }} [options]
 * @returns {Promise<any>} the parsed JSON, or null for a 204
 */
export async function request(method, url, body, options = {}) {
    const init = { method, signal: options.signal, headers: { accept: 'application/json' } };
    if (body !== undefined) {
        init.headers['content-type'] = 'application/json';
        init.body = JSON.stringify(body);
    }

    const response = await fetch(url, init);
    if (response.status === 204) {
        return null;
    }

    const payload = await response.json().catch(() => null);
    if (!response.ok) {
        throw new ApiError(response.status, payload?.detail ?? payload?.title ?? `${response.status} ${response.statusText}`);
    }
    return payload;
}

export const getJson = (url, options) => request('GET', url, undefined, options);
export const postJson = (url, body) => request('POST', url, body ?? {});
export const patchJson = (url, body) => request('PATCH', url, body);
export const deleteJson = url => request('DELETE', url);
