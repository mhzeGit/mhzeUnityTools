// One task card on the board.

import { state } from "./state.js";
import { avatar, h, icon, shortAge, timeAgo } from "./util.js";

function linkTags(task) {
  return task.links.map((link) => {
    if (link.kind === "virtual_commit") return h("span", { class: "tag vc", title: "Virtual commit" }, `VC #${link.value}`);
    if (link.kind === "commit") return h("span", { class: "tag commit", title: link.value }, link.value.slice(0, 7));
    return null;
  });
}

function leaseAgeSeconds(task) {
  if (!task.claim) return null;
  return (Date.now() + state.skewMs - Date.parse(task.claim.heartbeat_at)) / 1000;
}

function isStale(task) {
  const age = leaseAgeSeconds(task);
  const staleMinutes = state.data?.board.lease?.stale_minutes ?? 20;
  return age !== null && age > staleMinutes * 60;
}

function who(task) {
  if (task.status === "in_progress" && task.assignee) {
    const live = task.claim ? `live${isStale(task) ? " stale" : ""}` : "";
    return h("span", { class: "who" }, avatar(task.assignee, live), h("span", { class: "name" }, task.assignee));
  }
  if (["review", "done", "blocked"].includes(task.status) && task.assignee) {
    return h("span", { class: "who" }, avatar(task.assignee), h("span", { class: "name" }, task.assignee));
  }
  if (task.status === "proposed" && task.created_by) {
    return h("span", { class: "who" }, avatar(task.created_by), h("span", { class: "name" }, `from ${task.created_by}`));
  }
  return h("span", { class: "who" });
}

function meta(task) {
  const items = [];
  if (task.status === "in_progress" && task.claim) {
    const age = leaseAgeSeconds(task);
    const stale = isStale(task);
    items.push(
      h(
        "span",
        { class: stale ? "stale" : "", title: stale ? "No activity from the agent for a while" : "Last agent activity" },
        icon(stale ? "alert" : "clock"),
        stale ? `quiet ${shortAge(age)}` : shortAge(age),
      ),
    );
  } else if (["done", "review", "cancelled"].includes(task.status)) {
    items.push(h("span", { title: "Last change" }, timeAgo(task.updated_at, state.skewMs)));
  }
  if (task.depends_on.length) items.push(h("span", { title: "Dependencies" }, icon("deps"), task.depends_on.length));
  if (task.touches.length) items.push(h("span", { title: task.touches.join("\n") }, icon("folder"), task.touches.length));
  const notes = task.activity.filter((a) => a.kind === "note").length;
  if (notes) items.push(h("span", { title: "Notes" }, icon("note"), notes));
  return h("span", { class: "meta" }, items);
}

function callout(task) {
  if (task.status === "blocked" && task.blocked_reason) {
    return h("div", { class: "card-note blocked" }, icon("ban"), h("span", {}, task.blocked_reason));
  }
  if (task.status === "ready" && task.blockers.length) {
    return h("div", { class: "card-note waiting", title: task.blockers.join("\n") }, icon("hourglass"), h("span", {}, task.blockers[0]));
  }
  if (task.status === "review" && task.result) {
    return h("div", { class: "card-note result" }, icon("check"), h("span", {}, task.result));
  }
  return null;
}

export function renderCard(task, { onOpen, onDragStart, onDragEnd }) {
  const prio = ["urgent", "high", "low"].includes(task.priority)
    ? h("span", { class: `prio ${task.priority}` }, task.priority)
    : null;
  const tags = [
    ...task.labels.map((label) => h("span", { class: "tag" }, label)),
    ...task.needs.map((need) => h("span", { class: "tag resource", title: "Exclusive resource" }, need)),
    ...linkTags(task),
  ].filter(Boolean);

  const classes = ["card", `s-${task.status}`, `p-${task.priority}`];
  if (state.selectedId === task.id) classes.push("selected");
  if (state.flash.has(task.id)) classes.push("flash");

  const card = h(
    "article",
    {
      class: classes.join(" "),
      tabindex: "0",
      draggable: "true",
      dataset: { id: task.id },
      onclick: () => onOpen(task.id),
      onkeydown: (e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onOpen(task.id);
        }
      },
      ondragstart: (e) => onDragStart(e, task, card),
      ondragend: (e) => onDragEnd(e, task, card),
    },
    h("div", { class: "card-top" }, h("span", { class: "card-key" }, task.key), task.project ? ["·", h("span", { class: "card-project" }, task.project)] : null, prio),
    h("div", { class: "card-title" }, task.title),
    tags.length ? h("div", { class: "tags" }, tags) : null,
    callout(task),
    h("div", { class: "card-foot" }, who(task), meta(task)),
  );
  return card;
}
