import { createBus } from './core/bus.js';
import { api } from './net/api.js';
import { installSuggestTransport } from './net/suggest-transport.js';
import { mountActivityPanel } from './panels/activity-panel.js';
import { mountEnvelopePanel } from './panels/envelope-panel.js';
import { mountHitsPanel } from './panels/hits-panel.js';
import { mountIndexesPanel } from './panels/indexes-panel.js';
import { mountScenario } from './panels/scenario-view.js';
import { mountSettingsPanel } from './panels/settings-panel.js';
import { presenters, scenarios } from './scenarios/index.js';
import { createSettings, defaultSettings } from './suggest/settings.js';
import { clear, el } from './core/dom.js';

/**
 * The composition root of the page: the one place that knows every module. It asks the server for the library's defaults,
 * puts the fetch boundary in place, builds the settings, and mounts the panels. A panel gets what it needs and no more;
 * panels talk to each other through the bus only.
 */
async function main() {
    const bus = createBus();
    const meta = await api.meta();

    const transport = installSuggestTransport({ bus, presenters: presenters() });
    const defaults = defaultSettings(meta.defaults);
    const settings = createSettings(defaults, safeStorage(), detail => bus.emit('settings:changed', detail));

    const region = id => document.getElementById(id);
    mountSettingsPanel({ root: region('settings'), meta, settings });
    mountEnvelopePanel({ root: region('envelope'), bus });
    mountHitsPanel({ root: region('hits'), bus });
    mountActivityPanel({ root: region('activity'), bus });
    mountIndexesPanel({ root: region('indexes'), bus, meta });

    const views = mountScenarios({ settings, transport, bus });
    // the input is rebuilt only when a setting of the input moved; a setting of the query — and the demo latency, which is a
    // parameter — is read at the next request
    const inputKey = ({ ui }) => JSON.stringify({ ...ui, delayMs: 0 });
    let built = inputKey(settings.get());
    bus.on('settings:changed', ({ settings: next }) => {
        if (inputKey(next) !== built) {
            built = inputKey(next);
            views.restartAll();
        }
    });

    await views.startActive();
}

/** The tabs of the autocompletes, one scenario visible at a time. */
function mountScenarios({ settings, transport, bus }) {
    const tabs = document.getElementById('tabs');
    const stage = document.getElementById('scenario');
    const mounted = new Map();
    let active = scenarios[0].id;

    function show(id) {
        mounted.get(active)?.dispose();
        active = id;
        for (const tab of tabs.children) {
            tab.setAttribute('aria-selected', String(tab.dataset.id === id));
        }
        clear(stage);
        const view = mountScenario({ root: stage, scenario: scenarios.find(scenario => scenario.id === id), settings, transport, api, bus });
        mounted.clear();
        mounted.set(id, view);
        return view.start();
    }

    for (const scenario of scenarios) {
        tabs.append(el('button', {
            type: 'button',
            role: 'tab',
            class: 'tab',
            dataset: { id: scenario.id },
            'aria-selected': String(scenario.id === active),
            text: scenario.title,
            on: { click: () => show(scenario.id) },
        }));
    }

    return {
        startActive: () => show(active),
        restartAll: () => mounted.get(active)?.start(),
    };
}

/** The browser's storage, or nothing: it can be blocked, and the page works without. */
function safeStorage() {
    try {
        return globalThis.localStorage ?? null;
    } catch {
        return null;
    }
}

main().catch(error => {
    document.getElementById('scenario').replaceChildren(el('p', { class: 'error', text: `The page could not start: ${error.message}` }));
});
