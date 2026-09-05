import { motion } from "motion/react";
import { Link } from "react-router-dom";
import { Reveal } from "./motion-primitives";

export default function Devices() {
  return (
    <section className="section" id="devices">
      <div className="container">
        <div className="devices-row">
          <div className="devices-text">
            <Reveal>
              <div className="platform-pills devices-pills">
                <span className="platform-pill">macOS</span>
                <span className="platform-pill current">Windows</span>
                <span className="platform-pill">iOS</span>
                <span className="platform-pill">Android</span>
              </div>
            </Reveal>

            <Reveal delay={0.05}>
              <h2 className="devices-h2">
                Your voice, on <em>every device</em>
              </h2>
            </Reveal>

            <Reveal delay={0.1}>
              <p className="devices-sub">
                Use Echo on your mobile and computer for a consistent experience across the devices you rely on every day.
              </p>
            </Reveal>

            <Reveal delay={0.15}>
              <motion.div
                whileHover={{ scale: 1.03 }}
                whileTap={{ scale: 0.97 }}
                transition={{ type: "spring", stiffness: 400, damping: 17 }}
                style={{ display: "inline-block" }}
              >
                <Link to="/downloads" className="btn btn-primary devices-cta">
                  Download for Windows
                </Link>
              </motion.div>
            </Reveal>
          </div>

          <Reveal delay={0.1}>
            <div className="devices-card">
              <img
                src="/movile-view.webp"
                alt="Echo on mobile and desktop"
                className="devices-image"
                loading="lazy"
              />
            </div>
          </Reveal>
        </div>
      </div>
    </section>
  );
}
