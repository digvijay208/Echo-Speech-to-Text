import { useEffect, useRef } from "react";

/* Drifting aurora particle field that sits behind the hero waveform.
   Particles slowly float upward, twinkle, and are attracted to the cursor. */
export default function ParticleField({ theme }: { theme: "light" | "dark" }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const themeRef = useRef(theme);
  themeRef.current = theme;

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    let raf = 0;
    const mouse = { x: -9999, y: -9999 };

    type P = {
      x: number;
      y: number;
      vx: number;
      vy: number;
      r: number;
      phase: number;
      speed: number;
      hue: number;
    };
    let particles: P[] = [];

    const resize = () => {
      const parent = canvas.parentElement;
      canvas.width = parent ? parent.clientWidth : window.innerWidth;
      canvas.height = parent ? parent.clientHeight : window.innerHeight;
      const count = Math.min(90, Math.floor((canvas.width * canvas.height) / 16000));
      particles = Array.from({ length: count }, () => ({
        x: Math.random() * canvas.width,
        y: Math.random() * canvas.height,
        vx: (Math.random() - 0.5) * 0.18,
        vy: -0.12 - Math.random() * 0.3,
        r: 0.6 + Math.random() * 1.8,
        phase: Math.random() * Math.PI * 2,
        speed: 0.008 + Math.random() * 0.02,
        hue: Math.random(),
      }));
    };

    const draw = () => {
      const dark = themeRef.current === "dark";
      ctx.clearRect(0, 0, canvas.width, canvas.height);

      for (const p of particles) {
        p.phase += p.speed;
        p.x += p.vx;
        p.y += p.vy;

        // gentle cursor attraction
        const dx = mouse.x - p.x;
        const dy = mouse.y - p.y;
        const dist = Math.hypot(dx, dy);
        if (dist < 200 && dist > 0.001) {
          const f = ((200 - dist) / 200) * 0.06;
          p.x += (dx / dist) * f;
          p.y += (dy / dist) * f;
        }

        if (p.y < -10) {
          p.y = canvas.height + 10;
          p.x = Math.random() * canvas.width;
        }
        if (p.x < -10) p.x = canvas.width + 10;
        if (p.x > canvas.width + 10) p.x = -10;

        const twinkle = 0.35 + 0.65 * (0.5 + 0.5 * Math.sin(p.phase));
        const alpha = twinkle * (dark ? 0.7 : 0.45);
        const color =
          p.hue > 0.72
            ? dark
              ? `rgba(150, 125, 255, ${alpha})`
              : `rgba(90, 100, 200, ${alpha * 0.7})`
            : dark
              ? `rgba(111, 214, 255, ${alpha})`
              : `rgba(50, 110, 200, ${alpha * 0.7})`;

        ctx.beginPath();
        ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2);
        ctx.fillStyle = color;
        ctx.shadowColor = dark ? "rgba(111,214,255,0.8)" : "rgba(80,130,220,0.5)";
        ctx.shadowBlur = dark ? 6 : 3;
        ctx.fill();
        ctx.shadowBlur = 0;
      }

      raf = requestAnimationFrame(draw);
    };

    const onMove = (e: MouseEvent) => {
      const rect = canvas.getBoundingClientRect();
      mouse.x = e.clientX - rect.left;
      mouse.y = e.clientY - rect.top;
    };
    const onLeave = () => {
      mouse.x = -9999;
      mouse.y = -9999;
    };

    window.addEventListener("resize", resize);
    canvas.parentElement?.addEventListener("mousemove", onMove);
    canvas.parentElement?.addEventListener("mouseleave", onLeave);

    resize();
    draw();

    return () => {
      cancelAnimationFrame(raf);
      window.removeEventListener("resize", resize);
      canvas.parentElement?.removeEventListener("mousemove", onMove);
      canvas.parentElement?.removeEventListener("mouseleave", onLeave);
    };
  }, []);

  return (
    <canvas
      ref={canvasRef}
      style={{
        position: "absolute",
        inset: 0,
        zIndex: 1,
        width: "100%",
        height: "100%",
        pointerEvents: "none",
      }}
    />
  );
}
