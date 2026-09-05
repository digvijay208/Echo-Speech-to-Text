import { Reveal } from "./motion-primitives";

const ROW_1 = {
  title: "Speak to edit selected text",
  body: "Select any text and speak your command. Instantly make it shorter, longer, change its tone, or even generate a creative response. It is your personal editor, ready anywhere you write.",
  mockup: "edit",
};

const ROW_2 = {
  title: "Speak to ask about selected text",
  body: "Now you can interact with read-only text. Select a paragraph on any website or document and ask for a summary, an explanation, or a translation, all without breaking your flow.",
  mockup: "ask",
};

const ROW_3 = {
  title: "Quick answers & actions",
  body: "Just ask for what you need. Echo can check the latest info, help you brainstorm, search across sites and services, and open the right page with the details already in place, so you can stay in flow.",
  mockup: "answers",
};

/* Mockup: a speech bubble command over a "selected text" snippet that gets rewritten */
function EditMockup() {
  return (
    <div className="ask-mockup ask-mockup-edit">
      <div className="mockup-bubble mockup-bubble-raw">
        <span className="mockup-waveform" aria-hidden>
          <i /><i /><i /><i /><i />
        </span>
        <p><strong>Rewrite it into a viral Twitter post with a confident tone, emojis, and hashtags.</strong></p>
      </div>
      <div className="ask-snip">
        <div className="ask-snip-hint">turns your words into polished</div>
        <div className="ask-snip-label">
          <span className="ask-app-icon ask-app-icon-x">𝕏</span>
          <span>X (Twitter)</span>
        </div>
        <p className="ask-snip-line">Stop wasting time at your keyboard. <s>⌨️</s> ❌</p>
        <p className="ask-snip-line">Typeless lets you speak naturally and instantly converts your thoughts into polished, professional emails and documents.</p>
        <p className="ask-snip-line">It&rsquo;s 4x faster than typing and reads like you spent hours crafting it. 🚀✨</p>
        <p className="ask-snip-line">Stop typing. Start talking. Win your time back. ⏳💪</p>
        <p className="ask-snip-line ask-snip-tags">#Productivity #AI #Typeless #WorkSmarter</p>
      </div>
    </div>
  );
}

/* Mockup: speech command "Translate to English" over a Japanese paragraph, with the answer card */
function AskMockup() {
  return (
    <div className="ask-mockup ask-mockup-ask">
      <div className="mockup-bubble mockup-bubble-raw ask-bubble-right">
        <span className="mockup-waveform" aria-hidden>
          <i /><i /><i /><i /><i />
        </span>
        <p><strong>Translate to English</strong></p>
      </div>
      <div className="ask-snip ask-snip-jp">
        <p>日本一高くそびえる富士山は日本八つについて神聖な存在です。古くから「信仰の対象」として、日本人の自然観に大きな影響を与えてきました。古来、山岳信仰の...</p>
      </div>
      <div className="ask-card">
        <div className="ask-card-head">
          <span className="ask-app-icon">⏎</span>
          <span>Typeless</span>
          <span className="ask-card-x">×</span>
        </div>
        <div className="ask-card-row">
          <span className="ask-card-icon">🎙</span>
          <span>Translate to English</span>
          <span className="ask-card-copy">⧉</span>
        </div>
        <div className="ask-card-row ask-card-answer-head">
          <span>✨</span>
          <span>Answer</span>
          <span className="ask-card-copy">⧉</span>
        </div>
        <p className="ask-card-answer">
          The towering Mount Fuji in Japan is a sacred figure for the Japanese people. Since ancient times, it has been regarded as a "object of faith", exerting a significant influence on the Japanese view of nature. The constantly erupting Mount Fuji, from its foothills to its summit, has been the subject of "remote worship" that the Japanese people have revered.
        </p>
      </div>
    </div>
  );
}

/* Mockup: two stacked cards — "Quick answers" + "Quick actions" with example prompts */
function AnswersMockup() {
  return (
    <div className="ask-mockup ask-mockup-answers">
      <div className="ask-card ask-card-flat">
        <div className="ask-card-row"><span>✦</span><strong>Quick answers</strong></div>
        <ul className="ask-list">
          <li>🌤️ <em>What&rsquo;s the weather in New York tomorrow?</em></li>
          <li>💰 <em>What&rsquo;s 100 USD in euros?</em></li>
          <li>📱 <em>What&rsquo;s the iPhone price right now?</em></li>
        </ul>
      </div>
      <div className="ask-card ask-card-flat">
        <div className="ask-card-row"><span>⚡</span><strong>Quick actions</strong></div>
        <ul className="ask-list">
          <li>▶️ <em>Open YouTube and search for yoga workouts.</em></li>
          <li>🛒 <em>Search Amazon for a standing desk.</em></li>
          <li>📍 <em>Find nearby sushi on Google Maps.</em></li>
        </ul>
      </div>
    </div>
  );
}

function MockupFor({ kind }: { kind: string }) {
  if (kind === "edit") return <EditMockup />;
  if (kind === "ask") return <AskMockup />;
  if (kind === "answers") return <AnswersMockup />;
  return null;
}

export default function AskAnything() {
  return (
    <section className="section" id="ask-anything">
      <div className="container">
        <Reveal>
          <h2 className="section-h2 center">Ask anything</h2>
        </Reveal>

        {/* Top row: two columns — left = "edit", right = "ask" */}
        <div className="ask-grid">
          <Reveal delay={0.04}>
            <div className="ask-cell">
              <div className="dictate-row-media ask-media">
                <MockupFor kind={ROW_1.mockup} />
              </div>
              <div className="ask-cell-text">
                <h3>{ROW_1.title}</h3>
                <p>{ROW_1.body}</p>
              </div>
            </div>
          </Reveal>

          <Reveal delay={0.08}>
            <div className="ask-cell ask-cell-offset">
              <div className="dictate-row-media ask-media">
                <MockupFor kind={ROW_2.mockup} />
              </div>
              <div className="ask-cell-text">
                <h3>{ROW_2.title}</h3>
                <p>{ROW_2.body}</p>
              </div>
            </div>
          </Reveal>
        </div>

        {/* Bottom row: "Quick answers & actions" — text left, mockup right */}
        <Reveal delay={0.12}>
          <div className="dictate-row ask-bottom-row">
            <div className="dictate-row-text">
              <h3>{ROW_3.title}</h3>
              <p>{ROW_3.body}</p>
            </div>
            <div className="dictate-row-media">
              <MockupFor kind={ROW_3.mockup} />
            </div>
          </div>
        </Reveal>
      </div>
    </section>
  );
}
