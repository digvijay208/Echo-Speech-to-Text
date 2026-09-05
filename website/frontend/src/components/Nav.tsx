import { Link } from "react-router-dom";
import { AudioWaveform } from "lucide-react";

export default function Nav() {
  return (
    <header className="nav">
      <div className="container nav-inner">
        <Link className="brand" to="/">
          <span className="brand-mark" aria-hidden>
            <AudioWaveform width={20} height={20} strokeWidth={2.2} />
          </span>
          Echo
        </Link>
        <nav className="nav-links">
          <Link to="/#features">Features</Link>
          <Link to="/#use-cases">Use cases</Link>
          <Link to="/#privacy">Privacy</Link>
          <Link to="/#faq">FAQ</Link>
        </nav>
        <div className="nav-actions">
          <Link to="/downloads" className="btn btn-primary nav-cta">
            Download free
          </Link>
        </div>
      </div>
    </header>
  );
}
