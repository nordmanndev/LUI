import assert from 'node:assert/strict';
import { test } from 'node:test';
import { highlight, highlightRanges, queryWords, toHighlightHtml } from '../wwwroot/js/suggest/highlight.js';

const marked = (text, query) => highlight(text, query).replaceAll('<span class="highlight">', '[').replaceAll('</span>', ']');

test('the prefix of a word is marked', () => {
    assert.equal(marked('Kartoffelsuppe', 'kartoffel'), '[Kartoffel]suppe');
});

test('a word start is preferred to a match in the middle of a word', () => {
    assert.equal(marked('Bratapfel Apfelkuchen', 'apfel'), 'Bratapfel [Apfel]kuchen');
});

test('without a word start the match in the middle is marked (the trigram index finds it)', () => {
    assert.equal(marked('Gedeckter Apfelkuchen', 'kuchen'), 'Gedeckter Apfel[kuchen]');
});

test('transliteration: "kaesekuchen" marks the whole of "Käsekuchen"', () => {
    assert.equal(marked('Käsekuchen', 'kaesekuchen'), '[Käsekuchen]');
    assert.equal(marked('Müller Schmidt', 'mueller'), '[Müller] Schmidt');
    assert.equal(marked('Straße', 'strasse'), '[Straße]');
});

test('a decomposed umlaut in the text is marked as one letter', () => {
    assert.equal(marked('Müller', 'mueller'), '[Müller]');
});

test('every word of the input is marked, in any order', () => {
    assert.equal(marked('Gedeckter Apfelkuchen', 'kuchen apfel'), 'Gedeckter [Apfelkuchen]');
    assert.equal(marked('Spaghetti Bolognese', 'bolo spag'), '[Spag]hetti [Bolo]gnese');
});

test('the corrected query is what the caller passes: "kartoffel" for the typo "kartofel"', () => {
    assert.equal(marked('Kartoffelsuppe', 'kartoffel'), '[Kartoffel]suppe');
});

test('a word is marked once; an empty or separator-only input marks nothing', () => {
    assert.deepEqual(highlightRanges('Apfel Apfel', 'apfel'), [[0, 5]]);
    assert.deepEqual(highlightRanges('Apfel', ''), []);
    assert.deepEqual(highlightRanges('Apfel', ' - '), []);
    assert.deepEqual(queryWords('Apfel-Kuchen  Zimt'), ['kuchen', 'apfel', 'zimt']);
});

test('adjacent ranges merge', () => {
    assert.deepEqual(highlightRanges('abcd', 'ab cd'), [[0, 4]]);
});

test('the HTML is escaped, whatever the text', () => {
    const hostile = '<img src=x onerror="alert(1)"> & \' "';
    const html = highlight(hostile, 'img');
    assert.ok(!html.includes('<img'), html);
    assert.ok(html.includes('&lt;<span class="highlight">img</span> src=x'), html);
    assert.equal(toHighlightHtml('<b>', []), '&lt;b&gt;');
});
