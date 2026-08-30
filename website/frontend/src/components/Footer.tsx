import { AudioWaveform } from "lucide-react";

export default function Footer() {
  return (
    <footer className="footer">
      <div className="container footer-inner">
        <a className="footer-brand" href="#top">
          <AudioWaveform style={{ width: 18, height: 18 }} strokeWidth={2.2} />
          Echo
        </a>
        <span className="footer-note">The voice layer for every app. Built for Windows.</span>
        <nav className="footer-links">
          <a href="#features">Features</a>
          <a href="#use-cases">Use cases</a>
          <a href="#faq">FAQ</a>
        </nav>
      </div>
    </footer>
  );
}
