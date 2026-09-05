/* Shared motion primitives — patterns ported from Inspira UI / Animate UI,
   re-implemented for React + motion. All respect prefers-reduced-motion. */

import {
  motion,
  useMotionValue,
  useSpring,
  useTransform,
  useInView,
  useReducedMotion,
  animate,
} from "motion/react";
import {
  useEffect,
  useRef,
  useState,
  type ReactNode,
  type CSSProperties,
  type MouseEvent as ReactMouseEvent,
} from "react";

/* ------------------------------------------------------------------ */
/* Reveal — scroll-triggered fade/slide wrapper                        */
/* ------------------------------------------------------------------ */
export function Reveal({
  children,
  delay = 0,
  y = 28,
  duration = 0.75,
  className,
  style,
}: {
  children: ReactNode;
  delay?: number;
  y?: number;
  duration?: number;
  className?: string;
  style?: CSSProperties;
}) {
  const reduce = useReducedMotion();
  return (
    <motion.div
      className={className}
      style={style}
      initial={{ opacity: 0, y: reduce ? 0 : y }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: "0px 0px -12% 0px" }}
      transition={{ duration, delay, ease: [0.21, 0.65, 0.36, 1] }}
    >
      {children}
    </motion.div>
  );
}

/* ------------------------------------------------------------------ */
/* WordsReveal — staggered word-by-word rise (Animate UI style)        */
/* ------------------------------------------------------------------ */
export function WordsReveal({
  text,
  className,
  delay = 0,
  stagger = 0.09,
  once = true,
  immediate = false,
}: {
  text: string;
  className?: string;
  delay?: number;
  stagger?: number;
  once?: boolean;
  /** Skip in-view trigger; animate on mount (for above-the-fold content). */
  immediate?: boolean;
}) {
  const reduce = useReducedMotion();
  const words = text.split(" ");
  return (
    <span className={className} style={{ display: "inline-block" }}>
      {words.map((word, i) => (
        <span
          key={`${word}-${i}`}
          style={{
            display: "inline-block",
            overflow: "hidden",
            verticalAlign: "bottom",
            paddingBottom: "0.18em",
            marginBottom: "-0.18em",
          }}
        >
          <motion.span
            style={{ display: "inline-block", willChange: "transform" }}
            initial={{ y: reduce ? 0 : "110%", opacity: 0 }}
            animate={immediate ? { y: 0, opacity: 1 } : undefined}
            whileInView={immediate ? undefined : { y: 0, opacity: 1 }}
            viewport={immediate ? undefined : { once, margin: "0px 0px 200px 0px" }}
            transition={{
              duration: 0.7,
              delay: delay + i * stagger,
              ease: [0.21, 0.65, 0.36, 1],
            }}
          >
            {word}
            {i < words.length - 1 ? "\u00A0" : ""}
          </motion.span>
        </span>
      ))}
    </span>
  );
}

/* ------------------------------------------------------------------ */
/* BlurReveal — Inspira-style blur fade-up                             */
/* ------------------------------------------------------------------ */
export function BlurReveal({
  children,
  delay = 0,
  className,
}: {
  children: ReactNode;
  delay?: number;
  className?: string;
}) {
  const reduce = useReducedMotion();
  return (
    <motion.div
      className={className}
      initial={{ opacity: 0, filter: reduce ? "blur(0px)" : "blur(12px)", y: reduce ? 0 : 18 }}
      whileInView={{ opacity: 1, filter: "blur(0px)", y: 0 }}
      viewport={{ once: true, margin: "0px 0px -10% 0px" }}
      transition={{ duration: 0.8, delay, ease: "easeOut" }}
    >
      {children}
    </motion.div>
  );
}

/* ------------------------------------------------------------------ */
/* Magnetic — element gently follows the cursor (Animate UI)           */
/* ------------------------------------------------------------------ */
export function Magnetic({
  children,
  strength = 0.35,
  className,
}: {
  children: ReactNode;
  strength?: number;
  className?: string;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const reduce = useReducedMotion();
  const x = useMotionValue(0);
  const y = useMotionValue(0);
  const sx = useSpring(x, { stiffness: 180, damping: 14, mass: 0.25 });
  const sy = useSpring(y, { stiffness: 180, damping: 14, mass: 0.25 });

  const onMove = (e: ReactMouseEvent) => {
    if (reduce || !ref.current) return;
    const rect = ref.current.getBoundingClientRect();
    const relX = e.clientX - (rect.left + rect.width / 2);
    const relY = e.clientY - (rect.top + rect.height / 2);
    x.set(relX * strength);
    y.set(relY * strength);
  };
  const onLeave = () => {
    x.set(0);
    y.set(0);
  };

  return (
    <motion.div
      ref={ref}
      className={className}
      style={{ x: sx, y: sy, display: "inline-block" }}
      onMouseMove={onMove}
      onMouseLeave={onLeave}
    >
      {children}
    </motion.div>
  );
}

/* ------------------------------------------------------------------ */
/* TiltCard — 3D perspective tilt on hover (Inspira style)             */
/* ------------------------------------------------------------------ */
export function TiltCard({
  children,
  className,
  maxTilt = 7,
  scale = 1.015,
  style,
}: {
  children: ReactNode;
  className?: string;
  maxTilt?: number;
  scale?: number;
  style?: CSSProperties;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const reduce = useReducedMotion();
  const rx = useMotionValue(0);
  const ry = useMotionValue(0);
  const srx = useSpring(rx, { stiffness: 220, damping: 18 });
  const sry = useSpring(ry, { stiffness: 220, damping: 18 });

  const onMove = (e: ReactMouseEvent) => {
    if (reduce || !ref.current) return;
    const rect = ref.current.getBoundingClientRect();
    const px = (e.clientX - rect.left) / rect.width - 0.5;
    const py = (e.clientY - rect.top) / rect.height - 0.5;
    ry.set(px * maxTilt * 2);
    rx.set(-py * maxTilt * 2);
  };
  const onLeave = () => {
    rx.set(0);
    ry.set(0);
  };

  return (
    <div style={{ perspective: 1100, ...style }} className={className}>
      <motion.div
        ref={ref}
        style={{
          rotateX: srx,
          rotateY: sry,
          transformStyle: "preserve-3d",
          height: "100%",
        }}
        whileHover={{ scale }}
        onMouseMove={onMove}
        onMouseLeave={onLeave}
      >
        {children}
      </motion.div>
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* Spotlight — cursor-following radial glow inside a card              */
/* ------------------------------------------------------------------ */
export function Spotlight({ children, className }: { children: ReactNode; className?: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const mx = useMotionValue(-400);
  const my = useMotionValue(-400);

  const onMove = (e: ReactMouseEvent) => {
    if (!ref.current) return;
    const rect = ref.current.getBoundingClientRect();
    mx.set(e.clientX - rect.left);
    my.set(e.clientY - rect.top);
  };

  const background = useTransform(
    [mx, my],
    ([x, y]) =>
      `radial-gradient(340px circle at ${x}px ${y}px, rgba(111, 214, 255, 0.13), transparent 70%)`
  );

  return (
    <div
      ref={ref}
      className={className}
      onMouseMove={onMove}
      style={{ position: "relative" }}
    >
      <motion.div
        aria-hidden
        style={{
          position: "absolute",
          inset: 0,
          borderRadius: "inherit",
          background,
          pointerEvents: "none",
          zIndex: 1,
        }}
      />
      {children}
    </div>
  );
}

/* ------------------------------------------------------------------ */
/* CountUp — number animates to target when scrolled into view         */
/* ------------------------------------------------------------------ */
export function CountUp({
  to,
  suffix = "",
  prefix = "",
  duration = 1.6,
  className,
}: {
  to: number;
  suffix?: string;
  prefix?: string;
  duration?: number;
  className?: string;
}) {
  const ref = useRef<HTMLSpanElement>(null);
  const inView = useInView(ref, { once: true, margin: "0px 0px -15% 0px" });
  const reduce = useReducedMotion();
  const [display, setDisplay] = useState(0);

  useEffect(() => {
    if (!inView) return;
    if (reduce) {
      setDisplay(to);
      return;
    }
    const controls = animate(0, to, {
      duration,
      ease: [0.16, 1, 0.3, 1],
      onUpdate: (v) => setDisplay(Math.round(v)),
    });
    return () => controls.stop();
  }, [inView, to, duration, reduce]);

  return (
    <span ref={ref} className={className}>
      {prefix}
      {display}
      {suffix}
    </span>
  );
}

/* ------------------------------------------------------------------ */
/* ScrollProgress — thin accent bar pinned to the top of the viewport  */
/* ------------------------------------------------------------------ */
import { useScroll, useSpring as useSpringMotion } from "motion/react";

export function ScrollProgress() {
  const { scrollYProgress } = useScroll();
  const scaleX = useSpringMotion(scrollYProgress, {
    stiffness: 120,
    damping: 26,
    restDelta: 0.001,
  });
  return (
    <motion.div
      aria-hidden
      style={{
        position: "fixed",
        top: 0,
        left: 0,
        right: 0,
        height: 2,
        transformOrigin: "0%",
        scaleX,
        background: "linear-gradient(90deg, var(--accent), #8c6eff)",
        zIndex: 100,
        pointerEvents: "none",
      }}
    />
  );
}

/* ------------------------------------------------------------------ */
/* GlowBorder — animated conic gradient border (Inspira "moving border") */
/* ------------------------------------------------------------------ */
export function GlowBorder({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div className={className} style={{ position: "relative", borderRadius: "inherit" }}>
      <motion.div
        aria-hidden
        animate={{ rotate: 360 }}
        transition={{ duration: 6, repeat: Infinity, ease: "linear" }}
        style={{
          position: "absolute",
          inset: -1,
          borderRadius: "inherit",
          padding: 1,
          background:
            "conic-gradient(from 0deg, transparent 0%, rgba(111,214,255,0.55) 12%, transparent 26%, transparent 55%, rgba(140,110,255,0.45) 68%, transparent 82%)",
          WebkitMask: "linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0)",
          WebkitMaskComposite: "xor",
          maskComposite: "exclude",
          pointerEvents: "none",
          zIndex: 2,
        }}
      />
      {children}
    </div>
  );
}
