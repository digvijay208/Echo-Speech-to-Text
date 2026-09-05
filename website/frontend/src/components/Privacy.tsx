import { useState } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Reveal } from "./motion-primitives";

const TABS = {
  "On device": [
    { h: "Zero cloud data retention", p: "Your voice dictations are processed securely with zero data retention." },
    { h: "Never trained on your data", p: "Your dictation data is never used for model training by us or any third party." },
    { h: "On-device history storage", p: "Your dictation history stays on your device. Only you can access it." },
    { h: "No audio stored in the cloud", p: "Your audio is processed securely in real time and never stored in the cloud." },
  ],
  "Cloud Sync": [
    { h: "Never trained on your data", p: "Your dictation data is never used for model training by us or any third party." },
    { h: "Encrypted across devices", p: "Your History is encrypted and accessible only to you. Echo is ISO 27001 certified, GDPR compliant, and HIPAA compliant." },
    { h: "Zero data retention policy", p: "Sync metadata is processed in transit and not stored on our servers." },
    { h: "You own your key", p: "End-to-end encryption means we cannot read your synced dictionary or style profile — even if we wanted to." },
  ],
} as const;

type Tab = keyof typeof TABS;
const EASE = [0.21, 0.65, 0.36, 1] as const;

export default function Privacy() {
  const [active, setActive] = useState<Tab>("On device");
  const items = TABS[active];

  return (
    <section className="section" id="privacy">
      <div className="container">
        <Reveal>
          <div className="privacy-img">
            <img src="https://typeless-static.com/webpage/assets/homepage/Privacy.webp" alt="Privacy by design" loading="lazy" />

            <div className="privacy-overlay">
              <h2 className="privacy-h2">
                Private <em>by design</em>
              </h2>

              <div className="privacy-tabs" role="tablist">
                {(Object.keys(TABS) as Tab[]).map((tab) => (
                  <button
                    key={tab}
                    role="tab"
                    aria-selected={active === tab}
                    className={`privacy-tab${active === tab ? " active" : ""}`}
                    onClick={() => setActive(tab)}
                  >
                    {tab}
                  </button>
                ))}
              </div>

              <AnimatePresence mode="wait">
                <motion.ul
                  key={active}
                  className="privacy-list"
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  exit={{ opacity: 0 }}
                  transition={{ duration: 0.25, ease: EASE }}
                >
                  {items.map((item) => (
                    <li className="privacy-item" key={item.h}>
                      <h4>{item.h}</h4>
                      <p>{item.p}</p>
                    </li>
                  ))}
                </motion.ul>
              </AnimatePresence>
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
