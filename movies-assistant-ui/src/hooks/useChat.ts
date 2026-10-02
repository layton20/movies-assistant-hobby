import { useState, useCallback, useRef } from "react";
import type { ChatMessage, Message, SseEvent } from "../types/chat";

const AGENT_ID = "movie_assistant";

interface UseChatReturn {
  messages: ChatMessage[];
  streamingContent: string;
  isStreaming: boolean;
  error: string | null;
  sendMessage: (content: string) => Promise<void>;
}

export function useChat(): UseChatReturn {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [streamingContent, setStreamingContent] = useState("");
  const [isStreaming, setIsStreaming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nextMessageId = useRef(0);

  const sendMessage = useCallback(
    async (content: string) => {
      if (isStreaming) return;

      setError(null);

      // Build new history before setting state to avoid stale closure
      const updatedHistory: ChatMessage[] = [
        ...messages,
        { id: nextMessageId.current++, role: "user", content },
      ];

      setMessages(updatedHistory);
      setIsStreaming(true);
      setStreamingContent("");

      try {
        const response = await fetch("/api/chat", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({
            agentId: AGENT_ID,
            history: updatedHistory.map(({ role, content }): Message => ({
              role,
              content,
            })),
          }),
        });

        if (!response.ok) throw new Error(`API error: ${response.status}`);

        const reader = response.body!.getReader();
        const decoder = new TextDecoder();
        let buffer = "";
        let assembled = "";

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          // Append decoded bytes to buffer — handles chunks split across reads
          buffer += decoder.decode(value, { stream: true });

          // Process complete lines, keep any incomplete line in the buffer
          const lines = buffer.split("\n");
          buffer = lines.pop() ?? "";

          for (const line of lines) {
            if (!line.startsWith("data: ")) continue;
            const rawData = line.slice(6).trim();
            if (!rawData) continue;

            try {
              const event = JSON.parse(rawData) as SseEvent;

              if (event.type === "chunk" && event.delta) {
                assembled += event.delta;
                setStreamingContent(assembled);
              } else if (event.type === "done") {
                const assistantMessage: ChatMessage = {
                  id: nextMessageId.current++,
                  role: "assistant",
                  content: assembled,
                };
                setMessages((prev) => [...prev, assistantMessage]);
                setStreamingContent("");
              } else if (event.type === "error") {
                setError(event.error ?? "An unknown error occurred");
              }
            } catch {
              /* ignore malformed lines */
            }
          }
        }
      } catch (err) {
        setError(
          err instanceof Error ? err.message : "Failed to reach the server",
        );
        setStreamingContent("");
      } finally {
        setIsStreaming(false);
      }
    },
    [messages, isStreaming],
  );

  return { messages, streamingContent, isStreaming, error, sendMessage };
}
