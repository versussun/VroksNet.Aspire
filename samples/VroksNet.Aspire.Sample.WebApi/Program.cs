var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
    options.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2));

builder.AddNatsClient("events");
builder.Services.AddControllers();
builder.Services.AddGrpc();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<VroksNet.Aspire.Sample.WebApi.Services.BooksGrpcService>();

app.Run();
