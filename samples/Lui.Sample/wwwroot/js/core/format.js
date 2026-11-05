/** Number and label formatting shared by the panels. */

/** @param {number|null|undefined} value milliseconds */
export function ms(value) {
    if (value === null || value === undefined || Number.isNaN(value)) {
        return '–';
    }
    return value < 10 ? `${value.toFixed(2)} ms` : `${value.toFixed(1)} ms`;
}

/** @param {number|null|undefined} value */
export function number(value) {
    return value === null || value === undefined ? '–' : new Intl.NumberFormat('en').format(value);
}

/** @param {number} value 0 … 1 */
export function ratio(value) {
    return Number.isFinite(value) ? value.toFixed(3) : '–';
}

/** The clock time for the activity log. */
export function clock(date = new Date()) {
    return date.toLocaleTimeString('en-GB', { hour12: false }) + '.' + String(date.getMilliseconds()).padStart(3, '0');
}

/** @param {string} priority the name of a LuiPriority */
export function priorityClass(priority) {
    return `priority priority-${String(priority).toLowerCase()}`;
}

/**
 * The progress of a fill of a bulk index, as the index panel shows it (ADR-0029).
 *
 * @param {any} status the state of the fill, or null when there was none
 * @returns {string}
 */
export function fillProgress(status) {
    if (!status) {
        return 'Not filled yet.';
    }
    if (status.state === 'running') {
        return status.note ?? `Filling … ${number(status.read)} of ${number(status.requested)} entries, ${number(status.batches)} batches.`;
    }
    if (status.state === 'done') {
        return `Filled: ${number(status.read)} entries — ${number(status.added)} added, ${number(status.removed)} removed, ${duration(status.elapsedMs)}.`;
    }
    if (status.state === 'cancelled') {
        return `Cancelled after ${number(status.read)} entries.`;
    }
    if (status.state === 'failed') {
        return `Failed: ${status.error ?? 'unknown reason'}.`;
    }
    return String(status.state);
}

/**
 * A duration, in the unit that reads best: a query is measured in milliseconds, a fill in minutes.
 *
 * @param {number|null|undefined} value milliseconds
 * @returns {string}
 */
export function duration(value) {
    if (value === null || value === undefined || Number.isNaN(value)) {
        return '–';
    }
    if (value < 1000) {
        return `${Math.round(value)} ms`;
    }
    if (value < 60000) {
        return `${(value / 1000).toFixed(1)} s`;
    }
    const minutes = Math.floor(value / 60000);
    const seconds = Math.round((value % 60000) / 1000);
    return `${minutes} m ${String(seconds).padStart(2, '0')} s`;
}
