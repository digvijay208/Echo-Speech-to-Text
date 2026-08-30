const APPS = [
  "Slack",
  "Gmail",
  "Notion",
  "VS Code",
  "Cursor",
  "Discord",
  "Chrome",
  "Linear",
  "Figma",
  "WhatsApp",
];

export default function Marquee() {
  const items = [...APPS, ...APPS];
  return (
    <section className="marquee-section">
      <div className="container">
        <div className="marquee-label">Works in the apps you already use</div>
      </div>
      <div className="marquee">
        <div className="marquee-track">
          {items.map((app, i) => (
            <span className="app-chip" key={`${app}-${i}`}>
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <rect x="3" y="3" width="18" height="18" rx="5" />
              </svg>
              {app}
            </span>
          ))}
        </div>
      </div>
    </section>
  );
}
