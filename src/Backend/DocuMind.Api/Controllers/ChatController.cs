using System.Text.Json;
using Anthropic.Exceptions;
using DocuMind.Api.Chat;
using Microsoft.AspNetCore.Mvc;

namespace DocuMind.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController(ChatService chat, ILogger<ChatController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Streams the answer as server-sent events: sources, block (a new text span starts), text, citation, done, error
    [HttpPost]
    public async Task Post(ChatRequest request, CancellationToken cancellationToken)
    {
        if (request.Messages[^1].Role != "user")
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new ProblemDetails { Detail = "The last message must be from the user." },
                cancellationToken);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var streamEvent in chat.StreamAnswerAsync(request, cancellationToken))
            {
                switch (streamEvent)
                {
                    case SourcesStreamEvent sources:
                        await WriteEventAsync("sources", sources.Sources, cancellationToken);
                        break;
                    case BlockStartStreamEvent:
                        await WriteEventAsync("block", new { }, cancellationToken);
                        break;
                    case TextStreamEvent text:
                        await WriteEventAsync("text", new ChatTextEvent(text.Text), cancellationToken);
                        break;
                    case CitationStreamEvent citation:
                        await WriteEventAsync("citation",
                            new ChatCitationEvent(citation.SourceIndex, citation.CitedText), cancellationToken);
                        break;
                    case DoneStreamEvent done:
                        await WriteEventAsync("done", new ChatDoneEvent(done.StopReason), cancellationToken);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected; nothing left to send
        }
        catch (Exception ex)
        {
            // Headers are already sent, so report the failure as a stream event instead of a status code
            logger.LogError(ex, "Chat request failed");
            await WriteEventAsync("error", new ChatErrorEvent(ToUserMessage(ex)), CancellationToken.None);
        }
    }

    private async Task WriteEventAsync<T>(string name, T payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await Response.WriteAsync($"event: {name}\ndata: {json}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static string ToUserMessage(Exception ex) => ex switch
    {
        AnthropicRateLimitException => "Claude is rate limiting requests right now. Please try again in a moment.",
        Anthropic5xxException => "Claude is temporarily unavailable. Please try again shortly.",
        AnthropicUnauthorizedException => "The Anthropic API key is missing or invalid.",
        HttpRequestException => "Could not reach an AI service (Voyage or Claude). Please try again.",
        InvalidOperationException => ex.Message,
        _ => "Something went wrong while answering. Please try again.",
    };
}
