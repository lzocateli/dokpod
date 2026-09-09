using Dokpod.ControlPlane.Api;

var builder = ApiHost.CreateBuilder(args);
var app = builder.Build();
ApiHost.MapEndpoints(app);
app.Run();

public partial class Program;