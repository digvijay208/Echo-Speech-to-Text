import { Reveal } from "./motion-primitives";

function TranslateMockup() {
  return (
    <div className="mockup-fillers">
      <div className="mockup-bubble mockup-bubble-raw">
        <span className="mockup-waveform" aria-hidden>
          <i /><i /><i /><i /><i />
        </span>
        <p>Here&rsquo;s the product info, the price is 9 dollars, the size is 30, and we offer free returns.</p>
      </div>
      <div className="mockup-bubble mockup-bubble-translate">
        <p>Aqu&iacute; tienes la informaci&oacute;n del producto:</p>
        <ol className="mockup-list">
          <li>El precio es de $9</li>
          <li>El tama&ntilde;o es 30</li>
          <li>Ofrecemos devoluciones gratuitas</li>
        </ol>
      </div>
    </div>
  );
}

export default function Translate() {
  return (
    <section className="section" id="translate">
      <div className="container">
        <Reveal>
          <h2 className="section-h2">Translate</h2>
        </Reveal>

        <Reveal delay={0.05}>
          <div className="dictate-row">
            <div className="dictate-row-text">
              <h3>Translates as you speak</h3>
              <p>Echo instantly translates your speech into your chosen language with phrasing that reads like a local would write it.</p>
            </div>
            <div className="dictate-row-media">
              <TranslateMockup />
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
