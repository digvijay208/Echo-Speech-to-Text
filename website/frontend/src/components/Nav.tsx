type Props = {
  theme: "light" | "dark";
  onToggleTheme: () => void;
};

export default function Nav({ theme, onToggleTheme }: Props) {
  return (
    <header className="nav">
      <div className="container nav-inner">
        <a className="brand" href="#top">
          <span className="brand-mark" aria-hidden>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round">
              <path d="M12 3v18M7 8v8M17 8v8M3 11v2M21 11v2" />
            </svg>
          </span>
          Echo
        </a>
        <nav className="nav-links">
          <a href="#features">Features</a>
          <a href="#use-cases">Use cases</a>
          <a href="#faq">FAQ</a>
        </nav>
        <div className="nav-actions">
          <button className="theme-toggle" onClick={onToggleTheme} aria-label="Toggle theme">
            {theme === "light" ? (
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <path d="M21 12.8A9 9 0 1 1 11.2 3 7 7 0 0 0 21 12.8z" />
              </svg>
            ) : (
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                <circle cx="12" cy="12" r="4" />
                <path d="M12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4" />
              </svg>
            )}
          </button>
          <a className="btn btn-primary" href="#download" style={{ padding: "10px 20px", fontSize: "0.88rem" }}>
            Download free
          </a>
        </div>
      </div>
    </header>
  );
}
