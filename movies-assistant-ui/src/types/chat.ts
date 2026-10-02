export interface Message {
  role: "user" | "assistant";
  content: string;
}

export interface ChatMessage extends Message {
  id: number;
}

export interface SseEvent {
  type: "chunk" | "done" | "error" | "trace";
  delta?: string;
  error?: string;
  traceId?: string;
}
