import { motion, useScroll, useTransform } from "motion/react";
import { useRef } from "react";
import { Sparkles, ArrowUpRight, ChevronRight, AudioWaveform, Sun, Moon } from "lucide-react";
import SonicWaveformCanvas from "./SonicWaveformCanvas";
import ParticleField from "./ParticleField";
import { Magnetic } from "./motion-primitives";

type Theme = "light" | "dark";

const EASE = [0.21, 0.65, 0.36, 1] as const;

function HeroBadge() {
  return (
    <motion.div
      className="hero-badge"
      initial={{ opacity: 0, y: 20, scale: 0.94 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      transition={{ duration: 0.7, ease: EASE }}
    >
      <motion.span
        animate={{ rotate: [0, 14, -8, 0] }}
        transition={{ duration: 2.4, repeat: Infinity, repeatDelay: 3.2, ease: "easeInOut" }}
        style={{ display: "inline-flex" }}
      >
        <Sparkles style={{ width: 16, height: 16, color: "rgba(120,220,255,0.9)" }} />
      </motion.span>
      <span>Local-first voice dictation</span>
    </motion.div>
  );
}

function Navbar({ theme, onToggle }: { theme: Theme; onToggle: () => void }) {
  return (
    <motion.nav
      className="hero-nav"
      initial={{ y: -26, opacity: 0 }}
      animate={{ y: 0, opacity: 1 }}
      transition={{ duration: 0.7, ease: EASE }}
    >
      <a className="hero-nav-logo" href="#top">
        <AudioWaveform style={{ width: 20, height: 20 }} strokeWidth={2.2} />
        Echo
      </a>
      <ul className="hero-nav-menu">
        <li><a href="#features">Features</a></li>
        <li>
          <a href="#use-cases">
            Use cases <ChevronRight style={{ width: 16, height: 16 }} />
          </a>
        </li>
        <li><a href="#faq">FAQ</a></li>
      </ul>
      <div className="hero-nav-cta">
        <button className="theme-toggle" onClick={onToggle} aria-label="Toggle theme">
          {theme === "dark" ? (
            <Sun style={{ width: 16, height: 16 }} />
          ) : (
            <Moon style={{ width: 16, height: 16 }} />
          )}
        </button>
        <Magnetic strength={0.3}>
          <motion.a
            className="btn btn-navy"
            href="#download"
            whileHover={{ scale: 1.04 }}
            whileTap={{ scale: 0.96 }}
            transition={{ type: "spring", stiffness: 400, damping: 17 }}
          >
            <span className="btn-icon">
              <ArrowUpRight style={{ width: 16, height: 16 }} />
            </span>
            <span>Download free</span>
          </motion.a>
        </Magnetic>
      </div>
    </motion.nav>
  );
}

/* Headline with staggered word rise + hand-drawn style animated underline */
function Headline() {
  const words = ["Don't", "type,", "just", "speak."];
  return (
    <h1>
      {words.map((word, i) => (
        <span
          key={word}
          style={{ display: "inline-block", overflow: "hidden", verticalAlign: "bottom" }}
        >
          <motion.span
            style={{ display: "inline-block", willChange: "transform" }}
            initial={{ y: "112%" }}
            animate={{ y: 0 }}
            transition={{ duration: 0.85, delay: 0.15 + i * 0.09, ease: EASE }}
          >
            {word === "speak." ? (
              <span className="hero-accent-word">
                speak.
                <motion.svg
                  className="hero-accent-underline"
                  viewBox="0 0 220 22"
                  fill="none"
                  preserveAspectRatio="none"
                  aria-hidden
                >
                  <motion.path
                    d="M4 15 C 60 6, 150 4, 216 12"
                    stroke="var(--accent)"
                    strokeWidth="5"
                    strokeLinecap="round"
                    initial={{ pathLength: 0, opacity: 0 }}
                    animate={{ pathLength: 1, opacity: 0.9 }}
                    transition={{ duration: 0.9, delay: 0.85, ease: "easeInOut" }}
                  />
                </motion.svg>
              </span>
            ) : (
              word
            )}
            {i < words.length - 1 ? "\u00A0" : ""}
          </motion.span>
        </span>
      ))}
    </h1>
  );
}

function BottomLeftCard({ progress }: { progress: any }) {
  const y = useTransform(progress, [0, 1], [0, -70]);
  return (
    <motion.div
      className="hero-stat-card"
      style={{ y }}
      initial={{ x: -24, opacity: 0 }}
      animate={{ x: 0, opacity: 1 }}
      transition={{ duration: 0.8, delay: 0.55, ease: EASE }}
    >
      <div>
        <div className="hero-stat-num">150 WPM</div>
        <div className="hero-stat-label">Avg dictation speed</div>
      </div>
      <motion.a
        className="btn btn-white"
        style={{ padding: "8px 14px", fontSize: "0.85rem", alignSelf: "flex-start" }}
        href="#use-cases"
        whileHover={{ scale: 1.04 }}
        whileTap={{ scale: 0.96 }}
        transition={{ type: "spring", stiffness: 400, damping: 17 }}
      >
        <span className="btn-icon">
          <ArrowUpRight style={{ width: 14, height: 14 }} />
        </span>
        <span>See use cases</span>
      </motion.a>
    </motion.div>
  );
}

function BottomRightCorner({ cornerFill, progress }: { cornerFill: string; progress: any }) {
  const y = useTransform(progress, [0, 1], [0, 50]);
  return (
    <motion.div
      className="hero-corner"
      style={{ y }}
      initial={{ y: 24, opacity: 0 }}
      animate={{ y: 0, opacity: 1 }}
      transition={{ duration: 0.8, delay: 0.7, ease: EASE }}
    >
      <div className="hero-corner-mask top">
        <svg width="100%" height="100%" viewBox="0 0 56 56" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M56 56V0C56 30.9279 30.9279 56 0 56H56Z" fill={cornerFill} />
        </svg>
      </div>
      <div className="hero-corner-mask left">
        <svg width="100%" height="100%" viewBox="0 0 56 56" fill="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M56 56H0C30.9279 56 56 30.9279 56 0V56Z" fill={cornerFill} />
        </svg>
      </div>
      <motion.div
        className="hero-corner-icon"
        whileHover={{ rotate: 45 }}
        transition={{ type: "spring", stiffness: 300, damping: 15 }}
      >
        <ArrowUpRight style={{ width: 20, height: 20 }} />
      </motion.div>
      <div>
        <div className="hero-corner-title">Download for Windows</div>
        <a className="hero-corner-link" href="#download">
          <span>Get started free</span>
          <ChevronRight style={{ width: 14, height: 14 }} />
        </a>
      </div>
    </motion.div>
  );
}

export default function Hero({ theme, onToggle }: { theme: Theme; onToggle: () => void }) {
  const cornerFill = theme === "dark" ? "#05070f" : "#eef0f2";
  const panelRef = useRef<HTMLElement>(null);
  const { scrollYProgress } = useScroll({
    target: panelRef,
    offset: ["start start", "end start"],
  });
  const textY = useTransform(scrollYProgress, [0, 1], [0, 90]);
  const textOpacity = useTransform(scrollYProgress, [0, 0.75], [1, 0]);

  return (
    <div className="hero-wrap" id="top">
      <section className="hero-panel" ref={panelRef}>
        <SonicWaveformCanvas theme={theme} />
        <ParticleField theme={theme} />
        <div className="hero-vignette" />
        <div className="hero-content">
          <Navbar theme={theme} onToggle={onToggle} />
          <motion.div className="hero-text" style={{ y: textY, opacity: textOpacity }}>
            <HeroBadge />
            <Headline />
            <motion.p
              initial={{ opacity: 0, y: 14 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ duration: 0.8, delay: 0.7, ease: EASE }}
            >
              Echo is the voice layer for every app on your desktop. One hotkey turns
              messy speech into clean, ready-to-send text — anywhere you can type.
            </motion.p>
          </motion.div>
          <BottomLeftCard progress={scrollYProgress} />
          <BottomRightCorner cornerFill={cornerFill} progress={scrollYProgress} />
        </div>
      </section>
    </div>
  );
}
