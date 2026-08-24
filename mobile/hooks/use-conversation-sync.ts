import { useEffect, useRef } from "react";

/**
 * Runs lightweight LAN refreshes for a visible conversation surface. The callback is kept in a
 * ref so polling does not restart on each render, while the in-flight guard prevents overlapping
 * requests on slower local networks. An AbortController is created per tick and aborted on
 * cleanup so in-flight fetches are cancelled when the component unmounts or polling stops.
 */
export function useConversationSync({
  enabled,
  intervalMs,
  refresh,
}: {
  enabled: boolean;
  intervalMs: number;
  refresh: (signal: AbortSignal) => Promise<void>;
}) {
  const refreshRef = useRef(refresh);
  const inFlight = useRef(false);
  useEffect(() => { refreshRef.current = refresh; }, [refresh]);

  useEffect(() => {
    if (!enabled) return;
    let disposed = false;
    const tick = async () => {
      if (disposed || inFlight.current) return;
      inFlight.current = true;
      const controller = new AbortController();
      try { await refreshRef.current(controller.signal); }
      catch { /* The next interval retries without disrupting the active UI. */ }
      finally {
        controller.abort();
        inFlight.current = false;
      }
    };
    const timer = setInterval(() => { void tick(); }, intervalMs);
    return () => { disposed = true; clearInterval(timer); };
  }, [enabled, intervalMs]);
}
