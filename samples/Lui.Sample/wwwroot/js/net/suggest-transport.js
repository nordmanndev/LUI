/**
 * The fetch boundary of the page, part two: every suggestion request goes through here.
 *
 * flexdatalist calls the global `fetch` for its `url`. What it does not do (read in the source of 3.1.1, see
 * vendor/README.md), this module does:
 *
 *  - **Abort.** flexdatalist debounces, and keeps one request pending while one is in flight, but cancels none — a slow
 *    answer to an input the user has already left is still rendered. Here a new request for an index aborts the one in
 *    flight, and `cancel(index)` aborts it on a keystroke, before the debounce has even fired. The server stops its work
 *    too: the token of the request reaches `LuiIndex.Search`.
 *  - **The envelope.** flexdatalist reads `results` and drops the rest of the response. The whole LuiResult — corrected
 *    query, truncation, timings — is put on the bus for the panels.
 *  - **The items.** The response flexdatalist gets is made by the presenter of the index (`scenarios/*`): flat items with
 *    escaped, highlighted twins of every displayed property.
 *  - **Errors.** A refused request (a 400 for a setting the library rejects) must not reach flexdatalist as a body: it
 *    would try to render a problem document. It gets an empty list, and the bus gets the reason.
 */

/**
 * @typedef {(envelope: any) => any[]} Presenter
 * @typedef {{ cancel(index: string): void, fetch: typeof fetch, uninstall(): void }} SuggestTransport
 */

const json = body => new Response(JSON.stringify(body), { status: 200, headers: { 'content-type': 'application/json' } });

/**
 * @param {{
 *   fetchImpl: typeof fetch,
 *   bus: { emit(type: string, detail?: any): void },
 *   presenters: Map<string, Presenter>,
 *   prefix?: string,
 *   base?: string,
 *   now?: () => number,
 * }} options
 * @returns {{ cancel(index: string): void, fetch: typeof fetch }}
 */
export function createSuggestTransport({ fetchImpl, bus, presenters, prefix = '/api/suggest/', base = globalThis.location?.origin ?? 'http://localhost', now = () => performance.now() }) {
    /** @type {Map<string, AbortController>} */
    const inflight = new Map();

    function cancel(index) {
        inflight.get(index)?.abort();
    }

    async function suggestFetch(input, init) {
        const url = new URL(typeof input === 'string' ? input : input.url, base);
        if (!url.pathname.startsWith(prefix)) {
            return fetchImpl(input, init);
        }

        const index = decodeURIComponent(url.pathname.slice(prefix.length));
        const path = url.pathname + url.search;
        const request = Object.fromEntries(url.searchParams);

        cancel(index);
        const controller = new AbortController();
        inflight.set(index, controller);
        const started = now();
        bus.emit('suggest:request', { index, url: path });

        try {
            const response = await fetchImpl(path, { ...init, signal: controller.signal });
            if (!response.ok) {
                const problem = await response.json().catch(() => null);
                bus.emit('suggest:error', { index, status: response.status, detail: problem?.detail ?? response.statusText });
                return json({ results: [] });
            }

            const envelope = await response.json();
            bus.emit('suggest:response', { index, request, envelope, roundtripMs: now() - started });
            return json({ results: (presenters.get(index) ?? (e => e.results))(envelope) });
        } catch (error) {
            if (controller.signal.aborted) {
                bus.emit('suggest:aborted', { index, request });
                throw error; // flexdatalist swallows it (debug: false) and runs the request it kept pending
            }
            bus.emit('suggest:error', { index, status: 0, detail: error?.message ?? String(error) });
            return json({ results: [] });
        } finally {
            if (inflight.get(index) === controller) {
                inflight.delete(index);
            }
        }
    }

    return { cancel, fetch: suggestFetch };
}

/**
 * Puts the transport in place of the global `fetch`; everything that is not a suggestion goes on to the original.
 *
 * @param {Parameters<typeof createSuggestTransport>[0] extends infer O ? Omit<O & {}, 'fetchImpl'> : never} options
 * @param {typeof globalThis} [target]
 * @returns {SuggestTransport}
 */
export function installSuggestTransport(options, target = globalThis) {
    const original = target.fetch.bind(target);
    const transport = createSuggestTransport({ ...options, fetchImpl: original });
    target.fetch = transport.fetch;
    return { ...transport, uninstall: () => { target.fetch = original; } };
}
