using Dokpod.Bff;

var builder = BffHost.CreateBuilder(args);
var app = builder.Build();

BffHost.ConfigurePipeline(app);
BffHost.MapEndpoints(app);

app.Run();

public partial class Program;