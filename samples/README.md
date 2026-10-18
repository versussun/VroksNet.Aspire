# VroksNet Aspire sample specifications

The sample AppHost mounts these files read-only into VroksNet's `/app/provisioning/specs` directory. VroksNet imports them during startup:

- `specifications/books.openapi.yaml` — OpenAPI 3.0 REST contract.
- `specifications/book-events.asyncapi.yaml` — AsyncAPI 3.0 event contract.
- `specifications/books.proto` — Protocol Buffers service contract.

The sample Web API implements the REST and gRPC contracts and publishes `BookCreated` messages to the `books.created` NATS subject. The AppHost passes the `events` NATS connection to both the API and VroksNet, and enables VroksNet's gRPC endpoint. Run the Aspire AppHost with Docker available, then open the VroksNet Admin UI from the Aspire dashboard to see the imported specifications.

## Try the sample API

Copy the sample Web API's address from the Aspire dashboard and use it as `API` below:

```bash
API=http://localhost:5034

# OpenAPI: fetch a sample book.
curl "$API/api/books/book-123"

# OpenAPI: create a book; this publishes a BookCreated event to NATS.
curl -i -X POST "$API/api/books" \
  -H 'Content-Type: application/json' \
  -d '{"title":"The Example Book","author":"A. Sample"}'
```

To observe the AsyncAPI event, subscribe to `books.created` with a NATS client using the `events` connection URL from the Aspire dashboard before sending the POST request. The JSON payload contains `bookId` and `title`.

The sample API serves the protobuf RPC over h2c on the same HTTP endpoint. With `grpcurl` installed, invoke it using the checked-in proto:

```bash
grpcurl -plaintext -proto samples/specifications/books.proto \
	-d '{"bookId":"book-123"}' \
  localhost:5034 samples.books.v1.Books/GetBook
```

Use the actual Web API address shown in the dashboard if Aspire assigned a different port.
