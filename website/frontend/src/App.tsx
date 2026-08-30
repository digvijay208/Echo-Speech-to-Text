import { useEffect, useState } from "react";
import Hero from "./components/Hero";
import Marquee from "./components/Marquee";
import Features from "./components/Features";
import UseCases from "./components/UseCases";
import Stats from "./components/Stats";
import Faq from "./components/Faq";
import Cta from "./components/Cta";
import Footer from "./components/Footer";
import { ScrollProgress } from "./components/motion-primitives";
import { useLenis } from "./hooks/useLenis";

type Theme = "light" | "dark";

export default function App() {
  const [theme, setTheme] = useState<Theme>(() => {
    if (typeof window === "undefined") return "dark";
    const saved = window.localStorage.getItem("echo-theme");
    return saved === "light" || saved === "dark" ? saved : "dark";
  });

  useLenis();

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", theme);
    window.localStorage.setItem("echo-theme", theme);
  }, [theme]);

  return (
    <main>
      <ScrollProgress />
      <div className="blobs" aria-hidden />
      <Hero theme={theme} onToggle={() => setTheme(theme === "dark" ? "light" : "dark")} />
      <Marquee />
      <Features />
      <UseCases />
      <Stats />
      <Faq />
      <Cta />
      <Footer />
    </main>
  );
}
