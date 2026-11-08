// HTTP API client and the live event stream. Mirrors the routes in server.py.

async function request(method, path, body) {
  const init = { method, headers: {} };
  if (body !== undefined) {
    init.headers["Content-Type"] = "application/json";
    init.body = JSON.stringify(body);
  }
  const response = await fetch(path, init);
  let data = null;
  try {
    data = await response.json();
  } catch {
    data = null;
  }
  if (!response.ok) {
    const error = new Error((data && data.error) || `${response.status} ${response.statusText}`);
    error.status = response.status;
    throw error;
  }
  return data;
}

export const api = {
  board: () => request("GET", "/api/board"),
  task: (id) => request("GET", `/api/tasks/${id}`),
  events: (limit = 80) => request("GET", `/api/events?limit=${limit}`),
  stats: () => request("GET", "/api/stats"),
  create: (fields) => request("POST", "/api/tasks", fields),
  update: (id, fields) => request("PATCH", `/api/tasks/${id}`, fields),
  move: (id, status, note = "") => request("POST", `/api/tasks/${id}/move`, { status, note }),
  place: (id, placement) => request("POST", `/api/tasks/${id}/place`, placement),
  note: (id, text) => request("POST", `/api/tasks/${id}/notes`, { text }),
  reopen: (id, note = "") => request("POST", `/api/tasks/${id}/reopen`, { note }),
  sync: () => request("POST", "/api/sync", {}),
};

/**
 * Subscribe to /api/stream. Calls onEvent(event) per board change and
 * onState(true|false) when the connection opens or drops. EventSource
 * reconnects on its own; after a reconnect the caller should reload.
 */
export function connectStream(onEvent, onState) {
  const source = new EventSource("/api/stream");
  source.addEventListener("open", () => onState(true));
  source.addEventListener("error", () => onState(false));
  source.addEventListener("change", (message) => {
    try {
      onEvent(JSON.parse(message.data));
    } catch {
      /* ignore malformed lines */
    }
  });
  return source;
}
