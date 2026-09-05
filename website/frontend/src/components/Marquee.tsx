import { Reveal } from "./motion-primitives";

const BASE = "https://typeless-static.com/webpage/assets/homepage/appIcons";

// Exact filenames from typeless.com — first marquee row
const ROW_A = [
  "Notability.webp", "PDF_Reader.webp", "onenote.webp", "teams.webp",
  "outlook.webp", "zoom.webp", "things.webp", "OneDrive.webp",
  "Foxit_PDF_Editor.webp", "Joplin.webp", "Microsoft_Excel.webp", "Cursor.webp",
  "Microsoft_PowerPoint.webp", "PDF_Editor_Master.webp", "Hinge_Dating.webp",
  "Obsidian.webp", "FineReader_Pro.webp", "Telegram.webp", "imessage.webp",
  "ChatBox_AI.webp", "QuarkXPress.webp", "Bear.webp", "Adobe_Acrobat_Reader.webp",
  "Amazon_Kindle.webp", "ins.webp", "arc.webp", "superhuman.webp",
  "perplexity.webp", "github.webp", "canva.webp", "Microsoft_Copilot.webp",
  "PDFelement.webp", "mail.webp", "Nitro_PDF_Pro.webp", "PDF_Expert.webp",
];

// Exact filenames from typeless.com — second marquee row (reversed)
const ROW_B = [
  "Lemon8.webp", "classroom.webp", "evernote.webp", "figma.webp",
  "x.webp", "Tinder.webp", "Ulysses.webp", "notion.webp",
  "messenger.webp", "snap.webp", "vscode.webp", "replit.webp",
  "todolist.webp", "WhatsApp.webp", "claude.webp", "linear.webp",
  "Signal.webp", "gmail.webp", "Numbers.webp", "google.webp",
  "Medium.webp", "grammarly.webp", "docs.webp", "applenotes.webp",
  "chatgpt.webp", "Astra.webp", "PREVIEW.webp", "Slack.webp",
  "Pinterest.webp", "TikTok.webp", "Raycast.webp", "Brave_Browser.webp",
  "Goodnotes_6.webp", "keynote.webp", "TextEdit.webp", "Google_Keep.webp",
];

export default function Marquee() {
  return (
    <section className="marquee-section" id="works-everywhere">
      <div className="container">
        <Reveal>
          <h2 className="marquee-h2">
            Works everywhere <em>you write</em>
          </h2>
          <p className="marquee-sub">
            Echo works across all apps you use on Mac, Windows, iOS, and Android.
            Speak naturally, and your words instantly become perfect writing
            wherever you work.
          </p>
        </Reveal>
      </div>

      <div className="marquee-card" aria-hidden>
        <div className="marquee">
          <div className="marquee-track">
            {[...ROW_A, ...ROW_A].map((f, i) => (
              <div className="marquee-icon" key={`a-${i}`}>
                <img src={`${BASE}/${f}`} alt="" loading="lazy" />
              </div>
            ))}
          </div>
        </div>

        <div className="marquee reverse">
          <div className="marquee-track">
            {[...ROW_B, ...ROW_B].map((f, i) => (
              <div className="marquee-icon" key={`b-${i}`}>
                <img src={`${BASE}/${f}`} alt="" loading="lazy" />
              </div>
            ))}
          </div>
        </div>
      </div>

      <div className="container">
        <div className="platform-pills">
          <span className="platform-pill">macOS</span>
          <span className="platform-pill current">Windows</span>
          <span className="platform-pill">iOS</span>
          <span className="platform-pill">Android</span>
        </div>
      </div>
    </section>
  );
}
