using ShowingBooking.Api;
using ShowingBooking.Application;
using ShowingBooking.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddSingleton(TimeProvider.System); // tests replace this with a controllable clock
builder.Services.AddApplication();
builder.Services.AddInfrastructure();

var app = builder.Build();

app.UseExceptionHandler(); // anything unexpected becomes a 500 ProblemDetails, never a stack trace
app.MapShowingEndpoints();

app.Run();

// Lets the integration tests start the app with WebApplicationFactory<Program>.
public partial class Program { }
