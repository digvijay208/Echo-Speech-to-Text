import { motion } from "motion/react";
import { Link } from "react-router-dom";
import { Reveal, WordsReveal } from "./motion-primitives";

const EASE = [0.21, 0.65, 0.36, 1] as const;

function StatCard({
  kicker,
  value,
  unit,
}: {
  kicker: string;
  value: string;
  unit?: string;
}) {
  return (
    <div className="hero-stat">
      <div className="hero-stat-kicker">{kicker}</div>
      <div className="hero-stat-value">
        {value}
        {unit && <small>{unit}</small>}
      </div>
    </div>
  );
}

function HeroTypingCard() {
  return (
    <div className="hero-card hero-card-typing">
      <StatCard kicker="QWERTY keyboard" value="45" unit="wpm" />
      <div className="hero-card-media hero-card-media-typing">
        <video
          className="hero-card-video"
          src="/hero/hero_text_beta.webm"
          autoPlay
          muted
          loop
          playsInline
        />
      </div>
    </div>
  );
}

function HeroSpeakingCard() {
  return (
    <div className="hero-card hero-card-speaking">
      <div className="hero-card-stats">
        <StatCard kicker="Echo voice keyboard" value="220" unit="wpm" />
        <StatCard kicker="Save" value="1 day" unit="/week" />
      </div>
      <div className="hero-card-media hero-card-media-speaking">
        <video
          className="hero-card-video"
          src="/hero/hero_voice_alpha.webm"
          autoPlay
          muted
          loop
          playsInline
        />
      </div>
    </div>
  );
}

export default function Hero() {
  return (
    <section className="hero" id="top">
      <div className="container">
        <motion.div
          className="hero-text"
          initial={{ opacity: 0, y: 14 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.8, ease: EASE }}
        >
          <h1>
            <WordsReveal text="Don't type," delay={0} immediate />
            {" "}
            <span className="hero-h1-mute">
              <WordsReveal text="just speak" delay={0.12} immediate />
            </span>
          </h1>
          <p className="hero-sub">
            Speak naturally, and Echo will{" "}
            <strong>turn your words into polished messages, emails, and documents</strong>{" "}
            that read like you carefully typed them — in real time.{" "}
            <strong>4x faster than typing.</strong>
          </p>
          <motion.div
            whileHover={{ scale: 1.03 }}
            whileTap={{ scale: 0.97 }}
            transition={{ type: "spring", stiffness: 400, damping: 17 }}
            style={{ display: "inline-block" }}
          >
            <Link to="/downloads" className="btn btn-primary">
              Download for free
            </Link>
          </motion.div>
        </motion.div>

        <Reveal delay={0.25}>
          <div className="hero-panels">
            <HeroTypingCard />
            <HeroSpeakingCard />
          </div>
        </Reveal>
      </div>
    </section>
  );
}
