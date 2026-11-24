using Microsoft.AspNetCore.Mvc;
using PropertyShowing.Api.Data;
using PropertyShowing.Api.Repositories;
using PropertyShowing.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
        new ApiError(string.Join(" ", context.ModelState.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage))));
});
builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("BookingDatabase") ?? "Data Source=property-showings.db"));
builder.Services.AddScoped<ISlotRepository, SlotRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<BookingService>();
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}
app.UseHttpsRedirection();
app.MapControllers();
app.Run();
public partial class Program { }
