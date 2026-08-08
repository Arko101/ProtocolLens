var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
//Adding CORS policy to allow requests from Angular frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    }
        );
});

var app = builder.Build();
app.UseCors("AngularPolicy");


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();



app.MapGet("/api/status", () =>
{
    return new
    {
        message = "ProtocolLens API is running"
    };
});

app.MapPost("/api/pcap", (IFormFile file) =>
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest(new { message = "No file provided" });
    }

    var fileName = file.FileName;
    var fileSize = file.Length;


return Results.Ok(
    new
{
    fileName = fileName,
    fileSize = fileSize
});
}).DisableAntiforgery();
app.Run();


