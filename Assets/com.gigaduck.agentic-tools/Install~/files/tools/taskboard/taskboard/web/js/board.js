// The columns, and drag and drop between and within them.
//
// Inbox and Queue are ordered (priority, then your manual order): dropping a
// card shows a line where it will land and sends its new neighbors to
// /place. Other columns just change the status.

import { api } from "./api.js";
import { renderCard } from "./card.js";
import { RANKED_STATUSES, columnTasks, reload, state, visibleStatuses } from "./state.js";
import { ask } from "./ask.js";
import { toast } from "./toast.js";
import { h } from "./util.js";

const DONE_LIMIT = 25;
const HINTS = { ready: "agents take from the top" };

let root = null;
let openTask = () => {};
let pendingRender = false;
let dropLine = null;

export function initBoard(element, onOpenTask) {
  root = element;
  openTask = onOpenTask;
}

export function renderBoard() {
  if (!root || !state.data) return;
  if (state.draggingId !== null) {
    pendingRender = true; // never rebuild under the user's cursor
    return;
  }
  const scroll = {};
  root.querySelectorAll(".column").forEach((col) => {
    scroll[col.dataset.status] = col.querySelector(".column-list").scrollTop;
  });
  const boardScroll = root.scrollLeft;

  root.replaceChildren(...visibleStatuses().map(renderColumn));

  root.querySelectorAll(".column").forEach((col) => {
    col.querySelector(".column-list").scrollTop = scroll[col.dataset.status] || 0;
  });
  root.scrollLeft = boardScroll;
  state.flash.clear();
}

function renderColumn(status) {
  const tasks = columnTasks(status.key);
  const limited = status.terminal && !state.expanded.has(status.key) && tasks.length > DONE_LIMIT;
  const shown = limited ? tasks.slice(0, DONE_LIMIT) : tasks;
  const handlers = { onOpen: openTask, onDragStart: dragStart, onDragEnd: dragEnd };

  const list = h(
    "div",
    { class: "column-list" },
    shown.length ? shown.map((t) => renderCard(t, handlers)) : h("div", { class: "empty" }, emptyText(status.key)),
    limited
      ? h("button", { class: "more", onclick: () => { state.expanded.add(status.key); renderBoard(); } },
          `Show ${tasks.length - DONE_LIMIT} more`)
      : null,
  );
  const column = h(
    "section",
    { class: "column", dataset: { status: status.key }, style: { "--status": status.color } },
    h(
      "header",
      { class: "column-head", title: status.hint },
      h("span", { class: "dot" }),
      h("h2", {}, status.label),
      h("span", { class: "count" }, tasks.length),
      HINTS[status.key] ? h("span", { class: "hint" }, HINTS[status.key]) : null,
    ),
    list,
  );
  column.addEventListener("dragover", (e) => dragOver(e, column, status.key));
  column.addEventListener("dragleave", (e) => {
    if (!column.contains(e.relatedTarget)) clearDropState(column);
  });
  column.addEventListener("drop", (e) => drop(e, column, status.key));
  return column;
}

function emptyText(status) {
  return {
    proposed: "Agent ideas land here",
    ready: "Queue is empty",
    in_progress: "No agent is working",
    blocked: "Nothing blocked",
    review: "Nothing to review",
    done: "Nothing finished yet",
    cancelled: "Nothing cancelled",
  }[status] || "Empty";
}

// ------------------------------------------------------------- drag and drop

function dragStart(e, task, card) {
  state.draggingId = task.id;
  e.dataTransfer.effectAllowed = "move";
  e.dataTransfer.setData("text/plain", String(task.id));
  requestAnimationFrame(() => card.classList.add("dragging"));
}

function dragEnd(e, task, card) {
  card.classList.remove("dragging");
  state.draggingId = null;
  root.querySelectorAll(".column").forEach(clearDropState);
  if (pendingRender) {
    pendingRender = false;
    renderBoard();
  }
}

function clearDropState(column) {
  column.classList.remove("drop-target");
  if (dropLine && column.contains(dropLine)) dropLine.remove();
}

/** The card the dragged one would be inserted before, or null for the end. */
function cardAfterPointer(list, y) {
  const cards = [...list.querySelectorAll(".card:not(.dragging)")];
  return cards.find((card) => {
    const box = card.getBoundingClientRect();
    return y < box.top + box.height / 2;
  }) || null;
}

function dragOver(e, column, status) {
  if (state.draggingId === null) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = "move";
  root.querySelectorAll(".column.drop-target").forEach((c) => c !== column && clearDropState(c));
  column.classList.add("drop-target");
  if (!RANKED_STATUSES.has(status)) return;
  const list = column.querySelector(".column-list");
  const before = cardAfterPointer(list, e.clientY);
  dropLine = dropLine || h("div", { class: "drop-line" });
  if (before) list.insertBefore(dropLine, before);
  else list.append(dropLine);
}

async function drop(e, column, status) {
  e.preventDefault();
  const id = state.draggingId ?? Number(e.dataTransfer.getData("text/plain"));
  let above = null;
  let below = null;
  if (RANKED_STATUSES.has(status) && dropLine && column.contains(dropLine)) {
    const prev = dropLine.previousElementSibling;
    const next = dropLine.nextElementSibling;
    above = prev?.classList.contains("card") && !prev.classList.contains("dragging") ? Number(prev.dataset.id) : null;
    below = next?.classList.contains("card") && !next.classList.contains("dragging") ? Number(next.dataset.id) : null;
    if (prev?.classList.contains("dragging")) above = cardIdBefore(prev);
    if (next?.classList.contains("dragging")) below = cardIdAfter(next);
  }
  clearDropState(column);
  const task = state.data.tasks.find((t) => t.id === id);
  if (!task) return;
  try {
    if (status === "blocked" && task.status !== "blocked") {
      const reason = await ask({
        title: `Block ${task.key}`,
        message: "What is needed before this can continue? Agents read this.",
        placeholder: "e.g. Decide which door model to use",
        confirm: "Block",
        required: true,
      });
      if (reason === null) return;
      await api.move(id, "blocked", reason);
    } else if (status !== task.status || above !== null || below !== null) {
      const placed = await api.place(id, { status, above, below });
      if (placed.priority !== task.priority) toast(`${task.key} is now ${placed.priority} priority`);
    }
    await reload();
  } catch (error) {
    toast(error.message, "error");
    await reload();
  }
}

function cardIdBefore(el) {
  let node = el.previousElementSibling;
  while (node && !node.classList.contains("card")) node = node.previousElementSibling;
  return node ? Number(node.dataset.id) : null;
}

function cardIdAfter(el) {
  let node = el.nextElementSibling;
  while (node && !node.classList.contains("card")) node = node.nextElementSibling;
  return node ? Number(node.dataset.id) : null;
}
