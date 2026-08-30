import { useState, type FormEvent } from "react";
import { motion, AnimatePresence } from "motion/react";
import { Mail, Check, Loader2 } from "lucide-react";

type Status = "idle" | "submitting" | "success" | "error";

export default function WaitlistForm() {
  const [email, setEmail] = useState("");
  const [status, setStatus] = useState<Status>("idle");
  const [message, setMessage] = useState("");

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (status === "submitting") return;

    setStatus("submitting");
    setMessage("");

    try {
      const res = await fetch("/api/waitlist", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: email.trim() }),
      });
      const data = (await res.json().catch(() => ({}))) as { ok?: boolean; message?: string; error?: string };
      if (!res.ok) {
        setStatus("error");
        setMessage(data.error || "Something went wrong. Try again.");
        return;
      }
      setStatus("success");
      setMessage(data.message || "Thanks — we'll be in touch.");
      setEmail("");
    } catch {
      setStatus("error");
      setMessage("Network error. Check your connection and retry.");
    }
  }

  return (
    <form className="waitlist" onSubmit={handleSubmit} noValidate>
      <label className="waitlist-label" htmlFor="waitlist-email">
        Or join the waitlist for updates
      </label>

      <div className="waitlist-row">
        <div className="waitlist-input-wrap">
          <Mail className="waitlist-input-icon" style={{ width: 16, height: 16 }} />
          <input
            id="waitlist-email"
            type="email"
            inputMode="email"
            autoComplete="email"
            placeholder="you@somewhere.com"
            value={email}
            disabled={status === "submitting" || status === "success"}
            onChange={(e) => {
              setEmail(e.target.value);
              if (status === "error") setStatus("idle");
            }}
            required
            aria-invalid={status === "error"}
            aria-describedby="waitlist-msg"
          />
        </div>

        <motion.button
          type="submit"
          className="btn btn-navy waitlist-submit"
          disabled={status === "submitting" || status === "success"}
          whileHover={status === "idle" || status === "error" ? { scale: 1.04 } : {}}
          whileTap={status === "idle" || status === "error" ? { scale: 0.96 } : {}}
          transition={{ type: "spring", stiffness: 400, damping: 17 }}
        >
          {status === "submitting" ? (
            <Loader2 style={{ width: 16, height: 16, animation: "spin 0.9s linear infinite" }} />
          ) : status === "success" ? (
            <Check style={{ width: 16, height: 16 }} />
          ) : null}
          <span>
            {status === "submitting"
              ? "Joining…"
              : status === "success"
              ? "On the list"
              : "Notify me"}
          </span>
        </motion.button>
      </div>

      <AnimatePresence>
        {message && (
          <motion.p
            id="waitlist-msg"
            key={status + message}
            className={`waitlist-msg waitlist-msg-${status}`}
            initial={{ opacity: 0, y: -4 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.25 }}
          >
            {message}
          </motion.p>
        )}
      </AnimatePresence>
    </form>
  );
}
