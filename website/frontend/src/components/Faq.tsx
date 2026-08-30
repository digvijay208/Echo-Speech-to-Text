import { useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Reveal } from "./motion-primitives";

const FAQS = [
  {
    q: "How much time will I actually save?",
    a: "Most people type around 40 words per minute but speak at 150. Users report saving 20+ hours a month on email, chat, docs, and code — the gains compound fastest if you write a lot every day.",
  },
  {
    q: "Does it work in my apps?",
    a: "Yes. Echo works at the OS level, so it types into any text field in any app — Gmail, Slack, VS Code, Cursor, Notion, browsers, and more. If you can click in it, you can dictate into it.",
  },
  {
    q: "Is my voice data private?",
    a: "Echo is local-first. Your audio is processed on your device and never uploaded. We retain zero data — no recordings, no transcripts, no exceptions.",
  },
  {
    q: "What makes Echo different from built-in dictation?",
    a: "Built-in dictation transcribes words. Echo understands context: it removes filler, fixes punctuation, formats for the app you're in, and learns your custom vocabulary and names.",
  },
  {
    q: "Which platforms are supported?",
    a: "Windows 10 and 11 are fully supported today, with a lightweight app that runs quietly in the background and a configurable global hotkey.",
  },
];

export default function Faq() {
  const [open, setOpen] = useState<number | null>(0);

  return (
    <section className="section" id="faq">
      <div className="container">
        <Reveal>
          <span className="overline">Good to know</span>
          <h2>Frequently asked questions</h2>
        </Reveal>

        <div className="faq-list">
          {FAQS.map((f, i) => {
            const isOpen = open === i;
            return (
              <Reveal key={f.q} delay={i * 0.05} y={14}>
                <div className={`faq-item ${isOpen ? "open" : ""}`}>
                  <button className="faq-q" onClick={() => setOpen(isOpen ? null : i)}>
                    {f.q}
                    <motion.svg
                      width="16"
                      height="16"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                      animate={{ rotate: isOpen ? 45 : 0 }}
                      transition={{ type: "spring", stiffness: 320, damping: 20 }}
                    >
                      <path d="M12 5v14M5 12h14" />
                    </motion.svg>
                  </button>
                  <AnimatePresence initial={false}>
                    {isOpen && (
                      <motion.div
                        className="faq-a"
                        initial={{ height: 0, opacity: 0 }}
                        animate={{ height: "auto", opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        transition={{ type: "spring", stiffness: 260, damping: 28 }}
                        style={{ overflow: "hidden" }}
                      >
                        <p>{f.a}</p>
                      </motion.div>
                    )}
                  </AnimatePresence>
                </div>
              </Reveal>
            );
          })}
        </div>
      </div>
    </section>
  );
}
