/**
 * A tiny event bus: the modules of the page do not know each other, they know the events.
 *
 *   suggest:request   { index, url }                                       a query left the page
 *   suggest:response  { index, request, envelope, roundtripMs }            the whole LuiResult arrived
 *   suggest:aborted   { index, request }                                   the page cancelled it
 *   suggest:error     { index, status, detail }                            the server refused it
 *   settings:changed  { settings }                                         a knob moved
 *   index:changed     { index }                                            an index was written or maintained
 *
 * @returns {{ on(type: string, handler: (detail: any) => void): () => void, emit(type: string, detail?: any): void }}
 */
export function createBus() {
    /** @type {Map<string, Set<(detail: any) => void>>} */
    const handlers = new Map();

    return {
        on(type, handler) {
            if (!handlers.has(type)) {
                handlers.set(type, new Set());
            }
            handlers.get(type).add(handler);
            return () => handlers.get(type)?.delete(handler);
        },

        emit(type, detail) {
            for (const handler of [...(handlers.get(type) ?? [])]) {
                try {
                    handler(detail);
                } catch (error) {
                    // One panel that breaks must not take the others down with it: the hits of a query do not vanish
                    // because the index panel could not be read.
                    console.error(`The handler of '${type}' failed:`, error);
                }
            }
        },
    };
}
