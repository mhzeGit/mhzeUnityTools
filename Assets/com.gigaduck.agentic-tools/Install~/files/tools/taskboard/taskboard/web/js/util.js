// Small DOM and formatting helpers shared by every module.
// User text is only ever inserted as text nodes (never innerHTML), so task
// content cannot inject markup.

const ICONS = {
  search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-3.6-3.6"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  x: '<path d="M18 6 6 18M6 6l12 12"/>',
  activity: '<path d="M22 12h-4l-3 9L9 3l-3 9H2"/>',
  link: '<path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7"/><path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"/>',
  folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  alert: '<path d="M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4M12 17h.01"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
  check: '<path d="M20 6 9 17l-5-5"/>',
  deps: '<circle cx="6" cy="6" r="2.5"/><circle cx="18" cy="18" r="2.5"/><path d="M6 8.5V12a6 6 0 0 0 6 6h3.5"/>',
  hourglass: '<path d="M6 3h12M6 21h12M7 3v3a5 5 0 0 0 5 5 5 5 0 0 0 5-5V3M7 21v-3a5 5 0 0 1 5-5 5 5 0 0 1 5 5v3"/>',
  note: '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>',
  chevron: '<path d="m9 18 6-6-6-6"/>',
  edit: '<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/>',
  cpu: '<rect x="5" y="5" width="14" height="14" rx="2"/><path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3"/>',
  ban: '<circle cx="12" cy="12" r="9"/><path d="m5.6 5.6 12.8 12.8"/>',
  commit: '<circle cx="12" cy="12" r="3.5"/><path d="M3 12h5.5M15.5 12H21"/>',
};

const SVG_NS = "http://www.w3.org/2000/svg";

export function icon(name) {
  const span = document.createElement("span");
  span.className = "icon";
  const svg = document.createElementNS(SVG_NS, "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("fill", "none");
  svg.setAttribute("stroke", "currentColor");
  svg.setAttribute("stroke-width", "1.8");
  svg.setAttribute("stroke-linecap", "round");
  svg.setAttribute("stroke-linejoin", "round");
  svg.innerHTML = ICONS[name] || ""; // static, trusted markup only
  span.append(svg);
  return span;
}

/** Fill every <span data-icon="..."> already in the HTML. */
export function hydrateIcons(root = document) {
  root.querySelectorAll("[data-icon]").forEach((el) => {
    if (!el.firstChild) el.replaceWith(icon(el.dataset.icon));
  });
}

/**
 * h("div", {class: "x", onclick: fn, dataset: {id: 1}, style: {"--status": c}}, child, "text", [more])
 * Children may be nodes, strings, numbers, arrays or null/false (skipped).
 */
export function h(tag, props = {}, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(props || {})) {
    if (value === null || value === undefined || value === false) continue;
    if (key === "class") el.className = value;
    else if (key === "dataset") Object.assign(el.dataset, value);
    else if (key === "style") for (const [k, v] of Object.entries(value)) el.style.setProperty(k, v);
    else if (key.startsWith("on") && typeof value === "function") el.addEventListener(key.slice(2), value);
    else if (key === "value") el.value = value; // textarea content is a property, not an attribute
    else if (key in el && typeof value !== "string") el[key] = value;
    else el.setAttribute(key, value === true ? "" : value);
  }
  append(el, children);
  return el;
}

/** Replace an element's children; accepts the same children as h() (arrays, null). */
export function fill(el, ...children) {
  el.replaceChildren();
  append(el, children);
  return el;
}

function append(el, children) {
  for (const child of children) {
    if (child === null || child === undefined || child === false) continue;
    if (Array.isArray(child)) append(el, child);
    else el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
}

/** Text with http(s) links turned into anchors, built from text nodes only. */
export function linkify(text) {
  const frag = document.createDocumentFragment();
  const re = /https?:\/\/[^\s<>()]+/g;
  let last = 0;
  for (const match of String(text || "").matchAll(re)) {
    frag.append(text.slice(last, match.index));
    frag.append(h("a", { href: match[0], target: "_blank", rel: "noreferrer" }, match[0]));
    last = match.index + match[0].length;
  }
  frag.append(String(text || "").slice(last));
  return frag;
}

export function timeAgo(iso, skewMs = 0) {
  if (!iso) return "";
  const seconds = Math.max(0, (Date.now() + skewMs - Date.parse(iso)) / 1000);
  if (seconds < 45) return "just now";
  if (seconds < 3600) return `${Math.round(seconds / 60)}m ago`;
  if (seconds < 86400) return `${Math.round(seconds / 3600)}h ago`;
  if (seconds < 86400 * 7) return `${Math.round(seconds / 86400)}d ago`;
  return new Date(iso).toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

export function shortAge(seconds) {
  seconds = Math.max(0, Math.round(seconds));
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.round(seconds / 60)}m`;
  if (seconds < 86400) return `${Math.round(seconds / 3600)}h`;
  return `${Math.round(seconds / 86400)}d`;
}

export function fullTime(iso) {
  return iso ? new Date(iso).toLocaleString() : "";
}

export function debounce(fn, ms) {
  let timer = null;
  return (...args) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), ms);
  };
}

const AVATAR_COLORS = ["#8d9bff", "#6ec8c0", "#f2b65a", "#b691f5", "#f08fb0", "#7cc4ff", "#a6d36d", "#e8a07a"];

export function avatar(name, extraClass = "") {
  const text = String(name || "?").trim();
  let hash = 0;
  for (const ch of text.toLowerCase()) hash = (hash * 31 + ch.codePointAt(0)) >>> 0;
  const isSystem = ["board", "virtual-commit"].includes(text);
  const color = isSystem ? "#5d626b" : AVATAR_COLORS[hash % AVATAR_COLORS.length];
  const letters = text.split(/[\s\-_.]+/).filter(Boolean).slice(0, 2).map((w) => w[0]).join("").toUpperCase();
  return h("span", { class: `avatar ${extraClass}`.trim(), style: { "--avatar": color }, title: text }, letters || "?");
}

export function taskKey(id) {
  return `T-${id}`;
}
