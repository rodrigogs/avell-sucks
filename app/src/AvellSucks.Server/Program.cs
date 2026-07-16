using AvellSucks.Server.Hosting;

var app = ServerHostBuilder.Build(args);
await app.RunAsync();

public partial class Program { }
