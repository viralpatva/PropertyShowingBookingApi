var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        new ApiError(string.Join(" ", context.ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage))));
});
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ISlotService, SlotService>();

var app = builder.Build();
await app.Services.InitializeDatabaseAsync();

app.UseHttpsRedirection();
app.MapControllers();
app.Run();

public partial class Program { }
