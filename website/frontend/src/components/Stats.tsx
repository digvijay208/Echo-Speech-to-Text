import { CountUp, Reveal } from "./motion-primitives";

const STATS = [
  { to: 150, suffix: "", label: "Words per minute" },
  { to: 100, suffix: "+", label: "Languages" },
  { to: 20, suffix: "h", label: "Saved per month" },
  { to: 0, suffix: "", label: "Data retained" },
];

export default function Stats() {
  return (
    <section className="section" style={{ paddingTop: 0, paddingBottom: 0 }}>
      <div className="container">
        <div className="stats">
          {STATS.map((s, i) => (
            <Reveal key={s.label} delay={i * 0.08} y={16}>
              <div className="stat">
                <div className="stat-num">
                  <CountUp to={s.to} suffix={s.suffix} duration={1.4 + i * 0.15} />
                </div>
                <div className="stat-label">{s.label}</div>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
