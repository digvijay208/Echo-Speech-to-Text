import { Reveal } from "./motion-primitives";

export default function AskAi() {
  return (
    <section className="section ask-ai" id="ask-ai">
      <div className="container">
        <Reveal>
          <div className="ask-ai-card">
            <Reveal>
              <h2 className="ask-ai-h2">Not sure if Echo is right for you?</h2>
            </Reveal>

            <Reveal delay={0.1}>
              <div className="ask-ai-wordmark" aria-hidden>
                Echo
              </div>
            </Reveal>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
