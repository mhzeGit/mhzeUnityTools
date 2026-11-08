// Boot: wire the top bar, panels, keyboard shortcuts and the live stream.

import { api, connectStream } from "./api.js";
import { initBoard, renderBoard } from "./board.js";
import { closeDrawer, initDrawer, isDrawerOpen, openDrawer, renderDrawer } from "./drawer.js";
import { openEditor } from "./editor.js";
import { initFeed, isFeedOpen, pushEvent, renderFeed, toggleFeed } from "./feed.js";
import { counts, reload, state, subscribe, taskById } from "./state.js";
import { toast } from "./toast.js";
import { debounce, fill, h, hydrateIcons } from "./util.js";

const $ = (id) => document.getElementById(id);

function renderTopbar() {
  const data = state.data;
  $("board-name").textContent = data.board.name;
  document.title = data.board.name;

  const select = $("project-filter");
  const current = state.filters.project;
  const options = [h("option", { value: "" }, "All projects"), ...data.projects.map((p) => h("option", { value: p }, p))];
  select.replaceChildren(...options);
  select.value = data.projects.includes(current) ? current : "";

  const c = counts();
  fill(
    $("summary"),
    h("span", { title: "Tasks agents are working on" }, h("b", {}, c.running), "running"),
    h("span", { title: "Approved tasks waiting for an agent" }, h("b", {}, c.queued), "queued"),
    h("span", { class: c.needsYou ? "needs-you" : "", title: "Inbox + Blocked + Review" }, h("b", {}, c.needsYou), "need you"),
  );
}

function render() {
  if (!state.data) return;
  renderTopbar();
  renderBoard();
  renderDrawer();
}

function openTask(id) {
  if (!taskById(id)) {
    toast(`T-${id} is archived or deleted`);
    return;
  }
  openDrawer(Number(id));
  renderBoard();
}

async function newTask() {
  const saved = await openEditor(null);
  if (saved) {
    await reload();
    toast(`Created ${saved.key}`);
  }
}

async function editTask(task) {
  const saved = await openEditor(task);
  if (saved) await reload();
}

function setLive(on) {
  const live = $("live");
  live.classList.toggle("on", on);
  live.classList.toggle("off", !on);
  live.querySelector(".live-label").textContent = on ? "Live" : "Reconnecting";
}

function startStream() {
  const refresh = debounce(() => reload().catch(() => {}), 120);
  let wasOpen = false;
  connectStream(
    (event) => {
      if (event.task) state.flash.add(event.task);
      pushEvent(event);
      refresh();
    },
    (open) => {
      setLive(open);
      if (open && wasOpen) refresh(); // catch up on anything missed while offline
      wasOpen = wasOpen || open;
    },
  );
}

function bindTopbar() {
  const search = $("search");
  search.addEventListener("input", debounce(() => {
    state.filters.q = search.value.trim();
    render();
  }, 80));
  $("project-filter").addEventListener("change", (e) => {
    state.filters.project = e.target.value;
    localStore("project", e.target.value);
    render();
  });
  const cancelled = $("toggle-cancelled");
  cancelled.addEventListener("click", () => {
    state.filters.showCancelled = !state.filters.showCancelled;
    cancelled.setAttribute("aria-pressed", String(state.filters.showCancelled));
    render();
  });
  $("new-task").addEventListener("click", newTask);
  $("toggle-feed").addEventListener("click", () => toggleFeed());
  document.querySelector('[data-close="feed"]').addEventListener("click", () => toggleFeed(false));
}

function bindKeys() {
  document.addEventListener("keydown", (e) => {
    const typing = /^(INPUT|TEXTAREA|SELECT)$/.test(document.activeElement?.tagName) || document.querySelector("dialog[open]");
    if (e.key === "Escape" && !document.querySelector("dialog[open]")) {
      if (isFeedOpen()) toggleFeed(false);
      else if (isDrawerOpen()) {
        closeDrawer();
        renderBoard();
      }
      return;
    }
    if (typing || e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.key === "n" || e.key === "N") {
      e.preventDefault();
      newTask();
    } else if (e.key === "/") {
      e.preventDefault();
      $("search").focus();
    } else if (e.key === "a" || e.key === "A") {
      toggleFeed();
    } else if ((e.key === "e" || e.key === "E") && state.selectedId !== null) {
      const task = taskById(state.selectedId);
      if (task) editTask(task);
    }
  });
}

function localStore(key, value) {
  try {
    if (value === undefined) return localStorage.getItem(`taskboard.${key}`);
    localStorage.setItem(`taskboard.${key}`, value);
  } catch {
    return null;
  }
  return null;
}

async function boot() {
  hydrateIcons();
  state.filters.project = localStore("project") || "";
  initBoard($("board"), openTask);
  initDrawer($("drawer"), { onEdit: editTask, onClose: () => renderBoard() });
  initFeed($("feed"), openTask);
  bindTopbar();
  bindKeys();
  subscribe(render);
  try {
    await reload();
  } catch (error) {
    toast(`Cannot load the board: ${error.message}`, "error", 10000);
  }
  startStream();
  // Ages ("4m", "quiet 25m") move on even when nothing changes.
  setInterval(() => {
    render();
    if (isFeedOpen()) renderFeed();
  }, 30000);
  const params = new URLSearchParams(location.search);
  if (params.get("task")) openTask(Number(params.get("task")));
}

window.taskboard = { api, state, reload }; // handy in the browser console
boot();
