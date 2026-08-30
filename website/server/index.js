// Echo marketing site — backend
// Serves the built React frontend + exposes a small JSON API:
//   GET  /api/health        health probe
//   GET  /api/app           Echo desktop app metadata (version, download, features)
//   GET  /api/waitlist      waitlist count (for the landing page counter)
//   POST /api/waitlist      join the waitlist ({ email })
//
// Persistence: writes to server/data/waitlist.json (mirrors the desktop app's
// %LOCALAPPDATA%\Echo\*.json pattern). Atomic write via temp + rename.

import express from "express";
import cors from "cors";
import path from "path";
import fs from "fs/promises";
import { existsSync, mkdirSync, createWriteStream } from "fs";
import { fileURLToPath } from "url";
import { rateLimit } from "express-rate-limit";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const DATA_DIR = path.join(__dirname, "data");
const WAITLIST_FILE = path.join(DATA_DIR, "waitlist.json");

// ---- Bootstrap data dir ----
if (!existsSync(DATA_DIR)) mkdirSync(DATA_DIR, { recursive: true });
if (!existsSync(WAITLIST_FILE)) {
  await fs.writeFile(WAITLIST_FILE, JSON.stringify({ entries: [] }, null, 2), "utf8");
}

const APP_META = Object.freeze({
  name: "Echo",
  model: "TC-80",
  tagline: "Type with your voice.",
  description:
    "On-device speech-to-text for Windows. Hold a key, talk, watch the words land. Zero cloud, zero telemetry.",
  version: "1.0.0",
  platform: "windows",
  architecture: ["x64"],
  minWindowsVersion: "10.0.19041",
  download: {
    // Served from /downloads/Echo-Setup.exe once published beside the dist.
    // Frontend can use this href for the CTA button.
    url: "/downloads/Echo-Setup.exe",
    sizeBytes: null,
    sha256: null,
  },
  features: [
    { id: "offline",     title: "Fully on-device",      body: "Whisper neural engine runs locally. No audio ever leaves your machine." },
    { id: "everywhere",  title: "Works in every app",  body: "Global push-to-talk injects into any focused text field." },
    { id: "dictionary",  title: "Personal dictionary", body: "Two-pass cleanup with custom terms, corrections, and glued-word matching." },
    { id: "tape-history",title: "Tape history",        body: "Every dictation logged locally with corrections, durations, and re-inject." },
    { id: "models",      title: "Bring your own model",body: "Drop in any Whisper ggml model — tiny/base/small/medium/large-v3." },
    { id: "privacy",     title: "Zero telemetry",      body: "No analytics, no crash reports, no network calls. Verify with Wireshark." },
  ],
  hotkey: { default: "RightCtrl hold-to-talk" },
});

const app = express();
const PORT = Number(process.env.PORT) || 3001;
const NODE_ENV = process.env.NODE_ENV || "development";

// ---- Middleware ----
app.disable("x-powered-by");
app.use(express.json({ limit: "8kb" }));

if (NODE_ENV !== "production") {
  // Vite dev server runs on 5173; allow it to call us directly.
  app.use(cors({ origin: ["http://localhost:5173", "http://127.0.0.1:5173"], credentials: false }));
}

// Minimal request log. Single line per request.
app.use((req, res, next) => {
  const t0 = process.hrtime.bigint();
  res.on("finish", () => {
    const ms = Number(process.hrtime.bigint() - t0) / 1e6;
    process.stdout.write(`[${new Date().toISOString()}] ${req.method} ${req.originalUrl} -> ${res.statusCode} (${ms.toFixed(1)}ms)\n`);
  });
  next();
});

// ---- Rate limit (waitlist only) ----
const waitlistLimiter = rateLimit({
  windowMs: 60 * 1000,   // 1 min
  max: 5,                // 5 attempts / min / IP
  standardHeaders: true,
  legacyHeaders: false,
  message: { error: "Too many requests. Try again in a minute." },
});

// ---- Helpers ----
const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]{2,}$/;
const SAFE_EMAIL_RE = /^[A-Za-z0-9._%+\-@]+$/; // extra guard against header injection

function isValidEmail(value) {
  if (typeof value !== "string") return false;
  const trimmed = value.trim();
  if (trimmed.length < 5 || trimmed.length > 254) return false;
  if (!EMAIL_RE.test(trimmed)) return false;
  if (!SAFE_EMAIL_RE.test(trimmed)) return false;
  return true;
}

async function readWaitlist() {
  try {
    const raw = await fs.readFile(WAITLIST_FILE, "utf8");
    const parsed = JSON.parse(raw);
    if (!parsed || !Array.isArray(parsed.entries)) return { entries: [] };
    return parsed;
  } catch (err) {
    console.error(`[waitlist] read failed: ${err.message}`);
    return { entries: [] };
  }
}

async function writeWaitlist(data) {
  const tmp = WAITLIST_FILE + ".tmp";
  await fs.writeFile(tmp, JSON.stringify(data, null, 2), "utf8");
  await fs.rename(tmp, WAITLIST_FILE);
}

// ---- API routes ----

// Liveness probe.
app.get("/api/health", (_req, res) => {
  res.json({
    ok: true,
    service: "echo-site",
    version: APP_META.version,
    timestamp: new Date().toISOString(),
    uptime: process.uptime(),
  });
});

// Public app metadata — frontend reads this so the landing page stays in sync
// with whatever the current desktop build is.
app.get("/api/app", (_req, res) => {
  res.json(APP_META);
});

// Waitlist count — used by the landing page counter.
app.get("/api/waitlist", async (_req, res) => {
  const data = await readWaitlist();
  res.json({ count: data.entries.length });
});

// Join waitlist.
app.post("/api/waitlist", waitlistLimiter, async (req, res) => {
  const email = req.body?.email;
  if (!isValidEmail(email)) {
    return res.status(400).json({ error: "Valid email required" });
  }
  const normalized = email.trim().toLowerCase();

  const data = await readWaitlist();
  if (data.entries.some((e) => e.email === normalized)) {
    return res.status(200).json({ ok: true, message: "You're already on the list." });
  }

  data.entries.push({
    email: normalized,
    createdAt: new Date().toISOString(),
    source: req.get("referer") || "direct",
    userAgent: req.get("user-agent")?.slice(0, 200) || "",
  });

  try {
    await writeWaitlist(data);
  } catch (err) {
    console.error(`[waitlist] write failed: ${err.message}`);
    return res.status(500).json({ error: "Could not save. Try again." });
  }

  res.status(201).json({ ok: true, message: "Thanks — we'll be in touch." });
});

// ---- Frontend (production) ----
const distPath = path.join(__dirname, "..", "frontend", "dist");

// Downloadable assets (installer etc.). Anything placed under public/downloads/
// is served with Content-Disposition: attachment so browsers trigger a save.
const downloadsPath = path.join(__dirname, "public", "downloads");
if (existsSync(downloadsPath)) {
  app.use(
    "/downloads",
    express.static(downloadsPath, {
      index: false,
      maxAge: "1h",
      setHeaders(res, filePath) {
        res.setHeader("Content-Disposition", `attachment; filename="${path.basename(filePath)}"`);
        res.setHeader("X-Content-Type-Options", "nosniff");
      },
    })
  );
}

app.use(express.static(distPath, { index: false, maxAge: "1h" }));
app.get(/^\/(?!api\/).*/, (_req, res, next) => {
  const indexFile = path.join(distPath, "index.html");
  if (!existsSync(indexFile)) return next();
  res.sendFile(indexFile);
});

// ---- Error handler (last) ----
// eslint-disable-next-line no-unused-vars
app.use((err, _req, res, _next) => {
  console.error(`[error] ${err.stack || err.message}`);
  if (err.type === "entity.parse.failed") {
    return res.status(400).json({ error: "Invalid JSON body" });
  }
  res.status(500).json({ error: "Internal server error" });
});

// ---- Start + graceful shutdown ----
const server = app.listen(PORT, () => {
  console.log(`Echo site listening on http://localhost:${PORT} (${NODE_ENV})`);
});

function shutdown(signal) {
  console.log(`\n[${signal}] shutting down…`);
  server.close(() => process.exit(0));
  // hard-exit after 5s if connections hang
  setTimeout(() => process.exit(1), 5000).unref();
}
process.on("SIGINT",  () => shutdown("SIGINT"));
process.on("SIGTERM", () => shutdown("SIGTERM"));
