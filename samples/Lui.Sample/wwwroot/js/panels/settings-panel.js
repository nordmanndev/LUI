import { el } from '../core/dom.js';

/**
 * The control panel: one control per setting of a LUI query (`LuiQueryOptions`), named like its member, then the settings
 * of the flexdatalist input, then the demo latency. The queries go out with what is set here, from the next keystroke on.
 *
 * @param {{
 *   root: HTMLElement,
 *   meta: any,
 *   settings: ReturnType<typeof import('../suggest/settings.js').createSettings>,
 * }} options
 */
export function mountSettingsPanel({ root, meta, settings }) {
    const query = patch => settings.update({ query: patch });
    const ui = patch => settings.update({ ui: patch });
    const current = settings.get();

    const number = (value, min, max, onChange) => el('input', {
        type: 'number', min, max, value,
        on: { change: event => onChange(Math.min(max, Math.max(min, Number(event.target.value) || min))) },
    });
    // The value of a select can be longer than the field that shows it (the names of the specification are long): the
    // tooltip of the field says what it is set to, so that nothing is cut off without a way to know it.
    const select = (value, options, onChange) => {
        const label = options.find(([optionValue]) => optionValue === value)?.[1] ?? value;

        return el('select', {
            title: label,
            on: {
                change: event => {
                    event.target.title = event.target.options[event.target.selectedIndex].text;
                    onChange(event.target.value);
                },
            },
        },
            ...options.map(([optionValue, text]) => el('option', { value: optionValue, text, selected: optionValue === value })));
    };
    const check = (value, onChange) => el('input', { type: 'checkbox', checked: value, on: { change: event => onChange(event.target.checked) } });

    /**
     * One setting: its name, its control, and a "?" that shows what it means.
     *
     * The explanation is the tooltip of the setting as well — the panel shows it only when it is asked for, so that the
     * panel fits a screen and stays beside the autocomplete instead of over it (ADR-0027's layout, ADR-0029).
     */
    const row = (member, control, hint) => {
        const text = el('small', { text: hint, hidden: true });
        const toggle = el('button', {
            type: 'button',
            class: 'hint-toggle',
            text: '?',
            title: hint,
            'aria-label': `What ${member} means`,
            on: {
                click: event => {
                    // the button stands inside the label of the control: without this, a click on it would press the
                    // control as well
                    event.preventDefault();
                    text.hidden = !text.hidden;
                },
            },
        });

        return el('label', { class: 'setting', title: hint }, el('code', { text: member }), control, toggle, text);
    };

    const names = values => values.map(value => [value, value]);

    root.append(
        el('h2', { text: 'Query settings' }),
        el('p', { class: 'muted', text: 'LuiQueryOptions — sent with every request, one parameter each.' }),
        row('Take', number(current.query.take, 1, meta.limits.take, value => query({ take: value })), 'MaxResults. How many hits come back.'),
        row('Candidates', number(current.query.candidates, 1, 500, value => query({ candidates: value })), 'How many candidates are ranked; 500 is the budget (this page stops there).'),
        row('Rank', select(current.query.rank, names(meta.rank), value => query({ rank: value })), 'The order of the ranking.'),
        row('PriorityMode', select(current.query.priorityMode, names(meta.priorityMode), value => query({ priorityMode: value })), 'First: a higher priority wins before anything else. CandidatesOnly: it only makes an entry a certain candidate.'),
        row('Similarity', select(current.query.similarity, [['', '(index default)'], ...names([...meta.similarity])], value => query({ similarity: value })), 'The measure that orders the hits.'),
        row('TypoTolerance', select(current.query.typo, [['default', '(index default)'], ['on', 'on'], ['off', 'off']], value => query({ typo: value })), 'Correct an input that has no word start.'),
        row('TrackTimings', check(current.query.timings, value => query({ timings: value })), 'Where the time of the query goes (the envelope panel).'),

        el('h2', { text: 'Input settings' }),
        el('p', { class: 'muted', text: 'flexdatalist — not LUI. The input is rebuilt when one of these changes.' }),
        row('minLength', number(current.ui.minLength, 0, 5, value => ui({ minLength: value })), 'Characters before it searches.'),
        row('searchDelay', number(current.ui.searchDelay, 0, 1000, value => ui({ searchDelay: value })), 'Debounce in ms.'),
        row('maxShownResults', number(current.ui.maxShown, 1, 200, value => ui({ maxShown: value })), 'Rows drawn.'),
        row('groupBy', check(current.ui.grouped, value => ui({ grouped: value })), 'Group by the payload (category, kind).'),

        el('h2', { text: 'Demo' }),
        row('delayMs', number(current.ui.delayMs, 0, meta.limits.delayMs, value => ui({ delayMs: value })),
            'A slow server, to see an abort: type fast, and the requests in the activity log end as "aborted".'),
        el('button', { type: 'button', class: 'button', text: 'Reset all', on: { click: () => settings.reset() } }));
}
