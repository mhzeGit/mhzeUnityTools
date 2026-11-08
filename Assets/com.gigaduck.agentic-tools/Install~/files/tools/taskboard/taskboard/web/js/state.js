// The single client-side state and the derived views every module reads.
// Modules never fetch on their own: they call reload() and re-render from
// state when notified through subscribe().

import { api } from "./api.js";

export const state = {
  data: null, // last /api/board response
  skewMs: 0, // server clock minus browser clock
  filters: { q: "", project: "", showCancelled: false },
  selectedId: null, // task open in the drawer
  draggingId: null,
  expanded: new Set(), // columns showing all cards (done)
  flash: new Set(), // task ids changed by the latest events
};

const listeners = new Set();

export function subscribe(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function notify() {
  for (const fn of listeners) fn(state);
}

export async function reload() {
  const data = await api.board();
  state.data = data;
  state.skewMs = Date.parse(data.now) - Date.now();
  notify();
  return data;
}

export function statusDef(key) {
  return state.data?.statuses.find((s) => s.key === key);
}

export function taskById(id) {
  return state.data?.tasks.find((t) => t.id === Number(id));
}

export function priorityIndex(priority) {
  const list = state.data?.priorities || [];
  const i = list.indexOf(priority);
  return i < 0 ? list.length : i;
}

function matchesFilters(task) {
  const { q, project } = state.filters;
  if (project && task.project && task.project !== project) return false;
  if (!q) return true;
  const hay = [task.key, task.title, task.description, task.project, task.assignee, ...task.labels, ...task.touches]
    .join(" ")
    .toLowerCase();
  return q
    .toLowerCase()
    .split(/\s+/)
    .every((word) => hay.includes(word));
}

/** Queue columns are ordered like the scheduler; the rest show recent first. */
export const RANKED_STATUSES = new Set(["proposed", "ready"]);

export function columnTasks(status) {
  const tasks = (state.data?.tasks || []).filter((t) => t.status === status && matchesFilters(t));
  if (RANKED_STATUSES.has(status)) {
    tasks.sort((a, b) => priorityIndex(a.priority) - priorityIndex(b.priority) || a.rank - b.rank || a.id - b.id);
  } else {
    tasks.sort((a, b) => (b.updated_at || "").localeCompare(a.updated_at || ""));
  }
  return tasks;
}

export function visibleStatuses() {
  return (state.data?.statuses || []).filter((s) => !s.hidden_by_default || state.filters.showCancelled);
}

export function counts() {
  const tasks = (state.data?.tasks || []).filter(matchesFilters);
  const by = (s) => tasks.filter((t) => t.status === s).length;
  return {
    running: by("in_progress"),
    queued: by("ready"),
    needsYou: by("proposed") + by("blocked") + by("review"),
  };
}
