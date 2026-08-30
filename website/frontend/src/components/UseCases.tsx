import { Reveal, TiltCard, Spotlight } from "./motion-primitives";

const CASES = [
  {
    tag: "For developers",
    title: "Think out loud, ship code",
    body: "Dictate commit messages, PR descriptions, and docs without breaking flow. Echo understands technical vocabulary and formats code terms correctly.",
    example: `"refactor the auth middleware to use uh async await"  →  "Refactor the auth middleware to use async/await."`,
  },
  {
    tag: "For writers",
    title: "First drafts at speaking speed",
    body: "Get ideas down before they evaporate. Echo turns rambling voice notes into structured, punctuated prose you can actually edit.",
    example: `"okay so the intro should hook them with like a story about"  →  "The intro should hook readers with a story about…"`,
  },
  {
    tag: "For teams",
    title: "Replies in a fraction of the time",
    body: "Clear your inbox and Slack backlog by speaking. Context-aware formatting keeps emails professional and chats casual — automatically.",
    example: `"tell the team standup moves to ten and please update jira"  →  "Team — standup moves to 10:00. Please update Jira."`,
  },
];

export default function UseCases() {
  return (
    <section className="section" id="use-cases" style={{ paddingTop: 0 }}>
      <div className="container">
        <Reveal>
          <span className="overline">Use cases</span>
          <h2>Made for the way you work</h2>
          <p className="section-sub">
            One hotkey. Every app. Echo adapts to your context, your vocabulary, and your voice.
          </p>
        </Reveal>

        <div className="usecases">
          {CASES.map((c, i) => (
            <Reveal key={c.tag} delay={i * 0.1} style={{ display: "flex" }}>
              <TiltCard style={{ width: "100%" }} maxTilt={5} scale={1.02}>
                <Spotlight className="usecase">
                  <span className="usecase-tag">{c.tag}</span>
                  <h3>{c.title}</h3>
                  <p>{c.body}</p>
                  <div className="usecase-example">{c.example}</div>
                </Spotlight>
              </TiltCard>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  );
}
