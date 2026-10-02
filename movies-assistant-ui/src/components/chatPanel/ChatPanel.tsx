import { useEffect, useRef } from "react";
import { MessageBubble } from "./MessageBubble";
import { ChatInput } from "./ChatInput";
import { useChat } from "../../hooks/useChat";
import styles from "./ChatPanel.module.css";

export function ChatPanel() {
  const { messages, streamingContent, isStreaming, error, sendMessage } =
    useChat();
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, streamingContent]);

  return (
    <aside className={styles.panel}>
      <header className={styles.header}>
        <span className={styles.dot} />
        Movie Assistant
      </header>

      <div className={styles.messages}>
        {messages.length === 0 && (
          <p className={styles.emptyState}>
            Ask me anything — genres, decades, directors, mood.
          </p>
        )}

        {messages.map((message) => (
          <MessageBubble key={message.id} message={message} />
        ))}

        {/* Streaming message rendered separately so it doesn't flicker */}
        {isStreaming && streamingContent && (
          <MessageBubble
            message={{ role: "assistant", content: streamingContent }}
            isStreaming
          />
        )}

        {error && <p className={styles.error}>{error}</p>}
        <div ref={bottomRef} />
      </div>

      <ChatInput onSend={sendMessage} disabled={isStreaming} />
    </aside>
  );
}
