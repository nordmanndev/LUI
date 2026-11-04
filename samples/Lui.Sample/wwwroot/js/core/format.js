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
