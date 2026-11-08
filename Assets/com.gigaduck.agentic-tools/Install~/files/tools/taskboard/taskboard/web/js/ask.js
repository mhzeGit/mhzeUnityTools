// A small modal that asks for one line of text (block reason, rework note...).
// Resolves to the text, or null when cancelled.

import { h } from "./util.js";

export function ask({ title, message = "", placeholder = "", confirm = "OK", required = false, value = "" }) {
  const dialog = document.getElementById("ask");
  return new Promise((resolve) => {
    const input = h("textarea", { rows: 3, placeholder, value });
    const submit = h("button", { class: "primary", type: "submit" }, confirm);
    const form = h(
      "form",
      { method: "dialog" },
      h("h3", {}, title),
      message ? h("p", {}, message) : null,
      input,
      h("div", { class: "foot" }, h("button", { class: "ghost", type: "button", onclick: () => dialog.close("cancel") }, "Cancel"), submit),
    );
    const sync = () => (submit.disabled = required && !input.value.trim());
    input.addEventListener("input", sync);
    input.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && (e.ctrlKey || e.metaKey || !e.shiftKey)) {
        e.preventDefault();
        if (!submit.disabled) dialog.close("ok");
      }
    });
    form.addEventListener("submit", (e) => {
      e.preventDefault();
      if (!submit.disabled) dialog.close("ok");
    });
    sync();
    dialog.replaceChildren(form);
    dialog.returnValue = "";
    dialog.addEventListener(
      "close",
      () => resolve(dialog.returnValue === "ok" ? input.value.trim() : null),
      { once: true },
    );
    dialog.showModal();
    input.focus();
  });
}
