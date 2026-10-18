using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using NATS.Client.Core;
using NATS.Client.Serializers.Json;

namespace VroksNet.Aspire.Sample.WebApi.Controllers;

[ApiController]
[Route("api/books")]
public sealed class BooksController(INatsConnection nats) : ControllerBase
{
    [HttpGet("{bookId}", Name = nameof(GetBookById))]
    public ActionResult<BookResponse> GetBookById(string bookId) =>
        Ok(new BookResponse(bookId, "The Example Book", "A. Sample"));

    [HttpPost]
    public async Task<ActionResult<BookResponse>> CreateBook(
        CreateBookRequest request,
        CancellationToken cancellationToken)
    {
        var book = new BookResponse($"book-{Guid.NewGuid():N}", request.Title, request.Author);

        await nats.PublishAsync(
            "books.created",
            new BookCreatedEvent(book.Id, book.Title),
            serializer: NatsJsonSerializer<BookCreatedEvent>.Default,
            cancellationToken: cancellationToken);

        return CreatedAtAction(nameof(GetBookById), new { bookId = book.Id }, book);
    }
}

public sealed record CreateBookRequest(string Title, string Author);

public sealed record BookResponse(string Id, string Title, string Author);

public sealed record BookCreatedEvent(
    [property: JsonPropertyName("bookId")] string BookId,
    [property: JsonPropertyName("title")] string Title);
