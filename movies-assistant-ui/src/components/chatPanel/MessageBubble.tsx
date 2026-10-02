import ReactMarkdown, { defaultUrlTransform } from "react-markdown";
import remarkGfm from "remark-gfm";
import { useNavigate } from "react-router-dom";
import type { Message } from "../../types/chat";
import styles from "./MessageBubble.module.css";

interface MessageBubbleProps {
  message: Message;
  isStreaming?: boolean;
}

export function MessageBubble({
  message,
  isStreaming = false,
}: MessageBubbleProps) {
  const navigate = useNavigate();

  return (
    <div className={`${styles.bubble} ${styles[message.role]}`}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        // The agent links titles as [Title](movie:{id}); react-markdown strips
        // unknown protocols by default, so let movie: through
        urlTransform={(url) =>
          url.startsWith("movie:") ? url : defaultUrlTransform(url)
        }
        // Model output is untrusted: images would auto-fire requests to
        // attacker URLs (data exfiltration), so never render them
        disallowedElements={["img"]}
        components={{
          // Only movie:{id} links are rendered as links. Anything else the model
          // emits (external URLs, arbitrary routes) is shown as plain text
          a({ href, children }) {
            const id = href?.match(/^movie:(\d+)$/)?.[1];
            if (id) {
              return (
                <button
                  className={styles.internalLink}
                  onClick={() => navigate(`/movies/${id}`)}
                >
                  {children}
                </button>
              );
            }
            return <span>{children}</span>;
          },
        }}
      >
        {message.content}
      </ReactMarkdown>
      {isStreaming && <span className={styles.cursor} />}
    </div>
  );
}
