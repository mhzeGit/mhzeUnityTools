// The activity panel: every change on the board, newest first.

import { api } from "./api.js";
import { state } from "./state.js";
import { avatar, fill, fullTime, h, timeAgo } from "./util.js";

const MAX_ITEMS = 120;
let panel = null;
let list = null;
let events = [];
let openTask = () => {};

export function initFeed(element, onOpenTask) {
  panel = element;
  list = element.querySelector("#feed-list");
  openTask = onOpenTask;
}

export function isFeedOpen() {
  return panel?.classList.contains("open");
}

export async function toggleFeed(force) {
  const open = force ?? !isFeedOpen();
  panel.classList.toggle("open", open);
  panel.setAttribute("aria-hidden", String(!open));
  document.getElementById("toggle-feed").setAttribute("aria-pressed", String(open));
  if (open) {
    events = (await api.events(MAX_ITEMS)).reverse();
    renderFeed();
  }
}

/** Called for each live event from the stream. */
export function pushEvent(event) {
  if (event.data?.quiet) return;
  events.unshift(event);
  events = events.slice(0, MAX_ITEMS);
  if (isFeedOpen()) renderFeed();
}

function summaryNode(event) {
  const text = event.summary || event.type;
  const key = `T-${event.task}`;
  if (!text.startsWith(key)) return text;
  return [h("b", {}, key), text.slice(key.length)];
}

export function renderFeed() {
  if (!list) return;
  if (!events.length) {
    list.replaceChildren(h("li", { class: "empty" }, "No activity yet"));
    return;
  }
  fill(
    list,
    events.map((event) =>
      h(
        "li",
        { onclick: () => event.task && openTask(event.task) },
        avatar(event.by),
        h("div", { class: "text" }, h("b", {}, event.by), " · ", summaryNode(event), h("time", { title: fullTime(event.at) }, timeAgo(event.at, state.skewMs))),
      ),
    ),
  );
}
