import { motion } from "motion/react";
import { ArrowDownToLine } from "lucide-react";
import { Reveal, Magnetic } from "./motion-primitives";
import WaitlistForm from "./WaitlistForm";

export default function Cta() {
  return (
    <section className="section" id="download" style={{ paddingTop: 0 }}>
      <div className="container">
        <Reveal y={32}>
          <div className="cta">
            <span className="overline" style={{ display: "inline-flex" }}>
              Get started
            </span>
            <h2>
              Stop typing.
              <br />
              Start talking.
            </h2>
            <p>
              Download Echo for Windows and dictate your first message in under two
              minutes. Free to start — no credit card, no trial limits.
            </p>
            <Magnetic strength={0.35}>
              <motion.a
                className="btn btn-navy"
                href="/downloads/Echo-Setup.exe"
                whileHover={{ scale: 1.05 }}
                whileTap={{ scale: 0.95 }}
                transition={{ type: "spring", stiffness: 400, damping: 17 }}
              >
                <span className="btn-icon">
                  <ArrowDownToLine style={{ width: 15, height: 15, color: "#fff" }} />
                </span>
                <span>Download for Windows</span>
              </motion.a>
            </Magnetic>

            <div className="cta-divider" aria-hidden />

            <WaitlistForm />
          </div>
        </Reveal>
      </div>
    </section>
  );
}
