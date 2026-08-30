import { Zap, Lock, Globe, Download } from "lucide-react";
import { Reveal, BlurReveal, TiltCard, Spotlight } from "./motion-primitives";

const CARDS = [
  {
    span: "span-7",
    icon: Zap,
    title: "Speak at the speed of thought",
    body: "Press one hotkey and talk. Echo transcribes, removes filler words, fixes punctuation, and formats the text for the app you're in — instantly.",
    stat: { num: "150", label: "words per minute, vs ~40 typing" },
  },
  {
    span: "span-5",
    icon: Lock,
    title: "Private by design",
    body: "Local-first processing. Your recordings stay on your device. Zero data retention — your words are yours, always.",
  },
  {
    span: "span-5",
    icon: Globe,
    title: "Speak your language",
    body: "100+ languages supported, with custom dictionaries for your jargon, names, and the way your team actually talks.",
  },
  {
    span: "span-7",
    icon: Download,
    title: "Works everywhere you do",
    body: "Gmail, Slack, Cursor, Notion — one hotkey, any text field. Echo adapts its formatting to the context: casual in chat, professional in email, precise in code.",
    stat: { num: "20+", label: "hours saved per month, on average" },
  },
];

export default function Features() {
  return (
    <section className="section features-bg" id="features">
      <div className="container">
        <Reveal>
          <span className="overline">What Echo does</span>
          <h2>
            Typing is the bottleneck.
            <br />
            Your voice isn't.
          </h2>
          <p className="section-sub">
            Your thoughts move faster than your hands ever will. Echo closes that gap —
            in every app, in every language, on your terms.
          </p>
        </Reveal>

        <div className="glass-grid">
          <BlurReveal className="demo-panel" delay={0.05}>
            <div className="demo-bar">
              <span className="demo-hotkey">Ctrl + Shift + Space</span>
              <span className="demo-status">
                <span className="pulse-dot" />
                Listening
                <span className="wave" aria-hidden>
                  {Array.from({ length: 12 }).map((_, i) => (
                    <span key={i} />
                  ))}
                </span>
              </span>
            </div>
            <div className="demo-row">
              <div className="demo-who">You said</div>
              <div className="demo-text raw">
                "so um basically i want to like reply to sarah and say the deploy is done
                but uh we still need to fix the auth bug before friday"
              </div>
            </div>
            <div className="demo-row">
              <div className="demo-who">Echo wrote</div>
              <div className="demo-text clean">
                "Reply to Sarah: the deploy is done, but we still need to fix the auth bug
                before Friday."
              </div>
            </div>
          </BlurReveal>

          {CARDS.map((card, i) => (
            <Reveal
              key={card.title}
              className={card.span}
              delay={0.06 * (i + 1)}
              style={{ display: "flex" }}
            >
              <TiltCard style={{ width: "100%" }} maxTilt={6}>
                <Spotlight className="glass-card" >
                  <div>
                    <div className="glass-icon">
                      <card.icon style={{ width: 20, height: 20 }} />
                    </div>
                    <h3>{card.title}</h3>
                    <p>{card.body}</p>
                  </div>
                  {card.stat && (
                    <div className="g-stat">
                      <strong>{card.stat.num}</strong>
                      <span>{card.stat.label}</span>
                    </div>
                  )}
                </Spotlight>
              </TiltCard>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
