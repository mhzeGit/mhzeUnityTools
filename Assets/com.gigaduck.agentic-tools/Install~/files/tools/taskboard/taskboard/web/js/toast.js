// Short messages in the bottom-right corner.

import { h } from "./util.js";

export function toast(message, kind = "info", ms = 4200) {
  const box = document.getElementById("toasts");
  const el = h("div", { class: `toast ${kind}` }, message);
  box.append(el);
  setTimeout(() => el.remove(), ms);
}
