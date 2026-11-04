import { names } from './names.js';
import { recipes } from './recipes.js';
import { tags } from './tags.js';

/** The autocompletes of the page, in the order of the tabs. A scenario is data and a presenter; the page knows no index by name. */
export const scenarios = [recipes, tags, names];

/** @returns {Map<string, (envelope: any) => any[]>} what the fetch boundary hands the response of each index to */
export function presenters() {
    return new Map(scenarios.map(scenario => [scenario.index, scenario.presenter]));
}
