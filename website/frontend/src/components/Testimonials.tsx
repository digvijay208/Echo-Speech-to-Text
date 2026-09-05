import { motion } from "motion/react";
import { Link } from "react-router-dom";
import { Reveal } from "./motion-primitives";

const QUOTES = [
  { q: "In less than 4 minutes, I've already saved 25 minutes from not having to type.", name: "Linus Ekenstam", role: "AI Evangelist & Designer", initials: "LE" },
  { q: "Nearly 100x cheaper than conventional dictation software.", name: "Nate Williams", role: "Network Communications Specialist, US Army", initials: "NW" },
  { q: "An AI tool that changes my life.", name: "Robert Scoble", role: "Technology Futurist & Best-selling Author", initials: "RS" },
  { q: "App of the year for 2026 just dropped!", name: "Dr. Joe Borelli", role: "M.D. Radiologist & MRI Clinic Owner", initials: "JB" },
  { q: "If typing slows down your creativity, this is worth trying.", name: "Ajay Sharma", role: "Software Engineer", initials: "AS" },
  { q: "It's better than other voice input apps because it formats what you say.", name: "Jiayuan Zhang", role: "Founder & CEO, Devv AI", initials: "JZ" },
  { q: "Echo confirmed, we are entering a new phase.", name: "Andre Oliveira", role: "Founder & Investor", initials: "AO" },
  { q: "I am posting replying 10x!", name: "Gideon Shalwick", role: "Founder, Vubli.ai", initials: "GS" },
  { q: "Echo on iOS works better than any other voice to text tool I've tried.", name: "Francis Teo", role: "Founder, Bluelambda", initials: "FT" },
  { q: "Now I can just speak, and Echo turns what I say into well-structured text.", name: "Braxxxx Li", role: "PM & Growth Expert", initials: "BL" },
];

export default function Testimonials() {
  const a = [...QUOTES, ...QUOTES];
  const b = [...[...QUOTES].reverse(), ...[...QUOTES].reverse()];

  return (
    <section className="section" id="testimonials">
      <div className="container">
        <Reveal>
          <h2 className="section-h2 center">People love Echo</h2>
          <p className="section-sub center">They get more done by speaking naturally while Echo handles the rest.</p>
        </Reveal>
      </div>

      <div className="marquee" aria-hidden>
        <div className="marquee-track" style={{ gap: 20 }}>
          {a.map((t, i) => (
            <article className="testimonial" key={`a-${t.name}-${i}`}>
              <blockquote>“{t.q}”</blockquote>
              <div className="testimonial-attr">
                <div className="testimonial-avatar">{t.initials}</div>
                <div>
                  <div className="testimonial-name">{t.name}</div>
                  <div className="testimonial-role">{t.role}</div>
                </div>
              </div>
            </article>
          ))}
        </div>
      </div>

      <div className="marquee reverse" aria-hidden>
        <div className="marquee-track" style={{ gap: 20 }}>
          {b.map((t, i) => (
            <article className="testimonial" key={`b-${t.name}-${i}`}>
              <blockquote>“{t.q}”</blockquote>
              <div className="testimonial-attr">
                <div className="testimonial-avatar">{t.initials}</div>
                <div>
                  <div className="testimonial-name">{t.name}</div>
                  <div className="testimonial-role">{t.role}</div>
                </div>
              </div>
            </article>
          ))}
        </div>
      </div>

      <Reveal delay={0.05}>
        <div className="testimonials-cta">
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
        </div>
      </Reveal>
    </section>
  );
}
