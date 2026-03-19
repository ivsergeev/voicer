import type { Plugin } from "@opencode-ai/plugin"
import { tool } from "@opencode-ai/plugin"
import { readFileSync } from "fs"

const VOICER_PORT = process.env.VOICER_PORT || "5050"
const RECONNECT_MAX_DELAY = 10_000

function detectVoicerUrl(): string {
  if (process.env.VOICER_URL) return process.env.VOICER_URL

  // Detect WSL2: resolve Windows host IP from /etc/resolv.conf
  try {
    const resolv = readFileSync("/etc/resolv.conf", "utf-8")
    const match = resolv.match(/nameserver\s+(\d+\.\d+\.\d+\.\d+)/)
    if (match && match[1] !== "127.0.0.1") {
      return `ws://${match[1]}:${VOICER_PORT}`
    }
  } catch {
    // Not WSL or no resolv.conf — use localhost
  }

  return `ws://localhost:${VOICER_PORT}`
}

interface VoicerMessage {
  type: "transcription" | "status" | "error" | "claimed"
  text?: string
  status?: string
  message?: string
  timestamp?: string
  active?: boolean
}

export const VoicerPlugin: Plugin = async ({ client }) => {
  // Only activate when explicitly enabled
  const enabled =
    process.env.VOICER_ENABLED === "1" ||
    process.env.VOICER_ENABLED === "true" ||
    !!process.env.VOICER_URL
  if (!enabled) return {}

  const VOICER_URL = detectVoicerUrl()

  let ws: WebSocket | null = null
  let activeSessionId: string | null = null
  let reconnectDelay = 1000
  let reconnectTimer: ReturnType<typeof setTimeout> | null = null
  let transcriptionCount = 0
  let connected = false
  let isClaimed = false
  let lastStatus = "unknown"
  let lastError = ""

  async function resolveActiveSession(): Promise<string | null> {
    if (activeSessionId) return activeSessionId

    try {
      const result = await client.session.list()
      const sessions = result.data
      if (sessions && sessions.length > 0) {
        activeSessionId = sessions[0].id
        log("info", `Resolved session from list: ${activeSessionId}`)
        return activeSessionId
      }
    } catch (err) {
      log("error", `Failed to list sessions: ${err}`)
    }
    return null
  }

  function sendClaim() {
    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: "claim" }))
      log("debug", "Sent claim request")
    }
  }

  function sendRelease() {
    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: "release" }))
      log("debug", "Sent release request")
    }
  }

  function sendAck(status: string, message?: string) {
    if (ws && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify({ type: "ack", status, ...(message && { message }) }))
      log("debug", `Sent ack: ${status}${message ? ` — ${message}` : ""}`)
    }
  }

  function showRichToast(
    message: string,
    variant: "info" | "success" | "warning" | "error" = "info",
    title?: string,
    duration?: number,
  ) {
    client.tui
      .publish({
        body: {
          type: "tui.toast.show" as const,
          properties: { message, variant, ...(title && { title }), ...(duration && { duration }) },
        },
      })
      .catch(() => {})
  }

  function log(level: "debug" | "info" | "warn" | "error", message: string) {
    client.app
      .log({
        body: {
          service: "voicer",
          level,
          message,
        },
      })
      .catch(() => {})
  }

  function connectToVoicer() {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer)
      reconnectTimer = null
    }

    log("info", `Connecting to Voicer at ${VOICER_URL}...`)

    try {
      ws = new WebSocket(VOICER_URL)
    } catch (err) {
      lastError = `Failed to create WebSocket: ${err}`
      log("error", lastError)
      scheduleReconnect()
      return
    }

    ws.onopen = () => {
      connected = true
      lastError = ""
      reconnectDelay = 1000
      log("info", `Connected to Voicer at ${VOICER_URL}`)
      showRichToast("Connected", "success", "Voicer")
    }

    ws.onmessage = async (event) => {
      let msg: VoicerMessage
      try {
        msg = JSON.parse(typeof event.data === "string" ? event.data : event.data.toString())
      } catch {
        log("warn", `Invalid message from Voicer: ${event.data}`)
        return
      }

      switch (msg.type) {
        case "claimed": {
          const wasClaimed = isClaimed
          isClaimed = msg.active === true
          if (isClaimed && !wasClaimed) {
            log("info", "This instance is now the active voice target")
            showRichToast("Voice input active", "success", "Voicer")
          } else if (!isClaimed && wasClaimed) {
            log("info", "Another instance claimed voice input")
            showRichToast("Voice input moved to another instance", "warning", "Voicer")
          }
          break
        }

        case "transcription": {
          if (!msg.text?.trim()) break

          const text = msg.text.trim()
          log("info", `Received transcription: "${text}"`)

          // Use appendPrompt + submitPrompt for visible UI interaction
          try {
            const appendResult = await client.tui.appendPrompt({
              body: { text },
            })
            if (appendResult.error) {
              lastError = `appendPrompt error: ${JSON.stringify(appendResult.error)}`
              log("error", lastError)
              showRichToast(lastError, "error", "Voicer")
              break
            }

            const submitResult = await client.tui.submitPrompt()
            if (submitResult.error) {
              lastError = `submitPrompt error: ${JSON.stringify(submitResult.error)}`
              log("error", lastError)
              showRichToast(lastError, "error", "Voicer")
              break
            }

            transcriptionCount++
            lastError = ""
            log("info", `Sent voice prompt: "${text}"`)
            sendAck("submitted")
          } catch (err) {
            lastError = `Failed to send prompt: ${err}`
            log("error", lastError)
            showRichToast("Failed to send prompt", "error", "Voicer")
            sendAck("error", lastError)
          }
          break
        }

        case "status": {
          lastStatus = msg.status || "unknown"
          if (isClaimed) {
            if (msg.status === "recording") {
              showRichToast("Listening...", "info", "Voicer")
            } else if (msg.status === "processing") {
              showRichToast("Recognizing...", "info", "Voicer")
            }
          }
          break
        }

        case "error": {
          lastError = msg.message || "unknown error"
          log("error", `Voicer error: ${msg.message}`)
          if (isClaimed) {
            showRichToast(msg.message || "Unknown error", "error", "Voicer")
          }
          break
        }
      }
    }

    ws.onclose = () => {
      connected = false
      isClaimed = false
      lastStatus = "disconnected"
      log("info", "Disconnected from Voicer")
      scheduleReconnect()
    }

    ws.onerror = (err) => {
      lastError = `WebSocket error: ${err}`
      log("error", lastError)
      // onclose will fire after onerror
    }
  }

  function scheduleReconnect() {
    if (reconnectTimer) return
    reconnectTimer = setTimeout(() => {
      reconnectTimer = null
      connectToVoicer()
      reconnectDelay = Math.min(reconnectDelay * 1.5, RECONNECT_MAX_DELAY)
    }, reconnectDelay)
  }

  // Start connection
  connectToVoicer()

  return {
    "experimental.chat.system.transform": async (_input, output) => {
      const lines: string[] = []
      lines.push(`[Voicer] Voice input: ${connected ? "connected" : "disconnected"}`)
      if (connected) {
        lines.push(`[Voicer] This instance: ${isClaimed ? "ACTIVE (receiving voice)" : "inactive"}`)
      }
      lines.push(`[Voicer] IMPORTANT: The voicer_new_session tool MUST ONLY be called when the user EXPLICITLY asks to start a new session (e.g. "новая сессия", "new session", "начни новую сессию"). NEVER call it on your own initiative — only on a direct user request.`)
      output.system.push(lines.join(". "))
    },

    event: async ({ event }) => {
      // Log all events to discover available event types
      log("debug", `Event: ${event.type} ${JSON.stringify(event.properties ?? {})}`)

      if (
        event.type === "session.created" ||
        event.type === "session.updated"
      ) {
        const props = event.properties as { info?: { id?: string } }
        if (typeof props?.info?.id === "string") {
          activeSessionId = props.info.id
          log("debug", `Active session updated: ${activeSessionId}`)
        }

        // Auto-claim on new session start (app launch or manual creation)
        if (event.type === "session.created") {
          sendClaim()
        }
      }

      // Detect assistant response lifecycle
      if (event.type === "message.created") {
        const props = event.properties as { role?: string }
        if (props?.role === "assistant" && isClaimed) {
          sendAck("responding")
        }
      }

      if (event.type === "message.completed" || event.type === "message.updated") {
        const props = event.properties as { role?: string; status?: string }
        if (props?.role === "assistant" && isClaimed) {
          if (event.type === "message.completed" || props?.status === "completed") {
            sendAck("done")
          }
        }
      }
    },

    tool: {
      voicer_status: tool({
        description:
          "Check Voicer voice input connection status. Returns whether Voicer is connected, whether this instance is the active voice target, the number of voice commands sent, and the current microphone state.",
        args: {},
        async execute() {
          return JSON.stringify({
            connected,
            isClaimed,
            voicerUrl: VOICER_URL,
            transcriptionCount,
            microphoneStatus: lastStatus,
            activeSessionId,
            lastError: lastError || null,
          })
        },
      }),

      voicer_new_session: tool({
        description:
          "Start a new opencode session. Use when the user asks to start a new session, e.g. 'новая сессия', 'new session', 'начни новую сессию'. Creates a new session and navigates TUI to it.",
        args: {},
        async execute() {
          try {
            // Create a new session via SDK
            const createResult = await client.session.create({
              body: {},
            })
            if (createResult.error) {
              return JSON.stringify({ success: false, error: JSON.stringify(createResult.error) })
            }

            const newId = createResult.data?.id
            if (!newId) {
              return JSON.stringify({ success: false, error: "No session ID returned" })
            }

            activeSessionId = newId
            log("info", `New session created: ${newId}`)

            // Note: claim is auto-sent via session.created event handler

            // Navigate TUI to the new session via command
            await client.tui.publish({
              body: {
                type: "tui.command.execute" as const,
                properties: { command: "session.new" },
              },
            }).catch(() => {})

            showRichToast("New session started", "success", "Voicer")
            return JSON.stringify({ success: true, sessionId: newId })
          } catch (err) {
            return JSON.stringify({ success: false, error: String(err) })
          }
        },
      }),

      voicer_claim: tool({
        description:
          "Claim this opencode instance as the active voice input target. Only the claimed instance receives voice transcriptions from Voicer. Use when voice input should be directed to this session instead of another opencode instance.",
        args: {},
        async execute() {
          sendClaim()
          await new Promise((r) => setTimeout(r, 100))
          return JSON.stringify({
            success: true,
            isClaimed,
          })
        },
      }),

      voicer_release: tool({
        description:
          "Release the voice input claim. This instance will stop receiving voice transcriptions. The active role may be auto-promoted to another connected instance.",
        args: {},
        async execute() {
          sendRelease()
          await new Promise((r) => setTimeout(r, 100))
          return JSON.stringify({
            success: true,
            isClaimed,
          })
        },
      }),
    },
  }
}
