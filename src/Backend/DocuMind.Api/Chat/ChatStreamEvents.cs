namespace DocuMind.Api.Chat;

public abstract record ChatStreamEvent;
public record SourcesStreamEvent(IReadOnlyList<ChatSource> Sources) : ChatStreamEvent;

// A new span of answer text starts; citations that follow support this span
public record BlockStartStreamEvent : ChatStreamEvent;
public record TextStreamEvent(string Text) : ChatStreamEvent;
public record CitationStreamEvent(int SourceIndex, string CitedText) : ChatStreamEvent;

// StopReason uses Claude's vocabulary for every provider: end_turn, max_tokens, refusal, no_results
public record DoneStreamEvent(string? StopReason) : ChatStreamEvent;
