import assert from 'node:assert/strict';
import { test } from 'node:test';
import { escapeHtml } from '../wwwroot/js/core/dom.js';
import { ms, number, priorityClass, ratio } from '../wwwroot/js/core/format.js';

test('escapeHtml escapes the five characters that matter, and only text', () => {
    assert.equal(escapeHtml(`<a href="x" onclick='y'>&</a>`), '&lt;a href=&quot;x&quot; onclick=&#39;y&#39;&gt;&amp;&lt;/a&gt;');
    assert.equal(escapeHtml(42), '42');
});

test('formatting of numbers for the panels', () => {
    assert.equal(ms(0.123), '0.12 ms');
    assert.equal(ms(12.34), '12.3 ms');
    assert.equal(ms(undefined), '–');
    assert.equal(number(1234567), '1,234,567');
    assert.equal(number(null), '–');
    assert.equal(ratio(0.12345), '0.123');
    assert.equal(priorityClass('System'), 'priority priority-system');
});
