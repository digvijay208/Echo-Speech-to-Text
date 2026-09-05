import { Link } from "react-router-dom";
import { motion } from "motion/react";
import { Reveal } from "./motion-primitives";

export default function FinalCta() {
  return (
    <section className="section final-cta">
      <div className="container">
        <Reveal>
          <div className="final-cta-card">
            <Reveal>
              <h2 className="final-cta-h2">
                Free yourself from<br />the keyboard
              </h2>
              <p className="final-cta-sub">Write smarter with Echo and save 1 day per week.</p>
            </Reveal>

            <Reveal delay={0.1}>
              <div className="final-cta-action">
                <motion.div
                  whileHover={{ scale: 1.03 }}
                  whileTap={{ scale: 0.97 }}
                  transition={{ type: "spring", stiffness: 400, damping: 17 }}
                  style={{ display: "inline-block" }}
                >
                  <Link to="/downloads" className="btn btn-primary">
                    Download for Windows
                  </Link>
                </motion.div>
              </div>
            </Reveal>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
