using Grpc.Core;
using Samples.Books.V1;

namespace VroksNet.Aspire.Sample.WebApi.Services;

public sealed class BooksGrpcService : Books.BooksBase
{
    public override Task<GetBookResponse> GetBook(GetBookRequest request, ServerCallContext context) =>
        Task.FromResult(new GetBookResponse
        {
            BookId = request.BookId,
            Title = "The Example Book",
            Author = "A. Sample"
        });
}
