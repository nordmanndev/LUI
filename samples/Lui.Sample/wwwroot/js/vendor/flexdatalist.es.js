var w = Object.defineProperty;
var x = (u) => {
  throw TypeError(u);
};
var R = (u, t, e) => t in u ? w(u, t, { enumerable: !0, configurable: !0, writable: !0, value: e }) : u[t] = e;
var S = (u, t, e) => R(u, typeof t != "symbol" ? t + "" : t, e), v = (u, t, e) => t.has(u) || x("Cannot " + e);
var f = (u, t, e) => (v(u, t, "read from private field"), e ? e.call(u) : t.get(u)), g = (u, t, e) => t.has(u) ? x("Cannot add the same private member more than once") : t instanceof WeakSet ? t.add(u) : t.set(u, e), b = (u, t, e, s) => (v(u, t, "write to private field"), s ? s.call(u, e) : t.set(u, e), e), E = (u, t, e) => (v(u, t, "access private method"), e);
/**
 * Flexdatalist — standalone ES6 class.
 *
 * Autocomplete / tag input with remote & static data, grouping, keyboard
 * navigation, localStorage caching, and a clean chainable API.
 *
 * Compatible CSS: use flexdatalist.css (class names are identical to v2).
 *
 * @author Sergio Dinis Lopes <flexdatalist@sergiodinislopes.pt>
 * @copyright 2016-2026 Sergio Dinis Lopes
 * @version 3.1.1
 * @license MIT
 *
 * Basic usage
 * -----------
 * ```js
 * // Initialize one or many inputs
 * const [fd] = await Flexdatalist.init('#city', { url: '/api/cities', minLength: 2 });
 *
 * // Chainable API
 * fd.setValue('Paris').disable().on('select:flexdatalist', e => console.log(e.detail));
 *
 * // Get the stored value at any time
 * console.log(fd.getValue());
 * ```
 *
 * Events are dispatched on the original <input> element as CustomEvents.
 * `e.detail` contains the payload described next to each event name in the
 * _dispatch() helper below.
 */
var p, _, y, A;
const h = class h {
  // =========================================================================
  // Static API
  // =========================================================================
  /**
   * Initialise Flexdatalist on one or more elements.
   * Returns a Promise that resolves after every instance has finished loading its initial value. Each instance also exposes `instance.ready` as a standalone Promise.
   *
   * @param {string|HTMLElement|NodeList} selector CSS selector, single element, or NodeList.
   * @param {Object} [options={}] Option overrides (see `Flexdatalist.defaults`).
   * @returns {Promise<Flexdatalist[]>} Resolves when all instances are ready.
   *
   * @example
   * const [fd] = await Flexdatalist.init('#city', { url: '/api/cities' });
   */
  static async init(t, e = {}) {
    let s;
    typeof t == "string" ? s = [...document.querySelectorAll(t)] : t instanceof NodeList || Array.isArray(t) ? s = [...t] : s = [t];
    const i = s.map((l) => new h(l, e));
    return await Promise.all(i.map((l) => l.ready)), i;
  }
  /**
   * Return the Flexdatalist instance attached to a given element, or null.
   *
   * @param {HTMLElement|string} el
   * @returns {Flexdatalist|null}
   */
  static getInstance(t) {
    return typeof t == "string" && (t = document.querySelector(t)), f(this, p).get(t) ?? null;
  }
  // =========================================================================
  // Constructor
  // =========================================================================
  /**
   * Create a Flexdatalist instance on a single `<input>` element.
   * If the element already has an instance it is destroyed first.
   *
   * @param {HTMLInputElement|string} el  The target `<input>` element.
   * @param {Object} [options={}] Option overrides.
   * @throws {TypeError} If `el` is not an HTMLElement.
   */
  constructor(t, e = {}) {
    var i, l;
    if (typeof t == "string" && (t = document.querySelector(t)), !(t instanceof HTMLElement))
      throw new TypeError("Flexdatalist: first argument must be an HTMLElement.");
    if (t.classList.contains("flexdatalist-set")) {
      console.warn("Flexdatalist: element already has an instance. This might cause issues.", t);
      return;
    }
    (i = h.getInstance(t)) == null || i.destroy(), this._hiddenInput = t, this._alias = null, this._multipleEl = null, this._value = t.value || "", this._texts = [], this._itemSelected = !1, this._fetchDisabled = !1, this._pendingRequest = null, this._searchTimeout = null, this._leaveInterval = null, this._options = this._buildOptions(e), f(h, p).set(t, this), E(l = h, y, A).call(l), this._setUp(), this._bindAlias(), window.addEventListener("resize", () => this._position()), this._cacheGC();
    const s = this._value;
    this._options.selectionRequired && this._clearValue(!0, !0), this.ready = new Promise((a) => {
      s.length > 0 ? this._hiddenInput.addEventListener("init:flexdatalist", () => a(this), { once: !0 }) : a(this);
    }), s.length > 0 ? (this._value = s, this._hiddenInput.value = s, this._loadingStart(), this._loadValue(s, () => {
      this._applyDisabled(this._options.disabled), this._loadingStop(), this._dispatch("init:flexdatalist", this._options);
    }, !0)) : this._applyDisabled(this._options.disabled);
  }
  // =========================================================================
  // Options
  // =========================================================================
  /**
   * Merge defaults, JS options, and element data-attributes into a resolved
   * options object.  Data-attributes take the form `data-my-option` (kebab-case).
   *
   * @param {Object} jsOpts Options passed to the constructor.
   * @returns {Object} Resolved and normalised options.
   */
  _buildOptions(t) {
    var a;
    const e = this._hiddenInput, s = t.valuesSeparator ?? h.defaults.valuesSeparator, i = {};
    for (const n of Object.keys(h.defaults)) {
      const o = "data-" + n.replace(/([A-Z])/g, (r) => "-" + r.toLowerCase());
      if (e.hasAttribute(o)) {
        let r = e.getAttribute(o);
        try {
          r = JSON.parse(r);
        } catch {
        }
        i[n] = r;
      }
    }
    const l = { ...h.defaults, ...t, ...i };
    return l.multiple = l.multiple === null ? e.hasAttribute("multiple") : l.multiple, l.disabled = l.disabled === null ? e.hasAttribute("disabled") : l.disabled, l.originalValue = this._value, typeof l.searchIn == "string" && (l.searchIn = l.searchIn.split(s).map((n) => n.trim())), l.textProperty || (l.textProperty = l.searchIn[0]), typeof l.visibleProperties == "string" && (l.visibleProperties = l.visibleProperties.length > 0 ? l.visibleProperties.split(s).map((n) => n.trim()) : [...l.searchIn]), (a = l.visibleProperties) != null && a.length || (l.visibleProperties = [...l.searchIn]), typeof l.relatives == "string" ? l.relatives = [...document.querySelectorAll(l.relatives)] : l.relatives instanceof NodeList && (l.relatives = [...l.relatives]), l;
  }
  // =========================================================================
  // Public API — options
  // =========================================================================
  /**
   * Get a single option value.
   *
   * @param {string} name Option key (see `Flexdatalist.defaults`).
   * @returns {*} Current option value.
   */
  getOption(t) {
    return this._options[t];
  }
  /**
   * Set a single option value at runtime.
   *
   * @param {string} name  Option key.
   * @param {*}      value New value.
   * @returns {this} Chainable.
   */
  setOption(t, e) {
    return this._options[t] = e, this;
  }
  // =========================================================================
  // Public API — value
  // =========================================================================
  /**
   * Return the current value.
   * - Single mode → raw string or primitive.
   * - Multiple mode → array of values.
   * - JSON valueProperty → object or array of objects.
   *
   * @returns {string|Array|Object}
   */
  getValue() {
    return this._options.multiple || this._isJSON() ? this._toObj(this._value) : this._value;
  }
  /**
   * Replace the current value.
   * Triggers data loading if `valueProperty` is set, so a server round-trip
   * may occur before the alias is updated.
   *
   * @param {string|Array|Object} val  New value.
   * @returns {this} Chainable.
   */
  setValue(t) {
    return this._isDisabled() || (this._clearValue(!0), this._loadValue(t)), this;
  }
  /**
   * Append one or more values (multiple mode only).
   *
   * @param {string|Array} val Value(s) to add.
   * @returns {this} Chainable.
   */
  addValue(t) {
    if (this._options.multiple && !this._isDisabled()) {
      const e = Array.isArray(t) ? t : [t];
      for (const s of e) this._loadValue(s);
    }
    return this;
  }
  /**
   * Remove a specific value (multiple mode only).
   *
   * @param {string} val The serialised value to remove.
   * @returns {this} Chainable.
   */
  removeValue(t) {
    if (!this._isDisabled()) {
      this._dispatch("before:flexdatalist.remove", t);
      const e = Array.isArray(t) ? t : [t];
      for (const s of e) this._multipleRemove(String(s));
    }
    return this;
  }
  /**
   * Toggle the disabled/enabled visual state of a tag (multiple + toggleSelected only).
   *
   * @param {string} val Tag value to toggle.
   * @returns {this} Chainable.
   */
  toggleValue(t) {
    return this._isDisabled() || this._multipleToggle(t), this;
  }
  /**
   * Clear all selected values and reset the alias input.
   *
   * @returns {this} Chainable.
   */
  clear() {
    return this._clearValue(!0), this;
  }
  /**
   * Return the user-facing display text(s) shown in the alias input.
   *
   * In single mode, always returns a plain `string` (mirroring `getValue()`).
   *
   * In multiple mode, `format` controls the return type:
   * @param {'array'|'string'|string} [format='array']
   *   - `'array'`  — returns a `string[]` (default).
   *   - `'string'` — joins with the configured `valuesSeparator` (e.g. `','`).
   *   - any other string — used directly as the join separator.
   * @returns {string|string[]}
   *
   * @example
   * // Single mode
   * fd.getText();              // 'Paris'
   *
   * // Multiple mode
   * fd.getText();              // ['Paris', 'London']
   * fd.getText('string');      // 'Paris,London'
   * fd.getText(' | ');         // 'Paris | London'
   */
  getText(t = "array") {
    var i;
    if (!this._options.multiple)
      return ((i = this._alias) == null ? void 0 : i.value) ?? "";
    const e = [...this._texts];
    if (t === "array") return e;
    const s = t === "string" ? this._options.valuesSeparator : t;
    return e.join(s);
  }
  // =========================================================================
  // Public API — state
  // =========================================================================
  /**
   * Disable the widget (prevents interaction and value changes).
   *
   * @returns {this} Chainable.
   */
  disable() {
    return this._applyDisabled(!0), this;
  }
  /**
   * Enable the widget after it was disabled.
   *
   * @returns {this} Chainable.
   */
  enable() {
    return this._applyDisabled(!1), this;
  }
  /**
   * Set or query the readonly state.
   * When `state` is omitted, returns the current readonly flag.
   *
   * @param {boolean} [state] Pass `true` to make readonly, `false` to remove.
   * @returns {this|boolean} `this` when setting, current state when getting.
   */
  readonly(t) {
    return t === void 0 ? !!this._options.readonly : (this._applyReadonly(t), this);
  }
  /**
   * Whether the widget is currently disabled.
   *
   * @returns {boolean}
   */
  isDisabled() {
    return this._isDisabled();
  }
  /**
   * Whether the widget is currently readonly.
   *
   * @returns {boolean}
   */
  isReadonly() {
    return !!this._options.readonly;
  }
  // =========================================================================
  // Public API — search
  // =========================================================================
  /**
   * Programmatically trigger a search with the given keyword.
   * Results are displayed as if the user had typed the keyword.
   *
   * @param {string} keyword Search string.
   * @returns {this} Chainable.
   */
  search(t) {
    return this._alias.value = t, this._dataLoad((e) => {
      this._searchGet(t, e, (s) => this._resultsShow(s));
    }), this;
  }
  /**
   * Close the results dropdown.
   *
   * @returns {this} Chainable.
   */
  closeResults() {
    return this._resultsRemove(), this;
  }
  // =========================================================================
  // Public API — events
  // =========================================================================
  /**
   * Subscribe to a Flexdatalist event dispatched on the original input.
   *
   * Common events:
   * - `init:flexdatalist`           — widget initialised
   * - `select:flexdatalist`         — a result was selected
   * - `change:flexdatalist`         — value changed
   * - `clear:flexdatalist`          — values were cleared
   * - `addnew:flexdatalist`         — "add new item" was clicked
   * - `before:flexdatalist.value`   — before value extraction
   * - `after:flexdatalist.value`    — after value extraction
   * - `before:flexdatalist.select`  — before selection applied
   * - `after:flexdatalist.select`   — after selection applied
   * - `before:flexdatalist.remove`  — before a tag is removed
   * - `after:flexdatalist.remove`   — after a tag is removed
   * - `before:flexdatalist.search`  — before local filtering
   * - `after:flexdatalist.search`   — after local filtering
   * - `show:flexdatalist.results`   — results about to be rendered
   * - `shown:flexdatalist.results`  — results were rendered
   * - `item:flexdatalist.results`   — a result `<li>` was created
   * - `empty:flexdatalist.results`  — no results found
   * - `remove:flexdatalist.results` — dropdown is being removed
   * - `before:flexdatalist.data`    — before data loading
   * - `after:flexdatalist.data`     — data loaded
   *
   * @param {string}   event    Event name (e.g. `'select:flexdatalist'`).
   * @param {Function} handler  Callback receiving the native CustomEvent.
   * @returns {this} Chainable.
   */
  on(t, e) {
    return this._hiddenInput.addEventListener(t, e), this;
  }
  /**
   * Unsubscribe an event handler previously registered with `on()`.
   *
   * @param {string}   event   Event name.
   * @param {Function} handler The same function reference passed to `on()`.
   * @returns {this} Chainable.
   */
  off(t, e) {
    return this._hiddenInput.removeEventListener(t, e), this;
  }
  // =========================================================================
  // Public API — lifecycle
  // =========================================================================
  /**
   * Destroy this instance, restore the original `<input>`, and remove all
   * injected DOM nodes.
   *
   * @param {boolean} [clear=false] Also clear the original input's value.
   */
  destroy(t = !1) {
    var i;
    const e = this._hiddenInput, s = this._multipleEl ?? this._alias;
    e.classList.remove("flexdatalist", "flexdatalist-set"), e.style.position = "", e.style.top = "", e.style.left = "", e.removeAttribute("tabindex"), e.value = !t && ((i = this._options) != null && i.originalValue) ? this._options.originalValue : "", s == null || s.remove(), f(h, p).delete(e);
  }
  // =========================================================================
  // Internal – DOM setup
  // =========================================================================
  /**
   * Build and insert all DOM nodes for the widget.
   *
   * @private
   */
  _setUp() {
    var s;
    const t = this._hiddenInput, e = this._options;
    this._alias = this._makeAlias(), e.multiple ? this._multipleEl = this._makeMultiple(this._alias) : t.insertAdjacentElement("afterend", this._alias), this._setA11y(this._alias), t.hasAttribute("autofocus") && this._alias.focus(), t.style.cssText += ";position:absolute;top:-14000px;left:-14000px", t.setAttribute("tabindex", "-1"), t.classList.add("flexdatalist", "flexdatalist-set"), t.id && ((s = document.querySelector(`label[for="${t.id}"]`)) == null || s.setAttribute("for", this._alias.id)), this._setupChained(!0);
  }
  /**
   * Create the visible alias `<input>`.
   * The alias intentionally has no `name` attribute, so it is never
   * included in form submissions (only the original hidden input is).
   *
   * @private
   * @returns {HTMLInputElement}
   */
  _makeAlias() {
    const t = this._hiddenInput, e = t.id ? t.id + "-flexdatalist" : "fdl-" + Math.random().toString(36).slice(2, 9), s = document.createElement("input");
    return s.type = "text", s.className = [...t.classList].filter((i) => i !== "flexdatalist").join(" "), s.classList.add("flexdatalist-alias", e), s.id = e, s.placeholder = t.placeholder || "", s.autocomplete = "off", s;
  }
  /**
   * Create the `<ul>` wrapper used in multiple-value mode.
   * The alias `<input>` is appended as the last `<li>`.
   *
   * @private
   * @param {HTMLInputElement} alias
   * @returns {HTMLUListElement}
   */
  _makeMultiple(t) {
    const e = this._hiddenInput, s = getComputedStyle(e), i = document.createElement("ul");
    i.setAttribute("tabindex", "1"), i.className = "flexdatalist-multiple", i.style.borderColor = s.borderLeftColor, i.style.borderWidth = s.borderLeftWidth, i.style.borderStyle = s.borderLeftStyle, i.style.borderRadius = s.borderTopLeftRadius, i.style.backgroundColor = s.backgroundColor, i.addEventListener("click", () => t.focus());
    const l = document.createElement("li");
    return l.className = "input-container flexdatalist-multiple-value", l.appendChild(t), i.appendChild(l), e.insertAdjacentElement("afterend", i), i;
  }
  /**
   * Apply ARIA attributes to the alias input for screen-reader support.
   *
   * @private
   * @param {HTMLInputElement} alias
   */
  _setA11y(t) {
    t.setAttribute("aria-autocomplete", "list"), t.setAttribute("aria-expanded", "false"), t.setAttribute("aria-owns", t.id + "-results");
  }
  /**
   * Wire up chained-relatives behaviour: disable this input when all
   * relative inputs are empty.
   *
   * @private
   * @param {boolean} init Whether this is the first call during setup.
   */
  _setupChained(t) {
    var i;
    const e = this._options;
    if (!((i = e.relatives) != null && i.length) || !e.chainedRelatives) return;
    const s = () => {
      const l = e.relatives.every((a) => this._isEmpty(a.value));
      this._applyReadonly(l);
    };
    for (const l of e.relatives) l.addEventListener("change", s);
    s();
  }
  // =========================================================================
  // Internal – alias event binding
  // =========================================================================
  /**
   * Attach all event listeners to the alias `<input>`.
   *
   * @private
   */
  _bindAlias() {
    const t = this._alias;
    t.addEventListener("focusin", () => {
      var e;
      (e = this._multipleEl) == null || e.classList.add("focus"), this._actAddValueOnLeave();
    }), t.addEventListener("focusout", () => {
      var e;
      (e = this._multipleEl) == null || e.classList.remove("focus"), this._actClearText(), this._actClearValue();
    }), t.addEventListener("click", (e) => this._actShowAll(e)), t.addEventListener("keydown", (e) => this._onKeydown(e)), t.addEventListener("input", (e) => this._onKeydown(e)), t.addEventListener("keyup", (e) => this._onKeyup(e)), t.addEventListener("input", (e) => this._onKeyup(e)), t.addEventListener("paste", (e) => this._onPaste(e));
  }
  // =========================================================================
  // Internal – key handlers
  // =========================================================================
  /**
   * Handle `keydown` and `input` events on the alias.
   *
   * @private
   * @param {KeyboardEvent|InputEvent} e
   */
  _onKeydown(t) {
    const e = t.key, s = this._options;
    e === "Tab" && this._resultsRemove(), this._actKeypressValue(t, ","), this._actBackspace(t), s.multiple && this._alias.value.length > 0 && e === "Enter" && (document.querySelectorAll(".flexdatalist-results li:not(.no-results)").length || t.preventDefault());
  }
  /**
   * Handle `keyup` and secondary `input` events on the alias.
   *
   * @private
   * @param {KeyboardEvent|InputEvent} e
   */
  _onKeyup(t) {
    const e = t.key, s = this._options;
    let i = !1;
    if (s.multiple && this._alias.value.length > 0 && e === "Enter" && (document.querySelector(".flexdatalist-results li.item.active") || (s.selectionRequired || this._actKeypressValue(t, "Enter"), i = !0)), this._actSearch(t), this._actCopyValue(t), this._actBackspace(t), this._actShowAll(t), this._actClearValue(), this._actRemoveResults(), this._alias.value.length || this._actClearValue(), i)
      return t.preventDefault(), !1;
  }
  /**
   * Handle the `paste` event on the alias.
   * Splits pasted text on commas and newlines, then adds each as a value.
   *
   * @private
   * @param {ClipboardEvent} e
   */
  _onPaste(t) {
    var s;
    let e = ((s = t.clipboardData) == null ? void 0 : s.getData("text/plain")) ?? "";
    if (e) {
      t.preventDefault(), t.stopPropagation();
      for (const i of [`\r
`, `
\r`, `
`, "\r"])
        if (e.includes(i)) {
          e = e.split(i).join(",");
          break;
        }
      this.addValue(e.split(",").filter((i) => i.length > 0)), setTimeout(() => this._alias.focus(), 200);
    }
  }
  // =========================================================================
  // Internal – actions (triggered by key events)
  // =========================================================================
  /**
   * Add the current alias text as a tag when Enter (13) or comma (188) is pressed.
   *
   * @private
   * @param {KeyboardEvent} e
   * @param {number}        keyCode  Key code that triggers the add.
   */
  _actKeypressValue(t, e) {
    const s = t.key, i = this._options;
    this._alias.value.length > 0 && s === e && !i.selectionRequired && i.multiple && (t.preventDefault(), t.stopPropagation(), this._extractValue(this._alias.value), this._resultsRemove());
  }
  /**
   * Start a recurring interval that commits a pending free-text value when
   * focus leaves the widget (multiple + !selectionRequired only).
   *
   * @private
   */
  _actAddValueOnLeave() {
    const t = this._options;
    if (this._leaveInterval || t.selectionRequired || !t.multiple) return;
    const e = this._hiddenInput.id;
    this._leaveInterval = setInterval(() => {
      var i;
      const s = (((i = document.activeElement) == null ? void 0 : i.id) ?? "").replace("-flexdatalist", "");
      e === s || this._itemSelected || (this._fetchDisabled = !0, this._alias.value.length > 0 && (this._extractValue(this._alias.value), this._resultsRemove()), clearInterval(this._leaveInterval), this._leaveInterval = null);
    }, 800);
  }
  /**
   * Debounce and trigger a search based on the current alias value.
   *
   * @private
   * @param {KeyboardEvent|InputEvent} e
   */
  _actSearch(t) {
    const e = t.key, s = this._options, i = this._alias.value;
    this._fetchDisabled = !1, clearTimeout(this._searchTimeout), (!e || !["Enter", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(e)) && (this._searchTimeout = setTimeout(() => {
      const a = i.length;
      (s.minLength === 0 && a > 0 || a >= s.minLength) && (this._resultsRemove(), this._resultsLoadingStart(), this._dataLoad((n) => {
        this._resultsLoadingStop(), this._searchGet(i, n, (o) => this._resultsShow(o));
      }));
    }, s.searchDelay));
  }
  /**
   * Copy alias text to the hidden input in single + !selectionRequired mode.
   *
   * @private
   * @param {KeyboardEvent} e
   */
  _actCopyValue(t) {
    if (t.key === "Enter") return;
    const e = this._options;
    !e.multiple && !e.selectionRequired && this._alias.value.length !== this._value.length && this._extractValue(this._alias.value);
  }
  /**
   * Handle Backspace: first press marks the last tag, second press removes it.
   *
   * @private
   * @param {KeyboardEvent} e
   */
  _actBackspace(t) {
    var s;
    const e = this._options;
    if (!(!e.removeOnBackspace || !e.multiple || t.key !== "Backspace"))
      if (this._alias.value.length)
        this._alias._fdRemove = null;
      else {
        const i = this._alias._fdRemove;
        if (i)
          this._multipleRemove(i.dataset.value), this._alias._fdRemove = null;
        else {
          const l = (s = this._multipleEl) == null ? void 0 : s.querySelector("li.input-container");
          this._alias._fdRemove = (l == null ? void 0 : l.previousElementSibling) ?? null;
        }
      }
  }
  /**
   * Show all results when `minLength` is 0 and alias is empty.
   *
   * @private
   * @param {Event} e
   */
  _actShowAll(t) {
    !this._alias.value.trim() && this._options.minLength === 0 && this._dataLoad((e) => this._resultsShow(e));
  }
  /**
   * Clear alias text if selectionRequired and nothing is selected.
   *
   * @private
   */
  _actClearText() {
    const t = this._options;
    !t.multiple && t.selectionRequired && !this._value.length && (this._alias.value = "");
  }
  /**
   * Clear stored value when alias falls below minLength.
   *
   * @private
   */
  _actClearValue() {
    const t = this._options;
    !t.multiple && t.selectionRequired && this._alias.value.length <= t.minLength && this._clearValue();
  }
  /**
   * Remove the results dropdown when alias falls below minLength.
   *
   * @private
   */
  _actRemoveResults() {
    const t = this._options;
    t.minLength > 0 && this._alias.value.length < t.minLength && this._resultsRemove();
  }
  // =========================================================================
  // Internal – value management
  // =========================================================================
  /**
   * Load a value (or array of values) — resolving against remote/static data
   * when `valueProperty` is configured.
   *
   * @private
   * @param {string|Array|Object} values   The raw value(s) to load.
   * @param {Function}            [cb]     Called with resolved values.
   * @param {boolean}             [init]   True during constructor init (suppresses events).
   */
  _loadValue(t, e, s) {
    let l = this._options.valueProperty;
    if (e = e ?? (() => {
    }), !this._toStr(t).length && !this._value.length) {
      e(t);
      return;
    }
    if (t = this._toObj(t), !this._isEmpty(t) && !this._isEmpty(l) && l !== "*") {
      typeof l == "string" && (l = l.split(",")), this._isObj(t) ? Array.isArray(t) || (t = [t]) : t = t.split(","), this._dataLoad((n) => {
        for (let o = 0; o < t.length; o++) {
          const r = t[o];
          for (const c of n)
            for (const d of l) {
              const m = this._prop(r, d) ?? r;
              this._prop(c, d) !== void 0 && String(m) === String(c[d]) && (t[o] = c);
            }
        }
        t.length && this._extractValue(t, s), e(t);
      }, t);
      return;
    }
    e(t), this._extractValue(t, s);
  }
  /**
   * Extract the display text and stored value from `values`, then update the
   * alias input and hidden input accordingly.
   *
   * @private
   * @param {string|Array|Object} values
   * @param {boolean} [init]  Suppress events when true.
   */
  _extractValue(t, e) {
    e || this._dispatch("before:flexdatalist.value", t);
    const s = [];
    if (Array.isArray(t))
      for (const i of t)
        s.push(this._extractOne(i));
    else
      s.push(this._extractOne(t));
    e !== !1 && (this._dispatch("after:flexdatalist.value", s), this._dispatch("change:flexdatalist", this._changePayload()), this._dispatch("change"));
  }
  /**
   * Extract and apply a single item: update alias display and hidden value.
   *
   * @private
   * @param {string|Object} val
   * @returns {{ value: string, text: string }}
   */
  _extractOne(t) {
    const e = this._getText(t), s = this._getVal(t);
    return this._options.multiple ? !this._isEmpty(e) && !this._isDup(e) && (this._texts.push(e), this._multipleAdd(s, e)) : (e && e !== this._alias.value && (this._alias.value = e), this._value = s, this._hiddenInput.value = this._value), { value: s, text: e };
  }
  /**
   * Derive the display text for `item`.
   * Honours `textProperty` and `{placeholder}` patterns.
   *
   * @private
   * @param {string|Object} item
   * @returns {string}
   */
  _getText(t) {
    if (!this._isObj(t)) return t;
    const e = this._options;
    let s = this._prop(t, e.searchIn[0]);
    return this._prop(t, e.textProperty) !== void 0 ? s = this._prop(t, e.textProperty) : s = this._replacePlaceholders(t, e.textProperty, s), s;
  }
  /**
   * Derive the stored value for `item`.
   * Handles scalar, specific property, `valueProperty:'*'` (whole object as JSON),
   * and array-of-properties modes.
   *
   * @private
   * @param {string|Object} item
   * @returns {string}
   */
  _getVal(t) {
    if (!this._isObj(t)) return t;
    const e = this._options, s = e.valueProperty;
    if (this._isJSON() || this._isMixed()) {
      const i = { ...t };
      if (delete i.name_highlight, Array.isArray(s)) {
        const l = {};
        for (const a of s) {
          const n = this._prop(t, a);
          n !== void 0 && (l[a] = n);
        }
        return this._toStr(l);
      }
      return this._toStr(i);
    }
    return this._prop(t, s) !== void 0 ? this._prop(t, s) : this._prop(t, e.searchIn[0]) !== void 0 ? this._prop(t, e.searchIn[0]) : null;
  }
  /**
   * Reset stored value and visible tags.
   *
   * @private
   * @param {boolean} [clearAlias] Also clear the alias input text.
   * @param {boolean} [init]       Suppress events.
   */
  _clearValue(t, e) {
    const s = this._value;
    this._options.multiple && this._multipleRemoveAll(), this._value = "", this._hiddenInput.value = "", this._texts = [], t && (this._alias.value = ""), s !== "" && !e && (this._dispatch("change:flexdatalist", { value: "", text: "" }), this._dispatch("clear:flexdatalist"), this._dispatch("change"));
  }
  /**
   * Deserialise a value string into its native type (array, object, string).
   *
   * @private
   * @param {string|Array|Object} val
   * @returns {string|Array|Object}
   */
  _toObj(t) {
    if (typeof t == "object") return t;
    const e = this._options;
    return this._isEmpty(t) ? e.multiple ? [] : this._isJSON() ? {} : "" : this._isCSV() ? t.toString().split(e.valuesSeparator).map((s) => s.trim()) : (this._isMixed() || this._isJSON()) && /^[\[{]/.test(t) ? JSON.parse(t) : typeof t == "number" ? t.toString() : t;
  }
  /**
   * Serialise a value (array / object / primitive) into a string for storage.
   *
   * @private
   * @param {*} val
   * @returns {string}
   */
  _toStr(t) {
    if (typeof t == "string")
      return t.trim();
    const e = this._options;
    return this._isEmpty(t) ? "" : typeof t == "number" ? t.toString() : this._isCSV() ? t.join(e.valuesSeparator) : this._isJSON() || this._isMixed() ? JSON.stringify(t) : "";
  }
  /**
   * Determine whether the valueProperty configuration expects JSON storage.
   * When called with a string argument, checks if that string is a JSON object.
   *
   * @private
   * @param {string} [str] Optional: test a specific string.
   * @returns {boolean}
   */
  _isJSON(t) {
    if (t !== void 0)
      return this._isObj(t) && (t = JSON.stringify(t)), typeof t != "string" ? !1 : t[0] === "{" || t.startsWith("[{");
    const e = this._options.valueProperty;
    return this._isObj(e) || e === "*";
  }
  /**
   * Whether the input accepts mixed (JSON + free-text) values.
   *
   * @private
   * @returns {boolean}
   */
  _isMixed() {
    const t = this._options;
    return !t.selectionRequired && (t.valueProperty === "*" || this._isObj(t.valueProperty));
  }
  /**
   * Whether the serialised value should be a comma-separated string.
   *
   * @private
   * @returns {boolean}
   */
  _isCSV() {
    return !this._isJSON() && this._options.multiple;
  }
  /**
   * Return true if `txt` already exists in the current tag list and duplicate
   * values are not allowed.
   *
   * @private
   * @param {string} txt Display text to check.
   * @returns {boolean}
   */
  _isDup(t) {
    return !this._options.allowDuplicateValues && this._texts.includes(t);
  }
  // =========================================================================
  // Internal – multiple value DOM
  // =========================================================================
  /**
   * Add a new tag to the multiple-value `<ul>`.
   *
   * @private
   * @param {string} val   Serialised value stored on the tag element.
   * @param {string} txt   Display text shown inside the tag.
   */
  _multipleAdd(t, e) {
    var l;
    const s = this._multipleMakeLi(t, e);
    s.addEventListener("click", () => this._multipleToggle(s)), (l = s.querySelector(".fdl-remove")) == null || l.addEventListener("click", (a) => {
      a.stopPropagation(), this._multipleRemove(s.dataset.value);
    });
    const i = this.getValue();
    i.includes(t) || (i.push(this._toObj(t)), this._value = this._toStr(i), this._hiddenInput.value = this._value), this._alias.value = "", this._multipleHandleLimit(), this._multipleCollapse();
  }
  /**
   * Toggle the visual disabled state of a tag (only when `toggleSelected` is on).
   *
   * @private
   * @param {HTMLLIElement|string} liOrVal Tag element or value string.
   */
  _multipleToggle(t) {
    if (!this._options.toggleSelected)
      return;
    const s = t instanceof HTMLElement ? t : this._multipleFindLi(String(t));
    if (!s)
      return;
    const i = s.classList.contains("disabled") ? "enable" : "disable", l = { value: s.dataset.value, text: s.dataset.text, action: i };
    this._dispatch("before:flexdatalist.toggle", l), s.classList.toggle("disabled", i === "disable"), this._value = this._toStr(
      [...this._multipleEl.querySelectorAll("li.toggle:not(.disabled)")].map((a) => a.dataset.value)
    ), this._hiddenInput.value = this._value, this._dispatch("after:flexdatalist.toggle", l), this._dispatch("change:flexdatalist", this._changePayload()), this._dispatch("change");
  }
  /**
   * Remove a tag by its serialised value.
   *
   * @private
   * @param {string} val Serialised value (matches `li.dataset.value`).
   * @returns {{ value: string, text: string }|undefined} Removed item data.
   */
  _multipleRemove(t) {
    const e = this._multipleFindLi(t);
    if (!e)
      return;
    const i = [...this._multipleEl.querySelectorAll("li:not(.input-container)")].indexOf(e), l = { value: e.dataset.value, text: e.dataset.text }, a = this.getValue();
    return a.splice(i, 1), this._value = this._toStr(a), this._hiddenInput.value = this._value, this._texts.splice(i, 1), e.remove(), this._multipleHandleLimit(), this._dispatch("after:flexdatalist.remove", this._changePayload()), this._dispatch("change:flexdatalist", this._changePayload()), this._dispatch("change"), l;
  }
  /**
   * Remove all tags from the multiple-value `<ul>`.
   *
   * @private
   */
  _multipleRemoveAll() {
    this._multipleEl && (this._dispatch("before:flexdatalist.remove.all", this.getValue()), this._multipleEl.querySelectorAll("li:not(.input-container)").forEach((t) => t.remove()), this._value = "", this._hiddenInput.value = "", this._texts = [], this._multipleHandleLimit(), this._dispatch("after:flexdatalist.remove.all", []));
  }
  /**
   * Create and insert a tag `<li>` before the input container.
   *
   * @private
   * @param {string} val  Serialised value.
   * @param {string} txt  Display text.
   * @returns {HTMLLIElement}
   */
  _multipleMakeLi(t, e) {
    const s = this._options, i = document.createElement("li");
    i.className = "value" + (s.toggleSelected ? " toggle" : ""), i.dataset.text = e, i.dataset.value = this._toStr(t);
    const l = document.createElement("span");
    l.className = "text", l.textContent = e;
    const a = document.createElement("span");
    a.className = "fdl-remove", a.innerHTML = "&times;", i.appendChild(l), i.appendChild(a);
    const n = this._multipleEl.querySelector("li.input-container");
    return this._multipleEl.insertBefore(i, n), i;
  }
  /**
   * Show or hide the alias input container based on `limitOfValues`.
   *
   * @private
   */
  _multipleHandleLimit() {
    if (!this._multipleEl) return;
    const t = this._options.limitOfValues, e = this._multipleEl.querySelector("li.input-container");
    e && (e.style.display = t > 0 && this._texts.length >= t ? "none" : "");
  }
  /**
   * Find a tag `<li>` element by its serialised value.
   *
   * @private
   * @param {string} val
   * @returns {HTMLLIElement|null}
   */
  _multipleFindLi(t) {
    return this._multipleEl ? [...this._multipleEl.querySelectorAll("li:not(.input-container)")].find((e) => e.dataset.value === String(t)) ?? null : null;
  }
  /**
   * Collapse tags beyond `collapseAfterN` into a single "N More" control.
   *
   * @private
   */
  _multipleCollapse() {
    const t = this._options;
    if (t.collapseAfterN === !1) return;
    const e = [...this._multipleEl.querySelectorAll("li.value")];
    if (e.length <= t.collapseAfterN) return;
    this._multipleExpand(), e.forEach((a, n) => {
      n + 1 > t.collapseAfterN && a.classList.add("flexdatalist-collapsed-item");
    });
    const s = this._multipleEl.querySelectorAll("li.flexdatalist-collapsed-item").length, i = document.createElement("li");
    i.className = "flexdatalist-collapsed-control", i.textContent = t.collapsedValuesText.replace("{count}", s), i.addEventListener("click", () => this._multipleExpand());
    const l = this._multipleEl.querySelector("li.input-container");
    this._multipleEl.insertBefore(i, l);
  }
  /**
   * Expand previously collapsed tags.
   *
   * @private
   */
  _multipleExpand() {
    var t;
    this._multipleEl && (this._multipleEl.querySelectorAll("li.flexdatalist-collapsed-item").forEach((e) => e.classList.remove("flexdatalist-collapsed-item")), (t = this._multipleEl.querySelector("li.flexdatalist-collapsed-control")) == null || t.remove());
  }
  // =========================================================================
  // Internal – data loading
  // =========================================================================
  /**
   * Load data from all three sources (remote, static, `<datalist>`), merge
   * them, then call `callback` with the combined array.
   *
   * @private
   * @param {Function} callback  `(data: Object[]) => void`
   * @param {*}        [load]    Optional value passed to the remote fetch
   *                             (used during initialisation to pre-populate).
   */
  _dataLoad(t, e) {
    if (this._fetchDisabled) return;
    this._dispatch("before:flexdatalist.data");
    let s = [];
    this._dataFetch((i) => {
      i && (s = s.concat(i)), this._dataStatic((l) => {
        l && (s = s.concat(l)), this._dataFromDatalist((a) => {
          a != null && a.length && (s = s.concat(a)), this._dispatch("after:flexdatalist.data", s), t(s);
        });
      });
    }, e);
  }
  /**
   * Load data from the `options.data` array or URL string.
   *
   * @private
   * @param {Function} callback `(data: Object[]|null) => void`
   */
  _dataStatic(t) {
    const e = this._options;
    if (this._isEmpty(e.data)) {
      t(null);
      return;
    }
    if (typeof e.data == "string") {
      const s = this._cacheRead(e.data, !0);
      if (s) {
        t(s);
        return;
      }
      this._request({ url: e.data, success: (i) => {
        e.data = i, t(i), this._cacheWrite(e.data, i, e.cacheLifetime, !0);
      } });
    } else
      Array.isArray(e.data) || (e.data = []), t(e.data);
  }
  /**
   * Extract `<option>` elements from a linked `<datalist>` element.
   *
   * @private
   * @param {Function} callback `(data: Object[]) => void`
   */
  _dataFromDatalist(t) {
    var i;
    const e = this._hiddenInput.getAttribute("list"), s = [];
    e && ((i = document.getElementById(e)) == null || i.querySelectorAll("option").forEach((l) => {
      const a = l.value, n = l.textContent.trim();
      s.push({ label: n || a, value: a });
    })), t(s);
  }
  /**
   * Fetch results from the remote `options.url`.
   * Checks the cache before making a network request.
   *
   * @private
   * @param {Function} callback `(data: Object[]|null) => void`
   * @param {*}        [load]   Passed as `load` in the request body.
   */
  _dataFetch(t, e) {
    const s = this._options;
    if (this._isEmpty(s.url)) {
      t(null);
      return;
    }
    const i = this._alias.value, l = this._relativesData(), a = this._cacheKeyGen(
      { relative: l, load: e, keyword: i, contain: s.searchContain },
      s.url
    ), n = this._cacheRead(a, !0);
    if (n) {
      t(n);
      return;
    }
    const o = typeof s.params == "function" ? s.params.call(this._hiddenInput, i) : { ...s.params }, r = { ...l, ...o, selected: this._value, original: s.originalValue };
    e !== void 0 && (r.load = e), r[s.keywordParamName] = i, r[s.searchContainParamName] = s.searchContain, this._request({
      url: s.url,
      data: r,
      success: (c) => {
        t(c), this._cacheWrite(a, c, s.cacheLifetime, !0);
      }
    });
  }
  /**
   * Execute a fetch request, applying method, headers, and content-type.
   *
   * @private
   * @param {{ url: string, data?: Object, success: Function }} settings
   */
  _request(t) {
    const e = this._options, s = this._hiddenInput;
    if (s.classList.contains("flexdatalist-loading")) {
      this._pendingRequest = t;
      return;
    }
    this._pendingRequest = null, s.classList.add("flexdatalist-loading");
    let i = t.url;
    const l = { method: e.requestType.toUpperCase() };
    e.requestHeaders && (l.headers = { ...e.requestHeaders }), e.requestType.toLowerCase() === "post" ? e.requestContentType === "json" ? (l.headers = { ...l.headers ?? {}, "Content-Type": "application/json; charset=UTF-8" }, l.body = JSON.stringify(t.data ?? {})) : (l.headers = { ...l.headers ?? {}, "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8" }, l.body = new URLSearchParams(t.data ?? {}).toString()) : t.data && (i += (i.includes("?") ? "&" : "?") + new URLSearchParams(t.data).toString()), fetch(i, l).then((a) => a.json()).then((a) => t.success(this._extractRemoteData(a))).catch((a) => {
      e.debug && console.error("Flexdatalist fetch error:", a);
    }).finally(() => {
      if (s.classList.remove("flexdatalist-loading"), this._pendingRequest) {
        const a = this._pendingRequest;
        this._pendingRequest = null, this._request(a);
      }
    });
  }
  /**
   * Unwrap the `resultsProperty` key from a remote response object.
   * Also applies any inline option overrides the server may return.
   *
   * @private
   * @param {Object|Array} data Raw server response.
   * @returns {Array}
   */
  _extractRemoteData(t) {
    const e = this._options;
    let s = this._prop(t, e.resultsProperty) ?? t;
    return typeof s == "string" && s.startsWith("[{") && (s = JSON.parse(s)), s != null && s.options && Object.assign(this._options, s.options), this._isObj(s) ? s : [];
  }
  /**
   * Build the `relatives` sub-object from linked input elements.
   *
   * @private
   * @returns {{ relatives?: Object }}
   */
  _relativesData() {
    const t = this._options.relatives;
    if (!(t != null && t.length)) return {};
    const e = { relatives: {} };
    for (const s of t) {
      const i = (s.name ?? "").replace(/\]\[/g, "-").replace(/[\[\]]/g, "-").replace(/^[-|]+|[-|]+$/g, "");
      e.relatives[i] = s.value;
    }
    return e;
  }
  /**
   * Show the animated loading bar above the alias / multiple wrapper.
   *
   * @private
   */
  _loadingStart() {
    const t = this._multipleEl ?? this._alias;
    t.classList.add("flexdatalist-fetching");
    const e = document.createElement("div");
    e.className = "flexdatalist-fetch-animation", e._fdTarget = t, document.body.appendChild(e), this._position(e, t);
  }
  /**
   * Remove the loading bar.
   *
   * @private
   */
  _loadingStop() {
    document.querySelectorAll(".flexdatalist-fetch-animation").forEach((t) => t.remove());
  }
  // =========================================================================
  // Internal – search / filtering
  // =========================================================================
  /**
   * Filter `data` against `keywords`, caching results in localStorage.
   *
   * @private
   * @param {string}   keywords  The typed keyword string.
   * @param {Object[]} data      Full data set to filter.
   * @param {Function} callback  `(matches: Object[]) => void`
   */
  _searchGet(t, e, s) {
    if (this._options.searchDisabled) {
      s(e);
      return;
    }
    const l = this._cacheRead(t);
    if (l) {
      s(l);
      return;
    }
    this._dispatch("before:flexdatalist.search", { keywords: t, data: e });
    let a = e;
    if (!this._isEmpty(t)) {
      a = [];
      const n = this._searchSplit(t);
      for (const o of e) {
        if (this._isDup(this._getText(o))) continue;
        const r = this._searchMatch(o, n);
        r && a.push(r);
      }
    }
    this._cacheWrite(t, a, 2), this._dispatch("after:flexdatalist.search", { keywords: t, data: e, results: a }), s(a);
  }
  /**
   * Test `item` against all search `keywords`.
   * Returns an augmented copy of the item with `_highlight` properties added,
   * or `false` if the item does not match.
   *
   * @private
   * @param {Object}   item     Data item to test.
   * @param {string[]} keywords Words to look for.
   * @returns {Object|false}
   */
  _searchMatch(t, e) {
    const s = this._options, i = { ...t }, l = [];
    for (const a of s.searchIn) {
      const n = this._prop(t, a);
      if (!n) continue;
      const o = String(n);
      let r = o;
      const c = this._searchSplit(o);
      for (const d of e)
        this._searchFind(d, c) && (l.push(d), r = this._hlMark(d, r));
      r !== o && (i[a + "_highlight"] = this._hlWrap(r));
    }
    return !l.length || s.searchByWord && l.length < e.length - 1 ? !1 : i;
  }
  /**
   * Mark keyword occurrences in `text` with sentinel tokens for later wrapping.
   *
   * @private
   * @param {string} kw   Keyword to mark.
   * @param {string} text Source text.
   * @returns {string} Text with `|:|…|::|` sentinel markers.
   */
  _hlMark(t, e) {
    if (!e) return e;
    const s = t.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return e.replace(new RegExp(s, this._options.searchContain ? "igu" : "iu"), "|:|$&|::|");
  }
  /**
   * Replace sentinel tokens with `<span class="highlight">…</span>`.
   *
   * @private
   * @param {string} str  Text containing sentinel tokens.
   * @returns {string} HTML string with highlight spans.
   */
  _hlWrap(t) {
    return t.split("|:|").join('<span class="highlight">').split("|::|").join("</span>");
  }
  /**
   * Test whether `kw` appears in any of the `parts` strings,
   * according to `searchContain` / `searchEqual` rules.
   *
   * @private
   * @param {string}   kw    Keyword.
   * @param {string[]} parts String parts to search.
   * @returns {boolean}
   */
  _searchFind(t, e) {
    const s = this._options, i = this._normalize(t);
    for (const l of e) {
      const a = this._normalize(l);
      if (s.searchEqual) return a === i;
      if (s.searchContain ? a.includes(i) : a.startsWith(i)) return !0;
    }
    return !1;
  }
  /**
   * Split a keyword string into an array, optionally expanding word-by-word.
   *
   * @private
   * @param {string|string[]} kws  Input keyword(s).
   * @returns {string[]}
   */
  _searchSplit(t) {
    if (typeof t == "string" && (t = [t.trim()]), this._options.searchByWord) {
      const e = [];
      for (const s of t)
        s.includes(" ") && e.push(...s.split(" "));
      t = [...t, ...e];
    }
    return t;
  }
  /**
   * Normalise a string for comparison: strip diacritics, uppercase.
   * Applies `options.normalizeString` first when provided.
   *
   * @private
   * @param {string} str
   * @returns {string}
   */
  _normalize(t) {
    if (typeof t != "string") return t;
    const e = this._options.normalizeString;
    return typeof e == "function" && (t = e(t)), t.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toUpperCase();
  }
  // =========================================================================
  // Internal – results rendering
  // =========================================================================
  /**
   * Render a result list from `results`, replacing any previous dropdown.
   *
   * @private
   * @param {Object[]|null} results  Matched items (empty array → "no results").
   */
  _resultsShow(t) {
    var l;
    if (this._resultsRemove(!0), !t) return;
    if (!t.length) {
      this._resultsEmpty(this._options.noResultsText);
      return;
    }
    const e = this._resultsContainer(), s = this._options;
    if (!s.groupBy)
      this._renderItems(t, e);
    else {
      const a = this._groupResults(t);
      for (const [n, o] of Object.entries(a)) {
        const r = this._hlProp(o[0], s.groupBy, n), c = document.createElement("li");
        c.className = "group", c.innerHTML = `<span class="group-name">${r}</span><span class="group-item-count"> ${o.length}</span>`, e.appendChild(c), this._renderItems(o, e);
      }
    }
    const i = [...e.querySelectorAll("li:not(.group)")];
    for (const a of i)
      a.addEventListener("click", () => {
        const n = a._fdItem;
        n && (this._dispatch("before:flexdatalist.select", n), n.ignoreSelection || this._extractValue(n), this._resultsRemove(), this._dispatch("after:flexdatalist.select", n), this._dispatch("select:flexdatalist", n), this._itemSelected = !0, setTimeout(() => {
          this._itemSelected = !1;
        }, 1e3), s.multiple && setTimeout(() => this._alias.focus(), 500));
      }), a.addEventListener("mouseenter", () => {
        i.forEach((n) => n.classList.remove("active")), a.classList.add("active"), a.dispatchEvent(new CustomEvent("active:flexdatalist.results", { detail: a._fdItem }));
      }), a.addEventListener("mouseleave", () => a.classList.remove("active"));
    s.focusFirstResult && ((l = e.querySelector("li.item")) == null || l.classList.add("active"));
  }
  /**
   * Show the "no results" message or "add new item" option.
   *
   * @private
   * @param {string} text  Message template (may contain `{keyword}`).
   */
  _resultsEmpty(t) {
    if (this._isEmpty(t)) return;
    const e = this._resultsContainer(), s = this._options, i = this._alias.value;
    if (s.showAddNewItem && i.length > 0) {
      const l = document.createElement("li");
      l.className = "item no-results add-new-item", l.innerHTML = s.addNewItemText.replace("{keyword}", this._escape(i)), l._fdItem = { isAddNew: !0, keyword: i }, e.appendChild(l), l.addEventListener("click", () => {
        this._resultsRemove(), this._dispatch("addnew:flexdatalist", i);
      }), l.addEventListener("mouseenter", () => l.classList.add("active")), l.addEventListener("mouseleave", () => l.classList.remove("active"));
    } else {
      const l = document.createElement("li");
      l.className = "item no-results", l.textContent = t.replace("{keyword}", i), e.appendChild(l);
    }
    this._dispatch("empty:flexdatalist.results", [t]);
  }
  /**
   * Iterate `items` and append result `<li>` elements to `ul`.
   *
   * @private
   * @param {Object[]}       items
   * @param {HTMLUListElement} ul
   */
  _renderItems(t, e) {
    const s = this._options.maxShownResults;
    this._dispatch("show:flexdatalist.results", t);
    for (let i = 0; i < t.length && !(s > 0 && i >= s); i++)
      e.appendChild(this._makeItem(t[i], i, t.length));
    this._dispatch("shown:flexdatalist.results", t);
  }
  /**
   * Create a single result `<li>` for `item`.
   * Renders each property listed in `visibleProperties`, supporting
   * `{placeholder}` patterns and the icon property.
   *
   * @private
   * @param {Object} item
   * @param {number} idx   Zero-based index within the results list.
   * @param {number} total Total result count (for ARIA attributes).
   * @returns {HTMLLIElement}
   */
  _makeItem(t, e, s) {
    const i = this._options, l = document.createElement("li");
    l.className = "item", l.setAttribute("role", "option"), l.setAttribute("tabindex", "-1"), l.setAttribute("aria-posinset", e + 1), l.setAttribute("aria-setsize", s), l._fdItem = t;
    for (const a of i.visibleProperties) {
      let n;
      if (a.includes("{")) {
        const o = this._replacePlaceholders(t, a, ""), r = this._parsePlaceholders(a);
        n = document.createElement("span"), n.className = "prop-" + (Object.values(r ?? {}).join("-") || a), n.innerHTML = o + " ";
      } else {
        if (i.groupBy === a || this._prop(t, a) === void 0) continue;
        a === i.iconProperty ? (n = document.createElement("img"), n.className = "prop-" + a, n.src = t[a]) : (n = document.createElement("span"), n.className = "prop-" + a, n.innerHTML = this._hlProp(t, a) + " ");
      }
      l.appendChild(n);
    }
    return this._dispatch("item:flexdatalist.results", t), l;
  }
  /**
   * Return (creating if necessary) the `<ul class="flexdatalist-results">` container.
   *
   * @private
   * @returns {HTMLUListElement}
   */
  _resultsContainer() {
    let t = document.querySelector("ul.flexdatalist-results");
    if (t) return t;
    const e = this._multipleEl ?? this._alias, s = getComputedStyle(e), i = e.closest("dialog") ?? document.body;
    return t = document.createElement("ul"), t.className = "flexdatalist-results", t.id = this._alias.id + "-results", t.setAttribute("role", "listbox"), t.style.borderTopLeftRadius = s.borderTopLeftRadius, t.style.borderTopRightRadius = s.borderTopRightRadius, t.style.borderBottomLeftRadius = s.borderBottomLeftRadius, t.style.borderBottomRightRadius = s.borderBottomRightRadius, t._fdTarget = e, t._fdInput = this._hiddenInput, i.appendChild(t), this._position(t, e), t;
  }
  /**
   * Group a flat results array by `options.groupBy` property value.
   *
   * @private
   * @param {Object[]} results
   * @returns {Object.<string, Object[]>} Map of groupName → items.
   */
  _groupResults(t) {
    const e = this._options.groupBy, s = {};
    for (const i of t) {
      const l = this._prop(i, e);
      if (l === void 0) {
        this._options.debug && console.warn(`Flexdatalist: groupBy property "${e}" not found.`);
        break;
      }
      s[l] || (s[l] = []), s[l].push(i);
    }
    return s;
  }
  /**
   * Return the highlighted value of `property` for `item`, falling back to
   * the raw property value or `fallback`.
   *
   * @private
   * @param {Object} item
   * @param {string} property
   * @param {string} [fallback='']
   * @returns {string}
   */
  _hlProp(t, e, s = "") {
    const i = this._prop(t, e + "_highlight");
    return i !== void 0 ? i : String(this._prop(t, e) ?? s);
  }
  /**
   * Replace the results dropdown with a loading spinner `<ul>`.
   *
   * @private
   */
  _resultsLoadingStart() {
    const t = this._options;
    if (!t.resultsLoader || document.querySelector(".flexdatalist-fetching-results")) return;
    this._resultsRemove();
    const e = this._resultsContainer();
    e.classList.replace("flexdatalist-results", "flexdatalist-fetching-results");
    const s = document.createElement("li");
    s.className = "flexdatalist-fetching-results-loader";
    const i = document.createElement("img");
    i.src = t.resultsLoader, i.alt = "", s.appendChild(i), e.appendChild(s);
  }
  /**
   * Remove the loading spinner `<ul>`.
   *
   * @private
   */
  _resultsLoadingStop() {
    document.querySelectorAll("ul.flexdatalist-fetching-results").forEach((t) => t.remove());
  }
  /**
   * Remove the results dropdown (or only its `<li>` children when `itemsOnly`).
   *
   * @private
   * @param {boolean} [itemsOnly=false]
   */
  _resultsRemove(t = !1) {
    this._dispatch("remove:flexdatalist.results"), document.querySelectorAll(t ? "ul.flexdatalist-results li" : "ul.flexdatalist-results").forEach((e) => e.remove()), this._dispatch("removed:flexdatalist.results");
  }
  // =========================================================================
  // Internal – positioning
  // =========================================================================
  /**
   * Position `container` directly below `target` using fixed coordinates.
   * Handles the case where both elements are inside a `<dialog>`.
   *
   * @private
   * @param {HTMLElement} [container]  Defaults to `ul.flexdatalist-results`.
   * @param {HTMLElement} [target]     Defaults to `container._fdTarget`.
   */
  _position(t, e) {
    var d;
    if (t = t ?? document.querySelector("ul.flexdatalist-results"), !t || (e = e ?? t._fdTarget, !e)) return;
    const s = e.getBoundingClientRect(), i = window.scrollY ?? document.documentElement.scrollTop, l = window.scrollX ?? document.documentElement.scrollLeft, a = Math.max(s.width, 200), n = e.closest("dialog"), o = (d = t.closest) == null ? void 0 : d.call(t, "dialog");
    let r, c;
    if (n && o) {
      const m = n.getBoundingClientRect();
      r = s.top - m.top + s.height + "px", c = s.left - m.left + "px";
    } else
      r = s.top + i + s.height + "px", c = s.left + l + "px";
    t.style.width = a + "px", t.style.top = r, t.style.left = c;
  }
  // =========================================================================
  // Internal – disabled / readonly
  // =========================================================================
  /**
   * Apply or remove the disabled state on the original input, alias, and
   * multiple-value wrapper.
   *
   * @private
   * @param {boolean|null} disabled
   * @returns {boolean} Current disabled state.
   */
  _applyDisabled(t) {
    if (t == null) return this._options.disabled;
    if (this._hiddenInput.disabled = t, this._alias.disabled = t, this._multipleEl) {
      const e = this._multipleEl.querySelectorAll("li .fdl-remove"), s = this._multipleEl.querySelector("li.input-container");
      this._multipleEl.classList.toggle("disabled", t), e.forEach((i) => {
        i.style.display = t ? "none" : "";
      }), s && (s.style.display = t ? "none" : "");
    }
    return this._options.disabled = t, t;
  }
  /**
   * @private
   * @returns {boolean}
   */
  _isDisabled() {
    return !!this._options.disabled;
  }
  /**
   * Apply or remove the readonly state.
   *
   * @private
   * @param {boolean} ro
   */
  _applyReadonly(t) {
    if (this._hiddenInput.readOnly = t, this._alias.readOnly = t, this._multipleEl) {
      const e = this._multipleEl.querySelectorAll("li .fdl-remove"), s = this._multipleEl.querySelector("li.input-container");
      this._multipleEl.classList.toggle("disabled", t), e.forEach((i) => {
        i.style.display = t ? "none" : "";
      }), s && (s.style.display = t ? "none" : "");
    }
    this._options.readonly = t;
  }
  // =========================================================================
  // Internal – localStorage cache
  // =========================================================================
  /**
   * @private
   * @returns {boolean} Whether localStorage is available and cache is enabled.
   */
  _cacheSupported() {
    if (!this._options.cache) return !1;
    try {
      return "localStorage" in window && window.localStorage !== null;
    } catch {
      return !1;
    }
  }
  /**
   * Write a value to the cache.
   *
   * @private
   * @param {string|Object} key
   * @param {*}             value
   * @param {number}        [lifetime]  Seconds until expiry.
   * @param {boolean}       [global]    Use the shared global key prefix.
   */
  _cacheWrite(t, e, s, i) {
    this._cacheSupported() && localStorage.setItem(this._cacheKeyGen(t, void 0, i), JSON.stringify({
      value: e,
      timestamp: Math.round(Date.now() / 1e3),
      lifetime: s || !1
    }));
  }
  /**
   * Read a value from the cache.  Returns `null` if missing or expired.
   *
   * @private
   * @param {string|Object} key
   * @param {boolean}       [global]
   * @returns {*|null}
   */
  _cacheRead(t, e) {
    if (!this._cacheSupported()) return null;
    const s = this._cacheKeyGen(t, void 0, e), i = localStorage.getItem(s);
    if (!i) return null;
    const l = JSON.parse(i);
    return l.lifetime && Math.round(Date.now() / 1e3) - l.timestamp >= l.lifetime ? (localStorage.removeItem(s), null) : l.value;
  }
  /**
   * Remove expired cache entries whose keys begin with the `fdl-` prefix.
   *
   * @private
   */
  _cacheGC() {
    if (this._cacheSupported())
      for (const t of Object.keys(localStorage)) {
        if (!t.startsWith("fdl-")) continue;
        const e = JSON.parse(localStorage.getItem(t));
        e != null && e.lifetime && Math.round(Date.now() / 1e3) - e.timestamp >= e.lifetime && localStorage.removeItem(t);
      }
  }
  /**
   * Generate a short FNV-1a hash key from a string or object.
   *
   * @private
   * @param {string|Object} str     Input to hash.
   * @param {number}        [seed]  Hash seed.
   * @param {boolean}       [global] Use `fdl-g-` prefix instead of `fdl-`.
   * @returns {string}
   */
  _cacheKeyGen(t, e, s) {
    typeof t == "object" && (t = JSON.stringify(t));
    let i = e ?? 2166136261;
    for (let l = 0; l < t.length; l++)
      i ^= t.charCodeAt(l), i += (i << 1) + (i << 4) + (i << 7) + (i << 8) + (i << 24);
    return (s ? "fdl-g-" : "fdl-") + ("0000000" + (i >>> 0).toString(16)).slice(-8);
  }
  // =========================================================================
  // Internal – utilities
  // =========================================================================
  /**
   * Safely read a nested property using dot-notation.
   *
   * @private
   * @param {Object} obj
   * @param {string} path  Dot-separated path, e.g. `'address.city'`.
   * @returns {*} Property value, or `undefined` if not found.
   */
  _prop(t, e) {
    if (!(!t || typeof e != "string"))
      return e.split(".").reduce((s, i) => s == null ? void 0 : s[i], t);
  }
  /**
   * @private
   * @param {*} v
   * @returns {boolean}
   */
  _isObj(t) {
    return t !== null && typeof t == "object";
  }
  /**
   * Broad "empty" check covering undefined, null, '', [], {}.
   *
   * @private
   * @param {*} v
   * @returns {boolean}
   */
  _isEmpty(t) {
    return t == null ? !0 : t === !0 ? !1 : typeof t == "string" ? t.trim() === "" : Array.isArray(t) ? t.length === 0 : this._isObj(t) ? Object.keys(t).length === 0 : !1;
  }
  /**
   * Escape a string for safe insertion into HTML.
   *
   * @private
   * @param {string} str
   * @returns {string}
   */
  _escape(t) {
    return String(t).replace(/&/g, "&amp;").replace(/"/g, "&quot;").replace(/'/g, "&#039;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  }
  /**
   * Replace `{key}` placeholders in `pattern` with values from `item`.
   *
   * @private
   * @param {Object} item      Data source object.
   * @param {string} pattern   Pattern string, e.g. `'{name} ({city})'`.
   * @param {*}      fallback  Returned when pattern is empty or no match.
   * @returns {string|*}
   */
  _replacePlaceholders(t, e, s) {
    if (!this._isObj(t) || typeof e != "string") return s;
    const i = this._parsePlaceholders(e);
    if (!i) return s;
    for (const [l, a] of Object.entries(i)) {
      const n = this._prop(t, a);
      n !== void 0 && (e = e.replace(l, n));
    }
    return e;
  }
  /**
   * Extract `{key}` tokens from a pattern string into a `{ '{key}': 'key' }` map.
   *
   * @private
   * @param {string} pattern
   * @returns {Object.<string,string>|false}
   */
  _parsePlaceholders(t) {
    if (!t) return !1;
    const e = t.match(/\{.+?\}/g);
    return e ? Object.fromEntries(e.map((s) => [s, s.slice(1, -1)])) : !1;
  }
  /**
   * Build a consistent `{value, text}` payload for `change:flexdatalist`.
   *
   * @private
   * @returns {{ value: string, text: string }}
   */
  _changePayload() {
    var e;
    const t = this._options.valuesSeparator;
    return {
      value: this._hiddenInput.value,
      text: this._options.multiple ? this._texts.join(t) : ((e = this._alias) == null ? void 0 : e.value) ?? ""
    };
  }
  /**
   * Dispatch a CustomEvent on the hidden input element.
   * The value is available directly as `e.detail` (no array wrapping).
   *
   * @private
   * @param {string} name      Event name.
   * @param {*}      [detail]  Value placed in `event.detail`. Omit for null.
   */
  _dispatch(t, e = null) {
    this._hiddenInput.dispatchEvent(new CustomEvent(t, { detail: e, bubbles: !0 }));
  }
};
p = new WeakMap(), _ = new WeakMap(), y = new WeakSet(), A = function() {
  f(this, _) || (b(this, _, !0), document.addEventListener("mouseup", (t) => {
    const e = document.querySelector("ul.flexdatalist-results");
    if (!e) return;
    const s = e._fdTarget;
    s && s === document.activeElement || !e.contains(t.target) && e !== t.target && e.remove();
  }), document.addEventListener("keydown", (t) => {
    const e = document.querySelector("ul.flexdatalist-results");
    if (!e) return;
    const s = [...e.querySelectorAll("li.item")];
    if (!s.length) return;
    const i = t.key;
    if (i === "Escape") {
      e.remove();
      return;
    }
    if (i === "Enter") {
      const l = e.querySelector("li.item.active");
      t.preventDefault(), l ? l.click() : e.remove();
      return;
    }
    if (i === "ArrowDown" || i === "ArrowUp") {
      t.preventDefault();
      const l = e.querySelector("li.item.active"), a = l ? s.indexOf(l) : -1;
      l == null || l.classList.remove("active");
      const n = i === "ArrowDown" ? s[a + 1] ?? s[0] : s[a - 1] ?? s[s.length - 1];
      n == null || n.classList.add("active"), n && (e.scrollTop = n.offsetTop);
    }
  }), window.addEventListener("pageshow", (t) => {
    t.persisted && document.querySelectorAll("input.flexdatalist-set").forEach((e) => {
      const s = h.getInstance(e), i = e.id ? e.id + "-flexdatalist" : "", l = i ? document.getElementById(i) : null;
      s && e.value && (!l || !l.value) && s.setValue(e.value);
    });
  }), document.addEventListener("DOMContentLoaded", () => {
    h.init("input.flexdatalist:not(.flexdatalist-set):not(.autodiscover-disabled)");
  }));
}, g(h, y), // =========================================================================
// Static – defaults & registry
// =========================================================================
/**
 * Default option values merged with every instance.
 * All keys can be overridden via the constructor options object or
 * via `data-*` attributes on the element (camelCase → kebab-case).
 *
 * @type {Object}
 */
S(h, "defaults", {
  /** @type {string|null} Remote URL to fetch results from. */
  url: null,
  /** @type {Array|string} Static data array or URL string to pre-load. */
  data: [],
  /**
   * Extra query-string / body params sent with every remote request.
   * May also be a function `(keyword) => Object`.
   * @type {Object|Function}
   */
  params: {},
  /**
   * CSS selector or NodeList of inputs whose values are sent as
   * `relatives[name]` with every request.
   * @type {string|NodeList|null}
   */
  relatives: null,
  /** @type {boolean} Disable this input when all relatives are empty. */
  chainedRelatives: !1,
  /** @type {boolean} Cache remote results in localStorage. */
  cache: !0,
  /** @type {number} Cache lifetime in seconds. */
  cacheLifetime: 60,
  /** @type {number} Minimum characters before triggering search (0 = show all on focus). */
  minLength: 3,
  /** @type {string|false} Item property name to group results by. */
  groupBy: !1,
  /**
   * Property (or `{placeholder}` pattern) used as the display text in
   * the alias input. Defaults to the first entry of `searchIn`.
   * @type {string|null}
   */
  textProperty: null,
  /**
   * Property name(s) stored as the actual value.
   * Use `'*'` to store the entire matched object as JSON.
   * @type {string|string[]|null}
   */
  valueProperty: null,
  /**
   * Properties rendered inside each result `<li>`.
   * Supports `{placeholder}` patterns. Defaults to `searchIn`.
   * @type {string[]}
   */
  visibleProperties: [],
  /** @type {string} Property containing an image URL rendered as <img>. */
  iconProperty: "thumb",
  /** @type {string[]} Properties searched when filtering results. */
  searchIn: ["label"],
  /** @type {boolean} Match keyword anywhere in the string (not just at start). */
  searchContain: !1,
  /** @type {boolean} Require exact full-string match. */
  searchEqual: !1,
  /** @type {boolean} Split keyword on spaces and match all words independently. */
  searchByWord: !1,
  /** @type {boolean} Skip local filtering entirely (rely on server-side search). */
  searchDisabled: !1,
  /** @type {number} Debounce delay in ms before running a search. */
  searchDelay: 300,
  /**
   * Custom string normalizer called before comparison.
   * Signature: `(string) => string`
   * @type {Function|null}
   */
  normalizeString: null,
  /** @type {boolean|null} Allow multiple values. Inferred from [multiple] attribute when null. */
  multiple: null,
  /** @type {boolean|null} Start disabled. Inferred from [disabled] attribute when null. */
  disabled: null,
  /** @type {boolean} Require the user to select a result (disables free-text). */
  selectionRequired: !1,
  /** @type {boolean} Automatically activate the first result item. */
  focusFirstResult: !1,
  /** @type {string} Key in the remote JSON response that holds the results array. */
  resultsProperty: "results",
  /** @type {number} Maximum number of results rendered (0 = unlimited). */
  maxShownResults: 100,
  /**
   * Collapse multiple-value tags after this many items.
   * Set `false` to disable collapsing.
   * @type {number|false}
   */
  collapseAfterN: 50,
  /** @type {string} Label for the collapse toggle. `{count}` is replaced. */
  collapsedValuesText: "{count} More",
  /** @type {string} Message shown when no results match. `{keyword}` is replaced. */
  noResultsText: 'No results found for "{keyword}"',
  /**
   * URL of a loading spinner image shown while fetching results.
   * Set null to disable.
   * @type {string|null}
   */
  resultsLoader: null,
  /** @type {boolean} Clicking a selected tag toggles its disabled state. */
  toggleSelected: !1,
  /** @type {boolean} Allow the same value to be added more than once. */
  allowDuplicateValues: !1,
  /** @type {boolean} Pressing Backspace on an empty alias marks then removes the last tag. */
  removeOnBackspace: !0,
  /** @type {string} HTTP method for remote requests ('get' or 'post'). */
  requestType: "get",
  /** @type {string} Content-type for POST bodies ('x-www-form-urlencoded' or 'json'). */
  requestContentType: "x-www-form-urlencoded",
  /** @type {Object|null} Extra HTTP headers merged into every request. */
  requestHeaders: null,
  /** @type {string} Query-string parameter name that carries the typed keyword. */
  keywordParamName: "keyword",
  /** @type {string} Query-string parameter name that carries the searchContain flag. */
  searchContainParamName: "contain",
  /** @type {number} Maximum number of tags in multiple mode (0 = unlimited). */
  limitOfValues: 0,
  /** @type {string} Separator used when serialising multiple values into a string. */
  valuesSeparator: ",",
  /** @type {boolean} Show an "Add new item" option when no results exist. */
  showAddNewItem: !1,
  /** @type {string} Text of the "Add new item" option. `{keyword}` is replaced. */
  addNewItemText: 'No results found for "{keyword}". Click to add it.',
  /** @type {boolean} Log warnings to the console. */
  debug: !0
}), /** @type {WeakMap<HTMLElement, Flexdatalist>} */
g(h, p, /* @__PURE__ */ new WeakMap()), /** @type {boolean} Whether global document listeners have been attached. */
g(h, _, !1);
let L = h;
export {
  L as Flexdatalist,
  L as default
};
