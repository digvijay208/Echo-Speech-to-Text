import { Link } from "react-router-dom";
import { Monitor, Smartphone, ArrowLeft } from "lucide-react";
import { Reveal } from "./motion-primitives";
import { WINDOWS_INSTALLER_URL, ALL_RELEASES_URL, WINDOWS_MIN_LABEL } from "../download";

/* /downloads — picks the platform. Mirrors the typeless layout:
   nav + heading + 2 cards (Desktop / Mobile) + footer. */

const BTN_SPRING = { type: "spring" as const, stiffness: 400, damping: 17 };

function AppleIcon() {
  return (
    <svg viewBox="0 0 24 24" width={20} height={20} fill="currentColor" aria-hidden>
      <path d="M16.365 1.43c0 1.14-.456 2.247-1.193 3.029-.789.842-2.05 1.5-3.107 1.41-.123-1.112.418-2.27 1.176-3.04.83-.86 2.227-1.503 3.124-1.4Zm3.61 16.214c-.59 1.36-.876 1.97-1.633 3.166-1.064 1.673-2.563 3.756-4.42 3.772-1.65.014-2.075-1.072-4.314-1.06-2.24.012-2.71 1.078-4.36 1.064-1.857-.016-3.273-1.892-4.337-3.566C-1.43 16.94-1.81 11.27 1.18 8.36c2.108-2.05 4.327-2.42 5.808-2.42 1.502 0 3.06.918 4.02.918.918 0 2.764-1.137 4.66-.97.795.033 3.03.32 4.466 2.424-.117.072-2.667 1.553-2.638 4.633.033 3.677 3.232 4.9 3.479 4.998Z" />
    </svg>
  );
}

function WindowsIcon() {
  return (
    <svg viewBox="0 0 24 24" width={20} height={20} fill="currentColor" aria-hidden>
      <path d="M3 5.4 11 4.3v7.3H3V5.4Zm0 7.7h8v7.3l-8-1.1v-6.2Zm9-9L21 3v8.6h-9V4.1Zm0 9h9V21l-9-1.2v-6.7Z" />
    </svg>
  );
}

function PlayStoreIcon() {
  return (
    <svg viewBox="0 0 24 24" width={20} height={20} fill="currentColor" aria-hidden>
      <path d="m13.64 12-3.04-3.04 7.6-4.36c.4-.23.86.18.66.6L13.64 12Zm-3.04 3.04 7.6 4.36c.4.23.86-.18.66-.6L13.64 12l-3.04 3.04ZM3.6 5.6c-.4.23-.4.83 0 1.06L7.6 9.1l3.04-3.04L3.6 5.6Zm0 12.8 7.04-3.46 3.04 3.04-7.6 4.36c-.4.23-.86-.18-.66-.6L3.6 18.4Z" />
    </svg>
  );
}

export default function Downloads() {
  return (
    <main className="downloads-page">
      <div className="container">
        <Reveal>
          <Link to="/" className="downloads-back" aria-label="Back to home">
            <ArrowLeft width={16} height={16} strokeWidth={2.2} />
            <span>Back to Echo</span>
          </Link>
        </Reveal>

        <Reveal>
          <h1 className="downloads-h1">Pick your download</h1>
        </Reveal>

        <div className="downloads-grid">
          <Reveal>
            <article className="downloads-card">
              <div className="downloads-card-head">
                <span className="downloads-card-icon" aria-hidden>
                  <Monitor width={22} height={22} strokeWidth={1.8} />
                </span>
                <h2 className="downloads-card-h2">Desktop</h2>
              </div>
              <p className="downloads-card-sub">
                Smart voice dictation that turns speech into clear, polished writing in every app.
              </p>
              <div className="downloads-card-actions">
                <a className="btn btn-primary downloads-btn" href={WINDOWS_INSTALLER_URL}>
                  <WindowsIcon />
                  <span>Download for Windows</span>
                </a>
                <p className="downloads-note">{WINDOWS_MIN_LABEL} · free</p>
                <span className="btn btn-primary downloads-btn downloads-btn-disabled" aria-disabled="true" title="Echo for macOS is not available yet">
                  <AppleIcon />
                  <span>Download for macOS</span>
                  <span className="downloads-soon">Soon</span>
                </span>
                <a className="downloads-note downloads-note-link" href={ALL_RELEASES_URL} target="_blank" rel="noreferrer">
                  All releases &amp; changelog →
                </a>
              </div>
            </article>
          </Reveal>

          <Reveal delay={0.08}>
            <article className="downloads-card">
              <div className="downloads-card-head">
                <span className="downloads-card-icon" aria-hidden>
                  <Smartphone width={22} height={22} strokeWidth={1.8} />
                </span>
                <h2 className="downloads-card-h2">Mobile</h2>
              </div>
              <p className="downloads-card-sub">
                AI voice keyboard for your phone. 6× faster than typing.
              </p>
              <div className="downloads-card-actions">
                <span className="btn btn-primary downloads-btn downloads-btn-disabled" aria-disabled="true" title="Echo for iOS is not available yet">
                  <AppleIcon />
                  <span>Download on the App Store</span>
                  <span className="downloads-soon">Soon</span>
                </span>
                <span className="btn btn-primary downloads-btn downloads-btn-disabled" aria-disabled="true" title="Echo for Android is not available yet">
                  <PlayStoreIcon />
                  <span>Get it on Google Play</span>
                  <span className="downloads-soon">Soon</span>
                </span>
              </div>
            </article>
          </Reveal>
        </div>
      </div>
    </main>
  );
}
