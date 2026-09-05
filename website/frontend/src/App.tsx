import { Routes, Route, useLocation } from "react-router-dom";
import { useEffect } from "react";
import Nav from "./components/Nav";
import Hero from "./components/Hero";
import Marquee from "./components/Marquee";
import Dictate from "./components/Dictate";
import Translate from "./components/Translate";
import AskAnything from "./components/AskAnything";
import Privacy from "./components/Privacy";
import Devices from "./components/Devices";
import Testimonials from "./components/Testimonials";
import FinalCta from "./components/FinalCta";
import AskAi from "./components/AskAi";
import Footer from "./components/Footer";
import Downloads from "./components/Downloads";
import { ScrollProgress } from "./components/motion-primitives";
import { useLenis } from "./hooks/useLenis";

function Landing() {
  return (
    <>
      <Hero />
      <Marquee />
      <Dictate />
      <Translate />
      <AskAnything />
      <Privacy />
      <Devices />
      <Testimonials />
      <FinalCta />
      <AskAi />
    </>
  );
}

export default function App() {
  useLenis();
  const { pathname, hash } = useLocation();

  /* When user lands on "/" with a hash (e.g. /#privacy from a footer link
     on the /downloads page), scroll to that section after mount. */
  useEffect(() => {
    if (pathname !== "/") return;
    if (!hash) {
      window.scrollTo({ top: 0 });
      return;
    }
    const id = hash.slice(1);
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: "smooth", block: "start" });
  }, [pathname, hash]);

  return (
    <>
      <ScrollProgress />
      <Nav />
      <Routes>
        <Route path="/" element={<Landing />} />
        <Route path="/downloads" element={<Downloads />} />
      </Routes>
      <Footer />
    </>
  );
}
