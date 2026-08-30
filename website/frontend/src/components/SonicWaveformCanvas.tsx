import { useEffect, useRef } from "react";

export default function SonicWaveformCanvas({ theme }: { theme: "light" | "dark" }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const themeRef = useRef(theme);
  themeRef.current = theme;

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    let animationFrameId: number;
    const mouse = { x: -9999, y: -9999 };
    let time = 0;

    const resizeCanvas = () => {
      const parent = canvas.parentElement;
      canvas.width = parent ? parent.clientWidth : window.innerWidth;
      canvas.height = parent ? parent.clientHeight : window.innerHeight;
    };

    const draw = () => {
      const dark = themeRef.current === "dark";
      ctx.fillStyle = dark ? "rgba(6, 10, 20, 0.16)" : "rgba(238, 240, 242, 0.2)";
      ctx.fillRect(0, 0, canvas.width, canvas.height);

      const lineCount = 56;
      const segmentCount = 90;
      const midY = canvas.height / 2;

      for (let i = 0; i < lineCount; i++) {
        ctx.beginPath();
        const progress = i / lineCount;
        const intensity = Math.sin(progress * Math.PI);

        const r = dark ? Math.round(64 + intensity * 32) : Math.round(30 + intensity * 20);
        const g = dark ? Math.round(190 + intensity * 40) : Math.round(90 + intensity * 40);
        const b = dark ? 255 : Math.round(180 + intensity * 40);
        ctx.strokeStyle = `rgba(${r}, ${g}, ${b}, ${intensity * (dark ? 0.42 : 0.36)})`;
        ctx.lineWidth = 1.4;

        for (let j = 0; j < segmentCount + 1; j++) {
          const x = (j / segmentCount) * canvas.width;

          const distToMouse = Math.hypot(x - mouse.x, midY - mouse.y);
          const mouseEffect = Math.max(0, 1 - distToMouse / 380);

          const noise = Math.sin(j * 0.11 + time + i * 0.2) * 18;
          const spike =
            Math.cos(j * 0.21 + time + i * 0.1) *
            Math.sin(j * 0.05 + time) *
            46;
          const y = midY + noise + spike * (1 + mouseEffect * 2.2);

          if (j === 0) ctx.moveTo(x, y);
          else ctx.lineTo(x, y);
        }
        ctx.stroke();
      }

      time += 0.02;
      animationFrameId = requestAnimationFrame(draw);
    };

    const handleMouseMove = (event: MouseEvent) => {
      const rect = canvas.getBoundingClientRect();
      mouse.x = event.clientX - rect.left;
      mouse.y = event.clientY - rect.top;
    };
    const handleMouseLeave = () => {
      mouse.x = -9999;
      mouse.y = -9999;
    };

    window.addEventListener("resize", resizeCanvas);
    canvas.parentElement?.addEventListener("mousemove", handleMouseMove);
    canvas.parentElement?.addEventListener("mouseleave", handleMouseLeave);

    resizeCanvas();
    draw();

    return () => {
      cancelAnimationFrame(animationFrameId);
      window.removeEventListener("resize", resizeCanvas);
      canvas.parentElement?.removeEventListener("mousemove", handleMouseMove);
      canvas.parentElement?.removeEventListener("mouseleave", handleMouseLeave);
    };
  }, []);

  return (
    <canvas
      ref={canvasRef}
      style={{
        position: "absolute",
        inset: 0,
        zIndex: 0,
        width: "100%",
        height: "100%",
        background: theme === "dark" ? "#060a14" : "#eef0f2",
        transition: "background 240ms ease",
      }}
    />
  );
}
