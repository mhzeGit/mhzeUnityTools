// The create / edit task dialog. Resolves to the saved task, or null.

import { api } from "./api.js";
import { state } from "./state.js";
import { h, icon } from "./util.js";

function segmented(options, value, onChange) {
  const group = h("div", { class: "segmented", role: "group" });
  const buttons = options.map(([key, label]) =>
    h(
      "button",
      {
        type: "button",
        class: key,
        dataset: { key },
        "aria-pressed": String(key === value),
        onclick: () => {
          buttons.forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.key === key)));
          onChange(key);
        },
      },
      label,
    ),
  );
  group.append(...buttons);
  return group;
}

function field(label, control, hint = "") {
  return h("label", { class: "field" }, h("span", {}, label, hint ? h("small", {}, `  ${hint}`) : null), control);
}

function datalist(id, values) {
  return h("datalist", { id }, values.map((v) => h("option", { value: v })));
}

export function openEditor(task = null, defaults = {}) {
  const dialog = document.getElementById("editor");
  const editing = Boolean(task);
  const values = {
    priority: task?.priority || defaults.priority || "normal",
    status: defaults.status || "ready",
  };

  return new Promise((resolve) => {
    const title = h("input", { type: "text", class: "title-input", placeholder: "What needs doing?", value: task?.title || "", required: true });
    const description = h("textarea", { rows: 4, placeholder: "Context, steps, links. Agents read all of it.", value: task?.description || "" });
    const acceptance = h("textarea", { rows: 3, placeholder: "One per line — how anyone can tell it is done", value: (task?.acceptance || []).join("\n") });
    const project = h("input", {
      type: "text",
      list: "projects-list",
      placeholder: "Any repository",
      value: task ? task.project : defaults.project ?? state.filters.project ?? "",
    });
    const labels = h("input", { type: "text", placeholder: "ui, bug", value: (task?.labels || []).join(", ") });
    const needs = h("input", { type: "text", list: "resources-list", placeholder: "unity-editor", value: (task?.needs || []).join(", ") });
    const depends = h("input", { type: "text", placeholder: "T-3, T-7", value: (task?.depends_on || []).map((i) => `T-${i}`).join(", ") });
    const touches = h("textarea", { rows: 3, placeholder: "Assets/Scripts/Doors/\nsrc/GraphMat.Core/Nodes/Blur*.cs", value: (task?.touches || []).join("\n") });
    const error = h("span", { class: "hint" }, editing ? "Ctrl+Enter to save" : "Ctrl+Enter to create");
    const submit = h("button", { class: "primary", type: "submit" }, editing ? "Save" : "Create task");

    const priority = segmented(
      state.data.priorities.map((p) => [p, p[0].toUpperCase() + p.slice(1)]),
      values.priority,
      (v) => (values.priority = v),
    );
    const placement = editing
      ? null
      : field("Column", segmented([["ready", "Queue"], ["proposed", "Inbox"]], values.status, (v) => (values.status = v)));

    const advancedOpen = editing && (task.touches.length || task.needs.length || task.depends_on.length || task.labels.length);
    const form = h(
      "form",
      { method: "dialog" },
      h("div", { class: "head" }, editing ? `Edit ${task.key}` : "New task"),
      h(
        "div",
        { class: "body" },
        title,
        field("Description", description),
        field("Acceptance criteria", acceptance),
        h("div", { class: "grid" }, field("Project", project), field("Priority", priority)),
        placement,
        h(
          "details",
          { open: Boolean(advancedOpen) },
          h("summary", {}, icon("chevron"), "Scheduling: labels, files, resources, dependencies"),
          h(
            "div",
            { class: "grid" },
            field("Labels", labels),
            field("Needs", needs, "exclusive, e.g. unity-editor"),
            field("Depends on", depends, "done first"),
            h("span"),
          ),
          h("div", { style: { "margin-top": "14px" } }, field("Touches", touches, "files, folders or globs; agents never edit the same ones at once")),
        ),
        datalist("projects-list", state.data.projects),
        datalist("resources-list", Object.keys(state.data.resources || {})),
      ),
      h("div", { class: "foot" }, error, h("button", { class: "ghost", type: "button", onclick: () => dialog.close() }, "Cancel"), submit),
    );

    async function save() {
      const fields = {
        title: title.value,
        description: description.value,
        acceptance: acceptance.value,
        project: project.value.trim(),
        priority: values.priority,
        labels: labels.value,
        needs: needs.value,
        depends_on: depends.value,
        touches: touches.value,
      };
      submit.disabled = true;
      try {
        const saved = editing ? await api.update(task.id, fields) : await api.create({ ...fields, status: values.status });
        dialog.close();
        resolve(saved);
      } catch (e) {
        error.textContent = e.message;
        error.style.color = "var(--danger)";
        submit.disabled = false;
      }
    }

    form.addEventListener("submit", (e) => {
      e.preventDefault();
      save();
    });
    form.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
        e.preventDefault();
        save();
      }
    });
    dialog.replaceChildren(form);
    dialog.addEventListener("close", () => resolve(null), { once: true });
    dialog.showModal();
    title.focus();
  });
}
