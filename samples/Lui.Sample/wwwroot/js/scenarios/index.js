import { hashtags } from './hashtags.js';
import { names } from './names.js';
import { people } from './people.js';
import { recipes } from './recipes.js';
import { tags } from './tags.js';
import { words } from './words.js';

/**
 * The autocompletes of the page, in the order of the tabs. A scenario is data and a presenter; the page knows no index by name.
 * The three at the end are the bulk indexes: they are empty until the panel fills them (ADR-0029).
 */
export const scenarios = [recipes, tags, names, words, people, hashtags];

/** @returns {Map<string, (envelope: any) => any[]>} what the fetch boundary hands the response of each index to */
export function presenters() {
    return new Map(scenarios.map(scenario => [scenario.index, scenario.presenter]));
}
