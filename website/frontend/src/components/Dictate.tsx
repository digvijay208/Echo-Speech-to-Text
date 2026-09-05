import { Reveal } from "./motion-primitives";

const FEATURES = [
  {
    title: "Removes filler words",
    body: 'Automatically removes filler words like "um," "uh," and "you know," making your transcriptions clear and professional.',
    mockup: "fillers",
  },
  {
    title: "Removes repetition",
    body: "Automatically detects and removes unnecessary and repeated words in your speech, ensuring your language is concise and easy to understand.",
    mockup: "repetition",
  },
  {
    title: "Auto-edits when you change your mind",
    body: "Echo recognizes when you correct yourself mid-sentence and keeps only your final intended message, avoiding clutter and confusion.",
    mockup: "self-correct",
  },
  {
    title: "Auto-formats",
    body: "Automatically organizes spoken lists, steps, and key points into clean, structured text, saving you the hassle of manual formatting.",
    mockup: "format",
  },
  {
    title: "Find the perfect words",
    body: "Echo lets you effortlessly express ideas, ensuring your message is easy to understand and your points come across clearly.",
    mockup: "polish",
  },
];

const DEEP_FEATURES = [
  {
    title: "Personalized style and tone",
    body: "Echo adapts to your tone, phrasing, and writing habits, creating dictation that feels like you. Your personalization progress highlights how closely Echo aligns with the way you communicate.",
    img: "https://typeless-static.com/webpage/assets/homepage/Personalization-progress.webp",
    flip: false,
  },
  {
    title: "Personal dictionary",
    body: "Echo recognizes the names, terms, and expressions that matter to you. Whether added automatically or manually, your vocabulary stays accurate across every app and context.",
    img: "https://typeless-static.com/webpage/assets/homepage/Personal-dictionary.webp",
    flip: true,
  },
  {
    title: "100+ languages supported",
    body: "Speak in any language or mix them seamlessly, and Echo automatically detects your language and transcribes your speech. It is simple and effortless.",
    img: "https://typeless-static.com/webpage/assets/homepage/100%2B-languages-supported.webp",
    flip: false,
  },
  {
    title: "Different tones for each app",
    body: "Echo adapts tone and style based on the app you are using, from work emails to casual chats, so everything fits the context.",
    img: "https://typeless-static.com/webpage/assets/homepage/Different-tones-for-each-app.webp",
    flip: true,
  },
];

/* Mockup: filler words — shows the "raw" dictation with strike-throughs and the cleaned version */
function FillerMockup() {
  return (
    <div className="mockup-fillers">
      <div className="mockup-bubble mockup-bubble-raw">
        <span className="mockup-waveform" aria-hidden>
          <i /><i /><i /><i /><i />
        </span>
        <p>
          <s>Um,</s> what's the plan for today, <s>um, like,</s> just to confirm?
        </p>
      </div>
      <div className="mockup-bubble mockup-bubble-clean">
        <p>What's the plan for today? Just to confirm.</p>
      </div>
    </div>
  );
}

function MockupFor({ kind }: { kind: string }) {
  if (kind === "fillers") return <FillerMockup />;
  return <div className="mockup-placeholder" />;
}

export default function Dictate() {
  return (
    <section className="section" id="dictate">
      <div className="container">
        <Reveal>
          <h2 className="section-h2">Dictate</h2>
        </Reveal>

        {/* One feature per row — text on left, mockup on right (typeless.com layout) */}
        <div className="dictate-rows">
          {FEATURES.map((f, i) => (
            <Reveal key={f.title} delay={i * 0.04}>
              <div className="dictate-row">
                <div className="dictate-row-text">
                  <h3>{f.title}</h3>
                  <p>{f.body}</p>
                </div>
                <div className="dictate-row-media">
                  <MockupFor kind={f.mockup} />
                </div>
              </div>
            </Reveal>
          ))}
        </div>

        {/* Deep feature blocks: 2-col with real typeless images */}
        <div className="feature-blocks">
          {DEEP_FEATURES.map((f, i) => (
            <Reveal key={f.title} delay={i * 0.04}>
              <div className={`feature-block${f.flip ? " flip" : ""}`}>
                <div className="feature-block-img">
                  <img src={f.img} alt={f.title} loading="lazy" />
                </div>
                <div className="feature-block-text">
                  <h3>{f.title}</h3>
                  <p>{f.body}</p>
                </div>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
