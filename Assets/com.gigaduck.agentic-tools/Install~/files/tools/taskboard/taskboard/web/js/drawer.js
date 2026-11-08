// The task detail panel: everything about one task, quick actions and notes.

import { api } from "./api.js";
import { ask } from "./ask.js";
import { reload, state, statusDef, taskById } from "./state.js";
import { toast } from "./toast.js";
import { avatar, fill, fullTime, h, icon, linkify, shortAge, timeAgo } from "./util.js";

let panel = null;
let body = null;
let head = null;
let composer = null;
let noteInput = null;
let callbacks = { onEdit: () => {}, onClose: () => {} };

export function initDrawer(element, options) {
  panel = element;
  callbacks = { ...callbacks, ...options };
  head = h("div", { class: "panel-head" });
  body = h("div", { class: "drawer-body" });
  noteInput = h("textarea", { rows: 2, placeholder: "Add a note for the agents… (Ctrl+Enter)" });
  noteInput.addEventListener("keydown", (e) => {
    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      sendNote();
    }
  });
  composer = h(
    "div",
    { class: "composer" },
    noteInput,
    h("div", { class: "row" }, h("span", {}, "Agents see notes in `taskboard show`"), h("button", { class: "secondary", onclick: sendNote }, "Add note")),
  );
  panel.replaceChildren(head, body, composer);
}

export function openDrawer(id) {
  if (state.selectedId !== id) noteInput.value = "";
  state.selectedId = id;
  panel.classList.add("open");
  panel.setAttribute("aria-hidden", "false");
  body.scrollTop = 0;
  renderDrawer();
}

export function closeDrawer() {
  state.selectedId = null;
  panel.classList.remove("open");
  panel.setAttribute("aria-hidden", "true");
  callbacks.onClose();
}

export function isDrawerOpen() {
  return panel?.classList.contains("open");
}

async function run(action) {
  try {
    await action();
    await reload();
  } catch (error) {
    toast(error.message, "error");
  }
}

async function sendNote() {
  const text = noteInput.value.trim();
  if (!text || state.selectedId === null) return;
  await run(async () => {
    await api.note(state.selectedId, text);
    noteInput.value = "";
  });
}

function actionsFor(task) {
  const move = (status, note = "") => () => run(() => api.move(task.id, status, note));
  const moveAsking = (status, title, message, confirm, required = false) => async () => {
    const note = await ask({ title, message, confirm, required });
    if (note !== null) await run(() => api.move(task.id, status, note));
  };
  const cancel = moveAsking("cancelled", `Cancel ${task.key}?`, "Optional: why it was dropped.", "Cancel task");
  const block = moveAsking("blocked", `Block ${task.key}`, "What is needed before this can continue? Agents read this.", "Block", true);
  const btn = (label, cls, fn) => h("button", { class: cls, onclick: fn }, label);

  switch (task.status) {
    case "proposed":
      return [btn("Approve to Queue", "primary", move("ready", "Approved")), btn("Cancel", "danger", cancel)];
    case "ready":
      return [btn("Start myself", "secondary", move("in_progress")), btn("Back to Inbox", "ghost", move("proposed")), btn("Cancel", "danger", cancel)];
    case "in_progress":
      return [
        btn("Send to Review", "primary", move("review")),
        btn("Mark done", "secondary", move("done")),
        btn("Release to Queue", "ghost", moveAsking("ready", `Release ${task.key}`, "Optional note for the next agent.", "Release")),
        btn("Block", "ghost", block),
      ];
    case "blocked":
      return [
        btn("Unblock to Queue", "primary", moveAsking("ready", `Unblock ${task.key}`, "Your answer for the agent (optional).", "Unblock")),
        btn("Cancel", "danger", cancel),
      ];
    case "review":
      return [
        btn("Approve as Done", "primary", move("done", "Approved")),
        btn("Send back", "secondary", moveAsking("ready", `Send ${task.key} back`, "What needs another pass? The next agent reads this.", "Send back", true)),
        btn("Block", "ghost", block),
      ];
    default:
      return [btn("Reopen", "secondary", () => run(() => api.reopen(task.id)))];
  }
}

function statusPill(key) {
  const def = statusDef(key);
  return h("span", { class: "status-pill", style: { "--status": def?.color || "#888" } }, def?.label || key);
}

function prop(label, value) {
  return [h("dt", {}, label), h("dd", {}, value ?? h("span", { class: "muted" }, "—"))];
}

function prioritySelect(task) {
  const select = h(
    "select",
    { onchange: (e) => run(() => api.update(task.id, { priority: e.target.value })) },
    state.data.priorities.map((p) => h("option", { value: p, selected: p === task.priority }, p)),
  );
  return select;
}

function dependencyList(task) {
  if (!task.depends_on.length) return null;
  return task.depends_on.map((id) => {
    const dep = taskById(id);
    return h(
      "button",
      { class: `dep ${dep?.status === "done" ? "done" : ""}`, title: dep ? `${dep.title} (${statusDef(dep.status)?.label})` : "archived", onclick: () => dep && openDrawer(id) },
      `T-${id}`,
    );
  });
}

function leaseCallout(task) {
  if (!task.claim) return null;
  const age = (Date.now() + state.skewMs - Date.parse(task.claim.heartbeat_at)) / 1000;
  const lease = state.data.board.lease || { stale_minutes: 20, expire_minutes: 120 };
  const stale = age > lease.stale_minutes * 60;
  const left = lease.expire_minutes * 60 - age;
  return h(
    "div",
    { class: `callout lease ${stale ? "stale" : ""}` },
    h("strong", {}, stale ? `${task.claim.agent} has been quiet for ${shortAge(age)}` : `${task.claim.agent} is working on it`),
    `Claimed ${timeAgo(task.claim.claimed_at, state.skewMs)}, last activity ${shortAge(age)} ago. `,
    left > 0 ? `Returns to the Queue in ${shortAge(left)} without activity.` : "The lease has expired; it returns to the Queue on the next sync.",
  );
}

function timelineEntry(entry) {
  let line;
  if (entry.kind === "status") {
    const from = statusDef(entry.from);
    const to = statusDef(entry.to);
    line = [
      " moved ",
      h("span", { class: "mini-status", style: { "--status": from?.color } }, from?.label || entry.from),
      " → ",
      h("span", { class: "mini-status", style: { "--status": to?.color } }, to?.label || entry.to),
    ];
  } else if (entry.kind === "created") {
    const to = statusDef(entry.to);
    line = [" created it in ", h("span", { class: "mini-status", style: { "--status": to?.color } }, to?.label || entry.to)];
  } else if (entry.kind === "note") {
    line = [" noted"];
  } else {
    line = [" ", entry.text || entry.kind];
  }
  const text = entry.kind === "status" || entry.kind === "note" ? entry.text : "";
  return h(
    "li",
    {},
    avatar(entry.by),
    h(
      "div",
      { class: `entry ${entry.kind}` },
      h("span", { class: "by" }, entry.by),
      line,
      h("time", { title: fullTime(entry.at) }, timeAgo(entry.at, state.skewMs)),
      text ? h("div", { class: "text" }, linkify(text)) : null,
    ),
  );
}

function linkItems(task) {
  return task.links.map((link) => {
    if (link.kind === "virtual_commit") return h("span", { class: "tag vc", title: link.repo }, `Virtual commit #${link.value}`);
    if (link.kind === "commit") return h("span", { class: "tag commit", title: `${link.value}\n${link.repo}` }, `commit ${link.value.slice(0, 9)}`);
    if (link.kind === "url") return h("a", { class: "tag", href: link.value, target: "_blank", rel: "noreferrer" }, link.label || link.value);
    return h("span", { class: "tag", title: link.value }, link.label || link.value);
  });
}

export function renderDrawer() {
  if (!panel || state.selectedId === null) return;
  const task = taskById(state.selectedId);
  if (!task) {
    closeDrawer();
    return;
  }
  const scroll = body.scrollTop;
  const def = statusDef(task.status);

  fill(
    head,
    statusPill(task.status),
    h("span", { class: "card-key" }, task.key),
    h("span", { style: { flex: "1" } }),
    h("button", { class: "icon-button", title: "Edit (E)", onclick: () => callbacks.onEdit(task) }, icon("edit")),
    h("button", { class: "icon-button", title: "Close (Esc)", onclick: closeDrawer }, icon("x")),
  );

  const props = h(
    "dl",
    { class: "props" },
    prop("Priority", prioritySelect(task)),
    prop("Project", task.project || null),
    prop("Assignee", task.assignee ? [avatar(task.assignee), task.assignee] : null),
    prop("Labels", task.labels.length ? task.labels.map((l) => h("span", { class: "tag" }, l)) : null),
    prop("Needs", task.needs.length ? task.needs.map((n) => h("span", { class: "tag resource" }, n)) : null),
    prop("Depends on", dependencyList(task)),
    prop("Touches", task.touches.length ? task.touches.map((p) => h("span", { class: "path" }, p)) : null),
    prop("Created", [task.created_by, h("span", { class: "muted", title: fullTime(task.created_at) }, `· ${timeAgo(task.created_at, state.skewMs)}`)]),
  );

  const sections = [];
  if (task.description) sections.push(section("Description", h("div", { class: "prose" }, linkify(task.description))));
  if (task.acceptance.length) {
    sections.push(
      section(
        "Acceptance criteria",
        h("ul", { class: `checklist ${task.status === "done" ? "met" : ""}` }, task.acceptance.map((a) => h("li", {}, a))),
        task.acceptance.length,
      ),
    );
  }
  if (task.result) sections.push(section("Result", h("div", { class: "prose" }, linkify(task.result))));
  if (task.links.length) sections.push(section("Links", h("div", { class: "link-list" }, linkItems(task))));
  sections.push(section("Activity", h("ol", { class: "timeline" }, task.activity.map(timelineEntry)), task.activity.length));

  fill(
    body,
    h("h1", { class: "drawer-title" }, task.title),
    h("div", { class: "drawer-sub" }, def?.hint || ""),
    h("div", { class: "actions" }, actionsFor(task)),
    task.status === "blocked" && task.blocked_reason ? h("div", { class: "callout blocked" }, h("strong", {}, "Blocked"), task.blocked_reason) : null,
    task.status === "ready" && task.blockers.length
      ? h("div", { class: "callout waiting" }, h("strong", {}, "Waiting — agents will skip it for now"), h("ul", {}, task.blockers.map((b) => h("li", {}, b))))
      : null,
    leaseCallout(task),
    h("div", { class: "section" }, props),
    sections,
  );
  body.scrollTop = scroll;
}

function section(title, content, count = null) {
  return h("div", { class: "section" }, h("h3", {}, title, count !== null ? h("span", { class: "count" }, count) : null), content);
}
