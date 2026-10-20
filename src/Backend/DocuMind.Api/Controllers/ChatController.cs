using System.ClientModel;
using System.Net;
using System.Text.Json;
using Anthropic.Exceptions;
using DocuMind.Api.ApiKeys;
using DocuMind.Api.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using DocuMind.Api.Resilience;

namespace DocuMind.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController(ChatService chat, ILogger<ChatController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Streams the answer as server-sent events: sources, block (a new text span starts), text, citation, done, error
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Expensive)]
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
                    case ConversationStreamEvent conversation:
                        await WriteEventAsync("conversation", new ChatConversationEvent(conversation.ConversationId), cancellationToken);
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
        MissingApiKeyException => ex.Message,
        AnthropicUnauthorizedException or ClientResultException { Status: 401 or 403 }
            or HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } =>
            "The AI provider rejected the API key saved in Settings. Update the key and try again.",
        AnthropicRateLimitException or ClientResultException { Status: 429 }
            or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } =>
            "The AI provider is rate limiting your API key right now. Please try again in a moment.",
        Anthropic5xxException or ClientResultException { Status: >= 500 } =>
            "The AI provider is temporarily unavailable. Please try again shortly.",
        ClientResultException { Status: 404 } =>
            "The model chosen in Settings isn't available for this API key. Pick another model.",
        Polly.CircuitBreaker.BrokenCircuitException =>
            "The AI provider is currently unavailable. Please try again in a minute.",
        TimeoutException => "The AI provider took too long to respond. Please try again.",
        HttpRequestException => "Could not reach an AI service. Please try again.",
        InvalidOperationException => ex.Message,
        _ => "Something went wrong while answering. Please try again.",
    };
}
